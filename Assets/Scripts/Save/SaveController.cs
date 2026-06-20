using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Orchestrates capturing and restoring a Save File. See ADR 0003.
//
// FIRST SLICE: proves the save -> scene-reload -> restore loop over the cheap core
// (World Seed, boat transform, health, hunger, gold, time of day, weather).
// Hull wear, inventory items, enemies, markers, met-shopkeepers and the discovered
// map are later slices and are deliberately not captured yet.
//
// Load works by scene reload: the World Seed must be injected before any scene
// object Awakes (IslandGenerationController consumes it in Awake), so we set the
// diagnostic seed override, reload the gameplay scene, and restore the remaining
// live state one frame after load (once every Start has run).
public sealed class SaveController : MonoBehaviour
{
    public const int SchemaVersion = 4;

    // Slot 0 is the reserved Autosave; slots 1..ManualSlotCount are player-writable.
    public const int AutosaveSlot = 0;
    public const int ManualSlotCount = 3;

    // The gameplay scene reloaded on load. Title/menu scenes are not reloaded here.
    const string GameplaySceneName = "SampleScene";

    public static SaveController Instance { get; private set; }

    // Last human-readable status ("File Saved", "Load failed", ...) for UI to show.
    public static event Action<string> StatusChanged;
    public static string LastStatus { get; private set; } = "";

    // Temporary slice scaffolding so the loop is testable before the slot UI exists.
    // F5 = save to slot 1, F9 = load slot 1.
    public bool debugHotkeysEnabled = true;

    readonly ISaveSerializer serializer = new JsonUtilitySaveSerializer();
    SaveFile pendingLoad;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null)
            return;

        GameObject host = new GameObject("[SaveController]");
        Instance = host.AddComponent<SaveController>();
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
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    void Update()
    {
        if (!debugHotkeysEnabled || Keyboard.current == null)
            return;

        if (Keyboard.current.f5Key.wasPressedThisFrame)
            SaveToSlot(1);
        else if (Keyboard.current.f9Key.wasPressedThisFrame)
            LoadFromSlot(1);
    }

    public static string SlotPath(int slot) =>
        Path.Combine(Application.persistentDataPath, $"slot{slot}.json");

    public static bool SlotExists(int slot) => File.Exists(SlotPath(slot));

    // Reads just the header of a slot for the load/save list without deserializing
    // the whole payload. Returns null if the slot is empty or unreadable.
    public SaveHeader PeekHeader(int slot)
    {
        try
        {
            if (!SlotExists(slot))
                return null;

            SaveFile file = serializer.Deserialize(File.ReadAllText(SlotPath(slot)));
            return file?.header;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveController] Could not read header for slot {slot}: {e.Message}");
            return null;
        }
    }

    // Writes the Autosave slot as a safety net. No-ops outside gameplay so we never
    // overwrite a good autosave with an empty capture (e.g. from the title screen).
    public bool Autosave()
    {
        if (FindAnyObjectByType<BoatController>() == null)
            return false;

        return SaveToSlot(AutosaveSlot);
    }

    void OnApplicationQuit()
    {
        Autosave();
    }

    public bool SaveToSlot(int slot)
    {
        try
        {
            SaveFile file = CaptureCurrentState();
            File.WriteAllText(SlotPath(slot), serializer.Serialize(file));
            SetStatus("File Saved");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveController] Save to slot {slot} failed: {e}");
            SetStatus("Save failed");
            return false;
        }
    }

    public bool LoadFromSlot(int slot)
    {
        SaveFile file;
        try
        {
            if (!SlotExists(slot))
            {
                SetStatus("Empty slot");
                return false;
            }

            file = serializer.Deserialize(File.ReadAllText(SlotPath(slot)));
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveController] Load from slot {slot} failed: {e}");
            SetStatus("Load failed");
            return false;
        }

        if (!IsCompatibleSaveFile(file))
        {
            SetStatus("Incompatible save");
            return false;
        }

        // Inject the World Seed before the gameplay scene Awakes, then reload it.
        // Remaining state is restored one frame after load (see HandleSceneLoaded).
        pendingLoad = file;
        // The reloaded scene inherits the global timescale; clear any pause freeze
        // so gameplay isn't loaded into a frozen state.
        Time.timeScale = 1f;
        IslandGenerationController.SetDiagnosticPlaySeedOverride(file.world.seed, randomizeSeed: false);
        SceneManager.LoadScene(GameplaySceneName);
        return true;
    }

    SaveFile CaptureCurrentState()
    {
        SaveFile file = new SaveFile();

        file.header.version = SchemaVersion;
        file.header.savedAtUtc = DateTime.UtcNow.ToString("o");

        IslandGenerationController island = FindAnyObjectByType<IslandGenerationController>();
        if (island != null)
            file.world.seed = island.Seed;

        BoatController boat = FindAnyObjectByType<BoatController>();
        if (boat != null)
        {
            Vector3 p = boat.transform.position;
            file.boat.posX = p.x;
            file.boat.posY = p.y;
            file.boat.rotationZ = boat.transform.eulerAngles.z;
        }

        BoatHealthController health = FindAnyObjectByType<BoatHealthController>();
        if (health != null)
            file.progress.health = health.CurrentHealth;

        HungerController hunger = FindAnyObjectByType<HungerController>();
        if (hunger != null)
            file.progress.hunger = hunger.CurrentHunger;

        if (PlaytimeController.Instance != null)
            file.progress.playtimeSeconds = PlaytimeController.Instance.TotalPlaytimeSeconds;

        ShipInventoryController inventory = ShipInventoryController.ActiveInventory;
        if (inventory != null)
        {
            file.progress.gold = inventory.Gold;

            List<InventoryItemSaveData> items = new List<InventoryItemSaveData>();
            foreach (ShipInventoryController.InventorySlotSnapshot slot in inventory.Slots)
            {
                if (slot.IsEmpty || slot.Item == null)
                    continue;

                items.Add(new InventoryItemSaveData { itemId = slot.Item.ItemId, quantity = slot.Quantity });
            }

            file.progress.items = items.ToArray();
        }

        NightEnemySpawner spawner = FindAnyObjectByType<NightEnemySpawner>();
        if (spawner != null)
        {
            List<EnemySaveData> enemies = new List<EnemySaveData>();
            foreach (NightEnemyController enemy in spawner.ActiveEnemies)
            {
                if (enemy == null || enemy.Config == null)
                    continue;

                Vector2 p = enemy.Position;
                enemies.Add(new EnemySaveData
                {
                    configId = enemy.Config.SaveId,
                    posX = p.x,
                    posY = p.y,
                    health = enemy.CurrentHealth
                });
            }

            file.enemies = enemies.ToArray();
        }

        MapMarkerController markerController = MapMarkerController.ActiveInstance;
        if (markerController != null)
        {
            List<MarkerSaveData> markerList = new List<MarkerSaveData>();
            foreach (MapMarkerController.MarkerRecord record in markerController.Markers)
            {
                markerList.Add(new MarkerSaveData
                {
                    id = record.id,
                    markerType = (int)record.markerType,
                    name = record.name,
                    posX = record.worldPosition.x,
                    posY = record.worldPosition.y
                });
            }

            file.markers.markers = markerList.ToArray();
            file.markers.nextMarkerId = markerController.NextMarkerId;
            file.markers.nextDefaultMarkerNumber = markerController.NextDefaultMarkerNumber;
        }

        if (ShopController.ActiveInstance != null)
        {
            List<ShopIdSaveData> metIds = new List<ShopIdSaveData>();
            foreach (Vector2Int id in ShopController.ActiveInstance.MetShopkeepers)
                metIds.Add(new ShopIdSaveData { x = id.x, y = id.y });

            file.metShopkeepers = metIds.ToArray();
        }

        MapDiscoveryController discovery = MapDiscoveryController.ActiveInstance;
        if (discovery != null)
        {
            IReadOnlyCollection<Vector2Int> blocks = discovery.RevealedBlocks;
            int[] blockX = new int[blocks.Count];
            int[] blockY = new int[blocks.Count];
            int index = 0;
            foreach (Vector2Int block in blocks)
            {
                blockX[index] = block.x;
                blockY[index] = block.y;
                index++;
            }

            file.mapDiscovery.blockSize = discovery.RevealMaskBlockSize;
            file.mapDiscovery.blockX = blockX;
            file.mapDiscovery.blockY = blockY;
        }

        DayNightController dayNight = FindAnyObjectByType<DayNightController>();
        int dayCount = 0;
        if (dayNight != null)
        {
            file.worldState.dayCount = dayNight.DayCount;
            file.worldState.normalizedTimeOfDay = dayNight.NormalizedTimeOfDay;
            dayCount = dayNight.DayCount;
        }

        if (WeatherController.ActiveInstance != null)
            file.worldState.weather = (int)WeatherController.ActiveInstance.CurrentWeather;

        int healthPct = health != null && health.MaxHealth > 0f
            ? Mathf.RoundToInt(health.CurrentHealth / health.MaxHealth * 100f)
            : 0;
        file.header.title = $"Day {GetCalendarDay(dayCount)}";
        file.header.summary = $"Day {GetCalendarDay(dayCount)} - {healthPct}% HP - {FormatPlaytime(file.progress.playtimeSeconds)}";

        return file;
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (pendingLoad == null || scene.name != GameplaySceneName)
            return;

        StartCoroutine(ApplyPendingAfterStart());
    }

    IEnumerator ApplyPendingAfterStart()
    {
        // Let every Start() run so we restore over final initialised state, not
        // over values a controller sets in its own Start (e.g. starting health).
        yield return null;
        yield return null;

        SaveFile file = pendingLoad;
        pendingLoad = null;
        if (file == null)
            yield break;

        // Boat first so streaming chunks centre on the restored position.
        BoatController boat = FindAnyObjectByType<BoatController>();
        if (boat != null)
        {
            boat.transform.SetPositionAndRotation(
                new Vector3(file.boat.posX, file.boat.posY, boat.transform.position.z),
                Quaternion.Euler(0f, 0f, file.boat.rotationZ));
        }

        BoatHealthController health = FindAnyObjectByType<BoatHealthController>();
        if (health != null)
            health.SetHealth(file.progress.health);

        HungerController hunger = FindAnyObjectByType<HungerController>();
        if (hunger != null)
            hunger.SetHunger(file.progress.hunger);

        if (PlaytimeController.Instance != null)
            PlaytimeController.Instance.RestorePlaytimeSeconds(file.progress.playtimeSeconds);

        ShipInventoryController inventory = ShipInventoryController.ActiveInventory;
        if (inventory != null)
        {
            inventory.SetGold(file.progress.gold);
            inventory.ClearAllItems();
            if (file.progress.items != null)
            {
                foreach (InventoryItemSaveData item in file.progress.items)
                {
                    ItemDefinition definition = ItemRegistry.Resolve(item.itemId);
                    if (definition != null)
                        inventory.TryAddItem(definition, item.quantity, out _);
                    else
                        Debug.LogWarning($"[SaveController] Could not resolve saved item id '{item.itemId}'.");
                }
            }
        }

        NightEnemySpawner spawner = FindAnyObjectByType<NightEnemySpawner>();
        if (spawner != null)
        {
            spawner.ClearActiveEnemies();
            if (file.enemies != null)
            {
                foreach (EnemySaveData enemy in file.enemies)
                    spawner.RestoreEnemy(enemy.configId, new Vector2(enemy.posX, enemy.posY), enemy.health);
            }
        }

        MapMarkerController markerController = MapMarkerController.ActiveInstance;
        if (markerController != null && file.markers != null)
        {
            List<MapMarkerController.MarkerRecord> records = new List<MapMarkerController.MarkerRecord>();
            if (file.markers.markers != null)
            {
                foreach (MarkerSaveData marker in file.markers.markers)
                {
                    records.Add(new MapMarkerController.MarkerRecord
                    {
                        id = marker.id,
                        markerType = (MapMarkerController.MarkerType)marker.markerType,
                        name = marker.name,
                        worldPosition = new Vector2(marker.posX, marker.posY)
                    });
                }
            }

            markerController.RestoreMarkers(records, file.markers.nextMarkerId, file.markers.nextDefaultMarkerNumber);
        }

        if (ShopController.ActiveInstance != null && file.metShopkeepers != null)
        {
            List<Vector2Int> metIds = new List<Vector2Int>();
            foreach (ShopIdSaveData id in file.metShopkeepers)
                metIds.Add(new Vector2Int(id.x, id.y));

            ShopController.ActiveInstance.RestoreMetShopkeepers(metIds);
        }

        MapDiscoveryController discovery = MapDiscoveryController.ActiveInstance;
        if (discovery != null && file.mapDiscovery != null
            && file.mapDiscovery.blockX != null && file.mapDiscovery.blockY != null)
        {
            int count = Mathf.Min(file.mapDiscovery.blockX.Length, file.mapDiscovery.blockY.Length);
            List<Vector2Int> blocks = new List<Vector2Int>(count);
            for (int i = 0; i < count; i++)
                blocks.Add(new Vector2Int(file.mapDiscovery.blockX[i], file.mapDiscovery.blockY[i]));

            discovery.RestoreRevealedRegions(file.mapDiscovery.blockSize, blocks);
        }

        DayNightController dayNight = FindAnyObjectByType<DayNightController>();
        if (dayNight != null)
            dayNight.RestoreTimeState(file.worldState.dayCount, file.worldState.normalizedTimeOfDay);

        if (WeatherController.ActiveInstance != null)
            WeatherController.ActiveInstance.SetWeather((WeatherState)file.worldState.weather);

        SetStatus("Game Loaded");
    }

    static void SetStatus(string message)
    {
        LastStatus = message;
        StatusChanged?.Invoke(message);
    }

    static bool IsCompatibleSaveFile(SaveFile file)
    {
        return file != null
            && file.header != null
            && file.header.version > 0
            && file.header.version <= SchemaVersion;
    }

    static int GetCalendarDay(int dayCount)
    {
        return Mathf.Max(1, dayCount + 1);
    }

    static string FormatPlaytime(int playtimeSeconds)
    {
        int sanitizedSeconds = Mathf.Max(0, playtimeSeconds);
        int hours = sanitizedSeconds / 3600;
        int minutes = (sanitizedSeconds % 3600) / 60;

        if (hours > 0)
            return $"{hours}h {minutes:00}m";

        return $"{minutes}m";
    }
}
