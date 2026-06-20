using System.Collections.Generic;
using UnityEngine;

// Renders the large readable wake silhouette as two stamped rail chains.
//
// This is intentionally separate from WaveController:
// - WakeRailController owns the macro V shape.
// - WaveController keeps the foam / disturbance particles as the micro detail.
//
// The rails are regenerated every frame from a rolling stern history buffer so
// they can widen, fade, and curve naturally while the boat turns.
public class WakeRailController : MonoBehaviour
{
    static Transform sharedRuntimeStampRoot;

    [Header("References")]
    [SerializeField] Transform sternAnchor;
    [SerializeField] Transform reverseWakeAnchor;
    [SerializeField] Rigidbody2D targetRb;
    [SerializeField] BoatController boatController;
    [SerializeField] Sprite railStampSprite;
    [SerializeField] Transform stampRoot;

    [Header("Sampling")]
    [SerializeField] float minSpeedThreshold = 0.12f;
    [SerializeField] float sampleInterval = 0.045f;
    [SerializeField] float wakeLifetimeSeconds = 2.2f;
    [SerializeField] float fallbackFullSpeed = 6f;
    [SerializeField] [Range(-1f, 0f)] float reverseWakeDotThreshold = -0.15f;
    [SerializeField] float spawnInsetAlongForward = 0.42f;

    [Header("Shape")]
    [SerializeField] float nearRailHalfWidth = 0.16f;
    [SerializeField] float farRailHalfWidth = 2.2f;
    [SerializeField] float railSeparationExponent = 1.12f;
    [SerializeField] float railSpacing = 0.16f;
    [SerializeField] float tangentFallbackLength = 0.08f;
    [SerializeField] float minVisualSpeedFraction = 0.12f;
    [SerializeField] AnimationCurve widthBySpeed = AnimationCurve.EaseInOut(0f, 0.14f, 1f, 1f);
    [SerializeField] AnimationCurve alphaBySpeed = AnimationCurve.EaseInOut(0f, 0.18f, 1f, 1f);

    [Header("Stamps")]
    [SerializeField] int maxMarksPerRail = 96;
    [SerializeField] float nearStampSize = 0.08f;
    [SerializeField] float farStampSize = 0.045f;
    [SerializeField] float speedSizeBoost = 0.03f;
    [SerializeField] float nearAlpha = 1f;
    [SerializeField] float farAlpha = 0.42f;
    [SerializeField] string sortingLayerName = "Default";
    [SerializeField] int sortingOrder = -5;

    [Header("Breakup")]
    [SerializeField] float farSpacingJitter = 0.06f;
    [SerializeField] float farPositionJitter = 0.05f;
    [SerializeField] float farRotationJitterDegrees = 12f;
    [SerializeField] AnimationCurve alphaByAge = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.45f);
    [SerializeField] AnimationCurve widthByAge = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] int debugHistorySampleCount;
    [SerializeField] int debugActiveLeftMarks;
    [SerializeField] int debugActiveRightMarks;
    [SerializeField] float debugCurrentSpeed;
    [SerializeField] Vector2 debugCurrentDirection = Vector2.up;
    [SerializeField] bool debugUsingReverseWakeAnchor;

    readonly List<WakeSample> history = new List<WakeSample>();
    readonly List<SpriteRenderer> leftRailPool = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> rightRailPool = new List<SpriteRenderer>();

    Vector3 lastSamplePosition;
    Vector2 lastWakeOrigin;
    bool hasLastWakeOrigin;
    bool wasMovingLastFrame;
    bool lastUsedReverseWakeAnchor;
    Sprite runtimeFallbackSprite;

    struct WakeSample
    {
        public Vector2 sternPosition;
        public Vector2 direction;
        public float speed01;
        public float timestamp;
    }

    void Awake()
    {
        EnsureDefaults();
        BuildPools();
        lastSamplePosition = sternAnchor.position;
        lastWakeOrigin = GetWakeOrigin(sternAnchor.up, out lastUsedReverseWakeAnchor);
        hasLastWakeOrigin = true;
        wasMovingLastFrame = false;
    }

    void OnEnable()
    {
        EnsureDefaults();
        BuildPools();
        history.Clear();
        lastSamplePosition = sternAnchor.position;
        lastWakeOrigin = GetWakeOrigin(sternAnchor.up, out lastUsedReverseWakeAnchor);
        hasLastWakeOrigin = true;
        wasMovingLastFrame = false;
        DisableUnused(leftRailPool, 0);
        DisableUnused(rightRailPool, 0);
    }

    void OnDisable()
    {
        DisableUnused(leftRailPool, 0);
        DisableUnused(rightRailPool, 0);
    }

    void OnValidate()
    {
        minSpeedThreshold = Mathf.Max(0f, minSpeedThreshold);
        sampleInterval = Mathf.Max(0.01f, sampleInterval);
        wakeLifetimeSeconds = Mathf.Max(0.1f, wakeLifetimeSeconds);
        fallbackFullSpeed = Mathf.Max(0.01f, fallbackFullSpeed);
        reverseWakeDotThreshold = Mathf.Clamp(reverseWakeDotThreshold, -1f, 0f);
        spawnInsetAlongForward = Mathf.Max(0f, spawnInsetAlongForward);
        nearRailHalfWidth = Mathf.Max(0f, nearRailHalfWidth);
        farRailHalfWidth = Mathf.Max(nearRailHalfWidth, farRailHalfWidth);
        railSeparationExponent = Mathf.Max(0.1f, railSeparationExponent);
        railSpacing = Mathf.Max(0.01f, railSpacing);
        tangentFallbackLength = Mathf.Max(0.001f, tangentFallbackLength);
        minVisualSpeedFraction = Mathf.Clamp01(minVisualSpeedFraction);
        maxMarksPerRail = Mathf.Max(1, maxMarksPerRail);
        nearStampSize = Mathf.Max(0.001f, nearStampSize);
        farStampSize = Mathf.Max(0.001f, farStampSize);
        speedSizeBoost = Mathf.Max(0f, speedSizeBoost);
        nearAlpha = Mathf.Clamp01(nearAlpha);
        farAlpha = Mathf.Clamp01(farAlpha);
        farSpacingJitter = Mathf.Max(0f, farSpacingJitter);
        farPositionJitter = Mathf.Max(0f, farPositionJitter);
        farRotationJitterDegrees = Mathf.Max(0f, farRotationJitterDegrees);
    }

    void FixedUpdate()
    {
        EnsureDefaults();
        SamplePhysicsWake();
    }

    void LateUpdate()
    {
        EnsureDefaults();
        UpdateCurrentMotion(out float speed, out Vector2 direction);
        RebuildRails(speed, direction);
    }

    void EnsureDefaults()
    {
        if (sternAnchor == null)
            sternAnchor = transform;

        if (targetRb == null)
            targetRb = GetComponentInParent<Rigidbody2D>();

        if (boatController == null)
            boatController = GetComponentInParent<BoatController>();

        if (stampRoot == null)
            stampRoot = ResolveRuntimeStampRoot();
    }

    void BuildPools()
    {
        EnsurePoolSize(leftRailPool, maxMarksPerRail, "LeftWakeRail");
        EnsurePoolSize(rightRailPool, maxMarksPerRail, "RightWakeRail");
    }

    void EnsurePoolSize(List<SpriteRenderer> pool, int requiredSize, string prefix)
    {
        for (int i = pool.Count; i < requiredSize; i++)
        {
            GameObject stamp = new GameObject($"{prefix}_{i:000}");
            stamp.transform.SetParent(stampRoot, false);
            SpriteRenderer renderer = stamp.AddComponent<SpriteRenderer>();
            renderer.enabled = false;
            renderer.sprite = ResolveStampSprite();
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
            pool.Add(renderer);
        }

        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] == null)
                continue;

            if (pool[i].transform.parent != stampRoot)
                pool[i].transform.SetParent(stampRoot, true);

            pool[i].sprite = ResolveStampSprite();
            pool[i].sortingLayerName = sortingLayerName;
            pool[i].sortingOrder = sortingOrder;
        }
    }

    Sprite ResolveStampSprite()
    {
        if (railStampSprite != null)
            return railStampSprite;

        if (runtimeFallbackSprite != null)
            return runtimeFallbackSprite;

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.name = "WakeRailFallback";
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        runtimeFallbackSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        runtimeFallbackSprite.name = "WakeRailFallback";
        return runtimeFallbackSprite;
    }

    Transform ResolveRuntimeStampRoot()
    {
        if (sharedRuntimeStampRoot != null)
            return sharedRuntimeStampRoot;

        GameObject root = new GameObject("__WakeRailRuntimeRoot");
        root.transform.position = Vector3.zero;
        sharedRuntimeStampRoot = root.transform;
        return sharedRuntimeStampRoot;
    }

    void UpdateCurrentMotion(out float speed, out Vector2 direction)
    {
        Vector2 fallbackDirection = sternAnchor.up.sqrMagnitude >= 0.0001f ? ((Vector2)sternAnchor.up).normalized : Vector2.up;

        if (targetRb != null)
        {
            Vector2 velocity = targetRb.linearVelocity;
            speed = velocity.magnitude;
            direction = speed > 0.001f ? velocity / speed : fallbackDirection;
        }
        else
        {
            Vector2 delta = (Vector2)sternAnchor.position - (Vector2)lastSamplePosition;
            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            speed = delta.magnitude / deltaTime;
            direction = speed > 0.001f ? delta.normalized : fallbackDirection;
        }

        lastSamplePosition = sternAnchor.position;
        debugCurrentSpeed = speed;
        debugCurrentDirection = direction;
    }

    void SamplePhysicsWake()
    {
        UpdateCurrentMotion(out float speed, out Vector2 direction);
        float deltaTime = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        float sampleTimestamp = Time.time;
        Vector2 currentWakeOrigin = GetWakeOrigin(direction, out bool usingReverseWakeAnchor);

        if (!hasLastWakeOrigin)
        {
            lastWakeOrigin = currentWakeOrigin;
            hasLastWakeOrigin = true;
            lastUsedReverseWakeAnchor = usingReverseWakeAnchor;
        }

        bool isMoving = speed >= minSpeedThreshold;
        debugUsingReverseWakeAnchor = usingReverseWakeAnchor;

        if (usingReverseWakeAnchor != lastUsedReverseWakeAnchor)
        {
            history.Clear();
            wasMovingLastFrame = false;
            lastWakeOrigin = currentWakeOrigin;
            hasLastWakeOrigin = true;
        }

        if (isMoving)
        {
            if (!wasMovingLastFrame || history.Count == 0)
            {
                history.Add(new WakeSample
                {
                    sternPosition = lastWakeOrigin,
                    direction = direction.sqrMagnitude >= 0.0001f ? direction.normalized : Vector2.up,
                    speed01 = GetSpeed01(speed),
                    timestamp = sampleTimestamp - deltaTime
                });
            }

            float distance = Vector2.Distance(lastWakeOrigin, currentWakeOrigin);
            int distanceSubdivisions = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(railSpacing, 0.001f)));
            int timeSubdivisions = sampleInterval > 0.0001f
                ? Mathf.Max(1, Mathf.CeilToInt(deltaTime / sampleInterval))
                : 1;
            int subdivisions = Mathf.Max(distanceSubdivisions, timeSubdivisions);

            for (int sampleIndex = 1; sampleIndex <= subdivisions; sampleIndex++)
            {
                float interpolationT = sampleIndex / (float)subdivisions;
                float sampleTime = Mathf.Lerp(sampleTimestamp - deltaTime, sampleTimestamp, interpolationT);
                Vector2 samplePosition = Vector2.Lerp(lastWakeOrigin, currentWakeOrigin, interpolationT);
                Vector2 sampleDirection = Vector2.Lerp(
                    history[history.Count - 1].direction,
                    direction,
                    interpolationT);
                if (sampleDirection.sqrMagnitude < 0.0001f)
                    sampleDirection = direction;
                sampleDirection.Normalize();

                history.Add(new WakeSample
                {
                    sternPosition = samplePosition,
                    direction = sampleDirection,
                    speed01 = GetSpeed01(speed),
                    timestamp = sampleTime
                });
            }
        }

        lastWakeOrigin = currentWakeOrigin;
        lastUsedReverseWakeAnchor = usingReverseWakeAnchor;
        wasMovingLastFrame = isMoving;

        float cutoff = Time.time - wakeLifetimeSeconds;
        history.RemoveAll(sample => sample.timestamp < cutoff);
        debugHistorySampleCount = history.Count;
    }

    float GetSpeed01(float speed)
    {
        if (boatController != null)
            return boatController.SpeedFraction;

        return Mathf.Clamp01(speed / fallbackFullSpeed);
    }

    Vector2 GetWakeOrigin(Vector2 direction, out bool usingReverseWakeAnchor)
    {
        usingReverseWakeAnchor = ShouldUseReverseWakeAnchor(direction);

        if (usingReverseWakeAnchor && reverseWakeAnchor != null)
            return reverseWakeAnchor.position;

        if (sternAnchor != null && sternAnchor != transform)
            return sternAnchor.position;

        Vector2 fallbackForward = sternAnchor.up.sqrMagnitude >= 0.0001f
            ? ((Vector2)sternAnchor.up).normalized
            : Vector2.up;
        Vector2 forward = direction.sqrMagnitude >= 0.0001f ? direction.normalized : fallbackForward;
        return (Vector2)sternAnchor.position + forward * spawnInsetAlongForward;
    }

    bool ShouldUseReverseWakeAnchor(Vector2 direction)
    {
        if (reverseWakeAnchor == null || direction.sqrMagnitude < 0.0001f)
            return false;

        Vector2 hullForward = sternAnchor != null && sternAnchor.up.sqrMagnitude >= 0.0001f
            ? ((Vector2)sternAnchor.up).normalized
            : ((Vector2)transform.up).normalized;
        float travelDot = Vector2.Dot(hullForward, direction.normalized);
        return travelDot <= reverseWakeDotThreshold;
    }

    void RebuildRails(float currentSpeed, Vector2 currentDirection)
    {
        if (history.Count == 0)
        {
            DisableUnused(leftRailPool, 0);
            DisableUnused(rightRailPool, 0);
            debugActiveLeftMarks = 0;
            debugActiveRightMarks = 0;
            return;
        }

        Vector2 liveDirection = currentDirection.sqrMagnitude >= 0.0001f ? currentDirection.normalized : Vector2.up;
        WakeSample liveSample = new WakeSample
        {
            sternPosition = GetWakeOrigin(liveDirection, out _),
            direction = liveDirection,
            speed01 = GetSpeed01(currentSpeed),
            timestamp = Time.time
        };

        int leftIndex = 0;
        int rightIndex = 0;

        for (int i = 0; i < history.Count - 1; i++)
        {
            WakeSample sampleA = history[i];
            WakeSample sampleB = history[i + 1];
            float segmentSpeed01 = Mathf.Lerp(sampleA.speed01, sampleB.speed01, 0.5f);
            if (segmentSpeed01 < minVisualSpeedFraction)
                continue;

            Vector2 segment = sampleB.sternPosition - sampleA.sternPosition;
            float distance = segment.magnitude;
            int subdivisions = Mathf.Max(1, Mathf.CeilToInt(distance / railSpacing));

            for (int step = 0; step < subdivisions; step++)
            {
                float t = subdivisions <= 1 ? 0f : step / (float)subdivisions;
                Vector2 center = Vector2.Lerp(sampleA.sternPosition, sampleB.sternPosition, t);
                float ageT = Mathf.Clamp01((Time.time - Mathf.Lerp(sampleA.timestamp, sampleB.timestamp, t)) / wakeLifetimeSeconds);
                float speed01 = Mathf.Lerp(sampleA.speed01, sampleB.speed01, t);
                if (speed01 < minVisualSpeedFraction)
                    continue;

                Vector2 tangent = GetSegmentTangent(i, t, liveSample, false);
                if (tangent.sqrMagnitude < 0.0001f)
                    tangent = Vector2.Lerp(sampleA.direction, sampleB.direction, t).normalized;
                if (tangent.sqrMagnitude < 0.0001f)
                    tangent = Vector2.up;

                Vector2 perpendicular = new Vector2(-tangent.y, tangent.x);
                float widthT = Mathf.Clamp01(widthByAge.Evaluate(ageT));
                float speedWidthScale = Mathf.Clamp01(widthBySpeed.Evaluate(speed01));
                float halfWidth = Mathf.Lerp(nearRailHalfWidth, farRailHalfWidth, Mathf.Pow(widthT, railSeparationExponent)) * speedWidthScale;

                float spacingJitter = Mathf.Lerp(0f, farSpacingJitter, ageT);
                float positionJitter = Mathf.Lerp(0f, farPositionJitter, ageT);
                float rotationJitter = Mathf.Lerp(0f, farRotationJitterDegrees, ageT);
                float leftSpacingNoise = SignedHash01(sampleIndex: i, subdivisionIndex: step, sideSeed: 1);
                float rightSpacingNoise = SignedHash01(sampleIndex: i, subdivisionIndex: step, sideSeed: 2);
                float leftPositionNoise = SignedHash01(sampleIndex: i, subdivisionIndex: step, sideSeed: 3);
                float rightPositionNoise = SignedHash01(sampleIndex: i, subdivisionIndex: step, sideSeed: 4);
                float leftRotationNoise = SignedHash01(sampleIndex: i, subdivisionIndex: step, sideSeed: 5);
                float rightRotationNoise = SignedHash01(sampleIndex: i, subdivisionIndex: step, sideSeed: 6);

                Vector2 leftPos = center + perpendicular * (halfWidth + leftSpacingNoise * spacingJitter)
                                + tangent * (leftPositionNoise * positionJitter);
                Vector2 rightPos = center - perpendicular * (halfWidth + rightSpacingNoise * spacingJitter)
                                 + tangent * (rightPositionNoise * positionJitter);

                float speedAlphaScale = Mathf.Clamp01(alphaBySpeed.Evaluate(speed01));
                float alpha = Mathf.Lerp(nearAlpha, farAlpha, ageT) * Mathf.Clamp01(alphaByAge.Evaluate(ageT)) * speedAlphaScale;
                float size = Mathf.Lerp(nearStampSize, farStampSize, ageT) + speed01 * speedSizeBoost;
                float baseAngle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;

                if (leftIndex < leftRailPool.Count)
                    ApplyStamp(leftRailPool[leftIndex++], leftPos, size, alpha, baseAngle + leftRotationNoise * rotationJitter);

                if (rightIndex < rightRailPool.Count)
                    ApplyStamp(rightRailPool[rightIndex++], rightPos, size, alpha, baseAngle + rightRotationNoise * rotationJitter);
            }
        }

        WakeSample newestSample = history[history.Count - 1];
        float newestSegmentSpeed01 = Mathf.Lerp(newestSample.speed01, liveSample.speed01, 0.5f);
        if (newestSegmentSpeed01 >= minVisualSpeedFraction)
        {
            Vector2 liveSegment = liveSample.sternPosition - newestSample.sternPosition;
            float liveDistance = liveSegment.magnitude;
            int liveSubdivisions = Mathf.Max(1, Mathf.CeilToInt(liveDistance / railSpacing));

            for (int step = 0; step <= liveSubdivisions; step++)
            {
                float t = liveSubdivisions <= 0 ? 1f : step / (float)liveSubdivisions;
                Vector2 center = Vector2.Lerp(newestSample.sternPosition, liveSample.sternPosition, t);
                float ageT = Mathf.Clamp01((Time.time - Mathf.Lerp(newestSample.timestamp, liveSample.timestamp, t)) / wakeLifetimeSeconds);
                float speed01 = Mathf.Lerp(newestSample.speed01, liveSample.speed01, t);
                if (speed01 < minVisualSpeedFraction)
                    continue;

                Vector2 tangent = GetSegmentTangent(history.Count - 1, t, liveSample, true);
                if (tangent.sqrMagnitude < 0.0001f)
                    tangent = Vector2.Lerp(newestSample.direction, liveSample.direction, t).normalized;
                if (tangent.sqrMagnitude < 0.0001f)
                    tangent = Vector2.up;

                Vector2 perpendicular = new Vector2(-tangent.y, tangent.x);
                float widthT = Mathf.Clamp01(widthByAge.Evaluate(ageT));
                float speedWidthScale = Mathf.Clamp01(widthBySpeed.Evaluate(speed01));
                float halfWidth = Mathf.Lerp(nearRailHalfWidth, farRailHalfWidth, Mathf.Pow(widthT, railSeparationExponent)) * speedWidthScale;

                float spacingJitter = Mathf.Lerp(0f, farSpacingJitter, ageT);
                float positionJitter = Mathf.Lerp(0f, farPositionJitter, ageT);
                float rotationJitter = Mathf.Lerp(0f, farRotationJitterDegrees, ageT);
                int hashSampleIndex = history.Count;
                float leftSpacingNoise = SignedHash01(sampleIndex: hashSampleIndex, subdivisionIndex: step, sideSeed: 1);
                float rightSpacingNoise = SignedHash01(sampleIndex: hashSampleIndex, subdivisionIndex: step, sideSeed: 2);
                float leftPositionNoise = SignedHash01(sampleIndex: hashSampleIndex, subdivisionIndex: step, sideSeed: 3);
                float rightPositionNoise = SignedHash01(sampleIndex: hashSampleIndex, subdivisionIndex: step, sideSeed: 4);
                float leftRotationNoise = SignedHash01(sampleIndex: hashSampleIndex, subdivisionIndex: step, sideSeed: 5);
                float rightRotationNoise = SignedHash01(sampleIndex: hashSampleIndex, subdivisionIndex: step, sideSeed: 6);

                Vector2 leftPos = center + perpendicular * (halfWidth + leftSpacingNoise * spacingJitter)
                                + tangent * (leftPositionNoise * positionJitter);
                Vector2 rightPos = center - perpendicular * (halfWidth + rightSpacingNoise * spacingJitter)
                                 + tangent * (rightPositionNoise * positionJitter);

                float speedAlphaScale = Mathf.Clamp01(alphaBySpeed.Evaluate(speed01));
                float alpha = Mathf.Lerp(nearAlpha, farAlpha, ageT) * Mathf.Clamp01(alphaByAge.Evaluate(ageT)) * speedAlphaScale;
                float size = Mathf.Lerp(nearStampSize, farStampSize, ageT) + speed01 * speedSizeBoost;
                float baseAngle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;

                if (leftIndex < leftRailPool.Count)
                    ApplyStamp(leftRailPool[leftIndex++], leftPos, size, alpha, baseAngle + leftRotationNoise * rotationJitter);

                if (rightIndex < rightRailPool.Count)
                    ApplyStamp(rightRailPool[rightIndex++], rightPos, size, alpha, baseAngle + rightRotationNoise * rotationJitter);
            }
        }

        DisableUnused(leftRailPool, leftIndex);
        DisableUnused(rightRailPool, rightIndex);
        debugActiveLeftMarks = leftIndex;
        debugActiveRightMarks = rightIndex;
    }

    Vector2 GetSegmentTangent(int sampleIndex, float interpolationT, WakeSample liveSample, bool connectsToLiveSample)
    {
        Vector2 tangent;

        if (connectsToLiveSample)
        {
            if (history.Count >= 2)
            {
                tangent = liveSample.sternPosition - history[history.Count - 2].sternPosition;
            }
            else
            {
                tangent = liveSample.sternPosition - history[history.Count - 1].sternPosition;
            }
        }
        else if (sampleIndex > 0 && sampleIndex < history.Count - 2)
        {
            Vector2 before = history[sampleIndex - 1].sternPosition;
            Vector2 after = history[sampleIndex + 2].sternPosition;
            tangent = after - before;
        }
        else
        {
            Vector2 current = history[sampleIndex].sternPosition;
            Vector2 next = history[sampleIndex + 1].sternPosition;
            tangent = next - current;
        }

        if (tangent.sqrMagnitude < tangentFallbackLength * tangentFallbackLength)
        {
            Vector2 fallbackA = history[Mathf.Clamp(sampleIndex, 0, history.Count - 1)].direction;
            Vector2 fallbackB = connectsToLiveSample ? liveSample.direction : history[Mathf.Min(sampleIndex + 1, history.Count - 1)].direction;
            tangent = Vector2.Lerp(fallbackA, fallbackB, interpolationT);
        }

        return tangent.sqrMagnitude >= 0.0001f ? tangent.normalized : Vector2.up;
    }

    void ApplyStamp(SpriteRenderer renderer, Vector2 worldPosition, float size, float alpha, float rotationDegrees)
    {
        if (renderer == null)
            return;

        Transform rendererTransform = renderer.transform;
        rendererTransform.position = new Vector3(worldPosition.x, worldPosition.y, rendererTransform.position.z);
        rendererTransform.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
        rendererTransform.localScale = new Vector3(size, size, 1f);

        renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        renderer.enabled = true;
    }

    void DisableUnused(List<SpriteRenderer> pool, int activeCount)
    {
        for (int i = activeCount; i < pool.Count; i++)
        {
            if (pool[i] != null)
                pool[i].enabled = false;
        }
    }

    static float SignedHash01(int sampleIndex, int subdivisionIndex, int sideSeed)
    {
        float seed = sampleIndex * 17.173f + subdivisionIndex * 31.941f + sideSeed * 53.627f;
        return Mathf.Repeat(Mathf.Sin(seed) * 43758.5453f, 1f) * 2f - 1f;
    }
}
