# Minimap Rendering Contract

This document locks the rendering approach for the HUD minimap so rebuild work
keeps one rendering model through a debug pass.

## Chosen Rendering Model

The HUD minimap uses:

1. `VisualElement` layered widget structure
2. authored art assigned through `StyleBackground(sprite)`
3. one dynamic texture layer for the live local map view

Layer sources:

- face: authored sprite background
- frame: authored sprite background
- player icon: authored sprite background
- live map: generated `Texture2D` background

## Layer Rules

### `minimap-face-layer`

- Type: `VisualElement`
- Background: `StyleBackground(mapCircleInnerSprite)`
- Purpose: static authored face art

### `minimap-live-map-layer`

- Type: `VisualElement` inside the mask container
- Background: `StyleBackground(minimapTexture)`
- Purpose: dynamic local map crop
- Box: inset according to the UI contract

### `minimap-frame-layer`

- Type: `VisualElement`
- Background: `StyleBackground(mapCircleSprite)`
- Purpose: visible border ring

### `minimap-player-layer`

- Type: `VisualElement`
- Background: `StyleBackground(boatMarkerSprite)`
- Purpose: centered player icon with rotation only

## Masking

Masking crops the dynamic live-map layer to the intended circular viewport. The
authored frame and face define the widget's appearance.

## Full Map Boundary

The full world map can use `Image`, direct texture assignment, and square panel
composition. It has a different job and a different visual structure.

## Verification Order

Verify rendering in this order:

1. frame visible alone
2. inner face visible alone
3. centered boat marker visible alone
4. live map layer visible inside mask
5. marker rotation active
6. live map scroll active under centered marker

## Acceptance Criteria

This contract is satisfied when:

1. There is one dynamic minimap image layer.
2. Frame, face, and marker use authored sprite backgrounds.
3. The live map texture is the only generated texture layer.
4. A missing boat icon can be diagnosed as a marker-layer problem.
5. A border readability problem can be diagnosed inside the minimap widget.

## Change Rule

If a different render model becomes necessary, revise this contract before
revising the code.
