# 4. The discovered map is reconstructed from the seed, not stored

Date: 2026-06-19

## Status

Accepted (Phase 1 and Phase 2 implemented)

## Context

The Save File must restore the player's discovered map. The naive options all
fail at this game's scale:

- The world is **~148 million tiles** (`playableRadiusTiles = 6000`, so ~12,200
  tiles per side). A fixed full-world bitmap is ~150 MB — infeasible.
- Storing the charted cells (even sparse, compressed pages) works but grows with
  exploration and bloats the save with data that is *derivable*.
- A fixed low-res raster (the mainstream fog-of-war approach) is O(1) but blurs
  away per-tile detail — and this map **zooms to per-tile detail** via
  `MapDiscoveryController.RenderViewport`, so docks/treasure (1-tile features)
  would be destroyed.

The decisive insight: the chart's colours/categories are a **deterministic
function of the World Seed**, which the Save File already stores. The only
player-unique information is *where the player revealed*. `IslandGeneration
Controller` already exposes a deterministic, stream-independent island query API
(`CollectAcceptedIslandSourcesNearWorldPosition`, `VisitAcceptedIslandTiles`,
`TryResolveAcceptedIslandTile`, `TryGetAcceptedIslandDockCells`), and the chart's
reveal flow funnels through a single replayable method,
`RevealAtWorldPosition(worldPosition)`.

## Decision

Persist only a **coarse discovery mask** — the set of revealed blocks (world
position snapped to a `blockSize` grid, `blockSize <= revealRadiusWorld`). On
load, **replay** `RevealAtWorldPosition` over the saved blocks; the chart
repaints from the seed. This is the information-theoretic floor: store the
player's footprint, regenerate everything else.

This is delivered in phases:

- **Phase 1:** capture/restore the revealed-block mask and replay it, spread over
  frames. Open water reconstructs immediately (the per-cell scan classifies an
  un-streamed cell as Water), and *loaded* islands stamp deterministically.

- **Phase 2 (implemented, smaller than first expected):** the investigation found
  there was no need for a new `TryClassifyCellFromSeed`. The stamp queue already
  paints island land/dock from the deterministic `TryResolveAcceptedIslandTile`
  regardless of streaming; the only blocker was a `IsAcceptedIslandCurrentlyLoaded`
  gate in `RevealIntersectingIslands` that skipped un-streamed islands. Phase 2
  bypasses that gate **only during mask replay** (a `maskReplayActive` flag), so
  distant islands/docks reconstruct from the seed at load. The gate is unchanged
  for live play.

- **Phase 2.1 (implemented):** treasure-target marker cells are stamped from the
  deterministic `TryGetTreasureTargetCells` once reconstruction fully drains
  (block replay + island stamping both empty), so the island stamp can't overwrite
  them as land. They are stamped only if the player had revealed that block, and
  only the 1–2 marker cells are affected.

- **Phase 3 (optional, not done):** a seed-keyed cache of the reconstructed
  preview texture to remove the brief "fills in on load" moment.

Known minor remaining gap: the boundary band still records via the live-tilemap
path, so it repaints when the player next streams that area rather than instantly
on load. Islands, docks, open water, and treasure markers all reconstruct on load.

## Consequences

- The map section of the save is tiny (a set of block coords, bounded by *area*
  explored, gzips to almost nothing) and keeps full per-tile zoom fidelity.
- Load incurs a reconstruction pass, batched over frames via the existing
  `pendingIslandStamps` / `pendingTiles` machinery; idempotent (already-charted
  cells/islands are skipped), so cost is bounded by area, not block count.
- Until Phase 2, a reloaded save shows charted islands/docks/treasure instantly
  but open-water fog repaints as the player revisits.
- The approach depends on the chart being fully world-derived. If a future
  feature writes non-seed-deterministic data into the chart, that part would not
  reconstruct and would need separate persistence.
- Replaying at block centres reveals marginally more at the edges than the exact
  path — an acceptable fog approximation.

## Alternatives considered

- **Store charted cells, gzip'd pages (Family A)** — simple, instant restore,
  faithful regardless of determinism, but a blob that grows with exploration and
  stores derivable data. Kept as the fallback if reconstruction proves fragile.
- **Fixed low-res raster (Family D)** — O(1) but destroys per-tile zoom detail
  (docks/treasure). Rejected for a zoomable map.
- **Sparse tile list (Family C)** — page-size-independent but bulkier than A.
