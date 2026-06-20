using UnityEngine;

public class BoatRamDamagePoofEffect : MonoBehaviour, IDayNightTintTarget
{
    [Header("Visuals")]
    [SerializeField] Sprite squareSprite;
    [SerializeField] Color baseParticleColor = new Color(0.66f, 0.66f, 0.66f, 0.94f);
    [SerializeField] string sortingLayerName = "Default";
    [SerializeField] int sortingOrder = 4;

    [Header("Burst")]
    [SerializeField] int minBurstCount = 6;
    [SerializeField] int maxBurstCount = 12;
    [SerializeField] float startSpeedMin = 0.24f;
    [SerializeField] float startSpeedMax = 0.54f;
    [SerializeField] float startSizeMin = 0.026f;
    [SerializeField] float startSizeMax = 0.055f;
    [SerializeField] float startLifetimeMin = 0.16f;
    [SerializeField] float startLifetimeMax = 0.3f;
    [SerializeField] float spawnRadius = 0.05f;
    [SerializeField] float severitySizeScaleMin = 0.9f;
    [SerializeField] float severitySizeScaleMax = 1.35f;
    [SerializeField] float destroyDelay = 0.45f;

    static Material cachedSquareMaterial;

    ParticleSystem poofParticles;
    ParticleSystemRenderer poofRenderer;
    Color currentTintColor = Color.white;
    float currentTintBrightness = 1f;
    float severity01 = 0.5f;
    bool hasPlayed;

    public void SetSeverity01(float normalizedSeverity)
    {
        severity01 = Mathf.Clamp01(normalizedSeverity);
    }

    void Awake()
    {
        EnsureParticleSystem();
    }

    void OnEnable()
    {
        if (DayNightLightingController.ActiveController != null)
            DayNightLightingController.ActiveController.RegisterTintTarget(this);
    }

    void Start()
    {
        PlayBurstOnce();
    }

    void OnDisable()
    {
        if (DayNightLightingController.ActiveController != null)
            DayNightLightingController.ActiveController.UnregisterTintTarget(this);
    }

    public void ApplyTint(Color colorMultiplier, float brightnessMultiplier)
    {
        currentTintColor = colorMultiplier;
        currentTintBrightness = Mathf.Clamp01(brightnessMultiplier);
        ApplyParticleTint();
    }

    void PlayBurstOnce()
    {
        if (hasPlayed)
            return;

        hasPlayed = true;
        EnsureParticleSystem();
        ConfigureForSeverity();
        poofParticles.Clear(true);
        poofParticles.Emit(ResolveBurstCount());
        Destroy(gameObject, destroyDelay);
    }

    void EnsureParticleSystem()
    {
        poofParticles = GetComponent<ParticleSystem>();
        if (poofParticles == null)
            poofParticles = gameObject.AddComponent<ParticleSystem>();

        poofParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        poofRenderer = poofParticles.GetComponent<ParticleSystemRenderer>();
        if (poofRenderer == null)
            poofRenderer = gameObject.AddComponent<ParticleSystemRenderer>();

        var main = poofParticles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(startLifetimeMin, startLifetimeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(startSpeedMin, startSpeedMax);
        main.maxParticles = Mathf.Max(18, maxBurstCount + 6);

        var emission = poofParticles.emission;
        emission.enabled = false;

        var shape = poofParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = spawnRadius;
        shape.arcMode = ParticleSystemShapeMultiModeValue.Random;

        var colorOverLifetime = poofParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;

        var sizeOverLifetime = poofParticles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.45f, 0.92f),
            new Keyframe(1f, 0.15f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var limitVelocity = poofParticles.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.limit = 1.8f;
        limitVelocity.dampen = 0.2f;

        poofRenderer.sortingLayerName = sortingLayerName;
        poofRenderer.sortingOrder = sortingOrder;
        poofRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        poofRenderer.minParticleSize = 0.01f;
        poofRenderer.maxParticleSize = 0.5f;
        poofRenderer.sharedMaterial = GetOrCreateSquareMaterial();

        ApplyParticleTint();
    }

    void ConfigureForSeverity()
    {
        float severitySizeScale = Mathf.Lerp(severitySizeScaleMin, severitySizeScaleMax, severity01);

        var main = poofParticles.main;
        main.startSize = new ParticleSystem.MinMaxCurve(
            startSizeMin * severitySizeScale,
            startSizeMax * severitySizeScale);

        ApplyParticleTint();
    }

    void ApplyParticleTint()
    {
        if (poofParticles == null)
            return;

        Color tintedColor = baseParticleColor;
        tintedColor.r = Mathf.Clamp01(baseParticleColor.r * currentTintColor.r * currentTintBrightness);
        tintedColor.g = Mathf.Clamp01(baseParticleColor.g * currentTintColor.g * currentTintBrightness);
        tintedColor.b = Mathf.Clamp01(baseParticleColor.b * currentTintColor.b * currentTintBrightness);

        var main = poofParticles.main;
        main.startColor = tintedColor;

        var colorOverLifetime = poofParticles.colorOverLifetime;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(tintedColor, 0f),
                new GradientColorKey(tintedColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(tintedColor.a, 0f),
                new GradientAlphaKey(tintedColor.a * 0.65f, 0.45f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    int ResolveBurstCount()
    {
        return Mathf.RoundToInt(Mathf.Lerp(minBurstCount, maxBurstCount, severity01));
    }

    Material GetOrCreateSquareMaterial()
    {
        if (cachedSquareMaterial != null)
            return cachedSquareMaterial;

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader == null)
            return null;

        cachedSquareMaterial = new Material(spriteShader)
        {
            name = "BoatRamDamagePoofSpriteMaterial"
        };

        if (squareSprite != null)
            cachedSquareMaterial.mainTexture = squareSprite.texture;

        return cachedSquareMaterial;
    }
}
