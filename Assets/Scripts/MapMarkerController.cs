using System;
using System.Collections.Generic;
using UnityEngine;

public class MapMarkerController : MonoBehaviour
{
    public static MapMarkerController ActiveInstance { get; private set; }

    public enum MarkerType
    {
        PurpleX = 0,
        RedX = 1,
        YellowX = 2
    }

    [Serializable]
    public struct MarkerTypeDefinition
    {
        public Sprite sprite;
        [Range(0f, 1f)] public float labelVisibleZoomThreshold;
        [Range(0f, 1f)] public float iconVisibleZoomThreshold;
        public bool visibleByDefault;
        public bool allowManualRename;
        public bool allowManualRemove;
    }

    [Serializable]
    public struct MarkerRecord
    {
        public int id;
        public MarkerType markerType;
        public string name;
        public Vector2 worldPosition;
    }

    [Header("Marker Types")]
    [SerializeField] MarkerTypeDefinition purpleXDefinition = new MarkerTypeDefinition
    {
        labelVisibleZoomThreshold = 0.28f,
        iconVisibleZoomThreshold = 0.08f,
        visibleByDefault = true,
        allowManualRename = true,
        allowManualRemove = true
    };
    [SerializeField] MarkerTypeDefinition redXDefinition = new MarkerTypeDefinition
    {
        labelVisibleZoomThreshold = 0.22f,
        iconVisibleZoomThreshold = 0.05f,
        visibleByDefault = true,
        allowManualRename = false,
        allowManualRemove = false
    };
    [SerializeField] MarkerTypeDefinition yellowXDefinition = new MarkerTypeDefinition
    {
        labelVisibleZoomThreshold = 0.22f,
        iconVisibleZoomThreshold = 0.05f,
        visibleByDefault = true,
        allowManualRename = false,
        allowManualRemove = false
    };

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] bool debugPurpleXVisible = true;
    [SerializeField] bool debugRedXVisible = true;
    [SerializeField] bool debugYellowXVisible = true;
    [SerializeField] int debugMarkerCount;
    [SerializeField] int debugNextMarkerId = 1;
    [SerializeField] int debugNextDefaultMarkerNumber = 1;

    readonly List<MarkerRecord> markers = new List<MarkerRecord>();

    bool visibilityInitialized;
    bool purpleXVisible;
    bool redXVisible;
    bool yellowXVisible;
    int nextMarkerId = 1;
    int nextDefaultMarkerNumber = 1;

    public event Action MarkersChanged;

    public IReadOnlyList<MarkerRecord> Markers => markers;
    public int NextMarkerId => nextMarkerId;
    public int NextDefaultMarkerNumber => nextDefaultMarkerNumber;

    // Save-restore seam: replace all markers and the id counters wholesale.
    public void RestoreMarkers(List<MarkerRecord> records, int nextId, int nextDefaultNumber)
    {
        EnsureRuntimeState();
        markers.Clear();
        if (records != null)
            markers.AddRange(records);

        nextMarkerId = Mathf.Max(1, nextId);
        nextDefaultMarkerNumber = Mathf.Max(1, nextDefaultNumber);

        SyncDebugState();
        MarkersChanged?.Invoke();
    }

    void OnEnable()
    {
        ActiveInstance = this;
        EnsureRuntimeState();
    }

    void OnDisable()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;
    }

    void OnValidate()
    {
        ClampDefinition(ref purpleXDefinition);
        ClampDefinition(ref redXDefinition);
        ClampDefinition(ref yellowXDefinition);

        if (nextMarkerId < 1)
            nextMarkerId = 1;
        if (nextDefaultMarkerNumber < 1)
            nextDefaultMarkerNumber = 1;

        SyncDebugState();
    }

    void EnsureRuntimeState()
    {
        if (!visibilityInitialized)
        {
            purpleXVisible = purpleXDefinition.visibleByDefault;
            redXVisible = redXDefinition.visibleByDefault;
            yellowXVisible = yellowXDefinition.visibleByDefault;
            visibilityInitialized = true;
        }

        if (nextMarkerId < 1)
            nextMarkerId = 1;
        if (nextDefaultMarkerNumber < 1)
            nextDefaultMarkerNumber = 1;

        SyncDebugState();
    }

    public MarkerTypeDefinition GetTypeDefinition(MarkerType markerType)
    {
        return markerType switch
        {
            MarkerType.PurpleX => purpleXDefinition,
            MarkerType.RedX => redXDefinition,
            MarkerType.YellowX => yellowXDefinition,
            _ => purpleXDefinition
        };
    }

    public bool IsTypeVisible(MarkerType markerType)
    {
        return markerType switch
        {
            MarkerType.PurpleX => purpleXVisible,
            MarkerType.RedX => redXVisible,
            MarkerType.YellowX => yellowXVisible,
            _ => false
        };
    }

    public void SetTypeVisible(MarkerType markerType, bool visible)
    {
        EnsureRuntimeState();

        bool changed = markerType switch
        {
            MarkerType.PurpleX => SetVisibility(ref purpleXVisible, visible),
            MarkerType.RedX => SetVisibility(ref redXVisible, visible),
            MarkerType.YellowX => SetVisibility(ref yellowXVisible, visible),
            _ => false
        };

        if (!changed)
            return;

        SyncDebugState();
        RaiseMarkersChanged();
    }

    public MarkerRecord AddMarker(MarkerType markerType, Vector2 worldPosition)
    {
        return AddMarker(markerType, worldPosition, null);
    }

    public MarkerRecord AddMarker(MarkerType markerType, Vector2 worldPosition, string customName)
    {
        EnsureRuntimeState();

        MarkerRecord marker = new MarkerRecord
        {
            id = nextMarkerId++,
            markerType = markerType,
            name = string.IsNullOrWhiteSpace(customName)
                ? GenerateDefaultName(markerType)
                : customName.Trim(),
            worldPosition = worldPosition
        };

        markers.Add(marker);
        SyncDebugState();
        RaiseMarkersChanged();
        return marker;
    }

    public MarkerRecord SetSingleMarker(MarkerType markerType, Vector2 worldPosition, string customName)
    {
        EnsureRuntimeState();
        ClearMarkersOfType(markerType);
        return AddMarker(markerType, worldPosition, customName);
    }

    public void ClearMarkersOfType(MarkerType markerType)
    {
        EnsureRuntimeState();

        bool removedAny = false;
        for (int i = markers.Count - 1; i >= 0; i--)
        {
            if (markers[i].markerType != markerType)
                continue;

            markers.RemoveAt(i);
            removedAny = true;
        }

        if (!removedAny)
            return;

        SyncDebugState();
        RaiseMarkersChanged();
    }

    public bool TryGetFirstMarkerOfType(MarkerType markerType, out MarkerRecord markerRecord)
    {
        EnsureRuntimeState();

        for (int i = 0; i < markers.Count; i++)
        {
            if (markers[i].markerType != markerType)
                continue;

            markerRecord = markers[i];
            return true;
        }

        markerRecord = default;
        return false;
    }

    public bool RenameMarker(int markerId, string requestedName)
    {
        EnsureRuntimeState();

        for (int i = 0; i < markers.Count; i++)
        {
            MarkerRecord marker = markers[i];
            if (marker.id != markerId)
                continue;

            MarkerTypeDefinition definition = GetTypeDefinition(marker.markerType);
            if (!definition.allowManualRename)
                return false;

            string trimmedName = string.IsNullOrWhiteSpace(requestedName)
                ? marker.name
                : requestedName.Trim();

            if (string.Equals(marker.name, trimmedName, StringComparison.Ordinal))
                return false;

            marker.name = trimmedName;
            markers[i] = marker;
            RaiseMarkersChanged();
            return true;
        }

        return false;
    }

    public bool RemoveMarker(int markerId)
    {
        EnsureRuntimeState();

        for (int i = 0; i < markers.Count; i++)
        {
            if (markers[i].id != markerId)
                continue;

            MarkerTypeDefinition definition = GetTypeDefinition(markers[i].markerType);
            if (!definition.allowManualRemove)
                return false;

            markers.RemoveAt(i);
            SyncDebugState();
            RaiseMarkersChanged();
            return true;
        }

        return false;
    }

    static void ClampDefinition(ref MarkerTypeDefinition definition)
    {
        definition.labelVisibleZoomThreshold = Mathf.Clamp01(definition.labelVisibleZoomThreshold);
        definition.iconVisibleZoomThreshold = Mathf.Clamp01(definition.iconVisibleZoomThreshold);
    }

    bool SetVisibility(ref bool field, bool value)
    {
        if (field == value)
            return false;

        field = value;
        return true;
    }

    string GenerateDefaultName(MarkerType markerType)
    {
        if (markerType == MarkerType.PurpleX)
        {
            string purpleName = $"Marker {nextDefaultMarkerNumber}";
            nextDefaultMarkerNumber++;
            return purpleName;
        }

        return markerType switch
        {
            MarkerType.RedX => "Lead",
            MarkerType.YellowX => "Treasure",
            _ => "Marker"
        };
    }

    void RaiseMarkersChanged()
    {
        SyncDebugState();
        MarkersChanged?.Invoke();
    }

    void SyncDebugState()
    {
        debugPurpleXVisible = purpleXVisible;
        debugRedXVisible = redXVisible;
        debugYellowXVisible = yellowXVisible;
        debugMarkerCount = markers.Count;
        debugNextMarkerId = nextMarkerId;
        debugNextDefaultMarkerNumber = nextDefaultMarkerNumber;
    }
}
