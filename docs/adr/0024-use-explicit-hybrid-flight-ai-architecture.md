# ADR 0024: Use an explicit hybrid flight AI architecture

## Status

Accepted

## Context

Flight execution grew around deterministic campaign rules. The design already
contained a flight phase enum, persisted tactical intent, ordered combat rules,
utility-style ground-attack scoring, and a package-plan contract. Those parts
were sound, but their architectural roles were implicit. State transitions and
point-mass movement lived inside large execution classes, making it difficult to
tell whether a method was choosing behavior, changing lifecycle state, or merely
executing a command.

A single general-purpose AI pattern is a poor fit for the whole problem. Safety
rules require fixed priority, flight lifecycle transitions require strict
validation, target and weapon choices benefit from comparable scores, and future
operational planning must coordinate packages and resources over time.

## Decision

Flight AI uses four explicit layers.

1. `IAirPlanProducer` produces `AirPackagePlan` records. The current producer
   reads scripted campaign plans. A future HTN planner may replace that producer
   without changing package construction or flight execution.
2. `FlightStateMachine` owns valid combinations of tasking lifecycle and physical
   execution phase. `AirFlight` requests named transitions rather than assigning
   those fields directly. Tactical intent remains separate from physical phase
   so combat maneuvers do not multiply the lifecycle state space.
3. Package-level mission behavior is dispatched through
   `IFlightMissionBehavior`. DEAD and strike behavior retain their deterministic
   execution rules while presenting explicit behavior boundaries.
4. Tactical combat remains an ordered rule pipeline. Safety overrides and
   weapon-guidance commitments run before mission and engagement decisions.
   Command constraints such as BARCAP boundaries and known-SAM avoidance run
   afterward. Commands record the stage that produced them.

`FlightMovementSystem` executes commands and owns point-mass integration, motion
fuel burn, and waypoint arrival detection. It does not select targets or tactical
intent.

Utility scoring remains local to choices between comparable options. Ground
attack planning records named score factors with the selected plan. Hard rules
such as incoming-weapon defense, bingo recovery, and route safety are not utility
scores.

## Consequences

The main execution loop reads as coordination rather than a mixture of decision
and movement code. Flight state changes have one validation point, and tactical
logs identify the decision stage responsible for a change. Scripted planning
behavior and deterministic tick order remain unchanged.

Adding a mission type still requires deterministic mission behavior and route
semantics. The behavior interface is a boundary, not permission to hide all
flight logic behind interchangeable micro-classes. GOAP is not introduced into
individual flight execution. If autonomous operational planning returns, it
should begin as an HTN-style `IAirPlanProducer` and continue to emit the existing
package-plan contract.
