using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class ShopController : MonoBehaviour
{
    [Serializable]
    class ShopStockEntry
    {
        public ItemDefinition item;
        public int buyPrice = 1;
        public int purchaseQuantity = 1;
    }

    class ShopRuntimeStockEntry
    {
        public ItemDefinition item;
        public int buyPrice;
        public int purchaseQuantity;
        public int remainingChunks;
    }

    public static bool IsShopOpen { get; private set; }
    public static ShopController ActiveInstance { get; private set; }

    [Header("References")]
    [SerializeField] UIDocument uiDocument;
    [SerializeField] ShopDockController shopDockController;
    [SerializeField] TreasureHuntController treasureHuntController;
    [SerializeField] IslandGenerationController islandGenerationController;
    [SerializeField] Transform boatTransform;
    [SerializeField] DayNightController dayNightController;
    [SerializeField] BoatHealthController boatHealthController;
    [SerializeField] Sprite fallbackNullItemIcon;

    [Header("Repair")]
    [SerializeField] [Range(0.1f, 20f)] float goldPerHullPoint = 2f;

    [Header("Shop Stock")]
    [SerializeField] List<ShopStockEntry> sharedShopStock = new();
    [SerializeField] int minEntriesPerShop = 2;
    [SerializeField] int maxEntriesPerShop = 4;
    [SerializeField] int minChunksPerEntry = 1;
    [SerializeField] int maxChunksPerEntry = 3;

    [Header("Talk")]
    [SerializeField] string fallbackTalkLine = "The dockmaster has nothing new to share right now.";

    [Header("Element Names")]
    [SerializeField] string overlayElementName = "shop-menu-overlay";
    [SerializeField] string panelElementName = "shop-menu-panel";
    [SerializeField] string titleElementName = "shop-menu-title";
    [SerializeField] string shopButtonElementName = "shop-menu-shop-button";
    [SerializeField] string repairButtonElementName = "shop-menu-repair-button";
    [SerializeField] string sellButtonElementName = "shop-menu-sell-button";
    [SerializeField] string talkButtonElementName = "shop-menu-talk-button";
    [SerializeField] string restButtonElementName = "shop-menu-rest-button";
    [SerializeField] string leaveButtonElementName = "shop-menu-leave-button";
    [SerializeField] string talkContentElementName = "shop-menu-talk-content";
    [SerializeField] string talkBodyElementName = "shop-menu-talk-body";
    [SerializeField] string talkUpdateElementName = "shop-menu-talk-update";
    [SerializeField] string talkBackButtonElementName = "shop-menu-talk-back-button";
    [SerializeField] string sellContentElementName = "shop-menu-sell-content";
    [SerializeField] string sellSummaryRowElementName = "shop-menu-sell-summary-row";
    [SerializeField] string sellGoldBlockElementName = "shop-menu-sell-gold-block";
    [SerializeField] string sellSlotsBlockElementName = "shop-menu-sell-slots-block";
    [SerializeField] string sellWeightBlockElementName = "shop-menu-sell-weight-block";
    [SerializeField] string sellMainRowElementName = "shop-menu-sell-main-row";
    [SerializeField] string sellGridSectionElementName = "shop-menu-sell-grid-section";
    [SerializeField] string sellGoldValueElementName = "shop-menu-sell-gold-value";
    [SerializeField] string sellSlotSummaryElementName = "shop-menu-sell-slot-summary";
    [SerializeField] string sellWeightSummaryElementName = "shop-menu-sell-weight-summary";
    [SerializeField] string sellGridElementName = "shop-menu-sell-grid";
    [SerializeField] string sellDetailsPanelElementName = "shop-menu-sell-details-panel";
    [SerializeField] string sellDetailsScrollElementName = "shop-menu-sell-details-scroll";
    [SerializeField] string sellDetailsIconElementName = "shop-menu-sell-details-icon";
    [SerializeField] string sellDetailsNameElementName = "shop-menu-sell-details-name";
    [SerializeField] string sellDetailsCategoryElementName = "shop-menu-sell-details-category";
    [SerializeField] string sellDetailsQuantityElementName = "shop-menu-sell-details-quantity";
    [SerializeField] string sellDetailsWeightElementName = "shop-menu-sell-details-weight";
    [SerializeField] string sellDetailsUnitValueElementName = "shop-menu-sell-details-unit-value";
    [SerializeField] string sellDetailsStackValueElementName = "shop-menu-sell-details-stack-value";
    [SerializeField] string sellDetailsDescriptionElementName = "shop-menu-sell-details-description";
    [SerializeField] string sellStatusLabelElementName = "shop-menu-sell-status-label";
    [SerializeField] string tradeFooterElementName = "shop-menu-trade-footer";
    [SerializeField] string buyActionsElementName = "shop-menu-buy-actions";
    [SerializeField] string buyQuantityContextElementName = "shop-menu-buy-quantity-context";
    [SerializeField] string buyOneButtonElementName = "shop-menu-buy-one-button";
    [SerializeField] string buyTwoButtonElementName = "shop-menu-buy-two-button";
    [SerializeField] string buyMaxButtonElementName = "shop-menu-buy-max-button";
    [SerializeField] string sellQuantityPanelElementName = "shop-menu-sell-quantity-panel";
    [SerializeField] string sellQuantityContextElementName = "shop-menu-sell-quantity-context";
    [SerializeField] string sellOneButtonElementName = "shop-menu-sell-one-button";
    [SerializeField] string sellHalfButtonElementName = "shop-menu-sell-half-button";
    [SerializeField] string sellAllButtonElementName = "shop-menu-sell-all-button";
    [SerializeField] string sellCustomFieldElementName = "shop-menu-sell-custom-field";
    [SerializeField] string sellCustomButtonElementName = "shop-menu-sell-custom-button";
    [SerializeField] string sellCancelButtonElementName = "shop-menu-sell-cancel-button";
    [SerializeField] string sellBackButtonElementName = "shop-menu-sell-back-button";
    [SerializeField] string statusLabelElementName = "shop-menu-status-label";
    [SerializeField] string footerHintElementName = "shop-menu-footer-hint";

#pragma warning disable CS0414
    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] bool debugUiReady;
    [SerializeField] bool debugCanOpenShopMenu;
    [SerializeField] bool debugTalkModeActive;
    [SerializeField] bool debugSellModeActive;
    [SerializeField] bool debugBuyModeActive;
    [SerializeField] bool debugSellQuantityPromptActive;
    [SerializeField] Vector2Int debugCurrentShopId;
    [SerializeField] float debugCurrentShopDistance;
    [SerializeField] bool debugHasLatchedShop;
    [SerializeField] Vector2Int debugLatchedShopId;
    [SerializeField] string debugLastTalkBody;
    [SerializeField] string debugLastTalkUpdate;
    [SerializeField] int debugCurrentTalkStepIndex = -1;
    [SerializeField] int debugCurrentTalkStepCount;
    [SerializeField] int debugSelectedSellSlotIndex = -1;
    [SerializeField] string debugLatestSaleMessage;
    [SerializeField] bool debugRepairConfirmActive;
#pragma warning restore CS0414

    VisualElement overlayElement;
    VisualElement panelElement;
    VisualElement talkContentElement;
    VisualElement sellContentElement;
    VisualElement sellSummaryRowElement;
    VisualElement sellGoldBlockElement;
    VisualElement sellSlotsBlockElement;
    VisualElement sellWeightBlockElement;
    VisualElement sellMainRowElement;
    VisualElement sellGridSectionElement;
    VisualElement sellGridElement;
    VisualElement sellDetailsPanelElement;
    VisualElement sellDetailsIconElement;
    VisualElement tradeFooterElement;
    VisualElement buyActionsElement;
    VisualElement sellQuantityPanelElement;
    VisualElement sellDetailsContentElement;
    ScrollView sellDetailsScrollView;
    Label titleElement;
    Label talkBodyElement;
    Label talkUpdateElement;
    Label footerHintElement;
    Label sellGoldValueLabel;
    Label sellSlotSummaryLabel;
    Label sellWeightSummaryLabel;
    Label sellDetailsNameLabel;
    Label sellDetailsCategoryLabel;
    Label sellDetailsQuantityLabel;
    Label sellDetailsWeightLabel;
    Label sellDetailsUnitValueLabel;
    Label sellDetailsStackValueLabel;
    Label sellDetailsDescriptionLabel;
    Label sellStatusLabel;
    Label buyQuantityContextLabel;
    Label sellQuantityContextLabel;
    Label statusLabel;
    Button shopButton;
    Button repairButton;
    Button sellButton;
    Button talkButton;
    Button restButton;
    Button leaveButton;
    Button talkBackButton;
    Button buyOneButton;
    Button buyTwoButton;
    Button buyMaxButton;
    Button sellOneButton;
    Button sellHalfButton;
    Button sellAllButton;
    Button sellCustomButton;
    Button sellCancelButton;
    Button sellBackButton;
    TextField sellCustomField;

    readonly List<VisualElement> sellSlotElements = new();
    readonly List<VisualElement> sellSlotIconElements = new();
    readonly List<Label> sellSlotQuantityLabels = new();

    ShipInventoryController activeInventory;
    bool uiReady;
    bool warnedMissingUi;
    bool callbacksRegistered;
    bool talkModeActive;
    bool sellModeActive;
    bool buyModeActive;
    bool sellQuantityPromptActive;
    bool repairConfirmActive;
    float previousTimeScale = 1f;
    CursorLockMode previousCursorLockMode;
    bool previousCursorVisible;
    string[] activeTalkWhiteLines = Array.Empty<string>();
    string activeTalkYellowLine = string.Empty;
    int currentTalkStepIndex = -1;
    int selectedSellSlotIndex = -1;
    int selectedBuyStockIndex = -1;
    int currentBuyPreviewStackCount = 1;
    int quantityPromptSlotIndex = -1;
    int quantityPromptAvailable = 0;
    string latestSaleMessage = string.Empty;
    string latestPurchaseMessage = string.Empty;
    string statusMessage = string.Empty;
    bool hasLatchedShopTarget;
    ShopDockController.ShopDockQueryResult latchedShopTarget;
    SellLayoutTier currentSellLayoutTier = SellLayoutTier.WideSidebar;

    const float DefaultPanelWidth = 380f;
    const float SellPanelWidth = 1200f;
    const float MinimumResponsivePanelWidth = 340f;
    const float OverlayWidthFraction = 0.88f;
    const float SellOverlayWidthFraction = 0.97f;
    const float OverlayHeightFraction = 0.88f;
    const float WideSidebarMinimumLaneWidth = 500f;
    const float WideSidebarWidthComfortMargin = 44f;
    const float WideSidebarMinimumGridWidth = 360f;
    const float WideSidebarMinimumCardHeight = 452f;
    const float WideSidebarHeightComfortMargin = 36f;
    const float MeaningfulRepairThreshold = 0.05f;
    static readonly Color SlotBorderDefault = new Color32(76, 89, 118, 255);
    static readonly Color SlotBorderSelected = new Color32(242, 209, 111, 255);
    static readonly Color SlotBorderMuted = new Color32(54, 60, 79, 255);
    static readonly Color SlotBackgroundDefault = new Color(14f / 255f, 19f / 255f, 31f / 255f, 0.95f);
    static readonly Color SlotBackgroundEmpty = new Color(11f / 255f, 14f / 255f, 24f / 255f, 0.6f);

    readonly Dictionary<Vector2Int, List<ShopRuntimeStockEntry>> shopStockById = new();
    // Shops the player has invoked "Talk" with at least once (a Met Shopkeeper).
    readonly HashSet<Vector2Int> metShopkeepers = new();

    enum SellLayoutTier
    {
        WideSidebar,
        CompactStacked,
    }

    void OnEnable()
    {
        ActiveInstance = this;
        ShipInventoryController.OnActiveInventoryRegistered += HandleActiveInventoryRegistered;
        HandleActiveInventoryRegistered(ShipInventoryController.ActiveInventory);
        TryInitialize();
        SetShopOpen(false, false);
    }

    void Start()
    {
        TryInitialize();
        SetShopOpen(false, false);
    }

    void Update()
    {
        TryInitialize();
        HandleToggleInput();
    }

    void OnDisable()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;

        ShipInventoryController.OnActiveInventoryRegistered -= HandleActiveInventoryRegistered;
        UnsubscribeFromInventory();
        UnregisterButtonCallbacks();

        if (IsShopOpen)
            RestorePausedState();

        IsShopOpen = false;
        uiReady = false;
        debugUiReady = false;
    }

    void TryInitialize()
    {
        if (uiReady)
            return;

        ResolveFallbackNullItemIcon();
        EnsureDefaultShopStock();

        if (uiDocument == null)
            uiDocument = FindAnyObjectByType<UIDocument>();

        if (shopDockController == null)
            shopDockController = FindAnyObjectByType<ShopDockController>();

        if (treasureHuntController == null)
            treasureHuntController = TreasureHuntController.ActiveInstance ?? FindAnyObjectByType<TreasureHuntController>();

        if (islandGenerationController == null)
            islandGenerationController = FindAnyObjectByType<IslandGenerationController>();

        if (boatTransform == null)
        {
            BoatController boatController = FindAnyObjectByType<BoatController>();
            if (boatController != null)
                boatTransform = boatController.transform;
        }

        if (dayNightController == null)
            dayNightController = FindAnyObjectByType<DayNightController>();

        if (boatHealthController == null)
            boatHealthController = FindAnyObjectByType<BoatHealthController>();

        if (uiDocument == null || shopDockController == null || boatTransform == null)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        overlayElement = root.Q(overlayElementName);
        panelElement = root.Q(panelElementName);
        titleElement = root.Q<Label>(titleElementName);
        shopButton = root.Q<Button>(shopButtonElementName);
        repairButton = root.Q<Button>(repairButtonElementName);
        sellButton = root.Q<Button>(sellButtonElementName);
        talkButton = root.Q<Button>(talkButtonElementName);
        restButton = root.Q<Button>(restButtonElementName);
        leaveButton = root.Q<Button>(leaveButtonElementName);
        talkContentElement = root.Q(talkContentElementName);
        talkBodyElement = root.Q<Label>(talkBodyElementName);
        talkUpdateElement = root.Q<Label>(talkUpdateElementName);
        talkBackButton = root.Q<Button>(talkBackButtonElementName);
        sellContentElement = root.Q(sellContentElementName);
        sellSummaryRowElement = root.Q(sellSummaryRowElementName);
        sellGoldBlockElement = root.Q(sellGoldBlockElementName);
        sellSlotsBlockElement = root.Q(sellSlotsBlockElementName);
        sellWeightBlockElement = root.Q(sellWeightBlockElementName);
        sellMainRowElement = root.Q(sellMainRowElementName);
        sellGridSectionElement = root.Q(sellGridSectionElementName);
        sellGridElement = root.Q(sellGridElementName);
        sellDetailsPanelElement = root.Q(sellDetailsPanelElementName);
        sellDetailsContentElement = root.Q(sellDetailsScrollElementName);
        sellDetailsScrollView = root.Q<ScrollView>(sellDetailsScrollElementName);
        sellDetailsIconElement = root.Q(sellDetailsIconElementName);
        tradeFooterElement = root.Q(tradeFooterElementName);
        buyActionsElement = root.Q(buyActionsElementName);
        sellGoldValueLabel = root.Q<Label>(sellGoldValueElementName);
        sellSlotSummaryLabel = root.Q<Label>(sellSlotSummaryElementName);
        sellWeightSummaryLabel = root.Q<Label>(sellWeightSummaryElementName);
        sellDetailsNameLabel = root.Q<Label>(sellDetailsNameElementName);
        sellDetailsCategoryLabel = root.Q<Label>(sellDetailsCategoryElementName);
        sellDetailsQuantityLabel = root.Q<Label>(sellDetailsQuantityElementName);
        sellDetailsWeightLabel = root.Q<Label>(sellDetailsWeightElementName);
        sellDetailsUnitValueLabel = root.Q<Label>(sellDetailsUnitValueElementName);
        sellDetailsStackValueLabel = root.Q<Label>(sellDetailsStackValueElementName);
        sellDetailsDescriptionLabel = root.Q<Label>(sellDetailsDescriptionElementName);
        sellStatusLabel = root.Q<Label>(sellStatusLabelElementName);
        buyQuantityContextLabel = root.Q<Label>(buyQuantityContextElementName);
        buyOneButton = root.Q<Button>(buyOneButtonElementName);
        buyTwoButton = root.Q<Button>(buyTwoButtonElementName);
        buyMaxButton = root.Q<Button>(buyMaxButtonElementName);
        sellQuantityPanelElement = root.Q(sellQuantityPanelElementName);
        sellQuantityContextLabel = root.Q<Label>(sellQuantityContextElementName);
        sellOneButton = root.Q<Button>(sellOneButtonElementName);
        sellHalfButton = root.Q<Button>(sellHalfButtonElementName);
        sellAllButton = root.Q<Button>(sellAllButtonElementName);
        sellCustomField = root.Q<TextField>(sellCustomFieldElementName);
        sellCustomButton = root.Q<Button>(sellCustomButtonElementName);
        sellCancelButton = root.Q<Button>(sellCancelButtonElementName);
        sellBackButton = root.Q<Button>(sellBackButtonElementName);
        statusLabel = root.Q<Label>(statusLabelElementName);
        footerHintElement = root.Q<Label>(footerHintElementName);

        if (overlayElement == null || panelElement == null || titleElement == null ||
            shopButton == null || repairButton == null || sellButton == null ||
            talkButton == null || restButton == null || leaveButton == null || talkContentElement == null ||
            talkBodyElement == null || talkUpdateElement == null || talkBackButton == null ||
            sellContentElement == null || sellSummaryRowElement == null || sellGoldBlockElement == null ||
            sellSlotsBlockElement == null || sellWeightBlockElement == null || sellMainRowElement == null ||
            sellGridSectionElement == null || sellGridElement == null || sellDetailsPanelElement == null ||
            sellDetailsContentElement == null || sellDetailsScrollView == null || sellDetailsIconElement == null || tradeFooterElement == null ||
            buyActionsElement == null ||
            sellGoldValueLabel == null || sellSlotSummaryLabel == null || sellWeightSummaryLabel == null ||
            sellDetailsNameLabel == null || sellDetailsCategoryLabel == null || sellDetailsQuantityLabel == null ||
            sellDetailsWeightLabel == null || sellDetailsUnitValueLabel == null || sellDetailsStackValueLabel == null ||
            sellDetailsDescriptionLabel == null || sellStatusLabel == null || buyQuantityContextLabel == null ||
            buyOneButton == null || buyTwoButton == null || buyMaxButton == null || sellQuantityPanelElement == null ||
            sellQuantityContextLabel == null || sellOneButton == null || sellHalfButton == null ||
            sellAllButton == null || sellCustomField == null || sellCustomButton == null ||
            sellCancelButton == null || sellBackButton == null || statusLabel == null || footerHintElement == null)
        {
            if (!warnedMissingUi)
            {
                Debug.LogWarning("[ShopController] Missing one or more required shop menu UI elements.", this);
                warnedMissingUi = true;
            }
            return;
        }

        shopButton.SetEnabled(HasAnyShopStockEntries());
        repairButton.SetEnabled(false);
        sellButton.SetEnabled(activeInventory != null);
        talkButton.SetEnabled(treasureHuntController != null);
        RefreshRestButtonState();
        EnsureSellGridBuilt();
        RegisterButtonCallbacks();
        overlayElement.style.display = DisplayStyle.None;
        uiReady = true;
        debugUiReady = true;
        SetTalkMode(false);
        SetSellMode(false, false);
        RefreshSellView();
    }

    void ResolveFallbackNullItemIcon()
    {
        if (fallbackNullItemIcon != null)
            return;

        InventoryUIController inventoryUi = InventoryUIController.ActiveInstance ?? FindAnyObjectByType<InventoryUIController>();
        if (inventoryUi != null)
            fallbackNullItemIcon = inventoryUi.FallbackNullItemIcon;
    }

    void HandleActiveInventoryRegistered(ShipInventoryController inventory)
    {
        if (activeInventory == inventory)
        {
            if (shopButton != null)
                shopButton.SetEnabled(HasAnyShopStockEntries());
            if (sellButton != null)
                sellButton.SetEnabled(activeInventory != null);
            return;
        }

        UnsubscribeFromInventory();
        activeInventory = inventory;
        if (activeInventory != null)
            activeInventory.OnInventoryChanged += HandleInventoryChanged;

        if (shopButton != null)
            shopButton.SetEnabled(HasAnyShopStockEntries());

        if (sellButton != null)
            sellButton.SetEnabled(activeInventory != null);

        if (uiReady)
            RefreshSellView();
    }

    void UnsubscribeFromInventory()
    {
        if (activeInventory != null)
            activeInventory.OnInventoryChanged -= HandleInventoryChanged;
    }

    void HandleInventoryChanged()
    {
        RefreshSellView();
    }

    void HandleToggleInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        bool enterPressed = (keyboard.enterKey != null && keyboard.enterKey.wasPressedThisFrame)
            || (keyboard.numpadEnterKey != null && keyboard.numpadEnterKey.wasPressedThisFrame);

        if (IsShopOpen && talkModeActive && keyboard.spaceKey.wasPressedThisFrame)
        {
            if (!AdvanceTalkDialogue())
                SetTalkMode(false);

            return;
        }

        if (IsShopOpen && repairConfirmActive && enterPressed)
        {
            ExecuteRepair();
            return;
        }

        if (IsShopOpen && buyModeActive && enterPressed)
        {
            TryExecutePreviewedPurchase();
            return;
        }

        if (IsShopOpen && sellQuantityPromptActive && enterPressed)
        {
            TryConfirmCustomSellQuantity();
            return;
        }

        if (keyboard.qKey.wasPressedThisFrame)
        {
            if (talkModeActive)
                return;

            if (IsShopOpen)
            {
                if (repairConfirmActive)
                {
                    CloseRepairConfirm();
                    return;
                }

                if (sellQuantityPromptActive)
                {
                    CloseSellQuantityPrompt();
                    TryResetSellDetailsState();
                    return;
                }

                if (sellModeActive)
                {
                    if (TryResetSellDetailsState())
                        return;

                    SetSellMode(false, true);
                    return;
                }

                if (buyModeActive)
                {
                    if (TryResetBuyDetailsState())
                        return;

                    SetBuyMode(false, true);
                    return;
                }

                SetShopOpen(false, true);
                return;
            }

            if (InventoryUIController.IsInventoryOpen || WorldMapUIController.IsMapOpen || FishingMinigameController.IsFishingOpen || PauseMenuController.IsPauseOpen || EndMenuController.IsEndMenuOpen)
                return;

            if (!TryGetNearbyShop(out ShopDockController.ShopDockQueryResult shopResult))
                return;

            LatchShopTarget(shopResult);
            debugCurrentShopId = shopResult.ShopId;
            debugCurrentShopDistance = shopResult.Distance;
            SetShopOpen(true, true);
            return;
        }

    }

    bool TryGetNearbyShop(out ShopDockController.ShopDockQueryResult shopResult)
    {
        if (shopDockController == null || boatTransform == null)
        {
            debugCanOpenShopMenu = false;
            shopResult = default;
            return false;
        }

        bool hasShop = shopDockController.TryGetNearestShopDock(boatTransform.position, out shopResult);
        debugCanOpenShopMenu = hasShop;
        if (!hasShop)
        {
            debugCurrentShopId = default;
            debugCurrentShopDistance = 0f;
        }

        return hasShop;
    }

    bool TryGetCurrentShopId(out Vector2Int shopId)
    {
        if (hasLatchedShopTarget)
        {
            shopId = latchedShopTarget.ShopId;
            return true;
        }

        if (TryGetNearbyShop(out ShopDockController.ShopDockQueryResult shopResult))
        {
            shopId = shopResult.ShopId;
            return true;
        }

        shopId = default;
        return false;
    }

    void EnsureCurrentShopStockGenerated()
    {
        if (!TryGetCurrentShopId(out Vector2Int shopId))
            return;

        GetOrCreateShopStock(shopId);
    }

    List<ShopRuntimeStockEntry> GetCurrentShopStockEntries()
    {
        if (!TryGetCurrentShopId(out Vector2Int shopId))
            return null;

        return GetOrCreateShopStock(shopId);
    }

    List<ShopRuntimeStockEntry> GetOrCreateShopStock(Vector2Int shopId)
    {
        if (shopStockById.TryGetValue(shopId, out List<ShopRuntimeStockEntry> stockEntries))
            return stockEntries;

        stockEntries = GenerateShopStock(shopId);
        shopStockById[shopId] = stockEntries;
        return stockEntries;
    }

    List<ShopRuntimeStockEntry> GenerateShopStock(Vector2Int shopId)
    {
        List<int> validTemplateIndices = new();
        for (int i = 0; i < sharedShopStock.Count; i++)
        {
            if (IsTemplateEligibleForGeneration(sharedShopStock[i]))
                validTemplateIndices.Add(i);
        }

        List<ShopRuntimeStockEntry> generatedEntries = new();
        if (validTemplateIndices.Count <= 0)
            return generatedEntries;

        System.Random random = new System.Random(CombineShopSeed(ResolveWorldSeed(), shopId));
        for (int i = validTemplateIndices.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            (validTemplateIndices[i], validTemplateIndices[swapIndex]) = (validTemplateIndices[swapIndex], validTemplateIndices[i]);
        }

        int minEntryCount = Mathf.Clamp(minEntriesPerShop, 1, validTemplateIndices.Count);
        int maxEntryCount = Mathf.Clamp(maxEntriesPerShop, minEntryCount, validTemplateIndices.Count);
        int entryCount = random.Next(minEntryCount, maxEntryCount + 1);

        for (int i = 0; i < entryCount; i++)
        {
            ShopStockEntry templateEntry = sharedShopStock[validTemplateIndices[i]];
            generatedEntries.Add(new ShopRuntimeStockEntry
            {
                item = templateEntry.item,
                buyPrice = Mathf.Max(0, templateEntry.buyPrice),
                purchaseQuantity = Mathf.Max(1, templateEntry.purchaseQuantity),
                remainingChunks = random.Next(Mathf.Max(1, minChunksPerEntry), Mathf.Max(Mathf.Max(1, minChunksPerEntry), maxChunksPerEntry) + 1)
            });
        }

        return generatedEntries;
    }

    bool IsTemplateEligibleForGeneration(ShopStockEntry templateEntry)
    {
        return templateEntry != null
            && templateEntry.item != null
            && !templateEntry.item.IsFish;
    }

    int ResolveWorldSeed()
    {
        if (islandGenerationController == null)
            islandGenerationController = FindAnyObjectByType<IslandGenerationController>();

        return islandGenerationController != null ? islandGenerationController.Seed : 0;
    }

    static int CombineShopSeed(int worldSeed, Vector2Int shopId)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + worldSeed;
            hash = hash * 31 + shopId.x;
            hash = hash * 31 + shopId.y;
            return hash;
        }
    }

    bool HasAnyShopStockEntries()
    {
        List<ShopRuntimeStockEntry> currentShopStock = GetCurrentShopStockEntries();
        if (currentShopStock == null)
        {
            for (int i = 0; i < sharedShopStock.Count; i++)
            {
                if (IsTemplateEligibleForGeneration(sharedShopStock[i]))
                    return true;
            }

            return false;
        }

        return currentShopStock.Count > 0;
    }

    bool HasAnyPurchasableShopStock()
    {
        List<ShopRuntimeStockEntry> currentShopStock = GetCurrentShopStockEntries();
        if (currentShopStock == null)
            return false;

        for (int i = 0; i < currentShopStock.Count; i++)
        {
            if (currentShopStock[i] != null && currentShopStock[i].item != null && currentShopStock[i].remainingChunks > 0)
                return true;
        }

        return false;
    }

    bool TryGetCurrentShopStockEntry(int stockIndex, out ShopRuntimeStockEntry entry)
    {
        entry = null;
        List<ShopRuntimeStockEntry> currentShopStock = GetCurrentShopStockEntries();
        if (currentShopStock == null || stockIndex < 0 || stockIndex >= currentShopStock.Count)
            return false;

        entry = currentShopStock[stockIndex];
        return entry != null && entry.item != null;
    }

    void SetShopOpen(bool shouldOpen, bool manageCursorAndPauseState)
    {
        TryInitialize();

        if (shouldOpen)
            EnsureCurrentShopStockGenerated();

        if (overlayElement != null)
            overlayElement.style.display = shouldOpen ? DisplayStyle.Flex : DisplayStyle.None;

        if (panelElement != null)
            panelElement.SetEnabled(shouldOpen);

        if (shouldOpen && shopButton != null)
            shopButton.SetEnabled(HasAnyShopStockEntries());

        if (!shouldOpen)
        {
            ClearLatchedShopTarget();
            SetTalkMode(false);
            SetSellMode(false, false);
            SetBuyMode(false, false);
            CloseRepairConfirm();
            ClearStatusMessage();
        }

        if (shouldOpen == IsShopOpen)
            return;

        IsShopOpen = shouldOpen;
        if (!manageCursorAndPauseState)
            return;

        if (shouldOpen)
            ApplyPausedState();
        else
            RestorePausedState();
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

    void RegisterButtonCallbacks()
    {
        if (callbacksRegistered)
            return;

        callbacksRegistered = true;
        shopButton.clicked += HandleShopClicked;
        leaveButton.clicked += HandleLeaveClicked;
        repairButton.clicked += HandleRepairClicked;
        sellButton.clicked += HandleSellClicked;
        talkButton.clicked += HandleTalkClicked;
        restButton.clicked += HandleRestClicked;
        talkBackButton.clicked += HandleTalkBackClicked;
        buyOneButton.clicked += HandleBuyOneClicked;
        buyTwoButton.clicked += HandleBuyTwoClicked;
        buyMaxButton.clicked += HandleBuyMaxClicked;
        buyOneButton.RegisterCallback<MouseEnterEvent>(HandleBuyOneHovered);
        buyTwoButton.RegisterCallback<MouseEnterEvent>(HandleBuyTwoHovered);
        buyMaxButton.RegisterCallback<MouseEnterEvent>(HandleBuyMaxHovered);
        buyOneButton.RegisterCallback<FocusInEvent>(HandleBuyOneFocused);
        buyTwoButton.RegisterCallback<FocusInEvent>(HandleBuyTwoFocused);
        buyMaxButton.RegisterCallback<FocusInEvent>(HandleBuyMaxFocused);
        buyOneButton.RegisterCallback<MouseLeaveEvent>(HandleBuyPreviewMouseLeft);
        buyTwoButton.RegisterCallback<MouseLeaveEvent>(HandleBuyPreviewMouseLeft);
        buyMaxButton.RegisterCallback<MouseLeaveEvent>(HandleBuyPreviewMouseLeft);
        buyOneButton.RegisterCallback<FocusOutEvent>(HandleBuyPreviewFocusLeft);
        buyTwoButton.RegisterCallback<FocusOutEvent>(HandleBuyPreviewFocusLeft);
        buyMaxButton.RegisterCallback<FocusOutEvent>(HandleBuyPreviewFocusLeft);
        sellOneButton.clicked += HandleSellOneClicked;
        sellHalfButton.clicked += HandleSellHalfClicked;
        sellAllButton.clicked += HandleSellAllClicked;
        sellCustomButton.clicked += HandleSellCustomClicked;
        sellCancelButton.clicked += HandleSellCancelClicked;
        sellBackButton.clicked += HandleSellBackClicked;
        sellCustomField.RegisterValueChangedCallback(HandleSellCustomValueChanged);
        if (talkContentElement != null)
            talkContentElement.RegisterCallback<ClickEvent>(HandleTalkContentClicked);
    }

    void UnregisterButtonCallbacks()
    {
        if (!callbacksRegistered)
            return;

        callbacksRegistered = false;
        if (shopButton != null)
            shopButton.clicked -= HandleShopClicked;
        if (leaveButton != null)
            leaveButton.clicked -= HandleLeaveClicked;
        if (repairButton != null)
            repairButton.clicked -= HandleRepairClicked;
        if (sellButton != null)
            sellButton.clicked -= HandleSellClicked;
        if (talkButton != null)
            talkButton.clicked -= HandleTalkClicked;
        if (restButton != null)
            restButton.clicked -= HandleRestClicked;
        if (talkBackButton != null)
            talkBackButton.clicked -= HandleTalkBackClicked;
        if (buyOneButton != null)
            buyOneButton.clicked -= HandleBuyOneClicked;
        if (buyTwoButton != null)
            buyTwoButton.clicked -= HandleBuyTwoClicked;
        if (buyMaxButton != null)
            buyMaxButton.clicked -= HandleBuyMaxClicked;
        if (sellOneButton != null)
            sellOneButton.clicked -= HandleSellOneClicked;
        if (sellHalfButton != null)
            sellHalfButton.clicked -= HandleSellHalfClicked;
        if (sellAllButton != null)
            sellAllButton.clicked -= HandleSellAllClicked;
        if (buyOneButton != null)
        {
            buyOneButton.UnregisterCallback<MouseEnterEvent>(HandleBuyOneHovered);
            buyOneButton.UnregisterCallback<FocusInEvent>(HandleBuyOneFocused);
            buyOneButton.UnregisterCallback<MouseLeaveEvent>(HandleBuyPreviewMouseLeft);
            buyOneButton.UnregisterCallback<FocusOutEvent>(HandleBuyPreviewFocusLeft);
        }
        if (buyTwoButton != null)
        {
            buyTwoButton.UnregisterCallback<MouseEnterEvent>(HandleBuyTwoHovered);
            buyTwoButton.UnregisterCallback<FocusInEvent>(HandleBuyTwoFocused);
            buyTwoButton.UnregisterCallback<MouseLeaveEvent>(HandleBuyPreviewMouseLeft);
            buyTwoButton.UnregisterCallback<FocusOutEvent>(HandleBuyPreviewFocusLeft);
        }
        if (buyMaxButton != null)
        {
            buyMaxButton.UnregisterCallback<MouseEnterEvent>(HandleBuyMaxHovered);
            buyMaxButton.UnregisterCallback<FocusInEvent>(HandleBuyMaxFocused);
            buyMaxButton.UnregisterCallback<MouseLeaveEvent>(HandleBuyPreviewMouseLeft);
            buyMaxButton.UnregisterCallback<FocusOutEvent>(HandleBuyPreviewFocusLeft);
        }
        if (sellCustomButton != null)
            sellCustomButton.clicked -= HandleSellCustomClicked;
        if (sellCancelButton != null)
            sellCancelButton.clicked -= HandleSellCancelClicked;
        if (sellBackButton != null)
            sellBackButton.clicked -= HandleSellBackClicked;
        if (sellCustomField != null)
            sellCustomField.UnregisterValueChangedCallback(HandleSellCustomValueChanged);
        if (talkContentElement != null)
            talkContentElement.UnregisterCallback<ClickEvent>(HandleTalkContentClicked);
    }

    void HandleLeaveClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        if (repairConfirmActive)
        {
            CloseRepairConfirm();
            return;
        }

        SetShopOpen(false, true);
    }

    void HandleShopClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        if (!IsShopOpen || activeInventory == null || !HasAnyShopStockEntries())
            return;

        latestPurchaseMessage = string.Empty;
        ClearStatusMessage();
        SetBuyMode(true, false);
    }

    void HandleSellClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        if (!IsShopOpen || activeInventory == null)
            return;

        latestSaleMessage = string.Empty;
        ClearStatusMessage();
        SetSellMode(true, false);
    }

    void HandleTalkClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        if (!IsShopOpen)
            return;

        ShopDockController.ShopDockQueryResult shopResult;
        if (hasLatchedShopTarget)
        {
            shopResult = latchedShopTarget;
        }
        else if (!TryGetNearbyShop(out shopResult))
        {
            return;
        }

        debugCurrentShopId = shopResult.ShopId;
        debugCurrentShopDistance = shopResult.Distance;

        TreasureHuntController.ShopTalkResult talkResult;
        if (treasureHuntController == null || !treasureHuntController.TryResolveShopTalk(shopResult.ShopId, out talkResult))
            talkResult = BuildFallbackTalkResult();

        debugLastTalkBody = string.Join("\n", talkResult.whiteLines ?? Array.Empty<string>());
        debugLastTalkUpdate = talkResult.yellowUpdateLine;
        ShowTalkDialogue(talkResult);
    }

    void HandleRestClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        if (!IsShopOpen)
            return;

        if (!CanRestUntilSunrise())
        {
            SetStatusMessage("Rest is only available at night.");
            RefreshModeVisuals();
            return;
        }

        if (dayNightController != null && dayNightController.AdvanceForwardToPhase(DayNightPhase.Sunrise))
            SetStatusMessage("You rest until sunrise.");

        RefreshModeVisuals();
    }

    void HandleTalkBackClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        if (!AdvanceTalkDialogue())
            SetTalkMode(false);
    }

    void HandleTalkContentClicked(ClickEvent evt)
    {
        if (!talkModeActive)
            return;

        if (evt.target is VisualElement targetElement &&
            talkBackButton != null &&
            (targetElement == talkBackButton || talkBackButton.Contains(targetElement)))
            return;

        if (!AdvanceTalkDialogue())
            SetTalkMode(false);
    }

    void HandleSellBackClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        if (buyModeActive)
        {
            if (TryResetBuyDetailsState())
                return;

            SetBuyMode(false, true);
            return;
        }

        if (sellQuantityPromptActive)
        {
            CloseSellQuantityPrompt();
            TryResetSellDetailsState();
            return;
        }

        if (TryResetSellDetailsState())
            return;

        SetSellMode(false, true);
    }

    bool TryResetSellDetailsState()
    {
        bool hadSelection = selectedSellSlotIndex >= 0;
        bool hadSaleMessage = !string.IsNullOrWhiteSpace(latestSaleMessage);
        if (!hadSelection && !hadSaleMessage)
            return false;

        selectedSellSlotIndex = -1;
        latestSaleMessage = string.Empty;
        RefreshSellView();
        return true;
    }

    bool TryResetBuyDetailsState()
    {
        bool hadSelection = selectedBuyStockIndex >= 0;
        bool hadPurchaseMessage = !string.IsNullOrWhiteSpace(latestPurchaseMessage);
        if (!hadSelection && !hadPurchaseMessage)
            return false;

        selectedBuyStockIndex = -1;
        currentBuyPreviewStackCount = 1;
        latestPurchaseMessage = string.Empty;
        RefreshSellView();
        return true;
    }

    void HandleSellOneClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        if (!sellQuantityPromptActive)
            return;

        ExecuteSale(quantityPromptSlotIndex, 1);
    }

    void HandleSellHalfClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        if (!sellQuantityPromptActive || quantityPromptAvailable <= 1)
            return;

        ExecuteSale(quantityPromptSlotIndex, Mathf.Max(1, quantityPromptAvailable / 2));
    }

    void HandleSellAllClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        if (!sellQuantityPromptActive)
            return;

        ExecuteSale(quantityPromptSlotIndex, quantityPromptAvailable);
    }

    void HandleBuyOneClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        ExecutePurchase(selectedBuyStockIndex, 1);
    }

    void HandleBuyTwoClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        ExecutePurchase(selectedBuyStockIndex, 2);
    }

    void HandleBuyMaxClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        ExecutePurchase(selectedBuyStockIndex, GetMaxPurchasableChunkCount(selectedBuyStockIndex));
    }

    void HandleSellCustomClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        TryConfirmCustomSellQuantity();
    }

    void HandleSellCancelClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        CloseSellQuantityPrompt();
        TryResetSellDetailsState();
    }

    void HandleSellCustomValueChanged(ChangeEvent<string> _)
    {
        RefreshSellQuantityConfirmState();
    }

    void HandleBuyOneHovered(MouseEnterEvent _)
    {
        PreviewBuyStackOption(1);
    }

    void HandleBuyTwoHovered(MouseEnterEvent _)
    {
        PreviewBuyStackOption(2);
    }

    void HandleBuyMaxHovered(MouseEnterEvent _)
    {
        PreviewBuyMaxOption();
    }

    void HandleBuyOneFocused(FocusInEvent _)
    {
        PreviewBuyStackOption(1);
    }

    void HandleBuyTwoFocused(FocusInEvent _)
    {
        PreviewBuyStackOption(2);
    }

    void HandleBuyMaxFocused(FocusInEvent _)
    {
        PreviewBuyMaxOption();
    }

    void HandleBuyPreviewMouseLeft(MouseLeaveEvent _)
    {
        ResetBuyPreviewToDefault();
    }

    void HandleBuyPreviewFocusLeft(FocusOutEvent _)
    {
        ResetBuyPreviewToDefault();
    }

    void ShowTalkDialogue(TreasureHuntController.ShopTalkResult talkResult)
    {
        ClearStatusMessage();
        activeTalkWhiteLines = FilterDialogueLines(talkResult.whiteLines);
        activeTalkYellowLine = talkResult.yellowUpdateLine ?? string.Empty;
        currentTalkStepIndex = 0;
        debugCurrentTalkStepCount = GetTotalTalkStepCount();
        metShopkeepers.Add(debugCurrentShopId);
        SetTalkMode(true);
        ApplyCurrentTalkStep();
    }

    public IReadOnlyCollection<Vector2Int> MetShopkeepers => metShopkeepers;

    // Save-restore seam: replace the met-shopkeeper set wholesale.
    public void RestoreMetShopkeepers(IEnumerable<Vector2Int> shopIds)
    {
        metShopkeepers.Clear();
        if (shopIds == null)
            return;

        foreach (Vector2Int id in shopIds)
            metShopkeepers.Add(id);
    }

    void SetTalkMode(bool active)
    {
        talkModeActive = active;
        debugTalkModeActive = active;

        if (!active)
        {
            activeTalkWhiteLines = Array.Empty<string>();
            activeTalkYellowLine = string.Empty;
            currentTalkStepIndex = -1;
            debugCurrentTalkStepIndex = -1;
            debugCurrentTalkStepCount = 0;
            if (talkBodyElement != null)
                talkBodyElement.text = string.Empty;
            if (talkUpdateElement != null)
            {
                talkUpdateElement.text = string.Empty;
                talkUpdateElement.style.display = DisplayStyle.None;
            }
            if (talkBackButton != null)
                talkBackButton.text = "Back";
        }

        RefreshModeVisuals();
    }

    void SetSellMode(bool active, bool preserveSelection)
    {
        if (active)
        {
            buyModeActive = false;
            debugBuyModeActive = false;
        }

        sellModeActive = active;
        debugSellModeActive = active;

        if (!active)
        {
            CloseSellQuantityPrompt();
            if (!preserveSelection)
                selectedSellSlotIndex = -1;
        }
        else if (!preserveSelection)
        {
            selectedSellSlotIndex = -1;
        }

        RefreshModeVisuals();
        if (active)
            RefreshSellView();
    }

    void SetBuyMode(bool active, bool preserveSelection)
    {
        if (active)
        {
            sellModeActive = false;
            debugSellModeActive = false;
        }

        buyModeActive = active;
        debugBuyModeActive = active;

        if (!active)
        {
            if (!preserveSelection)
                selectedBuyStockIndex = -1;
            currentBuyPreviewStackCount = 1;
            latestPurchaseMessage = string.Empty;
        }
        else if (!preserveSelection)
        {
            selectedBuyStockIndex = -1;
            currentBuyPreviewStackCount = 1;
        }

        RefreshModeVisuals();
        if (active)
            RefreshSellView();
    }

    void RefreshModeVisuals()
    {
        bool showMainMenu = !talkModeActive && !sellModeActive && !buyModeActive;
        bool tradeMode = sellModeActive || buyModeActive;

        // During repair confirm the main menu stays visible but Shop/Sell/Talk are hidden:
        // Repair becomes the Confirm button and Leave becomes Cancel.
        SetDisplay(shopButton,   showMainMenu && !repairConfirmActive);
        SetDisplay(repairButton, showMainMenu);
        SetDisplay(sellButton,   showMainMenu && !repairConfirmActive);
        SetDisplay(talkButton,   showMainMenu && !repairConfirmActive);
        SetDisplay(restButton,   showMainMenu && !repairConfirmActive);
        SetDisplay(leaveButton,  showMainMenu);
        SetDisplay(talkContentElement,     talkModeActive);
        SetDisplay(sellContentElement,     sellModeActive || buyModeActive);
        SetDisplay(tradeFooterElement, sellModeActive || buyModeActive);
        SetDisplay(buyActionsElement, buyModeActive);
        SetDisplay(sellQuantityPanelElement, sellModeActive);
        SetDisplay(statusLabel, showMainMenu && !string.IsNullOrWhiteSpace(statusMessage));

        if (tradeMode)
            currentSellLayoutTier = ResolveSellLayoutTier();

        if (panelElement != null)
        {
            float screenHeight = Screen.height > 0 ? Screen.height : 720f;
            panelElement.style.width = CalculateTargetPanelWidth();
            float heightFraction = tradeMode
                ? (currentSellLayoutTier == SellLayoutTier.WideSidebar ? 0.992f : 0.996f)
                : OverlayHeightFraction;
            panelElement.style.maxHeight = Mathf.Max(320f, screenHeight * heightFraction);
        }

        ApplySellLayoutTier();

        if (titleElement != null)
            titleElement.text = sellModeActive
                ? "Dockside Shop - Sell Cargo"
                : buyModeActive
                    ? "Dockside Shop - Shop"
                    : "Dockside Shop";

        if (statusLabel != null)
            statusLabel.text = statusMessage;

        if (repairButton != null && showMainMenu)
        {
            int repairCost = CalculateRepairCost();
            if (repairConfirmActive)
            {
                bool canAffordRepair = activeInventory != null && activeInventory.Gold >= repairCost;
                repairButton.SetEnabled(canAffordRepair);
                repairButton.text = $"Confirm Repair ({repairCost}g)";
            }
            else if (!HasMeaningfulHullDamage())
            {
                repairButton.SetEnabled(false);
                repairButton.text = "Repair (Full)";
            }
            else
            {
                bool canAffordRepair = activeInventory != null && activeInventory.Gold >= repairCost;
                repairButton.SetEnabled(canAffordRepair);
                repairButton.text = $"Repair ({repairCost}g)";
            }
        }

        if (restButton != null && showMainMenu)
            RefreshRestButtonState();

        if (leaveButton != null && showMainMenu)
            leaveButton.text = repairConfirmActive ? "Cancel" : "Leave";

        if (footerHintElement != null)
        {
            if (talkModeActive)
                footerHintElement.text = currentTalkStepIndex + 1 < GetTotalTalkStepCount()
                    ? "Space / Click / Next progress dialogue"
                    : "Space / Click / Close";
            else if (repairConfirmActive)
                footerHintElement.text = "Enter confirm repair / Q cancel";
            else if (sellQuantityPromptActive)
                footerHintElement.text = quantityPromptAvailable <= 1
                    ? "Confirm / Q cancel sale"
                    : "Enter confirm X / Q cancel quantity";
            else if (buyModeActive)
                footerHintElement.text = "Click stock to buy / Q back";
            else if (sellModeActive)
                footerHintElement.text = "Click cargo to sell / Q back";
            else
                footerHintElement.text = "Q close";
        }
    }

    void LatchShopTarget(ShopDockController.ShopDockQueryResult shopResult)
    {
        latchedShopTarget = shopResult;
        hasLatchedShopTarget = true;
        debugHasLatchedShop = true;
        debugLatchedShopId = shopResult.ShopId;
    }

    void ClearLatchedShopTarget()
    {
        latchedShopTarget = default;
        hasLatchedShopTarget = false;
        debugHasLatchedShop = false;
        debugLatchedShopId = default;
    }

    bool AdvanceTalkDialogue()
    {
        if (!talkModeActive)
            return false;

        int totalStepCount = GetTotalTalkStepCount();
        if (totalStepCount <= 0)
            return false;

        if (currentTalkStepIndex + 1 >= totalStepCount)
            return false;

        currentTalkStepIndex++;
        ApplyCurrentTalkStep();
        return true;
    }

    void ApplyCurrentTalkStep()
    {
        int totalStepCount = GetTotalTalkStepCount();
        if (totalStepCount <= 0)
        {
            if (talkBodyElement != null)
                talkBodyElement.text = string.Empty;
            if (talkUpdateElement != null)
                talkUpdateElement.style.display = DisplayStyle.None;
            if (talkBackButton != null)
                talkBackButton.text = "Close";
            debugCurrentTalkStepIndex = -1;
            return;
        }

        currentTalkStepIndex = Mathf.Clamp(currentTalkStepIndex, 0, totalStepCount - 1);
        debugCurrentTalkStepIndex = currentTalkStepIndex;

        bool showingYellowStep = HasYellowTalkStep() && currentTalkStepIndex >= activeTalkWhiteLines.Length;

        if (talkBodyElement != null)
        {
            if (activeTalkWhiteLines.Length > 0)
            {
                int whiteIndex = Mathf.Clamp(Mathf.Min(currentTalkStepIndex, activeTalkWhiteLines.Length - 1), 0, activeTalkWhiteLines.Length - 1);
                talkBodyElement.text = activeTalkWhiteLines[whiteIndex];
            }
            else
            {
                talkBodyElement.text = string.Empty;
            }
        }

        if (talkUpdateElement != null)
        {
            bool showYellow = showingYellowStep && !string.IsNullOrWhiteSpace(activeTalkYellowLine);
            talkUpdateElement.text = showYellow ? activeTalkYellowLine : string.Empty;
            talkUpdateElement.style.display = showYellow ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (talkBackButton != null)
            talkBackButton.text = currentTalkStepIndex + 1 < totalStepCount ? "Next" : "Close";
    }

    int GetTotalTalkStepCount()
    {
        return activeTalkWhiteLines.Length + (HasYellowTalkStep() ? 1 : 0);
    }

    bool HasYellowTalkStep()
    {
        return !string.IsNullOrWhiteSpace(activeTalkYellowLine);
    }

    void EnsureSellGridBuilt()
    {
        if (sellGridElement == null)
            return;

        List<ShopRuntimeStockEntry> currentShopStock = buyModeActive ? GetCurrentShopStockEntries() : null;
        int targetCount = buyModeActive
            ? Mathf.Max(1, currentShopStock != null ? currentShopStock.Count : 0)
            : activeInventory != null ? activeInventory.MaxSlots : 0;

        if (targetCount <= 0)
            return;

        if (sellSlotElements.Count == targetCount)
            return;

        sellGridElement.Clear();
        sellSlotElements.Clear();
        sellSlotIconElements.Clear();
        sellSlotQuantityLabels.Clear();

        for (int slotIndex = 0; slotIndex < targetCount; slotIndex++)
        {
            int capturedIndex = slotIndex;

            VisualElement slot = new VisualElement();
            slot.style.width = 84f;
            slot.style.height = 84f;
            slot.style.marginRight = 10f;
            slot.style.marginBottom = 10f;
            slot.style.position = Position.Relative;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            slot.style.backgroundColor = SlotBackgroundDefault;
            slot.style.borderTopWidth = 2f;
            slot.style.borderRightWidth = 2f;
            slot.style.borderBottomWidth = 2f;
            slot.style.borderLeftWidth = 2f;
            slot.pickingMode = PickingMode.Position;
            slot.RegisterCallback<ClickEvent>(_ => HandleTradeSlotClicked(capturedIndex));

            VisualElement icon = new VisualElement();
            icon.style.width = 56f;
            icon.style.height = 56f;
            icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            icon.pickingMode = PickingMode.Ignore;

            Label quantityLabel = new Label();
            quantityLabel.style.position = Position.Absolute;
            quantityLabel.style.right = 4f;
            quantityLabel.style.bottom = 4f;
            quantityLabel.style.minWidth = 18f;
            quantityLabel.style.paddingLeft = 4f;
            quantityLabel.style.paddingRight = 4f;
            quantityLabel.style.paddingTop = 1f;
            quantityLabel.style.paddingBottom = 1f;
            quantityLabel.style.color = new StyleColor(new Color32(248, 248, 255, 255));
            quantityLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            quantityLabel.style.fontSize = 15f;
            quantityLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            quantityLabel.style.backgroundColor = new Color(9f / 255f, 12f / 255f, 20f / 255f, 0.9f);
            quantityLabel.pickingMode = PickingMode.Ignore;

            slot.Add(icon);
            slot.Add(quantityLabel);
            sellGridElement.Add(slot);

            sellSlotElements.Add(slot);
            sellSlotIconElements.Add(icon);
            sellSlotQuantityLabels.Add(quantityLabel);
        }

        ApplySellLayoutTier();
    }

    void HandleTradeSlotClicked(int slotIndex)
    {
        if (buyModeActive)
        {
            HandleBuySlotClicked(slotIndex);
            return;
        }

        HandleSellSlotClicked(slotIndex);
    }

    void HandleSellSlotClicked(int slotIndex)
    {
        if (!sellModeActive || activeInventory == null)
            return;

        selectedSellSlotIndex = slotIndex;
        debugSelectedSellSlotIndex = selectedSellSlotIndex;
        RefreshSellDetails();
        RefreshSellSlotVisuals();

        if (!activeInventory.TryGetSlotSnapshot(slotIndex, out ShipInventoryController.InventorySlotSnapshot slot) || slot.IsEmpty || slot.Item == null)
        {
            if (sellQuantityPromptActive)
                CloseSellQuantityPrompt();
            return;
        }

        if (slot.Item.Value <= 0)
        {
            if (sellQuantityPromptActive)
                CloseSellQuantityPrompt();
            return;
        }

        if (sellQuantityPromptActive && quantityPromptSlotIndex == slotIndex)
            return;

        UIAudioController.ActiveInstance?.PlayInventoryClick();
        OpenSellQuantityPrompt(slotIndex, slot);
    }

    void HandleBuySlotClicked(int stockIndex)
    {
        if (!buyModeActive || !TryGetCurrentShopStockEntry(stockIndex, out _))
            return;

        UIAudioController.ActiveInstance?.PlayInventoryClick();
        selectedBuyStockIndex = stockIndex;
        currentBuyPreviewStackCount = 1;
        latestPurchaseMessage = string.Empty;
        RefreshSellDetails();
        RefreshSellSlotVisuals();
        RefreshSellQuantityPrompt();
        RefreshModeVisuals();
    }

    void RefreshSellView()
    {
        if (!uiReady)
            return;

        EnsureSellGridBuilt();
        RefreshSellSummary();
        RefreshSellGrid();
        RefreshSellDetails();
        RefreshSellQuantityPrompt();
        RefreshModeVisuals();
    }

    void RefreshSellSummary()
    {
        if (sellGoldValueLabel == null || sellSlotSummaryLabel == null || sellWeightSummaryLabel == null)
            return;

        if (activeInventory == null)
        {
            sellGoldValueLabel.text = "0";
            sellSlotSummaryLabel.text = "0/0";
            sellWeightSummaryLabel.text = "0/0";
            return;
        }

        ShipEquipmentController equipment = ShipEquipmentController.ActiveEquipment;
        int reservedUsedSlots = equipment != null ? equipment.UsedSlotCount : 0;
        float reservedWeight = equipment != null ? equipment.CurrentWeight : 0f;
        sellGoldValueLabel.text = activeInventory.Gold.ToString();
        sellSlotSummaryLabel.text = $"{activeInventory.UsedSlotCount + reservedUsedSlots}/{activeInventory.MaxSlots}";
        sellWeightSummaryLabel.text = $"{activeInventory.CurrentWeight + reservedWeight:0.##}/{activeInventory.MaxCarryWeight:0.##}";
    }

    void RefreshSellGrid()
    {
        if (buyModeActive)
        {
            RefreshBuyGrid();
            return;
        }

        if (activeInventory == null)
            return;

        for (int i = 0; i < sellSlotElements.Count; i++)
        {
            activeInventory.TryGetSlotSnapshot(i, out ShipInventoryController.InventorySlotSnapshot slot);
            VisualElement slotElement = sellSlotElements[i];
            VisualElement iconElement = sellSlotIconElements[i];
            Label quantityLabel = sellSlotQuantityLabels[i];

            if (!slot.IsEmpty && slot.Item != null)
            {
                Sprite icon = ResolveItemIcon(slot.Item);
                iconElement.style.backgroundImage = icon != null ? new StyleBackground(icon) : StyleKeyword.None;
                quantityLabel.text = slot.Quantity > 1 ? slot.Quantity.ToString() : string.Empty;
                quantityLabel.style.display = slot.Quantity > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else
            {
                iconElement.style.backgroundImage = StyleKeyword.None;
                quantityLabel.text = string.Empty;
                quantityLabel.style.display = DisplayStyle.None;
            }

            ApplyTradeSlotVisualState(slotElement, i == selectedSellSlotIndex, slot.IsEmpty, !slot.IsEmpty);
        }
    }

    void RefreshSellSlotVisuals()
    {
        if (buyModeActive)
        {
            for (int i = 0; i < sellSlotElements.Count; i++)
            {
                bool hasEntry = TryGetCurrentShopStockEntry(i, out _);
                ApplyTradeSlotVisualState(sellSlotElements[i], i == selectedBuyStockIndex, !hasEntry, hasEntry && CanPurchaseStockEntry(i));
            }
            return;
        }

        if (activeInventory == null)
            return;

        for (int i = 0; i < sellSlotElements.Count; i++)
        {
            activeInventory.TryGetSlotSnapshot(i, out ShipInventoryController.InventorySlotSnapshot slot);
            ApplyTradeSlotVisualState(sellSlotElements[i], i == selectedSellSlotIndex, slot.IsEmpty, !slot.IsEmpty);
        }
    }

    void RefreshBuyGrid()
    {
        for (int i = 0; i < sellSlotElements.Count; i++)
        {
            VisualElement slotElement = sellSlotElements[i];
            VisualElement iconElement = sellSlotIconElements[i];
            Label quantityLabel = sellSlotQuantityLabels[i];

            bool hasEntry = TryGetCurrentShopStockEntry(i, out ShopRuntimeStockEntry entry);
            if (hasEntry)
            {
                Sprite icon = ResolveItemIcon(entry.item);
                iconElement.style.backgroundImage = icon != null ? new StyleBackground(icon) : StyleKeyword.None;
                quantityLabel.text = entry.purchaseQuantity > 1 ? $"x{entry.purchaseQuantity}" : string.Empty;
                quantityLabel.style.display = entry.purchaseQuantity > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else
            {
                iconElement.style.backgroundImage = StyleKeyword.None;
                quantityLabel.text = string.Empty;
                quantityLabel.style.display = DisplayStyle.None;
            }

            ApplyTradeSlotVisualState(slotElement, i == selectedBuyStockIndex, !hasEntry, hasEntry && CanPurchaseStockEntry(i));
        }
    }

    void ApplyTradeSlotVisualState(VisualElement slotElement, bool selected, bool empty, bool interactive)
    {
        if (slotElement == null)
            return;

        Color border = selected ? SlotBorderSelected : interactive ? SlotBorderDefault : SlotBorderMuted;
        slotElement.style.borderTopColor = border;
        slotElement.style.borderRightColor = border;
        slotElement.style.borderBottomColor = border;
        slotElement.style.borderLeftColor = border;
        slotElement.style.backgroundColor = empty ? SlotBackgroundEmpty : SlotBackgroundDefault;
        slotElement.style.opacity = empty ? 0.55f : interactive ? 1f : 0.6f;
    }

    void RefreshSellDetails()
    {
        if (buyModeActive)
        {
            RefreshBuyDetails();
            return;
        }

        if (sellDetailsIconElement == null || sellDetailsNameLabel == null || sellDetailsCategoryLabel == null ||
            sellDetailsQuantityLabel == null || sellDetailsWeightLabel == null || sellDetailsUnitValueLabel == null ||
            sellDetailsStackValueLabel == null || sellDetailsDescriptionLabel == null || sellStatusLabel == null)
        {
            return;
        }

        bool hasSaleMessage = !sellQuantityPromptActive && !string.IsNullOrWhiteSpace(latestSaleMessage);
        sellStatusLabel.text = latestSaleMessage;
        sellStatusLabel.style.display = hasSaleMessage ? DisplayStyle.Flex : DisplayStyle.None;
        debugLatestSaleMessage = latestSaleMessage;
        debugSelectedSellSlotIndex = selectedSellSlotIndex;

        if (activeInventory == null || selectedSellSlotIndex < 0 || !activeInventory.TryGetSlotSnapshot(selectedSellSlotIndex, out ShipInventoryController.InventorySlotSnapshot slot) || slot.IsEmpty || slot.Item == null)
        {
            sellDetailsIconElement.style.backgroundImage = StyleKeyword.None;
            sellDetailsNameLabel.text = "Empty Slot";
            sellDetailsCategoryLabel.text = string.Empty;
            sellDetailsQuantityLabel.text = string.Empty;
            sellDetailsWeightLabel.text = string.Empty;
            sellDetailsUnitValueLabel.text = string.Empty;
            sellDetailsStackValueLabel.text = string.Empty;
            sellDetailsDescriptionLabel.text = "Choose cargo from the hold to sell it.";
            RefreshTradeDetailsScrollState(false);
            return;
        }

        ItemDefinition item = slot.Item;
        Sprite icon = ResolveItemIcon(item);
        sellDetailsIconElement.style.backgroundImage = icon != null ? new StyleBackground(icon) : StyleKeyword.None;
        sellDetailsNameLabel.text = item.DisplayName;
        sellDetailsCategoryLabel.text = item.Category.ToString();
        sellDetailsQuantityLabel.text = $"Quantity: {slot.Quantity}";
        sellDetailsWeightLabel.text = $"Weight: {(item.Weight * slot.Quantity):0.##} total ({item.Weight:0.##} each)";
        sellDetailsDescriptionLabel.text = GetSellDescriptionText(item);

        if (item.Value <= 0)
        {
            sellDetailsUnitValueLabel.text = "Unit Value: 0 gold";
            sellDetailsStackValueLabel.text = "Cannot be sold";
            RefreshTradeDetailsScrollState(!string.IsNullOrWhiteSpace(sellDetailsDescriptionLabel.text));
            return;
        }

        sellDetailsUnitValueLabel.text = $"Unit Value: {item.Value} gold";
        sellDetailsStackValueLabel.text = $"Stack Total: {item.Value * slot.Quantity} gold";
        RefreshTradeDetailsScrollState(!string.IsNullOrWhiteSpace(sellDetailsDescriptionLabel.text));
    }

    void RefreshBuyDetails()
    {
        if (sellDetailsIconElement == null || sellDetailsNameLabel == null || sellDetailsCategoryLabel == null ||
            sellDetailsQuantityLabel == null || sellDetailsWeightLabel == null || sellDetailsUnitValueLabel == null ||
            sellDetailsStackValueLabel == null || sellDetailsDescriptionLabel == null || sellStatusLabel == null)
        {
            return;
        }

        bool hasPurchaseMessage = !string.IsNullOrWhiteSpace(latestPurchaseMessage);
        sellStatusLabel.text = latestPurchaseMessage;
        sellStatusLabel.style.display = hasPurchaseMessage ? DisplayStyle.Flex : DisplayStyle.None;
        debugSelectedSellSlotIndex = -1;

        if (!TryGetCurrentShopStockEntry(selectedBuyStockIndex, out ShopRuntimeStockEntry entry))
        {
            sellDetailsIconElement.style.backgroundImage = StyleKeyword.None;
            sellDetailsNameLabel.text = "Harbor Stock";
            sellDetailsCategoryLabel.text = string.Empty;
            sellDetailsQuantityLabel.text = string.Empty;
            sellDetailsWeightLabel.text = string.Empty;
            sellDetailsUnitValueLabel.text = string.Empty;
            sellDetailsStackValueLabel.text = string.Empty;
            sellDetailsDescriptionLabel.text = HasAnyShopStockEntries()
                ? HasAnyPurchasableShopStock()
                    ? "Choose supplies from the stock list to stock your hold."
                    : "This shop is sold out."
                : "This dock has nothing for sale.";
            RefreshTradeDetailsScrollState(false);
            return;
        }

        int maxStacks = GetMaxPurchasableChunkCount(selectedBuyStockIndex);
        int previewedStacks = GetClampedBuyPreviewStackCount(maxStacks);
        int perStackQuantity = Mathf.Max(1, entry.purchaseQuantity);
        int previewedQuantity = perStackQuantity * previewedStacks;
        int previewedPrice = Mathf.Max(0, entry.buyPrice) * previewedStacks;
        Sprite icon = ResolveItemIcon(entry.item);
        sellDetailsIconElement.style.backgroundImage = icon != null ? new StyleBackground(icon) : StyleKeyword.None;
        sellDetailsNameLabel.text = entry.item.DisplayName;
        sellDetailsCategoryLabel.text = entry.item.Category.ToString();
        sellDetailsDescriptionLabel.text = GetSellDescriptionText(entry.item);
        sellDetailsQuantityLabel.text = $"Per Stack: {perStackQuantity}\nRemaining: {Mathf.Max(0, entry.remainingChunks)} stack{(Mathf.Max(0, entry.remainingChunks) == 1 ? string.Empty : "s")}";
        float totalWeight = entry.item.Weight * previewedQuantity;
        sellDetailsWeightLabel.text = $"Selected Order: {previewedQuantity} total ({previewedStacks} stack{(previewedStacks == 1 ? string.Empty : "s")})\nWeight: {totalWeight:0.##}";
        sellDetailsUnitValueLabel.text = $"Total Price: {previewedPrice} gold ({Mathf.Max(0, entry.buyPrice)} per stack)";
        sellDetailsStackValueLabel.text = maxStacks > 0
            ? $"Available Today: up to {maxStacks} stack{(maxStacks == 1 ? string.Empty : "s")}"
            : "Sold Out";
        RefreshTradeDetailsScrollState(!string.IsNullOrWhiteSpace(sellDetailsDescriptionLabel.text));
    }

    void RefreshTradeDetailsScrollState(bool allowOverflowScroll)
    {
        if (sellDetailsScrollView == null)
            return;

        sellDetailsScrollView.verticalScrollerVisibility = allowOverflowScroll
            ? ScrollerVisibility.Auto
            : ScrollerVisibility.Hidden;
    }

    void RefreshSellQuantityPrompt()
    {
        if (buyModeActive)
        {
            RefreshBuyActionPanel();
            return;
        }

        debugSellQuantityPromptActive = sellQuantityPromptActive;
        if (!sellModeActive)
            return;

        if (!sellQuantityPromptActive)
        {
            if (sellQuantityContextLabel != null)
                sellQuantityContextLabel.text = selectedSellSlotIndex >= 0
                    ? "Choose how many to sell."
                    : "Select cargo from the grid to prepare a sale.";

            if (sellOneButton != null)
            {
                sellOneButton.text = "Sell 1";
                sellOneButton.SetEnabled(false);
            }
            if (sellHalfButton != null)
            {
                sellHalfButton.text = "Sell Half";
                SetDisplay(sellHalfButton, true);
                sellHalfButton.SetEnabled(false);
            }
            if (sellAllButton != null)
            {
                sellAllButton.text = "Sell All";
                SetDisplay(sellAllButton, true);
                sellAllButton.SetEnabled(false);
            }
            if (sellCustomField != null)
                sellCustomField.SetEnabled(false);
            if (sellCustomButton != null)
            {
                sellCustomButton.text = "Sell X";
                sellCustomButton.SetEnabled(false);
            }
            if (sellCancelButton != null)
                sellCancelButton.SetEnabled(false);
            return;
        }

        if (activeInventory == null
            || quantityPromptSlotIndex < 0
            || !activeInventory.TryGetSlotSnapshot(quantityPromptSlotIndex, out ShipInventoryController.InventorySlotSnapshot slot)
            || slot.IsEmpty
            || slot.Item == null
            || slot.Quantity < 1
            || slot.Item.Value <= 0)
        {
            CloseSellQuantityPrompt();
            return;
        }

        quantityPromptAvailable = slot.Quantity;

        bool isSingleItem = slot.Quantity == 1;
        if (sellQuantityContextLabel != null)
        {
            sellQuantityContextLabel.text = isSingleItem
                ? $"Sell 1 {slot.Item.DisplayName} for {slot.Item.Value} gold?"
                : $"{slot.Item.DisplayName} x{slot.Quantity}\nChoose how many to sell.";
        }

        if (sellOneButton != null)
        {
            sellOneButton.text = isSingleItem ? "Confirm" : "Sell 1";
            sellOneButton.SetEnabled(true);
        }
        if (sellHalfButton != null)
        {
            SetDisplay(sellHalfButton, true);
            sellHalfButton.text = "Sell Half";
            sellHalfButton.SetEnabled(!isSingleItem && slot.Quantity > 1);
        }
        if (sellAllButton != null)
        {
            SetDisplay(sellAllButton, true);
            sellAllButton.text = "Sell All";
            sellAllButton.SetEnabled(true);
        }
        if (sellCustomField != null)
            sellCustomField.SetEnabled(!isSingleItem);
        if (sellCustomButton != null)
            sellCustomButton.SetEnabled(!isSingleItem && TryGetValidCustomSellQuantity(out _));
        if (sellCancelButton != null)
            sellCancelButton.SetEnabled(true);

        RefreshSellQuantityConfirmState();
    }

    void RefreshBuyActionPanel()
    {
        ShopRuntimeStockEntry entry = null;
        bool hasSelection = buyModeActive && TryGetCurrentShopStockEntry(selectedBuyStockIndex, out entry);
        debugSellQuantityPromptActive = false;
        if (!buyModeActive)
            return;

        if (!hasSelection)
        {
            if (buyQuantityContextLabel != null)
                buyQuantityContextLabel.text = selectedBuyStockIndex >= 0
                    ? "This stock entry is unavailable."
                    : "Select dock stock from the grid to choose a purchase.";

            if (buyOneButton != null)
                buyOneButton.SetEnabled(false);
            if (buyTwoButton != null)
                buyTwoButton.SetEnabled(false);
            if (buyMaxButton != null)
                buyMaxButton.SetEnabled(false);
            return;
        }

        int maxStacks = GetMaxPurchasableChunkCount(selectedBuyStockIndex);
        int previewedStacks = GetClampedBuyPreviewStackCount(maxStacks);
        int receiveQuantity = Mathf.Max(1, entry.purchaseQuantity) * previewedStacks;
        int totalPrice = Mathf.Max(0, entry.buyPrice) * previewedStacks;

        if (buyQuantityContextLabel != null)
            buyQuantityContextLabel.text = maxStacks > 0
                ? $"Order Cost: {totalPrice} gold\nYou receive: {receiveQuantity} {entry.item.DisplayName}.\nRemaining: {entry.remainingChunks} stack{(entry.remainingChunks == 1 ? string.Empty : "s")}."
                : $"{entry.item.DisplayName} is sold out.";

        if (buyOneButton != null)
            buyOneButton.SetEnabled(maxStacks >= 1);
        if (buyTwoButton != null)
            buyTwoButton.SetEnabled(maxStacks >= 2);
        if (buyMaxButton != null)
            buyMaxButton.SetEnabled(maxStacks >= 1);
    }

    void RefreshSellQuantityConfirmState()
    {
        if (sellCustomButton == null)
            return;

        sellCustomButton.SetEnabled(sellQuantityPromptActive && TryGetValidCustomSellQuantity(out _));
    }

    void OpenSellQuantityPrompt(int slotIndex, ShipInventoryController.InventorySlotSnapshot slot)
    {
        quantityPromptSlotIndex = slotIndex;
        quantityPromptAvailable = slot.Quantity;
        sellQuantityPromptActive = true;
        debugSellQuantityPromptActive = true;

        if (sellCustomField != null)
        {
            sellCustomField.value = slot.Quantity.ToString();
            sellCustomField.Focus();
            sellCustomField.SelectAll();
        }

        RefreshSellQuantityPrompt();
        RefreshSellDetails();
        RefreshModeVisuals();
    }

    void CloseSellQuantityPrompt()
    {
        sellQuantityPromptActive = false;
        debugSellQuantityPromptActive = false;
        quantityPromptSlotIndex = -1;
        quantityPromptAvailable = 0;
        if (sellCustomField != null)
            sellCustomField.value = string.Empty;
        if (sellOneButton != null)
            sellOneButton.text = "Sell 1";
        if (sellHalfButton != null)
            sellHalfButton.text = "Sell Half";
        if (sellAllButton != null)
            sellAllButton.text = "Sell All";
        RefreshSellDetails();
        RefreshModeVisuals();
    }

    bool TryGetValidCustomSellQuantity(out int quantity)
    {
        quantity = 0;
        if (!sellQuantityPromptActive || sellCustomField == null)
            return false;

        string raw = sellCustomField.value != null ? sellCustomField.value.Trim() : string.Empty;
        if (!int.TryParse(raw, out int parsed))
            return false;

        if (parsed < 1 || parsed > quantityPromptAvailable)
            return false;

        quantity = parsed;
        return true;
    }

    void TryConfirmCustomSellQuantity()
    {
        if (!TryGetValidCustomSellQuantity(out int quantity))
            return;

        ExecuteSale(quantityPromptSlotIndex, quantity);
    }

    int CalculateRepairCost()
    {
        if (boatHealthController == null) return 0;
        float missing = GetMissingHullHealth();
        if (missing <= MeaningfulRepairThreshold)
            return 0;

        return Mathf.CeilToInt(missing * goldPerHullPoint);
    }

    float GetMissingHullHealth()
    {
        if (boatHealthController == null)
            return 0f;

        return Mathf.Max(0f, boatHealthController.MaxHealth - boatHealthController.CurrentHealth);
    }

    bool HasMeaningfulHullDamage()
    {
        return boatHealthController != null && GetMissingHullHealth() > MeaningfulRepairThreshold;
    }

    bool CanRestUntilSunrise()
    {
        return dayNightController != null && dayNightController.CurrentPhase == DayNightPhase.Night;
    }

    void RefreshRestButtonState()
    {
        if (restButton == null)
            return;

        bool canRest = CanRestUntilSunrise();
        restButton.text = canRest ? "Rest until Sunrise" : "Rest (Night only)";
        restButton.SetEnabled(canRest);
    }

    void HandleRepairClicked()
    {
        UIAudioController.ActiveInstance?.PlayButtonClick();
        if (repairConfirmActive)
        {
            ExecuteRepair();
            return;
        }

        if (!IsShopOpen || boatHealthController == null || activeInventory == null) return;
        if (!HasMeaningfulHullDamage()) return;
        OpenRepairConfirm();
    }

    void OpenRepairConfirm()
    {
        ClearStatusMessage();
        repairConfirmActive = true;
        debugRepairConfirmActive = true;
        RefreshModeVisuals();
    }

    void CloseRepairConfirm()
    {
        if (!repairConfirmActive) return;
        repairConfirmActive = false;
        debugRepairConfirmActive = false;
        RefreshModeVisuals();
    }

    void ExecuteRepair()
    {
        if (!repairConfirmActive || boatHealthController == null || activeInventory == null) return;

        int cost = CalculateRepairCost();
        if (cost <= 0)
        {
            CloseRepairConfirm();
            return;
        }

        if (!activeInventory.SpendGold(cost))
        {
            SetStatusMessage($"Not enough gold. Need {cost}g, have {activeInventory.Gold}g.");
            RefreshModeVisuals();
            return;
        }

        float missing = boatHealthController.MaxHealth - boatHealthController.CurrentHealth;
        boatHealthController.Heal(missing);
        SetStatusMessage($"Repaired hull for {cost} gold.");
        CloseRepairConfirm();
        RefreshModeVisuals();
    }

    bool ExecuteSale(int slotIndex, int quantity)
    {
        if (activeInventory == null || slotIndex < 0 || quantity <= 0)
            return false;

        if (!activeInventory.TrySellQuantityAt(slotIndex, quantity, out ItemDefinition item, out int soldQuantity, out int goldEarned))
            return false;

        selectedSellSlotIndex = slotIndex;
        latestSaleMessage = $"Sold {soldQuantity} {item.DisplayName} for {goldEarned} gold.";
        CloseSellQuantityPrompt();
        RefreshSellView();
        return true;
    }

    bool ExecutePurchase(int stockIndex, int chunkCount)
    {
        if (activeInventory == null || !TryGetCurrentShopStockEntry(stockIndex, out ShopRuntimeStockEntry entry))
            return false;

        int sanitizedChunkCount = Mathf.Max(1, chunkCount);
        int maxChunks = GetMaxPurchasableChunkCount(stockIndex);
        if (maxChunks <= 0 || sanitizedChunkCount > maxChunks)
            return false;

        if (!TryGetPurchasePlacement(entry, sanitizedChunkCount, out int quantityToCargo, out int quantityToMainSlot))
            return false;

        int buyPrice = Mathf.Max(0, entry.buyPrice) * sanitizedChunkCount;
        if (!activeInventory.SpendGold(buyPrice))
            return false;

        if (quantityToMainSlot > 0)
            ShipEquipmentController.ActiveEquipment?.TryStorePurchasedItemInMatchingSlot(entry.item, quantityToMainSlot);

        if (quantityToCargo > 0)
            activeInventory.TryAddItem(entry.item, quantityToCargo, out _);

        entry.remainingChunks = Mathf.Max(0, entry.remainingChunks - sanitizedChunkCount);
        int totalQuantity = Mathf.Max(1, entry.purchaseQuantity) * sanitizedChunkCount;
        latestPurchaseMessage = $"Bought {sanitizedChunkCount} stack{(sanitizedChunkCount == 1 ? string.Empty : "s")} ({totalQuantity} {entry.item.DisplayName}) for {buyPrice} gold.";
        RefreshSellView();
        return true;
    }

    bool CanPurchaseStockEntry(int stockIndex)
    {
        if (activeInventory == null || !TryGetCurrentShopStockEntry(stockIndex, out ShopRuntimeStockEntry entry))
            return false;

        if (entry.remainingChunks <= 0)
            return false;

        return GetMaxPurchasableChunkCount(stockIndex) >= 1;
    }

    bool TryGetPurchasePlacement(ShopRuntimeStockEntry entry, int chunkCount, out int quantityToCargo, out int quantityToMainSlot)
    {
        quantityToCargo = 0;
        quantityToMainSlot = 0;
        if (entry == null || entry.item == null || activeInventory == null)
            return false;

        int totalQuantity = Mathf.Max(1, entry.purchaseQuantity) * Mathf.Max(1, chunkCount);
        ShipEquipmentController equipment = ShipEquipmentController.ActiveEquipment;
        if (entry.item.AmmoDefinition != null && equipment != null)
        {
            quantityToMainSlot = Mathf.Min(totalQuantity, equipment.GetMatchingSlotAvailableCapacity(entry.item));
        }

        quantityToCargo = totalQuantity - quantityToMainSlot;
        if (quantityToCargo > 0 && !activeInventory.CanFullyAddItem(entry.item, quantityToCargo))
            return false;

        float reservedWeight = equipment != null ? equipment.CurrentWeight : 0f;
        float totalProjectedWeight = activeInventory.CurrentWeight + reservedWeight + (entry.item.Weight * totalQuantity);
        return totalProjectedWeight <= activeInventory.MaxCarryWeight + 0.0001f;
    }

    int GetMaxPurchasableChunkCount(int stockIndex)
    {
        if (activeInventory == null || !TryGetCurrentShopStockEntry(stockIndex, out ShopRuntimeStockEntry entry))
            return 0;

        if (entry.remainingChunks <= 0)
            return 0;

        int singlePrice = Mathf.Max(0, entry.buyPrice);
        int affordabilityCap = singlePrice > 0 ? activeInventory.Gold / singlePrice : entry.remainingChunks;
        affordabilityCap = Mathf.Min(affordabilityCap, entry.remainingChunks);
        if (affordabilityCap <= 0)
            return 0;

        int maxChunks = 0;
        for (int chunkCount = 1; chunkCount <= affordabilityCap; chunkCount++)
        {
            if (!TryGetPurchasePlacement(entry, chunkCount, out _, out _))
                break;

            maxChunks = chunkCount;
        }

        return maxChunks;
    }

    int GetClampedBuyPreviewStackCount(int maxStacks)
    {
        if (maxStacks <= 0)
            return 0;

        return Mathf.Clamp(currentBuyPreviewStackCount, 1, maxStacks);
    }

    void PreviewBuyStackOption(int desiredStackCount)
    {
        if (!buyModeActive || selectedBuyStockIndex < 0)
            return;

        int maxStacks = GetMaxPurchasableChunkCount(selectedBuyStockIndex);
        if (desiredStackCount > maxStacks && maxStacks > 0)
            return;

        currentBuyPreviewStackCount = Mathf.Max(1, desiredStackCount);
        RefreshSellDetails();
        RefreshSellQuantityPrompt();
    }

    void PreviewBuyMaxOption()
    {
        if (!buyModeActive || selectedBuyStockIndex < 0)
            return;

        int maxStacks = GetMaxPurchasableChunkCount(selectedBuyStockIndex);
        currentBuyPreviewStackCount = Mathf.Max(0, maxStacks);
        RefreshSellDetails();
        RefreshSellQuantityPrompt();
    }

    void ResetBuyPreviewToDefault()
    {
        if (!buyModeActive || selectedBuyStockIndex < 0)
            return;

        if (currentBuyPreviewStackCount == 1)
            return;

        currentBuyPreviewStackCount = 1;
        RefreshSellDetails();
        RefreshSellQuantityPrompt();
    }

    void TryExecutePreviewedPurchase()
    {
        if (!buyModeActive || selectedBuyStockIndex < 0)
            return;

        int maxStacks = GetMaxPurchasableChunkCount(selectedBuyStockIndex);
        int previewedStacks = GetClampedBuyPreviewStackCount(maxStacks);
        ExecutePurchase(selectedBuyStockIndex, previewedStacks);
    }

    Sprite ResolveItemIcon(ItemDefinition item)
    {
        if (item != null && item.IconSprite != null)
            return item.IconSprite;

        ResolveFallbackNullItemIcon();
        return fallbackNullItemIcon;
    }

    void SetStatusMessage(string message)
    {
        statusMessage = message ?? string.Empty;
    }

    void ClearStatusMessage()
    {
        statusMessage = string.Empty;
    }

    static void SetDisplay(VisualElement element, bool show)
    {
        if (element == null)
            return;

        element.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }

    static string[] FilterDialogueLines(string[] lines)
    {
        if (lines == null || lines.Length == 0)
            return Array.Empty<string>();

        int count = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
                count++;
        }

        if (count == 0)
            return Array.Empty<string>();

        string[] filtered = new string[count];
        int writeIndex = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            filtered[writeIndex++] = lines[i];
        }

        return filtered;
    }

    void ApplySellLayoutTier()
    {
        if (!uiReady)
            return;

        SellLayoutTier tier = ResolveSellLayoutTier();
        currentSellLayoutTier = tier;

        ApplySummaryLayoutTier(tier);
        ApplySellMainLayoutTier(tier);
        ApplySellGridLayoutTier(tier);
        ApplySellDetailsLayoutTier(tier);
    }

    SellLayoutTier ResolveSellLayoutTier()
    {
        if (!sellModeActive && !buyModeActive)
            return SellLayoutTier.WideSidebar;

        return CanUseWideSidebarLayout()
            ? SellLayoutTier.WideSidebar
            : SellLayoutTier.CompactStacked;
    }

    bool CanUseWideSidebarLayout()
    {
        float screenHeight = Screen.height > 0 ? Screen.height : 720f;
        float panelWidth = CalculateTargetPanelWidth();
        float panelHeight = Mathf.Max(320f, screenHeight * 0.992f);

        // Fail safe: if the right-hand card is anywhere near borderline, prefer
        // the stacked layout rather than risking header overlap or footer spill.
        float availableSidebarLaneWidth = panelWidth - WideSidebarMinimumGridWidth - 24f;
        float requiredSidebarLaneWidth = WideSidebarMinimumLaneWidth + WideSidebarWidthComfortMargin;

        // Approximate the panel space lost to the title, summary row, footer hint,
        // and panel padding before the right card can even begin laying out.
        float availableSidebarCardHeight = panelHeight - 240f;
        float requiredSidebarCardHeight = WideSidebarMinimumCardHeight + WideSidebarHeightComfortMargin;

        return availableSidebarLaneWidth >= requiredSidebarLaneWidth
            && availableSidebarCardHeight >= requiredSidebarCardHeight;
    }

    float CalculateTargetPanelWidth()
    {
        float screenWidth = Screen.width > 0 ? Screen.width : 1280f;
        bool tradeMode = sellModeActive || buyModeActive;
        float widthFraction = tradeMode ? SellOverlayWidthFraction : OverlayWidthFraction;
        float targetWidth = tradeMode ? SellPanelWidth : DefaultPanelWidth;
        return Mathf.Max(
            MinimumResponsivePanelWidth,
            Mathf.Min(targetWidth, screenWidth * widthFraction));
    }

    void ApplySummaryLayoutTier(SellLayoutTier tier)
    {
        if (sellSummaryRowElement == null)
            return;

        bool wide = tier == SellLayoutTier.WideSidebar;
        float statMinWidth = wide ? 124f : 92f;
        float statRightMargin = wide ? 22f : 12f;
        float statBottomMargin = wide ? 0f : 6f;
        float labelFont = wide ? 13f : 11f;
        float valueFont = wide ? 19f : 15f;

        sellSummaryRowElement.style.flexDirection = FlexDirection.Row;
        sellSummaryRowElement.style.flexWrap = Wrap.Wrap;
        sellSummaryRowElement.style.marginBottom = wide ? 12f : 10f;

        ApplySummaryBlockStyles(sellGoldBlockElement, statMinWidth, statRightMargin, statBottomMargin, labelFont, valueFont);
        ApplySummaryBlockStyles(sellSlotsBlockElement, statMinWidth, statRightMargin, statBottomMargin, labelFont, valueFont);
        ApplySummaryBlockStyles(sellWeightBlockElement, wide ? 144f : 100f, 0f, 0f, labelFont, valueFont);
    }

    void ApplySummaryBlockStyles(VisualElement block, float minWidth, float rightMargin, float bottomMargin, float labelFont, float valueFont)
    {
        if (block == null)
            return;

        block.style.minWidth = minWidth;
        block.style.marginRight = rightMargin;
        block.style.marginBottom = bottomMargin;

        if (block.childCount < 2)
            return;

        if (block[0] is Label label)
            label.style.fontSize = labelFont;
        if (block[1] is Label value)
            value.style.fontSize = valueFont;
    }

    void ApplySellMainLayoutTier(SellLayoutTier tier)
    {
        if (sellMainRowElement == null || sellGridSectionElement == null || sellDetailsPanelElement == null)
            return;

        bool wide = tier == SellLayoutTier.WideSidebar;
        float screenHeight = Screen.height > 0 ? Screen.height : 720f;
        sellMainRowElement.style.flexDirection = wide ? FlexDirection.Row : FlexDirection.Column;
        sellMainRowElement.style.flexGrow = 1f;
        sellMainRowElement.style.minHeight = 0f;
        sellMainRowElement.style.alignItems = Align.Stretch;

        sellGridSectionElement.style.flexGrow = wide ? 1f : 0f;
        sellGridSectionElement.style.flexShrink = 1f;
        sellGridSectionElement.style.minWidth = 0f;
        sellGridSectionElement.style.marginRight = wide ? 24f : 0f;
        sellGridSectionElement.style.marginBottom = wide ? 0f : 12f;
        sellGridSectionElement.style.minHeight = wide ? 220f : 92f;
        sellGridSectionElement.style.maxHeight = wide ? StyleKeyword.None : Mathf.Max(108f, screenHeight * 0.16f);

        sellDetailsPanelElement.style.alignSelf = wide ? Align.Auto : Align.Stretch;
        sellDetailsPanelElement.style.flexShrink = 0f;
        sellDetailsPanelElement.style.flexGrow = wide ? 0f : 1f;
    }

    void ApplySellGridLayoutTier(SellLayoutTier tier)
    {
        if (sellGridElement == null)
            return;

        bool wide = tier == SellLayoutTier.WideSidebar;
        float slotSize = wide ? 84f : 62f;
        float slotGap = wide ? 10f : 6f;
        float iconSize = wide ? 56f : 40f;
        float quantityFont = wide ? 15f : 12f;
        float quantityInset = wide ? 4f : 3f;

        for (int i = 0; i < sellSlotElements.Count; i++)
        {
            VisualElement slot = sellSlotElements[i];
            VisualElement icon = sellSlotIconElements[i];
            Label quantityLabel = sellSlotQuantityLabels[i];

            slot.style.width = slotSize;
            slot.style.height = slotSize;
            slot.style.marginRight = slotGap;
            slot.style.marginBottom = slotGap;

            icon.style.width = iconSize;
            icon.style.height = iconSize;

            quantityLabel.style.right = quantityInset;
            quantityLabel.style.bottom = quantityInset;
            quantityLabel.style.fontSize = quantityFont;
            quantityLabel.style.minWidth = wide ? 18f : 16f;
            quantityLabel.style.paddingLeft = wide ? 4f : 3f;
            quantityLabel.style.paddingRight = wide ? 4f : 3f;
        }
    }

    void ApplySellDetailsLayoutTier(SellLayoutTier tier)
    {
        if (sellDetailsPanelElement == null || sellDetailsContentElement == null || sellDetailsIconElement == null ||
            sellDetailsNameLabel == null || sellDetailsCategoryLabel == null || sellDetailsQuantityLabel == null ||
            sellDetailsWeightLabel == null || sellDetailsUnitValueLabel == null || sellDetailsStackValueLabel == null ||
            sellDetailsDescriptionLabel == null || sellStatusLabel == null || sellQuantityContextLabel == null ||
            buyQuantityContextLabel == null || buyActionsElement == null || tradeFooterElement == null ||
            buyOneButton == null || buyTwoButton == null || buyMaxButton == null || sellBackButton == null)
        {
            return;
        }

        bool wide = tier == SellLayoutTier.WideSidebar;
        bool relaxedTradeDetails = buyModeActive || sellModeActive;
        float screenHeight = Screen.height > 0 ? Screen.height : 720f;
        bool compactShort = !wide && screenHeight < 760f;
        bool compactSell = !wide && sellModeActive;
        float panelPadding = wide ? 12f : 10f;
        float compactBodyFloor = compactShort ? 92f : 116f;
        float compactFooterFloor = compactShort
            ? (sellModeActive ? 156f : 102f)
            : (sellModeActive ? 176f : 116f);
        float compactHeaderFloor = compactShort ? 78f : 94f;
        float compactPanelFloor = compactHeaderFloor + compactBodyFloor + compactFooterFloor + (panelPadding * 2f);
        float panelMinHeight = relaxedTradeDetails
            ? (wide ? 0f : compactPanelFloor)
            : wide ? 440f : compactPanelFloor;
        float panelMaxHeight = relaxedTradeDetails
            ? (wide ? 9999f : Mathf.Max(compactPanelFloor, screenHeight * 0.56f))
            : wide ? 600f : 9999f;
        float iconSize = wide ? 68f : 52f;
        float nameFont = wide ? 20f : 18f;
        float categoryFont = wide ? 12f : 11f;
        float detailFont = wide ? 12f : 11f;
        float descriptionFont = wide ? 13f : 12f;
        float stackFont = wide ? 13f : 12f;
        float quantityHeadingFont = wide ? 14f : 13f;
        float contextFont = wide ? 12f : 11f;
        Length actionColumnWidth = new Length(wide ? 52f : 50f, LengthUnit.Percent);

        if (wide)
        {
            sellDetailsPanelElement.style.width = 540f;
            sellDetailsPanelElement.style.minWidth = 460f;
            sellDetailsPanelElement.style.maxWidth = 640f;
        }
        else
        {
            sellDetailsPanelElement.style.width = StyleKeyword.Auto;
            sellDetailsPanelElement.style.minWidth = 0f;
            sellDetailsPanelElement.style.maxWidth = StyleKeyword.None;
        }

        sellDetailsPanelElement.style.minHeight = panelMinHeight;
        sellDetailsPanelElement.style.maxHeight = panelMaxHeight;
        sellDetailsPanelElement.style.paddingLeft = panelPadding;
        sellDetailsPanelElement.style.paddingRight = panelPadding;
        sellDetailsPanelElement.style.paddingTop = panelPadding;
        sellDetailsPanelElement.style.paddingBottom = panelPadding;

        sellDetailsContentElement.style.flexGrow = 1f;
        sellDetailsContentElement.style.flexShrink = 1f;
        sellDetailsContentElement.style.minHeight = relaxedTradeDetails
            ? (wide ? 144f : compactBodyFloor)
            : wide ? 220f : 0f;
        tradeFooterElement.style.flexShrink = 0f;
        tradeFooterElement.style.minHeight = wide ? 112f : compactFooterFloor;
        tradeFooterElement.style.alignItems = Align.Stretch;

        sellDetailsIconElement.style.width = iconSize;
        sellDetailsIconElement.style.height = iconSize;
        sellDetailsIconElement.style.marginBottom = wide ? 4f : 4f;

        sellDetailsNameLabel.style.fontSize = nameFont;
        sellDetailsNameLabel.style.marginBottom = wide ? 6f : 6f;
        sellDetailsCategoryLabel.style.fontSize = categoryFont;
        sellDetailsCategoryLabel.style.marginBottom = wide ? 6f : 6f;
        sellDetailsQuantityLabel.style.fontSize = detailFont;
        sellDetailsWeightLabel.style.fontSize = detailFont;
        sellDetailsUnitValueLabel.style.fontSize = detailFont;
        sellDetailsStackValueLabel.style.fontSize = stackFont;
        sellDetailsDescriptionLabel.style.fontSize = descriptionFont;
        sellDetailsDescriptionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        sellStatusLabel.style.fontSize = detailFont;
        buyQuantityContextLabel.style.fontSize = contextFont;
        sellQuantityContextLabel.style.fontSize = contextFont;
        sellDetailsDescriptionLabel.style.marginBottom = sellQuantityPromptActive ? 12f : 14f;

        if (buyActionsElement.childCount > 0 && buyActionsElement[0] is Label buyHeadingLabel)
            buyHeadingLabel.style.fontSize = quantityHeadingFont;
        if (sellQuantityPanelElement != null && sellQuantityPanelElement.childCount > 0 && sellQuantityPanelElement[0] is Label quantityHeadingLabel)
            quantityHeadingLabel.style.fontSize = quantityHeadingFont;
        if (buyActionsElement.childCount > 0 && buyActionsElement[0] is VisualElement buyHeadingElement)
            buyHeadingElement.style.marginBottom = wide ? 8f : compactShort ? 6f : 8f;
        if (sellQuantityPanelElement != null && sellQuantityPanelElement.childCount > 0 && sellQuantityPanelElement[0] is VisualElement sellHeadingElement)
            sellHeadingElement.style.marginBottom = wide ? 8f : compactShort ? 5f : 7f;
        if (buyQuantityContextLabel != null)
            buyQuantityContextLabel.style.marginBottom = wide ? 10f : compactShort ? 6f : 8f;
        if (sellQuantityContextLabel != null)
            sellQuantityContextLabel.style.marginBottom = wide ? 10f : compactShort ? 5f : 7f;

        if (buyActionsElement != null)
        {
            buyActionsElement.style.marginBottom = 0f;
            buyActionsElement.style.alignItems = Align.Stretch;
        }
        if (sellQuantityPanelElement != null)
        {
            sellQuantityPanelElement.style.marginBottom = 0f;
            sellQuantityPanelElement.style.alignItems = Align.Stretch;
        }

        float actionButtonHeight = wide ? 30f : compactShort ? 27f : 30f;
        if (buyOneButton != null)
        {
            buyOneButton.style.height = actionButtonHeight;
            buyOneButton.style.marginBottom = wide ? 8f : compactShort ? 5f : 7f;
            buyOneButton.style.width = actionColumnWidth;
            buyOneButton.style.alignSelf = Align.FlexEnd;
        }
        if (buyTwoButton != null)
        {
            buyTwoButton.style.height = actionButtonHeight;
            buyTwoButton.style.marginBottom = wide ? 8f : compactShort ? 5f : 7f;
            buyTwoButton.style.width = actionColumnWidth;
            buyTwoButton.style.alignSelf = Align.FlexEnd;
        }
        if (buyMaxButton != null)
        {
            buyMaxButton.style.height = actionButtonHeight;
            buyMaxButton.style.marginBottom = wide ? 8f : compactShort ? 5f : 7f;
            buyMaxButton.style.width = actionColumnWidth;
            buyMaxButton.style.alignSelf = Align.FlexEnd;
        }
        if (sellOneButton != null)
        {
            sellOneButton.style.height = actionButtonHeight;
            sellOneButton.style.marginBottom = wide ? 8f : compactShort ? 5f : 7f;
            sellOneButton.style.width = actionColumnWidth;
            sellOneButton.style.alignSelf = Align.FlexEnd;
        }
        if (sellHalfButton != null)
        {
            sellHalfButton.style.height = actionButtonHeight;
            sellHalfButton.style.marginBottom = wide ? 8f : compactShort ? 4f : 6f;
            sellHalfButton.style.width = actionColumnWidth;
            sellHalfButton.style.alignSelf = Align.FlexEnd;
        }
        if (sellAllButton != null)
        {
            sellAllButton.style.height = actionButtonHeight;
            sellAllButton.style.marginBottom = wide ? 8f : compactShort ? 4f : 6f;
            sellAllButton.style.width = actionColumnWidth;
            sellAllButton.style.alignSelf = Align.FlexEnd;
        }
        if (sellCustomButton != null)
        {
            sellCustomButton.style.height = actionButtonHeight;
            sellCustomButton.style.width = wide ? 82f : compactShort ? 70f : 74f;
            sellCustomButton.style.minWidth = wide ? 82f : compactShort ? 70f : 74f;
        }
        if (sellCancelButton != null)
        {
            sellCancelButton.style.height = actionButtonHeight;
            sellCancelButton.style.marginTop = compactShort ? 0f : 0f;
            sellCancelButton.style.width = actionColumnWidth;
            sellCancelButton.style.alignSelf = Align.FlexEnd;
        }
        if (sellCustomField != null)
            sellCustomField.style.marginRight = wide ? 8f : compactShort ? 6f : 8f;
        if (sellQuantityPanelElement != null)
            sellQuantityPanelElement.style.marginBottom = 0f;
        VisualElement sellCustomRowElement = sellCustomField?.parent;
        if (sellCustomRowElement != null)
        {
            sellCustomRowElement.style.width = actionColumnWidth;
            sellCustomRowElement.style.alignSelf = Align.FlexEnd;
            sellCustomRowElement.style.marginBottom = compactSell ? 6f : 8f;
        }

        sellBackButton.style.height = wide ? 32f : compactShort ? 29f : 32f;
        sellBackButton.style.marginTop = wide ? 6f : compactSell ? 4f : 6f;
        sellBackButton.style.width = actionColumnWidth;
        sellBackButton.style.alignSelf = Align.FlexEnd;
    }

    string GetSellDescriptionText(ItemDefinition item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Description))
            return string.Empty;

        string description = item.Description.Replace('\n', ' ').Replace('\r', ' ').Trim();
        SellLayoutTier tier = (sellModeActive || buyModeActive) ? ResolveSellLayoutTier() : currentSellLayoutTier;
        int maxLength = tier == SellLayoutTier.WideSidebar ? 150 : 120;
        if (description.Length <= maxLength)
            return description;

        int truncateIndex = description.LastIndexOf(' ', Mathf.Min(description.Length - 1, maxLength));
        if (truncateIndex < maxLength * 0.6f)
            truncateIndex = maxLength;

        return description.Substring(0, truncateIndex).TrimEnd() + "...";
    }

    void EnsureDefaultShopStock()
    {
        if (sharedShopStock != null && sharedShopStock.Count > 0)
            return;

        sharedShopStock ??= new List<ShopStockEntry>();
        sharedShopStock.Clear();

        ItemDefinition breadItem = Resources.Load<ItemDefinition>("BreadItem");
        ItemDefinition goldenCodItem = Resources.Load<ItemDefinition>("GoldenCodItem");
        ItemDefinition cannonballItem = Resources.Load<ItemDefinition>("CannonballItem");
        ItemDefinition musketBallItem = Resources.Load<ItemDefinition>("MusketBallItem");

        TryAddDefaultStockEntry(breadItem, 12, 1);
        TryAddDefaultStockEntry(goldenCodItem, 28, 1);
        TryAddDefaultStockEntry(cannonballItem, 60, 10);
        TryAddDefaultStockEntry(musketBallItem, 45, 20);
    }

    void TryAddDefaultStockEntry(ItemDefinition item, int buyPrice, int purchaseQuantity)
    {
        if (item == null)
            return;

        sharedShopStock.Add(new ShopStockEntry
        {
            item = item,
            buyPrice = Mathf.Max(0, buyPrice),
            purchaseQuantity = Mathf.Max(1, purchaseQuantity)
        });
    }

    void OnValidate()
    {
        goldPerHullPoint = Mathf.Max(0.1f, goldPerHullPoint);
        minEntriesPerShop = Mathf.Max(1, minEntriesPerShop);
        maxEntriesPerShop = Mathf.Max(minEntriesPerShop, maxEntriesPerShop);
        minChunksPerEntry = Mathf.Max(1, minChunksPerEntry);
        maxChunksPerEntry = Mathf.Max(minChunksPerEntry, maxChunksPerEntry);
    }

    TreasureHuntController.ShopTalkResult BuildFallbackTalkResult()
    {
        string line = string.IsNullOrWhiteSpace(fallbackTalkLine)
            ? "The dockmaster has nothing new to share right now."
            : fallbackTalkLine.Trim();

        return new TreasureHuntController.ShopTalkResult(
            new[] { line },
            string.Empty,
            false,
            false);
    }
}
