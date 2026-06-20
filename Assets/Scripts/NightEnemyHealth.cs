using UnityEngine;

[DisallowMultipleComponent]
public class NightEnemyHealth : MonoBehaviour
{
    [SerializeField] float debugCurrentHealth;
    [SerializeField] float debugMaxHealth = 1f;

    NightEnemyController owner;
    bool hasInitialized;

    public float CurrentHealth => debugCurrentHealth;
    public float MaxHealth => debugMaxHealth;
    public bool IsDead => hasInitialized && debugCurrentHealth <= 0f;

    public void Initialize(NightEnemyController targetOwner, float maxHealth)
    {
        owner = targetOwner;
        debugMaxHealth = Mathf.Max(1f, maxHealth);
        debugCurrentHealth = debugMaxHealth;
        hasInitialized = true;
    }

    // Save-restore seam: override current health after Initialize set it to max.
    public void SetCurrentHealth(float value)
    {
        debugCurrentHealth = Mathf.Clamp(value, 0f, debugMaxHealth);
    }

    public bool TryApplyProjectileDamage(float damage, Vector2 hitPoint, Vector2 impactDirection)
    {
        if (!hasInitialized || owner == null || IsDead || damage <= 0f)
            return false;

        debugCurrentHealth = Mathf.Max(0f, debugCurrentHealth - damage);
        owner.HandleProjectileDamage(hitPoint, impactDirection);

        if (debugCurrentHealth <= 0f)
            owner.HandleDeath(hitPoint, impactDirection);

        return true;
    }
}
