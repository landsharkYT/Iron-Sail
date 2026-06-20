# Minimap Rebuild Sequence

This document defines the implementation order for rebuilding the minimap from the outside in.

The purpose of this phase is to ensure the minimap is assembled in a controlled sequence, with visible stop conditions after each layer, instead of debugging the whole stack at once.

## Why This Sequence Exists

Previous minimap work failed because too many moving parts were active simultaneously:
- frame art
- inner face art
- live map texture
- player marker
- masking
- rotation
- HUD integration

That made every failure ambiguous.

This rebuild sequence removes that ambiguity by introducing one visual concern at a time.

## Governing Contracts

This sequence must obey:
- `Minimap UI Contract`
- `Map UI Architecture Contract`
- `Minimap Rendering Contract`
- `Minimap Component Contract`
- `Minimap Marker Contract`

If any step here conflicts with those contracts, update the contract first before updating the code.

## Rebuild Order

### Step 1: Frame Only

Active layers:
- `minimap-frame-layer`

Disabled/absent:
- face
- live map
- player marker

Goal:
- prove `MapCircle` is visible and unmistakably reads as the border

Pass condition:
- clear visible black ring
- not a blue disc
- not dependent on any live map content

If this step fails:
- do not continue
- only debug frame asset assignment, frame box, and frame layer visibility

### Step 2: Face Only With Frame

Active layers:
- `minimap-face-layer`
- `minimap-frame-layer`

Disabled/absent:
- live map
- player marker

Goal:
- prove `MapCircleInner` reads as a deliberate inner face below the frame

Pass condition:
- the minimap reads like a framed instrument face, not just a ring
- the inner face remains distinguishable from the outer frame

If this step fails:
- do not continue
- only debug face assignment, face box, and frame/face layering

### Step 3: Marker Only Over Face + Frame

Active layers:
- `minimap-face-layer`
- `minimap-frame-layer`
- `minimap-player-layer`

Disabled/absent:
- live map

Goal:
- prove the player marker independently of map content

Pass condition:
- marker clearly visible
- marker centered
- marker upright at zero rotation

Then:
- rotate marker only

Second pass condition:
- marker rotates around center with no drift

If this step fails:
- do not continue
- only debug marker layer issues under the marker contract

### Step 4: Live Map Layer Added Under Verified Static Layers

Active layers:
- `minimap-face-layer`
- `minimap-live-map-layer`
- `minimap-frame-layer`
- `minimap-player-layer`

Goal:
- add the one dynamic layer after the static layers are already trusted

Pass condition:
- live map appears only inside intended viewport
- live map does not visually erase the frame
- live map does not visually erase the face
- player marker remains visible on top

If this step fails:
- do not touch frame or marker assumptions first
- debug only live map texture assignment, viewport box, or masking

### Step 5: Mask Verification

Active layers:
- same as Step 4

Goal:
- verify the live map is being cropped correctly without becoming the visual identity of the widget

Pass condition:
- dynamic map content stays inside the viewport circle
- the frame is still what visually defines the widget edge
- the face still exists as a visible authored layer

If this step fails:
- only debug mask box and viewport box
- do not reclassify frame/face roles

### Step 6: Live Scroll Under Fixed Marker

Active behavior:
- player moves in world
- live minimap crop updates
- centered marker stays put

Goal:
- prove the local viewport behavior

Pass condition:
- map content moves under the marker
- marker does not drift
- minimap stays north-up

If this step fails:
- debug minimap crop logic only
- do not touch marker centering assumptions first

### Step 7: HUD Integration

Active behavior:
- minimap visible in HUD
- `Tab` hides/shows HUD including minimap
- world map overlay suppresses minimap if intended by current behavior

Goal:
- prove the minimap survives actual HUD integration after visual/widget verification

Pass condition:
- minimap layout remains stable in real HUD
- HUD toggles behave correctly
- minimap still matches the visual contract

If this step fails:
- debug only integration/placement concerns
- do not reopen lower layer contracts unless clearly broken

## Required Stop Conditions

At the end of each step:
- capture a visual confirmation
- do not continue until the current step is visibly correct

This is mandatory.

No step may be considered “close enough” if its pass condition is not satisfied.

## Explicitly Forbidden Shortcuts

During the rebuild, do not:

1. Re-enable all layers at once “just to see what happens.”
2. Change frame, face, marker, and live map boxes in the same iteration.
3. Touch full world map code while fixing minimap layering.
4. Change render path and layer hierarchy simultaneously.
5. Diagnose marker failure while the live map layer is still untrusted.

## Failure Routing Table

If the visible failure is:

### “No black border”
- return to Step 1

### “Blue disc with no real face”
- return to Step 2

### “Marker missing or unreadable”
- return to Step 3

### “Map texture covers everything or wipes out the frame”
- return to Step 4

### “Crop shape wrong”
- return to Step 5

### “Marker drifts with motion”
- return to Step 6

### “Looks right alone, breaks in HUD”
- return to Step 7

## Completion Definition

The minimap rebuild is complete only when all steps pass in order without reopening earlier assumptions.

That means:
- frame is trusted
- face is trusted
- marker is trusted
- live map is trusted
- mask is trusted
- scrolling behavior is trusted
- HUD integration is trusted

## Guardrails For Implementation

When implementation resumes:
- treat this document as the exact order of work
- if a step fails, revert to that step’s scope
- do not broaden the scope of the debug session until the current step passes

If the implementation starts feeling like “everything is broken at once” again, the sequence has been violated.
