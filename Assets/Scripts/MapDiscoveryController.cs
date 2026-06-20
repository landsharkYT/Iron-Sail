using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class MapDiscoveryController : MonoBehaviour
{
    public static MapDiscoveryController ActiveInstance { get; private set; }

    public enum ChartCategory : byte
    {
        Undiscovered = 0,
        Water = 1,
        LowLand = 2,
        MidLand = 3,
        HighLand = 4,
        Border = 5,
        CustomPalette = 6,
        Dock = 7,
        Treasure = 8
    }

    public struct ChartTruthDiagnostic
    {
        public Vector3Int cell;
        public Vector2 worldPosition;
        public ChartCategory chartCategory;
        public int seed;
        public bool hasAcceptedSource;
        public Vector2Int nearestAcceptedSourceKey;
        public Vector2 nearestAcceptedSourceCenter;
        public float nearestAcceptedSourceDistance;
        public bool chunkLoaded;
        public bool chunkDeferred;
        public string reason;

        public override string ToString()
        {
            string sourceText = hasAcceptedSource
                ? $"nearestSource={nearestAcceptedSourceKey} center={nearestAcceptedSourceCenter} distance={nearestAcceptedSourceDistance:0.0}"
                : "nearestSource=<none>";
            return $"cell={cell} world={worldPosition} category={chartCategory} seed={seed} chunkLoaded={chunkLoaded} chunkDeferred={chunkDeferred} {sourceText} reason={reason}";
        }
    }

    sealed class ChartPage
    {
        public readonly byte[] categories;
        public readonly byte[] paletteIndices;
        public int discoveredCount;

        public ChartPage(int tileCount)
        {
            categories = new byte[tileCount];
            paletteIndices = new byte[tileCount];
        }
    }

    sealed class DiscoveredIslandState
    {
        public IslandGenerationController.IslandSourceDescriptor source;
        public readonly HashSet<long> dockCellKeys = new HashSet<long>();
    }

    sealed class CompletedIslandRecord
    {
        public readonly HashSet<long> contributedCellKeys = new HashSet<long>();
    }

    struct PendingIslandStamp
    {
        public Vector2Int deterministicKey;
        public int minX;
        public int maxX;
        public int minY;
        public int maxY;
        public int nextX;
        public int nextY;
        public bool hasPendingChanges;
    }

    [Header("References")]
    [SerializeField] WorldGenerationSettings worldSettings;
    [SerializeField] Transform boatTransform;
    [SerializeField] IslandGenerationController islandGenerationController;
    [SerializeField] Tilemap islandTilemap;
    [SerializeField] Tilemap dockTilemap;
    [SerializeField] Tilemap goldTilemap;
    [SerializeField] Tilemap waterTilemap;
    [SerializeField] Tilemap borderTilemap;
    [SerializeField] WorldBoundryController worldBoundaryController;

    [Header("Chart Storage")]
    [SerializeField][Min(16)] int chartPageSize = 64;
    [SerializeField][Min(1)] int compatibilityPreviewTileStep = 24;
    [SerializeField][Min(64)] int compatibilityTextureSizeLimit = 2048;

    [Header("Discovery")]
    [SerializeField][Min(8f)] float revealRadiusWorld = 120f;
    // Save mask granularity: revealed positions snap to this world-unit grid.
    // Must stay <= revealRadiusWorld so replayed reveals leave no gaps on load.
    [SerializeField][Min(8)] int revealMaskBlockSize = 64;
    [SerializeField][Min(1f)] float revealMovementCadenceWorld = 30f;
    [SerializeField][Min(0.05f)] float stationaryRevealRefreshSeconds = 0.35f;
    [SerializeField][Min(1)] int pendingRetryBatchSize = 2048;
    [SerializeField][Min(1)] int backgroundPendingRetryBatchSize = 256;
    [SerializeField][Min(0.05f)] float pendingBurstDurationSeconds = 0.35f;
    [SerializeField][Min(1)] int maxIslandStampTilesPerFrame = 2048;

    [Header("Performance")]
    [SerializeField] bool enableDiscoveryAudit = false;

    [Header("Map Colors")]
    [SerializeField] Color32 waterMapColor = new Color32(49, 96, 184, 255);
    [SerializeField] Color32 undiscoveredColor = new Color32(63, 67, 76, 255);
    [SerializeField] Color32 outsideWorldMapColor = new Color32(63, 67, 76, 255);
    [SerializeField] Color32 borderMapColor = new Color32(7, 7, 9, 255);
    [SerializeField] Color32 dockMapColor = new Color32(130, 88, 48, 255);
    [SerializeField] Color32 treasureMapColor = new Color32(230, 192, 68, 255);
    [SerializeField] Color32 lowLandMapColor = new Color32(80, 189, 78, 255);
    [SerializeField] Color32 midLandMapColor = new Color32(60, 146, 56, 255);
    [SerializeField] Color32 highLandMapColor = new Color32(35, 87, 28, 255);
    [SerializeField] Color32 fallbackLandMapColor = new Color32(75, 160, 69, 255);

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] int debugTextureWidth;
    [SerializeField] int debugTextureHeight;
    [SerializeField] int debugTextureVersion;
    [SerializeField] Vector2 debugLastRevealWorldPosition;
    [SerializeField] int debugRecordedTileCount;
    [SerializeField] int debugPendingTileCount;
    [SerializeField] int debugAllocatedPageCount;
    [SerializeField] Vector2 debugBoatWorldPosition;
    [SerializeField] Vector3Int debugBoatTileCell;
    [SerializeField] ChartCategory debugBoatChartCategory = ChartCategory.Undiscovered;
    [SerializeField] int debugLandTileCountNearBoat;
    [SerializeField] float debugNearestLandTileDistance = -1f;
    [SerializeField] Vector3Int debugNearestLandTileCell;
    [SerializeField] int debugLastRevealTilesScanned;
    [SerializeField] int debugLastRevealTilesRecorded;
    [SerializeField] int debugLastPendingTilesProcessed;
    [SerializeField] int debugLastPendingTilesRecorded;
    [SerializeField] int debugCurrentPendingRetryBudget;
    [SerializeField] float debugLastRevealDurationMs;
    [SerializeField] float debugLastPendingDurationMs;
    [SerializeField] float debugLastAuditDurationMs;
    [SerializeField] float debugLastRenderViewportDurationMs;

    readonly Dictionary<Vector2Int, ChartPage> chartPages = new Dictionary<Vector2Int, ChartPage>();
    // Save mask: coarse blocks the player has revealed. Persisted and replayed.
    readonly HashSet<Vector2Int> revealedBlocks = new HashSet<Vector2Int>();
    readonly List<Vector2Int> pendingMaskReplayBlocks = new List<Vector2Int>();
    int pendingMaskReplayBlockSize;
    int pendingMaskReplayIndex;
    // True while replaying a saved mask. Lets reveal stamp islands deterministically
    // from the seed even when their chunk isn't streamed yet (Phase 2).
    bool maskReplayActive;
    // Set while a mask restore is in flight; drives the final treasure-marker stamp
    // once block replay and island stamping have fully drained (Phase 2.1).
    bool maskReconstructionPending;
    readonly List<Vector3Int> pendingTiles = new List<Vector3Int>();
    readonly HashSet<long> pendingTileKeys = new HashSet<long>();
    readonly List<IslandGenerationController.IslandSourceDescriptor> nearbyAcceptedIslands = new List<IslandGenerationController.IslandSourceDescriptor>();
    readonly List<IslandGenerationController.IslandSourceDescriptor> truthAcceptedIslands = new List<IslandGenerationController.IslandSourceDescriptor>();
    readonly List<Vector3Int> truthDockCells = new List<Vector3Int>();
    readonly HashSet<long> revealedIslandKeys = new HashSet<long>();
    readonly HashSet<long> completedIslandKeys = new HashSet<long>();
    readonly Queue<PendingIslandStamp> pendingIslandStamps = new Queue<PendingIslandStamp>();
    readonly HashSet<long> pendingIslandStampKeys = new HashSet<long>();
    readonly Dictionary<long, DiscoveredIslandState> inProgressIslandStates = new Dictionary<long, DiscoveredIslandState>();
    readonly Dictionary<long, CompletedIslandRecord> completedIslandRecords = new Dictionary<long, CompletedIslandRecord>();
    readonly List<Vector3Int> dockCellsScratch = new List<Vector3Int>();
    readonly List<Color32> customPalette = new List<Color32>();
    readonly Dictionary<uint, byte> customPaletteLookup = new Dictionary<uint, byte>();

    Texture2D compatibilityPreviewTexture;
    Color32[] compatibilityPreviewPixels;
    bool compatibilityPreviewDirty = true;
    bool referencesValid;
    bool hasStampedInitialReveal;
    int textureVersion;
    int worldMinTileCoord;
    int worldMaxTileCoordExclusive;
    float worldOuterRadiusTiles;
    Vector2 lastRevealWorldPosition;
    float lastRevealTime;
    int recordedTileCount;
    float pendingBurstUntilTime;
    IslandGenerationController subscribedIslandGenerationController;
    bool hasActiveIslandStamp;
    PendingIslandStamp activeIslandStamp;

    public Texture2D DiscoveredTexture
    {
        get
        {
            EnsureCompatibilityPreviewUpToDate();
            return compatibilityPreviewTexture;
        }
    }

    public int TextureWidth
    {
        get
        {
            EnsureCompatibilityPreviewUpToDate();
            return compatibilityPreviewTexture != null ? compatibilityPreviewTexture.width : 0;
        }
    }

    public int TextureHeight
    {
        get
        {
            EnsureCompatibilityPreviewUpToDate();
            return compatibilityPreviewTexture != null ? compatibilityPreviewTexture.height : 0;
        }
    }

    public int TextureVersion => textureVersion;
    public Color32 UndiscoveredColor => undiscoveredColor;
    public float RevealRadiusWorld => revealRadiusWorld;
    public float WorldOuterRadiusTiles => worldOuterRadiusTiles;

    void OnEnable()
    {
        ActiveInstance = this;
    }

    void OnDisable()
    {
        UnsubscribeFromIslandGeneration();
        if (ActiveInstance == this)
            ActiveInstance = null;
    }

    IEnumerator Start()
    {
        referencesValid = ResolveReferences();
        if (!referencesValid)
            yield break;

        InitializeChartStorage();

        // Let the streamed world populate for one frame before the chart records history.
        yield return null;

        ForceRevealAtBoat();
    }

    void Update()
    {
        if (!referencesValid || boatTransform == null)
            return;

        ProcessPendingMaskReplay();
        HandleRevealTick();
        ProcessIslandStampQueue();
        ProcessPendingTiles();

        // Treasure markers must be stamped after the island stamps that would
        // otherwise overwrite them as land — i.e. once reconstruction has drained.
        if (maskReconstructionPending
            && pendingMaskReplayBlocks.Count == 0
            && pendingIslandStamps.Count == 0
            && !hasActiveIslandStamp)
        {
            StampDiscoveredTreasureCells();
            maskReconstructionPending = false;
        }
    }

    bool ResolveReferences()
    {
        if (islandGenerationController == null)
            islandGenerationController = FindAnyObjectByType<IslandGenerationController>();

        if (worldSettings == null && islandGenerationController != null)
            worldSettings = islandGenerationController.WorldSettings;

        if (boatTransform == null && islandGenerationController != null)
            boatTransform = islandGenerationController.BoatTransform;

        if (boatTransform == null)
        {
            BoatController boatController = FindAnyObjectByType<BoatController>();
            if (boatController != null)
                boatTransform = boatController.transform;
        }

        if (islandTilemap == null && islandGenerationController != null)
            islandTilemap = islandGenerationController.IslandTilemap;

        if (dockTilemap == null && islandGenerationController != null)
            dockTilemap = islandGenerationController.DockTilemap;

        if (goldTilemap == null && islandGenerationController != null)
            goldTilemap = islandGenerationController.GoldTilemap;

        if (waterTilemap == null)
        {
            InfiniteWaterTileMap waterController = InfiniteWaterTileMap.ActiveInstance;
            if (waterController == null)
                waterController = FindAnyObjectByType<InfiniteWaterTileMap>();
            if (waterController != null)
                waterTilemap = waterController.WaterTilemap;
        }

        if (worldBoundaryController == null)
            worldBoundaryController = FindAnyObjectByType<WorldBoundryController>();

        if (borderTilemap == null && worldBoundaryController != null)
            borderTilemap = worldBoundaryController.BorderTilemap;

        if (worldSettings == null)
        {
            Debug.LogWarning("[MapDiscoveryController] Missing WorldGenerationSettings reference.", this);
            return false;
        }

        if (boatTransform == null)
        {
            Debug.LogWarning("[MapDiscoveryController] Missing boat Transform reference.", this);
            return false;
        }

        if (islandTilemap == null)
        {
            Debug.LogWarning("[MapDiscoveryController] Missing island Tilemap reference.", this);
            return false;
        }

        if (chartPageSize <= 0)
            chartPageSize = 64;

        worldOuterRadiusTiles = worldSettings.WallOuterRadiusTiles;
        int maxAbsTile = Mathf.CeilToInt(worldOuterRadiusTiles);
        worldMinTileCoord = -maxAbsTile;
        worldMaxTileCoordExclusive = maxAbsTile;
        SubscribeToIslandGeneration();
        return true;
    }

    void SubscribeToIslandGeneration()
    {
        if (subscribedIslandGenerationController == islandGenerationController)
            return;

        UnsubscribeFromIslandGeneration();
        if (islandGenerationController == null)
            return;

        islandGenerationController.ChunkGenerated += HandleIslandChunkGenerated;
        islandGenerationController.AcceptedIslandSourceCacheInvalidated += HandleAcceptedIslandSourceCacheInvalidated;
        islandGenerationController.TreasurePlacementChanged += HandleTreasurePlacementChanged;
        subscribedIslandGenerationController = islandGenerationController;
    }

    void UnsubscribeFromIslandGeneration()
    {
        if (subscribedIslandGenerationController == null)
            return;

        subscribedIslandGenerationController.ChunkGenerated -= HandleIslandChunkGenerated;
        subscribedIslandGenerationController.AcceptedIslandSourceCacheInvalidated -= HandleAcceptedIslandSourceCacheInvalidated;
        subscribedIslandGenerationController.TreasurePlacementChanged -= HandleTreasurePlacementChanged;
        subscribedIslandGenerationController = null;
    }

    void InitializeChartStorage()
    {
        chartPages.Clear();
        pendingTiles.Clear();
        pendingTileKeys.Clear();
        revealedIslandKeys.Clear();
        completedIslandKeys.Clear();
        completedIslandRecords.Clear();
        pendingIslandStamps.Clear();
        pendingIslandStampKeys.Clear();
        inProgressIslandStates.Clear();
        customPalette.Clear();
        customPaletteLookup.Clear();
        customPalette.Add(new Color32(0, 0, 0, 0));

        textureVersion = 1;
        recordedTileCount = 0;
        hasStampedInitialReveal = false;
        compatibilityPreviewDirty = true;
        hasActiveIslandStamp = false;
        activeIslandStamp = default;

        debugTextureVersion = textureVersion;
        debugRecordedTileCount = recordedTileCount;
        debugPendingTileCount = 0;
        debugAllocatedPageCount = 0;
    }

    void HandleRevealTick()
    {
        Vector2 currentWorld = boatTransform.position;
        if (!hasStampedInitialReveal)
        {
            RevealAtWorldPosition(currentWorld);
            return;
        }

        bool movedEnough = (currentWorld - lastRevealWorldPosition).sqrMagnitude >= revealMovementCadenceWorld * revealMovementCadenceWorld;
        bool refreshDue = pendingTiles.Count > 0
            && Time.unscaledTime - lastRevealTime >= stationaryRevealRefreshSeconds;
        if (!movedEnough && !refreshDue)
            return;

        RevealAtWorldPosition(currentWorld);
    }

    void ForceRevealAtBoat()
    {
        if (boatTransform == null)
            return;

        RevealAtWorldPosition(boatTransform.position);
    }

    void RevealAtWorldPosition(Vector2 worldPosition)
    {
        if (!referencesValid)
            return;

        double startTime = Time.realtimeSinceStartupAsDouble;
        Vector3Int centerCell = islandTilemap.WorldToCell(worldPosition);
        int cellRadius = Mathf.CeilToInt(revealRadiusWorld) + 1;
        float revealRadiusSqr = revealRadiusWorld * revealRadiusWorld;
        bool anyRecorded = false;
        int scannedTileCount = 0;
        int recordedTileCountThisPass = 0;

        RevealIntersectingIslands(worldPosition);

        for (int y = centerCell.y - cellRadius; y <= centerCell.y + cellRadius; y++)
        {
            for (int x = centerCell.x - cellRadius; x <= centerCell.x + cellRadius; x++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                if (!IsInsideChartBounds(cell))
                    continue;

                Vector2 cellCenterWorld = islandTilemap.GetCellCenterWorld(cell);
                if (((Vector2)cellCenterWorld - worldPosition).sqrMagnitude > revealRadiusSqr)
                    continue;

                scannedTileCount++;

                if (IsTileRecorded(cell))
                    continue;

                if (!IsTileReadyForRecording(cell))
                {
                    QueuePendingTile(cell);
                    continue;
                }

                if (RecordTileAtCell(cell))
                {
                    anyRecorded = true;
                    recordedTileCountThisPass++;
                }
            }
        }

        if (anyRecorded)
            CommitChartChanges();

        revealedBlocks.Add(WorldToRevealBlock(worldPosition));

        hasStampedInitialReveal = true;
        lastRevealWorldPosition = worldPosition;
        lastRevealTime = Time.unscaledTime;
        pendingBurstUntilTime = Time.unscaledTime + pendingBurstDurationSeconds;
        debugLastRevealWorldPosition = worldPosition;
        debugLastRevealTilesScanned = scannedTileCount;
        debugLastRevealTilesRecorded = recordedTileCountThisPass;
        debugLastRevealDurationMs = (float)((Time.realtimeSinceStartupAsDouble - startTime) * 1000.0);

        if (enableDiscoveryAudit)
        {
            double auditStartTime = Time.realtimeSinceStartupAsDouble;
            AuditBoatDiscoveryState(worldPosition);
            debugLastAuditDurationMs = (float)((Time.realtimeSinceStartupAsDouble - auditStartTime) * 1000.0);
        }
        else
        {
            debugBoatWorldPosition = worldPosition;
            debugBoatTileCell = islandTilemap.WorldToCell(worldPosition);
            debugBoatChartCategory = GetChartCategoryAtCell(debugBoatTileCell);
            debugLandTileCountNearBoat = 0;
            debugNearestLandTileDistance = -1f;
            debugNearestLandTileCell = default;
            debugLastAuditDurationMs = 0f;
        }
    }

    // --- Save mask (Family E reconstruction) --------------------------------

    public int RevealMaskBlockSize => Mathf.Max(8, revealMaskBlockSize);
    public IReadOnlyCollection<Vector2Int> RevealedBlocks => revealedBlocks;

    Vector2Int WorldToRevealBlock(Vector2 worldPosition)
    {
        int size = Mathf.Max(8, revealMaskBlockSize);
        return new Vector2Int(Mathf.FloorToInt(worldPosition.x / size), Mathf.FloorToInt(worldPosition.y / size));
    }

    // Queues a saved discovery mask for replay. The reveals run a few per frame in
    // Update so the chart repaints from the seed without a single-frame spike.
    public void RestoreRevealedRegions(int blockSize, IList<Vector2Int> blocks)
    {
        if (blocks == null || blocks.Count == 0)
            return;

        pendingMaskReplayBlockSize = Mathf.Max(8, blockSize);
        pendingMaskReplayBlocks.Clear();
        pendingMaskReplayBlocks.AddRange(blocks);
        pendingMaskReplayIndex = 0;
        maskReconstructionPending = true;
    }

    // After reconstruction drains, force the treasure-target cells to Treasure if
    // the player had revealed that block (the island stamp leaves them as land).
    void StampDiscoveredTreasureCells()
    {
        if (islandGenerationController == null
            || !islandGenerationController.TryGetTreasureTargetCells(out Vector3Int cellA, out Vector3Int cellB))
            return;

        bool anyStamped = TryStampTreasureCell(cellA);
        if (cellB != cellA)
            anyStamped |= TryStampTreasureCell(cellB);

        if (anyStamped)
            CommitChartChanges();
    }

    bool TryStampTreasureCell(Vector3Int cell)
    {
        if (!IsInsideChartBounds(cell) || !IsRevealedBlockForCell(cell))
            return false;

        return UpsertRecordedTile(cell, ChartCategory.Treasure, 0);
    }

    bool IsRevealedBlockForCell(Vector3Int cell)
    {
        int size = Mathf.Max(8, revealMaskBlockSize);
        Vector2Int block = new Vector2Int(
            Mathf.FloorToInt((cell.x + 0.5f) / size),
            Mathf.FloorToInt((cell.y + 0.5f) / size));
        return revealedBlocks.Contains(block);
    }

    void ProcessPendingMaskReplay()
    {
        if (pendingMaskReplayIndex >= pendingMaskReplayBlocks.Count)
            return;

        int size = Mathf.Max(8, pendingMaskReplayBlockSize);
        const int blocksPerFrame = 8;
        int processed = 0;

        maskReplayActive = true;
        while (pendingMaskReplayIndex < pendingMaskReplayBlocks.Count && processed < blocksPerFrame)
        {
            Vector2Int block = pendingMaskReplayBlocks[pendingMaskReplayIndex++];
            Vector2 center = new Vector2((block.x + 0.5f) * size, (block.y + 0.5f) * size);
            RevealAtWorldPosition(center);
            processed++;
        }
        maskReplayActive = false;

        if (pendingMaskReplayIndex >= pendingMaskReplayBlocks.Count)
            pendingMaskReplayBlocks.Clear();
    }

    void ProcessPendingTiles()
    {
        if (pendingTiles.Count == 0)
        {
            debugPendingTileCount = 0;
            debugCurrentPendingRetryBudget = 0;
            debugLastPendingTilesProcessed = 0;
            debugLastPendingTilesRecorded = 0;
            debugLastPendingDurationMs = 0f;
            return;
        }

        double startTime = Time.realtimeSinceStartupAsDouble;
        bool anyRecorded = false;
        int processed = 0;
        int recordedThisPass = 0;
        int retryBudget = Time.unscaledTime <= pendingBurstUntilTime
            ? pendingRetryBatchSize
            : Mathf.Min(backgroundPendingRetryBatchSize, pendingRetryBatchSize);
        debugCurrentPendingRetryBudget = retryBudget;

        for (int i = pendingTiles.Count - 1; i >= 0 && processed < retryBudget; i--, processed++)
        {
            Vector3Int cell = pendingTiles[i];
            if (IsTileRecorded(cell))
            {
                RemovePendingAt(i);
                continue;
            }

            if (!IsTileReadyForRecording(cell))
                continue;

            if (RecordTileAtCell(cell))
            {
                anyRecorded = true;
                recordedThisPass++;
            }

            RemovePendingAt(i);
        }

        if (anyRecorded)
            CommitChartChanges();

        debugPendingTileCount = pendingTiles.Count;
        debugLastPendingTilesProcessed = processed;
        debugLastPendingTilesRecorded = recordedThisPass;
        debugLastPendingDurationMs = (float)((Time.realtimeSinceStartupAsDouble - startTime) * 1000.0);
    }

    void RemovePendingAt(int listIndex)
    {
        Vector3Int cell = pendingTiles[listIndex];
        pendingTileKeys.Remove(PackTileKey(cell.x, cell.y));
        pendingTiles.RemoveAt(listIndex);
    }

    void RevealIntersectingIslands(Vector2 worldPosition)
    {
        if (islandGenerationController == null)
            return;

        islandGenerationController.CollectAcceptedIslandSourcesNearWorldPosition(worldPosition, revealRadiusWorld, nearbyAcceptedIslands);
        for (int i = 0; i < nearbyAcceptedIslands.Count; i++)
        {
            IslandGenerationController.IslandSourceDescriptor source = nearbyAcceptedIslands[i];
            long islandKey = PackTileKey(source.deterministicKey.x, source.deterministicKey.y);
            if (completedIslandKeys.Contains(islandKey) || inProgressIslandStates.ContainsKey(islandKey))
                continue;

            // During live play, wait until the whole island is allowed to render
            // atomically. "Any affected chunk is loaded" is not enough here:
            // a multi-chunk island can otherwise appear on the chart while the
            // world renderer is still withholding it for protection/atomicity.
            // During save-mask replay, stamp straight from the deterministic
            // source so distant islands reconstruct without needing streaming.
            if (!maskReplayActive
                && !islandGenerationController.IsAcceptedIslandCurrentlyRenderable(
                    worldPosition,
                    revealRadiusWorld,
                    source.deterministicKey))
            {
                continue;
            }

            revealedIslandKeys.Add(islandKey);
            inProgressIslandStates[islandKey] = BuildDiscoveredIslandState(source);
            QueueIslandStamp(source, islandKey);
        }
    }

    DiscoveredIslandState BuildDiscoveredIslandState(IslandGenerationController.IslandSourceDescriptor source)
    {
        DiscoveredIslandState state = new DiscoveredIslandState
        {
            source = source
        };

        if (islandGenerationController != null
            && islandGenerationController.TryGetAcceptedIslandDockCells(source.deterministicKey, dockCellsScratch))
        {
            for (int i = 0; i < dockCellsScratch.Count; i++)
                state.dockCellKeys.Add(PackTileKey(dockCellsScratch[i].x, dockCellsScratch[i].y));
        }

        return state;
    }

    void QueueIslandStamp(IslandGenerationController.IslandSourceDescriptor source, long islandKey)
    {
        if (pendingIslandStampKeys.Contains(islandKey))
            return;

        int radius = Mathf.CeilToInt(source.maxRadius) + 1;
        int minX = Mathf.FloorToInt(source.center.x - radius);
        int minY = Mathf.FloorToInt(source.center.y - radius);
        PendingIslandStamp work = new PendingIslandStamp
        {
            deterministicKey = source.deterministicKey,
            minX = minX,
            maxX = Mathf.FloorToInt(source.center.x + radius),
            minY = minY,
            maxY = Mathf.FloorToInt(source.center.y + radius),
            nextX = minX,
            nextY = minY,
            hasPendingChanges = false
        };

        pendingIslandStampKeys.Add(islandKey);
        pendingIslandStamps.Enqueue(work);
    }

    bool TryResolveInProgressIslandOverlay(Vector3Int cell, out ChartCategory category, out byte paletteIndex)
    {
        category = ChartCategory.Undiscovered;
        paletteIndex = 0;

        if (islandGenerationController == null || inProgressIslandStates.Count == 0)
            return false;

        foreach (KeyValuePair<long, DiscoveredIslandState> pair in inProgressIslandStates)
        {
            IslandGenerationController.IslandSourceDescriptor source = pair.Value.source;
            int radius = Mathf.CeilToInt(source.maxRadius) + 1;
            if (cell.x < Mathf.FloorToInt(source.center.x - radius)
                || cell.x > Mathf.FloorToInt(source.center.x + radius)
                || cell.y < Mathf.FloorToInt(source.center.y - radius)
                || cell.y > Mathf.FloorToInt(source.center.y + radius))
            {
                continue;
            }

            if (!islandGenerationController.TryResolveAcceptedIslandTile(source.deterministicKey, cell, out TileBase tile))
                continue;

            return TryResolveIslandChartEntryFromTile(tile, out category, out paletteIndex);
        }

        return false;
    }

    bool IsPredictiveDockCell(Vector3Int cell)
    {
        if (inProgressIslandStates.Count == 0)
            return false;

        long cellKey = PackTileKey(cell.x, cell.y);
        foreach (KeyValuePair<long, DiscoveredIslandState> pair in inProgressIslandStates)
        {
            if (pair.Value.dockCellKeys.Contains(cellKey))
                return true;
        }

        return false;
    }

    void ProcessIslandStampQueue()
    {
        if (islandGenerationController == null)
            return;

        int tileBudget = Mathf.Max(1, maxIslandStampTilesPerFrame);
        bool anyRecorded = false;

        while (tileBudget > 0)
        {
            if (!hasActiveIslandStamp)
            {
                if (pendingIslandStamps.Count == 0)
                    break;

                activeIslandStamp = pendingIslandStamps.Dequeue();
                hasActiveIslandStamp = true;
            }

            while (tileBudget > 0 && activeIslandStamp.nextY <= activeIslandStamp.maxY)
            {
                Vector3Int cell = new Vector3Int(activeIslandStamp.nextX, activeIslandStamp.nextY, 0);
                long activeIslandKey = PackTileKey(activeIslandStamp.deterministicKey.x, activeIslandStamp.deterministicKey.y);
                CompletedIslandRecord completedRecord = GetOrCreateCompletedIslandRecord(activeIslandKey);
                if (IsInsideChartBounds(cell)
                    && islandGenerationController.TryResolveAcceptedIslandTile(activeIslandStamp.deterministicKey, cell, out TileBase tile)
                    && TryResolveIslandChartEntryFromTile(tile, out ChartCategory category, out byte paletteIndex)
                    && UpsertRecordedTile(cell, category, paletteIndex))
                {
                    activeIslandStamp.hasPendingChanges = true;
                    completedRecord.contributedCellKeys.Add(PackTileKey(cell.x, cell.y));
                }

                tileBudget--;
                activeIslandStamp.nextX++;
                if (activeIslandStamp.nextX > activeIslandStamp.maxX)
                {
                    activeIslandStamp.nextX = activeIslandStamp.minX;
                    activeIslandStamp.nextY++;
                }
            }

            if (activeIslandStamp.nextY > activeIslandStamp.maxY)
            {
                long islandKey = PackTileKey(activeIslandStamp.deterministicKey.x, activeIslandStamp.deterministicKey.y);
                if (activeIslandStamp.hasPendingChanges)
                    anyRecorded = true;

                completedIslandKeys.Add(islandKey);
                CompletedIslandRecord completedRecord = GetOrCreateCompletedIslandRecord(islandKey);
                if (inProgressIslandStates.TryGetValue(islandKey, out DiscoveredIslandState completedState))
                {
                    foreach (long dockCellKey in completedState.dockCellKeys)
                    {
                        UnpackTileKey(dockCellKey, out int x, out int y);
                        if (UpsertRecordedTile(new Vector3Int(x, y, 0), ChartCategory.Dock, 0))
                        {
                            anyRecorded = true;
                            completedRecord.contributedCellKeys.Add(dockCellKey);
                        }
                    }
                }

                inProgressIslandStates.Remove(islandKey);
                pendingIslandStampKeys.Remove(islandKey);
                activeIslandStamp = default;
                hasActiveIslandStamp = false;
                continue;
            }

            break;
        }

        if (anyRecorded)
            CommitChartChanges();
    }

    void HandleAcceptedIslandSourceCacheInvalidated()
    {
        bool anyCompletedIslandRemoved = RevalidateCompletedIslands();
        List<KeyValuePair<long, DiscoveredIslandState>> incompleteIslands =
            new List<KeyValuePair<long, DiscoveredIslandState>>(inProgressIslandStates);

        pendingIslandStamps.Clear();
        pendingIslandStampKeys.Clear();
        hasActiveIslandStamp = false;
        activeIslandStamp = default;

        for (int i = 0; i < incompleteIslands.Count; i++)
        {
            long islandKey = incompleteIslands[i].Key;
            IslandGenerationController.IslandSourceDescriptor source = incompleteIslands[i].Value.source;
            if (islandGenerationController != null
                && islandGenerationController.TryGetAcceptedIslandSourceDescriptor(source.deterministicKey, out IslandGenerationController.IslandSourceDescriptor refreshedSource))
            {
                source = refreshedSource;
            }

            inProgressIslandStates[islandKey] = BuildDiscoveredIslandState(source);
            QueueIslandStamp(source, islandKey);
        }

        if (anyCompletedIslandRemoved)
            CommitChartChanges();
    }

    void HandleTreasurePlacementChanged()
    {
        bool anyRemoved = ClearRecordedTreasureCells();
        bool anyRecorded = RecordCurrentTreasureTargetCells();

        if (anyRemoved || anyRecorded)
            CommitChartChanges();
    }

    bool RevalidateCompletedIslands()
    {
        if (islandGenerationController == null || completedIslandKeys.Count == 0)
            return false;

        List<long> staleIslandKeys = new List<long>();
        foreach (long islandKey in completedIslandKeys)
        {
            Vector2Int deterministicKey = new Vector2Int((int)(islandKey >> 32), (int)(islandKey & 0xffffffff));
            if (!islandGenerationController.TryGetAcceptedIslandSourceDescriptor(deterministicKey, out _))
                staleIslandKeys.Add(islandKey);
        }

        bool anyRemoved = false;
        for (int i = 0; i < staleIslandKeys.Count; i++)
        {
            long islandKey = staleIslandKeys[i];
            if (EraseCompletedIslandRecord(islandKey))
                anyRemoved = true;

            completedIslandKeys.Remove(islandKey);
            revealedIslandKeys.Remove(islandKey);
            completedIslandRecords.Remove(islandKey);
        }

        return anyRemoved;
    }

    bool EraseCompletedIslandRecord(long islandKey)
    {
        if (!completedIslandRecords.TryGetValue(islandKey, out CompletedIslandRecord record))
            return false;

        bool anyRemoved = false;
        foreach (long cellKey in record.contributedCellKeys)
        {
            UnpackTileKey(cellKey, out int x, out int y);
            if (ClearRecordedTile(new Vector3Int(x, y, 0)))
                anyRemoved = true;
        }

        return anyRemoved;
    }

    bool ClearRecordedTreasureCells()
    {
        if (chartPages.Count == 0)
            return false;

        List<Vector2Int> emptyPages = null;
        bool anyRemoved = false;
        List<Vector2Int> pageCoords = new List<Vector2Int>(chartPages.Keys);
        for (int i = 0; i < pageCoords.Count; i++)
        {
            Vector2Int pageCoord = pageCoords[i];
            if (!chartPages.TryGetValue(pageCoord, out ChartPage page))
                continue;

            for (int localIndex = 0; localIndex < page.categories.Length; localIndex++)
            {
                if (page.categories[localIndex] != (byte)ChartCategory.Treasure)
                    continue;

                page.categories[localIndex] = (byte)ChartCategory.Undiscovered;
                page.paletteIndices[localIndex] = 0;
                page.discoveredCount = Mathf.Max(0, page.discoveredCount - 1);
                recordedTileCount = Mathf.Max(0, recordedTileCount - 1);
                anyRemoved = true;
            }

            if (page.discoveredCount != 0)
                continue;

            emptyPages ??= new List<Vector2Int>();
            emptyPages.Add(pageCoord);
        }

        if (emptyPages != null)
        {
            for (int i = 0; i < emptyPages.Count; i++)
                chartPages.Remove(emptyPages[i]);

            debugAllocatedPageCount = chartPages.Count;
        }

        return anyRemoved;
    }

    bool RecordCurrentTreasureTargetCells()
    {
        if (islandGenerationController == null || !islandGenerationController.TryGetTreasureTargetCells(out Vector3Int cellA, out Vector3Int cellB))
            return false;

        bool anyRecorded = false;
        if (IsInsideChartBounds(cellA) && IsTileReadyForRecording(cellA) && RecordTileAtCell(cellA))
            anyRecorded = true;

        if (cellB != cellA && IsInsideChartBounds(cellB) && IsTileReadyForRecording(cellB) && RecordTileAtCell(cellB))
            anyRecorded = true;

        return anyRecorded;
    }

    bool RecordTileAtCell(Vector3Int cell)
    {
        if (TryResolveRecordedChartEntry(cell, out ChartCategory category, out byte paletteIndex))
            return SetRecordedTile(cell, category, paletteIndex);

        return false;
    }

    bool UpsertRecordedTile(Vector3Int cell, ChartCategory category, byte paletteIndex)
    {
        if (SetRecordedTile(cell, category, paletteIndex))
            return true;

        return UpdateRecordedTile(cell, category, paletteIndex);
    }

    CompletedIslandRecord GetOrCreateCompletedIslandRecord(long islandKey)
    {
        if (completedIslandRecords.TryGetValue(islandKey, out CompletedIslandRecord existing))
            return existing;

        CompletedIslandRecord created = new CompletedIslandRecord();
        completedIslandRecords[islandKey] = created;
        return created;
    }

    bool TryResolveIslandChartEntryFromTile(TileBase tile, out ChartCategory category, out byte paletteIndex)
    {
        paletteIndex = 0;
        category = ChartCategory.LowLand;

        if (tile == islandGenerationController.LowElevationTile)
        {
            category = ChartCategory.LowLand;
            return true;
        }

        if (tile == islandGenerationController.MidElevationTile)
        {
            category = ChartCategory.MidLand;
            return true;
        }

        if (tile == islandGenerationController.HighElevationTile)
        {
            category = ChartCategory.HighLand;
            return true;
        }

        category = ChartCategory.CustomPalette;
        paletteIndex = GetOrCreatePaletteIndex(fallbackLandMapColor);
        return true;
    }

    bool TryResolveRecordedChartEntry(Vector3Int cell, out ChartCategory category, out byte paletteIndex)
    {
        paletteIndex = 0;

        if (IsBorderTile(cell))
        {
            category = ChartCategory.Border;
            return true;
        }

        if (dockTilemap != null && dockTilemap.GetTile(cell) != null)
        {
            category = ChartCategory.Dock;
            return true;
        }

        if (goldTilemap != null && goldTilemap.GetTile(cell) != null)
        {
            category = ChartCategory.Treasure;
            return true;
        }

        TileBase tile = islandTilemap != null ? islandTilemap.GetTile(cell) : null;
        if (tile != null)
        {
            if (TryResolveIslandChartEntryFromTile(tile, out category, out paletteIndex)
                && category != ChartCategory.CustomPalette)
                return true;

            if (MapColorResolver.TryResolveIslandTileColorAtCell(
                    islandTilemap,
                    cell,
                    islandGenerationController.LowElevationTile,
                    islandGenerationController.MidElevationTile,
                    islandGenerationController.HighElevationTile,
                    lowLandMapColor,
                    midLandMapColor,
                    highLandMapColor,
                    fallbackLandMapColor,
                    out Color32 customLandColor))
            {
                category = ChartCategory.CustomPalette;
                paletteIndex = GetOrCreatePaletteIndex(customLandColor);
                return true;
            }

            category = ChartCategory.CustomPalette;
            paletteIndex = GetOrCreatePaletteIndex(fallbackLandMapColor);
            return true;
        }

        category = ChartCategory.Water;
        return true;
    }

    bool SetRecordedTile(Vector3Int cell, ChartCategory category, byte paletteIndex)
    {
        GetPageAndLocalIndex(cell, out Vector2Int pageCoord, out int localIndex);
        ChartPage page = GetOrCreatePage(pageCoord);
        if (page.categories[localIndex] != (byte)ChartCategory.Undiscovered)
            return false;

        page.categories[localIndex] = (byte)category;
        page.paletteIndices[localIndex] = paletteIndex;
        page.discoveredCount++;
        recordedTileCount++;
        return true;
    }

    bool ClearRecordedTile(Vector3Int cell)
    {
        GetPageAndLocalIndex(cell, out Vector2Int pageCoord, out int localIndex);
        if (!chartPages.TryGetValue(pageCoord, out ChartPage page))
            return false;

        if (page.categories[localIndex] == (byte)ChartCategory.Undiscovered)
            return false;

        page.categories[localIndex] = (byte)ChartCategory.Undiscovered;
        page.paletteIndices[localIndex] = 0;
        page.discoveredCount = Mathf.Max(0, page.discoveredCount - 1);
        recordedTileCount = Mathf.Max(0, recordedTileCount - 1);
        if (page.discoveredCount == 0)
            chartPages.Remove(pageCoord);

        debugAllocatedPageCount = chartPages.Count;
        return true;
    }

    bool UpdateRecordedTile(Vector3Int cell, ChartCategory category, byte paletteIndex)
    {
        if (!TryGetPage(cell, out ChartPage page, out int localIndex))
            return false;

        if (page.categories[localIndex] == (byte)ChartCategory.Undiscovered)
            return false;

        if (page.categories[localIndex] == (byte)category && page.paletteIndices[localIndex] == paletteIndex)
            return false;

        page.categories[localIndex] = (byte)category;
        page.paletteIndices[localIndex] = paletteIndex;
        return true;
    }

    bool IsTileRecorded(Vector3Int cell)
    {
        if (!TryGetPage(cell, out ChartPage page, out int localIndex))
            return false;

        return page.categories[localIndex] != (byte)ChartCategory.Undiscovered;
    }

    bool TryGetRecordedTile(Vector3Int cell, out ChartCategory category, out byte paletteIndex)
    {
        category = ChartCategory.Undiscovered;
        paletteIndex = 0;

        if (IsBorderTile(cell))
        {
            category = ChartCategory.Border;
            return true;
        }

        if (!TryGetPage(cell, out ChartPage page, out int localIndex))
            return false;

        category = (ChartCategory)page.categories[localIndex];
        if (category == ChartCategory.Undiscovered)
            return false;

        paletteIndex = page.paletteIndices[localIndex];
        return true;
    }

    bool IsTileReadyForRecording(Vector3Int cell)
    {
        if (!IsInsideChartBounds(cell))
            return false;

        bool isBorderTile = IsBorderTile(cell);
        TileBase islandTile = islandTilemap != null ? islandTilemap.GetTile(cell) : null;
        bool isIslandTile = islandTile != null;

        if (IsPotentialBoundaryTile(cell) && worldBoundaryController != null && !worldBoundaryController.IsChunkLoadedForCell(cell))
            return false;

        if (isIslandTile)
            return true;

        if (isBorderTile)
            return true;

        return true;
    }

    void QueuePendingTile(Vector3Int cell)
    {
        long key = PackTileKey(cell.x, cell.y);
        if (pendingTileKeys.Contains(key))
            return;

        pendingTileKeys.Add(key);
        pendingTiles.Add(cell);
        debugPendingTileCount = pendingTiles.Count;
    }

    void CommitChartChanges()
    {
        textureVersion++;
        compatibilityPreviewDirty = true;
        debugTextureVersion = textureVersion;
        debugRecordedTileCount = recordedTileCount;
        debugAllocatedPageCount = chartPages.Count;
    }

    void HandleIslandChunkGenerated(RectInt chunkRect)
    {
        if (!referencesValid)
            return;

        RefreshRecordedChunk(chunkRect);
    }

    void RefreshRecordedChunk(RectInt chunkRect)
    {
        bool anyChanged = false;
        for (int y = chunkRect.yMin; y < chunkRect.yMax; y++)
        {
            for (int x = chunkRect.xMin; x < chunkRect.xMax; x++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                if (!IsInsideChartBounds(cell))
                    continue;

                if (!IsTileRecorded(cell))
                    continue;

                if (!TryResolveRecordedChartEntry(cell, out ChartCategory category, out byte paletteIndex))
                    continue;

                if (UpdateRecordedTile(cell, category, paletteIndex))
                    anyChanged = true;
            }
        }

        if (anyChanged)
            CommitChartChanges();
    }

    byte GetOrCreatePaletteIndex(Color32 color)
    {
        uint key = PackColor(color);
        if (customPaletteLookup.TryGetValue(key, out byte existing))
            return existing;

        if (customPalette.Count >= 255)
            return 0;

        byte index = (byte)customPalette.Count;
        customPalette.Add(color);
        customPaletteLookup[key] = index;
        return index;
    }

    void AuditBoatDiscoveryState(Vector2 worldPosition)
    {
        debugBoatWorldPosition = worldPosition;
        Vector3Int boatCell = islandTilemap.WorldToCell(worldPosition);
        debugBoatTileCell = boatCell;
        debugBoatChartCategory = GetChartCategoryAtCell(boatCell);
        debugLandTileCountNearBoat = 0;
        debugNearestLandTileDistance = -1f;
        debugNearestLandTileCell = default;

        int tileRadius = Mathf.CeilToInt(revealRadiusWorld) + 1;
        float revealRadiusSqr = revealRadiusWorld * revealRadiusWorld;
        float nearestDistanceSqr = float.MaxValue;

        for (int y = boatCell.y - tileRadius; y <= boatCell.y + tileRadius; y++)
        {
            for (int x = boatCell.x - tileRadius; x <= boatCell.x + tileRadius; x++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                ChartCategory sampledCategory = GetChartCategoryAtCell(cell);
                if (sampledCategory != ChartCategory.LowLand
                    && sampledCategory != ChartCategory.MidLand
                    && sampledCategory != ChartCategory.HighLand
                    && sampledCategory != ChartCategory.CustomPalette
                    && sampledCategory != ChartCategory.Dock
                    && sampledCategory != ChartCategory.Treasure)
                    continue;

                Vector2 cellCenterWorld = islandTilemap.GetCellCenterWorld(cell);
                float distanceSqr = ((Vector2)cellCenterWorld - worldPosition).sqrMagnitude;
                if (distanceSqr > revealRadiusSqr)
                    continue;

                debugLandTileCountNearBoat++;
                if (distanceSqr >= nearestDistanceSqr)
                    continue;

                nearestDistanceSqr = distanceSqr;
                debugNearestLandTileCell = cell;
            }
        }

        if (nearestDistanceSqr < float.MaxValue)
            debugNearestLandTileDistance = Mathf.Sqrt(nearestDistanceSqr);
    }

    public void RenderViewport(Vector2 centerWorld, float halfWorldWidth, float halfWorldHeight, Texture2D targetTexture, Color32[] targetPixels)
    {
        if (targetTexture == null || targetPixels == null)
            return;

        double startTime = Time.realtimeSinceStartupAsDouble;
        int targetWidth = targetTexture.width;
        int targetHeight = targetTexture.height;
        if (targetPixels.Length != targetWidth * targetHeight)
            return;

        float viewportWidth = Mathf.Max(1f, halfWorldWidth * 2f);
        float viewportHeight = Mathf.Max(1f, halfWorldHeight * 2f);

        for (int y = 0; y < targetHeight; y++)
        {
            float normalizedY = targetHeight > 1 ? (y + 0.5f) / targetHeight : 0.5f;
            float sampleWorldY = centerWorld.y + ((0.5f - normalizedY) * viewportHeight);
            int tileY = Mathf.FloorToInt(sampleWorldY);
            int flippedRowStart = (targetHeight - 1 - y) * targetWidth;

            for (int x = 0; x < targetWidth; x++)
            {
                float normalizedX = targetWidth > 1 ? (x + 0.5f) / targetWidth : 0.5f;
                float sampleWorldX = centerWorld.x + ((normalizedX - 0.5f) * viewportWidth);
                int tileX = Mathf.FloorToInt(sampleWorldX);
                targetPixels[flippedRowStart + x] = GetChartColorAtCell(new Vector3Int(tileX, tileY, 0));
            }
        }

        targetTexture.SetPixels32(targetPixels);
        targetTexture.Apply(false, false);
        debugLastRenderViewportDurationMs = (float)((Time.realtimeSinceStartupAsDouble - startTime) * 1000.0);
    }

    public Vector2 ClampWorldCenterToBounds(Vector2 desiredCenterWorld, float halfWorldWidth, float halfWorldHeight)
    {
        float minCenterX = worldMinTileCoord + halfWorldWidth;
        float maxCenterX = worldMaxTileCoordExclusive - halfWorldWidth;
        float minCenterY = worldMinTileCoord + halfWorldHeight;
        float maxCenterY = worldMaxTileCoordExclusive - halfWorldHeight;

        if (minCenterX > maxCenterX)
        {
            float midpoint = (worldMinTileCoord + worldMaxTileCoordExclusive) * 0.5f;
            minCenterX = midpoint;
            maxCenterX = midpoint;
        }

        if (minCenterY > maxCenterY)
        {
            float midpoint = (worldMinTileCoord + worldMaxTileCoordExclusive) * 0.5f;
            minCenterY = midpoint;
            maxCenterY = midpoint;
        }

        return new Vector2(
            Mathf.Clamp(desiredCenterWorld.x, minCenterX, maxCenterX),
            Mathf.Clamp(desiredCenterWorld.y, minCenterY, maxCenterY));
    }

    public Vector2 ClampWorldPositionToBounds(Vector2 desiredWorldPosition)
    {
        float maxWorldPosition = worldMaxTileCoordExclusive - 0.001f;
        return new Vector2(
            Mathf.Clamp(desiredWorldPosition.x, worldMinTileCoord, maxWorldPosition),
            Mathf.Clamp(desiredWorldPosition.y, worldMinTileCoord, maxWorldPosition));
    }

    public Vector2 GetNormalizedPositionInViewport(Vector2 worldPosition, Vector2 viewportCenterWorld, float halfWorldWidth, float halfWorldHeight)
    {
        float minX = viewportCenterWorld.x - halfWorldWidth;
        float maxX = viewportCenterWorld.x + halfWorldWidth;
        float minY = viewportCenterWorld.y - halfWorldHeight;
        float maxY = viewportCenterWorld.y + halfWorldHeight;
        float normalizedX = Mathf.InverseLerp(minX, maxX, worldPosition.x);
        float normalizedY = Mathf.InverseLerp(minY, maxY, worldPosition.y);
        return new Vector2(Mathf.Clamp01(normalizedX), Mathf.Clamp01(normalizedY));
    }

    public Vector2 GetNormalizedPositionInWorld(Vector2 worldPosition)
    {
        float worldWidth = Mathf.Max(1f, worldMaxTileCoordExclusive - worldMinTileCoord);
        float normalizedX = (worldPosition.x - worldMinTileCoord) / worldWidth;
        float normalizedY = (worldPosition.y - worldMinTileCoord) / worldWidth;
        return new Vector2(Mathf.Clamp01(normalizedX), Mathf.Clamp01(normalizedY));
    }

    public ChartCategory GetChartCategoryAtCell(Vector3Int cell)
    {
        if (IsBorderTile(cell))
            return ChartCategory.Border;

        if (dockTilemap != null && dockTilemap.GetTile(cell) != null)
            return ChartCategory.Dock;

        if (goldTilemap != null && goldTilemap.GetTile(cell) != null)
            return ChartCategory.Treasure;

        if (IsPredictiveDockCell(cell))
            return ChartCategory.Dock;

        if (TryResolveInProgressIslandOverlay(cell, out ChartCategory overlayCategory, out _))
            return overlayCategory;

        if (!TryGetRecordedTile(cell, out ChartCategory category, out _))
            return ChartCategory.Undiscovered;

        return IsRecordedMapTruthBackedByLoadedWorld(cell, category)
            ? category
            : ChartCategory.Water;
    }

    public int CountMapTruthCategoriesNearWorldPosition(Vector2 worldPosition, int sampleStep = 1)
    {
        return SampleChartTruthNearWorldPosition(worldPosition, null, 0, sampleStep, false);
    }

    public int CollectChartTruthFailuresNearWorldPosition(Vector2 worldPosition, List<ChartTruthDiagnostic> failures, int maxFailures = 8, int sampleStep = 1)
    {
        if (failures == null)
            return 0;

        failures.Clear();
        return SampleChartTruthNearWorldPosition(worldPosition, failures, maxFailures, sampleStep, true);
    }

    int SampleChartTruthNearWorldPosition(Vector2 worldPosition, List<ChartTruthDiagnostic> failures, int maxFailures, int sampleStep, bool collectFailures)
    {
        if (!referencesValid || islandTilemap == null)
            return 0;

        int matchedCategoryCount = 0;
        int step = Mathf.Max(1, sampleStep);
        Vector3Int centerCell = islandTilemap.WorldToCell(worldPosition);
        int cellRadius = Mathf.CeilToInt(revealRadiusWorld) + 1;
        float revealRadiusSqr = revealRadiusWorld * revealRadiusWorld;

        for (int y = centerCell.y - cellRadius; y <= centerCell.y + cellRadius; y += step)
        {
            for (int x = centerCell.x - cellRadius; x <= centerCell.x + cellRadius; x += step)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                Vector2 cellCenterWorld = islandTilemap.GetCellCenterWorld(cell);
                if (((Vector2)cellCenterWorld - worldPosition).sqrMagnitude > revealRadiusSqr)
                    continue;

                ChartCategory category = GetChartCategoryAtCell(cell);
                if (!IsMapTruthCategory(category))
                    continue;

                matchedCategoryCount++;
                if (!collectFailures)
                    continue;

                if (DoesChartCategoryMatchWorldTruth(cell, category, out ChartTruthDiagnostic diagnostic))
                    continue;

                failures.Add(diagnostic);
                if (failures.Count >= maxFailures)
                    return failures.Count;
            }
        }

        return collectFailures ? failures.Count : matchedCategoryCount;
    }

    bool DoesChartCategoryMatchWorldTruth(Vector3Int cell, ChartCategory category, out ChartTruthDiagnostic diagnostic)
    {
        Vector2 worldPosition = islandTilemap.GetCellCenterWorld(cell);
        diagnostic = BuildChartTruthDiagnostic(cell, worldPosition, category, "No accepted island, dock, or treasure target matched this mapped cell.");

        if (category == ChartCategory.Treasure)
        {
            if (islandGenerationController != null
                && islandGenerationController.TryGetTreasureTargetCells(out Vector3Int cellA, out Vector3Int cellB)
                && (cell == cellA || cell == cellB))
            {
                return true;
            }

            diagnostic.reason = "Mapped treasure cell does not match the active treasure target cells.";
            return false;
        }

        if (category == ChartCategory.Dock && dockTilemap != null && dockTilemap.GetTile(cell) != null)
            return true;

        if (category != ChartCategory.Dock && islandTilemap != null && islandTilemap.GetTile(cell) != null)
            return true;

        if (islandGenerationController == null)
            return false;

        // Once the chunk is loaded, the live tilemaps are the truth. Do not let
        // deterministic source prediction mask a chart/world mismatch.
        if (islandGenerationController.IsChunkLoadedForCell(cell))
        {
            diagnostic.reason = category == ChartCategory.Dock
                ? "Mapped dock cell is in a loaded chunk, but the live dock tilemap has no dock tile there."
                : "Mapped island cell is in a loaded chunk, but the live island tilemap has no island tile there.";
            return false;
        }

        islandGenerationController.CollectAcceptedIslandSourcesNearWorldPosition(worldPosition, 1f, truthAcceptedIslands);
        for (int i = 0; i < truthAcceptedIslands.Count; i++)
        {
            IslandGenerationController.IslandSourceDescriptor source = truthAcceptedIslands[i];
            if (category == ChartCategory.Dock)
            {
                if (!islandGenerationController.TryGetAcceptedIslandDockCells(source.deterministicKey, truthDockCells))
                    continue;

                for (int dockIndex = 0; dockIndex < truthDockCells.Count; dockIndex++)
                {
                    if (truthDockCells[dockIndex] == cell)
                        return true;
                }

                continue;
            }

            if (islandGenerationController.TryResolveAcceptedIslandTile(source.deterministicKey, cell, out _))
                return true;
        }

        return false;
    }

    ChartTruthDiagnostic BuildChartTruthDiagnostic(Vector3Int cell, Vector2 worldPosition, ChartCategory category, string reason)
    {
        ChartTruthDiagnostic diagnostic = new ChartTruthDiagnostic
        {
            cell = cell,
            worldPosition = worldPosition,
            chartCategory = category,
            seed = islandGenerationController != null ? islandGenerationController.Seed : 0,
            reason = reason
        };

        if (islandGenerationController == null)
            return diagnostic;

        diagnostic.chunkLoaded = islandGenerationController.IsChunkLoadedForCell(cell);
        diagnostic.chunkDeferred = islandGenerationController.IsChunkDeferredForCell(cell);
        islandGenerationController.CollectAcceptedIslandSourcesNearWorldPosition(worldPosition, revealRadiusWorld, truthAcceptedIslands);

        float nearestDistanceSqr = float.MaxValue;
        for (int i = 0; i < truthAcceptedIslands.Count; i++)
        {
            IslandGenerationController.IslandSourceDescriptor source = truthAcceptedIslands[i];
            float distanceSqr = (source.center - worldPosition).sqrMagnitude;
            if (distanceSqr >= nearestDistanceSqr)
                continue;

            nearestDistanceSqr = distanceSqr;
            diagnostic.hasAcceptedSource = true;
            diagnostic.nearestAcceptedSourceKey = source.deterministicKey;
            diagnostic.nearestAcceptedSourceCenter = source.center;
            diagnostic.nearestAcceptedSourceDistance = Mathf.Sqrt(distanceSqr);
        }

        return diagnostic;
    }

    static bool IsMapTruthCategory(ChartCategory category)
    {
        return category == ChartCategory.LowLand
            || category == ChartCategory.MidLand
            || category == ChartCategory.HighLand
            || category == ChartCategory.CustomPalette
            || category == ChartCategory.Dock
            || category == ChartCategory.Treasure;
    }

    Color32 GetChartColorAtCell(Vector3Int cell)
    {
        if (!IsInsideChartBounds(cell))
            return outsideWorldMapColor;

        if (IsBorderTile(cell))
            return borderMapColor;

        if (dockTilemap != null && dockTilemap.GetTile(cell) != null)
            return dockMapColor;

        if (goldTilemap != null && goldTilemap.GetTile(cell) != null)
            return treasureMapColor;

        if (IsPredictiveDockCell(cell))
            return dockMapColor;

        if (TryResolveInProgressIslandOverlay(cell, out ChartCategory overlayCategory, out byte overlayPaletteIndex))
        {
            return overlayCategory switch
            {
                ChartCategory.LowLand => lowLandMapColor,
                ChartCategory.MidLand => midLandMapColor,
                ChartCategory.HighLand => highLandMapColor,
                ChartCategory.CustomPalette => ResolvePaletteColor(overlayPaletteIndex),
                _ => undiscoveredColor
            };
        }

        if (!TryGetRecordedTile(cell, out ChartCategory category, out byte paletteIndex))
            return undiscoveredColor;

        if (!IsRecordedMapTruthBackedByLoadedWorld(cell, category))
            return waterMapColor;

        return category switch
        {
            ChartCategory.Water => waterMapColor,
            ChartCategory.LowLand => lowLandMapColor,
            ChartCategory.MidLand => midLandMapColor,
            ChartCategory.HighLand => highLandMapColor,
            ChartCategory.Border => borderMapColor,
            ChartCategory.Dock => dockMapColor,
            ChartCategory.Treasure => treasureMapColor,
            ChartCategory.CustomPalette => ResolvePaletteColor(paletteIndex),
            _ => undiscoveredColor
        };
    }

    bool IsRecordedMapTruthBackedByLoadedWorld(Vector3Int cell, ChartCategory category)
    {
        if (!IsMapTruthCategory(category)
            || islandGenerationController == null
            || !islandGenerationController.IsChunkLoadedForCell(cell))
        {
            return true;
        }

        if (category == ChartCategory.Treasure)
            return goldTilemap != null && goldTilemap.GetTile(cell) != null;

        if (category == ChartCategory.Dock)
            return dockTilemap != null && dockTilemap.GetTile(cell) != null;

        return islandTilemap != null && islandTilemap.GetTile(cell) != null;
    }

    Color32 ResolvePaletteColor(byte paletteIndex)
    {
        if (paletteIndex <= 0 || paletteIndex >= customPalette.Count)
            return fallbackLandMapColor;

        return customPalette[paletteIndex];
    }

    bool IsInsideChartBounds(Vector3Int cell)
    {
        return cell.x >= worldMinTileCoord
            && cell.x < worldMaxTileCoordExclusive
            && cell.y >= worldMinTileCoord
            && cell.y < worldMaxTileCoordExclusive;
    }

    bool IsPotentialBoundaryTile(Vector3Int cell)
    {
        float radialDistance = new Vector2(cell.x + 0.5f, cell.y + 0.5f).magnitude;
        return radialDistance >= worldSettings.WallInnerRadiusTiles - 1f
            && radialDistance <= worldSettings.WallOuterRadiusTiles + 1f;
    }

    bool IsBorderTile(Vector3Int cell)
    {
        float radialDistance = new Vector2(cell.x + 0.5f, cell.y + 0.5f).magnitude;
        return radialDistance >= worldSettings.WallInnerRadiusTiles
            && radialDistance <= worldSettings.WallOuterRadiusTiles;
    }

    bool TryGetPage(Vector3Int cell, out ChartPage page, out int localIndex)
    {
        GetPageAndLocalIndex(cell, out Vector2Int pageCoord, out localIndex);
        return chartPages.TryGetValue(pageCoord, out page);
    }

    ChartPage GetOrCreatePage(Vector2Int pageCoord)
    {
        if (chartPages.TryGetValue(pageCoord, out ChartPage existing))
            return existing;

        ChartPage created = new ChartPage(chartPageSize * chartPageSize);
        chartPages[pageCoord] = created;
        debugAllocatedPageCount = chartPages.Count;
        return created;
    }

    void GetPageAndLocalIndex(Vector3Int cell, out Vector2Int pageCoord, out int localIndex)
    {
        int pageX = FloorDiv(cell.x - worldMinTileCoord, chartPageSize);
        int pageY = FloorDiv(cell.y - worldMinTileCoord, chartPageSize);
        int localX = Mod(cell.x - worldMinTileCoord, chartPageSize);
        int localY = Mod(cell.y - worldMinTileCoord, chartPageSize);
        pageCoord = new Vector2Int(pageX, pageY);
        localIndex = localY * chartPageSize + localX;
    }

    void EnsureCompatibilityPreviewUpToDate()
    {
        if (!compatibilityPreviewDirty)
            return;

        int worldWidthTiles = Mathf.Max(1, worldMaxTileCoordExclusive - worldMinTileCoord);
        int previewStep = Mathf.Max(1, compatibilityPreviewTileStep);
        int previewWidth = Mathf.Clamp(Mathf.CeilToInt(worldWidthTiles / (float)previewStep), 1, compatibilityTextureSizeLimit);
        int previewHeight = previewWidth;

        if (compatibilityPreviewTexture == null || compatibilityPreviewTexture.width != previewWidth || compatibilityPreviewTexture.height != previewHeight)
        {
            compatibilityPreviewTexture = new Texture2D(previewWidth, previewHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "CompatibilityChartPreview"
            };
            compatibilityPreviewPixels = new Color32[previewWidth * previewHeight];
        }

        for (int y = 0; y < previewHeight; y++)
        {
            float normalizedY = previewHeight > 1 ? (y + 0.5f) / previewHeight : 0.5f;
            float sampleWorldY = Mathf.Lerp(worldMaxTileCoordExclusive, worldMinTileCoord, normalizedY);
            int tileY = Mathf.FloorToInt(sampleWorldY);
            int flippedRowStart = (previewHeight - 1 - y) * previewWidth;
            for (int x = 0; x < previewWidth; x++)
            {
                float normalizedX = previewWidth > 1 ? (x + 0.5f) / previewWidth : 0.5f;
                float sampleWorldX = Mathf.Lerp(worldMinTileCoord, worldMaxTileCoordExclusive, normalizedX);
                int tileX = Mathf.FloorToInt(sampleWorldX);
                compatibilityPreviewPixels[flippedRowStart + x] = GetChartColorAtCell(new Vector3Int(tileX, tileY, 0));
            }
        }

        compatibilityPreviewTexture.SetPixels32(compatibilityPreviewPixels);
        compatibilityPreviewTexture.Apply(false, false);
        compatibilityPreviewDirty = false;
        debugTextureWidth = previewWidth;
        debugTextureHeight = previewHeight;
    }

    static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
            quotient--;

        return quotient;
    }

    static int Mod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    static long PackTileKey(int x, int y)
    {
        unchecked
        {
            return ((long)x << 32) ^ (uint)y;
        }
    }

    static void UnpackTileKey(long key, out int x, out int y)
    {
        x = (int)(key >> 32);
        y = (int)(key & 0xffffffff);
    }

    static uint PackColor(Color32 color)
    {
        return (uint)(color.r | (color.g << 8) | (color.b << 16) | (color.a << 24));
    }
}
