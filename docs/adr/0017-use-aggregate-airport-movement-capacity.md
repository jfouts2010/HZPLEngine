# ADR 0017: Use aggregate airport movement capacity

## Status

Accepted; runway-damage representation superseded by ADR 0023

## Context

Flights already carry materialized takeoff and landing waypoints, airports already inherit build level, damage, and functional level from buildings, and air execution already supports recovery diversion after an airport becomes hostile. Airport damage previously had no effect on package timing, launch, landing, or diversion.

The campaign needs runway damage to constrain air operations without introducing physical runway entities, tactical ATC, taxi queues, parking, weather limits, or aircraft-specific runway requirements.

## Decision

An airport's build level determines one nominal capacity channel at levels 1–5 and two at levels 6–10. ADR 0023 replaces this ADR's original integrity-fraction damage rule: each channel is now independently operational or closed according to its abstract runway damage.

Airport throughput uses fixed fifteen-minute movement windows independent of simulation tick length. Each effective channel provides one capacity slot per window, representing up to four aircraft in one takeoff or landing movement. Larger flight movements consume consecutive windows. Departures and planned recoveries share capacity.

The airport schedule is a projection derived from non-ended flights' materialized takeoff and landing waypoints. It is not persisted on the airport. Package construction searches for the earliest capacity-feasible shift and moves the whole package so its route, rendezvous, support dependencies, and effect timing remain coordinated. Capacity is checked again immediately before aircraft reservation and package commitment.

When damage reduces capacity, airborne packages and their recoveries are retained first. Later grounded packages that no longer fit are cancelled and returned to planning. A closed launch airport prevents takeoff, while a closed recovery airport invokes the existing diversion hierarchy. Runway closure does not destroy grounded aircraft; only hostile capture causes airbase-overrun losses.

## Consequences

Airport damage now has an immediate, deterministic operational effect while flight routes remain the source of truth for timing. Cancellation and recovery-route replacement cannot leave stale reservation state. The same rules define planning eligibility, launch eligibility, recovery selection, diagnostics, and airport UI status.

Capacity channels are campaign abstractions rather than physical runways. Exact runway geometry, surface condition, wind, taxi flow, parking, emergency sequencing, crater locations, repair crews, and aircraft-specific field requirements remain deferred until an implemented consumer needs them.
