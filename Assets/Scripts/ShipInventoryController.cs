using System;
using System.Collections.Generic;
using UnityEngine;

// Owns the active ship cargo hold, including slot rules, weight rules, and gold.
//
// This controller is the source of truth for cargo data. The UI layer only reads
// from this controller and asks it to move / merge / swap stacks.
public class ShipInventoryController : MonoBehaviour
{
    public enum InventorySortType
    {
        Default,
        Name,
        Category,
        Weight,
        Value,
        Quantity
    }

    public readonly struct InventorySlotSnapshot
    {
        public InventorySlotSnapshot(ItemDefinition item, int quantity)
        {
            Item = item;
            Quantity = quantity;
        }

        public ItemDefinition Item { get; }
        public int Quantity { get; }
        public bool IsEmpty => Item == null || Quantity <= 0;
    }

    [Serializable]
    class InventorySlot
    {
        public ItemDefinition item;
        public int quantity;

        public bool IsEmpty => item == null || quantity <= 0;

        public void Clear()
        {
            item = null;
            quantity = 0;
        }
    }

    [Header("Capacity")]
    [SerializeField] int maxSlots = 15;
    [SerializeField] float maxCarryWeight = 100f;

    [Header("Economy")]
    [SerializeField] int startingGold = 500;

    [Header("Startup Ammo")]
    [SerializeField] ItemDefinition startingAmmoItem;
    [SerializeField] int startingAmmoQuantity = 20;

    [Header("Startup Gear")]
    [SerializeField] ItemDefinition startingWeaponItem;
    [SerializeField] int startingWeaponQuantity = 1;
    [SerializeField] ItemDefinition startingSecondaryAmmoItem;
    [SerializeField] int startingSecondaryAmmoQuantity = 20;

    [Header("Registration")]
    [SerializeField] bool registerAsActiveInventory = true;

    public static ShipInventoryController ActiveInventory { get; private set; }
    public static event Action<ShipInventoryController> OnActiveInventoryRegistered;

    public event Action OnInventoryChanged;

    public int MaxSlots => maxSlots;
    public float MaxCarryWeight => maxCarryWeight;
    public int Gold => gold;
    public int UsedSlotCount => GetUsedSlotCount();
    public float CurrentWeight => GetCurrentWeight();
    public IReadOnlyList<InventorySlotSnapshot> Slots => slotSnapshots;

    readonly List<InventorySlot> slots = new();
    readonly List<InventorySlotSnapshot> slotSnapshots = new();

    int gold;
    bool startupSanitizedInventory;

    void Awake()
    {
        maxSlots = Mathf.Max(1, maxSlots);
        maxCarryWeight = Mathf.Max(0f, maxCarryWeight);

        EnsureSlotCount();
        gold = Mathf.Max(0, startingGold);
        startupSanitizedInventory = SanitizePrimitiveSlotStates();
        RefreshSlotSnapshots();

        if (startingAmmoItem != null && startingAmmoQuantity > 0)
            TryAddItem(startingAmmoItem, Mathf.Max(0, startingAmmoQuantity), out _);
        if (startingWeaponItem != null && startingWeaponQuantity > 0)
            TryAddItem(startingWeaponItem, Mathf.Max(0, startingWeaponQuantity), out _);
        if (startingSecondaryAmmoItem != null && startingSecondaryAmmoQuantity > 0)
            TryAddItem(startingSecondaryAmmoItem, Mathf.Max(0, startingSecondaryAmmoQuantity), out _);
    }

    void OnEnable()
    {
        if (!registerAsActiveInventory)
            return;

        ActiveInventory = this;
        OnActiveInventoryRegistered?.Invoke(this);
        if (startupSanitizedInventory)
        {
            NotifyInventoryChanged();
            startupSanitizedInventory = false;
        }
    }

    void OnDisable()
    {
        if (!registerAsActiveInventory)
            return;

        if (ActiveInventory != this)
            return;

        ActiveInventory = null;
        OnActiveInventoryRegistered?.Invoke(null);
    }

    void OnValidate()
    {
        maxSlots = Mathf.Max(1, maxSlots);
        maxCarryWeight = Mathf.Max(0f, maxCarryWeight);
        startingGold = Mathf.Max(0, startingGold);
        startingAmmoQuantity = Mathf.Max(0, startingAmmoQuantity);
        startingWeaponQuantity = Mathf.Max(0, startingWeaponQuantity);
        startingSecondaryAmmoQuantity = Mathf.Max(0, startingSecondaryAmmoQuantity);
    }

    public void SetGold(int amount)
    {
        int sanitizedAmount = Mathf.Max(0, amount);
        if (gold == sanitizedAmount)
            return;

        gold = sanitizedAmount;
        NotifyInventoryChanged();
    }

    // Save-restore seam: empty every slot so a saved inventory can be re-added.
    public void ClearAllItems()
    {
        for (int i = 0; i < slots.Count; i++)
            slots[i].Clear();

        RefreshSlotSnapshots();
        NotifyInventoryChanged();
    }

    public void AddGold(int amount)
    {
        if (amount <= 0)
            return;

        gold += amount;
        NotifyInventoryChanged();
    }

    public bool SpendGold(int amount)
    {
        if (amount <= 0)
            return true;

        if (gold < amount)
            return false;

        gold -= amount;
        NotifyInventoryChanged();
        return true;
    }

    public bool TryAddItem(ItemDefinition item, int quantity, out int remainder)
    {
        remainder = 0;

        if (item == null || quantity <= 0)
            return false;

        if (!ValidateExistingInventoryIdentity("add item"))
            return false;

        int remainingQuantity = quantity;

        if (item.Stackable)
        {
            for (int i = 0; i < slots.Count && remainingQuantity > 0; i++)
            {
                InventorySlot slot = slots[i];
                if (slot.IsEmpty || slot.quantity >= item.MaxStackSize)
                    continue;

                if (!CanItemsShareStack(slot.item, item, out bool conflict))
                {
                    if (conflict)
                    {
                        Debug.LogError($"ShipInventoryController refused add due to conflicting item data for itemId '{GetItemIdOrFallback(item)}'.", this);
                        remainder = quantity;
                        return false;
                    }

                    continue;
                }

                int stackSpace = item.MaxStackSize - slot.quantity;
                if (stackSpace <= 0)
                    continue;

                int weightLimitedAmount = GetMaxAddableUnitsByWeight(item, remainingQuantity);
                if (weightLimitedAmount <= 0)
                    break;

                int amountToAdd = Mathf.Min(remainingQuantity, Mathf.Min(stackSpace, weightLimitedAmount));
                slot.quantity += amountToAdd;
                remainingQuantity -= amountToAdd;
            }
        }

        for (int i = 0; i < slots.Count && remainingQuantity > 0; i++)
        {
            InventorySlot slot = slots[i];
            if (!slot.IsEmpty)
                continue;

            int weightLimitedAmount = GetMaxAddableUnitsByWeight(item, remainingQuantity);
            if (weightLimitedAmount <= 0)
                break;

            int amountToAdd = item.Stackable
                ? Mathf.Min(weightLimitedAmount, Mathf.Min(remainingQuantity, item.MaxStackSize))
                : Mathf.Min(weightLimitedAmount, 1);

            slot.item = item;
            slot.quantity = amountToAdd;
            remainingQuantity -= amountToAdd;
        }

        remainder = remainingQuantity;
        if (remainingQuantity != quantity)
            NotifyInventoryChanged();

        return remainder == 0;
    }

    public bool CanFullyAddItem(ItemDefinition item, int quantity)
    {
        if (item == null || quantity <= 0)
            return false;

        if (!ValidateExistingInventoryIdentity("preview add item"))
            return false;

        int remainingQuantity = quantity;
        float simulatedCurrentWeight = CurrentWeight;

        if (item.Stackable)
        {
            for (int i = 0; i < slots.Count && remainingQuantity > 0; i++)
            {
                InventorySlot slot = slots[i];
                if (slot.IsEmpty || slot.quantity >= item.MaxStackSize)
                    continue;

                if (!CanItemsShareStack(slot.item, item, out bool conflict))
                {
                    if (conflict)
                        return false;

                    continue;
                }

                int stackSpace = item.MaxStackSize - slot.quantity;
                if (stackSpace <= 0)
                    continue;

                int weightLimitedAmount = GetMaxAddableUnitsByWeightPreview(item, remainingQuantity, simulatedCurrentWeight);
                if (weightLimitedAmount <= 0)
                    break;

                int amountToAdd = Mathf.Min(remainingQuantity, Mathf.Min(stackSpace, weightLimitedAmount));
                simulatedCurrentWeight += item.Weight * amountToAdd;
                remainingQuantity -= amountToAdd;
            }
        }

        for (int i = 0; i < slots.Count && remainingQuantity > 0; i++)
        {
            InventorySlot slot = slots[i];
            if (!slot.IsEmpty)
                continue;

            int weightLimitedAmount = GetMaxAddableUnitsByWeightPreview(item, remainingQuantity, simulatedCurrentWeight);
            if (weightLimitedAmount <= 0)
                break;

            int amountToAdd = item.Stackable
                ? Mathf.Min(weightLimitedAmount, Mathf.Min(remainingQuantity, item.MaxStackSize))
                : Mathf.Min(weightLimitedAmount, 1);

            simulatedCurrentWeight += item.Weight * amountToAdd;
            remainingQuantity -= amountToAdd;
        }

        return remainingQuantity <= 0;
    }

    public bool MoveOrMergeSlot(int fromIndex, int toIndex)
    {
        if (!IsValidSlotIndex(fromIndex) || !IsValidSlotIndex(toIndex) || fromIndex == toIndex)
            return false;

        if (!ValidateExistingInventoryIdentity("move or merge"))
            return false;

        InventorySlot fromSlot = slots[fromIndex];
        InventorySlot toSlot = slots[toIndex];

        if (fromSlot.IsEmpty)
            return false;

        if (toSlot.IsEmpty)
        {
            SwapSlotContents(fromSlot, toSlot);
            NotifyInventoryChanged();
            return true;
        }

        bool mergeConflict = false;
        bool canShareMergeStack = fromSlot.item != null &&
                                  CanItemsShareStack(toSlot.item, fromSlot.item, out mergeConflict);
        if (canShareMergeStack && fromSlot.item.Stackable)
        {
            int stackSpace = fromSlot.item.MaxStackSize - toSlot.quantity;
            if (stackSpace > 0)
            {
                int amountToMove = Mathf.Min(stackSpace, fromSlot.quantity);
                toSlot.quantity += amountToMove;
                fromSlot.quantity -= amountToMove;
                if (fromSlot.quantity <= 0)
                    fromSlot.Clear();

                NotifyInventoryChanged();
                return true;
            }
        }
        else if (mergeConflict)
        {
            Debug.LogError($"ShipInventoryController refused merge due to conflicting item data for itemId '{GetItemIdOrFallback(fromSlot.item)}'.", this);
            return false;
        }

        SwapSlotContents(fromSlot, toSlot);
        NotifyInventoryChanged();
        return true;
    }

    public bool TryExtractSplitStack(int fromIndex, int quantity, out ItemDefinition item, out int extractedQuantity)
    {
        item = null;
        extractedQuantity = 0;

        if (!IsValidSlotIndex(fromIndex) || quantity <= 0)
            return false;

        InventorySlot fromSlot = slots[fromIndex];
        if (fromSlot.IsEmpty || fromSlot.item == null || fromSlot.quantity <= 1)
            return false;

        int amountToExtract = Mathf.Clamp(quantity, 1, fromSlot.quantity - 1);
        item = fromSlot.item;
        extractedQuantity = amountToExtract;
        fromSlot.quantity -= amountToExtract;
        NotifyInventoryChanged();
        return true;
    }

    public int TryInsertStackAt(int toIndex, ItemDefinition item, int quantity)
    {
        if (!IsValidSlotIndex(toIndex) || item == null || quantity <= 0)
            return quantity;

        if (!ValidateExistingInventoryIdentity("insert stack"))
            return quantity;

        InventorySlot toSlot = slots[toIndex];
        if (toSlot.IsEmpty)
        {
            int amountToPlace = item.Stackable
                ? Mathf.Min(quantity, item.MaxStackSize)
                : 1;

            toSlot.item = item;
            toSlot.quantity = amountToPlace;
            NotifyInventoryChanged();
            return quantity - amountToPlace;
        }

        bool insertConflict = false;
        bool canShareInsertStack = item.Stackable &&
                                   toSlot.quantity < item.MaxStackSize &&
                                   CanItemsShareStack(toSlot.item, item, out insertConflict);
        if (canShareInsertStack)
        {
            int stackSpace = item.MaxStackSize - toSlot.quantity;
            int amountToPlace = Mathf.Min(quantity, stackSpace);
            toSlot.quantity += amountToPlace;
            NotifyInventoryChanged();
            return quantity - amountToPlace;
        }
        if (insertConflict)
            Debug.LogError($"ShipInventoryController refused insert due to conflicting item data for itemId '{GetItemIdOrFallback(item)}'.", this);

        return quantity;
    }

    public bool TryPlaceLooseItemAt(int toIndex, ItemDefinition item, int quantity)
    {
        if (!IsValidSlotIndex(toIndex) || item == null || quantity <= 0)
            return false;

        if (!ValidateExistingInventoryIdentity("place loose item"))
            return false;

        int remainder = TryInsertStackAt(toIndex, item, quantity);
        if (remainder <= 0)
            return true;

        InventorySlot targetSlot = slots[toIndex];
        if (targetSlot.IsEmpty)
            return false;

        int emptySlotIndex = FindFirstEmptySlotIndexExcluding(toIndex);
        if (emptySlotIndex < 0)
            return false;

        InventorySlot emptySlot = slots[emptySlotIndex];
        emptySlot.item = targetSlot.item;
        emptySlot.quantity = targetSlot.quantity;

        targetSlot.item = item;
        targetSlot.quantity = quantity;

        NotifyInventoryChanged();
        return true;
    }

    public bool TryGetSlotSnapshot(int slotIndex, out InventorySlotSnapshot slot)
    {
        slot = default;
        if (!IsValidSlotIndex(slotIndex))
            return false;

        InventorySlot internalSlot = slots[slotIndex];
        slot = new InventorySlotSnapshot(internalSlot.item, internalSlot.quantity);
        return true;
    }

    public int GetTotalQuantityByItemId(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return 0;

        int totalQuantity = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (slot.IsEmpty || slot.item == null)
                continue;

            if (!string.Equals(GetItemIdOrFallback(slot.item), itemId, StringComparison.Ordinal))
                continue;

            totalQuantity += slot.quantity;
        }

        return totalQuantity;
    }

    public bool TryConsumeItemByItemId(string itemId, int amount)
    {
        if (string.IsNullOrWhiteSpace(itemId) || amount <= 0)
            return false;

        if (!ValidateExistingInventoryIdentity("consume item"))
            return false;

        if (GetTotalQuantityByItemId(itemId) < amount)
            return false;

        int remainingToConsume = amount;
        for (int i = 0; i < slots.Count && remainingToConsume > 0; i++)
        {
            InventorySlot slot = slots[i];
            if (slot.IsEmpty || slot.item == null)
                continue;

            if (!string.Equals(GetItemIdOrFallback(slot.item), itemId, StringComparison.Ordinal))
                continue;

            int amountToConsume = Mathf.Min(slot.quantity, remainingToConsume);
            slot.quantity -= amountToConsume;
            remainingToConsume -= amountToConsume;

            if (slot.quantity <= 0)
                slot.Clear();
        }

        NotifyInventoryChanged();
        return true;
    }

    public bool TryRemoveQuantityAt(int slotIndex, int quantity, out ItemDefinition item, out int removedQuantity)
    {
        item = null;
        removedQuantity = 0;

        if (!IsValidSlotIndex(slotIndex) || quantity <= 0)
            return false;

        InventorySlot slot = slots[slotIndex];
        if (slot.IsEmpty || slot.item == null)
            return false;

        int amountToRemove = Mathf.Min(quantity, slot.quantity);
        item = slot.item;
        removedQuantity = amountToRemove;
        slot.quantity -= amountToRemove;

        if (slot.quantity <= 0)
            slot.Clear();

        NotifyInventoryChanged();
        return true;
    }

    public bool TrySellQuantityAt(int slotIndex, int quantity, out ItemDefinition item, out int soldQuantity, out int goldEarned)
    {
        item = null;
        soldQuantity = 0;
        goldEarned = 0;

        if (!IsValidSlotIndex(slotIndex) || quantity <= 0)
            return false;

        InventorySlot slot = slots[slotIndex];
        if (slot.IsEmpty || slot.item == null || slot.item.Value <= 0)
            return false;

        int amountToSell = Mathf.Min(quantity, slot.quantity);
        item = slot.item;
        soldQuantity = amountToSell;
        goldEarned = amountToSell * item.Value;

        slot.quantity -= amountToSell;
        if (slot.quantity <= 0)
            slot.Clear();

        gold += goldEarned;
        NotifyInventoryChanged();
        return true;
    }

    public void SortInventory(InventorySortType sortType, bool ascending)
    {
        if (!ValidateExistingInventoryIdentity("sort"))
            return;

        if (!TryBuildMergedOccupiedSlotList(out List<InventorySlot> occupiedSlots))
            return;

        if (occupiedSlots.Count > slots.Count)
        {
            Debug.LogError("ShipInventoryController refused sort because compacted contents exceed slot capacity.", this);
            return;
        }

        occupiedSlots.Sort((a, b) => CompareSlots(a, b, sortType, ascending));

        for (int i = 0; i < slots.Count; i++)
            slots[i].Clear();

        for (int i = 0; i < occupiedSlots.Count && i < slots.Count; i++)
        {
            slots[i].item = occupiedSlots[i].item;
            slots[i].quantity = occupiedSlots[i].quantity;
        }

        NotifyInventoryChanged();
    }

    float GetCurrentWeight()
    {
        float totalWeight = 0f;
        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (slot.IsEmpty || slot.item == null)
                continue;

            totalWeight += slot.item.Weight * slot.quantity;
        }

        return totalWeight;
    }

    int GetUsedSlotCount()
    {
        int used = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (!slots[i].IsEmpty)
                used++;
        }

        return used;
    }

    int GetMaxAddableUnitsByWeight(ItemDefinition item, int desiredQuantity)
    {
        if (item == null || desiredQuantity <= 0)
            return 0;

        if (item.Weight <= 0f)
            return desiredQuantity;

        float remainingWeightCapacity = maxCarryWeight - CurrentWeight;
        if (remainingWeightCapacity <= 0f)
            return 0;

        return Mathf.Clamp(Mathf.FloorToInt(remainingWeightCapacity / item.Weight), 0, desiredQuantity);
    }

    int GetMaxAddableUnitsByWeightPreview(ItemDefinition item, int desiredQuantity, float simulatedCurrentWeight)
    {
        if (item == null || desiredQuantity <= 0)
            return 0;

        if (item.Weight <= 0f)
            return desiredQuantity;

        float remainingWeightCapacity = maxCarryWeight - simulatedCurrentWeight;
        if (remainingWeightCapacity <= 0f)
            return 0;

        return Mathf.Clamp(Mathf.FloorToInt(remainingWeightCapacity / item.Weight), 0, desiredQuantity);
    }

    List<InventorySlot> BuildMergedOccupiedSlotList()
    {
        TryBuildMergedOccupiedSlotList(out List<InventorySlot> mergedSlots);
        return mergedSlots;
    }

    bool TryBuildMergedOccupiedSlotList(out List<InventorySlot> mergedSlots)
    {
        mergedSlots = new List<InventorySlot>();
        var mergedByItemId = new Dictionary<string, int>();
        var canonicalItemsByItemId = new Dictionary<string, ItemDefinition>();
        var encounterOrder = new List<string>();
        var unstackableSlots = new List<InventorySlot>();

        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (slot.IsEmpty || slot.item == null || slot.quantity <= 0)
                continue;

            if (slot.item.Stackable)
            {
                string itemId = GetItemIdOrFallback(slot.item);
                if (canonicalItemsByItemId.TryGetValue(itemId, out ItemDefinition canonicalItem))
                {
                    if (!AreItemDefinitionsEquivalent(canonicalItem, slot.item))
                    {
                        Debug.LogError($"ShipInventoryController refused merge/sort because itemId '{itemId}' maps to conflicting item definitions.", this);
                        return false;
                    }
                }
                else
                {
                    canonicalItemsByItemId[itemId] = slot.item;
                    encounterOrder.Add(itemId);
                }

                mergedByItemId[itemId] = mergedByItemId.TryGetValue(itemId, out int existingQuantity)
                    ? existingQuantity + slot.quantity
                    : slot.quantity;
            }
            else
            {
                for (int quantity = 0; quantity < slot.quantity; quantity++)
                {
                    unstackableSlots.Add(new InventorySlot
                    {
                        item = slot.item,
                        quantity = 1
                    });
                }
            }
        }

        for (int i = 0; i < encounterOrder.Count; i++)
        {
            string itemId = encounterOrder[i];
            ItemDefinition item = canonicalItemsByItemId[itemId];
            int remainingQuantity = mergedByItemId[itemId];
            while (remainingQuantity > 0)
            {
                int chunkQuantity = Mathf.Min(remainingQuantity, item.MaxStackSize);
                mergedSlots.Add(new InventorySlot
                {
                    item = item,
                    quantity = chunkQuantity
                });
                remainingQuantity -= chunkQuantity;
            }
        }

        mergedSlots.AddRange(unstackableSlots);
        return true;
    }

    int CompareSlots(InventorySlot a, InventorySlot b, InventorySortType sortType, bool ascending)
    {
        int primaryComparison = sortType switch
        {
            InventorySortType.Default => CompareItemId(a, b, true),
            InventorySortType.Name => CompareName(a, b, ascending),
            InventorySortType.Category => CompareCategory(a, b, ascending),
            InventorySortType.Weight => CompareWeight(a, b, ascending),
            InventorySortType.Value => CompareValue(a, b, ascending),
            InventorySortType.Quantity => CompareQuantity(a, b, ascending),
            _ => 0
        };

        if (primaryComparison != 0)
            return primaryComparison;

        int categoryComparison = CompareCategory(a, b, true);
        if (categoryComparison != 0)
            return categoryComparison;

        int nameComparison = CompareName(a, b, true);
        if (nameComparison != 0)
            return nameComparison;

        int itemIdComparison = CompareItemId(a, b, true);
        if (itemIdComparison != 0)
            return itemIdComparison;

        int quantityComparison = CompareQuantity(a, b, false);
        if (quantityComparison != 0)
            return quantityComparison;

        return 0;
    }

    static int ApplyDirection(int comparison, bool ascending)
    {
        return ascending ? comparison : -comparison;
    }

    static string GetSafeName(InventorySlot slot)
    {
        return slot?.item != null ? slot.item.DisplayName ?? string.Empty : string.Empty;
    }

    static string GetSafeItemId(InventorySlot slot)
    {
        return slot?.item != null ? slot.item.ItemId ?? string.Empty : string.Empty;
    }

    static ItemCategory GetSafeCategory(InventorySlot slot)
    {
        return slot?.item != null ? slot.item.Category : ItemCategory.Misc;
    }

    static float GetSafeWeight(InventorySlot slot)
    {
        return slot?.item != null ? slot.item.Weight : 0f;
    }

    static int GetSafeValue(InventorySlot slot)
    {
        return slot?.item != null ? slot.item.Value : 0;
    }

    static int GetSafeQuantity(InventorySlot slot)
    {
        return slot != null ? slot.quantity : 0;
    }

    static int CompareName(InventorySlot a, InventorySlot b, bool ascending)
    {
        return ApplyDirection(string.Compare(GetSafeName(a), GetSafeName(b), StringComparison.OrdinalIgnoreCase), ascending);
    }

    static int CompareCategory(InventorySlot a, InventorySlot b, bool ascending)
    {
        return ApplyDirection(string.Compare(GetSafeCategory(a).ToString(), GetSafeCategory(b).ToString(), StringComparison.OrdinalIgnoreCase), ascending);
    }

    static int CompareItemId(InventorySlot a, InventorySlot b, bool ascending)
    {
        return ApplyDirection(string.Compare(GetSafeItemId(a), GetSafeItemId(b), StringComparison.OrdinalIgnoreCase), ascending);
    }

    static int CompareWeight(InventorySlot a, InventorySlot b, bool ascending)
    {
        return ApplyDirection(GetSafeWeight(a).CompareTo(GetSafeWeight(b)), ascending);
    }

    static int CompareValue(InventorySlot a, InventorySlot b, bool ascending)
    {
        return ApplyDirection(GetSafeValue(a).CompareTo(GetSafeValue(b)), ascending);
    }

    static int CompareQuantity(InventorySlot a, InventorySlot b, bool ascending)
    {
        return ApplyDirection(GetSafeQuantity(a).CompareTo(GetSafeQuantity(b)), ascending);
    }

    bool IsValidSlotIndex(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < slots.Count;
    }

    int FindFirstEmptySlotIndexExcluding(int excludedIndex)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (i == excludedIndex)
                continue;

            if (slots[i].IsEmpty)
                return i;
        }

        return -1;
    }

    bool ValidateExistingInventoryIdentity(string context)
    {
        var canonicalItemsByItemId = new Dictionary<string, ItemDefinition>();
        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (slot.IsEmpty || slot.item == null)
                continue;

            string itemId = GetItemIdOrFallback(slot.item);
            if (canonicalItemsByItemId.TryGetValue(itemId, out ItemDefinition canonicalItem))
            {
                if (!AreItemDefinitionsEquivalent(canonicalItem, slot.item))
                {
                    Debug.LogError($"ShipInventoryController refused to {context} because itemId '{itemId}' maps to conflicting item definitions.", this);
                    return false;
                }
            }
            else
            {
                canonicalItemsByItemId[itemId] = slot.item;
            }
        }

        return true;
    }

    bool CanItemsShareStack(ItemDefinition a, ItemDefinition b, out bool conflict)
    {
        conflict = false;
        if (a == null || b == null)
            return false;

        string itemIdA = GetItemIdOrFallback(a);
        string itemIdB = GetItemIdOrFallback(b);
        if (!string.Equals(itemIdA, itemIdB, StringComparison.Ordinal))
            return false;

        if (!AreItemDefinitionsEquivalent(a, b))
        {
            conflict = true;
            return false;
        }

        return true;
    }

    bool AreItemDefinitionsEquivalent(ItemDefinition a, ItemDefinition b)
    {
        if (a == null || b == null)
            return false;

        return string.Equals(GetItemIdOrFallback(a), GetItemIdOrFallback(b), StringComparison.Ordinal) &&
               string.Equals(a.DisplayName ?? string.Empty, b.DisplayName ?? string.Empty, StringComparison.Ordinal) &&
               string.Equals(a.Description ?? string.Empty, b.Description ?? string.Empty, StringComparison.Ordinal) &&
               a.Category == b.Category &&
               a.AmmoDefinition == b.AmmoDefinition &&
               a.WeaponDefinition == b.WeaponDefinition &&
               Mathf.Approximately(a.FoodRestoreAmount, b.FoodRestoreAmount) &&
               Mathf.Approximately(a.Weight, b.Weight) &&
               a.Value == b.Value &&
               a.Stackable == b.Stackable &&
               a.MaxStackSize == b.MaxStackSize;
    }

    static string GetItemIdOrFallback(ItemDefinition item)
    {
        return item != null ? item.ItemId ?? string.Empty : string.Empty;
    }

    void SwapSlotContents(InventorySlot a, InventorySlot b)
    {
        ItemDefinition tempItem = a.item;
        int tempQuantity = a.quantity;

        a.item = b.item;
        a.quantity = b.quantity;

        b.item = tempItem;
        b.quantity = tempQuantity;
    }

    void EnsureSlotCount()
    {
        while (slots.Count < maxSlots)
            slots.Add(new InventorySlot());

        while (slots.Count > maxSlots)
            slots.RemoveAt(slots.Count - 1);
    }

    bool SanitizePrimitiveSlotStates()
    {
        bool changed = false;
        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            bool invalidState = (slot.item == null && slot.quantity != 0) || (slot.item != null && slot.quantity <= 0);
            if (!invalidState)
                continue;

            Debug.LogWarning($"ShipInventoryController sanitized invalid primitive slot state at index {i}.", this);
            slot.Clear();
            changed = true;
        }

        return changed;
    }

    void NotifyInventoryChanged()
    {
        RefreshSlotSnapshots();
        OnInventoryChanged?.Invoke();
    }

    void RefreshSlotSnapshots()
    {
        slotSnapshots.Clear();
        for (int i = 0; i < slots.Count; i++)
            slotSnapshots.Add(new InventorySlotSnapshot(slots[i].item, slots[i].quantity));
    }
}
