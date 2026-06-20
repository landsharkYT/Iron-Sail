using UnityEngine;

public sealed class PlaytimeController : MonoBehaviour
{
    public static PlaytimeController Instance { get; private set; }

    public int TotalPlaytimeSeconds => Mathf.Max(0, Mathf.FloorToInt(totalPlaytimeSeconds));

    float totalPlaytimeSeconds;
    BoatController cachedBoatController;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null)
            return;

        GameObject host = new GameObject("[PlaytimeController]");
        Instance = host.AddComponent<PlaytimeController>();
        DontDestroyOnLoad(host);
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        if (!ShouldAccumulatePlaytime())
            return;

        totalPlaytimeSeconds += Time.unscaledDeltaTime;
    }

    public void ResetPlaytime()
    {
        totalPlaytimeSeconds = 0f;
    }

    public void RestorePlaytimeSeconds(int restoredPlaytimeSeconds)
    {
        totalPlaytimeSeconds = Mathf.Max(0, restoredPlaytimeSeconds);
    }

    bool ShouldAccumulatePlaytime()
    {
        if (Time.timeScale <= 0f)
            return false;

        if (InventoryUIController.IsInventoryOpen || WorldMapUIController.IsMapOpen ||
            ShopController.IsShopOpen || FishingMinigameController.IsFishingOpen ||
            PauseMenuController.IsPauseOpen || EndMenuController.IsEndMenuOpen)
        {
            return false;
        }

        if (cachedBoatController == null)
            cachedBoatController = FindAnyObjectByType<BoatController>();

        return cachedBoatController != null;
    }
}
