# Minimap Component Contract

This document defines the minimap as an isolated UI component, separate from an
inline HUD structure.

## Goal

The minimap should have one authoritative widget structure that can be understood,
tested, and rebuilt on its own.

## Authoritative Assets

Planned canonical component:

- `Assets/Minimap.uxml`
- `Assets/Minimap.uss`

`Assets/GameUI.uxml` hosts the minimap by composition and owns only its placement
inside the HUD.

## Ownership Split

### Minimap Component

Owns:
- internal visual hierarchy
- layer names
- frame, face, viewport, and marker layout
- minimap-only styling

### Outer HUD

Owns:
- where the minimap sits on screen
- whether the HUD is visible
- layout relationships with compass, hunger, and time UI

## `Minimap.uxml`

Owns:
- minimap root
- face layer
- live map layer
- frame layer
- player layer
- local helper containers such as mask or viewport containers

## `Minimap.uss`

Owns:
- minimap-only sizes
- minimap-only offsets
- minimap-only scale modes
- minimap-only masking and inset rules

## `GameUI.uxml`

Owns:
- the placement of a minimap instance in the HUD
- outer HUD composition

## Required Element Names

The canonical minimap component defines these names:

- `minimap-root`
- `minimap-face-layer`
- `minimap-circle-inner`
- `minimap-mask`
- `minimap-image`
- `minimap-frame-layer`
- `minimap-frame`
- `minimap-player-layer`
- `minimap-player`

If the names change, update this contract and the controller together.

## Controller Rules

`MinimapUIController` assumes one minimap widget exists and that its internal
structure matches `Minimap.uxml`. If component cloning or composition fails, fix
the integration path before restoring a second inline minimap structure.

## Integration Order

1. Make `Minimap.uxml` match the visual contract.
2. Make `Minimap.uss` match the pixel boxes.
3. Verify a standalone minimap instance.
4. Replace the inline minimap structure in `GameUI.uxml` with one component
   instance.
5. Repoint `MinimapUIController` if naming or root lookup requires it.

## Temporary State

The repo may temporarily contain both an old standalone `Minimap.uxml` and an
inline minimap in `GameUI.uxml`. The final target is one active minimap component
structure.

## Acceptance Criteria

The component boundary is correct when:

1. There is one authoritative minimap widget structure.
2. `GameUI.uxml` hosts the minimap without duplicating internals.
3. `Minimap.uxml` defines the minimap layer hierarchy.
4. `Minimap.uss` contains minimap-specific styling.
5. A minimap layer change can be made in one place.
