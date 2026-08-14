# ADR 0021: Make Aircraft Loadout Stations Authoritative

## Status

Accepted

## Context

Aircraft loadouts were stored as aggregate counts by ordnance type and constrained by one aircraft-wide capacity plus a compatible-ordnance allow-list. The DCS exporter then guessed pylon placement from hardcoded aircraft and ordnance tables. A campaign-valid count could therefore be impossible to mount, and the exporter could place only part of it while continuing with a warning. An ordnance-level third-party ID also treated a DCS payload CLSID as though it identified only a munition even when it represented a rail, rack, adapter, quantity, and weapon together.

Campaign combat should remain aggregate and should not reproduce flight-simulator store geometry. Export nevertheless requires the exact external mounting choice, and future racks, pods, tanks, paired restrictions, and partially expended multi-store configurations require a distinction between a munition and its installed carriage.

## Decision

Each aircraft type owns stable simulator-neutral loadout stations and aircraft-specific carriage configurations. A station defines its authoring order, optional mirror, legal configuration IDs, and optional opaque third-party station mapping. A carriage configuration defines its complete ordnance contents, external-load cost, and optional opaque third-party payload mapping. Aircraft compatibility is derived from the configurations legal on its stations rather than authored as a separate ordnance allow-list.

Every planned and runtime external loadout item retains its station ID and carriage-configuration ID. At most one configuration may occupy a station, the runtime contents originate from that configuration, all contained ordnance must be permitted by the campaign, and the total configuration load cost must remain within the aircraft-wide capacity. Internal guns remain stationless installed inventory.

Loadout planners choose exact station configurations. Aggregate ordnance queries and employment continue to operate across their contents, preserving flight-level combat and individual-aircraft expenditure without adding pylon geometry to combat resolution. Station loads are the only mutable authority; aggregate counts are projections.

Scenario export snapshots preserve exact station assignments, carriage identity, and remaining contents. A sim adapter maps those assignments to its payload format. The DCS adapter maps station IDs to pylon numbers and carriage IDs to CLSIDs; it does not choose pylons. Missing mappings, duplicate pylon mappings, or a partially expended configuration that DCS cannot represent exactly fail export instead of dropping or restoring ordnance.

## Consequences

Campaign and exported payloads can no longer diverge because of adapter placement heuristics. Rails, racks, and multiple weapons on one station can be represented without changing ordnance identity, and simulator payload IDs no longer pollute reusable munition definitions. External-load capacity remains a useful whole-aircraft constraint and planning cost but does not establish mounting compatibility.

Aircraft catalogs must author station topology and legal carriage configurations. New cross-station constraints should be added only for demonstrated aircraft requirements rather than through a general loadout-rules language. A simulator that cannot represent a remaining rack state must reject that export until an exact realization is authored.
