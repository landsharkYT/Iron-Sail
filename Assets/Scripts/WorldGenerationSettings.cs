using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "WorldGenerationSettings", menuName = "The Iron Sail/World Generation Settings")]
public class WorldGenerationSettings : ScriptableObject
{
    [Header("Finite World")]
    [SerializeField][Min(256f)] float playableRadiusTiles = 24000f;
    [SerializeField][Min(8f)] float outerWallThicknessTiles = 96f;
    [SerializeField] TileBase borderTile;

    [Header("Treasure Band")]
    [SerializeField][Range(0.5f, 0.99f)] float treasureBandMinNormalized = 0.85f;
    [SerializeField][Range(0.5f, 0.995f)] float treasureBandMaxNormalized = 0.94f;
    [SerializeField][Min(1f)] float treasureIsolationMultiplier = 3.25f;

    [Header("Radial Sparsity")]
    [SerializeField][Min(0.5f)] float spacingMultiplierInner = 1.35f;
    [SerializeField][Min(0.5f)] float spacingMultiplierOuter = 3.1f;
    [SerializeField][Range(0f, 1f)] float spacingRampStartNormalized = 0.55f;
    [SerializeField][Range(0f, 1f)] float spacingRampEndNormalized = 0.92f;
    [SerializeField][Range(0.05f, 1f)] float occupancyMultiplierOuter = 0.6f;
    [SerializeField][Range(0f, 1f)] float occupancyRampStartNormalized = 0.6f;
    [SerializeField][Range(0f, 1f)] float occupancyRampEndNormalized = 0.94f;

    public float PlayableRadiusTiles => playableRadiusTiles;
    public float OuterWallThicknessTiles => outerWallThicknessTiles;
    public float WallInnerRadiusTiles => playableRadiusTiles;
    public float WallOuterRadiusTiles => playableRadiusTiles + outerWallThicknessTiles;
    public TileBase BorderTile => borderTile;
    public float TreasureBandMinNormalized => treasureBandMinNormalized;
    public float TreasureBandMaxNormalized => treasureBandMaxNormalized;
    public float TreasureIsolationMultiplier => treasureIsolationMultiplier;

    public bool HasValidBorderTile => borderTile != null;

    public float GetTreasureBandMinRadiusTiles()
    {
        return playableRadiusTiles * treasureBandMinNormalized;
    }

    public float GetTreasureBandMaxRadiusTiles()
    {
        return playableRadiusTiles * treasureBandMaxNormalized;
    }

    public float NormalizeRadius(float radialDistanceTiles)
    {
        if (playableRadiusTiles <= 0f)
            return 0f;

        return Mathf.Clamp01(radialDistanceTiles / playableRadiusTiles);
    }

    public float EvaluateSpacingMultiplier(float normalizedRadius)
    {
        float t = EvaluateRamp(normalizedRadius, spacingRampStartNormalized, spacingRampEndNormalized);
        return Mathf.Lerp(spacingMultiplierInner, spacingMultiplierOuter, EaseInOut(t));
    }

    public float EvaluateOccupancyMultiplier(float normalizedRadius)
    {
        float t = EvaluateRamp(normalizedRadius, occupancyRampStartNormalized, occupancyRampEndNormalized);
        return Mathf.Lerp(1f, occupancyMultiplierOuter, EaseInOut(t));
    }

    static float EvaluateRamp(float value, float start, float end)
    {
        if (end <= start)
            return value >= end ? 1f : 0f;

        return Mathf.InverseLerp(start, end, value);
    }

    static float EaseInOut(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    void OnValidate()
    {
        playableRadiusTiles = Mathf.Max(256f, playableRadiusTiles);
        outerWallThicknessTiles = Mathf.Max(8f, outerWallThicknessTiles);
        treasureBandMinNormalized = Mathf.Clamp(treasureBandMinNormalized, 0.5f, 0.99f);
        treasureBandMaxNormalized = Mathf.Clamp(treasureBandMaxNormalized, treasureBandMinNormalized + 0.01f, 0.995f);
        treasureIsolationMultiplier = Mathf.Max(1f, treasureIsolationMultiplier);
        spacingMultiplierInner = Mathf.Max(0.5f, spacingMultiplierInner);
        spacingMultiplierOuter = Mathf.Max(spacingMultiplierInner, spacingMultiplierOuter);
        spacingRampStartNormalized = Mathf.Clamp01(spacingRampStartNormalized);
        spacingRampEndNormalized = Mathf.Clamp(spacingRampEndNormalized, spacingRampStartNormalized + 0.01f, 1f);
        occupancyMultiplierOuter = Mathf.Clamp(occupancyMultiplierOuter, 0.05f, 1f);
        occupancyRampStartNormalized = Mathf.Clamp01(occupancyRampStartNormalized);
        occupancyRampEndNormalized = Mathf.Clamp(occupancyRampEndNormalized, occupancyRampStartNormalized + 0.01f, 1f);
    }
}
