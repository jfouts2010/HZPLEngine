# Context

Canonical **domain vocabulary** for HZPL Engine.

| Document | Responsibility |
|----------|----------------|
| [`VISION.md`](VISION.md) | Target product and design intent |
| [`CONTEXT.md`](CONTEXT.md) | Domain vocabulary (this file) |

## Domain Vocabulary

### Module

A **Module** is the integration boundary for a specific third-party flight simulator. The player or author picks a Module before creating or loading a campaign template. That choice constrains which maps, unit types, and placements are available so everything in the template can be represented in the target simulator.

A Module bundles two concerns:

- **Module content catalog** — the maps, countries, and unit definitions available under that Module.
- **Sim adapter** — mappings from campaign entities to third-party simulator identifiers, plus (eventually) scenario export and mission-result import.

Modules change *what* exists and how capable those things are. They do not change *how* the core engine resolves combat during a dynamic campaign.

_Avoid_: using "module" to mean a Module when the codebase pattern "deep module" is meant (a small, isolated rule-owning component inside the core engine).

### Module content catalog

The maps, national rosters, and unit definitions supplied by a Module. When authoring or playing under a Module, only catalog entries from that Module may be used.

Each **module unit definition** is a human-authored representation of a unit as it behaves during runtime campaign play. Stats such as range, altitude ceiling, era, and role are defined in the Module; they are not read from the third-party flight simulator at runtime.

Example: a Korean War flight-sim Module and a 2010s+ flight-sim Module expose different aircraft and SAM systems, but both obey the same core SAM engagement rules.

### Third-party ID

An identifier on a module unit definition (or other mappable entity such as an airport) that tells the sim adapter which third-party asset to spawn or reference when exporting a playable scenario.

Third-party IDs are used at scenario-export time, not during normal runtime campaign simulation. The core engine resolves combat from module unit definitions; the third-party game resolves combat when the player flies the exported mission.

If module authoring is inaccurate, the same named unit can behave differently during runtime play than in the third-party simulator. That fidelity gap is an accepted consequence of keeping simulation rules inside the core engine rather than delegating stats to the third-party game.

### Sim adapter

The part of a Module that connects runtime campaign play to a third-party flight simulator: entity ID mappings (airports, aircraft types, etc.), scenario generation, and mission-outcome ingestion.

Organizational constructs such as wings, squadrons, battalions, and divisions do not require third-party mappings. Physical and platform-level entities do.

When the player is not flying an exported mission, the sim adapter is not involved in turn resolution. It participates when a sortie is exported for third-party play and (eventually) when mission results are imported back into the campaign.

The sim adapter may be a stub during early development when the project is focused on standalone dynamic-campaign simulation rather than export or import.

### Mappable entity

A campaign or catalog entity that carries a **third-party ID** so the sim adapter can place or reference the correct asset when exporting a sortie.

**Requires third-party ID on the module definition:**

- Aircraft type
- Airport / airfield
- Static SAM site and IADS components
- Mobile SAM / self-propelled SAM unit types
- Ground unit / vehicle types (when they may appear in an exported scene)
- Infrastructure that can appear in an exported scene (bridges, runways, fuel depots, and similar)
- Map / theater binding for the campaign template
- Weapon / ordnance types when the target simulator needs explicit loadout or store IDs

**Does not require third-party ID:**

- Organizational constructs: wings, squadrons, battalions, divisions
- Abstract campaign state: supply levels, doctrine labels, AI tasking metadata

**Live at export time (not prebuilt catalog mappings):**

- Airspace zones (CAP stations, ROZs, restricted areas, and similar) — derived from runtime campaign state when the scenario is generated, not authored as fixed third-party zone assets in the module catalog.

_Avoid_: pre-authoring every airspace zone in the module catalog when zones should reflect the live war at export time.

### Standalone Module

A first-class Module used for development and testing. It has a module content catalog and the same sim-adapter shape as real flight-sim Modules, but its sim adapter is a no-op: no scenario export or mission-result import.

Standalone uses the same core engine rules and campaign workflow as a DCS or MSFS Module. It exists so a dynamic campaign can be built and validated before third-party integration is implemented.

The project's current test content catalog is the Standalone Module until real flight-sim Modules are authored.

_Avoid_: treating Standalone as "no Module selected" or as sim-agnostic authoring outside the Module system.

### Core engine

The module-independent rule system that governs runtime campaign play: air operations, IADS behavior, sortie planning and execution, land combat, supply, intelligence, and turn progression.

Core engine rules stay the same across Modules. A less-capable SAM in one Module and a modern SAM in another interact through the same engagement, track-quality, and launch-authorization rules; only their stats and catalog identity differ.

### Ground formations

**Module country** — a country supplied by a Module content catalog. Campaign templates reference module country IDs for alliance assignment, national unit availability, and authored force structures; they do not define countries themselves.

**Battalion definition** — reusable authored combat stats for one battalion type available to a specific module country. In v1, battalion definitions are Module catalog items; future campaign templates may allow custom battalion definitions when a campaign needs units missing from the selected Module.

**Division template** — the authored full-strength structure of a division: a module-country-scoped collection of battalion definition references and counts. In v1, division templates are Module catalog items; future campaign templates may allow custom division templates for campaign-specific force structures.

**Division** — the movable ground formation represented on the campaign map during play. A division follows a division template and carries its full-strength combat capability during runtime play.

At full strength, a division's additive combat stats are derived from the sum of its battalion definitions multiplied by their counts. Division speed is the minimum speed among its battalion definitions, and division softness is a strength-weighted average of battalion softness values.

Division speed and full-strength combat stats are stable capabilities derived when the division enters runtime play. A division's tile ID is the tile it physically occupies now; in-progress movement toward another tile belongs to its current order or movement state rather than replacing its current tile ID.

**Ground order** — the persistent operational responsibility assigned to a division during runtime play, such as holding a critical tile, moving to a destination, attacking, supporting an attack, or retreating. Ground orders may carry rationale and other decision context for AI or future player-command systems, but they are resolved by core ground operation rules rather than by the division object itself.

**AI order intent** — a persisted label on a ground order that records the AI's reason for assigning that order, such as holding a front tile, refilling a front gap, staging for an assault, supporting an attack, or pinning adjacent defenders. AI order intent is not a separate ground order type; it explains why an existing hold, move, attack, or support attack order exists.

_Avoid_: treating AI role labels as independent order classes when they are planning intent on a ground order.

**Hold front intent** — an AI order intent for a division anchored as a non-relocating defender of a front tile. A division with hold front intent is not available for offensive reassignment while that tile remains its front responsibility.

**Hold edge intent** — an AI order intent for a division anchored on a front tile that has exactly one adjacent hostile-controlled land tile. A division with hold edge intent remains a defender, but may be eligible to attack that single adjacent hostile tile because its front responsibility has only one enemy edge.

**Projected defensive coverage** — a front tile is defensively covered when a combat-ready friendly division is physically holding it or when a friendly division has a still-valid defensive movement order whose AI order intent commits it to that tile. Offensive staging, ordinary relocation, and non-defensive movement do not count as projected defensive coverage.

_Avoid_: counting every in-progress move through or toward a front tile as defense.

**Front coverage guarantee** — the defensive rule that every front tile should have at least one projected defender before the AI spends divisions on offensive plans. It answers whether the front has gaps, not whether every tile has enough strength for its local threat.

**Threat reinforcement** — the defensive rule that assigns extra divisions to already covered front tiles whose local danger or strategic value calls for more than the minimum defender. Threat reinforcement is subordinate to the front coverage guarantee and may be left incomplete when reserves are scarce.

**Tile strategic value** — the AI's estimate of how valuable a land tile is to control. In v1 it may be derived from existing map features such as forts, other buildings, tile infrastructure, and terrain context; future supply rules may add to this value rather than replace the concept.

_Avoid_: using supply criticality as the general term before supply exists.

**Defensive reserve** — a combat-ready friendly division that is available to receive defensive AI orders because it is not the sole physically present defender of a front tile, not already committed to a non-replaceable order, not retreating, and not engaged in ground combat. A division on a front tile may donate to an uncovered front tile only when its source tile has at least one other eligible defender physically present.

_Avoid_: using a projected incoming defender to justify pulling the last physically present defender off a front tile; this can create timing holes and oscillating orders where the original tile immediately requests the same strength back.

**Offensive plan** — the alliance AI's persistent coordination state for one chosen hostile front-adjacent target tile. In v1, an alliance may have at most one active offensive plan at a time, and offense only begins after the front coverage guarantee is satisfied; individual division ground orders record their assigned execution intent, but the plan itself belongs to the alliance AI.

**Offensive assembly phase** — the phase of an offensive plan where assigned divisions move to friendly staging tiles adjacent to their assigned engagement target. The AI should not issue the coordinated attack until every assigned division is in position and the plan has finished assembly or has been explicitly replanned.

**Offensive attack phase** — the phase of an offensive plan where assembled divisions execute their assigned attack, support attack, or pin responsibilities against hostile-controlled target tiles.

**Offensive replan** — the cancellation of an active offensive plan so the alliance AI can choose a fresh target and assignments on a later planning pass. In v1, an unavailable assigned division, invalid staging tile, invalid target, or failed feasibility check causes the whole offensive plan to abort rather than substituting individual divisions mid-plan.

**Current order** — the one active ground order carried by a division at a given moment. Every runtime division should have exactly one current order; when an order completes, the system assigns a default hold/no-action order rather than leaving the division without order state.

**Move order** — a movement ground order for a division to relocate toward a final destination tile through a current movement target tile. Combat is not the intended responsibility of a move order, though ground operation rules may still need to resolve what happens if hostile contact interrupts or blocks the move.

**Hold order** — a ground order for a division to remain responsible for the tile it currently occupies. A hold order does not carry a separate target tile; the division's tile ID defines the place being held.

**Attack order** — a specialized movement ground order for a division to enter and seize a target tile. A division attacking from one tile toward a hostile-held neighbor contributes to combat while still physically occupying its origin tile and may make movement progress during combat, but final arrival into the target tile is blocked while hostile defenders still hold it. A failed attacker halts on its current tile rather than retreating.

**Support attack order** — a ground order for a division to engage a neighboring target tile without intending to move into or seize that tile. Support attack divisions are full ground combat participants for frontage, firing, return fire, losses, and attack failure, but they cannot capture the target tile or change its control by themselves; the target tile must neighbor the supporting division's current tile. A failed support attacker halts on its current tile rather than retreating.

**Tile capture** — the change of tile control caused by a division physically arriving in a tile under a movement or attack order when no hostile divisions are present. A tile cleared by support attack remains under its existing control until AI or another assigning system orders a division to move or attack into it.

**Defending tile** — the tile being attacked in a ground combat. There is at most one active ground combat per defending tile; additional attack or support attack orders against that tile join the existing combat for that tile.

**Tile occupancy** — the divisions physically present on a tile. Non-retreating divisions on the same tile should belong to a single alliance that matches tile control; hostile divisions may overlap on a tile only when at least one side is retreating through or out of that tile.

**Ground combat** — an active battle centered on one defending tile. There is at most one ground combat per defending tile; additional normal attacks or support attacks against that tile join the existing ground combat rather than creating separate battles.

**Combat-ready division** — a division that can currently participate in ground combat: it is not retreating, has at least 1 current strength and at least 1 current organization, belongs to the relevant combat alliance, and satisfies the role-specific position or order requirement for that combat.

**Front-line division** — a combat-ready division selected to actively fire and absorb shots in a ground tactical combat round because it fits within its side's combat width for that round. Front-line assignment uses first-fit ordering: a division that does not fit goes to reserve, but later smaller divisions may still fill remaining width. If a side has combat-ready divisions but none fit, its first combat-ready division fights over-width so every combat-ready side has at least one front-line division.

**Reserve division** — a combat-ready division present in a ground combat but not selected as front-line for the current ground tactical combat round because its side's combat width is already filled. Front-line and reserve assignment is recalculated each combat round.

**Combat width** — the maximum division width that may fight on a side's front line during one ground tactical combat round. Combat width is derived from the defending tile terrain; when combat-ready attackers participate from more than one distinct current physical tile, the increased frontage applies to both attackers and defenders.

**Advancing** — the movement state of a successful attacking division that is closing the remaining distance into its attack target after defenders have lost or begun retreating. An advancing division captures the target tile only when it physically arrives there.

**Retreating** — the forced movement responsibility of a non-combat-ready defending division moving from its defended tile toward a valid friendly destination. Retreating uses normal movement behavior but is system-assigned, explicitly marked as retreat movement, and cannot be changed like an ordinary move order; a retreating division cannot contribute to combat and is destroyed if no valid retreat destination exists or if its retreat destination is captured by a hostile alliance before it arrives. Combat-ready defenders on the same tile continue defending while broken defenders retreat.

**Overrun** — destruction of a retreating division because its retreat destination is captured by a hostile alliance before the retreating division arrives there.

**Division starting condition** — a campaign-template entry that places one starting division instance on a tile at turn zero. It references a module division template, a module country, and a starting tile; it does not duplicate the division's derived combat stats.

### Campaign template

What an author creates in the campaign editor after choosing a Module. A campaign template defines the starting premise for play under that Module. The Module is fixed for the lifetime of an edit session; it cannot be changed while editing an open template.

**Static premise** — fixed across every play from this template: map layout and extent, **template tile** geography (terrain, surface, urbanization, forest cover, edge properties, hex neighbors), sides, Module-scoped unit availability, authored building placement, and other geography that should not change because the story shifted.

**Starting conditions** — fixed every time a player starts from this template: **starting tile** data (initial tile control and tile infrastructure build/damage values), starting building build/damage values, initial unit locations and strengths, starting air wings and squadrons, **campaign start calendar** (`CampaignStartTime` — in-world date and time at turn zero, authored on the template not the Module), and other day-zero force dispositions.

A v1 campaign template directly contains tile definitions for static tile geography, starting tile state for day-zero tile control and tile infrastructure values, and authored building data for building placement and starting building values. Additional map aggregate models should be introduced only when a real rule needs them.

Building categories are core-engine concepts, not Module-specific template classes. A Korean War campaign template and a Gulf War campaign template use the same building options; the template records which building types are placed where and their starting build/damage values. V1 building types are airport, factory, supply hub, fort, port, railroad, refinery, and power plant. Static SAM sites are not buildings; they belong to a separate future IADS implementation. Any future third-party export mapping for buildings belongs to the sim adapter/export process, not to the tile implementation.

The v1 tile/building implementation includes both template authoring data and runtime campaign state. Starting tile and building data are copied or instantiated into runtime state when play begins; runtime systems mutate the runtime state rather than the campaign template.

Once play begins, the starting order of battle is historical context only. Runtime simulation cares about current unit locations, strength, destruction, tile control, airport operational status, and the rest of the live war state — not re-reading the template’s starting conditions each tick.

### Tile

A hexagonal cell on the operational hex map. Tiles are hexagons in the model, visuals, and code; they should not be represented as square grid cells. Each tile's cube coordinate is its stable identity within a campaign template's grid. Geography on a tile is immutable during play; control and assets on the tile evolve.

**Tile ID** — the tile's `Vector3Int` cube coordinate. Tile ID is the source of identity for references, neighbor lists, building placement, and runtime state. Campaign templates are authored with stable coordinates; changing a tile's coordinate is an identity change.

**Template tile** — static geography authored on the campaign template (terrain, tile surface, urbanization, forest cover, hex neighbors, river crossings). Same every play from that template.

Template tile definitions use one class for both land and ocean tiles. Land/ocean differences are represented by tile surface and by polymorphic runtime tile state, not by polymorphic tile definitions.

**Tile neighbors** — the static adjacent hexes for a tile. Neighbor coordinate IDs are stored on the template tile definition as part of hex-grid geography and do not change during play. Runtime systems read these authored neighbor IDs rather than recalculating adjacency.

**River neighbors** — neighboring tile coordinate IDs that have a river crossing between them and this tile. River data is stored on the template tile definition next to the neighbor list so systems can answer “does this move cross a river?” without a separate edge-property collection. River neighbor IDs should reference existing tile neighbors.

**Terrain** — the tile's base static geography subtype. Terrain is a simple enum with land values such as plains, hills, mountain, desert, tundra, and coast, plus ocean values such as ocean, shallow ocean, or deep ocean. Coast is a land terrain type where land transitions to ocean within the tile. It is treated as a land tile for control, movement, infrastructure, and building placement, but may unlock coastal capabilities such as ports.

**Tile surface** — the tile's broad land/water classification used for simple pathfinding gates. Land units can path through land tiles and cannot path through ocean tiles unless a future rule explicitly allows it. Tile surface is stored separately from terrain so land/water checks do not need to infer from every terrain subtype. Land tiles have tile control; ocean tiles do not. Validation should ensure land surface uses land terrain values and ocean surface uses ocean terrain values.

**Urbanization** — a land tile's static settlement pattern layered on top of terrain. Urbanization is a simple enum such as rural, suburban, or urban. It is separate from tile infrastructure: an urban tile usually has high infrastructure, but urbanization describes settlement character while infrastructure describes built-up travel and logistics capability. Ocean tiles do not have meaningful urbanization values.

**Forest cover** — a land tile's static tree cover layered on top of terrain. Forest cover is a simple level such as none, light forest, or heavy forest. Ocean tiles do not have meaningful forest cover values.

**Starting tile** — day-zero political and asset state authored on the campaign template (initial tile control, tile infrastructure level, per-building starting state, and similar). Copied into runtime state when a campaign begins; the template is not re-read each tick during play.

**Tile state** — runtime mutable state for a tile. Tile state is polymorphic so land and ocean rules are explicit in the model.

**Land tile state** — runtime tile state for a land tile. Land tile state carries tile control and tile infrastructure values.

**Ocean tile state** — runtime tile state for an ocean tile. Ocean tiles may still have tile state records so runtime arrays and save/load can align with tile definitions, but ocean tile state is explicitly non-territorial. Ocean tile state does not carry controller, tile infrastructure, or building-use values.

**Movement cost** — the derived cost or difficulty of moving across or within tiles. Movement cost may use terrain, tile infrastructure functional level, buildings, rivers, and future modifiers, but it does not add or remove tile neighbors.

### Alliance

The gameplay faction that controls territory and participates in the war at the operational level. Alliance is a fixed enum with exactly three values: Bluefor, Redfor, and Neutral. These values are not authored per template and do not change.

Countries may exist as lightweight political or content metadata. A campaign template assigns countries to alliances, but tile control belongs to alliances in v1.

Neutral can control tiles the same way Bluefor and Redfor can. Neutral is not an active war participant by default, and Bluefor/Redfor cannot use neutral-controlled tiles or buildings unless future access rules explicitly allow it.

### Tile control

The alliance that militarily holds a land tile — movement, combat, and occupation rights derive from tile control. Every land tile has a controller. Ocean tiles never have a controller.

In v1, tile control is the only political field on a tile. It stands in for ownership: supply, production, and scoring benefits apply to the controlling faction as if they owned the tile.

In a later phase, **tile owner** (original or legal affiliation, fixed at campaign start) may diverge from tile control. A faction that controls a tile it does not own is **occupying** it and does not receive the normal supply or production benefits of that tile. A faction that owns a tile it does not control does not receive those benefits either.

_Avoid_: using “owner” in v1 data or rules when tile control is the sole authority.

### Front

For a given alliance, the **front** is the set of land tiles **controlled by that alliance** that share a hex edge with at least one land tile controlled by a **hostile** alliance. Front membership is derived from current tile control and static tile neighbors; it is not authored on the campaign template.

In v1, only Bluefor and Redfor are hostile to each other. Neutral is hostile to no alliance, so Neutral has no front tiles, and a Bluefor- or Redfor-controlled tile that borders only Neutral-controlled land is not a front tile.

Ocean tiles never appear on a front: they have no tile control.

_Avoid_: putting enemy-controlled tiles in an alliance's front set, treating both sides of a contact line as that alliance's front, or counting neutral-adjacent tiles as front tiles unless explicit hostility rules are added later.

### Tile infrastructure

The abstract built-up travel and logistics capability **within** a land tile — road density, urban development, ease of movement inside the hex. Not a list of named structures.

High tile infrastructure represents cities and developed areas where forces move easily. Low tile infrastructure represents rural or backcountry terrain with poor internal travel even if the hex is passable at the map level.

Tile infrastructure is land-only. It is authored on the campaign template as part of starting land tile data and tracked on land tile state with the same value model as buildings:

- **Build level** — authored capacity at day zero and the current built capacity during play. Build level never decreases; it may increase during play when infrastructure construction is implemented. Capped at 0-10.
- **Damage** — runtime wear to the tile's general travel and logistics capability. Capped at build level and never goes negative.
- **Functional level** — derived, not persisted: `max(0, buildLevel - damage)`. Used for movement, supply throughput, combat modifiers, and similar rules.

Runtime infrastructure construction is planned for a later phase; v1 may author build level and apply damage before construction rules exist.

_Avoid_: treating tile infrastructure as a synonym for **building** or as a catch-all for ports, factories, and airports.

### Building

A discrete, placed asset on a tile with its own type and identity. V1 building types are airport, factory, supply hub, fort, port, railroad, refinery, and power plant.

Railroad is represented as a building in v1. Future supply and pathfinding work may introduce rail connectivity as tile edge properties or a transport network when rules need connected rail lines rather than tile-local rail presence.

Buildings are authored on the campaign template (which building types exist on which tiles). Runtime state tracks each building instance separately so one asset on a tile can be damaged while another on the same tile is untouched.

Buildings may only be placed on land tiles. Ports cannot be placed on ocean tiles. Specific coastal placement validation for ports is not enforced in v1.

**Building ID** — a stable GUID-based identifier for a building instance. A building also records its `Vector3Int` `TileId` placement and building type. Systems may index buildings by tile for efficient lookup, but building identity belongs to the building instance rather than to tile data.

Multiple building instances may exist on the same tile, including multiple buildings of the same type. Rules that need buildings on a tile query by `TileId` and then filter by building type or capabilities.

Buildings inherit control from their tile. In v1, buildings do not carry separate owner or controller state; a building's usable faction is determined by the current tile controller and the building's functional level. When tile control changes, building control changes immediately with no separate capture delay or automatic damage.

Building categories and their runtime classes belong to the core engine. Runtime buildings share an abstract building concept for common identity, placement, build level, damage, and functional level. Specific building categories may have their own runtime classes when their behavior or state differs meaningfully. Airports are expected to be specialized and relatively complex; forts may remain simple specialized buildings.

Buildings are owned by a building collection or building system rather than stored inside tile data. Tile data may reference or query buildings by coordinate `TileId`, but tiles are not the aggregate root for building state.

Each building is tracked with two persisted integers:

- **Build level** — authored capacity at day zero and the current built capacity during play. Build level never decreases; it may increase during play when construction is implemented. Capped at 0–10.
- **Damage** — runtime wear from bombing and similar effects. Damage is per building. Capped at build level and never goes negative.
- **Functional level** — derived, not persisted: `max(0, buildLevel - damage)`. Used for supply throughput, production, combat modifiers, strike targeting, and export when the building is a **mappable entity**.

`cityType` is not a leveled property in v1. Whether a tile reads as urban is inferred from tile infrastructure level and building mix, not a separate persisted flag.

Supply hubs are a **building type** when explicitly placed on a tile. Hub effectiveness may still degrade through supply-line damage rather than direct hub bombing in v1 — specific rules TBD.

_Avoid_: using “infrastructure” alone when you mean either tile infrastructure or a specific building.

### Dynamic campaign

A campaign **archetype**, not a save file or runtime object name. A dynamic campaign is a living, unscripted simulated war: the conflict progresses on its own, units and logistics are tracked, and outcomes reshape what happens next.

Victories and failures have consequences — destroyed bridges, depleted supply, lost airbases, and shifted frontlines change what forces can do and what missions become possible. The design goal for this project is this archetype: a realistic, highly detailed air-and-ground war that can run without player intervention, with optional third-party flight missions layered on top later.

_Avoid_: using "dynamic campaign" to mean the runtime save blob, the campaign template, or any specific serialized class name.

### Player role

During current development, play is **autonomous**: all factions are AI-controlled and the war advances with no human input on the operational map or in turn resolution. The human is an observer validating that the dynamic campaign behaves realistically.

This is a phase choice, not the long-term product ceiling. Future phases may add human command of a side, pilot intervention through exported third-party missions, or other roles. Those modes should layer on top of the same core engine and dynamic-campaign archetype rather than replacing them.

_Avoid_: assuming a human player must issue orders each turn for the war to progress.

### Air-primary simulation

The dynamic campaign is **air-primary**: air warfare is the detailed simulation — sorties, packages, IADS, SEAD/DEAD, track quality, SAM execution, and related tactical air behavior.

Ground warfare is **operational and abstract**: divisions move on hexes, fight, hold territory, and affect supply at lower fidelity. Ground exists primarily to shape the air war: frontlines, air targets, logistics consequences, airbase capture, and territory control that air operations must respond to.

Air and ground must interact (ground state influences air tasking; air outcomes feed back into ground and infrastructure), but ground does not need flight-sim-level fidelity. Deepening ground simulation beyond this operational layer is out of scope until far later.

_Avoid_: treating division hex combat and air sortie execution as equally detailed simulations in the near term.

### Simulation tick

The single campaign clock step. The target design is one high-fidelity clock, not separate unrelated turn and slice clocks.

Simulation tick length is template-configurable within engine bounds (**one to ten minutes** of in-game time; **default five minutes**). Air warfare execution resolves every simulation tick. Ground warfare uses the same clock but splits work by cost: lightweight combat resolution may run every tick; expensive ground decisions run less often on a longer cadence measured in the same clock.

_Avoid_: separate turn clocks for air and ground unless a future ADR explicitly chooses that trade-off again.

**Game turn** — runtime synonym for one simulation tick. Use simulation tick in domain language when clarity matters; game turn may remain as code or UI wording when it means the same clock step.

**Ground tactical combat round** — one execution of active ground combat resolution during a simulation tick.

### Ground operational cadence

How often expensive ground decision logic runs on the simulation tick clock: objective selection, order assignment, retreat destination selection, and other planning work that does not need to be recalculated every tick.

Ground operational cadence is template-configurable within engine bounds (**one to six hours** of in-game time; **default six hours**). Many simulation ticks pass between ground operational updates.

Between ground operational updates, active ground combat and movement progress can still resolve every simulation tick without full strategic replanning every tick.

### Ground tactical combat

Lightweight ground resolution that may run on most simulation ticks when opposing forces are engaged — for example, attrition or control pressure while two tiles are fighting. This keeps the frontline alive between operational movement decisions without running full division AI every tick.

### Tick resolution order

Within each simulation tick, resolution order should stay deterministic. Air picture, air execution, air-to-ground effects, active ground combat, and ground movement progress participate every tick. Expensive ground planning and order assignment run only on ticks that fall on a ground operational cadence boundary.

### Air planning cadence

How often the air tasking layer performs heavy planning — mission request generation, package building, route and TOT planning, and wing or squadron tasking.

Full air planning runs on a longer cadence aligned with ground operational cadence — template-configurable within engine bounds (**one to six hours**; **default six hours**) — and may also run when significant campaign events trigger replanning, such as major airbase loss or a collapsed SAM belt.

Air **execution** (sortie movement, IADS refresh, engagement assignment, SAM launch resolution, and air-to-ground effects) runs every simulation tick. Tick-level **adjustments** to already-active air operations — scrubbing packages, rerouting en-route sorties, immediate reactions to new threats — may run without a full replan.

_Avoid_: full theater-wide air replanning every simulation tick.

### Exported scenario

A third-party flight-simulator mission generated from runtime campaign state when a player flies instead of watching autonomous simulation.

Export scope is the **scenario area**: every aircraft currently flying in that area and every ground unit in that area are placed in the exported mission. Exactly **one** aircraft is the player's; all others are AI-controlled in the third-party simulator.

The scenario area is the **whole active battlespace** — not a small local radius around one sortie.

While the player is flying an exported scenario, the **campaign clock pauses** until mission results are imported. The wider war does not advance in core engine simulation during third-party play.

The sim adapter maps **mappable entities** to third-party IDs and derives **live airspace zones** from runtime state at export time.

Not in scope for current autonomous testing. Standalone Module uses a no-op adapter.

_Avoid_: exporting only the player's flight while omitting other active aircraft and ground units in the same scenario area.
_Avoid_: continuing to simulate the same exported entities in the core engine while the player is in the third-party simulator.

### Active battlespace

The **full campaign map** for the loaded campaign template. It is the scenario area for an **exported scenario**: every aircraft flying anywhere on the map and every ground unit on the map are candidates for export, not a local bubble around one sortie.

While the player flies, the campaign clock pauses for the entire map because export scope equals the full battlespace.

### Mission result import

The future flow that reads outcomes after an **exported scenario** and applies them back to runtime campaign state.

Import includes **literal outcomes** — losses, damage to mapped targets, ordnance expended, and other facts reported from the third-party session.

Import may also include **mission performance evaluation** — a graded assessment of how well the player accomplished objectives. Performance can drive campaign-level decisions or effects for the player beyond raw damage (for example tasking priority, restrictions, or rewards). Specific performance rules and player effects are not designed yet and should not block current autonomous simulation work.

_Avoid_: campaign turn (old meaning: coarse ground step that owns air slices), air execution slice (old meaning: sub-turn air clock parallel to ground turns)

### IADS current track

An IADS current track is an air contact that a site or network is currently aware of through direct detection or shared cueing. Current track awareness is not authorization to fire.

Remote cueing may add current track awareness for another site, but remote cueing alone is not enough to authorize a SAM launch.

### IADS engagement assignment

An IADS engagement assignment is the command decision that commits a SAM site to fire-control action against a current track. SAM launches should be based on engagement assignments, not merely on current track awareness.

Remote engagement may allow a shooter to receive an engagement assignment against a shared track when doctrine, network quality, and track quality support it.

The IADS commander layer owns engagement assignment. SAM launch execution consumes assignments and resolves whether assigned shots occur; it does not choose targets itself.

### IADS commander refresh

IADS commander refresh is the update of tactical IADS commander decisions for static air-defense sites: suppression decay, network membership, EMCON and radar posture, track and engagement reset, IADS current track assignment, and IADS engagement assignment. It reflects what a human IADS commander would decide before shooters fire, not threat-field products for route planning, SAM launch resolution, or per-slice emission bookkeeping for debug or EMCON history.

Most commander decisions refresh each simulation tick because the air battle is tactically dense. Commander inputs that change slowly or need not be re-evaluated every tick may refresh on an operational cadence boundary instead.

Suppression decay is part of IADS commander refresh and runs on the tactical clock (each simulation tick in the target model). It follows a tactical 30-minute half-life, so suppression fades according to elapsed in-game time rather than turn or slice count.

IADS network topology is regenerated on an operational cadence boundary by default. When IADS network topology is invalid, topology is also regenerated at the start of that tick's IADS commander refresh (after sortie movement, before current tracks and engagement assignments).

### IADS network topology invalidation

IADS network topology invalidation means the static air-defense mesh must be rebuilt because layout or network roles changed: a site left the fight, a command or relay role was lost, or another change that alters who can share tracks or support remote engagement. Suppression, EMCON, partial damage that does not remove a network role, and per-slice track or assignment clears do not invalidate topology.

Invalidation is cleared when topology is regenerated. Regeneration runs on an operational cadence boundary and, when invalid, again at the start of the tick's IADS commander refresh.

_Avoid_: network dirty flag (implementation name), full IADS refresh

### Weapon-quality track

A weapon-quality track is an IADS current track precise enough to support a SAM launch. Remote SAM launches require a weapon-quality track, typically provided by a live fire-control-capable site or an equivalent organic shooter sensor; search-only and passive-only tracks provide awareness or cueing until a fire-control-quality source upgrades the track.

Organic launcher guidance can make a track weapon-quality for that launcher site's own assigned shot. Organic launcher guidance does not upgrade shared tracks into weapon-quality tracks for other shooters.

### Weapon-quality for shot

Weapon-quality for shot is whether a specific SAM site may treat an IADS current track as weapon-quality for that site's own engagement assignment or SAM launch. It is evaluated per shooter and per track, not only from the track's shared quality label.

A weapon-quality track on the air picture does not by itself mean every networked launcher has weapon-quality for shot; remote engagement still requires doctrine, network support, and a valid fire-control source on the network.

Weapon-quality for shot is one input to SAM launch authorization, not the whole decision. Full launch eligibility (ammo, reload, envelope, channels, role status, and other gates) belongs in a separate launch-authorization step that composes multiple checks; it should not be folded into the weapon-quality for shot module.

### SAM launch execution

SAM launch execution is the resolution of assigned SAM engagements after sortie movement has updated target positions. It validates whether assigned sites can still fire and resolves launch outcomes without choosing engagements itself.

SAM launch execution happens once per simulation tick after all live sorties have moved for that tick, so all launch decisions use the same updated air picture.

Within a tick, sortie movement updates the air picture first, IADS detection and engagement assignment refresh second, and SAM launch execution resolves assigned shots third.

SAM launch execution is site-driven: SAM sites are the actors that consume assigned engagements and fire. Debug output should still make package and sortie exposure legible by showing whether a package was fired at and, when it was not, why assigned or plausible SAM sites did not launch.

Package-level SAM debug should answer why a package was or was not fired on without listing every site unless drill-down detail is requested.

### SAM site role status

SAM site role status describes the remaining combat contributions of an air-defense site by role rather than by one broad operational flag. A site may still contribute sensors, shooters, or command/network support independently as component damage changes.

Use `CanContributeSensor`, `CanContributeShooter`, `CanContributeCommand`, `IsCombatIneffective`, and `IsTemporarilySuppressed` style concepts for air-defense behavior. Avoid treating a single `IsOperational` flag as the source of truth for detection, engagement assignment, or SAM launch execution.

Suppression is temporary behavior degradation, not permanent combat ineffectiveness. A suppressed site may lose or reduce local sensor/command contribution while still contributing a live launcher if another suitable site provides weapon-quality guidance through remote engagement.

By default, suppression primarily degrades local emissions, search, fire-control contribution, command/network quality, and launch tempo. It should decay over time so intact components can recover without repair.

### Remote cueing

Remote cueing is network-shared track awareness that helps a site acquire current tracks. Remote cueing alone does not authorize a SAM launch.

### Remote engagement

Remote engagement is network-authorized fire-control use of a shared track by a shooter that did not directly detect the target. Remote engagement requires supporting doctrine, network capability, and sufficient track/network quality.
