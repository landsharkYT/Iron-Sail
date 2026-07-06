# Minimap Rebuild Sequence

This document defines the order for rebuilding the minimap from the outside in.
Each step adds one visual concern and gives a clear place to stop and verify.

## Governing Contracts

This sequence follows:

- `Minimap UI Contract`
- `Map UI Architecture Contract`
- `Minimap Rendering Contract`
- `Minimap Component Contract`
- `Minimap Marker Contract`
- `Minimap Masking Contract`

If a step conflicts with a contract, update the contract first.

## Rebuild Order

### Step 1: Frame Only

Active layers:

- `minimap-frame-layer`

Inactive layers:

- face
- live map
- player marker

Goal:

- prove `MapCircle` is visible and reads as the border

Pass condition:

- clear visible black ring
- independent of live map content

If this step fails, inspect frame asset assignment, frame box, and frame layer
visibility.

### Step 2: Face With Frame

Active layers:

- `minimap-face-layer`
- `minimap-frame-layer`

Inactive layers:

- live map
- player marker

Goal:

- prove `MapCircleInner` reads as a deliberate inner face below the frame

Pass condition:

- the minimap reads like a framed instrument face
- the inner face remains distinguishable from the outer frame

If this step fails, inspect face assignment, face box, and frame/face layering.

### Step 3: Marker Over Face And Frame

Active layers:

- `minimap-face-layer`
- `minimap-frame-layer`
- `minimap-player-layer`

Inactive layers:

- live map

Goal:

- prove the player marker independently of map content

Pass condition:

- marker is visible
- marker is centered
- marker is upright at zero rotation

Then rotate only the marker.

Second pass condition:

- marker rotates around center with no drift

If this step fails, inspect marker-layer issues under the marker contract.

### Step 4: Live Map Under Verified Static Layers

Active layers:

- `minimap-face-layer`
- `minimap-live-map-layer`
- `minimap-frame-layer`
- `minimap-player-layer`

Goal:

- add the dynamic layer after the static layers are trusted

Pass condition:

- live map appears only inside the intended viewport
- live map leaves the frame readable
- live map leaves the face readable
- player marker remains visible on top

If this step fails, inspect live map texture assignment, viewport box, and
masking.

### Step 5: Mask Verification

Active layers:

- same as Step 4

Goal:

- verify the live map crop while preserving authored frame and face identity

Pass condition:

- dynamic map content stays inside the viewport circle
- the frame defines the widget edge
- the face remains visible as an authored layer

If this step fails, inspect mask box and viewport box.

### Step 6: Live Scroll Under Fixed Marker

Active behavior:

- player moves in world
- live minimap crop updates
- centered marker stays put

Goal:

- prove local viewport behavior

Pass condition:

- map content moves under the marker
- marker stays fixed
- minimap stays north-up

If this step fails, inspect minimap crop logic.

### Step 7: HUD Integration

Active behavior:

- minimap visible in HUD
- `Tab` hides/shows HUD including minimap
- world map overlay suppresses minimap if intended by current behavior

Goal:

- prove the verified minimap survives real HUD integration

Pass condition:

- minimap layout remains stable in the HUD
- HUD toggles behave correctly
- minimap still matches the visual contract

If this step fails, inspect integration and placement concerns.

## Stop Conditions

At the end of each step:

- capture visual confirmation
- continue only after the current step is visibly correct

## Failure Routing

- No black border: return to Step 1.
- Blue disc with no readable face: return to Step 2.
- Marker missing or unreadable: return to Step 3.
- Map texture covers the frame: return to Step 4.
- Crop shape wrong: return to Step 5.
- Marker drifts with motion: return to Step 6.
- Works alone but breaks in HUD: return to Step 7.

## Completion Definition

The minimap rebuild is complete when all steps pass in order:

- frame trusted
- face trusted
- marker trusted
- live map trusted
- mask trusted
- scrolling behavior trusted
- HUD integration trusted

## Implementation Guardrails

- Add one visual concern at a time.
- Keep frame, face, marker, live map, and mask box changes separate.
- Keep full world map code out of minimap layering work.
- Change render path and layer hierarchy in separate passes.
