using UnityEngine;

// Controls the boat's ShopIndicatorBubble presentation:
// - shows it when a loaded shop dock is nearby
// - keeps it upright by counter-rotating against the boat
public class BoatShopIndicatorController : MonoBehaviour
{
    const string DefaultBubbleName = "ShopIndicatorBubble";

    [Header("References")]
    [SerializeField] Transform bubbleTransform;
    [SerializeField] ShopDockController shopDockController;

#pragma warning disable CS0414
    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] Vector3 debugDesiredBubbleLocalOffset;
    [SerializeField] bool debugNearShop;
    [SerializeField] Vector2Int debugCurrentShopId;
    [SerializeField] Vector3 debugCurrentShopAnchorWorldPosition;
    [SerializeField] float debugCurrentShopDistance;
#pragma warning restore CS0414

    GameObject bubbleObject;
    Vector3 desiredBubbleLocalOffset;
    bool cachedDesiredOffset;

    void Awake()
    {
        ResolveBubble();
        ResolveShopDockController();
        CacheDesiredOffset();
        SetBubbleVisible(false);
    }

    void LateUpdate()
    {
        ResolveBubble();
        ResolveShopDockController();

        if (bubbleTransform == null || shopDockController == null)
        {
            SetBubbleVisible(false);
            return;
        }

        if (!shopDockController.TryGetNearestShopDock(transform.position, out ShopDockController.ShopDockQueryResult shopResult))
        {
            debugNearShop = false;
            debugCurrentShopId = default;
            debugCurrentShopAnchorWorldPosition = Vector3.zero;
            debugCurrentShopDistance = 0f;
            SetBubbleVisible(false);
            return;
        }

        debugNearShop = true;
        debugCurrentShopId = shopResult.ShopId;
        debugCurrentShopAnchorWorldPosition = shopResult.AnchorWorldPosition;
        debugCurrentShopDistance = shopResult.Distance;

        SetBubbleVisible(true);
        bubbleTransform.localPosition = Quaternion.Inverse(transform.rotation) * desiredBubbleLocalOffset;
        bubbleTransform.localRotation = Quaternion.Inverse(transform.rotation);
    }

    void ResolveBubble()
    {
        if (bubbleTransform == null)
        {
            Transform foundBubble = transform.Find(DefaultBubbleName);
            if (foundBubble != null)
                bubbleTransform = foundBubble;
        }

        if (bubbleTransform != null)
            bubbleObject = bubbleTransform.gameObject;
    }

    void CacheDesiredOffset()
    {
        if (cachedDesiredOffset || bubbleTransform == null)
            return;

        desiredBubbleLocalOffset = bubbleTransform.localPosition;
        debugDesiredBubbleLocalOffset = desiredBubbleLocalOffset;
        cachedDesiredOffset = true;
    }

    void ResolveShopDockController()
    {
        if (shopDockController == null)
            shopDockController = FindAnyObjectByType<ShopDockController>();
    }

    void SetBubbleVisible(bool visible)
    {
        if (bubbleObject != null && bubbleObject.activeSelf != visible)
            bubbleObject.SetActive(visible);
    }
}
