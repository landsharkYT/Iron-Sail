# 3. Save system: JsonUtility snapshot, monolithic typed root, scene-reload load

Date: 2026-06-19

## Status

Accepted

## Context

We are building exportable JSON Save Files that restore a session faithfully
("almost dead stays almost dead"). Three design choices had real alternatives.

**Serializer.** The project has no Newtonsoft; only Unity's built-in
`JsonUtility`, which cannot serialize dictionaries, `HashSet`s, polymorphic
lists, or properties. The target is desktop/itch.io with **WebGL possibly
relevant**, which forces IL2CPP/AOT. That is where Newtonsoft needs extra care
and `JsonUtility` stays simple. Factions (nested/dictionary-ish state) are on
the long-term roadmap and are the scenario that would most favour Newtonsoft.

**Assembly shape.** Either a dynamic registry of self-serializing sections
(open-ended, mod-friendly) or a monolithic typed root DTO composed of per-system
sub-DTOs. With `JsonUtility`, the registry approach forces escaped-JSON-string
payloads nested in JSON, which works against the "human-editable export" goal.

**Load model.** The World Seed must be applied *before* world generation runs:
`IslandGenerationController.Awake()` consumes a static seed override and locks the
seed; generation streams afterwards. The world is an infinite chunk stream inside
a finite gameplay boundary, so there is no one-shot "world ready" event.

## Decision

1. **`JsonUtility` behind an `ISaveSerializer` seam.** All serialization goes
   through one interface; today it is a `JsonUtility` implementation. The seam
   makes the choice swappable in a single file if faction migrations later
   justify Newtonsoft without touching any capture/restore code.
2. **Monolithic typed root `SaveFile`** composed of per-system `[Serializable]`
   sub-DTOs (`header`, `world`, `boat`, `progress`, `worldState`, `enemies[]`,
   `markers[]`, `metShopkeepers[]`). Each system owns `Capture()`/`Restore()` for
   its own DTO; `SaveController` assembles the root. Adding a section is additive.
3. **Load via scene reload.** `SaveController` (`DontDestroyOnLoad`) stashes the
   pending `SaveFile`, calls `SetDiagnosticPlaySeedOverride(seed, randomize:false)`,
   reloads the gameplay scene, then on scene-loaded restores the boat position
   first (so chunks stream around it) and applies the remaining sections.
4. **Stable string identities** for anything ScriptableObject-backed (enemy
   `configId`, `itemId`); seed-derived `ShopId`s for Met Shopkeepers.

## Consequences

- Dependency-free, AOT/WebGL-safe, human-editable exports.
- Per-section ownership keeps the root additive as enemies, bosses, flags, and
  factions arrive. The growing entity list is an architecture concern more than
  a serializer concern.
- Load reuses the exact fresh-boot path, leaning on the determinism guarantee
  (seed -> identical world) and avoids a runtime full-regeneration path.
- `JsonUtility` cannot distinguish "field absent" from "default", and welds JSON
  keys to C# field names. We accept this for now; the `header.version` int plus
  the serializer seam are the escape hatch if/when migrations get painful.
- File size at scale is addressed separately (gzip + map compaction), independent
  of the serializer.

## Alternatives considered

- **Newtonsoft now**: better dictionary ergonomics and migration control, with a
  new dependency and AOT/WebGL risk. Deferred behind the serializer seam.
- **Dynamic section registry**: useful for mod-authored save data, which is
  outside the current roadmap. Under `JsonUtility` it also produces nested-string
  JSON.
- **In-place restore without reload**: requires a runtime regeneration path and
  full teardown of spawned state. Reusing the boot path is smaller.
