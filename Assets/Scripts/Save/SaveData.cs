using System;

// Plain serializable DTOs for the Save File. JsonUtility requires [Serializable]
// types with public fields (not properties), so everything here is intentionally
// flat and field-based. Each section is owned conceptually by one runtime system;
// the monolithic root keeps the whole save shape readable in one place.
//
// First slice covers the "cheap core" only. Later slices add enemies[], markers[],
// metShopkeepers[] (and v2 adds the discovered map) as additional sections without
// breaking this shape — readers tolerate missing sections via header.version.

[Serializable]
public sealed class SaveFile
{
    public SaveHeader header = new SaveHeader();
    public WorldSaveData world = new WorldSaveData();
    public BoatSaveData boat = new BoatSaveData();
    public ProgressSaveData progress = new ProgressSaveData();
    public WorldStateSaveData worldState = new WorldStateSaveData();
    public EnemySaveData[] enemies;
    public MarkersSaveData markers = new MarkersSaveData();
    public ShopIdSaveData[] metShopkeepers;
    public MapDiscoverySaveData mapDiscovery = new MapDiscoverySaveData();
}

[Serializable]
public sealed class SaveHeader
{
    // Schema version. Bumped when the save shape changes incompatibly.
    public int version;
    // ISO-8601 UTC timestamp the save was written.
    public string savedAtUtc;
    // Short display title for the slot list.
    public string title;
    // One-line summary for the slot list (e.g. "Day 4 - 12% HP").
    public string summary;
}

[Serializable]
public sealed class WorldSaveData
{
    // The single authoritative World Seed; reproduces all static world features.
    public int seed;
}

[Serializable]
public sealed class BoatSaveData
{
    public float posX;
    public float posY;
    // 2D heading: transform.eulerAngles.z.
    public float rotationZ;
}

[Serializable]
public sealed class ProgressSaveData
{
    public float health;
    public float hunger;
    public int gold;
    public int playtimeSeconds;
    public InventoryItemSaveData[] items;
    // NOTE: hull wear is intentionally absent — it has no independent state. It
    // applies damage straight to health (BoatDamageSource.HullWear), so saved
    // health already encodes it; there is nothing extra to persist.
}

[Serializable]
public sealed class InventoryItemSaveData
{
    public string itemId;
    public int quantity;
}

[Serializable]
public sealed class EnemySaveData
{
    // Stable NightEnemyConfig.SaveId, not asset name or list index.
    public string configId;
    public float posX;
    public float posY;
    public float health;
}

[Serializable]
public sealed class MarkersSaveData
{
    public MarkerSaveData[] markers;
    public int nextMarkerId = 1;
    public int nextDefaultMarkerNumber = 1;
}

[Serializable]
public sealed class MarkerSaveData
{
    public int id;
    public int markerType;
    public string name;
    public float posX;
    public float posY;
}

[Serializable]
public sealed class ShopIdSaveData
{
    public int x;
    public int y;
}

// The discovered map as a coarse reveal mask (Family E). Only WHERE the player
// revealed is stored; the chart is reconstructed from the World Seed on load by
// replaying reveals over these blocks. Parallel arrays keep JsonUtility happy.
[Serializable]
public sealed class MapDiscoverySaveData
{
    public int blockSize = 64;
    public int[] blockX;
    public int[] blockY;
}

[Serializable]
public sealed class WorldStateSaveData
{
    // Internal elapsed-day counter. Player-facing Calendar Day is dayCount + 1.
    public int dayCount;
    public float normalizedTimeOfDay;
    // Stored as the int value of WeatherState for forward tolerance.
    public int weather;
}
