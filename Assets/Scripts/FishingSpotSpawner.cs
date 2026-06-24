using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent]
public class FishingSpotSpawner : MonoBehaviour
{
    class SpotState
    {
        public int spotIndex;
        public bool isCoastal;
        public bool isActive;
        public float respawnReadyTime;
        public Vector3Int cell;
        public FishingSpotController instance;
    }

    class ChunkState
    {
        public Vector2Int chunkCoord;
        public bool initialized;
        public int desiredCoastalCount;
        public int desiredOpenWaterCount;
        public readonly List<SpotState> spots = new List<SpotState>();
    }

    public static FishingSpotSpawner ActiveInstance { get; private set; }

    [Header("References")]
    [SerializeField] Transform boatTransform;
    [SerializeField] Camera worldCamera;
    [SerializeField] Tilemap islandTilemap;
    [SerializeField] Tilemap dockTilemap;
    [SerializeField] Tilemap waterTilemap;
    [SerializeField] FishingSpotController fishingSpotPrefab;
    [SerializeField] Transform spotRoot;

    [Header("Fallback Visual")]
    [SerializeField] TileBase fishIndicatorTile;
    [SerializeField] Sprite fishIndicatorSprite;
    [SerializeField] string fishingSpotSortingLayerName = "FishingSpots";
    [SerializeField] int fishingSpotSortingOrder = 0;

    [Header("Chunk Layout")]
    [SerializeField][Min(4)] int chunkSize = 24;
    [SerializeField][Min(0)] int generationMarginChunks = 1;
    [SerializeField][Min(1)] int candidateAttemptsPerSpot = 80;

    [Header("Density")]
    [SerializeField][Range(0f, 1f)] float coastalWeight = 0.8f;
    [SerializeField][Range(0f, 1f)] float openWaterWeight = 0.2f;
    [SerializeField][Range(0f, 1f)] float coastalSlotSpawnChance = 0.45f;
    [SerializeField][Range(0f, 1f)] float openWaterSlotSpawnChance = 0.35f;
    [SerializeField][Range(0, 4)] int maxCoastalSpotsPerChunk = 3;
    [SerializeField][Range(0, 4)] int maxOpenWaterSpotsPerChunk = 1;

    [Header("Placement Rules")]
    [SerializeField][Min(0f)] float minimumDistanceBetweenSpotsWorld = 2.75f;
    [SerializeField][Min(0)] int coastalMinDistanceToLandCells = 2;
    [SerializeField][Min(1)] int coastalMaxDistanceToLandCells = 6;
    [SerializeField][Min(1)] int openWaterMinDistanceToLandCells = 7;
    [SerializeField][Min(1)] int landSearchRadiusCells = 10;
    [SerializeField][Min(0)] int minimumDistanceToDockCells = 3;

    [Header("Lifecycle")]
    [SerializeField][Min(1f)] float respawnCooldownSeconds = 10f;
    [SerializeField][Min(0.1f)] float initialPlacementRetryDelaySeconds = 1f;
    // Unloaded chunks farther than this from the boat are dropped from the retained
    // set to bound memory. Must stay well outside the loaded region so a short
    // back-and-forth keeps depletion state. Far enough that a round-trip exceeds the
    // respawn cooldown, so dropping depletion state is never an exploit.
    [SerializeField][Min(32f)] float evictionRadiusWorld = 256f;
    [SerializeField][Min(0.25f)] float evictionSweepIntervalSeconds = 2f;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] int debugSessionSeed;
    [SerializeField] RectInt debugLoadedChunkRect;
    [SerializeField] int debugLoadedChunkCount;
    [SerializeField] int debugActiveSpotCount;
    [SerializeField] int debugCoastalSpotCount;
    [SerializeField] int debugOpenWaterSpotCount;
    [SerializeField] bool debugReferencesValid;

    readonly Dictionary<Vector2Int, ChunkState> chunkStates = new Dictionary<Vector2Int, ChunkState>();
    readonly HashSet<Vector2Int> loadedChunkCoords = new HashSet<Vector2Int>();
    readonly Dictionary<FishingSpotController, SpotState> spotStateByInstance = new Dictionary<FishingSpotController, SpotState>();
    // Reused each RefreshLoadedChunks to avoid per-frame allocations.
    readonly HashSet<Vector2Int> requiredChunksScratch = new HashSet<Vector2Int>();
    readonly List<Vector2Int> chunksToUnloadScratch = new List<Vector2Int>();
    readonly List<Vector2Int> evictionScratch = new List<Vector2Int>();
    float nextEvictionSweepTime;

    // Diagnostic seam: loaded chunks are active (instances spawned); retained
    // chunks keep their state across unload so respawn timers survive a revisit.
    public int LoadedChunkCount => loadedChunkCoords.Count;
    public int RetainedChunkCount => chunkStates.Count;

    bool referencesValid;
    IslandGenerationController islandGenerationController;

    void OnEnable()
    {
        ActiveInstance = this;
        ResolveReferences();
        referencesValid = ValidateReferences();
        debugReferencesValid = referencesValid;
    }

    void Start()
    {
        ResolveReferences();
        referencesValid = ValidateReferences();
        debugReferencesValid = referencesValid;
        if (!referencesValid)
            return;

        RefreshLoadedChunks();
    }

    void LateUpdate()
    {
        ResolveReferences();
        if (!referencesValid)
        {
            referencesValid = ValidateReferences();
            debugReferencesValid = referencesValid;
            if (!referencesValid)
                return;
        }

        RefreshLoadedChunks();
        RevalidateActiveSpots();
        UpdateChunkRespawns();
        EvictDistantRetainedChunks();
        UpdateDebugCounters();
    }

    void OnDisable()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;

        DestroyAllInstances();
    }

    public bool TryGetNearestActiveSpot(Vector3 worldPosition, float maxDistance, out FishingSpotController nearestSpot, out float nearestDistance)
    {
        nearestSpot = null;
        nearestDistance = float.MaxValue;
        bool found = false;

        foreach (KeyValuePair<FishingSpotController, SpotState> pair in spotStateByInstance)
        {
            FishingSpotController spot = pair.Key;
            if (spot == null || !spot.IsAvailable || !pair.Value.isActive)
                continue;

            float distance = Vector2.Distance(new Vector2(worldPosition.x, worldPosition.y), new Vector2(spot.transform.position.x, spot.transform.position.y));
            if (distance > maxDistance)
                continue;

            if (!found || distance < nearestDistance)
            {
                found = true;
                nearestSpot = spot;
                nearestDistance = distance;
            }
        }

        return found;
    }

    public void ConsumeSpot(FishingSpotController spot)
    {
        if (spot == null || !spotStateByInstance.TryGetValue(spot, out SpotState state))
            return;

        state.isActive = false;
        state.respawnReadyTime = Time.time + respawnCooldownSeconds;
        state.cell = default;

        spot.MarkConsumed();
        spotStateByInstance.Remove(spot);
        Destroy(spot.gameObject);
    }

    void ResolveReferences()
    {
        if (boatTransform == null)
        {
            BoatController boatController = FindAnyObjectByType<BoatController>();
            if (boatController != null)
                boatTransform = boatController.transform;
        }

        if (worldCamera == null)
            worldCamera = Camera.main;

        if (waterTilemap == null)
        {
            InfiniteWaterTileMap waterMap = InfiniteWaterTileMap.ActiveInstance ?? FindAnyObjectByType<InfiniteWaterTileMap>();
            if (waterMap != null)
                waterTilemap = waterMap.WaterTilemap;
        }

        if (islandGenerationController == null)
            islandGenerationController = FindAnyObjectByType<IslandGenerationController>();

        if (islandTilemap == null && islandGenerationController != null)
        {
            islandTilemap = islandGenerationController.IslandTilemap;
            if (dockTilemap == null)
                dockTilemap = islandGenerationController.DockTilemap;
        }

        if (spotRoot == null)
            spotRoot = transform;

        if (fishIndicatorSprite == null)
        {
            Tile indicatorTileAsset = fishIndicatorTile as Tile;
            if (indicatorTileAsset != null)
                fishIndicatorSprite = indicatorTileAsset.sprite;
        }
    }

    bool ValidateReferences()
    {
        if (boatTransform == null || worldCamera == null || islandTilemap == null || waterTilemap == null)
            return false;

        if (spotRoot == null)
            return false;

        if (fishingSpotPrefab == null && fishIndicatorSprite == null)
            return false;

        return true;
    }

    void RefreshLoadedChunks()
    {
        RectInt requiredRect = GetRequiredChunkRect();
        debugLoadedChunkRect = requiredRect;

        requiredChunksScratch.Clear();
        for (int y = requiredRect.yMin; y < requiredRect.yMax; y++)
        {
            for (int x = requiredRect.xMin; x < requiredRect.xMax; x++)
            {
                Vector2Int chunkCoord = new Vector2Int(x, y);
                requiredChunksScratch.Add(chunkCoord);

                if (!loadedChunkCoords.Contains(chunkCoord))
                    LoadChunk(chunkCoord);
            }
        }

        chunksToUnloadScratch.Clear();
        foreach (Vector2Int loadedChunk in loadedChunkCoords)
        {
            if (!requiredChunksScratch.Contains(loadedChunk))
                chunksToUnloadScratch.Add(loadedChunk);
        }

        for (int i = 0; i < chunksToUnloadScratch.Count; i++)
            UnloadChunk(chunksToUnloadScratch[i]);
    }

    RectInt GetRequiredChunkRect()
    {
        Vector3 bottomLeft = worldCamera.ViewportToWorldPoint(new Vector3(0f, 0f, 0f));
        Vector3 topRight = worldCamera.ViewportToWorldPoint(new Vector3(1f, 1f, 0f));
        Vector3 minWorld = Vector3.Min(bottomLeft, topRight);
        Vector3 maxWorld = Vector3.Max(bottomLeft, topRight);

        Vector3Int minCell = waterTilemap.WorldToCell(minWorld);
        Vector3Int maxCell = waterTilemap.WorldToCell(maxWorld);

        Vector2Int minChunk = WorldCellToChunkCoord(minCell);
        Vector2Int maxChunk = WorldCellToChunkCoord(maxCell);

        return BuildRequiredChunkRect(minChunk, maxChunk, generationMarginChunks);
    }

    // Diagnostic seam: the inclusive chunk span [min,max] expanded by margin on
    // every side, as a RectInt covering every chunk that must be loaded.
    public static RectInt BuildRequiredChunkRect(Vector2Int minChunk, Vector2Int maxChunk, int margin)
    {
        int xMin = minChunk.x - margin;
        int yMin = minChunk.y - margin;
        int xMax = maxChunk.x + margin;
        int yMax = maxChunk.y + margin;
        return new RectInt(xMin, yMin, (xMax - xMin) + 1, (yMax - yMin) + 1);
    }

    void LoadChunk(Vector2Int chunkCoord)
    {
        loadedChunkCoords.Add(chunkCoord);
        ChunkState state = GetOrCreateChunkState(chunkCoord);
        InitializeChunkStateIfNeeded(state);
        ActivateChunkInstances(state);
    }

    void UnloadChunk(Vector2Int chunkCoord)
    {
        if (!loadedChunkCoords.Remove(chunkCoord))
            return;

        if (!chunkStates.TryGetValue(chunkCoord, out ChunkState state))
            return;

        for (int i = 0; i < state.spots.Count; i++)
        {
            SpotState spot = state.spots[i];
            if (spot.instance == null)
                continue;

            spotStateByInstance.Remove(spot.instance);
            Destroy(spot.instance.gameObject);
            spot.instance = null;
        }
    }

    void UpdateChunkRespawns()
    {
        foreach (Vector2Int chunkCoord in loadedChunkCoords)
        {
            if (!chunkStates.TryGetValue(chunkCoord, out ChunkState state))
                continue;

            for (int i = 0; i < state.spots.Count; i++)
            {
                SpotState spot = state.spots[i];
                if (spot.isActive || Time.time < spot.respawnReadyTime)
                    continue;

                TryAssignSpotCell(state, spot, true);
                if (!spot.isActive)
                    continue;

                if (spot.instance == null)
                    SpawnSpotInstance(state, spot);
            }
        }
    }

    void RevalidateActiveSpots()
    {
        // Only loaded chunks have live spots worth revalidating. Retained-but-
        // unloaded chunks have no instances, so scanning them every frame was pure
        // overhead that grew with how much of the world had been explored.
        foreach (Vector2Int chunkCoord in loadedChunkCoords)
        {
            if (!chunkStates.TryGetValue(chunkCoord, out ChunkState state))
                continue;

            for (int i = 0; i < state.spots.Count; i++)
            {
                SpotState spot = state.spots[i];
                if (!spot.isActive)
                    continue;

                if (IsStillValidSpotCell(spot.cell, spot.isCoastal, state, spot))
                    continue;

                if (spot.instance != null)
                {
                    spotStateByInstance.Remove(spot.instance);
                    Destroy(spot.instance.gameObject);
                    spot.instance = null;
                }

                spot.isActive = false;
                spot.respawnReadyTime = Time.time + initialPlacementRetryDelaySeconds;
            }
        }
    }

    // Throttled sweep: drop retained-but-unloaded chunks the boat has sailed far
    // from, bounding the retained set to a disc around the player. An evicted chunk
    // has no live instances (those were destroyed at unload), so its deterministic
    // layout simply regenerates if revisited; only short-lived depletion state is
    // forgotten, which is exploit-free at this distance (see ADR 0007).
    void EvictDistantRetainedChunks()
    {
        if (Time.time < nextEvictionSweepTime)
            return;

        nextEvictionSweepTime = Time.time + Mathf.Max(0.25f, evictionSweepIntervalSeconds);
        if (boatTransform == null || waterTilemap == null)
            return;

        Vector2 boatPosition = boatTransform.position;
        evictionScratch.Clear();
        foreach (KeyValuePair<Vector2Int, ChunkState> pair in chunkStates)
        {
            bool isLoaded = loadedChunkCoords.Contains(pair.Key);
            if (IsChunkEvictable(boatPosition, ChunkCenterWorld(pair.Key), evictionRadiusWorld, isLoaded))
                evictionScratch.Add(pair.Key);
        }

        for (int i = 0; i < evictionScratch.Count; i++)
            chunkStates.Remove(evictionScratch[i]);
    }

    // Diagnostic seam: a chunk is evictable when it is not currently loaded and its
    // centre is beyond the eviction radius from the boat. Loaded chunks are never
    // evicted regardless of distance.
    public static bool IsChunkEvictable(Vector2 boatPosition, Vector2 chunkCenterWorld, float evictionRadius, bool isLoaded)
    {
        if (isLoaded)
            return false;

        return (chunkCenterWorld - boatPosition).sqrMagnitude > evictionRadius * evictionRadius;
    }

    Vector2 ChunkCenterWorld(Vector2Int chunkCoord)
    {
        RectInt cellRect = GetChunkCellRect(chunkCoord);
        Vector3Int centerCell = new Vector3Int(cellRect.xMin + (cellRect.width / 2), cellRect.yMin + (cellRect.height / 2), 0);
        return waterTilemap.GetCellCenterWorld(centerCell);
    }

    ChunkState GetOrCreateChunkState(Vector2Int chunkCoord)
    {
        if (chunkStates.TryGetValue(chunkCoord, out ChunkState existingState))
            return existingState;

        ChunkState newState = new ChunkState();
        newState.chunkCoord = chunkCoord;
        chunkStates.Add(chunkCoord, newState);
        return newState;
    }

    void InitializeChunkStateIfNeeded(ChunkState state)
    {
        if (state.initialized)
            return;

        System.Random random = CreateChunkRandom(state.chunkCoord, 0);
        state.desiredCoastalCount = DetermineDesiredCount(random, maxCoastalSpotsPerChunk, coastalSlotSpawnChance);
        state.desiredOpenWaterCount = DetermineDesiredCount(random, maxOpenWaterSpotsPerChunk, openWaterSlotSpawnChance);

        int spotIndex = 0;
        for (int i = 0; i < state.desiredCoastalCount; i++)
        {
            SpotState spot = new SpotState();
            spot.spotIndex = spotIndex++;
            spot.isCoastal = true;
            TryAssignSpotCell(state, spot, false);
            state.spots.Add(spot);
        }

        for (int i = 0; i < state.desiredOpenWaterCount; i++)
        {
            SpotState spot = new SpotState();
            spot.spotIndex = spotIndex++;
            spot.isCoastal = false;
            TryAssignSpotCell(state, spot, false);
            state.spots.Add(spot);
        }

        state.initialized = true;
    }

    int DetermineDesiredCount(System.Random random, int maxCount, float slotChance)
    {
        int count = 0;
        for (int i = 0; i < maxCount; i++)
        {
            if (random.NextDouble() <= slotChance)
                count++;
        }

        return count;
    }

    void ActivateChunkInstances(ChunkState state)
    {
        for (int i = 0; i < state.spots.Count; i++)
        {
            SpotState spot = state.spots[i];
            if (!spot.isActive)
                continue;

            if (spot.instance == null)
                SpawnSpotInstance(state, spot);
        }
    }

    void TryAssignSpotCell(ChunkState state, SpotState spot, bool rerollPosition)
    {
        Vector3Int chosenCell;
        if (TryFindCandidateCell(state, spot, rerollPosition, out chosenCell))
        {
            spot.cell = chosenCell;
            spot.isActive = true;
            spot.respawnReadyTime = 0f;
            return;
        }

        spot.isActive = false;
        spot.respawnReadyTime = Time.time + (rerollPosition ? respawnCooldownSeconds : initialPlacementRetryDelaySeconds);
    }

    bool TryFindCandidateCell(ChunkState state, SpotState spot, bool rerollPosition, out Vector3Int chosenCell)
    {
        chosenCell = default;
        RectInt chunkBounds = GetChunkCellRect(state.chunkCoord);
        int rerollSalt = rerollPosition ? Mathf.FloorToInt(Time.time * 10f) : 0;
        System.Random random = CreateChunkRandom(state.chunkCoord, (spot.spotIndex + 1) * 173 + rerollSalt);

        float totalWeight = Mathf.Max(0.0001f, coastalWeight + openWaterWeight);
        float normalizedCoastalWeight = coastalWeight / totalWeight;
        bool preferCoastal = random.NextDouble() <= normalizedCoastalWeight;
        if (spot.isCoastal != preferCoastal)
            random = CreateChunkRandom(state.chunkCoord, (spot.spotIndex + 1) * 173 + rerollSalt + 31);

        for (int attempt = 0; attempt < candidateAttemptsPerSpot; attempt++)
        {
            int cellX = random.Next(chunkBounds.xMin, chunkBounds.xMax);
            int cellY = random.Next(chunkBounds.yMin, chunkBounds.yMax);
            Vector3Int candidateCell = new Vector3Int(cellX, cellY, 0);

            if (!IsValidWaterCell(candidateCell))
                continue;

            int distanceToLand = FindDistanceToNearestLand(candidateCell, landSearchRadiusCells);
            if (!MatchesRequestedWaterBand(distanceToLand, spot.isCoastal))
                continue;

            if (!RespectsDockBuffer(candidateCell))
                continue;

            if (!RespectsSpotSpacing(candidateCell, state, spot))
                continue;

            chosenCell = candidateCell;
            return true;
        }

        return false;
    }

    bool IsStillValidSpotCell(Vector3Int candidateCell, bool isCoastal, ChunkState owningChunk, SpotState currentSpot)
    {
        if (!IsValidWaterCell(candidateCell))
            return false;

        int distanceToLand = FindDistanceToNearestLand(candidateCell, landSearchRadiusCells);
        if (!MatchesRequestedWaterBand(distanceToLand, isCoastal))
            return false;

        if (!RespectsDockBuffer(candidateCell))
            return false;

        return RespectsSpotSpacing(candidateCell, owningChunk, currentSpot);
    }

    bool MatchesRequestedWaterBand(int distanceToLand, bool wantsCoastal)
    {
        if (distanceToLand < 0)
            return false;

        if (wantsCoastal)
            return distanceToLand >= coastalMinDistanceToLandCells && distanceToLand <= coastalMaxDistanceToLandCells;

        return distanceToLand >= openWaterMinDistanceToLandCells;
    }

    bool IsValidWaterCell(Vector3Int cell)
    {
        if (!waterTilemap.HasTile(cell))
            return false;

        if (islandTilemap.HasTile(cell))
            return false;

        return true;
    }

    bool RespectsDockBuffer(Vector3Int candidateCell)
    {
        if (dockTilemap == null || minimumDistanceToDockCells <= 0)
            return true;

        int minimumDistanceSqr = minimumDistanceToDockCells * minimumDistanceToDockCells;
        for (int y = -minimumDistanceToDockCells; y <= minimumDistanceToDockCells; y++)
        {
            for (int x = -minimumDistanceToDockCells; x <= minimumDistanceToDockCells; x++)
            {
                if ((x * x) + (y * y) > minimumDistanceSqr)
                    continue;

                Vector3Int candidate = new Vector3Int(candidateCell.x + x, candidateCell.y + y, 0);
                if (dockTilemap.GetTile(candidate) != null)
                    return false;
            }
        }

        return true;
    }

    int FindDistanceToNearestLand(Vector3Int cell, int maxRadius)
    {
        float closestSqrDistance = float.MaxValue;
        bool foundLand = false;

        for (int y = -maxRadius; y <= maxRadius; y++)
        {
            for (int x = -maxRadius; x <= maxRadius; x++)
            {
                Vector3Int candidate = new Vector3Int(cell.x + x, cell.y + y, 0);
                if (!islandTilemap.HasTile(candidate))
                    continue;

                float sqrDistance = (x * x) + (y * y);
                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    foundLand = true;
                }
            }
        }

        if (!foundLand)
            return maxRadius + 1;

        return Mathf.RoundToInt(Mathf.Sqrt(closestSqrDistance));
    }

    bool RespectsSpotSpacing(Vector3Int candidateCell, ChunkState owningChunk, SpotState currentSpot)
    {
        Vector3 candidateWorld = waterTilemap.GetCellCenterWorld(candidateCell);
        float minimumDistanceSqr = minimumDistanceBetweenSpotsWorld * minimumDistanceBetweenSpotsWorld;

        foreach (ChunkState state in chunkStates.Values)
        {
            for (int i = 0; i < state.spots.Count; i++)
            {
                SpotState otherSpot = state.spots[i];
                if (otherSpot == currentSpot || !otherSpot.isActive)
                    continue;

                Vector3 otherWorld = waterTilemap.GetCellCenterWorld(otherSpot.cell);
                if ((otherWorld - candidateWorld).sqrMagnitude < minimumDistanceSqr)
                    return false;
            }
        }

        return true;
    }

    void SpawnSpotInstance(ChunkState state, SpotState spot)
    {
        FishingSpotController instance = CreateSpotInstance();
        if (instance == null)
            return;

        Vector3 worldPosition = waterTilemap.GetCellCenterWorld(spot.cell);
        instance.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
        instance.Initialize(this, state.chunkCoord, spot.spotIndex, spot.isCoastal);
        spot.instance = instance;
        spotStateByInstance[instance] = spot;
    }

    FishingSpotController CreateSpotInstance()
    {
        if (fishingSpotPrefab != null)
            return Instantiate(fishingSpotPrefab, spotRoot);

        GameObject spotObject = new GameObject("FishingSpot");
        spotObject.transform.SetParent(spotRoot, false);

        SpriteRenderer spriteRenderer = spotObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = fishIndicatorSprite;
        spriteRenderer.sortingLayerName = fishingSpotSortingLayerName;
        spriteRenderer.sortingOrder = fishingSpotSortingOrder;

        spotObject.AddComponent<DayNightTintGroup>();
        return spotObject.AddComponent<FishingSpotController>();
    }

    RectInt GetChunkCellRect(Vector2Int chunkCoord)
    {
        int minX = chunkCoord.x * chunkSize;
        int minY = chunkCoord.y * chunkSize;
        return new RectInt(minX, minY, chunkSize, chunkSize);
    }

    Vector2Int WorldCellToChunkCoord(Vector3Int cell)
    {
        return new Vector2Int(FloorDiv(cell.x, chunkSize), FloorDiv(cell.y, chunkSize));
    }

    static int FloorDiv(int value, int divisor)
    {
        if (divisor == 0)
            return 0;

        int quotient = value / divisor;
        int remainder = value % divisor;
        if (remainder != 0 && ((remainder < 0) ^ (divisor < 0)))
            quotient--;

        return quotient;
    }

    System.Random CreateChunkRandom(Vector2Int chunkCoord, int salt)
    {
        // Fishing spots are a static world feature: derive them from the single
        // World Seed so they reproduce across sessions (and survive a save/load).
        int worldSeed = ResolveWorldSeed();
        debugSessionSeed = worldSeed;
        unchecked
        {
            int hash = worldSeed;
            hash = (hash * 397) ^ chunkCoord.x;
            hash = (hash * 397) ^ chunkCoord.y;
            hash = (hash * 397) ^ salt;
            return new System.Random(hash);
        }
    }

    int ResolveWorldSeed()
    {
        if (islandGenerationController == null)
            islandGenerationController = FindAnyObjectByType<IslandGenerationController>();

        return islandGenerationController != null ? islandGenerationController.Seed : 0;
    }

    void DestroyAllInstances()
    {
        List<FishingSpotController> instances = new List<FishingSpotController>(spotStateByInstance.Keys);
        for (int i = 0; i < instances.Count; i++)
        {
            if (instances[i] != null)
                Destroy(instances[i].gameObject);
        }

        spotStateByInstance.Clear();

        foreach (ChunkState state in chunkStates.Values)
        {
            for (int i = 0; i < state.spots.Count; i++)
                state.spots[i].instance = null;
        }

        loadedChunkCoords.Clear();
    }

    void UpdateDebugCounters()
    {
        debugLoadedChunkCount = loadedChunkCoords.Count;
        debugActiveSpotCount = 0;
        debugCoastalSpotCount = 0;
        debugOpenWaterSpotCount = 0;

        foreach (ChunkState state in chunkStates.Values)
        {
            for (int i = 0; i < state.spots.Count; i++)
            {
                SpotState spot = state.spots[i];
                if (!spot.isActive)
                    continue;

                debugActiveSpotCount++;
                if (spot.isCoastal)
                    debugCoastalSpotCount++;
                else
                    debugOpenWaterSpotCount++;
            }
        }
    }
}
