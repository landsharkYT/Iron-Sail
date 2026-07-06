# Minimap Marker Contract

This document isolates the player boat marker as its own minimap layer.

## Purpose

The marker should be provable before it is debugged together with the live map,
frame, face, and mask.

## Source Asset Facts

`BoatMapIndicator` source sprite:

- nominal sprite rect: `7 x 13`
- tiny authored art
- pixel-perfect look matters

The minimap marker is a deliberately scaled-up authored icon.

## Marker Role

The marker represents:

- player boat
- current heading

Velocity, wake, drift, and cursor state belong to other systems.

## Position

The marker remains centered in the minimap widget at all times. The minimap is a
local viewport around the player, so map content moves under the player marker.

## Rotation

The marker rotates with boat heading around its own center.

## Visibility

The marker sits above the live map layer, inner face, and frame when needed for
readability.

## Marker Box

Primary target box:

- left: `91`
- top: `83`
- width: `18`
- height: `34`

Fallback verification box:

- left: `90`
- top: `81`
- width: `20`
- height: `38`

Use the fallback only if the baseline size is too subtle to verify visually.

## Render Path

The marker uses:

- `VisualElement`
- `StyleBackground(boatMarkerSprite)`

## Isolation Verification

Before the full minimap is trusted:

1. Show frame, inner face, and marker with the live map disabled.
2. Confirm the marker is visible, centered, and upright at zero rotation.
3. Rotate only the marker.
4. Confirm it rotates around center with no drift.
5. Re-enable the live minimap texture layer.

## Failure Routing

If the marker is missing while the live map is disabled, inspect:

1. asset assignment
2. layer order
3. box size and placement
4. sprite background rendering
5. tint and alpha

Discovery texture logic, map crop logic, and world coordinate conversion are
outside the marker layer.

## Acceptance Criteria

The marker is correct when:

1. It can be tested without the live map layer.
2. It can be centered and rotated without map data.
3. Visibility issues stay diagnosable inside the marker layer.
4. The baseline and fallback size boxes are documented.
