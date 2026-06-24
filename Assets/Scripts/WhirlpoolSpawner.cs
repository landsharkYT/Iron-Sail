using System.Collections.Generic;
using UnityEngine;

// Deterministically places Whirlpools in the water gaps between nearby islands
// (tidal-race straits), mirroring RockGenerationController's chunk lifecycle.
// Whirlpools are World Features reproduced from the World Seed, so they are never
// saved (see ADR 0005). Open-ocean spawning is intentionally omitted for v1.
// Per chunk it owns a root GameObject plus the count of each size it spawned, so
// the loaded Medium/Large totals can be tracked as chunks stream in and out.
[DisallowMultipleComponent]
public class WhirlpoolSpawner : WorldChunkSpawner<WhirlpoolSpawner.ChunkWhirlpools>
{
    // Public because it is the WorldChunkSpawner<TChunk> payload type (a public
    // base-type argument must be at least as accessible).
    public class ChunkWhirlpools
    {
        public GameObject root;
        public int mediumCount;
        public int largeCount;
    }

    [Header("References")]
    [SerializeField] IslandGenerationController islandGenerationController;
    [SerializeField] Transform boatTransform;
    [SerializeField] Camera worldCamera;
    [SerializeField] Transform whirlpoolRoot;
    [SerializeField] WhirlpoolController mediumPrefab;
    [SerializeField] WhirlpoolController largePrefab;

    [Header("Strait Qualification (world tiles)")]
    [SerializeField, Min(1f)] float minStraitGap = 14f;
    [SerializeField, Min(2f)] float maxStraitGap = 40f;

    [Header("Chances")]
    [SerializeField, Range(0f, 1f)] float baseStraitChance = 0.18f;
    // Probability a spawned whirlpool is Large rather than Medium (flat 50/50).
    [SerializeField, Range(0f, 1f)] float largeWhirlpoolChance = 0.5f;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] int debugMediumWhirlpoolCount;
    [SerializeField] int debugLargeWhirlpoolCount;

    readonly List<IslandGenerationController.IslandSourceDescriptor> chunkIslandSources = new List<IslandGenerationController.IslandSourceDescriptor>();
    readonly List<IslandGenerationController.IslandSourceDescriptor> neighborScratch = new List<IslandGenerationController.IslandSourceDescriptor>();

    bool referencesValid;

    public int MediumWhirlpoolCount => debugMediumWhirlpoolCount;
    public int LargeWhirlpoolCount => debugLargeWhirlpoolCount;

    // --- Diagnostic seam: pure strait geometry (unit-testable) ----------------

    // A pair of islands forms a strait when their edge gap is within the band. The
    // whirlpool sits at the midpoint of that gap on the centre-to-centre line.
    public static bool TryEvaluateStrait(
        Vector2 centerA, float radiusA, Vector2 centerB, float radiusB,
        float minGap, float maxGap, out Vector2 midpoint, out float gap)
    {
        midpoint = Vector2.zero;
        Vector2 delta = centerB - centerA;
        float dist = delta.magnitude;
        gap = dist - radiusA - radiusB;

        if (dist <= 0.0001f || gap < minGap || gap > maxGap)
            return false;

        Vector2 dir = delta / dist;
        midpoint = centerA + dir * (radiusA + gap * 0.5f);
        return true;
    }

    protected override bool PrepareReferences()
    {
        if (referencesValid)
            return true;

        EnsureDefaults();
        referencesValid = ValidateReferences();
        return referencesValid;
    }

    void EnsureDefaults()
    {
        if (islandGenerationController == null)
            islandGenerationController = FindAnyObjectByType<IslandGenerationController>();

        if (boatTransform == null && islandGenerationController != null)
            boatTransform = islandGenerationController.BoatTransform;

        if (worldCamera == null && islandGenerationController != null)
            worldCamera = islandGenerationController.GetWorldCamera();

        if (whirlpoolRoot == null)
            whirlpoolRoot = transform;
    }

    bool ValidateReferences()
    {
        if (islandGenerationController == null || islandGenerationController.ChunkSize <= 0)
        {
            Debug.LogWarning("[WhirlpoolSpawner] Missing or invalid IslandGenerationController.", this);
            return false;
        }

        if (mediumPrefab == null || largePrefab == null)
        {
            Debug.LogWarning("[WhirlpoolSpawner] Whirlpool prefabs are not assigned.", this);
            return false;
        }

        maxStraitGap = Mathf.Max(maxStraitGap, minStraitGap + 1f);
        return true;
    }

    protected override void CollectRequiredChunks(HashSet<Vector2Int> into)
    {
        Camera cameraToUse = worldCamera != null ? worldCamera : islandGenerationController.GetWorldCamera();
        if (cameraToUse == null)
            return;

        AddRectChunks(islandGenerationController.GetRequiredChunkRectForCamera(cameraToUse), into);
    }

    protected override void UnloadChunk(Vector2Int coord, ChunkWhirlpools chunk)
    {
        if (chunk == null)
            return;

        // Keep the loaded totals in step as this chunk's whirlpools go away.
        debugMediumWhirlpoolCount = Mathf.Max(0, debugMediumWhirlpoolCount - chunk.mediumCount);
        debugLargeWhirlpoolCount = Mathf.Max(0, debugLargeWhirlpoolCount - chunk.largeCount);

        if (chunk.root != null)
            Destroy(chunk.root);
    }

    protected override ChunkWhirlpools GenerateChunk(Vector2Int chunkCoord)
    {
        RectInt chunkRect = islandGenerationController.GetChunkRectForCoord(chunkCoord);
        islandGenerationController.CollectIslandSourcesForChunk(chunkRect, chunkIslandSources);

        ChunkWhirlpools chunk = new ChunkWhirlpools();
        int worldSeed = islandGenerationController.Seed;
        float searchRadius = maxStraitGap * 2f + 64f;

        for (int i = 0; i < chunkIslandSources.Count; i++)
        {
            IslandGenerationController.IslandSourceDescriptor owner = chunkIslandSources[i];
            // Process each island in exactly one chunk (the one its centre falls in).
            if (islandGenerationController.GetChunkCoordForWorldPosition(owner.center) != chunkCoord)
                continue;

            islandGenerationController.CollectAcceptedIslandSourcesNearWorldPosition(owner.center, searchRadius, neighborScratch);
            for (int n = 0; n < neighborScratch.Count; n++)
            {
                IslandGenerationController.IslandSourceDescriptor neighbor = neighborScratch[n];
                // Canonical ordering so each strait pair is evaluated once only.
                if (!IsCanonicalPair(owner.deterministicKey, neighbor.deterministicKey))
                    continue;

                if (!TryEvaluateStrait(owner.center, owner.maxRadius, neighbor.center, neighbor.maxRadius,
                        minStraitGap, maxStraitGap, out Vector2 midpoint, out float gap))
                    continue;

                if (Hash01(worldSeed, owner.deterministicKey, neighbor.deterministicKey, 7301) >= StraitChance(gap))
                    continue;

                bool large = Hash01(worldSeed, owner.deterministicKey, neighbor.deterministicKey, 9173) < largeWhirlpoolChance;
                SpawnWhirlpool(large, midpoint, chunk);
            }
        }

        return chunk;
    }

    float StraitChance(float gap)
    {
        // Narrower gaps host whirlpools more often (a tighter race).
        float narrowness = Mathf.InverseLerp(maxStraitGap, minStraitGap, gap);
        return Mathf.Lerp(baseStraitChance * 0.5f, baseStraitChance, narrowness);
    }

    void SpawnWhirlpool(bool large, Vector2 position, ChunkWhirlpools chunk)
    {
        WhirlpoolController prefab = large ? largePrefab : mediumPrefab;
        if (prefab == null)
            return;

        if (chunk.root == null)
        {
            chunk.root = new GameObject("Whirlpool Chunk");
            chunk.root.transform.SetParent(whirlpoolRoot, false);
        }

        WhirlpoolController instance = Instantiate(prefab, new Vector3(position.x, position.y, 0f), Quaternion.identity, chunk.root.transform);
        instance.name = prefab.name;

        if (large)
        {
            chunk.largeCount++;
            debugLargeWhirlpoolCount++;
        }
        else
        {
            chunk.mediumCount++;
            debugMediumWhirlpoolCount++;
        }
    }

    static bool IsCanonicalPair(Vector2Int a, Vector2Int b)
    {
        if (a == b)
            return false;

        return a.x < b.x || (a.x == b.x && a.y < b.y);
    }

    static float Hash01(int worldSeed, Vector2Int a, Vector2Int b, int salt)
    {
        unchecked
        {
            int hash = worldSeed;
            hash = (hash * 397) ^ a.x;
            hash = (hash * 397) ^ a.y;
            hash = (hash * 397) ^ b.x;
            hash = (hash * 397) ^ b.y;
            hash = (hash * 397) ^ salt;
            return ((uint)hash & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
