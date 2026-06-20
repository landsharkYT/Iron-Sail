# Minimap Rendering Contract

This document locks the rendering approach for the minimap widget.

The purpose of this phase is to stop switching between multiple UI Toolkit rendering paths while debugging the same widget.

From this point forward, the minimap gets one approved rendering model.

## Problem This Fixes

The minimap has previously drifted through several rendering approaches:

- `VisualElement` background sprites
- `Image` elements with `sprite`
- `Image` elements with raw `texture`
- generic rounded-corner crop behavior standing in for authored layout

That made it hard to know whether a failure was caused by:
- asset assignment
- clipping behavior
- scale mode behavior
- sprite-vs-texture rendering
- layer ordering

This contract removes that ambiguity.

## Chosen Rendering Model

### Allowed model

The HUD minimap uses:

1. `VisualElement` layered widget structure
2. authored art assigned through `StyleBackground(sprite)`
3. exactly one dynamic texture layer for the live local map view

In other words:
- face = authored sprite background
- frame = authored sprite background
- player icon = authored sprite background
- live map = generated `Texture2D` background

## Layer-by-Layer Rendering Rules

### `minimap-face-layer`

Type:
- `VisualElement`

Background source:
- `StyleBackground(mapCircleInnerSprite)`

Purpose:
- static authored face art

Must not:
- host dynamic texture generation
- act as the crop implementation

### `minimap-live-map-layer`

Type:
- `VisualElement` inside the minimap mask container

Background source:
- `StyleBackground(minimapTexture)`

Purpose:
- the only dynamic visual layer in the minimap

Must:
- render the local crop from discovered-map data
- be inset according to the UI contract

Must not:
- own frame art
- own player icon art
- leak outside the viewport bounds

### `minimap-frame-layer`

Type:
- `VisualElement`

Background source:
- `StyleBackground(mapCircleSprite)`

Purpose:
- visible border ring

Must not:
- be replaced by CSS-like border styling
- be hidden under the live map

### `minimap-player-layer`

Type:
- `VisualElement`

Background source:
- `StyleBackground(boatMarkerSprite)`

Purpose:
- static centered layer with rotation only

Must:
- stay centered in the widget
- rotate around its center

Must not:
- use moving percentage-based position
- use raw texture if sprite background works

## Masking Rules

### What masking is allowed to do

Masking may:
- crop the dynamic live-map layer to the intended circular viewport

### What masking is not allowed to define

Masking must not be the primary source of:
- the border appearance
- the face appearance
- the widget identity

The authored assets define the look.
Masking only enforces where the dynamic content is visible.

## Banned Rendering Alternatives

The following are explicitly disallowed for the minimap unless this contract is updated first:

1. Replacing the widget with a single `Image` element.
2. Using the same rendering path as the full world map.
3. Treating `MapCircleInner` as the live texture itself.
4. Using a raw `Texture2D` for frame or player icon unless sprite background assignment has been proven impossible.
5. Using generic border radius as the main visual circle solution.
6. Mixing `Image.sprite`, `Image.texture`, and `StyleBackground(sprite)` for the same logical layer during the same rebuild pass.

## Why This Model Was Chosen

This model matches the closest successful pattern already in the HUD:
- layered authored widget structure like the wind compass

It also keeps only one truly dynamic visual:
- the minimap crop texture

That reduces the moving parts to:
- static frame
- static face
- static marker
- one changing map layer

This is the minimum-complexity version that still supports the desired visual design.

## Full Map Is Different

The full world map is not covered by this contract.

It may legitimately use:
- `Image`
- direct texture assignment
- square panel composition

That is acceptable because the full map is a different product with a different job.

The minimap must not inherit full-map rendering choices.

## Debugging Order

When rebuilding under this contract, rendering must be verified in this order:

1. frame visible alone
2. inner face visible alone
3. centered boat marker visible alone
4. live map layer visible inside mask
5. marker rotation active
6. live map scroll active under centered marker

Do not debug all layers simultaneously first.

## Acceptance Criteria

This phase is satisfied when these statements are true:

1. There is exactly one dynamic minimap image layer.
2. The frame, face, and marker all use the same authored sprite-background model.
3. No minimap layer needs `Image.sprite` or raw texture assignment except the live map texture.
4. A missing boat icon can be diagnosed as a single layer problem, not a render-path identity crisis.
5. A border readability problem can be diagnosed without touching the world map.

## Guardrails For The Next Phase

When implementation resumes:

- do not swap the chosen rendering model mid-pass
- if the model fails, prove which exact layer failed first
- only change one rendering variable at a time:
  - asset assignment
  - layer order
  - viewport box
  - mask box
  - marker box

If a future change requires a different render model, revise this contract before revising the code.
