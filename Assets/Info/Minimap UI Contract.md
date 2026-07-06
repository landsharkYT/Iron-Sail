# Minimap UI Contract

This contract defines the HUD minimap as an authored layered widget. It covers
the small in-HUD minimap only; the full `M` map overlay, chart labels, and other
marker systems have their own behavior.

## Widget Size

- Full minimap widget box: `200 x 200`
- Coordinate origin: top-left of the widget
- All minimap layers align to this root box

## Asset Roles

### `MapCircle`

`MapCircle` is the visible outer frame. It should read as a clear black border
around the minimap face.

### `MapCircleInner`

`MapCircleInner` is the authored inner face. It gives the minimap a visible
surface beneath the live map texture.

### `BoatMapIndicator`

`BoatMapIndicator` is the player boat icon. It stays centered in the minimap and
rotates with heading.

## Layer Order

Back to front:

1. `minimap-face-layer`
2. `minimap-live-map-layer`
3. `minimap-frame-layer`
4. `minimap-player-layer`

The live map sits inside the authored face, the frame remains readable above it,
and the boat marker remains visible on top.

## Pixel Boxes

### Root

- `minimap-root`: `0,0 -> 200x200`

### Outer Frame

- `MapCircle` draw box:
  - left: `20`
  - top: `20`
  - width: `160`
  - height: `160`

### Inner Face

- `MapCircleInner` draw box:
  - left: `20`
  - top: `20`
  - width: `160`
  - height: `160`

### Live Minimap Viewport

- live map viewport box:
  - left: `28`
  - top: `28`
  - width: `144`
  - height: `144`

The inset keeps the live texture from running into the frame and leaves a visible
face rim around the map content.

### Player Marker

- `BoatMapIndicator` box:
  - left: `91`
  - top: `83`
  - width: `18`
  - height: `34`

If this is too subtle after the final rebuild, test `20 x 38` next.

## Behavior

- The minimap is north-up.
- The minimap texture stays unrotated.
- The boat icon rotates with heading.
- The boat icon stays centered.
- Map content scrolls beneath the centered icon.
- The live map is one dynamic image layer clipped to the circular viewport.

## Acceptance Criteria

The minimap is correct when:

1. `MapCircle` reads as a distinct outer frame.
2. The widget reads as a purpose-built minimap, with authored frame and face art.
3. The live map texture is visibly inset inside the frame.
4. The boat icon is visible, centered, and rotating.
5. The boat icon stays fixed while the player moves.

## Change Rule

If implementation needs different boxes, layer order, or asset roles, update this
contract first and then update the code to match.
