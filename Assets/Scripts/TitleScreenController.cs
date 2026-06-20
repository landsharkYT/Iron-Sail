using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
//Kenneth

public class TitleScreenController : MonoBehaviour
{
    enum SettingsTab
    {
        Keybinds = 0,
        Audio = 1,
        Worldgen = 2
    }

    [Header("References")]
    [SerializeField] UIDocument uiDocument;
    [SerializeField] Sprite mainMenuBackgroundSprite;
    [SerializeField] string gameplaySceneName = "SampleScene";

    [Header("Element Names")]
    [SerializeField] string backgroundImageElementName = "title-screen-background-image";
    [SerializeField] string mainPageElementName = "title-screen-main-page";
    [SerializeField] string controlsPageElementName = "title-screen-controls-page";
    [SerializeField] string helpPageElementName = "title-screen-help-page";
    [SerializeField] string loadPageElementName = "title-screen-load-page";
    [SerializeField] string settingsPageElementName = "title-screen-settings-page";
    [SerializeField] string newGameButtonElementName = "title-screen-new-game-button";
    [SerializeField] string loadGameButtonElementName = "title-screen-load-game-button";
    [SerializeField] string controlsButtonElementName = "title-screen-controls-button";
    [SerializeField] string helpButtonElementName = "title-screen-help-button";
    [SerializeField] string settingsButtonElementName = "title-screen-settings-button";
    [SerializeField] string exitGameButtonElementName = "title-screen-exit-game-button";
    [SerializeField] string controlsBackButtonElementName = "title-screen-controls-back-button";
    [SerializeField] string helpBackButtonElementName = "title-screen-help-back-button";
    [SerializeField] string loadSlotListElementName = "title-screen-load-slot-list";
    [SerializeField] string loadBackButtonElementName = "title-screen-load-back-button";
    [SerializeField] string settingsKeybindsTabButtonElementName = "title-screen-settings-keybinds-tab-button";
    [SerializeField] string settingsAudioTabButtonElementName = "title-screen-settings-audio-tab-button";
    [SerializeField] string settingsWorldgenTabButtonElementName = "title-screen-settings-worldgen-tab-button";
    [SerializeField] string settingsKeybindsContentElementName = "title-screen-settings-keybinds-content";
    [SerializeField] string settingsAudioContentElementName = "title-screen-settings-audio-content";
    [SerializeField] string settingsWorldgenContentElementName = "title-screen-settings-worldgen-content";
    [SerializeField] string settingsSeedFieldElementName = "title-screen-settings-seed-field";
    [SerializeField] string settingsSchemeWasdButtonElementName = "title-screen-settings-scheme-wasd-button";
    [SerializeField] string settingsSchemeIjklButtonElementName = "title-screen-settings-scheme-ijkl-button";
    [SerializeField] string settingsSchemeArrowsButtonElementName = "title-screen-settings-scheme-arrows-button";
    [SerializeField] string settingsMasterSliderElementName = "title-screen-settings-master-slider";
    [SerializeField] string settingsMasterValueLabelElementName = "title-screen-settings-master-value-label";
    [SerializeField] string settingsMusicSliderElementName = "title-screen-settings-music-slider";
    [SerializeField] string settingsMusicValueLabelElementName = "title-screen-settings-music-value-label";
    [SerializeField] string settingsSfxSliderElementName = "title-screen-settings-sfx-slider";
    [SerializeField] string settingsSfxValueLabelElementName = "title-screen-settings-sfx-value-label";
    [SerializeField] string settingsAmbienceSliderElementName = "title-screen-settings-ambience-slider";
    [SerializeField] string settingsAmbienceValueLabelElementName = "title-screen-settings-ambience-value-label";
    [SerializeField] string settingsBackButtonElementName = "title-screen-settings-back-button";
    [SerializeField] string controlsSailingLabelElementName = "title-screen-controls-sailing-label";
    [SerializeField] string controlsCombatLabelElementName = "title-screen-controls-combat-label";
    [SerializeField] string controlsMenuLabelElementName = "title-screen-controls-menu-label";

    [Header("Focus Colors")]
    [SerializeField] Color focusedButtonBackground = new(0.91f, 0.82f, 0.43f, 1f);
    [SerializeField] Color focusedButtonText = new(0.14f, 0.12f, 0.08f, 1f);
    [SerializeField] Color focusedButtonBorder = new(0.98f, 0.93f, 0.72f, 1f);
    [SerializeField] Color normalButtonBackground = new(0.86f, 0.86f, 0.86f, 1f);
    [SerializeField] Color normalButtonText = new(0.22f, 0.22f, 0.22f, 1f);
    [SerializeField] Color normalButtonBorder = new(0.52f, 0.52f, 0.52f, 1f);

    Image backgroundImageElement;
    VisualElement mainPageElement;
    VisualElement controlsPageElement;
    VisualElement helpPageElement;
    VisualElement loadPageElement;
    VisualElement settingsPageElement;
    VisualElement loadSlotListElement;
    VisualElement settingsKeybindsContentElement;
    VisualElement settingsAudioContentElement;
    VisualElement settingsWorldgenContentElement;
    TextField settingsSeedField;
    bool seedFieldEditing;
    Button newGameButton;
    Button loadGameButton;
    Button controlsButton;
    Button helpButton;
    Button settingsButton;
    Button exitGameButton;
    Button controlsBackButton;
    Button helpBackButton;
    Button loadBackButton;
    Button settingsKeybindsTabButton;
    Button settingsAudioTabButton;
    Button settingsWorldgenTabButton;
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
    Label controlsSailingLabel;
    Label controlsCombatLabel;
    Label controlsMenuLabel;

    readonly List<Button> mainPageButtons = new();
    readonly List<Button> loadPageButtons = new();
    readonly List<LoadMenuSlotData> currentSlots = new();
    readonly List<VisualElement> settingsPageButtons = new();

    bool uiReady;
    bool warnedMissingUi;
    bool callbacksRegistered;
    bool settingsSubscribed;
    bool controlsPageOpen;
    bool helpPageOpen;
    bool loadPageOpen;
    bool settingsPageOpen;
    int focusedMainButtonIndex;
    int focusedLoadButtonIndex;
    int focusedSettingsButtonIndex;
    SettingsTab activeSettingsTab = SettingsTab.Keybinds;

    void OnEnable()
    {
        SubscribeSettingsChanged();
        TryInitialize();
        ApplyMenuCursorState();
        SetControlsPage(false, false);
    }

    void Start()
    {
        TryInitialize();
        ApplyMenuCursorState();
        SetControlsPage(false, false);
    }

    void Update()
    {
        TryInitialize();
        HandleKeyboardInput();
    }

    void OnDisable()
    {
        UnsubscribeSettingsChanged();
        UnregisterCallbacks();
        uiReady = false;
    }

    void TryInitialize()
    {
        if (uiReady)
            return;

        if (uiDocument == null)
            uiDocument = FindAnyObjectByType<UIDocument>();

        if (uiDocument == null)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        backgroundImageElement = root.Q<Image>(backgroundImageElementName);
        mainPageElement = root.Q(mainPageElementName);
        controlsPageElement = root.Q(controlsPageElementName);
        helpPageElement = root.Q(helpPageElementName);
        loadPageElement = root.Q(loadPageElementName);
        settingsPageElement = root.Q(settingsPageElementName);
        newGameButton = root.Q<Button>(newGameButtonElementName);
        loadGameButton = root.Q<Button>(loadGameButtonElementName);
        controlsButton = root.Q<Button>(controlsButtonElementName);
        helpButton = root.Q<Button>(helpButtonElementName);
        settingsButton = root.Q<Button>(settingsButtonElementName);
        exitGameButton = root.Q<Button>(exitGameButtonElementName);
        controlsBackButton = root.Q<Button>(controlsBackButtonElementName);
        helpBackButton = root.Q<Button>(helpBackButtonElementName);
        loadSlotListElement = root.Q(loadSlotListElementName);
        loadBackButton = root.Q<Button>(loadBackButtonElementName);
        settingsKeybindsTabButton = root.Q<Button>(settingsKeybindsTabButtonElementName);
        settingsAudioTabButton = root.Q<Button>(settingsAudioTabButtonElementName);
        settingsWorldgenTabButton = root.Q<Button>(settingsWorldgenTabButtonElementName);
        settingsKeybindsContentElement = root.Q(settingsKeybindsContentElementName);
        settingsAudioContentElement = root.Q(settingsAudioContentElementName);
        settingsWorldgenContentElement = root.Q(settingsWorldgenContentElementName);
        settingsSeedField = root.Q<TextField>(settingsSeedFieldElementName);
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
        controlsSailingLabel = root.Q<Label>(controlsSailingLabelElementName);
        controlsCombatLabel = root.Q<Label>(controlsCombatLabelElementName);
        controlsMenuLabel = root.Q<Label>(controlsMenuLabelElementName);

        if (backgroundImageElement == null || mainPageElement == null || controlsPageElement == null || helpPageElement == null || loadPageElement == null || settingsPageElement == null ||
            newGameButton == null || loadGameButton == null || controlsButton == null || helpButton == null || settingsButton == null || exitGameButton == null ||
            controlsBackButton == null || helpBackButton == null || loadSlotListElement == null || loadBackButton == null ||
            settingsKeybindsTabButton == null || settingsAudioTabButton == null || settingsWorldgenTabButton == null ||
            settingsKeybindsContentElement == null || settingsAudioContentElement == null ||
            settingsWorldgenContentElement == null || settingsSeedField == null ||
            settingsSchemeWasdButton == null || settingsSchemeIjklButton == null || settingsSchemeArrowsButton == null ||
            settingsMasterSlider == null || settingsMasterValueLabel == null ||
            settingsMusicSlider == null || settingsMusicValueLabel == null ||
            settingsSfxSlider == null || settingsSfxValueLabel == null ||
            settingsAmbienceSlider == null || settingsAmbienceValueLabel == null ||
            settingsBackButton == null ||
            controlsSailingLabel == null || controlsCombatLabel == null || controlsMenuLabel == null)
        {
            if (!warnedMissingUi)
            {
                Debug.LogWarning("[TitleScreenController] Missing one or more required title screen UI elements.", this);
                warnedMissingUi = true;
            }
            return;
        }

        if (mainMenuBackgroundSprite != null)
        {
            backgroundImageElement.image = mainMenuBackgroundSprite.texture;
            backgroundImageElement.scaleMode = ScaleMode.ScaleAndCrop;
        }

        RefreshControlsText();

        mainPageButtons.Clear();
        mainPageButtons.Add(newGameButton);
        mainPageButtons.Add(loadGameButton);
        mainPageButtons.Add(controlsButton);
        mainPageButtons.Add(helpButton);
        mainPageButtons.Add(settingsButton);
        mainPageButtons.Add(exitGameButton);

        BuildLoadSlotButtons();
        BuildSettingsButtons();

        RegisterCallbacks();
        focusedMainButtonIndex = 0;
        focusedLoadButtonIndex = 0;
        focusedSettingsButtonIndex = 0;
        ApplyMenuCursorState();
        RefreshSettingsValues();
        RefreshVisuals();
        uiReady = true;
    }

    void RegisterCallbacks()
    {
        if (callbacksRegistered)
            return;

        callbacksRegistered = true;

        newGameButton.clicked += HandleNewGameClicked;
        loadGameButton.clicked += HandleLoadGameClicked;
        controlsButton.clicked += HandleControlsClicked;
        helpButton.clicked += HandleHelpClicked;
        settingsButton.clicked += HandleSettingsClicked;
        exitGameButton.clicked += HandleExitGameClicked;
        controlsBackButton.clicked += HandleControlsBackClicked;
        helpBackButton.clicked += HandleHelpBackClicked;
        loadBackButton.clicked += HandleLoadBackClicked;
        settingsKeybindsTabButton.clicked += HandleSettingsKeybindsTabClicked;
        settingsAudioTabButton.clicked += HandleSettingsAudioTabClicked;
        settingsWorldgenTabButton.clicked += HandleSettingsWorldgenTabClicked;
        settingsWorldgenTabButton.RegisterCallback<PointerEnterEvent>(_ => HandleSettingsElementHovered(settingsWorldgenTabButton));
        settingsSeedField.RegisterCallback<PointerEnterEvent>(_ => HandleSettingsElementHovered(settingsSeedField));
        settingsSeedField.RegisterValueChangedCallback(OnSeedFieldChanged);
        settingsSeedField.SetValueWithoutNotify(GameRuntimeSettings.WorldgenSeedInput);
        settingsSchemeWasdButton.clicked += HandleSettingsSchemeWasdClicked;
        settingsSchemeIjklButton.clicked += HandleSettingsSchemeIjklClicked;
        settingsSchemeArrowsButton.clicked += HandleSettingsSchemeArrowsClicked;
        settingsMasterSlider.RegisterValueChangedCallback(OnSettingsMasterSliderChanged);
        settingsMusicSlider.RegisterValueChangedCallback(OnSettingsMusicSliderChanged);
        settingsSfxSlider.RegisterValueChangedCallback(OnSettingsSfxSliderChanged);
        settingsAmbienceSlider.RegisterValueChangedCallback(OnSettingsAmbienceSliderChanged);
        settingsBackButton.clicked += HandleSettingsBackClicked;

        RegisterHoverFocus(newGameButton, 0);
        RegisterHoverFocus(loadGameButton, 1);
        RegisterHoverFocus(controlsButton, 2);
        RegisterHoverFocus(helpButton, 3);
        RegisterHoverFocus(settingsButton, 4);
        RegisterHoverFocus(exitGameButton, 5);
        controlsBackButton.RegisterCallback<PointerEnterEvent>(_ => HandleControlsBackHovered());
        helpBackButton.RegisterCallback<PointerEnterEvent>(_ => HandleHelpBackHovered());
        loadBackButton.RegisterCallback<PointerEnterEvent>(_ => HandleLoadBackHovered());
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

        if (newGameButton != null)
            newGameButton.clicked -= HandleNewGameClicked;
        if (loadGameButton != null)
            loadGameButton.clicked -= HandleLoadGameClicked;
        if (controlsButton != null)
            controlsButton.clicked -= HandleControlsClicked;
        if (helpButton != null)
            helpButton.clicked -= HandleHelpClicked;
        if (settingsButton != null)
            settingsButton.clicked -= HandleSettingsClicked;
        if (exitGameButton != null)
            exitGameButton.clicked -= HandleExitGameClicked;
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
        if (settingsWorldgenTabButton != null)
            settingsWorldgenTabButton.clicked -= HandleSettingsWorldgenTabClicked;
        if (settingsSeedField != null)
            settingsSeedField.UnregisterValueChangedCallback(OnSeedFieldChanged);
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

    void RegisterHoverFocus(Button button, int buttonIndex)
    {
        button.RegisterCallback<PointerEnterEvent>(_ => HandleMainButtonHovered(buttonIndex));
    }

    void HandleKeyboardInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !uiReady)
            return;

        if (controlsPageOpen)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                HandleControlsBackClicked();
                return;
            }

            if (WasSubmitPressed(keyboard))
            {
                HandleControlsBackClicked();
                return;
            }

            return;
        }

        if (helpPageOpen)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                HandleHelpBackClicked();
                return;
            }

            if (WasSubmitPressed(keyboard))
            {
                HandleHelpBackClicked();
                return;
            }

            return;
        }

        if (settingsPageOpen)
        {
            // While editing the seed field, let the TextField receive every key.
            // Only Esc/Enter leave edit mode; nav is suspended.
            if (seedFieldEditing)
            {
                if (keyboard.escapeKey.wasPressedThisFrame || WasSubmitPressed(keyboard))
                    StopEditingSeedField();
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                HandleSettingsBackClicked();
                return;
            }

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
                ActivateFocusedSettingsButton();

            return;
        }

        if (loadPageOpen)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                HandleLoadBackClicked();
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

            return;
        }

        if (WasMoveDownPressed(keyboard))
        {
            MoveMainFocus(1);
            return;
        }

        if (WasMoveUpPressed(keyboard))
        {
            MoveMainFocus(-1);
            return;
        }

        if (WasSubmitPressed(keyboard))
            ActivateFocusedMainButton();
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

    void MoveMainFocus(int delta)
    {
        if (mainPageButtons.Count == 0)
            return;

        focusedMainButtonIndex += delta;
        if (focusedMainButtonIndex < 0)
            focusedMainButtonIndex = mainPageButtons.Count - 1;
        else if (focusedMainButtonIndex >= mainPageButtons.Count)
            focusedMainButtonIndex = 0;

        RefreshVisuals();
        mainPageButtons[focusedMainButtonIndex].Focus();
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

        // Don't give the seed field real keyboard focus during navigation, or it
        // would capture typing before the player chooses to edit it. It only takes
        // focus in edit mode (StartEditingSeedField).
        VisualElement target = settingsPageButtons[focusedSettingsButtonIndex];
        if (target == settingsSeedField && !seedFieldEditing)
            return;

        target.Focus();
    }

    void ActivateFocusedMainButton()
    {
        if (focusedMainButtonIndex < 0 || focusedMainButtonIndex >= mainPageButtons.Count)
            return;

        Button button = mainPageButtons[focusedMainButtonIndex];
        if (button == newGameButton)
            HandleNewGameClicked();
        else if (button == loadGameButton)
            HandleLoadGameClicked();
        else if (button == controlsButton)
            HandleControlsClicked();
        else if (button == helpButton)
            HandleHelpClicked();
        else if (button == settingsButton)
            HandleSettingsClicked();
        else if (button == exitGameButton)
            HandleExitGameClicked();
    }

    void HandleMainButtonHovered(int buttonIndex)
    {
        if (controlsPageOpen || helpPageOpen || loadPageOpen || settingsPageOpen)
            return;

        if (buttonIndex < 0 || buttonIndex >= mainPageButtons.Count)
            return;

        focusedMainButtonIndex = buttonIndex;
        RefreshVisuals();
    }

    void HandleControlsBackHovered()
    {
        if (!controlsPageOpen)
            return;

        RefreshVisuals();
        controlsBackButton.Focus();
    }

    void HandleHelpBackHovered()
    {
        if (!helpPageOpen)
            return;

        RefreshVisuals();
        helpBackButton.Focus();
    }

    void HandleLoadBackHovered()
    {
        if (!loadPageOpen)
            return;

        focusedLoadButtonIndex = loadPageButtons.Count - 1;
        RefreshVisuals();
        loadBackButton.Focus();
    }

    void HandleNewGameClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        PlaytimeController.Instance?.ResetPlaytime();

        // Apply the chosen Worldgen seed before the gameplay scene Awakes. A blank
        // field clears any override so the world generates a random seed.
        if (GameRuntimeSettings.TryResolveWorldgenSeed(out int chosenSeed))
            IslandGenerationController.SetDiagnosticPlaySeedOverride(chosenSeed, randomizeSeed: false);
        else
            IslandGenerationController.ClearDiagnosticPlaySeedOverride();

        Time.timeScale = 1f;
        SceneManager.LoadScene(gameplaySceneName);
    }

    void HandleLoadGameClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetLoadPage(true, true);
    }

    void HandleControlsClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetControlsPage(true, true);
    }

    void HandleHelpClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetHelpPage(true, true);
    }

    void HandleSettingsClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetSettingsPage(true, true);
    }

    void HandleExitGameClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        Debug.Log("[TitleScreenController] Exit Game pressed. Exit is not implemented yet in Play Mode.", this);
    }

    void HandleControlsBackClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetControlsPage(false, true);
    }

    void HandleHelpBackClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetHelpPage(false, true);
    }

    void HandleLoadBackClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetLoadPage(false, true);
    }

    void HandleSettingsBackClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        SetSettingsPage(false, true);
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

    void HandleSettingsWorldgenTabClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        StopEditingSeedField();
        activeSettingsTab = SettingsTab.Worldgen;
        RefreshSettingsFocusOrder();
        RefreshVisuals();
    }

    void OnSeedFieldChanged(ChangeEvent<string> evt)
    {
        GameRuntimeSettings.WorldgenSeedInput = evt.newValue;
    }

    void StartEditingSeedField()
    {
        if (settingsSeedField == null)
            return;

        seedFieldEditing = true;
        settingsSeedField.Focus();
        RefreshVisuals();
    }

    void StopEditingSeedField()
    {
        if (!seedFieldEditing)
            return;

        seedFieldEditing = false;
        settingsSeedField?.Blur();
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
        if (!Mathf.Approximately(evt.newValue, evt.previousValue))
            UIAudioController.ActiveInstance?.PlayButtonClick();

        GameRuntimeSettings.MasterVolume01 = evt.newValue / 100f;
    }

    void OnSettingsMusicSliderChanged(ChangeEvent<float> evt)
    {
        if (!Mathf.Approximately(evt.newValue, evt.previousValue))
            UIAudioController.ActiveInstance?.PlayButtonClick();

        GameRuntimeSettings.MusicVolume01 = evt.newValue / 100f;
    }

    void OnSettingsSfxSliderChanged(ChangeEvent<float> evt)
    {
        if (!Mathf.Approximately(evt.newValue, evt.previousValue))
            UIAudioController.ActiveInstance?.PlayButtonClick();

        GameRuntimeSettings.SfxVolume01 = evt.newValue / 100f;
    }

    void OnSettingsAmbienceSliderChanged(ChangeEvent<float> evt)
    {
        if (!Mathf.Approximately(evt.newValue, evt.previousValue))
            UIAudioController.ActiveInstance?.PlayButtonClick();

        GameRuntimeSettings.AmbienceVolume01 = evt.newValue / 100f;
    }

    void SetControlsPage(bool shouldOpen, bool updateFocus)
    {
        controlsPageOpen = shouldOpen;
        helpPageOpen = false;
        loadPageOpen = false;
        settingsPageOpen = false;
        RefreshVisuals();

        if (!uiReady || !updateFocus)
            return;

        if (controlsPageOpen)
            controlsBackButton.Focus();
        else
            mainPageButtons[Mathf.Clamp(focusedMainButtonIndex, 0, mainPageButtons.Count - 1)].Focus();
    }

    void SetHelpPage(bool shouldOpen, bool updateFocus)
    {
        helpPageOpen = shouldOpen;
        controlsPageOpen = false;
        loadPageOpen = false;
        settingsPageOpen = false;
        RefreshVisuals();

        if (!uiReady || !updateFocus)
            return;

        if (helpPageOpen)
            helpBackButton.Focus();
        else
            mainPageButtons[Mathf.Clamp(focusedMainButtonIndex, 0, mainPageButtons.Count - 1)].Focus();
    }

    void SetLoadPage(bool shouldOpen, bool updateFocus)
    {
        loadPageOpen = shouldOpen;
        controlsPageOpen = false;
        helpPageOpen = false;
        settingsPageOpen = false;
        RefreshVisuals();

        if (!uiReady || !updateFocus)
            return;

        if (loadPageOpen)
            loadPageButtons[Mathf.Clamp(focusedLoadButtonIndex, 0, loadPageButtons.Count - 1)].Focus();
        else
            mainPageButtons[Mathf.Clamp(focusedMainButtonIndex, 0, mainPageButtons.Count - 1)].Focus();
    }

    void SetSettingsPage(bool shouldOpen, bool updateFocus)
    {
        settingsPageOpen = shouldOpen;
        controlsPageOpen = false;
        helpPageOpen = false;
        loadPageOpen = false;
        RefreshSettingsFocusOrder();
        RefreshVisuals();

        if (!uiReady || !updateFocus)
            return;

        if (settingsPageOpen)
            settingsPageButtons[Mathf.Clamp(focusedSettingsButtonIndex, 0, settingsPageButtons.Count - 1)].Focus();
        else
            mainPageButtons[Mathf.Clamp(focusedMainButtonIndex, 0, mainPageButtons.Count - 1)].Focus();
    }

    void RefreshVisuals()
    {
        if (!uiReady)
            return;

        mainPageElement.style.display = (!controlsPageOpen && !helpPageOpen && !loadPageOpen && !settingsPageOpen) ? DisplayStyle.Flex : DisplayStyle.None;
        controlsPageElement.style.display = controlsPageOpen ? DisplayStyle.Flex : DisplayStyle.None;
        helpPageElement.style.display = helpPageOpen ? DisplayStyle.Flex : DisplayStyle.None;
        loadPageElement.style.display = loadPageOpen ? DisplayStyle.Flex : DisplayStyle.None;
        settingsPageElement.style.display = settingsPageOpen ? DisplayStyle.Flex : DisplayStyle.None;
        settingsKeybindsContentElement.style.display = activeSettingsTab == SettingsTab.Keybinds ? DisplayStyle.Flex : DisplayStyle.None;
        settingsAudioContentElement.style.display = activeSettingsTab == SettingsTab.Audio ? DisplayStyle.Flex : DisplayStyle.None;
        settingsWorldgenContentElement.style.display = activeSettingsTab == SettingsTab.Worldgen ? DisplayStyle.Flex : DisplayStyle.None;

        for (int i = 0; i < mainPageButtons.Count; i++)
            ApplyButtonState(mainPageButtons[i], !controlsPageOpen && !helpPageOpen && !loadPageOpen && !settingsPageOpen && i == focusedMainButtonIndex);

        ApplyButtonState(controlsBackButton, controlsPageOpen);
        ApplyButtonState(helpBackButton, helpPageOpen);
        for (int i = 0; i < loadPageButtons.Count; i++)
            ApplyButtonState(loadPageButtons[i], loadPageOpen && i == focusedLoadButtonIndex);
        VisualElement focusedSettingsElement = settingsPageOpen
            && focusedSettingsButtonIndex >= 0
            && focusedSettingsButtonIndex < settingsPageButtons.Count
            ? settingsPageButtons[focusedSettingsButtonIndex]
            : null;
        ApplySettingsButtonState(settingsKeybindsTabButton, focusedSettingsElement == settingsKeybindsTabButton, activeSettingsTab == SettingsTab.Keybinds);
        ApplySettingsButtonState(settingsAudioTabButton, focusedSettingsElement == settingsAudioTabButton, activeSettingsTab == SettingsTab.Audio);
        ApplySettingsButtonState(settingsWorldgenTabButton, focusedSettingsElement == settingsWorldgenTabButton, activeSettingsTab == SettingsTab.Worldgen);
        ApplySettingsButtonState(settingsSchemeWasdButton, focusedSettingsElement == settingsSchemeWasdButton, GameRuntimeSettings.CurrentBoatControlScheme == BoatControlScheme.WASD);
        ApplySettingsButtonState(settingsSchemeIjklButton, focusedSettingsElement == settingsSchemeIjklButton, GameRuntimeSettings.CurrentBoatControlScheme == BoatControlScheme.IJKL);
        ApplySettingsButtonState(settingsSchemeArrowsButton, focusedSettingsElement == settingsSchemeArrowsButton, GameRuntimeSettings.CurrentBoatControlScheme == BoatControlScheme.ArrowKeys);
        ApplySettingsSliderState(settingsMasterSlider, focusedSettingsElement == settingsMasterSlider);
        ApplySettingsSliderState(settingsMusicSlider, focusedSettingsElement == settingsMusicSlider);
        ApplySettingsSliderState(settingsSfxSlider, focusedSettingsElement == settingsSfxSlider);
        ApplySettingsSliderState(settingsAmbienceSlider, focusedSettingsElement == settingsAmbienceSlider);
        ApplySettingsFieldState(settingsSeedField, focusedSettingsElement == settingsSeedField || seedFieldEditing);
        ApplyButtonState(settingsBackButton, focusedSettingsElement == settingsBackButton);
    }

    void ApplyButtonState(Button button, bool isFocused)
    {
        button.style.backgroundColor = isFocused ? focusedButtonBackground : normalButtonBackground;
        button.style.color = isFocused ? focusedButtonText : normalButtonText;
        button.style.borderTopColor = isFocused ? focusedButtonBorder : normalButtonBorder;
        button.style.borderRightColor = isFocused ? focusedButtonBorder : normalButtonBorder;
        button.style.borderBottomColor = isFocused ? focusedButtonBorder : normalButtonBorder;
        button.style.borderLeftColor = isFocused ? focusedButtonBorder : normalButtonBorder;
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

        ApplyButtonState(button, isFocused);
    }

    void ApplySettingsSliderState(Slider slider, bool isFocused)
    {
        if (slider == null)
            return;

        slider.style.borderTopWidth = 2f;
        slider.style.borderRightWidth = 2f;
        slider.style.borderBottomWidth = 2f;
        slider.style.borderLeftWidth = 2f;
        slider.style.borderTopColor = isFocused ? focusedButtonBorder : normalButtonBorder;
        slider.style.borderRightColor = isFocused ? focusedButtonBorder : normalButtonBorder;
        slider.style.borderBottomColor = isFocused ? focusedButtonBorder : normalButtonBorder;
        slider.style.borderLeftColor = isFocused ? focusedButtonBorder : normalButtonBorder;
        slider.style.backgroundColor = isFocused ? new Color(0.19f, 0.19f, 0.19f, 1f) : Color.clear;
    }

    void ApplySettingsFieldState(TextField field, bool isFocused)
    {
        if (field == null)
            return;

        field.style.borderTopWidth = 2f;
        field.style.borderRightWidth = 2f;
        field.style.borderBottomWidth = 2f;
        field.style.borderLeftWidth = 2f;
        field.style.borderTopColor = isFocused ? focusedButtonBorder : normalButtonBorder;
        field.style.borderRightColor = isFocused ? focusedButtonBorder : normalButtonBorder;
        field.style.borderBottomColor = isFocused ? focusedButtonBorder : normalButtonBorder;
        field.style.borderLeftColor = isFocused ? focusedButtonBorder : normalButtonBorder;
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
                name = slot.IsAutosave ? "title-screen-load-slot-auto" : $"title-screen-load-slot-{slot.SlotNumber}",
                text = $"{slot.SlotLabel}\n{slot.TitleText}\n{slot.DetailText}"
            };

            rowButton.style.height = 98f;
            rowButton.style.marginBottom = 10f;
            rowButton.style.paddingTop = 10f;
            rowButton.style.paddingBottom = 10f;
            rowButton.style.paddingLeft = 14f;
            rowButton.style.paddingRight = 14f;
            rowButton.style.alignItems = Align.FlexStart;
            rowButton.style.justifyContent = Justify.Center;
            rowButton.style.whiteSpace = WhiteSpace.Normal;
            rowButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            rowButton.style.fontSize = 13f;

            int hoverIndex = loadPageButtons.Count;
            rowButton.RegisterCallback<PointerEnterEvent>(_ => HandleLoadSlotHovered(hoverIndex));
            loadPageButtons.Add(rowButton);
            loadSlotListElement.Add(rowButton);
        }

        loadPageButtons.Add(loadBackButton);
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
        settingsPageButtons.Add(settingsWorldgenTabButton);

        if (activeSettingsTab == SettingsTab.Keybinds)
        {
            settingsPageButtons.Add(settingsSchemeWasdButton);
            settingsPageButtons.Add(settingsSchemeIjklButton);
            settingsPageButtons.Add(settingsSchemeArrowsButton);
        }
        else if (activeSettingsTab == SettingsTab.Audio)
        {
            settingsPageButtons.Add(settingsMasterSlider);
            settingsPageButtons.Add(settingsMusicSlider);
            settingsPageButtons.Add(settingsSfxSlider);
            settingsPageButtons.Add(settingsAmbienceSlider);
        }
        else
        {
            settingsPageButtons.Add(settingsSeedField);
        }

        settingsPageButtons.Add(settingsBackButton);
        focusedSettingsButtonIndex = Mathf.Clamp(focusedSettingsButtonIndex, 0, settingsPageButtons.Count - 1);
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
        if (!loadPageOpen || buttonIndex < 0 || buttonIndex >= loadPageButtons.Count - 1)
            return;

        focusedLoadButtonIndex = buttonIndex;
        RefreshVisuals();
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
        else if (button == settingsWorldgenTabButton)
            HandleSettingsWorldgenTabClicked();
        else if (button == settingsSeedField)
            StartEditingSeedField();
        else if (button == settingsSchemeWasdButton)
            HandleSettingsSchemeWasdClicked();
        else if (button == settingsSchemeIjklButton)
            HandleSettingsSchemeIjklClicked();
        else if (button == settingsSchemeArrowsButton)
            HandleSettingsSchemeArrowsClicked();
        else if (button == settingsBackButton)
            HandleSettingsBackClicked();
    }

    void HandleSettingsElementHovered(VisualElement element)
    {
        if (!settingsPageOpen || element == null)
            return;

        int elementIndex = settingsPageButtons.IndexOf(element);
        if (elementIndex < 0)
            return;

        focusedSettingsButtonIndex = elementIndex;
        RefreshVisuals();
    }

    void AdjustSettingsMasterSlider(float delta)
    {
        if (settingsMasterSlider == null)
            return;

        float nextValue = Mathf.Clamp(settingsMasterSlider.value + delta, settingsMasterSlider.lowValue, settingsMasterSlider.highValue);
        if (Mathf.Approximately(nextValue, settingsMasterSlider.value))
            return;

        UIAudioController.ActiveInstance?.PlayButtonClick();
        GameRuntimeSettings.MasterVolume01 = nextValue / 100f;
    }

    void AdjustSettingsMusicSlider(float delta)
    {
        if (settingsMusicSlider == null)
            return;

        float nextValue = Mathf.Clamp(settingsMusicSlider.value + delta, settingsMusicSlider.lowValue, settingsMusicSlider.highValue);
        if (Mathf.Approximately(nextValue, settingsMusicSlider.value))
            return;

        UIAudioController.ActiveInstance?.PlayButtonClick();
        GameRuntimeSettings.MusicVolume01 = nextValue / 100f;
    }

    void AdjustSettingsSfxSlider(float delta)
    {
        if (settingsSfxSlider == null)
            return;

        float nextValue = Mathf.Clamp(settingsSfxSlider.value + delta, settingsSfxSlider.lowValue, settingsSfxSlider.highValue);
        if (Mathf.Approximately(nextValue, settingsSfxSlider.value))
            return;

        UIAudioController.ActiveInstance?.PlayButtonClick();
        GameRuntimeSettings.SfxVolume01 = nextValue / 100f;
    }

    void AdjustSettingsAmbienceSlider(float delta)
    {
        if (settingsAmbienceSlider == null)
            return;

        float nextValue = Mathf.Clamp(settingsAmbienceSlider.value + delta, settingsAmbienceSlider.lowValue, settingsAmbienceSlider.highValue);
        if (Mathf.Approximately(nextValue, settingsAmbienceSlider.value))
            return;

        UIAudioController.ActiveInstance?.PlayButtonClick();
        GameRuntimeSettings.AmbienceVolume01 = nextValue / 100f;
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

    void ApplyMenuCursorState()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }
}
