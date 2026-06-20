using UnityEngine;

public class RainVisualController : MonoBehaviour, IDayNightTintTarget
{
    [Header("Sources")]
    [SerializeField] WeatherController weatherController;
    [SerializeField] Camera targetCamera;
    [SerializeField] Transform visibleAreaCenterTarget;

    [Header("Particle Systems")]
    [SerializeField] ParticleSystem fallingRainParticles;
    [SerializeField] ParticleSystem rainImpactParticles;

    [Header("Intensity")]
    [SerializeField, Min(0.01f)] float visualFadeSeconds = 1.5f;
    [SerializeField, Min(0f)] float fallingRainIntensityMultiplier = 1f;

    [Header("Falling Rain")]
    [SerializeField, Min(0f)] float fallingRainEmissionRate = 700f;
    [SerializeField, Min(1)] int fallingRainMaxParticles = 1200;
    [SerializeField, Min(1f)] float rainBoundsPaddingMultiplier = 1.25f;
    [SerializeField] Vector2 fallingRainVelocity = new Vector2(-1.6f, -8f);
    // Matches the soft translucent white used by SpeedLinesElement.
    [SerializeField] Color fallingRainColor = new Color(1f, 1f, 1f, 0.35f);
    // Spread of fall distances: short-lived drops "land" high, others rake through.
    [SerializeField] Vector2 fallingRainLifetimeRange = new Vector2(0.4f, 1.3f);

    [Header("Day/Night Tint")]
    [SerializeField] bool tintRainByTimeOfDay = true;
    [SerializeField, Range(0f, 1f)] float rainTintInfluence = 0.55f;
    [SerializeField, Range(0f, 1f)] float rainBrightnessInfluence = 0.65f;

    [Header("Rain Impacts")]
    // Fraction of falling drops that land and spawn a Rain Impact on death.
    [SerializeField, Range(0f, 1f)] float rainImpactLandingFraction = 0.5f;
    [SerializeField, Min(1)] int rainImpactMaxParticles = 900;
    [SerializeField] Vector2 rainImpactLifetimeRange = new Vector2(0.22f, 0.42f);
    [SerializeField] Vector2 rainImpactSizeRange = new Vector2(0.12f, 0.2f);
    [SerializeField] Color rainImpactColor = new Color(0.9f, 0.96f, 1f, 0.46f);

    [Header("Rendering")]
    [SerializeField] string rainSortingLayerName = "Default";
    [SerializeField] int fallingRainSortingOrder = 25;
    // Above the water surface (0) so ripples are visible, below the falling rain (25).
    [SerializeField] int rainImpactSortingOrder = 2;

    float visualIntensity;
    bool subscribed;
    Material rainParticleMaterial;
    Color currentTimeOfDayTint = Color.white;
    float currentTimeOfDayBrightness = 1f;
    DayNightLightingController registeredDayNightLightingController;

    void OnEnable()
    {
        ResolveReferences();
        EnsureParticleSystems();
        ConfigureParticleSystems();
        Subscribe();
        RegisterDayNightTintTarget();
    }

    void Start()
    {
        ResolveReferences();
        EnsureParticleSystems();
        ConfigureParticleSystems();
    }

    void Update()
    {
        ResolveReferences();
        EnsureParticleSystems();
        RegisterDayNightTintTarget();

        float targetIntensity = weatherController != null ? weatherController.WeatherIntensity : 0f;
        visualIntensity = Mathf.MoveTowards(visualIntensity, targetIntensity, Time.deltaTime / visualFadeSeconds);

        UpdateFallingRain();
    }

    void OnDisable()
    {
        Unsubscribe();
        UnregisterDayNightTintTarget();
    }

    public void ApplyTint(Color colorMultiplier, float brightnessMultiplier)
    {
        currentTimeOfDayTint = OpaqueColor(colorMultiplier);
        currentTimeOfDayBrightness = Mathf.Clamp01(brightnessMultiplier);
        ApplyFallingRainColor();
    }

    void ResolveReferences()
    {
        if (weatherController == null)
            weatherController = WeatherController.ActiveInstance != null ? WeatherController.ActiveInstance : FindAnyObjectByType<WeatherController>();

        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    void Subscribe()
    {
        if (weatherController == null || subscribed)
            return;

        weatherController.OnWeatherChanged += HandleWeatherChanged;
        subscribed = true;
    }

    void Unsubscribe()
    {
        if (weatherController != null && subscribed)
            weatherController.OnWeatherChanged -= HandleWeatherChanged;

        subscribed = false;
    }

    void HandleWeatherChanged(WeatherState previousWeather, WeatherState currentWeather)
    {
        if (currentWeather == WeatherState.Rainfall && fallingRainParticles != null && !fallingRainParticles.isPlaying)
            fallingRainParticles.Play();

        if (currentWeather == WeatherState.Rainfall && rainImpactParticles != null && !rainImpactParticles.isPlaying)
            rainImpactParticles.Play();
    }

    void EnsureParticleSystems()
    {
        if (fallingRainParticles == null)
            fallingRainParticles = CreateChildParticleSystem("Falling Rain Particles", transform);

        // The impact system must be a child of the falling system it sub-emits from,
        // or Unity's SubEmitter module silently never fires it.
        if (rainImpactParticles == null)
            rainImpactParticles = CreateChildParticleSystem("Rain Impact Particles", fallingRainParticles.transform);
    }

    ParticleSystem CreateChildParticleSystem(string childName, Transform parent)
    {
        GameObject particleObject = new GameObject(childName);
        particleObject.transform.SetParent(parent, false);
        return particleObject.AddComponent<ParticleSystem>();
    }

    void ConfigureParticleSystems()
    {
        ConfigureFallingRainParticles();
        ConfigureRainImpactParticles();
    }

    void ConfigureFallingRainParticles()
    {
        if (fallingRainParticles == null)
            return;

        ParticleSystem.MainModule main = fallingRainParticles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = fallingRainMaxParticles;
        main.startLifetime = new ParticleSystem.MinMaxCurve(fallingRainLifetimeRange.x, fallingRainLifetimeRange.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
        main.startColor = GetTintedFallingRainColor();

        ParticleSystem.EmissionModule emission = fallingRainParticles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = fallingRainParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;

        ParticleSystem.VelocityOverLifetimeModule velocity = fallingRainParticles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(fallingRainVelocity.x * 0.75f, fallingRainVelocity.x * 1.25f);
        velocity.y = new ParticleSystem.MinMaxCurve(fallingRainVelocity.y * 0.85f, fallingRainVelocity.y * 1.15f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        ParticleSystemRenderer renderer = fallingRainParticles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 1.9f;
        renderer.velocityScale = 0.12f;
        renderer.sortingLayerName = rainSortingLayerName;
        renderer.sortingOrder = fallingRainSortingOrder;
        renderer.material = GetRainParticleMaterial();

        ConfigureLandingSubEmitter();

        if (!fallingRainParticles.isPlaying)
            fallingRainParticles.Play();
    }

    // A fraction of falling drops spawn a Rain Impact at their death position.
    // Handled natively by the SubEmitter module (no per-frame managed work).
    void ConfigureLandingSubEmitter()
    {
        if (fallingRainParticles == null || rainImpactParticles == null)
            return;

        ParticleSystem.SubEmittersModule subEmitters = fallingRainParticles.subEmitters;
        subEmitters.enabled = true;

        for (int i = subEmitters.subEmittersCount - 1; i >= 0; i--)
            subEmitters.RemoveSubEmitter(i);

        subEmitters.AddSubEmitter(
            rainImpactParticles,
            ParticleSystemSubEmitterType.Death,
            ParticleSystemSubEmitterProperties.InheritNothing);
        subEmitters.SetSubEmitterEmitProbability(0, rainImpactLandingFraction);
    }

    Material GetRainParticleMaterial()
    {
        if (rainParticleMaterial == null)
            rainParticleMaterial = new Material(Shader.Find("Sprites/Default"));

        return rainParticleMaterial;
    }

    void ConfigureRainImpactParticles()
    {
        if (rainImpactParticles == null)
            return;

        ParticleSystem.MainModule main = rainImpactParticles.main;
        // Driven only as a death sub-emitter, so it must not self-loop.
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = rainImpactMaxParticles;
        main.startSpeed = 0f;
        // Sub-emit inherits nothing, so impacts carry their own start values.
        main.startLifetime = new ParticleSystem.MinMaxCurve(rainImpactLifetimeRange.x, rainImpactLifetimeRange.y);
        main.startSize = new ParticleSystem.MinMaxCurve(rainImpactSizeRange.x, rainImpactSizeRange.y);
        main.startColor = rainImpactColor;

        // A death sub-emitter spawns the number of particles defined by this
        // system's emission BURSTS, not its rate. Without a burst, every landing
        // would trigger the sub-emitter yet emit zero ripples.
        ParticleSystem.EmissionModule emission = rainImpactParticles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        ParticleSystem.ShapeModule shape = rainImpactParticles.shape;
        shape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = rainImpactParticles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve rippleCurve = new AnimationCurve(
            new Keyframe(0f, 0.2f),
            new Keyframe(0.35f, 1f),
            new Keyframe(1f, 0f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, rippleCurve);

        ParticleSystemRenderer renderer = rainImpactParticles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingLayerName = rainSortingLayerName;
        renderer.sortingOrder = rainImpactSortingOrder;
        renderer.material = GetRainParticleMaterial();

        // No Play() here: this system only ever emits as the falling-rain death
        // sub-emitter, never on its own.
    }

    void RegisterDayNightTintTarget()
    {
        DayNightLightingController activeController = DayNightLightingController.ActiveController;
        if (registeredDayNightLightingController == activeController)
            return;

        if (registeredDayNightLightingController != null)
            registeredDayNightLightingController.UnregisterTintTarget(this);

        registeredDayNightLightingController = activeController;
        if (registeredDayNightLightingController != null)
            registeredDayNightLightingController.RegisterTintTarget(this);
    }

    void UnregisterDayNightTintTarget()
    {
        if (registeredDayNightLightingController != null)
            registeredDayNightLightingController.UnregisterTintTarget(this);

        registeredDayNightLightingController = null;
    }

    void ApplyFallingRainColor()
    {
        if (fallingRainParticles == null)
            return;

        ParticleSystem.MainModule main = fallingRainParticles.main;
        main.startColor = GetTintedFallingRainColor();
    }

    Color GetTintedFallingRainColor()
    {
        Color authoredColor = fallingRainColor;
        if (!tintRainByTimeOfDay)
            return authoredColor;

        Color tintColor = Color.Lerp(Color.white, currentTimeOfDayTint, rainTintInfluence);
        float brightness = Mathf.Lerp(1f, currentTimeOfDayBrightness, rainBrightnessInfluence);

        Color tintedColor = authoredColor;
        tintedColor.r = Mathf.Clamp01(authoredColor.r * tintColor.r * brightness);
        tintedColor.g = Mathf.Clamp01(authoredColor.g * tintColor.g * brightness);
        tintedColor.b = Mathf.Clamp01(authoredColor.b * tintColor.b * brightness);
        tintedColor.a = authoredColor.a;
        return tintedColor;
    }

    static Color OpaqueColor(Color color)
    {
        color.a = 1f;
        return color;
    }

    void UpdateFallingRain()
    {
        if (fallingRainParticles == null || targetCamera == null)
            return;

        Vector3 center = GetVisibleAreaCenter();
        Vector2 size = GetCameraWorldSize() * rainBoundsPaddingMultiplier;
        fallingRainParticles.transform.position = new Vector3(center.x, center.y, transform.position.z);

        ParticleSystem.ShapeModule shape = fallingRainParticles.shape;
        shape.scale = new Vector3(size.x, size.y, 0.1f);

        ParticleSystem.EmissionModule emission = fallingRainParticles.emission;
        emission.rateOverTime = fallingRainEmissionRate * visualIntensity * fallingRainIntensityMultiplier;
    }

    Vector3 GetVisibleAreaCenter()
    {
        if (visibleAreaCenterTarget != null)
            return visibleAreaCenterTarget.position;

        return targetCamera.transform.position;
    }

    Vector2 GetCameraWorldSize()
    {
        float height = targetCamera.orthographicSize * 2f;
        float width = height * targetCamera.aspect;
        return new Vector2(width, height);
    }

    void OnValidate()
    {
        visualFadeSeconds = Mathf.Max(0.01f, visualFadeSeconds);
        rainTintInfluence = Mathf.Clamp01(rainTintInfluence);
        rainBrightnessInfluence = Mathf.Clamp01(rainBrightnessInfluence);
        rainBoundsPaddingMultiplier = Mathf.Max(1f, rainBoundsPaddingMultiplier);
        fallingRainLifetimeRange.x = Mathf.Max(0.05f, fallingRainLifetimeRange.x);
        fallingRainLifetimeRange.y = Mathf.Max(fallingRainLifetimeRange.x, fallingRainLifetimeRange.y);
        rainImpactLifetimeRange.x = Mathf.Max(0.01f, rainImpactLifetimeRange.x);
        rainImpactLifetimeRange.y = Mathf.Max(rainImpactLifetimeRange.x, rainImpactLifetimeRange.y);
        rainImpactSizeRange.x = Mathf.Max(0.001f, rainImpactSizeRange.x);
        rainImpactSizeRange.y = Mathf.Max(rainImpactSizeRange.x, rainImpactSizeRange.y);
        ApplyFallingRainColor();
    }
}
