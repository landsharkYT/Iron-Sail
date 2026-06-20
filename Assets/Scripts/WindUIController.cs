using UnityEngine;
using UnityEngine.UIElements;

// Drives the wind compass HUD each frame.
//
// Three layers in WindCompass.uxml rotate independently:
//   - wind-arc-layer: rotates to the absolute world wind direction so the green
//     arc shows the helpful downwind sector, while the orbiting wind icon sits
//     opposite it to show where the wind comes from.
//   - compass-ring: never rotates — NESW labels stay fixed in world space.
//   - pointer-layer: rotates to the boat's world heading so the arrow always
//     points in the direction the player is travelling.
//
// Sprites are assigned here rather than in USS because USS url() references
// require project-database GUIDs that break when assets are moved.
//
// Rotation sign note: UI Toolkit uses screen space (Y-axis down), so
// Quaternion.Euler(0, 0, +angle) rotates clockwise on screen. Unity world-space
// Z rotation is CCW-positive, so -boatAngle converts to CW screen-space heading.
public class WindUIController : MonoBehaviour
{
    [SerializeField] WindController windController;
    [SerializeField] Transform      boatTransform;
    [SerializeField] UIDocument     uiDocument;
    [SerializeField] BoatController boatController;
    // Softens the tint transition at the exact arc edge to avoid one-frame flicker
    // when the boat sits right on the boundary between valid and invalid wind.
    [SerializeField] float windTintFeatherDegrees = 4f;
    [SerializeField] [Range(60f, 180f)] float helpfulWindArcDegrees = 150f;

    [Header("Speed Effects")]
    // Speed fraction at which compass shake begins.
    [SerializeField] [Range(0f, 1f)] float shakeThreshold = 0.35f;
    // Max pixel offset of the shake at full speed.
    [SerializeField] float shakeAmplitude = 3f;
    // Speed fraction at which speed lines begin (passed to SpeedLinesElement).
    [SerializeField] [Range(0f, 1f)] float speedLinesThreshold = 0.45f;

    [Header("Sprites")]
    [SerializeField] Sprite spriteWindIndicator;
    [SerializeField] Sprite spriteWindIndicatorBlackened;
    [SerializeField] Sprite spriteWindArrow;
    [SerializeField] Sprite spritePointer;

    VisualElement      windArcLayer;
    VisualElement      pointerLayer;
    VisualElement      windArrow;
    // Shared HUD parent so the compass and time-of-day icon shake together.
    VisualElement      hudRightStack;
    SpeedLinesElement  speedLines;
    WindArcElement     windIndicator;

    static readonly Color ColorGoodWind = Color.white;
    static readonly Color ColorDeadZone = new Color(0.45f, 0.45f, 0.45f, 1f);

    void Start()
    {
        var root = uiDocument.rootVisualElement;

        windArcLayer    = root.Q("wind-arc-layer");
        pointerLayer    = root.Q("pointer-layer");
        windArrow       = root.Q("wind-arrow");
        hudRightStack   = root.Q("hud-right-stack");
        windIndicator   = root.Q<WindArcElement>("wind-indicator");

        speedLines = root.Q<SpeedLinesElement>("speed-lines");
        if (speedLines != null) speedLines.Threshold = speedLinesThreshold;

        if (windIndicator != null) windIndicator.ArcSprite = spriteWindIndicator;
        if (windIndicator != null) windIndicator.ArcDegrees = helpfulWindArcDegrees;

        SetSprite(root.Q("wind-indicator-blackened"), spriteWindIndicatorBlackened);
        SetSprite(root.Q("wind-arrow"),               spriteWindArrow);
        SetSprite(root.Q("pointer"),                  spritePointer);

        // Glow: same sprite as the arrow, black tint, rendered behind it.
        var glow = root.Q("wind-arrow-glow");
        SetSprite(glow, spriteWindArrow);
        if (glow != null)
            glow.style.unityBackgroundImageTintColor = Color.black;

        UpdateWindIconTint();
    }

    void Update()
    {
        if (windController == null || boatTransform == null) return;

        float boatAngle = boatTransform.eulerAngles.z;
        float windAngle = windController.WindAngle;

        // Pointer rotates to show the boat's world heading.
        if (pointerLayer != null)
#pragma warning disable CS0618
            pointerLayer.transform.rotation = Quaternion.Euler(0f, 0f, -boatAngle);
#pragma warning restore CS0618

        // Wind arc layer rotates to the downwind direction.
        // The green arc is authored at the top of the layer, while the wind icon
        // is authored at the bottom, so one layer rotation keeps them opposite.
        if (windArcLayer != null)
#pragma warning disable CS0618
            windArcLayer.transform.rotation = Quaternion.Euler(0f, 0f, windAngle);
#pragma warning restore CS0618

        // Speed effects: share one SpeedFraction read per frame.
        float speedFraction = 0f;
        if (boatController != null)
            speedFraction = boatController.SpeedFraction;

        // Compass shake: two sine waves at coprime frequencies give irregular rattle.
        if (hudRightStack != null)
        {
            float shakeStrength = Mathf.Clamp01((speedFraction - shakeThreshold)
                                / Mathf.Max(1f - shakeThreshold, 0.01f));
            float sx = Mathf.Sin(Time.time * 37f) * shakeAmplitude * shakeStrength;
            float sy = Mathf.Sin(Time.time * 43f) * shakeAmplitude * shakeStrength;
#pragma warning disable CS0618
            hudRightStack.transform.position = new Vector3(sx, sy, 0f);
#pragma warning restore CS0618
        }

        // Speed lines: push current fraction, boat screen position, and heading.
        if (speedLines != null)
        {
            speedLines.SpeedFraction = speedFraction;

            float   pw = speedLines.resolvedStyle.width;
            float   ph = speedLines.resolvedStyle.height;
            Camera  worldCamera = Camera.main;

            // Only drive the speed-line origin from the boat projection when the
            // boat is actually in front of the camera and inside the viewport.
            // Otherwise let the element fall back to its built-in centered origin.
            if (pw > 0f && ph > 0f && worldCamera != null)
            {
                Vector3 viewportPoint = worldCamera.WorldToViewportPoint(boatTransform.position);
                bool isProjectedVisible =
                    float.IsFinite(viewportPoint.x) &&
                    float.IsFinite(viewportPoint.y) &&
                    float.IsFinite(viewportPoint.z) &&
                    viewportPoint.z > 0f &&
                    viewportPoint.x >= 0f && viewportPoint.x <= 1f &&
                    viewportPoint.y >= 0f && viewportPoint.y <= 1f;

                if (isProjectedVisible)
                {
                    speedLines.ScreenCenter = new Vector2(
                        viewportPoint.x * pw,
                        (1f - viewportPoint.y) * ph);
                }
                else
                {
                    speedLines.ScreenCenter = Vector2.zero;
                }
            }
            else
            {
                speedLines.ScreenCenter = Vector2.zero;
            }

            Vector2 worldFwd = boatTransform.up;
            speedLines.ScreenForward = new Vector2(worldFwd.x, -worldFwd.y);
        }

        UpdateWindIconTint();
    }

    void SetSprite(VisualElement element, Sprite sprite)
    {
        if (element == null || sprite == null) return;
        element.style.backgroundImage = new StyleBackground(sprite);
    }

    void UpdateWindIconTint()
    {
        if (windArrow == null || windController == null || boatTransform == null) return;

        if (boatController != null)
        {
            float efficiency = boatController.WindEfficiency;
            windArrow.style.unityBackgroundImageTintColor = Color.Lerp(ColorDeadZone, ColorGoodWind, efficiency);
            return;
        }

        float arcDegrees = 180f;
        if (windIndicator != null)
            arcDegrees = windIndicator.ArcDegrees;

        float halfArc  = arcDegrees * 0.5f;
        float feather  = Mathf.Max(windTintFeatherDegrees, 0f);
        float windRad  = windController.WindAngle * Mathf.Deg2Rad;
        Vector2 windDir = new Vector2(Mathf.Sin(windRad), Mathf.Cos(windRad));
        float angleOff = Vector2.Angle(boatTransform.up, windDir);

        // Tint is based on the green arc, not the icon position:
        // if the boat points into the helpful downwind sector, the icon should
        // stay bright; otherwise it fades toward gray in the black sector.
        float inArc = 1f;
        if (feather > 0f)
            inArc = 1f - Mathf.Clamp01((angleOff - halfArc) / feather);
        else if (angleOff > halfArc)
            inArc = 0f;

        windArrow.style.unityBackgroundImageTintColor = Color.Lerp(ColorDeadZone, ColorGoodWind, inArc);
    }
}
