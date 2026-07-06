# Minimap Masking Contract

This document defines the masking and viewport rules for the HUD minimap.

## Purpose

The mask crops the live map texture. The authored art layers define the minimap's
visual identity.

## Core Principle

Masking is a viewport constraint. The minimap should read this way:

- `MapCircle` defines the visible frame.
- `MapCircleInner` defines the visible face.
- The mask limits where the live map texture shows through.

## Mask Responsibility

The mask crops the live minimap texture to the intended circular viewport.

## Authored Layer Responsibility

These elements define the widget:

- `MapCircle`
- `MapCircleInner`
- `BoatMapIndicator`

They own the visible border, face, frame thickness, widget identity, and player
icon shape.

## Viewport Box

The live viewport box is fixed by the visual contract:

- left: `28`
- top: `28`
- width: `144`
- height: `144`

This box is inset relative to:

- frame box: `20,20,160,160`
- face box: `20,20,160,160`

The inset preserves border separation and leaves a visible face rim around the
live content.

## Mask Shape

The live map texture may be cropped to a circle using:

- rounded-corner clipping on the live viewport container
- a custom circular clip element later

The clip is part of the viewport, not the frame.

## Layer Relationship

Behind the mask:

- dynamic live map texture layer

Outside the mask:

- face layer
- frame layer
- player marker layer

The face and frame should remain meaningful even when the live map layer is
disabled.

## Isolation Test

1. Disable the live map texture layer.
2. Leave face and frame active.
3. Confirm the minimap still reads like a widget.

## Acceptance Criteria

The mask is correct when:

1. It affects only the live map texture layer.
2. The minimap still reads as a widget without the live map layer.
3. The visible edge comes from the frame and face art.
4. The live map remains visibly inset from the frame.
5. Masking bugs can be debugged without changing frame or marker semantics.
