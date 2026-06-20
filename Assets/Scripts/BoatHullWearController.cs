using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoatHealthController))]
[RequireComponent(typeof(Rigidbody2D))]
public class BoatHullWearController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] BoatHealthController boatHealthController;
    [SerializeField] Rigidbody2D targetRb;
    [SerializeField] DayNightController dayNightController;
    [SerializeField] ShopDockController shopDockController;
    [SerializeField] Tilemap islandTilemap;

    [Header("Evaluation")]
    [SerializeField] float evaluationIntervalSeconds = 0.3f;
    [SerializeField] float movementSpeedThreshold = 0.35f;
    [SerializeField] float nearShoreDistanceWorld = 4f;
    [SerializeField] float dockSafetyDistanceWorld = 3.2f;
    [SerializeField] int shoreSearchRadiusCells = 10;

    [Header("Wear")]
    [SerializeField] [Range(0f, 1f)] float maxWearFractionPerInGameDay = 0.05f;
    [SerializeField] [Range(1f, 3f)] float movementWearSpeedMultiplier = 1.6f;
    [SerializeField] [Range(0f, 1f)] float driftWearSpeedFraction = 0.45f;

    [Header("Warnings")]
    [SerializeField] [Range(0.005f, 0.2f)] float warningMilestoneFraction = 0.025f;
    [SerializeField] string warningText = "The hull groans from the voyage.";

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] bool debugWearActive;
    [SerializeField] bool debugNearShore;
    [SerializeField] bool debugNearDock;
    [SerializeField] float debugCurrentSpeed;
    [SerializeField] float debugWearAppliedToday;
    [SerializeField] float debugDailyWearCap;
    [SerializeField] int debugLastProcessedDayCount;
    [SerializeField] float debugAccumulatedWarningThreshold;

    float nextEvaluationTime;
    float wearAppliedToday;
    float nextWarningThreshold;
    int lastProcessedDayCount = int.MinValue;

    void Awake()
    {
        if (boatHealthController == null)
            boatHealthController = GetComponent<BoatHealthController>();
        if (targetRb == null)
            targetRb = GetComponent<Rigidbody2D>();
    }

    void Start()
    {
        ResolveReferences();
        ResetDailyTracking(forceCurrentDay: true);
    }

    void Update()
    {
        ResolveReferences();
        if (boatHealthController == null || dayNightController == null)
            return;

        if (Time.unscaledTime < nextEvaluationTime)
            return;

        nextEvaluationTime = Time.unscaledTime + Mathf.Max(0.05f, evaluationIntervalSeconds);
        EvaluateWear();
    }

    void ResolveReferences()
    {
        if (dayNightController == null)
            dayNightController = FindAnyObjectByType<DayNightController>();

        if (shopDockController == null)
            shopDockController = FindAnyObjectByType<ShopDockController>();

        if (islandTilemap == null)
        {
            IslandGenerationController islandGenerationController = FindAnyObjectByType<IslandGenerationController>();
            if (islandGenerationController != null)
                islandTilemap = islandGenerationController.IslandTilemap;
        }
    }

    void EvaluateWear()
    {
        if (dayNightController == null || boatHealthController == null)
            return;

        ResetDailyTracking(forceCurrentDay: false);

        float currentSpeed = targetRb != null ? targetRb.linearVelocity.magnitude : 0f;
        bool nearDock = IsNearDock();
        bool nearShore = IsNearShore();
        bool moving = currentSpeed >= movementSpeedThreshold;
        bool wearActive = !nearDock && !nearShore && (moving || IsMeaningfullyAtSea());

        debugCurrentSpeed = currentSpeed;
        debugNearDock = nearDock;
        debugNearShore = nearShore;
        debugWearActive = wearActive;
        debugWearAppliedToday = wearAppliedToday;
        debugDailyWearCap = ResolveDailyWearCap();
        debugLastProcessedDayCount = lastProcessedDayCount;
        debugAccumulatedWarningThreshold = nextWarningThreshold;

        if (!wearActive)
            return;

        float wearCap = ResolveDailyWearCap();
        if (wearAppliedToday >= wearCap)
            return;

        float deltaTime = Mathf.Max(0f, evaluationIntervalSeconds);
        float effectiveTimeScale = dayNightController.EffectiveTimeScale;
        if (effectiveTimeScale <= 0f)
            return;

        float totalDayLengthSeconds = dayNightController.GetPhaseEndSeconds(DayNightPhase.Night);
        if (totalDayLengthSeconds <= 0f)
            return;

        float wearPerRealSecondAtBase = wearCap / totalDayLengthSeconds;
        float movementFactor = moving ? movementWearSpeedMultiplier : driftWearSpeedFraction;
        float wearAmount = deltaTime * effectiveTimeScale * wearPerRealSecondAtBase * movementFactor;
        wearAmount = Mathf.Min(wearAmount, wearCap - wearAppliedToday);
        if (wearAmount <= 0f)
            return;

        boatHealthController.TakeDamage(wearAmount, BoatDamageSource.HullWear);
        wearAppliedToday += wearAmount;

        if (wearAppliedToday >= nextWarningThreshold)
        {
            ShowWarning();
            nextWarningThreshold += ResolveWarningThresholdAmount();
        }

        debugWearAppliedToday = wearAppliedToday;
        debugAccumulatedWarningThreshold = nextWarningThreshold;
    }

    void ResetDailyTracking(bool forceCurrentDay)
    {
        int currentDay = dayNightController != null ? dayNightController.DayCount : 0;
        if (!forceCurrentDay && currentDay == lastProcessedDayCount)
            return;

        lastProcessedDayCount = currentDay;
        wearAppliedToday = 0f;
        nextWarningThreshold = ResolveWarningThresholdAmount();
    }

    float ResolveDailyWearCap()
    {
        if (boatHealthController == null)
            return 0f;

        return boatHealthController.MaxHealth * Mathf.Clamp01(maxWearFractionPerInGameDay);
    }

    float ResolveWarningThresholdAmount()
    {
        if (boatHealthController == null)
            return float.PositiveInfinity;

        return boatHealthController.MaxHealth * Mathf.Max(0.001f, warningMilestoneFraction);
    }

    bool IsNearDock()
    {
        if (shopDockController == null)
            return false;

        if (!shopDockController.TryGetNearestShopDock(transform.position, out ShopDockController.ShopDockQueryResult result))
            return false;

        return result.Distance <= dockSafetyDistanceWorld;
    }

    bool IsNearShore()
    {
        if (islandTilemap == null)
            return false;

        Vector3Int boatCell = islandTilemap.WorldToCell(transform.position);
        int maxCellsFromShore = Mathf.Max(1, Mathf.CeilToInt(nearShoreDistanceWorld / Mathf.Max(islandTilemap.cellSize.x, 0.01f)));
        int searchRadius = Mathf.Max(maxCellsFromShore, shoreSearchRadiusCells);

        for (int y = boatCell.y - searchRadius; y <= boatCell.y + searchRadius; y++)
        {
            for (int x = boatCell.x - searchRadius; x <= boatCell.x + searchRadius; x++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                if (islandTilemap.GetTile(cell) == null)
                    continue;

                Vector3 cellCenter = islandTilemap.GetCellCenterWorld(cell);
                if (Vector2.Distance(transform.position, cellCenter) <= nearShoreDistanceWorld)
                    return true;
            }
        }

        return false;
    }

    bool IsMeaningfullyAtSea()
    {
        return !debugNearDock && !debugNearShore;
    }

    void ShowWarning()
    {
        if (string.IsNullOrWhiteSpace(warningText))
            return;

        FishingInteractionController fishingInteractionController = FishingInteractionController.ActiveInstance;
        if (fishingInteractionController != null)
        {
            fishingInteractionController.ShowExternalPrompt(warningText);
            return;
        }

        Debug.Log(warningText, this);
    }

    void OnValidate()
    {
        evaluationIntervalSeconds = Mathf.Max(0.05f, evaluationIntervalSeconds);
        movementSpeedThreshold = Mathf.Max(0f, movementSpeedThreshold);
        nearShoreDistanceWorld = Mathf.Max(0.1f, nearShoreDistanceWorld);
        dockSafetyDistanceWorld = Mathf.Max(0.1f, dockSafetyDistanceWorld);
        shoreSearchRadiusCells = Mathf.Max(1, shoreSearchRadiusCells);
        movementWearSpeedMultiplier = Mathf.Max(1f, movementWearSpeedMultiplier);
        driftWearSpeedFraction = Mathf.Clamp01(driftWearSpeedFraction);
        warningMilestoneFraction = Mathf.Clamp(warningMilestoneFraction, 0.001f, 0.5f);
    }
}
