using System;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Tilemap))]
[RequireComponent(typeof(TilemapCollider2D))]
public class TreasureTargetController : MonoBehaviour
{
    public static TreasureTargetController ActiveInstance { get; private set; }

    public static event Action TreasureReached;

    [Header("References")]
    [SerializeField] IslandGenerationController islandGenerationController;
    [SerializeField] Tilemap goldTilemap;
    [SerializeField] Collider2D boatCollider;
    [SerializeField] Rigidbody2D boatRigidbody;

#pragma warning disable CS0414
    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] bool debugHasTarget;
    [SerializeField] bool debugTreasureReached;
    [SerializeField] Vector3Int debugTargetCellA;
    [SerializeField] Vector3Int debugTargetCellB;
    [SerializeField] Vector2 debugTargetAnchor;
#pragma warning restore CS0414

    TilemapCollider2D goldTilemapCollider;
    bool hasReachedTreasure;
    bool warnedMissingBoatOwnershipReference;

    public bool HasReachedTreasure => hasReachedTreasure;

    void OnEnable()
    {
        ActiveInstance = this;
        ResolveReferences();
        EnsureTriggerSurface();
        RefreshDebugTargetState();
    }

    void Start()
    {
        ResolveReferences();
        EnsureTriggerSurface();
        RefreshDebugTargetState();
    }

    void OnDisable()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;
    }

    public bool IsTreasureCell(Vector3Int cell)
    {
        if (islandGenerationController == null)
            return false;

        return islandGenerationController.IsTreasureTargetCell(cell);
    }

    void ResolveReferences()
    {
        if (goldTilemap == null)
            goldTilemap = GetComponent<Tilemap>();

        if (islandGenerationController == null)
            islandGenerationController = FindAnyObjectByType<IslandGenerationController>();

        if (boatRigidbody == null)
        {
            BoatController boatController = FindAnyObjectByType<BoatController>();
            if (boatController != null)
                boatRigidbody = boatController.GetComponent<Rigidbody2D>();
        }

        if (boatCollider == null)
        {
            if (boatRigidbody != null)
                boatCollider = boatRigidbody.GetComponent<Collider2D>();

            if (boatCollider == null)
            {
                BoatHealthController boatHealthController = FindAnyObjectByType<BoatHealthController>();
                if (boatHealthController != null)
                    boatCollider = boatHealthController.GetComponent<Collider2D>();
            }
        }
    }

    void EnsureTriggerSurface()
    {
        if (goldTilemap == null)
            return;

        if (goldTilemapCollider == null)
            goldTilemapCollider = goldTilemap.GetComponent<TilemapCollider2D>();
        if (goldTilemapCollider == null)
            goldTilemapCollider = goldTilemap.gameObject.AddComponent<TilemapCollider2D>();

        goldTilemapCollider.isTrigger = true;
        goldTilemapCollider.compositeOperation = Collider2D.CompositeOperation.None;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        TryHandleTreasureTouch(other);
    }

    void OnTriggerStay2D(Collider2D other)
    {
        TryHandleTreasureTouch(other);
    }

    void TryHandleTreasureTouch(Collider2D other)
    {
        if (hasReachedTreasure || other == null)
            return;

        ResolveReferences();
        RefreshDebugTargetState();
        if (!debugHasTarget)
            return;

        if (boatRigidbody == null && boatCollider == null)
        {
            if (!warnedMissingBoatOwnershipReference)
            {
                Debug.LogWarning("[TreasureTargetController] Treasure touch ignored because no boat collider/rigidbody could be resolved.", this);
                warnedMissingBoatOwnershipReference = true;
            }

            return;
        }

        Rigidbody2D otherBody = other.attachedRigidbody;
        if (boatRigidbody != null && otherBody != boatRigidbody)
        {
            if (boatCollider == null || !other.transform.IsChildOf(boatRigidbody.transform))
                return;
        }

        if (boatCollider != null
            && other != boatCollider
            && !other.transform.IsChildOf(boatCollider.transform)
            && (boatRigidbody == null || !other.transform.IsChildOf(boatRigidbody.transform)))
        {
            return;
        }

        hasReachedTreasure = true;
        debugTreasureReached = true;
        Debug.Log("[TreasureTargetController] Treasure reached. Emitting TreasureReached event.", this);
        TreasureReached?.Invoke();
    }

    void RefreshDebugTargetState()
    {
        debugHasTarget = false;
        debugTargetCellA = default;
        debugTargetCellB = default;
        debugTargetAnchor = Vector2.zero;

        if (islandGenerationController == null)
            return;

        if (!islandGenerationController.TryGetTreasureTargetCells(out Vector3Int cellA, out Vector3Int cellB))
            return;
        if (!islandGenerationController.TryGetTreasureTargetContactAnchor(out Vector2 anchor))
            return;

        debugHasTarget = true;
        debugTargetCellA = cellA;
        debugTargetCellB = cellB;
        debugTargetAnchor = anchor;
    }
}
