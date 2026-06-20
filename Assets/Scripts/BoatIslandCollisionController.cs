using UnityEngine;

// Handles boat collision response against solid hull obstacles such as
// generated islands and rock prefabs.
//
// Responsibilities:
// - detect collisions with the configured hull obstacle layer
// - reduce only the velocity component pushing into the island
// - apply hull damage through BoatHealthController based on impact speed
// - rate-limit repeated damage while the boat scrapes along the shoreline
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoatController))]
public class BoatIslandCollisionController : MonoBehaviour
{
    public GameObject RamDamagePoofPrefab => ramDamagePoofPrefab;

    [Header("References")]
    [SerializeField] Rigidbody2D targetRb;
    [SerializeField] BoatHealthController boatHealthController;

    [Header("Obstacle Detection")]
    [SerializeField] string obstacleLayerName = "Island";

    [Header("Slowdown")]
    [SerializeField] float slowdownImpactThreshold = 0.35f;
    [SerializeField] float fullSlowdownImpactSpeed = 8f;
    [SerializeField] [Range(0f, 1f)] float retainedNormalVelocityAtLightImpact = 0.55f;
    [SerializeField] [Range(0f, 1f)] float retainedNormalVelocityAtHeavyImpact = 0.08f;

    [Header("Damage")]
    [SerializeField] float damageImpactThreshold = 1.2f;
    [SerializeField] float fullDamageImpactSpeed = 9f;
    [SerializeField] float maxCollisionDamage = 66f;
    [SerializeField] float repeatDamageCooldownSeconds = 0.45f;

    [Header("Impact FX")]
    [SerializeField] GameObject ramDamagePoofPrefab;
    [SerializeField] float ramDamageFxCooldownSeconds = 0.08f;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] float debugLastImpactSpeed;
    [SerializeField] float debugLastDamageApplied;
    [SerializeField] float debugLastSlowdownSeverity;
    [SerializeField] Vector2 debugLastSurfaceNormal = Vector2.up;

    int cachedObstacleLayer = -1;
    float nextDamageTime;
    float nextRamDamageFxTime;
    bool hasWarnedMissingObstacleLayer;

    void Reset()
    {
        targetRb = GetComponent<Rigidbody2D>();
        boatHealthController = GetComponent<BoatHealthController>();
    }

    void Awake()
    {
        if (targetRb == null)
            targetRb = GetComponent<Rigidbody2D>();
        if (boatHealthController == null)
            boatHealthController = GetComponent<BoatHealthController>();

        cachedObstacleLayer = LayerMask.NameToLayer(obstacleLayerName);
    }

    void OnValidate()
    {
        slowdownImpactThreshold = Mathf.Max(0f, slowdownImpactThreshold);
        fullSlowdownImpactSpeed = Mathf.Max(slowdownImpactThreshold + 0.01f, fullSlowdownImpactSpeed);
        retainedNormalVelocityAtLightImpact = Mathf.Clamp01(retainedNormalVelocityAtLightImpact);
        retainedNormalVelocityAtHeavyImpact = Mathf.Clamp01(retainedNormalVelocityAtHeavyImpact);

        damageImpactThreshold = Mathf.Max(0f, damageImpactThreshold);
        fullDamageImpactSpeed = Mathf.Max(damageImpactThreshold + 0.01f, fullDamageImpactSpeed);
        maxCollisionDamage = Mathf.Max(0f, maxCollisionDamage);
        repeatDamageCooldownSeconds = Mathf.Max(0f, repeatDamageCooldownSeconds);
        ramDamageFxCooldownSeconds = Mathf.Max(0f, ramDamageFxCooldownSeconds);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        HandleObstacleCollision(collision, allowDamage: true);
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        HandleObstacleCollision(collision, allowDamage: Time.time >= nextDamageTime);
    }

    void HandleObstacleCollision(Collision2D collision, bool allowDamage)
    {
        if (!IsObstacleCollision(collision))
            return;
        if (targetRb == null)
            return;

        Vector2 surfaceNormal = GetAverageSurfaceNormal(collision);
        Vector2 contactPoint = GetAverageContactPoint(collision);
        float impactSpeed = collision.relativeVelocity.magnitude;
        bool isTreasureContact = IsTreasureContact(collision);

        ApplyDirectionalSlowdown(surfaceNormal, impactSpeed);

        float damageApplied = 0f;
        if (allowDamage && !isTreasureContact)
            damageApplied = TryApplyImpactDamage(impactSpeed);

        if (damageApplied > 0f)
            TrySpawnRamDamagePoof(contactPoint, impactSpeed);

        debugLastImpactSpeed = impactSpeed;
        debugLastDamageApplied = damageApplied;
        debugLastSurfaceNormal = surfaceNormal;
    }

    bool IsObstacleCollision(Collision2D collision)
    {
        if (collision == null || collision.collider == null)
            return false;

        if (cachedObstacleLayer < 0)
        {
            cachedObstacleLayer = LayerMask.NameToLayer(obstacleLayerName);
            if (cachedObstacleLayer < 0 && !hasWarnedMissingObstacleLayer)
            {
                hasWarnedMissingObstacleLayer = true;
                Debug.LogWarning($"[BoatIslandCollisionController] Layer '{obstacleLayerName}' was not found. Assign islands and rocks to the same hull obstacle layer.", this);
            }
        }

        return cachedObstacleLayer >= 0 && collision.collider.gameObject.layer == cachedObstacleLayer;
    }

    Vector2 GetAverageSurfaceNormal(Collision2D collision)
    {
        int contactCount = collision.contactCount;
        if (contactCount <= 0)
            return Vector2.up;

        Vector2 summedNormal = Vector2.zero;
        for (int i = 0; i < contactCount; i++)
            summedNormal += collision.GetContact(i).normal;

        if (summedNormal.sqrMagnitude < 0.0001f)
            return Vector2.up;

        return summedNormal.normalized;
    }

    Vector2 GetAverageContactPoint(Collision2D collision)
    {
        int contactCount = collision.contactCount;
        if (contactCount <= 0)
            return targetRb != null ? targetRb.position : (Vector2)transform.position;

        Vector2 summedPoint = Vector2.zero;
        for (int i = 0; i < contactCount; i++)
            summedPoint += collision.GetContact(i).point;

        return summedPoint / contactCount;
    }

    void ApplyDirectionalSlowdown(Vector2 surfaceNormal, float impactSpeed)
    {
        Vector2 currentVelocity = targetRb.linearVelocity;
        float inwardNormalSpeed = -Vector2.Dot(currentVelocity, surfaceNormal);
        if (inwardNormalSpeed <= slowdownImpactThreshold)
        {
            debugLastSlowdownSeverity = 0f;
            return;
        }

        float severity = Mathf.InverseLerp(slowdownImpactThreshold, fullSlowdownImpactSpeed, impactSpeed);
        severity = Mathf.Clamp01(severity);

        float retainedNormalFraction = Mathf.Lerp(
            retainedNormalVelocityAtLightImpact,
            retainedNormalVelocityAtHeavyImpact,
            severity);

        float normalComponent = Vector2.Dot(currentVelocity, surfaceNormal);
        Vector2 normalVelocity = surfaceNormal * normalComponent;
        Vector2 tangentialVelocity = currentVelocity - normalVelocity;

        if (normalComponent < 0f)
            normalVelocity *= retainedNormalFraction;

        targetRb.linearVelocity = tangentialVelocity + normalVelocity;
        debugLastSlowdownSeverity = severity;
    }

    float TryApplyImpactDamage(float impactSpeed)
    {
        if (boatHealthController == null)
            return 0f;
        if (impactSpeed < damageImpactThreshold)
            return 0f;

        float normalizedImpact = Mathf.InverseLerp(damageImpactThreshold, fullDamageImpactSpeed, impactSpeed);
        normalizedImpact = Mathf.Clamp01(normalizedImpact);
        float curvedImpact = normalizedImpact * normalizedImpact;
        float damage = curvedImpact * maxCollisionDamage;

        if (damage <= 0f)
            return 0f;

        boatHealthController.TakeDamage(damage, BoatDamageSource.Collision);
        nextDamageTime = Time.time + repeatDamageCooldownSeconds;
        return damage;
    }

    void TrySpawnRamDamagePoof(Vector2 contactPoint, float impactSpeed)
    {
        if (ramDamagePoofPrefab == null)
            return;
        if (Time.time < nextRamDamageFxTime)
            return;

        GameObject effectInstance = Instantiate(
            ramDamagePoofPrefab,
            new Vector3(contactPoint.x, contactPoint.y, 0f),
            Quaternion.identity);

        if (effectInstance != null && effectInstance.TryGetComponent(out BoatRamDamagePoofEffect poofEffect))
        {
            float severity01 = Mathf.InverseLerp(damageImpactThreshold, fullDamageImpactSpeed, impactSpeed);
            poofEffect.SetSeverity01(severity01);
        }

        nextRamDamageFxTime = Time.time + ramDamageFxCooldownSeconds;
    }

    bool IsTreasureContact(Collision2D collision)
    {
        TreasureTargetController treasureTargetController = TreasureTargetController.ActiveInstance;
        if (treasureTargetController == null || collision == null)
            return false;

        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector2 point = collision.GetContact(i).point;
            Vector3Int cell = new Vector3Int(
                Mathf.FloorToInt(point.x),
                Mathf.FloorToInt(point.y),
                0);
            if (treasureTargetController.IsTreasureCell(cell))
                return true;
        }

        return false;
    }
}
