using UnityEngine;
using UnityEngine.Tilemaps;

// Applies day/night tinting to a single Tilemap by multiplying against its
// cached baseline color.
public class DayNightTilemapTint : MonoBehaviour, IDayNightTintTarget
{
    [Header("References")]
    [SerializeField] Tilemap tilemap;

    [Header("Behavior")]
    [SerializeField] bool tintEnabled = true;
    [SerializeField] [Range(0f, 1f)] float colorInfluence = 1f;
    [SerializeField] [Range(0f, 1f)] float brightnessInfluence = 1f;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] bool debugHasActiveTint;
    [SerializeField] Color debugBaselineColor = Color.white;

    Color baselineColor = Color.white;
    Color currentTintColor = Color.white;
    float currentTintBrightness = 1f;
    bool hasCachedBaselineColor;
    bool hasActiveTint;
    bool hasWarnedMissingTilemap;

    void Reset()
    {
        tilemap = GetComponent<Tilemap>();
    }

    void OnEnable()
    {
        EnsureSetup();

        if (DayNightLightingController.ActiveController != null)
            DayNightLightingController.ActiveController.RegisterTintTarget(this);
    }

    void OnDisable()
    {
        if (DayNightLightingController.ActiveController != null)
            DayNightLightingController.ActiveController.UnregisterTintTarget(this);

        RestoreBaselineColor();
    }

    void OnValidate()
    {
        colorInfluence = Mathf.Clamp01(colorInfluence);
        brightnessInfluence = Mathf.Clamp01(brightnessInfluence);

        if (!Application.isPlaying)
            return;

        EnsureSetup();

        if (!tintEnabled)
        {
            RestoreBaselineColor();
            return;
        }

        if (hasActiveTint)
            ApplyTint(currentTintColor, currentTintBrightness);
    }

    public void ApplyTint(Color colorMultiplier, float brightnessMultiplier)
    {
        EnsureSetup();
        if (tilemap == null)
            return;

        currentTintColor = OpaqueColor(colorMultiplier);
        currentTintBrightness = Mathf.Clamp01(brightnessMultiplier);

        if (!tintEnabled)
        {
            RestoreBaselineColor();
            return;
        }

        Color tintColor = Color.Lerp(Color.white, currentTintColor, colorInfluence);
        float brightness = Mathf.Lerp(1f, currentTintBrightness, brightnessInfluence);

        Color liveColor = tilemap.color;
        Color finalColor = baselineColor;
        finalColor.r = Mathf.Clamp01(baselineColor.r * tintColor.r * brightness);
        finalColor.g = Mathf.Clamp01(baselineColor.g * tintColor.g * brightness);
        finalColor.b = Mathf.Clamp01(baselineColor.b * tintColor.b * brightness);
        finalColor.a = liveColor.a;

        tilemap.color = finalColor;
        hasActiveTint = true;
        UpdateDebugMirrors();
    }

    public void RefreshBaselineColor()
    {
        EnsureSetup();
        if (tilemap == null)
            return;

        baselineColor = OpaqueColor(tilemap.color);
        hasCachedBaselineColor = true;

        if (!tintEnabled)
        {
            RestoreBaselineColor();
            return;
        }

        if (hasActiveTint)
            ApplyTint(currentTintColor, currentTintBrightness);
        else
            UpdateDebugMirrors();
    }

    void EnsureSetup()
    {
        if (tilemap == null)
            tilemap = GetComponent<Tilemap>();

        if (tilemap == null)
        {
            if (!hasWarnedMissingTilemap)
            {
                hasWarnedMissingTilemap = true;
                Debug.LogWarning("[DayNightTilemapTint] Missing Tilemap reference.", this);
            }

            return;
        }

        if (!hasCachedBaselineColor)
        {
            baselineColor = OpaqueColor(tilemap.color);
            hasCachedBaselineColor = true;
        }

        debugBaselineColor = baselineColor;
    }

    void RestoreBaselineColor()
    {
        if (tilemap == null)
            return;

        Color restored = baselineColor;
        restored.a = tilemap.color.a;
        tilemap.color = restored;
        hasActiveTint = false;
        UpdateDebugMirrors();
    }

    Color OpaqueColor(Color color)
    {
        color.a = 1f;
        return color;
    }

    void UpdateDebugMirrors()
    {
        if (!Application.isPlaying)
            return;

        debugHasActiveTint = hasActiveTint && tintEnabled;
        debugBaselineColor = baselineColor;
    }
}
