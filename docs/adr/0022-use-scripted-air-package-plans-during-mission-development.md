# ADR 0022: Use Scripted Air Package Plans During Mission Development

## Status

Accepted

## Context

The autonomous air planner combined mission-request generation, priority scoring, projected effects, support demand, package composition, and route selection. That breadth made failures difficult to attribute while BARCAP, SEAD escort, fighter escort, OCA sweep, strike, and recovery behavior are still being developed. The game is not deployed, so preserving the temporary planning model has less value than establishing a small deterministic seam around mission execution.

## Decision

Campaign templates author explicit `AirPackagePlan` records. A plan chooses its alliance, operation type, effect window and area, named squadron and aircraft strength for every flight, flight task, escort relationships, route geometry, recovery airport, optional loadout, and operation-specific BARCAP, DEAD, or OCA airport-strike data. The package builder validates and materializes those decisions; it does not infer demand, targets, composition, or routes.

The air-tasking system attempts each due plan once in stable order, applies airport-capacity and aircraft-reservation rules, and commits the resulting package to the existing execution pipeline. Failures are recorded against the stable plan identity and are not automatically replanned. `AirOperationType` describes the package-level purpose, while `AirFlightTaskType` describes each flight's job, allowing fighter and SEAD escorts to accompany a primary flight without pretending to be separate operational demands.

Development campaign schedules are bounded to the first 24 campaign hours. The Advanced Mechanics Test Campaign uses that horizon for explicit two-hour BARCAP rotations rather than generating an open-ended patrol schedule.

The mission-request graph, request generator, priority and projected-effect services, air-planning intelligence snapshots, BARCAP and DEAD strategic planners, remembered air-control assessment, request history, and support-demand history are removed. Runtime execution, aircraft and airport reservations, loadout validation, route semantics, fuel, IADS, combat, ground attack, recovery, logging, and scenario export remain.

This decision temporarily supersedes the autonomous planning portions of ADR 0004 and the generated-planning portions of ADRs 0009 and 0019. Their execution geometry and tactical behavior remain applicable where an authored plan provides the required materialized data.

## Consequences

Mission behavior can be tested from a repeatable authored setup without planner churn. A failed plan stays failed, making invalid composition, timing, route, loadout, or airport assumptions visible instead of being hidden by retries. Adding a new mission initially requires authoring its plan and execution behavior explicitly.

A later autonomous planner should produce the same `AirPackagePlan` contract. Reintroducing AI decision-making should therefore replace the plan producer, not the builder or execution pipeline, and should not restore request objects merely for compatibility with the removed implementation.
