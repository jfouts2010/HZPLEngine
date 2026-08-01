# ADR 0019: Fly threat-aligned BARCAP stations behind spatial barriers

## Status

Accepted

## Context

ADR 0009 established an ordered friendly-side barrier across each probable hostile approach and gave each patrol aircraft-specific, time-based response coverage. Its first station implementation made the station racetrack perpendicular to the hostile approach, scaled track length with campaign tiles, searched one greedy chain directly rearward, and preferred the deepest feasible station. It also treated one aircraft's spatial coverage as complete fulfillment even when doctrine preferred mutual support.

The barrier and the station serve different geometric purposes. The barrier blocks an approach across the defended front. The patrol racetrack should let the flight alternate a forward threat-facing leg with a rearward leg along that approach. Station placement also needs enough engagement depth before the protected release line without requiring every scarce aircraft to reinforce one location while another approach remains open.

## Decision

The ordered BARCAP barrier remains across the probable hostile approach. A generated station racetrack is aligned with the local threat axis and enters on its threat-facing leg. It consists of two parallel straight legs joined by sampled semicircular turns whose radius comes from the assigned aircraft's cruise speed and turn rate. Track length is derived from alliance doctrine's station-leg time and the assigned aircraft's cruise speed. Station altitude is also alliance doctrine and remains a replaceable default until generated airspace-block selection exists.

For the center of the largest current barrier gap, the package builder evaluates a fixed set of ten continuous world-space station centers in a defended-side wedge rather than restricting stations to campaign tile centers or sweeping a distance lattice. Three useful depths each receive center, left, and right candidates, with one maximum-depth retreat option. Every point and segment of a candidate's closed racetrack must remain route-, fuel-, known-SAM-, warning-, and response-feasible. Coverage uses the worst point on the complete racetrack. Candidate ordering maximizes newly covered gap tiles, then intercept margin up to doctrine's desired value, then rearward depth and ordinary aircraft/transit tie-breakers. Planned command delay is part of the response budget. Emergency displacement compares signed depth in the local barrier frame and can only move rearward. A committed future BARCAP closes the corresponding projected tasking gap while it prepares or transits, preventing duplicate packages from being created at each simulation tick; coverage gaps after its effect end remain eligible for handoff planning.

The package records the active known-SAM threat site identities used during planning. Immediately before takeoff, execution refreshes current intelligence and revalidates every point and segment of the complete committed BARCAP route with the aircraft's conservative maneuver clearance. If the route is no longer safe, the grounded package is cancelled and the sustained request becomes actionable for a fresh materialization. Diagnostics distinguish a blocking site absent from the planning threat set from one already present during planning and record the route leg, station geometry, and commitment-to-takeoff age.

Spatial coverage and defensive capacity are distinct. Zero-coverage barrier tiles are tasked before covered tiles below doctrine's preferred station aircraft count. The final remaining spatial gap may receive the preferred count when no other barrier remains spatially open; later packages reinforce understrength segments. Projected strength is calculated from the aircraft assigned to every compatible overlapping flight.

The ten-nautical-mile protected-asset release standoff remains a fallback until hostile attack loadouts provide a better deadline. Generated BARCAP altitude does not yet search ACO blocks, terrain, or friendly engagement zones.

## Consequences

BARCAP flights now fly along the attack lane while collectively forming a barrier across the front. World-space tracks are stable when campaign tile scale changes. Faster aircraft fly longer doctrine-timed legs, and coverage estimates, route materialization, rendering, and rearward relocation share the same stored station geometry. The planner accepts some forward exposure when necessary to obtain engagement depth, then prefers rearward stations once that margin is satisfied.

Late intelligence changes no longer produce a predictable takeoff followed immediately by a SAM-driven recovery. They can instead cancel the grounded sortie and reopen the request. A blocker that was already in the planning threat set remains visible as a planning/revalidation discrepancy rather than being misclassified as an intelligence change.

Scarce aircraft still establish the broadest possible screen first. A singleton station is useful spatial presence but leaves a defensive-capacity gap that remains actionable until reinforced. Detailed ACO, fighter/missile engagement-zone, airborne-C2 latency, and hostile weapon-release modeling remain future work.
