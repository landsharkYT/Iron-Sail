# Map UI Architecture Contract

This document separates the shared map-data system from the two map views: the
HUD minimap and the full `M` map overlay.

## Core Principle

There is one shared world-map data model and two separate view products:

1. HUD minimap
2. Full world map overlay

They share discovery data, texture data, and coordinate helpers. Their viewport
behavior, marker behavior, layout, and layer composition stay separate.

## Product Definitions

### HUD Minimap

Purpose:
- immediate local navigation
- always visible with HUD unless hidden by `Tab`

Behavior:
- local crop around the player
- north-up
- centered player marker
- heading-only marker rotation
- live content scrolls beneath marker
- authored circular widget

Visual language:
- authored widget first
- live map second

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

Visual language:
- chart panel first
- world texture inside it

## System Roles

### `MapDiscoveryController`

Owns:
- map-cell world resolution
- discovery state
- discovered texture
- world-to-map conversion
- terrain color resolution
- border visibility in map data

Exposes:
- whole discovered texture
- normalized world position lookup
- local crop/blit helpers
- map cell lookup helpers

### `MinimapUIController`

Owns:
- minimap widget references
- local crop size/radius
- minimap refresh behavior
- centered marker rotation
- HUD visibility integration

Consumes:
- local crop data or local map view
- player world position conversion helpers when needed

### `WorldMapUIController`

Owns:
- full map overlay open/close
- cursor and input-blocking behavior
- full map panel layout
- whole-world texture presentation
- moving player marker position within world-map space

Consumes:
- whole discovered texture
- normalized player position in world map

## Spaces

### Shared Map Data Space

- finite square texture bounds
- coarse world-map resolution
- normalized coordinate helpers

### Minimap View Space

- crop window around player
- player locked to center
- content moves

### Full Map View Space

- full discovered texture
- player moves within it
- world extents define visible domain

## Acceptance Criteria

The boundary is healthy when:

1. The minimap can be described without full-map behavior.
2. The full map can be described without minimap crop behavior.
3. `MapDiscoveryController` can be described without UI layout.
4. Minimap framing bugs stay in minimap presentation code.
5. Full-map marker bugs stay in full-map presentation code.

## Change Rule

Classify every map change before editing:

- shared data concern
- minimap presentation concern
- full-map presentation concern

Only shared data helpers belong in `MapDiscoveryController`.
