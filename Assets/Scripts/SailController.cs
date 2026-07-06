using UnityEngine;

// The three positions the sail can be in, stepped through one at a time by input.
public enum SailState { Up, Middle, Down }

// Animates the sail sprite between three scale states (Up / Middle / Down) and
// keeps the sail outline geometry in sync with the current sail size.
//
// The sail "opens" by stretching its localScale.y toward a larger target value.
// Two outline objects track this:
//   - SailOutlineWalls: stretches with the sail (same scale).
//   - SailOutlineBottom: stays a fixed world-space height by counter-scaling,
//     and slides to the open end of the sail as it extends.
public class SailController : MonoBehaviour
{
    // The localScale.y values the sail animates toward for each state.
    // Up = sail furled (small), Down = sail fully open (large).
    [SerializeField] float sailUpScale    = 2f;
    [SerializeField] float sailMidScale   = 12f;
    [SerializeField] float sailDownScale  = 20f;

    // How fast (units per second) the sail scale moves toward its target.
    [SerializeField] float animationSpeed = 10f;

    // The stretched walls of the sail outline; its Y scale mirrors the sail's.
    [SerializeField] Transform sailOutlineWalls;

    // The fixed-height cap at the open end of the sail outline.
    // Must be a child of sailOutlineWalls so it inherits the parent's position offset.
    [SerializeField] Transform sailOutlineBottom;

    // Local Y position of the bottom cap for each sail state.
    // Three separate values allow fine-tuning the cap position at each stage
    // as the sail geometry may not be perfectly linear.
    [SerializeField] float sailUpBottomLocalY   = -0.02f;
    [SerializeField] float sailMidBottomLocalY  = -0.02f;
    [SerializeField] float sailDownBottomLocalY = -0.02f;

    [Header("Wind Effects")]

    // BoatController to read WindEfficiency from each frame.
    [SerializeField] BoatController boatController;

    // X-scale range: sail is narrow when facing headwind, wide when tailwind fills it.
    [SerializeField] float billowMin   = 0.9f;
    [SerializeField] float billowMax   = 1.1f;
    [SerializeField] float billowSpeed = 3f;

    // Flutter: when efficiency drops below threshold the sail luffs (oscillates).
    [SerializeField] [Range(0f, 1f)] float flutterThreshold = 0.15f;
    [SerializeField] float flutterAmplitude = 0.04f;
    [SerializeField] float flutterFrequency = 8f;
    [SerializeField] float flutterFadeSpeed = 4f;

    // Set by BoatVisualController each frame when the boat faces south.
    // When south-facing, the sail sprite is flipped with flipY, which reverses
    // which physical end of the sail is visually "open". Negating bottomY keeps
    // the outline cap on the correct (open) end of the sail.
    public bool FlipOutline { get; set; }

    SailState currentState = SailState.Up;
    float     targetScale;
    SpriteRenderer sailRenderer;

    float currentBillow       = 1f;
    float currentFlutterBlend = 0f;

    // Original localScale.x values captured at startup so billow/flutter multiply
    // on top of the prefab's art scale rather than replacing it.
    float sailOriginalScaleX;
    float outlineOriginalScaleX;

    // The world-space Y scale of the bottom cap at startup, used to restore its
    // true height when counter-scaling against a growing parent.
    float outlineNaturalScaleY;

    void Start()
    {
        sailRenderer = GetComponent<SpriteRenderer>();

        // Cache art scales before any runtime modification so billow/flutter
        // multiply on top of the prefab values rather than replacing them.
        sailOriginalScaleX    = transform.localScale.x;
        if (sailOutlineWalls != null)
            outlineOriginalScaleX = sailOutlineWalls.localScale.x;
        else
            outlineOriginalScaleX = 1f;

        // Cache the bottom cap's natural world scale before any animation runs.
        if (sailOutlineBottom != null)
            outlineNaturalScaleY = sailOutlineBottom.lossyScale.y;

        // Start fully raised so the boat loads with the sail furled.
        targetScale = sailUpScale;
        SetScaleY(sailUpScale);
        UpdateOutlines();
    }

    void Update()
    {
        // Recalculate target in case state changed this frame via RaiseSail/LowerSail.
        UpdateTarget();

        // Smoothly animate the sail's Y scale toward the current target.
        Vector3 s = transform.localScale;
        s.y = Mathf.MoveTowards(s.y, targetScale, animationSpeed * Time.deltaTime);

        // --- Wind effect: billowing ---
        // Widens the sail when running downwind, flattens it when heading into wind.
        // Disabled (returns to 1) when the sail is furled.
        float efficiency = 0f;
        if (boatController != null)
            efficiency = boatController.WindEfficiency;

        float billowTarget;
        if (currentState != SailState.Up)
            billowTarget = Mathf.Lerp(billowMin, billowMax, efficiency);
        else
            billowTarget = 1f;

        currentBillow = Mathf.MoveTowards(currentBillow, billowTarget, billowSpeed * Time.deltaTime);

        // --- Wind effect: flapping / luffing ---
        // When efficiency is near zero the sail oscillates as if luffing in a headwind.
        // Blends in and out smoothly so there's no snap when crossing the threshold.
        bool  luffing     = currentState != SailState.Up && efficiency < flutterThreshold;
        float flutterBlendTarget;
        if (luffing)
            flutterBlendTarget = 1f;
        else
            flutterBlendTarget = 0f;

        currentFlutterBlend = Mathf.MoveTowards(currentFlutterBlend, flutterBlendTarget, flutterFadeSpeed * Time.deltaTime);
        float flutter = Mathf.Sin(Time.time * flutterFrequency) * flutterAmplitude * currentFlutterBlend;

        s.x = sailOriginalScaleX * (currentBillow + flutter);
        transform.localScale = s;

        UpdateOutlines();
    }

    // Syncs the sail outline objects to the sail's current scale each frame.
    void UpdateOutlines()
    {
        float scaleY = transform.localScale.y;

        // Stretch the outline walls to match the sail in both axes.
        if (sailOutlineWalls != null)
        {
            Vector3 ws = sailOutlineWalls.localScale;
            ws.y = scaleY;
            ws.x = outlineOriginalScaleX * (transform.localScale.x / sailOriginalScaleX);
            sailOutlineWalls.localScale = ws;
        }

        if (sailOutlineBottom != null)
        {
            // The bottom cap is a child of sailOutlineWalls and inherits its growing
            // scale. Counter-scale it so it keeps a constant world-space height
            // regardless of how far the sail has extended.
            Vector3 cs = sailOutlineBottom.localScale;
            cs.y = outlineNaturalScaleY / transform.lossyScale.y;
            sailOutlineBottom.localScale = cs;

            // Slide the cap's local Y position between per-state values as the
            // sail animates, using the current scale as the blend parameter.
            float s = transform.localScale.y;
            float bottomY;
            if (s <= sailMidScale)
                bottomY = Mathf.Lerp(sailUpBottomLocalY,  sailMidBottomLocalY,  Mathf.InverseLerp(sailUpScale,  sailMidScale,  s));
            else
                bottomY = Mathf.Lerp(sailMidBottomLocalY, sailDownBottomLocalY, Mathf.InverseLerp(sailMidScale, sailDownScale, s));

            // Negate when south-facing: flipY on the sail swaps which physical end
            // is the open end, so the cap must move to the opposite local side.
            Vector3 lp = sailOutlineBottom.localPosition;
            if (FlipOutline)
                lp.y = -bottomY;
            else
                lp.y = bottomY;
            sailOutlineBottom.localPosition = lp;
        }
    }

    // Steps the sail up by one state per call (Down -> Middle -> Up).
    public void RaiseSail()
    {
        if      (currentState == SailState.Down)   currentState = SailState.Middle;
        else if (currentState == SailState.Middle) currentState = SailState.Up;
        UpdateTarget();
    }

    // Steps the sail down by one state per call (Up -> Middle -> Down).
    public void LowerSail()
    {
        if      (currentState == SailState.Up)     currentState = SailState.Middle;
        else if (currentState == SailState.Middle) currentState = SailState.Down;
        UpdateTarget();
    }

    // Maps the current enum state to the corresponding scale value.
    void UpdateTarget()
    {
        if (currentState == SailState.Up)
            targetScale = sailUpScale;
        else if (currentState == SailState.Middle)
            targetScale = sailMidScale;
        else if (currentState == SailState.Down)
            targetScale = sailDownScale;
        else
            targetScale = sailUpScale;
    }

    // Sets the sail's Y scale directly, bypassing animation (used on startup).
    void SetScaleY(float y)
    {
        Vector3 s = transform.localScale;
        s.y = y;
        transform.localScale = s;
    }

    // Read-only access for other scripts (e.g. SideSailController) that need
    // to mirror the sail's current state and scale without controlling it.
    public SailState CurrentState  => currentState;
    public float     CurrentScale  => transform.localScale.y;
    public float     SailUpScale   => sailUpScale;
    public float     SailMidScale  => sailMidScale;
    public float     SailDownScale => sailDownScale;
}
