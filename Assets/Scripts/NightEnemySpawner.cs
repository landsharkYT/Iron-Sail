using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent]
public class NightEnemySpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] DayNightController dayNightController;
    [SerializeField] Transform boatTransform;
    [SerializeField] BoatHealthController boatHealthController;
    [SerializeField] Tilemap islandTilemap;
    [SerializeField] NightEnemyController enemyPrefab;
    [SerializeField] NightEnemyConfig[] enemyConfigs;

    [Header("Spawn Ring")]
    [SerializeField] float minSpawnRadius = 10f;
    [SerializeField] float maxSpawnRadius = 22f;
    [SerializeField] [Range(0f, 1f)] float outerRingBias = 0.7f;
    [SerializeField] float minEnemySpacing = 2.5f;
    [SerializeField] int maxSpawnAttemptsPerTick = 12;

    [Header("Spawn Safety")]
    [SerializeField] string obstacleLayerName = "Island";
    [SerializeField] float minObstacleClearanceRadius = 1.25f;
    [SerializeField] float shorelineProbeInset = 0.6f;

    [Header("Night Pressure")]
    [SerializeField] float spawnIntervalAtNightStart = 7.5f;
    [SerializeField] float spawnIntervalAtNightEnd = 3.8f;
    [SerializeField] int activeCapAtNightStart = 4;
    [SerializeField] int activeCapAtNightEnd = 8;
    [SerializeField] float firstSpawnDelaySeconds = 0.8f;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] int debugActiveEnemyCount;
    [SerializeField] float debugNightProgress;
    [SerializeField] float debugCurrentSpawnInterval;
    [SerializeField] int debugCurrentActiveCap;

    readonly List<NightEnemyController> activeEnemies = new List<NightEnemyController>();

    float nextSpawnTime;
    int cachedObstacleLayer = -1;
    IslandGenerationController islandGenerationController;
    // Isolated RNG stream seeded from the World Seed so enemy spawns never draw
    // from the shared UnityEngine.Random and desync seeded world features.
    System.Random spawnRandom;

    void Awake()
    {
        ResolveReferences();
        cachedObstacleLayer = LayerMask.NameToLayer(obstacleLayerName);
    }

    void OnEnable()
    {
        ResolveReferences();
        cachedObstacleLayer = LayerMask.NameToLayer(obstacleLayerName);
        if (dayNightController != null)
            dayNightController.OnPhaseChanged += HandlePhaseChanged;
    }

    void OnDisable()
    {
        if (dayNightController != null)
            dayNightController.OnPhaseChanged -= HandlePhaseChanged;
    }

    void Start()
    {
        if (IsNightActive())
            nextSpawnTime = Time.time + Mathf.Max(0f, firstSpawnDelaySeconds);
    }

    void Update()
    {
        CleanupDestroyedEnemies();
        UpdateDebugMirrors();

        if (!IsNightActive() || !ReferencesReady())
            return;

        if (Time.time < nextSpawnTime)
            return;

        if (activeEnemies.Count < GetCurrentActiveCap())
            TrySpawnEnemy();

        ScheduleNextSpawn();
    }

    public void NotifyEnemyDestroyed(NightEnemyController enemy)
    {
        if (enemy == null)
            return;

        activeEnemies.Remove(enemy);
        debugActiveEnemyCount = activeEnemies.Count;
    }

    public bool HasActiveEnemyWithinRadius(Vector2 worldPosition, float radius)
    {
        float radiusSqr = radius * radius;
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            NightEnemyController activeEnemy = activeEnemies[i];
            if (activeEnemy == null)
                continue;

            if ((activeEnemy.Position - worldPosition).sqrMagnitude <= radiusSqr)
                return true;
        }

        return false;
    }

    void HandlePhaseChanged(DayNightPhase previousPhase, DayNightPhase nextPhase)
    {
        if (nextPhase == DayNightPhase.Night)
        {
            nextSpawnTime = Time.time + Mathf.Max(0f, firstSpawnDelaySeconds);
            return;
        }

        if (previousPhase == DayNightPhase.Night && nextPhase != DayNightPhase.Night)
            DespawnAllNightEnemies();
    }

    void TrySpawnEnemy()
    {
        if (enemyPrefab == null || enemyConfigs == null || enemyConfigs.Length == 0 || boatTransform == null)
            return;

        InfiniteWaterTileMap waterTileMap = InfiniteWaterTileMap.ActiveInstance;
        if (waterTileMap == null)
            return;

        NightEnemyConfig chosenConfig = ChooseEnemyConfig();
        if (chosenConfig == null)
            return;

        if (!TryFindSpawnPosition(waterTileMap, out Vector2 spawnPosition))
            return;

        NightEnemyController enemyInstance = Instantiate(enemyPrefab, spawnPosition, Quaternion.identity);
        enemyInstance.Initialize(chosenConfig, boatTransform, boatHealthController, this);
        activeEnemies.Add(enemyInstance);
        debugActiveEnemyCount = activeEnemies.Count;
    }

    NightEnemyConfig ChooseEnemyConfig()
    {
        float totalWeight = 0f;
        for (int i = 0; i < enemyConfigs.Length; i++)
        {
            NightEnemyConfig config = enemyConfigs[i];
            if (config == null)
                continue;

            totalWeight += Mathf.Max(0f, config.SpawnWeight);
        }

        if (totalWeight <= 0f)
            return null;

        float roll = NextRandomFloat() * totalWeight;
        for (int i = 0; i < enemyConfigs.Length; i++)
        {
            NightEnemyConfig config = enemyConfigs[i];
            if (config == null)
                continue;

            roll -= Mathf.Max(0f, config.SpawnWeight);
            if (roll <= 0f)
                return config;
        }

        return enemyConfigs[enemyConfigs.Length - 1];
    }

    bool TryFindSpawnPosition(InfiniteWaterTileMap waterTileMap, out Vector2 spawnPosition)
    {
        spawnPosition = Vector2.zero;

        Vector2 boatPosition = boatTransform.position;
        for (int attempt = 0; attempt < maxSpawnAttemptsPerTick; attempt++)
        {
            float angle = NextRandomFloat() * Mathf.PI * 2f;
            float uniformRadius = NextRandomFloat();
            float outerBiasedRadius = Mathf.Sqrt(NextRandomFloat());
            float radiusT = Mathf.Lerp(uniformRadius, outerBiasedRadius, outerRingBias);
            float radius = Mathf.Lerp(minSpawnRadius, maxSpawnRadius, radiusT);

            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            Vector2 candidate = boatPosition + offset;

            if (!waterTileMap.HasWaterTileAtWorldPosition(candidate))
                continue;
            if ((candidate - boatPosition).sqrMagnitude < minSpawnRadius * minSpawnRadius)
                continue;
            if (!HasObstacleClearance(candidate))
                continue;
            if (!HasEnoughEnemySpacing(candidate))
                continue;

            spawnPosition = candidate;
            return true;
        }

        return false;
    }

    bool HasEnoughEnemySpacing(Vector2 candidate)
    {
        float minimumSpacingSqr = minEnemySpacing * minEnemySpacing;
        for (int i = 0; i < activeEnemies.Count; i++)
        {
            NightEnemyController activeEnemy = activeEnemies[i];
            if (activeEnemy == null)
                continue;

            if ((activeEnemy.Position - candidate).sqrMagnitude < minimumSpacingSqr)
                return false;
        }

        return true;
    }

    bool HasObstacleClearance(Vector2 candidate)
    {
        if (minObstacleClearanceRadius <= 0f)
            return true;

        if (cachedObstacleLayer < 0)
            cachedObstacleLayer = LayerMask.NameToLayer(obstacleLayerName);

        if (cachedObstacleLayer >= 0)
        {
            int obstacleMask = 1 << cachedObstacleLayer;
            if (Physics2D.OverlapCircle(candidate, minObstacleClearanceRadius, obstacleMask) != null)
                return false;
        }

        if (islandTilemap == null)
            return true;

        float probeDistance = Mathf.Max(shorelineProbeInset, minObstacleClearanceRadius);
        Vector2[] probeOffsets =
        {
            Vector2.zero,
            Vector2.up * probeDistance,
            Vector2.down * probeDistance,
            Vector2.left * probeDistance,
            Vector2.right * probeDistance
        };

        for (int i = 0; i < probeOffsets.Length; i++)
        {
            if (islandTilemap.HasTile(islandTilemap.WorldToCell(candidate + probeOffsets[i])))
                return false;
        }

        return true;
    }

    void DespawnAllNightEnemies()
    {
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            NightEnemyController enemy = activeEnemies[i];
            if (enemy == null)
                continue;

            enemy.Despawn(playEffect: false);
        }

        activeEnemies.Clear();
        debugActiveEnemyCount = 0;
    }

    void ScheduleNextSpawn()
    {
        nextSpawnTime = Time.time + GetCurrentSpawnInterval();
    }

    float GetCurrentSpawnInterval()
    {
        float progress = GetNightProgress01();
        return Mathf.Lerp(spawnIntervalAtNightStart, spawnIntervalAtNightEnd, progress);
    }

    int GetCurrentActiveCap()
    {
        float progress = GetNightProgress01();
        return Mathf.RoundToInt(Mathf.Lerp(activeCapAtNightStart, activeCapAtNightEnd, progress));
    }

    float GetNightProgress01()
    {
        if (dayNightController == null || dayNightController.CurrentPhase != DayNightPhase.Night)
            return 0f;

        return Mathf.Clamp01(dayNightController.PhaseProgress);
    }

    bool IsNightActive()
    {
        return dayNightController != null && dayNightController.CurrentPhase == DayNightPhase.Night;
    }

    bool ReferencesReady()
    {
        return dayNightController != null &&
               boatTransform != null &&
               boatHealthController != null &&
               enemyPrefab != null &&
               enemyConfigs != null &&
               enemyConfigs.Length > 0;
    }

    void ResolveReferences()
    {
        if (dayNightController == null)
            dayNightController = FindAnyObjectByType<DayNightController>();

        if (boatHealthController == null)
            boatHealthController = FindAnyObjectByType<BoatHealthController>();

        if (boatTransform == null && boatHealthController != null)
            boatTransform = boatHealthController.transform;

        if (boatTransform == null)
        {
            BoatController boatController = FindAnyObjectByType<BoatController>();
            if (boatController != null)
                boatTransform = boatController.transform;
        }

        if (islandGenerationController == null)
            islandGenerationController = FindAnyObjectByType<IslandGenerationController>();

        if (islandTilemap == null && islandGenerationController != null)
            islandTilemap = islandGenerationController.IslandTilemap;
    }

    int ResolveWorldSeed()
    {
        if (islandGenerationController == null)
            islandGenerationController = FindAnyObjectByType<IslandGenerationController>();

        return islandGenerationController != null ? islandGenerationController.Seed : 0;
    }

    // Private RNG stream, lazily seeded from the World Seed.
    float NextRandomFloat()
    {
        if (spawnRandom == null)
            spawnRandom = new System.Random(ResolveWorldSeed());

        return (float)spawnRandom.NextDouble();
    }

    // --- Save / restore (Live Entities) -------------------------------------

    public IReadOnlyList<NightEnemyController> ActiveEnemies => activeEnemies;

    public void ClearActiveEnemies()
    {
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            if (activeEnemies[i] != null)
                Destroy(activeEnemies[i].gameObject);
        }

        activeEnemies.Clear();
        debugActiveEnemyCount = 0;
    }

    // Re-instantiates a saved enemy at its position and health. Returns false if
    // the saved type can no longer be resolved (e.g. a removed config).
    public bool RestoreEnemy(string configSaveId, Vector2 position, float health)
    {
        ResolveReferences();

        NightEnemyConfig config = ResolveConfig(configSaveId);
        if (config == null || enemyPrefab == null)
            return false;

        NightEnemyController enemy = Instantiate(enemyPrefab, position, Quaternion.identity);
        enemy.Initialize(config, boatTransform, boatHealthController, this);
        enemy.RestoreState(position, health);
        activeEnemies.Add(enemy);
        debugActiveEnemyCount = activeEnemies.Count;
        return true;
    }

    NightEnemyConfig ResolveConfig(string configSaveId)
    {
        if (string.IsNullOrEmpty(configSaveId) || enemyConfigs == null)
            return null;

        for (int i = 0; i < enemyConfigs.Length; i++)
        {
            if (enemyConfigs[i] != null && enemyConfigs[i].SaveId == configSaveId)
                return enemyConfigs[i];
        }

        return null;
    }

    void CleanupDestroyedEnemies()
    {
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            if (activeEnemies[i] == null)
                activeEnemies.RemoveAt(i);
        }
    }

    void UpdateDebugMirrors()
    {
        debugActiveEnemyCount = activeEnemies.Count;
        debugNightProgress = GetNightProgress01();
        debugCurrentSpawnInterval = GetCurrentSpawnInterval();
        debugCurrentActiveCap = GetCurrentActiveCap();
    }

    void OnValidate()
    {
        minSpawnRadius = Mathf.Max(0.5f, minSpawnRadius);
        maxSpawnRadius = Mathf.Max(minSpawnRadius + 0.1f, maxSpawnRadius);
        minEnemySpacing = Mathf.Max(0f, minEnemySpacing);
        maxSpawnAttemptsPerTick = Mathf.Max(1, maxSpawnAttemptsPerTick);
        minObstacleClearanceRadius = Mathf.Max(0f, minObstacleClearanceRadius);
        shorelineProbeInset = Mathf.Max(0f, shorelineProbeInset);

        spawnIntervalAtNightStart = Mathf.Max(0.1f, spawnIntervalAtNightStart);
        spawnIntervalAtNightEnd = Mathf.Max(0.1f, spawnIntervalAtNightEnd);
        activeCapAtNightStart = Mathf.Max(1, activeCapAtNightStart);
        activeCapAtNightEnd = Mathf.Max(activeCapAtNightStart, activeCapAtNightEnd);
        firstSpawnDelaySeconds = Mathf.Max(0f, firstSpawnDelaySeconds);
    }
}
