using UnityEngine;

// Marks a moving object as something that should disturb the water surface.
//
// WaveController reads this component each frame and emits wake/ripple particles
// based on the source's current motion.
public class WaterDisturbanceSource : MonoBehaviour
{
    public enum DisturbanceArchetype
    {
        WakeMover,
        IdleLapper,
        BurstSplash
    }

    [Header("References")]
    [SerializeField] Transform targetTransform;
    [SerializeField] Rigidbody2D targetRb;
    [SerializeField] Transform wakeAnchor;
    [SerializeField] Transform reverseWakeAnchor;

    [Header("Behavior")]
    [SerializeField] DisturbanceArchetype archetype = DisturbanceArchetype.WakeMover;
    [SerializeField] float disturbanceStrength = 1f;
    [SerializeField] float sizeMultiplier = 1f;
    [SerializeField] float minSpeedThreshold = 0.15f;
    [SerializeField] [Range(-1f, 0f)] float reverseWakeDotThreshold = -0.15f;
    [SerializeField] float wakeSpawnOffset = 0.5f;
    [SerializeField] Vector3 localWakeOffset = new Vector3(0f, -0.5f, 0f);
    [SerializeField] bool emitIdleLappingWhenStill = true;
    [SerializeField] float idlePulseIntervalMin = 0.55f;
    [SerializeField] float idlePulseIntervalMax = 1.25f;
    [SerializeField] float idlePulseRadius = 0.35f;
    [SerializeField] float idlePulseStrengthMultiplier = 0.4f;
    [SerializeField] float idleRippleHalfLength = 0.52f;
    [SerializeField] float idleRippleHalfWidth = 0.22f;
    [SerializeField] float burstIntervalMin = 0.2f;
    [SerializeField] float burstIntervalMax = 0.75f;
    [SerializeField] float burstRadius = 0.45f;
    [SerializeField] float burstStrengthMultiplier = 1.2f;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] float debugCurrentSpeed;
    [SerializeField] Vector2 debugCurrentDirection = Vector2.up;

    public Transform TargetTransform => targetTransform != null ? targetTransform : transform;
    public Rigidbody2D TargetRigidbody => targetRb;
    public DisturbanceArchetype Archetype => archetype;
    public float DisturbanceStrength => disturbanceStrength;
    public float SizeMultiplier => sizeMultiplier;
    public float MinSpeedThreshold => minSpeedThreshold;
    public float WakeSpawnOffset => wakeSpawnOffset;
    public Transform WakeAnchor => wakeAnchor;
    public Transform ReverseWakeAnchor => reverseWakeAnchor;
    public Vector3 LocalWakeOffset => localWakeOffset;
    public bool EmitIdleLappingWhenStill => emitIdleLappingWhenStill;
    public float IdlePulseIntervalMin => idlePulseIntervalMin;
    public float IdlePulseIntervalMax => idlePulseIntervalMax;
    public float IdlePulseRadius => idlePulseRadius;
    public float IdlePulseStrengthMultiplier => idlePulseStrengthMultiplier;
    public float IdleRippleHalfLength => idleRippleHalfLength;
    public float IdleRippleHalfWidth => idleRippleHalfWidth;
    public float BurstIntervalMin => burstIntervalMin;
    public float BurstIntervalMax => burstIntervalMax;
    public float BurstRadius => burstRadius;
    public float BurstStrengthMultiplier => burstStrengthMultiplier;
    public float CurrentSpeed { get; private set; }
    public Vector2 CurrentDirection { get; private set; } = Vector2.up;

    internal float WakeEmissionAccumulator { get; set; }
    internal float RippleBurstTimer { get; set; }
    internal float IdlePulseTimer { get; set; }
    internal float BurstTimer { get; set; }

    Vector3 lastSamplePosition;

    void Reset()
    {
        targetTransform = transform;
        targetRb = GetComponent<Rigidbody2D>();
    }

    void OnEnable()
    {
        EnsureDefaults();
        lastSamplePosition = TargetTransform.position;
        CurrentDirection = SafeFallbackDirection();
        CurrentSpeed = 0f;
        WakeEmissionAccumulator = 0f;
        RippleBurstTimer = 0f;
        IdlePulseTimer = Random.Range(idlePulseIntervalMin, idlePulseIntervalMax);
        BurstTimer = Random.Range(burstIntervalMin, burstIntervalMax);

        if (WaveController.ActiveController != null)
            WaveController.ActiveController.RegisterSource(this);
    }

    void OnDisable()
    {
        if (WaveController.ActiveController != null)
            WaveController.ActiveController.UnregisterSource(this);
    }

    void OnValidate()
    {
        disturbanceStrength = Mathf.Max(0f, disturbanceStrength);
        sizeMultiplier = Mathf.Max(0.01f, sizeMultiplier);
        minSpeedThreshold = Mathf.Max(0f, minSpeedThreshold);
        reverseWakeDotThreshold = Mathf.Clamp(reverseWakeDotThreshold, -1f, 0f);
        wakeSpawnOffset = Mathf.Max(0f, wakeSpawnOffset);
        idlePulseIntervalMin = Mathf.Max(0.05f, idlePulseIntervalMin);
        idlePulseIntervalMax = Mathf.Max(idlePulseIntervalMin, idlePulseIntervalMax);
        idlePulseRadius = Mathf.Max(0.01f, idlePulseRadius);
        idlePulseStrengthMultiplier = Mathf.Max(0f, idlePulseStrengthMultiplier);
        idleRippleHalfLength = Mathf.Max(0.05f, idleRippleHalfLength);
        idleRippleHalfWidth = Mathf.Max(0.05f, idleRippleHalfWidth);
        burstIntervalMin = Mathf.Max(0.05f, burstIntervalMin);
        burstIntervalMax = Mathf.Max(burstIntervalMin, burstIntervalMax);
        burstRadius = Mathf.Max(0.01f, burstRadius);
        burstStrengthMultiplier = Mathf.Max(0f, burstStrengthMultiplier);
    }

    public void SampleMotion(float deltaTime)
    {
        EnsureDefaults();

        Vector2 fallbackDirection = SafeFallbackDirection();
        Vector3 currentPosition = TargetTransform.position;

        if (targetRb != null)
        {
            Vector2 velocity = targetRb.linearVelocity;
            CurrentSpeed = velocity.magnitude;
            CurrentDirection = CurrentSpeed > 0.001f ? velocity / CurrentSpeed : fallbackDirection;
            lastSamplePosition = currentPosition;
        }
        else
        {
            Vector2 delta = currentPosition - lastSamplePosition;
            CurrentSpeed = deltaTime > 0f ? delta.magnitude / deltaTime : 0f;
            CurrentDirection = CurrentSpeed > 0.001f ? delta.normalized : fallbackDirection;
            lastSamplePosition = currentPosition;
        }

        debugCurrentSpeed = CurrentSpeed;
        debugCurrentDirection = CurrentDirection;
    }

    Vector2 SafeFallbackDirection()
    {
        Vector2 forward = TargetTransform.up;
        if (forward.sqrMagnitude < 0.0001f)
            return Vector2.up;

        return forward.normalized;
    }

    void EnsureDefaults()
    {
        if (targetTransform == null)
            targetTransform = transform;
    }

    public Vector2 GetWakeSpawnWorldPosition(Vector2 movementDirection)
    {
        EnsureDefaults();

        if (ShouldUseReverseWakeAnchor(movementDirection) && reverseWakeAnchor != null)
            return reverseWakeAnchor.position;

        if (wakeAnchor != null)
            return wakeAnchor.position;

        Vector3 anchoredPosition = TargetTransform.TransformPoint(localWakeOffset);

        if (wakeSpawnOffset <= 0f)
            return anchoredPosition;

        Vector2 direction = movementDirection.sqrMagnitude >= 0.0001f
            ? movementDirection.normalized
            : SafeFallbackDirection();

        return (Vector2)anchoredPosition - direction * wakeSpawnOffset;
    }

    bool ShouldUseReverseWakeAnchor(Vector2 movementDirection)
    {
        if (reverseWakeAnchor == null || movementDirection.sqrMagnitude < 0.0001f)
            return false;

        Vector2 hullForward = SafeFallbackDirection();
        float travelDot = Vector2.Dot(hullForward, movementDirection.normalized);
        return travelDot <= reverseWakeDotThreshold;
    }

    public Vector2 GetAnchorWorldPosition()
    {
        EnsureDefaults();
        return TargetTransform.position;
    }

    public void ConfigureIdleLappingForStaticObstacle(float footprintRadius, float baseStrength, float configuredSizeMultiplier)
    {
        EnsureDefaults();

        archetype = DisturbanceArchetype.IdleLapper;
        targetTransform = transform;
        targetRb = null;
        wakeAnchor = null;
        reverseWakeAnchor = null;
        emitIdleLappingWhenStill = true;

        float clampedRadius = Mathf.Max(0.05f, footprintRadius);
        disturbanceStrength = Mathf.Max(0.01f, baseStrength);
        sizeMultiplier = Mathf.Max(0.1f, configuredSizeMultiplier);
        minSpeedThreshold = Mathf.Max(minSpeedThreshold, 0.15f);
        wakeSpawnOffset = 0f;
        localWakeOffset = Vector3.zero;

        idlePulseRadius = Mathf.Max(0.08f, clampedRadius * 0.92f);
        idleRippleHalfLength = Mathf.Max(0.12f, clampedRadius * 1.18f);
        idleRippleHalfWidth = Mathf.Max(0.08f, clampedRadius * 0.88f);
        idlePulseStrengthMultiplier = Mathf.Max(0.2f, 0.48f + clampedRadius * 0.28f);
        idlePulseIntervalMin = Mathf.Max(0.18f, 0.68f - clampedRadius * 0.24f);
        idlePulseIntervalMax = Mathf.Max(idlePulseIntervalMin + 0.05f, 1.08f - clampedRadius * 0.14f);

        IdlePulseTimer = Random.Range(idlePulseIntervalMin * 0.15f, idlePulseIntervalMax * 0.35f);
        RippleBurstTimer = 0f;
        BurstTimer = Mathf.Max(BurstTimer, burstIntervalMin);
    }

    public void ConfigureWakeMoverProfile(
        Transform configuredTargetTransform,
        Rigidbody2D configuredTargetRb,
        float baseStrength,
        float configuredSizeMultiplier,
        float configuredMinSpeedThreshold,
        float configuredWakeSpawnOffset,
        Vector3 configuredLocalWakeOffset,
        bool allowIdleLappingWhenStill)
    {
        EnsureDefaults();

        archetype = DisturbanceArchetype.WakeMover;
        targetTransform = configuredTargetTransform != null ? configuredTargetTransform : transform;
        targetRb = configuredTargetRb;
        wakeAnchor = null;
        reverseWakeAnchor = null;
        disturbanceStrength = Mathf.Max(0.01f, baseStrength);
        sizeMultiplier = Mathf.Max(0.05f, configuredSizeMultiplier);
        minSpeedThreshold = Mathf.Max(0f, configuredMinSpeedThreshold);
        wakeSpawnOffset = Mathf.Max(0f, configuredWakeSpawnOffset);
        localWakeOffset = configuredLocalWakeOffset;
        emitIdleLappingWhenStill = allowIdleLappingWhenStill;

        WakeEmissionAccumulator = 0f;
        RippleBurstTimer = 0f;
        IdlePulseTimer = Random.Range(idlePulseIntervalMin, idlePulseIntervalMax);
        BurstTimer = Random.Range(burstIntervalMin, burstIntervalMax);
    }

    public void SetRuntimeDisturbanceProfile(float configuredStrength, float configuredSizeMultiplier, float configuredMinSpeedThreshold)
    {
        disturbanceStrength = Mathf.Max(0f, configuredStrength);
        sizeMultiplier = Mathf.Max(0.05f, configuredSizeMultiplier);
        minSpeedThreshold = Mathf.Max(0f, configuredMinSpeedThreshold);
    }
}
