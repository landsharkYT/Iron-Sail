# 2. A single World Seed reproduces the world; Live Entities are saved, not seeded

Date: 2026-06-19

## Status

Accepted

## Context

We are building toward an exportable JSON save that restores a session faithfully
("a player who is almost dead should be almost dead"). That requires deciding,
for each part of the world, whether it is *reproduced* from a seed or *persisted*
as explicit state.

The codebase had three different determinism regimes:

- **Islands, rocks, shops** — already deterministic from
  `IslandGenerationController.Seed`. Rocks derive from island position; shops
  combine the same seed per shop. This seed is the de-facto World Seed.
- **Fishing spots** — chunk-deterministic, but keyed off an *independent*
  `debugSessionSeed` that is randomized per session, so they did not reproduce
  from the World Seed.
- **Night enemies** — spawned on a tick, at night, around the boat's *live*
  position, using the shared `UnityEngine.Random`. Their existence depends on the
  player's path and timing, not on world layout.

Two questions had to be answered: what is the authoritative seed, and how do
Night Enemies survive a reload?

## Decision

**There is one World Seed**, owned by island generation. Every static world
feature is reproduced from it. Fishing spots are changed to derive their base
seed from the World Seed (read lazily, after island generation resolves its
possibly-randomized seed) instead of an independent session seed.

**Night Enemies are Live Entities: persisted, not seeded.** Two parts:

1. *Isolated RNG now.* The spawner draws from its own `System.Random` seeded from
   the World Seed rather than the shared `UnityEngine.Random`, so enemy spawns
   can no longer perturb the RNG of seeded systems. This does **not** make
   encounters reproducible, and is not intended to.
2. *Per-entity persistence later.* In the save, each active enemy stores
   **position + current health + type**. Type is keyed by a new stable string
   `id` on `NightEnemyConfig` (not asset name, not list index). Restoring partial
   health needs a new seam on `NightEnemyHealth`, since spawning currently forces
   max health.

## Consequences

- The world (islands, rocks, shops, fishing spots) is fully reproducible from a
  single integer in the save file.
- Enemy encounters are inherently path-dependent and will never be regenerated;
  the save carries the actual live enemies. Velocity and AI state (aggro, target,
  knockback) are treated as transient and re-derive on load — only position,
  health, and type are persisted.
- Enemy type identity is decoupled from asset names and array order, so renaming
  a config or reordering the spawner list does not corrupt existing saves.
- A small amount of new surface is required before the save lands: a stable `id`
  on `NightEnemyConfig` and a set-current-health seam on `NightEnemyHealth`.

## Alternatives considered

- **Reproducible enemy encounters from the seed** — rejected. Spawns depend on
  the boat's trajectory and timing, so reproducing them is a simulation-replay
  problem: large, fragile, and pointless once enemies are persisted directly.
- **Identify enemy type by asset name or list index** — rejected. Both silently
  break old saves on a rename or reorder, with no compile-time error.
