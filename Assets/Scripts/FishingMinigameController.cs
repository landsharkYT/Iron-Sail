using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class FishingMinigameController : MonoBehaviour
{
    public enum FishingEndReason
    {
        Success,
        Failed,
        Cancelled
    }

    public static bool IsFishingOpen { get; private set; }
    public static FishingMinigameController ActiveInstance { get; private set; }

    public event Action<FishingEndReason> OnFishingAttemptEnded;

    [Header("References")]
    [SerializeField] UIDocument uiDocument;
    [SerializeField] DayNightController dayNightController;

    [Header("Sprites")]
    [SerializeField] Sprite fishingBarBackgroundSprite;
    [SerializeField] Sprite fishingBarSuccessSprite;
    [SerializeField] Sprite fishingBarCursorSprite;

    [Header("Element Names")]
    [SerializeField] string overlayElementName = "fishing-minigame-overlay";
    [SerializeField] string instanceRootElementName = "fishing-minigame-instance";
    [SerializeField] string panelElementName = "fishing-minigame-panel";
    [SerializeField] string trackElementName = "fishing-minigame-bar-track";
    [SerializeField] string backgroundElementName = "fishing-minigame-bar-background";
    [SerializeField] string successElementName = "fishing-minigame-bar-success";
    [SerializeField] string cursorElementName = "fishing-minigame-bar-cursor";
    [SerializeField] string progressLabelName = "fishing-minigame-progress-label";
    [SerializeField] string strikesLabelName = "fishing-minigame-strikes-label";
    [SerializeField] string instructionLabelName = "fishing-minigame-instruction-label";

    [Header("Attempt Rules")]
    [SerializeField][Min(1)] int requiredSuccesses = 6;
    [SerializeField][Min(1)] int maxStrikes = 3;
    [SerializeField][Range(0.05f, 1f)] float initialZoneWidthNormalized = 0.35f;
    [SerializeField][Range(0.1f, 1f)] float zoneShrinkMultiplier = 0.75f;
    [SerializeField][Range(0.02f, 0.8f)] float minimumZoneWidthNormalized = 0.08f;

    [Header("Cursor Motion")]
    [SerializeField][Min(0.05f)] float cursorSpeedNormalized = 0.65f;
    [SerializeField][Min(0f)] float cursorSpeedIncreasePerSuccess = 0.08f;

    [Header("Feedback Timing")]
    [SerializeField][Min(0f)] float successPauseSeconds = 0.2f;
    [SerializeField][Min(0f)] float missPauseSeconds = 0.25f;

#pragma warning disable CS0414
    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] bool debugUiReady;
    [SerializeField] bool debugAttemptActive;
    [SerializeField] int debugSuccesses;
    [SerializeField] int debugRemainingStrikes;
    [SerializeField] float debugCursorNormalized;
    [SerializeField] float debugZoneStartNormalized;
    [SerializeField] float debugZoneWidthNormalized;
    [SerializeField] float debugPauseRemaining;
#pragma warning restore CS0414

    enum PauseOutcome
    {
        None,
        AdvanceRound
    }

    VisualElement overlayElement;
    VisualElement instanceRootElement;
    VisualElement panelElement;
    VisualElement trackElement;
    VisualElement backgroundElement;
    VisualElement successElement;
    VisualElement cursorElement;
    Label progressLabel;
    Label strikesLabel;
    Label instructionLabel;

    bool uiReady;
    bool warnedMissingUi;
    bool warnedMissingSprites;
    float cursorNormalized;
    float cursorDirection = 1f;
    float zoneStartNormalized;
    float zoneWidthNormalized;
    float pauseRemaining;
    float currentCursorSpeedNormalized;
    int currentSuccesses;
    int remainingStrikes;
    PauseOutcome pendingPauseOutcome;
    CursorLockMode previousCursorLockMode;
    bool previousCursorVisible;
    float previousTimeScale = 1f;

    void OnEnable()
    {
        ActiveInstance = this;
        TryInitialize();
        SetFishingOpen(false, false);
    }

    void Start()
    {
        TryInitialize();
        SetFishingOpen(false, false);
    }

    void Update()
    {
        TryInitialize();
        HandleToggleInput();

        if (!IsFishingOpen)
        {
            UpdateDebugMirrors();
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        UpdatePause(deltaTime);
        if (pauseRemaining <= 0f)
            UpdateCursor(deltaTime);

        UpdateVisuals();
        UpdateDebugMirrors();
    }

    void OnDisable()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;

        if (IsFishingOpen)
            RestoreModalState();

        IsFishingOpen = false;
        debugAttemptActive = false;
        debugUiReady = false;
        uiReady = false;
    }

    public bool StartFishingAttempt()
    {
        TryInitialize();
        if (!uiReady || IsFishingOpen)
            return false;

        if (InventoryUIController.IsInventoryOpen || WorldMapUIController.IsMapOpen || ShopController.IsShopOpen)
            return false;

        currentSuccesses = 0;
        remainingStrikes = maxStrikes;
        zoneWidthNormalized = Mathf.Clamp(initialZoneWidthNormalized, minimumZoneWidthNormalized, 1f);
        currentCursorSpeedNormalized = cursorSpeedNormalized;
        cursorNormalized = UnityEngine.Random.value;
        cursorDirection = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        pauseRemaining = 0f;
        pendingPauseOutcome = PauseOutcome.None;
        RandomizeZonePosition();

        SetFishingOpen(true, true);
        UpdateVisuals();
        UpdateDebugMirrors();
        return true;
    }

    void TryInitialize()
    {
        if (uiReady)
            return;

        if (uiDocument == null)
            uiDocument = FindAnyObjectByType<UIDocument>();

        if (dayNightController == null)
            dayNightController = FindAnyObjectByType<DayNightController>();

        if (uiDocument == null)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        instanceRootElement = root.Q(instanceRootElementName);
        overlayElement = root.Q(overlayElementName);
        panelElement = root.Q(panelElementName);
        trackElement = root.Q(trackElementName);
        backgroundElement = root.Q(backgroundElementName);
        successElement = root.Q(successElementName);
        cursorElement = root.Q(cursorElementName);
        progressLabel = root.Q<Label>(progressLabelName);
        strikesLabel = root.Q<Label>(strikesLabelName);
        instructionLabel = root.Q<Label>(instructionLabelName);

        if (instanceRootElement == null || overlayElement == null || panelElement == null || trackElement == null || backgroundElement == null ||
            successElement == null || cursorElement == null || progressLabel == null || strikesLabel == null || instructionLabel == null)
        {
            if (!warnedMissingUi)
            {
                Debug.LogWarning("[FishingMinigameController] Missing one or more required fishing UI elements.", this);
                warnedMissingUi = true;
            }
            return;
        }

        ApplySprites();
        instanceRootElement.style.display = DisplayStyle.None;
        overlayElement.style.display = DisplayStyle.None;
        panelElement.SetEnabled(false);

        uiReady = true;
        debugUiReady = true;
    }

    void ApplySprites()
    {
        if (backgroundElement == null || successElement == null || cursorElement == null)
            return;

        if (fishingBarBackgroundSprite == null || fishingBarSuccessSprite == null || fishingBarCursorSprite == null)
        {
            if (!warnedMissingSprites)
            {
                Debug.LogWarning("[FishingMinigameController] Assign background, success, and cursor sprites.", this);
                warnedMissingSprites = true;
            }
            return;
        }

        backgroundElement.style.backgroundImage = new StyleBackground(fishingBarBackgroundSprite);
        successElement.style.backgroundImage = new StyleBackground(fishingBarSuccessSprite);
        cursorElement.style.backgroundImage = new StyleBackground(fishingBarCursorSprite);
    }

    void HandleToggleInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (!IsFishingOpen)
            return;

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            EndAttempt(FishingEndReason.Cancelled);
            return;
        }

        if (pauseRemaining > 0f)
            return;

        if (keyboard.cKey.wasPressedThisFrame)
            ResolvePlayerAttempt();
    }

    void ResolvePlayerAttempt()
    {
        if (IsCursorInsideZone())
            HandleSuccessfulHit();
        else
            HandleMiss();
    }

    bool IsCursorInsideZone()
    {
        float zoneEnd = zoneStartNormalized + zoneWidthNormalized;
        return cursorNormalized >= zoneStartNormalized && cursorNormalized <= zoneEnd;
    }

    void HandleSuccessfulHit()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        currentSuccesses++;
        if (currentSuccesses >= requiredSuccesses)
        {
            EndAttempt(FishingEndReason.Success);
            return;
        }

        pauseRemaining = successPauseSeconds;
        pendingPauseOutcome = PauseOutcome.AdvanceRound;
        instructionLabel.text = "Good catch timing. Get ready for the next bite.";
        UpdateVisuals();
    }

    void HandleMiss()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        remainingStrikes--;
        instructionLabel.text = "Missed the bite window.";

        if (remainingStrikes <= 0)
        {
            EndAttempt(FishingEndReason.Failed);
            return;
        }

        pauseRemaining = missPauseSeconds;
        pendingPauseOutcome = PauseOutcome.None;
        UpdateVisuals();
    }

    void UpdatePause(float deltaTime)
    {
        if (pauseRemaining <= 0f)
            return;

        pauseRemaining = Mathf.Max(0f, pauseRemaining - deltaTime);
        if (pauseRemaining > 0f)
            return;

        if (pendingPauseOutcome == PauseOutcome.AdvanceRound)
            AdvanceRound();

        pendingPauseOutcome = PauseOutcome.None;
    }

    void AdvanceRound()
    {
        zoneWidthNormalized = Mathf.Clamp(zoneWidthNormalized * zoneShrinkMultiplier, minimumZoneWidthNormalized, 1f);
        currentCursorSpeedNormalized += cursorSpeedIncreasePerSuccess;
        RandomizeZonePosition();
        instructionLabel.text = "Press C when the cursor is inside the green zone.";
        UpdateVisuals();
    }

    void RandomizeZonePosition()
    {
        float maxStart = Mathf.Max(0f, 1f - zoneWidthNormalized);
        zoneStartNormalized = maxStart <= 0f ? 0f : UnityEngine.Random.Range(0f, maxStart);
    }

    void UpdateCursor(float deltaTime)
    {
        cursorNormalized += cursorDirection * currentCursorSpeedNormalized * deltaTime;
        if (cursorNormalized > 1f)
        {
            cursorNormalized = 1f;
            cursorDirection = -1f;
        }
        else if (cursorNormalized < 0f)
        {
            cursorNormalized = 0f;
            cursorDirection = 1f;
        }
    }

    void UpdateVisuals()
    {
        if (!uiReady)
            return;

        progressLabel.text = $"Catch Progress: {currentSuccesses}/{requiredSuccesses}";
        strikesLabel.text = $"Strikes Left: {remainingStrikes}";

        float trackWidth = trackElement.resolvedStyle.width;
        float cursorWidth = cursorElement.resolvedStyle.width;
        if (trackWidth <= 0f)
            return;

        float zoneLeftPx = zoneStartNormalized * trackWidth;
        float zoneWidthPx = zoneWidthNormalized * trackWidth;
        successElement.style.left = zoneLeftPx;
        successElement.style.width = zoneWidthPx;

        float cursorLeftPx = cursorNormalized * trackWidth - cursorWidth * 0.5f;
        cursorLeftPx = Mathf.Clamp(cursorLeftPx, 0f, Mathf.Max(0f, trackWidth - cursorWidth));
        cursorElement.style.left = cursorLeftPx;
    }

    void EndAttempt(FishingEndReason reason)
    {
        switch (reason)
        {
            case FishingEndReason.Success:
                Debug.Log("[FishingMinigameController] Fishing attempt succeeded.", this);
                break;
            case FishingEndReason.Failed:
                Debug.Log("[FishingMinigameController] Fishing attempt failed.", this);
                break;
            case FishingEndReason.Cancelled:
                Debug.Log("[FishingMinigameController] Fishing attempt cancelled.", this);
                break;
        }

        OnFishingAttemptEnded?.Invoke(reason);
        SetFishingOpen(false, true);
    }

    void SetFishingOpen(bool shouldOpen, bool manageModalState)
    {
        TryInitialize();

        if (overlayElement != null)
            overlayElement.style.display = shouldOpen ? DisplayStyle.Flex : DisplayStyle.None;

        if (instanceRootElement != null)
            instanceRootElement.style.display = shouldOpen ? DisplayStyle.Flex : DisplayStyle.None;

        if (panelElement != null)
            panelElement.SetEnabled(shouldOpen);

        if (shouldOpen == IsFishingOpen)
            return;

        IsFishingOpen = shouldOpen;
        debugAttemptActive = shouldOpen;

        if (!manageModalState)
            return;

        if (shouldOpen)
            ApplyModalState();
        else
            RestoreModalState();
    }

    void ApplyModalState()
    {
        previousCursorLockMode = UnityEngine.Cursor.lockState;
        previousCursorVisible = UnityEngine.Cursor.visible;
        previousTimeScale = Time.timeScale;

        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
        Time.timeScale = 0f;

        if (dayNightController != null)
            dayNightController.SetPaused(true);
    }

    void RestoreModalState()
    {
        UnityEngine.Cursor.lockState = previousCursorLockMode;
        UnityEngine.Cursor.visible = previousCursorVisible;
        Time.timeScale = previousTimeScale;

        if (dayNightController != null)
            dayNightController.SetPaused(false);
    }

    void UpdateDebugMirrors()
    {
        debugAttemptActive = IsFishingOpen;
        debugSuccesses = currentSuccesses;
        debugRemainingStrikes = remainingStrikes;
        debugCursorNormalized = cursorNormalized;
        debugZoneStartNormalized = zoneStartNormalized;
        debugZoneWidthNormalized = zoneWidthNormalized;
        debugPauseRemaining = pauseRemaining;
    }
}
