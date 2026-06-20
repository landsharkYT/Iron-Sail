using System;
using System.Collections.Generic;
using UnityEngine;

public class TreasureHuntController : MonoBehaviour
{
    public readonly struct ShopTalkResult
    {
        public readonly string[] whiteLines;
        public readonly string yellowUpdateLine;
        public readonly bool isHelpful;
        public readonly bool isFinalReveal;

        public int WhiteLineCount => whiteLines != null ? whiteLines.Length : 0;
        public bool HasYellowUpdateLine => !string.IsNullOrWhiteSpace(yellowUpdateLine);

        public ShopTalkResult(string[] whiteLines, string yellowUpdateLine, bool isHelpful, bool isFinalReveal)
        {
            this.whiteLines = whiteLines ?? Array.Empty<string>();
            this.yellowUpdateLine = yellowUpdateLine ?? string.Empty;
            this.isHelpful = isHelpful;
            this.isFinalReveal = isFinalReveal;
        }
    }

    readonly struct DialogueContent
    {
        public readonly string[] whiteLines;
        public readonly string yellowUpdateLine;

        public DialogueContent(string[] whiteLines, string yellowUpdateLine)
        {
            this.whiteLines = whiteLines ?? Array.Empty<string>();
            this.yellowUpdateLine = yellowUpdateLine ?? string.Empty;
        }
    }

    struct WorldShopRecord
    {
        public Vector2Int shopId;
        public Vector2 worldPosition;
        public float distanceToTreasure;
    }

    struct ResolvedShopOutcome
    {
        public bool exhausted;
        public bool isHelpful;
        public bool isFinalReveal;
        public Vector2Int leadShopId;
        public string[] whiteLines;
        public string yellowUpdateLine;
    }

    readonly struct StaticLeadCandidate
    {
        public readonly Vector2Int shopId;
        public readonly float distanceFromCurrent;
        public readonly float progressToFinal;

        public StaticLeadCandidate(Vector2Int shopId, float distanceFromCurrent, float progressToFinal)
        {
            this.shopId = shopId;
            this.distanceFromCurrent = distanceFromCurrent;
            this.progressToFinal = progressToFinal;
        }
    }

    struct ReachabilityFrame
    {
        public Vector2Int shopId;
        public int issuedLeadCount;
        public int nextCandidateIndex;
    }

    public static TreasureHuntController ActiveInstance { get; private set; }

    [Header("References")]
    [SerializeField] IslandGenerationController islandGenerationController;
    [SerializeField] MapMarkerController mapMarkerController;
    [SerializeField] MapDiscoveryController mapDiscoveryController;
    [SerializeField] ShopDialogueLibrary shopDialogueLibrary;

    [Header("Route Search")]
    [SerializeField][Min(4)] int candidateNeighborCount = 12;
    [SerializeField][Min(128f)] float candidateSearchRadius = 2600f;
    [SerializeField][Min(256f)] float fallbackSearchRadius = 4200f;
    [SerializeField][Min(128f)] float minimumLeadDistance = 900f;
    [SerializeField][Min(64f)] float minimumProgressTowardFinal = 550f;
    [SerializeField][Min(128f)] float idealLeadDistance = 1800f;
    [SerializeField][Range(0f, 1f)] float detourRandomnessWeight = 0.12f;
    [SerializeField][Range(0f, 1f)] float undiscoveredShopBonus = 0.1f;
    [SerializeField][Min(0)] int minimumLeadCountBeforeFinalShopEligible = 1;

    [Header("Treasure Hunt Validation")]
    [SerializeField] bool validateTreasurePlacementOnPlay = false;
    [SerializeField][Min(128f)] float validationStartRegionRadius = 1200f;
    [SerializeField][Min(128f)] float validationTreasureLocalSearchRadius = 2200f;
    [SerializeField][Min(1)] int validationMaxTreasureCandidatesToTest = 8;
    [SerializeField][Min(1)] int validationMaxStartRegionShopsToTest = 6;

    [Header("Dialogue")]
    [SerializeField] string clueUpdateText = "I've marked it on your map on a place you can go to get more information on the treasure.";
    [SerializeField] string treasureUpdateText = "I've marked the treasure on your map.";
    [SerializeField] string repeatTalkText = "You've already asked me!";
    [SerializeField] string routeCalculationErrorText = "There has been an error in calculating the route to the treasure. Please contact the dev.";

    [Header("Route Search Safety")]
    [SerializeField][Min(16)] int reachabilitySearchExpansionBudget = 512;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] bool debugInitialized;
    [SerializeField] bool debugTreasureValid;
    [SerializeField] bool debugHuntStarted;
    [SerializeField] bool debugFinalRevealIssued;
    [SerializeField] int debugWorldShopCount;
    [SerializeField] int debugIssuedLeadCount;
    [SerializeField] Vector2 debugTreasureWorldPosition;
    [SerializeField] Vector2Int debugFinalRevealShopId;
    [SerializeField] Vector2 debugFinalRevealShopWorldPosition;
    [SerializeField] Vector2Int debugCurrentLeadShopId;
    [SerializeField] int debugExhaustedShopCount;
    [SerializeField] int debugUsedLeadTargetCount;
    [SerializeField] string debugLastTalkBody;
    [SerializeField] string debugLastTalkUpdate;

    readonly List<IslandGenerationController.ShopDockRegistration> allShopRegistrations = new List<IslandGenerationController.ShopDockRegistration>();
    readonly List<WorldShopRecord> allWorldShops = new List<WorldShopRecord>();
    readonly List<IslandGenerationController.TreasurePlacementCandidate> treasurePlacementCandidates = new List<IslandGenerationController.TreasurePlacementCandidate>();
    readonly List<WorldShopRecord> validationStartRegionShops = new List<WorldShopRecord>();
    readonly List<WorldShopRecord> validationCandidateRegionShops = new List<WorldShopRecord>();
    readonly List<WorldShopRecord> validationWorkingShops = new List<WorldShopRecord>();
    readonly Dictionary<Vector2Int, WorldShopRecord> worldShopsById = new Dictionary<Vector2Int, WorldShopRecord>();
    readonly Dictionary<Vector2Int, ResolvedShopOutcome> resolvedOutcomes = new Dictionary<Vector2Int, ResolvedShopOutcome>();
    readonly Dictionary<Vector2Int, List<StaticLeadCandidate>> staticLeadCandidateCache = new Dictionary<Vector2Int, List<StaticLeadCandidate>>();
    readonly HashSet<Vector2Int> exhaustedShopIds = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> usedLeadTargetShopIds = new HashSet<Vector2Int>();
    readonly List<WorldShopRecord> candidateBuffer = new List<WorldShopRecord>();
    readonly List<ReachabilityFrame> reachabilityStack = new List<ReachabilityFrame>();
    readonly HashSet<Vector2Int> reachabilityVisitedShopIds = new HashSet<Vector2Int>();

    bool initialized;
    bool treasureLocationValid;
    bool routeNetworkReady;
    bool huntStarted;
    bool finalRevealIssued;
    int issuedLeadCount;
    Vector2 treasureWorldPosition;
    Vector2Int finalRevealShopId;
    Vector2Int currentLeadShopId;
    WorldShopRecord finalRevealShopRecord;

    void OnEnable()
    {
        ActiveInstance = this;
        TryInitialize();
    }

    void Start()
    {
        TryInitialize();
    }

    void OnDisable()
    {
        if (islandGenerationController != null)
            islandGenerationController.ClearForcedVisibleShopDock();

        if (ActiveInstance == this)
            ActiveInstance = null;
    }

    void TryInitialize()
    {
        if (initialized)
            return;

        if (islandGenerationController == null)
            islandGenerationController = FindAnyObjectByType<IslandGenerationController>();

        if (mapMarkerController == null)
            mapMarkerController = MapMarkerController.ActiveInstance ?? FindAnyObjectByType<MapMarkerController>();

        if (mapDiscoveryController == null)
            mapDiscoveryController = MapDiscoveryController.ActiveInstance ?? FindAnyObjectByType<MapDiscoveryController>();

        if (islandGenerationController == null || mapMarkerController == null)
            return;

        if (validateTreasurePlacementOnPlay)
            TryApplyValidatedTreasurePlacement();
        ResetRuntimeState();
        treasureLocationValid =
            islandGenerationController.TryGetTreasureTargetContactAnchor(out treasureWorldPosition)
            || islandGenerationController.TryGetTreasureIslandCenter(out treasureWorldPosition);
        initialized = treasureLocationValid;
        debugInitialized = initialized;
        SyncDebugState();
    }

    void ResetRuntimeState()
    {
        allWorldShops.Clear();
        worldShopsById.Clear();
        staticLeadCandidateCache.Clear();
        allShopRegistrations.Clear();
        resolvedOutcomes.Clear();
        exhaustedShopIds.Clear();
        usedLeadTargetShopIds.Clear();
        currentLeadShopId = default;
        finalRevealShopId = default;
        finalRevealShopRecord = default;
        routeNetworkReady = false;
        huntStarted = false;
        finalRevealIssued = false;
        issuedLeadCount = 0;
        debugLastTalkBody = string.Empty;
        debugLastTalkUpdate = string.Empty;
        SyncFocusedLeadShopVisibility();
    }

    bool EnsureRouteNetworkReady()
    {
        if (routeNetworkReady)
            return true;

        if (!treasureLocationValid
            && !islandGenerationController.TryGetTreasureTargetContactAnchor(out treasureWorldPosition)
            && !islandGenerationController.TryGetTreasureIslandCenter(out treasureWorldPosition))
        {
            debugTreasureValid = false;
            debugWorldShopCount = 0;
            return false;
        }

        allWorldShops.Clear();
        worldShopsById.Clear();
        allShopRegistrations.Clear();
        islandGenerationController.CollectAllShopDockRegistrations(allShopRegistrations);
        for (int i = 0; i < allShopRegistrations.Count; i++)
        {
            IslandGenerationController.ShopDockRegistration registration = allShopRegistrations[i];
            WorldShopRecord shop = new WorldShopRecord
            {
                shopId = registration.ShopId,
                worldPosition = registration.AnchorWorldPosition,
                distanceToTreasure = Vector2.Distance(registration.AnchorWorldPosition, treasureWorldPosition)
            };
            allWorldShops.Add(shop);
            worldShopsById[shop.shopId] = shop;
        }

        ChooseFinalRevealShop();
        routeNetworkReady = finalRevealShopId != default;
        SyncDebugState();
        return routeNetworkReady;
    }

    void TryApplyValidatedTreasurePlacement()
    {
        if (islandGenerationController == null)
            return;

        allShopRegistrations.Clear();
        islandGenerationController.CollectShopDockRegistrationsInRadius(allShopRegistrations, Vector2.zero, validationStartRegionRadius);
        if (allShopRegistrations.Count == 0)
            return;

        List<WorldShopRecord> validationShops = new List<WorldShopRecord>(allShopRegistrations.Count);
        for (int i = 0; i < allShopRegistrations.Count; i++)
        {
            validationShops.Add(new WorldShopRecord
            {
                shopId = allShopRegistrations[i].ShopId,
                worldPosition = allShopRegistrations[i].AnchorWorldPosition,
                distanceToTreasure = 0f
            });
        }

        treasurePlacementCandidates.Clear();
        islandGenerationController.CollectTreasurePlacementCandidates(treasurePlacementCandidates);
        if (treasurePlacementCandidates.Count == 0)
            return;

        treasurePlacementCandidates.Sort((a, b) => b.score.CompareTo(a.score));
        int candidateCountToTest = Mathf.Min(
            treasurePlacementCandidates.Count,
            Mathf.Max(1, validationMaxTreasureCandidatesToTest));
        int bestAttemptIndex = -1;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < candidateCountToTest; i++)
        {
            IslandGenerationController.TreasurePlacementCandidate candidate = treasurePlacementCandidates[i];
            if (!DoesTreasurePlacementPassValidation(validationShops, candidate))
                continue;

            if (candidate.score <= bestScore)
                continue;

            bestScore = candidate.score;
            bestAttemptIndex = candidate.attemptIndex;
        }

        if (bestAttemptIndex >= 0)
        {
            islandGenerationController.TryApplyTreasurePlacementAttempt(bestAttemptIndex);
        }
    }

    bool DoesTreasurePlacementPassValidation(List<WorldShopRecord> worldShops, IslandGenerationController.TreasurePlacementCandidate treasureCandidate)
    {
        if (worldShops == null || worldShops.Count == 0)
            return false;

        BuildValidationStartRegionShops(worldShops, validationStartRegionShops);
        if (validationStartRegionShops.Count == 0)
            return false;

        allShopRegistrations.Clear();
        islandGenerationController.CollectShopDockRegistrationsInRadius(
            allShopRegistrations,
            treasureCandidate.center,
            validationTreasureLocalSearchRadius);
        if (allShopRegistrations.Count == 0)
            return false;

        validationCandidateRegionShops.Clear();
        for (int i = 0; i < allShopRegistrations.Count; i++)
        {
            validationCandidateRegionShops.Add(new WorldShopRecord
            {
                shopId = allShopRegistrations[i].ShopId,
                worldPosition = allShopRegistrations[i].AnchorWorldPosition,
                distanceToTreasure = 0f
            });
        }

        if (!TryChooseFinalRevealShop(validationCandidateRegionShops, treasureCandidate.center, out WorldShopRecord finalShop))
            return false;

        for (int i = 0; i < validationStartRegionShops.Count; i++)
        {
            if (validationStartRegionShops[i].shopId == finalShop.shopId)
                return false;
        }

        validationWorkingShops.Clear();
        AddUniqueValidationShops(validationWorkingShops, validationStartRegionShops);
        AddUniqueValidationShops(validationWorkingShops, validationCandidateRegionShops);

        bool foundUsefulOpening = false;
        for (int i = 0; i < validationStartRegionShops.Count; i++)
        {
            WorldShopRecord startShop = validationStartRegionShops[i];
            if (TrySimulateFirstLead(
                    validationWorkingShops,
                    startShop,
                    treasureCandidate.center,
                    finalShop.shopId,
                    out _))
            {
                foundUsefulOpening = true;
                break;
            }
        }

        return foundUsefulOpening;
    }

    void BuildValidationStartRegionShops(List<WorldShopRecord> worldShops, List<WorldShopRecord> results)
    {
        results.Clear();
        if (worldShops == null)
            return;

        for (int i = 0; i < worldShops.Count; i++)
        {
            WorldShopRecord shop = worldShops[i];
            if (shop.worldPosition.magnitude > validationStartRegionRadius)
                continue;

            results.Add(shop);
        }

        results.Sort((a, b) =>
        {
            float aDistance = a.worldPosition.sqrMagnitude;
            float bDistance = b.worldPosition.sqrMagnitude;
            int compare = aDistance.CompareTo(bDistance);
            if (compare != 0)
                return compare;

            compare = a.shopId.x.CompareTo(b.shopId.x);
            return compare != 0 ? compare : a.shopId.y.CompareTo(b.shopId.y);
        });

        int maxCount = Mathf.Max(1, validationMaxStartRegionShopsToTest);
        if (results.Count > maxCount)
            results.RemoveRange(maxCount, results.Count - maxCount);
    }

    static void AddUniqueValidationShops(List<WorldShopRecord> destination, List<WorldShopRecord> source)
    {
        for (int i = 0; i < source.Count; i++)
        {
            WorldShopRecord candidate = source[i];
            bool alreadyPresent = false;
            for (int j = 0; j < destination.Count; j++)
            {
                if (destination[j].shopId != candidate.shopId)
                    continue;

                alreadyPresent = true;
                break;
            }

            if (!alreadyPresent)
                destination.Add(candidate);
        }
    }

    bool TryChooseFinalRevealShop(List<WorldShopRecord> worldShops, Vector2 treasurePosition, out WorldShopRecord finalShop)
    {
        float nearestDistance = float.PositiveInfinity;
        finalShop = default;
        bool foundShop = false;

        for (int i = 0; i < worldShops.Count; i++)
        {
            WorldShopRecord shop = worldShops[i];
            float distance = Vector2.Distance(shop.worldPosition, treasurePosition);
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            finalShop = shop;
            foundShop = true;
        }

        return foundShop;
    }

    bool TrySimulateFirstLead(
        List<WorldShopRecord> worldShops,
        WorldShopRecord startShop,
        Vector2 treasurePosition,
        Vector2Int simulatedFinalShopId,
        out WorldShopRecord firstLead)
    {
        HashSet<Vector2Int> simulatedResolvedShops = new HashSet<Vector2Int>();
        HashSet<Vector2Int> simulatedUsedLeadTargets = new HashSet<Vector2Int>();

        if (!TryChooseLeadFromShopSim(
                worldShops,
                startShop,
                treasurePosition,
                simulatedFinalShopId,
                simulatedResolvedShops,
                simulatedUsedLeadTargets,
                0,
                out firstLead,
                true))
        {
            if (!TryChooseAnyNonFinalFallbackLeadSim(
                    worldShops,
                    startShop,
                    treasurePosition,
                    simulatedFinalShopId,
                    simulatedResolvedShops,
                    simulatedUsedLeadTargets,
                    out firstLead))
            {
                return false;
            }
        }

        if (firstLead.worldPosition.magnitude <= validationStartRegionRadius)
            return false;

        return true;
    }

    public bool TryResolveShopTalk(Vector2Int shopId, out ShopTalkResult talkResult)
    {
        TryInitialize();
        if (!initialized || !EnsureRouteNetworkReady() || !worldShopsById.TryGetValue(shopId, out WorldShopRecord currentShop))
        {
            talkResult = default;
            return false;
        }

        if (resolvedOutcomes.TryGetValue(shopId, out ResolvedShopOutcome existingOutcome))
        {
            talkResult = BuildRepeatTalkResult(shopId);
            debugLastTalkBody = FlattenWhiteLines(talkResult.whiteLines);
            debugLastTalkUpdate = talkResult.yellowUpdateLine;
            return true;
        }

        ResolvedShopOutcome resolvedOutcome;
        if (finalRevealIssued)
        {
            resolvedOutcome = ResolvePostRevealOutcome(currentShop);
        }
        else if (shopId == finalRevealShopId)
        {
            resolvedOutcome = ResolveFinalRevealOutcome(currentShop);
        }
        else if (!huntStarted)
        {
            resolvedOutcome = ResolveGuaranteedOpeningLead(currentShop);
        }
        else if (TryChooseLeadFromShop(currentShop, out WorldShopRecord leadShop))
        {
            resolvedOutcome = ResolveHelpfulLeadOutcome(currentShop, leadShop);
        }
        else
        {
            if (currentLeadShopId == shopId)
            {
                Debug.LogWarning($"[TreasureHuntController] Marked lead shop {shopId} resolved without a follow-up lead. This should have been filtered during route planning.", this);
                resolvedOutcome = ResolveMarkedLeadRouteErrorOutcome();
            }
            else
            {
                resolvedOutcome = ResolveNonHelpfulOutcome(currentShop);
            }
        }

        resolvedOutcomes[shopId] = resolvedOutcome;
        if (resolvedOutcome.exhausted)
            exhaustedShopIds.Add(shopId);
        talkResult = BuildTalkResult(resolvedOutcome);
        debugLastTalkBody = FlattenWhiteLines(talkResult.whiteLines);
        debugLastTalkUpdate = talkResult.yellowUpdateLine;
        SyncDebugState();
        return true;
    }

    void ChooseFinalRevealShop()
    {
        float nearestDistance = float.PositiveInfinity;
        WorldShopRecord chosenShop = default;
        bool foundShop = false;

        for (int i = 0; i < allWorldShops.Count; i++)
        {
            WorldShopRecord shop = allWorldShops[i];
            if (shop.distanceToTreasure >= nearestDistance)
                continue;

            nearestDistance = shop.distanceToTreasure;
            chosenShop = shop;
            foundShop = true;
        }

        if (!foundShop)
            return;

        finalRevealShopId = chosenShop.shopId;
        finalRevealShopRecord = chosenShop;
        debugFinalRevealShopWorldPosition = chosenShop.worldPosition;
    }

    ResolvedShopOutcome ResolveGuaranteedOpeningLead(WorldShopRecord currentShop)
    {
        huntStarted = true;

        if (TryChooseLeadFromShop(currentShop, out WorldShopRecord leadShop, true))
            return ResolveHelpfulLeadOutcome(currentShop, leadShop);

        if (TryChooseAnyNonFinalFallbackLead(currentShop, out leadShop))
            return ResolveHelpfulLeadOutcome(currentShop, leadShop);

        return ResolveNonHelpfulOutcome(currentShop);
    }

    ResolvedShopOutcome ResolveHelpfulLeadOutcome(WorldShopRecord currentShop, WorldShopRecord leadShop)
    {
        issuedLeadCount++;
        currentLeadShopId = leadShop.shopId;
        usedLeadTargetShopIds.Add(leadShop.shopId);
        SyncFocusedLeadShopVisibility();
        mapMarkerController.ClearMarkersOfType(MapMarkerController.MarkerType.YellowX);
        mapMarkerController.SetSingleMarker(MapMarkerController.MarkerType.RedX, leadShop.worldPosition, $"Lead {issuedLeadCount}");

        DialogueContent dialogue = ResolveDialogueContent(
            ShopDialogueLibrary.DialogueCategory.HelpfulIntermediate,
            currentShop.shopId.x,
            currentShop.shopId.y,
            issuedLeadCount + 31,
            currentShop.worldPosition,
            leadShop.worldPosition,
            BuildHelpfulBodyText(currentShop, leadShop),
            clueUpdateText);

        return new ResolvedShopOutcome
        {
            exhausted = true,
            isHelpful = true,
            isFinalReveal = false,
            leadShopId = leadShop.shopId,
            whiteLines = dialogue.whiteLines,
            yellowUpdateLine = dialogue.yellowUpdateLine
        };
    }

    ResolvedShopOutcome ResolveFinalRevealOutcome(WorldShopRecord currentShop)
    {
        huntStarted = true;
        finalRevealIssued = true;
        currentLeadShopId = default;
        SyncFocusedLeadShopVisibility();
        mapMarkerController.ClearMarkersOfType(MapMarkerController.MarkerType.RedX);
        mapMarkerController.SetSingleMarker(MapMarkerController.MarkerType.YellowX, treasureWorldPosition, "Treasure");

        DialogueContent dialogue = ResolveDialogueContent(
            ShopDialogueLibrary.DialogueCategory.FinalReveal,
            currentShop.shopId.x,
            currentShop.shopId.y,
            811,
            currentShop.worldPosition,
            treasureWorldPosition,
            BuildFinalRevealBodyText(currentShop),
            treasureUpdateText);

        return new ResolvedShopOutcome
        {
            exhausted = true,
            isHelpful = true,
            isFinalReveal = true,
            leadShopId = default,
            whiteLines = dialogue.whiteLines,
            yellowUpdateLine = dialogue.yellowUpdateLine
        };
    }

    ResolvedShopOutcome ResolveNonHelpfulOutcome(WorldShopRecord currentShop)
    {
        DialogueContent dialogue = ResolveDialogueContent(
            ShopDialogueLibrary.DialogueCategory.NonHelpful,
            currentShop.shopId.x,
            currentShop.shopId.y,
            509,
            currentShop.worldPosition,
            currentShop.worldPosition,
            BuildNonHelpfulBodyText(currentShop),
            string.Empty);

        return new ResolvedShopOutcome
        {
            exhausted = true,
            isHelpful = false,
            isFinalReveal = false,
            leadShopId = default,
            whiteLines = dialogue.whiteLines,
            yellowUpdateLine = dialogue.yellowUpdateLine
        };
    }

    ResolvedShopOutcome ResolveMarkedLeadRouteErrorOutcome()
    {
        ClearCurrentLeadMarker();

        return new ResolvedShopOutcome
        {
            exhausted = true,
            isHelpful = false,
            isFinalReveal = false,
            leadShopId = default,
            whiteLines = new[] { routeCalculationErrorText },
            yellowUpdateLine = string.Empty
        };
    }

    ResolvedShopOutcome ResolvePostRevealOutcome(WorldShopRecord currentShop)
    {
        DialogueContent dialogue = ResolveDialogueContent(
            ShopDialogueLibrary.DialogueCategory.PostReveal,
            currentShop.shopId.x,
            currentShop.shopId.y,
            947,
            currentShop.worldPosition,
            treasureWorldPosition,
            "The merchant gives you a tired smile. \"You've already got the answer you need. Best go claim it.\"",
            string.Empty);

        return new ResolvedShopOutcome
        {
            exhausted = true,
            isHelpful = false,
            isFinalReveal = false,
            leadShopId = default,
            whiteLines = dialogue.whiteLines,
            yellowUpdateLine = dialogue.yellowUpdateLine
        };
    }

    ShopTalkResult BuildTalkResult(ResolvedShopOutcome outcome)
    {
        return new ShopTalkResult(
            outcome.whiteLines != null && outcome.whiteLines.Length > 0
                ? outcome.whiteLines
                : new[] { repeatTalkText },
            outcome.yellowUpdateLine,
            outcome.isHelpful,
            outcome.isFinalReveal);
    }

    ShopTalkResult BuildRepeatTalkResult(Vector2Int shopId)
    {
        DialogueContent dialogue = ResolveDialogueContent(
            ShopDialogueLibrary.DialogueCategory.RepeatTalk,
            shopId.x,
            shopId.y,
            issuedLeadCount + 173,
            Vector2.zero,
            Vector2.zero,
            repeatTalkText,
            string.Empty);

        return new ShopTalkResult(
            dialogue.whiteLines.Length > 0 ? dialogue.whiteLines : new[] { repeatTalkText },
            dialogue.yellowUpdateLine,
            false,
            false);
    }

    bool TryChooseLeadFromShop(WorldShopRecord currentShop, out WorldShopRecord chosenShop, bool forceExcludeFinal = false)
    {
        bool allowFinalTarget = !forceExcludeFinal && issuedLeadCount >= minimumLeadCountBeforeFinalShopEligible;
        if (TryChooseReachableLeadFromPool(currentShop, candidateSearchRadius, allowFinalTarget, true, out chosenShop))
            return true;

        return TryChooseReachableLeadFromPool(currentShop, fallbackSearchRadius, allowFinalTarget, false, out chosenShop);
    }

    bool TryChooseAnyNonFinalFallbackLead(WorldShopRecord currentShop, out WorldShopRecord chosenShop)
    {
        if (!worldShopsById.ContainsKey(finalRevealShopId))
        {
            chosenShop = default;
            return false;
        }

        candidateBuffer.Clear();
        List<StaticLeadCandidate> staticCandidates = GetOrBuildStaticLeadCandidates(currentShop);
        for (int i = 0; i < staticCandidates.Count; i++)
        {
            StaticLeadCandidate candidate = staticCandidates[i];
            if (candidate.shopId == finalRevealShopId)
                continue;
            if (exhaustedShopIds.Contains(candidate.shopId) || usedLeadTargetShopIds.Contains(candidate.shopId))
                continue;
            if (!CanCandidateReachFinal(currentShop.shopId, candidate.shopId, issuedLeadCount + 1))
                continue;
            if (!worldShopsById.TryGetValue(candidate.shopId, out WorldShopRecord candidateRecord))
                continue;

            candidateBuffer.Add(candidateRecord);
        }

        if (candidateBuffer.Count == 0)
        {
            chosenShop = default;
            return false;
        }

        candidateBuffer.Sort((a, b) =>
            Vector2.Distance(a.worldPosition, currentShop.worldPosition)
                .CompareTo(Vector2.Distance(b.worldPosition, currentShop.worldPosition)));
        chosenShop = candidateBuffer[0];
        return true;
    }

    bool TryChooseLeadFromPool(WorldShopRecord currentShop, float maxRadius, bool allowFinalTarget, bool requireStrongProgress, out WorldShopRecord chosenShop)
    {
        return TryChooseLeadFromPoolSim(
            allWorldShops,
            currentShop,
            treasureWorldPosition,
            finalRevealShopId,
            exhaustedShopIds,
            usedLeadTargetShopIds,
            issuedLeadCount,
            maxRadius,
            allowFinalTarget,
            requireStrongProgress,
            true,
            out chosenShop);
    }

    bool TryChooseLeadFromShopSim(
        List<WorldShopRecord> worldShops,
        WorldShopRecord currentShop,
        Vector2 treasurePosition,
        Vector2Int simulatedFinalShopId,
        IEnumerable<Vector2Int> resolvedShopIds,
        IEnumerable<Vector2Int> usedLeadTargetIds,
        int simulatedIssuedLeadCount,
        out WorldShopRecord chosenShop,
        bool forceExcludeFinal = false,
        bool validateContinuation = true)
    {
        bool allowFinalTarget = !forceExcludeFinal && simulatedIssuedLeadCount >= minimumLeadCountBeforeFinalShopEligible;
        if (TryChooseLeadFromPoolSim(
                worldShops,
                currentShop,
                treasurePosition,
                simulatedFinalShopId,
                resolvedShopIds,
                usedLeadTargetIds,
                simulatedIssuedLeadCount,
                candidateSearchRadius,
                allowFinalTarget,
                true,
                validateContinuation,
                out chosenShop))
        {
            return true;
        }

        return TryChooseLeadFromPoolSim(
            worldShops,
            currentShop,
            treasurePosition,
            simulatedFinalShopId,
            resolvedShopIds,
            usedLeadTargetIds,
            simulatedIssuedLeadCount,
            fallbackSearchRadius,
            allowFinalTarget,
            false,
            validateContinuation,
            out chosenShop);
    }

    bool TryChooseAnyNonFinalFallbackLeadSim(
        List<WorldShopRecord> worldShops,
        WorldShopRecord currentShop,
        Vector2 treasurePosition,
        Vector2Int simulatedFinalShopId,
        IEnumerable<Vector2Int> resolvedShopIds,
        IEnumerable<Vector2Int> usedLeadTargetIds,
        out WorldShopRecord chosenShop)
    {
        HashSet<Vector2Int> resolvedSet = EnsureHashSet(resolvedShopIds);
        HashSet<Vector2Int> usedLeadSet = EnsureHashSet(usedLeadTargetIds);

        candidateBuffer.Clear();
        for (int i = 0; i < worldShops.Count; i++)
        {
            WorldShopRecord candidate = worldShops[i];
            if (candidate.shopId == currentShop.shopId || candidate.shopId == simulatedFinalShopId)
                continue;
            if (resolvedSet.Contains(candidate.shopId))
                continue;
            if (usedLeadSet.Contains(candidate.shopId))
                continue;
            if (Vector2.Distance(candidate.worldPosition, currentShop.worldPosition) < minimumLeadDistance)
                continue;
            if (!CanLeadCandidateContinueSim(
                    worldShops,
                    currentShop,
                    candidate,
                    treasurePosition,
                    simulatedFinalShopId,
                    resolvedSet,
                    usedLeadSet,
                    1,
                    false))
            {
                continue;
            }

            candidateBuffer.Add(candidate);
        }

        if (candidateBuffer.Count == 0)
        {
            chosenShop = default;
            return false;
        }

        candidateBuffer.Sort((a, b) =>
        {
            float aDistance = Vector2.Distance(a.worldPosition, currentShop.worldPosition);
            float bDistance = Vector2.Distance(b.worldPosition, currentShop.worldPosition);
            int distanceCompare = aDistance.CompareTo(bDistance);
            return distanceCompare != 0 ? distanceCompare : a.shopId.x.CompareTo(b.shopId.x);
        });
        chosenShop = candidateBuffer[0];
        return true;
    }

    bool TryChooseLeadFromPoolSim(
        List<WorldShopRecord> worldShops,
        WorldShopRecord currentShop,
        Vector2 treasurePosition,
        Vector2Int simulatedFinalShopId,
        IEnumerable<Vector2Int> resolvedShopIds,
        IEnumerable<Vector2Int> usedLeadTargetIds,
        int simulatedIssuedLeadCount,
        float maxRadius,
        bool allowFinalTarget,
        bool requireStrongProgress,
        bool validateContinuation,
        out WorldShopRecord chosenShop)
    {
        HashSet<Vector2Int> resolvedSet = EnsureHashSet(resolvedShopIds);
        HashSet<Vector2Int> usedLeadSet = EnsureHashSet(usedLeadTargetIds);
        WorldShopRecord finalShop;
        if (worldShops == allWorldShops && simulatedFinalShopId == finalRevealShopId)
        {
            finalShop = finalRevealShopRecord;
        }
        else if (!TryGetShopById(worldShops, simulatedFinalShopId, out finalShop))
        {
            chosenShop = default;
            return false;
        }

        candidateBuffer.Clear();
        float currentDistanceToFinal = Vector2.Distance(currentShop.worldPosition, finalShop.worldPosition);

        for (int i = 0; i < worldShops.Count; i++)
        {
            WorldShopRecord candidate = worldShops[i];
            if (candidate.shopId == currentShop.shopId)
                continue;
            if (candidate.shopId == simulatedFinalShopId && !allowFinalTarget)
                continue;
            if (resolvedSet.Contains(candidate.shopId))
                continue;
            if (usedLeadSet.Contains(candidate.shopId))
                continue;

            float distanceFromCurrent = Vector2.Distance(candidate.worldPosition, currentShop.worldPosition);
            if (distanceFromCurrent < minimumLeadDistance || distanceFromCurrent > maxRadius)
                continue;

            float progressToFinal = currentDistanceToFinal - Vector2.Distance(candidate.worldPosition, finalShop.worldPosition);
            if (progressToFinal <= 0f)
                continue;

            if (requireStrongProgress && progressToFinal < minimumProgressTowardFinal)
                continue;
            if (validateContinuation && !CanLeadCandidateContinueSim(
                    worldShops,
                    currentShop,
                    candidate,
                    treasurePosition,
                    simulatedFinalShopId,
                    resolvedSet,
                    usedLeadSet,
                    simulatedIssuedLeadCount + 1,
                    allowFinalTarget))
            {
                continue;
            }

            candidateBuffer.Add(candidate);
        }

        if (candidateBuffer.Count == 0)
        {
            chosenShop = default;
            return false;
        }

        candidateBuffer.Sort((a, b) =>
            Vector2.Distance(currentShop.worldPosition, a.worldPosition)
            .CompareTo(Vector2.Distance(currentShop.worldPosition, b.worldPosition)));
        if (candidateBuffer.Count > candidateNeighborCount)
            candidateBuffer.RemoveRange(candidateNeighborCount, candidateBuffer.Count - candidateNeighborCount);

        float bestScore = float.NegativeInfinity;
        int bestIndex = -1;
        for (int i = 0; i < candidateBuffer.Count; i++)
        {
            WorldShopRecord candidate = candidateBuffer[i];
            float score = ScoreLeadCandidateSim(
                currentShop,
                candidate,
                finalShop.shopId,
                finalShop.worldPosition,
                currentDistanceToFinal,
                simulatedIssuedLeadCount);
            if (score <= bestScore)
                continue;

            bestScore = score;
            bestIndex = i;
        }

        if (bestIndex < 0)
        {
            chosenShop = default;
            return false;
        }

        chosenShop = candidateBuffer[bestIndex];
        return true;
    }

    bool CanLeadCandidateContinueSim(
        List<WorldShopRecord> worldShops,
        WorldShopRecord currentShop,
        WorldShopRecord candidateShop,
        Vector2 treasurePosition,
        Vector2Int simulatedFinalShopId,
        HashSet<Vector2Int> resolvedSet,
        HashSet<Vector2Int> usedLeadSet,
        int nextIssuedLeadCount,
        bool currentStepAllowsFinalTarget)
    {
        if (candidateShop.shopId == simulatedFinalShopId)
            return currentStepAllowsFinalTarget;

        HashSet<Vector2Int> simulatedResolvedSet = new HashSet<Vector2Int>(resolvedSet)
        {
            currentShop.shopId
        };
        HashSet<Vector2Int> simulatedUsedLeadSet = new HashSet<Vector2Int>(usedLeadSet)
        {
            candidateShop.shopId
        };

        return TryChooseLeadFromShopSim(
            worldShops,
            candidateShop,
            treasurePosition,
            simulatedFinalShopId,
            simulatedResolvedSet,
            simulatedUsedLeadSet,
            nextIssuedLeadCount,
            out _,
            false,
            false);
    }

    float ScoreLeadCandidate(WorldShopRecord currentShop, WorldShopRecord candidateShop, float currentDistanceToFinal)
    {
        if (!worldShopsById.TryGetValue(finalRevealShopId, out WorldShopRecord finalShop))
            return float.NegativeInfinity;

        return ScoreLeadCandidateSim(currentShop, candidateShop, finalShop.shopId, finalShop.worldPosition, currentDistanceToFinal, issuedLeadCount);
    }

    bool TryChooseReachableLeadFromPool(
        WorldShopRecord currentShop,
        float maxRadius,
        bool allowFinalTarget,
        bool requireStrongProgress,
        out WorldShopRecord chosenShop)
    {
        if (!worldShopsById.TryGetValue(finalRevealShopId, out WorldShopRecord finalShop))
        {
            chosenShop = default;
            return false;
        }

        candidateBuffer.Clear();
        List<StaticLeadCandidate> staticCandidates = GetOrBuildStaticLeadCandidates(currentShop);
        for (int i = 0; i < staticCandidates.Count; i++)
        {
            StaticLeadCandidate candidate = staticCandidates[i];
            if (candidate.distanceFromCurrent > maxRadius)
                continue;
            if (candidate.shopId == finalRevealShopId && !allowFinalTarget)
                continue;
            if (exhaustedShopIds.Contains(candidate.shopId) || usedLeadTargetShopIds.Contains(candidate.shopId))
                continue;
            if (requireStrongProgress && candidate.progressToFinal < minimumProgressTowardFinal)
                continue;
            if (!CanCandidateReachFinal(currentShop.shopId, candidate.shopId, issuedLeadCount + 1))
                continue;
            if (!worldShopsById.TryGetValue(candidate.shopId, out WorldShopRecord candidateRecord))
                continue;

            candidateBuffer.Add(candidateRecord);
        }

        if (candidateBuffer.Count == 0)
        {
            chosenShop = default;
            return false;
        }

        float currentDistanceToFinal = Vector2.Distance(currentShop.worldPosition, finalShop.worldPosition);
        candidateBuffer.Sort((a, b) =>
            Vector2.Distance(currentShop.worldPosition, a.worldPosition)
                .CompareTo(Vector2.Distance(currentShop.worldPosition, b.worldPosition)));
        if (candidateBuffer.Count > candidateNeighborCount)
            candidateBuffer.RemoveRange(candidateNeighborCount, candidateBuffer.Count - candidateNeighborCount);

        float bestScore = float.NegativeInfinity;
        int bestIndex = -1;
        for (int i = 0; i < candidateBuffer.Count; i++)
        {
            WorldShopRecord candidate = candidateBuffer[i];
            float score = ScoreLeadCandidate(currentShop, candidate, currentDistanceToFinal);
            if (score <= bestScore)
                continue;

            bestScore = score;
            bestIndex = i;
        }

        if (bestIndex < 0)
        {
            chosenShop = default;
            return false;
        }

        chosenShop = candidateBuffer[bestIndex];
        return true;
    }

    List<StaticLeadCandidate> GetOrBuildStaticLeadCandidates(WorldShopRecord currentShop)
    {
        if (staticLeadCandidateCache.TryGetValue(currentShop.shopId, out List<StaticLeadCandidate> cachedCandidates))
            return cachedCandidates;

        cachedCandidates = new List<StaticLeadCandidate>();
        if (!worldShopsById.TryGetValue(finalRevealShopId, out WorldShopRecord finalShop))
        {
            staticLeadCandidateCache[currentShop.shopId] = cachedCandidates;
            return cachedCandidates;
        }

        float currentDistanceToFinal = Vector2.Distance(currentShop.worldPosition, finalShop.worldPosition);
        for (int i = 0; i < allWorldShops.Count; i++)
        {
            WorldShopRecord candidate = allWorldShops[i];
            if (candidate.shopId == currentShop.shopId)
                continue;

            float distanceFromCurrent = Vector2.Distance(candidate.worldPosition, currentShop.worldPosition);
            if (distanceFromCurrent < minimumLeadDistance || distanceFromCurrent > fallbackSearchRadius)
                continue;

            float progressToFinal = currentDistanceToFinal - Vector2.Distance(candidate.worldPosition, finalShop.worldPosition);
            if (progressToFinal <= 0f)
                continue;

            cachedCandidates.Add(new StaticLeadCandidate(candidate.shopId, distanceFromCurrent, progressToFinal));
        }

        cachedCandidates.Sort((a, b) => a.distanceFromCurrent.CompareTo(b.distanceFromCurrent));
        staticLeadCandidateCache[currentShop.shopId] = cachedCandidates;
        return cachedCandidates;
    }

    bool CanCandidateReachFinal(Vector2Int currentShopId, Vector2Int candidateShopId, int nextIssuedLeadCount)
    {
        if (candidateShopId == finalRevealShopId)
            return nextIssuedLeadCount - 1 >= minimumLeadCountBeforeFinalShopEligible;

        if (!worldShopsById.TryGetValue(candidateShopId, out WorldShopRecord candidateShop))
            return false;

        reachabilityStack.Clear();
        reachabilityVisitedShopIds.Clear();
        reachabilityVisitedShopIds.Add(currentShopId);
        reachabilityVisitedShopIds.Add(candidateShopId);
        reachabilityStack.Add(new ReachabilityFrame
        {
            shopId = candidateShop.shopId,
            issuedLeadCount = nextIssuedLeadCount,
            nextCandidateIndex = 0
        });

        int expansionCount = 0;
        while (reachabilityStack.Count > 0)
        {
            int frameIndex = reachabilityStack.Count - 1;
            ReachabilityFrame frame = reachabilityStack[frameIndex];
            List<StaticLeadCandidate> candidates = GetOrBuildStaticLeadCandidates(worldShopsById[frame.shopId]);
            bool advanced = false;

            while (frame.nextCandidateIndex < candidates.Count)
            {
                if (++expansionCount > reachabilitySearchExpansionBudget)
                {
                    reachabilityStack.Clear();
                    reachabilityVisitedShopIds.Clear();
                    return false;
                }

                StaticLeadCandidate candidate = candidates[frame.nextCandidateIndex];
                frame.nextCandidateIndex++;
                reachabilityStack[frameIndex] = frame;

                if (candidate.shopId == finalRevealShopId)
                {
                    if (frame.issuedLeadCount >= minimumLeadCountBeforeFinalShopEligible)
                    {
                        reachabilityStack.Clear();
                        reachabilityVisitedShopIds.Clear();
                        return true;
                    }

                    continue;
                }

                if (exhaustedShopIds.Contains(candidate.shopId) || usedLeadTargetShopIds.Contains(candidate.shopId))
                    continue;
                if (reachabilityVisitedShopIds.Contains(candidate.shopId))
                    continue;
                if (!worldShopsById.ContainsKey(candidate.shopId))
                    continue;

                reachabilityVisitedShopIds.Add(candidate.shopId);
                reachabilityStack.Add(new ReachabilityFrame
                {
                    shopId = candidate.shopId,
                    issuedLeadCount = frame.issuedLeadCount + 1,
                    nextCandidateIndex = 0
                });
                advanced = true;
                break;
            }

            if (advanced)
                continue;

            reachabilityVisitedShopIds.Remove(frame.shopId);
            reachabilityStack.RemoveAt(frameIndex);
        }

        reachabilityVisitedShopIds.Clear();
        return false;
    }

    void ClearCurrentLeadMarker()
    {
        currentLeadShopId = default;
        SyncFocusedLeadShopVisibility();
        mapMarkerController?.ClearMarkersOfType(MapMarkerController.MarkerType.RedX);
    }

    bool IsShopUndiscovered(WorldShopRecord shop)
    {
        if (mapDiscoveryController == null)
            return false;

        Vector3Int cell = new Vector3Int(
            Mathf.FloorToInt(shop.worldPosition.x),
            Mathf.FloorToInt(shop.worldPosition.y),
            0);
        return mapDiscoveryController.GetChartCategoryAtCell(cell) == MapDiscoveryController.ChartCategory.Undiscovered;
    }

    float GetDistanceToFinalRevealShop(WorldShopRecord shop)
    {
        if (!worldShopsById.TryGetValue(finalRevealShopId, out WorldShopRecord finalShop))
            return float.PositiveInfinity;

        return Vector2.Distance(shop.worldPosition, finalShop.worldPosition);
    }

    float ScoreLeadCandidateSim(
        WorldShopRecord currentShop,
        WorldShopRecord candidateShop,
        Vector2Int simulatedFinalShopId,
        Vector2 finalShopWorldPosition,
        float currentDistanceToFinal,
        int simulatedIssuedLeadCount)
    {
        float distanceFromCurrent = Vector2.Distance(candidateShop.worldPosition, currentShop.worldPosition);
        float distanceToFinal = Vector2.Distance(candidateShop.worldPosition, finalShopWorldPosition);
        float progressToFinal = Mathf.Max(0f, currentDistanceToFinal - distanceToFinal);

        float progressScore = Mathf.Clamp01(progressToFinal / Mathf.Max(minimumProgressTowardFinal, 1f));
        float idealDistanceScore = 1f - Mathf.Clamp01(Mathf.Abs(distanceFromCurrent - idealLeadDistance) / Mathf.Max(idealLeadDistance, 1f));
        float undiscoveredBonus = IsShopUndiscovered(candidateShop)
            ? undiscoveredShopBonus
            : 0f;
        float randomness = Hash01(
            currentShop.shopId.x + candidateShop.shopId.x,
            currentShop.shopId.y + candidateShop.shopId.y,
            simulatedIssuedLeadCount + 91) * detourRandomnessWeight;
        float finalPenalty = candidateShop.shopId == simulatedFinalShopId ? 0.08f : 0f;

        return progressScore * 0.6f
            + idealDistanceScore * 0.24f
            + undiscoveredBonus
            + randomness
            - finalPenalty;
    }

    static HashSet<Vector2Int> EnsureHashSet(IEnumerable<Vector2Int> source)
    {
        if (source is HashSet<Vector2Int> typedHashSet)
            return typedHashSet;

        return new HashSet<Vector2Int>(source);
    }

    static bool TryGetShopById(List<WorldShopRecord> worldShops, Vector2Int shopId, out WorldShopRecord shopRecord)
    {
        for (int i = 0; i < worldShops.Count; i++)
        {
            if (worldShops[i].shopId != shopId)
                continue;

            shopRecord = worldShops[i];
            return true;
        }

        shopRecord = default;
        return false;
    }

    void SyncFocusedLeadShopVisibility()
    {
        if (islandGenerationController == null)
            return;

        if (currentLeadShopId != default)
            islandGenerationController.SetForcedVisibleShopDock(currentLeadShopId);
        else
            islandGenerationController.ClearForcedVisibleShopDock();
    }

    string BuildHelpfulBodyText(WorldShopRecord currentShop, WorldShopRecord leadShop)
    {
        string direction = GetDirectionWord(currentShop.worldPosition, leadShop.worldPosition);
        int variant = HashIndex(currentShop.shopId.x, currentShop.shopId.y, issuedLeadCount + 31, 3);
        return variant switch
        {
            0 => $"The merchant leans in. \"You should keep asking around {direction}. There's another shopkeeper who may know more than I do.\"",
            1 => $"The merchant drums his fingers on the counter. \"The rumor trail runs {direction} from here. Find another dockside trader and press them for the rest.\"",
            _ => $"The merchant lowers their voice. \"I've heard the next solid lead lies {direction}. Another merchant out that way should be able to help.\""
        };
    }

    string BuildFinalRevealBodyText(WorldShopRecord currentShop)
    {
        string direction = GetDirectionWord(currentShop.worldPosition, treasureWorldPosition);
        int variant = HashIndex(currentShop.shopId.x, currentShop.shopId.y, 811, 3);
        return variant switch
        {
            0 => $"The merchant nods slowly. \"You've come far enough. The treasure lies {direction} from here, on the very island folk whisper about when the lanterns burn low.\"",
            1 => $"The merchant finally relents. \"Aye, I know the place. The treasure is hidden {direction} from this dock. Best make for it before the sea changes its mind.\"",
            _ => $"The merchant exhales through their teeth. \"You've earned the truth. The treasure waits {direction} from here. Go claim it while the rumor still favors you.\""
        };
    }

    string BuildNonHelpfulBodyText(WorldShopRecord currentShop)
    {
        int variant = HashIndex(currentShop.shopId.x, currentShop.shopId.y, 509, 3);
        return variant switch
        {
            0 => "The merchant shrugs. \"I've heard the same stories you have, but nothing solid enough to send you sailing on.\"",
            1 => "The merchant frowns into the distance. \"Rumors drift through here every week. I've got no better lead for you today.\"",
            _ => "The merchant spreads their hands. \"If I knew more, I'd tell you. All I've got are sea tales and empty cups.\""
        };
    }

    DialogueContent ResolveDialogueContent(
        ShopDialogueLibrary.DialogueCategory category,
        int hashA,
        int hashB,
        int hashC,
        Vector2 from,
        Vector2 to,
        string fallbackBody,
        string fallbackYellow)
    {
        int deterministicIndex = HashIndex(hashA, hashB, hashC, 1024);
        if (shopDialogueLibrary != null &&
            shopDialogueLibrary.TryGetDialogue(category, deterministicIndex, out ShopDialogue dialogue) &&
            dialogue != null &&
            dialogue.HasAnyContent)
        {
            return BuildDialogueContent(dialogue, from, to, fallbackBody, fallbackYellow);
        }

        string[] fallbackWhiteLines = string.IsNullOrWhiteSpace(fallbackBody)
            ? Array.Empty<string>()
            : new[] { fallbackBody };
        return new DialogueContent(fallbackWhiteLines, fallbackYellow);
    }

    DialogueContent BuildDialogueContent(ShopDialogue dialogue, Vector2 from, Vector2 to, string fallbackBody, string fallbackYellow)
    {
        List<string> whiteLines = new List<string>();
        if (dialogue.whiteLines != null)
        {
            for (int i = 0; i < dialogue.whiteLines.Length; i++)
            {
                string line = ApplyDialogueTokens(dialogue.whiteLines[i], from, to);
                if (!string.IsNullOrWhiteSpace(line))
                    whiteLines.Add(line);
            }
        }

        if (whiteLines.Count == 0 && !string.IsNullOrWhiteSpace(fallbackBody))
            whiteLines.Add(fallbackBody);

        string yellowLine = ApplyDialogueTokens(dialogue.yellowUpdateLine, from, to);
        if (string.IsNullOrWhiteSpace(yellowLine))
            yellowLine = fallbackYellow ?? string.Empty;

        return new DialogueContent(whiteLines.ToArray(), yellowLine);
    }

    string ApplyDialogueTokens(string line, Vector2 from, Vector2 to)
    {
        if (string.IsNullOrWhiteSpace(line))
            return string.Empty;

        string direction = GetDirectionWord(from, to);
        return line
            .Replace("{direction}", direction)
            .Replace("{Direction}", char.ToUpper(direction[0]) + direction.Substring(1));
    }

    static string FlattenWhiteLines(string[] whiteLines)
    {
        if (whiteLines == null || whiteLines.Length == 0)
            return string.Empty;

        return string.Join("\n", whiteLines);
    }

    string GetDirectionWord(Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        if (delta.sqrMagnitude <= 0.001f)
            return "nearby";

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        if (angle < 0f)
            angle += 360f;

        if (angle >= 337.5f || angle < 22.5f)
            return "east";
        if (angle < 67.5f)
            return "northeast";
        if (angle < 112.5f)
            return "north";
        if (angle < 157.5f)
            return "northwest";
        if (angle < 202.5f)
            return "west";
        if (angle < 247.5f)
            return "southwest";
        if (angle < 292.5f)
            return "south";
        return "southeast";
    }

    static int HashIndex(int a, int b, int c, int count)
    {
        if (count <= 0)
            return 0;

        return Mathf.Clamp(Mathf.FloorToInt(Hash01(a, b, c) * count), 0, count - 1);
    }

    static float Hash01(int a, int b, int c)
    {
        uint hash = 2166136261u;
        hash = (hash ^ (uint)a) * 16777619u;
        hash = (hash ^ (uint)b) * 16777619u;
        hash = (hash ^ (uint)c) * 16777619u;
        hash ^= hash >> 13;
        hash *= 1274126177u;
        hash ^= hash >> 16;
        return (hash & 0x00FFFFFFu) / 16777215f;
    }

    void SyncDebugState()
    {
        debugInitialized = initialized;
        debugTreasureValid = treasureLocationValid;
        debugHuntStarted = huntStarted;
        debugFinalRevealIssued = finalRevealIssued;
        debugWorldShopCount = allWorldShops.Count;
        debugIssuedLeadCount = issuedLeadCount;
        debugTreasureWorldPosition = treasureWorldPosition;
        debugFinalRevealShopId = finalRevealShopId;
        debugCurrentLeadShopId = currentLeadShopId;
        debugExhaustedShopCount = resolvedOutcomes.Count;
        debugUsedLeadTargetCount = usedLeadTargetShopIds.Count;
    }
}
