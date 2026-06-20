using UnityEngine;

[CreateAssetMenu(fileName = "WeaponDefinition", menuName = "The Iron Sail/Weapon Definition")]
public class WeaponDefinition : ScriptableObject
{
    [Header("Equipment")]
    [SerializeField] ShipEquipmentController.EquipmentSlotType allowedSlotType = ShipEquipmentController.EquipmentSlotType.Gun;

    [Header("Ammo")]
    [SerializeField] ItemDefinition requiredAmmoItem;

    [Header("Firing")]
    [SerializeField] float cooldownSeconds = 0.35f;

    public ShipEquipmentController.EquipmentSlotType AllowedSlotType => allowedSlotType;
    public ItemDefinition RequiredAmmoItem => requiredAmmoItem;
    public float CooldownSeconds => cooldownSeconds;

    void OnValidate()
    {
        cooldownSeconds = Mathf.Max(0.01f, cooldownSeconds);
    }
}
