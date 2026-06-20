using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent]
public class EnemyPathfindingController : MonoBehaviour
{
    struct GridKey
    {
        public int x;
        public int y;

        public GridKey(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    [Header("References")]
    [SerializeField] IslandGenerationController islandGenerationController;
    [SerializeField] Tilemap islandTilemap;

    [Header("Navigation Grid")]
    [SerializeField] float navigationCellSize = 2f;
    [SerializeField] float corridorPadding = 8f;
    [SerializeField] int maxCellsPerAxis = 40;
    [SerializeField] float sharedClearanceRadius = 1.25f;

    [Header("Obstacle Sampling")]
    [SerializeField] string obstacleLayerName = "Island";
    [SerializeField] float lineProbeStep = 0.8f;
    [SerializeField] float goalSnapRadiusCells = 3f;

    [Header("Debug (Play Mode Only)")]
    [SerializeField] int debugLastExpandedNodeCount;
    [SerializeField] int debugLastWaypointCount;
    [SerializeField] Vector2 debugLastQueryMin;
    [SerializeField] Vector2 debugLastQueryMax;

    static EnemyPathfindingController activeInstance;

    readonly List<int> openSet = new List<int>();
    readonly List<int> rawPathIndices = new List<int>();
    readonly List<Vector2> rawPathPoints = new List<Vector2>();
    readonly HashSet<GridKey> blockedCellCache = new HashSet<GridKey>();

    int cachedObstacleLayer = -1;

    public static EnemyPathfindingController ActiveInstance
    {
        get
        {
            if (activeInstance != null)
                return activeInstance;

            activeInstance = FindAnyObjectByType<EnemyPathfindingController>();
            if (activeInstance != null)
                return activeInstance;

            GameObject serviceObject = new GameObject("EnemyPathfindingController");
            activeInstance = serviceObject.AddComponent<EnemyPathfindingController>();
            return activeInstance;
        }
    }

    public float NavigationCellSize => navigationCellSize;

    void Awake()
    {
        activeInstance = this;
        ResolveReferences();
        cachedObstacleLayer = LayerMask.NameToLayer(obstacleLayerName);
    }

    void OnEnable()
    {
        activeInstance = this;
        ResolveReferences();
        cachedObstacleLayer = LayerMask.NameToLayer(obstacleLayerName);
    }

    void OnDisable()
    {
        if (activeInstance == this)
            activeInstance = null;
    }

    public bool HasDirectWaterLine(Vector2 startWorld, Vector2 endWorld)
    {
        ResolveReferences();
        if (IsObstacleLineBlocked(startWorld, endWorld))
            return false;

        return IsWaterLineClear(startWorld, endWorld);
    }

    public bool TryBuildPath(Vector2 startWorld, Vector2 goalWorld, out List<Vector2> smoothedWaypoints)
    {
        smoothedWaypoints = null;
        ResolveReferences();

        float cellSize = Mathf.Max(0.25f, navigationCellSize);
        Vector2 midpoint = (startWorld + goalWorld) * 0.5f;
        Vector2 span = new Vector2(Mathf.Abs(goalWorld.x - startWorld.x), Mathf.Abs(goalWorld.y - startWorld.y));
        Vector2 halfExtents = span * 0.5f + Vector2.one * corridorPadding;
        float maxHalfExtent = Mathf.Max(cellSize, maxCellsPerAxis * cellSize * 0.5f);
        halfExtents.x = Mathf.Min(maxHalfExtent, Mathf.Max(halfExtents.x, cellSize * 2f));
        halfExtents.y = Mathf.Min(maxHalfExtent, Mathf.Max(halfExtents.y, cellSize * 2f));

        Vector2 queryMin = midpoint - halfExtents;
        Vector2 queryMax = midpoint + halfExtents;
        int width = Mathf.Clamp(Mathf.CeilToInt((queryMax.x - queryMin.x) / cellSize), 3, maxCellsPerAxis);
        int height = Mathf.Clamp(Mathf.CeilToInt((queryMax.y - queryMin.y) / cellSize), 3, maxCellsPerAxis);

        debugLastQueryMin = queryMin;
        debugLastQueryMax = queryMax;

        bool[] blocked = new bool[width * height];
        blockedCellCache.Clear();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = ToIndex(x, y, width);
                blocked[index] = IsCellBlocked(x, y, width, height, queryMin, cellSize);
                if (blocked[index])
                    blockedCellCache.Add(new GridKey(x, y));
            }
        }

        int startIndex = FindNearestReachableCell(startWorld, queryMin, cellSize, width, height, blocked);
        int goalIndex = FindNearestReachableCell(goalWorld, queryMin, cellSize, width, height, blocked);
        if (startIndex < 0 || goalIndex < 0)
            return false;

        if (startIndex == goalIndex)
        {
            smoothedWaypoints = new List<Vector2> { GetCellCenter(goalIndex, width, queryMin, cellSize) };
            debugLastExpandedNodeCount = 1;
            debugLastWaypointCount = smoothedWaypoints.Count;
            return true;
        }

        float[] gScore = new float[width * height];
        float[] fScore = new float[width * height];
        int[] cameFrom = new int[width * height];
        bool[] closed = new bool[width * height];
        for (int i = 0; i < gScore.Length; i++)
        {
            gScore[i] = float.PositiveInfinity;
            fScore[i] = float.PositiveInfinity;
            cameFrom[i] = -1;
        }

        openSet.Clear();
        gScore[startIndex] = 0f;
        fScore[startIndex] = Heuristic(startIndex, goalIndex, width, queryMin, cellSize);
        openSet.Add(startIndex);

        int expandedNodes = 0;
        while (openSet.Count > 0)
        {
            int current = PopLowestFScore(openSet, fScore);
            if (current == goalIndex)
                break;

            closed[current] = true;
            expandedNodes++;

            int currentX = current % width;
            int currentY = current / width;

            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (offsetX == 0 && offsetY == 0)
                        continue;

                    int nextX = currentX + offsetX;
                    int nextY = currentY + offsetY;
                    if (nextX < 0 || nextY < 0 || nextX >= width || nextY >= height)
                        continue;

                    int neighborIndex = ToIndex(nextX, nextY, width);
                    if (blocked[neighborIndex] || closed[neighborIndex])
                        continue;

                    if (offsetX != 0 && offsetY != 0)
                    {
                        int sideA = ToIndex(currentX + offsetX, currentY, width);
                        int sideB = ToIndex(currentX, currentY + offsetY, width);
                        if (blocked[sideA] || blocked[sideB])
                            continue;
                    }

                    float stepCost = (offsetX != 0 && offsetY != 0) ? 1.41421356f : 1f;
                    float tentativeG = gScore[current] + stepCost;
                    if (tentativeG >= gScore[neighborIndex])
                        continue;

                    cameFrom[neighborIndex] = current;
                    gScore[neighborIndex] = tentativeG;
                    fScore[neighborIndex] = tentativeG + Heuristic(neighborIndex, goalIndex, width, queryMin, cellSize);
                    if (!openSet.Contains(neighborIndex))
                        openSet.Add(neighborIndex);
                }
            }
        }

        if (cameFrom[goalIndex] < 0)
            return false;

        rawPathIndices.Clear();
        int walk = goalIndex;
        rawPathIndices.Add(walk);
        while (walk != startIndex)
        {
            walk = cameFrom[walk];
            if (walk < 0)
                return false;

            rawPathIndices.Add(walk);
        }

        rawPathPoints.Clear();
        for (int i = rawPathIndices.Count - 1; i >= 0; i--)
            rawPathPoints.Add(GetCellCenter(rawPathIndices[i], width, queryMin, cellSize));

        smoothedWaypoints = SmoothPath(rawPathPoints);
        debugLastExpandedNodeCount = expandedNodes;
        debugLastWaypointCount = smoothedWaypoints.Count;
        return smoothedWaypoints.Count > 0;
    }

    List<Vector2> SmoothPath(List<Vector2> rawPoints)
    {
        List<Vector2> smoothed = new List<Vector2>();
        if (rawPoints == null || rawPoints.Count == 0)
            return smoothed;

        int anchorIndex = 0;
        smoothed.Add(rawPoints[0]);
        while (anchorIndex < rawPoints.Count - 1)
        {
            int furthestVisible = anchorIndex + 1;
            for (int candidate = rawPoints.Count - 1; candidate > anchorIndex; candidate--)
            {
                if (HasDirectWaterLine(rawPoints[anchorIndex], rawPoints[candidate]))
                {
                    furthestVisible = candidate;
                    break;
                }
            }

            smoothed.Add(rawPoints[furthestVisible]);
            anchorIndex = furthestVisible;
        }

        return smoothed;
    }

    bool IsCellBlocked(int x, int y, int width, int height, Vector2 queryMin, float cellSize)
    {
        Vector2 center = GetCellCenter(ToIndex(x, y, width), width, queryMin, cellSize);
        if (InfiniteWaterTileMap.ActiveInstance == null || !InfiniteWaterTileMap.ActiveInstance.HasWaterTileAtWorldPosition(center))
            return true;

        if (islandTilemap != null && islandTilemap.HasTile(islandTilemap.WorldToCell(center)))
            return true;

        if (cachedObstacleLayer < 0)
            cachedObstacleLayer = LayerMask.NameToLayer(obstacleLayerName);

        if (cachedObstacleLayer >= 0)
        {
            int obstacleMask = 1 << cachedObstacleLayer;
            if (Physics2D.OverlapCircle(center, sharedClearanceRadius, obstacleMask) != null)
                return true;
        }

        return false;
    }

    int FindNearestReachableCell(Vector2 worldPosition, Vector2 queryMin, float cellSize, int width, int height, bool[] blocked)
    {
        int bestIndex = -1;
        float bestDistanceSqr = float.PositiveInfinity;
        float maxGoalDistance = Mathf.Max(cellSize, goalSnapRadiusCells * cellSize);
        float maxGoalDistanceSqr = maxGoalDistance * maxGoalDistance;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = ToIndex(x, y, width);
                if (blocked[index])
                    continue;

                Vector2 center = GetCellCenter(index, width, queryMin, cellSize);
                float distanceSqr = (center - worldPosition).sqrMagnitude;
                if (distanceSqr > maxGoalDistanceSqr && bestIndex >= 0)
                    continue;

                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestIndex = index;
                }
            }
        }

        return bestIndex;
    }

    bool IsObstacleLineBlocked(Vector2 startWorld, Vector2 endWorld)
    {
        if (cachedObstacleLayer < 0)
            cachedObstacleLayer = LayerMask.NameToLayer(obstacleLayerName);

        if (cachedObstacleLayer < 0)
            return false;

        int obstacleMask = 1 << cachedObstacleLayer;
        return Physics2D.Linecast(startWorld, endWorld, obstacleMask);
    }

    bool IsWaterLineClear(Vector2 startWorld, Vector2 endWorld)
    {
        InfiniteWaterTileMap waterTileMap = InfiniteWaterTileMap.ActiveInstance;
        if (waterTileMap == null)
            return false;

        float totalDistance = Vector2.Distance(startWorld, endWorld);
        int steps = Mathf.Max(1, Mathf.CeilToInt(totalDistance / Mathf.Max(0.1f, lineProbeStep)));
        for (int i = 0; i <= steps; i++)
        {
            float t = steps <= 0 ? 0f : (float)i / steps;
            Vector2 sample = Vector2.Lerp(startWorld, endWorld, t);
            if (!waterTileMap.HasWaterTileAtWorldPosition(sample))
                return false;

            if (islandTilemap != null && islandTilemap.HasTile(islandTilemap.WorldToCell(sample)))
                return false;
        }

        return true;
    }

    Vector2 GetCellCenter(int index, int width, Vector2 queryMin, float cellSize)
    {
        int x = index % width;
        int y = index / width;
        return new Vector2(
            queryMin.x + (x + 0.5f) * cellSize,
            queryMin.y + (y + 0.5f) * cellSize);
    }

    float Heuristic(int fromIndex, int toIndex, int width, Vector2 queryMin, float cellSize)
    {
        Vector2 from = GetCellCenter(fromIndex, width, queryMin, cellSize);
        Vector2 to = GetCellCenter(toIndex, width, queryMin, cellSize);
        return Vector2.Distance(from, to) / Mathf.Max(0.01f, cellSize);
    }

    static int ToIndex(int x, int y, int width)
    {
        return y * width + x;
    }

    int PopLowestFScore(List<int> candidates, float[] fScore)
    {
        int bestListIndex = 0;
        float bestScore = fScore[candidates[0]];
        for (int i = 1; i < candidates.Count; i++)
        {
            float score = fScore[candidates[i]];
            if (score < bestScore)
            {
                bestScore = score;
                bestListIndex = i;
            }
        }

        int bestCandidate = candidates[bestListIndex];
        candidates.RemoveAt(bestListIndex);
        return bestCandidate;
    }

    void ResolveReferences()
    {
        if (islandGenerationController == null)
            islandGenerationController = FindAnyObjectByType<IslandGenerationController>();

        if (islandTilemap == null && islandGenerationController != null)
            islandTilemap = islandGenerationController.IslandTilemap;
    }

    void OnValidate()
    {
        navigationCellSize = Mathf.Max(0.5f, navigationCellSize);
        corridorPadding = Mathf.Max(2f, corridorPadding);
        maxCellsPerAxis = Mathf.Clamp(maxCellsPerAxis, 8, 128);
        sharedClearanceRadius = Mathf.Max(0f, sharedClearanceRadius);
        lineProbeStep = Mathf.Max(0.1f, lineProbeStep);
        goalSnapRadiusCells = Mathf.Max(1f, goalSnapRadiusCells);
    }
}
