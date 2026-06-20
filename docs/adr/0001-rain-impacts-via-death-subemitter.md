# 1. Rain Impacts via a native death sub-emitter

Date: 2026-06-19

## Status

Accepted

## Context

Rainfall produces two visuals: falling rain streaks and Rain Impacts (the
ripple where a drop lands). We want each Rain Impact to appear at the exact
point a falling drop terminates, so a drop visibly "hits the water", and we want
only a budgeted fraction of drops to produce one.

The Iron Sail runs many systems on the main thread alongside rain — wind,
day/night, enemies, water chunks, hunger. Rain must not steal frame time from
them. Falling rain emits up to ~700 particles/second (cap 1200), so however
impacts are coupled to drop deaths runs at high frequency.

Three couplings were possible:

1. **Decoupled spray** (the original code): impacts emitted at random positions,
   unrelated to where any drop lands. Cheap-ish but the ripples never line up
   with a streak, so drops don't read as "landing".
2. **Managed `OnParticleDeath` callback**: query dying particles in C# each
   frame and `Emit` an impact at each landing point. Fully flexible, but Unity
   marshals the particle buffer into a managed array every frame and we allocate
   and loop per landing — per-frame managed work and GC pressure on the same
   thread as everything else.
3. **Native death sub-emitter**: Unity's `SubEmitter` module spawns the impact
   system on a falling drop's death, entirely in native code, with a built-in
   `emitProbability` for the "only some drops land" fraction.

## Decision

Use a **native death sub-emitter** (`ParticleSystemSubEmitterType.Death`) with
`emitProbability ≈ 0.25` to spawn Rain Impacts at the landing point of falling
drops. Inherit nothing — impacts use their own start size/lifetime/colour. The
hand-rolled `EmitRainImpacts` loop and its `impactAccumulator` bookkeeping are
deleted.

The landing fraction comes from `emitProbability`, not from a second emission
stream, so impact count is a fraction of drop deaths and stays inside the same
budget. Everything remains gated on `WeatherIntensity`: no Rainfall means no
falling drops, hence no deaths and no impacts — zero cost when it isn't raining.

## Consequences

- Net main-thread cost **drops** versus the original code: the per-frame
  accumulator math, `Random.Range` calls, `EmitParams` struct and up-to-24
  `Emit` calls per frame are gone, replaced by a probability the native
  simulation handles for free.
- Rain Impacts are now conceptually tied to falling drops (see `CONTEXT.md`):
  every ripple marks a real landing, never ambient decoration.
- Less positional flexibility than the managed callback — impacts spawn where
  drops die, and "where they die" is tuned via the falling lifetime spread
  rather than chosen per-impact. Accepted, because that is exactly the desired
  behaviour and it costs nothing.
- Intensity-scaled impact alpha is no longer applied per emit; fade in/out
  instead comes from falling-drop frequency scaling with intensity.

### Sub-emitter gotchas (all "wired but silently does nothing")

Three conditions must all hold or impacts never appear, with no error logged:

1. **Hierarchy** — the impact system must be a *child* of the falling system it
   sub-emits from. A sibling reference is silently ignored.
2. **Emission burst** — a death sub-emitter spawns the count defined by the
   sub-emitter's emission *bursts*, not its rate-over-time. With no burst, every
   landing triggers the sub-emitter yet emits zero particles. We set one burst
   of 1, rate 0, and the system never self-plays (`loop`/`playOnAwake` off, no
   `Play()` call).
3. **Sorting** — impacts must render above the water surface (sorting order 0)
   or they are hidden behind it. They sit at order 2, below the falling rain.

## Alternatives considered

- **Managed `OnParticleDeath`** — rejected for per-frame marshalling and GC on a
  contended main thread.
- **Keep the decoupled random spray** — rejected because ripples never align
  with where drops land, so the "hitting the water" effect is only half-sold.
