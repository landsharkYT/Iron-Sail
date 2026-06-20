using UnityEngine;

[CreateAssetMenu(fileName = "AmmoDefinition", menuName = "The Iron Sail/Ammo Definition")]
public class AmmoDefinition : ScriptableObject
{
    [Header("Damage")]
    [SerializeField] float damage = 10f;

    [Header("Projectile")]
    [SerializeField] GameObject projectilePrefab;
    [SerializeField] float projectileSpeed = 12f;
    [SerializeField] float projectileLifetime = 4f;
    [SerializeField] float maxRange = 18f;

    [Header("Expiry Effects")]
    [SerializeField] bool spawnWaterSplashOnExpire;
    [SerializeField] GameObject waterSplashPrefab;

    [Header("Cosmetic Arc")]
    [SerializeField] float cosmeticArcStrength;

    public float Damage => damage;
    public GameObject ProjectilePrefab => projectilePrefab;
    public float ProjectileSpeed => projectileSpeed;
    public float ProjectileLifetime => projectileLifetime;
    public float MaxRange => maxRange;
    public bool SpawnWaterSplashOnExpire => spawnWaterSplashOnExpire;
    public GameObject WaterSplashPrefab => waterSplashPrefab;
    public float CosmeticArcStrength => cosmeticArcStrength;

    void OnValidate()
    {
        damage = Mathf.Max(0f, damage);
        projectileSpeed = Mathf.Max(0.01f, projectileSpeed);
        projectileLifetime = Mathf.Max(0.05f, projectileLifetime);
        maxRange = Mathf.Max(0.05f, maxRange);
        cosmeticArcStrength = Mathf.Max(0f, cosmeticArcStrength);
    }
}
