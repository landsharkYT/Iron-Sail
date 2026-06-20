# Minimap UI Contract

This document freezes the intended minimap composition before any further code or UI restructuring.

The goal is to stop treating the minimap like a generic circular crop and instead treat it like an authored layered widget, similar in spirit to `WindCompass.uxml`.

## Scope

This contract covers only the HUD minimap widget.

It does not define:
- the full `M` world map overlay
- map discovery logic
- chart labeling or decoration
- marker systems beyond the player boat icon

## Widget Size

- Full minimap widget box: `200 x 200`
- Coordinate origin: top-left of the widget

All minimap layers must align to this same root box.

## Asset Roles

### `MapCircle`

Purpose:
- the unmistakable visible outer frame
- reads as the border/ring of the minimap

Must not be treated as:
- optional decoration
- a hidden underlay
- a clipping mask

Expected read:
- clearly visible black border ring around the minimap face

### `MapCircleInner`

Purpose:
- the visible inner face the live minimap sits within
- gives the minimap a distinct authored surface

Must not be treated as:
- the clipping implementation itself
- a disposable background that gets fully hidden by the live texture

Expected read:
- the player should be able to tell there is a face beneath the live map, not just a raw blue disc

### `BoatMapIndicator`

Purpose:
- player boat icon
- centered in the minimap
- rotates with heading only

Must not:
- translate around the minimap
- be used as a generic dot
- be so large that it reads like a UI decal instead of a map marker

## Layer Order

Back to front:

1. `minimap-face-layer`
2. `minimap-live-map-layer`
3. `minimap-frame-layer`
4. `minimap-player-layer`

Interpretation:
- the authored face is visible below the live map
- the live map is visually contained inside the face
- the border frame sits above both
- the boat marker sits above everything

## Pixel Boxes

These are the fixed authored layout targets for the first proper rebuild.

### Root

- `minimap-root`: `0,0 -> 200x200`

### Outer Frame

- `MapCircle` draw box:
  - left: `20`
  - top: `20`
  - width: `160`
  - height: `160`

This is the authoritative visible border box.

### Inner Face

- `MapCircleInner` draw box:
  - left: `20`
  - top: `20`
  - width: `160`
  - height: `160`

This matches the frame footprint and provides the visible circular face.

### Live Minimap Viewport

- live map viewport box:
  - left: `28`
  - top: `28`
  - width: `144`
  - height: `144`

This inset is intentional.

Reason:
- the live map must not visually run all the way to the frame edge
- the frame has to read as its own border
- the face must still exist as a visible authored layer around the live content

### Player Marker

- `BoatMapIndicator` box:
  - left: `91`
  - top: `83`
  - width: `18`
  - height: `34`

Notes:
- this is centered inside the widget
- it rotates around its own center
- it does not move inside the minimap

If this proves too small to read after the final architecture rebuild, the first fallback size to test is:
- width: `20`
- height: `38`

## Behavioral Rules

### Minimap Orientation

- world is north-up
- minimap texture does not rotate
- only the boat icon rotates

### Marker Position

- marker is always centered in the minimap root
- map content scrolls beneath it

### Live Map Content

- local crop of shared discovered-map data
- clipped to circular viewport bounds
- rendered as a single dynamic image layer only

## Non-Goals

The minimap is not responsible for:
- whole-world display
- panning
- zooming
- route hints
- obstacle markers
- shops
- treasure icons

## Acceptance Criteria

The minimap is correct only if all of these are true:

1. The outer black border from `MapCircle` is clearly readable as a distinct frame.
2. The minimap does not read like a plain blue circle pasted into the HUD.
3. The live map texture sits visibly inset inside the border.
4. The boat icon is visible, centered, and rotating.
5. The boat icon does not drift when the player moves.
6. The minimap reads like a purpose-built widget, not a generic masked texture.

## Implementation Guardrails

Until this contract is satisfied:

- do not change the asset semantics
- do not merge minimap and full-map presentation logic
- do not switch rendering approaches repeatedly without proving the previous layer model wrong

If a future implementation deviates from these boxes or layer semantics, update this contract first rather than silently improvising in code.
