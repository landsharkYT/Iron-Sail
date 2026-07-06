using UnityEngine;
using UnityEngine.UIElements;

// Custom VisualElement that clips the green wind-indicator SPRITE to the
// good-wind sector, revealing the blackened circle sprite underneath in the
// zones where the boat gets no wind benefit.
//
// Rather than drawing a replacement arc, this element renders the sprite
// texture itself, but only within a filled pie-slice mesh that covers the
// good-wind zone centred at the TOP (12 o'clock / 270° in screen space).
//
// Arc angle convention (screen space Y-down):
//   0° = right (3 o'clock), 90° = down, 180° = left, 270° = up (12 o'clock).
//
// ArcDegrees defaults to 180, matching the current physics model where
// efficiency = Dot(boatForward, windDir) drops to zero at 90° off-axis.
//
// Set ArcSprite from code (WindUIController.Start) before the first repaint.
[UxmlElement]
public partial class WindArcElement : VisualElement
{
    float _arcDegrees = 180f;
    [UxmlAttribute]
    public float ArcDegrees
    {
        get => _arcDegrees;
        set { _arcDegrees = value; MarkDirtyRepaint(); }
    }

    Sprite _arcSprite;
    public Sprite ArcSprite
    {
        get => _arcSprite;
        set { _arcSprite = value; MarkDirtyRepaint(); }
    }

    public WindArcElement()
    {
        generateVisualContent += OnGenerateVisualContent;
    }

    void OnGenerateVisualContent(MeshGenerationContext ctx)
    {
        if (_arcSprite == null) return;

        float w = resolvedStyle.width;
        float h = resolvedStyle.height;
        if (w <= 0f || h <= 0f) return;

        Texture2D tex     = _arcSprite.texture;
        Rect      texRect = _arcSprite.textureRect;
        float     texW    = tex.width;
        float     texH    = tex.height;

        // Sprite UV bounds inside the atlas texture.
        float uMin = texRect.xMin / texW;
        float uMax = texRect.xMax / texW;
        float vMin = texRect.yMin / texH;
        float vMax = texRect.yMax / texH;

        var   center  = new Vector2(w * 0.5f, h * 0.5f);
        float radius  = Mathf.Min(w, h) * 0.5f;

        float halfArc       = _arcDegrees * 0.5f;
        float startAngleDeg = 270f - halfArc;
        float endAngleDeg   = 270f + halfArc;
        const int Segments  = 64;

        // 1 centre vertex + (Segments + 1) rim vertices.
        var md = ctx.Allocate(1 + Segments + 1, Segments * 3, tex);

        md.SetNextVertex(MakeVertex(center.x, center.y, w, h, uMin, uMax, vMin, vMax));

        for (int i = 0; i <= Segments; i++)
        {
            float t        = (float)i / Segments;
            float angleDeg = Mathf.Lerp(startAngleDeg, endAngleDeg, t);
            float angleRad = angleDeg * Mathf.Deg2Rad;
            float px       = center.x + Mathf.Cos(angleRad) * radius;
            float py       = center.y + Mathf.Sin(angleRad) * radius;
            md.SetNextVertex(MakeVertex(px, py, w, h, uMin, uMax, vMin, vMax));
        }

        for (int i = 0; i < Segments; i++)
        {
            md.SetNextIndex(0);
            md.SetNextIndex((ushort)(i + 1));
            md.SetNextIndex((ushort)(i + 2));
        }
    }

    static Vertex MakeVertex(float px, float py, float w, float h,
                             float uMin, float uMax, float vMin, float vMax)
    {
        // Element space: (0,0) = top-left, Y increases downward.
        // Texture space: Y increases upward, so flip vertical.
        return new Vertex
        {
            position = new Vector3(px, py, Vertex.nearZ),
            uv       = new Vector2(Mathf.Lerp(uMin, uMax,  px / w),
                                   Mathf.Lerp(vMin, vMax,  1f - py / h)),
            tint     = Color.white
        };
    }
}
