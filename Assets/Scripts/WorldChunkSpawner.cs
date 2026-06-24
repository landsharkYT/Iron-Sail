using System.Collections.Generic;
using UnityEngine;

// Shared chunk-streaming skeleton for obstacle spawners (see ADR 0006).
// Owns the loaded-chunk dictionary and the load/unload diff against the set of
// chunks that currently need to exist. Subclasses supply only what differs:
// which chunks are required, how to fill one, and how to tear one down. A
// per-frame TickLoadedChunks hook supports stateful spawners (e.g. fishing
// respawns) without forcing that complexity on stateless ones (rocks, whirlpools).
//
// TChunk is the per-chunk payload — typically a root GameObject, but anything the
// subclass needs to remember and clean up on unload.
public abstract class WorldChunkSpawner<TChunk> : MonoBehaviour
{
    readonly Dictionary<Vector2Int, TChunk> loadedChunks = new Dictionary<Vector2Int, TChunk>();
    readonly HashSet<Vector2Int> requiredChunks = new HashSet<Vector2Int>();
    readonly List<Vector2Int> unloadScratch = new List<Vector2Int>();

    protected IReadOnlyDictionary<Vector2Int, TChunk> LoadedChunks => loadedChunks;
    protected int LoadedChunkCount => loadedChunks.Count;

    protected virtual void Start()
    {
        if (PrepareReferences())
            RefreshChunks();
    }

    protected virtual void LateUpdate()
    {
        if (!PrepareReferences())
            return;

        RefreshChunks();
        TickLoadedChunks();
    }

    void RefreshChunks()
    {
        requiredChunks.Clear();
        CollectRequiredChunks(requiredChunks);

        unloadScratch.Clear();
        foreach (KeyValuePair<Vector2Int, TChunk> pair in loadedChunks)
        {
            if (!requiredChunks.Contains(pair.Key))
                unloadScratch.Add(pair.Key);
        }

        for (int i = 0; i < unloadScratch.Count; i++)
        {
            Vector2Int coord = unloadScratch[i];
            if (loadedChunks.TryGetValue(coord, out TChunk chunk))
            {
                UnloadChunk(coord, chunk);
                loadedChunks.Remove(coord);
            }
        }

        foreach (Vector2Int coord in requiredChunks)
        {
            if (!loadedChunks.ContainsKey(coord))
                loadedChunks.Add(coord, GenerateChunk(coord));
        }
    }

    // Convenience for the common case of a rectangular required-chunk region.
    protected static void AddRectChunks(RectInt rect, HashSet<Vector2Int> into)
    {
        for (int y = rect.yMin; y < rect.yMax; y++)
        {
            for (int x = rect.xMin; x < rect.xMax; x++)
                into.Add(new Vector2Int(x, y));
        }
    }

    // Returns false until the spawner has every reference it needs to run.
    protected abstract bool PrepareReferences();

    // Fill the set with every chunk that should currently be loaded.
    protected abstract void CollectRequiredChunks(HashSet<Vector2Int> into);

    // Build a chunk's contents and return its payload.
    protected abstract TChunk GenerateChunk(Vector2Int coord);

    // Tear down a chunk that has left the required set.
    protected abstract void UnloadChunk(Vector2Int coord, TChunk chunk);

    // Per-frame hook for stateful chunks. No-op by default.
    protected virtual void TickLoadedChunks() { }
}
