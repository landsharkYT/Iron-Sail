using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

public class FishingInteractionController : MonoBehaviour
{
    public static FishingInteractionController ActiveInstance { get; private set; }

    [Header("References")]
    [SerializeField] UIDocument uiDocument;
    [SerializeField] FishingMinigameController fishingMinigameController;
    [SerializeField] FishingSpotSpawner fishingSpotSpawner;
    [SerializeField] ShipInventoryController inventoryController;
    [SerializeField] Transform boatTransform;
    [SerializeField] Tilemap fishSpotTilemap;
    [SerializeField] TileBase fishIndicatorTile;

    [Header("Fishing Items")]
    [SerializeField] ItemDefinition fishingRodItem;
    [SerializeField] Sprite fishingRodSprite;
    [SerializeField] ItemDefinition goldenCodItem;
    [SerializeField] Sprite goldenCodSprite;
    [SerializeField] float goldenCodFoodRestoreAmount = 12f;
    [SerializeField] ItemDefinition cannonballRewardItem;

    [Header("Fishing Rewards")]
    [SerializeField] [Range(0f, 1f)] float fishRewardWeight = 0.82f;
    [SerializeField] [Range(0f, 1f)] float salvageRewardWeight = 0.18f;
    [SerializeField] [Range(0f, 1f)] float salvageRewardTwoWeight = 0.7f;
    [SerializeField] [Range(0f, 1f)] float salvageRewardThreeWeight = 0.22f;
    [SerializeField] [Range(0f, 1f)] float salvageRewardFourWeight = 0.08f;

    [Header("Interaction")]
    [SerializeField][Min(0f)] float fishingSpotSearchRadiusWorld = 2.1f;
    [SerializeField][Min(0.1f)] float promptMessageDurationSeconds = 1.5f;

    [Header("Element Names")]
    [SerializeField] string promptRootElementName = "fishing-interaction-prompt-root";
    [SerializeField] string promptLabelElementName = "fishing-interaction-prompt-label";

    [Header("Runtime Debug (Play Mode Only)")]
#pragma warning disable CS0414
    [SerializeField] bool debugUiReady;
#pragma warning restore CS0414
    [SerializeField] bool debugNearFishingSpot;
    [SerializeField] bool debugUsingSpawnedFishingSpot;
    [SerializeField] bool debugHasFishingRod;
    [SerializeField] Vector3Int debugNearestFishingSpotCell;
    [SerializeField] float debugNearestFishingSpotDistance;
    [SerializeField] string debugPromptText;

    VisualElement promptRootElement;
    Label promptLabel;
    bool uiReady;
    bool warnedMissingUi;
    float transientPromptTimer;
    string transientPromptText = string.Empty;
    Vector3Int activeFishingSpotCell;
    bool hasActiveFishingSpotCell;
    FishingSpotController activeFishingSpot;
    ItemDefinition runtimeFishingRodItem;
    ItemDefinition runtimeGoldenCodItem;
    FishingMinigameController subscribedMinigameController;
    bool startingFishingRodEnsured;

    void OnEnable()
    {
        ActiveInstance = this;
        TryInitialize();
        ResolveRuntimeReferences();
        EnsureMinigameSubscription();
    }

    void Start()
    {
        TryInitialize();
        ResolveRuntimeReferences();
        EnsureStartingFishingRod();
        HidePrompt();
    }

    void Update()
    {
        TryInitialize();
        ResolveRuntimeReferences();
        EnsureMinigameSubscription();
        if (!startingFishingRodEnsured)
            EnsureStartingFishingRod();
        UpdateTransientPromptTimer();
        UpdateFishingPromptAndInput();
    }

    void OnDisable()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;

        if (subscribedMinigameController != null)
        {
            subscribedMinigameController.OnFishingAttemptEnded -= HandleFishingAttemptEnded;
            subscribedMinigameController = null;
        }
    }

    void TryInitialize()
    {
        if (uiReady)
            return;

        if (uiDocument == null)
            uiDocument = FindAnyObjectByType<UIDocument>();

        if (uiDocument == null)
            return;

        VisualElement root = uiDocument.rootVisualElement;
        promptRootElement = root.Q(promptRootElementName);
        promptLabel = root.Q<Label>(promptLabelElementName);
        if (promptRootElement == null || promptLabel == null)
        {
            if (!warnedMissingUi)
            {
                Debug.LogWarning("[FishingInteractionController] Missing fishing prompt UI elements.", this);
                warnedMissingUi = true;
            }
            return;
        }

        promptRootElement.style.display = DisplayStyle.None;
        uiReady = true;
        debugUiReady = true;
    }

    void ResolveRuntimeReferences()
    {
        if (fishingMinigameController == null)
            fishingMinigameController = FishingMinigameController.ActiveInstance ?? FindAnyObjectByType<FishingMinigameController>();

        if (fishingSpotSpawner == null)
            fishingSpotSpawner = FishingSpotSpawner.ActiveInstance ?? FindAnyObjectByType<FishingSpotSpawner>();

        if (inventoryController == null)
            inventoryController = ShipInventoryController.ActiveInventory ?? FindAnyObjectByType<ShipInventoryController>();

        if (boatTransform == null)
        {
            BoatController boatController = FindAnyObjectByType<BoatController>();
            if (boatController != null)
                boatTransform = boatController.transform;
        }

        if (fishSpotTilemap == null)
        {
            InfiniteWaterTileMap waterMap = InfiniteWaterTileMap.ActiveInstance ?? FindAnyObjectByType<InfiniteWaterTileMap>();
            if (waterMap != null)
                fishSpotTilemap = waterMap.WaterTilemap;
        }
    }

    void EnsureMinigameSubscription()
    {
        if (fishingMinigameController == null || subscribedMinigameController == fishingMinigameController)
            return;

        if (subscribedMinigameController != null)
            subscribedMinigameController.OnFishingAttemptEnded -= HandleFishingAttemptEnded;

        fishingMinigameController.OnFishingAttemptEnded += HandleFishingAttemptEnded;
        subscribedMinigameController = fishingMinigameController;
    }

    void EnsureStartingFishingRod()
    {
        if (startingFishingRodEnsured)
            return;

        if (inventoryController == null)
            return;

        ItemDefinition rodItem = ResolveFishingRodItem();
        if (rodItem == null)
            return;

        ShipEquipmentController equipmentController = ShipEquipmentController.ActiveEquipment;
        int equippedRodCount = equipmentController != null ? equipmentController.GetTotalQuantityByItemId(rodItem.ItemId) : 0;
        int inventoryRodCount = inventoryController.GetTotalQuantityByItemId(rodItem.ItemId);
        int totalRodCount = equippedRodCount + inventoryRodCount;

        if (totalRodCount > 1)
        {
            int extrasToRemove = totalRodCount - 1;
            if (inventoryRodCount > 0)
            {
                int removeFromInventory = Mathf.Min(extrasToRemove, inventoryRodCount);
                if (removeFromInventory > 0)
                {
                    inventoryController.TryConsumeItemByItemId(rodItem.ItemId, removeFromInventory);
                    extrasToRemove -= removeFromInventory;
                }
            }

            if (extrasToRemove > 0 && equipmentController != null)
                equipmentController.RemoveExtraItemsByItemId(rodItem.ItemId, 1, ShipEquipmentController.EquipmentSlotType.FishingRod);
        }

        equippedRodCount = equipmentController != null ? equipmentController.GetTotalQuantityByItemId(rodItem.ItemId) : 0;
        inventoryRodCount = inventoryController.GetTotalQuantityByItemId(rodItem.ItemId);
        totalRodCount = equippedRodCount + inventoryRodCount;
        if (totalRodCount > 0)
        {
            equipmentController?.TryAutoEquipFishingRod(rodItem);
            startingFishingRodEnsured = true;
            return;
        }

        inventoryController.TryAddItem(rodItem, 1, out _);
        equipmentController?.TryAutoEquipFishingRod(rodItem);
        startingFishingRodEnsured = true;
    }

    void UpdateTransientPromptTimer()
    {
        if (transientPromptTimer <= 0f)
            return;

        transientPromptTimer = Mathf.Max(0f, transientPromptTimer - Time.deltaTime);
        if (transientPromptTimer <= 0f)
            transientPromptText = string.Empty;
    }

    void UpdateFishingPromptAndInput()
    {
        if (boatTransform == null)
            ResolveRuntimeReferences();

        if (boatTransform == null)
        {
            HidePrompt();
            return;
        }

        bool blockedByOtherUi = InventoryUIController.IsInventoryOpen || WorldMapUIController.IsMapOpen || ShopController.IsShopOpen || FishingMinigameController.IsFishingOpen;
        if (blockedByOtherUi)
        {
            HidePrompt();
            return;
        }

        Vector3Int fishingSpotCell = default;
        float fishingSpotDistance = float.MaxValue;
        bool foundSpawnedSpot = TryGetNearestSpawnedFishingSpot(out FishingSpotController spawnedSpot, out float spawnedSpotDistance);
        bool foundTileSpot = !foundSpawnedSpot && TryGetNearestFishingSpot(out fishingSpotCell, out fishingSpotDistance);
        bool foundSpot = foundSpawnedSpot || foundTileSpot;
        debugNearFishingSpot = foundSpot;
        debugUsingSpawnedFishingSpot = foundSpawnedSpot;
        debugNearestFishingSpotCell = foundTileSpot ? fishingSpotCell : default;
        debugNearestFishingSpotDistance = foundSpawnedSpot ? spawnedSpotDistance : fishingSpotDistance;
        debugHasFishingRod = HasFishingRod();

        if (!foundSpot)
        {
            if (transientPromptTimer > 0f)
                ShowPrompt(transientPromptText);
            else
                HidePrompt();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.fKey.wasPressedThisFrame)
        {
            if (!HasFishingRod())
            {
                ShowTransientPrompt("Equip a fishing rod in the Rod slot.");
                return;
            }

            if (fishingMinigameController == null || !fishingMinigameController.StartFishingAttempt())
                return;

            UIAudioController.ActiveInstance?.PlayFishingStartSound();
            activeFishingSpot = foundSpawnedSpot ? spawnedSpot : null;
            if (foundTileSpot)
            {
                activeFishingSpotCell = fishingSpotCell;
                hasActiveFishingSpotCell = true;
            }
            else
            {
                activeFishingSpotCell = default;
                hasActiveFishingSpotCell = false;
            }

            HidePrompt();
            return;
        }

        if (transientPromptTimer > 0f)
            ShowPrompt(transientPromptText);
        else
            ShowPrompt("Press F to Fish");
    }

    bool TryGetNearestFishingSpot(out Vector3Int nearestCell, out float nearestDistance)
    {
        nearestCell = default;
        nearestDistance = float.MaxValue;

        if (boatTransform == null || fishSpotTilemap == null || fishIndicatorTile == null)
            return false;

        Vector3 boatPosition = boatTransform.position;
        Vector3Int boatCell = fishSpotTilemap.WorldToCell(boatPosition);
        int radiusCells = Mathf.CeilToInt(fishingSpotSearchRadiusWorld / Mathf.Max(fishSpotTilemap.cellSize.x, 0.01f));
        bool found = false;

        for (int y = boatCell.y - radiusCells; y <= boatCell.y + radiusCells; y++)
        {
            for (int x = boatCell.x - radiusCells; x <= boatCell.x + radiusCells; x++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                if (fishSpotTilemap.GetTile(cell) != fishIndicatorTile)
                    continue;

                Vector3 cellCenter = fishSpotTilemap.GetCellCenterWorld(cell);
                float distance = Vector2.Distance(new Vector2(boatPosition.x, boatPosition.y), new Vector2(cellCenter.x, cellCenter.y));
                if (distance > fishingSpotSearchRadiusWorld)
                    continue;

                if (!found || distance < nearestDistance)
                {
                    found = true;
                    nearestCell = cell;
                    nearestDistance = distance;
                }
            }
        }

        return found;
    }

    bool TryGetNearestSpawnedFishingSpot(out FishingSpotController nearestSpot, out float nearestDistance)
    {
        nearestSpot = null;
        nearestDistance = float.MaxValue;

        if (boatTransform == null || fishingSpotSpawner == null)
            return false;

        return fishingSpotSpawner.TryGetNearestActiveSpot(boatTransform.position, fishingSpotSearchRadiusWorld, out nearestSpot, out nearestDistance);
    }

    bool HasFishingRod()
    {
        return ShipEquipmentController.ActiveEquipment != null &&
               ShipEquipmentController.ActiveEquipment.HasFishingRodEquipped();
    }

    ItemDefinition ResolveFishingRodItem()
    {
        if (fishingRodItem != null)
            return fishingRodItem;

        if (runtimeFishingRodItem != null)
            return runtimeFishingRodItem;

        runtimeFishingRodItem = ScriptableObject.CreateInstance<ItemDefinition>();
        runtimeFishingRodItem.name = "RuntimeFishingRod";
        runtimeFishingRodItem.InitializeRuntime(
            "fishing_rod",
            "Fishing Rod",
            "A sturdy fishing rod for catching fish at marked spots.",
            ItemCategory.Misc,
            2f,
            25,
            false,
            1,
            fishingRodSprite,
            0f,
            UtilityItemType.FishingRod);
        return runtimeFishingRodItem;
    }

    ItemDefinition ResolveGoldenCodItem()
    {
        if (goldenCodItem != null)
            return goldenCodItem;

        if (runtimeGoldenCodItem != null)
            return runtimeGoldenCodItem;

        runtimeGoldenCodItem = ScriptableObject.CreateInstance<ItemDefinition>();
        runtimeGoldenCodItem.name = "RuntimeGoldenCod";
        runtimeGoldenCodItem.InitializeRuntime(
            "golden_cod",
            "Golden Cod",
            "A fresh golden cod. Restores a medium amount of hunger when eaten.",
            ItemCategory.Food,
            1f,
            18,
            true,
            10,
            goldenCodSprite,
            goldenCodFoodRestoreAmount);
        return runtimeGoldenCodItem;
    }

    void HandleFishingAttemptEnded(FishingMinigameController.FishingEndReason reason)
    {
        if (reason != FishingMinigameController.FishingEndReason.Success)
        {
            activeFishingSpot = null;
            hasActiveFishingSpotCell = false;
            return;
        }

        if (inventoryController == null)
        {
            activeFishingSpot = null;
            hasActiveFishingSpotCell = false;
            return;
        }

        bool awardedItem = TryAwardFishingSuccessReward();
        if (!awardedItem)
        {
            activeFishingSpot = null;
            hasActiveFishingSpotCell = false;
            return;
        }

        if (activeFishingSpot != null)
            ConsumeSpawnedFishingSpot(activeFishingSpot);
        else if (hasActiveFishingSpotCell)
            ReplaceFishingSpotWithWater(activeFishingSpotCell);

        activeFishingSpot = null;
        hasActiveFishingSpotCell = false;
    }

    bool TryAwardFishingSuccessReward()
    {
        float fishWeight = Mathf.Max(0f, fishRewardWeight);
        float salvageWeight = Mathf.Max(0f, salvageRewardWeight);
        float totalWeight = fishWeight + salvageWeight;
        if (totalWeight <= 0f)
            return TryAwardFish();

        float roll = Random.value * totalWeight;
        if (roll < salvageWeight)
            return TryAwardSalvage();

        return TryAwardFish();
    }

    bool TryAwardFish()
    {
        ItemDefinition fishItem = ResolveGoldenCodItem();
        if (fishItem == null)
            return false;

        bool added = inventoryController.TryAddItem(fishItem, 1, out int remainder) && remainder == 0;
        if (!added)
        {
            ShowTransientPrompt("No room to store the fish.");
            return false;
        }

        ShowTransientPrompt("Caught: Golden Cod");
        return true;
    }

    bool TryAwardSalvage()
    {
        ItemDefinition cannonballItem = ResolveCannonballRewardItem();
        if (cannonballItem == null)
            return TryAwardFish();

        int rolledAmount = RewardUtility.RollWeightedRewardAmount(
            salvageRewardTwoWeight,
            salvageRewardThreeWeight,
            salvageRewardFourWeight);
        if (rolledAmount <= 0)
            return TryAwardFish();

        inventoryController.TryAddItem(cannonballItem, rolledAmount, out int remainder);
        int addedAmount = Mathf.Max(0, rolledAmount - remainder);
        if (addedAmount <= 0)
        {
            ShowTransientPrompt("No room to store the salvage.");
            return false;
        }

        string message = $"You reel up drifting salvage.\nRecovered {addedAmount} cannonballs.";
        if (remainder > 0)
            message += $"\n{remainder} were lost to lack of space.";

        ShowTransientPrompt(message);
        return true;
    }

    ItemDefinition ResolveCannonballRewardItem()
    {
        if (cannonballRewardItem != null)
            return cannonballRewardItem;

        cannonballRewardItem = RewardUtility.ResolveCannonballItem();
        return cannonballRewardItem;
    }

    void ConsumeSpawnedFishingSpot(FishingSpotController fishingSpot)
    {
        if (fishingSpot == null)
            return;

        FishingSpotSpawner spotSpawner = fishingSpotSpawner != null ? fishingSpotSpawner : FishingSpotSpawner.ActiveInstance;
        if (spotSpawner == null)
            return;

        spotSpawner.ConsumeSpot(fishingSpot);
    }

    void ReplaceFishingSpotWithWater(Vector3Int cell)
    {
        InfiniteWaterTileMap waterMap = InfiniteWaterTileMap.ActiveInstance ?? FindAnyObjectByType<InfiniteWaterTileMap>();
        if (waterMap != null && waterMap.WaterTilemap == fishSpotTilemap)
        {
            waterMap.FillCellWithWater(cell);
            return;
        }

        fishSpotTilemap.SetTile(cell, null);
    }

    void ShowTransientPrompt(string text)
    {
        transientPromptText = text;
        transientPromptTimer = promptMessageDurationSeconds;
        ShowPrompt(text);
    }

    public void ShowExternalPrompt(string text)
    {
        ShowTransientPrompt(text);
    }

    void ShowPrompt(string text)
    {
        if (!uiReady || promptRootElement == null || promptLabel == null)
            return;

        promptRootElement.style.display = DisplayStyle.Flex;
        promptLabel.text = text;
        debugPromptText = text;
    }

    void HidePrompt()
    {
        if (!uiReady || promptRootElement == null)
            return;

        promptRootElement.style.display = DisplayStyle.None;
        debugPromptText = string.Empty;
    }
}
