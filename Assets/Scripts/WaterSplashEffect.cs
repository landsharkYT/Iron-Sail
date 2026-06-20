using UnityEngine;

public class WaterSplashEffect : MonoBehaviour
{
    [SerializeField] int burstCount = 18;
    [SerializeField] float startSpeedMin = 0.3f;
    [SerializeField] float startSpeedMax = 1.1f;
    [SerializeField] float startSizeMin = 0.05f;
    [SerializeField] float startSizeMax = 0.095f;
    [SerializeField] float startLifetimeMin = 0.55f;
    [SerializeField] float startLifetimeMax = 0.9f;
    [SerializeField] float spawnRadius = 0.04f;
    [SerializeField] float destroyDelay = 1.1f;
    [SerializeField] string sortingLayerName = "Default";
    [SerializeField] int sortingOrder = -5;

    static Material fallbackSpriteMaterial;

    ParticleSystem splashParticles;
    ParticleSystemRenderer splashRenderer;

    void Awake()
    {
        EnsureParticleSystem();
        if (splashRenderer != null)
            splashRenderer.enabled = false;

        splashParticles.Clear(true);
        splashParticles.Emit(burstCount);

        if (splashRenderer != null)
            splashRenderer.enabled = true;

        Destroy(gameObject, destroyDelay);
    }

    void EnsureParticleSystem()
    {
        splashParticles = GetComponent<ParticleSystem>();
        if (splashParticles == null)
            splashParticles = gameObject.AddComponent<ParticleSystem>();

        splashParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        splashRenderer = splashParticles.GetComponent<ParticleSystemRenderer>();
        if (splashRenderer == null)
            splashRenderer = gameObject.AddComponent<ParticleSystemRenderer>();

        var main = splashParticles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.18f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(startLifetimeMin, startLifetimeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(startSpeedMin, startSpeedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(startSizeMin, startSizeMax);
        main.startColor = new Color(1f, 1f, 1f, 0.95f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(16, burstCount + 8);

        var emission = splashParticles.emission;
        emission.enabled = false;

        var shape = splashParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = spawnRadius;
        shape.arcMode = ParticleSystemShapeMultiModeValue.Random;

        var colorOverLifetime = splashParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0f),
                new GradientAlphaKey(0.72f, 0.45f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var sizeOverLifetime = splashParticles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.65f),
            new Keyframe(0.3f, 1.18f),
            new Keyframe(1f, 0.48f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var velocityOverLifetime = splashParticles.velocityOverLifetime;
        velocityOverLifetime.enabled = false;

        var limitVelocityOverLifetime = splashParticles.limitVelocityOverLifetime;
        limitVelocityOverLifetime.enabled = true;
        limitVelocityOverLifetime.limit = 2.2f;
        limitVelocityOverLifetime.dampen = 0.18f;

        if (splashRenderer != null)
        {
            splashRenderer.sortingLayerName = sortingLayerName;
            splashRenderer.sortingOrder = sortingOrder;
            splashRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            splashRenderer.minParticleSize = 0.01f;
            splashRenderer.maxParticleSize = 0.5f;
            splashRenderer.sharedMaterial = GetOrCreateFallbackSpriteMaterial();
        }
    }

    static Material GetOrCreateFallbackSpriteMaterial()
    {
        if (fallbackSpriteMaterial != null)
            return fallbackSpriteMaterial;

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader == null)
            return null;

        fallbackSpriteMaterial = new Material(spriteShader)
        {
            name = "RuntimeWaterSplashSpriteMaterial"
        };
        return fallbackSpriteMaterial;
    }
}
