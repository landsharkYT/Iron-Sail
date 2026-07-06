using UnityEngine;

// A circular sea hazard (see ADR 0005 and the Whirlpool glossary entry). While the
// boat is inside the trigger it is pulled toward the eye by a logarithmic-spiral
// force field and takes depth-scaled damage over time. There is no scripted
// "escape": breaking out is emergent, since only near-full wind thrust beats the pull,
// so escape is possible roughly downwind.
[RequireComponent(typeof(CircleCollider2D))]
public class WhirlpoolController : MonoBehaviour
{
    [Header("Pull")]
    [SerializeField] CircleCollider2D areaTrigger;
    [SerializeField, Min(0f)] float maxPullForce = 14f;
    // Tangential : radial ratio. 0 = pure suction, higher = tighter orbit.
    [SerializeField, Min(0f)] float swirlRatio = 0.6f;

    [Header("Damage Over Time (low side)")]
    [SerializeField, Min(0f)] float rimDamagePerSecond = 1.5f;
    [SerializeField, Min(0f)] float eyeDamagePerSecond = 6f;

    // Code-created spiral particles matching the wave system's white squares (an
    // untextured Sprites/Default billboard quad), so nothing needs prefab wiring.
    [Header("Visual")]
    // Higher rate fills the gaps left by the now-tiny squares.
    [SerializeField, Min(0f)] float particleRimEmissionRate = 240f;
    [SerializeField] Color particleColor = new Color(1f, 1f, 1f, 0.6f);
    // Match the wave squares' start size (a constant ~0.08) with a small range for
    // variety, so the whirlpool foam is indistinguishable from the wave particles.
    [SerializeField, Min(0.001f)] float particleSizeMin = 0.06f;
    [SerializeField, Min(0.001f)] float particleSizeMax = 0.1f;
    // Render above the water tilemap (which sits on Default/0) so the foam is
    // visible. Tunable if you want it on a specific water-effects plane.
    [SerializeField] string particleSortingLayerName = "Default";
    [SerializeField] int particleSortingOrder = 2;

    Rigidbody2D boatRb;
    BoatHealthController boatHealth;
    ParticleSystem vortexParticles;
    Material vortexParticleMaterial;

    public float Radius => areaTrigger != null ? areaTrigger.radius * Mathf.Max(transform.lossyScale.x, 0.0001f) : 0f;

    // --- Diagnostic seam: pure force model (unit-testable, no scene needed) ----

    // Force applied to a boat at worldOffset from the eye. Rim-weak, centre-strong:
    // pull(r) ramps from 0 at the rim to maxPull near the eye via smoothstep.
    public static Vector2 EvaluateVortexForce(Vector2 worldOffset, float radius, float maxPullForce, float swirlRatio)
    {
        if (radius <= 0f)
            return Vector2.zero;

        float r = worldOffset.magnitude;
        if (r >= radius)
            return Vector2.zero;

        // Avoid a divide-by-zero singularity exactly at the eye.
        Vector2 inward = r > 0.0001f ? -worldOffset / r : Vector2.zero;
        Vector2 tangential = new Vector2(-inward.y, inward.x);

        float depth01 = Mathf.SmoothStep(0f, 1f, 1f - (r / radius));
        float pull = maxPullForce * depth01;

        return inward * pull + tangential * (pull * swirlRatio);
    }

    // Damage per second at distance r from the eye: rim rate at the edge ramping to
    // the eye rate at the centre, on the same depth curve as the pull.
    public static float EvaluateDamageRate(float r, float radius, float rimRate, float eyeRate)
    {
        if (radius <= 0f)
            return 0f;

        float depth01 = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01(r / radius));
        return Mathf.Lerp(rimRate, eyeRate, depth01);
    }

    void Awake()
    {
        if (areaTrigger == null)
            areaTrigger = GetComponent<CircleCollider2D>();
        if (areaTrigger != null)
            areaTrigger.isTrigger = true;

        ConfigureParticles();
    }

    void OnValidate()
    {
        if (areaTrigger == null)
            areaTrigger = GetComponent<CircleCollider2D>();
        eyeDamagePerSecond = Mathf.Max(eyeDamagePerSecond, rimDamagePerSecond);
        particleSizeMax = Mathf.Max(particleSizeMax, particleSizeMin);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null || other.GetComponentInParent<BoatController>() == null)
            return;

        boatRb = other.attachedRigidbody;
        boatHealth = other.GetComponentInParent<BoatHealthController>();
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other != null && other.attachedRigidbody == boatRb)
        {
            boatRb = null;
            boatHealth = null;
        }
    }

    void FixedUpdate()
    {
        if (boatRb == null)
            return;

        Vector2 offset = boatRb.position - (Vector2)transform.position;
        float radius = Radius;
        if (offset.magnitude >= radius)
            return;

        boatRb.AddForce(EvaluateVortexForce(offset, radius, maxPullForce, swirlRatio));

        if (boatHealth != null)
        {
            float rate = EvaluateDamageRate(offset.magnitude, radius, rimDamagePerSecond, eyeDamagePerSecond);
            if (rate > 0f)
                boatHealth.TakeDamage(rate * Time.fixedDeltaTime, BoatDamageSource.Whirlpool);
        }
    }

    // Creates the particle system in code (like RainVisualController) so no prefab
    // wiring is needed, and configures a spiral of white Sprites/Default billboard
    // squares matching the wave particles, scaled to the collider radius.
    void ConfigureParticles()
    {
        EnsureParticles();
        float radius = Radius > 0f ? Radius : 3f;

        ParticleSystem.MainModule main = vortexParticles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startLifetime = 2.2f;
        main.startSize = new ParticleSystem.MinMaxCurve(particleSizeMin, particleSizeMax);
        main.startColor = particleColor;

        ParticleSystem.ShapeModule shape = vortexParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.radiusThickness = 0.15f; // emit near the rim

        // Spiral inward: orbital (tangential) plus inward radial velocity.
        ParticleSystem.VelocityOverLifetimeModule velocity = vortexParticles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.orbitalZ = 2.4f;
        velocity.radial = -radius * 0.45f;

        ParticleSystem.EmissionModule emission = vortexParticles.emission;
        emission.enabled = true;
        emission.rateOverTime = particleRimEmissionRate;

        ParticleSystemRenderer renderer = vortexParticles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = GetParticleMaterial();
        renderer.sortingLayerName = particleSortingLayerName;
        renderer.sortingOrder = particleSortingOrder;

        if (!vortexParticles.isPlaying)
            vortexParticles.Play();
    }

    void EnsureParticles()
    {
        if (vortexParticles != null)
            return;

        GameObject particleObject = new GameObject("Vortex Particles");
        particleObject.transform.SetParent(transform, false);
        vortexParticles = particleObject.AddComponent<ParticleSystem>();
    }

    Material GetParticleMaterial()
    {
        if (vortexParticleMaterial == null)
            vortexParticleMaterial = new Material(Shader.Find("Sprites/Default"));

        return vortexParticleMaterial;
    }
}
