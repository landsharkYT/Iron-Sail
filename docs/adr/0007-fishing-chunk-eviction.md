# 7. Distant retained fishing chunks are evicted

Date: 2026-06-24

## Status

Accepted

## Context

`FishingSpotSpawner` retains a `ChunkState` for every chunk the player has ever
visited (`chunkStates` is only cleared on disable). Retention exists so that a
depleted fishing spot stays depleted, with its respawn cooldown, when the
player sails away and back. The set grows across long voyages in a
6000-tile-radius world, creating a slow memory climb. (The related
per-frame full-scan cost was already removed by scoping revalidation to loaded
chunks.)

Two facts make dropping distant state safe:

- **Layout is deterministic.** A chunk's spot layout is generated from the World
  Seed (`CreateChunkRandom(chunkCoord, 0)`), so an evicted chunk regenerates its
  layout identically on revisit. Only the *mutable* depletion state (which spots
  are currently fished, their cooldowns, and time-salted re-rolled respawn
  positions) is lost.
- **Depletion is already ephemeral.** Fishing depletion is outside the Save File,
  so save/load already resets it. Eviction extends the same property to
  long-distance travel.

The only real risk is an exploit: fish a spot, leave, return before its 10s
cooldown, and find it active again.

## Decision

Run a throttled, distance-based eviction sweep (~every 2s): drop any retained
chunk that is currently unloaded and whose centre is beyond
`evictionRadiusWorld` (default 256 world units) from the **boat**.

The radius is chosen to sit well outside the loaded+margin region, so a short
back-and-forth still finds spots depleted, and the unloaded-chunk gate keeps
active chunks in memory. At that distance a round trip far exceeds the 10s
cooldown, so the spot would have respawned anyway. The eviction decision is a
pure, unit-tested predicate
(`IsChunkEvictable`).

## Consequences

- The retained set is bounded to a disc around the player; memory no longer grows
  with total distance travelled.
- A spot that had respawned at a *re-rolled* (time-salted) position and is then
  evicted returns at its deterministic initial position on revisit. It can
  appear to have "moved". Given the distance and elapsed time this is judged
  unnoticeable.
- Behaviour is unchanged within the eviction radius, so normal play (fishing an
  area, circling back) is unaffected.
- The sweep is O(retained) but throttled, so it does not reintroduce the per-frame
  cost that scoping revalidation to loaded chunks removed.

## Alternatives considered

- **Count-cap / LRU**: a hard absolute memory ceiling regardless of geometry, but
  the cap is an arbitrary number instead of a spatial intuition and needs
  last-access bookkeeping. A reasonable future backstop; not needed first.
- **Persist depletion rather than evicting**: would make spots survive correctly
  at any distance, but adds save surface to a system that currently treats
  depletion as temporary.
- **Do nothing**: acceptable for short sessions, risky for long voyages.
