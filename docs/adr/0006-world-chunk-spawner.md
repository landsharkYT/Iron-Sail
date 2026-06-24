# 6. Obstacle spawners share a chunk-streaming base

Date: 2026-06-20

## Status

Accepted (Phase 1: Rock + Whirlpool migrated; Phase 2: Fishing pending)

## Context

`RockGenerationController`, `FishingSpotSpawner`, and the new `WhirlpoolSpawner`
all repeated the same chunk-streaming skeleton: a `Dictionary<Vector2Int,…>` of
loaded chunks, `Start`/`LateUpdate` driving a refresh, computing the required
chunk set from the camera, unloading chunks outside it, and generating new ones.
Only the *contents* of a chunk differ. With a third copy arriving (rule of three),
the duplication was worth removing.

The three are not identical. Rock and Whirlpool ride the **island chunk grid** and
are **stateless per chunk** (generate once, destroy on unload). Fishing uses its
**own grid + margin**, `Camera.main`, and a **stateful** per-frame lifecycle
(spot respawns, consumption, revalidation).

## Decision

Introduce a generic abstract base `WorldChunkSpawner<TChunk>` that owns the loaded
chunk dictionary and the load/unload diff against a required set. Subclasses
supply only what differs:

- `PrepareReferences()` — readiness gate.
- `CollectRequiredChunks(set)` — which chunks must exist (island grid, own grid, …).
- `GenerateChunk(coord) -> TChunk` — fill a chunk.
- `UnloadChunk(coord, chunk)` — tear it down.
- `TickLoadedChunks()` — optional per-frame hook for stateful spawners.

`TChunk` is the per-chunk payload (a root `GameObject` for the stateless spawners;
a richer state object for fishing).

Migration is phased because two of the three are working, shipped code:

- **Phase 1 (this change):** Whirlpool (new) and Rock (island-grid, stateless)
  migrate. Rock's payload `ChunkRockData` became `public` because a public class's
  base-type argument must be at least as accessible.
- **Phase 2 (pending):** Fishing migrates — its respawn/consume/revalidate state
  maps onto `TChunk` + `TickLoadedChunks`. It keeps its own grid+margin via
  `CollectRequiredChunks`. Deferred and given its own verification because it is
  the most complex and behaviour-sensitive.

## Consequences

- Adding a new obstacle type is now ~the placement loop plus four short overrides.
- Behaviour is preserved for Rock and Whirlpool; the streaming logic lives in one
  place. A minor improvement: the base retries `PrepareReferences` each frame, so
  a spawner whose dependencies aren't ready at `Start` recovers instead of staying
  dead.
- Inheritance (not composition) was chosen as the idiomatic fit for this
  MonoBehaviour-heavy codebase; Fishing's extra complexity is absorbed by the
  optional tick hook rather than forced into every subclass.
- Rock has no automated test, so its migration must be verified in-editor (rocks
  still generate near islands and unload off-camera).

## Alternatives considered

- **Composition (`ChunkStreamer` helper driven by callbacks)** — more flexible and
  unit-testable in isolation, but heavier wiring and less idiomatic here.
- **Base fits only the stateless spawners; Fishing stays separate** — rejected:
  the goal was to unify all three; the tick hook makes Fishing fit without
  contorting the others.
