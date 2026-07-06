using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Manages all top-down visual switching, directional sprite adjustments,
// and turn-feel effects for the boat.
//
// The boat has two visual modes:
//   - Top-down view: used when facing roughly north or south (main boat sprites).
//   - Side view: used when facing roughly east or west (sideboat prefab).
//
// Additionally, when facing south the top-down sprites need two corrections:
//   1. Texture flip (flipY): mirrors the sprite around its own pivot so the
//      artwork appears correctly oriented without moving the object.
//   2. Position flip (negate local Y): moves mast/sail to the correct end of
//      the hull, since the boat's rotation alone would leave them on the wrong side.
//
// When facing north, the mast and mast outline are raised above the sail in
// sorting order so they visually appear in front of it.
//
// Turn effects (all applied to visualRoot, which must contain all top-down sprites):
//   - Banking lean: squishes localScale.x slightly when turning, suggesting the hull
//     heeling into the turn.
//   - Overshoot: visualRoot's rotation slightly leads the true angle then springs back
//     via a damped spring, giving a physical momentum feel.
//   - Sail shift: the sail nudges sideways in the turn direction, as if catching wind
//     on the new tack.
public class BoatVisualController : MonoBehaviour
{
    public enum BoatViewMode
    {
        TopDown,
        Side
    }

    // Parent transform containing all top-down view sprites.
    // Used for the banking lean (localScale.x squish) and overshoot rotation.
    // The sideboat must NOT be under this transform.
    [SerializeField] Transform visualRoot;

    // The root GameObject of the sideboat prefab, shown when facing east or west.
    [SerializeField] GameObject   sideboatRoot;

    // GameObjects whose SpriteRenderers make up the top-down boat view.
    // Used to fade them out when switching to the side view.
    [SerializeField] GameObject[] mainBoatVisuals;

    // Reference to the sail so this script can tell it which way the boat faces.
    // SailController needs this to correctly place the sail outline when south-facing.
    [SerializeField] SailController sailController;

    // How close to east or west (in degrees) the boat must face before the
    // side view activates. A larger value widens the arc that shows the side view.
    [SerializeField] float sideAngleThreshold = 60f;

    // X position of the hull centre in the sideboat's local space.
    // When the sideboat is flipped horizontally to face west, simply negating
    // localScale.x would mirror around the transform origin and cause the hull
    // to jump sideways. Instead, the position is offset by 2 * flipPivotX so
    // the flip mirrors around the hull centre, keeping it visually planted.
    [SerializeField] float flipPivotX = 0.383f;

    [Header("North/South")]

    // Sprites that receive a vertical texture flip (flipY) when facing south.
    // flipY mirrors the texture around the sprite's own pivot, no position change.
    [SerializeField] SpriteRenderer[] southFlipRenderers;

    // Transforms whose local Y position is negated when facing south.
    // This moves mast, sail, and sail outline to the correct end of the hull.
    // Negating rather than inverting the full position preserves X and Z offsets.
    [SerializeField] Transform[] southYFlipTransforms;

    // Renderers (mast, mast outline) raised above the sail in sorting order
    // when facing north, so they appear on top of the sail visually.
    [SerializeField] SpriteRenderer[] northTopRenderers;

    // The sail renderer used as the sorting order reference point.
    // northTopRenderers are assigned orders above this value when facing north.
    [SerializeField] SpriteRenderer northTopSailRef;

    [Header("Side View Motion")]

    // Wave bob: vertical sine oscillation on the side sprites, amplitude grows with speed.
    [SerializeField] float bobAmplitude  = 0.12f;
    [SerializeField] float bobFrequency  = 2.2f;
    // Speed (units/s) at which bob reaches full amplitude.
    [SerializeField] float bobFullSpeed  = 5f;

    // Speed pitch: bow-up rotation that builds with speed, like a hull planing.
    // Degrees of tilt at full speed.
    [SerializeField] float pitchMax       = 5f;
    // Speed at which pitch reaches pitchMax.
    [SerializeField] float pitchFullSpeed = 6f;
    // How fast pitch eases in and out.
    [SerializeField] float pitchSmoothing = 3f;

    [Header("Turn Effects")]

    // Max amount localScale.x is squished at full turn. 0.05 = 5% squish.
    [SerializeField] float leanAmount = 0.05f;
    // How fast the lean builds and releases toward its target each second.
    [SerializeField] float leanSpeed  = 8f;

    // Max extra degrees the visual rotates past the true angle during a turn.
    [SerializeField] float overshootAmount  = 3f;
    // Spring constant: higher = snappier return to true angle.
    [SerializeField] float overshootSpring  = 40f;
    // Damping constant: higher = less oscillation after overshoot.
    [SerializeField] float overshootDamping = 6f;

    // Transforms that shift sideways together during a turn (Sail + SailOutlineWalls).
    // Grouped so the outline never drifts from the sail as it shifts.
    [SerializeField] Transform[] sailShiftTransforms;
    // Max local X offset applied to each sail transform at full turn.
    [SerializeField] float sailShiftAmount = 0.05f;
    // How fast the sail shift eases in and out each second.
    [SerializeField] float sailShiftSpeed  = 6f;

    // The side-view sail transform that shifts during a turn.
    [SerializeField] Transform sideSailShiftTransform;
    // Max local X offset applied to the side sail at full turn.
    [SerializeField] float sideSailShiftAmount = 0.05f;
    // How fast the side sail shift eases in and out each second.
    [SerializeField] float sideSailShiftSpeed  = 6f;

    // Cached at runtime; avoids repeated GetComponent calls each frame.
    SpriteRenderer[] mainRenderers;
    SpriteRenderer[] sideboatRenderers;

    // The sideboat's local position at startup, used as the base for
    // the west-facing offset calculation each frame.
    Vector3 sideboatOriginalPos;
    Quaternion sideboatOriginalLocalRotation;

    // Sorting orders of northTopRenderers as set in the prefab, restored
    // when the boat stops facing north.
    int[] northTopOriginalOrders;

    // The sail's sorting order captured at startup, used as the baseline
    // when elevating mast/outline above it.
    int sailSortingOrder;

    // Local Y positions of southYFlipTransforms as set in the prefab.
    // Stored so they can be negated when south and restored when not.
    float[] southOriginalLocalY;

    // Local X positions of sailShiftTransforms at startup, used as bases for
    // the sideways shift so each always eases back to its exact original position.
    float[] sailOriginalLocalX;

    // Local X position of the side sail at startup, same purpose.
    float sideSailOriginalLocalX;

    Rigidbody2D rb;

    // Runtime state for side view motion.
    float currentPitch;

    // Runtime state for the three turn effects.
    float currentLean;
    float currentOvershootAngle;
    float overshootVelocity;
    float currentSailShift;
    float currentSideSailShift;

    public BoatViewMode CurrentViewMode { get; private set; } = BoatViewMode.TopDown;
    public bool IsSideViewActive => CurrentViewMode == BoatViewMode.Side;
    public bool IsFacingEastSideView { get; private set; } = true;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        // Cache sideboat renderers (include inactive children for completeness).
        sideboatRenderers   = sideboatRoot.GetComponentsInChildren<SpriteRenderer>(true);
        sideboatOriginalPos = sideboatRoot.transform.localPosition;
        sideboatOriginalLocalRotation = sideboatRoot.transform.localRotation;

        // Collect the SpriteRenderer from each main-view GameObject.
        // Stored separately from the GameObjects themselves so alpha changes
        // can be applied directly without a GetComponent call per frame.
        var list = new List<SpriteRenderer>();
        foreach (var obj in mainBoatVisuals)
        {
            if (obj == null) continue;
            var sr = obj.GetComponent<SpriteRenderer>();
            if (sr != null) list.Add(sr);
        }
        mainRenderers = list.ToArray();

        // Ensure the sideboat starts at neutral scale (no stale prefab overrides).
        Vector3 s = sideboatRoot.transform.localScale;
        s.x = 1f;
        s.y = 1f;
        sideboatRoot.transform.localScale = s;

        // Start with the top-down view visible and the sideboat hidden.
        sideboatRoot.SetActive(false);
        SetGroupAlpha(mainRenderers,     1f);
        SetGroupAlpha(sideboatRenderers, 0f);

        // Capture the sail's sorting order for the north-top elevation logic.
        if (northTopSailRef != null)
            sailSortingOrder = northTopSailRef.sortingOrder;

        // Snapshot the original sorting orders and local Y positions before
        // any runtime modifications, so they can be restored each frame.
        int northTopLength = 0;
        if (northTopRenderers != null) northTopLength = northTopRenderers.Length;
        northTopOriginalOrders = new int[northTopLength];

        int southYLength = 0;
        if (southYFlipTransforms != null) southYLength = southYFlipTransforms.Length;
        southOriginalLocalY = new float[southYLength];

        for (int i = 0; i < southOriginalLocalY.Length; i++)
            if (southYFlipTransforms[i] != null)
                southOriginalLocalY[i] = southYFlipTransforms[i].localPosition.y;

        for (int i = 0; i < northTopOriginalOrders.Length; i++)
            if (northTopRenderers[i] != null)
                northTopOriginalOrders[i] = northTopRenderers[i].sortingOrder;

        // Capture each sail transform's resting X so shifts ease back to exact original positions.
        int sailShiftLength = 0;
        if (sailShiftTransforms != null)
            sailShiftLength = sailShiftTransforms.Length;
        sailOriginalLocalX = new float[sailShiftLength];
        for (int i = 0; i < sailOriginalLocalX.Length; i++)
            if (sailShiftTransforms[i] != null)
                sailOriginalLocalX[i] = sailShiftTransforms[i].localPosition.x;

        if (sideSailShiftTransform != null)
            sideSailOriginalLocalX = sideSailShiftTransform.localPosition.x;
    }

    void Update()
    {
        float angle = transform.eulerAngles.z;

        // Read turn input once, shared by all three turn effects below.
        float turnInput = 0f;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (GameRuntimeSettings.IsTurnLeftPressed(kb)) turnInput =  1f;
            if (GameRuntimeSettings.IsTurnRightPressed(kb)) turnInput = -1f;
        }

        // The boat's local up-axis points east when eulerAngles.z = 270 and
        // west when z = 90. DeltaAngle gives the shortest signed arc so the
        // comparison works correctly across the 0/360 boundary.
        float distFromEast = Mathf.Abs(Mathf.DeltaAngle(angle, 270f));
        float distFromWest = Mathf.Abs(Mathf.DeltaAngle(angle, 90f));
        bool  showSide     = Mathf.Min(distFromEast, distFromWest) < sideAngleThreshold;
        bool  facingEast   = distFromEast <= distFromWest;
        bool  facingSouth  = Mathf.Abs(Mathf.DeltaAngle(angle, 180f)) < 90f;

        CurrentViewMode = showSide ? BoatViewMode.Side : BoatViewMode.TopDown;
        IsFacingEastSideView = facingEast;

        // Tell the sail whether to negate its outline bottom offset this frame.
        if (sailController != null)
            sailController.FlipOutline = facingSouth;

        // --- Side view orientation ---
        // Mirror the sideboat horizontally when facing west by flipping localScale.x.
        // Compensate the X position by 2 * flipPivotX so the flip pivots around
        // the hull centre rather than the transform origin, preventing hull drift.
        Vector3 ss = sideboatRoot.transform.localScale;
        Vector3 sp = sideboatOriginalPos;
        if (facingEast)
        {
            ss.x = 1f;
        }
        else
        {
            ss.x = -1f;
            sp.x += 2f * flipPivotX;
        }
        ss.y = 1f;
        sideboatRoot.transform.localScale = ss;

        // --- Side view motion: bob and pitch ---
        float speed = 0f;
        if (rb != null)
            speed = rb.linearVelocity.magnitude;
        float speedFactor = Mathf.Clamp01(speed / bobFullSpeed);

        // Bob: sine-wave Y offset added on top of the flip-corrected base position.
        sp.y += Mathf.Sin(Time.time * bobFrequency) * bobAmplitude * speedFactor;
        sideboatRoot.transform.localPosition = sp;

        // Pitch: bow-up tilt that grows with speed. Sign flips with facing direction
        // because the sprite is mirrored for west, so the rotation reads correctly
        // in both cases with the same sign convention.
        float pitchTarget;
        if (showSide)
            pitchTarget = Mathf.Clamp01(speed / pitchFullSpeed) * pitchMax;
        else
            pitchTarget = 0f;

        currentPitch = Mathf.Lerp(currentPitch, pitchTarget, pitchSmoothing * Time.deltaTime);
        float pitchSign;
        if (facingEast)
            pitchSign = 1f;
        else
            pitchSign = -1f;

        sideboatRoot.transform.localRotation =
            sideboatOriginalLocalRotation * Quaternion.Euler(0f, 0f, pitchSign * currentPitch);

        // --- View switching ---
        // Fade alpha rather than disabling the main visuals, because disabling
        // the Sail GameObject would also stop SailController.Update() from running,
        // which would freeze the sail animation mid-transition.
        if (showSide)
        {
            SetGroupAlpha(mainRenderers,     0f);
            SetGroupAlpha(sideboatRenderers, 1f);
        }
        else
        {
            SetGroupAlpha(mainRenderers,     1f);
            SetGroupAlpha(sideboatRenderers, 0f);
        }
        sideboatRoot.SetActive(showSide);

        // --- South-facing: texture flip ---
        // flipY mirrors each sprite's texture around its own sprite pivot.
        // This corrects the artwork orientation without moving the object,
        // which is why position is handled separately below.
        if (southFlipRenderers != null)
        {
            foreach (var sr in southFlipRenderers)
            {
                if (sr == null) continue;
                sr.flipY = facingSouth;
            }
        }

        // --- South-facing: position flip ---
        // With the boat rotated 180°, mast and sail would appear at the bow
        // instead of the stern. Negating their local Y moves them to the correct
        // end. The original Y values are restored when facing north so no drift
        // accumulates over time.
        if (southYFlipTransforms != null)
        {
            for (int i = 0; i < southYFlipTransforms.Length; i++)
            {
                if (southYFlipTransforms[i] == null) continue;
                Vector3 lp = southYFlipTransforms[i].localPosition;
                if (facingSouth)
                    lp.y = -southOriginalLocalY[i];
                else
                    lp.y = southOriginalLocalY[i];
                southYFlipTransforms[i].localPosition = lp;
            }
        }

        // --- North-facing: sorting order ---
        // When facing north the mast sits behind the sail in the default order.
        // Temporarily raise mast and mast outline above the sail's sorting order
        // so they render in front of it. Restored to original values when not north.
        if (northTopRenderers != null)
        {
            bool facingNorth = !facingSouth;
            for (int i = 0; i < northTopRenderers.Length; i++)
            {
                if (northTopRenderers[i] == null) continue;
                if (facingNorth)
                    northTopRenderers[i].sortingOrder = sailSortingOrder + i + 1;
                else
                    northTopRenderers[i].sortingOrder = northTopOriginalOrders[i];
            }
        }

        // --- Turn effect: banking lean ---
        // Squishes localScale.x on the visual group to suggest the hull heeling into
        // the turn. Both left and right turns squish equally (Abs), since a heel
        // always compresses the visible width regardless of direction.
        // MoveTowards eases both in and out so it never snaps.
        float targetLean = turnInput * leanAmount;
        currentLean = Mathf.MoveTowards(currentLean, targetLean, leanSpeed * Time.deltaTime);
        if (visualRoot != null)
        {
            Vector3 vs = visualRoot.localScale;
            vs.x = 1f - Mathf.Abs(currentLean);
            visualRoot.localScale = vs;
        }

        // --- Turn effect: overshoot rotation ---
        // A damped spring drives currentOvershootAngle toward (turnInput * overshootAmount).
        // When the player releases the key the target snaps to 0, the spring overshoots,
        // then damps out, giving the visual a physical momentum feel without moving
        // the true rotation that all direction logic reads from.
        float targetOvershoot = turnInput * overshootAmount;
        overshootVelocity += (targetOvershoot - currentOvershootAngle) * overshootSpring * Time.deltaTime;
        overshootVelocity *= Mathf.Max(0f, 1f - overshootDamping * Time.deltaTime);
        currentOvershootAngle += overshootVelocity * Time.deltaTime;
        if (visualRoot != null)
            visualRoot.rotation = Quaternion.Euler(0f, 0f, angle + currentOvershootAngle);

        // --- Turn effect: sail shift (top-down) ---
        // Sail and SailOutlineWalls nudge sideways together so the outline never drifts
        // from the sail. Runs after southYFlipTransforms so only localPosition.x is
        // changed here; the Y set by the south-flip logic is preserved.
        float targetSailShift = turnInput * sailShiftAmount;
        currentSailShift = Mathf.MoveTowards(currentSailShift, targetSailShift, sailShiftSpeed * Time.deltaTime);
        if (sailShiftTransforms != null)
        {
            for (int i = 0; i < sailShiftTransforms.Length; i++)
            {
                if (sailShiftTransforms[i] == null) continue;
                Vector3 lp = sailShiftTransforms[i].localPosition;
                lp.x = sailOriginalLocalX[i] + currentSailShift;
                sailShiftTransforms[i].localPosition = lp;
            }
        }

        // --- Turn effect: sail shift (side view) ---
        // Mirrors the top-down sail shift for the side sprite so both views
        // respond consistently to turning.
        float targetSideSailShift = turnInput * sideSailShiftAmount;
        currentSideSailShift = Mathf.MoveTowards(currentSideSailShift, targetSideSailShift, sideSailShiftSpeed * Time.deltaTime);
        if (sideSailShiftTransform != null)
        {
            Vector3 lp = sideSailShiftTransform.localPosition;
            lp.x = sideSailOriginalLocalX + currentSideSailShift;
            sideSailShiftTransform.localPosition = lp;
        }
    }

    // Sets the alpha of every renderer in the array to the given value.
    // Used to fade groups of sprites in and out without disabling their GameObjects.
    void SetGroupAlpha(SpriteRenderer[] renderers, float a)
    {
        foreach (var sr in renderers)
        {
            if (sr == null) continue;
            Color c = sr.color;
            c.a = a;
            sr.color = c;
        }
    }
}
