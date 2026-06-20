using System.Collections.Generic;
using UnityEngine;

// Owns day/night tint application for one world prefab or scene visual root.
//
// This component stays phase-agnostic on purpose. It only knows:
// - which SpriteRenderers belong to it
// - what their cached baseline colors are
// - how to apply a pushed tint/brightness pair while preserving live alpha
public class DayNightTintGroup : MonoBehaviour, IDayNightTintTarget
{
    static readonly HashSet<DayNightTintGroup> activeTintGroups = new HashSet<DayNightTintGroup>();

    public static IEnumerable<DayNightTintGroup> ActiveTintGroups => activeTintGroups;

    [Header("Collection")]
    [SerializeField] Transform collectionRoot;
    [SerializeField] Transform[] excludedRoots;

    [Header("Behavior")]
    [SerializeField] bool tintEnabled = true;
    [SerializeField] [Range(0f, 1f)] float colorInfluence = 1f;
    [SerializeField] [Range(0f, 1f)] float brightnessInfluence = 1f;
    [SerializeField] bool useNightReadabilityTuning;
    [SerializeField] [Range(0f, 1f)] float nightReadabilityBoost = 0.35f;
    [SerializeField] [Range(0f, 1f)] float nightBrightnessFloor = 0.62f;
    [SerializeField] Color nightLiftColor = new Color(0.78f, 0.83f, 0.94f, 1f);

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] int debugCollectedRendererCount;
    [SerializeField] bool debugHasActiveTint;

    SpriteRenderer[] collectedRenderers = new SpriteRenderer[0];
    Color[] cachedBaselineColors = new Color[0];
    Color currentTintColor = Color.white;
    float currentTintBrightness = 1f;
    bool hasActiveTint;
    bool hasWarnedMissingCollectionRoot;

    void Reset()
    {
        collectionRoot = transform;
    }

    void Awake()
    {
        if (collectionRoot == null)
            collectionRoot = transform;
    }

    void OnEnable()
    {
        EnsureSetup();

        activeTintGroups.Add(this);

        if (DayNightLightingController.ActiveController != null)
            DayNightLightingController.ActiveController.RegisterTintTarget(this);
    }

    void OnDisable()
    {
        if (DayNightLightingController.ActiveController != null)
            DayNightLightingController.ActiveController.UnregisterTintTarget(this);

        activeTintGroups.Remove(this);
        RestoreBaselineColors();
    }

    void OnValidate()
    {
        colorInfluence = Mathf.Clamp01(colorInfluence);
        brightnessInfluence = Mathf.Clamp01(brightnessInfluence);
        nightReadabilityBoost = Mathf.Clamp01(nightReadabilityBoost);
        nightBrightnessFloor = Mathf.Clamp01(nightBrightnessFloor);
        nightLiftColor.a = 1f;

        if (!Application.isPlaying)
            return;

        EnsureSetup();

        if (!tintEnabled)
        {
            RestoreBaselineColors();
            return;
        }

        if (hasActiveTint)
            ApplyTint(currentTintColor, currentTintBrightness);
    }

    public void ApplyTint(Color colorMultiplier, float brightnessMultiplier)
    {
        EnsureSetup();

        currentTintColor = OpaqueColor(colorMultiplier);
        currentTintBrightness = Mathf.Clamp01(brightnessMultiplier);

        if (!tintEnabled)
        {
            RestoreBaselineColors();
            return;
        }

        Color tintColor = Color.Lerp(Color.white, currentTintColor, colorInfluence);
        float brightness = Mathf.Lerp(1f, currentTintBrightness, brightnessInfluence);
        if (useNightReadabilityTuning)
        {
            float duskNightFactor = Mathf.Clamp01((1f - currentTintBrightness) / 0.35f);
            tintColor = Color.Lerp(tintColor, OpaqueColor(nightLiftColor), nightReadabilityBoost * duskNightFactor);
            brightness = Mathf.Max(brightness, Mathf.Lerp(0f, nightBrightnessFloor, duskNightFactor));
        }

        for (int i = 0; i < collectedRenderers.Length; i++)
        {
            SpriteRenderer renderer = collectedRenderers[i];
            if (renderer == null)
                continue;

            Color baseline = cachedBaselineColors[i];
            Color liveColor = renderer.color;
            Color finalColor = baseline;

            finalColor.r = Mathf.Clamp01(baseline.r * tintColor.r * brightness);
            finalColor.g = Mathf.Clamp01(baseline.g * tintColor.g * brightness);
            finalColor.b = Mathf.Clamp01(baseline.b * tintColor.b * brightness);
            finalColor.a = liveColor.a;

            renderer.color = finalColor;
        }

        hasActiveTint = true;
        UpdateDebugMirrors();
    }

    public void RebuildCollectedRenderers()
    {
        CollectRenderersAndBaselines();

        if (!tintEnabled)
        {
            RestoreBaselineColors();
            return;
        }

        if (hasActiveTint)
            ApplyTint(currentTintColor, currentTintBrightness);
        else
            UpdateDebugMirrors();
    }

    public void RefreshBaselineColors()
    {
        CacheBaselineColors();

        if (!tintEnabled)
        {
            RestoreBaselineColors();
            return;
        }

        if (hasActiveTint)
            ApplyTint(currentTintColor, currentTintBrightness);
        else
            UpdateDebugMirrors();
    }

    public void AssignCollectionRoot(Transform root, bool rebuildImmediately = true)
    {
        collectionRoot = root != null ? root : transform;
        hasWarnedMissingCollectionRoot = false;

        if (!rebuildImmediately)
            return;

        RebuildCollectedRenderers();
    }

    void EnsureSetup()
    {
        if (collectionRoot == null)
        {
            collectionRoot = transform;

            if (!hasWarnedMissingCollectionRoot)
            {
                hasWarnedMissingCollectionRoot = true;
                Debug.LogWarning("[DayNightTintGroup] Missing collection root. Falling back to the component transform.", this);
            }
        }

        if (collectedRenderers.Length == 0 || cachedBaselineColors.Length != collectedRenderers.Length)
            CollectRenderersAndBaselines();
    }

    void CollectRenderersAndBaselines()
    {
        List<SpriteRenderer> foundRenderers = new List<SpriteRenderer>();
        SpriteRenderer[] renderersInChildren = collectionRoot.GetComponentsInChildren<SpriteRenderer>(true);

        foreach (SpriteRenderer renderer in renderersInChildren)
        {
            if (renderer == null)
                continue;
            if (IsUnderExcludedRoot(renderer.transform))
                continue;

            foundRenderers.Add(renderer);
        }

        collectedRenderers = foundRenderers.ToArray();
        CacheBaselineColors();
        UpdateDebugMirrors();
    }

    void CacheBaselineColors()
    {
        cachedBaselineColors = new Color[collectedRenderers.Length];

        for (int i = 0; i < collectedRenderers.Length; i++)
        {
            SpriteRenderer renderer = collectedRenderers[i];
            if (renderer == null)
                continue;

            cachedBaselineColors[i] = OpaqueColor(renderer.color);
        }

        UpdateDebugMirrors();
    }

    bool IsUnderExcludedRoot(Transform candidate)
    {
        if (excludedRoots == null)
            return false;

        foreach (Transform excludedRoot in excludedRoots)
        {
            if (excludedRoot == null)
                continue;
            if (candidate == excludedRoot)
                return true;
            if (candidate.IsChildOf(excludedRoot))
                return true;
        }

        return false;
    }

    void RestoreBaselineColors()
    {
        for (int i = 0; i < collectedRenderers.Length; i++)
        {
            SpriteRenderer renderer = collectedRenderers[i];
            if (renderer == null)
                continue;

            Color baseline = cachedBaselineColors[i];
            Color liveColor = renderer.color;
            baseline.a = liveColor.a;
            renderer.color = baseline;
        }

        hasActiveTint = false;
        UpdateDebugMirrors();
    }

    Color OpaqueColor(Color color)
    {
        color.a = 1f;
        return color;
    }

    public void ConfigureNightReadability(bool enabled, float readabilityBoost, float brightnessFloor, Color liftColor)
    {
        useNightReadabilityTuning = enabled;
        nightReadabilityBoost = Mathf.Clamp01(readabilityBoost);
        nightBrightnessFloor = Mathf.Clamp01(brightnessFloor);
        nightLiftColor = OpaqueColor(liftColor);

        if (!Application.isPlaying)
            return;

        EnsureSetup();
        if (hasActiveTint && tintEnabled)
            ApplyTint(currentTintColor, currentTintBrightness);
    }

    void UpdateDebugMirrors()
    {
        if (!Application.isPlaying)
            return;

        debugCollectedRendererCount = collectedRenderers.Length;
        debugHasActiveTint = hasActiveTint && tintEnabled;
    }
}
