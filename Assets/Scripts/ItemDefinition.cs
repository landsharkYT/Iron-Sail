using UnityEngine;

public enum ItemCategory
{
    Resource,
    Food,
    Ammo,
    Treasure,
    UpgradeMaterial,
    Quest,
    Misc
}

public enum UtilityItemType
{
    None,
    FishingRod
}

[CreateAssetMenu(fileName = "ItemDefinition", menuName = "The Iron Sail/Item Definition")]
public class ItemDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] string itemId = "item_id";
    [SerializeField] string displayName = "Item";
    [SerializeField] [TextArea(2, 4)] string description = "Item description.";
    [SerializeField] ItemCategory category = ItemCategory.Misc;

    [Header("Presentation")]
    [SerializeField] Sprite iconSprite;

    [Header("Gameplay")]
    [SerializeField] AmmoDefinition ammoDefinition;
    [SerializeField] WeaponDefinition weaponDefinition;
    [SerializeField] float foodRestoreAmount;
    [SerializeField] UtilityItemType utilityItemType;
    [SerializeField] bool isFish;

    [Header("Economy")]
    [SerializeField] float weight = 1f;
    [SerializeField] int value = 1;

    [Header("Stacking")]
    [SerializeField] bool stackable = true;
    [SerializeField] int maxStackSize = 9999;

    public string ItemId => itemId;
    public string DisplayName => displayName;
    public string Description => description;
    public ItemCategory Category => category;
    public Sprite IconSprite => iconSprite;
    public AmmoDefinition AmmoDefinition => ammoDefinition;
    public WeaponDefinition WeaponDefinition => weaponDefinition;
    public float FoodRestoreAmount => foodRestoreAmount;
    public UtilityItemType UtilityItemType => utilityItemType;
    public bool IsFish => isFish;
    public float Weight => weight;
    public int Value => value;
    public bool Stackable => stackable;
    public int MaxStackSize => stackable ? maxStackSize : 1;

    void OnValidate()
    {
        itemId = string.IsNullOrWhiteSpace(itemId) ? "item_id" : itemId.Trim();
        displayName = string.IsNullOrWhiteSpace(displayName) ? "Item" : displayName.Trim();
        weight = Mathf.Max(0f, weight);
        foodRestoreAmount = Mathf.Max(0f, foodRestoreAmount);
        value = Mathf.Max(0, value);
        maxStackSize = Mathf.Clamp(maxStackSize, 1, 9999);
    }

    public void InitializeRuntime(
        string runtimeItemId,
        string runtimeDisplayName,
        string runtimeDescription,
        ItemCategory runtimeCategory,
        float runtimeWeight,
        int runtimeValue,
        bool runtimeStackable,
        int runtimeMaxStackSize,
        Sprite runtimeIconSprite = null,
        float runtimeFoodRestoreAmount = 0f,
        UtilityItemType runtimeUtilityItemType = UtilityItemType.None)
    {
        itemId = runtimeItemId;
        displayName = runtimeDisplayName;
        description = runtimeDescription;
        category = runtimeCategory;
        weight = runtimeWeight;
        value = runtimeValue;
        stackable = runtimeStackable;
        maxStackSize = runtimeMaxStackSize;
        iconSprite = runtimeIconSprite;
        foodRestoreAmount = runtimeFoodRestoreAmount;
        utilityItemType = runtimeUtilityItemType;

        OnValidate();
    }
}
