using UnityEngine;
using UnityEngine.UIElements;

// Drives the persistent HUD hunger bar beneath the wind compass.
//
// Hunger truth comes from HungerController. This script only:
//   - resolves the UI Toolkit elements
//   - animates the displayed fraction toward the target fraction
//   - swaps the active fill sprite by threshold
//   - shrinks the fill from the top downward while keeping the bottom anchored
public class HungerBarUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] UIDocument uiDocument;
    [SerializeField] HungerController hungerController;

    [Header("Element Names")]
    [SerializeField] string fillElementName = "hunger-bar-fill";
    [SerializeField] string outlineElementName = "hunger-bar-outline";

    [Header("Sprites")]
    [SerializeField] Sprite outlineSprite;
    [SerializeField] Sprite fullColorSprite;
    [SerializeField] Sprite halfColorSprite;
    [SerializeField] Sprite almostEmptyColorSprite;

    [Header("Animation")]
    [SerializeField] float fillCatchUpSpeed = 1.5f;

    [Header("Thresholds")]
    [SerializeField] [Range(0f, 1f)] float halfThreshold = 0.6f;
    [SerializeField] [Range(0f, 1f)] float almostEmptyThreshold = 0.3f;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] float debugTargetHungerFraction;
    [SerializeField] float debugDisplayedHungerFraction;
    [SerializeField] string debugActiveFillName;

    public string DebugActiveFillName => debugActiveFillName;

    VisualElement fillElement;
    VisualElement outlineElement;
    bool isSubscribed;
    bool warnedMissingIcon;
    bool warnedMissingController;
    bool warnedMissingSprite;
    float targetHungerFraction;
    float displayedHungerFraction;
    Sprite activeFillSprite;
    bool hasInitializedTarget;

    void OnEnable()
    {
        TryInitialize();
        Subscribe();
        InitializeFromCurrentHunger();
        ApplyVisuals();
    }

    void Start()
    {
        TryInitialize();
        Subscribe();
        InitializeFromCurrentHunger();
        ApplyVisuals();
    }

    void Update()
    {
        TryInitialize();
        Subscribe();
        if (!hasInitializedTarget && hungerController != null)
            InitializeFromCurrentHunger();
        AnimateDisplayedHunger();
        ApplyVisuals();
        UpdateDebugMirrors();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void OnValidate()
    {
        fillCatchUpSpeed = Mathf.Max(0.01f, fillCatchUpSpeed);
        halfThreshold = Mathf.Clamp01(halfThreshold);
        almostEmptyThreshold = Mathf.Clamp01(almostEmptyThreshold);
        if (almostEmptyThreshold > halfThreshold)
            almostEmptyThreshold = halfThreshold;
    }

    void TryInitialize()
    {
        if (uiDocument == null)
            uiDocument = FindAnyObjectByType<UIDocument>();

        if (hungerController == null)
            hungerController = FindAnyObjectByType<HungerController>();

        if (uiDocument != null)
        {
            if (fillElement == null)
                fillElement = uiDocument.rootVisualElement.Q(fillElementName);

            if (outlineElement == null)
                outlineElement = uiDocument.rootVisualElement.Q(outlineElementName);
        }

        if ((fillElement == null || outlineElement == null) && !warnedMissingIcon)
        {
            Debug.LogWarning("HungerBarUIController could not find one or more hunger bar UI elements.", this);
            warnedMissingIcon = true;
        }

        if (hungerController == null && !warnedMissingController)
        {
            Debug.LogWarning("HungerBarUIController could not find a HungerController.", this);
            warnedMissingController = true;
        }
    }

    void Subscribe()
    {
        if (hungerController == null || isSubscribed)
            return;

        hungerController.OnHungerChanged += HandleHungerChanged;
        isSubscribed = true;
    }

    void Unsubscribe()
    {
        if (hungerController != null && isSubscribed)
            hungerController.OnHungerChanged -= HandleHungerChanged;

        isSubscribed = false;
    }

    void HandleHungerChanged(float previousHunger, float currentHunger)
    {
        if (hungerController == null)
            return;

        targetHungerFraction = hungerController.HungerFraction;
    }

    void InitializeFromCurrentHunger()
    {
        if (hungerController == null)
            return;

        targetHungerFraction = hungerController.HungerFraction;
        displayedHungerFraction = targetHungerFraction;
        hasInitializedTarget = true;
    }

    void AnimateDisplayedHunger()
    {
        displayedHungerFraction = Mathf.MoveTowards(
            displayedHungerFraction,
            targetHungerFraction,
            fillCatchUpSpeed * Time.deltaTime);
    }

    void ApplyVisuals()
    {
        if (fillElement == null || outlineElement == null)
            return;

        if (outlineSprite == null)
        {
            WarnMissingSpritesOnce();
            return;
        }

        outlineElement.style.backgroundImage = new StyleBackground(outlineSprite);
        fillElement.style.backgroundImage = new StyleBackground(GetActiveFillSprite());
        fillElement.style.height = Length.Percent(Mathf.Clamp01(displayedHungerFraction) * 100f);
    }

    Sprite GetActiveFillSprite()
    {
        Sprite sprite;
        if (displayedHungerFraction > halfThreshold)
        {
            sprite = fullColorSprite;
            debugActiveFillName = "Full";
        }
        else if (displayedHungerFraction > almostEmptyThreshold)
        {
            sprite = halfColorSprite;
            debugActiveFillName = "Half";
        }
        else
        {
            sprite = almostEmptyColorSprite;
            debugActiveFillName = "AlmostEmpty";
        }

        activeFillSprite = sprite;
        if (activeFillSprite == null)
            WarnMissingSpritesOnce();

        return activeFillSprite;
    }

    void WarnMissingSpritesOnce()
    {
        if (warnedMissingSprite)
            return;

        Debug.LogWarning("HungerBarUIController is missing one or more hunger bar sprites.", this);
        warnedMissingSprite = true;
    }

    void UpdateDebugMirrors()
    {
        debugTargetHungerFraction = targetHungerFraction;
        debugDisplayedHungerFraction = displayedHungerFraction;
    }
}
