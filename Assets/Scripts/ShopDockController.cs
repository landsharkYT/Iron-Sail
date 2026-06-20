using System.Collections.Generic;
using UnityEngine;

// Tracks currently loaded shop docks and answers proximity queries for the boat.
//
// This is intentionally narrow for the first shop skeleton:
// - island generation owns deterministic shop identity and dock placement
// - this service tracks which shop docks are currently loaded
// - boats ask this service whether a nearby shop dock exists
public class ShopDockController : MonoBehaviour
{
    public readonly struct ShopDockQueryResult
    {
        public readonly Vector2Int ShopId;
        public readonly Vector3 AnchorWorldPosition;
        public readonly Vector3 ClosestPointWorldPosition;
        public readonly float Distance;
        public readonly float InteractionRadius;

        public ShopDockQueryResult(Vector2Int shopId, Vector3 anchorWorldPosition, Vector3 closestPointWorldPosition, float distance, float interactionRadius)
        {
            ShopId = shopId;
            AnchorWorldPosition = anchorWorldPosition;
            ClosestPointWorldPosition = closestPointWorldPosition;
            Distance = distance;
            InteractionRadius = interactionRadius;
        }
    }

    class ActiveShopDock
    {
        public Vector2Int ShopId;
        public Vector3 AnchorWorldPosition;
        public Vector3 SpanStartWorldPosition;
        public Vector3 SpanEndWorldPosition;
        public float InteractionRadius;
        public readonly HashSet<Vector2Int> SourceChunks = new HashSet<Vector2Int>();
    }

    [Header("References")]
    [SerializeField] IslandGenerationController islandGenerationController;

    [Header("Detection")]
    [SerializeField][Min(0.1f)] float dockInteractionRadius = 3.2f;

#pragma warning disable CS0414
    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] int debugActiveShopCount;
    [SerializeField] bool debugHasNearestShop;
    [SerializeField] Vector2Int debugNearestShopId;
    [SerializeField] Vector3 debugNearestAnchorWorldPosition;
    [SerializeField] Vector3 debugNearestClosestPointWorldPosition;
    [SerializeField] float debugNearestDistance;
#pragma warning restore CS0414

    readonly Dictionary<Vector2Int, ActiveShopDock> activeShopDocks = new Dictionary<Vector2Int, ActiveShopDock>();
    readonly Dictionary<Vector2Int, HashSet<Vector2Int>> chunkToShopIds = new Dictionary<Vector2Int, HashSet<Vector2Int>>();

    public void RegisterTemporaryShopDock(
        Vector2Int shopId,
        Vector3 anchorWorldPosition,
        Vector3 spanStartWorldPosition,
        Vector3 spanEndWorldPosition,
        float interactionRadius,
        Vector2Int sourceChunkCoord)
    {
        HandleShopDockRegistered(new IslandGenerationController.ShopDockRegistration
        {
            ShopId = shopId,
            AnchorWorldPosition = anchorWorldPosition,
            SpanStartWorldPosition = spanStartWorldPosition,
            SpanEndWorldPosition = spanEndWorldPosition,
            InteractionRadius = interactionRadius > 0f ? interactionRadius : dockInteractionRadius,
            SourceChunkCoord = sourceChunkCoord,
        });
    }

    public bool TryGetNearestShopDock(Vector3 worldPosition, out ShopDockQueryResult result)
    {
        ActiveShopDock nearestDock = null;
        float nearestDistance = float.PositiveInfinity;
        Vector3 nearestPoint = Vector3.zero;

        foreach (ActiveShopDock activeDock in activeShopDocks.Values)
        {
            Vector3 closestPoint = GetClosestPointOnDockSpan(activeDock, worldPosition);
            float distance = Vector2.Distance(worldPosition, closestPoint);
            if (distance > activeDock.InteractionRadius || distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearestDock = activeDock;
            nearestPoint = closestPoint;
        }

        if (nearestDock == null)
        {
            result = default;
            debugHasNearestShop = false;
            debugNearestShopId = default;
            debugNearestAnchorWorldPosition = Vector3.zero;
            debugNearestClosestPointWorldPosition = Vector3.zero;
            debugNearestDistance = 0f;
            return false;
        }

        result = new ShopDockQueryResult(
            nearestDock.ShopId,
            nearestDock.AnchorWorldPosition,
            nearestPoint,
            nearestDistance,
            nearestDock.InteractionRadius);

        debugHasNearestShop = true;
        debugNearestShopId = nearestDock.ShopId;
        debugNearestAnchorWorldPosition = nearestDock.AnchorWorldPosition;
        debugNearestClosestPointWorldPosition = nearestPoint;
        debugNearestDistance = nearestDistance;
        return true;
    }

    void OnEnable()
    {
        ResolveIslandGenerationController();
        Subscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
        activeShopDocks.Clear();
        chunkToShopIds.Clear();
        debugActiveShopCount = 0;
    }

    void ResolveIslandGenerationController()
    {
        if (islandGenerationController != null)
            return;

        islandGenerationController = GetComponent<IslandGenerationController>();
        if (islandGenerationController == null)
            islandGenerationController = FindAnyObjectByType<IslandGenerationController>();
    }

    void Subscribe()
    {
        if (islandGenerationController == null)
            return;

        islandGenerationController.ShopDockRegistered -= HandleShopDockRegistered;
        islandGenerationController.ChunkUnloaded -= HandleChunkUnloaded;
        islandGenerationController.ShopDockRegistered += HandleShopDockRegistered;
        islandGenerationController.ChunkUnloaded += HandleChunkUnloaded;
    }

    void Unsubscribe()
    {
        if (islandGenerationController == null)
            return;

        islandGenerationController.ShopDockRegistered -= HandleShopDockRegistered;
        islandGenerationController.ChunkUnloaded -= HandleChunkUnloaded;
    }

    void HandleShopDockRegistered(IslandGenerationController.ShopDockRegistration registration)
    {
        if (!activeShopDocks.TryGetValue(registration.ShopId, out ActiveShopDock activeDock))
        {
            activeDock = new ActiveShopDock
            {
                ShopId = registration.ShopId
            };
            activeShopDocks.Add(registration.ShopId, activeDock);
        }

        activeDock.AnchorWorldPosition = registration.AnchorWorldPosition;
        activeDock.SpanStartWorldPosition = registration.SpanStartWorldPosition;
        activeDock.SpanEndWorldPosition = registration.SpanEndWorldPosition;
        activeDock.InteractionRadius = registration.InteractionRadius > 0f
            ? registration.InteractionRadius
            : dockInteractionRadius;
        activeDock.SourceChunks.Add(registration.SourceChunkCoord);

        if (!chunkToShopIds.TryGetValue(registration.SourceChunkCoord, out HashSet<Vector2Int> shopIds))
        {
            shopIds = new HashSet<Vector2Int>();
            chunkToShopIds.Add(registration.SourceChunkCoord, shopIds);
        }

        shopIds.Add(registration.ShopId);
        debugActiveShopCount = activeShopDocks.Count;
    }

    void HandleChunkUnloaded(Vector2Int chunkCoord)
    {
        if (!chunkToShopIds.TryGetValue(chunkCoord, out HashSet<Vector2Int> shopIds))
            return;

        foreach (Vector2Int shopId in shopIds)
        {
            if (!activeShopDocks.TryGetValue(shopId, out ActiveShopDock activeDock))
                continue;

            activeDock.SourceChunks.Remove(chunkCoord);
            if (activeDock.SourceChunks.Count == 0)
                activeShopDocks.Remove(shopId);
        }

        chunkToShopIds.Remove(chunkCoord);
        debugActiveShopCount = activeShopDocks.Count;
    }

    static Vector3 GetClosestPointOnDockSpan(ActiveShopDock activeDock, Vector3 worldPosition)
    {
        Vector2 start = activeDock.SpanStartWorldPosition;
        Vector2 end = activeDock.SpanEndWorldPosition;
        Vector2 point = worldPosition;
        Vector2 segment = end - start;
        float sqrMagnitude = segment.sqrMagnitude;
        if (sqrMagnitude <= 0.0001f)
            return activeDock.AnchorWorldPosition;

        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / sqrMagnitude);
        Vector2 closest = start + segment * t;
        return new Vector3(closest.x, closest.y, activeDock.AnchorWorldPosition.z);
    }
}
