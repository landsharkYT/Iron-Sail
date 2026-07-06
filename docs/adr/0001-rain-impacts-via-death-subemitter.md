# 1. Rain Impacts via a native death sub-emitter

Date: 2026-06-19

## Status

Accepted

## Context

Rainfall produces two visuals: falling rain streaks and Rain Impacts (the
ripple where a drop lands). We want each Rain Impact to appear at the exact
point a falling drop terminates, so a drop visibly "hits the water", and we want
only a budgeted fraction of drops to produce one.

The Iron Sail runs many systems on the main thread alongside rain: wind,
day/night, enemies, water chunks, and hunger. Rain needs a small frame-time
footprint. Falling rain emits up to ~700 particles/second (cap 1200), so the
drop-to-impact coupling runs at high frequency.

Three couplings were possible:

1. **Decoupled spray** (the original code): impacts emitted at random positions,
   unrelated to where any drop lands. Cheap-ish, but the ripples rarely line up
   with a streak, so drops read more like ambience than water contact.
2. **Managed `OnParticleDeath` callback**: query dying particles in C# each
   frame and `Emit` an impact at each landing point. Fully flexible, but Unity
   marshals the particle buffer into a managed array every frame and we allocate
   and loop per landing, adding per-frame managed work and GC pressure on the same
   thread as everything else.
3. **Native death sub-emitter**: Unity's `SubEmitter` module spawns the impact
   system on a falling drop's death, entirely in native code, with a built-in
   `emitProbability` for the "only some drops land" fraction.

## Decision

Use a **native death sub-emitter** (`ParticleSystemSubEmitterType.Death`) with
`emitProbability ≈ 0.25` to spawn Rain Impacts at the landing point of falling
drops. Impacts use their own start size, lifetime, and colour. The hand-rolled
`EmitRainImpacts` loop and its `impactAccumulator` bookkeeping are deleted.

The landing fraction comes from `emitProbability`, so impact count stays tied to
drop deaths and inside the same budget. Everything remains gated on
`WeatherIntensity`: no Rainfall means no falling drops, no deaths, and no impact
work.

## Consequences

- Net main-thread cost **drops** versus the original code: the per-frame
  accumulator math, `Random.Range` calls, `EmitParams` struct and up-to-24
  `Emit` calls per frame are gone, replaced by a probability the native
  simulation handles for free.
- Rain Impacts are now conceptually tied to falling drops (see `CONTEXT.md`):
  every ripple marks a real landing.
- Impacts spawn where drops die. The falling lifetime spread controls those
  landing points, which is enough for the desired visual and keeps the work in
  native particle simulation.
- Intensity-scaled impact alpha is no longer applied per emit; fade in/out comes
  from falling-drop frequency scaling with intensity.

### Sub-emitter gotchas

Three conditions must all hold for impacts to appear:

1. **Hierarchy**: the impact system must be a *child* of the falling system it
   sub-emits from. A sibling reference is silently ignored.
2. **Emission burst**: a death sub-emitter spawns the count defined by the
   sub-emitter's emission *bursts*, not its rate-over-time. With no burst, every
   landing triggers the sub-emitter yet emits zero particles. We set one burst
   of 1, rate 0, and the system stays idle (`loop`/`playOnAwake` off, no
   `Play()` call).
3. **Sorting**: impacts must render above the water surface (sorting order 0)
   or they are hidden behind it. They sit at order 2, below the falling rain.

## Alternatives considered

- **Managed `OnParticleDeath`**: offers full control, but adds per-frame
  marshalling and GC on a contended main thread.
- **Keep the decoupled random spray**: cheap, but the ripples rarely
  align with drop landings, so the effect reads weaker.
