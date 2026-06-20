using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class WorldBoundryController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Tilemap borderTilemap;
    [SerializeField] Camera worldCamera;
    [SerializeField] WorldGenerationSettings worldSettings;
    [SerializeField] Transform boatTransform;
    [SerializeField] Rigidbody2D boatRigidbody;
    [SerializeField] BoatHealthController boatHealthController;
    [SerializeField] BoatIslandCollisionController boatIslandCollisionController;

    [Header("Streaming")]
    [SerializeField] int chunkSize = 48;
    [SerializeField] int generationMarginChunks = 1;

    [Header("Gameplay Boundary")]
    [SerializeField] bool disableBorderTilemapColliderAtRuntime = true;
    [SerializeField] float wallImpactSpeedThreshold = 1.25f;
    [SerializeField] float fullWallImpactSpeed = 9f;
    [SerializeField] float maxWallImpactDamage = 54f;
    [SerializeField] float repeatedWallImpactCooldownSeconds = 0.45f;

    [Header("Impact FX")]
    [SerializeField] GameObject wallImpactPoofPrefab;
    [SerializeField] float wallImpactFxCooldownSeconds = 0.08f;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] RectInt debugRequiredChunkRect;
    [SerializeField] int debugLoadedChunkCount;
    [SerializeField] float debugLastWallImpactSpeed;
    [SerializeField] float debugLastWallDamageApplied;
    [SerializeField] Vector2 debugLastWallNormal = Vector2.up;

    readonly HashSet<Vector2Int> loadedChunks = new HashSet<Vector2Int>();

    bool referencesValid;
    float nextWallDamageTime;
    float nextWallImpactFxTime;

    public Tilemap BorderTilemap => borderTilemap;

    void OnValidate()
    {
        chunkSize = Mathf.Max(1, chunkSize);
        generationMarginChunks = Mathf.Max(0, generationMarginChunks);
        wallImpactSpeedThreshold = Mathf.Max(0f, wallImpactSpeedThreshold);
        fullWallImpactSpeed = Mathf.Max(wallImpactSpeedThreshold + 0.01f, fullWallImpactSpeed);
        maxWallImpactDamage = Mathf.Max(0f, maxWallImpactDamage);
        repeatedWallImpactCooldownSeconds = Mathf.Max(0f, repeatedWallImpactCooldownSeconds);
        wallImpactFxCooldownSeconds = Mathf.Max(0f, wallImpactFxCooldownSeconds);
    }

    void Start()
    {
        referencesValid = ValidateReferences();
        if (!referencesValid)
            return;

        DisableBorderColliderIfNeeded();
        RefreshVisibleChunks();
    }

    void FixedUpdate()
    {
        if (!referencesValid)
            return;

        EnforceBoatBoundary();
    }

    void LateUpdate()
    {
        if (!referencesValid)
            return;

        RefreshVisibleChunks();
    }

    bool ValidateReferences()
    {
        if (borderTilemap == null)
        {
            Debug.LogWarning("[WorldBoundryController] Missing BorderTilemap reference.", this);
            return false;
        }

        if (worldSettings == null)
        {
            Debug.LogWarning("[WorldBoundryController] Missing WorldGenerationSettings reference.", this);
            return false;
        }

        if (!worldSettings.HasValidBorderTile)
        {
            Debug.LogWarning("[WorldBoundryController] WorldGenerationSettings is missing a border tile.", this);
            return false;
        }

        if (chunkSize <= 0)
        {
            Debug.LogWarning("[WorldBoundryController] Chunk size must be greater than zero.", this);
            return false;
        }

        if (generationMarginChunks < 0)
        {
            Debug.LogWarning("[WorldBoundryController] Generation margin chunks must be zero or greater.", this);
            return false;
        }

        ResolveBoatReferences();

        return true;
    }

    void ResolveBoatReferences()
    {
        if (boatTransform == null)
        {
            BoatController boatController = FindAnyObjectByType<BoatController>();
            if (boatController != null)
                boatTransform = boatController.transform;
        }

        if (boatRigidbody == null && boatTransform != null)
            boatRigidbody = boatTransform.GetComponent<Rigidbody2D>();

        if (boatHealthController == null && boatTransform != null)
            boatHealthController = boatTransform.GetComponent<BoatHealthController>();

        if (boatIslandCollisionController == null && boatTransform != null)
            boatIslandCollisionController = boatTransform.GetComponent<BoatIslandCollisionController>();
    }

    void DisableBorderColliderIfNeeded()
    {
        if (!disableBorderTilemapColliderAtRuntime || borderTilemap == null)
            return;

        TilemapCollider2D borderCollider = borderTilemap.GetComponent<TilemapCollider2D>();
        if (borderCollider != null)
            borderCollider.enabled = false;
    }

    void RefreshVisibleChunks()
    {
        Camera cameraToUse = ResolveCamera();
        if (cameraToUse == null)
            return;

        RectInt requiredChunkRect = GetRequiredChunkRect(cameraToUse);
        debugRequiredChunkRect = requiredChunkRect;

        UnloadDistantChunks(requiredChunkRect);

        for (int y = requiredChunkRect.yMin; y < requiredChunkRect.yMax; y++)
        {
            for (int x = requiredChunkRect.xMin; x < requiredChunkRect.xMax; x++)
            {
                Vector2Int chunkCoord = new Vector2Int(x, y);
                if (loadedChunks.Contains(chunkCoord))
                    continue;

                RectInt chunkRect = GetChunkRect(chunkCoord);
                if (!ChunkCanContainBoundary(chunkRect))
                    continue;

                GenerateChunk(chunkCoord, chunkRect);
            }
        }

        debugLoadedChunkCount = loadedChunks.Count;
    }

    Camera ResolveCamera()
    {
        return worldCamera != null ? worldCamera : Camera.main;
    }

    RectInt GetRequiredChunkRect(Camera cameraToUse)
    {
        Vector3 bottomLeft = cameraToUse.ViewportToWorldPoint(new Vector3(0f, 0f, 0f));
        Vector3 topRight = cameraToUse.ViewportToWorldPoint(new Vector3(1f, 1f, 0f));

        Vector3 minWorld = Vector3.Min(bottomLeft, topRight);
        Vector3 maxWorld = Vector3.Max(bottomLeft, topRight);

        Vector3Int minCell = borderTilemap.WorldToCell(minWorld);
        Vector3Int maxCell = borderTilemap.WorldToCell(maxWorld);

        Vector2Int minChunk = CellToChunkCoord(minCell);
        Vector2Int maxChunk = CellToChunkCoord(maxCell);

        int minX = minChunk.x - generationMarginChunks;
        int minY = minChunk.y - generationMarginChunks;
        int maxX = maxChunk.x + generationMarginChunks + 1;
        int maxY = maxChunk.y + generationMarginChunks + 1;
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    Vector2Int CellToChunkCoord(Vector3Int cell)
    {
        return new Vector2Int(FloorDiv(cell.x, chunkSize), FloorDiv(cell.y, chunkSize));
    }

    public bool IsChunkLoadedForCell(Vector3Int cell)
    {
        return loadedChunks.Contains(CellToChunkCoord(cell));
    }

    static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
            quotient--;

        return quotient;
    }

    RectInt GetChunkRect(Vector2Int chunkCoord)
    {
        return new RectInt(chunkCoord.x * chunkSize, chunkCoord.y * chunkSize, chunkSize, chunkSize);
    }

    void UnloadDistantChunks(RectInt requiredChunkRect)
    {
        List<Vector2Int> chunksToUnload = new List<Vector2Int>();
        foreach (Vector2Int chunkCoord in loadedChunks)
        {
            if (!requiredChunkRect.Contains(chunkCoord))
                chunksToUnload.Add(chunkCoord);
        }

        for (int i = 0; i < chunksToUnload.Count; i++)
            UnloadChunk(chunksToUnload[i]);
    }

    void UnloadChunk(Vector2Int chunkCoord)
    {
        ClearRect(GetChunkRect(chunkCoord));
        loadedChunks.Remove(chunkCoord);
    }

    bool ChunkCanContainBoundary(RectInt chunkRect)
    {
        float innerRadius = Mathf.Max(0f, worldSettings.WallInnerRadiusTiles - 1f);
        float outerRadius = worldSettings.WallOuterRadiusTiles + 1f;
        return RectTouchesRadiusBand(chunkRect, innerRadius, outerRadius);
    }

    void GenerateChunk(Vector2Int chunkCoord, RectInt chunkRect)
    {
        TileBase[] tiles = new TileBase[chunkRect.width * chunkRect.height];
        int index = 0;

        float innerRadius = worldSettings.WallInnerRadiusTiles;
        float outerRadius = worldSettings.WallOuterRadiusTiles;
        TileBase borderTile = worldSettings.BorderTile;

        for (int y = chunkRect.yMin; y < chunkRect.yMax; y++)
        {
            for (int x = chunkRect.xMin; x < chunkRect.xMax; x++)
            {
                Vector2 cellCenter = new Vector2(x + 0.5f, y + 0.5f);
                float radialDistance = cellCenter.magnitude;
                tiles[index++] = radialDistance >= innerRadius && radialDistance <= outerRadius
                    ? borderTile
                    : null;
            }
        }

        borderTilemap.SetTilesBlock(
            new BoundsInt(chunkRect.xMin, chunkRect.yMin, 0, chunkRect.width, chunkRect.height, 1),
            tiles);
        loadedChunks.Add(chunkCoord);
    }

    void ClearRect(RectInt rect)
    {
        TileBase[] emptyTiles = new TileBase[rect.width * rect.height];
        borderTilemap.SetTilesBlock(
            new BoundsInt(rect.xMin, rect.yMin, 0, rect.width, rect.height, 1),
            emptyTiles);
    }

    void EnforceBoatBoundary()
    {
        ResolveBoatReferences();
        if (boatTransform == null)
            return;

        Vector2 currentPosition = boatRigidbody != null ? boatRigidbody.position : (Vector2)boatTransform.position;
        float innerRadius = worldSettings != null ? worldSettings.WallInnerRadiusTiles : 0f;
        if (innerRadius <= 0f)
            return;

        float radialDistance = currentPosition.magnitude;
        if (radialDistance <= innerRadius)
            return;

        Vector2 outwardNormal = radialDistance > 0.0001f ? currentPosition / radialDistance : Vector2.up;
        Vector2 clampedPosition = outwardNormal * innerRadius;

        float outwardSpeed = 0f;
        Vector2 currentVelocity = Vector2.zero;
        if (boatRigidbody != null)
        {
            currentVelocity = boatRigidbody.linearVelocity;
            outwardSpeed = Vector2.Dot(currentVelocity, outwardNormal);

            if (outwardSpeed > 0f)
                boatRigidbody.linearVelocity = currentVelocity - outwardNormal * outwardSpeed;

            boatRigidbody.position = clampedPosition;
        }
        else
        {
            boatTransform.position = new Vector3(clampedPosition.x, clampedPosition.y, boatTransform.position.z);
        }

        float damageApplied = 0f;
        if (outwardSpeed >= wallImpactSpeedThreshold && Time.time >= nextWallDamageTime)
        {
            damageApplied = ApplyWallImpactDamage(outwardSpeed);
            if (damageApplied > 0f)
                TrySpawnWallImpactPoof(clampedPosition, outwardSpeed);
        }

        debugLastWallImpactSpeed = outwardSpeed;
        debugLastWallDamageApplied = damageApplied;
        debugLastWallNormal = outwardNormal;
    }

    float ApplyWallImpactDamage(float outwardSpeed)
    {
        if (boatHealthController == null)
            return 0f;

        float normalizedImpact = Mathf.InverseLerp(wallImpactSpeedThreshold, fullWallImpactSpeed, outwardSpeed);
        normalizedImpact = Mathf.Clamp01(normalizedImpact);
        float curvedImpact = normalizedImpact * normalizedImpact;
        float damage = curvedImpact * maxWallImpactDamage;
        if (damage <= 0f)
            return 0f;

        boatHealthController.TakeDamage(damage, BoatDamageSource.Boundary);
        nextWallDamageTime = Time.time + repeatedWallImpactCooldownSeconds;
        return damage;
    }

    void TrySpawnWallImpactPoof(Vector2 impactPoint, float outwardSpeed)
    {
        if (Time.time < nextWallImpactFxTime)
            return;

        GameObject impactPrefab = ResolveWallImpactPoofPrefab();
        if (impactPrefab == null)
            return;

        GameObject effectInstance = Instantiate(
            impactPrefab,
            new Vector3(impactPoint.x, impactPoint.y, 0f),
            Quaternion.identity);

        if (effectInstance != null && effectInstance.TryGetComponent(out BoatRamDamagePoofEffect poofEffect))
        {
            float severity01 = Mathf.InverseLerp(wallImpactSpeedThreshold, fullWallImpactSpeed, outwardSpeed);
            poofEffect.SetSeverity01(severity01);
        }

        nextWallImpactFxTime = Time.time + wallImpactFxCooldownSeconds;
    }

    GameObject ResolveWallImpactPoofPrefab()
    {
        if (wallImpactPoofPrefab != null)
            return wallImpactPoofPrefab;

        return boatIslandCollisionController != null ? boatIslandCollisionController.RamDamagePoofPrefab : null;
    }

    static bool RectTouchesRadiusBand(RectInt rect, float innerRadius, float outerRadius)
    {
        float nearestX = 0f;
        if (0f < rect.xMin)
            nearestX = rect.xMin;
        else if (0f > rect.xMax)
            nearestX = rect.xMax;

        float nearestY = 0f;
        if (0f < rect.yMin)
            nearestY = rect.yMin;
        else if (0f > rect.yMax)
            nearestY = rect.yMax;

        float nearestDistanceSqr = nearestX * nearestX + nearestY * nearestY;

        float farthestX = Mathf.Max(Mathf.Abs(rect.xMin), Mathf.Abs(rect.xMax));
        float farthestY = Mathf.Max(Mathf.Abs(rect.yMin), Mathf.Abs(rect.yMax));
        float farthestDistanceSqr = farthestX * farthestX + farthestY * farthestY;

        float innerRadiusSqr = innerRadius * innerRadius;
        float outerRadiusSqr = outerRadius * outerRadius;

        return nearestDistanceSqr <= outerRadiusSqr && farthestDistanceSqr >= innerRadiusSqr;
    }
}
