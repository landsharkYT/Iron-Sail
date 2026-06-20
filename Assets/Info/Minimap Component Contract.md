# Minimap Component Contract

This document defines how the minimap becomes an isolated UI component instead of an inline HUD improvisation.

The purpose of this phase is to make the minimap independently understandable, testable, and rebuildable.

## Problem Being Solved

Right now the minimap has multiple structural identities:

- an old standalone `Assets/Minimap.uxml`
- an inline live copy inside `Assets/GameUI.uxml`
- styling in `Assets/Minimap.uss`
- logic in `MinimapUIController`

That creates drift:
- one structure can be updated while the other is forgotten
- the controller may target a composition that no longer matches the real widget
- fixes become “edit wherever it currently seems to live”

This phase ends that ambiguity.

## Single Source of Truth

The minimap must have exactly one authoritative widget structure.

### Authoritative assets

Planned canonical component:
- `Assets/Minimap.uxml`
- `Assets/Minimap.uss`

### Non-authoritative container

`Assets/GameUI.uxml` must only host the minimap by composition, not by duplicating its internal structure.

In other words:
- `GameUI.uxml` places the minimap
- `Minimap.uxml` defines the minimap

## Component Boundary

The minimap component owns:
- its internal visual hierarchy
- its layer names
- its frame/face/viewport/marker layout
- its styling

The outer HUD owns:
- where the minimap sits on screen
- whether the HUD is visible
- broader layout relationships with compass/hunger/time UI

The minimap component must not require inline structure duplication to function.

## Intended Ownership Split

### `Minimap.uxml`

Owns:
- minimap root
- face layer
- live map layer
- frame layer
- player layer
- any local helper containers such as mask/viewport containers

Must not include:
- unrelated HUD widgets
- inventory
- world map overlay
- speed lines

### `Minimap.uss`

Owns:
- minimap-only sizes
- minimap-only offsets
- minimap-only scale modes
- minimap-only masking/inset rules

Must not own:
- general HUD layout
- right-side HUD stack layout
- world map overlay styling

### `GameUI.uxml`

Owns:
- the placement of a minimap instance in the HUD
- outer HUD composition

Must not own:
- minimap internal layers after the rebuild

## Required Structural Outcome

After the rebuild, the live HUD should conceptually look like:

1. `GameUI.uxml`
   - includes/instances minimap component once
   - positions it at the intended HUD location

2. `Minimap.uxml`
   - contains the full internal minimap widget hierarchy

3. `MinimapUIController`
   - targets only the minimap component's named elements

## Element Naming Contract

The canonical minimap component must define these names:

- `minimap-root`
- `minimap-face-layer`
- `minimap-circle-inner`
- `minimap-mask`
- `minimap-image`
- `minimap-frame-layer`
- `minimap-frame`
- `minimap-player-layer`
- `minimap-player`

These names belong to the minimap component contract, not to the outer HUD.

If the names change, the component contract must be updated first, and the controller must follow that contract.

## Controller Dependency Rules

`MinimapUIController` must assume:
- one minimap widget exists
- its internal structure matches `Minimap.uxml`

It must not assume:
- the minimap lives inline in `GameUI.uxml`
- duplicate fallback hierarchies exist

If the component fails to clone or compose, that is a widget integration issue, not a reason to re-inline the structure permanently.

## Integration Strategy

When the implementation phase happens, use this order:

1. Make `Minimap.uxml` match the Phase 1 visual contract exactly.
2. Make `Minimap.uss` match the Phase 1 pixel boxes exactly.
3. Make a standalone preview/test instance of the minimap component work.
4. Replace the inline minimap structure in `GameUI.uxml` with a single minimap component instance.
5. Repoint `MinimapUIController` only if naming or root lookup requires it.

Do not:
- edit both the standalone and inline versions in parallel
- leave two active minimap structures in the repo after the rebuild

## Temporary State vs Final State

### Temporary state now

Current repo state may contain:
- old `Minimap.uxml`
- live inline minimap in `GameUI.uxml`

That is acceptable only as transition debt.

### Final state target

Final desired state:
- one active minimap component structure
- no duplicated active minimap hierarchy

## Acceptance Criteria

Phase 4 is satisfied when these statements are true:

1. There is exactly one authoritative minimap widget structure.
2. `GameUI.uxml` no longer duplicates minimap internals.
3. `Minimap.uxml` alone defines the minimap layer hierarchy.
4. `Minimap.uss` contains only minimap-specific styling.
5. A minimap layer change can be made in one place without hunting through HUD markup.

## Guardrails For The Next Phase

When implementation resumes:

- do not keep both inline and standalone minimap structures alive as co-equal sources
- do not edit `GameUI.uxml` internals to fix minimap layer bugs unless the issue is component placement
- do not let the minimap component absorb world map overlay concerns

If an implementation step requires duplicating the minimap structure again, that is a sign the component boundary is being violated.
