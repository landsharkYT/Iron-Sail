using System;
using UnityEngine;

[Serializable]
public struct TreasureIslandPlacement
{
    public bool isValid;
    public Vector2 center;
    public Vector2 radii;
    public float rotationDegrees;
    public Vector2[] voronoiPoints;
    public Vector2[] lobePoints;
    public float[] lobeRadii;
    public float lobeStrength;
    public float footprintRadius;
    public float exclusionRadius;
    public float normalizedRadius;
    public bool hasTarget;
    public Vector3Int targetCellA;
    public Vector3Int targetCellB;
    public Vector2 targetMidpointWorld;
    public Vector2 targetContactAnchorWorld;
    public Vector2 targetOutwardDirection;

    public float MaxRadius => Mathf.Max(Mathf.Max(radii.x, radii.y), footprintRadius);
}
