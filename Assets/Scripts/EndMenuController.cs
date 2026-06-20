using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using System.Collections.Generic;
//Kenneth
public class EndMenuController : MonoBehaviour
{
    enum EndOutcome
    {
        None = 0,
        Win = 1,
        Starved = 2,
        Sunk = 3
    }

    public static bool IsEndMenuOpen { get; private set; }
    public static EndMenuController ActiveInstance { get; private set; }

    [Header("References")]
    [SerializeField] UIDocument uiDocument;
    [SerializeField] DayNightController dayNightController;
    [SerializeField] string titleSceneName = "TitleScreen";

    [Header("Element Names")]
    [SerializeField] string overlayElementName = "end-menu-overlay";
    [SerializeField] string panelElementName = "end-menu-panel";
    [SerializeField] string titleElementName = "end-menu-title";
    [SerializeField] string mainContentElementName = "end-menu-main-content";
    [SerializeField] string loadContentElementName = "end-menu-load-content";
    [SerializeField] string bodyElementName = "end-menu-body";
    [SerializeField] string loadButtonElementName = "end-menu-load-button";
    [SerializeField] string restartButtonElementName = "end-menu-restart-button";
    [SerializeField] string titleScreenButtonElementName = "end-menu-title-screen-button";
    [SerializeField] string footerHintElementName = "end-menu-footer-hint";
    [SerializeField] string loadSlotListElementName = "end-menu-load-slot-list";
    [SerializeField] string loadBackButtonElementName = "end-menu-load-back-button";

#pragma warning disable CS0414
    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] bool debugUiReady;
    [SerializeField] string debugOutcome = "None";
#pragma warning restore CS0414

    VisualElement overlayElement;
    VisualElement panelElement;
    VisualElement mainContentElement;
    VisualElement loadContentElement;
    VisualElement loadSlotListElement;
    Label titleElement;
    Label bodyElement;
    Label footerHintLabel;
    Button loadButton;
    Button restartButton;
    Button titleScreenButton;
    Button loadBackButton;

    readonly List<Button> loadPageButtons = new();
    readonly List<LoadMenuSlotData> currentSlots = new();

    bool uiReady;
    bool warnedMissingUi;
    bool callbacksRegistered;
    bool terminalTriggered;
    bool loadPageActive;
    CursorLockMode previousCursorLockMode;
    bool previousCursorVisible;
    float previousTimeScale = 1f;
    EndOutcome currentOutcome;
    int focusedLoadButtonIndex;

    void OnEnable()
    {
        ActiveInstance = this;
        TreasureTargetController.TreasureReached += HandleTreasureReached;
        HungerController.Starved += HandleStarved;
        BoatHealthController.BoatSunk += HandleBoatSunk;
        TryInitialize();
        SetEndMenuOpen(false, false);
    }

    void Start()
    {
        TryInitialize();
        SetEndMenuOpen(false, false);
    }

    void Update()
    {
        TryInitialize();
        HandleLoadPageInput();
    }

    void OnDisable()
    {
        TreasureTargetController.TreasureReached -= HandleTreasureReached;
        HungerController.Starved -= HandleStarved;
        BoatHealthController.BoatSunk -= HandleBoatSunk;

        if (ActiveInstance == this)
            ActiveInstance = null;

        UnregisterCallbacks();

        if (IsEndMenuOpen)
            RestoreModalState();

        IsEndMenuOpen = false;
        uiReady = false;
        debugUiReady = false;
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
        overlayElement = root.Q(overlayElementName);
        panelElement = root.Q(panelElementName);
        titleElement = root.Q<Label>(titleElementName);
        mainContentElement = root.Q(mainContentElementName);
        loadContentElement = root.Q(loadContentElementName);
        bodyElement = root.Q<Label>(bodyElementName);
        loadButton = root.Q<Button>(loadButtonElementName);
        restartButton = root.Q<Button>(restartButtonElementName);
        titleScreenButton = root.Q<Button>(titleScreenButtonElementName);
        footerHintLabel = root.Q<Label>(footerHintElementName);
        loadSlotListElement = root.Q(loadSlotListElementName);
        loadBackButton = root.Q<Button>(loadBackButtonElementName);

        if (overlayElement == null || panelElement == null || titleElement == null || mainContentElement == null || loadContentElement == null ||
            bodyElement == null || loadButton == null || restartButton == null || titleScreenButton == null || footerHintLabel == null ||
            loadSlotListElement == null || loadBackButton == null)
        {
            if (!warnedMissingUi)
            {
                Debug.LogWarning("[EndMenuController] Missing one or more required end menu UI elements.", this);
                warnedMissingUi = true;
            }
            return;
        }

        BuildLoadSlotButtons();
        RegisterCallbacks();
        RefreshVisuals();
        uiReady = true;
        debugUiReady = true;
    }

    void RegisterCallbacks()
    {
        if (callbacksRegistered)
            return;

        callbacksRegistered = true;
        loadButton.clicked += HandleLoadClicked;
        restartButton.clicked += HandleRestartClicked;
        titleScreenButton.clicked += HandleTitleScreenClicked;
        loadBackButton.clicked += HandleLoadBackClicked;
        loadBackButton.RegisterCallback<PointerEnterEvent>(_ => HandleLoadBackHovered());
    }

    void UnregisterCallbacks()
    {
        if (!callbacksRegistered)
            return;

        callbacksRegistered = false;
        if (loadButton != null)
            loadButton.clicked -= HandleLoadClicked;
        if (restartButton != null)
            restartButton.clicked -= HandleRestartClicked;
        if (titleScreenButton != null)
            titleScreenButton.clicked -= HandleTitleScreenClicked;
        if (loadBackButton != null)
            loadBackButton.clicked -= HandleLoadBackClicked;
    }

    void HandleTreasureReached()
    {
        TriggerOutcome(EndOutcome.Win);
    }

    void HandleStarved()
    {
        TriggerOutcome(EndOutcome.Starved);
    }

    void HandleBoatSunk()
    {
        TriggerOutcome(EndOutcome.Sunk);
    }

    void TriggerOutcome(EndOutcome outcome)
    {
        if (terminalTriggered || outcome == EndOutcome.None)
            return;

        terminalTriggered = true;
        currentOutcome = outcome;

        if (PauseMenuController.IsPauseOpen && PauseMenuController.ActiveInstance != null)
            PauseMenuController.ActiveInstance.CloseForTerminalState();

        SetEndMenuOpen(true, true);
    }

    void HandleLoadPageInput()
    {
        if (!IsEndMenuOpen || !loadPageActive)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            SetLoadPage(false);
            return;
        }

        if (WasMoveDownPressed(keyboard))
        {
            MoveLoadFocus(1);
            return;
        }

        if (WasMoveUpPressed(keyboard))
        {
            MoveLoadFocus(-1);
            return;
        }

        if (WasSubmitPressed(keyboard))
            ActivateFocusedLoadButton();
    }

    void SetEndMenuOpen(bool shouldOpen, bool manageModalState)
    {
        TryInitialize();

        if (overlayElement != null)
            overlayElement.style.display = shouldOpen ? DisplayStyle.Flex : DisplayStyle.None;

        if (panelElement != null)
            panelElement.SetEnabled(shouldOpen);

        if (shouldOpen == IsEndMenuOpen)
        {
            RefreshVisuals();
            return;
        }

        IsEndMenuOpen = shouldOpen;
        if (!manageModalState)
        {
            RefreshVisuals();
            return;
        }

        if (shouldOpen)
            ApplyModalState();
        else
            RestoreModalState();

        RefreshVisuals();
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

    void HandleRestartClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        Time.timeScale = 1f;
        if (dayNightController != null)
            dayNightController.SetPaused(false);

        PlaytimeController.Instance?.ResetPlaytime();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void HandleLoadClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetLoadPage(true);
    }

    void HandleTitleScreenClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        Time.timeScale = 1f;
        if (dayNightController != null)
            dayNightController.SetPaused(false);

        SceneManager.LoadScene(titleSceneName);
    }

    void HandleLoadBackClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetLoadPage(false);
    }

    void RefreshVisuals()
    {
        debugOutcome = currentOutcome.ToString();

        if (titleElement == null || bodyElement == null || footerHintLabel == null)
            return;

        SetDisplay(loadButton, currentOutcome == EndOutcome.Starved || currentOutcome == EndOutcome.Sunk);
        SetDisplay(mainContentElement, !loadPageActive);
        SetDisplay(loadContentElement, loadPageActive);

        switch (currentOutcome)
        {
            case EndOutcome.Win:
                titleElement.text = "You Found the Treasure!";
                bodyElement.text = "Your voyage is complete. You reached the treasure island and claimed the prize at the end of the run.";
                break;
            case EndOutcome.Starved:
                titleElement.text = "You Starved!";
                bodyElement.text = "Your supplies ran dry before the voyage was done. The sea outlasted your hunger.";
                break;
            case EndOutcome.Sunk:
                titleElement.text = "Your Boat Sank!";
                bodyElement.text = "Your hull gave out and the voyage was lost to the sea before you could finish the run.";
                break;
            default:
                titleElement.text = "Run Complete";
                bodyElement.text = string.Empty;
                break;
        }

        footerHintLabel.text = "Choose an option to continue";

        for (int i = 0; i < loadPageButtons.Count; i++)
            ApplyLoadButtonState(loadPageButtons[i], loadPageActive && i == focusedLoadButtonIndex);
    }

    static void SetDisplay(VisualElement element, bool visible)
    {
        if (element == null)
            return;

        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void SetLoadPage(bool active)
    {
        loadPageActive = active;
        RefreshVisuals();

        if (!uiReady || !loadPageActive)
            return;

        loadPageButtons[Mathf.Clamp(focusedLoadButtonIndex, 0, loadPageButtons.Count - 1)].Focus();
    }

    bool WasMoveDownPressed(Keyboard keyboard)
    {
        return keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame;
    }

    bool WasMoveUpPressed(Keyboard keyboard)
    {
        return keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame;
    }

    bool WasSubmitPressed(Keyboard keyboard)
    {
        return keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
    }

    void BuildLoadSlotButtons()
    {
        loadSlotListElement.Clear();
        loadPageButtons.Clear();
        currentSlots.Clear();

        foreach (LoadMenuSlotData slot in LoadMenuSlotData.BuildCurrent())
        {
            currentSlots.Add(slot);
            LoadMenuSlotData capturedSlot = slot;
            Button rowButton = new Button(() => HandleLoadSlotClicked(capturedSlot))
            {
                name = slot.IsAutosave ? "end-menu-load-slot-auto" : $"end-menu-load-slot-{slot.SlotNumber}",
                text = $"{slot.SlotLabel}\n{slot.TitleText}\n{slot.DetailText}"
            };

            rowButton.style.height = 92f;
            rowButton.style.marginBottom = 10f;
            rowButton.style.paddingLeft = 14f;
            rowButton.style.paddingRight = 14f;
            rowButton.style.paddingTop = 8f;
            rowButton.style.paddingBottom = 8f;
            rowButton.style.alignItems = Align.FlexStart;
            rowButton.style.justifyContent = Justify.Center;
            rowButton.style.whiteSpace = WhiteSpace.Normal;
            rowButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            rowButton.style.fontSize = 12f;

            int hoverIndex = loadPageButtons.Count;
            rowButton.RegisterCallback<PointerEnterEvent>(_ => HandleLoadSlotHovered(hoverIndex));
            loadPageButtons.Add(rowButton);
            loadSlotListElement.Add(rowButton);
        }

        loadPageButtons.Add(loadBackButton);
    }

    void HandleLoadSlotClicked(LoadMenuSlotData slot)
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();

        if (!slot.HasData || SaveController.Instance == null)
            return;

        Time.timeScale = 1f;
        SaveController.Instance.LoadFromSlot(slot.SlotNumber);
    }

    void HandleLoadSlotHovered(int buttonIndex)
    {
        if (!loadPageActive || buttonIndex < 0 || buttonIndex >= loadPageButtons.Count - 1)
            return;

        focusedLoadButtonIndex = buttonIndex;
        RefreshVisuals();
    }

    void HandleLoadBackHovered()
    {
        if (!loadPageActive)
            return;

        focusedLoadButtonIndex = loadPageButtons.Count - 1;
        RefreshVisuals();
    }

    void MoveLoadFocus(int delta)
    {
        if (loadPageButtons.Count == 0)
            return;

        focusedLoadButtonIndex += delta;
        if (focusedLoadButtonIndex < 0)
            focusedLoadButtonIndex = loadPageButtons.Count - 1;
        else if (focusedLoadButtonIndex >= loadPageButtons.Count)
            focusedLoadButtonIndex = 0;

        RefreshVisuals();
        loadPageButtons[focusedLoadButtonIndex].Focus();
    }

    void ActivateFocusedLoadButton()
    {
        if (focusedLoadButtonIndex < 0 || focusedLoadButtonIndex >= loadPageButtons.Count)
            return;

        if (focusedLoadButtonIndex == loadPageButtons.Count - 1)
        {
            HandleLoadBackClicked();
            return;
        }

        if (focusedLoadButtonIndex < currentSlots.Count)
            HandleLoadSlotClicked(currentSlots[focusedLoadButtonIndex]);
    }

    void ApplyLoadButtonState(Button button, bool isFocused)
    {
        Color bg = isFocused ? new Color(0.91f, 0.82f, 0.43f, 1f) : new Color(0.86f, 0.86f, 0.86f, 1f);
        Color text = isFocused ? new Color(0.14f, 0.12f, 0.08f, 1f) : new Color(0.22f, 0.22f, 0.22f, 1f);
        Color border = isFocused ? new Color(0.98f, 0.93f, 0.72f, 1f) : new Color(0.52f, 0.52f, 0.52f, 1f);

        button.style.backgroundColor = bg;
        button.style.color = text;
        button.style.borderTopColor = border;
        button.style.borderRightColor = border;
        button.style.borderBottomColor = border;
        button.style.borderLeftColor = border;
        button.style.borderTopWidth = 2f;
        button.style.borderRightWidth = 2f;
        button.style.borderBottomWidth = 2f;
        button.style.borderLeftWidth = 2f;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
    }
}
