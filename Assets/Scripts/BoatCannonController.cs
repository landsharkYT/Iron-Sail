using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(BoatController))]
public class BoatCannonController : MonoBehaviour
{
    readonly struct MuzzleShot
    {
        public MuzzleShot(Vector2 position, Vector2 direction)
        {
            Position = position;
            Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;
        }

        public Vector2 Position { get; }
        public Vector2 Direction { get; }
    }

    enum CannonSideSelection
    {
        None,
        Left,
        Right,
        Up,
        Down
    }

    [Header("References")]
    [SerializeField] BoatVisualController boatVisualController;
    [SerializeField] Camera worldCamera;
    [SerializeField] ShipEquipmentController equipmentController;
    [SerializeField] BoatWeaponAudio boatWeaponAudio;
    [SerializeField] ItemDefinition cannonballItem;
    [SerializeField] SpriteRenderer topDownCannonRenderer;
    [SerializeField] SpriteRenderer sideViewCannonRenderer;

    [Header("Top-Down Broadside Muzzles")]
    [SerializeField] Transform[] leftMuzzles;
    [SerializeField] Transform[] rightMuzzles;

    [Header("Side-View Broadside Muzzles")]
    [SerializeField] Transform[] upMuzzles;
    [SerializeField] Transform[] downMuzzles;

    [Header("Firing")]
    [SerializeField] float cooldownSeconds = 0.45f;

    float cooldownRemaining;

    void Awake()
    {
        if (boatVisualController == null)
            boatVisualController = GetComponent<BoatVisualController>();
        if (equipmentController == null)
            equipmentController = GetComponent<ShipEquipmentController>();
        if (boatWeaponAudio == null)
            boatWeaponAudio = GetComponent<BoatWeaponAudio>();

        if (topDownCannonRenderer == null)
        {
            Transform topDownCannons = transform.Find("BoatVisuals/Cannons");
            if (topDownCannons != null)
                topDownCannonRenderer = topDownCannons.GetComponent<SpriteRenderer>();
        }

        if (sideViewCannonRenderer == null)
        {
            SpriteRenderer[] spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] != null && spriteRenderers[i].name == "SideCannons")
                {
                    sideViewCannonRenderer = spriteRenderers[i];
                    break;
                }
            }
        }
    }

    void Update()
    {
        if (cooldownRemaining > 0f)
            cooldownRemaining -= Time.deltaTime;

        if (InventoryUIController.IsInventoryOpen || WorldMapUIController.IsMapOpen || ShopController.IsShopOpen || FishingMinigameController.IsFishingOpen || PauseMenuController.IsPauseOpen || EndMenuController.IsEndMenuOpen)
            return;

        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.spaceKey.wasPressedThisFrame)
            return;

        bool fireBothSides = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        TryFire(fireBothSides);
    }

    bool TryFire(bool fireBothSides)
    {
        if (cooldownRemaining > 0f)
            return false;

        if (cannonballItem == null || cannonballItem.AmmoDefinition == null)
            return false;

        AmmoDefinition ammoDefinition = cannonballItem.AmmoDefinition;
        if (ammoDefinition.ProjectilePrefab == null)
            return false;
        if (ammoDefinition.ProjectilePrefab.GetComponent<CannonBallProjectile>() == null)
            return false;

        ShipInventoryController inventory = ShipInventoryController.ActiveInventory;
        if (inventory == null)
            return false;

        Camera activeCamera = worldCamera != null ? worldCamera : Camera.main;
        Mouse mouse = Mouse.current;
        if (activeCamera == null || mouse == null)
            return false;

        Vector3 pointerWorld3 = activeCamera.ScreenToWorldPoint(mouse.position.value);
        Vector2 pointerWorld = new Vector2(pointerWorld3.x, pointerWorld3.y);
        bool isSideView = boatVisualController != null && boatVisualController.IsSideViewActive;

        CannonSideSelection chosenSide = GetChosenSide(pointerWorld, isSideView);
        if (chosenSide == CannonSideSelection.None)
            return false;

        MuzzleShot[][] selectedMuzzleSets = fireBothSides
            ? GetDualMuzzleSets(isSideView)
            : GetSingleMuzzleSets(chosenSide);

        if (!AreMuzzleSetsValid(selectedMuzzleSets))
            return false;

        int ammoCost = fireBothSides ? 2 : 1;
        bool consumedAmmo = equipmentController != null
            ? equipmentController.TryConsumeMatchingAmmo(cannonballItem, ammoCost, inventory)
            : inventory.TryConsumeItemByItemId(cannonballItem.ItemId, ammoCost);
        if (!consumedAmmo)
        {
            boatWeaponAudio?.PlayNoAmmoClick();
            RewardUtility.ShowPrompt("Equip cannonballs in the Cannon slot.");
            return false;
        }

        Collider2D[] ownerColliders = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < selectedMuzzleSets.Length; i++)
        {
            MuzzleShot[] muzzleSet = selectedMuzzleSets[i];
            float arcSign = GetArcSignForMuzzleSet(chosenSide, fireBothSides, isSideView, i);
            for (int j = 0; j < muzzleSet.Length; j++)
            {
                MuzzleShot muzzle = muzzleSet[j];
                Vector3 launchDirection = new Vector3(muzzle.Direction.x, muzzle.Direction.y, 0f);
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, launchDirection);
                Vector3 spawnPosition = new Vector3(muzzle.Position.x, muzzle.Position.y, 0f);
                GameObject projectileObject = Instantiate(ammoDefinition.ProjectilePrefab, spawnPosition, rotation);
                CannonBallProjectile projectile = projectileObject.GetComponent<CannonBallProjectile>();
                projectile.Launch(
                    muzzle.Direction,
                    ammoDefinition.ProjectileSpeed,
                    ammoDefinition.ProjectileLifetime,
                    ammoDefinition.MaxRange,
                    ammoDefinition.Damage,
                    ownerColliders,
                    ammoDefinition.SpawnWaterSplashOnExpire,
                    ammoDefinition.WaterSplashPrefab,
                    ammoDefinition.CosmeticArcStrength,
                    arcSign);
            }
        }

        boatWeaponAudio?.PlayCannonFire(fireBothSides);
        cooldownRemaining = cooldownSeconds;
        return true;
    }

    static float GetArcSignForMuzzleSet(CannonSideSelection chosenSide, bool fireBothSides, bool isSideView, int setIndex)
    {
        if (isSideView)
            return 0f;

        if (fireBothSides)
            return setIndex == 0 ? -1f : 1f;

        return chosenSide == CannonSideSelection.Left ? -1f :
               chosenSide == CannonSideSelection.Right ? 1f : 0f;
    }

    CannonSideSelection GetChosenSide(Vector2 pointerWorld, bool isSideView)
    {
        if (isSideView)
            return ChooseSideFromRepresentativePoints(pointerWorld, BuildUpShots(), CannonSideSelection.Up, BuildDownShots(), CannonSideSelection.Down);

        return ChooseSideFromRepresentativePoints(pointerWorld, BuildLeftShots(), CannonSideSelection.Left, BuildRightShots(), CannonSideSelection.Right);
    }

    CannonSideSelection ChooseSideFromRepresentativePoints(
        Vector2 pointerWorld,
        MuzzleShot[] primaryShots,
        CannonSideSelection primarySide,
        MuzzleShot[] secondaryShots,
        CannonSideSelection secondarySide)
    {
        if (!TryGetRepresentativePoint(primaryShots, out Vector2 primaryPoint) ||
            !TryGetRepresentativePoint(secondaryShots, out Vector2 secondaryPoint))
            return CannonSideSelection.None;

        Vector2 sideAxis = primaryPoint - secondaryPoint;
        if (sideAxis.sqrMagnitude <= 0.0001f)
            return CannonSideSelection.None;

        Vector2 midpoint = (primaryPoint + secondaryPoint) * 0.5f;
        float sideDot = Vector2.Dot(pointerWorld - midpoint, sideAxis.normalized);
        return sideDot >= 0f ? primarySide : secondarySide;
    }

    bool TryGetRepresentativePoint(MuzzleShot[] shots, out Vector2 point)
    {
        point = Vector2.zero;
        if (shots == null || shots.Length == 0)
            return false;

        for (int i = 0; i < shots.Length; i++)
            point += shots[i].Position;

        point /= shots.Length;
        return true;
    }

    MuzzleShot[][] GetSingleMuzzleSets(CannonSideSelection side)
    {
        switch (side)
        {
            case CannonSideSelection.Left:
                return new[] { BuildLeftShots() };
            case CannonSideSelection.Right:
                return new[] { BuildRightShots() };
            case CannonSideSelection.Up:
                return new[] { BuildUpShots() };
            case CannonSideSelection.Down:
                return new[] { BuildDownShots() };
            default:
                return null;
        }
    }

    MuzzleShot[][] GetDualMuzzleSets(bool isSideView, bool unused = false)
    {
        if (isSideView)
            return new[] { BuildUpShots(), BuildDownShots() };

        return new[] { BuildLeftShots(), BuildRightShots() };
    }

    bool AreMuzzleSetsValid(MuzzleShot[][] muzzleSets)
    {
        if (muzzleSets == null || muzzleSets.Length == 0)
            return false;

        for (int i = 0; i < muzzleSets.Length; i++)
        {
            MuzzleShot[] muzzleSet = muzzleSets[i];
            if (muzzleSet == null || muzzleSet.Length == 0)
                return false;
        }

        return true;
    }

    MuzzleShot[] BuildLeftShots()
    {
        if (TryBuildShotsFromTransforms(leftMuzzles, out MuzzleShot[] shots))
            return shots;

        return BuildHorizontalShots(topDownCannonRenderer, true);
    }

    MuzzleShot[] BuildRightShots()
    {
        if (TryBuildShotsFromTransforms(rightMuzzles, out MuzzleShot[] shots))
            return shots;

        return BuildHorizontalShots(topDownCannonRenderer, false);
    }

    MuzzleShot[] BuildUpShots()
    {
        if (TryBuildShotsFromTransforms(upMuzzles, out MuzzleShot[] shots))
            return shots;

        return BuildVerticalShots(sideViewCannonRenderer, true);
    }

    MuzzleShot[] BuildDownShots()
    {
        if (TryBuildShotsFromTransforms(downMuzzles, out MuzzleShot[] shots))
            return shots;

        return BuildVerticalShots(sideViewCannonRenderer, false);
    }

    bool TryBuildShotsFromTransforms(Transform[] muzzles, out MuzzleShot[] shots)
    {
        shots = null;
        if (muzzles == null || muzzles.Length == 0)
            return false;

        for (int i = 0; i < muzzles.Length; i++)
        {
            if (muzzles[i] == null)
                return false;
        }

        shots = new MuzzleShot[muzzles.Length];
        for (int i = 0; i < muzzles.Length; i++)
        {
            Vector3 muzzlePosition3 = muzzles[i].position;
            Vector3 muzzleDirection3 = muzzles[i].up;
            shots[i] = new MuzzleShot(
                new Vector2(muzzlePosition3.x, muzzlePosition3.y),
                new Vector2(muzzleDirection3.x, muzzleDirection3.y));
        }

        return true;
    }

    MuzzleShot[] BuildHorizontalShots(SpriteRenderer spriteRenderer, bool leftSide)
    {
        if (spriteRenderer == null)
            return null;

        Vector3 center3 = spriteRenderer.transform.position;
        Vector2 center = new Vector2(center3.x, center3.y);
        Vector3 horizontalAxis3 = spriteRenderer.transform.right.normalized;
        Vector3 verticalAxis3 = spriteRenderer.transform.up.normalized;
        Vector2 horizontalAxis = new Vector2(horizontalAxis3.x, horizontalAxis3.y);
        Vector2 verticalAxis = new Vector2(verticalAxis3.x, verticalAxis3.y);
        Vector2 halfExtents = GetScaledSpriteHalfExtents(spriteRenderer);

        float horizontalDistance = halfExtents.x;
        float verticalOffset = halfExtents.y * 0.22f;
        Vector2 direction = leftSide ? -horizontalAxis : horizontalAxis;
        Vector2 endpointCenter = center + direction * horizontalDistance;

        return new[]
        {
            new MuzzleShot(endpointCenter + verticalAxis * verticalOffset, direction),
            new MuzzleShot(endpointCenter - verticalAxis * verticalOffset, direction)
        };
    }

    MuzzleShot[] BuildVerticalShots(SpriteRenderer spriteRenderer, bool upSide)
    {
        if (spriteRenderer == null)
            return null;

        Vector3 center3 = spriteRenderer.transform.position;
        Vector2 center = new Vector2(center3.x, center3.y);
        Vector3 primaryAxis3 = spriteRenderer.transform.right.normalized;
        Vector3 secondaryAxis3 = spriteRenderer.transform.up.normalized;
        Vector2 primaryAxis = new Vector2(primaryAxis3.x, primaryAxis3.y);
        Vector2 secondaryAxis = new Vector2(secondaryAxis3.x, secondaryAxis3.y);
        Vector2 halfExtents = GetScaledSpriteHalfExtents(spriteRenderer);

        Vector2 positiveEndpoint = center + primaryAxis * halfExtents.x;
        Vector2 negativeEndpoint = center - primaryAxis * halfExtents.x;
        Vector2 endpointCenter = positiveEndpoint.y >= negativeEndpoint.y
            ? (upSide ? positiveEndpoint : negativeEndpoint)
            : (upSide ? negativeEndpoint : positiveEndpoint);

        Vector2 direction = (endpointCenter - center).normalized;
        float sideOffset = halfExtents.y * 0.7f;

        return new[]
        {
            new MuzzleShot(endpointCenter + secondaryAxis * sideOffset, direction),
            new MuzzleShot(endpointCenter - secondaryAxis * sideOffset, direction)
        };
    }

    Vector2 GetScaledSpriteHalfExtents(SpriteRenderer spriteRenderer)
    {
        Vector3 localExtents = spriteRenderer.sprite != null
            ? spriteRenderer.sprite.bounds.extents
            : Vector3.one * 0.1f;

        Vector3 lossyScale = spriteRenderer.transform.lossyScale;
        return new Vector2(
            Mathf.Abs(localExtents.x * lossyScale.x),
            Mathf.Abs(localExtents.y * lossyScale.y));
    }
}
