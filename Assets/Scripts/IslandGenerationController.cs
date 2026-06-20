using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Generates medium-sized islands into a dedicated land tilemap as the player sails.
//
// Design goals for this generator:
// - deterministic chunk-based generation from a seed
// - freeform island placement without visible macro-grid rhythm
// - three elevation tiers using explicit tile assignments
// - unload distant chunks and regenerate them identically when revisited
// - defer chunks too close to the boat so islands do not pop in directly on top of it
// - collision comes from the IslandTilemap's TilemapCollider2D
public class IslandGenerationController : MonoBehaviour
{
#if UNITY_INCLUDE_TESTS
    static bool hasDiagnosticSeedOverride;
    static int diagnosticSeedOverride;
    static bool diagnosticRandomizeSeedOverride;
#endif

    public delegate void IslandTileVisitor(Vector3Int cell, TileBase tile);

    public struct IslandSourceDescriptor
    {
        public Vector2 center;
        public Vector2 radii;
        public float rotationDegrees;
        public float maxRadius;
        public float normalizedRadius;
        public bool isTreasure;
        public Vector2Int deterministicKey;
    }

    public struct ShopDockRegistration
    {
        public Vector2Int ShopId;
        public Vector3 AnchorWorldPosition;
        public Vector3 SpanStartWorldPosition;
        public Vector3 SpanEndWorldPosition;
        public Vector2Int SourceChunkCoord;
        public float InteractionRadius;
    }

    public struct TreasurePlacementCandidate
    {
        public int attemptIndex;
        public Vector2 center;
        public Vector2 radii;
        public float rotationDegrees;
        public float normalizedRadius;
        public float score;
    }

    public event Action<RectInt> ChunkGenerated;
    public event Action<ShopDockRegistration> ShopDockRegistered;
    public event Action<Vector2Int> ChunkUnloaded;
    public event Action AcceptedIslandSourceCacheInvalidated;
    public event Action TreasurePlacementChanged;

    [Header("References")]
    [SerializeField] Tilemap islandTilemap;
    [SerializeField] Tilemap dockTilemap;
    [SerializeField] Tilemap goldTilemap;
    [SerializeField] Camera worldCamera;
    [SerializeField] Transform boatTransform;
    [SerializeField] TileBase lowElevationTile;
    [SerializeField] TileBase midElevationTile;
    [SerializeField] TileBase highElevationTile;
    [SerializeField] TileBase dockTile;
    [SerializeField] TileBase goldTile;
    [SerializeField] WorldGenerationSettings worldSettings;

    [Header("World Layout")]
    [SerializeField] int seed = 385;
    [SerializeField] bool randomizeSeedOnPlay = true;
    [SerializeField] int chunkSize = 48;
    [SerializeField] int generationMarginChunks = 3;
    [SerializeField] float protectedSpawnRadiusTiles = 1f;

    [Header("Placement Field")]
    [SerializeField] int candidateSectorSize = 56;
    [SerializeField][Range(1, 8)] int candidateSlotsPerSector = 1;
    [SerializeField][Range(0f, 0.45f)] float candidatePointPadding = 0.04f;
    [SerializeField] float islandSpacingMultiplier = 1.35f;
    [SerializeField][Range(0f, 1f)] float candidateScoreRegionBias = 0.25f;

    [Header("Regional Density")]
    [SerializeField][Range(0f, 1f)] float islandChanceMin = 0.12f;
    [SerializeField][Range(0f, 1f)] float islandChanceMax = 0.36f;
    [SerializeField] float regionNoiseScale = 0.11f;
    [SerializeField] float regionNoiseBias = 0.08f;

    [Header("Island Shape")]
    [SerializeField] float minRadiusTiles = 9f;
    [SerializeField] float maxRadiusTiles = 15f;
    [SerializeField][Min(0f)] float islandFootprintSeparationPadding = 12f;
    [SerializeField] float edgeNoiseScale = 0.15f;
    [SerializeField] float edgeNoiseStrength = 0.34f;
    [SerializeField] int voronoiFeaturePointCount = 3;
    [SerializeField] float voronoiStrength = 0.24f;
    [SerializeField] float islandEdgeThreshold = 0.34f;

    [Header("Shape Variety")]
    [SerializeField][Range(0f, 1f)] float landmarkChanceInner = 0.015f;
    [SerializeField][Range(0f, 1f)] float landmarkChanceOuter = 0.16f;
    [SerializeField][Min(1f)] float landmarkRadiusMinMultiplier = 1.18f;
    [SerializeField][Min(1f)] float landmarkRadiusMaxMultiplier = 1.65f;
    [SerializeField][Min(1f)] float landmarkRadiusCeilingTiles = 24f;
    [SerializeField][Range(0.4f, 1f)] float mediumAspectRatioMin = 0.72f;
    [SerializeField][Min(1f)] float mediumAspectRatioMax = 1.38f;
    [SerializeField][Range(0.25f, 1f)] float landmarkAspectRatioMin = 0.52f;
    [SerializeField][Min(1f)] float landmarkAspectRatioMax = 1.95f;
    [SerializeField][Range(0, 4)] int mediumMaxLobeCount = 2;
    [SerializeField][Range(0, 6)] int landmarkMaxLobeCount = 4;
    [SerializeField][Range(0f, 0.4f)] float mediumLobeStrength = 0.12f;
    [SerializeField][Range(0f, 0.5f)] float landmarkLobeStrength = 0.24f;

    [Header("Elevation")]
    [SerializeField] float interiorNoiseScale = 0.18f;
    [SerializeField] float midElevationThreshold = 0.58f;
    [SerializeField] float highElevationThreshold = 0.76f;

    [Header("Docks")]
    [SerializeField] bool generateDocks = true;
    [SerializeField][Range(0f, 1f)] float shopIslandChance = 0.4f;
    [SerializeField] int dockLengthCells = 2;
    [SerializeField] int dockWidthCells = 1;
    [SerializeField] int dockAttachmentDepthCells = 1;
    [SerializeField] int dockRelaxedMinimumClearCells = 3;
    [SerializeField][Min(0.1f)] float shopDockInteractionRadius = 3.2f;
    [SerializeField][Min(0f)] float shopIslandExtraSeparationPadding = 14f;

    [Header("Treasure Islands")]
    [SerializeField] bool generateTreasureIsland = true;
    [SerializeField][Min(1)] int treasurePlacementAttempts = 24;
    [SerializeField][Min(1)] int treasureApproachLengthCells = 4;
    [SerializeField][Min(0)] int treasureApproachSideClearanceCells = 1;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] bool enableGenerationDebugMetrics = false;
    [SerializeField] RectInt debugRequiredChunkRect;
    [SerializeField] int debugLoadedChunkCount;
    [SerializeField] int debugDeferredChunkCount;
    [SerializeField] int debugLoadedShopIslandCount;
    [SerializeField] bool debugTreasurePlacementValid;
    [SerializeField] Vector2 debugTreasureCenter;
    [SerializeField] float debugTreasureExclusionRadius;
    [SerializeField] bool debugTreasureTargetValid;
    [SerializeField] Vector3Int debugTreasureTargetCellA;
    [SerializeField] Vector3Int debugTreasureTargetCellB;
    [SerializeField] Vector2 debugTreasureTargetAnchor;

    public int ChunkSize => chunkSize;
    public int Seed => seed;
    public int GenerationMarginChunks => generationMarginChunks;
    public float ProtectedSpawnRadiusTiles => protectedSpawnRadiusTiles;
    public Transform BoatTransform => boatTransform;
    public WorldGenerationSettings WorldSettings => worldSettings;
    public Tilemap IslandTilemap => islandTilemap;
    public Tilemap DockTilemap => dockTilemap;
    public Tilemap GoldTilemap => goldTilemap;
    public TileBase LowElevationTile => lowElevationTile;
    public TileBase MidElevationTile => midElevationTile;
    public TileBase HighElevationTile => highElevationTile;
    public TileBase DockTile => dockTile;
    public TileBase GoldTile => goldTile;
    public float ShopDockInteractionRadius => shopDockInteractionRadius;

#if UNITY_INCLUDE_TESTS
    public static void SetDiagnosticPlaySeedOverride(int fixedSeed, bool randomizeSeed = false)
    {
        hasDiagnosticSeedOverride = true;
        diagnosticSeedOverride = fixedSeed;
        diagnosticRandomizeSeedOverride = randomizeSeed;
    }

    public static void ClearDiagnosticPlaySeedOverride()
    {
        hasDiagnosticSeedOverride = false;
        diagnosticSeedOverride = 0;
        diagnosticRandomizeSeedOverride = false;
    }
#endif

    public void SetForcedVisibleShopDock(Vector2Int shopId)
    {
        forcedVisibleShopDockId = shopId;
        forcedVisibleShopDockActive = shopId != default;
    }

    public void ClearForcedVisibleShopDock()
    {
        forcedVisibleShopDockActive = false;
        forcedVisibleShopDockId = default;
    }

    readonly HashSet<Vector2Int> loadedChunks = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> deferredChunks = new HashSet<Vector2Int>();
    readonly Dictionary<Vector2Int, DockPlacement> cachedShopDockPlacements = new Dictionary<Vector2Int, DockPlacement>();
    readonly Dictionary<long, CachedAcceptedIslandSource> cachedAcceptedIslandSourcesBySectorSlot = new Dictionary<long, CachedAcceptedIslandSource>();
    readonly Dictionary<long, CachedAcceptedIslandSource> cachedAcceptedIslandSourcesByDeterministicKey = new Dictionary<long, CachedAcceptedIslandSource>();
    readonly List<ShopDockRegistration> cachedAllShopDockRegistrations = new List<ShopDockRegistration>();
    readonly List<ShopDockRegistration> buildingAllShopDockRegistrations = new List<ShopDockRegistration>();
    readonly HashSet<Vector2Int> buildingAllShopDockRegistrationIds = new HashSet<Vector2Int>();
    readonly List<Vector2Int> refreshChunksToUnloadScratch = new List<Vector2Int>();
    readonly List<Vector2Int> refreshDeferredToRemoveScratch = new List<Vector2Int>();
    readonly List<Vector2Int> refreshResolvedDeferredScratch = new List<Vector2Int>();
    readonly List<IslandSourceDescriptor> chunkIslandSourcesScratch = new List<IslandSourceDescriptor>();
    readonly List<IslandSourceDescriptor> protectedChunkIslandSourcesScratch = new List<IslandSourceDescriptor>();
    readonly HashSet<Vector2Int> countedShopIslandKeysScratch = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> emittedShopIdsScratch = new HashSet<Vector2Int>();

    bool referencesValid;
    bool forcedVisibleShopDockActive;
    Vector2Int forcedVisibleShopDockId;
    TreasureIslandPlacement treasureIslandPlacement;
    bool allShopDockRegistrationCacheValid;
    bool allShopDockRegistrationBuildInProgress;
    int allShopDockRegistrationBuildMinSectorX;
    int allShopDockRegistrationBuildMaxSectorX;
    int allShopDockRegistrationBuildMinSectorY;
    int allShopDockRegistrationBuildMaxSectorY;
    int allShopDockRegistrationBuildCurrentX;
    int allShopDockRegistrationBuildCurrentY;
    int allShopDockRegistrationBuildCurrentSlotIndex;
    Coroutine warmShopDockRegistrationCacheRoutine;

    struct IslandParameters
    {
        public Vector2 center;
        public Vector2 radii;
        public float rotationDegrees;
        public Vector2[] voronoiPoints;
        public Vector2[] lobePoints;
        public float[] lobeRadii;
        public float lobeStrength;
        public float footprintRadius;
    }

    struct PlacementCandidate
    {
        public Vector2Int sectorCoord;
        public int slotIndex;
        public Vector2Int deterministicKey;
        public IslandParameters island;
        public float occupancyChance;
        public float score;
        public float maxRadius;
        public float normalizedRadius;
        public bool exists;
    }

    struct DockPlacement
    {
        public Vector3Int rootCell;
        public Vector3Int outwardDirection;
        public int clearLength;
        public int qualityTier;
        public float selectionScore;
    }

    struct TreasureTargetCandidate
    {
        public Vector3Int cellA;
        public Vector3Int cellB;
        public Vector2 outwardDirection;
        public int clearDepth;
        public int flatness;
        public int seamBuffer;
        public float selectionScore;
    }

    struct CachedAcceptedIslandSource
    {
        public bool evaluated;
        public bool accepted;
        public IslandSourceDescriptor descriptor;
        public IslandParameters island;
    }

    const int DeterministicKeySlotStride = 97;
    const int ShopDockCacheBuildStepsPerFrame = 96;

    void Awake()
    {
        referencesValid = ValidateReferences();
        if (!referencesValid)
            return;

        ApplyPlaySeedIfNeeded();
        EnsureTreasurePlacementInitialized();
    }

    void Start()
    {
        if (!referencesValid)
            return;

        RefreshVisibleChunks();
        BeginWarmShopDockRegistrationCache();
    }

    void LateUpdate()
    {
        if (!referencesValid)
            return;

        RefreshVisibleChunks();
    }

    bool ValidateReferences()
    {
        if (islandTilemap == null)
        {
            Debug.LogWarning("[IslandGenerationController] Missing IslandTilemap reference.", this);
            return false;
        }

        if (generateDocks && dockTilemap == null)
        {
            Debug.LogWarning("[IslandGenerationController] Missing DockTilemap reference while dock generation is enabled.", this);
            return false;
        }

        if (generateTreasureIsland && goldTilemap == null)
        {
            Debug.LogWarning("[IslandGenerationController] Missing GoldTileMap reference while treasure island generation is enabled.", this);
            return false;
        }

        if (boatTransform == null)
        {
            Debug.LogWarning("[IslandGenerationController] Missing boat Transform reference.", this);
            return false;
        }

        if (worldSettings == null)
        {
            Debug.LogWarning("[IslandGenerationController] Missing WorldGenerationSettings reference.", this);
            return false;
        }

        if (lowElevationTile == null || midElevationTile == null || highElevationTile == null)
        {
            Debug.LogWarning("[IslandGenerationController] Assign low, mid, and high elevation tiles.", this);
            return false;
        }

        if (generateDocks && dockTile == null)
        {
            Debug.LogWarning("[IslandGenerationController] Assign a dock tile when dock generation is enabled.", this);
            return false;
        }

        if (generateTreasureIsland && goldTile == null)
        {
            Debug.LogWarning("[IslandGenerationController] Assign a gold tile when treasure island generation is enabled.", this);
            return false;
        }

        if (chunkSize <= 0)
        {
            Debug.LogWarning("[IslandGenerationController] Chunk size must be greater than zero.", this);
            return false;
        }

        if (candidateSectorSize <= 0)
        {
            Debug.LogWarning("[IslandGenerationController] Candidate sector size must be greater than zero.", this);
            return false;
        }

        if (generationMarginChunks < 0)
        {
            Debug.LogWarning("[IslandGenerationController] Generation margin chunks must be zero or greater.", this);
            return false;
        }

        if (candidateSlotsPerSector <= 0 || candidateSlotsPerSector >= DeterministicKeySlotStride)
        {
            Debug.LogWarning("[IslandGenerationController] Candidate slots per sector must be in a sane positive range.", this);
            return false;
        }

        if (candidatePointPadding < 0f || candidatePointPadding >= 0.5f)
        {
            Debug.LogWarning("[IslandGenerationController] Candidate point padding must be in [0, 0.5).", this);
            return false;
        }

        if (islandSpacingMultiplier < 0.5f)
        {
            Debug.LogWarning("[IslandGenerationController] Island spacing multiplier is too small.", this);
            return false;
        }

        if (candidateScoreRegionBias < 0f || candidateScoreRegionBias > 1f)
        {
            Debug.LogWarning("[IslandGenerationController] Candidate score region bias must be in [0, 1].", this);
            return false;
        }

        if (minRadiusTiles <= 0f || maxRadiusTiles < minRadiusTiles)
        {
            Debug.LogWarning("[IslandGenerationController] Island radii are invalid.", this);
            return false;
        }

        if (voronoiFeaturePointCount < 1)
        {
            Debug.LogWarning("[IslandGenerationController] Voronoi feature point count must be at least 1.", this);
            return false;
        }

        if (worldSettings.PlayableRadiusTiles <= GetMaximumPossibleIslandRadius() + 8f)
        {
            Debug.LogWarning("[IslandGenerationController] World radius is too small for island generation.", this);
            return false;
        }

        if (landmarkChanceInner < 0f || landmarkChanceInner > 1f || landmarkChanceOuter < 0f || landmarkChanceOuter > 1f)
        {
            Debug.LogWarning("[IslandGenerationController] Landmark chances must be in [0, 1].", this);
            return false;
        }

        if (landmarkRadiusMaxMultiplier < landmarkRadiusMinMultiplier)
        {
            Debug.LogWarning("[IslandGenerationController] Landmark radius multipliers are invalid.", this);
            return false;
        }

        if (landmarkRadiusCeilingTiles < maxRadiusTiles)
        {
            Debug.LogWarning("[IslandGenerationController] Landmark radius ceiling must be at least the normal max radius.", this);
            return false;
        }

        if (mediumAspectRatioMax < 1f || landmarkAspectRatioMax < 1f || mediumAspectRatioMin <= 0f || landmarkAspectRatioMin <= 0f)
        {
            Debug.LogWarning("[IslandGenerationController] Aspect-ratio ranges are invalid.", this);
            return false;
        }

        if (generateDocks)
        {
            if (shopIslandChance < 0f || shopIslandChance > 1f)
            {
                Debug.LogWarning("[IslandGenerationController] Shop island chance must be in [0, 1].", this);
                return false;
            }

            if (dockLengthCells <= 0 || dockWidthCells <= 0)
            {
                Debug.LogWarning("[IslandGenerationController] Dock footprint must be greater than zero.", this);
                return false;
            }

            if (dockAttachmentDepthCells < 0)
            {
                Debug.LogWarning("[IslandGenerationController] Dock attachment depth must be zero or greater.", this);
                return false;
            }
        }

        return true;
    }

    void EnsureTreasurePlacementInitialized()
    {
        if (treasureIslandPlacement.isValid)
        {
            debugTreasurePlacementValid = true;
            debugTreasureCenter = treasureIslandPlacement.center;
            debugTreasureExclusionRadius = treasureIslandPlacement.exclusionRadius;
            return;
        }

        RefreshTreasurePlacement();
    }

    void ApplyPlaySeedIfNeeded()
    {
#if UNITY_INCLUDE_TESTS
        if (hasDiagnosticSeedOverride)
        {
            seed = diagnosticSeedOverride;
            randomizeSeedOnPlay = diagnosticRandomizeSeedOverride;
            hasDiagnosticSeedOverride = false;
        }
#endif

        if (!randomizeSeedOnPlay)
            return;

        seed = Guid.NewGuid().GetHashCode();
        treasureIslandPlacement = default;
        cachedShopDockPlacements.Clear();
        InvalidateAcceptedIslandSourceCache();
        InvalidateShopDockRegistrationCache();
        TreasurePlacementChanged?.Invoke();
    }

    void RefreshTreasurePlacement()
    {
        treasureIslandPlacement = default;
        cachedShopDockPlacements.Clear();
        InvalidateAcceptedIslandSourceCache();
        InvalidateShopDockRegistrationCache();
        ClearAllTreasureTiles();
        debugTreasurePlacementValid = false;
        debugTreasureCenter = Vector2.zero;
        debugTreasureExclusionRadius = 0f;
        debugTreasureTargetValid = false;
        debugTreasureTargetCellA = default;
        debugTreasureTargetCellB = default;
        debugTreasureTargetAnchor = Vector2.zero;

        if (!generateTreasureIsland || worldSettings == null)
            return;

        treasureIslandPlacement = BuildTreasureIslandPlacement();
        debugTreasurePlacementValid = treasureIslandPlacement.isValid;
        debugTreasureCenter = treasureIslandPlacement.center;
        debugTreasureExclusionRadius = treasureIslandPlacement.exclusionRadius;
        debugTreasureTargetValid = treasureIslandPlacement.hasTarget;
        debugTreasureTargetCellA = treasureIslandPlacement.targetCellA;
        debugTreasureTargetCellB = treasureIslandPlacement.targetCellB;
        debugTreasureTargetAnchor = treasureIslandPlacement.targetContactAnchorWorld;
        BeginWarmShopDockRegistrationCache();
        TreasurePlacementChanged?.Invoke();
    }

    void RefreshVisibleChunks()
    {
        Camera cameraToUse = ResolveCamera();
        if (cameraToUse == null)
            return;

        RectInt requiredChunkRect = GetRequiredChunkRect(cameraToUse);
        debugRequiredChunkRect = requiredChunkRect;

        UnloadDistantChunks(requiredChunkRect);

        for (int y = requiredChunkRect.yMin; y < requiredChunkRect.yMax; y++)
        {
            for (int x = requiredChunkRect.xMin; x < requiredChunkRect.xMax; x++)
            {
                Vector2Int chunkCoord = new Vector2Int(x, y);

                if (loadedChunks.Contains(chunkCoord))
                    continue;

                if (IsChunkProtected(chunkCoord))
                {
                    deferredChunks.Add(chunkCoord);
                    continue;
                }

                if (TryGenerateChunk(chunkCoord, requiredChunkRect))
                    deferredChunks.Remove(chunkCoord);
                else
                    deferredChunks.Add(chunkCoord);
            }
        }

        // Recheck deferred chunks so they can appear once the player has sailed far enough away.
        if (deferredChunks.Count > 0)
        {
            refreshResolvedDeferredScratch.Clear();
            foreach (Vector2Int chunkCoord in deferredChunks)
            {
                if (!requiredChunkRect.Contains(chunkCoord))
                {
                    refreshResolvedDeferredScratch.Add(chunkCoord);
                    continue;
                }

                if (IsChunkProtected(chunkCoord))
                    continue;

                if (TryGenerateChunk(chunkCoord, requiredChunkRect))
                    refreshResolvedDeferredScratch.Add(chunkCoord);
            }

            for (int i = 0; i < refreshResolvedDeferredScratch.Count; i++)
                deferredChunks.Remove(refreshResolvedDeferredScratch[i]);
        }

        debugLoadedChunkCount = loadedChunks.Count;
        debugDeferredChunkCount = deferredChunks.Count;
        debugLoadedShopIslandCount = enableGenerationDebugMetrics
            ? CountLoadedShopIslands(requiredChunkRect)
            : 0;
    }

    Camera ResolveCamera()
    {
        if (worldCamera != null)
            return worldCamera;

        return Camera.main;
    }

    RectInt GetRequiredChunkRect(Camera cameraToUse)
    {
        Vector3 bottomLeft = cameraToUse.ViewportToWorldPoint(new Vector3(0f, 0f, 0f));
        Vector3 topRight = cameraToUse.ViewportToWorldPoint(new Vector3(1f, 1f, 0f));

        Vector3 minWorld = Vector3.Min(bottomLeft, topRight);
        Vector3 maxWorld = Vector3.Max(bottomLeft, topRight);

        Vector3Int minCell = islandTilemap.WorldToCell(minWorld);
        Vector3Int maxCell = islandTilemap.WorldToCell(maxWorld);

        Vector2Int minChunk = CellToChunkCoord(minCell);
        Vector2Int maxChunk = CellToChunkCoord(maxCell);

        int minX = minChunk.x - generationMarginChunks;
        int minY = minChunk.y - generationMarginChunks;
        int maxX = maxChunk.x + generationMarginChunks + 1;
        int maxY = maxChunk.y + generationMarginChunks + 1;
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    RectInt GetRequiredChunkRectForWorldArea(Vector2 centerWorld, float radiusWorld)
    {
        Vector3Int minCell = islandTilemap.WorldToCell(new Vector3(centerWorld.x - radiusWorld, centerWorld.y - radiusWorld, 0f));
        Vector3Int maxCell = islandTilemap.WorldToCell(new Vector3(centerWorld.x + radiusWorld, centerWorld.y + radiusWorld, 0f));

        Vector2Int minChunk = CellToChunkCoord(minCell);
        Vector2Int maxChunk = CellToChunkCoord(maxCell);

        int minX = minChunk.x - generationMarginChunks;
        int minY = minChunk.y - generationMarginChunks;
        int maxX = maxChunk.x + generationMarginChunks + 1;
        int maxY = maxChunk.y + generationMarginChunks + 1;
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    Vector2Int CellToChunkCoord(Vector3Int cell)
    {
        return new Vector2Int(
            FloorDiv(cell.x, chunkSize),
            FloorDiv(cell.y, chunkSize));
    }

    static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
            quotient--;

        return quotient;
    }

    void UnloadDistantChunks(RectInt requiredChunkRect)
    {
        refreshChunksToUnloadScratch.Clear();

        foreach (Vector2Int chunkCoord in loadedChunks)
        {
            if (!requiredChunkRect.Contains(chunkCoord))
                refreshChunksToUnloadScratch.Add(chunkCoord);
        }

        for (int i = 0; i < refreshChunksToUnloadScratch.Count; i++)
            UnloadChunk(refreshChunksToUnloadScratch[i]);

        refreshDeferredToRemoveScratch.Clear();
        foreach (Vector2Int chunkCoord in deferredChunks)
        {
            if (!requiredChunkRect.Contains(chunkCoord))
                refreshDeferredToRemoveScratch.Add(chunkCoord);
        }

        for (int i = 0; i < refreshDeferredToRemoveScratch.Count; i++)
            deferredChunks.Remove(refreshDeferredToRemoveScratch[i]);
    }

    void UnloadChunk(Vector2Int chunkCoord)
    {
        ChunkUnloaded?.Invoke(chunkCoord);
        RectInt chunkRect = GetChunkRect(chunkCoord);
        ClearRect(chunkRect);
        ClearDockRect(chunkRect);
        ClearGoldRect(chunkRect);
        loadedChunks.Remove(chunkCoord);
    }

    void RegenerateLoadedChunksForTreasureSelection()
    {
        List<Vector2Int> chunkCoords = new List<Vector2Int>(loadedChunks);
        for (int i = 0; i < chunkCoords.Count; i++)
            UnloadChunk(chunkCoords[i]);

        deferredChunks.Clear();
        RefreshVisibleChunks();
    }

    RectInt GetChunkRect(Vector2Int chunkCoord)
    {
        return new RectInt(chunkCoord.x * chunkSize, chunkCoord.y * chunkSize, chunkSize, chunkSize);
    }

    bool IsChunkProtected(Vector2Int chunkCoord)
    {
        RectInt chunkRect = GetChunkRect(chunkCoord);
        Vector3Int boatCell = islandTilemap.WorldToCell(boatTransform.position);
        Vector2 boatCellPos = new Vector2(boatCell.x + 0.5f, boatCell.y + 0.5f);
        float safeDistance = protectedSpawnRadiusTiles + GetMaximumPossibleIslandRadius();

        if (TreasureCanAffectChunk(chunkRect))
        {
            float treasureSafeDistance = safeDistance + treasureIslandPlacement.MaxRadius;
            if (Vector2.Distance(treasureIslandPlacement.center, boatCellPos) < treasureSafeDistance)
                return true;
        }

        CollectIslandSourcesForChunk(chunkRect, protectedChunkIslandSourcesScratch);
        for (int i = 0; i < protectedChunkIslandSourcesScratch.Count; i++)
        {
            if (!TryBuildPlacementCandidateFromSource(protectedChunkIslandSourcesScratch[i], out PlacementCandidate candidate))
                continue;

            if (forcedVisibleShopDockActive && candidate.deterministicKey == forcedVisibleShopDockId)
                continue;

            if (Vector2.Distance(candidate.island.center, boatCellPos) < safeDistance + candidate.maxRadius)
                return true;
        }

        return false;
    }

    bool TryGenerateChunk(Vector2Int chunkCoord, RectInt requiredChunkRect)
    {
        RectInt chunkRect = GetChunkRect(chunkCoord);
        if (!CanRenderChunkAtomically(chunkRect, requiredChunkRect))
            return false;

        ClearRect(chunkRect);
        ClearDockRect(chunkRect);
        ClearGoldRect(chunkRect);

        if (TreasureCanAffectChunk(chunkRect))
        {
            PaintIsland(chunkRect, ToIslandParameters(treasureIslandPlacement));
            PaintTreasureTarget(chunkRect);
        }

        CollectIslandSourcesForChunk(chunkRect, chunkIslandSourcesScratch);
        for (int i = 0; i < chunkIslandSourcesScratch.Count; i++)
        {
            if (!TryBuildPlacementCandidateFromSource(chunkIslandSourcesScratch[i], out PlacementCandidate candidate))
                continue;

            PaintIsland(chunkRect, candidate.island);
            if (generateDocks && TryResolveShopDockPlacement(candidate, out DockPlacement chosenDock))
                TryPlaceDockForIsland(chunkCoord, chunkRect, candidate, chosenDock);
        }

        loadedChunks.Add(chunkCoord);
        ChunkGenerated?.Invoke(chunkRect);
        return true;
    }

    bool CanRenderChunkAtomically(RectInt chunkRect, RectInt requiredChunkRect)
    {
        if (TreasureCanAffectChunk(chunkRect) && !CanRenderTreasureAtomically(requiredChunkRect))
            return false;

        CollectIslandSourcesForChunk(chunkRect, chunkIslandSourcesScratch);
        for (int i = 0; i < chunkIslandSourcesScratch.Count; i++)
        {
            if (!TryBuildPlacementCandidateFromSource(chunkIslandSourcesScratch[i], out PlacementCandidate candidate))
                continue;

            if (!CanRenderCandidateAtomically(candidate, requiredChunkRect))
                return false;
        }

        return true;
    }

    bool CanRenderTreasureAtomically(RectInt requiredChunkRect)
    {
        if (!treasureIslandPlacement.isValid)
            return true;

        RectInt affectedChunkRect = GetCandidateAffectedChunkRect(
            treasureIslandPlacement.center,
            treasureIslandPlacement.MaxRadius);
        return AreAllChunksReadyForAtomicRender(affectedChunkRect, requiredChunkRect);
    }

    bool CanRenderCandidateAtomically(PlacementCandidate candidate, RectInt requiredChunkRect)
    {
        RectInt affectedChunkRect = GetCandidateAffectedChunkRect(candidate.island.center, candidate.maxRadius);
        return AreAllChunksReadyForAtomicRender(affectedChunkRect, requiredChunkRect);
    }

    bool AreAllChunksReadyForAtomicRender(RectInt affectedChunkRect, RectInt requiredChunkRect)
    {
        if (!requiredChunkRect.Contains(new Vector2Int(affectedChunkRect.xMin, affectedChunkRect.yMin))
            || !requiredChunkRect.Contains(new Vector2Int(affectedChunkRect.xMax - 1, affectedChunkRect.yMax - 1)))
        {
            return false;
        }

        for (int y = affectedChunkRect.yMin; y < affectedChunkRect.yMax; y++)
        {
            for (int x = affectedChunkRect.xMin; x < affectedChunkRect.xMax; x++)
            {
                Vector2Int affectedChunk = new Vector2Int(x, y);
                if (IsChunkProtected(affectedChunk))
                    return false;
            }
        }

        return true;
    }

    RectInt GetCandidateAffectedChunkRect(Vector2 center, float radius)
    {
        radius += 1f;
        int minCellX = Mathf.FloorToInt(center.x - radius);
        int maxCellX = Mathf.FloorToInt(center.x + radius);
        int minCellY = Mathf.FloorToInt(center.y - radius);
        int maxCellY = Mathf.FloorToInt(center.y + radius);

        Vector2Int minChunk = CellToChunkCoord(new Vector3Int(minCellX, minCellY, 0));
        Vector2Int maxChunk = CellToChunkCoord(new Vector3Int(maxCellX, maxCellY, 0));
        return new RectInt(
            minChunk.x,
            minChunk.y,
            (maxChunk.x - minChunk.x) + 1,
            (maxChunk.y - minChunk.y) + 1);
    }

    public Camera GetWorldCamera()
    {
        return ResolveCamera();
    }

    public RectInt GetRequiredChunkRectForCamera(Camera cameraToUse)
    {
        return GetRequiredChunkRect(cameraToUse);
    }

    public RectInt GetChunkRectForCoord(Vector2Int chunkCoord)
    {
        return GetChunkRect(chunkCoord);
    }

    public Vector2Int GetChunkCoordForWorldPosition(Vector2 worldPosition)
    {
        return CellToChunkCoord(islandTilemap.WorldToCell(worldPosition));
    }

    public bool IsChunkLoadedForCell(Vector3Int cell)
    {
        return loadedChunks.Contains(CellToChunkCoord(cell));
    }

    // Returns true if the chunk is tracked by the generator but waiting for the
    // protection radius to lift before it can be generated.
    public bool IsChunkDeferredForCell(Vector3Int cell)
    {
        return deferredChunks.Contains(CellToChunkCoord(cell));
    }

    public void CollectAcceptedIslandSourcesNearWorldPosition(Vector2 worldPosition, float revealRadius, List<IslandSourceDescriptor> results)
    {
        if (results == null)
            return;

        results.Clear();
        if (!referencesValid)
            return;

        CacheTreasureIslandSourceIfNeeded();
        if (treasureIslandPlacement.isValid)
        {
            IslandSourceDescriptor treasureSource = ToDescriptor(treasureIslandPlacement);
            if (DoesRevealCircleIntersectSource(worldPosition, revealRadius, treasureSource))
                results.Add(treasureSource);
        }

        int minX = Mathf.FloorToInt(worldPosition.x - revealRadius);
        int maxX = Mathf.FloorToInt(worldPosition.x + revealRadius) + 1;
        int minY = Mathf.FloorToInt(worldPosition.y - revealRadius);
        int maxY = Mathf.FloorToInt(worldPosition.y + revealRadius) + 1;
        RectInt queryRect = new RectInt(minX, minY, maxX - minX, maxY - minY);
        RectInt candidateBounds = GetCandidateQueryBounds(queryRect, 0f);
        for (int y = candidateBounds.yMin; y < candidateBounds.yMax; y++)
        {
            for (int x = candidateBounds.xMin; x < candidateBounds.xMax; x++)
            {
                Vector2Int sectorCoord = new Vector2Int(x, y);
                for (int slotIndex = 0; slotIndex < candidateSlotsPerSector; slotIndex++)
                {
                    if (!TryGetCachedAcceptedIslandSource(sectorCoord, slotIndex, out CachedAcceptedIslandSource source))
                        continue;

                    if (DoesRevealCircleIntersectSource(worldPosition, revealRadius, source.descriptor))
                        results.Add(source.descriptor);
                }
            }
        }
    }

    public bool IsAcceptedIslandCurrentlyRenderable(Vector2 worldPosition, float revealRadius, Vector2Int deterministicKey)
    {
        if (!referencesValid)
            return false;

        CacheTreasureIslandSourceIfNeeded();
        RectInt requiredChunkRect = GetRequiredChunkRectForWorldArea(worldPosition, revealRadius);

        if (treasureIslandPlacement.isValid
            && deterministicKey == ToDescriptor(treasureIslandPlacement).deterministicKey)
        {
            RectInt affectedChunkRect = GetCandidateAffectedChunkRect(
                treasureIslandPlacement.center,
                treasureIslandPlacement.MaxRadius);
            return AreAllChunksReadyForAtomicRender(affectedChunkRect, requiredChunkRect);
        }

        if (!TryEnsureAcceptedIslandSourceCached(deterministicKey, out CachedAcceptedIslandSource source))
            return false;

        PlacementCandidate candidate = new PlacementCandidate
        {
            deterministicKey = deterministicKey,
            island = source.island,
            maxRadius = source.descriptor.maxRadius,
            normalizedRadius = source.descriptor.normalizedRadius,
            exists = true
        };

        return CanRenderCandidateAtomically(candidate, requiredChunkRect);
    }

    public bool IsAcceptedIslandCurrentlyLoaded(Vector2Int deterministicKey)
    {
        if (!referencesValid)
            return false;

        CacheTreasureIslandSourceIfNeeded();
        RectInt affectedChunkRect;

        if (treasureIslandPlacement.isValid
            && deterministicKey == ToDescriptor(treasureIslandPlacement).deterministicKey)
        {
            affectedChunkRect = GetCandidateAffectedChunkRect(
                treasureIslandPlacement.center,
                treasureIslandPlacement.MaxRadius);
        }
        else
        {
            if (!TryEnsureAcceptedIslandSourceCached(deterministicKey, out CachedAcceptedIslandSource source))
                return false;

            affectedChunkRect = GetCandidateAffectedChunkRect(source.island.center, source.descriptor.maxRadius);
        }

        for (int y = affectedChunkRect.yMin; y < affectedChunkRect.yMax; y++)
        {
            for (int x = affectedChunkRect.xMin; x < affectedChunkRect.xMax; x++)
            {
                if (loadedChunks.Contains(new Vector2Int(x, y)))
                    return true;
            }
        }

        return false;
    }

    public void CollectIslandSourcesForChunk(RectInt chunkRect, List<IslandSourceDescriptor> results)
    {
        if (results == null)
            return;

        results.Clear();
        if (!referencesValid)
            return;

        CacheTreasureIslandSourceIfNeeded();
        if (TreasureCanAffectChunk(chunkRect))
            results.Add(ToDescriptor(treasureIslandPlacement));

        RectInt candidateBounds = GetCandidateQueryBounds(chunkRect, GetMaximumPossibleIslandRadius());
        for (int y = candidateBounds.yMin; y < candidateBounds.yMax; y++)
        {
            for (int x = candidateBounds.xMin; x < candidateBounds.xMax; x++)
            {
                Vector2Int sectorCoord = new Vector2Int(x, y);
                for (int slotIndex = 0; slotIndex < candidateSlotsPerSector; slotIndex++)
                {
                    if (!TryGetCachedAcceptedIslandSource(sectorCoord, slotIndex, out CachedAcceptedIslandSource source))
                        continue;

                    PlacementCandidate candidate = new PlacementCandidate
                    {
                        deterministicKey = source.descriptor.deterministicKey,
                        island = source.island,
                        maxRadius = source.descriptor.maxRadius,
                        normalizedRadius = source.descriptor.normalizedRadius,
                        exists = true
                    };

                    if (!CandidateCanAffectChunk(candidate, chunkRect))
                        continue;

                    results.Add(source.descriptor);
                }
            }
        }
    }

    public bool VisitAcceptedIslandTiles(Vector2Int deterministicKey, IslandTileVisitor visitor)
    {
        if (visitor == null || !referencesValid)
            return false;

        CacheTreasureIslandSourceIfNeeded();
        if (!cachedAcceptedIslandSourcesByDeterministicKey.TryGetValue(PackDeterministicIslandKey(deterministicKey), out CachedAcceptedIslandSource source)
            || !source.accepted)
        {
            return false;
        }

        int radius = Mathf.CeilToInt(source.descriptor.maxRadius) + 1;
        int minX = Mathf.FloorToInt(source.descriptor.center.x - radius);
        int maxX = Mathf.FloorToInt(source.descriptor.center.x + radius);
        int minY = Mathf.FloorToInt(source.descriptor.center.y - radius);
        int maxY = Mathf.FloorToInt(source.descriptor.center.y + radius);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                if (TryResolveIslandTileAtCell(cell, source.island, out TileBase tile))
                    visitor(cell, tile);
            }
        }

        return true;
    }

    public bool TryResolveAcceptedIslandTile(Vector2Int deterministicKey, Vector3Int cell, out TileBase tile)
    {
        tile = null;
        if (!referencesValid)
            return false;

        if (!TryEnsureAcceptedIslandSourceCached(deterministicKey, out CachedAcceptedIslandSource source))
        {
            return false;
        }

        return TryResolveIslandTileAtCell(cell, source.island, out tile);
    }

    public bool TryGetAcceptedIslandSourceDescriptor(Vector2Int deterministicKey, out IslandSourceDescriptor descriptor)
    {
        descriptor = default;
        if (!referencesValid)
            return false;

        if (!TryEnsureAcceptedIslandSourceCached(deterministicKey, out CachedAcceptedIslandSource source))
        {
            return false;
        }

        descriptor = source.descriptor;
        return true;
    }

    public bool TryGetAcceptedIslandDockCells(Vector2Int deterministicKey, List<Vector3Int> results)
    {
        if (results == null)
            return false;

        results.Clear();
        if (!referencesValid || !generateDocks)
            return false;

        if (!TryEnsureAcceptedIslandSourceCached(deterministicKey, out CachedAcceptedIslandSource source))
            return false;

        PlacementCandidate candidate = new PlacementCandidate
        {
            deterministicKey = deterministicKey,
            island = source.island,
            maxRadius = source.descriptor.maxRadius,
            normalizedRadius = source.descriptor.normalizedRadius,
            exists = true
        };

        if (!TryResolveShopDockPlacement(candidate, out DockPlacement placement))
            return false;

        int paintedLength = Mathf.Min(placement.clearLength, dockLengthCells);
        if (paintedLength <= 0)
            return false;

        Vector3Int perpendicular = new Vector3Int(-placement.outwardDirection.y, placement.outwardDirection.x, 0);
        int halfWidth = dockWidthCells / 2;
        for (int step = 1; step <= paintedLength; step++)
        {
            Vector3Int rowStart = placement.rootCell + placement.outwardDirection * step;
            for (int lateral = -halfWidth; lateral <= halfWidth; lateral++)
                results.Add(rowStart + perpendicular * lateral);
        }

        return results.Count > 0;
    }

    public bool TryGetTreasureIslandCenter(out Vector2 treasureCenter)
    {
        treasureCenter = Vector2.zero;
        if (!referencesValid || !treasureIslandPlacement.isValid)
            return false;

        treasureCenter = treasureIslandPlacement.center;
        return true;
    }

    public bool TryGetTreasureTargetContactAnchor(out Vector2 contactAnchorWorld)
    {
        contactAnchorWorld = Vector2.zero;
        if (!referencesValid || !treasureIslandPlacement.isValid || !treasureIslandPlacement.hasTarget)
            return false;

        contactAnchorWorld = treasureIslandPlacement.targetContactAnchorWorld;
        return true;
    }

    public bool TryGetTreasureTargetCells(out Vector3Int cellA, out Vector3Int cellB)
    {
        cellA = default;
        cellB = default;
        if (!referencesValid || !treasureIslandPlacement.isValid || !treasureIslandPlacement.hasTarget)
            return false;

        cellA = treasureIslandPlacement.targetCellA;
        cellB = treasureIslandPlacement.targetCellB;
        return true;
    }

    public bool IsTreasureTargetCell(Vector3Int cell)
    {
        if (!referencesValid || !treasureIslandPlacement.isValid || !treasureIslandPlacement.hasTarget)
            return false;

        return cell == treasureIslandPlacement.targetCellA || cell == treasureIslandPlacement.targetCellB;
    }

    public void CollectTreasurePlacementCandidates(List<TreasurePlacementCandidate> results)
    {
        if (results == null)
            return;

        results.Clear();
        if (!referencesValid || !generateTreasureIsland || worldSettings == null)
            return;

        float minBandRadius = worldSettings.GetTreasureBandMinRadiusTiles();
        float maxBandRadius = worldSettings.GetTreasureBandMaxRadiusTiles();
        for (int attempt = 0; attempt < Mathf.Max(1, treasurePlacementAttempts); attempt++)
        {
            TreasureIslandPlacement candidate = BuildTreasureAttempt(attempt, minBandRadius, maxBandRadius);
            if (!candidate.isValid)
                continue;

            if (candidate.normalizedRadius < worldSettings.TreasureBandMinNormalized
                || candidate.normalizedRadius > worldSettings.TreasureBandMaxNormalized)
            {
                continue;
            }

            if (!IsInsidePlayableRadius(candidate.center, candidate.MaxRadius))
                continue;

            if (!candidate.hasTarget)
                continue;

            results.Add(new TreasurePlacementCandidate
            {
                attemptIndex = attempt,
                center = candidate.center,
                radii = candidate.radii,
                rotationDegrees = candidate.rotationDegrees,
                normalizedRadius = candidate.normalizedRadius,
                score = Hash01(attempt, seed, seed + 997)
            });
        }
    }

    public bool TryApplyTreasurePlacementAttempt(int attemptIndex)
    {
        if (!referencesValid || !generateTreasureIsland || worldSettings == null)
            return false;

        float minBandRadius = worldSettings.GetTreasureBandMinRadiusTiles();
        float maxBandRadius = worldSettings.GetTreasureBandMaxRadiusTiles();
        TreasureIslandPlacement candidate = BuildTreasureAttempt(attemptIndex, minBandRadius, maxBandRadius);
        if (!candidate.isValid)
            return false;

        if (candidate.normalizedRadius < worldSettings.TreasureBandMinNormalized
            || candidate.normalizedRadius > worldSettings.TreasureBandMaxNormalized)
        {
            return false;
        }

        if (!IsInsidePlayableRadius(candidate.center, candidate.MaxRadius))
            return false;

        if (!candidate.hasTarget)
            return false;

        treasureIslandPlacement = candidate;
        cachedShopDockPlacements.Clear();
        InvalidateAcceptedIslandSourceCache();
        InvalidateShopDockRegistrationCache();
        ClearAllTreasureTiles();
        debugTreasurePlacementValid = treasureIslandPlacement.isValid;
        debugTreasureCenter = treasureIslandPlacement.center;
        debugTreasureExclusionRadius = treasureIslandPlacement.exclusionRadius;
        debugTreasureTargetValid = treasureIslandPlacement.hasTarget;
        debugTreasureTargetCellA = treasureIslandPlacement.targetCellA;
        debugTreasureTargetCellB = treasureIslandPlacement.targetCellB;
        debugTreasureTargetAnchor = treasureIslandPlacement.targetContactAnchorWorld;

        if (referencesValid && loadedChunks.Count > 0)
            RegenerateLoadedChunksForTreasureSelection();

        BeginWarmShopDockRegistrationCache();
        TreasurePlacementChanged?.Invoke();

        return true;
    }

    public void CollectAllShopDockRegistrations(List<ShopDockRegistration> results)
    {
        if (results == null)
            return;

        EnsureAllShopDockRegistrationCacheReady();
        CopyShopDockRegistrations(cachedAllShopDockRegistrations, results);
    }

    public void CollectShopDockRegistrationsInRadius(List<ShopDockRegistration> results, Vector2 center, float radius)
    {
        CollectShopDockRegistrationsInternal(results, center, radius, true);
    }

    void CollectShopDockRegistrationsInternal(List<ShopDockRegistration> results, Vector2 center, float radius, bool filterByRadius)
    {
        if (results == null)
            return;

        results.Clear();
        if (!referencesValid || !generateDocks)
            return;

        float scanRadius = Mathf.Max(0f, radius) + GetMaximumPossibleIslandRadius() + candidateSectorSize;
        int minSector = Mathf.FloorToInt((center.x - scanRadius) / candidateSectorSize) - 1;
        int maxSector = Mathf.CeilToInt((center.x + scanRadius) / candidateSectorSize) + 1;
        int minSectorY = Mathf.FloorToInt((center.y - scanRadius) / candidateSectorSize) - 1;
        int maxSectorY = Mathf.CeilToInt((center.y + scanRadius) / candidateSectorSize) + 1;
        emittedShopIdsScratch.Clear();
        float radiusSqr = radius * radius;

        for (int y = minSectorY; y <= maxSectorY; y++)
        {
            for (int x = minSector; x <= maxSector; x++)
            {
                Vector2Int sectorCoord = new Vector2Int(x, y);
                for (int slotIndex = 0; slotIndex < candidateSlotsPerSector; slotIndex++)
                {
                    if (!TryGetCachedAcceptedIslandSource(sectorCoord, slotIndex, out CachedAcceptedIslandSource source)
                        || !TryBuildPlacementCandidateFromSource(source.descriptor, out PlacementCandidate candidate))
                        continue;

                    if (emittedShopIdsScratch.Contains(candidate.deterministicKey))
                        continue;

                    if (!TryResolveShopDockPlacement(candidate, out DockPlacement chosen))
                        continue;

                    ShopDockRegistration registration = BuildShopDockRegistration(candidate, CellToChunkCoord(chosen.rootCell), chosen);
                    if (filterByRadius)
                    {
                        Vector2 offset = (Vector2)registration.AnchorWorldPosition - center;
                        if (offset.sqrMagnitude > radiusSqr)
                            continue;
                    }

                    emittedShopIdsScratch.Add(candidate.deterministicKey);
                    results.Add(registration);
                }
            }
        }
    }

    void CopyShopDockRegistrations(List<ShopDockRegistration> source, List<ShopDockRegistration> destination)
    {
        destination.Clear();
        for (int i = 0; i < source.Count; i++)
            destination.Add(source[i]);
    }

    void InvalidateShopDockRegistrationCache()
    {
        allShopDockRegistrationCacheValid = false;
        allShopDockRegistrationBuildInProgress = false;
        cachedAllShopDockRegistrations.Clear();
        buildingAllShopDockRegistrations.Clear();
        buildingAllShopDockRegistrationIds.Clear();

        if (warmShopDockRegistrationCacheRoutine != null)
        {
            StopCoroutine(warmShopDockRegistrationCacheRoutine);
            warmShopDockRegistrationCacheRoutine = null;
        }
    }

    void BeginWarmShopDockRegistrationCache()
    {
        if (!referencesValid || !generateDocks || allShopDockRegistrationCacheValid || allShopDockRegistrationBuildInProgress)
            return;

        warmShopDockRegistrationCacheRoutine = StartCoroutine(WarmShopDockRegistrationCacheRoutine());
    }

    IEnumerator WarmShopDockRegistrationCacheRoutine()
    {
        BeginAllShopDockRegistrationCacheBuild();
        while (allShopDockRegistrationBuildInProgress)
        {
            StepAllShopDockRegistrationCacheBuild(ShopDockCacheBuildStepsPerFrame);
            yield return null;
        }

        warmShopDockRegistrationCacheRoutine = null;
    }

    void EnsureAllShopDockRegistrationCacheReady()
    {
        if (allShopDockRegistrationCacheValid)
            return;

        BeginAllShopDockRegistrationCacheBuild();
        while (allShopDockRegistrationBuildInProgress)
            StepAllShopDockRegistrationCacheBuild(int.MaxValue);
    }

    void BeginAllShopDockRegistrationCacheBuild()
    {
        if (allShopDockRegistrationCacheValid || allShopDockRegistrationBuildInProgress || !referencesValid || !generateDocks)
            return;

        float searchRadius = worldSettings != null
            ? worldSettings.PlayableRadiusTiles + GetMaximumPossibleIslandRadius() + candidateSectorSize
            : GetMaximumPossibleIslandRadius() + candidateSectorSize;
        float scanRadius = Mathf.Max(0f, searchRadius) + GetMaximumPossibleIslandRadius() + candidateSectorSize;

        allShopDockRegistrationBuildMinSectorX = Mathf.FloorToInt((-scanRadius) / candidateSectorSize) - 1;
        allShopDockRegistrationBuildMaxSectorX = Mathf.CeilToInt((scanRadius) / candidateSectorSize) + 1;
        allShopDockRegistrationBuildMinSectorY = Mathf.FloorToInt((-scanRadius) / candidateSectorSize) - 1;
        allShopDockRegistrationBuildMaxSectorY = Mathf.CeilToInt((scanRadius) / candidateSectorSize) + 1;
        allShopDockRegistrationBuildCurrentX = allShopDockRegistrationBuildMinSectorX;
        allShopDockRegistrationBuildCurrentY = allShopDockRegistrationBuildMinSectorY;
        allShopDockRegistrationBuildCurrentSlotIndex = 0;
        buildingAllShopDockRegistrations.Clear();
        buildingAllShopDockRegistrationIds.Clear();
        allShopDockRegistrationBuildInProgress = true;
    }

    void StepAllShopDockRegistrationCacheBuild(int maxSteps)
    {
        if (!allShopDockRegistrationBuildInProgress)
            return;

        int stepsRemaining = Mathf.Max(1, maxSteps);
        while (allShopDockRegistrationBuildInProgress && stepsRemaining-- > 0)
        {
            if (allShopDockRegistrationBuildCurrentY > allShopDockRegistrationBuildMaxSectorY)
            {
                FinishAllShopDockRegistrationCacheBuild();
                break;
            }

            Vector2Int sectorCoord = new Vector2Int(
                allShopDockRegistrationBuildCurrentX,
                allShopDockRegistrationBuildCurrentY);
            int slotIndex = allShopDockRegistrationBuildCurrentSlotIndex;

            if (TryGetAcceptedCandidate(sectorCoord, slotIndex, out PlacementCandidate candidate)
                && !buildingAllShopDockRegistrationIds.Contains(candidate.deterministicKey)
                && TryResolveShopDockPlacement(candidate, out DockPlacement chosen))
            {
                buildingAllShopDockRegistrationIds.Add(candidate.deterministicKey);
                buildingAllShopDockRegistrations.Add(
                    BuildShopDockRegistration(candidate, CellToChunkCoord(chosen.rootCell), chosen));
            }

            AdvanceAllShopDockRegistrationBuildCursor();
        }
    }

    void AdvanceAllShopDockRegistrationBuildCursor()
    {
        allShopDockRegistrationBuildCurrentSlotIndex++;
        if (allShopDockRegistrationBuildCurrentSlotIndex < candidateSlotsPerSector)
            return;

        allShopDockRegistrationBuildCurrentSlotIndex = 0;
        allShopDockRegistrationBuildCurrentX++;
        if (allShopDockRegistrationBuildCurrentX <= allShopDockRegistrationBuildMaxSectorX)
            return;

        allShopDockRegistrationBuildCurrentX = allShopDockRegistrationBuildMinSectorX;
        allShopDockRegistrationBuildCurrentY++;
    }

    void FinishAllShopDockRegistrationCacheBuild()
    {
        cachedAllShopDockRegistrations.Clear();
        CopyShopDockRegistrations(buildingAllShopDockRegistrations, cachedAllShopDockRegistrations);
        buildingAllShopDockRegistrations.Clear();
        buildingAllShopDockRegistrationIds.Clear();
        allShopDockRegistrationBuildInProgress = false;
        allShopDockRegistrationCacheValid = true;
    }

    RectInt GetCandidateQueryBounds(RectInt chunkRect, float extraRadius)
    {
        float searchPadding = Mathf.Max(extraRadius, GetMaximumPossibleIslandRadius()) + candidateSectorSize;
        int minX = Mathf.FloorToInt((chunkRect.xMin - searchPadding) / candidateSectorSize);
        int minY = Mathf.FloorToInt((chunkRect.yMin - searchPadding) / candidateSectorSize);
        int maxX = Mathf.FloorToInt((chunkRect.xMax + searchPadding) / candidateSectorSize) + 1;
        int maxY = Mathf.FloorToInt((chunkRect.yMax + searchPadding) / candidateSectorSize) + 1;
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    bool TryGetAcceptedCandidate(Vector2Int sectorCoord, int slotIndex, out PlacementCandidate candidate)
    {
        candidate = BuildPlacementCandidate(sectorCoord, slotIndex);
        if (!candidate.exists)
            return false;

        if (!IsInsidePlayableRadius(candidate.island.center, candidate.maxRadius))
            return false;

        if (TreasureSuppressesCandidate(candidate))
            return false;

        int competitionRadius = GetCandidateCompetitionSectorRadius();
        for (int y = sectorCoord.y - competitionRadius; y <= sectorCoord.y + competitionRadius; y++)
        {
            for (int x = sectorCoord.x - competitionRadius; x <= sectorCoord.x + competitionRadius; x++)
            {
                Vector2Int neighborSectorCoord = new Vector2Int(x, y);
                for (int neighborSlotIndex = 0; neighborSlotIndex < candidateSlotsPerSector; neighborSlotIndex++)
                {
                    if (neighborSectorCoord == sectorCoord && neighborSlotIndex == slotIndex)
                        continue;

                    PlacementCandidate competitor = BuildPlacementCandidate(neighborSectorCoord, neighborSlotIndex);
                    if (!competitor.exists)
                        continue;

                    if (!IsInsidePlayableRadius(competitor.island.center, competitor.maxRadius))
                        continue;

                    float spacingMultiplier = Mathf.Max(
                        GetSpacingMultiplier(candidate.normalizedRadius),
                        GetSpacingMultiplier(competitor.normalizedRadius));
                    float lobePadding =
                        Mathf.Max(candidate.island.lobeStrength, competitor.island.lobeStrength)
                        * Mathf.Max(candidate.maxRadius, competitor.maxRadius) * 0.8f;
                    float minSpacing =
                        (candidate.maxRadius + competitor.maxRadius) * spacingMultiplier
                        + islandFootprintSeparationPadding
                        + lobePadding;
                    if (IsShopIsland(candidate) && IsShopIsland(competitor))
                        minSpacing += shopIslandExtraSeparationPadding;
                    if ((competitor.island.center - candidate.island.center).sqrMagnitude >= minSpacing * minSpacing)
                        continue;

                    if (CompetitorSuppressesCandidate(candidate, competitor))
                        return false;
                }
            }
        }

        return true;
    }

    PlacementCandidate BuildPlacementCandidate(Vector2Int sectorCoord, int slotIndex)
    {
        PlacementCandidate candidate = new PlacementCandidate
        {
            sectorCoord = sectorCoord,
            slotIndex = slotIndex,
            deterministicKey = PackCandidateDeterministicKey(sectorCoord, slotIndex)
        };

        Vector2 center = GetCandidateCenter(sectorCoord, slotIndex);
        float normalizedRadius = worldSettings != null
            ? worldSettings.NormalizeRadius(center.magnitude)
            : 0f;
        IslandParameters parameters = BuildIslandParametersForCenter(candidate.deterministicKey, center, normalizedRadius);
        float occupancyChance = GetRegionalOccupancyChance(parameters.center) * GetOccupancyMultiplier(normalizedRadius);
        float occupancyRoll = Hash01(candidate.deterministicKey.x, candidate.deterministicKey.y, seed);
        if (occupancyRoll > occupancyChance)
            return candidate;

        float randomScore = Hash01(candidate.deterministicKey.x, candidate.deterministicKey.y, seed + 211);
        candidate.island = parameters;
        candidate.occupancyChance = occupancyChance;
        candidate.score = randomScore * (1f - candidateScoreRegionBias) + occupancyChance * candidateScoreRegionBias;
        candidate.maxRadius = parameters.footprintRadius;
        candidate.normalizedRadius = normalizedRadius;
        candidate.exists = true;
        return candidate;
    }

    Vector2 GetCandidateCenter(Vector2Int sectorCoord, int slotIndex)
    {
        Vector2 sectorOrigin = new Vector2(sectorCoord.x * candidateSectorSize, sectorCoord.y * candidateSectorSize);
        float paddedMin = candidatePointPadding;
        float paddedMax = 1f - candidatePointPadding;

        return new Vector2(
            sectorOrigin.x + Mathf.Lerp(paddedMin, paddedMax, Hash01Candidate(sectorCoord, slotIndex, seed + 11)) * candidateSectorSize,
            sectorOrigin.y + Mathf.Lerp(paddedMin, paddedMax, Hash01Candidate(sectorCoord, slotIndex, seed + 23)) * candidateSectorSize);
    }

    float GetRegionalOccupancyChance(Vector2 candidateCenter)
    {
        float regionNoise = Mathf.PerlinNoise(
            (candidateCenter.x + seed * 0.17f) * regionNoiseScale,
            (candidateCenter.y - seed * 0.11f) * regionNoiseScale);

        return Mathf.Lerp(islandChanceMin, islandChanceMax, Mathf.Clamp01(regionNoise + regionNoiseBias));
    }

    int GetCandidateCompetitionSectorRadius()
    {
        float spacingRadius = GetMaximumPossibleIslandRadius() * 2f * GetMaximumSpacingMultiplier();
        return Mathf.Max(1, Mathf.CeilToInt(spacingRadius / Mathf.Max(candidateSectorSize, 1))) + 1;
    }

    bool CompetitorSuppressesCandidate(PlacementCandidate candidate, PlacementCandidate competitor)
    {
        if (competitor.score > candidate.score)
            return true;

        if (candidate.score > competitor.score)
            return false;

        if (competitor.deterministicKey.x != candidate.deterministicKey.x)
            return competitor.deterministicKey.x < candidate.deterministicKey.x;

        return competitor.deterministicKey.y < candidate.deterministicKey.y;
    }

    bool CandidateCanAffectChunk(PlacementCandidate candidate, RectInt chunkRect)
    {
        float radius = candidate.maxRadius + 1f;
        float minX = candidate.island.center.x - radius;
        float maxX = candidate.island.center.x + radius;
        float minY = candidate.island.center.y - radius;
        float maxY = candidate.island.center.y + radius;

        return maxX >= chunkRect.xMin
            && minX <= chunkRect.xMax
            && maxY >= chunkRect.yMin
            && minY <= chunkRect.yMax;
    }

    bool TryGetCachedAcceptedIslandSource(Vector2Int sectorCoord, int slotIndex, out CachedAcceptedIslandSource source)
    {
        long cacheKey = PackSectorSlotCacheKey(sectorCoord, slotIndex);
        if (cachedAcceptedIslandSourcesBySectorSlot.TryGetValue(cacheKey, out source))
            return source.accepted;

        source = new CachedAcceptedIslandSource
        {
            evaluated = true,
            accepted = false
        };

        if (TryGetAcceptedCandidate(sectorCoord, slotIndex, out PlacementCandidate candidate))
        {
            source.accepted = true;
            source.descriptor = ToDescriptor(candidate);
            source.island = candidate.island;
            cachedAcceptedIslandSourcesByDeterministicKey[PackDeterministicIslandKey(source.descriptor.deterministicKey)] = source;
        }

        cachedAcceptedIslandSourcesBySectorSlot[cacheKey] = source;
        return source.accepted;
    }

    bool TryBuildPlacementCandidateFromSource(IslandSourceDescriptor descriptor, out PlacementCandidate candidate)
    {
        candidate = default;

        if (!TryEnsureAcceptedIslandSourceCached(descriptor.deterministicKey, out CachedAcceptedIslandSource source))
        {
            return false;
        }

        candidate = new PlacementCandidate
        {
            deterministicKey = descriptor.deterministicKey,
            island = source.island,
            maxRadius = descriptor.maxRadius,
            normalizedRadius = descriptor.normalizedRadius,
            exists = true
        };
        return true;
    }

    bool TryEnsureAcceptedIslandSourceCached(Vector2Int deterministicKey, out CachedAcceptedIslandSource source)
    {
        CacheTreasureIslandSourceIfNeeded();
        long packedKey = PackDeterministicIslandKey(deterministicKey);
        if (cachedAcceptedIslandSourcesByDeterministicKey.TryGetValue(packedKey, out source) && source.accepted)
            return true;

        int sectorY = FloorDiv(deterministicKey.y, DeterministicKeySlotStride);
        int slotIndex = deterministicKey.y - sectorY * DeterministicKeySlotStride;
        if (slotIndex < 0 || slotIndex >= candidateSlotsPerSector)
            return false;

        return TryGetCachedAcceptedIslandSource(new Vector2Int(deterministicKey.x, sectorY), slotIndex, out source);
    }

    void CacheTreasureIslandSourceIfNeeded()
    {
        if (!treasureIslandPlacement.isValid)
            return;

        CachedAcceptedIslandSource source = new CachedAcceptedIslandSource
        {
            evaluated = true,
            accepted = true,
            descriptor = ToDescriptor(treasureIslandPlacement),
            island = ToIslandParameters(treasureIslandPlacement)
        };
        cachedAcceptedIslandSourcesByDeterministicKey[PackDeterministicIslandKey(source.descriptor.deterministicKey)] = source;
    }

    void InvalidateAcceptedIslandSourceCache()
    {
        cachedAcceptedIslandSourcesBySectorSlot.Clear();
        cachedAcceptedIslandSourcesByDeterministicKey.Clear();
        AcceptedIslandSourceCacheInvalidated?.Invoke();
    }

    static bool DoesRevealCircleIntersectSource(Vector2 worldPosition, float revealRadius, IslandSourceDescriptor source)
    {
        float radius = revealRadius + source.maxRadius;
        return (source.center - worldPosition).sqrMagnitude <= radius * radius;
    }

    void PaintIsland(RectInt chunkRect, IslandParameters island)
    {
        for (int y = chunkRect.yMin; y < chunkRect.yMax; y++)
        {
            for (int x = chunkRect.xMin; x < chunkRect.xMax; x++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                if (TryResolveIslandTileAtCell(cell, island, out TileBase tileToPaint))
                    islandTilemap.SetTile(cell, tileToPaint);
            }
        }
    }

    bool TryResolveIslandTileAtCell(Vector3Int cell, IslandParameters island, out TileBase tileToPaint)
    {
        tileToPaint = null;

        Vector2 local = GetIslandLocalPoint(cell, island);
        if (!TryGetIslandShorelineMask(local, island, cell.x, cell.y, out float shorelineMask))
            return false;

        float interiorNoise = Mathf.PerlinNoise(
            (cell.x + seed * 0.47f) * interiorNoiseScale,
            (cell.y + seed * 0.53f) * interiorNoiseScale);

        float normalizedInterior = Mathf.InverseLerp(islandEdgeThreshold, 1f, shorelineMask);
        float elevationScore = Mathf.Clamp01(normalizedInterior * 0.72f + interiorNoise * 0.28f);

        tileToPaint = lowElevationTile;
        if (elevationScore >= highElevationThreshold)
            tileToPaint = highElevationTile;
        else if (elevationScore >= midElevationThreshold)
            tileToPaint = midElevationTile;

        return true;
    }

    Vector2 GetIslandLocalPoint(Vector3Int cell, IslandParameters island)
    {
        Vector2 cellCenter = new Vector2(cell.x + 0.5f, cell.y + 0.5f);
        return Quaternion.Euler(0f, 0f, -island.rotationDegrees) * (cellCenter - island.center);
    }

    bool TryGetIslandShorelineMask(Vector2 local, IslandParameters island, int x, int y, out float shorelineMask)
    {
        shorelineMask = 0f;

        float ellipseDistance =
            Mathf.Sqrt(
                (local.x * local.x) / Mathf.Max(island.radii.x * island.radii.x, 0.0001f) +
                (local.y * local.y) / Mathf.Max(island.radii.y * island.radii.y, 0.0001f));

        float baseMask = 1f - ellipseDistance;
        if (baseMask <= -0.25f)
            return false;

        float edgeNoise = Mathf.PerlinNoise(
            (x + seed * 0.31f) * edgeNoiseScale,
            (y - seed * 0.29f) * edgeNoiseScale);
        float edgePerturb = (edgeNoise - 0.5f) * 2f * edgeNoiseStrength;

        float nearestFeatureDistance = GetNearestVoronoiDistance(local, island.voronoiPoints);
        float voronoiRadius = Mathf.Max(Mathf.Min(island.radii.x, island.radii.y), 0.01f);
        float voronoiFactor = 1f - Mathf.Clamp01(nearestFeatureDistance / voronoiRadius);
        float voronoiPerturb = (voronoiFactor - 0.5f) * 2f * voronoiStrength;
        float lobePerturb = GetLobePerturbation(local, island.lobePoints, island.lobeRadii, island.lobeStrength);

        shorelineMask = baseMask + edgePerturb + voronoiPerturb + lobePerturb;
        return shorelineMask >= islandEdgeThreshold;
    }

    bool IsShopIsland(PlacementCandidate candidate)
    {
        float shopRoll = Hash01(candidate.deterministicKey.x, candidate.deterministicKey.y, seed + 263);
        return shopRoll <= shopIslandChance;
    }

    IslandSourceDescriptor ToDescriptor(PlacementCandidate candidate)
    {
        return new IslandSourceDescriptor
        {
            center = candidate.island.center,
            radii = candidate.island.radii,
            rotationDegrees = candidate.island.rotationDegrees,
            maxRadius = candidate.maxRadius,
            normalizedRadius = candidate.normalizedRadius,
            isTreasure = false,
            deterministicKey = candidate.deterministicKey
        };
    }

    IslandSourceDescriptor ToDescriptor(TreasureIslandPlacement treasure)
    {
        return new IslandSourceDescriptor
        {
            center = treasure.center,
            radii = treasure.radii,
            rotationDegrees = treasure.rotationDegrees,
            maxRadius = treasure.MaxRadius,
            normalizedRadius = treasure.normalizedRadius,
            isTreasure = true,
            // Reserve an impossible candidate-slot remainder so treasure cannot collide
            // with any normal island candidate deterministic key.
            deterministicKey = new Vector2Int(seed, seed * DeterministicKeySlotStride + (DeterministicKeySlotStride - 1))
        };
    }

    bool IsInsidePlayableRadius(Vector2 center, float islandRadius)
    {
        if (worldSettings == null)
            return true;

        return center.magnitude + islandRadius <= worldSettings.PlayableRadiusTiles;
    }

    float GetSpacingMultiplier(float normalizedRadius)
    {
        return worldSettings != null
            ? worldSettings.EvaluateSpacingMultiplier(normalizedRadius)
            : islandSpacingMultiplier;
    }

    float GetMaximumSpacingMultiplier()
    {
        return worldSettings != null
            ? worldSettings.EvaluateSpacingMultiplier(1f)
            : islandSpacingMultiplier;
    }

    float GetOccupancyMultiplier(float normalizedRadius)
    {
        return worldSettings != null
            ? worldSettings.EvaluateOccupancyMultiplier(normalizedRadius)
            : 1f;
    }

    bool TreasureSuppressesCandidate(PlacementCandidate candidate)
    {
        if (!treasureIslandPlacement.isValid)
            return false;

        float minDistance = candidate.maxRadius + treasureIslandPlacement.exclusionRadius;
        return (candidate.island.center - treasureIslandPlacement.center).sqrMagnitude < minDistance * minDistance;
    }

    bool TreasureCanAffectChunk(RectInt chunkRect)
    {
        if (!treasureIslandPlacement.isValid)
            return false;

        return CandidateCanAffectChunk(
            treasureIslandPlacement.center,
            treasureIslandPlacement.MaxRadius,
            chunkRect);
    }

    static bool CandidateCanAffectChunk(Vector2 center, float radius, RectInt chunkRect)
    {
        radius += 1f;
        float minX = center.x - radius;
        float maxX = center.x + radius;
        float minY = center.y - radius;
        float maxY = center.y + radius;

        return maxX >= chunkRect.xMin
            && minX <= chunkRect.xMax
            && maxY >= chunkRect.yMin
            && minY <= chunkRect.yMax;
    }

    TreasureIslandPlacement BuildTreasureIslandPlacement()
    {
        TreasureIslandPlacement placement = default;
        if (worldSettings == null)
            return placement;

        float minBandRadius = worldSettings.GetTreasureBandMinRadiusTiles();
        float maxBandRadius = worldSettings.GetTreasureBandMaxRadiusTiles();
        float bestScore = float.NegativeInfinity;

        for (int attempt = 0; attempt < Mathf.Max(1, treasurePlacementAttempts); attempt++)
        {
            TreasureIslandPlacement candidate = BuildTreasureAttempt(attempt, minBandRadius, maxBandRadius);
            if (!candidate.isValid)
                continue;

            if (candidate.normalizedRadius < worldSettings.TreasureBandMinNormalized
                || candidate.normalizedRadius > worldSettings.TreasureBandMaxNormalized)
            {
                continue;
            }

            if (!IsInsidePlayableRadius(candidate.center, candidate.MaxRadius))
                continue;

            if (!candidate.hasTarget)
                continue;

            float score = Hash01(attempt, seed, seed + 997);
            if (score <= bestScore)
                continue;

            bestScore = score;
            placement = candidate;
        }

        return placement;
    }

    TreasureIslandPlacement BuildTreasureAttempt(int attempt, float minBandRadius, float maxBandRadius)
    {
        TreasureIslandPlacement placement = default;
        float angle = Hash01(attempt, seed, seed + 401) * 360f;
        float radialT = Hash01(attempt, seed, seed + 419);
        float radialDistance = Mathf.Lerp(minBandRadius, maxBandRadius, radialT);
        Vector2 center = Quaternion.Euler(0f, 0f, angle) * (Vector2.up * radialDistance);

        Vector2Int treasureKey = new Vector2Int(
            seed + 7000 + attempt * 17,
            seed + 9000 + attempt * 29);
        IslandParameters island = BuildIslandParametersForCenter(treasureKey, center, worldSettings.NormalizeRadius(center.magnitude));
        float normalizedRadius = worldSettings.NormalizeRadius(center.magnitude);
        float exclusionRadius = island.footprintRadius * worldSettings.TreasureIsolationMultiplier;

        placement.isValid = true;
        placement.center = island.center;
        placement.radii = island.radii;
        placement.rotationDegrees = island.rotationDegrees;
        placement.voronoiPoints = island.voronoiPoints;
        placement.lobePoints = island.lobePoints;
        placement.lobeRadii = island.lobeRadii;
        placement.lobeStrength = island.lobeStrength;
        placement.footprintRadius = island.footprintRadius;
        placement.exclusionRadius = exclusionRadius;
        placement.normalizedRadius = normalizedRadius;

        if (!TryResolveTreasureTarget(island, out TreasureTargetCandidate target))
            return default;

        placement.hasTarget = true;
        placement.targetCellA = target.cellA;
        placement.targetCellB = target.cellB;
        placement.targetOutwardDirection = target.outwardDirection;
        placement.targetMidpointWorld = GetPairMidpointWorld(target.cellA, target.cellB);
        placement.targetContactAnchorWorld = placement.targetMidpointWorld + target.outwardDirection * 0.8f;
        return placement;
    }

    bool TryResolveTreasureTarget(IslandParameters island, out TreasureTargetCandidate chosenTarget)
    {
        chosenTarget = default;
        RectInt searchRect = GetDockSearchRect(island);
        bool foundCandidate = false;

        for (int y = searchRect.yMin; y < searchRect.yMax; y++)
        {
            for (int x = searchRect.xMin; x < searchRect.xMax; x++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                if (!IsCellInsideIsland(cell, island))
                    continue;

                EvaluateTreasureTargetCandidate(island, cell, Vector3Int.right, Vector2.up, ref chosenTarget, ref foundCandidate);
                EvaluateTreasureTargetCandidate(island, cell, Vector3Int.right, Vector2.down, ref chosenTarget, ref foundCandidate);
                EvaluateTreasureTargetCandidate(island, cell, Vector3Int.up, Vector2.left, ref chosenTarget, ref foundCandidate);
                EvaluateTreasureTargetCandidate(island, cell, Vector3Int.up, Vector2.right, ref chosenTarget, ref foundCandidate);
            }
        }

        return foundCandidate;
    }

    void EvaluateTreasureTargetCandidate(
        IslandParameters island,
        Vector3Int firstCell,
        Vector3Int pairDirection,
        Vector2 outwardDirection,
        ref TreasureTargetCandidate bestCandidate,
        ref bool foundCandidate)
    {
        Vector3Int secondCell = firstCell + pairDirection;
        if (!IsCellInsideIsland(secondCell, island))
            return;

        Vector3Int outwardCellA = firstCell + ToCellDirection(outwardDirection);
        Vector3Int outwardCellB = secondCell + ToCellDirection(outwardDirection);
        if (IsCellInsideIsland(outwardCellA, island) || IsCellInsideIsland(outwardCellB, island))
            return;

        Vector3Int inwardDirection = -ToCellDirection(outwardDirection);
        if (!IsCellInsideIsland(firstCell + inwardDirection, island) || !IsCellInsideIsland(secondCell + inwardDirection, island))
            return;

        int clearDepth = MeasureTreasureApproachDepth(island, firstCell, secondCell, pairDirection, outwardDirection);
        if (clearDepth < treasureApproachLengthCells)
            return;

        int flatness = 0;
        if (IsCellInsideIsland(firstCell - pairDirection, island))
            flatness++;
        if (IsCellInsideIsland(secondCell + pairDirection, island))
            flatness++;

        int seamBuffer = Mathf.Min(GetChunkEdgeBuffer(firstCell), GetChunkEdgeBuffer(secondCell));
        float selectionScore =
            clearDepth * 100f
            + flatness * 12f
            + seamBuffer * 0.35f
            + Hash01(firstCell.x + secondCell.x, firstCell.y + secondCell.y, seed + 1703);

        TreasureTargetCandidate candidate = new TreasureTargetCandidate
        {
            cellA = firstCell,
            cellB = secondCell,
            outwardDirection = outwardDirection.normalized,
            clearDepth = clearDepth,
            flatness = flatness,
            seamBuffer = seamBuffer,
            selectionScore = selectionScore
        };

        if (!foundCandidate || candidate.selectionScore > bestCandidate.selectionScore)
        {
            bestCandidate = candidate;
            foundCandidate = true;
        }
    }

    int MeasureTreasureApproachDepth(
        IslandParameters island,
        Vector3Int firstCell,
        Vector3Int secondCell,
        Vector3Int pairDirection,
        Vector2 outwardDirection)
    {
        Vector3Int outwardCellDirection = ToCellDirection(outwardDirection);
        int clearDepth = 0;

        for (int step = 1; step <= treasureApproachLengthCells + 2; step++)
        {
            bool rowClear = true;
            for (int lateral = -treasureApproachSideClearanceCells; lateral <= 1 + treasureApproachSideClearanceCells; lateral++)
            {
                Vector3Int baseCell = lateral <= 0 ? firstCell : secondCell;
                int tangentOffset = lateral <= 0 ? lateral : lateral - 1;
                Vector3Int waterCell = baseCell + pairDirection * tangentOffset + outwardCellDirection * step;
                if (IsCellInsideIsland(waterCell, island))
                {
                    rowClear = false;
                    break;
                }
            }

            if (!rowClear)
                break;

            clearDepth++;
        }

        return clearDepth;
    }

    static Vector3Int ToCellDirection(Vector2 worldDirection)
    {
        return new Vector3Int(
            Mathf.RoundToInt(worldDirection.x),
            Mathf.RoundToInt(worldDirection.y),
            0);
    }

    int GetChunkEdgeBuffer(Vector3Int cell)
    {
        int localX = Mathf.Abs(Mod(cell.x, chunkSize));
        int localY = Mathf.Abs(Mod(cell.y, chunkSize));
        int distanceToXEdge = Mathf.Min(localX, (chunkSize - 1) - localX);
        int distanceToYEdge = Mathf.Min(localY, (chunkSize - 1) - localY);
        return Mathf.Min(distanceToXEdge, distanceToYEdge);
    }

    static int Mod(int value, int modulus)
    {
        if (modulus == 0)
            return 0;

        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    Vector2 GetPairMidpointWorld(Vector3Int cellA, Vector3Int cellB)
    {
        Vector3 a = islandTilemap.GetCellCenterWorld(cellA);
        Vector3 b = islandTilemap.GetCellCenterWorld(cellB);
        Vector3 midpoint = (a + b) * 0.5f;
        return new Vector2(midpoint.x, midpoint.y);
    }

    IslandParameters BuildIslandParametersForCenter(Vector2Int deterministicKey, Vector2 center, float normalizedRadius)
    {
        IslandParameters parameters = new IslandParameters();
        parameters.center = center;

        bool isLandmark = Hash01(deterministicKey.x, deterministicKey.y, seed + 31)
            <= Mathf.Lerp(landmarkChanceInner, landmarkChanceOuter, Mathf.SmoothStep(0f, 1f, normalizedRadius));

        float averageRadius = Mathf.Lerp(minRadiusTiles, maxRadiusTiles, Hash01(deterministicKey.x, deterministicKey.y, seed + 41));
        if (isLandmark)
        {
            averageRadius *= Mathf.Lerp(
                landmarkRadiusMinMultiplier,
                landmarkRadiusMaxMultiplier,
                Hash01(deterministicKey.x, deterministicKey.y, seed + 43));
        }

        averageRadius = Mathf.Min(averageRadius, isLandmark ? landmarkRadiusCeilingTiles : maxRadiusTiles);
        float normalizedSize = Mathf.InverseLerp(minRadiusTiles, isLandmark ? landmarkRadiusCeilingTiles : maxRadiusTiles, averageRadius);

        float aspectMin = isLandmark
            ? landmarkAspectRatioMin
            : Mathf.Lerp(0.92f, mediumAspectRatioMin, normalizedSize);
        float aspectMax = isLandmark
            ? landmarkAspectRatioMax
            : Mathf.Lerp(1.08f, mediumAspectRatioMax, normalizedSize);
        float aspectRatio = Mathf.Lerp(aspectMin, aspectMax, Hash01(deterministicKey.x, deterministicKey.y, seed + 57));
        aspectRatio = Mathf.Max(0.25f, aspectRatio);

        float radiusA = averageRadius * Mathf.Sqrt(aspectRatio);
        float radiusB = averageRadius / Mathf.Sqrt(aspectRatio);
        float radiusCeiling = isLandmark ? landmarkRadiusCeilingTiles : maxRadiusTiles;
        if (radiusA > radiusCeiling || radiusB > radiusCeiling)
        {
            float scale = radiusCeiling / Mathf.Max(radiusA, radiusB);
            radiusA *= scale;
            radiusB *= scale;
        }

        parameters.radii = new Vector2(radiusA, radiusB);
        parameters.rotationDegrees = Hash01(deterministicKey.x, deterministicKey.y, seed + 79) * 360f;

        parameters.voronoiPoints = new Vector2[voronoiFeaturePointCount];
        for (int i = 0; i < voronoiFeaturePointCount; i++)
        {
            float px = Mathf.Lerp(-radiusA * 0.55f, radiusA * 0.55f, Hash01(deterministicKey.x, deterministicKey.y, seed + 101 + i * 13));
            float py = Mathf.Lerp(-radiusB * 0.55f, radiusB * 0.55f, Hash01(deterministicKey.x, deterministicKey.y, seed + 131 + i * 17));
            parameters.voronoiPoints[i] = new Vector2(px, py);
        }

        int lobeCount = GetLobeCountForIsland(isLandmark, normalizedSize, deterministicKey);
        parameters.lobeStrength = isLandmark
            ? landmarkLobeStrength
            : Mathf.Lerp(0f, mediumLobeStrength, normalizedSize);
        if (lobeCount > 0 && parameters.lobeStrength > 0.001f)
        {
            parameters.lobePoints = new Vector2[lobeCount];
            parameters.lobeRadii = new float[lobeCount];
            for (int i = 0; i < lobeCount; i++)
            {
                float angle = Hash01(deterministicKey.x, deterministicKey.y, seed + 173 + i * 19) * Mathf.PI * 2f;
                float directionalRadius = GetDirectionalEllipseRadius(parameters.radii, angle);
                float distanceAlongAxis = Mathf.Lerp(0.35f, 0.82f, Hash01(deterministicKey.x, deterministicKey.y, seed + 197 + i * 23));
                float lobeRadius = directionalRadius * Mathf.Lerp(0.28f, 0.58f, Hash01(deterministicKey.x, deterministicKey.y, seed + 223 + i * 29));
                parameters.lobePoints[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * directionalRadius * distanceAlongAxis;
                parameters.lobeRadii[i] = lobeRadius;
            }
        }
        else
        {
            parameters.lobePoints = Array.Empty<Vector2>();
            parameters.lobeRadii = Array.Empty<float>();
            parameters.lobeStrength = 0f;
        }

        parameters.footprintRadius = CalculateFootprintRadius(parameters);

        return parameters;
    }

    IslandParameters ToIslandParameters(TreasureIslandPlacement treasure)
    {
        return new IslandParameters
        {
            center = treasure.center,
            radii = treasure.radii,
            rotationDegrees = treasure.rotationDegrees,
            voronoiPoints = treasure.voronoiPoints,
            lobePoints = treasure.lobePoints ?? Array.Empty<Vector2>(),
            lobeRadii = treasure.lobeRadii ?? Array.Empty<float>(),
            lobeStrength = treasure.lobeStrength,
            footprintRadius = treasure.MaxRadius
        };
    }

    int CountLoadedShopIslands(RectInt requiredChunkRect)
    {
        int count = 0;
        RectInt candidateBounds = GetCandidateQueryBounds(requiredChunkRect, GetMaximumPossibleIslandRadius());
        countedShopIslandKeysScratch.Clear();

        for (int y = candidateBounds.yMin; y < candidateBounds.yMax; y++)
        {
            for (int x = candidateBounds.xMin; x < candidateBounds.xMax; x++)
            {
                Vector2Int sectorCoord = new Vector2Int(x, y);
                for (int slotIndex = 0; slotIndex < candidateSlotsPerSector; slotIndex++)
                {
                    if (!TryGetCachedAcceptedIslandSource(sectorCoord, slotIndex, out CachedAcceptedIslandSource source)
                        || !TryBuildPlacementCandidateFromSource(source.descriptor, out PlacementCandidate candidate))
                        continue;

                    if (countedShopIslandKeysScratch.Contains(candidate.deterministicKey))
                        continue;

                    if (!TryResolveShopDockPlacement(candidate, out _))
                        continue;

                    bool touchesLoadedChunk = false;
                    foreach (Vector2Int loadedChunk in loadedChunks)
                    {
                        if (!CandidateCanAffectChunk(candidate, GetChunkRect(loadedChunk)))
                            continue;

                        touchesLoadedChunk = true;
                        break;
                    }

                    if (!touchesLoadedChunk)
                        continue;

                    countedShopIslandKeysScratch.Add(candidate.deterministicKey);
                    count++;
                }
            }
        }

        return count;
    }

    void TryPlaceDockForIsland(Vector2Int chunkCoord, RectInt chunkRect, PlacementCandidate candidate, DockPlacement chosen)
    {
        if (!PaintDock(chunkRect, chosen))
            return;

        ShopDockRegistered?.Invoke(BuildShopDockRegistration(candidate, chunkCoord, chosen));
    }

    void PaintTreasureTarget(RectInt chunkRect)
    {
        if (goldTilemap == null || goldTile == null || !treasureIslandPlacement.hasTarget)
            return;

        PaintTreasureCell(chunkRect, treasureIslandPlacement.targetCellA);
        PaintTreasureCell(chunkRect, treasureIslandPlacement.targetCellB);
    }

    void PaintTreasureCell(RectInt chunkRect, Vector3Int cell)
    {
        if (!chunkRect.Contains(new Vector2Int(cell.x, cell.y)))
            return;

        goldTilemap.SetTile(cell, goldTile);
    }

    bool TryResolveShopDockPlacement(PlacementCandidate candidate, out DockPlacement chosen)
    {
        chosen = default;
        if (!IsShopIsland(candidate))
            return false;

        if (cachedShopDockPlacements.TryGetValue(candidate.deterministicKey, out chosen))
            return true;

        if (!TryResolveDockPlacementForIsland(candidate, out chosen))
            return false;

        cachedShopDockPlacements[candidate.deterministicKey] = chosen;
        return true;
    }

    bool TryResolveDockPlacementForIsland(PlacementCandidate candidate, out DockPlacement chosen)
    {
        IslandParameters island = candidate.island;
        RectInt dockSearchRect = GetDockSearchRect(island);
        List<DockPlacement> strictCandidates = CollectDockPlacements(dockSearchRect, island);
        List<DockPlacement> candidatesToUse = strictCandidates;

        if (strictCandidates.Count == 0)
            candidatesToUse = CollectDockPlacements(dockSearchRect, island, true);

        if (candidatesToUse.Count == 0)
        {
            chosen = default;
            return false;
        }

        chosen = candidatesToUse[0];
        for (int i = 1; i < candidatesToUse.Count; i++)
        {
            if (candidatesToUse[i].selectionScore > chosen.selectionScore)
                chosen = candidatesToUse[i];
        }

        return true;
    }

    ShopDockRegistration BuildShopDockRegistration(PlacementCandidate candidate, Vector2Int chunkCoord, DockPlacement placement)
    {
        int paintedLength = Mathf.Min(placement.clearLength, dockLengthCells);
        Vector3 rootWorld = dockTilemap.GetCellCenterWorld(placement.rootCell);
        Vector3 outward = new Vector3(placement.outwardDirection.x, placement.outwardDirection.y, 0f);
        Vector3 spanStartWorld = rootWorld + outward * 1f;
        Vector3 spanEndWorld = rootWorld + outward * paintedLength;
        Vector3 anchorWorld = rootWorld + outward * (0.5f + paintedLength * 0.5f);

        return new ShopDockRegistration
        {
            ShopId = candidate.deterministicKey,
            AnchorWorldPosition = anchorWorld,
            SpanStartWorldPosition = spanStartWorld,
            SpanEndWorldPosition = spanEndWorld,
            SourceChunkCoord = chunkCoord,
            InteractionRadius = shopDockInteractionRadius
        };
    }

    RectInt GetDockSearchRect(IslandParameters island)
    {
        float maxRadius = Mathf.Max(island.radii.x, island.radii.y);
        int padding = Mathf.CeilToInt(maxRadius) + dockLengthCells + dockAttachmentDepthCells + 2;
        int minX = Mathf.FloorToInt(island.center.x) - padding;
        int minY = Mathf.FloorToInt(island.center.y) - padding;
        int maxX = Mathf.CeilToInt(island.center.x) + padding + 1;
        int maxY = Mathf.CeilToInt(island.center.y) + padding + 1;
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    List<DockPlacement> CollectDockPlacements(RectInt chunkRect, IslandParameters island, bool allowRelaxedFallback = false)
    {
        List<DockPlacement> placements = new List<DockPlacement>();

        for (int y = chunkRect.yMin; y < chunkRect.yMax; y++)
        {
            for (int x = chunkRect.xMin; x < chunkRect.xMax; x++)
            {
                Vector3Int rootCell = new Vector3Int(x, y, 0);
                if (!IsCellInsideIsland(rootCell, island))
                    continue;

                TryAddDockPlacement(placements, island, rootCell, Vector3Int.up, allowRelaxedFallback);
                TryAddDockPlacement(placements, island, rootCell, Vector3Int.down, allowRelaxedFallback);
                TryAddDockPlacement(placements, island, rootCell, Vector3Int.left, allowRelaxedFallback);
                TryAddDockPlacement(placements, island, rootCell, Vector3Int.right, allowRelaxedFallback);
            }
        }

        return placements;
    }

    void TryAddDockPlacement(List<DockPlacement> placements, IslandParameters island, Vector3Int rootCell, Vector3Int outwardDirection, bool allowRelaxedFallback)
    {
        if (!IsWaterCellForDock(island, rootCell + outwardDirection))
            return;

        Vector3Int perpendicular = new Vector3Int(-outwardDirection.y, outwardDirection.x, 0);
        if (!HasLandAttachment(island, rootCell, outwardDirection, perpendicular, allowRelaxedFallback))
            return;

        int clearLength = CountWaterClearLength(island, rootCell, outwardDirection, perpendicular);
        bool passesStrict = clearLength >= dockLengthCells;
        bool passesRelaxed = allowRelaxedFallback && clearLength >= Mathf.Min(dockRelaxedMinimumClearCells, dockLengthCells);

        if (!passesStrict && !passesRelaxed)
            return;

        DockPlacement placement = new DockPlacement
        {
            rootCell = rootCell,
            outwardDirection = outwardDirection,
            clearLength = clearLength,
            qualityTier = passesStrict ? 0 : 1,
            selectionScore = BuildDockSelectionScore(rootCell, outwardDirection, clearLength, passesStrict ? 0 : 1)
        };

        placements.Add(placement);
    }

    bool HasLandAttachment(IslandParameters island, Vector3Int rootCell, Vector3Int outwardDirection, Vector3Int perpendicular, bool allowRelaxedFallback)
    {
        int halfWidth = dockWidthCells / 2;

        for (int depth = 0; depth <= dockAttachmentDepthCells; depth++)
        {
            Vector3Int rowStart = rootCell - outwardDirection * depth;
            for (int lateral = -halfWidth; lateral <= halfWidth; lateral++)
            {
                Vector3Int cell = rowStart + perpendicular * lateral;
                if (IsCellInsideIsland(cell, island))
                    continue;

                if (allowRelaxedFallback && depth == 0 && lateral == 0)
                    continue;

                return false;
            }
        }

        return true;
    }

    int CountWaterClearLength(IslandParameters island, Vector3Int rootCell, Vector3Int outwardDirection, Vector3Int perpendicular)
    {
        int halfWidth = dockWidthCells / 2;
        int clearCells = 0;

        for (int step = 1; step <= dockLengthCells; step++)
        {
            Vector3Int rowStart = rootCell + outwardDirection * step;
            bool rowClear = true;

            for (int lateral = -halfWidth; lateral <= halfWidth; lateral++)
            {
                Vector3Int cell = rowStart + perpendicular * lateral;
                if (!IsWaterCellForDock(island, cell))
                {
                    rowClear = false;
                    break;
                }
            }

            if (!rowClear)
                break;

            clearCells++;
        }

        return clearCells;
    }

    float BuildDockSelectionScore(Vector3Int rootCell, Vector3Int outwardDirection, int clearLength, int qualityTier)
    {
        float baseScore = Hash01(rootCell.x, rootCell.y, seed + outwardDirection.x * 17 + outwardDirection.y * 31);
        float clearanceScore = clearLength / (float)Mathf.Max(dockLengthCells, 1);
        float qualityPenalty = qualityTier * 2f;
        return baseScore + clearanceScore * 3f - qualityPenalty;
    }

    bool PaintDock(RectInt chunkRect, DockPlacement placement)
    {
        int paintedLength = Mathf.Min(placement.clearLength, dockLengthCells);
        if (paintedLength <= 0)
            return false;

        Vector3Int perpendicular = new Vector3Int(-placement.outwardDirection.y, placement.outwardDirection.x, 0);
        int halfWidth = dockWidthCells / 2;
        Matrix4x4 dockMatrix = GetDockTransformMatrix(placement.outwardDirection);
        bool paintedAny = false;

        for (int step = 1; step <= paintedLength; step++)
        {
            Vector3Int rowStart = placement.rootCell + placement.outwardDirection * step;
            for (int lateral = -halfWidth; lateral <= halfWidth; lateral++)
            {
                Vector3Int cell = rowStart + perpendicular * lateral;
                if (!chunkRect.Contains(new Vector2Int(cell.x, cell.y)))
                    continue;

                dockTilemap.SetTile(cell, dockTile);
                dockTilemap.SetTileFlags(cell, TileFlags.None);
                dockTilemap.SetTransformMatrix(cell, dockMatrix);
                paintedAny = true;
            }
        }

        return paintedAny;
    }

    Matrix4x4 GetDockTransformMatrix(Vector3Int outwardDirection)
    {
        float rotationDegrees = 0f;
        if (outwardDirection == Vector3Int.up)
            rotationDegrees = 180f;
        else if (outwardDirection == Vector3Int.left)
            rotationDegrees = 90f;
        else if (outwardDirection == Vector3Int.right)
            rotationDegrees = 270f;

        return Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, rotationDegrees), Vector3.one);
    }

    bool IsLandCell(Vector3Int cell)
    {
        return islandTilemap.GetTile(cell) != null;
    }

    bool IsCellInsideIsland(Vector3Int cell, IslandParameters island)
    {
        return TryGetIslandShorelineMask(GetIslandLocalPoint(cell, island), island, cell.x, cell.y, out _);
    }

    bool IsWaterCellForDock(IslandParameters island, Vector3Int cell)
    {
        if (IsCellInsideIsland(cell, island))
            return false;

        if (treasureIslandPlacement.isValid && IsCellInsideIsland(cell, ToIslandParameters(treasureIslandPlacement)))
            return false;

        return true;
    }

    float GetMaximumPossibleIslandRadius()
    {
        float baseCeiling = Mathf.Max(maxRadiusTiles, landmarkRadiusCeilingTiles);
        float lobePadding = 1f + Mathf.Max(mediumLobeStrength, landmarkLobeStrength) * 1.5f;
        return baseCeiling * lobePadding;
    }

    int GetLobeCountForIsland(bool isLandmark, float normalizedSize, Vector2Int deterministicKey)
    {
        if (isLandmark)
            return Mathf.RoundToInt(Hash01(deterministicKey.x, deterministicKey.y, seed + 149) * landmarkMaxLobeCount);

        if (normalizedSize < 0.38f || mediumMaxLobeCount <= 0)
            return 0;

        return Mathf.RoundToInt(Hash01(deterministicKey.x, deterministicKey.y, seed + 149) * mediumMaxLobeCount);
    }

    static float GetDirectionalEllipseRadius(Vector2 radii, float angleRadians)
    {
        float cos = Mathf.Cos(angleRadians);
        float sin = Mathf.Sin(angleRadians);
        float denominator =
            (cos * cos) / Mathf.Max(radii.x * radii.x, 0.0001f) +
            (sin * sin) / Mathf.Max(radii.y * radii.y, 0.0001f);
        return 1f / Mathf.Sqrt(Mathf.Max(denominator, 0.0001f));
    }

    float CalculateFootprintRadius(IslandParameters island)
    {
        float footprintRadius = Mathf.Max(island.radii.x, island.radii.y);
        if (island.lobePoints == null || island.lobeRadii == null)
            return CalculateFootprintPadding(island, footprintRadius) + footprintRadius;

        int count = Mathf.Min(island.lobePoints.Length, island.lobeRadii.Length);
        for (int i = 0; i < count; i++)
        {
            footprintRadius = Mathf.Max(
                footprintRadius,
                island.lobePoints[i].magnitude + island.lobeRadii[i] * (1f + island.lobeStrength));
        }

        return footprintRadius + CalculateFootprintPadding(island, footprintRadius);
    }

    float CalculateFootprintPadding(IslandParameters island, float baseFootprintRadius)
    {
        float safeBaseRadius = Mathf.Max(baseFootprintRadius, 0.01f);

        // Painting can extend beyond the raw ellipse/lobe radius via shoreline noise and
        // voronoi perturbation, so chunk-affect tests need a conservative extra margin.
        float edgePadding = edgeNoiseStrength * safeBaseRadius;
        float voronoiPadding = voronoiStrength * Mathf.Max(Mathf.Min(island.radii.x, island.radii.y), 0.01f);
        float lobePadding = island.lobeStrength > 0.0001f ? island.lobeStrength * safeBaseRadius : 0f;

        return edgePadding + voronoiPadding + lobePadding + 1f;
    }

    static float GetLobePerturbation(Vector2 point, Vector2[] lobePoints, float[] lobeRadii, float lobeStrength)
    {
        if (lobeStrength <= 0.0001f || lobePoints == null || lobeRadii == null)
            return 0f;

        float perturbation = 0f;
        int count = Mathf.Min(lobePoints.Length, lobeRadii.Length);
        for (int i = 0; i < count; i++)
        {
            float lobeRadius = Mathf.Max(0.0001f, lobeRadii[i]);
            float distance = Vector2.Distance(point, lobePoints[i]);
            float t = 1f - Mathf.Clamp01(distance / lobeRadius);
            perturbation += t * t * lobeStrength;
        }

        return perturbation;
    }

    bool IsWaterCell(Vector3Int cell)
    {
        if (islandTilemap.GetTile(cell) != null)
            return false;

        if (dockTilemap != null && dockTilemap.GetTile(cell) != null)
            return false;

        return true;
    }

    float GetNearestVoronoiDistance(Vector2 point, Vector2[] featurePoints)
    {
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < featurePoints.Length; i++)
        {
            float distance = Vector2.Distance(point, featurePoints[i]);
            if (distance < nearestDistance)
                nearestDistance = distance;
        }

        return nearestDistance;
    }

    void ClearRect(RectInt rect)
    {
        for (int y = rect.yMin; y < rect.yMax; y++)
        {
            for (int x = rect.xMin; x < rect.xMax; x++)
                islandTilemap.SetTile(new Vector3Int(x, y, 0), null);
        }
    }

    void ClearDockRect(RectInt rect)
    {
        if (dockTilemap == null)
            return;

        for (int y = rect.yMin; y < rect.yMax; y++)
        {
            for (int x = rect.xMin; x < rect.xMax; x++)
                dockTilemap.SetTile(new Vector3Int(x, y, 0), null);
        }
    }

    void ClearGoldRect(RectInt rect)
    {
        if (goldTilemap == null)
            return;

        for (int y = rect.yMin; y < rect.yMax; y++)
        {
            for (int x = rect.xMin; x < rect.xMax; x++)
                goldTilemap.SetTile(new Vector3Int(x, y, 0), null);
        }
    }

    void ClearAllTreasureTiles()
    {
        if (goldTilemap == null)
            return;

        goldTilemap.ClearAllTiles();
    }

    static float Hash01(int x, int y, int localSeed)
    {
        unchecked
        {
            int hash = localSeed;
            hash = (hash * 397) ^ x;
            hash = (hash * 397) ^ y;
            hash ^= hash >> 16;
            hash *= unchecked((int)0x7feb352d);
            hash ^= hash >> 15;
            hash *= unchecked((int)0x846ca68b);
            hash ^= hash >> 16;

            uint unsignedHash = (uint)hash;
            return unsignedHash / (float)uint.MaxValue;
        }
    }

    Vector2Int PackCandidateDeterministicKey(Vector2Int sectorCoord, int slotIndex)
    {
        return new Vector2Int(
            sectorCoord.x,
            sectorCoord.y * DeterministicKeySlotStride + slotIndex);
    }

    float Hash01Candidate(Vector2Int sectorCoord, int slotIndex, int localSeed)
    {
        Vector2Int deterministicKey = PackCandidateDeterministicKey(sectorCoord, slotIndex);
        return Hash01(deterministicKey.x, deterministicKey.y, localSeed);
    }

    long PackSectorSlotCacheKey(Vector2Int sectorCoord, int slotIndex)
    {
        return PackDeterministicIslandKey(PackCandidateDeterministicKey(sectorCoord, slotIndex));
    }

    static long PackDeterministicIslandKey(Vector2Int deterministicKey)
    {
        unchecked
        {
            return ((long)deterministicKey.x << 32) ^ (uint)deterministicKey.y;
        }
    }
}
