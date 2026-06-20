using UnityEngine;
using UnityEngine.Tilemaps;

// Keeps the visible camera area covered in water without repainting the whole
// tilemap every time the boat moves. The active painted region expands to match
// the camera's current visible bounds plus a padding margin, and only the new
// strips outside the retained region are painted during normal movement.
public class InfiniteWaterTileMap : MonoBehaviour
{
    public static InfiniteWaterTileMap ActiveInstance { get; private set; }
    public Tilemap WaterTilemap => tilemap;

    [Header("References")]
    [SerializeField] Tilemap tilemap;
    [SerializeField] Camera worldCamera;
    [SerializeField] TileBase[] waterTiles;

    [Header("Rendering")]
    [SerializeField] string waterSortingLayerName = "WaterBase";
    [SerializeField] int waterSortingOrder = -10;
    [SerializeField] int fallbackWaterSortingOrder = -10;

    [Header("Minimum Window Size")]
    [SerializeField] int width = 30;
    [SerializeField] int height = 30;

    [Header("Camera Padding")]
    [SerializeField] int paddingTiles = 4;

    [Header("Retention")]
    [SerializeField] float retainedSizeResetMultiplier = 2f;

    [Header("Forensics")]
    [SerializeField] bool debugScanRequiredRectForMissingTiles = true;
    [SerializeField] bool debugLogMissingTileWarnings;

    RectInt retainedRect;
    bool hasPaintedInitialWindow;
    bool referencesValid;
    int lastLoggedMissingTileFrame = -1;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] RectInt debugRequiredRect;
    [SerializeField] RectInt debugRetainedRect;
    [SerializeField] WaterRefreshMode debugLastRefreshMode;
    [SerializeField] string debugAppliedSortingLayerName = "WaterBase";
    [SerializeField] int debugAppliedSortingOrder = -10;
    [SerializeField] bool debugDetectedMissingTileInRequiredRect;
    [SerializeField] int debugMissingTileCountInRequiredRect;
    [SerializeField] Vector3Int debugFirstMissingTileCell;
    [SerializeField] int debugLastMissingTileScanFrame = -1;

    enum WaterRefreshMode
    {
        None,
        InitialFullPaint,
        IncrementalExpansion,
        FullRepaintFallback
    }

    void OnEnable()
    {
        ActiveInstance = this;
    }

    void OnDisable()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;
    }

    void Start()
    {
        referencesValid = ValidateReferences();
        if (!referencesValid)
            return;

        ConfigureTilemapRenderer();
        PaintInitialWindow();
    }

    void LateUpdate()
    {
        if (!referencesValid)
            return;

        Camera cameraToUse = ResolveCamera();
        if (cameraToUse == null)
            return;

        RectInt requiredRect = GetRequiredRect(cameraToUse);
        debugRequiredRect = requiredRect;

        if (!hasPaintedInitialWindow)
        {
            PaintRect(requiredRect);
            retainedRect = requiredRect;
            hasPaintedInitialWindow = true;
            debugRetainedRect = retainedRect;
            debugLastRefreshMode = WaterRefreshMode.InitialFullPaint;
            RunMissingTileForensics(requiredRect);
            return;
        }

        if (ShouldDoFullRepaint(requiredRect))
        {
            FullRepaint(requiredRect);
            return;
        }

        if (!ContainsRect(retainedRect, requiredRect))
        {
            ExpandRetainedRect(requiredRect);
            retainedRect = UnionRect(retainedRect, requiredRect);
            debugRetainedRect = retainedRect;
            debugLastRefreshMode = WaterRefreshMode.IncrementalExpansion;
            RunMissingTileForensics(requiredRect);
            return;
        }

        debugRetainedRect = retainedRect;
        debugLastRefreshMode = WaterRefreshMode.None;
        RunMissingTileForensics(requiredRect);
    }

    bool ValidateReferences()
    {
        if (tilemap == null)
        {
            Debug.LogWarning("[InfiniteWaterTileMap] Missing Tilemap reference.", this);
            return false;
        }

        if (waterTiles == null || waterTiles.Length == 0)
        {
            Debug.LogWarning("[InfiniteWaterTileMap] Assign at least one water tile.", this);
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            Debug.LogWarning("[InfiniteWaterTileMap] Width and height must be greater than zero.", this);
            return false;
        }

        if (paddingTiles < 0)
        {
            Debug.LogWarning("[InfiniteWaterTileMap] Padding tiles must be zero or greater.", this);
            return false;
        }

        if (retainedSizeResetMultiplier < 1f)
        {
            Debug.LogWarning("[InfiniteWaterTileMap] Retained size reset multiplier must be at least 1.", this);
            return false;
        }

        return true;
    }

    void ConfigureTilemapRenderer()
    {
        TilemapRenderer tilemapRenderer = tilemap != null ? tilemap.GetComponent<TilemapRenderer>() : null;
        if (tilemapRenderer == null)
            return;

        bool sortingLayerExists = SortingLayerExists(waterSortingLayerName);
        string sortingLayerToUse = sortingLayerExists ? waterSortingLayerName : ResolveSortingLayerName(waterSortingLayerName);
        int sortingOrderToUse = sortingLayerExists ? waterSortingOrder : fallbackWaterSortingOrder;

        tilemapRenderer.sortingLayerName = sortingLayerToUse;
        tilemapRenderer.sortingOrder = sortingOrderToUse;
        debugAppliedSortingLayerName = sortingLayerToUse;
        debugAppliedSortingOrder = sortingOrderToUse;
    }

    Camera ResolveCamera()
    {
        if (worldCamera != null)
            return worldCamera;

        return Camera.main;
    }

    static string ResolveSortingLayerName(string requestedLayerName)
    {
        if (SortingLayerExists(requestedLayerName))
            return requestedLayerName;

        return "Default";
    }

    static bool SortingLayerExists(string layerName)
    {
        if (string.IsNullOrWhiteSpace(layerName))
            return false;

        SortingLayer[] sortingLayers = SortingLayer.layers;
        for (int i = 0; i < sortingLayers.Length; i++)
        {
            if (sortingLayers[i].name == layerName)
                return true;
        }

        return false;
    }

    void PaintInitialWindow()
    {
        Camera cameraToUse = ResolveCamera();
        if (cameraToUse == null)
            return;

        RectInt initialRect = GetRequiredRect(cameraToUse);
        PaintRect(initialRect);
        retainedRect = initialRect;
        hasPaintedInitialWindow = true;
        debugRequiredRect = initialRect;
        debugRetainedRect = retainedRect;
        debugLastRefreshMode = WaterRefreshMode.InitialFullPaint;
        RunMissingTileForensics(initialRect);
    }

    RectInt GetRequiredRect(Camera cameraToUse)
    {
        Vector3 bottomLeft = cameraToUse.ViewportToWorldPoint(new Vector3(0f, 0f, 0f));
        Vector3 topRight   = cameraToUse.ViewportToWorldPoint(new Vector3(1f, 1f, 0f));

        Vector3 minWorld = Vector3.Min(bottomLeft, topRight);
        Vector3 maxWorld = Vector3.Max(bottomLeft, topRight);

        Vector3Int minCell = tilemap.WorldToCell(minWorld);
        Vector3Int maxCell = tilemap.WorldToCell(maxWorld);

        int rectMinX = minCell.x - paddingTiles;
        int rectMinY = minCell.y - paddingTiles;
        int rectMaxX = maxCell.x + paddingTiles;
        int rectMaxY = maxCell.y + paddingTiles;

        RectInt visibleRect = RectFromInclusive(rectMinX, rectMinY, rectMaxX, rectMaxY);

        Vector3Int cameraCenterCell = tilemap.WorldToCell(cameraToUse.transform.position);
        RectInt minimumRect = CenteredRect(cameraCenterCell.x, cameraCenterCell.y, width, height);

        return UnionRect(visibleRect, minimumRect);
    }

    bool ShouldDoFullRepaint(RectInt requiredRect)
    {
        if (!RectsOverlap(retainedRect, requiredRect))
            return true;

        int maxRetainedWidth = Mathf.CeilToInt(requiredRect.width * retainedSizeResetMultiplier);
        int maxRetainedHeight = Mathf.CeilToInt(requiredRect.height * retainedSizeResetMultiplier);

        return retainedRect.width > maxRetainedWidth || retainedRect.height > maxRetainedHeight;
    }

    void FullRepaint(RectInt requiredRect)
    {
        PaintRect(requiredRect);
        retainedRect = requiredRect;
        debugRetainedRect = retainedRect;
        debugLastRefreshMode = WaterRefreshMode.FullRepaintFallback;
        RunMissingTileForensics(requiredRect);
    }

    void ExpandRetainedRect(RectInt requiredRect)
    {
        RectInt previousRetained = retainedRect;
        RectInt expandedRetained = UnionRect(previousRetained, requiredRect);

        if (expandedRetained.xMin < previousRetained.xMin)
        {
            int stripWidth = previousRetained.xMin - expandedRetained.xMin;
            PaintRect(new RectInt(expandedRetained.xMin, expandedRetained.yMin, stripWidth, expandedRetained.height));
        }

        if (expandedRetained.xMax > previousRetained.xMax)
        {
            int stripWidth = expandedRetained.xMax - previousRetained.xMax;
            PaintRect(new RectInt(previousRetained.xMax, expandedRetained.yMin, stripWidth, expandedRetained.height));
        }

        if (expandedRetained.yMin < previousRetained.yMin)
        {
            int stripHeight = previousRetained.yMin - expandedRetained.yMin;
            PaintRect(new RectInt(previousRetained.xMin, expandedRetained.yMin, previousRetained.width, stripHeight));
        }

        if (expandedRetained.yMax > previousRetained.yMax)
        {
            int stripHeight = expandedRetained.yMax - previousRetained.yMax;
            PaintRect(new RectInt(previousRetained.xMin, previousRetained.yMax, previousRetained.width, stripHeight));
        }
    }

    void RunMissingTileForensics(RectInt requiredRect)
    {
        if (!debugScanRequiredRectForMissingTiles || tilemap == null)
        {
            debugDetectedMissingTileInRequiredRect = false;
            debugMissingTileCountInRequiredRect = 0;
            debugFirstMissingTileCell = default;
            return;
        }

        debugLastMissingTileScanFrame = Time.frameCount;
        debugDetectedMissingTileInRequiredRect = false;
        debugMissingTileCountInRequiredRect = 0;
        debugFirstMissingTileCell = default;

        for (int y = requiredRect.yMin; y < requiredRect.yMax; y++)
        {
            for (int x = requiredRect.xMin; x < requiredRect.xMax; x++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                if (tilemap.HasTile(cell))
                    continue;

                if (!debugDetectedMissingTileInRequiredRect)
                    debugFirstMissingTileCell = cell;

                debugDetectedMissingTileInRequiredRect = true;
                debugMissingTileCountInRequiredRect++;
            }
        }

        if (!debugDetectedMissingTileInRequiredRect || !debugLogMissingTileWarnings)
            return;

        if (lastLoggedMissingTileFrame == Time.frameCount)
            return;

        lastLoggedMissingTileFrame = Time.frameCount;
        Debug.LogWarning(
            $"[InfiniteWaterTileMap] Missing tiles detected inside required rect. " +
            $"Count={debugMissingTileCountInRequiredRect}, FirstCell={debugFirstMissingTileCell}, " +
            $"RequiredRect={requiredRect}, RetainedRect={retainedRect}, RefreshMode={debugLastRefreshMode}",
            this);
    }

    void PaintRect(RectInt rect)
    {
        if (rect.width <= 0 || rect.height <= 0)
            return;

        BoundsInt blockBounds = new BoundsInt(rect.xMin, rect.yMin, 0, rect.width, rect.height, 1);
        TileBase[] tileBuffer = BuildTileBuffer(rect);
        tilemap.SetTilesBlock(blockBounds, tileBuffer);
    }

    TileBase[] BuildTileBuffer(RectInt rect)
    {
        TileBase[] tiles = new TileBase[rect.width * rect.height];
        int index = 0;

        for (int y = 0; y < rect.height; y++)
        {
            for (int x = 0; x < rect.width; x++)
            {
                Vector3Int cell = new Vector3Int(rect.xMin + x, rect.yMin + y, 0);
                TileBase existingTile = tilemap.GetTile(cell);
                if (existingTile != null && !IsConfiguredWaterTile(existingTile))
                {
                    tiles[index++] = existingTile;
                    continue;
                }

                tiles[index++] = GetTileForCell(cell);
            }
        }

        return tiles;
    }

    bool IsConfiguredWaterTile(TileBase tile)
    {
        if (tile == null || waterTiles == null)
            return false;

        for (int i = 0; i < waterTiles.Length; i++)
        {
            if (waterTiles[i] == tile)
                return true;
        }

        return false;
    }

    static RectInt CenteredRect(int centerX, int centerY, int rectWidth, int rectHeight)
    {
        int minX = centerX - rectWidth / 2;
        int minY = centerY - rectHeight / 2;
        return new RectInt(minX, minY, rectWidth, rectHeight);
    }

    static RectInt RectFromInclusive(int minX, int minY, int maxX, int maxY)
    {
        return new RectInt(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
    }

    static RectInt UnionRect(RectInt a, RectInt b)
    {
        int minX = Mathf.Min(a.xMin, b.xMin);
        int minY = Mathf.Min(a.yMin, b.yMin);
        int maxX = Mathf.Max(a.xMax, b.xMax);
        int maxY = Mathf.Max(a.yMax, b.yMax);
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    static bool ContainsRect(RectInt outer, RectInt inner)
    {
        return inner.xMin >= outer.xMin
            && inner.yMin >= outer.yMin
            && inner.xMax <= outer.xMax
            && inner.yMax <= outer.yMax;
    }

    static bool RectsOverlap(RectInt a, RectInt b)
    {
        return a.xMin < b.xMax
            && a.xMax > b.xMin
            && a.yMin < b.yMax
            && a.yMax > b.yMin;
    }

    TileBase GetTileForCell(Vector3Int cell)
    {
        if (waterTiles.Length == 1)
            return waterTiles[0];

        unchecked
        {
            int hash = 17;
            hash = hash * 31 + cell.x;
            hash = hash * 31 + cell.y;
            hash = Mathf.Abs(hash);
            int tileIndex = hash % waterTiles.Length;
            return waterTiles[tileIndex];
        }
    }

    public void FillCellWithWater(Vector3Int cell)
    {
        if (tilemap == null)
            return;

        tilemap.SetTile(cell, GetTileForCell(cell));
    }

    public bool HasWaterTileAtWorldPosition(Vector2 worldPosition)
    {
        if (tilemap == null)
            return false;

        Vector3Int cell = tilemap.WorldToCell(worldPosition);
        return tilemap.HasTile(cell);
    }
}
