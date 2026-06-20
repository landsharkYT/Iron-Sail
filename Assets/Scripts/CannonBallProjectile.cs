using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class CannonBallProjectile : MonoBehaviour
{
    [SerializeField] float defaultSpeed = 12f;
    [SerializeField] float defaultLifetime = 4f;
    [SerializeField] float defaultMaxRange = 18f;
    [SerializeField] Transform visualRoot;

    Rigidbody2D rb;
    Collider2D[] projectileColliders;
    Vector3 initialVisualLocalPosition;
    Vector2 launchOrigin;
    float remainingLifetime;
    float maxRange;
    float damage;
    float cosmeticArcSignedStrength;
    bool launched;
    bool impactDestroyed;

    public float Damage => damage;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        projectileColliders = GetComponentsInChildren<Collider2D>(true);

        if (visualRoot == null)
            visualRoot = transform.childCount > 0 ? transform.GetChild(0) : transform;

        initialVisualLocalPosition = visualRoot != null ? visualRoot.localPosition : Vector3.zero;
        remainingLifetime = defaultLifetime;
        maxRange = defaultMaxRange;
        launchOrigin = transform.position;
    }

    void Update()
    {
        if (!launched)
            return;

        remainingLifetime -= Time.deltaTime;
        if (remainingLifetime <= 0f)
        {
            Expire();
            return;
        }

        if (maxRange > 0f)
        {
            float distanceTravelled = Vector2.Distance(launchOrigin, rb.position);
            if (distanceTravelled >= maxRange)
            {
                Expire();
                return;
            }
        }

        UpdateVisualArcOffset();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!launched || impactDestroyed || other == null)
            return;

        NightEnemyHealth enemyHealth = other.GetComponentInParent<NightEnemyHealth>();
        if (enemyHealth != null)
        {
            Vector2 impactDirection = rb != null && rb.linearVelocity.sqrMagnitude > 0.0001f
                ? rb.linearVelocity.normalized
                : (Vector2)transform.up;

            if (enemyHealth.TryApplyProjectileDamage(damage, transform.position, impactDirection))
            {
                impactDestroyed = true;
                Destroy(gameObject);
                return;
            }
        }

        impactDestroyed = true;
        Destroy(gameObject);
    }

    public void Launch(
        Vector2 direction,
        float speed,
        float lifetime,
        float launchMaxRange,
        float launchDamage,
        Collider2D[] ownerColliders = null,
        bool splashOnExpire = false,
        GameObject splashPrefab = null,
        float cosmeticArcStrength = 0f,
        float cosmeticArcSign = 0f)
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        if (projectileColliders == null || projectileColliders.Length == 0)
            projectileColliders = GetComponentsInChildren<Collider2D>(true);

        if (visualRoot == null)
            visualRoot = transform.childCount > 0 ? transform.GetChild(0) : transform;

        Vector2 normalizedDirection = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector2.up;
        float resolvedSpeed = speed > 0.01f ? speed : defaultSpeed;

        transform.up = normalizedDirection;
        rb.linearVelocity = normalizedDirection * Mathf.Max(0.01f, resolvedSpeed);

        launchOrigin = rb.position;
        remainingLifetime = Mathf.Max(0.05f, lifetime);
        maxRange = Mathf.Max(0.05f, launchMaxRange);
        damage = Mathf.Max(0f, launchDamage);
        cosmeticArcSignedStrength = Mathf.Max(0f, cosmeticArcStrength) * Mathf.Sign(cosmeticArcSign);
        launched = true;
        impactDestroyed = false;
        spawnWaterSplashOnExpire = splashOnExpire;
        waterSplashPrefab = splashPrefab;

        if (visualRoot != null)
            visualRoot.localPosition = initialVisualLocalPosition;

        IgnoreOwnerCollisions(ownerColliders);
    }

    void IgnoreOwnerCollisions(Collider2D[] ownerColliders)
    {
        if (ownerColliders == null || ownerColliders.Length == 0 || projectileColliders == null || projectileColliders.Length == 0)
            return;

        for (int i = 0; i < projectileColliders.Length; i++)
        {
            Collider2D projectileCollider = projectileColliders[i];
            if (projectileCollider == null)
                continue;

            for (int j = 0; j < ownerColliders.Length; j++)
            {
                Collider2D ownerCollider = ownerColliders[j];
                if (ownerCollider == null)
                    continue;

                Physics2D.IgnoreCollision(projectileCollider, ownerCollider, true);
            }
        }
    }

    void UpdateVisualArcOffset()
    {
        if (visualRoot == null || visualRoot == transform)
            return;

        if (Mathf.Abs(cosmeticArcSignedStrength) <= 0.0001f || maxRange <= 0.0001f)
        {
            visualRoot.localPosition = initialVisualLocalPosition;
            return;
        }

        float travelProgress = Mathf.Clamp01(Vector2.Distance(launchOrigin, rb.position) / maxRange);
        float arcAmount = Mathf.Sin(travelProgress * Mathf.PI) * cosmeticArcSignedStrength;
        visualRoot.localPosition = initialVisualLocalPosition + new Vector3(arcAmount, 0f, 0f);
    }

    void Expire()
    {
        if (spawnWaterSplashOnExpire && waterSplashPrefab != null && IsOverWater())
            Instantiate(waterSplashPrefab, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }

    bool IsOverWater()
    {
        InfiniteWaterTileMap waterTileMap = InfiniteWaterTileMap.ActiveInstance;
        return waterTileMap != null && waterTileMap.HasWaterTileAtWorldPosition(transform.position);
    }

    bool spawnWaterSplashOnExpire;
    GameObject waterSplashPrefab;
}
