using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class MapColorResolver
{
    static readonly Dictionary<int, Color32> SpriteColorCache = new Dictionary<int, Color32>();

    public static bool TryResolveIslandTileColorAtCell(
        Tilemap islandTilemap,
        Vector3Int cell,
        TileBase lowElevationTile,
        TileBase midElevationTile,
        TileBase highElevationTile,
        Color32 lowColor,
        Color32 midColor,
        Color32 highColor,
        Color32 fallbackLandColor,
        out Color32 color)
    {
        color = fallbackLandColor;
        if (islandTilemap == null)
            return false;

        TileBase tile = islandTilemap.GetTile(cell);
        if (tile == null)
            return false;

        color = ResolveIslandTileColor(
            islandTilemap,
            cell,
            tile,
            lowElevationTile,
            midElevationTile,
            highElevationTile,
            lowColor,
            midColor,
            highColor,
            fallbackLandColor);
        return true;
    }

    public static bool TryResolveDominantIslandColor(
        Tilemap islandTilemap,
        RectInt cellRect,
        TileBase lowElevationTile,
        TileBase midElevationTile,
        TileBase highElevationTile,
        Color32 lowColor,
        Color32 midColor,
        Color32 highColor,
        Color32 fallbackLandColor,
        out Color32 dominantColor)
    {
        dominantColor = fallbackLandColor;
        if (islandTilemap == null || cellRect.width <= 0 || cellRect.height <= 0)
            return false;

        Dictionary<uint, int> colorCounts = new Dictionary<uint, int>();
        Dictionary<uint, Color32> representativeColors = new Dictionary<uint, Color32>();
        bool foundLand = false;

        for (int y = cellRect.yMin; y < cellRect.yMax; y++)
        {
            for (int x = cellRect.xMin; x < cellRect.xMax; x++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                TileBase tile = islandTilemap.GetTile(cell);
                if (tile == null)
                    continue;

                foundLand = true;
                Color32 color = ResolveIslandTileColor(
                    islandTilemap,
                    cell,
                    tile,
                    lowElevationTile,
                    midElevationTile,
                    highElevationTile,
                    lowColor,
                    midColor,
                    highColor,
                    fallbackLandColor);

                uint key = PackColor(color);
                representativeColors[key] = color;
                colorCounts.TryGetValue(key, out int existingCount);
                colorCounts[key] = existingCount + 1;
            }
        }

        if (!foundLand)
            return false;

        uint bestKey = 0u;
        int bestCount = -1;
        foreach (KeyValuePair<uint, int> pair in colorCounts)
        {
            if (pair.Value <= bestCount)
                continue;

            bestKey = pair.Key;
            bestCount = pair.Value;
        }

        dominantColor = representativeColors[bestKey];
        return true;
    }

    static Color32 ResolveIslandTileColor(
        Tilemap tilemap,
        Vector3Int cell,
        TileBase tile,
        TileBase lowElevationTile,
        TileBase midElevationTile,
        TileBase highElevationTile,
        Color32 lowColor,
        Color32 midColor,
        Color32 highColor,
        Color32 fallbackLandColor)
    {
        if (tile == lowElevationTile)
            return lowColor;
        if (tile == midElevationTile)
            return midColor;
        if (tile == highElevationTile)
            return highColor;

        Sprite sprite = tilemap.GetSprite(cell);
        if (sprite != null && TryResolveCachedSpriteColor(sprite, out Color32 spriteColor))
            return spriteColor;

        Color tint = tilemap.GetColor(cell);
        if (tint.a > 0.01f && (tint.r < 0.99f || tint.g < 0.99f || tint.b < 0.99f))
            return (Color32)tint;

        return fallbackLandColor;
    }

    static bool TryResolveCachedSpriteColor(Sprite sprite, out Color32 color)
    {
        color = default;
        if (sprite == null)
            return false;

        int spriteId = RuntimeHelpers.GetHashCode(sprite);
        if (SpriteColorCache.TryGetValue(spriteId, out color))
            return true;

        if (!TryResolveReadableSpriteColor(sprite, out color))
            return false;

        SpriteColorCache[spriteId] = color;
        return true;
    }

    static bool TryResolveReadableSpriteColor(Sprite sprite, out Color32 color)
    {
        color = default;
        Texture2D texture = sprite.texture;
        if (texture == null || !texture.isReadable)
            return false;

        Rect textureRect = sprite.textureRect;
        int rectX = Mathf.RoundToInt(textureRect.x);
        int rectY = Mathf.RoundToInt(textureRect.y);
        int rectWidth = Mathf.RoundToInt(textureRect.width);
        int rectHeight = Mathf.RoundToInt(textureRect.height);
        if (rectWidth <= 0 || rectHeight <= 0)
            return false;

        Color32[] pixels = texture.GetPixels32();
        if (pixels == null || pixels.Length == 0)
            return false;

        Dictionary<int, int> bucketCounts = new Dictionary<int, int>();
        Dictionary<int, Vector4> bucketSums = new Dictionary<int, Vector4>();
        int textureWidth = texture.width;

        for (int y = rectY; y < rectY + rectHeight; y++)
        {
            int rowStart = y * textureWidth;
            for (int x = rectX; x < rectX + rectWidth; x++)
            {
                Color32 pixel = pixels[rowStart + x];
                if (pixel.a < 32)
                    continue;

                int bucket = QuantizeRgb(pixel.r) | (QuantizeRgb(pixel.g) << 4) | (QuantizeRgb(pixel.b) << 8);
                bucketCounts.TryGetValue(bucket, out int count);
                bucketCounts[bucket] = count + 1;

                bucketSums.TryGetValue(bucket, out Vector4 sum);
                sum += new Vector4(pixel.r, pixel.g, pixel.b, pixel.a);
                bucketSums[bucket] = sum;
            }
        }

        int bestBucket = -1;
        int bestCount = -1;
        foreach (KeyValuePair<int, int> pair in bucketCounts)
        {
            if (pair.Value <= bestCount)
                continue;

            bestBucket = pair.Key;
            bestCount = pair.Value;
        }

        if (bestBucket < 0 || bestCount <= 0)
            return false;

        Vector4 bestSum = bucketSums[bestBucket] / bestCount;
        color = new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(bestSum.x), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(bestSum.y), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(bestSum.z), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(bestSum.w), 0, 255));
        return true;
    }

    static int QuantizeRgb(byte value)
    {
        return Mathf.Clamp(value / 16, 0, 15);
    }

    static uint PackColor(Color32 color)
    {
        return (uint)(color.r | (color.g << 8) | (color.b << 16) | (color.a << 24));
    }
}
