# ADR 0023: Use Abstract Runway Channels for OCA Strikes

## Status

Accepted

## Context

An OCA airport strike must be able to close one- and two-channel airports, destroy several exposed parked aircraft in one pass, and attack selected nearby facilities. The campaign does not model physical runways, individual crater coordinates, parking spots, or blast geometry. The inherited building-damage fraction used by ADR 0017 could reduce capacity, but it could not express independent channel closure or retain bounded damage depth for later repair duration.

## Decision

An airport derives one abstract runway channel from build levels 1–5 and two from levels 6–10. Each channel persists a damage level from zero through five. Zero is operational; the first damage point closes the channel; additional direct hits deepen damage without further reducing current capacity. The cap represents the point beyond which more abstract craters do not increase future repair burden. Repair behavior remains deferred.

An OCA `StrikeMissionPlan` locks one hostile airport and an authored desired damage depth. Every Strike flight has one explicit assignment: runway denial, aircraft on the ground, or authorized airbase facilities. Runway opportunities interleave channels at each depth and reserve one direct release per aim-point slot. Runway targets reject secondary area effects. Parked aircraft use ordinary rolled multi-target ground opportunities and may receive compatible area effects. Facility opportunities contain only explicitly authorized functional buildings in the airport tile.

Strike execution uses the existing preparation, release, pending-effect, deterministic hit, damage, and diagnostic pipeline. Preparing and pending direct effects count against runway slots, while active or pending parked-aircraft and facility targets are temporarily covered to avoid duplicate simultaneous attacks. Flights reassess the locked objective after effects resolve and recover when it is achieved, the effect window ends, the target is invalidated, or useful ordnance is exhausted.

Airport operations count only undamaged channels. Closing the last channel immediately revalidates departures and recoveries through the existing aggregate scheduling and diversion rules. Destroying grounded aircraft does not require runway damage, and runway or facility damage does not imply aircraft losses.

## Consequences

One pass can release several weapons across one runway, both channels, several parked aircraft, or several authorized recipients according to the weapon and opportunity. One bomb cannot produce several runway damage points through area coverage. The model supports a future repair-duration function based on capped per-channel damage without implementing repair now.

ADR 0017 remains authoritative for movement windows, reservations, launch closure, recovery priority, and diversion, but its integrity-fraction representation of runway damage is superseded. Physical runway identity, headings, crater locations, parking layout, repair crews, and tactical blast geometry remain out of scope.
