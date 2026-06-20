using UnityEngine;
using UnityEngine.InputSystem;

// Drives the floating world-space boat health bar.
//
// The health truth comes from BoatHealthController. This script only:
//   - follows a dedicated anchor while keeping the bar upright on screen
//   - toggles renderer visibility with Tab
//   - animates the displayed fill fraction toward the latest target
//   - swaps between green / orange / red fill sprites by threshold
public class BoatHealthBarController : MonoBehaviour
{
    [Header("References")]

    [SerializeField] BoatHealthController boatHealthController;
    [SerializeField] Transform healthBarAnchor;
    [SerializeField] SpriteRenderer outlineRenderer;
    [SerializeField] SpriteRenderer greenFillRenderer;
    [SerializeField] SpriteRenderer orangeFillRenderer;
    [SerializeField] SpriteRenderer redFillRenderer;

    [Header("Follow")]

    // Extra local-space tuning offset applied relative to the anchor.
    [SerializeField] Vector3 localOffset = Vector3.zero;

    [Header("Animation")]

    // Displayed health fraction moves toward the true fraction at a constant speed.
    [SerializeField] float fillCatchUpSpeed = 1.5f;

    [Header("Thresholds")]

    [SerializeField] [Range(0f, 1f)] float orangeThreshold = 0.6f;
    [SerializeField] [Range(0f, 1f)] float redThreshold = 0.3f;

    [Header("Visibility")]

    [SerializeField] bool visibleByDefault = true;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] float debugTargetHealthFraction;
    [SerializeField] float debugDisplayedHealthFraction;
    [SerializeField] bool debugIsVisible = true;

    Vector3 greenBaselineScale;
    Vector3 orangeBaselineScale;
    Vector3 redBaselineScale;
    bool isSubscribed;
    bool warnedMissingReferences;
    float targetHealthFraction;
    float displayedHealthFraction;
    bool isVisible;

    void Awake()
    {
        CacheBaselineScales();
        isVisible = visibleByDefault;
    }

    void OnEnable()
    {
        Subscribe();
        InitializeFromCurrentHealth();
        ApplyVisibility();
        ApplyVisuals();
    }

    void Start()
    {
        Subscribe();
        InitializeFromCurrentHealth();
        ApplyVisibility();
        ApplyVisuals();
    }

    void Update()
    {
        HandleVisibilityToggleInput();
        AnimateDisplayedHealth();
        ApplyVisuals();
        UpdateDebugMirrors();
    }

    void LateUpdate()
    {
        FollowAnchor();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void OnValidate()
    {
        orangeThreshold = Mathf.Clamp01(orangeThreshold);
        redThreshold = Mathf.Clamp01(redThreshold);
        if (redThreshold > orangeThreshold)
            redThreshold = orangeThreshold;

        fillCatchUpSpeed = Mathf.Max(0.01f, fillCatchUpSpeed);
    }

    void Subscribe()
    {
        if (boatHealthController == null || isSubscribed)
            return;

        boatHealthController.OnHealthChanged += HandleHealthChanged;
        isSubscribed = true;
    }

    void Unsubscribe()
    {
        if (boatHealthController != null && isSubscribed)
            boatHealthController.OnHealthChanged -= HandleHealthChanged;

        isSubscribed = false;
    }

    void HandleHealthChanged(float previousHealth, float currentHealth)
    {
        if (boatHealthController == null)
            return;

        targetHealthFraction = boatHealthController.HealthFraction;
    }

    void InitializeFromCurrentHealth()
    {
        if (!HasRequiredReferences())
            return;

        targetHealthFraction = boatHealthController.HealthFraction;
        displayedHealthFraction = targetHealthFraction;
    }

    void HandleVisibilityToggleInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame)
            return;

        isVisible = !isVisible;
        ApplyVisibility();
    }

    void AnimateDisplayedHealth()
    {
        displayedHealthFraction = Mathf.MoveTowards(
            displayedHealthFraction,
            targetHealthFraction,
            fillCatchUpSpeed * Time.deltaTime);
    }

    void FollowAnchor()
    {
        if (healthBarAnchor == null)
        {
            WarnMissingReferencesOnce();
            return;
        }

        transform.position = healthBarAnchor.TransformPoint(localOffset);
        transform.rotation = Quaternion.identity;
    }

    void ApplyVisibility()
    {
        if (outlineRenderer != null)
            outlineRenderer.enabled = isVisible;

        ApplyFillRendererEnabledStates(false, false, false);
    }

    void ApplyVisuals()
    {
        if (!HasRequiredReferences())
            return;

        UpdateFillScale(greenFillRenderer, greenBaselineScale, displayedHealthFraction);
        UpdateFillScale(orangeFillRenderer, orangeBaselineScale, displayedHealthFraction);
        UpdateFillScale(redFillRenderer, redBaselineScale, displayedHealthFraction);

        if (!isVisible)
        {
            ApplyFillRendererEnabledStates(false, false, false);
            return;
        }

        if (displayedHealthFraction <= 0f)
        {
            ApplyFillRendererEnabledStates(false, false, false);
            return;
        }

        bool showGreen = displayedHealthFraction > orangeThreshold;
        bool showOrange = displayedHealthFraction > redThreshold && !showGreen;
        bool showRed = displayedHealthFraction > 0f && !showGreen && !showOrange;

        ApplyFillRendererEnabledStates(showGreen, showOrange, showRed);
    }

    void ApplyFillRendererEnabledStates(bool showGreen, bool showOrange, bool showRed)
    {
        if (greenFillRenderer != null)
            greenFillRenderer.enabled = showGreen;

        if (orangeFillRenderer != null)
            orangeFillRenderer.enabled = showOrange;

        if (redFillRenderer != null)
            redFillRenderer.enabled = showRed;
    }

    void UpdateFillScale(SpriteRenderer renderer, Vector3 baselineScale, float fraction)
    {
        if (renderer == null)
            return;

        Vector3 scale = baselineScale;
        scale.x = baselineScale.x * Mathf.Clamp01(fraction);
        renderer.transform.localScale = scale;
    }

    void CacheBaselineScales()
    {
        if (greenFillRenderer != null)
            greenBaselineScale = greenFillRenderer.transform.localScale;

        if (orangeFillRenderer != null)
            orangeBaselineScale = orangeFillRenderer.transform.localScale;

        if (redFillRenderer != null)
            redBaselineScale = redFillRenderer.transform.localScale;
    }

    bool HasRequiredReferences()
    {
        bool hasReferences =
            boatHealthController != null &&
            healthBarAnchor != null &&
            outlineRenderer != null &&
            greenFillRenderer != null &&
            orangeFillRenderer != null &&
            redFillRenderer != null;

        if (!hasReferences)
            WarnMissingReferencesOnce();

        return hasReferences;
    }

    void WarnMissingReferencesOnce()
    {
        if (warnedMissingReferences)
            return;

        Debug.LogWarning("BoatHealthBarController is missing one or more required references.", this);
        warnedMissingReferences = true;
    }

    void UpdateDebugMirrors()
    {
        debugTargetHealthFraction = targetHealthFraction;
        debugDisplayedHealthFraction = displayedHealthFraction;
        debugIsVisible = isVisible;
    }
}
