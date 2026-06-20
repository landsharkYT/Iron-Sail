using System;
using System.Collections.Generic;
using UnityEngine;

public class ShipEquipmentController : MonoBehaviour
{
    public enum EquipmentSlotType
    {
        Gun,
        FishingRod,
        CannonAmmo,
        MusketAmmo,
        Stub
    }

    public readonly struct EquipmentSlotSnapshot
    {
        public EquipmentSlotSnapshot(EquipmentSlotType slotType, string label, ItemDefinition item, int quantity)
        {
            SlotType = slotType;
            Label = label;
            Item = item;
            Quantity = quantity;
        }

        public EquipmentSlotType SlotType { get; }
        public string Label { get; }
        public ItemDefinition Item { get; }
        public int Quantity { get; }
        public bool IsEmpty => Item == null || Quantity <= 0;
        public bool IsInteractive => SlotType != EquipmentSlotType.Stub;
    }

    [Serializable]
    class EquipmentSlot
    {
        public EquipmentSlotType slotType;
        public string label;
        public ItemDefinition item;
        public int quantity;

        public bool IsEmpty => item == null || quantity <= 0;

        public void Clear()
        {
            item = null;
            quantity = 0;
        }
    }

    [Header("Setup")]
    [SerializeField] int slotCount = 6;
    [SerializeField] bool registerAsActiveEquipment = true;

    public static ShipEquipmentController ActiveEquipment { get; private set; }
    public static event Action<ShipEquipmentController> OnActiveEquipmentRegistered;

    public event Action OnEquipmentChanged;

    public int SlotCount => slots.Count;
    public int UsedSlotCount => GetUsedSlotCount();
    public float CurrentWeight => GetCurrentWeight();
    public IReadOnlyList<EquipmentSlotSnapshot> Slots => slotSnapshots;

    readonly List<EquipmentSlot> slots = new();
    readonly List<EquipmentSlotSnapshot> slotSnapshots = new();

    ItemDefinition cachedCannonballItem;
    ItemDefinition cachedMusketBallItem;

    void Awake()
    {
        EnsureDefaultSlots();
        RefreshSnapshots();
    }

    void Start()
    {
        TryAutoEquipStartupLoadout();
    }

    void OnEnable()
    {
        if (!registerAsActiveEquipment)
            return;

        ActiveEquipment = this;
        OnActiveEquipmentRegistered?.Invoke(this);
        TryAutoEquipStartupLoadout();
    }

    void OnDisable()
    {
        if (!registerAsActiveEquipment)
            return;

        if (ActiveEquipment != this)
            return;

        ActiveEquipment = null;
        OnActiveEquipmentRegistered?.Invoke(null);
    }

    void OnValidate()
    {
        slotCount = Mathf.Max(6, slotCount);
        EnsureDefaultSlots();
        RefreshSnapshots();
    }

    public bool TryGetSlotSnapshot(int slotIndex, out EquipmentSlotSnapshot slot)
    {
        slot = default;
        if (!IsValidSlotIndex(slotIndex))
            return false;

        EquipmentSlot internalSlot = slots[slotIndex];
        slot = new EquipmentSlotSnapshot(internalSlot.slotType, internalSlot.label, internalSlot.item, internalSlot.quantity);
        return true;
    }

    public EquipmentSlotSnapshot GetGunSlotSnapshot()
    {
        return GetFirstSlotSnapshotByType(EquipmentSlotType.Gun);
    }

    public bool HasFishingRodEquipped()
    {
        return TryFindSlotIndexByType(EquipmentSlotType.FishingRod, out int slotIndex) &&
               !slots[slotIndex].IsEmpty &&
               slots[slotIndex].item != null;
    }

    public int GetTotalQuantityByItemId(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return 0;

        return GetReservedQuantityByItemId(itemId);
    }

    public bool TryConsumeMatchingAmmo(ItemDefinition ammoItem, int amount, ShipInventoryController inventory)
    {
        if (ammoItem == null || amount <= 0)
            return false;

        int equippedQuantity = GetReservedQuantityByItemId(ammoItem.ItemId);
        if (equippedQuantity < amount)
            return false;

        int remaining = amount;
        remaining -= ConsumeReservedAmmo(ammoItem, remaining);
        return remaining <= 0;
    }

    public int GetMatchingSlotAvailableCapacity(ItemDefinition item)
    {
        if (item == null)
            return 0;

        if (!TryFindMatchingSlotIndexForItem(item, out int slotIndex))
            return 0;

        EquipmentSlot slot = slots[slotIndex];
        if (slot.IsEmpty)
            return GetPlaceAmountForSlot(item, item.MaxStackSize, slot.slotType);

        if (!CanMergeIntoSlot(slot, item))
            return 0;

        return Mathf.Max(0, item.MaxStackSize - slot.quantity);
    }

    public int TryStorePurchasedItemInMatchingSlot(ItemDefinition item, int quantity)
    {
        if (item == null || quantity <= 0)
            return 0;

        if (!TryFindMatchingSlotIndexForItem(item, out int slotIndex))
            return 0;

        EquipmentSlot slot = slots[slotIndex];
        if (slot.IsEmpty)
        {
            int amountToPlace = GetPlaceAmountForSlot(item, quantity, slot.slotType);
            if (amountToPlace <= 0)
                return 0;

            slot.item = item;
            slot.quantity = amountToPlace;
            NotifyEquipmentChanged();
            return amountToPlace;
        }

        if (!CanMergeIntoSlot(slot, item))
            return 0;

        int amountToAdd = Mathf.Min(quantity, Mathf.Max(0, item.MaxStackSize - slot.quantity));
        if (amountToAdd <= 0)
            return 0;

        slot.quantity += amountToAdd;
        NotifyEquipmentChanged();
        return amountToAdd;
    }

    public bool TryMoveEquipmentSlotToInventory(int slotIndex, ShipInventoryController inventory)
    {
        if (!IsValidSlotIndex(slotIndex) || inventory == null)
            return false;

        EquipmentSlot slot = slots[slotIndex];
        if (slot.IsEmpty || slot.item == null)
            return false;

        if (!inventory.TryAddItem(slot.item, slot.quantity, out int remainder) || remainder > 0)
            return false;

        slot.Clear();
        NotifyEquipmentChanged();
        return true;
    }

    public int RemoveExtraItemsByItemId(string itemId, int keepCount, EquipmentSlotType preferredSlotType)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return 0;

        int total = GetReservedQuantityByItemId(itemId);
        int toRemove = Mathf.Max(0, total - keepCount);
        if (toRemove <= 0)
            return 0;

        int removed = 0;
        removed += RemoveFromSlots(itemId, toRemove - removed, slot => slot.slotType != preferredSlotType);
        removed += RemoveFromSlots(itemId, toRemove - removed, slot => slot.slotType == preferredSlotType);
        if (removed > 0)
            NotifyEquipmentChanged();
        return removed;
    }

    public bool CanAcceptItemInSlot(int slotIndex, ItemDefinition item)
    {
        if (!IsValidSlotIndex(slotIndex) || item == null)
            return false;

        return IsItemValidForSlot(item, slots[slotIndex], out _);
    }

    public bool TryConsumeFromSlot(int slotIndex, int amount, out ItemDefinition consumedItem, out int consumedQuantity)
    {
        consumedItem = null;
        consumedQuantity = 0;
        if (!IsValidSlotIndex(slotIndex) || amount <= 0)
            return false;

        EquipmentSlot slot = slots[slotIndex];
        if (slot.IsEmpty || slot.item == null)
            return false;

        consumedItem = slot.item;
        consumedQuantity = Mathf.Min(amount, slot.quantity);
        slot.quantity -= consumedQuantity;
        if (slot.quantity <= 0)
            slot.Clear();

        NotifyEquipmentChanged();
        return true;
    }

    public bool TryEquipFromInventorySlot(ShipInventoryController inventory, int inventorySlotIndex, int equipmentSlotIndex)
    {
        if (inventory == null || !IsValidSlotIndex(equipmentSlotIndex))
            return false;

        if (!inventory.TryGetSlotSnapshot(inventorySlotIndex, out var inventorySlot) || inventorySlot.IsEmpty || inventorySlot.Item == null)
            return false;

        return TryPlaceItemFromInventorySnapshot(inventory, inventorySlotIndex, inventorySlot.Item, inventorySlot.Quantity, equipmentSlotIndex);
    }

    public bool TryPlaceCarriedItem(int equipmentSlotIndex, ItemDefinition item, int quantity, ShipInventoryController inventory, out int remainder)
    {
        remainder = quantity;
        if (!IsValidSlotIndex(equipmentSlotIndex) || item == null || quantity <= 0 || inventory == null)
            return false;

        EquipmentSlot slot = slots[equipmentSlotIndex];
        if (!IsItemValidForSlot(item, slot, out _))
            return false;

        if (slot.IsEmpty)
        {
            int placeAmount = GetPlaceAmountForSlot(item, quantity, slot.slotType);
            slot.item = item;
            slot.quantity = placeAmount;
            remainder = quantity - placeAmount;
            NotifyEquipmentChanged();
            return true;
        }

        if (CanMergeIntoSlot(slot, item))
        {
            int maxAdd = Mathf.Min(quantity, item.MaxStackSize - slot.quantity);
            if (maxAdd <= 0)
                return false;

            slot.quantity += maxAdd;
            remainder = quantity - maxAdd;
            NotifyEquipmentChanged();
            return true;
        }

        if (!inventory.TryAddItem(slot.item, slot.quantity, out int displacedRemainder) || displacedRemainder > 0)
            return false;

        int amountToPlace = GetPlaceAmountForSlot(item, quantity, slot.slotType);
        slot.item = item;
        slot.quantity = amountToPlace;
        remainder = quantity - amountToPlace;
        NotifyEquipmentChanged();
        return true;
    }

    public bool TryAutoEquipStartupLoadout(ItemDefinition fishingRodOverride = null)
    {
        ShipInventoryController inventory = ShipInventoryController.ActiveInventory ?? GetComponent<ShipInventoryController>();
        if (inventory == null)
            return false;

        bool changed = false;
        changed |= TryAutoEquipFirstMatch(inventory, EquipmentSlotType.Gun, item => item != null && item.WeaponDefinition != null);
        changed |= TryAutoEquipFirstMatch(inventory, EquipmentSlotType.FishingRod, item => item != null && item.UtilityItemType == UtilityItemType.FishingRod, fishingRodOverride);
        changed |= TryAutoEquipFirstMatch(inventory, EquipmentSlotType.CannonAmmo, item => MatchesAmmoSlot(item, EquipmentSlotType.CannonAmmo));
        changed |= TryAutoEquipFirstMatch(inventory, EquipmentSlotType.MusketAmmo, item => MatchesAmmoSlot(item, EquipmentSlotType.MusketAmmo));
        return changed;
    }

    public bool TryAutoEquipFishingRod(ItemDefinition fishingRodOverride = null)
    {
        ShipInventoryController inventory = ShipInventoryController.ActiveInventory ?? GetComponent<ShipInventoryController>();
        if (inventory == null)
            return false;

        return TryAutoEquipFirstMatch(
            inventory,
            EquipmentSlotType.FishingRod,
            item => item != null && item.UtilityItemType == UtilityItemType.FishingRod,
            fishingRodOverride);
    }

    int GetReservedQuantityByItemId(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return 0;

        int total = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            EquipmentSlot slot = slots[i];
            if (slot.IsEmpty || slot.item == null)
                continue;

            if (string.Equals(slot.item.ItemId, itemId, StringComparison.Ordinal))
                total += slot.quantity;
        }

        return total;
    }

    int ConsumeReservedAmmo(ItemDefinition ammoItem, int amount)
    {
        if (ammoItem == null || amount <= 0)
            return 0;

        int remaining = amount;
        for (int i = 0; i < slots.Count && remaining > 0; i++)
        {
            EquipmentSlot slot = slots[i];
            if (slot.IsEmpty || slot.item == null || !string.Equals(slot.item.ItemId, ammoItem.ItemId, StringComparison.Ordinal))
                continue;

            int amountToConsume = Mathf.Min(slot.quantity, remaining);
            slot.quantity -= amountToConsume;
            remaining -= amountToConsume;
            if (slot.quantity <= 0)
                slot.Clear();
        }

        if (remaining != amount)
            NotifyEquipmentChanged();

        return amount - remaining;
    }

    int RemoveFromSlots(string itemId, int remainingToRemove, Predicate<EquipmentSlot> predicate)
    {
        if (remainingToRemove <= 0)
            return 0;

        int removed = 0;
        for (int i = 0; i < slots.Count && remainingToRemove > 0; i++)
        {
            EquipmentSlot slot = slots[i];
            if (slot.IsEmpty || slot.item == null || !predicate(slot))
                continue;

            if (!string.Equals(slot.item.ItemId, itemId, StringComparison.Ordinal))
                continue;

            int amountToRemove = Mathf.Min(slot.quantity, remainingToRemove);
            slot.quantity -= amountToRemove;
            remainingToRemove -= amountToRemove;
            removed += amountToRemove;
            if (slot.quantity <= 0)
                slot.Clear();
        }

        return removed;
    }

    EquipmentSlotSnapshot GetFirstSlotSnapshotByType(EquipmentSlotType slotType)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].slotType == slotType)
                return new EquipmentSlotSnapshot(slots[i].slotType, slots[i].label, slots[i].item, slots[i].quantity);
        }

        return default;
    }

    bool TryAutoEquipFirstMatch(ShipInventoryController inventory, EquipmentSlotType slotType, Predicate<ItemDefinition> predicate, ItemDefinition preferredItem = null)
    {
        if (!TryFindSlotIndexByType(slotType, out int slotIndex))
            return false;

        EquipmentSlot slot = slots[slotIndex];
        if (!slot.IsEmpty)
            return false;

        if (preferredItem != null && TryFindInventorySlotByItem(inventory, preferredItem, out int preferredIndex))
            return TryEquipFromInventorySlot(inventory, preferredIndex, slotIndex);

        for (int i = 0; i < inventory.Slots.Count; i++)
        {
            if (!inventory.TryGetSlotSnapshot(i, out var snapshot) || snapshot.IsEmpty || snapshot.Item == null)
                continue;

            if (!predicate(snapshot.Item))
                continue;

            return TryEquipFromInventorySlot(inventory, i, slotIndex);
        }

        return false;
    }

    bool TryFindInventorySlotByItem(ShipInventoryController inventory, ItemDefinition item, out int slotIndex)
    {
        slotIndex = -1;
        if (inventory == null || item == null)
            return false;

        for (int i = 0; i < inventory.Slots.Count; i++)
        {
            if (!inventory.TryGetSlotSnapshot(i, out var snapshot) || snapshot.IsEmpty || snapshot.Item == null)
                continue;

            if (string.Equals(snapshot.Item.ItemId, item.ItemId, StringComparison.Ordinal))
            {
                slotIndex = i;
                return true;
            }
        }

        return false;
    }

    bool TryPlaceItemFromInventorySnapshot(ShipInventoryController inventory, int inventorySlotIndex, ItemDefinition item, int quantity, int equipmentSlotIndex)
    {
        EquipmentSlot slot = slots[equipmentSlotIndex];
        if (!IsItemValidForSlot(item, slot, out _))
            return false;

        if (CanMergeIntoSlot(slot, item))
        {
            int maxAdd = Mathf.Min(quantity, item.MaxStackSize - slot.quantity);
            if (maxAdd <= 0)
                return false;

            if (!inventory.TryRemoveQuantityAt(inventorySlotIndex, maxAdd, out ItemDefinition mergedRemovedItem, out int mergedRemovedQuantity) ||
                mergedRemovedItem == null || mergedRemovedQuantity <= 0)
                return false;

            slot.quantity += mergedRemovedQuantity;
            NotifyEquipmentChanged();
            return true;
        }

        if (!slot.IsEmpty)
        {
            if (!inventory.TryAddItem(slot.item, slot.quantity, out int displacedRemainder) || displacedRemainder > 0)
                return false;

            slot.Clear();
        }

        int amountToMove = GetPlaceAmountForSlot(item, quantity, slot.slotType);
        if (!inventory.TryRemoveQuantityAt(inventorySlotIndex, amountToMove, out ItemDefinition removedItem, out int removedQuantity) ||
            removedItem == null || removedQuantity <= 0)
            return false;

        slot.item = removedItem;
        slot.quantity = removedQuantity;
        NotifyEquipmentChanged();
        return true;
    }

    bool TryFindSlotIndexByType(EquipmentSlotType slotType, out int slotIndex)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].slotType == slotType)
            {
                slotIndex = i;
                return true;
            }
        }

        slotIndex = -1;
        return false;
    }

    bool TryFindMatchingSlotIndexForItem(ItemDefinition item, out int slotIndex)
    {
        slotIndex = -1;
        if (item == null)
            return false;

        EquipmentSlotType desiredSlotType;
        if (item.WeaponDefinition != null)
            desiredSlotType = EquipmentSlotType.Gun;
        else if (item.UtilityItemType == UtilityItemType.FishingRod)
            desiredSlotType = EquipmentSlotType.FishingRod;
        else if (MatchesAmmoSlot(item, EquipmentSlotType.CannonAmmo))
            desiredSlotType = EquipmentSlotType.CannonAmmo;
        else if (MatchesAmmoSlot(item, EquipmentSlotType.MusketAmmo))
            desiredSlotType = EquipmentSlotType.MusketAmmo;
        else
            return false;

        return TryFindSlotIndexByType(desiredSlotType, out slotIndex);
    }

    bool CanMergeIntoSlot(EquipmentSlot slot, ItemDefinition item)
    {
        return slot != null &&
               !slot.IsEmpty &&
               slot.item != null &&
               item != null &&
               slot.item.Stackable &&
               item.Stackable &&
               string.Equals(slot.item.ItemId, item.ItemId, StringComparison.Ordinal) &&
               slot.quantity < slot.item.MaxStackSize;
    }

    int GetPlaceAmountForSlot(ItemDefinition item, int quantity, EquipmentSlotType slotType)
    {
        if (item == null || quantity <= 0)
            return 0;

        if (!item.Stackable || slotType == EquipmentSlotType.Gun || slotType == EquipmentSlotType.FishingRod)
            return 1;

        return Mathf.Min(quantity, item.MaxStackSize);
    }

    bool MatchesAmmoSlot(ItemDefinition item, EquipmentSlotType slotType)
    {
        if (item == null || item.AmmoDefinition == null)
            return false;

        return slotType switch
        {
            EquipmentSlotType.CannonAmmo => string.Equals(item.ItemId, ResolveCannonballItem()?.ItemId, StringComparison.Ordinal),
            EquipmentSlotType.MusketAmmo => string.Equals(item.ItemId, ResolveMusketBallItem()?.ItemId, StringComparison.Ordinal),
            _ => false
        };
    }

    bool IsItemValidForSlot(ItemDefinition item, EquipmentSlot slot, out string error)
    {
        error = null;

        if (item == null)
        {
            error = "No item to place.";
            return false;
        }

        switch (slot.slotType)
        {
            case EquipmentSlotType.Gun:
                if (item.WeaponDefinition == null)
                {
                    error = "Item is missing a weapon definition.";
                    return false;
                }
                if (item.Stackable || item.MaxStackSize != 1)
                {
                    error = "Equippable guns must be non-stackable with MaxStackSize 1.";
                    return false;
                }
                if (item.WeaponDefinition.AllowedSlotType != slot.slotType)
                {
                    error = $"Weapon '{item.DisplayName}' is not compatible with slot type '{slot.slotType}'.";
                    return false;
                }
                return true;

            case EquipmentSlotType.FishingRod:
                if (item.UtilityItemType != UtilityItemType.FishingRod)
                {
                    error = "Only a fishing rod fits here.";
                    return false;
                }
                return true;

            case EquipmentSlotType.CannonAmmo:
            case EquipmentSlotType.MusketAmmo:
                if (!MatchesAmmoSlot(item, slot.slotType))
                {
                    error = "That ammo does not belong in this slot.";
                    return false;
                }
                return true;

            case EquipmentSlotType.Stub:
                error = "That reserved slot is inactive.";
                return false;

            default:
                error = "Unsupported slot type.";
                return false;
        }
    }

    bool IsValidSlotIndex(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < slots.Count;
    }

    void EnsureDefaultSlots()
    {
        if (slotCount < 6)
            slotCount = 6;

        while (slots.Count < slotCount)
            slots.Add(new EquipmentSlot());

        while (slots.Count > slotCount)
            slots.RemoveAt(slots.Count - 1);

        for (int i = 0; i < slots.Count; i++)
        {
            EquipmentSlot slot = slots[i];
            switch (i)
            {
                case 0:
                    slot.slotType = EquipmentSlotType.Gun;
                    slot.label = "Gun";
                    break;
                case 1:
                    slot.slotType = EquipmentSlotType.FishingRod;
                    slot.label = "Rod";
                    break;
                case 2:
                    slot.slotType = EquipmentSlotType.CannonAmmo;
                    slot.label = "Cannon";
                    break;
                case 3:
                    slot.slotType = EquipmentSlotType.MusketAmmo;
                    slot.label = "Musket";
                    break;
                default:
                    slot.slotType = EquipmentSlotType.Stub;
                    slot.label = "Stub";
                    break;
            }

            if (slot.quantity <= 0 || slot.item == null)
                slot.Clear();
        }
    }

    ItemDefinition ResolveCannonballItem()
    {
        if (cachedCannonballItem != null)
            return cachedCannonballItem;

        cachedCannonballItem = Resources.Load<ItemDefinition>("CannonballItem");
        return cachedCannonballItem;
    }

    ItemDefinition ResolveMusketBallItem()
    {
        if (cachedMusketBallItem != null)
            return cachedMusketBallItem;

        cachedMusketBallItem = Resources.Load<ItemDefinition>("MusketBallItem");
        return cachedMusketBallItem;
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

    float GetCurrentWeight()
    {
        float total = 0f;
        for (int i = 0; i < slots.Count; i++)
        {
            EquipmentSlot slot = slots[i];
            if (slot.IsEmpty || slot.item == null)
                continue;

            total += slot.item.Weight * slot.quantity;
        }

        return total;
    }

    void NotifyEquipmentChanged()
    {
        RefreshSnapshots();
        OnEquipmentChanged?.Invoke();
    }

    void RefreshSnapshots()
    {
        slotSnapshots.Clear();
        for (int i = 0; i < slots.Count; i++)
            slotSnapshots.Add(new EquipmentSlotSnapshot(slots[i].slotType, slots[i].label, slots[i].item, slots[i].quantity));
    }
}
