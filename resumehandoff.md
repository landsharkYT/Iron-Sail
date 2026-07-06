# The Iron Sail - Resume Writer Handoff

## Project Summary

The Iron Sail is a Unity 2D open-world sailing and survival game built around procedural ocean exploration. The player pilots a small boat across a tilemap ocean, manages limited cargo and equipment, discovers islands and docks, trades with shopkeepers, follows treasure-hunt clues, and survives environmental and combat pressure.

The project is best described on a resume as a solo or small-team systems-heavy Unity game project. Its strongest technical story is the number of interconnected systems that cooperate in real time: procedural world generation, streaming, inventory, trade UI, map discovery, audio state management, survival meters, boat control, and quest progression.

## Core Game Experience

Players navigate a boat through an ocean world with wind, water, day/night changes, islands, docks, shops, enemies, treasure objectives, and map discovery. The game blends exploration, resource management, light survival, trading, and naval combat.

The core loop is:

1. Sail through a procedurally populated ocean.
2. Discover islands, docks, and chart locations on a map.
3. Manage cargo, equipment, food, ammunition, and ship condition.
4. Visit shops to buy supplies, sell cargo, repair, or receive clues.
5. Follow shopkeeper clue chains toward treasure.
6. Survive hazards, enemies, hunger, hull wear, and navigation constraints.

## Notable Systems To Highlight

### Procedural Ocean And Island Generation

The game includes a deterministic island generation system that places islands across a large world using seeded candidate sectors, island shape parameters, spacing rules, treasure isolation, and shop-dock eligibility. Islands use tilemaps and are streamed by chunk around the player to keep the world scalable.

Resume framing:

- Built deterministic procedural island generation with seeded placement, spacing, and shoreline shape variation.
- Implemented chunk-based world streaming for tilemap islands, docks, treasure targets, and ocean boundaries.
- Tuned generation density, spawn protection, render distance, and map-discovery consistency to reduce pop-in and phantom map data.

### Map Discovery And Charting

The map system tracks explored areas, discovered islands, docks, markers, and treasure information. It evolved from direct tilemap sampling into a hybrid charting system that coordinates with procedural generation and world streaming so the map reflects what the player has actually discovered.

Resume framing:

- Designed a map discovery system with persistent chart memory, world-map panning/zooming, player markers, and discovered feature rendering.
- Optimized map rendering for interaction by reducing expensive redraw work during pan/zoom.
- Integrated procedural island and dock discovery with chart state while preventing stale or nonexistent world features from persisting on the map.

### Treasure Hunt And Shopkeeper Clue Chains

The game contains a treasure-hunt path where shopkeepers can point players toward the next clue or eventual treasure location. Treasure islands and target cells are generated deterministically, and clue routing validates that marked shopkeepers can continue the chain.

Resume framing:

- Implemented deterministic treasure-island placement and target generation.
- Built shopkeeper clue-chain logic that validates route continuity across procedurally placed docks.
- Added chart invalidation for moving treasure targets so stale treasure markers do not mislead the player.

### Inventory, Equipment, Shop, And Trade UI

The player has a constrained ship inventory with item slots, weight limits, gold, stack quantities, and equipment slots. Shops support buying dock stock, selling cargo, repair flows, and contextual item details. The UI has been heavily tuned for responsive layout across awkward Unity Game view resolutions.

Resume framing:

- Built inventory and equipment systems with stack handling, carry limits, item definitions, ammo handling, and equipment slots.
- Implemented shop workflows for buying, selling, repairs, contextual item previews, quantity selection, and UI audio feedback.
- Refactored complex Unity UI Toolkit trade layouts for responsive behavior across multiple resolutions.

### Boat, Wind, Survival, And Combat

The boat has movement, sail state, wind interaction, hull wear, health/damage, weapon audio, ammunition usage, and enemy interaction. The survival layer includes hunger and repair/maintenance pressure.

Resume framing:

- Developed boat control and survival systems including wind-influenced sailing, hull wear, health, hunger, and repair mechanics.
- Integrated ammunition and equipment logic with player weapons and combat audio.
- Added contextual audio feedback for boat damage, movement, UI actions, weapons, and world ambience.

### Audio And Music Systems

The game uses separate audio systems for UI, ambience, boat movement, weapons, impacts, and music. Music responds to game state such as day/night, high speed, and combat with crossfading between tracks.

Resume framing:

- Implemented state-driven music selection and crossfading based on combat, speed, and day/night phase.
- Built runtime volume settings for master, music, SFX, and ambience buses.
- Added non-repeating randomized hit audio and UI feedback sounds across gameplay and title-screen interactions.

## Technical Keywords

Useful keywords for a resume writer:

- Unity
- C#
- 2D game development
- Procedural generation
- Deterministic world generation
- Tilemap streaming
- UI Toolkit
- Responsive game UI
- Inventory systems
- Quest and clue-chain systems
- Map discovery
- Audio state management
- Gameplay systems programming
- Performance optimization
- State machines
- Runtime caching
- Crossfade audio
- Save/load ready architecture, if applicable after verification

## Strong Resume Bullet Drafts

- Built a systems-heavy Unity 2D sailing game featuring procedural island generation, tilemap streaming, shop trading, treasure clues, inventory management, combat, and survival mechanics.
- Designed deterministic world-generation systems for islands, docks, and treasure targets, including seeded placement, spacing rules, chunk streaming, and map-discovery integration.
- Implemented a persistent charting system with world-map pan/zoom, player markers, discovered islands, docks, and treasure state while resolving stale procedural feature data.
- Developed a ship inventory and equipment pipeline with stackable items, weight limits, gold economy, ammunition handling, shop buying/selling, and repair workflows.
- Refactored complex Unity UI Toolkit shop screens into responsive trade layouts that adapt across constrained resolutions and preserve input behavior.
- Created state-driven audio systems for UI, ambience, weapons, boat impacts, movement, and adaptive music transitions based on combat, speed, and day/night state.
- Optimized runtime performance by reducing map redraw cost during interaction, caching procedural island queries, and time-slicing expensive discovery work.

## Suggested Resume Positioning

This project should be positioned as a gameplay systems engineering project. The strongest angle is end-to-end ownership of interconnected systems in a real-time game:

- procedural content
- player progression
- UI workflows
- audio feedback
- performance tuning
- debugging emergent edge cases

For a technical resume, emphasize the engineering challenges: deterministic generation, streamed tilemaps, map/world consistency, UI Toolkit responsiveness, and state-driven systems. For a game design resume, emphasize the exploration loop, shopkeeper clue chain, resource constraints, and the way map discovery supports player navigation.

## Tone Guidance For Resume Writer

Use concrete engineering language. Avoid vague claims like "made a fun pirate game." Better phrasing:

- "Built a Unity 2D sailing exploration game..."
- "Implemented deterministic procedural island and dock generation..."
- "Refactored responsive shop UI using Unity UI Toolkit..."
- "Optimized map rendering and discovery systems..."

The project reads strongest when framed as a portfolio-scale game with production-style systems, debugging, iteration, and integration work.
