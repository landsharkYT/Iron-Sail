using System.Collections.Generic;
using UnityEngine;

// Resolves a saved itemId back to its ItemDefinition on load. ItemDefinition
// assets live under Assets/Resources, so they are all discoverable at runtime via
// Resources.LoadAll without any manual wiring. Built lazily and cached.
public static class ItemRegistry
{
    static Dictionary<string, ItemDefinition> byId;

    public static ItemDefinition Resolve(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            return null;

        EnsureLoaded();
        return byId.TryGetValue(itemId, out ItemDefinition item) ? item : null;
    }

    static void EnsureLoaded()
    {
        if (byId != null)
            return;

        byId = new Dictionary<string, ItemDefinition>();
        foreach (ItemDefinition item in Resources.LoadAll<ItemDefinition>(string.Empty))
        {
            if (item == null || string.IsNullOrEmpty(item.ItemId))
                continue;

            byId[item.ItemId] = item;
        }
    }
}
