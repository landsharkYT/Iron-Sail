# The Iron Sail Game Plan

## Logline

Harness the wind, hunt sea monsters, and survive the open ocean. Scavenge islands,
upgrade your ship, and push toward dangerous final waters in search of treasure.

## Concept

The Iron Sail is a 2D sailing adventure about crossing a procedural ocean in a
wind-driven boat. The player travels from island to island, gathers resources,
fights sea monsters, upgrades the ship, and gradually fills in an incomplete map.

The sailing model takes inspiration from Valheim: wind direction matters, sails
are adjustable, and a poor wind can push the player off course. Combat happens
from the boat with guns, harpoons, and cannons. Hunger and supplies encourage the
player to land, trade, fish, and plan routes carefully.

Islands are generated from rules. As the player moves farther from the start,
islands become more spread out, which makes better boats and supply planning more
important. Island shops can offer repairs, upgrades, food, and map information.

## Feasibility Notes

Procedural island generation is manageable compared with the feel of the boat.
The hardest design task is translating a 3D sailing idea into a satisfying 2D
control scheme. Procedural boat and sail animation can help the boat look lively
without requiring full directional sprite sets.

Island interactions can stay text-based at first so the sailing remains the main
focus. A fishing minigame is useful if time allows.

## Must Have

- Physics-based boat movement driven by wind.
- Wind schedule.
- Enemies with pathfinding.
- Island UI for shopping, repairs, and upgrades.
- Hunger.
- Procedural water and island tiles.
- Boat upgrades.
- Water particles.
- Universal currency.
- Player inventory.
- Wind direction UI inspired by Valheim.
- Muskets and cannons.
- Day/night cycle.
- Procedural boat turning and sail animation.
- Gradually revealed map.
- Save states.

## Should Have

- Limited ammunition for bullets and cannonballs.
- Wider enemy variety.
- Rest button to skip night.
- Night lighting from boat upgrades.

## Could Have

- Detailed enemy variants.
- Fishing minigame.
- Item weight limits by boat type.
- Fully authored detailed animations.
- Boat color customization.
- Fish encyclopedia shared across playthroughs.

## Out Of Scope

- Separate health bar system.
- Fog zones.
