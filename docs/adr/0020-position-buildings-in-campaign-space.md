# ADR 0020: Position Buildings in Campaign Space

## Status

Accepted

## Context

Buildings were independent runtime entities, but their authoritative placement was an operational-map `TileId`. Airports, static SAM sites, and building attacks therefore used the center of the containing tile for all physical geometry. This prevented distinct structures in one tile from having meaningful campaign-space positions and made tile membership both placement and spatial indexing.

The campaign already has a stable coordinate frame measured in feet, with X east-west, Y altitude, and Z north-south, plus deterministic conversion between this frame and flat-top cube-coordinate tiles.

## Decision

Every authored and runtime building stores one authoritative ground-level `PositionFeet`. Building Y must be zero until terrain elevation exists. `BuildingSystem` projects X/Z into a derived `TileId` when it builds its indexes, rejects non-finite, elevated, off-map, or ocean placement, and indexes buildings by both stable building ID and derived tile.

Tile-domain rules such as control, supply, rail presence, fort effects, and strategic tile queries continue to use derived tile membership. Physical rules such as airport routes, recovery distance, static radar and SAM geometry, known-threat envelopes, and ordnance release against a building use the exact building position.

Buildings do not persist separate owner, controller, or country state. They inherit the controller of their derived tile, so tile capture immediately changes every building in that area. A static air-defense building follows tile control while its hosted SAM site is disabled on capture rather than transferred operationally. Recapture alone does not reactivate the site; that requires a future explicit mechanic.

## Consequences

Buildings remain owned by the global building system, not by tiles. A tile index is a derived lookup and may be rebuilt without changing building identity or placement. Changing a building position requires rebuilding the spatial index; ordinary runtime buildings are static for now.

Multiple buildings may share a tile or exact position. Persistent building anchors do not add internal structure coordinates, blast-radius simulation, or persistent ground-attack clusters. Tile-center spacing is a fixed engine-wide 20 km invariant and is not authored in campaign templates; tile-centered campaign content uses the shared campaign-map coordinate conversion.
