using UnityEngine;
using UnityEngine.UIElements;

// Full-screen speed lines drawn with Painter2D.
// Lines are oriented parallel to the boat's heading and travel backward:
// they spawn ahead of the boat and zip past it in the opposite direction,
// like motion-blur streaks in a racing game.
//
// WindUIController sets SpeedFraction, ScreenCenter, and ScreenForward each frame.
[UxmlElement]
public partial class SpeedLinesElement : VisualElement
{
    // Driven by WindUIController each frame.
    public float   SpeedFraction { get; set; }
    public Vector2 ScreenCenter  { get; set; }
    public Vector2 ScreenForward { get; set; } = Vector2.up;

    // Minimum SpeedFraction before any streaks appear.
    float _threshold = 0.45f;
    [UxmlAttribute]
    public float Threshold
    {
        get => _threshold;
        set => _threshold = value;
    }

    // Number of independent travel lanes.
    const int   LineCount = 36;
    // How fast a line completes one full ahead-to-behind cycle.
    const float CycleSpeedMin = 0.45f;
    const float CycleSpeedMax = 2.4f;
    // Maximum line opacity at full speed before per-line fading is applied.
    const float MaxAlpha      = 0.38f;

    public SpeedLinesElement()
    {
        generateVisualContent += Draw;
        pickingMode = PickingMode.Ignore;
        // Repaint continuously so the procedural streak motion stays smooth.
        RegisterCallback<AttachToPanelEvent>(_ =>
            schedule.Execute(MarkDirtyRepaint).Every(16));
    }

    void Draw(MeshGenerationContext ctx)
    {
        float above = Mathf.Clamp01((SpeedFraction - _threshold)
                    / Mathf.Max(1f - _threshold, 0.01f));
        if (above <= 0f) return;

        float w = resolvedStyle.width;
        float h = resolvedStyle.height;
        if (w <= 0f || h <= 0f) return;

        Vector2 origin;
        if (ScreenCenter.sqrMagnitude > 0f)
            origin = ScreenCenter;
        else
            origin = new Vector2(w * 0.5f, h * 0.5f);

        // Forward axis and its perpendicular (left/right of travel direction).
        Vector2 fwd;
        if (ScreenForward.sqrMagnitude > 0.001f)
            fwd = ScreenForward.normalized;
        else
            fwd = new Vector2(0f, -1f);

        Vector2 perp = new Vector2(-fwd.y, fwd.x);

        float diag = Mathf.Sqrt(w * w + h * h);

        // Travel span runs from comfortably ahead of the boat to well behind it.
        float forwardSpawn = diag * 0.7f;
        float backwardCull = diag * 0.8f;

        // Keep the outermost lanes near the panel edges without pushing every
        // streak off-screen when the boat is near a border.
        float lateralHalf = Mathf.Min(Mathf.Max(w, h) * 0.42f, diag * 0.38f);

        // Faster movement at higher speed, but still visible near the threshold.
        float cycleSpeed = Mathf.Lerp(CycleSpeedMin, CycleSpeedMax, above);
        float animTime   = Time.time * cycleSpeed;

        var painter = ctx.painter2D;
        painter.lineWidth = Mathf.Lerp(1.25f, 2.25f, above);

        for (int i = 0; i < LineCount; i++)
        {
            // Deterministic pseudo-random values per lane. Each streak keeps a
            // fixed lateral lane and its own timing/length variation.
            float laneSeed   = Hash01(i * 0.731f + 1.3f);
            float phaseSeed  = Hash01(i * 1.173f + 7.1f);
            float lengthSeed = Hash01(i * 2.417f + 3.9f);
            float alphaSeed  = Hash01(i * 3.019f + 5.7f);

            float lateral = Mathf.Lerp(-lateralHalf, lateralHalf, laneSeed);

            // 0 = spawn ahead, 1 = fully behind the boat, then wrap.
            float travelT = Mathf.Repeat(animTime + phaseSeed, 1f);
            float fwdPos  = Mathf.Lerp(forwardSpawn, -backwardCull, travelT);

            // Longer, brighter streaks as speed climbs.
            float halfLen = Mathf.Lerp(8f, 34f, above) * Mathf.Lerp(0.65f, 1.2f, lengthSeed);
            Vector2 mid   = origin + fwd * fwdPos + perp * lateral;
            Vector2 lead  = mid + fwd * halfLen;
            Vector2 tail  = mid - fwd * halfLen;

            // Fade as the streak nears the back of its travel so wrapping is subtle.
            float wrapFade = Mathf.SmoothStep(1f, 0.2f, travelT);
            float alpha    = MaxAlpha * above * Mathf.Lerp(0.55f, 1f, alphaSeed) * wrapFade;
            if (alpha <= 0.001f) continue;

            // Cull lines fully outside the panel plus a small safety margin.
            const float Margin = 80f;
            bool leadOff = lead.x < -Margin || lead.x > w + Margin || lead.y < -Margin || lead.y > h + Margin;
            bool tailOff = tail.x < -Margin || tail.x > w + Margin || tail.y < -Margin || tail.y > h + Margin;
            if (leadOff && tailOff) continue;

            painter.strokeColor = new Color(1f, 1f, 1f, alpha);
            painter.BeginPath();
            painter.MoveTo(lead);
            painter.LineTo(tail);
            painter.Stroke();
        }
    }

    static float Hash01(float n)
    {
        // Cheap deterministic hash used to give each lane stable variation.
        return Mathf.Repeat(Mathf.Sin(n * 123.4567f) * 45678.9f, 1f);
    }
}
