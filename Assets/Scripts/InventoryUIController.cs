using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// Drives the dominant inventory overlay UI.
//
// The inventory truth lives on ShipInventoryController. This controller only:
//   - opens/closes the overlay
//   - manages cursor state
//   - renders slot data, summary data, and selected item details
//   - handles whole-stack click-to-move merge/swap behavior
public class InventoryUIController : MonoBehaviour
{
    enum DragSourceKind
    {
        None,
        Cargo,
        Equipment
    }

    enum InventoryInteractionState
    {
        Idle,
        CarryingWholeStack,
        SplitMenuOpen,
        CarryingSplitStack
    }

    public static bool IsInventoryOpen { get; private set; }
    public static InventoryUIController ActiveInstance { get; private set; }
    public Sprite FallbackNullItemIcon => fallbackNullItemIcon;

    [Header("References")]
    [SerializeField] UIDocument uiDocument;
    [SerializeField] Sprite fallbackNullItemIcon;
    [SerializeField] HungerController hungerController;

    [Header("Element Names")]
    [SerializeField] string overlayElementName = "inventory-overlay";
    [SerializeField] string gridElementName = "inventory-grid";
    [SerializeField] string equipmentStripElementName = "inventory-equipment-strip";
    [SerializeField] string currencyValueElementName = "inventory-currency-value";
    [SerializeField] string slotSummaryElementName = "inventory-slot-summary";
    [SerializeField] string weightSummaryElementName = "inventory-weight-summary";
    [SerializeField] string detailsIconElementName = "inventory-details-icon";
    [SerializeField] string detailsNameElementName = "inventory-details-name";
    [SerializeField] string detailsCategoryElementName = "inventory-details-category";
    [SerializeField] string detailsQuantityElementName = "inventory-details-quantity";
    [SerializeField] string detailsWeightElementName = "inventory-details-weight";
    [SerializeField] string detailsValueElementName = "inventory-details-value";
    [SerializeField] string detailsDescriptionElementName = "inventory-details-description";

    [Header("Layout")]
    [SerializeField] int fixedColumnCount = 5;
    [SerializeField] Vector2 splitPanelOffset = new(0f, 6f);

    VisualElement rootElement;
    VisualElement overlayElement;
    VisualElement gridElement;
    VisualElement equipmentStripElement;
    VisualElement detailsIconElement;
    VisualElement dragGhostElement;
    VisualElement dragGhostIconElement;
    VisualElement splitPanelElement;
    VisualElement sortPanelElement;
    Label currencyValueLabel;
    Label slotSummaryLabel;
    Label weightSummaryLabel;
    Label detailsNameLabel;
    Label detailsCategoryLabel;
    Label detailsQuantityLabel;
    Label detailsWeightLabel;
    Label detailsValueLabel;
    Label detailsDescriptionLabel;
    Button detailsConsumeButton;
    Label splitContextLabel;
    Button splitOneButton;
    Button splitHalfButton;
    Button splitCustomButton;
    TextField splitCustomField;
    Button sortDirectionButton;
    Button sortDefaultButton;
    Button sortNameButton;
    Button sortCategoryButton;
    Button sortWeightButton;
    Button sortValueButton;
    Button sortQuantityButton;

    readonly List<VisualElement> slotElements = new();
    readonly List<VisualElement> slotIconElements = new();
    readonly List<VisualElement> slotGhostIconElements = new();
    readonly List<Label> slotQuantityLabels = new();
    readonly List<VisualElement> equipmentSlotElements = new();
    readonly List<VisualElement> equipmentSlotIconElements = new();
    readonly List<VisualElement> equipmentSlotGhostIconElements = new();
    readonly List<Label> equipmentSlotLabels = new();
    readonly List<Label> equipmentSlotQuantityLabels = new();

    ShipInventoryController activeInventory;
    ShipEquipmentController activeEquipment;
    bool overlayInitialized;
    bool warnedMissingUi;
    bool warnedMissingInventory;
    bool uiCallbacksRegistered;
    bool ignoreNextOverlaySplitClose;
    bool hasLocalPointerPosition;
    int hoveredSlotIndex = -1;
    int hoveredEquipmentSlotIndex = -1;
    int selectedSlotIndex = -1;
    int selectedEquipmentSlotIndex = -1;
    int carriedSlotIndex = -1;
    int splitSourceSlotIndex = -1;
    int carriedSplitOriginSlotIndex = -1;
    ItemDefinition carriedSplitItem;
    int carriedSplitQuantity;
    int carriedEquipmentOriginSlotIndex = -1;
    ItemDefinition carriedEquipmentItem;
    int carriedEquipmentQuantity;
    bool sortAscending;
    Vector2 localPointerPosition;
    CursorLockMode previousCursorLockMode;
    bool previousCursorVisible;
    bool suppressNextClickAction;
    DragSourceKind pendingDragSourceKind;
    int pendingDragSourceIndex = -1;
    Vector2 pendingDragStartPosition;
    [SerializeField] float dragStartThresholdPixels = 10f;

    void OnEnable()
    {
        ActiveInstance = this;
        TryInitialize();
        ShipInventoryController.OnActiveInventoryRegistered += HandleActiveInventoryRegistered;
        ShipEquipmentController.OnActiveEquipmentRegistered += HandleActiveEquipmentRegistered;
        HandleActiveInventoryRegistered(ShipInventoryController.ActiveInventory);
        HandleActiveEquipmentRegistered(ShipEquipmentController.ActiveEquipment);
    }

    void Start()
    {
        TryInitialize();
        HandleActiveInventoryRegistered(ShipInventoryController.ActiveInventory);
        SetOverlayOpen(false);
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
        ShipEquipmentController.OnActiveEquipmentRegistered -= HandleActiveEquipmentRegistered;
        UnsubscribeFromInventory();
        UnsubscribeFromEquipment();
        UnregisterButtonCallbacks();
        ReturnCarriedSplitToOrigin();
        ReturnCarriedEquipmentToOrigin();
        if (IsInventoryOpen)
            RestoreCursorState();

        IsInventoryOpen = false;
        overlayInitialized = false;
        UnregisterUiCallbacks();
    }

    void TryInitialize()
    {
        if (uiDocument == null)
            uiDocument = FindAnyObjectByType<UIDocument>();

        if (uiDocument == null)
        {
            WarnMissingUiOnce();
            return;
        }

        if (overlayInitialized)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        rootElement = root;
        overlayElement = root.Q(overlayElementName);
        gridElement = root.Q(gridElementName);
        currencyValueLabel = root.Q<Label>(currencyValueElementName);
        equipmentStripElement = root.Q(equipmentStripElementName);
        slotSummaryLabel = root.Q<Label>(slotSummaryElementName);
        weightSummaryLabel = root.Q<Label>(weightSummaryElementName);
        detailsIconElement = root.Q(detailsIconElementName);
        detailsNameLabel = root.Q<Label>(detailsNameElementName);
        detailsCategoryLabel = root.Q<Label>(detailsCategoryElementName);
        detailsQuantityLabel = root.Q<Label>(detailsQuantityElementName);
        detailsWeightLabel = root.Q<Label>(detailsWeightElementName);
        detailsValueLabel = root.Q<Label>(detailsValueElementName);
        detailsDescriptionLabel = root.Q<Label>(detailsDescriptionElementName);
        detailsConsumeButton = root.Q<Button>("inventory-details-consume-button");
        dragGhostElement = root.Q("inventory-drag-ghost");
        dragGhostIconElement = root.Q("inventory-drag-ghost-icon");
        splitPanelElement = root.Q("inventory-split-panel");
        splitContextLabel = root.Q<Label>("inventory-split-context");
        splitOneButton = root.Q<Button>("inventory-split-one-button");
        splitHalfButton = root.Q<Button>("inventory-split-half-button");
        splitCustomButton = root.Q<Button>("inventory-split-custom-button");
        splitCustomField = root.Q<TextField>("inventory-split-custom-field");
        sortPanelElement = root.Q("inventory-sort-panel");
        sortDirectionButton = root.Q<Button>("inventory-sort-direction-button");
        sortDefaultButton = root.Q<Button>("inventory-sort-default-button");
        sortNameButton = root.Q<Button>("inventory-sort-name-button");
        sortCategoryButton = root.Q<Button>("inventory-sort-category-button");
        sortWeightButton = root.Q<Button>("inventory-sort-weight-button");
        sortValueButton = root.Q<Button>("inventory-sort-value-button");
        sortQuantityButton = root.Q<Button>("inventory-sort-quantity-button");

        if (overlayElement == null || gridElement == null || equipmentStripElement == null || currencyValueLabel == null || slotSummaryLabel == null ||
            weightSummaryLabel == null || detailsIconElement == null || detailsNameLabel == null ||
            detailsCategoryLabel == null || detailsQuantityLabel == null || detailsWeightLabel == null ||
            detailsValueLabel == null || detailsDescriptionLabel == null || detailsConsumeButton == null ||
            dragGhostElement == null || dragGhostIconElement == null || splitPanelElement == null ||
            splitContextLabel == null || splitOneButton == null || splitHalfButton == null ||
            splitCustomButton == null || splitCustomField == null || sortPanelElement == null ||
            sortDirectionButton == null || sortDefaultButton == null || sortNameButton == null ||
            sortCategoryButton == null || sortWeightButton == null || sortValueButton == null ||
            sortQuantityButton == null)
        {
            WarnMissingUiOnce();
            return;
        }

        detailsDescriptionLabel.style.whiteSpace = WhiteSpace.Normal;
        if (hungerController == null)
            hungerController = FindAnyObjectByType<HungerController>();
        splitContextLabel.style.whiteSpace = WhiteSpace.Normal;
        dragGhostElement.pickingMode = PickingMode.Ignore;
        dragGhostIconElement.pickingMode = PickingMode.Ignore;
        splitPanelElement.pickingMode = PickingMode.Position;
        splitCustomField.label = string.Empty;
        splitCustomField.value = "1";
        RegisterButtonCallbacks();
        RegisterUiCallbacks();
        overlayInitialized = true;
        BuildSlotGrid();
        BuildEquipmentSlots();
        UpdateSelectedDetails();
        RefreshVisualState();
    }

    void RegisterUiCallbacks()
    {
        if (uiCallbacksRegistered || overlayElement == null)
            return;

        overlayElement.RegisterCallback<PointerMoveEvent>(HandleOverlayPointerMove);
        overlayElement.RegisterCallback<PointerDownEvent>(HandleOverlayPointerDown);
        overlayElement.RegisterCallback<PointerUpEvent>(HandleOverlayPointerUp);
        overlayElement.RegisterCallback<GeometryChangedEvent>(HandleOverlayGeometryChanged);
        uiCallbacksRegistered = true;
    }

    void UnregisterUiCallbacks()
    {
        if (!uiCallbacksRegistered || overlayElement == null)
            return;

        overlayElement.UnregisterCallback<PointerMoveEvent>(HandleOverlayPointerMove);
        overlayElement.UnregisterCallback<PointerDownEvent>(HandleOverlayPointerDown);
        overlayElement.UnregisterCallback<PointerUpEvent>(HandleOverlayPointerUp);
        overlayElement.UnregisterCallback<GeometryChangedEvent>(HandleOverlayGeometryChanged);
        uiCallbacksRegistered = false;
    }

    void RegisterButtonCallbacks()
    {
        if (detailsConsumeButton == null || splitOneButton == null || splitHalfButton == null || splitCustomButton == null ||
            sortDirectionButton == null || sortDefaultButton == null || sortNameButton == null ||
            sortCategoryButton == null || sortWeightButton == null || sortValueButton == null ||
            sortQuantityButton == null)
            return;

        detailsConsumeButton.clicked += HandleConsumeButtonClicked;
        splitOneButton.clicked += HandleSplitOneClicked;
        splitHalfButton.clicked += HandleSplitHalfClicked;
        splitCustomButton.clicked += HandleSplitCustomClicked;
        sortDirectionButton.clicked += ToggleSortDirection;
        sortDefaultButton.clicked += HandleSortDefaultClicked;
        sortNameButton.clicked += HandleSortNameClicked;
        sortCategoryButton.clicked += HandleSortCategoryClicked;
        sortWeightButton.clicked += HandleSortWeightClicked;
        sortValueButton.clicked += HandleSortValueClicked;
        sortQuantityButton.clicked += HandleSortQuantityClicked;
    }

    void UnregisterButtonCallbacks()
    {
        if (detailsConsumeButton == null || splitOneButton == null || splitHalfButton == null || splitCustomButton == null ||
            sortDirectionButton == null || sortDefaultButton == null || sortNameButton == null ||
            sortCategoryButton == null || sortWeightButton == null || sortValueButton == null ||
            sortQuantityButton == null)
            return;

        detailsConsumeButton.clicked -= HandleConsumeButtonClicked;
        splitOneButton.clicked -= HandleSplitOneClicked;
        splitHalfButton.clicked -= HandleSplitHalfClicked;
        splitCustomButton.clicked -= HandleSplitCustomClicked;
        sortDirectionButton.clicked -= ToggleSortDirection;
        sortDefaultButton.clicked -= HandleSortDefaultClicked;
        sortNameButton.clicked -= HandleSortNameClicked;
        sortCategoryButton.clicked -= HandleSortCategoryClicked;
        sortWeightButton.clicked -= HandleSortWeightClicked;
        sortValueButton.clicked -= HandleSortValueClicked;
        sortQuantityButton.clicked -= HandleSortQuantityClicked;
    }

    void HandleActiveInventoryRegistered(ShipInventoryController inventory)
    {
        if (activeInventory == inventory)
            return;

        UnsubscribeFromInventory();
        activeInventory = inventory;

        if (activeInventory != null)
        {
            activeInventory.OnInventoryChanged += HandleInventoryChanged;
            warnedMissingInventory = false;
        }
        else
        {
            selectedSlotIndex = -1;
            selectedEquipmentSlotIndex = -1;
            carriedSlotIndex = -1;
            ClearSplitCarry();
            ClearEquipmentCarry();
            CloseSplitPanel();
        }

        SyncSlotGridToInventory();
        RefreshAllUi();
    }

    void HandleActiveEquipmentRegistered(ShipEquipmentController equipment)
    {
        if (activeEquipment == equipment)
            return;

        UnsubscribeFromEquipment();
        activeEquipment = equipment;

        if (activeEquipment != null)
            activeEquipment.OnEquipmentChanged += HandleEquipmentChanged;
        else
        {
            selectedEquipmentSlotIndex = -1;
            ClearEquipmentCarry();
        }

        BuildEquipmentSlots();
        RefreshAllUi();
    }

    void UnsubscribeFromInventory()
    {
        if (activeInventory != null)
            activeInventory.OnInventoryChanged -= HandleInventoryChanged;
    }

    void UnsubscribeFromEquipment()
    {
        if (activeEquipment != null)
            activeEquipment.OnEquipmentChanged -= HandleEquipmentChanged;
    }

    void HandleInventoryChanged()
    {
        SyncSlotGridToInventory();
        RefreshAllUi();
    }

    void HandleEquipmentChanged()
    {
        BuildEquipmentSlots();
        RefreshAllUi();
    }

    void HandleConsumeButtonClicked()
    {
        if (!IsInventoryOpen || hungerController == null)
            return;

        bool consumed = selectedEquipmentSlotIndex >= 0
            ? TryConsumeEquipmentSlot(selectedEquipmentSlotIndex)
            : TryConsumeInventorySlot(selectedSlotIndex);
        if (consumed)
        {
            UIAudioController.ActiveInstance?.PlayEatSound();
            RefreshAllUi();
        }
    }

    void HandleToggleInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (WorldMapUIController.IsMapOpen || ShopController.IsShopOpen || FishingMinigameController.IsFishingOpen)
            return;

        if (keyboard.eKey.wasPressedThisFrame)
        {
            SetOverlayOpen(!IsInventoryOpen);
            return;
        }

        if (IsInventoryOpen && keyboard.escapeKey.wasPressedThisFrame)
            SetOverlayOpen(false);
    }

    void HandleOverlayPointerMove(PointerMoveEvent evt)
    {
        if (!IsInventoryOpen || overlayElement == null)
            return;

        hasLocalPointerPosition = true;
        localPointerPosition = evt.localPosition;
        TryStartPendingDrag(localPointerPosition);
        UpdateHoveredPreviewTarget(evt.position);
        UpdateDragGhostPosition();
        RefreshVisualState();
    }

    void HandleOverlayPointerDown(PointerDownEvent evt)
    {
        if (!IsInventoryOpen || evt.button != 0)
            return;

        hasLocalPointerPosition = true;
        localPointerPosition = evt.localPosition;

        if (ignoreNextOverlaySplitClose)
        {
            ignoreNextOverlaySplitClose = false;
            return;
        }

        if (splitSourceSlotIndex < 0 || splitPanelElement == null)
            return;

        if (evt.target is VisualElement targetElement && IsElementOrDescendantOf(targetElement, splitPanelElement))
            return;

        CloseSplitPanel();
    }

    void HandleOverlayPointerUp(PointerUpEvent evt)
    {
        if (evt.button != 0)
            return;

        ClearPendingDragState();
    }

    void HandleOverlayGeometryChanged(GeometryChangedEvent evt)
    {
        if (!IsInventoryOpen)
            return;

        RefreshHoveredPreviewTargetFromStoredPointer();
        UpdateSplitPanelPosition();
        UpdateDragGhostPosition();
        RefreshVisualState();
    }

    void SetOverlayOpen(bool open)
    {
        TryInitialize();

        if (overlayElement != null)
            overlayElement.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

        if (open == IsInventoryOpen)
            return;

        IsInventoryOpen = open;
        if (open)
        {
            ResetSortDirectionToDefault();
            StoreCursorState();
            UnityEngine.Cursor.visible = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UIAudioController.ActiveInstance?.PlayInventoryOpenSound();
            RefreshAllUi();
        }
        else
        {
            RestoreCursorState();
            hasLocalPointerPosition = false;
            hoveredSlotIndex = -1;
            hoveredEquipmentSlotIndex = -1;
            ignoreNextOverlaySplitClose = false;
            ClearPendingDragState();
            carriedSlotIndex = -1;
            ReturnCarriedSplitToOrigin();
            ReturnCarriedEquipmentToOrigin();
            selectedSlotIndex = -1;
            selectedEquipmentSlotIndex = -1;
            ClearSplitCarry();
            ClearEquipmentCarry();
            CloseSplitPanel();
            RefreshVisualState();
        }
    }

    void StoreCursorState()
    {
        previousCursorVisible = UnityEngine.Cursor.visible;
        previousCursorLockMode = UnityEngine.Cursor.lockState;
    }

    void RestoreCursorState()
    {
        UnityEngine.Cursor.visible = previousCursorVisible;
        UnityEngine.Cursor.lockState = previousCursorLockMode;
    }

    void BuildSlotGrid()
    {
        if (!overlayInitialized || gridElement == null)
            return;

        gridElement.Clear();
        slotElements.Clear();
        slotIconElements.Clear();
        slotGhostIconElements.Clear();
        slotQuantityLabels.Clear();

        int slotCount = activeInventory != null ? activeInventory.MaxSlots : fixedColumnCount;
        for (int i = 0; i < slotCount; i++)
        {
            int slotIndex = i;

            var slot = new VisualElement();
            slot.name = $"inventory-slot-{slotIndex}";
            slot.AddToClassList("inventory-slot");
            slot.pickingMode = PickingMode.Position;
            slot.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                Vector2 overlayLocalPosition = overlayElement != null
                    ? overlayElement.WorldToLocal(evt.position)
                    : evt.localPosition;
                HandleSlotPointerDown(slotIndex, overlayLocalPosition);
            });
            slot.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                if (!hasLocalPointerPosition && overlayElement != null)
                {
                    hasLocalPointerPosition = true;
                    localPointerPosition = overlayElement.WorldToLocal(evt.position);
                }

                HandleSlotClicked(slotIndex, IsShiftPressed(evt.shiftKey), evt.clickCount);
            });
            var icon = new VisualElement();
            icon.AddToClassList("inventory-slot-icon");
            icon.pickingMode = PickingMode.Ignore;

            var ghostIcon = new VisualElement();
            ghostIcon.AddToClassList("inventory-slot-icon");
            ghostIcon.AddToClassList("inventory-slot-ghost-icon");
            ghostIcon.pickingMode = PickingMode.Ignore;

            var quantityLabel = new Label();
            quantityLabel.AddToClassList("inventory-slot-quantity");
            quantityLabel.pickingMode = PickingMode.Ignore;

            slot.Add(icon);
            slot.Add(ghostIcon);
            slot.Add(quantityLabel);

            slotElements.Add(slot);
            slotIconElements.Add(icon);
            slotGhostIconElements.Add(ghostIcon);
            slotQuantityLabels.Add(quantityLabel);
            gridElement.Add(slot);
        }
    }

    void BuildEquipmentSlots()
    {
        if (!overlayInitialized || equipmentStripElement == null)
            return;

        equipmentStripElement.Clear();
        equipmentSlotElements.Clear();
        equipmentSlotIconElements.Clear();
        equipmentSlotGhostIconElements.Clear();
        equipmentSlotLabels.Clear();
        equipmentSlotQuantityLabels.Clear();

        int slotCount = activeEquipment != null ? activeEquipment.SlotCount : 6;
        for (int i = 0; i < slotCount; i++)
        {
            int slotIndex = i;
            string label = "Stub";
            bool isInteractive = false;
            if (activeEquipment != null && activeEquipment.TryGetSlotSnapshot(i, out var slotSnapshot))
            {
                label = slotSnapshot.Label;
                isInteractive = slotSnapshot.IsInteractive;
            }
            else if (i == 0)
            {
                label = "Gun";
                isInteractive = true;
            }

            var slot = new VisualElement();
            slot.name = $"inventory-equipment-slot-{slotIndex}";
            slot.AddToClassList("inventory-equipment-slot");
            if (!isInteractive)
                slot.AddToClassList("inventory-equipment-slot--inactive");
            slot.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                Vector2 overlayLocalPosition = overlayElement != null
                    ? overlayElement.WorldToLocal(evt.position)
                    : evt.localPosition;
                HandleEquipmentPointerDown(slotIndex, overlayLocalPosition);
            });
            slot.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                if (!hasLocalPointerPosition && overlayElement != null)
                {
                    hasLocalPointerPosition = true;
                    localPointerPosition = overlayElement.WorldToLocal(evt.position);
                }

                HandleEquipmentSlotClicked(slotIndex);
            });

            var slotLabel = new Label(label);
            slotLabel.AddToClassList("inventory-equipment-slot-label");
            slotLabel.pickingMode = PickingMode.Ignore;

            var icon = new VisualElement();
            icon.AddToClassList("inventory-equipment-slot-icon");
            icon.pickingMode = PickingMode.Ignore;

            var ghostIcon = new VisualElement();
            ghostIcon.AddToClassList("inventory-equipment-slot-icon");
            ghostIcon.AddToClassList("inventory-slot-ghost-icon");
            ghostIcon.pickingMode = PickingMode.Ignore;

            var quantityLabel = new Label();
            quantityLabel.AddToClassList("inventory-slot-quantity");
            quantityLabel.pickingMode = PickingMode.Ignore;

            slot.Add(slotLabel);
            slot.Add(icon);
            slot.Add(ghostIcon);
            slot.Add(quantityLabel);

            equipmentSlotElements.Add(slot);
            equipmentSlotIconElements.Add(icon);
            equipmentSlotGhostIconElements.Add(ghostIcon);
            equipmentSlotLabels.Add(slotLabel);
            equipmentSlotQuantityLabels.Add(quantityLabel);
            equipmentStripElement.Add(slot);
        }
    }

    void SyncSlotGridToInventory()
    {
        if (!overlayInitialized)
            return;

        int targetCount = activeInventory != null ? activeInventory.MaxSlots : fixedColumnCount;
        if (slotElements.Count != targetCount)
            BuildSlotGrid();
    }

    void HandleSlotPointerDown(int slotIndex, Vector2 localPosition)
    {
        pendingDragSourceKind = DragSourceKind.Cargo;
        pendingDragSourceIndex = slotIndex;
        pendingDragStartPosition = localPosition;
    }

    void HandleEquipmentPointerDown(int slotIndex, Vector2 localPosition)
    {
        pendingDragSourceKind = DragSourceKind.Equipment;
        pendingDragSourceIndex = slotIndex;
        pendingDragStartPosition = localPosition;
    }

    void HandleSlotClicked(int slotIndex, bool shiftHeld, int clickCount)
    {
        if (!IsInventoryOpen || activeInventory == null)
            return;

        if (suppressNextClickAction)
        {
            suppressNextClickAction = false;
            return;
        }

        selectedSlotIndex = slotIndex;
        selectedEquipmentSlotIndex = -1;

        if (shiftHeld && !IsCarryingAnything() &&
            activeInventory.TryGetSlotSnapshot(slotIndex, out var splitSlot) &&
            !splitSlot.IsEmpty &&
            splitSlot.Quantity > 1)
        {
            OpenSplitPanel(slotIndex, splitSlot.Quantity);
            RefreshAllUi();
            return;
        }

        CloseSplitPanel();

        if (clickCount >= 2)
        {
            HandleInventorySlotDoubleClicked(slotIndex);
            RefreshAllUi();
            return;
        }

        if (IsCarryingWholeSlot())
        {
            if (carriedSlotIndex == slotIndex)
            {
                carriedSlotIndex = -1;
            }
            else
            {
                if (activeInventory.MoveOrMergeSlot(carriedSlotIndex, slotIndex))
                {
                    UIAudioController.ActiveInstance?.PlayInventoryClick();
                    carriedSlotIndex = -1;
                }
            }
        }
        else
        {
            if (IsCarryingEquipmentItem())
            {
                if (activeInventory.TryPlaceLooseItemAt(slotIndex, carriedEquipmentItem, carriedEquipmentQuantity))
                {
                    UIAudioController.ActiveInstance?.PlayInventoryClick();
                    ClearEquipmentCarry();
                }
            }
            else if (carriedSplitItem != null && carriedSplitQuantity > 0)
            {
                int remainder = activeInventory.TryInsertStackAt(slotIndex, carriedSplitItem, carriedSplitQuantity);
                if (remainder != carriedSplitQuantity)
                    UIAudioController.ActiveInstance?.PlayInventoryClick();
                carriedSplitQuantity = remainder;
                if (carriedSplitQuantity <= 0)
                    ClearSplitCarry();
            }
            else if (activeInventory.TryGetSlotSnapshot(slotIndex, out var slot) && !slot.IsEmpty)
            {
                LiftCargoSlotIntoCarry(slotIndex);
            }
        }

        RefreshAllUi();
    }

    void HandleEquipmentSlotClicked(int slotIndex)
    {
        if (!IsInventoryOpen || activeEquipment == null)
            return;

        if (suppressNextClickAction)
        {
            suppressNextClickAction = false;
            return;
        }

        selectedEquipmentSlotIndex = slotIndex;
        selectedSlotIndex = -1;

        if (!activeEquipment.TryGetSlotSnapshot(slotIndex, out var slot))
            return;

        if (!slot.IsInteractive)
        {
            RefreshAllUi();
            return;
        }

        CloseSplitPanel();

        if (IsCarryingWholeSlot())
        {
            if (activeInventory != null && activeEquipment.TryEquipFromInventorySlot(activeInventory, carriedSlotIndex, slotIndex))
            {
                UIAudioController.ActiveInstance?.PlayInventoryClick();
                carriedSlotIndex = -1;
            }
        }
        else if (IsCarryingEquipmentItem())
        {
            if (activeInventory != null && activeEquipment.TryPlaceCarriedItem(slotIndex, carriedEquipmentItem, carriedEquipmentQuantity, activeInventory, out int remainder))
            {
                UIAudioController.ActiveInstance?.PlayInventoryClick();
                carriedEquipmentQuantity = remainder;
                if (carriedEquipmentQuantity <= 0)
                    ClearEquipmentCarry();
            }
        }
        else if (carriedSplitItem != null && carriedSplitQuantity > 0)
        {
            if (activeInventory != null && activeEquipment.TryPlaceCarriedItem(slotIndex, carriedSplitItem, carriedSplitQuantity, activeInventory, out int remainder))
            {
                UIAudioController.ActiveInstance?.PlayInventoryClick();
                carriedSplitQuantity = remainder;
                if (carriedSplitQuantity <= 0)
                    ClearSplitCarry();
            }
        }
        else if (!slot.IsEmpty)
        {
            LiftEquipmentSlotIntoCarry(slotIndex, slot);
        }

        RefreshAllUi();
    }

    void HandleInventorySlotDoubleClicked(int slotIndex)
    {
        if (activeInventory == null || !activeInventory.TryGetSlotSnapshot(slotIndex, out var slot) || slot.IsEmpty || slot.Item == null)
            return;

        if (TryConsumeInventorySlot(slotIndex))
        {
            UIAudioController.ActiveInstance?.PlayEatSound();
            return;
        }

        if (activeEquipment == null)
            return;

        for (int i = 0; i < activeEquipment.SlotCount; i++)
        {
            if (activeEquipment.TryEquipFromInventorySlot(activeInventory, slotIndex, i))
            {
                UIAudioController.ActiveInstance?.PlayInventoryClick();
                return;
            }
        }
    }

    void HandleEquipmentSlotDoubleClicked(int slotIndex)
    {
        if (activeEquipment == null || !activeEquipment.TryGetSlotSnapshot(slotIndex, out var slot) || slot.IsEmpty || slot.Item == null)
            return;

        if (TryConsumeEquipmentSlot(slotIndex))
        {
            UIAudioController.ActiveInstance?.PlayEatSound();
            return;
        }

        if (activeInventory != null)
        {
            if (activeEquipment.TryMoveEquipmentSlotToInventory(slotIndex, activeInventory))
                UIAudioController.ActiveInstance?.PlayInventoryClick();
        }
    }

    bool TryConsumeInventorySlot(int slotIndex)
    {
        if (activeInventory == null || hungerController == null)
            return false;

        if (!activeInventory.TryGetSlotSnapshot(slotIndex, out var slot) || slot.IsEmpty || slot.Item == null)
            return false;

        if (!CanConsumeItem(slot.Item))
            return false;

        if (!activeInventory.TryRemoveQuantityAt(slotIndex, 1, out ItemDefinition removedItem, out int removedQuantity) ||
            removedItem == null || removedQuantity <= 0)
            return false;

        hungerController.ConsumeFood(removedItem.FoodRestoreAmount);
        return true;
    }

    bool TryConsumeEquipmentSlot(int slotIndex)
    {
        if (activeEquipment == null || hungerController == null)
            return false;

        if (!activeEquipment.TryGetSlotSnapshot(slotIndex, out var slot) || slot.IsEmpty || slot.Item == null)
            return false;

        if (!CanConsumeItem(slot.Item))
            return false;

        if (!activeEquipment.TryConsumeFromSlot(slotIndex, 1, out ItemDefinition removedItem, out int removedQuantity) ||
            removedItem == null || removedQuantity <= 0)
            return false;

        hungerController.ConsumeFood(removedItem.FoodRestoreAmount);
        return true;
    }

    bool CanConsumeItem(ItemDefinition item)
    {
        return item != null && item.Category == ItemCategory.Food && item.FoodRestoreAmount > 0f;
    }

    void TryStartPendingDrag(Vector2 currentLocalPosition)
    {
        if (pendingDragSourceKind == DragSourceKind.None || IsCarryingAnything())
            return;

        if ((currentLocalPosition - pendingDragStartPosition).sqrMagnitude < dragStartThresholdPixels * dragStartThresholdPixels)
            return;

        switch (pendingDragSourceKind)
        {
            case DragSourceKind.Cargo:
                if (activeInventory != null &&
                    activeInventory.TryGetSlotSnapshot(pendingDragSourceIndex, out var cargoSlot) &&
                    !cargoSlot.IsEmpty)
                {
                    LiftCargoSlotIntoCarry(pendingDragSourceIndex);
                }
                break;

            case DragSourceKind.Equipment:
                if (activeEquipment != null &&
                    activeEquipment.TryGetSlotSnapshot(pendingDragSourceIndex, out var equipmentSlot) &&
                    !equipmentSlot.IsEmpty)
                {
                    LiftEquipmentSlotIntoCarry(pendingDragSourceIndex, equipmentSlot);
                }
                break;
        }

        suppressNextClickAction = true;
        ClearPendingDragState();
    }

    void LiftEquipmentSlotIntoCarry(int slotIndex, ShipEquipmentController.EquipmentSlotSnapshot equipmentSlot)
    {
        if (activeEquipment == null || equipmentSlot.IsEmpty || equipmentSlot.Item == null)
            return;

        if (!activeEquipment.TryConsumeFromSlot(slotIndex, equipmentSlot.Quantity, out ItemDefinition removedItem, out int removedQuantity) ||
            removedItem == null || removedQuantity <= 0)
        {
            return;
        }

        carriedEquipmentItem = removedItem;
        carriedEquipmentQuantity = removedQuantity;
        carriedEquipmentOriginSlotIndex = slotIndex;
        selectedEquipmentSlotIndex = slotIndex;
        selectedSlotIndex = -1;
        UIAudioController.ActiveInstance?.PlayInventoryClick();
    }

    void LiftCargoSlotIntoCarry(int slotIndex)
    {
        if (activeInventory == null)
            return;

        if (!activeInventory.TryGetSlotSnapshot(slotIndex, out var slot) || slot.IsEmpty)
            return;

        carriedSlotIndex = slotIndex;
        selectedSlotIndex = slotIndex;
        selectedEquipmentSlotIndex = -1;
        UIAudioController.ActiveInstance?.PlayInventoryClick();
    }

    void ClearPendingDragState()
    {
        pendingDragSourceKind = DragSourceKind.None;
        pendingDragSourceIndex = -1;
    }

    void RefreshAllUi()
    {
        if (!overlayInitialized || overlayElement == null || rootElement == null)
            return;

        RefreshSummary();
        RefreshSlots();
        RefreshEquipmentSlots();
        UpdateSelectedDetails();
        RefreshVisualState();
        RefreshSortUiState();
    }

    void RefreshSummary()
    {
        if (!overlayInitialized ||
            activeInventory == null ||
            currencyValueLabel == null ||
            slotSummaryLabel == null ||
            weightSummaryLabel == null)
            return;

        int reservedUsedSlots = activeEquipment != null ? activeEquipment.UsedSlotCount : 0;
        float reservedWeight = activeEquipment != null ? activeEquipment.CurrentWeight : 0f;
        currencyValueLabel.text = activeInventory.Gold.ToString();
        slotSummaryLabel.text = $"{activeInventory.UsedSlotCount + reservedUsedSlots}/{activeInventory.MaxSlots}";
        weightSummaryLabel.text = $"{activeInventory.CurrentWeight + reservedWeight:0.##}/{activeInventory.MaxCarryWeight:0.##}";
    }

    void RefreshSlots()
    {
        if (!overlayInitialized ||
            activeInventory == null ||
            slotElements == null ||
            slotIconElements == null ||
            slotQuantityLabels == null)
            return;

        IReadOnlyList<ShipInventoryController.InventorySlotSnapshot> slots = activeInventory.Slots;
        for (int i = 0; i < slotElements.Count; i++)
        {
            bool hasSlot = i < slots.Count;
            ShipInventoryController.InventorySlotSnapshot slot = hasSlot ? slots[i] : default;

            VisualElement iconElement = slotIconElements[i];
            Label quantityLabel = slotQuantityLabels[i];
            if (iconElement == null || quantityLabel == null)
                continue;

            if (!hasSlot || slot.IsEmpty)
            {
                iconElement.style.backgroundImage = StyleKeyword.None;
                quantityLabel.text = string.Empty;
                continue;
            }

            Sprite icon = ResolveItemIcon(slot.Item);
            if (icon != null)
                iconElement.style.backgroundImage = new StyleBackground(icon);
            else
                iconElement.style.backgroundImage = StyleKeyword.None;

            quantityLabel.text = slot.Quantity.ToString();
        }
    }

    void RefreshEquipmentSlots()
    {
        if (!overlayInitialized || equipmentSlotElements == null || equipmentSlotIconElements == null || equipmentSlotLabels == null || equipmentSlotQuantityLabels == null)
            return;

        for (int i = 0; i < equipmentSlotElements.Count; i++)
        {
            VisualElement slotElement = equipmentSlotElements[i];
            VisualElement iconElement = equipmentSlotIconElements[i];
            Label labelElement = equipmentSlotLabels[i];
            Label quantityLabel = equipmentSlotQuantityLabels[i];
            if (slotElement == null || iconElement == null || labelElement == null || quantityLabel == null)
                continue;

            if (activeEquipment == null || !activeEquipment.TryGetSlotSnapshot(i, out var slot))
            {
                labelElement.text = i switch
                {
                    0 => "Gun",
                    1 => "Rod",
                    2 => "Cannon",
                    3 => "Musket",
                    _ => "Stub"
                };
                iconElement.style.backgroundImage = StyleKeyword.None;
                quantityLabel.text = string.Empty;
                continue;
            }

            labelElement.text = slot.Label;
            Sprite icon = ResolveItemIcon(slot.Item);
            if (!slot.IsEmpty && icon != null)
                iconElement.style.backgroundImage = new StyleBackground(icon);
            else
                iconElement.style.backgroundImage = StyleKeyword.None;
            quantityLabel.text = !slot.IsEmpty && slot.Quantity > 1 ? slot.Quantity.ToString() : string.Empty;
        }
    }

    void UpdateSelectedDetails()
    {
        if (!overlayInitialized ||
            detailsIconElement == null ||
            detailsNameLabel == null ||
            detailsCategoryLabel == null ||
            detailsQuantityLabel == null ||
            detailsWeightLabel == null ||
            detailsValueLabel == null ||
            detailsDescriptionLabel == null)
            return;

        if (selectedEquipmentSlotIndex >= 0 && activeEquipment != null && activeEquipment.TryGetSlotSnapshot(selectedEquipmentSlotIndex, out var equipmentSlot))
        {
            Sprite equipmentIcon = ResolveItemIcon(equipmentSlot.Item);
            if (!equipmentSlot.IsEmpty && equipmentIcon != null)
                detailsIconElement.style.backgroundImage = new StyleBackground(equipmentIcon);
            else
                detailsIconElement.style.backgroundImage = StyleKeyword.None;

            if (equipmentSlot.IsEmpty)
            {
                detailsNameLabel.text = $"{equipmentSlot.Label} Slot";
                detailsCategoryLabel.text = "Reserved Equipment";
                detailsQuantityLabel.text = "Empty Slot";
                detailsWeightLabel.text = string.Empty;
                detailsValueLabel.text = string.Empty;
                detailsDescriptionLabel.text = equipmentSlot.IsInteractive
                    ? $"Reserved {equipmentSlot.Label.ToLowerInvariant()} slot."
                    : "Reserved for future equipment.";
                detailsConsumeButton.style.display = DisplayStyle.None;
                return;
            }

            ItemDefinition equippedItem = equipmentSlot.Item;
            detailsNameLabel.text = equippedItem.DisplayName;
            detailsCategoryLabel.text = equippedItem.Category.ToString();
            detailsQuantityLabel.text = equippedItem.Stackable ? $"Qty: {equipmentSlot.Quantity}" : "Equipped";
            detailsWeightLabel.text = $"Weight: {equippedItem.Weight:0.##}";
            detailsValueLabel.text = $"Value: {equippedItem.Value}";
            detailsDescriptionLabel.text = equippedItem.Description;
            bool canConsumeEquipped = CanConsumeItem(equippedItem);
            detailsConsumeButton.style.display = canConsumeEquipped ? DisplayStyle.Flex : DisplayStyle.None;
            detailsConsumeButton.text = canConsumeEquipped ? $"Eat (+{equippedItem.FoodRestoreAmount:0.#} Hunger)" : "Eat";
            return;
        }

        if (activeInventory == null || selectedSlotIndex < 0 || !activeInventory.TryGetSlotSnapshot(selectedSlotIndex, out var slot) || slot.IsEmpty)
        {
            detailsIconElement.style.backgroundImage = StyleKeyword.None;
            detailsNameLabel.text = "Empty Slot";
            detailsCategoryLabel.text = string.Empty;
            detailsQuantityLabel.text = string.Empty;
            detailsWeightLabel.text = string.Empty;
            detailsValueLabel.text = string.Empty;
            detailsDescriptionLabel.text = string.Empty;
            detailsConsumeButton.style.display = DisplayStyle.None;
            return;
        }

        ItemDefinition item = slot.Item;
        Sprite icon = ResolveItemIcon(item);
        if (icon != null)
            detailsIconElement.style.backgroundImage = new StyleBackground(icon);
        else
            detailsIconElement.style.backgroundImage = StyleKeyword.None;

        detailsNameLabel.text = item.DisplayName;
        detailsCategoryLabel.text = item.Category.ToString();
        detailsQuantityLabel.text = $"Qty: {slot.Quantity}";
        detailsWeightLabel.text = $"Weight: {item.Weight:0.##}";
        detailsValueLabel.text = $"Value: {item.Value}";
        detailsDescriptionLabel.text = item.Description;

        bool canConsume = hungerController != null && item.Category == ItemCategory.Food && item.FoodRestoreAmount > 0f;
        detailsConsumeButton.style.display = canConsume ? DisplayStyle.Flex : DisplayStyle.None;
        detailsConsumeButton.text = canConsume ? $"Eat (+{item.FoodRestoreAmount:0.#} Hunger)" : "Eat";
    }

    void RefreshVisualState()
    {
        if (!overlayInitialized || dragGhostElement == null || dragGhostIconElement == null)
            return;

        for (int i = 0; i < slotElements.Count; i++)
        {
            VisualElement slot = slotElements[i];
            if (slot == null)
                continue;

            slot.EnableInClassList("inventory-slot--selected", i == selectedSlotIndex);
            slot.EnableInClassList("inventory-slot--carried", i == carriedSlotIndex || i == splitSourceSlotIndex);
            slot.EnableInClassList("inventory-slot--preview", i == hoveredSlotIndex && ShouldShowCargoPreview(i));
        }

        for (int i = 0; i < equipmentSlotElements.Count; i++)
        {
            VisualElement slot = equipmentSlotElements[i];
            if (slot == null)
                continue;

            slot.EnableInClassList("inventory-slot--selected", i == selectedEquipmentSlotIndex);
            slot.EnableInClassList("inventory-slot--carried", i == carriedEquipmentOriginSlotIndex);
            slot.EnableInClassList("inventory-slot--preview", i == hoveredEquipmentSlotIndex && ShouldShowEquipmentPreview(i));
        }

        if (splitPanelElement != null)
            splitPanelElement.style.display = splitSourceSlotIndex >= 0 ? DisplayStyle.Flex : DisplayStyle.None;

        RefreshSlotGhostPreviews();
        RefreshDragGhostVisibility();
        UpdateSplitPanelPosition();
        UpdateDragGhostPosition();
        RefreshSortUiState();
    }

    void RefreshSortUiState()
    {
        if (!overlayInitialized || sortDirectionButton == null)
            return;

        InventoryInteractionState interactionState = GetInteractionState();
        bool sortEnabled = IsInventoryOpen &&
                           interactionState != InventoryInteractionState.CarryingWholeStack &&
                           interactionState != InventoryInteractionState.CarryingSplitStack &&
                           interactionState != InventoryInteractionState.SplitMenuOpen;
        sortDirectionButton.text = sortAscending ? "Asc" : "Desc";
        sortDirectionButton.SetEnabled(sortEnabled);
        sortDefaultButton?.SetEnabled(sortEnabled);
        sortNameButton?.SetEnabled(sortEnabled);
        sortCategoryButton?.SetEnabled(sortEnabled);
        sortWeightButton?.SetEnabled(sortEnabled);
        sortValueButton?.SetEnabled(sortEnabled);
        sortQuantityButton?.SetEnabled(sortEnabled);
    }

    void UpdateSplitPanelPosition()
    {
        if (!overlayInitialized || splitPanelElement == null || overlayElement == null)
            return;

        if (splitSourceSlotIndex < 0 || splitSourceSlotIndex >= slotElements.Count)
            return;

        VisualElement sourceSlot = slotElements[splitSourceSlotIndex];
        if (sourceSlot == null)
            return;

        Rect slotWorldBound = sourceSlot.worldBound;
        Vector2 desiredTopLeft = overlayElement.WorldToLocal(new Vector2(slotWorldBound.xMin, slotWorldBound.yMax));
        float panelWidth = Mathf.Max(180f, splitPanelElement.resolvedStyle.width);
        float panelHeight = Mathf.Max(164f, splitPanelElement.resolvedStyle.height);
        float overlayWidth = overlayElement.resolvedStyle.width;
        float overlayHeight = overlayElement.resolvedStyle.height;

        float left = desiredTopLeft.x + splitPanelOffset.x;
        float top = desiredTopLeft.y + splitPanelOffset.y;

        if (left + panelWidth > overlayWidth - 8f)
            left = Mathf.Max(8f, overlayWidth - panelWidth - 8f);

        if (top + panelHeight > overlayHeight - 8f)
            top = Mathf.Max(8f, desiredTopLeft.y - panelHeight - 6f);

        splitPanelElement.style.left = left;
        splitPanelElement.style.top = top;
    }

    void UpdateDragGhostPosition()
    {
        if (!overlayInitialized || dragGhostElement == null || rootElement == null)
            return;

        if (!IsInventoryOpen || !IsCarryingAnything() || !hasLocalPointerPosition)
            return;

        dragGhostElement.style.left = localPointerPosition.x - 28f;
        dragGhostElement.style.top = localPointerPosition.y - 28f;
    }

    void RefreshSlotGhostPreviews()
    {
        ItemDefinition previewItem = GetCarriedPreviewItem();
        Sprite previewIcon = ResolveItemIcon(previewItem);

        for (int i = 0; i < slotGhostIconElements.Count; i++)
        {
            VisualElement ghost = slotGhostIconElements[i];
            if (ghost == null)
                continue;

            bool showPreview = previewItem != null && i == hoveredSlotIndex && ShouldShowCargoGhostPreview(i);
            ghost.style.display = showPreview ? DisplayStyle.Flex : DisplayStyle.None;
            if (showPreview && previewIcon != null)
                ghost.style.backgroundImage = new StyleBackground(previewIcon);
            else
                ghost.style.backgroundImage = StyleKeyword.None;
        }

        for (int i = 0; i < equipmentSlotGhostIconElements.Count; i++)
        {
            VisualElement ghost = equipmentSlotGhostIconElements[i];
            if (ghost == null)
                continue;

            bool showPreview = previewItem != null && i == hoveredEquipmentSlotIndex && ShouldShowEquipmentPreview(i);
            ghost.style.display = showPreview ? DisplayStyle.Flex : DisplayStyle.None;
            if (showPreview && previewIcon != null)
                ghost.style.backgroundImage = new StyleBackground(previewIcon);
            else
                ghost.style.backgroundImage = StyleKeyword.None;
        }
    }

    void RefreshDragGhostVisibility()
    {
        if (!overlayInitialized || dragGhostElement == null || dragGhostIconElement == null)
            return;

        ShipInventoryController.InventorySlotSnapshot carriedSlot = default;
        bool hasCarriedSlot =
            IsInventoryOpen &&
            activeInventory != null &&
            carriedSlotIndex >= 0 &&
            activeInventory.TryGetSlotSnapshot(carriedSlotIndex, out carriedSlot) &&
            !carriedSlot.IsEmpty;

        bool hasSplitCarry = IsInventoryOpen && carriedSplitItem != null && carriedSplitQuantity > 0;
        bool hasEquipmentCarry = IsInventoryOpen && carriedEquipmentItem != null && carriedEquipmentQuantity > 0;
        bool showGhost = (hasCarriedSlot || hasSplitCarry || hasEquipmentCarry) && hasLocalPointerPosition;
        dragGhostElement.style.display = showGhost ? DisplayStyle.Flex : DisplayStyle.None;
        if (!showGhost)
        {
            dragGhostIconElement.style.backgroundImage = StyleKeyword.None;
            return;
        }

        ItemDefinition ghostItem = hasCarriedSlot ? carriedSlot.Item : hasEquipmentCarry ? carriedEquipmentItem : carriedSplitItem;
        Sprite icon = ResolveItemIcon(ghostItem);
        if (icon != null)
            dragGhostIconElement.style.backgroundImage = new StyleBackground(icon);
        else
            dragGhostIconElement.style.backgroundImage = StyleKeyword.None;
    }

    void OpenSplitPanel(int slotIndex, int quantity)
    {
        splitSourceSlotIndex = slotIndex;
        ignoreNextOverlaySplitClose = true;
        if (splitContextLabel != null)
            splitContextLabel.text = $"Choose how many to split from {quantity}.";

        if (splitCustomField != null)
            splitCustomField.value = Mathf.Max(1, quantity / 2).ToString();

        UpdateSplitPanelPosition();
        RefreshVisualState();
    }

    void CloseSplitPanel()
    {
        splitSourceSlotIndex = -1;
        ignoreNextOverlaySplitClose = false;
        RefreshVisualState();
    }

    void HandleSplitOneClicked()
    {
        ExecuteSplit(1);
    }

    void HandleSplitHalfClicked()
    {
        if (activeInventory == null || splitSourceSlotIndex < 0)
            return;

        if (!activeInventory.TryGetSlotSnapshot(splitSourceSlotIndex, out var slot) || slot.IsEmpty)
            return;

        ExecuteSplit(Mathf.Max(1, slot.Quantity / 2));
    }

    void HandleSplitCustomClicked()
    {
        if (splitCustomField == null)
            return;

        if (!int.TryParse(splitCustomField.value, out int customAmount))
            return;

        ExecuteSplit(customAmount);
    }

    void HandleSortDefaultClicked() => HandleSortRequested(ShipInventoryController.InventorySortType.Default);
    void HandleSortNameClicked() => HandleSortRequested(ShipInventoryController.InventorySortType.Name);
    void HandleSortCategoryClicked() => HandleSortRequested(ShipInventoryController.InventorySortType.Category);
    void HandleSortWeightClicked() => HandleSortRequested(ShipInventoryController.InventorySortType.Weight);
    void HandleSortValueClicked() => HandleSortRequested(ShipInventoryController.InventorySortType.Value);
    void HandleSortQuantityClicked() => HandleSortRequested(ShipInventoryController.InventorySortType.Quantity);

    void ToggleSortDirection()
    {
        sortAscending = !sortAscending;
        UIAudioController.ActiveInstance?.PlayButtonClick();
        RefreshSortUiState();
    }

    void ResetSortDirectionToDefault()
    {
        sortAscending = false;
    }

    void HandleSortRequested(ShipInventoryController.InventorySortType sortType)
    {
        if (!IsInventoryOpen || activeInventory == null || IsCarryingAnything() || splitSourceSlotIndex >= 0)
            return;

        bool ascendingForRequest = sortType == ShipInventoryController.InventorySortType.Default
            ? true
            : sortAscending;

        activeInventory.SortInventory(sortType, ascendingForRequest);
        selectedSlotIndex = -1;
        UIAudioController.ActiveInstance?.PlayButtonClick();
        RefreshAllUi();
    }

    void ExecuteSplit(int requestedQuantity)
    {
        if (activeInventory == null || splitSourceSlotIndex < 0 || IsCarryingAnything())
            return;

        if (!activeInventory.TryExtractSplitStack(splitSourceSlotIndex, requestedQuantity, out ItemDefinition item, out int extractedQuantity))
            return;

        carriedSplitItem = item;
        carriedSplitQuantity = extractedQuantity;
        carriedSplitOriginSlotIndex = splitSourceSlotIndex;
        selectedSlotIndex = splitSourceSlotIndex;
        CloseSplitPanel();
        UIAudioController.ActiveInstance?.PlayInventoryClick();
        RefreshAllUi();
    }

    bool IsCarryingAnything()
    {
        return IsCarryingWholeSlot() || IsCarryingEquipmentItem() || (carriedSplitItem != null && carriedSplitQuantity > 0);
    }

    ItemDefinition GetCarriedPreviewItem()
    {
        if (activeInventory != null &&
            carriedSlotIndex >= 0 &&
            activeInventory.TryGetSlotSnapshot(carriedSlotIndex, out var carriedSlot) &&
            !carriedSlot.IsEmpty)
            return carriedSlot.Item;

        if (carriedEquipmentItem != null && carriedEquipmentQuantity > 0)
            return carriedEquipmentItem;

        if (carriedSplitItem != null && carriedSplitQuantity > 0)
            return carriedSplitItem;

        return null;
    }

    InventoryInteractionState GetInteractionState()
    {
        if (carriedSlotIndex >= 0)
            return InventoryInteractionState.CarryingWholeStack;

        if (carriedSplitItem != null && carriedSplitQuantity > 0)
            return InventoryInteractionState.CarryingSplitStack;

        if (carriedEquipmentItem != null && carriedEquipmentQuantity > 0)
            return InventoryInteractionState.CarryingSplitStack;

        if (splitSourceSlotIndex >= 0)
            return InventoryInteractionState.SplitMenuOpen;

        return InventoryInteractionState.Idle;
    }

    bool IsCarryingWholeSlot()
    {
        return carriedSlotIndex >= 0;
    }

    bool IsCarryingEquipmentItem()
    {
        return carriedEquipmentItem != null && carriedEquipmentQuantity > 0;
    }

    void ClearSplitCarry()
    {
        carriedSplitItem = null;
        carriedSplitQuantity = 0;
        splitSourceSlotIndex = -1;
        carriedSplitOriginSlotIndex = -1;
    }

    void ClearEquipmentCarry()
    {
        carriedEquipmentItem = null;
        carriedEquipmentQuantity = 0;
        carriedEquipmentOriginSlotIndex = -1;
    }

    void ReturnCarriedSplitToOrigin()
    {
        if (activeInventory == null || carriedSplitItem == null || carriedSplitQuantity <= 0 || carriedSplitOriginSlotIndex < 0)
            return;

        int remainder = activeInventory.TryInsertStackAt(carriedSplitOriginSlotIndex, carriedSplitItem, carriedSplitQuantity);
        carriedSplitQuantity = remainder;
    }

    void ReturnCarriedEquipmentToOrigin()
    {
        if (activeEquipment == null || carriedEquipmentItem == null || carriedEquipmentQuantity <= 0 || carriedEquipmentOriginSlotIndex < 0 || activeInventory == null)
            return;

        if (activeEquipment.TryPlaceCarriedItem(carriedEquipmentOriginSlotIndex, carriedEquipmentItem, carriedEquipmentQuantity, activeInventory, out int remainder))
        {
            carriedEquipmentQuantity = remainder;
            if (carriedEquipmentQuantity <= 0)
                ClearEquipmentCarry();
        }
    }

    void UpdateHoveredPreviewTarget(Vector2 panelPosition)
    {
        hoveredSlotIndex = FindHoveredSlotIndex(slotElements, panelPosition);
        hoveredEquipmentSlotIndex = FindHoveredSlotIndex(equipmentSlotElements, panelPosition);
    }

    void RefreshHoveredPreviewTargetFromStoredPointer()
    {
        if (!hasLocalPointerPosition || rootElement == null)
            return;

        Vector2 panelPosition = rootElement.LocalToWorld(localPointerPosition);
        UpdateHoveredPreviewTarget(panelPosition);
    }

    int FindHoveredSlotIndex(List<VisualElement> elements, Vector2 panelPosition)
    {
        if (elements == null)
            return -1;

        for (int i = 0; i < elements.Count; i++)
        {
            VisualElement element = elements[i];
            if (element == null)
                continue;

            if (element.worldBound.Contains(panelPosition))
                return i;
        }

        return -1;
    }

    bool ShouldShowCargoPreview(int slotIndex)
    {
        if (!IsInventoryOpen || activeInventory == null || !IsCarryingAnything())
            return false;

        if (slotIndex < 0 || !activeInventory.TryGetSlotSnapshot(slotIndex, out _))
            return false;

        return true;
    }

    bool ShouldShowCargoGhostPreview(int slotIndex)
    {
        if (!ShouldShowCargoPreview(slotIndex))
            return false;

        if (!activeInventory.TryGetSlotSnapshot(slotIndex, out var slot))
            return false;

        if (slot.IsEmpty)
            return true;

        if (IsCarryingWholeSlot())
            return true;

        if (IsCarryingEquipmentItem())
            return true;

        return false;
    }

    bool ShouldShowEquipmentPreview(int slotIndex)
    {
        if (!IsInventoryOpen || activeEquipment == null || !IsCarryingAnything())
            return false;

        ItemDefinition previewItem = GetCarriedPreviewItem();
        if (previewItem == null)
            return false;

        if (!activeEquipment.TryGetSlotSnapshot(slotIndex, out var slot))
            return false;

        if (!slot.IsInteractive)
            return false;

        return activeEquipment.CanAcceptItemInSlot(slotIndex, previewItem);
    }

    Sprite ResolveItemIcon(ItemDefinition item)
    {
        if (item != null && item.IconSprite != null)
            return item.IconSprite;

        return fallbackNullItemIcon;
    }

    bool IsShiftPressed(bool eventShift)
    {
        Keyboard keyboard = Keyboard.current;
        bool keyboardShift = keyboard != null &&
                             ((keyboard.leftShiftKey != null && keyboard.leftShiftKey.isPressed) ||
                              (keyboard.rightShiftKey != null && keyboard.rightShiftKey.isPressed));
        return eventShift || keyboardShift;
    }

    bool IsElementOrDescendantOf(VisualElement element, VisualElement potentialAncestor)
    {
        VisualElement current = element;
        while (current != null)
        {
            if (current == potentialAncestor)
                return true;

            current = current.parent;
        }

        return false;
    }

    void WarnMissingUiOnce()
    {
        if (warnedMissingUi)
            return;

        Debug.LogWarning("InventoryUIController could not resolve one or more required UI Toolkit elements.", this);
        warnedMissingUi = true;
    }

    void WarnMissingInventoryOnce()
    {
        if (warnedMissingInventory)
            return;

        Debug.LogWarning("InventoryUIController could not find an active ShipInventoryController.", this);
        warnedMissingInventory = true;
    }

    public bool IsPointerOverInventoryOverlay(Vector2 screenPosition)
    {
        if (!IsInventoryOpen || overlayElement == null || overlayElement.panel == null)
            return false;

        Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(overlayElement.panel, screenPosition);
        VisualElement picked = overlayElement.panel.Pick(panelPosition);
        if (picked == null)
            return false;

        return picked == overlayElement || IsElementOrDescendantOf(picked, overlayElement);
    }
}
