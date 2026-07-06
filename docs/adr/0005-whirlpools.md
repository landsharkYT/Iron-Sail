# 5. Whirlpools: emergent-escape force field, deterministic strait-bound spawning

Date: 2026-06-20

## Status

Accepted (design; implementation pending)

## Context

Whirlpools are a new sea hazard (Medium and Large prefabs, circle trigger
colliders r=3 and r=5). They must pull the boat toward their centre in a vortex,
deal damage over time, and be escapable "only by going full speed with the wind".
Two decisions here are non-obvious and worth recording.

**How does escape work?** The boat is already fully force-based: `HandleWindForce`
does `rb.AddForce(forward * maxSailForce * windEfficiency * windSpeed * sail)`,
where efficiency falls off as the heading deviates from the wind. A reader
implementing "escape only with the wind" would reasonably reach for a scripted
gate (hold the boat until `SpeedFraction`/`WindEfficiency` cross thresholds, then
release).

**Where do whirlpools come from?** The spawn could be dynamic and random (like
`NightEnemySpawner`) or deterministic and chunk-based (like
`RockGenerationController` / `FishingSpotSpawner`).

## Decision

**Escape comes from the force field.** The whirlpool adds an
inward pull plus a tangential swirl (a logarithmic-spiral field) every
`FixedUpdate` while the boat is in the trigger, with a rim-weak / centre-strong
falloff. Escape happens purely because the boat's *maximum* wind thrust can beat
the pull only at near-full `WindEfficiency`, roughly downwind. A weak or adverse
wind can make a Whirlpool lethal; the mitigation is environmental: turn, wait
for the cycling wind, then sail with it.

**Whirlpools are deterministic, strait-bound World Features.** They spawn
chunk-based and hashed from the World Seed (mirroring rocks/fishing spots),
biased to the water gaps between two nearby islands, modelling tidal races,
which is how real maelstroms form. Because they are reproducible from the seed
they are reproducible from the Save File's World Seed (consistent with ADR
0002/0004); a loaded world regenerates them. Open-ocean spawning is left out in v1
to keep them rare and keep the "straits are dangerous" reading clear.

## Consequences

- "Escape with the wind" means a directional force balance in existing physics.
- Tuning is load-bearing: max wind thrust should beat the pull, while a weak or
  misaligned approach should remain dangerous. `maxPullForce`, `swirlRatio`,
  radius, and rim/eye damage rates are per-prefab knobs.
- Whirlpools are learnable, chartable, avoidable hazards that need no save data.
- DoT is depth-scaled (light rim, heavier eye) on the low side, attributed to a
  new `BoatDamageSource.Whirlpool`; tick audio reuses the collision clips through
  `BoatHitAudio` with a separate longer cooldown.
- The visual is a spiral `ParticleSystem` the controller **creates in code** (like
  `RainVisualController`), rendering white `Sprites/Default` billboard squares that
  match the wave particles, on the same water sorting layer, so it needs no prefab
  or material wiring. It scales with the collider radius; swirl appearance is tuned
  independently of the physics `swirlRatio`. (This supersedes an earlier note that
  authored the system on the prefab.)

## Alternatives considered

- **Scripted escape gate**: a hold-then-release on speed/efficiency thresholds.
  This would feel binary and would fight the otherwise physical boat.
- **Dynamic random spawning**: easy to add, but it loses world reproducibility and
  makes whirlpools less learnable.
- **Uniform-random deterministic placement**: reproducible, but geographically
  arbitrary. It loses the strait-hazard gameplay.
