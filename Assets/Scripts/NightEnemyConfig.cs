using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "NightEnemyConfig", menuName = "The Iron Sail/Night Enemy Config")]
public class NightEnemyConfig : ScriptableObject
{
    public enum MovementStyle
    {
        Vertical,
        Horizontal
    }

    [Header("Identity")]
    // Stable id used by the save system to round-trip which enemy type this is.
    // Falls back to the asset name if left blank; set it explicitly per config so
    // renaming the asset never breaks existing saves (see ADR 0002/0003).
    [SerializeField] string saveId = "";
    [SerializeField] MovementStyle movementStyle = MovementStyle.Vertical;
    [SerializeField] Sprite sprite;
    [SerializeField] float spawnWeight = 1f;
    [SerializeField] float visualScale = 1f;
    [SerializeField] Color visualTint = Color.white;

    public string SaveId => string.IsNullOrEmpty(saveId) ? name : saveId;

    [Header("Health And Damage")]
    [SerializeField] float maxHealth = 20f;
    [SerializeField] float contactDamage = 8f;
    [SerializeField] float contactCooldownSeconds = 0.9f;

    [Header("Detection")]
    [SerializeField] float aggroRadius = 5.5f;
    [SerializeField] float loseRadius = 8f;
    [SerializeField] float despawnRadius = 28f;

    [Header("Movement")]
    [SerializeField] float idleSpeed = 1.05f;
    [SerializeField] float chaseSpeed = 1.9f;
    [SerializeField] float acceleration = 5.5f;
    [SerializeField] float idleDriftResponsiveness = 1f;
    [SerializeField] float chaseResponsiveness = 1f;
    [SerializeField] float idlePrimaryAmplitude = 1.8f;
    [SerializeField] float idleSecondaryAmplitude = 0.6f;
    [SerializeField] float idleFrequency = 1.1f;
    [SerializeField] float chaseWaveAmplitude = 0.35f;
    [SerializeField] float chaseWaveFrequency = 2.2f;
    [SerializeField] float chaseNoiseAmplitude = 0.18f;
    [SerializeField] float burstStrength = 0f;
    [SerializeField] float burstFrequency = 1f;
    [SerializeField] float burstDutyCycle = 0.5f;
    [SerializeField] float floatiness = 0f;

    [Header("State Timings")]
    [SerializeField] float alertDuration = 0.24f;
    [SerializeField] float alertSpeedMultiplier = 0.68f;
    [SerializeField] float alertResponsiveness = 1.15f;
    [SerializeField] float aggroSurgeDuration = 0.2f;
    [SerializeField] float aggroSurgeSpeedMultiplier = 1.2f;
    [SerializeField] float retreatDuration = 0.45f;
    [SerializeField] float retreatSpeedMultiplier = 0.82f;
    [SerializeField] float retreatResponsiveness = 1.05f;
    [SerializeField] float retreatArcDegrees = 24f;
    [SerializeField] float hitStateDuration = 0.09f;

    [Header("Collision")]
    [SerializeField] float bodyColliderRadius = 0.28f;
    [SerializeField] float contactTriggerRadius = 0.45f;
    [SerializeField] float boatBounceImpulse = 2.25f;
    [SerializeField] float projectileKnockbackImpulse = 1.35f;

    [Header("Visual Feedback")]
    [SerializeField] float maxVisualTiltDegrees = 10f;
    [SerializeField] float visualTiltVelocityScale = 3.5f;
    [FormerlySerializedAs("hitFlashColor")]
    [SerializeField] Color damageFlashColor = new Color(1f, 0.22f, 0.22f, 1f);
    [FormerlySerializedAs("hitFlashDuration")]
    [SerializeField] float damageFlashDuration = 0.08f;
    [SerializeField] [Range(0f, 1f)] float stateCueTintStrength = 0.22f;
    [SerializeField] float alertVisualScaleMultiplier = 1.03f;
    [SerializeField] float aggroVisualScaleMultiplier = 1.06f;
    [SerializeField] float retreatVisualScaleMultiplier = 0.98f;
    [SerializeField] float hitVisualScaleMultiplier = 0.94f;
    [SerializeField] [Range(0f, 1f)] float nightReadabilityBoost = 0.35f;
    [SerializeField] [Range(0f, 1f)] float nightBrightnessFloor = 0.62f;
    [SerializeField] Color nightLiftColor = new Color(0.78f, 0.83f, 0.94f, 1f);
    [SerializeField] [Range(0f, 1f)] float particleNightReadabilityBoost = 0.22f;

    [Header("Effects")]
    [SerializeField] GameObject contactSplashPrefab;
    [SerializeField] GameObject contactBurstPrefab;
    [SerializeField] GameObject deathEffectPrefab;

    [Header("Rewards")]
    [SerializeField] int coinReward = 1;
    [SerializeField] [Range(0f, 1f)] float cannonballRewardChance = 0.2f;
    [SerializeField] [Range(0f, 1f)] float cannonballRewardTwoWeight = 0.7f;
    [SerializeField] [Range(0f, 1f)] float cannonballRewardThreeWeight = 0.22f;
    [SerializeField] [Range(0f, 1f)] float cannonballRewardFourWeight = 0.08f;

    public MovementStyle Style => movementStyle;
    public Sprite Sprite => sprite;
    public float SpawnWeight => spawnWeight;
    public float VisualScale => visualScale;
    public Color VisualTint => visualTint;
    public float MaxHealth => maxHealth;
    public float ContactDamage => contactDamage;
    public float ContactCooldownSeconds => contactCooldownSeconds;
    public float AggroRadius => aggroRadius;
    public float LoseRadius => loseRadius;
    public float DespawnRadius => despawnRadius;
    public float IdleSpeed => idleSpeed;
    public float ChaseSpeed => chaseSpeed;
    public float Acceleration => acceleration;
    public float IdleDriftResponsiveness => idleDriftResponsiveness;
    public float ChaseResponsiveness => chaseResponsiveness;
    public float IdlePrimaryAmplitude => idlePrimaryAmplitude;
    public float IdleSecondaryAmplitude => idleSecondaryAmplitude;
    public float IdleFrequency => idleFrequency;
    public float ChaseWaveAmplitude => chaseWaveAmplitude;
    public float ChaseWaveFrequency => chaseWaveFrequency;
    public float ChaseNoiseAmplitude => chaseNoiseAmplitude;
    public float BurstStrength => burstStrength;
    public float BurstFrequency => burstFrequency;
    public float BurstDutyCycle => burstDutyCycle;
    public float Floatiness => floatiness;
    public float AlertDuration => alertDuration;
    public float AlertSpeedMultiplier => alertSpeedMultiplier;
    public float AlertResponsiveness => alertResponsiveness;
    public float AggroSurgeDuration => aggroSurgeDuration;
    public float AggroSurgeSpeedMultiplier => aggroSurgeSpeedMultiplier;
    public float RetreatDuration => retreatDuration;
    public float RetreatSpeedMultiplier => retreatSpeedMultiplier;
    public float RetreatResponsiveness => retreatResponsiveness;
    public float RetreatArcDegrees => retreatArcDegrees;
    public float HitStateDuration => hitStateDuration;
    public float BodyColliderRadius => bodyColliderRadius;
    public float ContactTriggerRadius => contactTriggerRadius;
    public float BoatBounceImpulse => boatBounceImpulse;
    public float ProjectileKnockbackImpulse => projectileKnockbackImpulse;
    public float MaxVisualTiltDegrees => maxVisualTiltDegrees;
    public float VisualTiltVelocityScale => visualTiltVelocityScale;
    public Color DamageFlashColor => damageFlashColor;
    public float DamageFlashDuration => damageFlashDuration;
    public float StateCueTintStrength => stateCueTintStrength;
    public float AlertVisualScaleMultiplier => alertVisualScaleMultiplier;
    public float AggroVisualScaleMultiplier => aggroVisualScaleMultiplier;
    public float RetreatVisualScaleMultiplier => retreatVisualScaleMultiplier;
    public float HitVisualScaleMultiplier => hitVisualScaleMultiplier;
    public float NightReadabilityBoost => nightReadabilityBoost;
    public float NightBrightnessFloor => nightBrightnessFloor;
    public Color NightLiftColor => nightLiftColor;
    public float ParticleNightReadabilityBoost => particleNightReadabilityBoost;
    public GameObject ContactSplashPrefab => contactSplashPrefab;
    public GameObject ContactBurstPrefab => contactBurstPrefab;
    public GameObject DeathEffectPrefab => deathEffectPrefab;
    public int CoinReward => coinReward;
    public float CannonballRewardChance => cannonballRewardChance;
    public float CannonballRewardTwoWeight => cannonballRewardTwoWeight;
    public float CannonballRewardThreeWeight => cannonballRewardThreeWeight;
    public float CannonballRewardFourWeight => cannonballRewardFourWeight;

    void OnValidate()
    {
        spawnWeight = Mathf.Max(0f, spawnWeight);
        visualScale = Mathf.Max(0.05f, visualScale);
        maxHealth = Mathf.Max(1f, maxHealth);
        contactDamage = Mathf.Max(0f, contactDamage);
        contactCooldownSeconds = Mathf.Max(0.05f, contactCooldownSeconds);

        aggroRadius = Mathf.Max(0.1f, aggroRadius);
        loseRadius = Mathf.Max(aggroRadius + 0.1f, loseRadius);
        despawnRadius = Mathf.Max(loseRadius + 0.1f, despawnRadius);

        idleSpeed = Mathf.Max(0f, idleSpeed);
        chaseSpeed = Mathf.Max(0f, chaseSpeed);
        acceleration = Mathf.Max(0.01f, acceleration);
        idleDriftResponsiveness = Mathf.Max(0.05f, idleDriftResponsiveness);
        chaseResponsiveness = Mathf.Max(0.05f, chaseResponsiveness);
        idlePrimaryAmplitude = Mathf.Max(0f, idlePrimaryAmplitude);
        idleSecondaryAmplitude = Mathf.Max(0f, idleSecondaryAmplitude);
        idleFrequency = Mathf.Max(0.01f, idleFrequency);
        chaseWaveAmplitude = Mathf.Max(0f, chaseWaveAmplitude);
        chaseWaveFrequency = Mathf.Max(0.01f, chaseWaveFrequency);
        chaseNoiseAmplitude = Mathf.Max(0f, chaseNoiseAmplitude);
        burstStrength = Mathf.Max(0f, burstStrength);
        burstFrequency = Mathf.Max(0.01f, burstFrequency);
        burstDutyCycle = Mathf.Clamp(burstDutyCycle, 0.05f, 0.95f);
        floatiness = Mathf.Clamp01(floatiness);
        alertDuration = Mathf.Max(0.01f, alertDuration);
        alertSpeedMultiplier = Mathf.Max(0.05f, alertSpeedMultiplier);
        alertResponsiveness = Mathf.Max(0.05f, alertResponsiveness);
        aggroSurgeDuration = Mathf.Max(0f, aggroSurgeDuration);
        aggroSurgeSpeedMultiplier = Mathf.Max(1f, aggroSurgeSpeedMultiplier);
        retreatDuration = Mathf.Max(0.05f, retreatDuration);
        retreatSpeedMultiplier = Mathf.Max(0.05f, retreatSpeedMultiplier);
        retreatResponsiveness = Mathf.Max(0.05f, retreatResponsiveness);
        retreatArcDegrees = Mathf.Clamp(retreatArcDegrees, 0f, 89f);
        hitStateDuration = Mathf.Max(0.01f, hitStateDuration);

        bodyColliderRadius = Mathf.Max(0.05f, bodyColliderRadius);
        contactTriggerRadius = Mathf.Max(bodyColliderRadius, contactTriggerRadius);
        boatBounceImpulse = Mathf.Max(0f, boatBounceImpulse);
        projectileKnockbackImpulse = Mathf.Max(0f, projectileKnockbackImpulse);

        maxVisualTiltDegrees = Mathf.Max(0f, maxVisualTiltDegrees);
        visualTiltVelocityScale = Mathf.Max(0.01f, visualTiltVelocityScale);
        damageFlashColor.a = 1f;
        damageFlashDuration = Mathf.Max(0.01f, damageFlashDuration);
        alertVisualScaleMultiplier = Mathf.Max(0.5f, alertVisualScaleMultiplier);
        aggroVisualScaleMultiplier = Mathf.Max(0.5f, aggroVisualScaleMultiplier);
        retreatVisualScaleMultiplier = Mathf.Max(0.5f, retreatVisualScaleMultiplier);
        hitVisualScaleMultiplier = Mathf.Max(0.5f, hitVisualScaleMultiplier);
        nightBrightnessFloor = Mathf.Clamp01(nightBrightnessFloor);
        nightLiftColor.a = 1f;

        coinReward = Mathf.Max(0, coinReward);
        cannonballRewardTwoWeight = Mathf.Max(0f, cannonballRewardTwoWeight);
        cannonballRewardThreeWeight = Mathf.Max(0f, cannonballRewardThreeWeight);
        cannonballRewardFourWeight = Mathf.Max(0f, cannonballRewardFourWeight);
    }
}
