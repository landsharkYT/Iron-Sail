using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// View-model for one row of the save/load slot list. Built fresh from the
/// SaveController each time the menu opens so headers reflect current files.
/// Slot 0 is the reserved Autosave (load-only); slots 1-3 are player-writable.
/// </summary>
public sealed class LoadMenuSlotData
{
    LoadMenuSlotData(int slotNumber, bool isAutosave, SaveHeader header)
    {
        SlotNumber = slotNumber;
        IsAutosave = isAutosave;
        HasData = header != null;
        SlotLabel = isAutosave ? "Autosave" : $"Slot {slotNumber}";

        if (header != null)
        {
            TitleText = string.IsNullOrEmpty(header.title) ? "Saved Game" : header.title;
            DetailText = BuildDetail(header);
        }
        else
        {
            TitleText = "Empty Slot";
            DetailText = "No save data";
        }
    }

    public int SlotNumber { get; }
    public bool IsAutosave { get; }
    public bool HasData { get; }
    public string SlotLabel { get; }
    public string TitleText { get; }
    public string DetailText { get; }

    public static IReadOnlyList<LoadMenuSlotData> BuildCurrent()
    {
        SaveController controller = SaveController.Instance;
        List<LoadMenuSlotData> slots = new List<LoadMenuSlotData>
        {
            Build(controller, SaveController.AutosaveSlot, true)
        };

        for (int slot = 1; slot <= SaveController.ManualSlotCount; slot++)
            slots.Add(Build(controller, slot, false));

        return slots;
    }

    static LoadMenuSlotData Build(SaveController controller, int slot, bool isAutosave)
    {
        SaveHeader header = controller != null ? controller.PeekHeader(slot) : null;
        return new LoadMenuSlotData(slot, isAutosave, header);
    }

    static string BuildDetail(SaveHeader header)
    {
        string summary = string.IsNullOrEmpty(header.summary) ? "Saved Game" : header.summary;
        string when = FormatTimestamp(header.savedAtUtc);
        return string.IsNullOrEmpty(when) ? summary : $"{summary}\n{when}";
    }

    static string FormatTimestamp(string iso)
    {
        if (string.IsNullOrEmpty(iso))
            return string.Empty;

        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime utc))
            return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        return string.Empty;
    }
}
