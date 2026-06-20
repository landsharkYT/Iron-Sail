using UnityEngine;
using UnityEngine.InputSystem;
public class BoatGunController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Camera worldCamera;
    [SerializeField] Transform gunAnchor;
    [SerializeField] ShipEquipmentController equipmentController;
    [SerializeField] BoatWeaponAudio boatWeaponAudio;

    float cooldownRemaining;

    void Awake()
    {
        if (equipmentController == null)
            equipmentController = GetComponent<ShipEquipmentController>();
        if (boatWeaponAudio == null)
            boatWeaponAudio = GetComponent<BoatWeaponAudio>();

        if (gunAnchor == null)
        {
            Transform anchor = transform.Find("GunAnchor");
            if (anchor != null)
                gunAnchor = anchor;
        }
    }

    void Update()
    {
        if (cooldownRemaining > 0f)
            cooldownRemaining -= Time.deltaTime;

        if (InventoryUIController.IsInventoryOpen || WorldMapUIController.IsMapOpen || ShopController.IsShopOpen || FishingMinigameController.IsFishingOpen || PauseMenuController.IsPauseOpen || EndMenuController.IsEndMenuOpen)
            return;

        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            return;

        InventoryUIController inventoryUi = InventoryUIController.ActiveInstance;
        if (inventoryUi != null && inventoryUi.IsPointerOverInventoryOverlay(mouse.position.value))
            return;

        TryFireEquippedGun(mouse.position.value);
    }

    bool TryFireEquippedGun(Vector2 mouseScreenPosition)
    {
        if (cooldownRemaining > 0f || equipmentController == null || gunAnchor == null)
            return false;

        ShipEquipmentController.EquipmentSlotSnapshot gunSlot = equipmentController.GetGunSlotSnapshot();
        if (gunSlot.IsEmpty || gunSlot.Item == null || gunSlot.Item.WeaponDefinition == null)
            return false;

        WeaponDefinition weapon = gunSlot.Item.WeaponDefinition;
        ItemDefinition ammoItem = weapon.RequiredAmmoItem;
        if (ammoItem == null || ammoItem.AmmoDefinition == null)
            return false;

        ShipInventoryController inventory = ShipInventoryController.ActiveInventory;
        if (inventory == null)
            return false;

        AmmoDefinition ammo = ammoItem.AmmoDefinition;
        if (ammo.ProjectilePrefab == null)
            return false;

        Camera activeCamera = worldCamera != null ? worldCamera : Camera.main;
        if (activeCamera == null)
            return false;

        Vector3 cursorWorld3 = activeCamera.ScreenToWorldPoint(mouseScreenPosition);
        Vector2 cursorWorld = new Vector2(cursorWorld3.x, cursorWorld3.y);
        Vector3 anchorPosition3 = gunAnchor.position;
        Vector2 anchorPosition = new Vector2(anchorPosition3.x, anchorPosition3.y);
        Vector2 launchDirection = cursorWorld - anchorPosition;
        if (launchDirection.sqrMagnitude <= 0.0001f)
            return false;

        if (ammo.ProjectilePrefab.GetComponent<CannonBallProjectile>() == null)
            return false;

        Vector2 normalizedDirection = launchDirection.normalized;
        Vector3 spawnPosition = new Vector3(anchorPosition.x, anchorPosition.y, 0f);
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, new Vector3(normalizedDirection.x, normalizedDirection.y, 0f));
        bool consumedAmmo = equipmentController != null
            ? equipmentController.TryConsumeMatchingAmmo(ammoItem, 1, inventory)
            : inventory.TryConsumeItemByItemId(ammoItem.ItemId, 1);
        if (!consumedAmmo)
        {
            boatWeaponAudio?.PlayNoAmmoClick();
            RewardUtility.ShowPrompt("Equip musket ammo in the Musket slot.");
            return false;
        }

        GameObject projectileObject = Instantiate(ammo.ProjectilePrefab, spawnPosition, rotation);
        CannonBallProjectile projectile = projectileObject.GetComponent<CannonBallProjectile>();
        projectile.Launch(
            normalizedDirection,
            ammo.ProjectileSpeed,
            ammo.ProjectileLifetime,
            ammo.MaxRange,
            ammo.Damage,
            GetComponentsInChildren<Collider2D>(true),
            ammo.SpawnWaterSplashOnExpire,
            ammo.WaterSplashPrefab,
            ammo.CosmeticArcStrength,
            0f);
        boatWeaponAudio?.PlayGunFire();
        cooldownRemaining = weapon.CooldownSeconds;
        return true;
    }
}
