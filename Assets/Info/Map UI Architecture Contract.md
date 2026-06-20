# Map UI Architecture Contract

This document defines the architectural separation between the shared map-data system, the HUD minimap, and the full `M` map overlay.

The purpose of this phase is to stop treating the minimap and full map as the same product with different sizes.

## Core Principle

There is one shared world-map data model and two different view products:

1. HUD minimap
2. Full world map overlay

They may share:
- discovery data
- world-map texture data
- coordinate conversion helpers

They must not share:
- presentation assumptions
- viewport behavior
- marker behavior
- layer composition rules

## Product Definitions

### HUD Minimap

Purpose:
- immediate local navigation aid
- always visible with HUD unless hidden by `Tab`

Behavior:
- local crop around the player
- north-up
- player marker remains centered
- player marker rotates only
- live content scrolls beneath marker
- rendered inside authored circular widget

Visual language:
- authored widget first
- live map second

This is not a “small full map.”

### Full Map Overlay

Purpose:
- planning and world understanding
- opened with `M`
- input-blocking overlay

Behavior:
- whole finite discovered world shown at once
- world extents visible
- undiscovered interior remains gray
- player marker moves within world space
- player marker rotates with heading
- map panel may clamp layout, but it is still a whole-world view

Visual language:
- simple chart panel first
- world texture inside it

This is not a “big minimap.”

## System Roles

### `MapDiscoveryController`

Role:
- authoritative shared map data service

Owns:
- coarse map-cell world resolution
- discovery state
- discovered texture
- world-to-map coordinate conversion
- terrain color resolution into map cells
- border visibility in map data

May expose:
- whole discovered texture
- normalized world position lookup
- local crop/blit helpers
- map cell lookup helpers

Must not own:
- minimap frame composition
- minimap marker sizing
- full map panel layout
- HUD visibility/input rules

In short:
- data authority only

### `MinimapUIController`

Role:
- minimap presentation controller only

Owns:
- minimap widget references
- minimap local crop size/radius
- minimap refresh behavior
- centered player marker rotation
- HUD visibility integration

Consumes from `MapDiscoveryController`:
- local crop data or local map view
- player world position conversion helpers if needed

Must not own:
- whole-world map layout
- full-map overlay visibility
- full-map player marker behavior

### `WorldMapUIController`

Role:
- full map presentation controller only

Owns:
- world map overlay open/close
- cursor/input-blocking behavior
- full map panel layout
- whole-world texture presentation
- moving player marker position within world map

Consumes from `MapDiscoveryController`:
- whole discovered texture
- normalized player position in world map

Must not own:
- minimap crop logic
- minimap frame/inner-face composition
- minimap marker centering rules

## Shared Data vs Separate Presentation

### Shared

- discovered world texture
- map color model
- world radius/bounds conversion
- reveal/discovery cadence

### Separate

- viewport selection
- mask/inset layout
- marker placement model
- widget hierarchy
- open/close behavior

If a future change affects both minimap and full map, it must be classified first:

- shared data concern
or
- separate view concern

It must not be changed in both places by reflex.

## Coordinate Contracts

### Shared Map Data Space

One canonical coarse world-map space:
- finite
- square texture bounds
- normalized coordinate helpers allowed

### Minimap View Space

Derived local space:
- crop window around player
- player locked to center
- content moves

### Full Map View Space

Derived whole-world space:
- full discovered texture
- player moves within it
- world extents define visible domain

These are three different spaces.

They must not be conflated.

## Rendering Contracts

### Minimap Rendering Contract

Exactly one dynamic image layer:
- the local crop texture

Everything else is authored UI:
- frame
- inner face
- player icon

If minimap work requires changing whole-world layout assumptions, the boundary has been violated.

### Full Map Rendering Contract

Exactly one dynamic image layer:
- the whole discovered world texture

Player marker is a separate UI layer.

If full-map work requires inheriting minimap masking/frame assumptions, the boundary has been violated.

## Current Architectural Smells To Avoid

These are the patterns that caused drift:

1. Using the same “crop/view” mental model for both map products.
2. Letting minimap styling decisions influence full-map layout.
3. Letting full-map marker logic influence minimap marker logic.
4. Treating authored minimap art as optional decoration rather than structural UI.
5. Switching between UI rendering approaches mid-debug without locking which controller owns what.

## Controller Upgrade Path

This is the intended end-state after future phases.

### Stable shared layer
- `MapDiscoveryController`

### Stable minimap layer
- `MinimapUIController`
- optional `Minimap.uxml` / `Minimap.uss`
- optional dedicated `MinimapViewElement` later if masking needs custom drawing

### Stable full-map layer
- `WorldMapUIController`
- optional dedicated chart UXML/USS later

## Acceptance Criteria

Phase 2 is satisfied when these statements are true:

1. The minimap can be described without mentioning full-map behavior.
2. The full map can be described without mentioning minimap crop behavior.
3. `MapDiscoveryController` can be described without mentioning UI layout.
4. A bug in minimap framing does not require changing full-map viewport logic.
5. A bug in full-map marker placement does not require changing minimap widget composition.

## Implementation Guardrails

Until the rebuild is complete:

- do not add new shared helpers unless they are genuinely data-space helpers
- do not let `MinimapUIController` query full-map panel state beyond simple visibility suppression
- do not let `WorldMapUIController` reuse minimap crop logic
- do not change `MapDiscoveryController` UI responsibilities upward

If a future change feels like “just one more shared convenience,” it probably belongs in the wrong layer.
