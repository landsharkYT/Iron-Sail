# Regression Gauntlet

The Regression Gauntlet is a focused suite of Unity Play Mode smoke tests for high-risk gameplay regressions.

## Location

Place tests under:

```text
Assets/Tests/PlayMode/RegressionGauntlet/
```

The first pass should load `Assets/Scenes/SampleScene.unity` and validate the real runtime wiring.
Keep pass-one tests in the default script assembly until production scripts are moved behind an asmdef; a custom test asmdef cannot reference `Assembly-CSharp` directly.

## Style

- Prefer controller-first scene smoke tests over full player navigation.
- Move the real boat only for discovery-radius or streaming scenarios where position matters.
- Use tiny read-only Diagnostic Seams when existing public APIs cannot expose an invariant.
- Avoid test-only mutation hooks unless a scenario cannot otherwise be made deterministic.
- Force deterministic setup for generation-sensitive tests; `SampleScene` currently randomizes island seed at play startup.
- When Map Truth fails, report the sampled world position, chart category, nearest accepted island source, chunk loaded/deferred state, and seed.

## First Scope

- Map Truth: mapped islands, docks, treasure targets, and markers must correspond to real reachable gameplay objects or valid world locations.
- Shop UI layout: buy and sell panels stay readable across common and awkward resolutions.
- Treasure hunt continuity: shopkeeper chains, final treasure reveal, treasure island placement, and chart markers must remain valid.
- Music exclusivity: gameplay music keeps the correct track active after state transitions.

## Implemented Scenarios

- `MapTruthRegressionTests.DiscoveredIslandTilesNearBoatResolveToRealWorldTruth`: loads `SampleScene` with a fixed island seed, moves the boat near an accepted island source, waits for discovery, then verifies mapped land/dock/treasure cells resolve to real world truth.

## Non-Goals For First Pass

- Exhaustive unit tests for every script.
- Pixel-perfect UI assertions.
- Long end-to-end sailing simulations.
- Performance benchmarking beyond simple smoke thresholds.
