using UnityEngine;

// Drives the side-view sail outline so it matches the main sail's current
// state and scale. This script reads from SailController rather than owning
// any sail state itself, ensuring both views stay perfectly in sync.
//
// The side sail outline consists of two parts:
//   - MastSideSailWalls: stretches vertically to represent the sail's extent.
//   - MastSideSailBottom: a fixed-height cap at the open end, parented under
//     MastSideSailWalls so it rides along as the walls grow.
public class SideSailController : MonoBehaviour
{
    // The main sail whose state and scale this script mirrors.
    [SerializeField] SailController sailController;

    // The stretched walls of the side sail outline.
    [SerializeField] Transform mastSideSailWalls;

    // The fixed-height cap at the open end of the side sail.
    // Must be a child of mastSideSailWalls so it inherits the parent's offset.
    [SerializeField] Transform mastSideSailBottom;

    // The top cap of the side sail outline. This correction path is kept
    // separate because it specifically helped the upward-facing overshoot.
    [SerializeField] Transform mastSideSailTop;

    // Target localScale.y values for the side sail walls at each sail state.
    // These are tuned separately from the main sail because the side-view
    // sprite proportions differ from the top-down sprite.
    [SerializeField] float sideUpScale   = 2f;
    [SerializeField] float sideMidScale  = 12f;
    [SerializeField] float sideDownScale = 20f;

    // Optional extra downward-facing wall stretch.
    // Kept at zero in the stable baseline, but left available for future tuning
    // because it only affects scale, never the wall transform position.
    [SerializeField] float downwardWallsStretchBonus = 0f;

    // Local Y position of the bottom cap for each sail state in side view.
    [SerializeField] float sideUpBottomLocalY   = -0.02f;
    [SerializeField] float sideMidBottomLocalY  = -0.02f;
    [SerializeField] float sideDownBottomLocalY = -0.02f;

    // Upward-facing overshoot fix for the outline top cap.
    [SerializeField] float upwardTopCapScaleY = 1.22f;
    [SerializeField] float upwardTopCapOffsetX = 0.04f;

    // Hide the side outline only when the sail is genuinely furled, not merely
    // near the up scale during transitions or startup timing.
    [SerializeField] float upRestScaleTolerance = 0.005f;

    // World-space Y scale of the bottom cap at startup, used for counter-scaling.
    float outlineNaturalScaleY;
    float topCapNaturalScaleY;
    Vector3 topCapNaturalLocalPosition;

    [SerializeField] SailState debugCurrentState;
    [SerializeField] float debugCurrentScale;
    [SerializeField] bool debugAtUpRest;
    [SerializeField] float debugUpwardBlend;
    [SerializeField] float debugDownwardBlend;

    void Start()
    {
        if (mastSideSailTop == null)
            mastSideSailTop = transform.Find("MastSideSailTop");

        // Cache the cap's natural world scale before any parent scaling occurs.
        if (mastSideSailBottom != null)
            outlineNaturalScaleY = mastSideSailBottom.lossyScale.y;

        if (mastSideSailTop != null)
        {
            topCapNaturalScaleY = mastSideSailTop.localScale.y;
            topCapNaturalLocalPosition = mastSideSailTop.localPosition;
        }

        // Leave the current active state alone on startup and let Update decide
        // from real sail state/scale once everything has initialized.
    }

    void Update()
    {
        if (sailController == null) return;

        // Hide the side outline when the sail is fully raised and at rest.
        // At full Up, the sail sprite itself is small enough to look correct
        // without the outline, and showing it would cause a visual glitch at the
        // exact moment the view switches between top-down and side mode.
        debugCurrentState = sailController.CurrentState;
        debugCurrentScale = sailController.CurrentScale;

        bool atUpRest = sailController.CurrentState == SailState.Up &&
                        Mathf.Abs(sailController.CurrentScale - sailController.SailUpScale) <= upRestScaleTolerance;
        debugAtUpRest = atUpRest;

        SetWallsActive(!atUpRest);
        if (atUpRest) return;

        float mainScale = sailController.CurrentScale;

        // Compute a 0-1 blend factor within the current animation segment.
        // The sail moves through two segments: Up->Mid and Mid->Down.
        // Using InverseLerp gives a normalised t for whichever segment is active.
        float t;
        if (mainScale <= sailController.SailMidScale)
            t = Mathf.InverseLerp(sailController.SailUpScale,  sailController.SailMidScale,  mainScale);
        else
            t = Mathf.InverseLerp(sailController.SailMidScale, sailController.SailDownScale, mainScale);

        // Stretch the side sail walls proportionally to the main sail's current scale.
        if (mastSideSailWalls != null)
        {
            float sideScale;
            if (mainScale <= sailController.SailMidScale)
                sideScale = Mathf.Lerp(sideUpScale,  sideMidScale,  t);
            else
                sideScale = Mathf.Lerp(sideMidScale, sideDownScale, t);

            float downwardBlend = 0f;
            if (sailController != null)
            {
                Vector2 boatForward = sailController.transform.root.up;
                downwardBlend = Mathf.Clamp01(-boatForward.y);
            }
            debugDownwardBlend = downwardBlend;

            Vector3 ws = mastSideSailWalls.localScale;
            ws.y = sideScale + (downwardWallsStretchBonus * downwardBlend);
            mastSideSailWalls.localScale = ws;
        }

        if (mastSideSailBottom != null)
        {
            // Guard against a near-zero parent scale to avoid a divide-by-zero
            // in the counter-scale calculation below.
            if (Mathf.Abs(mastSideSailWalls.lossyScale.y) < 0.0001f) return;

            // Counter-scale the cap against its growing parent so it keeps a
            // constant world-space height as the walls stretch.
            Vector3 cs = mastSideSailBottom.localScale;
            cs.y = outlineNaturalScaleY / mastSideSailWalls.lossyScale.y;
            mastSideSailBottom.localScale = cs;

            // Slide the cap to the correct local Y position for the current sail state.
            float bottomY;
            if (mainScale <= sailController.SailMidScale)
                bottomY = Mathf.Lerp(sideUpBottomLocalY,  sideMidBottomLocalY,  t);
            else
                bottomY = Mathf.Lerp(sideMidBottomLocalY, sideDownBottomLocalY, t);

            Vector3 lp = mastSideSailBottom.localPosition;
            lp.y = bottomY;
            mastSideSailBottom.localPosition = lp;
        }

        float upwardBlend = 0f;
        if (sailController != null)
        {
            Vector2 boatForward = sailController.transform.root.up;
            if (boatForward.y > 0f)
                upwardBlend = 1f;
        }
        debugUpwardBlend = upwardBlend;

        if (mastSideSailTop != null)
        {
            float topCapScaleY = Mathf.Lerp(1f, upwardTopCapScaleY, upwardBlend);
            float topCapOffsetX = upwardTopCapOffsetX * upwardBlend;

            Vector3 ts = mastSideSailTop.localScale;
            ts.y = topCapNaturalScaleY * topCapScaleY;
            mastSideSailTop.localScale = ts;

            Vector3 tp = mastSideSailTop.localPosition;
            tp.x = topCapNaturalLocalPosition.x - topCapOffsetX;
            tp.y = topCapNaturalLocalPosition.y;
            mastSideSailTop.localPosition = tp;
        }
    }

    // Activates or deactivates both outline objects together.
    void SetWallsActive(bool active)
    {
        if (mastSideSailWalls  != null) mastSideSailWalls.gameObject.SetActive(active);
        if (mastSideSailBottom != null) mastSideSailBottom.gameObject.SetActive(active);
    }
}
