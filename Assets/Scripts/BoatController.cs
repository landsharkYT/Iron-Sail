using UnityEngine;
using UnityEngine.InputSystem;

// Handles all player input and physics-based movement for the boat.
//
// Movement model (Valheim-inspired):
//   Wind pushes the boat forward based on the angle between the boat's heading
//   and the wind direction. Tailwind = full force, beam reach = partial, headwind = zero.
//   The sail state (Up/Mid/Down) gates how much wind force is captured.
//   At SailUp the player can also paddle backwards by holding S.
//
// Inertia:
//   Force is applied to a Rigidbody2D each frame. The rb's linear drag simulates
//   water resistance, so the boat coasts to a stop naturally when wind drops or
//   the sail is raised. Mass and drag are tuned in the Inspector.
//
// Turn rate:
//   Scales linearly with current speed so the boat steers sluggishly when slow
//   and responsively at speed, matching how a real hull behaves in water.
[RequireComponent(typeof(Rigidbody2D))]
public class BoatController : MonoBehaviour
{
    [Header("Turning")]

    // Turn rate (degrees/second) at maximum speed.
    // At lower speeds this is scaled down proportionally.
    [SerializeField] float turnSpeedMax = 180f;

    // Minimum turn rate applied even when nearly stationary, so the player
    // can always nudge the bow without waiting to build speed.
    [SerializeField] float turnSpeedMin = 20f;

    // Speed (units/s) at which the boat reaches its full turn rate.
    // Above this value turn rate is clamped to turnSpeedMax.
    [SerializeField] float fullTurnSpeed = 5f;

    [Header("Speed Reference")]

    // Terminal velocity estimate for this boat's mass/drag/force combo.
    // All speed-based visual effects normalise against this value, so changing
    // physics only requires updating this one number per boat prefab.
    // Formula: maxSailForce / (linearDrag * mass). Default: 10 / (1.5 * 1) ≈ 6.67.
    [SerializeField] float referenceMaxSpeed = 6f;

    // 0–1 fraction of referenceMaxSpeed. Read by camera, UI, and effect scripts.
    public float SpeedFraction
    {
        get
        {
            if (rb != null)
                return Mathf.Clamp01(rb.linearVelocity.magnitude / referenceMaxSpeed);

            return 0f;
        }
    }

    [Header("Sail & Wind")]

    // Reference to the sail, used to read current sail state and trigger raise/lower.
    [SerializeField] SailController sailController;

    // The wind source this boat reads from.
    [SerializeField] WindController windController;

    // Maximum force (units/s²) applied when sailing at full tailwind with sail down.
    [SerializeField] float maxSailForce = 10f;

    // Efficiency floor applied after the raw heading-vs-wind dot product.
    // Keep this at 0 when you want the wind HUD's dead zone to match propulsion:
    // outside the helpful half-circle, the sail produces no wind force.
    // Higher values intentionally add "forgiveness" and will no longer match the
    // current wind UI's hard no-wind sector exactly.
    [SerializeField] [Range(0f, 1f)] float minWindEfficiency = 0f;

    // Full-speed band around dead-downwind. Inside this angle, the sail keeps
    // near-maximum efficiency instead of already falling off sharply.
    [SerializeField] [Range(0f, 89f)] float fullWindEfficiencyHalfAngle = 50f;

    // Absolute cutoff for useful wind. Between the full-speed band and this
    // angle, efficiency tapers down quickly toward zero.
    [SerializeField] [Range(1f, 90f)] float maxWindEfficiencyHalfAngle = 75f;

    // Fraction of maxSailForce applied when the sail is at Middle state.
    [SerializeField] float midSailFraction = 0.5f;

    // Maximum force applied when paddling backwards (S held at SailUp).
    [SerializeField] float paddleForce = 2f;

    // How fast (force units per second) the paddle builds to full strength.
    [SerializeField] float paddleAcceleration = 1f;

    // Raw 0-1 wind efficiency based on heading angle alone (not gated by sail state).
    // Read by SailController for billowing and flapping visuals.
    public float WindEfficiency { get; private set; }

    // True only while the player is actively using the furled-sail backward paddle input.
    public bool IsPaddlingBackward { get; private set; }

    Rigidbody2D rb;
    float currentPaddleForce;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        if (!InventoryUIController.IsInventoryOpen && !WorldMapUIController.IsMapOpen && !ShopController.IsShopOpen && !FishingMinigameController.IsFishingOpen && !PauseMenuController.IsPauseOpen && !EndMenuController.IsEndMenuOpen)
            HandleSailInput();
    }

    void FixedUpdate()
    {
        if (!InventoryUIController.IsInventoryOpen && !WorldMapUIController.IsMapOpen && !ShopController.IsShopOpen && !FishingMinigameController.IsFishingOpen && !PauseMenuController.IsPauseOpen && !EndMenuController.IsEndMenuOpen)
        {
            HandleRotation();
            HandlePaddle();
        }
        else
        {
            IsPaddlingBackward = false;
        }

        HandleWindForce();
    }

    // Rotates the boat around its Z axis while A or D is held.
    // Turn rate scales with current speed: slow at rest, full rate at fullTurnSpeed.
    void HandleRotation()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float turn = 0f;
        if (GameRuntimeSettings.IsTurnLeftPressed(kb)) turn =  1f;
        if (GameRuntimeSettings.IsTurnRightPressed(kb)) turn = -1f;
        if (turn == 0f) return;

        // Map current speed to a 0-1 factor, then lerp between min and max turn rates.
        float speed      = rb.linearVelocity.magnitude;
        float speedFactor = Mathf.Clamp01(speed / fullTurnSpeed);
        float turnRate   = Mathf.Lerp(turnSpeedMin, turnSpeedMax, speedFactor);

        transform.Rotate(0f, 0f, turn * turnRate * Time.fixedDeltaTime);

        // Keep the rb in sync so its internal rotation matches the transform.
        rb.MoveRotation(transform.eulerAngles.z);
    }

    // Applies wind force along the boat's forward axis, scaled by:
    //   1. The cosine of the angle between heading and wind (efficiency curve).
    //   2. The current wind speed (0–1 normalised).
    //   3. The active sail state multiplier.
    void HandleWindForce()
    {
        if (windController == null) return;

        Vector2 boatForward = transform.up;
        float   windRad     = windController.WindAngle * Mathf.Deg2Rad;
        Vector2 windDir     = new(Mathf.Sin(windRad), Mathf.Cos(windRad));

        // Keep a strong center band, then only taper speed near the tips of the
        // helpful sector instead of across the whole half-circle.
        float angleOff = Vector2.Angle(boatForward, windDir);
        WindEfficiency = EvaluateWindEfficiency(angleOff);

        if (sailController == null) return;
        float sailMultiplier = GetSailMultiplier();
        if (sailMultiplier <= 0f) return;

        // Remap from [0,1] to [minWindEfficiency,1].
        // With the default floor at 0, this is a pure cosine-style falloff:
        // tailwind is strongest, broad reach is moderate, and beam reach drops to zero.
        float efficiency = WindEfficiency > 0f
            ? Mathf.Lerp(minWindEfficiency, 1f, WindEfficiency)
            : 0f;
        float force      = maxSailForce * efficiency * windController.WindSpeed * sailMultiplier;
        rb.AddForce(boatForward * force);
    }

    float EvaluateWindEfficiency(float angleOffDegrees)
    {
        float fullAngle = Mathf.Clamp(fullWindEfficiencyHalfAngle, 0f, 89f);
        float maxAngle = Mathf.Clamp(maxWindEfficiencyHalfAngle, fullAngle + 0.1f, 90f);

        if (angleOffDegrees <= fullAngle)
            return 1f;

        if (angleOffDegrees >= maxAngle)
            return 0f;

        float t = Mathf.InverseLerp(fullAngle, maxAngle, angleOffDegrees);
        return 1f - Mathf.SmoothStep(0f, 1f, t);
    }

    // Applies a backward force while S is held and the sail is fully up/furled.
    // Force ramps up via paddleAcceleration so there is a brief wind-up before
    // reaching full paddle strength. Ramps back to zero when released.
    void HandlePaddle()
    {
        var kb = Keyboard.current;
        bool paddling = kb != null
                     && sailController != null
                     && sailController.CurrentState == SailState.Up
                     && GameRuntimeSettings.IsRaiseSailHeld(kb);

        IsPaddlingBackward = paddling;

        float target;
        if (paddling)
            target = paddleForce;
        else
            target = 0f;

        currentPaddleForce = Mathf.MoveTowards(currentPaddleForce, target, paddleAcceleration * Time.fixedDeltaTime);

        if (currentPaddleForce > 0f)
            rb.AddForce(-transform.up * currentPaddleForce);
    }

    // Steps the sail state on W/S press.
    // W lowers/deploys the sail one step, S raises/furls it one step.
    // Once fully furled, holding S becomes backward paddling.
    void HandleSailInput()
    {
        var kb = Keyboard.current;
        if (kb == null || sailController == null) return;

        if (GameRuntimeSettings.WasLowerSailPressedThisFrame(kb) && sailController.CurrentState != SailState.Down)
            sailController.LowerSail();

        if (sailController.CurrentState != SailState.Up && GameRuntimeSettings.WasRaiseSailPressedThisFrame(kb))
            sailController.RaiseSail();
    }

    // Maps sail state to a force multiplier.
    float GetSailMultiplier()
    {
        if (sailController.CurrentState == SailState.Up)
            return 0f;
        if (sailController.CurrentState == SailState.Middle)
            return midSailFraction;
        return 1f;
    }
}
