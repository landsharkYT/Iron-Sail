using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class MinimapUIController : MonoBehaviour
{
    sealed class MarkerVisual
    {
        public VisualElement anchor;
        public VisualElement icon;
    }

    [Header("References")]
    [SerializeField] UIDocument uiDocument;
    [SerializeField] MapDiscoveryController mapDiscoveryController;
    [SerializeField] MapMarkerController mapMarkerController;
    [SerializeField] Transform boatTransform;

    [Header("Sprites")]
    [SerializeField] Sprite mapCircleSprite;
    [SerializeField] Sprite mapCircleInnerSprite;
    [SerializeField] Sprite boatMarkerSprite;

    [Header("Element Names")]
    [SerializeField] string minimapRootElementName = "minimap-root";
    [SerializeField] string minimapMaskElementName = "minimap-mask";
    [SerializeField] string minimapInnerElementName = "minimap-circle-inner";
    [SerializeField] string minimapImageElementName = "minimap-image";
    [SerializeField] string minimapMarkerLayerElementName = "minimap-marker-layer";
    [SerializeField] string minimapFrameElementName = "minimap-frame";
    [SerializeField] string minimapPlayerAnchorElementName = "minimap-player-anchor";
    [SerializeField] string minimapPlayerElementName = "minimap-player";

    [Header("View")]
    [SerializeField][Min(16f)] float minimapWorldRadius = 120f;
    [SerializeField][Min(32)] int minimapTextureSize = 160;
    [SerializeField] bool visibleByDefault = true;
    [SerializeField] float markerHeadingOffsetDegrees = 0f;
    [SerializeField][Range(0.1f, 1f)] float textureRefreshPixelThreshold = 0.5f;

    [Header("Markers")]
    [SerializeField][Min(6f)] float minimapMarkerIconSize = 12f;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] Vector3Int debugBoatTileCell;
    [SerializeField] float debugRenderedWorldRadius;
    [SerializeField] Vector2 debugRenderedCenterWorld;
    [SerializeField] Vector2 debugBoatNormalizedInViewport = new Vector2(0.5f, 0.5f);
    [SerializeField] Vector2 debugMarkerPosition;
    [SerializeField] Rect debugMaskLayout;
    [SerializeField] int debugMinimapTextureVersion = -1;
    [SerializeField] float debugMinimapRefreshThresholdWorld;
    [SerializeField] int debugMinimapRenderCount;
    [SerializeField] int debugMinimapSkippedRenderCount;
    [SerializeField] float debugLastMinimapRenderDurationMs;
    [SerializeField] int debugVisibleMarkerCount;

    VisualElement minimapRoot;
    VisualElement minimapMask;
    VisualElement minimapInner;
    VisualElement minimapImage;
    VisualElement minimapMarkerLayer;
    VisualElement minimapFrame;
    VisualElement minimapPlayerAnchor;
    VisualElement minimapPlayerShadow;
    VisualElement minimapPlayer;

    readonly Dictionary<int, MarkerVisual> markerVisuals = new Dictionary<int, MarkerVisual>();
    readonly List<int> markerIdsToRemove = new List<int>();

    Texture2D minimapTexture;
    Color32[] minimapPixels;
    bool uiReady;
    bool warnedMissingUi;
    bool hudVisible;
    int lastRenderedTextureVersion = -1;
    int lastRenderedTextureSize = -1;
    Vector2 currentRenderedCenterWorld;
    bool hasRenderedTexture;
    Vector2 lastMaskSize;
    float lastRenderedWorldRadius = -1f;

    void OnEnable()
    {
        hudVisible = visibleByDefault;
        TryInitialize();
        ApplyVisibility();
    }

    void Start()
    {
        hudVisible = visibleByDefault;
        TryInitialize();
        ApplyVisibility();
        ForceRefresh();
    }

    void Update()
    {
        TryInitialize();
        HandleHudToggleInput();
        if (!uiReady || !hudVisible || WorldMapUIController.IsMapOpen)
            return;

        UpdateMinimap();
    }

    void OnDisable()
    {
        ClearMarkerVisuals();
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
        minimapRoot = root.Q(minimapRootElementName);
        minimapMask = root.Q(minimapMaskElementName);
        minimapInner = root.Q(minimapInnerElementName);
        minimapImage = root.Q(minimapImageElementName);
        minimapMarkerLayer = root.Q(minimapMarkerLayerElementName);
        minimapFrame = root.Q(minimapFrameElementName);
        minimapPlayerAnchor = root.Q(minimapPlayerAnchorElementName);
        minimapPlayerShadow = root.Q("minimap-player-shadow");
        minimapPlayer = root.Q(minimapPlayerElementName);
        if (minimapRoot == null || minimapInner == null || minimapImage == null || minimapMarkerLayer == null ||
            minimapFrame == null || minimapPlayerAnchor == null || minimapPlayerShadow == null || minimapPlayer == null || minimapMask == null)
        {
            if (!warnedMissingUi)
            {
                Debug.LogWarning("[MinimapUIController] Missing one or more minimap UI elements.", this);
                warnedMissingUi = true;
            }
            return;
        }

        if (mapCircleInnerSprite != null)
            minimapInner.style.backgroundImage = new StyleBackground(mapCircleInnerSprite);
        if (mapCircleSprite != null)
            minimapFrame.style.backgroundImage = new StyleBackground(mapCircleSprite);
        ApplyBoatMarkerBackground();

        minimapTexture = new Texture2D(minimapTextureSize, minimapTextureSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "MinimapTileViewport"
        };
        minimapPixels = new Color32[minimapTextureSize * minimapTextureSize];
        minimapImage.style.backgroundImage = new StyleBackground(minimapTexture);
        minimapMarkerLayer.pickingMode = PickingMode.Ignore;
        uiReady = true;
    }

    void HandleHudToggleInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame)
            return;

        hudVisible = !hudVisible;
        ApplyVisibility();
    }

    void ApplyVisibility()
    {
        if (minimapRoot == null)
            return;

        minimapRoot.style.display = hudVisible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void ForceRefresh()
    {
        hasRenderedTexture = false;
        lastRenderedTextureVersion = -1;
        lastRenderedTextureSize = -1;
        lastMaskSize = Vector2.zero;
        lastRenderedWorldRadius = -1f;
    }

    void UpdateMinimap()
    {
        if (mapDiscoveryController == null
            || mapMarkerController == null
            || boatTransform == null
            || minimapMask == null
            || minimapPlayerAnchor == null
            || minimapPlayer == null
            || minimapPlayerShadow == null
            || minimapTexture == null
            || minimapPixels == null)
            return;

        Rect maskLayout = minimapMask.layout;
        if (maskLayout.width <= 0.1f || maskLayout.height <= 0.1f)
            return;

        debugMaskLayout = maskLayout;

        Vector3Int boatCell = GetBoatTileCell();
        Vector2 clampedCenter = mapDiscoveryController.ClampWorldCenterToBounds(
            boatTransform.position,
            minimapWorldRadius,
            minimapWorldRadius);
        if (ShouldRenderTexture(maskLayout, clampedCenter))
        {
            RenderMinimapTexture(clampedCenter);
            currentRenderedCenterWorld = clampedCenter;
            hasRenderedTexture = true;
            lastRenderedTextureVersion = mapDiscoveryController.TextureVersion;
            lastRenderedTextureSize = minimapTexture.width;
            lastMaskSize = new Vector2(maskLayout.width, maskLayout.height);
            lastRenderedWorldRadius = minimapWorldRadius;
            debugMinimapTextureVersion = lastRenderedTextureVersion;
            debugMinimapRenderCount++;
        }
        else
        {
            debugMinimapSkippedRenderCount++;
        }

        Vector2 markerViewportCenter = hasRenderedTexture ? currentRenderedCenterWorld : clampedCenter;

        Vector2 normalizedPosition = mapDiscoveryController.GetNormalizedPositionInViewport(
            boatTransform.position,
            markerViewportCenter,
            minimapWorldRadius,
            minimapWorldRadius);
        debugBoatNormalizedInViewport = normalizedPosition;

        float markerWidth = Mathf.Max(1f, minimapPlayer.resolvedStyle.width);
        float markerHeight = Mathf.Max(1f, minimapPlayer.resolvedStyle.height);
        float anchorLeft = maskLayout.xMin + (normalizedPosition.x * maskLayout.width);
        float anchorTop = maskLayout.yMin + ((1f - normalizedPosition.y) * maskLayout.height);

        minimapPlayerAnchor.style.left = anchorLeft;
        minimapPlayerAnchor.style.top = anchorTop;
        minimapPlayer.style.left = -markerWidth * 0.5f;
        minimapPlayer.style.top = -markerHeight * 0.5f;
        minimapPlayerShadow.style.left = (-markerWidth * 0.5f) - 1f;
        minimapPlayerShadow.style.top = (-markerHeight * 0.5f) - 1f;
        debugMarkerPosition = new Vector2(anchorLeft, anchorTop);
        debugBoatTileCell = boatCell;
        debugRenderedWorldRadius = minimapWorldRadius;
        debugRenderedCenterWorld = markerViewportCenter;
        UpdateMarkerRotation();
        RenderMarkers(maskLayout, markerViewportCenter);
    }

    bool ShouldRenderTexture(Rect maskLayout, Vector2 clampedCenter)
    {
        if (!hasRenderedTexture)
            return true;

        if (minimapTexture == null || minimapPixels == null)
            return true;

        if (mapDiscoveryController.TextureVersion != lastRenderedTextureVersion)
            return true;

        if (minimapTexture.width != lastRenderedTextureSize || minimapTexture.height != lastRenderedTextureSize)
            return true;

        Vector2 maskSize = new Vector2(maskLayout.width, maskLayout.height);
        if ((maskSize - lastMaskSize).sqrMagnitude > 0.01f)
            return true;

        if (!Mathf.Approximately(lastRenderedWorldRadius, minimapWorldRadius))
            return true;

        float worldUnitsPerPixel = (minimapWorldRadius * 2f) / Mathf.Max(1, minimapTexture.width);
        float refreshThreshold = Mathf.Max(0.05f, worldUnitsPerPixel * textureRefreshPixelThreshold);
        debugMinimapRefreshThresholdWorld = refreshThreshold;
        return (clampedCenter - currentRenderedCenterWorld).sqrMagnitude >= refreshThreshold * refreshThreshold;
    }

    void RenderMinimapTexture(Vector2 clampedCenter)
    {
        double startTime = Time.realtimeSinceStartupAsDouble;
        mapDiscoveryController.RenderViewport(
            clampedCenter,
            minimapWorldRadius,
            minimapWorldRadius,
            minimapTexture,
            minimapPixels);
        debugLastMinimapRenderDurationMs = (float)((Time.realtimeSinceStartupAsDouble - startTime) * 1000.0);
    }

    Vector3Int GetBoatTileCell()
    {
        return new Vector3Int(
            Mathf.FloorToInt(boatTransform.position.x),
            Mathf.FloorToInt(boatTransform.position.y),
            0);
    }

    void ApplyBoatMarkerBackground()
    {
        if (minimapPlayer == null || minimapPlayerShadow == null || boatMarkerSprite == null)
            return;

        minimapPlayerShadow.style.backgroundImage = new StyleBackground(boatMarkerSprite);
        minimapPlayerShadow.style.unityBackgroundImageTintColor = Color.black;
        minimapPlayer.style.backgroundImage = new StyleBackground(boatMarkerSprite);
        minimapPlayer.style.unityBackgroundImageTintColor = Color.white;
    }

    void UpdateMarkerRotation()
    {
        if (boatTransform == null || minimapPlayer == null || minimapPlayerShadow == null)
            return;

#pragma warning disable CS0618
        Quaternion rotation = Quaternion.Euler(0f, 0f, -boatTransform.eulerAngles.z + markerHeadingOffsetDegrees);
        minimapPlayer.transform.rotation = rotation;
        minimapPlayerShadow.transform.rotation = rotation;
#pragma warning restore CS0618
    }

    void RenderMarkers(Rect maskLayout, Vector2 viewportCenterWorld)
    {
        if (mapMarkerController == null || minimapMarkerLayer == null)
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

            if (!TryProjectMarkerToMinimap(marker.worldPosition, maskLayout, viewportCenterWorld, out Vector2 anchorPosition))
                continue;

            MarkerVisual visual = GetOrCreateMarkerVisual(marker.id, definition.sprite);
            visual.anchor.style.display = DisplayStyle.Flex;
            visual.anchor.style.left = anchorPosition.x;
            visual.anchor.style.top = anchorPosition.y;
            debugVisibleMarkerCount++;
            markerIdsToRemove.Remove(marker.id);
        }

        for (int i = 0; i < markerIdsToRemove.Count; i++)
            DestroyMarkerVisual(markerIdsToRemove[i]);
    }

    bool TryProjectMarkerToMinimap(Vector2 worldPosition, Rect maskLayout, Vector2 viewportCenterWorld, out Vector2 anchorPosition)
    {
        float halfExtent = minimapWorldRadius;
        float minX = viewportCenterWorld.x - halfExtent;
        float maxX = viewportCenterWorld.x + halfExtent;
        float minY = viewportCenterWorld.y - halfExtent;
        float maxY = viewportCenterWorld.y + halfExtent;
        if (worldPosition.x < minX || worldPosition.x > maxX || worldPosition.y < minY || worldPosition.y > maxY)
        {
            anchorPosition = default;
            return false;
        }

        float normalizedX = Mathf.InverseLerp(minX, maxX, worldPosition.x);
        float normalizedY = Mathf.InverseLerp(minY, maxY, worldPosition.y);
        anchorPosition = new Vector2(
            normalizedX * maskLayout.width,
            (1f - normalizedY) * maskLayout.height);
        return true;
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
            icon = new VisualElement()
        };

        visual.anchor.style.position = Position.Absolute;
        visual.anchor.style.left = 0f;
        visual.anchor.style.top = 0f;
        visual.anchor.style.width = 0f;
        visual.anchor.style.height = 0f;
        visual.anchor.pickingMode = PickingMode.Ignore;

        visual.icon.style.position = Position.Absolute;
        visual.icon.style.left = -minimapMarkerIconSize * 0.5f;
        visual.icon.style.top = -minimapMarkerIconSize * 0.5f;
        visual.icon.style.width = minimapMarkerIconSize;
        visual.icon.style.height = minimapMarkerIconSize;
        visual.icon.style.backgroundImage = new StyleBackground(sprite);
        visual.icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        visual.icon.pickingMode = PickingMode.Ignore;

        visual.anchor.Add(visual.icon);
        minimapMarkerLayer.Add(visual.anchor);
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
}
