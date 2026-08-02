# ADR 0018: Use Abstract Ground-Attack Opportunities

## Status

Accepted

## Context

Campaign tiles contain formations, positioned building anchors, air-defense sites, aircraft, and infrastructure, but the campaign does not store exact positions for their constituent tanks, trucks, infantry, runway aim points, or SAM components. The former air-to-ground executor modeled every pass as one store against one component. That could not represent multiple guided weapons in one pass, one area weapon affecting several nearby recipients, or repeated aim points against one broad target without introducing tactical-scale entities and geometry.

Future CAS, interdiction, strike, and offensive counter-air missions need the same employment machinery, but those mission planners do not yet exist.

## Decision

Before an air-to-ground pass, execution rolls a short-lived ground-attack opportunity from the assigned mission target's real composition and current state. The opportunity contains zero or more typed abstract recipients, a target tile, mission priorities, and a maximum useful release count. It is a transient attack-window snapshot, not persistent within-tile placement. A referenced building's persistent ground anchor may supply the release position without turning the opportunity or the building's internal parts into persistent tactical geometry.

Formation composition is authored through weighted ground-target profiles on battalion definitions. Division opportunities draw only from battalions present in that division template. Fixed structures, grounded aircraft, infrastructure, and air-defense components use typed references to their existing campaign entities. Mission-specific opportunity producers decide what can be exposed and how it is prioritized; the shared decision and employment services remain mission-neutral.

After an opportunity is known, the flight selects one carried ordnance type and a useful quantity. Target category, toughness, effectiveness, guidance compatibility, inventory, opportunity size, and authored weapon coverage constrain the choice. Point weapons assign one primary recipient per store. Area-capable weapons receive bounded, non-overlapping recipient groups; secondary effects use an authored multiplier and must independently satisfy effectiveness and toughness rules. Every primary delivery and secondary effect has a separate deterministic outcome and diagnostic record.

DEAD is the first active consumer. It rolls from authorized surviving components and may assign several exposed components in one pass while retaining existing fire-control-first priority, emitter requirements, mission-area limits, release-envelope checks, and delayed effect resolution. The generic executor can already resolve division, building, grounded-aircraft, and tile-infrastructure references, but no placeholder CAS, strike, or airport-attack mission types are added before they have planners and lifecycle rules.

## Consequences

Air-to-ground passes can vary from no useful exposure, to one store against one recipient, to several stores against several recipients, or one area store against several compatible recipients. The campaign still does not simulate individual vehicles, blast radii, component coordinates, or persistent sub-tile clusters. Persistent building anchors do not imply runway aim points, component layouts, or automatic proximity effects between neighboring buildings.

Adding a future mission requires an opportunity producer and its mission-specific priorities, not another ordnance-resolution pipeline. More detailed spatial simulation can later replace or enrich opportunity production while preserving target references, pass plans, ordnance selection, pending effects, and damage APIs.
