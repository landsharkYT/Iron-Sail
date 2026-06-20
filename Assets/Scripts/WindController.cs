using UnityEngine;

// Manages global wind state: direction and speed.
//
// Wind now shifts in one primary channel:
//   - Direction: smoothly rotates toward a random target angle, then holds for
//     a random duration before picking a new target.
//
// Strength is kept intentionally stable so the player can read boat speed
// mostly from heading and sail state instead of hidden gust math.
//
// Other scripts read WindAngle (degrees, world space, where wind blows TO)
// and WindSpeed (0–1 normalised) each frame. Nothing else needs to touch this.
[DefaultExecutionOrder(-10)]
public class WindController : MonoBehaviour
{
    // Degrees per second the wind direction rotates toward its current target.
    // Slow enough that the player feels a gradual shift, not an instant snap.
    [SerializeField] float directionShiftSpeed = 15f;

    // How close (degrees) to the target before a new target is chosen.
    [SerializeField] float arrivalThreshold = 2f;

    // Seconds the wind holds near its target before drifting to a new direction.
    [SerializeField] float holdTimeMin = 30f;
    [SerializeField] float holdTimeMax = 180f;

    [Header("Strength")]
    // Stable baseline wind strength. This is intentionally steady so speed loss
    // comes from heading mistakes or sail changes rather than invisible gust dips.
    [SerializeField] [Range(0f, 1f)] float baseWindSpeed = 0.85f;

    // Optional subtle variation to keep the world from feeling frozen while
    // still staying far more readable than the old randomized gust model.
    [SerializeField] bool useSubtleSpeedVariation = false;
    [SerializeField] [Range(0f, 0.25f)] float speedVariationAmplitude = 0.05f;
    [SerializeField] float speedVariationPeriodSeconds = 24f;

    // Read by BoatController and UI. Angle in world degrees, where wind blows TO.
    public float WindAngle { get; private set; }

    // Normalised 0–1 wind intensity this frame.
    public float WindSpeed { get; private set; }

    float targetAngle;
    float holdTimer;
    void Start()
    {
        // Start at a random direction and immediately pick a phase so the first
        // shift feels natural rather than always blowing the same way at load.
        WindAngle  = Random.Range(0f, 360f);
        targetAngle = WindAngle;
        PickNewHold();
        WindSpeed = Mathf.Clamp01(baseWindSpeed);
    }

    void Update()
    {
        UpdateDirection();
        UpdateSpeed();
    }

    void UpdateDirection()
    {
        // Rotate toward target at a fixed angular rate, taking the shortest arc.
        float delta = Mathf.DeltaAngle(WindAngle, targetAngle);
        float step  = directionShiftSpeed * Time.deltaTime;

        float arrivalWindow = Mathf.Max(arrivalThreshold, step);
        if (Mathf.Abs(delta) <= arrivalWindow)
        {
            WindAngle = targetAngle;

            // Hold at this direction for a random duration, then pick a new target.
            holdTimer -= Time.deltaTime;
            if (holdTimer <= 0f)
                PickNewTarget();
        }
        else
        {
            WindAngle += Mathf.Sign(delta) * step;
            WindAngle  = (WindAngle + 360f) % 360f;
        }
    }

    void UpdateSpeed()
    {
        if (!useSubtleSpeedVariation || speedVariationAmplitude <= 0f || speedVariationPeriodSeconds <= 0.01f)
        {
            WindSpeed = Mathf.Clamp01(baseWindSpeed);
            return;
        }

        float phase = Time.time * (2f * Mathf.PI / speedVariationPeriodSeconds);
        float offset = Mathf.Sin(phase) * speedVariationAmplitude;
        WindSpeed = Mathf.Clamp01(baseWindSpeed + offset);
    }

    // Chooses a new target angle, re-using the hold timer for the next arrival.
    void PickNewTarget()
    {
        targetAngle = Random.Range(0f, 360f);
        PickNewHold();
    }

    // Resets the hold timer to a random duration within the configured range.
    void PickNewHold()
    {
        holdTimer = Random.Range(holdTimeMin, holdTimeMax);
    }

    void OnValidate()
    {
        directionShiftSpeed = Mathf.Max(0f, directionShiftSpeed);
        arrivalThreshold = Mathf.Max(0.1f, arrivalThreshold);
        holdTimeMin = Mathf.Max(0f, holdTimeMin);
        holdTimeMax = Mathf.Max(holdTimeMin, holdTimeMax);
        baseWindSpeed = Mathf.Clamp01(baseWindSpeed);
        speedVariationAmplitude = Mathf.Clamp(speedVariationAmplitude, 0f, 0.25f);
        speedVariationPeriodSeconds = Mathf.Max(0.01f, speedVariationPeriodSeconds);
    }
}
