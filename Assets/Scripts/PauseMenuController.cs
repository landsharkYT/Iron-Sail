using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using System.Collections.Generic;
//Kenneth

public class PauseMenuController : MonoBehaviour
{
    enum SettingsTab
    {
        Keybinds = 0,
        Audio = 1
    }

    public static bool IsPauseOpen { get; private set; }
    public static PauseMenuController ActiveInstance { get; private set; }

    [Header("References")]
    [SerializeField] UIDocument uiDocument;
    [SerializeField] DayNightController dayNightController;
    [SerializeField] string titleSceneName = "TitleScreen";

    [Header("Element Names")]
    [SerializeField] string overlayElementName = "pause-menu-overlay";
    [SerializeField] string panelElementName = "pause-menu-panel";
    [SerializeField] string titleElementName = "pause-menu-title";
    [SerializeField] string mainContentElementName = "pause-menu-main-content";
    [SerializeField] string controlsContentElementName = "pause-menu-controls-content";
    [SerializeField] string helpContentElementName = "pause-menu-help-content";
    [SerializeField] string loadContentElementName = "pause-menu-load-content";
    [SerializeField] string settingsContentElementName = "pause-menu-settings-content";
    [SerializeField] string continueButtonElementName = "pause-menu-continue-button";
    [SerializeField] string controlsButtonElementName = "pause-menu-controls-button";
    [SerializeField] string helpButtonElementName = "pause-menu-help-button";
    [SerializeField] string settingsButtonElementName = "pause-menu-settings-button";
    [SerializeField] string saveButtonElementName = "pause-menu-save-button";
    [SerializeField] string loadButtonElementName = "pause-menu-load-button";
    [SerializeField] string restartButtonElementName = "pause-menu-restart-button";
    [SerializeField] string titleScreenButtonElementName = "pause-menu-title-screen-button";
    [SerializeField] string statusLabelElementName = "pause-menu-status-label";
    [SerializeField] string footerHintElementName = "pause-menu-footer-hint";
    [SerializeField] string controlsSailingLabelElementName = "pause-menu-controls-sailing-label";
    [SerializeField] string controlsCombatLabelElementName = "pause-menu-controls-combat-label";
    [SerializeField] string controlsMenuLabelElementName = "pause-menu-controls-menu-label";
    [SerializeField] string controlsBackButtonElementName = "pause-menu-controls-back-button";
    [SerializeField] string helpBackButtonElementName = "pause-menu-help-back-button";
    [SerializeField] string loadSlotListElementName = "pause-menu-load-slot-list";
    [SerializeField] string loadBackButtonElementName = "pause-menu-load-back-button";
    [SerializeField] string settingsKeybindsTabButtonElementName = "pause-menu-settings-keybinds-tab-button";
    [SerializeField] string settingsAudioTabButtonElementName = "pause-menu-settings-audio-tab-button";
    [SerializeField] string settingsKeybindsContentElementName = "pause-menu-settings-keybinds-content";
    [SerializeField] string settingsAudioContentElementName = "pause-menu-settings-audio-content";
    [SerializeField] string settingsSchemeWasdButtonElementName = "pause-menu-settings-scheme-wasd-button";
    [SerializeField] string settingsSchemeIjklButtonElementName = "pause-menu-settings-scheme-ijkl-button";
    [SerializeField] string settingsSchemeArrowsButtonElementName = "pause-menu-settings-scheme-arrows-button";
    [SerializeField] string settingsMasterSliderElementName = "pause-menu-settings-master-slider";
    [SerializeField] string settingsMasterValueLabelElementName = "pause-menu-settings-master-value-label";
    [SerializeField] string settingsMusicSliderElementName = "pause-menu-settings-music-slider";
    [SerializeField] string settingsMusicValueLabelElementName = "pause-menu-settings-music-value-label";
    [SerializeField] string settingsSfxSliderElementName = "pause-menu-settings-sfx-slider";
    [SerializeField] string settingsSfxValueLabelElementName = "pause-menu-settings-sfx-value-label";
    [SerializeField] string settingsAmbienceSliderElementName = "pause-menu-settings-ambience-slider";
    [SerializeField] string settingsAmbienceValueLabelElementName = "pause-menu-settings-ambience-value-label";
    [SerializeField] string settingsBackButtonElementName = "pause-menu-settings-back-button";

#pragma warning disable CS0414
    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] bool debugUiReady;
    [SerializeField] bool debugControlsPageActive;
    [SerializeField] bool debugRestartConfirmActive;
    [SerializeField] string debugStatusText = string.Empty;
#pragma warning restore CS0414

    VisualElement overlayElement;
    VisualElement panelElement;
    VisualElement mainContentElement;
    VisualElement controlsContentElement;
    VisualElement helpContentElement;
    VisualElement loadContentElement;
    VisualElement settingsContentElement;
    VisualElement loadSlotListElement;
    VisualElement settingsKeybindsContentElement;
    VisualElement settingsAudioContentElement;
    Label titleElement;
    Label statusLabel;
    Label footerHintLabel;
    Label controlsSailingLabel;
    Label controlsCombatLabel;
    Label controlsMenuLabel;
    Button continueButton;
    Button controlsButton;
    Button helpButton;
    Button settingsButton;
    Button saveButton;
    Button loadButton;
    Button restartButton;
    Button titleScreenButton;
    Button controlsBackButton;
    Button helpBackButton;
    Button loadBackButton;
    Button settingsKeybindsTabButton;
    Button settingsAudioTabButton;
    Button settingsSchemeWasdButton;
    Button settingsSchemeIjklButton;
    Button settingsSchemeArrowsButton;
    Button settingsBackButton;
    Slider settingsMasterSlider;
    Slider settingsMusicSlider;
    Slider settingsSfxSlider;
    Slider settingsAmbienceSlider;
    Label settingsMasterValueLabel;
    Label settingsMusicValueLabel;
    Label settingsSfxValueLabel;
    Label settingsAmbienceValueLabel;

    readonly List<Button> loadPageButtons = new();
    readonly List<VisualElement> settingsPageButtons = new();
    readonly List<LoadMenuSlotData> currentSlots = new();

    bool uiReady;
    bool warnedMissingUi;
    bool callbacksRegistered;
    bool controlsPageActive;
    bool helpPageActive;
    bool loadPageActive;
    bool settingsPageActive;
    bool restartConfirmActive;
    bool slotMenuSaveMode;
    int pendingOverwriteSlot = -1;
    string slotStatusMessage = string.Empty;
    int focusedLoadButtonIndex;
    int focusedSettingsButtonIndex;
    CursorLockMode previousCursorLockMode;
    bool previousCursorVisible;
    float previousTimeScale = 1f;
    SettingsTab activeSettingsTab = SettingsTab.Keybinds;
    bool settingsSubscribed;

    void OnEnable()
    {
        ActiveInstance = this;
        SubscribeSettingsChanged();
        TryInitialize();
        SetPauseOpen(false, false);
    }

    void Start()
    {
        TryInitialize();
        SetPauseOpen(false, false);
    }

    void Update()
    {
        TryInitialize();
        HandleToggleInput();
    }

    void OnDisable()
    {
        UnsubscribeSettingsChanged();
        if (ActiveInstance == this)
            ActiveInstance = null;

        UnregisterCallbacks();

        if (IsPauseOpen)
            RestorePausedState();

        IsPauseOpen = false;
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
        controlsContentElement = root.Q(controlsContentElementName);
        helpContentElement = root.Q(helpContentElementName);
        loadContentElement = root.Q(loadContentElementName);
        settingsContentElement = root.Q(settingsContentElementName);
        continueButton = root.Q<Button>(continueButtonElementName);
        controlsButton = root.Q<Button>(controlsButtonElementName);
        helpButton = root.Q<Button>(helpButtonElementName);
        settingsButton = root.Q<Button>(settingsButtonElementName);
        saveButton = root.Q<Button>(saveButtonElementName);
        loadButton = root.Q<Button>(loadButtonElementName);
        restartButton = root.Q<Button>(restartButtonElementName);
        titleScreenButton = root.Q<Button>(titleScreenButtonElementName);
        statusLabel = root.Q<Label>(statusLabelElementName);
        footerHintLabel = root.Q<Label>(footerHintElementName);
        controlsSailingLabel = root.Q<Label>(controlsSailingLabelElementName);
        controlsCombatLabel = root.Q<Label>(controlsCombatLabelElementName);
        controlsMenuLabel = root.Q<Label>(controlsMenuLabelElementName);
        controlsBackButton = root.Q<Button>(controlsBackButtonElementName);
        helpBackButton = root.Q<Button>(helpBackButtonElementName);
        loadSlotListElement = root.Q(loadSlotListElementName);
        loadBackButton = root.Q<Button>(loadBackButtonElementName);
        settingsKeybindsTabButton = root.Q<Button>(settingsKeybindsTabButtonElementName);
        settingsAudioTabButton = root.Q<Button>(settingsAudioTabButtonElementName);
        settingsKeybindsContentElement = root.Q(settingsKeybindsContentElementName);
        settingsAudioContentElement = root.Q(settingsAudioContentElementName);
        settingsSchemeWasdButton = root.Q<Button>(settingsSchemeWasdButtonElementName);
        settingsSchemeIjklButton = root.Q<Button>(settingsSchemeIjklButtonElementName);
        settingsSchemeArrowsButton = root.Q<Button>(settingsSchemeArrowsButtonElementName);
        settingsMasterSlider = root.Q<Slider>(settingsMasterSliderElementName);
        settingsMasterValueLabel = root.Q<Label>(settingsMasterValueLabelElementName);
        settingsMusicSlider = root.Q<Slider>(settingsMusicSliderElementName);
        settingsMusicValueLabel = root.Q<Label>(settingsMusicValueLabelElementName);
        settingsSfxSlider = root.Q<Slider>(settingsSfxSliderElementName);
        settingsSfxValueLabel = root.Q<Label>(settingsSfxValueLabelElementName);
        settingsAmbienceSlider = root.Q<Slider>(settingsAmbienceSliderElementName);
        settingsAmbienceValueLabel = root.Q<Label>(settingsAmbienceValueLabelElementName);
        settingsBackButton = root.Q<Button>(settingsBackButtonElementName);

        if (overlayElement == null || panelElement == null || titleElement == null || mainContentElement == null ||
            controlsContentElement == null || helpContentElement == null || loadContentElement == null || settingsContentElement == null ||
            continueButton == null || controlsButton == null || helpButton == null || settingsButton == null || saveButton == null || loadButton == null ||
            restartButton == null || titleScreenButton == null ||
            statusLabel == null || footerHintLabel == null || controlsSailingLabel == null || controlsCombatLabel == null ||
            controlsMenuLabel == null || controlsBackButton == null || helpBackButton == null || loadSlotListElement == null || loadBackButton == null ||
            settingsKeybindsTabButton == null || settingsAudioTabButton == null || settingsKeybindsContentElement == null || settingsAudioContentElement == null ||
            settingsSchemeWasdButton == null || settingsSchemeIjklButton == null || settingsSchemeArrowsButton == null ||
            settingsMasterSlider == null || settingsMasterValueLabel == null ||
            settingsMusicSlider == null || settingsMusicValueLabel == null ||
            settingsSfxSlider == null || settingsSfxValueLabel == null ||
            settingsAmbienceSlider == null || settingsAmbienceValueLabel == null ||
            settingsBackButton == null)
        {
            if (!warnedMissingUi)
            {
                Debug.LogWarning("[PauseMenuController] Missing one or more required pause menu UI elements.", this);
                warnedMissingUi = true;
            }
            return;
        }

        RefreshControlsText();

        BuildLoadSlotButtons();
        BuildSettingsButtons();
        RegisterCallbacks();
        focusedLoadButtonIndex = 0;
        focusedSettingsButtonIndex = 0;
        RefreshSettingsValues();
        RefreshVisuals();
        uiReady = true;
        debugUiReady = true;
    }

    void RegisterCallbacks()
    {
        if (callbacksRegistered)
            return;

        callbacksRegistered = true;
        continueButton.clicked += HandleContinueClicked;
        controlsButton.clicked += HandleControlsClicked;
        helpButton.clicked += HandleHelpClicked;
        settingsButton.clicked += HandleSettingsClicked;
        saveButton.clicked += HandleSaveClicked;
        loadButton.clicked += HandleLoadClicked;
        restartButton.clicked += HandleRestartClicked;
        titleScreenButton.clicked += HandleTitleScreenClicked;
        controlsBackButton.clicked += HandleControlsBackClicked;
        helpBackButton.clicked += HandleHelpBackClicked;
        loadBackButton.clicked += HandleLoadBackClicked;
        loadBackButton.RegisterCallback<PointerEnterEvent>(_ => HandleLoadBackHovered());
        settingsKeybindsTabButton.clicked += HandleSettingsKeybindsTabClicked;
        settingsAudioTabButton.clicked += HandleSettingsAudioTabClicked;
        settingsSchemeWasdButton.clicked += HandleSettingsSchemeWasdClicked;
        settingsSchemeIjklButton.clicked += HandleSettingsSchemeIjklClicked;
        settingsSchemeArrowsButton.clicked += HandleSettingsSchemeArrowsClicked;
        settingsMasterSlider.RegisterValueChangedCallback(OnSettingsMasterSliderChanged);
        settingsMusicSlider.RegisterValueChangedCallback(OnSettingsMusicSliderChanged);
        settingsSfxSlider.RegisterValueChangedCallback(OnSettingsSfxSliderChanged);
        settingsAmbienceSlider.RegisterValueChangedCallback(OnSettingsAmbienceSliderChanged);
        settingsBackButton.clicked += HandleSettingsBackClicked;
        settingsKeybindsTabButton.RegisterCallback<PointerEnterEvent>(_ => HandleSettingsElementHovered(settingsKeybindsTabButton));
        settingsAudioTabButton.RegisterCallback<PointerEnterEvent>(_ => HandleSettingsElementHovered(settingsAudioTabButton));
        settingsSchemeWasdButton.RegisterCallback<PointerEnterEvent>(_ => HandleSettingsElementHovered(settingsSchemeWasdButton));
        settingsSchemeIjklButton.RegisterCallback<PointerEnterEvent>(_ => HandleSettingsElementHovered(settingsSchemeIjklButton));
        settingsSchemeArrowsButton.RegisterCallback<PointerEnterEvent>(_ => HandleSettingsElementHovered(settingsSchemeArrowsButton));
        settingsMasterSlider.RegisterCallback<PointerEnterEvent>(_ => HandleSettingsElementHovered(settingsMasterSlider));
        settingsMusicSlider.RegisterCallback<PointerEnterEvent>(_ => HandleSettingsElementHovered(settingsMusicSlider));
        settingsSfxSlider.RegisterCallback<PointerEnterEvent>(_ => HandleSettingsElementHovered(settingsSfxSlider));
        settingsAmbienceSlider.RegisterCallback<PointerEnterEvent>(_ => HandleSettingsElementHovered(settingsAmbienceSlider));
        settingsBackButton.RegisterCallback<PointerEnterEvent>(_ => HandleSettingsElementHovered(settingsBackButton));
    }

    void UnregisterCallbacks()
    {
        if (!callbacksRegistered)
            return;

        callbacksRegistered = false;
        if (continueButton != null)
            continueButton.clicked -= HandleContinueClicked;
        if (controlsButton != null)
            controlsButton.clicked -= HandleControlsClicked;
        if (helpButton != null)
            helpButton.clicked -= HandleHelpClicked;
        if (settingsButton != null)
            settingsButton.clicked -= HandleSettingsClicked;
        if (saveButton != null)
            saveButton.clicked -= HandleSaveClicked;
        if (loadButton != null)
            loadButton.clicked -= HandleLoadClicked;
        if (restartButton != null)
            restartButton.clicked -= HandleRestartClicked;
        if (titleScreenButton != null)
            titleScreenButton.clicked -= HandleTitleScreenClicked;
        if (controlsBackButton != null)
            controlsBackButton.clicked -= HandleControlsBackClicked;
        if (helpBackButton != null)
            helpBackButton.clicked -= HandleHelpBackClicked;
        if (loadBackButton != null)
            loadBackButton.clicked -= HandleLoadBackClicked;
        if (settingsKeybindsTabButton != null)
            settingsKeybindsTabButton.clicked -= HandleSettingsKeybindsTabClicked;
        if (settingsAudioTabButton != null)
            settingsAudioTabButton.clicked -= HandleSettingsAudioTabClicked;
        if (settingsSchemeWasdButton != null)
            settingsSchemeWasdButton.clicked -= HandleSettingsSchemeWasdClicked;
        if (settingsSchemeIjklButton != null)
            settingsSchemeIjklButton.clicked -= HandleSettingsSchemeIjklClicked;
        if (settingsSchemeArrowsButton != null)
            settingsSchemeArrowsButton.clicked -= HandleSettingsSchemeArrowsClicked;
        if (settingsMasterSlider != null)
            settingsMasterSlider.UnregisterValueChangedCallback(OnSettingsMasterSliderChanged);
        if (settingsMusicSlider != null)
            settingsMusicSlider.UnregisterValueChangedCallback(OnSettingsMusicSliderChanged);
        if (settingsSfxSlider != null)
            settingsSfxSlider.UnregisterValueChangedCallback(OnSettingsSfxSliderChanged);
        if (settingsAmbienceSlider != null)
            settingsAmbienceSlider.UnregisterValueChangedCallback(OnSettingsAmbienceSliderChanged);
        if (settingsBackButton != null)
            settingsBackButton.clicked -= HandleSettingsBackClicked;
    }

    void HandleToggleInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (EndMenuController.IsEndMenuOpen)
            return;

        if (IsPauseOpen && loadPageActive)
        {
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
            {
                ActivateFocusedLoadButton();
                return;
            }
        }

        if (IsPauseOpen && settingsPageActive)
        {
            if (settingsPageButtons.Count > 0
                && focusedSettingsButtonIndex >= 0
                && focusedSettingsButtonIndex < settingsPageButtons.Count)
            {
                VisualElement focusedSettingsElement = settingsPageButtons[focusedSettingsButtonIndex];

                if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
                {
                    if (focusedSettingsElement == settingsMasterSlider)
                    {
                        AdjustSettingsMasterSlider(-5f);
                        return;
                    }

                    if (focusedSettingsElement == settingsMusicSlider)
                    {
                        AdjustSettingsMusicSlider(-5f);
                        return;
                    }

                    if (focusedSettingsElement == settingsSfxSlider)
                    {
                        AdjustSettingsSfxSlider(-5f);
                        return;
                    }

                    if (focusedSettingsElement == settingsAmbienceSlider)
                    {
                        AdjustSettingsAmbienceSlider(-5f);
                        return;
                    }
                }

                if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
                {
                    if (focusedSettingsElement == settingsMasterSlider)
                    {
                        AdjustSettingsMasterSlider(5f);
                        return;
                    }

                    if (focusedSettingsElement == settingsMusicSlider)
                    {
                        AdjustSettingsMusicSlider(5f);
                        return;
                    }

                    if (focusedSettingsElement == settingsSfxSlider)
                    {
                        AdjustSettingsSfxSlider(5f);
                        return;
                    }

                    if (focusedSettingsElement == settingsAmbienceSlider)
                    {
                        AdjustSettingsAmbienceSlider(5f);
                        return;
                    }
                }
            }

            if (WasMoveDownPressed(keyboard))
            {
                MoveSettingsFocus(1);
                return;
            }

            if (WasMoveUpPressed(keyboard))
            {
                MoveSettingsFocus(-1);
                return;
            }

            if (WasSubmitPressed(keyboard))
            {
                ActivateFocusedSettingsButton();
                return;
            }
        }

        if (IsPauseOpen && helpPageActive)
        {
            if (WasSubmitPressed(keyboard))
            {
                HandleHelpBackClicked();
                return;
            }
        }

        if (!keyboard.escapeKey.wasPressedThisFrame)
            return;

        if (IsPauseOpen)
        {
            if (controlsPageActive)
            {
                SetControlsPage(false);
                return;
            }

            if (helpPageActive)
            {
                SetHelpPage(false);
                return;
            }

            if (loadPageActive)
            {
                SetLoadPage(false);
                return;
            }

            if (settingsPageActive)
            {
                SetSettingsPage(false);
                return;
            }

            SetPauseOpen(false, true);
            return;
        }

        if (InventoryUIController.IsInventoryOpen || WorldMapUIController.IsMapOpen || ShopController.IsShopOpen || FishingMinigameController.IsFishingOpen)
            return;

        SetPauseOpen(true, true);
    }

    void SetPauseOpen(bool shouldOpen, bool manageModalState)
    {
        TryInitialize();

        if (overlayElement != null)
            overlayElement.style.display = shouldOpen ? DisplayStyle.Flex : DisplayStyle.None;

        if (panelElement != null)
            panelElement.SetEnabled(shouldOpen);

        if (!shouldOpen)
        {
            controlsPageActive = false;
            loadPageActive = false;
            settingsPageActive = false;
            restartConfirmActive = false;
            slotMenuSaveMode = false;
            pendingOverwriteSlot = -1;
            slotStatusMessage = string.Empty;
        }

        if (shouldOpen == IsPauseOpen)
        {
            RefreshVisuals();
            return;
        }

        IsPauseOpen = shouldOpen;
        if (!manageModalState)
        {
            RefreshVisuals();
            return;
        }

        if (shouldOpen)
            ApplyPausedState();
        else
            RestorePausedState();

        RefreshVisuals();
    }

    public void CloseForTerminalState()
    {
        if (!IsPauseOpen)
            return;

        SetPauseOpen(false, true);
    }

    void ApplyPausedState()
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

    void RestorePausedState()
    {
        UnityEngine.Cursor.lockState = previousCursorLockMode;
        UnityEngine.Cursor.visible = previousCursorVisible;
        Time.timeScale = previousTimeScale;

        if (dayNightController != null)
            dayNightController.SetPaused(false);
    }

    void HandleContinueClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetPauseOpen(false, true);
    }

    void HandleControlsClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        restartConfirmActive = false;
        SetControlsPage(true);
    }

    void HandleHelpClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        restartConfirmActive = false;
        SetHelpPage(true);
    }

    void HandleSettingsClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        restartConfirmActive = false;
        SetSettingsPage(true);
    }

    void HandleSaveClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        restartConfirmActive = false;
        SetLoadPage(true, saveMode: true);
    }

    void HandleLoadClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        restartConfirmActive = false;
        SetLoadPage(true, saveMode: false);
    }

    void HandleRestartClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        if (!restartConfirmActive)
        {
            restartConfirmActive = true;
            RefreshVisuals();
            return;
        }

        Time.timeScale = 1f;
        if (dayNightController != null)
            dayNightController.SetPaused(false);

        PlaytimeController.Instance?.ResetPlaytime();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void HandleControlsBackClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetControlsPage(false);
    }

    void HandleHelpBackClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetHelpPage(false);
    }

    void HandleLoadBackClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetLoadPage(false);
    }

    void HandleSettingsBackClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetSettingsPage(false);
    }

    void HandleSettingsKeybindsTabClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        activeSettingsTab = SettingsTab.Keybinds;
        RefreshSettingsFocusOrder();
        RefreshVisuals();
    }

    void HandleSettingsAudioTabClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        activeSettingsTab = SettingsTab.Audio;
        RefreshSettingsFocusOrder();
        RefreshVisuals();
    }

    void HandleSettingsSchemeWasdClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        GameRuntimeSettings.CurrentBoatControlScheme = BoatControlScheme.WASD;
    }

    void HandleSettingsSchemeIjklClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        GameRuntimeSettings.CurrentBoatControlScheme = BoatControlScheme.IJKL;
    }

    void HandleSettingsSchemeArrowsClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        GameRuntimeSettings.CurrentBoatControlScheme = BoatControlScheme.ArrowKeys;
    }

    void OnSettingsMasterSliderChanged(ChangeEvent<float> evt)
    {
        GameRuntimeSettings.MasterVolume01 = evt.newValue / 100f;
    }

    void OnSettingsMusicSliderChanged(ChangeEvent<float> evt)
    {
        GameRuntimeSettings.MusicVolume01 = evt.newValue / 100f;
    }

    void OnSettingsSfxSliderChanged(ChangeEvent<float> evt)
    {
        GameRuntimeSettings.SfxVolume01 = evt.newValue / 100f;
    }

    void OnSettingsAmbienceSliderChanged(ChangeEvent<float> evt)
    {
        GameRuntimeSettings.AmbienceVolume01 = evt.newValue / 100f;
    }

    void HandleTitleScreenClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();

        if (SaveController.Instance != null)
            SaveController.Instance.Autosave();

        Time.timeScale = 1f;
        if (dayNightController != null)
            dayNightController.SetPaused(false);

        SceneManager.LoadScene(titleSceneName);
    }

    void SetControlsPage(bool active)
    {
        controlsPageActive = active;
        helpPageActive = false;
        loadPageActive = false;
        settingsPageActive = false;
        restartConfirmActive = false;
        RefreshVisuals();
    }

    void SetHelpPage(bool active)
    {
        helpPageActive = active;
        controlsPageActive = false;
        loadPageActive = false;
        settingsPageActive = false;
        restartConfirmActive = false;
        RefreshVisuals();

        if (!uiReady)
            return;

        if (helpPageActive)
            helpBackButton.Focus();
    }

    void SetLoadPage(bool active, bool saveMode = false)
    {
        loadPageActive = active;
        slotMenuSaveMode = active && saveMode;
        pendingOverwriteSlot = -1;
        slotStatusMessage = string.Empty;
        controlsPageActive = false;
        helpPageActive = false;
        settingsPageActive = false;
        restartConfirmActive = false;

        // Rebuild each open so slot headers reflect the latest files on disk.
        if (active)
            BuildLoadSlotButtons();

        RefreshVisuals();

        if (!uiReady)
            return;

        if (loadPageActive)
            loadPageButtons[Mathf.Clamp(focusedLoadButtonIndex, 0, loadPageButtons.Count - 1)].Focus();
    }

    void SetSettingsPage(bool active)
    {
        settingsPageActive = active;
        controlsPageActive = false;
        helpPageActive = false;
        loadPageActive = false;
        restartConfirmActive = false;
        RefreshSettingsFocusOrder();
        RefreshVisuals();

        if (!uiReady)
            return;

        if (settingsPageActive)
            settingsPageButtons[Mathf.Clamp(focusedSettingsButtonIndex, 0, settingsPageButtons.Count - 1)].Focus();
    }

    void RefreshVisuals()
    {
        debugControlsPageActive = controlsPageActive;
        debugRestartConfirmActive = restartConfirmActive;
        debugStatusText = restartConfirmActive
            ? "Press Restart Game again to confirm. Continue or Esc will resume the run."
            : string.Empty;

        SetDisplay(mainContentElement, !controlsPageActive && !helpPageActive && !loadPageActive && !settingsPageActive);
        SetDisplay(controlsContentElement, controlsPageActive);
        SetDisplay(helpContentElement, helpPageActive);
        SetDisplay(loadContentElement, loadPageActive);
        SetDisplay(settingsContentElement, settingsPageActive);
        SetDisplay(settingsKeybindsContentElement, activeSettingsTab == SettingsTab.Keybinds);
        SetDisplay(settingsAudioContentElement, activeSettingsTab == SettingsTab.Audio);

        if (titleElement != null)
            titleElement.text = controlsPageActive ? "Paused - Controls" : helpPageActive ? "Paused - Help" : loadPageActive ? (slotMenuSaveMode ? "Paused - Save Game" : "Paused - Load Game") : settingsPageActive ? "Paused - Settings" : "Paused";

        if (statusLabel != null)
        {
            string statusText = restartConfirmActive
                ? "Press Restart Game again to confirm. Continue or Esc will resume the run."
                : (loadPageActive ? slotStatusMessage : string.Empty);
            statusLabel.text = statusText;
            statusLabel.style.display = string.IsNullOrEmpty(statusText) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        if (continueButton != null)
            continueButton.text = "Continue";

        if (controlsButton != null)
            controlsButton.SetEnabled(!restartConfirmActive);

        if (restartButton != null)
            restartButton.text = restartConfirmActive ? "Confirm Restart" : "Restart Game";

        if (footerHintLabel != null)
            footerHintLabel.text = restartConfirmActive
                ? "Esc to continue"
                : "Esc to continue";

        ApplyLoadButtonState(helpBackButton, helpPageActive);
        for (int i = 0; i < loadPageButtons.Count; i++)
            ApplyLoadButtonState(loadPageButtons[i], loadPageActive && i == focusedLoadButtonIndex);
        VisualElement focusedSettingsElement = settingsPageActive
            && focusedSettingsButtonIndex >= 0
            && focusedSettingsButtonIndex < settingsPageButtons.Count
            ? settingsPageButtons[focusedSettingsButtonIndex]
            : null;
        ApplySettingsButtonState(settingsKeybindsTabButton, focusedSettingsElement == settingsKeybindsTabButton, activeSettingsTab == SettingsTab.Keybinds);
        ApplySettingsButtonState(settingsAudioTabButton, focusedSettingsElement == settingsAudioTabButton, activeSettingsTab == SettingsTab.Audio);
        ApplySettingsButtonState(settingsSchemeWasdButton, focusedSettingsElement == settingsSchemeWasdButton, GameRuntimeSettings.CurrentBoatControlScheme == BoatControlScheme.WASD);
        ApplySettingsButtonState(settingsSchemeIjklButton, focusedSettingsElement == settingsSchemeIjklButton, GameRuntimeSettings.CurrentBoatControlScheme == BoatControlScheme.IJKL);
        ApplySettingsButtonState(settingsSchemeArrowsButton, focusedSettingsElement == settingsSchemeArrowsButton, GameRuntimeSettings.CurrentBoatControlScheme == BoatControlScheme.ArrowKeys);
        ApplySettingsSliderState(settingsMasterSlider, focusedSettingsElement == settingsMasterSlider);
        ApplySettingsSliderState(settingsMusicSlider, focusedSettingsElement == settingsMusicSlider);
        ApplySettingsSliderState(settingsSfxSlider, focusedSettingsElement == settingsSfxSlider);
        ApplySettingsSliderState(settingsAmbienceSlider, focusedSettingsElement == settingsAmbienceSlider);
        ApplyLoadButtonState(settingsBackButton, focusedSettingsElement == settingsBackButton);
    }

    static void SetDisplay(VisualElement element, bool visible)
    {
        if (element == null)
            return;

        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
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
        if (loadSlotListElement == null || loadBackButton == null)
            return;

        loadSlotListElement.Clear();
        loadPageButtons.Clear();
        currentSlots.Clear();

        foreach (LoadMenuSlotData slot in LoadMenuSlotData.BuildCurrent())
        {
            currentSlots.Add(slot);
            LoadMenuSlotData capturedSlot = slot;
            Button rowButton = new Button(() => HandleLoadSlotClicked(capturedSlot))
            {
                name = slot.IsAutosave ? "pause-menu-load-slot-auto" : $"pause-menu-load-slot-{slot.SlotNumber}",
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
        focusedLoadButtonIndex = Mathf.Clamp(focusedLoadButtonIndex, 0, loadPageButtons.Count - 1);
    }

    void BuildSettingsButtons()
    {
        RefreshSettingsFocusOrder();
    }

    void RefreshSettingsFocusOrder()
    {
        settingsPageButtons.Clear();
        settingsPageButtons.Add(settingsKeybindsTabButton);
        settingsPageButtons.Add(settingsAudioTabButton);

        if (activeSettingsTab == SettingsTab.Keybinds)
        {
            settingsPageButtons.Add(settingsSchemeWasdButton);
            settingsPageButtons.Add(settingsSchemeIjklButton);
            settingsPageButtons.Add(settingsSchemeArrowsButton);
        }
        else
        {
            settingsPageButtons.Add(settingsMasterSlider);
            settingsPageButtons.Add(settingsMusicSlider);
            settingsPageButtons.Add(settingsSfxSlider);
            settingsPageButtons.Add(settingsAmbienceSlider);
        }

        settingsPageButtons.Add(settingsBackButton);
        focusedSettingsButtonIndex = Mathf.Clamp(focusedSettingsButtonIndex, 0, settingsPageButtons.Count - 1);
    }

    void HandleLoadSlotClicked(LoadMenuSlotData slot)
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();

        if (slotMenuSaveMode)
        {
            HandleSaveTargetSelected(slot);
            return;
        }

        if (!slot.HasData)
        {
            pendingOverwriteSlot = -1;
            slotStatusMessage = "Empty slot";
            RefreshVisuals();
            return;
        }

        if (SaveController.Instance == null)
            return;

        // Loading reloads the scene; clear the pause freeze first so the loaded
        // game does not start frozen.
        Time.timeScale = 1f;
        if (dayNightController != null)
            dayNightController.SetPaused(false);

        SaveController.Instance.LoadFromSlot(slot.SlotNumber);
    }

    void HandleSaveTargetSelected(LoadMenuSlotData slot)
    {
        if (slot.IsAutosave)
        {
            pendingOverwriteSlot = -1;
            slotStatusMessage = "Autosave can't be overwritten";
            RefreshVisuals();
            return;
        }

        // Occupied slots require a second click to confirm (mirrors restart confirm).
        if (slot.HasData && pendingOverwriteSlot != slot.SlotNumber)
        {
            pendingOverwriteSlot = slot.SlotNumber;
            slotStatusMessage = $"Overwrite {slot.SlotLabel}? Select again to confirm.";
            RefreshVisuals();
            return;
        }

        pendingOverwriteSlot = -1;
        bool saved = SaveController.Instance != null && SaveController.Instance.SaveToSlot(slot.SlotNumber);
        slotStatusMessage = saved ? "File Saved" : "Save failed";

        // Rebuild so the just-written slot shows its header.
        if (saved)
            BuildLoadSlotButtons();

        RefreshVisuals();
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

    void HandleSettingsElementHovered(VisualElement element)
    {
        if (!settingsPageActive || element == null)
            return;

        int buttonIndex = settingsPageButtons.IndexOf(element);
        if (buttonIndex < 0)
            return;

        focusedSettingsButtonIndex = buttonIndex;
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

    void MoveSettingsFocus(int delta)
    {
        if (settingsPageButtons.Count == 0)
            return;

        focusedSettingsButtonIndex += delta;
        if (focusedSettingsButtonIndex < 0)
            focusedSettingsButtonIndex = settingsPageButtons.Count - 1;
        else if (focusedSettingsButtonIndex >= settingsPageButtons.Count)
            focusedSettingsButtonIndex = 0;

        RefreshVisuals();
        settingsPageButtons[focusedSettingsButtonIndex].Focus();
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

    void ActivateFocusedSettingsButton()
    {
        if (focusedSettingsButtonIndex < 0 || focusedSettingsButtonIndex >= settingsPageButtons.Count)
            return;

        VisualElement button = settingsPageButtons[focusedSettingsButtonIndex];
        if (button == settingsKeybindsTabButton)
            HandleSettingsKeybindsTabClicked();
        else if (button == settingsAudioTabButton)
            HandleSettingsAudioTabClicked();
        else if (button == settingsSchemeWasdButton)
            HandleSettingsSchemeWasdClicked();
        else if (button == settingsSchemeIjklButton)
            HandleSettingsSchemeIjklClicked();
        else if (button == settingsSchemeArrowsButton)
            HandleSettingsSchemeArrowsClicked();
        else if (button == settingsBackButton)
            HandleSettingsBackClicked();
    }

    void AdjustSettingsMasterSlider(float delta)
    {
        if (settingsMasterSlider == null)
            return;

        float nextValue = Mathf.Clamp(settingsMasterSlider.value + delta, settingsMasterSlider.lowValue, settingsMasterSlider.highValue);
        GameRuntimeSettings.MasterVolume01 = nextValue / 100f;
    }

    void AdjustSettingsMusicSlider(float delta)
    {
        if (settingsMusicSlider == null)
            return;

        float nextValue = Mathf.Clamp(settingsMusicSlider.value + delta, settingsMusicSlider.lowValue, settingsMusicSlider.highValue);
        GameRuntimeSettings.MusicVolume01 = nextValue / 100f;
    }

    void AdjustSettingsSfxSlider(float delta)
    {
        if (settingsSfxSlider == null)
            return;

        float nextValue = Mathf.Clamp(settingsSfxSlider.value + delta, settingsSfxSlider.lowValue, settingsSfxSlider.highValue);
        GameRuntimeSettings.SfxVolume01 = nextValue / 100f;
    }

    void AdjustSettingsAmbienceSlider(float delta)
    {
        if (settingsAmbienceSlider == null)
            return;

        float nextValue = Mathf.Clamp(settingsAmbienceSlider.value + delta, settingsAmbienceSlider.lowValue, settingsAmbienceSlider.highValue);
        GameRuntimeSettings.AmbienceVolume01 = nextValue / 100f;
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

    void ApplySettingsButtonState(Button button, bool isFocused, bool isSelected)
    {
        if (button == null)
            return;

        if (isSelected)
        {
            button.style.backgroundColor = new Color(0.78f, 0.71f, 0.35f, 1f);
            button.style.color = new Color(0.12f, 0.1f, 0.07f, 1f);
            button.style.borderTopColor = new Color(0.98f, 0.93f, 0.72f, 1f);
            button.style.borderRightColor = new Color(0.98f, 0.93f, 0.72f, 1f);
            button.style.borderBottomColor = new Color(0.98f, 0.93f, 0.72f, 1f);
            button.style.borderLeftColor = new Color(0.98f, 0.93f, 0.72f, 1f);
            button.style.borderTopWidth = 2f;
            button.style.borderRightWidth = 2f;
            button.style.borderBottomWidth = 2f;
            button.style.borderLeftWidth = 2f;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            return;
        }

        ApplyLoadButtonState(button, isFocused);
    }

    void ApplySettingsSliderState(Slider slider, bool isFocused)
    {
        if (slider == null)
            return;

        slider.style.borderTopWidth = 2f;
        slider.style.borderRightWidth = 2f;
        slider.style.borderBottomWidth = 2f;
        slider.style.borderLeftWidth = 2f;
        slider.style.borderTopColor = isFocused ? new Color(0.98f, 0.93f, 0.72f, 1f) : new Color(0.52f, 0.52f, 0.52f, 1f);
        slider.style.borderRightColor = isFocused ? new Color(0.98f, 0.93f, 0.72f, 1f) : new Color(0.52f, 0.52f, 0.52f, 1f);
        slider.style.borderBottomColor = isFocused ? new Color(0.98f, 0.93f, 0.72f, 1f) : new Color(0.52f, 0.52f, 0.52f, 1f);
        slider.style.borderLeftColor = isFocused ? new Color(0.98f, 0.93f, 0.72f, 1f) : new Color(0.52f, 0.52f, 0.52f, 1f);
        slider.style.backgroundColor = isFocused ? new Color(0.19f, 0.19f, 0.19f, 1f) : Color.clear;
    }

    void RefreshControlsText()
    {
        if (controlsSailingLabel != null)
            controlsSailingLabel.text = GameRuntimeSettings.BuildSailingControlsText();
        if (controlsCombatLabel != null)
            controlsCombatLabel.text = GameRuntimeSettings.BuildCombatControlsText();
        if (controlsMenuLabel != null)
            controlsMenuLabel.text = GameRuntimeSettings.BuildMenuControlsText();
    }

    void RefreshSettingsValues()
    {
        if (settingsMasterSlider != null)
            settingsMasterSlider.SetValueWithoutNotify(GameRuntimeSettings.MasterVolume01 * 100f);
        if (settingsMasterValueLabel != null)
            settingsMasterValueLabel.text = $"{Mathf.RoundToInt(GameRuntimeSettings.MasterVolume01 * 100f)}%";
        if (settingsMusicSlider != null)
            settingsMusicSlider.SetValueWithoutNotify(GameRuntimeSettings.MusicVolume01 * 100f);
        if (settingsMusicValueLabel != null)
            settingsMusicValueLabel.text = $"{Mathf.RoundToInt(GameRuntimeSettings.MusicVolume01 * 100f)}%";
        if (settingsSfxSlider != null)
            settingsSfxSlider.SetValueWithoutNotify(GameRuntimeSettings.SfxVolume01 * 100f);
        if (settingsSfxValueLabel != null)
            settingsSfxValueLabel.text = $"{Mathf.RoundToInt(GameRuntimeSettings.SfxVolume01 * 100f)}%";
        if (settingsAmbienceSlider != null)
            settingsAmbienceSlider.SetValueWithoutNotify(GameRuntimeSettings.AmbienceVolume01 * 100f);
        if (settingsAmbienceValueLabel != null)
            settingsAmbienceValueLabel.text = $"{Mathf.RoundToInt(GameRuntimeSettings.AmbienceVolume01 * 100f)}%";
    }

    void HandleSharedSettingsChanged()
    {
        RefreshControlsText();
        RefreshSettingsValues();
        RefreshVisuals();
    }

    void SubscribeSettingsChanged()
    {
        if (settingsSubscribed)
            return;

        settingsSubscribed = true;
        GameRuntimeSettings.SettingsChanged += HandleSharedSettingsChanged;
    }

    void UnsubscribeSettingsChanged()
    {
        if (!settingsSubscribed)
            return;

        settingsSubscribed = false;
        GameRuntimeSettings.SettingsChanged -= HandleSharedSettingsChanged;
    }
}
