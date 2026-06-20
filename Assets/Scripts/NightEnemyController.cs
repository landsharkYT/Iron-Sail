using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(NightEnemyHealth))]
public class NightEnemyController : MonoBehaviour
{
    enum EnemyState
    {
        Idle,
        Alert,
        Aggro,
        Hit,
        Retreat
    }

    [Header("References")]
    [SerializeField] Rigidbody2D targetRb;
    [SerializeField] NightEnemyHealth enemyHealth;
    [SerializeField] Transform visualRoot;
    [SerializeField] SpriteRenderer spriteRenderer;
    [SerializeField] CircleCollider2D bodyCollider;
    [SerializeField] CircleCollider2D contactTrigger;
    [SerializeField] DayNightTintGroup dayNightTintGroup;
    [SerializeField] WaterDisturbanceSource waterDisturbanceSource;
    [SerializeField] EnemyHitAudio enemyHitAudio;

    [Header("Motion")]
    [SerializeField] float knockbackDecayPerSecond = 7.5f;
    [SerializeField] float visualSmoothing = 12f;
    [SerializeField] float waypointArrivalDistance = 0.85f;
    [SerializeField] float baseReplanIntervalSeconds = 0.45f;
    [SerializeField] float replanJitterSeconds = 0.2f;
    [SerializeField] float replanGoalMoveDistance = 2.5f;

    [Header("Water Disturbance")]
    [SerializeField] float baseWaterDisturbanceStrength = 0.22f;
    [SerializeField] float waterDisturbanceSizeMultiplier = 0.42f;
    [SerializeField] float waterDisturbanceMinSpeedThreshold = 0.05f;
    [SerializeField] float alertWaterDisturbanceMultiplier = 1.15f;
    [SerializeField] float aggroWaterDisturbanceMultiplier = 1.35f;
    [SerializeField] float retreatWaterDisturbanceMultiplier = 1.2f;
    [SerializeField] float waterWakeSpawnOffset = 0.2f;
    [SerializeField] Vector3 localWaterWakeOffset = new Vector3(0f, -0.08f, 0f);

    [Header("Ambient FX")]
    [SerializeField] int ambientFxSortingOrder = 2;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] string debugState = "";
    [SerializeField] bool debugIsChasing;
    [SerializeField] float debugDistanceToBoat;
    [SerializeField] Vector2 debugDesiredVelocity;
    [SerializeField] Vector2 debugKnockbackVelocity;
    [SerializeField] string debugConfigName = "";
    [SerializeField] bool debugPathMode;
    [SerializeField] int debugWaypointCount;
    [SerializeField] int debugWaypointIndex;

    NightEnemyConfig config;
    NightEnemySpawner owningSpawner;
    EnemyPathfindingController pathfindingController;
    Transform boatTransform;
    BoatHealthController boatHealthController;
    Vector2 spawnOrigin;
    Vector2 knockbackVelocity;
    Vector2 retreatDirection;
    Vector2 lastBoatDirection = Vector2.up;
    Color baseVisualTint = Color.white;
    float flashTimer;
    float noiseSeed;
    float nextBoatDamageTime;
    float stateTimer;
    float aggroSurgeTimer;
    bool hasInitialized;
    bool isDying;
    EnemyState currentState;
    EnemyState interruptedState;
    ParticleSystem ambientParticles;
    ParticleSystemRenderer ambientParticleRenderer;
    Transform ambientFxRoot;
    Color ambientBaseColor = Color.white;
    float ambientNightReadabilityBoost;
    float nextPathReplanTime;
    static Material ambientFxMaterial;
    readonly List<Vector2> pathWaypoints = new List<Vector2>();
    int currentWaypointIndex;
    bool usingPathMode;
    Vector2 lastPathGoalPosition;

    public NightEnemyConfig Config => config;
    public Vector2 Position => targetRb != null ? targetRb.position : (Vector2)transform.position;
    public float CurrentHealth => enemyHealth != null ? enemyHealth.CurrentHealth : 0f;

    // Save-restore seam: place a freshly-spawned enemy at its saved position and
    // health. Velocity/aggro/path state are transient and re-derive on their own.
    public void RestoreState(Vector2 position, float health)
    {
        transform.position = new Vector3(position.x, position.y, transform.position.z);
        if (targetRb != null)
            targetRb.position = position;
        if (enemyHealth != null)
            enemyHealth.SetCurrentHealth(health);
    }

    void Awake()
    {
        if (targetRb == null)
            targetRb = GetComponent<Rigidbody2D>();
        if (enemyHealth == null)
            enemyHealth = GetComponent<NightEnemyHealth>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        if (dayNightTintGroup == null)
            dayNightTintGroup = GetComponent<DayNightTintGroup>();
        if (dayNightTintGroup != null)
            dayNightTintGroup.enabled = false;
        if (waterDisturbanceSource == null)
            waterDisturbanceSource = GetComponent<WaterDisturbanceSource>();
        if (waterDisturbanceSource == null)
            waterDisturbanceSource = gameObject.AddComponent<WaterDisturbanceSource>();
        if (enemyHitAudio == null)
            enemyHitAudio = GetComponent<EnemyHitAudio>();
        if (pathfindingController == null)
            pathfindingController = EnemyPathfindingController.ActiveInstance;
        if (visualRoot == null && spriteRenderer != null)
            visualRoot = spriteRenderer.transform;

        CircleCollider2D[] colliders = GetComponents<CircleCollider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            CircleCollider2D circleCollider = colliders[i];
            if (circleCollider == null)
                continue;

            if (circleCollider.isTrigger)
                contactTrigger = circleCollider;
            else
                bodyCollider = circleCollider;
        }

        noiseSeed = Random.Range(0f, 1000f);
    }

    void Update()
    {
        UpdateVisuals();
    }

    void FixedUpdate()
    {
        if (!hasInitialized || isDying || targetRb == null || config == null)
            return;

        if (boatTransform == null)
        {
            Despawn(playEffect: false);
            return;
        }

        Vector2 boatPosition = boatTransform.position;
        Vector2 currentPosition = targetRb.position;
        Vector2 toBoat = boatPosition - currentPosition;
        float distanceToBoat = toBoat.magnitude;
        if (toBoat.sqrMagnitude > 0.0001f)
            lastBoatDirection = toBoat.normalized;

        debugDistanceToBoat = distanceToBoat;
        TickStateTimers(Time.fixedDeltaTime, distanceToBoat);
        UpdateUntimedTransitions(distanceToBoat);
        UpdatePathMode(currentPosition, boatPosition);

        if (distanceToBoat > config.DespawnRadius)
        {
            Despawn(playEffect: false);
            return;
        }

        Vector2 desiredVelocity = ComputeDesiredVelocity(currentPosition, boatPosition);
        debugDesiredVelocity = desiredVelocity;
        debugKnockbackVelocity = knockbackVelocity;
        debugState = currentState.ToString();
        debugIsChasing = currentState == EnemyState.Aggro || currentState == EnemyState.Alert;
        debugPathMode = usingPathMode;
        debugWaypointCount = pathWaypoints.Count;
        debugWaypointIndex = currentWaypointIndex;
        UpdateWaterDisturbanceProfile();

        float responsiveness = ResolveResponsiveness();
        float blend = 1f - Mathf.Exp(-(config.Acceleration * responsiveness) * Time.fixedDeltaTime);
        Vector2 targetVelocity = desiredVelocity + knockbackVelocity;
        targetRb.linearVelocity = Vector2.Lerp(targetRb.linearVelocity, targetVelocity, blend);
        knockbackVelocity = Vector2.MoveTowards(knockbackVelocity, Vector2.zero, knockbackDecayPerSecond * Time.fixedDeltaTime);
    }

    public void Initialize(
        NightEnemyConfig enemyConfig,
        Transform targetBoatTransform,
        BoatHealthController targetBoatHealthController,
        NightEnemySpawner spawner)
    {
        config = enemyConfig;
        boatTransform = targetBoatTransform;
        boatHealthController = targetBoatHealthController;
        owningSpawner = spawner;
        spawnOrigin = targetRb != null ? targetRb.position : (Vector2)transform.position;
        nextBoatDamageTime = 0f;
        debugConfigName = config != null ? config.name : "";
        hasInitialized = config != null && enemyHealth != null;
        pathfindingController = EnemyPathfindingController.ActiveInstance;

        if (!hasInitialized)
            return;

        ApplyVisualSetup();
        ApplyColliderSetup();
        ConfigureAmbientFx();
        DisablePrefabTintGroup();
        ConfigureWaterDisturbance();
        enemyHealth.Initialize(this, config.MaxHealth);
        EnterState(EnemyState.Idle);
    }

    public void HandleProjectileDamage(Vector2 hitPoint, Vector2 impactDirection)
    {
        if (config == null || isDying)
            return;

        Vector2 resolvedDirection = impactDirection.sqrMagnitude > 0.0001f
            ? impactDirection.normalized
            : Vector2.up;

        enemyHitAudio?.PlayHit();
        knockbackVelocity += resolvedDirection * config.ProjectileKnockbackImpulse;
        flashTimer = config.DamageFlashDuration;

        EnemyState resumeState = currentState == EnemyState.Hit ? interruptedState : currentState;
        interruptedState = resumeState;
        EnterState(EnemyState.Hit);
    }

    public void HandleDeath(Vector2 hitPoint, Vector2 impactDirection)
    {
        if (isDying)
            return;

        isDying = true;
        AwardDeathRewards();
        SpawnEffect(config != null ? config.DeathEffectPrefab : null, hitPoint);

        if (bodyCollider != null)
            bodyCollider.enabled = false;
        if (contactTrigger != null)
            contactTrigger.enabled = false;
        if (targetRb != null)
            targetRb.simulated = false;
        if (waterDisturbanceSource != null)
            waterDisturbanceSource.enabled = false;

        Destroy(gameObject);
    }

    public void Despawn(bool playEffect)
    {
        if (isDying)
            return;

        isDying = true;
        if (playEffect && config != null)
            SpawnEffect(config.DeathEffectPrefab, Position);
        if (waterDisturbanceSource != null)
            waterDisturbanceSource.enabled = false;

        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (owningSpawner != null)
            owningSpawner.NotifyEnemyDestroyed(this);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        TryDamageBoat(other);
    }

    void OnTriggerStay2D(Collider2D other)
    {
        TryDamageBoat(other);
    }

    void TryDamageBoat(Collider2D other)
    {
        if (!hasInitialized || isDying || config == null || boatHealthController == null || other == null)
            return;
        if (Time.time < nextBoatDamageTime)
            return;

        BoatHealthController contactedBoatHealth = other.GetComponentInParent<BoatHealthController>();
        if (contactedBoatHealth == null || contactedBoatHealth != boatHealthController)
            return;

        Vector2 contactPoint = other.ClosestPoint(Position);
        boatHealthController.TakeDamage(config.ContactDamage, BoatDamageSource.EnemyContact);
        nextBoatDamageTime = Time.time + config.ContactCooldownSeconds;

        SpawnEffect(config.ContactSplashPrefab, contactPoint);
        SpawnEffect(config.ContactBurstPrefab, contactPoint);

        Vector2 bounceDirection = (Position - contactPoint).normalized;
        if (bounceDirection.sqrMagnitude <= 0.0001f && boatTransform != null)
            bounceDirection = (Position - (Vector2)boatTransform.position).normalized;
        if (bounceDirection.sqrMagnitude <= 0.0001f)
            bounceDirection = Vector2.up;

        knockbackVelocity += bounceDirection * config.BoatBounceImpulse;
        retreatDirection = ComputeRetreatDirection(bounceDirection);
        EnterState(EnemyState.Retreat);
    }

    void TickStateTimers(float deltaTime, float distanceToBoat)
    {
        if (flashTimer > 0f)
            flashTimer = Mathf.Max(0f, flashTimer - Time.deltaTime);
        if (aggroSurgeTimer > 0f)
            aggroSurgeTimer = Mathf.Max(0f, aggroSurgeTimer - deltaTime);
        if (stateTimer <= 0f)
            return;

        stateTimer = Mathf.Max(0f, stateTimer - deltaTime);
        if (stateTimer > 0f)
            return;

        switch (currentState)
        {
            case EnemyState.Alert:
                EnterState(EnemyState.Aggro);
                break;
            case EnemyState.Hit:
                ResumeInterruptedState(distanceToBoat);
                break;
            case EnemyState.Retreat:
                EnterState(distanceToBoat <= config.LoseRadius ? EnemyState.Alert : EnemyState.Idle);
                break;
        }
    }

    void UpdateUntimedTransitions(float distanceToBoat)
    {
        switch (currentState)
        {
            case EnemyState.Idle:
                if (distanceToBoat <= config.AggroRadius)
                    EnterState(EnemyState.Alert);
                break;
            case EnemyState.Alert:
                if (distanceToBoat >= config.LoseRadius)
                    EnterState(EnemyState.Idle);
                break;
            case EnemyState.Aggro:
                if (distanceToBoat >= config.LoseRadius)
                    EnterState(EnemyState.Idle);
                break;
        }
    }

    void ResumeInterruptedState(float distanceToBoat)
    {
        if (distanceToBoat > config.LoseRadius)
        {
            EnterState(EnemyState.Idle);
            return;
        }

        switch (interruptedState)
        {
            case EnemyState.Retreat:
                EnterState(EnemyState.Retreat);
                break;
            case EnemyState.Aggro:
                EnterState(EnemyState.Aggro);
                break;
            case EnemyState.Alert:
                EnterState(EnemyState.Alert);
                break;
            default:
                EnterState(distanceToBoat <= config.AggroRadius ? EnemyState.Alert : EnemyState.Idle);
                break;
        }
    }

    void EnterState(EnemyState newState)
    {
        if (newState != EnemyState.Alert && newState != EnemyState.Aggro)
            ClearPathMode();

        currentState = newState;

        switch (newState)
        {
            case EnemyState.Idle:
                stateTimer = 0f;
                aggroSurgeTimer = 0f;
                break;
            case EnemyState.Alert:
                stateTimer = config.AlertDuration;
                break;
            case EnemyState.Aggro:
                stateTimer = 0f;
                aggroSurgeTimer = config.AggroSurgeDuration;
                break;
            case EnemyState.Hit:
                stateTimer = config.HitStateDuration;
                break;
            case EnemyState.Retreat:
                stateTimer = config.RetreatDuration;
                break;
        }
    }

    Vector2 ComputeDesiredVelocity(Vector2 currentPosition, Vector2 boatPosition)
    {
        switch (currentState)
        {
            case EnemyState.Alert:
                return ComputeAlertVelocity(currentPosition, boatPosition);
            case EnemyState.Aggro:
                return ComputeAggroVelocity(currentPosition, boatPosition);
            case EnemyState.Hit:
                return Vector2.zero;
            case EnemyState.Retreat:
                return ComputeRetreatVelocity(currentPosition, boatPosition);
            default:
                return ComputeIdleVelocity(currentPosition);
        }
    }

    float ResolveResponsiveness()
    {
        switch (currentState)
        {
            case EnemyState.Alert:
                return config.AlertResponsiveness;
            case EnemyState.Aggro:
                return config.ChaseResponsiveness;
            case EnemyState.Hit:
                return config.ChaseResponsiveness;
            case EnemyState.Retreat:
                return config.RetreatResponsiveness;
            default:
                return config.IdleDriftResponsiveness;
        }
    }

    Vector2 ComputeIdleVelocity(Vector2 currentPosition)
    {
        Vector2 axis = GetPrimaryAxis();
        Vector2 perpendicular = new Vector2(-axis.y, axis.x);
        float time = Time.time + noiseSeed;

        Vector2 idleOffset =
            axis * Mathf.Sin(time * config.IdleFrequency) * config.IdlePrimaryAmplitude +
            perpendicular * Mathf.Sin(time * (config.IdleFrequency * 0.63f) + noiseSeed) * config.IdleSecondaryAmplitude;

        Vector2 idleTarget = spawnOrigin + idleOffset;
        Vector2 toIdleTarget = idleTarget - currentPosition;
        if (toIdleTarget.sqrMagnitude <= 0.0001f)
            return Vector2.zero;

        Vector2 direction = toIdleTarget.normalized;
        float burstMultiplier = ResolveBurstMultiplier();
        float floatieAngle = Mathf.Sin((Time.time + noiseSeed) * (config.IdleFrequency * 0.55f)) * config.Floatiness * 20f;
        direction = Rotate(direction, floatieAngle);
        return direction * (config.IdleSpeed * burstMultiplier);
    }

    Vector2 ComputeAlertVelocity(Vector2 currentPosition, Vector2 boatPosition)
    {
        Vector2 pursuitDirection = ResolvePursuitDirection(currentPosition, boatPosition);
        if (pursuitDirection.sqrMagnitude <= 0.0001f)
            return Vector2.zero;

        Vector2 flavoredDirection = ResolveFlavoredDirection(pursuitDirection, 0.45f, 0.55f);
        return flavoredDirection * (config.ChaseSpeed * config.AlertSpeedMultiplier * ResolveBurstMultiplier());
    }

    Vector2 ComputeAggroVelocity(Vector2 currentPosition, Vector2 boatPosition)
    {
        Vector2 pursuitDirection = ResolvePursuitDirection(currentPosition, boatPosition);
        if (pursuitDirection.sqrMagnitude <= 0.0001f)
            return Vector2.zero;

        Vector2 flavoredDirection = ResolveFlavoredDirection(pursuitDirection, 1f, 1f);
        float speedMultiplier = ResolveBurstMultiplier();
        if (aggroSurgeTimer > 0f && config.AggroSurgeDuration > 0f)
        {
            float surge01 = aggroSurgeTimer / config.AggroSurgeDuration;
            speedMultiplier *= Mathf.Lerp(1f, config.AggroSurgeSpeedMultiplier, surge01);
        }

        return flavoredDirection * (config.ChaseSpeed * speedMultiplier);
    }

    Vector2 ComputeRetreatVelocity(Vector2 currentPosition, Vector2 boatPosition)
    {
        Vector2 awayFromBoat = currentPosition - boatPosition;
        if (awayFromBoat.sqrMagnitude <= 0.0001f)
            awayFromBoat = retreatDirection.sqrMagnitude > 0.0001f ? retreatDirection : lastBoatDirection;

        Vector2 direction = retreatDirection.sqrMagnitude > 0.0001f
            ? retreatDirection
            : ComputeRetreatDirection(awayFromBoat.normalized);

        float driftAngle = Mathf.Sin((Time.time + noiseSeed) * 2.1f) * (config.Style == NightEnemyConfig.MovementStyle.Vertical ? 7f : 5f);
        direction = Rotate(direction, driftAngle);
        return direction.normalized * (config.ChaseSpeed * config.RetreatSpeedMultiplier);
    }

    Vector2 ResolveFlavoredDirection(Vector2 chaseDirection, float waveStrength, float noiseStrength)
    {
        Vector2 axis = GetPrimaryAxis();
        Vector2 perpendicular = new Vector2(-axis.y, axis.x);
        float time = Time.time + noiseSeed;
        float wobble = Mathf.Sin(time * config.ChaseWaveFrequency) * config.ChaseWaveAmplitude * waveStrength;
        float noise = (Mathf.PerlinNoise(noiseSeed, time * 0.35f) * 2f - 1f) * config.ChaseNoiseAmplitude * noiseStrength;
        Vector2 flavoredDirection = chaseDirection + axis * wobble + perpendicular * noise;
        if (flavoredDirection.sqrMagnitude <= 0.0001f)
            flavoredDirection = chaseDirection;

        float floatieAngle = Mathf.Sin((time * 0.75f) + noiseSeed) * config.Floatiness * 16f;
        return Rotate(flavoredDirection.normalized, floatieAngle);
    }

    Vector2 ResolvePursuitDirection(Vector2 currentPosition, Vector2 boatPosition)
    {
        if (usingPathMode)
        {
            AdvanceWaypointsIfNeeded(currentPosition);
            if (currentWaypointIndex < pathWaypoints.Count)
            {
                Vector2 toWaypoint = pathWaypoints[currentWaypointIndex] - currentPosition;
                if (toWaypoint.sqrMagnitude > 0.0001f)
                    return toWaypoint.normalized;
            }
        }

        Vector2 toBoat = boatPosition - currentPosition;
        return toBoat.sqrMagnitude > 0.0001f ? toBoat.normalized : Vector2.zero;
    }

    Vector2 ComputeRetreatDirection(Vector2 awayFromBoat)
    {
        Vector2 baseDirection = awayFromBoat.sqrMagnitude > 0.0001f ? awayFromBoat.normalized : Vector2.up;
        float arcSign = Mathf.PerlinNoise(noiseSeed, Time.time * 0.2f) >= 0.5f ? 1f : -1f;
        return Rotate(baseDirection, config.RetreatArcDegrees * arcSign).normalized;
    }

    float ResolveBurstMultiplier()
    {
        float burstStrength = Mathf.Max(0f, config.BurstStrength);
        if (burstStrength <= 0.0001f)
            return 1f;

        float cycle = Mathf.Repeat((Time.time + noiseSeed) * config.BurstFrequency, 1f);
        float activeWindow = Mathf.Clamp01(config.BurstDutyCycle);
        float pulse01 = cycle < activeWindow
            ? Mathf.Sin((cycle / activeWindow) * Mathf.PI)
            : 0f;

        return Mathf.Lerp(1f - burstStrength * 0.45f, 1f + burstStrength, pulse01);
    }

    static Vector2 Rotate(Vector2 vector, float degrees)
    {
        if (vector.sqrMagnitude <= 0.0001f || Mathf.Abs(degrees) <= 0.0001f)
            return vector;

        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(
            vector.x * cos - vector.y * sin,
            vector.x * sin + vector.y * cos);
    }

    Vector2 GetPrimaryAxis()
    {
        if (config == null)
            return Vector2.up;

        return config.Style == NightEnemyConfig.MovementStyle.Vertical
            ? Vector2.up
            : Vector2.right;
    }

    void ApplyVisualSetup()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = config.Sprite;
            baseVisualTint = config.VisualTint;
            spriteRenderer.color = ResolveDisplayColor(baseVisualTint);
        }

        if (visualRoot != null)
        {
            visualRoot.localScale = Vector3.one * config.VisualScale;
            visualRoot.localRotation = Quaternion.identity;
        }
    }

    void ApplyColliderSetup()
    {
        if (bodyCollider != null)
            bodyCollider.radius = config.BodyColliderRadius;

        if (contactTrigger != null)
            contactTrigger.radius = config.ContactTriggerRadius;
    }

    void UpdateVisuals()
    {
        if (spriteRenderer == null || visualRoot == null || targetRb == null || config == null)
            return;

        Vector2 velocity = targetRb.linearVelocity;
        if (Mathf.Abs(velocity.x) > 0.05f)
            spriteRenderer.flipX = velocity.x < 0f;

        float targetScale = config.VisualScale * ResolveStateScaleMultiplier();
        Vector3 smoothedScale = Vector3.Lerp(
            visualRoot.localScale,
            Vector3.one * targetScale,
            1f - Mathf.Exp(-visualSmoothing * Time.deltaTime));
        visualRoot.localScale = smoothedScale;

        float stateLean = ResolveStateLeanDegrees();
        float tilt = Mathf.Clamp(
            -velocity.x * config.VisualTiltVelocityScale + stateLean,
            -config.MaxVisualTiltDegrees,
            config.MaxVisualTiltDegrees);
        visualRoot.localRotation = Quaternion.Lerp(
            visualRoot.localRotation,
            Quaternion.Euler(0f, 0f, tilt),
            1f - Mathf.Exp(-visualSmoothing * Time.deltaTime));

        Color targetColor = ResolveDisplayColor(ResolveStateColor());
        if (flashTimer > 0f && config.DamageFlashDuration > 0f)
        {
            spriteRenderer.color = ResolveDamageFlashColor();
            UpdateAmbientFx(velocity);
            return;
        }

        spriteRenderer.color = Color.Lerp(
            spriteRenderer.color,
            targetColor,
            1f - Mathf.Exp(-visualSmoothing * Time.deltaTime));

        UpdateAmbientFx(velocity);
    }

    Color ResolveDamageFlashColor()
    {
        if (config == null)
            return baseVisualTint;

        Color damageColor = config.DamageFlashColor;
        damageColor.a = baseVisualTint.a;
        return damageColor;
    }

    float ResolveStateScaleMultiplier()
    {
        switch (currentState)
        {
            case EnemyState.Alert:
                return config.AlertVisualScaleMultiplier;
            case EnemyState.Aggro:
                return config.AggroVisualScaleMultiplier;
            case EnemyState.Hit:
                return config.HitVisualScaleMultiplier;
            case EnemyState.Retreat:
                return config.RetreatVisualScaleMultiplier;
            default:
                return 1f;
        }
    }

    float ResolveStateLeanDegrees()
    {
        switch (currentState)
        {
            case EnemyState.Alert:
                return config.Style == NightEnemyConfig.MovementStyle.Vertical ? 1.5f : 0.75f;
            case EnemyState.Aggro:
                return config.Style == NightEnemyConfig.MovementStyle.Vertical ? 2f : 1.25f;
            case EnemyState.Hit:
                return config.Style == NightEnemyConfig.MovementStyle.Vertical ? -2f : -1.5f;
            case EnemyState.Retreat:
                return config.Style == NightEnemyConfig.MovementStyle.Vertical ? -1.25f : -0.75f;
            default:
                return 0f;
        }
    }

    Color ResolveStateColor()
    {
        float tintStrength = config.StateCueTintStrength;
        if (tintStrength <= 0.0001f)
            return baseVisualTint;

        switch (currentState)
        {
            case EnemyState.Alert:
                return Color.Lerp(baseVisualTint, Color.white, tintStrength * 0.55f);
            case EnemyState.Aggro:
                return Color.Lerp(
                    baseVisualTint,
                    config.Style == NightEnemyConfig.MovementStyle.Vertical
                        ? new Color(1f, 0.92f, 0.96f, 1f)
                        : new Color(0.9f, 1f, 0.94f, 1f),
                    tintStrength);
            case EnemyState.Retreat:
                return Color.Lerp(baseVisualTint, Color.white, tintStrength * 0.3f);
            default:
                return baseVisualTint;
        }
    }

    void SpawnEffect(GameObject effectPrefab, Vector2 worldPosition)
    {
        if (effectPrefab == null)
            return;

        Instantiate(effectPrefab, new Vector3(worldPosition.x, worldPosition.y, 0f), Quaternion.identity);
    }

    void ConfigureWaterDisturbance()
    {
        if (waterDisturbanceSource == null)
            return;

        waterDisturbanceSource.ConfigureWakeMoverProfile(
            transform,
            targetRb,
            baseWaterDisturbanceStrength,
            waterDisturbanceSizeMultiplier,
            waterDisturbanceMinSpeedThreshold,
            waterWakeSpawnOffset,
            localWaterWakeOffset,
            false);
    }

    void UpdateWaterDisturbanceProfile()
    {
        if (waterDisturbanceSource == null)
            return;

        float multiplier = 1f;
        switch (currentState)
        {
            case EnemyState.Alert:
                multiplier = alertWaterDisturbanceMultiplier;
                break;
            case EnemyState.Aggro:
                multiplier = aggroWaterDisturbanceMultiplier;
                break;
            case EnemyState.Retreat:
                multiplier = retreatWaterDisturbanceMultiplier;
                break;
        }

        waterDisturbanceSource.SetRuntimeDisturbanceProfile(
            baseWaterDisturbanceStrength * multiplier,
            waterDisturbanceSizeMultiplier,
            waterDisturbanceMinSpeedThreshold);
    }

    void UpdatePathMode(Vector2 currentPosition, Vector2 boatPosition)
    {
        bool chaseRelevantState = currentState == EnemyState.Alert || currentState == EnemyState.Aggro;
        if (!chaseRelevantState)
        {
            ClearPathMode();
            return;
        }

        if (pathfindingController == null)
            pathfindingController = EnemyPathfindingController.ActiveInstance;
        if (pathfindingController == null)
        {
            ClearPathMode();
            return;
        }

        if (pathfindingController.HasDirectWaterLine(currentPosition, boatPosition))
        {
            ClearPathMode();
            return;
        }

        AdvanceWaypointsIfNeeded(currentPosition);

        bool shouldReplan = !usingPathMode
                            || currentWaypointIndex >= pathWaypoints.Count
                            || Time.time >= nextPathReplanTime
                            || (boatPosition - lastPathGoalPosition).sqrMagnitude >= replanGoalMoveDistance * replanGoalMoveDistance;

        if (!shouldReplan)
            return;

        if (pathfindingController.TryBuildPath(currentPosition, boatPosition, out List<Vector2> newPath))
        {
            pathWaypoints.Clear();
            pathWaypoints.AddRange(newPath);
            currentWaypointIndex = 0;
            usingPathMode = pathWaypoints.Count > 0;
            AdvanceWaypointsIfNeeded(currentPosition);
        }
        else
        {
            ClearPathMode();
        }

        lastPathGoalPosition = boatPosition;
        nextPathReplanTime = Time.time + baseReplanIntervalSeconds + ResolveReplanJitterSeconds();
    }

    void AdvanceWaypointsIfNeeded(Vector2 currentPosition)
    {
        float arrivalDistanceSqr = waypointArrivalDistance * waypointArrivalDistance;
        while (currentWaypointIndex < pathWaypoints.Count)
        {
            if ((pathWaypoints[currentWaypointIndex] - currentPosition).sqrMagnitude > arrivalDistanceSqr)
                break;

            currentWaypointIndex++;
        }

        if (currentWaypointIndex >= pathWaypoints.Count)
            usingPathMode = false;
    }

    void ClearPathMode()
    {
        usingPathMode = false;
        pathWaypoints.Clear();
        currentWaypointIndex = 0;
    }

    float ResolveReplanJitterSeconds()
    {
        if (replanJitterSeconds <= 0.0001f)
            return 0f;

        return Mathf.Repeat(noiseSeed * 0.173f, 1f) * replanJitterSeconds;
    }

    void ConfigureAmbientFx()
    {
        if (config == null)
            return;

        if (ambientFxRoot == null)
        {
            GameObject ambientRootObject = new GameObject("AmbientFX");
            ambientRootObject.transform.SetParent(transform, false);
            ambientFxRoot = ambientRootObject.transform;
        }

        ambientFxRoot.localPosition = Vector3.zero;

        if (ambientParticles == null)
            ambientParticles = ambientFxRoot.GetComponent<ParticleSystem>();
        if (ambientParticles == null)
            ambientParticles = ambientFxRoot.gameObject.AddComponent<ParticleSystem>();

        if (ambientParticleRenderer == null)
            ambientParticleRenderer = ambientFxRoot.GetComponent<ParticleSystemRenderer>();
        if (ambientParticleRenderer == null)
            ambientParticleRenderer = ambientFxRoot.gameObject.AddComponent<ParticleSystemRenderer>();

        ambientParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ambientParticles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 64;

        var emission = ambientParticles.emission;
        emission.enabled = true;

        var shape = ambientParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = config.Style == NightEnemyConfig.MovementStyle.Vertical ? 0.06f : 0.03f;

        var velocityOverLifetime = ambientParticles.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.space = ParticleSystemSimulationSpace.World;

        var colorOverLifetime = ambientParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;

        var sizeOverLifetime = ambientParticles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;

        if (config.Style == NightEnemyConfig.MovementStyle.Vertical)
        {
            ConfigureInkTrail(main, emission, velocityOverLifetime, colorOverLifetime, sizeOverLifetime);
            ambientBaseColor = new Color(0.10f, 0.08f, 0.14f, 0.72f);
        }
        else
        {
            ConfigureBubbleTrail(main, emission, velocityOverLifetime, colorOverLifetime, sizeOverLifetime);
            ambientBaseColor = new Color(0.82f, 0.93f, 1f, 0.85f);
        }

        ambientNightReadabilityBoost = config.ParticleNightReadabilityBoost;

        ambientParticleRenderer.sortingLayerName = spriteRenderer != null ? spriteRenderer.sortingLayerName : "Default";
        ambientParticleRenderer.sortingOrder = ambientFxSortingOrder;
        ambientParticleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        ambientParticleRenderer.sharedMaterial = GetOrCreateAmbientFxMaterial();

        ambientParticles.Play();
    }

    void DisablePrefabTintGroup()
    {
        if (dayNightTintGroup == null)
            return;

        // The enemy controller computes its sprite tint dynamically each frame to
        // combine state cues and day/night lighting. Disable the prefab tint group
        // so it does not fight that color path.
        dayNightTintGroup.enabled = false;
    }

    void ConfigureInkTrail(
        ParticleSystem.MainModule main,
        ParticleSystem.EmissionModule emission,
        ParticleSystem.VelocityOverLifetimeModule velocityOverLifetime,
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime,
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime)
    {
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
        main.startColor = new Color(0.08f, 0.06f, 0.1f, 0.7f);

        emission.rateOverTime = 14f;

        velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
        velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(-0.08f, 0.05f);
        velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.08f, 0.06f, 0.1f), 0f),
                new GradientColorKey(new Color(0.12f, 0.1f, 0.15f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.62f, 0f),
                new GradientAlphaKey(0.22f, 0.45f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.7f),
            new Keyframe(0.4f, 1.1f),
            new Keyframe(1f, 1.45f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
    }

    void ConfigureBubbleTrail(
        ParticleSystem.MainModule main,
        ParticleSystem.EmissionModule emission,
        ParticleSystem.VelocityOverLifetimeModule velocityOverLifetime,
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime,
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime)
    {
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.12f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startColor = new Color(0.82f, 0.93f, 1f, 0.85f);

        emission.rateOverTime = 10f;

        velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);
        velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
        velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.88f, 0.97f, 1f), 0f),
                new GradientColorKey(new Color(0.72f, 0.88f, 1f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.82f, 0f),
                new GradientAlphaKey(0.36f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.55f),
            new Keyframe(0.5f, 1f),
            new Keyframe(1f, 1.18f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
    }

    void UpdateAmbientFx(Vector2 velocity)
    {
        if (ambientParticles == null)
            return;

        Vector2 normalizedVelocity = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : Vector2.zero;
        float speed = velocity.magnitude;

        if (ambientFxRoot != null)
        {
            Vector2 offset = normalizedVelocity.sqrMagnitude > 0.0001f
                ? -normalizedVelocity * (config.Style == NightEnemyConfig.MovementStyle.Vertical ? 0.28f : 0.22f)
                : Vector2.zero;
            ambientFxRoot.position = new Vector3(Position.x + offset.x, Position.y + offset.y, 0f);
        }

        var emission = ambientParticles.emission;
        float baseRate = config.Style == NightEnemyConfig.MovementStyle.Vertical ? 14f : 10f;
        float stateRateMultiplier = currentState == EnemyState.Aggro ? 1.15f : currentState == EnemyState.Alert ? 0.85f : 1f;
        emission.rateOverTime = speed > 0.05f
            ? baseRate * Mathf.Clamp(speed / Mathf.Max(0.01f, config.ChaseSpeed), 0.35f, 1.35f) * stateRateMultiplier
            : 0f;

        Color ambientColor = ResolveAmbientColor();
        var main = ambientParticles.main;
        main.startColor = ambientColor;
    }

    Color ResolveAmbientColor()
    {
        if (DayNightLightingController.ActiveController == null || config == null)
            return ambientBaseColor;

        float brightness = DayNightLightingController.ActiveController.CurrentSpriteTintBrightness;
        Color tint = DayNightLightingController.ActiveController.CurrentSpriteTintColor;
        float duskNightFactor = Mathf.Clamp01((1f - brightness) / 0.35f);
        Color liftedTint = Color.Lerp(tint, config.NightLiftColor, ambientNightReadabilityBoost * duskNightFactor);
        float finalBrightness = Mathf.Max(brightness, Mathf.Lerp(0f, config.NightBrightnessFloor, duskNightFactor * 0.6f));

        Color finalColor = ambientBaseColor;
        finalColor.r = Mathf.Clamp01(ambientBaseColor.r * liftedTint.r * finalBrightness);
        finalColor.g = Mathf.Clamp01(ambientBaseColor.g * liftedTint.g * finalBrightness);
        finalColor.b = Mathf.Clamp01(ambientBaseColor.b * liftedTint.b * finalBrightness);
        finalColor.a = ambientBaseColor.a;
        return finalColor;
    }

    Color ResolveDisplayColor(Color untintedColor)
    {
        if (config == null || DayNightLightingController.ActiveController == null)
            return untintedColor;

        float brightness = DayNightLightingController.ActiveController.CurrentSpriteTintBrightness;
        Color tint = DayNightLightingController.ActiveController.CurrentSpriteTintColor;
        float duskNightFactor = Mathf.Clamp01((1f - brightness) / 0.35f);
        Color liftedTint = Color.Lerp(tint, config.NightLiftColor, config.NightReadabilityBoost * duskNightFactor);
        float finalBrightness = Mathf.Max(brightness, Mathf.Lerp(0f, config.NightBrightnessFloor, duskNightFactor));

        Color finalColor = untintedColor;
        finalColor.r = Mathf.Clamp01(untintedColor.r * liftedTint.r * finalBrightness);
        finalColor.g = Mathf.Clamp01(untintedColor.g * liftedTint.g * finalBrightness);
        finalColor.b = Mathf.Clamp01(untintedColor.b * liftedTint.b * finalBrightness);
        finalColor.a = untintedColor.a;
        return finalColor;
    }

    void OnValidate()
    {
        knockbackDecayPerSecond = Mathf.Max(0f, knockbackDecayPerSecond);
        visualSmoothing = Mathf.Max(0.01f, visualSmoothing);
        waypointArrivalDistance = Mathf.Max(0.1f, waypointArrivalDistance);
        baseReplanIntervalSeconds = Mathf.Max(0.05f, baseReplanIntervalSeconds);
        replanJitterSeconds = Mathf.Max(0f, replanJitterSeconds);
        replanGoalMoveDistance = Mathf.Max(0.1f, replanGoalMoveDistance);
        baseWaterDisturbanceStrength = Mathf.Max(0f, baseWaterDisturbanceStrength);
        waterDisturbanceSizeMultiplier = Mathf.Max(0.05f, waterDisturbanceSizeMultiplier);
        waterDisturbanceMinSpeedThreshold = Mathf.Max(0f, waterDisturbanceMinSpeedThreshold);
        alertWaterDisturbanceMultiplier = Mathf.Max(0f, alertWaterDisturbanceMultiplier);
        aggroWaterDisturbanceMultiplier = Mathf.Max(0f, aggroWaterDisturbanceMultiplier);
        retreatWaterDisturbanceMultiplier = Mathf.Max(0f, retreatWaterDisturbanceMultiplier);
        waterWakeSpawnOffset = Mathf.Max(0f, waterWakeSpawnOffset);
    }

    static Material GetOrCreateAmbientFxMaterial()
    {
        if (ambientFxMaterial != null)
            return ambientFxMaterial;

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader == null)
            return null;

        ambientFxMaterial = new Material(spriteShader)
        {
            name = "NightEnemyAmbientFxMaterial"
        };
        return ambientFxMaterial;
    }

    void AwardDeathRewards()
    {
        if (config == null)
            return;

        ShipInventoryController inventory = ShipInventoryController.ActiveInventory;
        if (inventory == null)
            return;

        int awardedGold = Mathf.Max(0, config.CoinReward);
        if (awardedGold > 0)
            inventory.AddGold(awardedGold);

        int awardedCannonballs = 0;
        int cannonballOverflow = 0;
        if (Random.value <= config.CannonballRewardChance)
        {
            ItemDefinition cannonballItem = RewardUtility.ResolveCannonballItem();
            if (cannonballItem != null)
            {
                int rolledAmount = RewardUtility.RollWeightedRewardAmount(
                    config.CannonballRewardTwoWeight,
                    config.CannonballRewardThreeWeight,
                    config.CannonballRewardFourWeight);
                if (rolledAmount > 0)
                {
                    inventory.TryAddItem(cannonballItem, rolledAmount, out cannonballOverflow);
                    awardedCannonballs = Mathf.Max(0, rolledAmount - cannonballOverflow);
                }
            }
        }

        string message = BuildRewardMessage(awardedGold, awardedCannonballs, cannonballOverflow);
        RewardUtility.ShowRewardPrompt(message);
    }

    static string BuildRewardMessage(int awardedGold, int awardedCannonballs, int cannonballOverflow)
    {
        if (awardedGold <= 0 && awardedCannonballs <= 0)
            return string.Empty;

        string message;
        if (awardedGold > 0 && awardedCannonballs > 0)
            message = $"You salvage {awardedGold} gold and {awardedCannonballs} cannonballs from the wreckage.";
        else if (awardedGold > 0)
            message = $"You salvage {awardedGold} gold from the wreckage.";
        else
            message = $"You salvage {awardedCannonballs} cannonballs from the wreckage.";

        if (cannonballOverflow > 0)
            message += $"\n{cannonballOverflow} cannonballs were lost to lack of space.";

        return message;
    }
}
