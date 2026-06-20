using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class WorldMapUIController : MonoBehaviour
{
    enum MapInteractionMode
    {
        None = 0,
        PlacePurpleX = 1,
        RemoveMarker = 2
    }

    enum NamingPromptMode
    {
        None = 0,
        Creation = 1,
        Rename = 2
    }

    sealed class MarkerVisual
    {
        public VisualElement anchor;
        public VisualElement icon;
        public Label label;
    }

    public static bool IsMapOpen { get; private set; }

    [Header("References")]
    [SerializeField] UIDocument uiDocument;
    [SerializeField] MapDiscoveryController mapDiscoveryController;
    [SerializeField] MapMarkerController mapMarkerController;
    [SerializeField] Transform boatTransform;

    [Header("Sprites")]
    [SerializeField] Sprite boatMarkerSprite;

    [Header("Element Names")]
    [SerializeField] string overlayElementName = "world-map-overlay";
    [SerializeField] string viewElementName = "world-map-view";
    [SerializeField] string imageElementName = "world-map-image";
    [SerializeField] string markerLayerElementName = "world-map-marker-layer";
    [SerializeField] string playerAnchorElementName = "world-map-player-anchor";
    [SerializeField] string playerElementName = "world-map-player";
    [SerializeField] string zoomTrackElementName = "world-map-zoom-track";
    [SerializeField] string zoomThumbElementName = "world-map-zoom-thumb";
    [SerializeField] string placePurpleXButtonElementName = "world-map-place-purplex-button";
    [SerializeField] string removeMarkerButtonElementName = "world-map-remove-marker-button";
    [SerializeField] string showPurpleXToggleElementName = "world-map-show-purplex-toggle";
    [SerializeField] string showRedXToggleElementName = "world-map-show-redx-toggle";
    [SerializeField] string markerNameRowElementName = "world-map-marker-name-row";
    [SerializeField] string markerNameFieldElementName = "world-map-marker-name-field";
    [SerializeField] string markerNameConfirmButtonElementName = "world-map-marker-name-confirm-button";
    [SerializeField] string markerNameCancelButtonElementName = "world-map-marker-name-cancel-button";
    [SerializeField] string cursorCoordinatesElementName = "world-map-cursor-coordinates";

    [Header("Zoom")]
    [SerializeField][Min(16f)] float minHalfViewWorldExtent = 32f;
    [SerializeField][Min(64f)] float initialHalfViewWorldExtent = 24096f;
    [SerializeField][Range(0.1f, 0.95f)] float initialOpenExtentFractionOfWorldRadius = 0.72f;
    [SerializeField][Min(0.01f)] float scrollZoomNormalizedStep = 0.06f;
    [SerializeField][Min(0.01f)] float scrollbarZoomStrength = 1f;
    [SerializeField][Min(128)] int maxRenderTextureSize = 1024;
    [SerializeField] float markerHeadingOffsetDegrees = 0f;

    [Header("Performance")]
    [SerializeField][Range(0.2f, 1f)] float interactionRenderResolutionScale = 0.5f;
    [SerializeField][Min(0.01f)] float interactionRenderSettleSeconds = 0.15f;

    [Header("Markers")]
    [SerializeField][Min(8f)] float markerSelectionRadiusPixels = 18f;
    [SerializeField][Min(8f)] float markerIconSize = 18f;
    [SerializeField][Min(40f)] float markerLabelWidth = 144f;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] float debugCurrentHalfViewWorldExtent;
    [SerializeField] Vector2 debugCurrentViewCenterWorld;
    [SerializeField] int debugRenderTextureSize;
    [SerializeField] float debugZoomNormalized;
    [SerializeField] int debugWorldMapRenderCount;
    [SerializeField] float debugLastWorldMapRenderDurationMs;
    [SerializeField] bool debugInteractionRenderMode;
    [SerializeField] int debugVisibleMarkerCount;
    [SerializeField] int debugPendingMarkerNamingId = -1;
    [SerializeField] string debugInteractionMode = "None";
    [SerializeField] string debugCursorCoordinatesText = "(-,-)";

    VisualElement overlayElement;
    VisualElement viewElement;
    VisualElement markerLayerElement;
    VisualElement playerAnchorElement;
    VisualElement zoomTrackElement;
    VisualElement zoomThumbElement;
    VisualElement markerNameRowElement;
    Label cursorCoordinatesLabel;
    Image imageElement;
    Image playerElement;
    Button placePurpleXButton;
    Button removeMarkerButton;
    Button markerNameConfirmButton;
    Button markerNameCancelButton;
    Toggle showPurpleXToggle;
    Toggle showRedXToggle;
    TextField markerNameField;
    bool zoomDragActive;
    int activeZoomPointerId = -1;
    bool mapPanActive;
    bool mapPanMoved;
    int activeMapPanPointerId = -1;
    Vector2 mapPanStartLocalPosition;
    Vector2 mapPanStartCenterWorld;

    readonly Dictionary<int, MarkerVisual> markerVisuals = new Dictionary<int, MarkerVisual>();
    readonly List<int> markerIdsToRemove = new List<int>();

    Texture2D mapTexture;
    Color32[] mapPixels;
    bool uiReady;
    bool warnedMissingUi;
    bool callbacksRegistered;
    int lastRenderedTextureVersion = -1;
    int lastRenderedTextureWidth = -1;
    int lastRenderedTextureHeight = -1;
    float currentHalfViewWorldExtent;
    Vector2 currentRenderedCenterWorld;
    bool zoomDirty = true;
    bool centerDirty = true;
    CursorLockMode previousCursorLockMode;
    bool previousCursorVisible;
    MapInteractionMode interactionMode;
    int pendingMarkerNamingId = -1;
    bool hasPointerInView;
    Vector2 lastPointerLocalPosition;
    Vector2 requestedViewCenterWorld;
    bool suppressNextMapClick;
    bool pendingInitialLayoutSnap = true;
    double lastRenderInteractionTime = double.NegativeInfinity;

    const float MapPanDragThresholdPixels = 4f;

    void OnEnable()
    {
        TryInitialize();
        SetMapOpen(false, false);
    }

    void Start()
    {
        TryInitialize();
        SetMapOpen(false, false);
    }

    void Update()
    {
        TryInitialize();
        HandleToggleInput();
        if (!uiReady || !IsMapOpen)
            return;

        HandleZoomInput();
        RenderMapIfNeeded();
        UpdateCursorCoordinateDisplay();
    }

    void OnDisable()
    {
        if (IsMapOpen)
            RestoreCursorState();

        DismissNamingPrompt();
        ReleaseZoomPointer();
        SetInteractionMode(MapInteractionMode.None);
        ClearMarkerVisuals();
        IsMapOpen = false;
    }

    void TryInitialize()
    {
        if (uiReady)
            return;

        if (uiDocument == null)
            uiDocument = FindAnyObjectByType<UIDocument>();

        if (mapDiscoveryController == null)
            mapDiscoveryController = MapDiscoveryController.ActiveInstance ?? FindAnyObjectByType<MapDiscoveryController>();

        if (mapMarkerController == null)
            mapMarkerController = MapMarkerController.ActiveInstance ?? FindAnyObjectByType<MapMarkerController>();

        if (boatTransform == null)
        {
            BoatController boatController = FindAnyObjectByType<BoatController>();
            if (boatController != null)
                boatTransform = boatController.transform;
        }

        if (uiDocument == null || mapDiscoveryController == null || mapMarkerController == null || boatTransform == null)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        overlayElement = root.Q(overlayElementName);
        viewElement = root.Q(viewElementName);
        markerLayerElement = root.Q(markerLayerElementName);
        playerAnchorElement = root.Q(playerAnchorElementName);
        imageElement = root.Q<Image>(imageElementName);
        playerElement = root.Q<Image>(playerElementName);
        zoomTrackElement = root.Q(zoomTrackElementName);
        zoomThumbElement = root.Q(zoomThumbElementName);
        placePurpleXButton = root.Q<Button>(placePurpleXButtonElementName);
        removeMarkerButton = root.Q<Button>(removeMarkerButtonElementName);
        showPurpleXToggle = root.Q<Toggle>(showPurpleXToggleElementName);
        showRedXToggle = root.Q<Toggle>(showRedXToggleElementName);
        markerNameRowElement = root.Q(markerNameRowElementName);
        markerNameField = root.Q<TextField>(markerNameFieldElementName);
        markerNameConfirmButton = root.Q<Button>(markerNameConfirmButtonElementName);
        markerNameCancelButton = root.Q<Button>(markerNameCancelButtonElementName);
        cursorCoordinatesLabel = root.Q<Label>(cursorCoordinatesElementName);

        if (overlayElement == null || viewElement == null || markerLayerElement == null || playerAnchorElement == null ||
            imageElement == null || playerElement == null || zoomTrackElement == null || zoomThumbElement == null ||
            placePurpleXButton == null || removeMarkerButton == null || showPurpleXToggle == null || showRedXToggle == null ||
            markerNameRowElement == null || markerNameField == null || markerNameConfirmButton == null || markerNameCancelButton == null ||
            cursorCoordinatesLabel == null)
        {
            if (!warnedMissingUi)
            {
                Debug.LogWarning("[WorldMapUIController] Missing one or more world map UI elements.", this);
                warnedMissingUi = true;
            }
            return;
        }

        ApplyBoatMarkerBackground();
        imageElement.scaleMode = ScaleMode.StretchToFill;
        playerElement.scaleMode = ScaleMode.ScaleToFit;
        markerLayerElement.pickingMode = PickingMode.Ignore;
        RegisterZoomInteractions();
        RegisterUiCallbacks();

        currentHalfViewWorldExtent = ResolveInitialHalfViewExtent();
        Rect initialMapRect = GetMapRect();
        GetViewportHalfExtents(initialMapRect, out float initialHalfViewWidth, out float initialHalfViewHeight);
        requestedViewCenterWorld = mapDiscoveryController.ClampWorldCenterToBounds(
            boatTransform.position,
            initialHalfViewWidth,
            initialHalfViewHeight);
        pendingInitialLayoutSnap = true;
        SyncMarkerVisibilityToggle();
        UpdateModeButtons();
        markerNameRowElement.style.display = DisplayStyle.None;
        uiReady = true;
    }

    void RegisterUiCallbacks()
    {
        if (callbacksRegistered)
            return;

        callbacksRegistered = true;
        placePurpleXButton.clicked += OnPlacePurpleXButtonClicked;
        removeMarkerButton.clicked += OnRemoveMarkerButtonClicked;
        markerNameConfirmButton.clicked += CommitPendingMarkerName;
        markerNameCancelButton.clicked += DismissNamingPrompt;
        showPurpleXToggle.RegisterValueChangedCallback(OnShowPurpleXToggleChanged);
        showRedXToggle.RegisterValueChangedCallback(OnShowRedXToggleChanged);
        markerNameField.RegisterCallback<KeyDownEvent>(OnMarkerNameFieldKeyDown);
        viewElement.RegisterCallback<PointerDownEvent>(OnMapPointerDown);
        viewElement.RegisterCallback<PointerUpEvent>(OnMapPointerUp);
        viewElement.RegisterCallback<ClickEvent>(OnMapClick);
        viewElement.RegisterCallback<PointerMoveEvent>(OnMapPointerMove);
        viewElement.RegisterCallback<PointerLeaveEvent>(OnMapPointerLeave);
        viewElement.RegisterCallback<PointerCaptureOutEvent>(OnMapPointerCaptureOut);
        viewElement.RegisterCallback<GeometryChangedEvent>(OnMapViewGeometryChanged);
    }

    void HandleToggleInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (IsMapOpen && pendingMarkerNamingId >= 0)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
                DismissNamingPrompt();

            return;
        }

        if (keyboard.mKey.wasPressedThisFrame)
        {
            if (!IsMapOpen && (InventoryUIController.IsInventoryOpen || ShopController.IsShopOpen || FishingMinigameController.IsFishingOpen || PauseMenuController.IsPauseOpen || EndMenuController.IsEndMenuOpen))
                return;

            SetMapOpen(!IsMapOpen, true);
            return;
        }

        if (IsMapOpen && keyboard.escapeKey.wasPressedThisFrame)
            SetMapOpen(false, true);
    }

    void HandleZoomInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || mapDiscoveryController == null)
            return;

        float scrollY = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scrollY) < 0.01f)
            return;

        Rect mapRect = GetMapRect();
        if (!HasValidMapLayout(mapRect))
            return;

        Vector2 previousCenterWorld = currentRenderedCenterWorld;

        bool anchorToPointer = hasPointerInView && mapRect.Contains(lastPointerLocalPosition);
        Vector2 anchorWorld = anchorToPointer
            ? MapLocalPositionToWorld(lastPointerLocalPosition, mapRect)
            : previousCenterWorld;

        float scrollStepMagnitude = Mathf.Max(1f, Mathf.Abs(scrollY) / 120f);
        float zoomDelta = Mathf.Sign(scrollY) * scrollStepMagnitude * scrollZoomNormalizedStep;
        float currentZoomNormalized = GetZoomNormalized(currentHalfViewWorldExtent);
        float newZoomNormalized = Mathf.Clamp01(currentZoomNormalized + zoomDelta);
        float newHalfExtent = GetHalfExtentFromNormalized(newZoomNormalized);
        currentHalfViewWorldExtent = Mathf.Clamp(
            newHalfExtent,
            minHalfViewWorldExtent,
            mapDiscoveryController.WorldOuterRadiusTiles);

        if (anchorToPointer)
        {
            GetViewportHalfExtents(mapRect, out float newHalfViewWidth, out float newHalfViewHeight);
            Vector2 normalizedPointer = GetNormalizedMapPosition(lastPointerLocalPosition, mapRect);
            Vector2 anchoredCenterWorld = new Vector2(
                anchorWorld.x - ((normalizedPointer.x - 0.5f) * newHalfViewWidth * 2f),
                anchorWorld.y - ((normalizedPointer.y - 0.5f) * newHalfViewHeight * 2f));
            requestedViewCenterWorld = mapDiscoveryController.ClampWorldCenterToBounds(
                anchoredCenterWorld,
                newHalfViewWidth,
                newHalfViewHeight);
        }
        else
        {
            GetViewportHalfExtents(mapRect, out float newHalfViewWidth, out float newHalfViewHeight);
            requestedViewCenterWorld = mapDiscoveryController.ClampWorldCenterToBounds(
                previousCenterWorld,
                newHalfViewWidth,
                newHalfViewHeight);
        }

        zoomDirty = true;
        centerDirty = true;
        NoteRenderInteraction();
        debugCurrentHalfViewWorldExtent = currentHalfViewWorldExtent;
        debugZoomNormalized = GetZoomNormalized(currentHalfViewWorldExtent);
        UpdateZoomBar();
    }

    float ResolveInitialHalfViewExtent()
    {
        if (mapDiscoveryController == null)
            return Mathf.Max(minHalfViewWorldExtent, initialHalfViewWorldExtent);

        float worldRadius = Mathf.Max(minHalfViewWorldExtent, mapDiscoveryController.WorldOuterRadiusTiles);
        float preferredInitialExtent = Mathf.Min(
            initialHalfViewWorldExtent,
            worldRadius * Mathf.Clamp(initialOpenExtentFractionOfWorldRadius, 0.1f, 0.95f));

        return Mathf.Clamp(preferredInitialExtent, minHalfViewWorldExtent, worldRadius);
    }

    void SetMapOpen(bool shouldOpen, bool manageCursorState)
    {
        IsMapOpen = shouldOpen;
        if (overlayElement != null)
            overlayElement.style.display = shouldOpen ? DisplayStyle.Flex : DisplayStyle.None;

        if (!shouldOpen)
        {
            DismissNamingPrompt();
            SetInteractionMode(MapInteractionMode.None);
            hasPointerInView = false;
            ReleaseZoomPointer();
            ReleaseMapPanPointer();
            SetCursorCoordinatesText("(-,-)");
        }

        if (!manageCursorState)
            return;

        if (shouldOpen)
            CaptureCursorState();
        else
            RestoreCursorState();

        if (shouldOpen)
        {
            pendingInitialLayoutSnap = true;
            centerDirty = true;
            suppressNextMapClick = false;
            SyncMarkerVisibilityToggle();
            ForceRefresh();
        }
    }

    void CaptureCursorState()
    {
        previousCursorLockMode = UnityEngine.Cursor.lockState;
        previousCursorVisible = UnityEngine.Cursor.visible;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }

    void RestoreCursorState()
    {
        UnityEngine.Cursor.lockState = previousCursorLockMode;
        UnityEngine.Cursor.visible = previousCursorVisible;
    }

    void ForceRefresh()
    {
        lastRenderedTextureVersion = -1;
        lastRenderedTextureWidth = -1;
        lastRenderedTextureHeight = -1;
        zoomDirty = true;
        centerDirty = true;
    }

    void RenderMapIfNeeded()
    {
        if (mapDiscoveryController == null || boatTransform == null || viewElement == null || imageElement == null || playerElement == null)
            return;

        Rect mapRect = GetMapRect();
        if (!HasValidMapLayout(mapRect))
            return;

        bool interactionRenderMode = IsUsingInteractionRenderMode();
        debugInteractionRenderMode = interactionRenderMode;
        GetRenderTextureDimensions(mapRect, interactionRenderMode, out int renderTextureWidth, out int renderTextureHeight);
        EnsureRenderTexture(renderTextureWidth, renderTextureHeight);
        GetViewportHalfExtents(mapRect, out float halfViewWidth, out float halfViewHeight);

        bool textureChanged = mapDiscoveryController.TextureVersion != lastRenderedTextureVersion;
        bool sizeChanged = renderTextureWidth != lastRenderedTextureWidth || renderTextureHeight != lastRenderedTextureHeight;
        if (textureChanged || sizeChanged || zoomDirty || centerDirty)
        {
            Vector2 clampedCenter = mapDiscoveryController.ClampWorldCenterToBounds(
                requestedViewCenterWorld,
                halfViewWidth,
                halfViewHeight);
            double startTime = Time.realtimeSinceStartupAsDouble;
            mapDiscoveryController.RenderViewport(
                clampedCenter,
                halfViewWidth,
                halfViewHeight,
                mapTexture,
                mapPixels);
            debugLastWorldMapRenderDurationMs = (float)((Time.realtimeSinceStartupAsDouble - startTime) * 1000.0);

            imageElement.image = mapTexture;
            ApplyMapLayout(mapRect);

            Vector2 normalizedPosition = mapDiscoveryController.GetNormalizedPositionInViewport(
                boatTransform.position,
                clampedCenter,
                halfViewWidth,
                halfViewHeight);
            PositionPlayerMarker(mapRect, normalizedPosition);

            lastRenderedTextureVersion = mapDiscoveryController.TextureVersion;
            lastRenderedTextureWidth = renderTextureWidth;
            lastRenderedTextureHeight = renderTextureHeight;
            currentRenderedCenterWorld = clampedCenter;
            requestedViewCenterWorld = clampedCenter;
            zoomDirty = false;
            centerDirty = false;
            debugWorldMapRenderCount++;

            debugCurrentHalfViewWorldExtent = currentHalfViewWorldExtent;
            debugCurrentViewCenterWorld = clampedCenter;
            debugRenderTextureSize = Mathf.Max(renderTextureWidth, renderTextureHeight);
            UpdateZoomBar();
            RenderMarkers(mapRect);
            return;
        }

        PositionPlayerMarker(
            mapRect,
            mapDiscoveryController.GetNormalizedPositionInViewport(
                boatTransform.position,
                currentRenderedCenterWorld,
                halfViewWidth,
                halfViewHeight));
        UpdateZoomBar();
        RenderMarkers(mapRect);
    }

    void OnMapViewGeometryChanged(GeometryChangedEvent _)
    {
        if (!IsMapOpen || !pendingInitialLayoutSnap || mapDiscoveryController == null || boatTransform == null)
            return;

        Rect mapRect = GetMapRect();
        if (!HasValidMapLayout(mapRect))
            return;

        GetViewportHalfExtents(mapRect, out float halfViewWidth, out float halfViewHeight);
        requestedViewCenterWorld = mapDiscoveryController.ClampWorldCenterToBounds(
            boatTransform.position,
            halfViewWidth,
            halfViewHeight);
        pendingInitialLayoutSnap = false;
        centerDirty = true;
        ForceRefresh();
    }

    void EnsureRenderTexture(int width, int height)
    {
        if (mapTexture != null && mapTexture.width == width && mapTexture.height == height &&
            mapPixels != null && mapPixels.Length == width * height)
            return;

        mapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "WorldMapViewportTexture"
        };
        mapPixels = new Color32[width * height];
    }

    void PositionPlayerMarker(Rect mapRect, Vector2 normalizedPosition)
    {
        playerAnchorElement.style.top = mapRect.yMin + ((1f - normalizedPosition.y) * mapRect.height);
        playerAnchorElement.style.left = mapRect.xMin + (normalizedPosition.x * mapRect.width);

        float markerWidth = Mathf.Max(1f, playerElement.resolvedStyle.width);
        float markerHeight = Mathf.Max(1f, playerElement.resolvedStyle.height);
        playerElement.style.left = -markerWidth * 0.5f;
        playerElement.style.top = -markerHeight * 0.5f;
#pragma warning disable CS0618
        playerElement.transform.rotation = Quaternion.Euler(0f, 0f, -boatTransform.eulerAngles.z + markerHeadingOffsetDegrees);
#pragma warning restore CS0618
    }

    void RenderMarkers(Rect mapRect)
    {
        if (mapMarkerController == null || markerLayerElement == null)
            return;

        IReadOnlyList<MapMarkerController.MarkerRecord> markers = mapMarkerController.Markers;
        markerIdsToRemove.Clear();
        foreach (KeyValuePair<int, MarkerVisual> pair in markerVisuals)
            markerIdsToRemove.Add(pair.Key);

        debugVisibleMarkerCount = 0;
        for (int i = 0; i < markers.Count; i++)
        {
            MapMarkerController.MarkerRecord marker = markers[i];
            if (!mapMarkerController.IsTypeVisible(marker.markerType))
                continue;

            MapMarkerController.MarkerTypeDefinition definition = mapMarkerController.GetTypeDefinition(marker.markerType);
            if (definition.sprite == null)
                continue;

            if (debugZoomNormalized < definition.iconVisibleZoomThreshold)
                continue;

            if (!TryProjectMarkerToMap(marker.worldPosition, mapRect, out Vector2 anchorPosition))
                continue;

            MarkerVisual visual = GetOrCreateMarkerVisual(marker.id, definition.sprite);
            visual.anchor.style.display = DisplayStyle.Flex;
            visual.anchor.style.left = anchorPosition.x;
            visual.anchor.style.top = anchorPosition.y;
            visual.label.text = marker.name;
            visual.label.style.display = debugZoomNormalized >= definition.labelVisibleZoomThreshold
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            debugVisibleMarkerCount++;
            markerIdsToRemove.Remove(marker.id);
        }

        for (int i = 0; i < markerIdsToRemove.Count; i++)
            DestroyMarkerVisual(markerIdsToRemove[i]);
    }

    MarkerVisual GetOrCreateMarkerVisual(int markerId, Sprite sprite)
    {
        if (markerVisuals.TryGetValue(markerId, out MarkerVisual existingVisual))
        {
            existingVisual.icon.style.backgroundImage = new StyleBackground(sprite);
            return existingVisual;
        }

        MarkerVisual visual = new MarkerVisual
        {
            anchor = new VisualElement(),
            icon = new VisualElement(),
            label = new Label()
        };

        visual.anchor.style.position = Position.Absolute;
        visual.anchor.style.left = 0f;
        visual.anchor.style.top = 0f;
        visual.anchor.style.width = 0f;
        visual.anchor.style.height = 0f;
        visual.anchor.pickingMode = PickingMode.Ignore;

        visual.icon.style.position = Position.Absolute;
        visual.icon.style.left = -markerIconSize * 0.5f;
        visual.icon.style.top = -markerIconSize * 0.5f;
        visual.icon.style.width = markerIconSize;
        visual.icon.style.height = markerIconSize;
        visual.icon.style.backgroundImage = new StyleBackground(sprite);
        visual.icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        visual.icon.pickingMode = PickingMode.Ignore;

        visual.label.style.position = Position.Absolute;
        visual.label.style.left = -(markerLabelWidth * 0.5f);
        visual.label.style.top = markerIconSize * 0.45f;
        visual.label.style.width = markerLabelWidth;
        visual.label.style.minHeight = 18f;
        visual.label.style.color = new StyleColor(new Color(0.97f, 0.95f, 0.83f, 1f));
        visual.label.style.backgroundColor = new StyleColor(new Color(0.08f, 0.11f, 0.18f, 0.86f));
        visual.label.style.paddingLeft = 4f;
        visual.label.style.paddingRight = 4f;
        visual.label.style.paddingTop = 2f;
        visual.label.style.paddingBottom = 2f;
        visual.label.style.fontSize = 12f;
        visual.label.style.unityTextAlign = TextAnchor.MiddleCenter;
        visual.label.style.borderTopWidth = 1f;
        visual.label.style.borderRightWidth = 1f;
        visual.label.style.borderBottomWidth = 1f;
        visual.label.style.borderLeftWidth = 1f;
        visual.label.style.borderTopColor = new StyleColor(new Color(0.52f, 0.58f, 0.73f, 1f));
        visual.label.style.borderRightColor = new StyleColor(new Color(0.52f, 0.58f, 0.73f, 1f));
        visual.label.style.borderBottomColor = new StyleColor(new Color(0.52f, 0.58f, 0.73f, 1f));
        visual.label.style.borderLeftColor = new StyleColor(new Color(0.52f, 0.58f, 0.73f, 1f));
        visual.label.pickingMode = PickingMode.Ignore;

        visual.anchor.Add(visual.icon);
        visual.anchor.Add(visual.label);
        markerLayerElement.Add(visual.anchor);
        markerVisuals.Add(markerId, visual);
        return visual;
    }

    void DestroyMarkerVisual(int markerId)
    {
        if (!markerVisuals.TryGetValue(markerId, out MarkerVisual visual))
            return;

        visual.anchor.RemoveFromHierarchy();
        markerVisuals.Remove(markerId);
    }

    void ClearMarkerVisuals()
    {
        foreach (KeyValuePair<int, MarkerVisual> pair in markerVisuals)
            pair.Value.anchor.RemoveFromHierarchy();

        markerVisuals.Clear();
        markerIdsToRemove.Clear();
        debugVisibleMarkerCount = 0;
    }

    bool TryProjectMarkerToMap(Vector2 worldPosition, Rect mapRect, out Vector2 anchorPosition)
    {
        GetViewportHalfExtents(mapRect, out float halfViewWidth, out float halfViewHeight);
        float minX = currentRenderedCenterWorld.x - halfViewWidth;
        float maxX = currentRenderedCenterWorld.x + halfViewWidth;
        float minY = currentRenderedCenterWorld.y - halfViewHeight;
        float maxY = currentRenderedCenterWorld.y + halfViewHeight;
        if (worldPosition.x < minX || worldPosition.x > maxX || worldPosition.y < minY || worldPosition.y > maxY)
        {
            anchorPosition = default;
            return false;
        }

        float normalizedX = Mathf.InverseLerp(minX, maxX, worldPosition.x);
        float normalizedY = Mathf.InverseLerp(minY, maxY, worldPosition.y);
        anchorPosition = new Vector2(
            mapRect.xMin + (normalizedX * mapRect.width),
            mapRect.yMin + ((1f - normalizedY) * mapRect.height));
        return true;
    }

    void UpdateZoomBar()
    {
        if (zoomTrackElement == null || zoomThumbElement == null)
            return;

        float trackHeight = zoomTrackElement.layout.height;
        if (trackHeight <= 1f)
            trackHeight = zoomTrackElement.worldBound.height;

        float thumbHeight = zoomThumbElement.layout.height;
        if (thumbHeight <= 1f)
            thumbHeight = zoomThumbElement.worldBound.height;

        if (trackHeight <= 1f || thumbHeight <= 1f)
            return;

        float zoomNormalized = GetZoomNormalized(currentHalfViewWorldExtent);
        debugZoomNormalized = zoomNormalized;

        float usableTravel = Mathf.Max(0f, trackHeight - thumbHeight);
        float thumbTop = usableTravel * (1f - zoomNormalized);
        zoomThumbElement.style.top = thumbTop;
    }

    void RegisterZoomInteractions()
    {
        if (zoomTrackElement == null || zoomThumbElement == null)
            return;

        zoomTrackElement.RegisterCallback<PointerDownEvent>(OnZoomPointerDown);
        zoomTrackElement.RegisterCallback<PointerMoveEvent>(OnZoomPointerMove);
        zoomTrackElement.RegisterCallback<PointerUpEvent>(OnZoomPointerUp);
        zoomTrackElement.RegisterCallback<PointerCaptureOutEvent>(OnZoomPointerCaptureOut);

        zoomThumbElement.RegisterCallback<PointerDownEvent>(OnZoomPointerDown);
        zoomThumbElement.RegisterCallback<PointerMoveEvent>(OnZoomPointerMove);
        zoomThumbElement.RegisterCallback<PointerUpEvent>(OnZoomPointerUp);
        zoomThumbElement.RegisterCallback<PointerCaptureOutEvent>(OnZoomPointerCaptureOut);
    }

    void OnZoomPointerDown(PointerDownEvent evt)
    {
        if (!IsMapOpen || zoomTrackElement == null)
            return;

        zoomDragActive = true;
        activeZoomPointerId = evt.pointerId;
        zoomTrackElement.CapturePointer(evt.pointerId);
        NoteRenderInteraction();
        ApplyZoomFromTrackPosition(evt.localPosition.y, evt.currentTarget as VisualElement);
        evt.StopPropagation();
    }

    void OnZoomPointerMove(PointerMoveEvent evt)
    {
        if (!zoomDragActive || evt.pointerId != activeZoomPointerId)
            return;

        ApplyZoomFromTrackPosition(evt.localPosition.y, evt.currentTarget as VisualElement);
        evt.StopPropagation();
    }

    void OnZoomPointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId != activeZoomPointerId)
            return;

        ReleaseZoomPointer();
        evt.StopPropagation();
    }

    void OnZoomPointerCaptureOut(PointerCaptureOutEvent _)
    {
        ReleaseZoomPointer();
    }

    void ReleaseZoomPointer()
    {
        if (zoomTrackElement != null && activeZoomPointerId >= 0 && zoomTrackElement.HasPointerCapture(activeZoomPointerId))
            zoomTrackElement.ReleasePointer(activeZoomPointerId);

        zoomDragActive = false;
        activeZoomPointerId = -1;
    }

    void ApplyZoomFromTrackPosition(float localY, VisualElement eventSource)
    {
        if (zoomTrackElement == null || mapDiscoveryController == null)
            return;

        float trackHeight = zoomTrackElement.layout.height;
        if (trackHeight <= 1f)
            trackHeight = zoomTrackElement.worldBound.height;

        float yInTrack = localY;

        if (eventSource == zoomThumbElement)
        {
            float thumbWorldTop = zoomThumbElement.worldBound.yMin;
            float trackWorldTop = zoomTrackElement.worldBound.yMin;
            yInTrack = (thumbWorldTop - trackWorldTop) + localY;
        }

        float normalized = 1f - Mathf.Clamp01(yInTrack / Mathf.Max(1f, trackHeight));
        float strengthenedNormalized = Mathf.Clamp01(Mathf.Pow(normalized, Mathf.Max(0.01f, scrollbarZoomStrength)));
        float newHalfExtent = GetHalfExtentFromNormalized(strengthenedNormalized);
        currentHalfViewWorldExtent = Mathf.Clamp(newHalfExtent, minHalfViewWorldExtent, mapDiscoveryController.WorldOuterRadiusTiles);
        zoomDirty = true;
        NoteRenderInteraction();
        debugZoomNormalized = strengthenedNormalized;
        UpdateZoomBar();
    }

    void NoteRenderInteraction()
    {
        lastRenderInteractionTime = Time.realtimeSinceStartupAsDouble;
    }

    bool IsUsingInteractionRenderMode()
    {
        if (mapPanActive && mapPanMoved)
            return true;

        if (zoomDragActive)
            return true;

        return Time.realtimeSinceStartupAsDouble - lastRenderInteractionTime <= Mathf.Max(0.01f, interactionRenderSettleSeconds);
    }

    float GetZoomNormalized(float halfExtent)
    {
        if (mapDiscoveryController == null)
            return 0f;

        float minExtent = Mathf.Max(0.0001f, minHalfViewWorldExtent);
        float maxExtent = Mathf.Max(minExtent + 0.0001f, mapDiscoveryController.WorldOuterRadiusTiles);
        float clampedExtent = Mathf.Clamp(halfExtent, minExtent, maxExtent);
        float logMin = Mathf.Log(minExtent);
        float logMax = Mathf.Log(maxExtent);
        float logExtent = Mathf.Log(clampedExtent);
        return 1f - Mathf.InverseLerp(logMin, logMax, logExtent);
    }

    float GetHalfExtentFromNormalized(float normalized)
    {
        if (mapDiscoveryController == null)
            return minHalfViewWorldExtent;

        float minExtent = Mathf.Max(0.0001f, minHalfViewWorldExtent);
        float maxExtent = Mathf.Max(minExtent + 0.0001f, mapDiscoveryController.WorldOuterRadiusTiles);
        float logMin = Mathf.Log(minExtent);
        float logMax = Mathf.Log(maxExtent);
        float logExtent = Mathf.Lerp(logMax, logMin, Mathf.Clamp01(normalized));
        return Mathf.Exp(logExtent);
    }

    void ApplyBoatMarkerBackground()
    {
        if (playerElement == null || boatMarkerSprite == null)
            return;

        playerElement.image = boatMarkerSprite.texture;
        playerElement.tintColor = Color.white;
    }

    void ApplyMapLayout(Rect mapRect)
    {
        if (imageElement == null)
            return;

        imageElement.style.left = mapRect.xMin;
        imageElement.style.top = mapRect.yMin;
        imageElement.style.width = mapRect.width;
        imageElement.style.height = mapRect.height;
    }

    void UpdateCursorCoordinateDisplay()
    {
        if (!IsMapOpen || viewElement == null || !hasPointerInView)
        {
            SetCursorCoordinatesText("(-,-)");
            return;
        }

        Rect mapRect = GetMapRect();
        if (!mapRect.Contains(lastPointerLocalPosition))
        {
            SetCursorCoordinatesText("(-,-)");
            return;
        }

        Vector2 worldPosition = MapLocalPositionToWorld(lastPointerLocalPosition, mapRect);
        SetCursorCoordinatesText($"({Mathf.RoundToInt(worldPosition.x)},{Mathf.RoundToInt(worldPosition.y)})");
    }

    void SetCursorCoordinatesText(string text)
    {
        debugCursorCoordinatesText = text;
        if (cursorCoordinatesLabel != null)
            cursorCoordinatesLabel.text = text;
    }

    void OnMapPointerMove(PointerMoveEvent evt)
    {
        hasPointerInView = true;
        Vector2 localPosition = evt.localPosition;
        lastPointerLocalPosition = localPosition;

        if (!mapPanActive || evt.pointerId != activeMapPanPointerId || interactionMode != MapInteractionMode.None || pendingMarkerNamingId >= 0)
            return;

        Rect mapRect = GetMapRect();
        float mapWidth = Mathf.Max(1f, mapRect.width);
        float mapHeight = Mathf.Max(1f, mapRect.height);
        GetViewportHalfExtents(mapRect, out float halfViewWidth, out float halfViewHeight);
        Vector2 localDelta = localPosition - mapPanStartLocalPosition;
        if (!mapPanMoved && localDelta.sqrMagnitude >= MapPanDragThresholdPixels * MapPanDragThresholdPixels)
        {
            mapPanMoved = true;
            suppressNextMapClick = true;
        }

        if (!mapPanMoved)
            return;

        Vector2 worldDelta = new Vector2(
            -(localDelta.x / mapWidth) * halfViewWidth * 2f,
            (localDelta.y / mapHeight) * halfViewHeight * 2f);
        requestedViewCenterWorld = mapDiscoveryController.ClampWorldCenterToBounds(
            mapPanStartCenterWorld + worldDelta,
            halfViewWidth,
            halfViewHeight);
        centerDirty = true;
        NoteRenderInteraction();
    }

    void OnMapPointerLeave(PointerLeaveEvent _)
    {
        hasPointerInView = false;
        SetCursorCoordinatesText("(-,-)");
    }

    void OnMapPointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId != activeMapPanPointerId)
            return;

        ReleaseMapPanPointer();
    }

    void OnMapPointerCaptureOut(PointerCaptureOutEvent _)
    {
        ReleaseMapPanPointer();
    }

    Rect GetMapRect()
    {
        if (viewElement == null)
            return new Rect(0f, 0f, 1f, 1f);

        float viewWidth = Mathf.Max(1f, viewElement.resolvedStyle.width);
        float viewHeight = Mathf.Max(1f, viewElement.resolvedStyle.height);

        float reservedRightWidth = 0f;
        if (zoomTrackElement != null)
        {
            float zoomTrackWidth = Mathf.Max(0f, zoomTrackElement.resolvedStyle.width);
            float rightInset = Mathf.Max(0f, viewWidth - zoomTrackElement.worldBound.xMax + viewElement.worldBound.xMin);
            reservedRightWidth = zoomTrackWidth + rightInset + 12f;
        }

        float usableWidth = Mathf.Max(1f, viewWidth - reservedRightWidth);
        float left = 0f;
        float top = 0f;
        return new Rect(left, top, usableWidth, viewHeight);
    }

    bool HasValidMapLayout(Rect mapRect)
    {
        if (mapRect.width <= 1f || mapRect.height <= 1f)
            return false;

        if (viewElement == null || viewElement.worldBound.width <= 1f || viewElement.worldBound.height <= 1f)
            return false;

        if (zoomTrackElement != null && zoomTrackElement.worldBound.height <= 1f)
            return false;

        return true;
    }

    void GetViewportHalfExtents(Rect mapRect, out float halfViewWidth, out float halfViewHeight)
    {
        halfViewHeight = currentHalfViewWorldExtent;
        float aspect = Mathf.Max(0.01f, mapRect.width / Mathf.Max(1f, mapRect.height));
        halfViewWidth = currentHalfViewWorldExtent * aspect;
    }

    void GetRenderTextureDimensions(Rect mapRect, bool interactionRenderMode, out int width, out int height)
    {
        float dominant = Mathf.Max(1f, Mathf.Max(mapRect.width, mapRect.height));
        float resolutionCap = maxRenderTextureSize;
        if (interactionRenderMode)
            resolutionCap *= Mathf.Clamp(interactionRenderResolutionScale, 0.2f, 1f);

        float scale = Mathf.Min(1f, resolutionCap / dominant);
        width = Mathf.Clamp(Mathf.RoundToInt(mapRect.width * scale), 128, maxRenderTextureSize);
        height = Mathf.Clamp(Mathf.RoundToInt(mapRect.height * scale), 128, maxRenderTextureSize);
    }

    void OnPlacePurpleXButtonClicked()
    {
        if (!IsMapOpen)
            return;

        if (pendingMarkerNamingId >= 0)
            return;

        DismissNamingPrompt();
        SetInteractionMode(interactionMode == MapInteractionMode.PlacePurpleX ? MapInteractionMode.None : MapInteractionMode.PlacePurpleX);
    }

    void OnRemoveMarkerButtonClicked()
    {
        if (!IsMapOpen)
            return;

        if (pendingMarkerNamingId >= 0)
            return;

        DismissNamingPrompt();
        SetInteractionMode(interactionMode == MapInteractionMode.RemoveMarker ? MapInteractionMode.None : MapInteractionMode.RemoveMarker);
    }

    void OnShowPurpleXToggleChanged(ChangeEvent<bool> evt)
    {
        if (mapMarkerController == null)
            return;

        if (pendingMarkerNamingId >= 0)
        {
            showPurpleXToggle?.SetValueWithoutNotify(mapMarkerController.IsTypeVisible(MapMarkerController.MarkerType.PurpleX));
            return;
        }

        mapMarkerController.SetTypeVisible(MapMarkerController.MarkerType.PurpleX, evt.newValue);
    }

    void OnShowRedXToggleChanged(ChangeEvent<bool> evt)
    {
        if (mapMarkerController == null)
            return;

        if (pendingMarkerNamingId >= 0)
        {
            showRedXToggle?.SetValueWithoutNotify(mapMarkerController.IsTypeVisible(MapMarkerController.MarkerType.RedX));
            return;
        }

        mapMarkerController.SetTypeVisible(MapMarkerController.MarkerType.RedX, evt.newValue);
    }

    void OnMarkerNameFieldKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
        {
            CommitPendingMarkerName();
            evt.StopPropagation();
        }
        else if (evt.keyCode == KeyCode.Escape)
        {
            DismissNamingPrompt();
            evt.StopPropagation();
        }
    }

    void OnMapPointerDown(PointerDownEvent evt)
    {
        if (!IsMapOpen || evt.button != 0)
            return;

        if (pendingMarkerNamingId >= 0)
        {
            evt.StopPropagation();
            return;
        }

        Rect mapRect = GetMapRect();
        Vector2 localPosition = evt.localPosition;
        if (!mapRect.Contains(localPosition))
            return;

        if (interactionMode == MapInteractionMode.None)
        {
            BeginMapPan(evt.pointerId, localPosition);
            return;
        }

        if (interactionMode == MapInteractionMode.PlacePurpleX)
        {
            Vector2 worldPosition = MapLocalPositionToWorld(localPosition, mapRect);
            worldPosition = mapDiscoveryController.ClampWorldPositionToBounds(worldPosition);
            MapMarkerController.MarkerRecord marker = mapMarkerController.AddMarker(MapMarkerController.MarkerType.PurpleX, worldPosition);
            SetInteractionMode(MapInteractionMode.None);
            ShowNamingPrompt(marker, NamingPromptMode.Creation);
            RenderMarkers(mapRect);
            evt.StopPropagation();
            return;
        }

        if (interactionMode == MapInteractionMode.RemoveMarker &&
            TryFindMarkerAtMapPosition(localPosition, mapRect, out int markerId, true))
        {
            mapMarkerController.RemoveMarker(markerId);
            SetInteractionMode(MapInteractionMode.None);
            RenderMarkers(mapRect);
            evt.StopPropagation();
        }
    }

    void OnMapClick(ClickEvent evt)
    {
        if (!IsMapOpen || pendingMarkerNamingId >= 0 || interactionMode != MapInteractionMode.None)
            return;

        if (suppressNextMapClick)
        {
            suppressNextMapClick = false;
            return;
        }

        if (evt.button != 0 || evt.clickCount < 2)
            return;

        Rect mapRect = GetMapRect();
        Vector2 localPosition = evt.localPosition;
        if (!mapRect.Contains(localPosition))
            return;

        if (!TryFindMarkerAtMapPosition(localPosition, mapRect, out int renameMarkerId, false))
            return;

        if (!TryGetMarkerById(renameMarkerId, out MapMarkerController.MarkerRecord marker))
            return;

        ShowNamingPrompt(marker, NamingPromptMode.Rename);
        evt.StopPropagation();
    }

    bool TryGetMarkerById(int markerId, out MapMarkerController.MarkerRecord markerRecord)
    {
        markerRecord = default;
        if (mapMarkerController == null)
            return false;

        IReadOnlyList<MapMarkerController.MarkerRecord> markers = mapMarkerController.Markers;
        for (int i = 0; i < markers.Count; i++)
        {
            if (markers[i].id != markerId)
                continue;

            markerRecord = markers[i];
            return true;
        }

        return false;
    }

    Vector2 MapLocalPositionToWorld(Vector2 localPosition, Rect mapRect)
    {
        Vector2 normalizedPosition = GetNormalizedMapPosition(localPosition, mapRect);
        GetViewportHalfExtents(mapRect, out float halfViewWidth, out float halfViewHeight);
        float worldX = currentRenderedCenterWorld.x + ((normalizedPosition.x - 0.5f) * halfViewWidth * 2f);
        float worldY = currentRenderedCenterWorld.y + ((normalizedPosition.y - 0.5f) * halfViewHeight * 2f);
        return new Vector2(worldX, worldY);
    }

    Vector2 GetNormalizedMapPosition(Vector2 localPosition, Rect mapRect)
    {
        float normalizedX = Mathf.Clamp01((localPosition.x - mapRect.xMin) / Mathf.Max(1f, mapRect.width));
        float normalizedY = 1f - Mathf.Clamp01((localPosition.y - mapRect.yMin) / Mathf.Max(1f, mapRect.height));
        return new Vector2(normalizedX, normalizedY);
    }

    bool TryFindMarkerAtMapPosition(Vector2 localPosition, Rect mapRect, out int markerId, bool requireManualRemove)
    {
        markerId = -1;
        if (mapMarkerController == null)
            return false;

        float closestDistance = float.MaxValue;
        IReadOnlyList<MapMarkerController.MarkerRecord> markers = mapMarkerController.Markers;
        for (int i = 0; i < markers.Count; i++)
        {
            MapMarkerController.MarkerRecord marker = markers[i];
            if (!mapMarkerController.IsTypeVisible(marker.markerType))
                continue;

            MapMarkerController.MarkerTypeDefinition definition = mapMarkerController.GetTypeDefinition(marker.markerType);
            if (requireManualRemove)
            {
                if (!definition.allowManualRemove)
                    continue;
            }
            else if (!definition.allowManualRename)
            {
                continue;
            }

            if (debugZoomNormalized < definition.iconVisibleZoomThreshold)
                continue;

            if (!TryProjectMarkerToMap(marker.worldPosition, mapRect, out Vector2 anchorPosition))
                continue;

            float distance = Vector2.Distance(localPosition, anchorPosition);
            if (distance > markerSelectionRadiusPixels || distance >= closestDistance)
                continue;

            closestDistance = distance;
            markerId = marker.id;
        }

        return markerId >= 0;
    }

    void ShowNamingPrompt(MapMarkerController.MarkerRecord marker, NamingPromptMode promptMode)
    {
        pendingMarkerNamingId = marker.id;
        debugPendingMarkerNamingId = pendingMarkerNamingId;
        markerNameRowElement.style.display = DisplayStyle.Flex;
        markerNameField.value = marker.name;
        if (markerNameCancelButton != null)
            markerNameCancelButton.text = promptMode == NamingPromptMode.Creation ? "Keep Default" : "Cancel";

        markerNameField.schedule.Execute(() =>
        {
            markerNameField.Focus();
            markerNameField.SelectAll();
        });
    }

    void CommitPendingMarkerName()
    {
        if (pendingMarkerNamingId < 0 || mapMarkerController == null)
        {
            DismissNamingPrompt();
            return;
        }

        mapMarkerController.RenameMarker(pendingMarkerNamingId, markerNameField.value);
        DismissNamingPrompt();
    }

    void DismissNamingPrompt()
    {
        pendingMarkerNamingId = -1;
        debugPendingMarkerNamingId = -1;

        if (markerNameRowElement != null)
            markerNameRowElement.style.display = DisplayStyle.None;

        if (markerNameCancelButton != null)
            markerNameCancelButton.text = "Keep Default";
    }

    void SetInteractionMode(MapInteractionMode mode)
    {
        interactionMode = mode;
        debugInteractionMode = mode.ToString();
        if (mode != MapInteractionMode.None)
            ReleaseMapPanPointer();
        UpdateModeButtons();
    }

    void UpdateModeButtons()
    {
        if (placePurpleXButton != null)
            placePurpleXButton.text = interactionMode == MapInteractionMode.PlacePurpleX ? "Placing Purple X..." : "Place Purple X";

        if (removeMarkerButton != null)
            removeMarkerButton.text = interactionMode == MapInteractionMode.RemoveMarker ? "Removing..." : "Remove Marker";
    }

    void SyncMarkerVisibilityToggle()
    {
        if (showPurpleXToggle == null || showRedXToggle == null || mapMarkerController == null)
            return;

        showPurpleXToggle.SetValueWithoutNotify(mapMarkerController.IsTypeVisible(MapMarkerController.MarkerType.PurpleX));
        showRedXToggle.SetValueWithoutNotify(mapMarkerController.IsTypeVisible(MapMarkerController.MarkerType.RedX));
    }

    void BeginMapPan(int pointerId, Vector2 localPosition)
    {
        if (viewElement == null)
            return;

        mapPanActive = true;
        mapPanMoved = false;
        activeMapPanPointerId = pointerId;
        mapPanStartLocalPosition = localPosition;
        mapPanStartCenterWorld = requestedViewCenterWorld;
        viewElement.CapturePointer(pointerId);
    }

    void ReleaseMapPanPointer()
    {
        if (viewElement != null && activeMapPanPointerId >= 0 && viewElement.HasPointerCapture(activeMapPanPointerId))
            viewElement.ReleasePointer(activeMapPanPointerId);

        mapPanActive = false;
        mapPanMoved = false;
        activeMapPanPointerId = -1;
    }
}
