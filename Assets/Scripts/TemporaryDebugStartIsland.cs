using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections;

[DefaultExecutionOrder(1000)]
public class TemporaryDebugStartIsland : MonoBehaviour
{
    [Header("References")]
    [SerializeField] IslandGenerationController islandGenerationController;
    [SerializeField] ShopDockController shopDockController;
    [SerializeField] Transform boatTransform;

    [Header("Placement")]
    [SerializeField] Vector2 offsetFromBoat = new Vector2(-9f, 0f);
    [SerializeField][Min(1)] int outerRadiusCells = 4;
    [SerializeField][Min(0)] int midRadiusCells = 2;
    [SerializeField][Min(0)] int highRadiusCells = 1;
    [SerializeField] bool addTemporaryShop = true;
    [SerializeField] Vector2Int temporaryShopId = new Vector2Int(9001, 9001);
    [SerializeField][Min(0)] int restampFrames = 3;

    [Header("Lifecycle")]
    [SerializeField] bool stampOnlyOncePerPlaySession = true;

    bool hasStamped;

    IEnumerator Start()
    {
        for (int i = 0; i < Mathf.Max(1, restampFrames); i++)
        {
            yield return null;
            TryStampIsland(force: true);
        }
    }

    void TryStampIsland(bool force = false)
    {
        if (!force && stampOnlyOncePerPlaySession && hasStamped)
            return;

        if (islandGenerationController == null)
            islandGenerationController = FindAnyObjectByType<IslandGenerationController>();

        if (shopDockController == null)
            shopDockController = FindAnyObjectByType<ShopDockController>();

        if (boatTransform == null)
        {
            BoatController boatController = FindAnyObjectByType<BoatController>();
            if (boatController != null)
                boatTransform = boatController.transform;
        }

        if (islandGenerationController == null || boatTransform == null)
            return;

        Tilemap islandTilemap = islandGenerationController.IslandTilemap;
        Tilemap dockTilemap = islandGenerationController.DockTilemap;
        TileBase lowTile = islandGenerationController.LowElevationTile;
        TileBase midTile = islandGenerationController.MidElevationTile;
        TileBase highTile = islandGenerationController.HighElevationTile;
        TileBase dockTile = islandGenerationController.DockTile;
        if (islandTilemap == null || lowTile == null || midTile == null || highTile == null)
            return;

        Vector3 worldCenter = boatTransform.position + (Vector3)offsetFromBoat;
        Vector3Int centerCell = islandTilemap.WorldToCell(worldCenter);

        int outerSqr = outerRadiusCells * outerRadiusCells;
        int midSqr = midRadiusCells * midRadiusCells;
        int highSqr = highRadiusCells * highRadiusCells;

        for (int y = -outerRadiusCells; y <= outerRadiusCells; y++)
        {
            for (int x = -outerRadiusCells; x <= outerRadiusCells; x++)
            {
                int sqrDistance = x * x + y * y;
                if (sqrDistance > outerSqr)
                    continue;

                TileBase tileToPaint = lowTile;
                if (sqrDistance <= highSqr)
                    tileToPaint = highTile;
                else if (sqrDistance <= midSqr)
                    tileToPaint = midTile;

                islandTilemap.SetTile(centerCell + new Vector3Int(x, y, 0), tileToPaint);
            }
        }

        if (addTemporaryShop && dockTilemap != null && dockTile != null && shopDockController != null)
            StampTemporaryShopDock(centerCell, dockTilemap, dockTile);

        hasStamped = true;
    }

    void StampTemporaryShopDock(Vector3Int islandCenterCell, Tilemap dockTilemap, TileBase dockTile)
    {
        Vector3Int rootCell = islandCenterCell + new Vector3Int(outerRadiusCells, 0, 0);
        Vector3Int outwardDirection = Vector3Int.right;
        int dockLength = 2;

        for (int step = 1; step <= dockLength; step++)
            dockTilemap.SetTile(rootCell + outwardDirection * step, dockTile);

        Vector3 rootWorld = dockTilemap.GetCellCenterWorld(rootCell);
        Vector3 outward = new Vector3(outwardDirection.x, outwardDirection.y, 0f);
        Vector3 spanStartWorld = rootWorld + outward * 1f;
        Vector3 spanEndWorld = rootWorld + outward * dockLength;
        Vector3 anchorWorld = rootWorld + outward * (0.5f + dockLength * 0.5f);

        shopDockController.RegisterTemporaryShopDock(
            temporaryShopId,
            anchorWorld,
            spanStartWorld,
            spanEndWorld,
            islandGenerationController.ShopDockInteractionRadius,
            Vector2Int.zero);
    }
}
