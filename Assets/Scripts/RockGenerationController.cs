using System.Runtime.CompilerServices;
using System.Collections.Generic;
using UnityEngine;

public class RockGenerationController : WorldChunkSpawner<RockGenerationController.ChunkRockData>
{
    enum RockSizeClass
    {
        Small,
        Medium,
        Large
    }

    enum HazardLayoutType
    {
        SingleSide,
        Channel,
        Wrapped
    }

    // Public because it is the WorldChunkSpawner<TChunk> payload type for this
    // spawner (a public base type argument must be at least as accessible).
    public class ChunkRockData
    {
        public GameObject root;
        public readonly List<GameObject> instances = new List<GameObject>();
    }

    struct PlacedRock
    {
        public Vector2 position;
        public float footprintRadius;
    }

    struct ClusterPlacementPlan
    {
        public Vector2 anchor;
        public float clusterAngleDegrees;
        public Vector2 outward;
        public float clusterRadius;
    }

    [Header("References")]
    [SerializeField] IslandGenerationController islandGenerationController;
    [SerializeField] Camera worldCamera;
    [SerializeField] Transform boatTransform;
    [SerializeField] WorldGenerationSettings worldSettings;

    [Header("Rock Prefabs")]
    [SerializeField] GameObject[] smallRocks;
    [SerializeField] GameObject[] mediumRocks;
    [SerializeField] GameObject[] largeRocks;

    [Header("Island Hazard Tuning")]
    [SerializeField][Range(0f, 1f)] float rockIslandChanceMin = 0.46f;
    [SerializeField][Range(0f, 1f)] float rockIslandChanceMax = 0.88f;
    [SerializeField] float outerHazardChanceBoost = 0.2f;
    [SerializeField] float safeApproachArcDegrees = 95f;
    [SerializeField] float hazardSectorArcDegrees = 85f;
    [SerializeField] float clusterShoreEmbedDistance = 0.35f;
    [SerializeField] float clusterOffshoreDistance = 1.8f;
    [SerializeField] float clusterRadiusMin = 0.8f;
    [SerializeField] float clusterRadiusMax = 2.6f;
    [SerializeField] float landingBufferExtraDistance = 0.75f;

    [Header("Hazard Layout Weights")]
    [SerializeField][Range(0f, 1f)] float channelLayoutWeightMedium = 0.38f;
    [SerializeField][Range(0f, 1f)] float channelLayoutWeightLarge = 0.58f;
    [SerializeField][Range(0f, 1f)] float wrappedLayoutWeightMedium = 0.16f;
    [SerializeField][Range(0f, 1f)] float wrappedLayoutWeightLarge = 0.28f;
    [SerializeField][Range(0f, 1f)] float outerChannelWeightBoost = 0.14f;
    [SerializeField][Range(0f, 1f)] float outerWrappedWeightBoost = 0.1f;
    [SerializeField][Min(0f)] float channelLaneOffsetDegrees = 34f;
    [SerializeField][Min(0f)] float wrappedSideOffsetDegrees = 72f;

    [Header("Cluster Composition")]
    [SerializeField] int maxClustersSmallIsland = 1;
    [SerializeField] int maxClustersMediumIsland = 2;
    [SerializeField] int maxClustersLargeIsland = 4;
    [SerializeField] int minRocksPerCluster = 2;
    [SerializeField] int maxRocksPerCluster = 6;
    [SerializeField][Range(0f, 1f)] float largeAnchorChance = 0.18f;
    [SerializeField][Range(0f, 1f)] float mediumAnchorChance = 0.64f;
    [SerializeField][Range(0f, 1f)] float mediumSatelliteChance = 0.4f;

    [Header("Offshore Satellites")]
    [SerializeField][Range(0f, 1f)] float offshoreSatelliteChanceMedium = 0.12f;
    [SerializeField][Range(0f, 1f)] float offshoreSatelliteChanceLarge = 0.28f;
    [SerializeField][Range(0f, 1f)] float offshoreSatelliteChanceOuterBoost = 0.18f;
    [SerializeField] float offshoreSatelliteDistanceMin = 3.1f;
    [SerializeField] float offshoreSatelliteDistanceMax = 5.4f;
    [SerializeField] float offshoreSatelliteClusterRadius = 1.3f;

    [Header("Spawn Orientation")]
    [SerializeField] float randomRotationMinDegrees = 0f;
    [SerializeField] float randomRotationMaxDegrees = 0f;

    [Header("Visible Water Lapping")]
    [SerializeField] bool configureVisibleIdleRipples = true;
    [SerializeField] float smallRockRippleStrength = 0.42f;
    [SerializeField] float mediumRockRippleStrength = 0.58f;
    [SerializeField] float largeRockRippleStrength = 0.8f;
    [SerializeField] float smallRockRippleSizeMultiplier = 0.65f;
    [SerializeField] float mediumRockRippleSizeMultiplier = 1.0f;
    [SerializeField] float largeRockRippleSizeMultiplier = 1.55f;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] RectInt debugRequiredChunkRect;
    [SerializeField] int debugLoadedChunkCount;
    [SerializeField] int debugSpawnedRockCount;

    readonly Dictionary<GameObject, float> prefabFootprintRadiusCache = new Dictionary<GameObject, float>();
    readonly HashSet<int> warnedMissingColliderPrefabs = new HashSet<int>();
    readonly HashSet<int> warnedMissingObstaclePrefabs = new HashSet<int>();
    readonly List<IslandGenerationController.IslandSourceDescriptor> chunkIslandSources = new List<IslandGenerationController.IslandSourceDescriptor>();

    bool referencesValid;
    Vector2 spawnProtectionCenter;
    int rockCountSeedOffset = 4701;

    protected override bool PrepareReferences()
    {
        if (referencesValid)
            return true;

        EnsureDefaults();
        if (!ValidateReferences())
            return false;

        referencesValid = true;
        spawnProtectionCenter = boatTransform != null ? (Vector2)boatTransform.position : Vector2.zero;
        return true;
    }

    void EnsureDefaults()
    {
        if (islandGenerationController == null)
            islandGenerationController = FindAnyObjectByType<IslandGenerationController>();

        if (worldSettings == null && islandGenerationController != null)
            worldSettings = islandGenerationController.WorldSettings;

        if (boatTransform == null && islandGenerationController != null)
            boatTransform = islandGenerationController.BoatTransform;

        if (worldCamera == null && islandGenerationController != null)
            worldCamera = islandGenerationController.GetWorldCamera();
    }

    bool ValidateReferences()
    {
        if (islandGenerationController == null)
        {
            Debug.LogWarning("[RockGenerationController] Missing IslandGenerationController reference.", this);
            return false;
        }

        if (worldSettings == null)
        {
            Debug.LogWarning("[RockGenerationController] Missing WorldGenerationSettings reference.", this);
            return false;
        }

        if (islandGenerationController.ChunkSize <= 0)
        {
            Debug.LogWarning("[RockGenerationController] Island chunk size is invalid.", this);
            return false;
        }

        minRocksPerCluster = Mathf.Max(1, minRocksPerCluster);
        maxRocksPerCluster = Mathf.Max(minRocksPerCluster, maxRocksPerCluster);
        if (randomRotationMaxDegrees < randomRotationMinDegrees)
            randomRotationMaxDegrees = randomRotationMinDegrees;
        return true;
    }

    protected override void CollectRequiredChunks(HashSet<Vector2Int> into)
    {
        Camera cameraToUse = ResolveCamera();
        if (cameraToUse == null)
            return;

        RectInt requiredChunkRect = islandGenerationController.GetRequiredChunkRectForCamera(cameraToUse);
        debugRequiredChunkRect = requiredChunkRect;
        AddRectChunks(requiredChunkRect, into);
    }

    protected override void TickLoadedChunks()
    {
        UpdateDebugCounts();
    }

    Camera ResolveCamera()
    {
        if (worldCamera != null)
            return worldCamera;

        return islandGenerationController != null ? islandGenerationController.GetWorldCamera() : Camera.main;
    }

    protected override void UnloadChunk(Vector2Int chunkCoord, ChunkRockData chunkData)
    {
        if (chunkData != null && chunkData.root != null)
            Destroy(chunkData.root);
    }

    protected override ChunkRockData GenerateChunk(Vector2Int chunkCoord)
    {
        RectInt chunkRect = islandGenerationController.GetChunkRectForCoord(chunkCoord);
        islandGenerationController.CollectIslandSourcesForChunk(chunkRect, chunkIslandSources);

        ChunkRockData chunkData = new ChunkRockData();
        for (int i = 0; i < chunkIslandSources.Count; i++)
        {
            IslandGenerationController.IslandSourceDescriptor source = chunkIslandSources[i];
            if (islandGenerationController.GetChunkCoordForWorldPosition(source.center) != chunkCoord)
                continue;

            GenerateRocksForIsland(source, chunkCoord, chunkData);
        }

        return chunkData;
    }

    void GenerateRocksForIsland(IslandGenerationController.IslandSourceDescriptor island, Vector2Int chunkCoord, ChunkRockData chunkData)
    {
        if (!ShouldIslandGenerateRocks(island))
            return;

        HazardLayoutType layout = SelectLayoutForIsland(island);
        int clusterCount = DetermineClusterCount(island, layout);
        if (clusterCount <= 0)
            return;

        List<PlacedRock> placedRocks = new List<PlacedRock>();
        float safeArcCenter = Hash01(island.deterministicKey.x, island.deterministicKey.y, 911 + rockCountSeedOffset) * 360f;
        float hazardCenter = GetHazardCenterDegrees(island, safeArcCenter);

        for (int clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
        {
            if (!TryGetClusterPlacementPlan(island, layout, clusterCount, clusterIndex, safeArcCenter, hazardCenter, out ClusterPlacementPlan plan))
                continue;

            int rocksInCluster = DetermineClusterRockCount(island, clusterIndex);

            for (int rockIndex = 0; rockIndex < rocksInCluster; rockIndex++)
            {
                RockSizeClass desiredSizeClass = DetermineRockSizeClassForCluster(island, clusterIndex, rockIndex);
                GameObject prefab = PickRockPrefab(desiredSizeClass, island, clusterIndex, rockIndex);
                if (prefab == null)
                    continue;

                if (!TryGetPrefabFootprintRadius(prefab, out float footprintRadius))
                    continue;

                Vector2 rockPosition = DetermineRockPosition(plan.anchor, plan.outward, plan.clusterRadius, island, clusterIndex, rockIndex);
                if (rockIndex == 0)
                    rockPosition = plan.anchor;

                if (!IsOutsideSpawnProtection(rockPosition, footprintRadius))
                    continue;

                if (!RespectsSafeApproachBuffer(island, safeArcCenter, rockPosition, footprintRadius))
                    continue;

                if (!CanPlaceRock(placedRocks, rockPosition, footprintRadius))
                    continue;

                GameObject instance = InstantiateRock(prefab, rockPosition, chunkCoord, island, clusterIndex, rockIndex, desiredSizeClass, footprintRadius, chunkData);
                if (instance == null)
                    continue;

                placedRocks.Add(new PlacedRock
                {
                    position = rockPosition,
                    footprintRadius = footprintRadius
                });
            }
        }

        TryGenerateOffshoreSatelliteCluster(island, layout, chunkCoord, chunkData, safeArcCenter, hazardCenter, placedRocks);
    }

    bool ShouldIslandGenerateRocks(IslandGenerationController.IslandSourceDescriptor island)
    {
        float sizeT = Mathf.Clamp01((island.maxRadius - 9f) / 6f);
        float radialBoost = island.normalizedRadius * outerHazardChanceBoost;
        float chance = Mathf.Lerp(rockIslandChanceMin, rockIslandChanceMax, sizeT);
        chance = Mathf.Clamp01(chance + radialBoost);

        float roll = Hash01(island.deterministicKey.x, island.deterministicKey.y, 401 + rockCountSeedOffset);
        return roll <= chance;
    }

    HazardLayoutType SelectLayoutForIsland(IslandGenerationController.IslandSourceDescriptor island)
    {
        float sizeT = Mathf.Clamp01((island.maxRadius - 9f) / 6f);
        float radialT = Mathf.Clamp01(island.normalizedRadius);

        float channelWeight = Mathf.Lerp(0f, channelLayoutWeightMedium, Mathf.Clamp01(sizeT * 2f));
        channelWeight = Mathf.Lerp(channelWeight, channelLayoutWeightLarge, sizeT);
        channelWeight += radialT * outerChannelWeightBoost;

        float wrappedWeight = Mathf.Lerp(0f, wrappedLayoutWeightMedium, Mathf.Clamp01(sizeT * 2f));
        wrappedWeight = Mathf.Lerp(wrappedWeight, wrappedLayoutWeightLarge, sizeT);
        wrappedWeight += radialT * outerWrappedWeightBoost;

        channelWeight = Mathf.Clamp01(channelWeight);
        wrappedWeight = Mathf.Clamp01(wrappedWeight);
        float singleSideWeight = Mathf.Max(0.1f, 1f - Mathf.Max(channelWeight, wrappedWeight) * 0.7f);

        float totalWeight = singleSideWeight + channelWeight + wrappedWeight;
        float roll = Hash01(island.deterministicKey.x, island.deterministicKey.y, 463 + rockCountSeedOffset) * totalWeight;

        if (roll < channelWeight)
            return HazardLayoutType.Channel;

        roll -= channelWeight;
        if (roll < wrappedWeight)
            return HazardLayoutType.Wrapped;

        return HazardLayoutType.SingleSide;
    }

    int DetermineClusterCount(IslandGenerationController.IslandSourceDescriptor island, HazardLayoutType layout)
    {
        int maxClusters = island.maxRadius >= 13.25f
            ? maxClustersLargeIsland
            : island.maxRadius >= 10.75f
                ? maxClustersMediumIsland
                : maxClustersSmallIsland;

        maxClusters = Mathf.Max(0, maxClusters);
        if (maxClusters == 0)
            return 0;

        int clusterCount = 0;
        for (int i = 0; i < maxClusters; i++)
        {
            float progressiveChance = Mathf.Lerp(0.92f, 0.35f, maxClusters <= 1 ? 0f : i / (float)(maxClusters - 1));
            progressiveChance = Mathf.Clamp01(progressiveChance + island.normalizedRadius * 0.08f);
            float roll = Hash01(island.deterministicKey.x, island.deterministicKey.y, 521 + i * 31 + rockCountSeedOffset);
            if (roll <= progressiveChance)
                clusterCount++;
        }

        if (layout == HazardLayoutType.Channel && island.maxRadius >= 10.75f)
            clusterCount = Mathf.Max(clusterCount, 2);

        if (layout == HazardLayoutType.Wrapped && island.maxRadius >= 13.25f)
            clusterCount = Mathf.Max(clusterCount, 3);

        return clusterCount;
    }

    float GetHazardCenterDegrees(IslandGenerationController.IslandSourceDescriptor island, float safeArcCenter)
    {
        return Mathf.Repeat(
            safeArcCenter + 150f + SignedHash01(island.deterministicKey.x, island.deterministicKey.y, 947 + rockCountSeedOffset) * 42f,
            360f);
    }

    bool TryGetClusterPlacementPlan(
        IslandGenerationController.IslandSourceDescriptor island,
        HazardLayoutType layout,
        int clusterCount,
        int clusterIndex,
        float safeArcCenter,
        float hazardCenter,
        out ClusterPlacementPlan plan)
    {
        plan = default;
        float clusterAngleDegrees = ResolveClusterAngleDegrees(island, layout, clusterCount, clusterIndex, safeArcCenter, hazardCenter);

        if (AngleDeltaDegrees(clusterAngleDegrees, safeArcCenter) <= safeApproachArcDegrees * 0.5f)
            clusterAngleDegrees = Mathf.Repeat(safeArcCenter + safeApproachArcDegrees * 0.5f + 18f + clusterIndex * 14f, 360f);

        GetEllipseEdgePoint(island, clusterAngleDegrees, out Vector2 shorelinePoint, out Vector2 outward);
        float offset = Mathf.Lerp(
            -clusterShoreEmbedDistance,
            clusterOffshoreDistance,
            Hash01(island.deterministicKey.x, island.deterministicKey.y, 673 + clusterIndex * 23 + rockCountSeedOffset));
        plan.anchor = shorelinePoint + outward * offset;
        plan.clusterAngleDegrees = clusterAngleDegrees;
        plan.outward = outward;
        plan.clusterRadius = ResolveClusterRadius(island, layout, clusterIndex);
        return true;
    }

    float ResolveClusterAngleDegrees(
        IslandGenerationController.IslandSourceDescriptor island,
        HazardLayoutType layout,
        int clusterCount,
        int clusterIndex,
        float safeArcCenter,
        float hazardCenter)
    {
        float jitter = SignedHash01(island.deterministicKey.x, island.deterministicKey.y, 631 + clusterIndex * 17 + rockCountSeedOffset) * 10f;

        if (layout == HazardLayoutType.Channel)
        {
            float laneHalf = safeApproachArcDegrees * 0.5f;
            bool useLeftFlank = clusterCount <= 1 || clusterIndex % 2 == 0;
            float flankSign = useLeftFlank ? -1f : 1f;
            float pairIndex = clusterIndex / 2f;
            float outwardBias = pairIndex * 12f;
            return Mathf.Repeat(safeArcCenter + flankSign * (laneHalf + channelLaneOffsetDegrees + outwardBias) + jitter, 360f);
        }

        if (layout == HazardLayoutType.Wrapped)
        {
            float[] wrappedAngles = new[]
            {
                Mathf.Repeat(hazardCenter - wrappedSideOffsetDegrees, 360f),
                Mathf.Repeat(hazardCenter, 360f),
                Mathf.Repeat(hazardCenter + wrappedSideOffsetDegrees, 360f),
                Mathf.Repeat(hazardCenter + 180f, 360f)
            };
            return Mathf.Repeat(wrappedAngles[clusterIndex % wrappedAngles.Length] + jitter, 360f);
        }

        float t = clusterCount <= 1 ? 0.5f : clusterIndex / (float)(clusterCount - 1);
        float sectorHalfWidth = hazardSectorArcDegrees * 0.5f;
        float baseAngle = Mathf.Lerp(hazardCenter - sectorHalfWidth, hazardCenter + sectorHalfWidth, t);
        return Mathf.Repeat(baseAngle + jitter, 360f);
    }

    float ResolveClusterRadius(IslandGenerationController.IslandSourceDescriptor island, HazardLayoutType layout, int clusterIndex)
    {
        float baseRadius = Mathf.Lerp(clusterRadiusMin, clusterRadiusMax, Mathf.Clamp01((island.maxRadius - 9f) / 6f));
        if (layout == HazardLayoutType.Channel)
            return baseRadius * 0.88f;
        if (layout == HazardLayoutType.Wrapped)
            return baseRadius * Mathf.Lerp(0.8f, 1.08f, Mathf.Clamp01(clusterIndex / 2f));

        return baseRadius;
    }

    int DetermineClusterRockCount(IslandGenerationController.IslandSourceDescriptor island, int clusterIndex)
    {
        float sizeT = Mathf.Clamp01((island.maxRadius - 9f) / 6f);
        float scaledMax = Mathf.Lerp(minRocksPerCluster + 1, maxRocksPerCluster, sizeT);
        int resolvedMax = Mathf.Max(minRocksPerCluster, Mathf.RoundToInt(scaledMax));
        float roll = Hash01(island.deterministicKey.x, island.deterministicKey.y, 727 + clusterIndex * 29 + rockCountSeedOffset);
        return Mathf.Clamp(minRocksPerCluster + Mathf.FloorToInt(roll * (resolvedMax - minRocksPerCluster + 1)), minRocksPerCluster, resolvedMax);
    }

    void TryGenerateOffshoreSatelliteCluster(
        IslandGenerationController.IslandSourceDescriptor island,
        HazardLayoutType layout,
        Vector2Int chunkCoord,
        ChunkRockData chunkData,
        float safeArcCenter,
        float hazardCenter,
        List<PlacedRock> placedRocks)
    {
        if (island.maxRadius < 10.75f)
            return;

        float chance = island.maxRadius >= 13.25f ? offshoreSatelliteChanceLarge : offshoreSatelliteChanceMedium;
        chance += island.normalizedRadius * offshoreSatelliteChanceOuterBoost;
        chance = Mathf.Clamp01(chance);

        float roll = Hash01(island.deterministicKey.x, island.deterministicKey.y, 1181 + rockCountSeedOffset);
        if (roll > chance)
            return;

        float safeHalf = safeApproachArcDegrees * 0.5f;
        float satelliteAngle = layout == HazardLayoutType.Channel
            ? Mathf.Repeat(safeArcCenter + (Hash01(island.deterministicKey.x, island.deterministicKey.y, 1213 + rockCountSeedOffset) < 0.5f ? -1f : 1f) * (safeHalf + channelLaneOffsetDegrees + 28f), 360f)
            : Mathf.Repeat(hazardCenter + SignedHash01(island.deterministicKey.x, island.deterministicKey.y, 1237 + rockCountSeedOffset) * 32f, 360f);

        GetEllipseEdgePoint(island, satelliteAngle, out Vector2 shorelinePoint, out Vector2 outward);
        float offshoreDistance = Mathf.Lerp(
            offshoreSatelliteDistanceMin,
            offshoreSatelliteDistanceMax,
            Hash01(island.deterministicKey.x, island.deterministicKey.y, 1261 + rockCountSeedOffset));
        Vector2 anchor = shorelinePoint + outward * offshoreDistance;
        int rocksInCluster = Mathf.Clamp(
            1 + Mathf.FloorToInt(Hash01(island.deterministicKey.x, island.deterministicKey.y, 1291 + rockCountSeedOffset) * 3f),
            1,
            3);

        for (int rockIndex = 0; rockIndex < rocksInCluster; rockIndex++)
        {
            RockSizeClass desiredSizeClass = rockIndex == 0 && island.maxRadius >= 13.25f && HasAnyPrefabForSizeClass(RockSizeClass.Medium)
                ? RockSizeClass.Medium
                : RockSizeClass.Small;
            GameObject prefab = PickRockPrefab(desiredSizeClass, island, 100 + chunkCoord.x, rockIndex + 200);
            if (prefab == null || !TryGetPrefabFootprintRadius(prefab, out float footprintRadius))
                continue;

            Vector2 rockPosition = DetermineRockPosition(anchor, outward, offshoreSatelliteClusterRadius, island, 100 + chunkCoord.y, rockIndex + 200);
            if (rockIndex == 0)
                rockPosition = anchor;

            if (!IsOutsideSpawnProtection(rockPosition, footprintRadius))
                continue;

            if (!RespectsSafeApproachBuffer(island, safeArcCenter, rockPosition, footprintRadius))
                continue;

            if (!CanPlaceRock(placedRocks, rockPosition, footprintRadius))
                continue;

            GameObject instance = InstantiateRock(prefab, rockPosition, chunkCoord, island, 100, rockIndex, desiredSizeClass, footprintRadius, chunkData);
            if (instance == null)
                continue;

            placedRocks.Add(new PlacedRock
            {
                position = rockPosition,
                footprintRadius = footprintRadius
            });
        }
    }

    RockSizeClass DetermineRockSizeClassForCluster(IslandGenerationController.IslandSourceDescriptor island, int clusterIndex, int rockIndex)
    {
        float roll = Hash01(island.deterministicKey.x, island.deterministicKey.y, 809 + clusterIndex * 37 + rockIndex * 11 + rockCountSeedOffset);

        if (rockIndex == 0)
        {
            if (roll <= largeAnchorChance && HasAnyPrefabForSizeClass(RockSizeClass.Large))
                return RockSizeClass.Large;

            if (roll <= largeAnchorChance + mediumAnchorChance && HasAnyPrefabForSizeClass(RockSizeClass.Medium))
                return RockSizeClass.Medium;

            return RockSizeClass.Small;
        }

        if (roll <= mediumSatelliteChance && HasAnyPrefabForSizeClass(RockSizeClass.Medium))
            return RockSizeClass.Medium;

        return RockSizeClass.Small;
    }

    GameObject PickRockPrefab(RockSizeClass desiredSizeClass, IslandGenerationController.IslandSourceDescriptor island, int clusterIndex, int rockIndex)
    {
        if (TryPickPrefabFromSizeClass(desiredSizeClass, island, clusterIndex, rockIndex, out GameObject prefab))
            return prefab;

        if (desiredSizeClass == RockSizeClass.Large && TryPickPrefabFromSizeClass(RockSizeClass.Medium, island, clusterIndex, rockIndex + 101, out prefab))
            return prefab;

        if (TryPickPrefabFromSizeClass(RockSizeClass.Small, island, clusterIndex, rockIndex + 211, out prefab))
            return prefab;

        if (TryPickPrefabFromSizeClass(RockSizeClass.Medium, island, clusterIndex, rockIndex + 307, out prefab))
            return prefab;

        if (TryPickPrefabFromSizeClass(RockSizeClass.Large, island, clusterIndex, rockIndex + 401, out prefab))
            return prefab;

        return null;
    }

    bool TryPickPrefabFromSizeClass(RockSizeClass sizeClass, IslandGenerationController.IslandSourceDescriptor island, int clusterIndex, int rockIndex, out GameObject prefab)
    {
        prefab = null;
        GameObject[] prefabs = GetPrefabsForSizeClass(sizeClass);
        if (prefabs == null || prefabs.Length == 0)
            return false;

        int startIndex = Mathf.FloorToInt(Hash01(
            island.deterministicKey.x,
            island.deterministicKey.y,
            887 + clusterIndex * 19 + rockIndex * 13 + (int)sizeClass * 97 + rockCountSeedOffset) * prefabs.Length);

        for (int i = 0; i < prefabs.Length; i++)
        {
            GameObject candidate = prefabs[(startIndex + i) % prefabs.Length];
            if (candidate == null)
                continue;

            if (!TryGetPrefabFootprintRadius(candidate, out _))
                continue;

            prefab = candidate;
            return true;
        }

        return false;
    }

    GameObject[] GetPrefabsForSizeClass(RockSizeClass sizeClass)
    {
        return sizeClass switch
        {
            RockSizeClass.Small => smallRocks,
            RockSizeClass.Medium => mediumRocks,
            RockSizeClass.Large => largeRocks,
            _ => smallRocks
        };
    }

    bool HasAnyPrefabForSizeClass(RockSizeClass sizeClass)
    {
        GameObject[] prefabs = GetPrefabsForSizeClass(sizeClass);
        return prefabs != null && prefabs.Length > 0;
    }

    bool TryGetPrefabFootprintRadius(GameObject prefab, out float radius)
    {
        radius = 0.5f;
        if (prefab == null)
            return false;

        if (prefabFootprintRadiusCache.TryGetValue(prefab, out float cachedRadius))
        {
            radius = cachedRadius;
            return true;
        }

        Collider2D collider = prefab.GetComponentInChildren<Collider2D>(true);
        if (collider == null)
        {
            WarnMissingCollider(prefab);
            return false;
        }

        float scaleX = Mathf.Abs(collider.transform.lossyScale.x);
        float scaleY = Mathf.Abs(collider.transform.lossyScale.y);
        float effectiveRadius = 0.5f;

        if (collider is BoxCollider2D box)
        {
            effectiveRadius = Mathf.Max(box.size.x * scaleX, box.size.y * scaleY) * 0.5f;
        }
        else if (collider is CircleCollider2D circle)
        {
            effectiveRadius = circle.radius * Mathf.Max(scaleX, scaleY);
        }
        else if (collider is CapsuleCollider2D capsule)
        {
            effectiveRadius = Mathf.Max(capsule.size.x * scaleX, capsule.size.y * scaleY) * 0.5f;
        }
        else
        {
            Bounds bounds = collider.bounds;
            effectiveRadius = Mathf.Max(bounds.extents.x, bounds.extents.y);
        }

        effectiveRadius = Mathf.Max(0.05f, effectiveRadius);
        prefabFootprintRadiusCache[prefab] = effectiveRadius;
        radius = effectiveRadius;
        return true;
    }

    void WarnMissingCollider(GameObject prefab)
    {
        int id = RuntimeHelpers.GetHashCode(prefab);
        if (warnedMissingColliderPrefabs.Contains(id))
            return;

        warnedMissingColliderPrefabs.Add(id);
        Debug.LogWarning($"[RockGenerationController] Rock prefab '{prefab.name}' is missing a Collider2D and will be skipped.", this);
    }

    void WarnMissingWaterObstacle(GameObject prefab)
    {
        int id = RuntimeHelpers.GetHashCode(prefab);
        if (warnedMissingObstaclePrefabs.Contains(id))
            return;

        warnedMissingObstaclePrefabs.Add(id);
        Debug.LogWarning($"[RockGenerationController] Rock prefab '{prefab.name}' is missing WaterObstacle, so it will not affect ripple flow until you add that component.", this);
    }

    Vector2 DetermineRockPosition(
        Vector2 clusterAnchor,
        Vector2 clusterOutward,
        float clusterRadius,
        IslandGenerationController.IslandSourceDescriptor island,
        int clusterIndex,
        int rockIndex)
    {
        float angle = Hash01(island.deterministicKey.x, island.deterministicKey.y, 991 + clusterIndex * 43 + rockIndex * 17 + rockCountSeedOffset) * Mathf.PI * 2f;
        float radius = Mathf.Sqrt(Hash01(island.deterministicKey.x, island.deterministicKey.y, 1049 + clusterIndex * 47 + rockIndex * 19 + rockCountSeedOffset)) * clusterRadius;
        Vector2 tangent = new Vector2(-clusterOutward.y, clusterOutward.x);
        Vector2 offset = clusterOutward * (Mathf.Sin(angle) * radius) + tangent * (Mathf.Cos(angle) * radius);
        return clusterAnchor + offset;
    }

    bool IsOutsideSpawnProtection(Vector2 position, float footprintRadius)
    {
        float safeDistance = islandGenerationController.ProtectedSpawnRadiusTiles + footprintRadius;
        return Vector2.Distance(position, spawnProtectionCenter) > safeDistance;
    }

    bool RespectsSafeApproachBuffer(
        IslandGenerationController.IslandSourceDescriptor island,
        float safeArcCenter,
        Vector2 rockPosition,
        float footprintRadius)
    {
        Vector2 local = Quaternion.Euler(0f, 0f, -island.rotationDegrees) * (rockPosition - island.center);
        float angle = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
        if (angle < 0f)
            angle += 360f;

        if (AngleDeltaDegrees(angle, safeArcCenter) > safeApproachArcDegrees * 0.5f)
            return true;

        GetEllipseEdgePoint(island, angle, out Vector2 shorelinePoint, out _);
        return Vector2.Distance(rockPosition, shorelinePoint) > landingBufferExtraDistance + footprintRadius;
    }

    bool CanPlaceRock(List<PlacedRock> placedRocks, Vector2 position, float footprintRadius)
    {
        for (int i = 0; i < placedRocks.Count; i++)
        {
            float minSpacing = placedRocks[i].footprintRadius + footprintRadius;
            if ((placedRocks[i].position - position).sqrMagnitude < minSpacing * minSpacing)
                return false;
        }

        return true;
    }

    GameObject InstantiateRock(
        GameObject prefab,
        Vector2 position,
        Vector2Int chunkCoord,
        IslandGenerationController.IslandSourceDescriptor island,
        int clusterIndex,
        int rockIndex,
        RockSizeClass sizeClass,
        float footprintRadius,
        ChunkRockData chunkData)
    {
        if (prefab == null)
            return null;

        if (chunkData.root == null)
            chunkData.root = new GameObject($"RockChunk_{chunkCoord.x}_{chunkCoord.y}");

        chunkData.root.transform.SetParent(transform, false);

        float rotationT = Hash01(
            island.deterministicKey.x,
            island.deterministicKey.y,
            1129 + clusterIndex * 59 + rockIndex * 23 + rockCountSeedOffset);
        float rotationDegrees = Mathf.Lerp(randomRotationMinDegrees, randomRotationMaxDegrees, rotationT);

        GameObject instance = Instantiate(
            prefab,
            new Vector3(position.x, position.y, prefab.transform.position.z),
            Quaternion.Euler(0f, 0f, rotationDegrees),
            chunkData.root.transform);

        EnsureRockReceivesDayNightTint(instance);

        if (instance.GetComponentInChildren<WaterObstacle>(true) == null)
            WarnMissingWaterObstacle(prefab);

        if (configureVisibleIdleRipples)
            ConfigureRockWaterLapping(instance, sizeClass, footprintRadius, prefab);

        chunkData.instances.Add(instance);
        return instance;
    }

    void EnsureRockReceivesDayNightTint(GameObject instance)
    {
        if (instance == null)
            return;

        if (instance.GetComponentInChildren<SpriteRenderer>(true) == null)
            return;

        DayNightTintGroup tintGroup = instance.GetComponent<DayNightTintGroup>();
        if (tintGroup == null)
            instance.AddComponent<DayNightTintGroup>();
    }

    void ConfigureRockWaterLapping(GameObject instance, RockSizeClass sizeClass, float footprintRadius, GameObject prefab)
    {
        if (instance == null)
            return;

        WaterDisturbanceSource disturbanceSource = instance.GetComponentInChildren<WaterDisturbanceSource>(true);
        if (disturbanceSource == null)
            return;

        disturbanceSource.ConfigureIdleLappingForStaticObstacle(
            footprintRadius,
            GetRippleStrengthForSizeClass(sizeClass),
            GetRippleSizeMultiplierForSizeClass(sizeClass));
    }

    float GetRippleStrengthForSizeClass(RockSizeClass sizeClass)
    {
        return sizeClass switch
        {
            RockSizeClass.Small => smallRockRippleStrength,
            RockSizeClass.Medium => mediumRockRippleStrength,
            RockSizeClass.Large => largeRockRippleStrength,
            _ => mediumRockRippleStrength
        };
    }

    float GetRippleSizeMultiplierForSizeClass(RockSizeClass sizeClass)
    {
        return sizeClass switch
        {
            RockSizeClass.Small => smallRockRippleSizeMultiplier,
            RockSizeClass.Medium => mediumRockRippleSizeMultiplier,
            RockSizeClass.Large => largeRockRippleSizeMultiplier,
            _ => mediumRockRippleSizeMultiplier
        };
    }

    void GetEllipseEdgePoint(
        IslandGenerationController.IslandSourceDescriptor island,
        float angleDegrees,
        out Vector2 worldPoint,
        out Vector2 outward)
    {
        float angleRadians = angleDegrees * Mathf.Deg2Rad;
        Vector2 localPoint = new Vector2(
            Mathf.Cos(angleRadians) * island.radii.x,
            Mathf.Sin(angleRadians) * island.radii.y);

        Vector2 gradient = new Vector2(
            localPoint.x / Mathf.Max(island.radii.x * island.radii.x, 0.0001f),
            localPoint.y / Mathf.Max(island.radii.y * island.radii.y, 0.0001f));
        if (gradient.sqrMagnitude < 0.0001f)
            gradient = localPoint.sqrMagnitude > 0.0001f ? localPoint.normalized : Vector2.up;

        Quaternion rotation = Quaternion.Euler(0f, 0f, island.rotationDegrees);
        worldPoint = island.center + (Vector2)(rotation * localPoint);
        outward = ((Vector2)(rotation * gradient)).normalized;
    }

    void UpdateDebugCounts()
    {
        debugLoadedChunkCount = LoadedChunkCount;

        int spawnedCount = 0;
        foreach (KeyValuePair<Vector2Int, ChunkRockData> pair in LoadedChunks)
            spawnedCount += pair.Value.instances.Count;

        debugSpawnedRockCount = spawnedCount;
    }

    static float AngleDeltaDegrees(float a, float b)
    {
        return Mathf.Abs(Mathf.DeltaAngle(a, b));
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

    static float SignedHash01(int x, int y, int localSeed)
    {
        return Hash01(x, y, localSeed) * 2f - 1f;
    }
}
