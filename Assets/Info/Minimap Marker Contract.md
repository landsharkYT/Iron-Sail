# Minimap Marker Contract

This document isolates the player boat marker as its own minimap layer contract.

The purpose of this phase is to ensure the boat marker can be proven independently before it is debugged together with the live map, frame, and face.

## Problem Being Solved

The marker has repeatedly been debugged at the same time as:
- live minimap texture rendering
- frame readability
- face readability
- masking/layout changes

That makes a missing marker ambiguous:
- is it too small?
- is it hidden by the live map?
- is it not assigned?
- is its pivot wrong?
- is the render path wrong?
- is its layer behind something else?

This contract removes that ambiguity.

## Source Asset Facts

`BoatMapIndicator` source sprite:
- nominal sprite rect: `7 x 13`
- tiny authored art
- pixel-perfect look matters

This means the minimap marker must be treated as:
- a deliberately scaled-up authored icon
- not as a 1:1 world-scale or texture-native size

## Marker Role

The marker represents:
- the player boat
- current heading

It does not represent:
- velocity direction
- wake direction
- drift direction
- map cursor

## Marker Behavior Contract

### Position

The minimap player marker must:
- remain centered in the minimap widget at all times

It must not:
- translate with player position
- use normalized map-position placement inside the minimap

Reason:
- the minimap is a local viewport around the player
- the map content moves under the player marker, not the other way around

### Rotation

The marker must:
- rotate with boat heading only
- rotate around its own center

It must not:
- rotate with map orientation
- rotate around a corner
- inherit local drift offsets

### Visibility

The marker must:
- sit above the live map layer
- sit above the inner face
- sit above the frame if needed to preserve readability, though it should visually remain centered inside the face

The exact layer order is controlled by the minimap UI contract.

## Marker Box Contract

Primary target box:
- left: `91`
- top: `83`
- width: `18`
- height: `34`

This is the intended baseline scale-up from the `7x13` authored sprite.

Fallback verification box:
- left: `90`
- top: `81`
- width: `20`
- height: `38`

The fallback box exists only as a verification step if the baseline is too subtle to confirm visually.

## Render Path Contract

The marker uses the minimap rendering contract:
- `VisualElement`
- `StyleBackground(boatMarkerSprite)`

The marker must not switch render path independently from the minimap render contract unless that contract is explicitly revised.

## Isolation Verification Procedure

Before the marker is trusted in the full minimap widget, it must be proven in isolation.

Required verification order:

1. Show the minimap widget with:
   - frame
   - inner face
   - marker
   - no live map texture

2. Confirm the marker is:
   - visible
   - centered
   - upright at zero rotation

3. Rotate the marker only.

4. Confirm it rotates around center with no drift.

5. Only then re-enable the live minimap texture layer.

This procedure is mandatory for debugging.

## Marker-Only Failure Classification

If the marker is missing while the live map is disabled, the problem must be classified as one of:

1. asset assignment failure
2. layer order failure
3. box sizing/placement failure
4. sprite background rendering failure
5. tint/alpha failure

It must not be blamed on:
- discovery texture logic
- map crop logic
- world coordinate conversion

Those belong to different layers.

## What The Marker Must Read Like

Correct result:
- a small but clearly identifiable boat icon
- centered in the minimap
- rotating with heading

Incorrect results:
- tiny unreadable speck
- generic dot
- icon clipped awkwardly
- icon visually fused into the border
- icon apparently absent until zoomed in on screenshots

## Acceptance Criteria

This phase is satisfied when these statements are true:

1. The marker can be tested without the live map layer.
2. The marker can be centered and rotated without any map-data dependency.
3. Marker visibility issues can be diagnosed without touching the world map.
4. The marker has one baseline size box and one explicit fallback verification size.
5. The marker is no longer treated as “just another thing in the minimap stack.”

## Guardrails For The Next Phase

When implementation resumes:

- verify marker alone before live map
- do not simultaneously tune marker size and live map inset on the first pass
- if the marker fails in isolation, do not touch discovery logic

If the marker cannot be made clearly visible under this contract, revise the marker box first before revising the rendering model.
