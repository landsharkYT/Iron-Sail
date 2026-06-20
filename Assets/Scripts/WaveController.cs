using System.Collections.Generic;
using UnityEngine;

// Central manager for water surface particles and the shared local water field.
//
// Responsibilities:
// - maintain a camera-local disturbance grid driven by wind, emitters, and obstacles
// - let all water visuals sample one shared source of motion and agitation
// - keep ambient wave particles aligned with the current wind state
// - emit authored boat wakes plus idle lapping and burst ripples
// - allow future enemies and water obstacles to participate without custom paths
public class WaveController : MonoBehaviour
{
    enum WaterRefreshMode
    {
        None,
        RebuiltGrid,
        ReusedGrid
    }

    public static WaveController ActiveController { get; private set; }

    [Header("References")]
    [SerializeField] WindController windController;
    [SerializeField] Camera targetCamera;
    [SerializeField] Transform visibleAreaCenterTarget;
    [SerializeField] ParticleSystem ambientWaveParticles;
    [SerializeField] ParticleSystem wakeTrailParticles;
    [SerializeField] ParticleSystem rippleBurstParticles;

    [Header("Rendering")]
    [SerializeField] string waterEffectsSortingLayerName = "Default";
    [SerializeField] int waterEffectsSortingOrder = -5;
    [SerializeField] int fallbackWaterEffectsSortingOrder = -5;

    [Header("Shared Water Field")]
    [SerializeField] float fieldCellWorldSize = 0.7f;
    [SerializeField] int fieldPaddingCells = 4;
    [SerializeField] float fieldSimulationRate = 14f;
    [SerializeField] [Range(0f, 1f)] float fieldPropagation = 0.42f;
    [SerializeField] [Range(0f, 1f)] float fieldDecay = 0.9f;
    [SerializeField] [Range(0f, 1f)] float fieldWindBlend = 0.2f;
    [SerializeField] float fieldBaselineDisturbanceAtCalm = 0.08f;
    [SerializeField] float fieldBaselineDisturbanceAtStrongWind = 0.22f;
    [SerializeField] float fieldMaxStrength = 1.5f;

    [Header("Ambient Waves")]
    [SerializeField] float ambientBoundsPaddingMultiplier = 1.2f;
    [SerializeField] float ambientEmissionRateAtCalm = 72f;
    [SerializeField] float ambientEmissionRateAtStrongWind = 180f;
    [SerializeField] float ambientParticleSpeedAtCalm = 0.12f;
    [SerializeField] float ambientParticleSpeedAtStrongWind = 0.5f;
    [SerializeField] float ambientParticleLifetime = 3.4f;
    [SerializeField] float ambientParticleSize = 0.11f;
    [SerializeField] float ambientDirectionNoiseDegrees = 12f;
    [SerializeField] float ambientDirectionNoiseFrequency = 0.18f;
    [SerializeField] int ambientMaxParticles = 1800;
    [SerializeField] float ambientRefreshInterval = 0.12f;
    [SerializeField] int ambientSamplesPerRefresh = 420;
    [SerializeField] float ambientMinimumSpawnStrength = 0.06f;
    [SerializeField] int ambientClusterParticlesMin = 3;
    [SerializeField] int ambientClusterParticlesMax = 6;
    [SerializeField] float ambientClusterLengthAtCalm = 0.18f;
    [SerializeField] float ambientClusterLengthAtStrongWind = 0.34f;
    [SerializeField] float ambientClusterLengthVariance = 0.55f;
    [SerializeField] float ambientClusterSpacingJitter = 0.025f;
    [SerializeField] float ambientClusterLineJitter = 0.018f;
    [SerializeField] float ambientClusterVelocityJitter = 0.02f;
    [SerializeField] float ambientClusterPerpBias = 0.88f;
    [SerializeField] float ambientAlphaMin = 0.18f;
    [SerializeField] float ambientAlphaMax = 0.42f;
    [SerializeField] float ambientArcBendAtCalm = 0.035f;
    [SerializeField] float ambientArcBendAtStrongWind = 0.085f;

    [Header("Shared Source Tuning")]
    [SerializeField] float disturbanceFullSpeed = 6f;
    [SerializeField] float windCarryMultiplier = 0.18f;
    [SerializeField] float idlePulseWindLeaning = 0.25f;

    [Header("Idle Ripples")]
    [SerializeField] int idleRippleParticlesPerPulse = 12;
    [SerializeField] float idleRippleArcDegrees = 56f;
    [SerializeField] float idleRippleSideBias = 0.68f;
    [SerializeField] float idleRippleSternBias = 0.42f;
    [SerializeField] float idleRippleWindwardBias = 0.55f;
    [SerializeField] float idleRippleOutwardSpeedMin = 0.07f;
    [SerializeField] float idleRippleOutwardSpeedMax = 0.19f;
    [SerializeField] float idleRippleWindCarry = 0.28f;
    [SerializeField] float idleRippleFieldInjectionRadius = 0.12f;
    [SerializeField] float idleRippleFieldInjectionStrength = 0.18f;

    [Header("Wake Trail")]
    [SerializeField] float wakeParticlesPerSecondAtMinSpeed = 12f;
    [SerializeField] float wakeParticlesPerSecondAtFullSpeed = 44f;
    [SerializeField] float wakeParticleSpeedMin = 0.16f;
    [SerializeField] float wakeParticleSpeedMax = 0.8f;
    [SerializeField] float wakeParticleLifetimeMin = 1.3f;
    [SerializeField] float wakeParticleLifetimeMax = 2.6f;
    [SerializeField] float wakeParticleSizeMin = 0.025f;
    [SerializeField] float wakeParticleSizeMax = 0.075f;
    [SerializeField] float wakeHalfWidth = 0.42f;
    [SerializeField] float wakeBackDepth = 1.8f;
    [SerializeField] float wakeLateralVelocity = 0.28f;
    [SerializeField] float wakeVAngleDegrees = 62f;
    [SerializeField] float wakeFieldInjectionLength = 1.4f;
    [SerializeField] float wakeFieldInjectionRadius = 0.32f;
    [SerializeField] float wakeFieldInjectionStrength = 0.36f;
    [SerializeField] [Range(0f, 1f)] float wakeCenterFoamFraction = 0.28f;
    [SerializeField] float wakeCenterFoamWidth = 0.08f;
    [SerializeField] float wakeCenterFoamBackDepth = 0.35f;
    [SerializeField] float wakeCenterFoamLateralVelocity = 0.08f;
    [SerializeField] float wakeRailOutwardVelocityMin = 0.2f;
    [SerializeField] float wakeRailOutwardVelocityMax = 0.46f;
    [SerializeField] float wakeRailLifetimeMultiplier = 1.45f;
    [SerializeField] float wakeCenterLifetimeMultiplier = 0.82f;
    [SerializeField] float wakeRailSpawnSpread = 0.95f;

    [Header("Ripple Bursts")]
    [SerializeField] float rippleBurstIntervalAtSlowSpeed = 0.45f;
    [SerializeField] float rippleBurstIntervalAtFullSpeed = 0.12f;
    [SerializeField] int rippleBurstParticlesAtSlowSpeed = 7;
    [SerializeField] int rippleBurstParticlesAtFullSpeed = 16;
    [SerializeField] float rippleParticleSpeedMin = 0.06f;
    [SerializeField] float rippleParticleSpeedMax = 0.3f;
    [SerializeField] float rippleParticleLifetimeMin = 1.2f;
    [SerializeField] float rippleParticleLifetimeMax = 2.2f;
    [SerializeField] float rippleParticleSizeMin = 0.02f;
    [SerializeField] float rippleParticleSizeMax = 0.07f;
    [SerializeField] float rippleSpawnRadius = 0.24f;
    [SerializeField] float rippleFieldInjectionStrength = 0.28f;

#pragma warning disable CS0414
    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] int debugRegisteredSourceCount;
    [SerializeField] int debugRegisteredObstacleCount;
    [SerializeField] Vector2 debugWindDirection = Vector2.up;
    [SerializeField] float debugWindSpeed;
    [SerializeField] string debugAppliedSortingLayerName = "Default";
    [SerializeField] int debugAppliedSortingOrder = -5;
    [SerializeField] Vector2 debugFieldOrigin;
    [SerializeField] Vector2Int debugFieldDimensions;
    [SerializeField] float debugFieldCellWorldSize;
    [SerializeField] WaterRefreshMode debugLastFieldRefreshMode;
    [SerializeField] float debugSimAccumulator;
#pragma warning restore CS0414

    readonly HashSet<WaterDisturbanceSource> registeredSources = new HashSet<WaterDisturbanceSource>();
    readonly HashSet<WaterObstacle> registeredObstacles = new HashSet<WaterObstacle>();

    float ambientRefreshTimer;
    float simulationAccumulator;
    bool hasWarnedMissingSortingLayer;

    float[] fieldStrength;
    float[] nextFieldStrength;
    Vector2[] fieldFlow;
    Vector2[] nextFieldFlow;
    int fieldMinX;
    int fieldMinY;
    int fieldWidth;
    int fieldHeight;
    bool fieldInitialized;

    void OnEnable()
    {
        if (ActiveController != null && ActiveController != this)
            Debug.LogWarning("[WaveController] Multiple active wave controllers detected. The newest one will become active.", this);

        ActiveController = this;
        RegisterExistingSources();
        RegisterExistingObstacles();
        ConfigureParticleSystems();
    }

    void Start()
    {
        ConfigureParticleSystems();
    }

    void LateUpdate()
    {
        ConfigureParticleSystems();

        Camera cameraToUse = ResolveCamera();
        if (cameraToUse == null)
            return;

        float deltaTime = Time.deltaTime;
        UpdateSourceSamples(deltaTime);
        EnsureFieldGrid(cameraToUse);

        float simStep = 1f / Mathf.Max(fieldSimulationRate, 1f);
        simulationAccumulator += deltaTime;
        while (simulationAccumulator >= simStep)
        {
            SimulateFieldStep(simStep);
            simulationAccumulator -= simStep;
        }

        UpdateAmbientWaves(cameraToUse);
        EmitSourceDisturbances(deltaTime);
        UpdateDebugMirrors();
    }

    void OnDisable()
    {
        if (ActiveController == this)
            ActiveController = null;
    }

    public void RegisterSource(WaterDisturbanceSource source)
    {
        if (source == null)
            return;

        registeredSources.Add(source);
        debugRegisteredSourceCount = registeredSources.Count;
    }

    public void UnregisterSource(WaterDisturbanceSource source)
    {
        if (source == null)
            return;

        registeredSources.Remove(source);
        debugRegisteredSourceCount = registeredSources.Count;
    }

    public void RegisterObstacle(WaterObstacle obstacle)
    {
        if (obstacle == null)
            return;

        registeredObstacles.Add(obstacle);
        debugRegisteredObstacleCount = registeredObstacles.Count;
    }

    public void UnregisterObstacle(WaterObstacle obstacle)
    {
        if (obstacle == null)
            return;

        registeredObstacles.Remove(obstacle);
        debugRegisteredObstacleCount = registeredObstacles.Count;
    }

    void RegisterExistingSources()
    {
        WaterDisturbanceSource[] sources = FindObjectsByType<WaterDisturbanceSource>(FindObjectsInactive.Include);
        foreach (WaterDisturbanceSource source in sources)
            RegisterSource(source);
    }

    void RegisterExistingObstacles()
    {
        WaterObstacle[] obstacles = FindObjectsByType<WaterObstacle>(FindObjectsInactive.Include);
        foreach (WaterObstacle obstacle in obstacles)
            RegisterObstacle(obstacle);
    }

    void ConfigureParticleSystems()
    {
        ConfigureAmbientSystem();
        ConfigureBurstSystem(wakeTrailParticles);
        ConfigureBurstSystem(rippleBurstParticles);
        ConfigureRendererSorting(ambientWaveParticles);
        ConfigureRendererSorting(wakeTrailParticles);
        ConfigureRendererSorting(rippleBurstParticles);
    }

    void ConfigureAmbientSystem()
    {
        if (ambientWaveParticles == null)
            return;

        var main = ambientWaveParticles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.loop = true;
        main.playOnAwake = true;
        main.maxParticles = Mathf.Max(ambientMaxParticles, 1);
        main.startLifetime = ambientParticleLifetime;
        main.startSize = ambientParticleSize;

        var shape = ambientWaveParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;

        var emission = ambientWaveParticles.emission;
        emission.enabled = true;

        var velocityOverLifetime = ambientWaveParticles.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.space = ParticleSystemSimulationSpace.World;

        if (!ambientWaveParticles.isPlaying)
            ambientWaveParticles.Play();
    }

    void ConfigureBurstSystem(ParticleSystem particleSystem)
    {
        if (particleSystem == null)
            return;

        var main = particleSystem.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.loop = true;
        main.playOnAwake = true;

        var emission = particleSystem.emission;
        emission.enabled = false;

        if (!particleSystem.isPlaying)
            particleSystem.Play();
    }

    void ConfigureRendererSorting(ParticleSystem particleSystem)
    {
        if (particleSystem == null)
            return;

        ParticleSystemRenderer particleRenderer = particleSystem.GetComponent<ParticleSystemRenderer>();
        if (particleRenderer == null)
            return;

        bool sortingLayerExists = SortingLayerExists(waterEffectsSortingLayerName);
        string sortingLayerToUse = sortingLayerExists ? waterEffectsSortingLayerName : ResolveSortingLayerName(waterEffectsSortingLayerName);
        int sortingOrderToUse = sortingLayerExists ? waterEffectsSortingOrder : fallbackWaterEffectsSortingOrder;

        particleRenderer.sortingLayerName = sortingLayerToUse;
        particleRenderer.sortingOrder = sortingOrderToUse;
        debugAppliedSortingLayerName = sortingLayerToUse;
        debugAppliedSortingOrder = sortingOrderToUse;
    }

    string ResolveSortingLayerName(string requestedLayerName)
    {
        if (SortingLayerExists(requestedLayerName))
            return requestedLayerName;

        if (!hasWarnedMissingSortingLayer)
        {
            Debug.LogWarning(
                $"[WaveController] Sorting layer '{requestedLayerName}' does not exist. Falling back to 'Default'.",
                this);
            hasWarnedMissingSortingLayer = true;
        }

        return "Default";
    }

    static bool SortingLayerExists(string layerName)
    {
        if (string.IsNullOrWhiteSpace(layerName))
            return false;

        SortingLayer[] sortingLayers = SortingLayer.layers;
        for (int i = 0; i < sortingLayers.Length; i++)
        {
            if (sortingLayers[i].name == layerName)
                return true;
        }

        return false;
    }

    void UpdateSourceSamples(float deltaTime)
    {
        foreach (WaterDisturbanceSource source in registeredSources)
        {
            if (source == null || !source.isActiveAndEnabled)
                continue;

            source.SampleMotion(deltaTime);
        }
    }

    void EnsureFieldGrid(Camera cameraToUse)
    {
        Vector3 center = ResolveVisibleAreaCenter(cameraToUse);
        Vector2 viewSize = GetCameraWorldSize(cameraToUse);

        float halfWidth = viewSize.x * 0.5f + fieldPaddingCells * fieldCellWorldSize;
        float halfHeight = viewSize.y * 0.5f + fieldPaddingCells * fieldCellWorldSize;

        int requiredMinX = Mathf.FloorToInt((center.x - halfWidth) / fieldCellWorldSize);
        int requiredMaxX = Mathf.CeilToInt((center.x + halfWidth) / fieldCellWorldSize);
        int requiredMinY = Mathf.FloorToInt((center.y - halfHeight) / fieldCellWorldSize);
        int requiredMaxY = Mathf.CeilToInt((center.y + halfHeight) / fieldCellWorldSize);

        int requiredWidth = Mathf.Max(requiredMaxX - requiredMinX + 1, 2);
        int requiredHeight = Mathf.Max(requiredMaxY - requiredMinY + 1, 2);

        bool needsRebuild =
            !fieldInitialized ||
            requiredMinX != fieldMinX ||
            requiredMinY != fieldMinY ||
            requiredWidth != fieldWidth ||
            requiredHeight != fieldHeight;

        if (!needsRebuild)
        {
            debugLastFieldRefreshMode = WaterRefreshMode.ReusedGrid;
            return;
        }

        RebuildFieldGrid(requiredMinX, requiredMinY, requiredWidth, requiredHeight);
        debugLastFieldRefreshMode = WaterRefreshMode.RebuiltGrid;
    }

    void RebuildFieldGrid(int newMinX, int newMinY, int newWidth, int newHeight)
    {
        int newLength = newWidth * newHeight;
        float[] newStrength = new float[newLength];
        float[] newNextStrength = new float[newLength];
        Vector2[] newFlow = new Vector2[newLength];
        Vector2[] newNextFlow = new Vector2[newLength];

        Vector2 windDirection = GetWindDirection();
        float baselineStrength = GetBaselineDisturbance(GetWindSpeed01());

        for (int i = 0; i < newLength; i++)
        {
            newStrength[i] = baselineStrength;
            newNextStrength[i] = baselineStrength;
            newFlow[i] = windDirection;
            newNextFlow[i] = windDirection;
        }

        if (fieldInitialized && fieldStrength != null && fieldFlow != null)
        {
            int overlapMinX = Mathf.Max(fieldMinX, newMinX);
            int overlapMaxX = Mathf.Min(fieldMinX + fieldWidth - 1, newMinX + newWidth - 1);
            int overlapMinY = Mathf.Max(fieldMinY, newMinY);
            int overlapMaxY = Mathf.Min(fieldMinY + fieldHeight - 1, newMinY + newHeight - 1);

            for (int y = overlapMinY; y <= overlapMaxY; y++)
            {
                for (int x = overlapMinX; x <= overlapMaxX; x++)
                {
                    int oldIndex = GetFieldIndex(x - fieldMinX, y - fieldMinY, fieldWidth);
                    int newIndex = GetFieldIndex(x - newMinX, y - newMinY, newWidth);
                    newStrength[newIndex] = fieldStrength[oldIndex];
                    newNextStrength[newIndex] = fieldStrength[oldIndex];
                    newFlow[newIndex] = fieldFlow[oldIndex];
                    newNextFlow[newIndex] = fieldFlow[oldIndex];
                }
            }
        }

        fieldStrength = newStrength;
        nextFieldStrength = newNextStrength;
        fieldFlow = newFlow;
        nextFieldFlow = newNextFlow;
        fieldMinX = newMinX;
        fieldMinY = newMinY;
        fieldWidth = newWidth;
        fieldHeight = newHeight;
        fieldInitialized = true;
    }

    void SimulateFieldStep(float deltaTime)
    {
        if (!fieldInitialized || fieldStrength == null || nextFieldStrength == null)
            return;

        Vector2 windDirection = GetWindDirection();
        float windSpeed = GetWindSpeed01();
        float baselineStrength = GetBaselineDisturbance(windSpeed);

        for (int y = 0; y < fieldHeight; y++)
        {
            for (int x = 0; x < fieldWidth; x++)
            {
                int index = GetFieldIndex(x, y, fieldWidth);

                float strengthSum = fieldStrength[index];
                Vector2 flowSum = fieldFlow[index];
                int sampleCount = 1;

                AccumulateNeighbor(x - 1, y, ref strengthSum, ref flowSum, ref sampleCount);
                AccumulateNeighbor(x + 1, y, ref strengthSum, ref flowSum, ref sampleCount);
                AccumulateNeighbor(x, y - 1, ref strengthSum, ref flowSum, ref sampleCount);
                AccumulateNeighbor(x, y + 1, ref strengthSum, ref flowSum, ref sampleCount);

                float averageStrength = strengthSum / sampleCount;
                Vector2 averageFlow = sampleCount > 0 ? flowSum / sampleCount : windDirection;
                if (averageFlow.sqrMagnitude < 0.0001f)
                    averageFlow = windDirection;

                float propagatedStrength = Mathf.Lerp(fieldStrength[index], averageStrength, fieldPropagation);
                propagatedStrength = Mathf.Max(propagatedStrength * fieldDecay, baselineStrength);
                propagatedStrength = Mathf.Clamp(propagatedStrength, 0f, fieldMaxStrength);

                Vector2 currentFlow = fieldFlow[index].sqrMagnitude >= 0.0001f ? fieldFlow[index].normalized : windDirection;
                Vector2 propagatedFlow = Vector2.Lerp(currentFlow, averageFlow.normalized, fieldPropagation);
                if (propagatedFlow.sqrMagnitude < 0.0001f)
                    propagatedFlow = windDirection;

                Vector2 blendedFlow = Vector2.Lerp(propagatedFlow.normalized, windDirection, fieldWindBlend * (0.35f + 0.65f * windSpeed));
                if (blendedFlow.sqrMagnitude < 0.0001f)
                    blendedFlow = windDirection;

                nextFieldStrength[index] = propagatedStrength;
                nextFieldFlow[index] = blendedFlow.normalized;
            }
        }

        SwapFieldBuffers();
        InjectSourceDisturbancesIntoField(deltaTime, windDirection, windSpeed);
        ApplyObstacleInfluence();
    }

    void AccumulateNeighbor(int x, int y, ref float strengthSum, ref Vector2 flowSum, ref int sampleCount)
    {
        if (x < 0 || y < 0 || x >= fieldWidth || y >= fieldHeight)
            return;

        int index = GetFieldIndex(x, y, fieldWidth);
        strengthSum += fieldStrength[index];
        flowSum += fieldFlow[index];
        sampleCount++;
    }

    void SwapFieldBuffers()
    {
        float[] strengthTemp = fieldStrength;
        fieldStrength = nextFieldStrength;
        nextFieldStrength = strengthTemp;

        Vector2[] flowTemp = fieldFlow;
        fieldFlow = nextFieldFlow;
        nextFieldFlow = flowTemp;
    }

    void InjectSourceDisturbancesIntoField(float deltaTime, Vector2 windDirection, float windSpeed)
    {
        foreach (WaterDisturbanceSource source in registeredSources)
        {
            if (source == null || !source.isActiveAndEnabled)
                continue;

            float speed = source.CurrentSpeed;
            Vector2 movementDirection = GetMovementDirection(source);
            bool isMoving = speed >= source.MinSpeedThreshold;

            switch (source.Archetype)
            {
                case WaterDisturbanceSource.DisturbanceArchetype.WakeMover:
                    if (isMoving)
                    {
                        InjectWakeIntoField(source, movementDirection, speed);
                        InjectBurstSplashIntoField(source, movementDirection, deltaTime, windDirection, windSpeed, false);
                    }
                    break;

                case WaterDisturbanceSource.DisturbanceArchetype.IdleLapper:
                    break;

                case WaterDisturbanceSource.DisturbanceArchetype.BurstSplash:
                    InjectBurstSplashIntoField(source, movementDirection, deltaTime, windDirection, windSpeed, true);
                    break;
            }
        }
    }

    void InjectWakeIntoField(WaterDisturbanceSource source, Vector2 movementDirection, float speed)
    {
        Vector2 rear = GetWakeSpawnCenter(source, movementDirection);
        Vector2 backward = -movementDirection.normalized;
        Vector2 leftRail = Rotate(backward, wakeVAngleDegrees);
        Vector2 rightRail = Rotate(backward, -wakeVAngleDegrees);
        float speedFactor = GetSpeedFactor(speed, source.MinSpeedThreshold);
        float length = wakeFieldInjectionLength * Mathf.Lerp(0.7f, 1.4f, speedFactor) * source.SizeMultiplier;
        float strength = wakeFieldInjectionStrength * Mathf.Lerp(0.8f, 1.45f, speedFactor) * Mathf.Max(source.DisturbanceStrength, 0f);

        InjectDisturbance(rear, wakeFieldInjectionRadius * source.SizeMultiplier, strength, backward, 0.45f);

        for (int i = 1; i <= 3; i++)
        {
            float t = i / 3f;
            float stepDistance = length * t;
            InjectDisturbance(rear + leftRail * stepDistance, wakeFieldInjectionRadius * source.SizeMultiplier, strength * (1f - 0.18f * i), leftRail, 0.9f);
            InjectDisturbance(rear + rightRail * stepDistance, wakeFieldInjectionRadius * source.SizeMultiplier, strength * (1f - 0.18f * i), rightRail, 0.9f);
        }
    }

    void InjectBurstSplashIntoField(
        WaterDisturbanceSource source,
        Vector2 movementDirection,
        float deltaTime,
        Vector2 windDirection,
        float windSpeed,
        bool allowWhenStill)
    {
        bool canSplash = allowWhenStill || source.CurrentSpeed >= source.MinSpeedThreshold;
        if (!canSplash)
            return;

        source.BurstTimer -= deltaTime;
        if (source.BurstTimer > 0f)
            return;

        source.BurstTimer = Random.Range(source.BurstIntervalMin, source.BurstIntervalMax);

        Vector2 anchor = source.GetWakeSpawnWorldPosition(movementDirection);
        Vector2 flowDirection = movementDirection.sqrMagnitude >= 0.0001f ? movementDirection : windDirection;
        float strength = source.DisturbanceStrength * source.BurstStrengthMultiplier * Mathf.Lerp(0.75f, 1.1f, windSpeed);
        InjectDisturbance(anchor, source.BurstRadius * source.SizeMultiplier, strength, flowDirection, 0.6f);
    }

    void ApplyObstacleInfluence()
    {
        foreach (WaterObstacle obstacle in registeredObstacles)
        {
            if (obstacle == null || !obstacle.isActiveAndEnabled)
                continue;

            Vector2 position = obstacle.Position;
            float radius = obstacle.EffectiveRadius;
            int minX = Mathf.FloorToInt((position.x - radius) / fieldCellWorldSize) - fieldMinX;
            int maxX = Mathf.CeilToInt((position.x + radius) / fieldCellWorldSize) - fieldMinX;
            int minY = Mathf.FloorToInt((position.y - radius) / fieldCellWorldSize) - fieldMinY;
            int maxY = Mathf.CeilToInt((position.y + radius) / fieldCellWorldSize) - fieldMinY;

            for (int y = Mathf.Max(0, minY); y <= Mathf.Min(fieldHeight - 1, maxY); y++)
            {
                for (int x = Mathf.Max(0, minX); x <= Mathf.Min(fieldWidth - 1, maxX); x++)
                {
                    Vector2 cellWorld = GetCellWorldPosition(x, y);
                    float distance = Vector2.Distance(cellWorld, position);
                    if (distance > radius)
                        continue;

                    float attenuation = 1f - distance / Mathf.Max(radius, 0.001f);
                    int index = GetFieldIndex(x, y, fieldWidth);
                    float dampingFactor = Mathf.Lerp(1f, 1f - obstacle.DisturbanceDamping, attenuation);
                    fieldStrength[index] *= dampingFactor;

                    Vector2 away = cellWorld - position;
                    if (away.sqrMagnitude < 0.0001f)
                        away = fieldFlow[index].sqrMagnitude >= 0.0001f ? new Vector2(-fieldFlow[index].y, fieldFlow[index].x) : Vector2.right;

                    Vector2 redirected = Vector2.Lerp(
                        fieldFlow[index].sqrMagnitude >= 0.0001f ? fieldFlow[index].normalized : Vector2.up,
                        away.normalized,
                        obstacle.FlowDeflection * attenuation);

                    if (redirected.sqrMagnitude >= 0.0001f)
                        fieldFlow[index] = redirected.normalized;
                }
            }
        }
    }

    void InjectDisturbance(Vector2 position, float radius, float strength, Vector2 direction, float directionBlend)
    {
        if (!fieldInitialized)
            return;

        Vector2 normalizedDirection = direction.sqrMagnitude >= 0.0001f ? direction.normalized : GetWindDirection();
        float clampedRadius = Mathf.Max(radius, fieldCellWorldSize * 0.5f);
        int minX = Mathf.FloorToInt((position.x - clampedRadius) / fieldCellWorldSize) - fieldMinX;
        int maxX = Mathf.CeilToInt((position.x + clampedRadius) / fieldCellWorldSize) - fieldMinX;
        int minY = Mathf.FloorToInt((position.y - clampedRadius) / fieldCellWorldSize) - fieldMinY;
        int maxY = Mathf.CeilToInt((position.y + clampedRadius) / fieldCellWorldSize) - fieldMinY;

        for (int y = Mathf.Max(0, minY); y <= Mathf.Min(fieldHeight - 1, maxY); y++)
        {
            for (int x = Mathf.Max(0, minX); x <= Mathf.Min(fieldWidth - 1, maxX); x++)
            {
                Vector2 cellWorld = GetCellWorldPosition(x, y);
                float distance = Vector2.Distance(cellWorld, position);
                if (distance > clampedRadius)
                    continue;

                float falloff = 1f - distance / Mathf.Max(clampedRadius, 0.001f);
                int index = GetFieldIndex(x, y, fieldWidth);
                fieldStrength[index] = Mathf.Clamp(fieldStrength[index] + strength * falloff, 0f, fieldMaxStrength);

                if (normalizedDirection.sqrMagnitude >= 0.0001f)
                {
                    Vector2 blended = Vector2.Lerp(
                        fieldFlow[index].sqrMagnitude >= 0.0001f ? fieldFlow[index].normalized : normalizedDirection,
                        normalizedDirection,
                        directionBlend * falloff);

                    if (blended.sqrMagnitude >= 0.0001f)
                        fieldFlow[index] = blended.normalized;
                }
            }
        }
    }

    void UpdateAmbientWaves(Camera cameraToUse)
    {
        if (ambientWaveParticles == null)
            return;

        Vector3 center = ResolveVisibleAreaCenter(cameraToUse);
        Vector2 viewSize = GetCameraWorldSize(cameraToUse);
        float padding = Mathf.Max(ambientBoundsPaddingMultiplier, 1f);

        Transform ambientTransform = ambientWaveParticles.transform;
        ambientTransform.position = new Vector3(center.x, center.y, ambientTransform.position.z);

        var emission = ambientWaveParticles.emission;
        emission.rateOverTime = 0f;

        ambientRefreshTimer -= Time.deltaTime;
        if (ambientRefreshTimer > 0f)
            return;

        ambientRefreshTimer = Mathf.Max(ambientRefreshInterval, 0.01f);
        EmitAmbientFieldParticles(center, viewSize * padding, GetWindSpeed01());
    }

    void EmitAmbientFieldParticles(Vector3 center, Vector2 areaSize, float windSpeed)
    {
        if (ambientWaveParticles == null || !fieldInitialized)
            return;

        float halfWidth = areaSize.x * 0.5f;
        float halfHeight = areaSize.y * 0.5f;
        int emissionBudget = Mathf.RoundToInt(Mathf.Lerp(ambientEmissionRateAtCalm, ambientEmissionRateAtStrongWind, windSpeed) * ambientRefreshInterval);
        int maxAttempts = Mathf.Max(ambientSamplesPerRefresh, emissionBudget * 2);
        int emitted = 0;

        for (int i = 0; i < maxAttempts; i++)
        {
            Vector2 samplePosition = new Vector2(
                center.x + Random.Range(-halfWidth, halfWidth),
                center.y + Random.Range(-halfHeight, halfHeight));

            FieldSample sample = SampleField(samplePosition);
            float spawnStrength = Mathf.Max(sample.strength, GetBaselineDisturbance(windSpeed));
            if (spawnStrength < ambientMinimumSpawnStrength)
                continue;

            Vector2 driftDirection = sample.direction.sqrMagnitude >= 0.0001f ? sample.direction.normalized : GetAmbientDriftDirection();
            float noiseDegrees = Random.Range(-ambientDirectionNoiseDegrees, ambientDirectionNoiseDegrees);
            driftDirection = Rotate(driftDirection, noiseDegrees).normalized;

            int clusterCount = Random.Range(ambientClusterParticlesMin, ambientClusterParticlesMax + 1);
            float clusterWindT = Mathf.Clamp01(Mathf.Lerp(windSpeed, spawnStrength, 0.45f));
            float baseClusterLength = Mathf.Lerp(ambientClusterLengthAtCalm, ambientClusterLengthAtStrongWind, clusterWindT);
            float clusterLength = baseClusterLength * Random.Range(1f, 1f + ambientClusterLengthVariance);
            emitted += EmitAmbientCrestCluster(samplePosition, driftDirection, spawnStrength, clusterLength, clusterCount);

            if (emitted >= emissionBudget)
                break;
        }
    }

    int EmitAmbientCrestCluster(Vector2 center, Vector2 driftDirection, float spawnStrength, float clusterLength, int clusterCount)
    {
        if (ambientWaveParticles == null)
            return 0;

        clusterCount = Mathf.Max(1, clusterCount);
        Vector2 perpendicular = new Vector2(-driftDirection.y, driftDirection.x);
        Vector2 crestAxis = Vector2.Lerp(driftDirection, perpendicular, ambientClusterPerpBias).normalized;
        if (crestAxis.sqrMagnitude < 0.0001f)
            crestAxis = perpendicular.sqrMagnitude >= 0.0001f ? perpendicular : Vector2.right;

        float velocityScale = Mathf.Lerp(ambientParticleSpeedAtCalm, ambientParticleSpeedAtStrongWind, Mathf.Clamp01(spawnStrength));
        float alphaBase = Mathf.Lerp(ambientAlphaMin, ambientAlphaMax, Mathf.Clamp01(spawnStrength));
        float halfLength = clusterLength * 0.5f;
        float bendAmount = Mathf.Lerp(ambientArcBendAtCalm, ambientArcBendAtStrongWind, Mathf.Clamp01(spawnStrength));
        float bendSign = Random.value < 0.5f ? -1f : 1f;

        for (int i = 0; i < clusterCount; i++)
        {
            float t = clusterCount == 1 ? 0.5f : i / (clusterCount - 1f);
            float along = Mathf.Lerp(-halfLength, halfLength, t) + Random.Range(-ambientClusterSpacingJitter, ambientClusterSpacingJitter);
            float arcOffset = (1f - Mathf.Pow(Mathf.Abs(t - 0.5f) * 2f, 1.5f)) * bendAmount * bendSign;
            float cross = arcOffset + Random.Range(-ambientClusterLineJitter, ambientClusterLineJitter);
            Vector2 position = center + crestAxis * along + driftDirection * cross;

            float sizeT = clusterCount == 1 ? 0.5f : Mathf.Abs(t - 0.5f) * 2f;
            float size = ambientParticleSize * Mathf.Lerp(0.82f, 0.46f, sizeT) * Random.Range(0.92f, 1.08f);
            float alpha = alphaBase * Mathf.Lerp(1f, 0.74f, sizeT) * Random.Range(0.92f, 1.04f);

            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, position.y, ambientWaveParticles.transform.position.z),
                velocity = driftDirection * velocityScale + Random.insideUnitCircle * ambientClusterVelocityJitter,
                startLifetime = ambientParticleLifetime * Random.Range(0.7f, 1.05f),
                startSize = size,
                startColor = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha))
            };
            ambientWaveParticles.Emit(emitParams, 1);
        }

        return clusterCount;
    }

    void EmitSourceDisturbances(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        Vector2 windDirection = GetWindDirection();
        float windSpeed = GetWindSpeed01();

        foreach (WaterDisturbanceSource source in registeredSources)
        {
            if (source == null || !source.isActiveAndEnabled)
                continue;

            float speed = source.CurrentSpeed;
            bool isMoving = speed >= source.MinSpeedThreshold;
            Vector2 movementDirection = GetMovementDirection(source);

            if (source.Archetype == WaterDisturbanceSource.DisturbanceArchetype.WakeMover && isMoving)
            {
                EmitWakeTrailForSource(source, movementDirection, windDirection, windSpeed, speed, deltaTime);
                EmitRippleBurstsForSource(source, movementDirection, windDirection, windSpeed, speed, deltaTime, false);
            }
            else if (source.Archetype == WaterDisturbanceSource.DisturbanceArchetype.IdleLapper
                  || (source.Archetype == WaterDisturbanceSource.DisturbanceArchetype.WakeMover
                   && !isMoving
                   && source.EmitIdleLappingWhenStill))
            {
                EmitIdleRipplesForSource(source, windDirection, deltaTime);
            }
            else if (source.Archetype == WaterDisturbanceSource.DisturbanceArchetype.BurstSplash)
            {
                EmitRippleBurstsForSource(source, movementDirection, windDirection, windSpeed, Mathf.Max(speed, source.MinSpeedThreshold), deltaTime, true);
            }
        }
    }

    void EmitWakeTrailForSource(
        WaterDisturbanceSource source,
        Vector2 movementDirection,
        Vector2 windDirection,
        float windSpeed,
        float speed,
        float deltaTime)
    {
        if (wakeTrailParticles == null)
            return;

        float speedFactor = GetSpeedFactor(speed, source.MinSpeedThreshold);
        float particlesPerSecond = Mathf.Lerp(wakeParticlesPerSecondAtMinSpeed, wakeParticlesPerSecondAtFullSpeed, speedFactor)
                                 * 1.35f
                                 * Mathf.Max(source.DisturbanceStrength, 0f);

        source.WakeEmissionAccumulator += particlesPerSecond * deltaTime;
        int emitCount = Mathf.FloorToInt(source.WakeEmissionAccumulator);
        source.WakeEmissionAccumulator -= emitCount;

        Vector2 spawnCenter = GetWakeSpawnCenter(source, movementDirection);
        Vector2 backward = -movementDirection.normalized;
        Vector2 leftRail = Rotate(backward, wakeVAngleDegrees);
        Vector2 rightRail = Rotate(backward, -wakeVAngleDegrees);
        Vector2 perpendicular = new Vector2(-movementDirection.y, movementDirection.x);

        if (emitCount <= 0)
            return;

        for (int i = 0; i < emitCount; i++)
        {
            bool emitCenterFoam = Random.value < wakeCenterFoamFraction;
            float sideSign = Random.value < 0.5f ? -1f : 1f;
            Vector2 railDirection = sideSign < 0f ? leftRail : rightRail;
            Vector2 position;
            Vector2 velocity;
            float lifetimeMultiplier;
            float alphaMin;
            float alphaMax;

            if (emitCenterFoam)
            {
                float along = Random.Range(0f, wakeCenterFoamBackDepth) * (1f + speedFactor * 0.5f) * source.SizeMultiplier;
                float sideOffset = Random.Range(-wakeCenterFoamWidth, wakeCenterFoamWidth) * source.SizeMultiplier;
                position = spawnCenter + backward * along + perpendicular * sideOffset;

                FieldSample sample = SampleField(position);
                Vector2 flowDirection = sample.direction.sqrMagnitude >= 0.0001f ? sample.direction : backward;
                velocity =
                    backward * Random.Range(wakeParticleSpeedMin, wakeParticleSpeedMax) * (0.8f + 0.55f * speedFactor)
                    + perpendicular * sideSign * Random.Range(0f, wakeCenterFoamLateralVelocity) * source.SizeMultiplier
                    + flowDirection * (sample.strength * 0.14f)
                    + windDirection * (windSpeed * windCarryMultiplier * 0.28f);

                lifetimeMultiplier = wakeCenterLifetimeMultiplier;
                alphaMin = 1f;
                alphaMax = 1f;
            }
            else
            {
                float along = Random.Range(0f, wakeBackDepth) * (1f + speedFactor * 1.05f) * source.SizeMultiplier;
                float normalizedAlong = Mathf.Clamp01(along / Mathf.Max(wakeBackDepth * source.SizeMultiplier, 0.001f));
                float railOffset = Mathf.Lerp(wakeHalfWidth * 0.15f, wakeHalfWidth, normalizedAlong)
                                 * wakeRailSpawnSpread
                                 * source.SizeMultiplier;
                float sideOffset = railOffset * sideSign + Random.Range(-wakeHalfWidth * 0.08f, wakeHalfWidth * 0.08f) * source.SizeMultiplier;
                position = spawnCenter + backward * along + perpendicular * sideOffset;

                FieldSample sample = SampleField(position);
                Vector2 flowDirection = sample.direction.sqrMagnitude >= 0.0001f ? sample.direction : railDirection;
                velocity =
                    backward * Random.Range(wakeParticleSpeedMin, wakeParticleSpeedMax) * (0.72f + 0.6f * speedFactor)
                    + perpendicular * sideSign * Random.Range(wakeRailOutwardVelocityMin, wakeRailOutwardVelocityMax) * Mathf.Lerp(1f, 1.8f, speedFactor) * source.SizeMultiplier
                    + railDirection * Random.Range(wakeLateralVelocity * 0.18f, wakeLateralVelocity * 0.64f)
                    + flowDirection * (sample.strength * 0.2f)
                    + windDirection * (windSpeed * windCarryMultiplier * 0.34f);

                lifetimeMultiplier = wakeRailLifetimeMultiplier;
                alphaMin = 1f;
                alphaMax = 1f;
            }

            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, position.y, wakeTrailParticles.transform.position.z),
                velocity = velocity,
                startLifetime = Random.Range(wakeParticleLifetimeMin, wakeParticleLifetimeMax) * Mathf.Lerp(1f, 1.3f, speedFactor) * lifetimeMultiplier,
                startSize = Random.Range(wakeParticleSizeMin, wakeParticleSizeMax) * source.SizeMultiplier,
                startColor = new Color(1f, 1f, 1f, Random.Range(alphaMin, alphaMax))
            };

            wakeTrailParticles.Emit(emitParams, 1);
        }
    }

    void EmitRippleBurstsForSource(
        WaterDisturbanceSource source,
        Vector2 movementDirection,
        Vector2 windDirection,
        float windSpeed,
        float speed,
        float deltaTime,
        bool allowWhenStill)
    {
        if (rippleBurstParticles == null)
            return;

        bool canEmit = allowWhenStill || speed >= source.MinSpeedThreshold;
        if (!canEmit)
            return;

        float strengthFactor = Mathf.Clamp01(GetSpeedFactor(speed, source.MinSpeedThreshold) * Mathf.Max(source.DisturbanceStrength, 0f));
        float interval = Mathf.Lerp(rippleBurstIntervalAtSlowSpeed, rippleBurstIntervalAtFullSpeed, strengthFactor);

        source.RippleBurstTimer -= deltaTime;
        if (source.RippleBurstTimer > 0f)
            return;

        source.RippleBurstTimer = Mathf.Max(interval, 0.01f);

        int burstCount = Mathf.RoundToInt(
            Mathf.Lerp(rippleBurstParticlesAtSlowSpeed, rippleBurstParticlesAtFullSpeed, strengthFactor)
            * Mathf.Max(source.DisturbanceStrength, 0.25f));

        Vector2 spawnCenter = GetWakeSpawnCenter(source, movementDirection);

        for (int i = 0; i < burstCount; i++)
        {
            Vector2 radial = Random.insideUnitCircle;
            if (radial.sqrMagnitude < 0.0001f)
                radial = Vector2.up;

            radial.Normalize();

            Vector2 position = spawnCenter + Random.insideUnitCircle * rippleSpawnRadius * source.SizeMultiplier;
            FieldSample sample = SampleField(position);
            Vector2 sampledDirection = sample.direction.sqrMagnitude >= 0.0001f ? sample.direction : windDirection;
            Vector2 velocity =
                radial * Random.Range(rippleParticleSpeedMin, rippleParticleSpeedMax) * source.SizeMultiplier
                + sampledDirection * (sample.strength * 0.15f)
                + windDirection * (windSpeed * windCarryMultiplier * 0.25f);

            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();
            emitParams.position = new Vector3(position.x, position.y, rippleBurstParticles.transform.position.z);
            emitParams.velocity = velocity;
            emitParams.startLifetime = Random.Range(rippleParticleLifetimeMin, rippleParticleLifetimeMax);
            emitParams.startSize = Random.Range(rippleParticleSizeMin, rippleParticleSizeMax) * source.SizeMultiplier;
            emitParams.startColor = new Color(1f, 1f, 1f, Random.Range(0.42f, 0.68f));
            rippleBurstParticles.Emit(emitParams, 1);
            InjectDisturbance(
                position,
                idleRippleFieldInjectionRadius * 1.6f * source.SizeMultiplier,
                rippleFieldInjectionStrength * Mathf.Lerp(0.5f, 1.15f, strengthFactor) * Mathf.Max(source.DisturbanceStrength, 0.35f),
                radial,
                0.42f);
        }
    }

    void EmitIdleRipplesForSource(WaterDisturbanceSource source, Vector2 windDirection, float deltaTime)
    {
        if (rippleBurstParticles == null)
            return;

        source.IdlePulseTimer -= deltaTime;
        if (source.IdlePulseTimer > 0f)
            return;

        source.IdlePulseTimer = Random.Range(source.IdlePulseIntervalMin, source.IdlePulseIntervalMax);

        Vector2 origin = source.GetAnchorWorldPosition();
        Vector2 hullForward = source.TargetTransform.up.sqrMagnitude >= 0.0001f ? ((Vector2)source.TargetTransform.up).normalized : Vector2.up;
        Vector2 hullSide = new Vector2(-hullForward.y, hullForward.x);
        Vector2 windFacing = windDirection.sqrMagnitude >= 0.0001f ? -windDirection.normalized : -hullForward;

        float halfLength = source.IdleRippleHalfLength * source.SizeMultiplier;
        float halfWidth = source.IdleRippleHalfWidth * source.SizeMultiplier;
        float pulseStrength = Mathf.Max(source.DisturbanceStrength, 0f) * source.IdlePulseStrengthMultiplier;
        float arcCenterDegrees = ChooseIdleRippleArcCenterDegrees(hullForward, hullSide, windFacing);
        int particleCount = Mathf.Max(6, Mathf.RoundToInt(idleRippleParticlesPerPulse * Mathf.Lerp(0.9f, 1.35f, Mathf.Clamp01(source.SizeMultiplier))));
        float arcDegrees = Mathf.Clamp(idleRippleArcDegrees, 12f, 170f);
        int primaryCount = Mathf.CeilToInt(particleCount * 0.65f);
        int secondaryCount = Mathf.Max(3, particleCount - primaryCount);

        EmitIdleRippleArc(source, origin, hullForward, hullSide, windDirection, halfLength, halfWidth, pulseStrength, arcCenterDegrees, arcDegrees, primaryCount, 1f);
        EmitIdleRippleArc(source, origin, hullForward, hullSide, windDirection, halfLength, halfWidth, pulseStrength, Mathf.Repeat(arcCenterDegrees + 155f + Random.Range(-12f, 12f), 360f), arcDegrees * 0.82f, secondaryCount, 0.72f);
    }

    void EmitIdleRippleArc(
        WaterDisturbanceSource source,
        Vector2 origin,
        Vector2 hullForward,
        Vector2 hullSide,
        Vector2 windDirection,
        float halfLength,
        float halfWidth,
        float pulseStrength,
        float arcCenterDegrees,
        float arcDegrees,
        int particleCount,
        float strengthScale)
    {
        for (int i = 0; i < particleCount; i++)
        {
            float t = particleCount == 1 ? 0.5f : i / (particleCount - 1f);
            float angleDeg = Mathf.Lerp(arcCenterDegrees - arcDegrees * 0.5f, arcCenterDegrees + arcDegrees * 0.5f, t)
                           + Random.Range(-5f, 5f);
            float angleRad = angleDeg * Mathf.Deg2Rad;

            Vector2 ellipsePoint = origin
                                 + hullSide * (Mathf.Cos(angleRad) * halfWidth)
                                 + hullForward * (Mathf.Sin(angleRad) * halfLength);

            Vector2 outward = GetEllipseOutwardNormal(origin, ellipsePoint, hullForward, hullSide, halfLength, halfWidth);
            Vector2 position = ellipsePoint + outward * Random.Range(0.015f, source.IdlePulseRadius * 0.34f) * source.SizeMultiplier;

            FieldSample sample = SampleField(position);
            Vector2 carriedFlow = sample.direction.sqrMagnitude >= 0.0001f ? sample.direction : windDirection;
            Vector2 velocity =
                outward * Random.Range(idleRippleOutwardSpeedMin, idleRippleOutwardSpeedMax) * Mathf.Lerp(0.95f, 1.28f, pulseStrength) * strengthScale
                + carriedFlow * (sample.strength * 0.12f)
                + windDirection * (windCarryMultiplier * idleRippleWindCarry * 0.85f);

            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, position.y, rippleBurstParticles.transform.position.z),
                velocity = velocity,
                startLifetime = Random.Range(rippleParticleLifetimeMin, rippleParticleLifetimeMax) * 1.08f,
                startSize = Random.Range(rippleParticleSizeMin, rippleParticleSizeMax) * 1.18f * source.SizeMultiplier * Mathf.Lerp(0.92f, 1.06f, strengthScale),
                startColor = new Color(1f, 1f, 1f, Random.Range(0.86f, 1f) * Mathf.Lerp(0.85f, 1f, strengthScale))
            };
            rippleBurstParticles.Emit(emitParams, 1);

            if (i % 2 == 0)
            {
                Vector2 fieldDirection = Vector2.Lerp(outward, carriedFlow.sqrMagnitude >= 0.0001f ? carriedFlow.normalized : outward, idlePulseWindLeaning).normalized;
                InjectDisturbance(
                    position,
                    idleRippleFieldInjectionRadius * source.SizeMultiplier,
                    idleRippleFieldInjectionStrength * pulseStrength * strengthScale,
                    fieldDirection,
                    0.32f);
            }
        }
    }

    float ChooseIdleRippleArcCenterDegrees(Vector2 hullForward, Vector2 hullSide, Vector2 windFacing)
    {
        float bestScore = float.MinValue;
        float bestAngle = -90f;

        for (int i = 0; i < 24; i++)
        {
            float angle = i / 24f * 360f;
            float rad = angle * Mathf.Deg2Rad;
            Vector2 localNormal = (hullSide * Mathf.Cos(rad) + hullForward * Mathf.Sin(rad)).normalized;

            float sideWeight = 1f - Mathf.Abs(Vector2.Dot(localNormal, hullForward));
            float sternWeight = Mathf.Max(0f, Vector2.Dot(localNormal, -hullForward));
            float windWeight = Mathf.Max(0f, Vector2.Dot(localNormal, windFacing));
            float noise = Random.value * 0.18f;
            float score =
                sideWeight * idleRippleSideBias
                + sternWeight * idleRippleSternBias
                + windWeight * idleRippleWindwardBias
                + noise;

            if (score > bestScore)
            {
                bestScore = score;
                bestAngle = angle;
            }
        }

        return bestAngle;
    }

    Vector2 GetEllipseOutwardNormal(
        Vector2 origin,
        Vector2 point,
        Vector2 hullForward,
        Vector2 hullSide,
        float halfLength,
        float halfWidth)
    {
        Vector2 delta = point - origin;
        float sideComponent = Vector2.Dot(delta, hullSide) / Mathf.Max(halfWidth, 0.0001f);
        float forwardComponent = Vector2.Dot(delta, hullForward) / Mathf.Max(halfLength, 0.0001f);
        Vector2 gradient = hullSide * (sideComponent / Mathf.Max(halfWidth, 0.0001f))
                         + hullForward * (forwardComponent / Mathf.Max(halfLength, 0.0001f));

        if (gradient.sqrMagnitude < 0.0001f)
            return (point - origin).sqrMagnitude >= 0.0001f ? (point - origin).normalized : hullSide;

        return gradient.normalized;
    }

    Vector2 GetWakeSpawnCenter(WaterDisturbanceSource source, Vector2 movementDirection)
    {
        return source.GetWakeSpawnWorldPosition(movementDirection);
    }

    Vector2 GetMovementDirection(WaterDisturbanceSource source)
    {
        Vector2 direction = source.CurrentDirection;
        if (direction.sqrMagnitude >= 0.0001f)
            return direction.normalized;

        Vector2 fallback = source.TargetTransform.up;
        if (fallback.sqrMagnitude < 0.0001f)
            return Vector2.up;

        return fallback.normalized;
    }

    Vector2 GetAmbientDriftDirection()
    {
        Vector2 baseDirection = GetWindDirection();
        float noiseSample = Mathf.PerlinNoise(Time.time * ambientDirectionNoiseFrequency, 0.137f) - 0.5f;
        float jitterDegrees = noiseSample * 2f * ambientDirectionNoiseDegrees;
        return Rotate(baseDirection, jitterDegrees).normalized;
    }

    Vector2 GetWindDirection()
    {
        if (windController == null)
            return Vector2.up;

        float windRadians = windController.WindAngle * Mathf.Deg2Rad;
        Vector2 windDirection = new Vector2(Mathf.Sin(windRadians), Mathf.Cos(windRadians));
        if (windDirection.sqrMagnitude < 0.0001f)
            return Vector2.up;

        return windDirection.normalized;
    }

    float GetWindSpeed01()
    {
        if (windController == null)
            return 0f;

        return Mathf.Clamp01(windController.WindSpeed);
    }

    float GetBaselineDisturbance(float windSpeed)
    {
        return Mathf.Lerp(fieldBaselineDisturbanceAtCalm, fieldBaselineDisturbanceAtStrongWind, windSpeed);
    }

    float GetSpeedFactor(float speed, float minThreshold)
    {
        float effectiveFullSpeed = Mathf.Max(disturbanceFullSpeed, minThreshold + 0.01f);
        return Mathf.Clamp01((speed - minThreshold) / Mathf.Max(effectiveFullSpeed - minThreshold, 0.01f));
    }

    Camera ResolveCamera()
    {
        if (targetCamera != null)
            return targetCamera;

        return Camera.main;
    }

    Vector3 ResolveVisibleAreaCenter(Camera cameraToUse)
    {
        if (visibleAreaCenterTarget != null)
            return visibleAreaCenterTarget.position;

        return cameraToUse.transform.position;
    }

    Vector2 GetCameraWorldSize(Camera cameraToUse)
    {
        if (!cameraToUse.orthographic)
            return new Vector2(20f, 20f);

        float height = cameraToUse.orthographicSize * 2f;
        float width = height * cameraToUse.aspect;
        return new Vector2(width, height);
    }

    FieldSample SampleField(Vector2 worldPosition)
    {
        if (!fieldInitialized || fieldStrength == null || fieldFlow == null)
            return new FieldSample(GetBaselineDisturbance(GetWindSpeed01()), GetWindDirection());

        float gx = worldPosition.x / fieldCellWorldSize - fieldMinX;
        float gy = worldPosition.y / fieldCellWorldSize - fieldMinY;
        int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, fieldWidth - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(gy), 0, fieldHeight - 1);
        int x1 = Mathf.Clamp(x0 + 1, 0, fieldWidth - 1);
        int y1 = Mathf.Clamp(y0 + 1, 0, fieldHeight - 1);
        float tx = Mathf.Clamp01(gx - x0);
        float ty = Mathf.Clamp01(gy - y0);

        int i00 = GetFieldIndex(x0, y0, fieldWidth);
        int i10 = GetFieldIndex(x1, y0, fieldWidth);
        int i01 = GetFieldIndex(x0, y1, fieldWidth);
        int i11 = GetFieldIndex(x1, y1, fieldWidth);

        float s0 = Mathf.Lerp(fieldStrength[i00], fieldStrength[i10], tx);
        float s1 = Mathf.Lerp(fieldStrength[i01], fieldStrength[i11], tx);
        float strength = Mathf.Lerp(s0, s1, ty);

        Vector2 f0 = Vector2.Lerp(fieldFlow[i00], fieldFlow[i10], tx);
        Vector2 f1 = Vector2.Lerp(fieldFlow[i01], fieldFlow[i11], tx);
        Vector2 direction = Vector2.Lerp(f0, f1, ty);
        if (direction.sqrMagnitude < 0.0001f)
            direction = GetWindDirection();

        return new FieldSample(strength, direction.normalized);
    }

    Vector2 GetCellWorldPosition(int localX, int localY)
    {
        return new Vector2(
            (fieldMinX + localX + 0.5f) * fieldCellWorldSize,
            (fieldMinY + localY + 0.5f) * fieldCellWorldSize);
    }

    static int GetFieldIndex(int x, int y, int width)
    {
        return y * width + x;
    }

    static Vector2 Rotate(Vector2 vector, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(
            vector.x * cos - vector.y * sin,
            vector.x * sin + vector.y * cos);
    }

    void UpdateDebugMirrors()
    {
        debugRegisteredSourceCount = registeredSources.Count;
        debugRegisteredObstacleCount = registeredObstacles.Count;
        debugWindDirection = GetWindDirection();
        debugWindSpeed = GetWindSpeed01();
        debugFieldOrigin = new Vector2(fieldMinX * fieldCellWorldSize, fieldMinY * fieldCellWorldSize);
        debugFieldDimensions = new Vector2Int(fieldWidth, fieldHeight);
        debugFieldCellWorldSize = fieldCellWorldSize;
        debugSimAccumulator = simulationAccumulator;
    }

    readonly struct FieldSample
    {
        public readonly float strength;
        public readonly Vector2 direction;

        public FieldSample(float strength, Vector2 direction)
        {
            this.strength = strength;
            this.direction = direction;
        }
    }
}
