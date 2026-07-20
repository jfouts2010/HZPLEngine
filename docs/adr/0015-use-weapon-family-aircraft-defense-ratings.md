# ADR 0015: Use weapon-family aircraft defense ratings

## Status

Accepted

## Context

Aircraft previously supplied one generic ECM-quality rating. Radar and infrared release calculations both consumed it, gun attacks had no target-specific defense, and terminal missile defense depended on a weapon-side countermeasure-resistance rating. Merely selecting a beam also granted full defensive effect before a slow-turning aircraft reached useful geometry. This could not express an aircraft that is difficult for an infrared seeker to acquire but poor at jinking, and it introduced flare and chaff distinctions that the campaign model does not need.

## Decision

This decision supersedes the generic ECM and ordnance countermeasure-resistance portions of ADRs 0008 and 0012.

Each aircraft type authors three normalized ratings: radar defense, infrared defense, and gun defense. Guidance mode selects missile defense, while the gun employment category selects gun defense. Radar detectability remains responsible only for hostile radar tracking, WVR combat rating remains responsible for earning attack opportunities, and survivability remains responsible for damage after a hit.

Radar and infrared defense modify their respective ordinary release probabilities. During pending missile flight, the matching aircraft defense rating controls how much achieved defensive maneuvering can reduce terminal probability. Beam, break, and drag effectiveness comes from the aircraft's actual heading relative to the current guidance-source bearing, so existing aircraft turn rate determines how quickly useful defense accumulates. Radar-guided terminal threats select a beam and infrared terminal threats select a break. WVR infrared and gun opportunities apply their matching aircraft defense only when the target is aware.

Flares and chaff are implicit parts of the aircraft ratings and have no definitions, inventories, programs, faction modifiers, or effectiveness values. Ordnance countermeasure resistance is removed; inherent weapon and seeker quality remains represented by authored hit probability.

## Consequences

Aircraft can differ against radar missiles, infrared missiles, and guns without a general defense-component framework. A low-signature but slow-turning aircraft can have strong infrared defense while still taking longer to earn geometric maneuver credit. The same guidance mapping works for aircraft and surface-launched missiles, including a future infrared SAM, without a SAM-specific defense table.

The ratings are intentionally aggregate. Detailed flare or chaff expenditure, missile-warning equipment, throttle-dependent infrared signature, physical missile position, per-aircraft maneuver state, and weapon-specific counter-countermeasure modeling remain deferred until an implemented consumer requires them.
