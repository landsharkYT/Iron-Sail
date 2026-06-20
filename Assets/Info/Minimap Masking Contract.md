# Minimap Masking Contract

This document defines the masking and viewport rules for the minimap.

The purpose of this phase is to stop the circular crop from doing visual jobs that belong to the authored art layers.

## Problem Being Solved

The minimap has repeatedly drifted into this failure mode:

- the circular crop makes the widget look like a blue disc
- the frame and inner face stop reading as authored UI
- the mask becomes the visual identity of the minimap

That is wrong.

The mask is only a technical crop for the live map layer.

The art defines the minimap’s identity.

## Core Principle

Masking is a viewport constraint, not a design system.

The minimap must be readable if described this way:
- `MapCircle` defines the visible frame
- `MapCircleInner` defines the visible face
- the mask only limits where the live map texture shows through

If the minimap instead reads like:
- “a circle cropped out of a texture”

then the contract has been violated.

## What The Mask Owns

The mask owns exactly one responsibility:

- crop the live minimap texture to the intended circular viewport

That is all.

## What The Mask Does Not Own

The mask must not define:
- the visible border
- the visible face
- the apparent frame thickness
- the apparent widget identity
- the player icon shape

Those belong to:
- `MapCircle`
- `MapCircleInner`
- `BoatMapIndicator`

## Viewport Box Contract

The live viewport box is fixed by the visual contract:

- left: `28`
- top: `28`
- width: `144`
- height: `144`

This box is inset relative to:

- frame box: `20,20,160,160`
- face box: `20,20,160,160`

That inset is not optional.

Its purpose is to preserve:
- visible border separation
- visible face rim around the live content

## Mask Shape Contract

The live map texture may be cropped to a circle using a simple circular mask derived from the viewport box.

Acceptable technical implementations:
- rounded-corner clipping on the live viewport container
- a custom circular clip element later

Unacceptable interpretation:
- treating that circular clip as the same thing as the frame

## Layer Relationship To The Mask

### Behind the mask

- only the dynamic live map texture layer

### Outside the mask responsibility

- face layer
- frame layer
- player marker layer

This means:
- the face and frame must remain visually meaningful even if the live map layer is disabled

If they do not, the mask is doing too much.

## Isolation Test

The mask must pass this test:

1. Disable the live map texture layer.
2. Leave face + frame active.
3. Confirm the minimap still reads like a real widget.

If the widget becomes visually meaningless without the live map texture, the mask and live layer have been over-authorized.

## Visual Failure Definitions

### Correct result

- live map sits inside a circular viewport
- frame visibly surrounds it
- face remains visibly distinct around it

### Incorrect result

- minimap reads like one blue circle
- frame appears to vanish into the crop edge
- inner face is visually overwritten
- the circle crop is the only thing defining the shape

## CSS / UI Toolkit Rule

Generic UI Toolkit radius clipping is permitted only as a technical helper.

It is not permitted as the final answer to the widget design.

That means:
- `border-radius` or equivalent may exist on the viewport container
- but the minimap must still be visually understandable from the authored assets alone

## Mask Debugging Scope

If the failure is:
- “the live map spills outside the intended circle”

then the problem belongs to:
- viewport box
- mask box
- live map layer sizing

It does not belong first to:
- frame art
- player icon
- world map controller

## Acceptance Criteria

This phase is satisfied when these statements are true:

1. The mask only affects the live map texture layer.
2. The minimap still reads like a widget when the live map layer is disabled.
3. The visible edge of the minimap comes from the frame/face art, not from the mask alone.
4. The live map remains visibly inset from the frame.
5. A masking bug can be debugged without changing frame or marker semantics.

## Guardrails For Implementation

When implementation resumes:

- do not tune the frame and mask in the same first pass
- do not use crop correctness as proof that the visual design is correct
- do not let the live viewport expand to the frame edge

If the minimap still reads like a raw blue disc after the rebuild, assume the mask contract has been violated before assuming the frame asset is bad.
