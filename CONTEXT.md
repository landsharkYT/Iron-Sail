# The Iron Sail

The Iron Sail is a Unity 2D sailing exploration game. This glossary defines project-specific language used when discussing gameplay systems, testing, and architecture.

## Language

**Regression Gauntlet**:
A repeatable suite of scenario-driven checks that stress high-risk gameplay systems across seeds, resolutions, state transitions, and player workflows.
_Avoid_: Mass testing, broad testing

**Map Truth**:
The rule that anything shown as an island, dock, treasure target, or marker on the map must correspond to a real reachable gameplay object or valid world location.
_Avoid_: Fake map objects, map-only islands

**Diagnostic Seam**:
A small read-only surface on production code that exposes a gameplay invariant for validation without letting tests drive or mutate hidden state.
_Avoid_: Test hook, debug hack

**Weather State**:
The current broad atmospheric condition of the world, owned separately from time of day so visual, UI, audio, and future gameplay effects can react consistently.
_Avoid_: Rain flag, day/night variant

**Rainfall**:
A Weather State where rain is active, causing rainy UI variants and visible rain or water-impact effects without implying a gameplay penalty by default.
_Avoid_: Rain mode, rainy day/night

**Rain Impacts**:
Small water-surface ripples that each mark where an individual falling raindrop lands. Only a budgeted fraction of drops produce one; the rest pass through without leaving a ripple. Visually similar to radius ripples but lighter and performance-budgeted for high spawn counts.
_Avoid_: Rain splashes, heavy rain particles, ambient ripples unrelated to falling rain

**Weather Debug Hotkeys**:
Inspector-gated keyboard shortcuts used during development to force or advance Weather State without changing the normal weather schedule.
_Avoid_: Permanent weather controls, player weather controls

**World Seed**:
The single authoritative seed, owned by island generation, that deterministically reproduces every static world feature — islands, rocks, shops, and fishing spots. Anything reproducible from it is a world feature. At New Game it may be chosen by the player (a number used as-is, or any text mapped to a seed) or left blank for a random world; a loaded game keeps the seed stored in its Save File.
_Avoid_: Per-system seeds, fishing session seed

**Live Entity**:
A runtime actor whose existence and state depend on the player's path through a session, not on the World Seed — currently Night Enemies. Live Entities are persisted individually in a save, never reproduced from the World Seed.
_Avoid_: Seeded enemies, reproducible encounters

**Whirlpool**:
A circular sea hazard that drags the boat toward its centre in a vortex and harms it over time while inside. Escape is emergent from the boat's own wind power — only near-full thrust beats the pull, so it is possible only roughly downwind, and weak or adverse wind can make a Whirlpool lethal. Exists in Medium and Large sizes.
_Avoid_: Maelstrom-only, instant-kill trap, collidable obstacle

**Save File**:
An exportable JSON snapshot of a single session that, on load, restores it faithfully — the World Seed (to reproduce world features), the boat's position and heading, mutable player progress, Calendar Day, Playtime, and every Live Entity. "Almost dead" must reload as almost dead.
_Avoid_: Checkpoint, seed-only save, autosave-only

**Playtime**:
Real-world active gameplay time accumulated by a Save File. Rest-skipped world time, paused time, title-screen time, and menu-idle time do not count.
_Avoid_: In-game time elapsed, clock time since save creation

**Calendar Day**:
The player-facing day number shown in save slots and UI. It starts at Day 1 even though the underlying elapsed-day counter starts at 0.
_Avoid_: Day 0, treating elapsed day count as display text

**Save Slot**:
A labelled destination that holds one Save File. Three slots are player-writable; one reserved slot holds the Autosave and is never a manual save target. The same slot menu serves both loading and choosing a manual save target.
_Avoid_: Quicksave, treating the slot and the file as the same thing

**Autosave**:
A Save File the game writes automatically to its own reserved Save Slot as a safety net, never overwritten by a manual save.
_Avoid_: Quicksave, checkpoint, manual slot

**Met Shopkeeper**:
A shopkeeper the player has invoked "Talk" with at least once — the interaction that may grant a map marker. Persisted as a set of seed-stable ShopIds; on load the set is restored directly, never by replaying "Talk", so a marker is never re-granted.
_Avoid_: Visited shop, docked-at shop, reachability-visited shop

**Rest**:
A Dockside Shop service that is only active during Night. Rest advances world time forward to the next Sunrise, including normal day-count progression and phase-change events, then leaves the shop menu open with a short confirmation status.
_Avoid_: Sleep teleport, phase jump, rewinding to sunrise
