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

Tile strategic value is the shared base value for both offensive target selection and defensive priority. Offense asks whether an enemy tile is worth taking; defense asks whether a friendly front tile is costly to lose.

**Defensive reserve** — a combat-ready friendly division that is available to receive defensive AI orders because it is not the sole physically present defender of a front tile, not already committed to a non-replaceable order, not retreating, and not engaged in ground combat. A division on a front tile may donate to an uncovered front tile only when its source tile has at least one other eligible defender physically present.

_Avoid_: using a projected incoming defender to justify pulling the last physically present defender off a front tile; this can create timing holes and oscillating orders where the original tile immediately requests the same strength back.

**Offensive plan** — the alliance AI's persistent coordination state for one chosen hostile front-adjacent target tile. In v1, an alliance may have at most one active offensive plan at a time, and offense only begins after the front coverage guarantee is satisfied; individual division ground orders record their assigned execution intent, but the plan itself belongs to the alliance AI.

**Offensive feasibility gate** — the minimum projected combat chance an offensive target must satisfy before tile strategic value is considered. Strategic value ranks viable targets; it should not cause the AI to launch attacks it estimates as hopeless.

**Offensive assembly phase** — the phase of an offensive plan where assigned divisions move to friendly staging tiles adjacent to their assigned engagement target. The AI should not issue the coordinated attack until every assigned division is in position and the plan has finished assembly or has been explicitly replanned.

**Offensive attack phase** — the phase of an offensive plan where assembled divisions execute their assigned attack, support attack, or pin responsibilities against hostile-controlled target tiles.

**Offensive replan** — the cancellation of an active offensive plan so the alliance AI can choose a fresh target and assignments on a later planning pass. In v1, an unavailable assigned division, invalid staging tile, invalid target, or failed feasibility check causes the whole offensive plan to abort rather than substituting individual divisions mid-plan.

**Late offensive assist** — an hourly combat-cadence check during an active offensive attack phase that may add newly available friendly divisions as support attackers only when they are already on friendly-controlled tiles adjacent to the target tile. Late assists add local combat help without reopening assembly or pulling distant reserves into the offensive.

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

### Air formations

**Aircraft type** — reusable authored identity and capability data for one aircraft model or variant in a Module catalog. Aircraft type definitions are not country-scoped; a campaign template gives an aircraft type to a country by creating a squadron for that country that uses the aircraft type.

Aircraft type capability data includes flight performance, range and endurance, sensors and defensive qualities, ordnance capacity and compatibility, and explicit support capabilities where applicable. Required point-mass performance consists of cruise and combat speed in knots, climb and descent rate in feet per minute, turn rate in degrees per second, and nominal cruise altitude and service ceiling in feet; these values are authored per aircraft type rather than inferred from mission role. The third-party ID remains an opaque export mapping rather than a source of campaign capability.

_Avoid_: using "airframe" for the Module catalog concept; in this project, airframe can sound like a physical aircraft body, a flight-model implementation, or an individual tail.

**Squadron** — the campaign air formation that owns aircraft inventory for one aircraft type and is based at a starting airport building instance. Squadrons do not require third-party IDs; their aircraft type and airport references provide the mappable simulator entities when sorties are exported.

**Squadron starting condition** — a campaign-template entry that creates one starting squadron at turn zero. It references a module country, aircraft type, and starting airport building ID, and records the squadron's starting aircraft count. Starting squadrons are assumed to have all aircraft ready; missing, damaged, or unavailable aircraft belong to later runtime state.

_Avoid_: adding air wings before a rule or authoring workflow needs an organizational layer above squadrons.

**Air mission capability** — an aircraft type's authored ability to contribute to a requested air effect. Airborne C2 and aerial refueling require explicit support capabilities; air-to-air and future DEAD suitability are evaluated from relevant aircraft performance, sensors, survivability, and compatible ordnance.

One aircraft type may provide multiple air mission capabilities. Capability does not permanently assign an aircraft type to a mission role; the air planner evaluates suitability for the current request and situation.

In v1, AWACS and tanker aircraft are dedicated support aircraft: an AWACS-capable aircraft fulfills airborne-C2 requests, a tanker-capable aircraft fulfills aerial-refueling requests, and combat aircraft do not substitute for either capability.

_Avoid_: fixed DCA, OCA, or DEAD aircraft-role labels when suitability can be derived from capabilities and loadout.

### Ordnance

**Ordnance type** — reusable authored identity data for one weapon or store in a Module catalog, such as AIM-120, GBU-38, or AGM-88. Ordnance types are mappable entities when the target simulator needs explicit loadout or store IDs. Module authors define each store's **ordnance weight** and how effective it is against each **ordnance target category**.

**Ordnance weight** — the capacity cost of one store on an aircraft loadout. Mixed loadouts are valid when the sum of carried ordnance weights is within the aircraft type's **ordnance capacity**.

**Ordnance effect power** — the coarse campaign effect strength of one store. Effect power is the v1 stat used to decide whether ordnance can meaningfully affect a target's toughness. It may correlate with warhead size or explosive power, but it is an authored campaign abstraction rather than exact physics.

**Ordnance capacity** — the maximum total ordnance weight an aircraft type may carry. Aircraft type definitions own ordnance capacity and a compatible ordnance allow-list. A store may be loaded only if it is on that aircraft type's allow-list and the loadout stays within ordnance capacity.

_Avoid_: bidirectional aircraft–ordnance compatibility lists that must be kept in sync on both ordnance and aircraft definitions.

**Ordnance target category** — the class of target a store is evaluated against. Ordnance capability is expressed as **ordnance effectiveness** ratings per target category, not per mission role.

V1 ordnance target categories:

- **Infantry**
- **Vehicle** — ground vehicles; target toughness distinguishes light vehicles from heavily armored vehicles
- **Building** — fixed structures, installations, and infrastructure
- **Aircraft** — airborne targets
- **Radar** — emitting air-defense sensors and similar radiating targets
- **Ship** — naval surface targets

**Ordnance employment category** — the loadout-composition role of an ordnance type, such as radar-guided air-to-air, infrared air-to-air, anti-radiation, precision air-to-ground, or unguided air-to-ground. Employment category is separate from **ordnance target category**: target category describes what the store can affect, while employment category describes why a loadout planner would include it.

_Avoid_: weapon category when the concept is specifically classifying air-launched ordnance for loadout planning.

**Ordnance effectiveness** — a 0–1 rating of how well an ordnance type performs against one ordnance target category. Sortie and loadout planning compare effectiveness against expected target categories rather than mission-role tags. In code, effectiveness is keyed by ordnance target category so lookups answer "how effective is this store against vehicles?" directly.

_Avoid_: using "weapon" alone when the concept is specifically air-launched stores modeled in the third-party sim; ground unit armament belongs to battalion definitions unless a future rule needs separate treatment.

**Campaign template ordnance allowance** — the subset of module ordnance types permitted in this campaign story. Campaign authors may allow or disallow specific ordnance regardless of the campaign's calendar year or the full Module catalog.

In v1, ordnance allowance is scoped per **alliance**. Each alliance on a campaign template has its own allowed ordnance list, represented as a dictionary keyed by alliance. A store must be both defined in the Module and allowed for that alliance before it may be loaded.

_Avoid_: scoping v1 ordnance allowance per squadron or per module country unless a future rule needs national ROE differences within the same alliance.

**Ordnance availability** — runtime logistics state for whether permitted ordnance can be drawn and loaded at an airport or squadron now. Availability is separate from allowance: a permitted store may be unavailable because the base is out of supply or local stock is depleted.

In v1, ordnance availability rules are not enforced. If ordnance is allowed for a side and a loadout is requested, the stores are granted instantly with no rearm step and no airbase stock check.

**Campaign aircraft** — one persistent runtime aircraft instance owned by a squadron. A campaign aircraft has a status such as ready, damaged, assigned, or lost, and carries an **aircraft loadout** describing what it can employ now.

Air tasking allocates individual campaign aircraft to flights. An aircraft is available for planning only when it is ready, unassigned, located at its operating base, and capable of the proposed route and loadout. Assignment to a committed flight reserves that aircraft from every other package.

An aircraft remains assigned from package commitment through landing, while its flight execution phase determines whether it is airborne. Aircraft do not duplicate flight position or airborne state; an individual aircraft leaves assignment early only through an individual outcome such as damage or loss.

**Airbase overrun** — hostile capture of an airport while squadron aircraft are physically present. Ready, damaged, and committed-but-unlaunched aircraft at that airport are lost and their unlaunched flights cancel; already-airborne flights survive, divert, and may reconstitute their owning squadron at recovery, while a squadron with no surviving aircraft is eliminated.

Squadron ready, damaged, assigned, and lost counts are summaries derived from the squadron's campaign aircraft; they are not a second source of allocation truth.

Loading and unloading ordnance onto a campaign aircraft is instant in v1. There is no rearm duration, transit time, or separate arming workflow yet.

Campaign aircraft start empty in v1. They receive a loadout when assigned to a sortie because sortie purpose determines what ordnance should be carried.

When a campaign aircraft lands at the end of a sortie in v1, its flight assignment and loadout are cleared and an undamaged survivor becomes ready immediately. The next sortie assignment generates a fresh loadout, and the package preparation delay is the only turnaround abstraction; future maintenance, supply, and recovery rules may add downtime or return unused ordnance to available stock.

**Aircraft loadout** — the ordnance physically carried by one campaign aircraft at a given moment, including remaining counts after expenditure. The core engine uses loadout state to know what that aircraft can still employ during autonomous simulation and, later, what to place on export.

In v1, a loadout is an abstract count per ordnance type, such as four AIM-120 and two AGM-88, with no pylon or station geometry. Pylon placement for third-party export is deferred to the sim adapter.

A loadout must satisfy the aircraft type's ordnance capacity: the sum of each carried store's ordnance weight may not exceed that aircraft's ordnance capacity. Each carried store must also be on that aircraft type's compatible ordnance allow-list and allowed for the aircraft's alliance.

**Mission-useful ordnance** — the carried ordnance that can still contribute to a flight's assigned mission effect. Time-based air-combat missions such as defensive counter-air patrols and offensive counter-air sweeps become unable to continue their mission when no air-to-air mission-useful ordnance remains; support missions such as airborne C2 and aerial refueling do not depend on ordnance.

For aggregate DCA/OCA execution, mission-useful ordnance exhaustion is evaluated at the flight formation level rather than per aircraft. A flight should continue while any assigned aircraft still carries mission-useful air-to-air ordnance; it should return to base only when the formation as a whole has no mission-useful ordnance remaining.

If a time-based combat flight returns to base early because the formation has exhausted mission-useful ordnance before satisfying its assigned effect, the flight is aborted rather than completed. If the assigned effect has already been satisfied, ordnance exhaustion does not change normal recovery.

V1 loadout planning is demand-driven. The planner should not fill unused ordnance capacity simply because capacity remains; carrying unnecessary stores increases fuel burden and should be avoided.

For a sortie's primary target category, the planned primary ordnance quantity should cover the expected target need plus a reserve of either 25% extra or one additional store, whichever is higher. Self-defense ordnance is planned separately and scales with expected enemy air threat: clear skies need little reserve, while enemy air superiority justifies a higher self-defense allocation.

In v1, self-defense ordnance is based on fixed desired shot counts by air-threat level. Longer term, self-defense planning may derive desired hits from expected aircraft threats. Determining the current air-threat level is outside the ordnance foundation.

For v1 defensive counter-air patrol and offensive counter-air sweep loadouts, each assigned combat aircraft plans a fixed mission shot budget of four air-to-air shots when capacity allows. At least two air-to-air shots must fit for the aircraft to be feasible; longer term, this budget may be replaced by expected engagement forecasting.

The preferred v1 air-to-air loadout split is two radar-guided air-to-air shots and two infrared air-to-air shots. If one employment category is unavailable or cannot fit, the planner may fill the remaining budget with the other air-to-air category; it should not exceed the fixed shot budget simply because unused capacity remains.

The game should only create sorties whose required loadout can fit the assigned aircraft. Detailed policy for resolving capacity conflicts between primary ordnance and self-defense ordnance is deferred until sortie generation is designed.

**Sortie target desired hits** — the expected number of successful weapon effects needed against a sortie target. V1 sortie planning may provide desired hits directly per target, usually one for simple point targets such as a tank, radar, or building and more for broad or durable targets such as an airfield runway. Loadout planning treats one desired hit as one planned weapon launch, then applies the primary ordnance reserve to cover misses without modeling hit probability.

**Target toughness** — a coarse rating of how hard a target is to meaningfully damage or destroy within its target category. Toughness lets the planner distinguish a tent from a hardened bunker without expanding **ordnance target category** into many narrow target types. Ordnance effect power must satisfy the target's toughness before weight efficiency is considered.

In v1, air-to-ground sortie targets are existing buildings or tile infrastructure. Building target toughness lives on the building definition/runtime building model, and infrastructure target toughness lives on the relevant infrastructure model. Future alternative targets that do not fit the building model may define their own toughness when that target type is introduced.

Target toughness is stable for weapon selection. Current target damage may reduce desired hits, but it does not reduce the toughness gate; a damaged hardened bunker still requires bunker-capable ordnance.

When multiple compatible and allowed ordnance types can satisfy a target category, v1 loadout planning should choose an adequately effective store before optimizing for weight. Among stores that meet the needed effect, prefer lighter or more weight-efficient stores so aircraft do not carry unnecessarily heavy ordnance. Avoid choosing a light store that is technically weight-efficient but too weak for the target.

_Avoid_: per-ordnance maximum counts on an aircraft type as the primary mixing rule; they cannot express tradeoffs between store types.

_Avoid_: treating squadron aggregate aircraft counts as a substitute for aircraft availability or loadout state; two ready aircraft of the same type may carry different stores, and one may already be reserved by a committed flight.

### Campaign template

What an author creates in the campaign editor after choosing a Module. A campaign template defines the starting premise for play under that Module. The Module is fixed for the lifetime of an edit session; it cannot be changed while editing an open template.

**Static premise** — fixed across every play from this template: map layout and extent, **template tile** geography (terrain, surface, urbanization, forest cover, edge properties, hex neighbors), sides, Module-scoped unit availability, authored building placement, and other geography that should not change because the story shifted.

**Starting conditions** — fixed every time a player starts from this template: **starting tile** data (initial tile control and tile infrastructure build/damage values), starting building build/damage values, initial unit locations and strengths, starting air wings and squadrons, **campaign start calendar** (`CampaignStartTime` — in-world date and time at turn zero, authored on the template not the Module), and other day-zero force dispositions.

A v1 campaign template directly contains tile definitions for static tile geography, starting tile state for day-zero tile control and tile infrastructure values, and authored building data for building placement and starting building values. Additional map aggregate models should be introduced only when a real rule needs them.

Building categories are core-engine concepts, not Module-specific template classes. A Korean War campaign template and a Gulf War campaign template use the same building options; the template records which building types are placed where and their starting build/damage values. V1 building types are airport, factory, supply hub, fort, port, railroad, refinery, power plant, static SAM, and standalone radar. Static SAM buildings and standalone radar buildings host air-defense components, but the component capabilities come from Module catalog definitions. Any future third-party export mapping for buildings belongs to the sim adapter/export process, not to the tile implementation.

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

**Ground tasking commander** — the per-alliance command authority that evaluates the land situation and assigns ground orders and offensive plans. It is represented in code by `GroundTaskingCommander`.

**Alliance air tasking commander** — the per-alliance command authority that owns alliance air doctrine, current mission requests, packages, flights, projected effects, and support-demand history. Bluefor and Redfor each have one; Neutral has none by default.

Ground tasking, air tasking, and IADS use separate per-alliance command state while operating under shared core-engine rules.

_Avoid_: using one generic alliance AI object as the owner of every ground, air-tasking, and IADS responsibility.

### Tile control

The alliance that militarily holds a land tile — movement, combat, and occupation rights derive from tile control. Every land tile has a controller. Ocean tiles never have a controller.

In v1, tile control is the only political field on a tile. It stands in for ownership: supply, production, and scoring benefits apply to the controlling faction as if they owned the tile.

In a later phase, **tile owner** (original or legal affiliation, fixed at campaign start) may diverge from tile control. A faction that controls a tile it does not own is **occupying** it and does not receive the normal supply or production benefits of that tile. A faction that owns a tile it does not control does not receive those benefits either.

_Avoid_: using “owner” in v1 data or rules when tile control is the sole authority.

### Supply capital

The designated alliance-level land tile that acts as the source of that alliance's supply network. In v1, a supply capital is a tile designation rather than a building, and it belongs to an alliance rather than a country.

_Avoid_: using "capitol" for this concept; a capitol is a building, while a capital is a source location.

### Supply network

The alliance-controlled land connection through which supply can flow from a supply capital to supply hubs and onward to divisions. In v1, supply may only use land tiles controlled by the same alliance as the supply capital and supplied divisions, and each tile on a rail path, including the capital and hub endpoint tiles, must contain a functional Railroad building.

_Avoid_: allowing supply to pass through hostile- or neutral-controlled land just because a railroad or supply hub exists there.

### Hub distribution

The local spread of supply from a supply hub to nearby divisions through same-alliance controlled land tiles. In v1, hub distribution is based on hex path distance through same-alliance controlled land tiles rather than tile infrastructure: full effect within 2 tiles, three-quarter effect at 3 tiles, half effect at 4 tiles, and no effect beyond 4 tiles.

When multiple hubs can reach a tile, divisions on that tile draw from the single best hub, chosen by highest supply amount after distance falloff, and hub effects do not stack. A hub's supply is shared by all divisions drawing supply from that hub, whether those divisions occupy the same tile or different tiles. Hub distribution cannot supply hostile- or neutral-controlled tiles in v1.

When divisions drawing from the same hub demand more supply than the hub can provide, the shortage is allocated proportionally across those divisions.

**Supply ratio** is a division's allocated supply divided by its supply consumption, clamped from zero to one.

**Supply effect** is the continuous effect of a division's supply ratio on out-of-combat strength and organization recovery. It applies to divisions that are stationary or under ordinary movement, but not to active combat participants or retreating divisions in v1. Full supply gives normal recovery, half supply is neutral, and zero supply turns the normal recovery amount into decay; supply-driven decay cannot reduce strength or organization below 1.

Supply network state and division supply ratios are recalculated every game turn.

### Hub supply

The amount of supply a supply hub can make available before local distribution to divisions. Hub supply is determined from the hub's effective hub level, but that level is a lookup value rather than the supply amount itself.

**Effective hub level** is the SupplyHub building's functional level capped by the lowest Railroad functional level on the best rail path from the supply capital to that hub. A level 10 SupplyHub supplied through a rail path whose weakest functional Railroad is level 3 functions as a level 3 hub.

The best rail path is the path that gives the highest effective hub level. Path distance is ignored except as a deterministic tiebreaker between paths with the same effective hub level.

If a SupplyHub cannot trace any valid rail path to its alliance's supply capital, it provides zero supply in v1.

_Avoid_: assuming a level 10 SupplyHub provides exactly 10 supply.

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

### Airspace position

An **airspace position** is a campaign-local position measured uniformly in feet from the campaign's stable origin, with X running east-west, Y representing altitude above mean sea level, and Z running north-south. Aircraft move continuously between airspace positions and are not snapped to operational-map tiles; tile centers, the kilometer-based ground-map scale, and placed assets are converted into this coordinate frame when air and ground geography interact.

_Avoid_: using tile cube coordinates or Unity scene-transform coordinates as an aircraft's authoritative position.

The center of cube tile `(0,0,0)` is the airspace origin. Tile centers use the operational map's existing flat-top hex orientation and `TileDistanceKM` center spacing converted to feet, with map east mapped to positive X and map north to positive Z; airports use their tile center at zero feet MSL until terrain elevation exists.

Physical air interactions, including sensor detection, weapon reach, and future aircraft encounters, use airspace positions and three-dimensional slant distance. Projection onto an operational-map tile is reserved for area membership, ownership, planning, and other tile-domain questions.

**Aggregate flight motion** — the operational point-mass approximation used to move a flight through airspace with finite forward speed, turn rate, climb rate, descent rate, and altitude limits. It provides plausible route following without simulating lift, control surfaces, stalls, or other flight-simulator aerodynamics, and one simulation tick may advance through multiple route legs when time remains.

Air-domain performance is authored in aviation units: airspeed in knots, climb and descent rates in feet per minute, and turn rate in degrees per second. The ground-map tile scale remains in kilometers and converts only when ground geography is represented in airspace.

Initial flight execution uses cruise speed. During a coordinated package segment, participating combat flights match the slowest participating cruise speed; after separating for recovery they resume their own aircraft type's cruise speed. Combat speed and other phase-specific speed choices belong to later mission behavior rather than to waypoint data.

During one simulation tick, a flight consumes the full elapsed-time budget continuously: reaching a waypoint consumes only the time physically needed, records the semantic crossing at its calculated within-tick campaign time, and leaves the remainder available for subsequent route legs. This is fractional event timing within the single simulation clock, not a second air-execution clock.

### Ground operational cadence

How often expensive ground decision logic runs on the simulation tick clock: objective selection, order assignment, retreat destination selection, and other planning work that does not need to be recalculated every tick.

Ground operational cadence is template-configurable within engine bounds (**one to six hours** of in-game time; **default six hours**). Many simulation ticks pass between ground operational updates.

Between ground operational updates, active ground combat and movement progress can still resolve every simulation tick without full strategic replanning every tick.

### Ground tactical combat

Lightweight ground resolution that may run on most simulation ticks when opposing forces are engaged — for example, attrition or control pressure while two tiles are fighting. This keeps the frontline alive between operational movement decisions without running full division AI every tick.

### Tick resolution order

Within each simulation tick, resolution order should stay deterministic. Air picture, air execution, air-to-ground effects, active ground combat, and ground movement progress participate every tick. Expensive ground planning and order assignment run only on ticks that fall on a ground operational cadence boundary.

Supply recalculation happens after ground movement and tile capture for the game turn, and supply recovery or decay uses that freshly recalculated supply state.

Air-tasking planning runs after the tick's current air, ground, and campaign effects resolve so it evaluates the freshest stable state. It then refreshes projected air effects, performs global priority rebuilding when the tick crosses an operational-cadence boundary, and runs support fulfillment before combat fulfillment.

In v1, each alliance may evaluate at most eight mission requests and create at most four packages per simulation tick. Request and alliance processing use stable ordering so the planning budget does not make seeded results nondeterministic.

### Air planning cadence

How often the air-tasking layer performs global planning and lightweight local planning.

Global air planning runs on a longer cadence aligned with ground operational cadence — template-configurable within engine bounds (**one to six hours**; **default six hours**). It recalculates theater-wide mission priorities and purges unfulfilled requests from the previous planning cycle.

Every simulation tick, lightweight planning updates projected effects, detects urgent coverage gaps, validates existing commitments, and may make bounded local adjustments. Significant events such as loss of supporting coverage, a major airbase loss, a newly detected raid, target destruction, or a collapsed SAM belt may trigger local replanning before the next global cadence.

Air **execution** (sortie movement, IADS refresh, engagement assignment, SAM launch resolution, and air-to-ground effects) also runs every simulation tick. Tick-level adjustments may scrub or retask pre-takeoff committed sorties in response to new threats or invalid plans. Active sorties are not cancelled, retasked, or rerouted by the air-planning layer.

_Avoid_: full theater-wide air replanning every simulation tick.

### Committed sortie

A **committed sortie** is one aircraft's planned employment after that aircraft and its flight mission have been assigned but before it has taken off. It survives later air-planning cycles, although the alliance AI may cancel or retask it before takeoff.

### Active sortie

An **active sortie** is one aircraft's employment after it has taken off. Takeoff locks its assigned mission intent and any already-assigned target: later air planning may neither cancel nor retask it.

An active sortie may still end unsuccessfully because its aircraft are lost, package-integrity rules force an abort and return to base, or the assigned effect can no longer be achieved. Future fuel rules may add another execution-driven abort condition without changing this boundary.

_Avoid_: using an operational replan to change an airborne sortie's assigned mission or target.

**Execution-level tasking** — a bounded decision made by an active flight's own mission behavior within its already-authorized mission intent, such as a DCA flight intercepting inside its patrol area or an on-call CAS flight receiving a target. It may replace only the unflown mission segment and materializes the amendment into the authoritative route; it is not an air-planning retask.

**Recovery diversion** — an execution-level change to an airborne flight's return and landing destination when its assigned recovery airport is no longer friendly. The flight applies the recovery-airport fallback hierarchy without changing its locked mission or target; because range is ignored initially, any valid alternate is reachable.

### Sortie

A **sortie** is one campaign aircraft's employment from takeoff through landing, loss, or another terminal outcome. It is the individual-aircraft unit of air activity, not necessarily a separately persisted planning object; its assignment and state may be represented by the aircraft's membership in a flight.

### Flight

A **flight** is a persistent air-planning formation containing one or more campaign aircraft that share a mission role, route, and timing. Flights are the aircraft-assignment and execution groups coordinated by packages.

Every flight draws its aircraft from exactly one squadron and therefore has one aircraft type and one operating base. A package may coordinate flights from multiple squadrons and bases.

During aggregate campaign execution, the flight owns the authoritative airspace position, velocity, and route progress shared by its member aircraft. Member aircraft retain their individual identity, condition, loadout, and sortie outcome without independently maneuvering inside the formation.

Each flight belongs to exactly one owning package. A supporting flight may additionally be referenced by other packages that use its service, but those references do not give the flight multiple owning packages.

**Flight route** — the concrete, ordered path assigned to every flight before its package commits, from takeoff through terminal landing, including any transit, rendezvous, station, mission, and return legs needed for that flight's role. Package creation may accept authored route input or generate default geometry, but in both cases it materializes a complete waypoint sequence that becomes the flight executor's single source of movement truth.

**Air route geometry planner** — the replaceable planning policy that selects generated ingress and egress transit geometry before a flight commits. The initial policy places one laterally offset transit waypoint on each leg, puts ingress and egress on opposite geographic sides of the direct route, and varies the selected side by package identity. It does not yet evaluate threats, fuel, weather, tanker placement, support timing, or airspace restrictions; those factors may enrich or replace the policy without changing the flight executor.

**Air waypoint** — an airspace position on a flight route paired with a semantic action or transition, such as takeoff, rendezvous, begin station work, perform a mission action, return to base, or land. A waypoint does not prescribe speed; flight guidance chooses movement performance.

A waypoint carries its planned campaign arrival time when timing is operationally meaningful. Takeoff, rendezvous, station entry, discrete mission action, racetrack release, and landing timing are therefore part of the materialized route; flight-level takeoff and effect timing are derived summaries rather than competing stored execution clocks.

A station-entry waypoint may carry the air mission area affected while its station loop is active. The initial route copies the mission request's area onto its one station entry; a flight's current effect area is derived from its active station waypoint rather than maintained as competing flight-level execution state.

A racetrack uses explicit endpoint waypoints in the ordered route. Its terminal endpoint identifies the earlier station-entry waypoint to repeat from and the campaign time when repetition ends; after that time execution advances to the next waypoint. Because each loop instruction and effect area belongs to its waypoints, one flight route may contain multiple sequential stations without a separate station entity, flight-level loop state, or a general waypoint graph.

**Flight execution event** — a bounded record that a flight actually crossed a semantic waypoint or completed an execution transition at a campaign time, such as takeoff, rendezvous, station entry, mission action, station exit, RTB, or landing. Common milestone times are derived from these events; repeated racetrack laps do not create events, and mission-specific outcomes such as damage, suppression, or weapons expended belong to separate typed effect records linked to the relevant execution event.

After commitment, ordinary tick execution does not regenerate direct legs, station geometry, or rendezvous points. The initial model permits only recovery diversion to replace the unflown recovery portion; future execution-level tasking may explicitly replace an authorized unflown mission segment before the executor continues.

_Avoid_: a committed flight without a complete materialized route, competing package- and flight-level route geometry, or opaque waypoint flags whose meaning depends on undocumented combinations.

**Recovery airport** — the friendly airport where a flight intends to land. Recovery selection prefers the flight's assigned recovery airport, then its squadron's current airport if that differs, then the nearest friendly airport; landing at the final fallback reconstitutes the squadron at that airport, while absence of any friendly recovery airport causes the flight to fail and its aircraft to be lost.

An airborne flight preserves its owning squadron even if that squadron loses its previous airport. A squadron is eliminated only when it has no surviving campaign aircraft, not merely because its base was captured or disabled.

In the initial air-execution model, airport damage and functional level do not affect launch, landing, recovery selection, or diversion. Hostile tile capture is the only airport-state change that invalidates air operations; damage-based closure and repair are deferred.

**Approach waypoint** — the final generated navigation waypoint before recovery, placed on the inbound line using the flight's cruise speed, descent rate, and current altitude so descent begins late enough to avoid ground-level transit and reaches the airport near zero altitude. Reaching the following airport landing waypoint ends the flight without runway, pattern, or ATC simulation.

**Racetrack station route** — the shared station-keeping route pattern for initial DCA, AWACS, and tanker flights: enter the station, fly between two track-end waypoints, follow the terminal endpoint's repeat instruction until its release time, then continue to the return legs. An automatically derived v1 racetrack is centered on the mission-area center, aligned east-west at mission altitude, and sized from the campaign tile scale; authored route waypoints override that placeholder geometry.

**Nominal cruise altitude** — the aircraft type's normal transit altitude in feet above mean sea level.

**Service ceiling** — the aircraft type's maximum supported campaign altitude in feet above mean sea level.

**Mission altitude** — the desired altitude in feet above mean sea level selected by a flight's mission behavior. Route generation uses the mission altitude where applicable, clamps it to the assigned aircraft type's service ceiling, and otherwise uses that type's nominal cruise altitude.

Generated v1 mission altitudes are 40,000 feet for DCA and OCA, 35,000 feet for AWACS, and 25,000 feet for tanker missions. These are replaceable defaults rather than hard limits.

_Avoid_: treating low, medium, and high altitude bands as exact aircraft performance or route altitudes.

In the initial air-execution model, a sustained flight repeats its station route until its already-assigned effect end, then follows its return and landing legs. Execution does not score late arrival or partial coverage, and fuel, range, and endurance do not constrain planning or movement; those aircraft characteristics remain available for a later fidelity pass.

**Flight execution phase** — the mission-independent stage of a flight's physical journey: awaiting takeoff, outbound, executing, returning, landing, or ended. Execution phase is separate from lifecycle outcome; an aborted flight has stopped its assigned mission and may have a terminal planning outcome while remaining physically in the returning phase until its surviving aircraft land.

_Avoid_: mission-specific execution phases such as DCA, AWACS orbit, tanker track, or strike; those are mission behaviors performed during the generic executing phase.

**Air mission behavior** — the mission-specific effect a flight performs during its generic executing phase, separate from the shared rules for takeoff, route movement, return, and landing. Initial behaviors establish DCA patrol presence, airborne-C2 service, placeholder tanker service, or a no-effect OCA sweep action; later air-combat and ground-attack behaviors may add their own resolution without replacing flight execution.

_Avoid_: implementing navigation, takeoff, RTB, or landing separately for each mission type.

An initial DCA, AWACS, or tanker flight achieves its mission by reaching station and remaining there through its assigned effect end. It becomes completed after returning and landing; a flight that never reaches station fails, while one explicitly directed home before achieving the mission is aborted.

An initial OCA flight achieves its placeholder mission by traversing its sweep mission waypoint, returning, and landing. It records no combat or air-superiority effect until air-combat resolution is added.

Flight size is adaptive. The package builder chooses the smallest aircraft allocation expected to produce the requested effect at acceptable risk, with alliance doctrine able to add a force or redundancy margin. Support-flight size reflects required service capacity, while combat-flight size reflects expected opposition and desired advantage.

_Avoid_: using sortie for a multi-aircraft formation.

_Avoid_: fixed flight sizes based only on mission category or assigning excess aircraft simply because they are available.

### Package

A **package** is the persistent coordination boundary for flights intended to achieve a shared objective. It aligns participating flights around timing, route, roles, and dependencies so they execute as one operational effort.

A mission request may be fulfilled by one or more packages, and each package traces back to exactly one originating mission request. A package may depend on a supporting flight owned by another package.

Packages and flights use the following lifecycle:

- **Committed** — planned with aircraft reserved, but no assigned aircraft has taken off; still cancellable or retaskable.
- **Active** — at least one assigned aircraft has taken off; the assigned mission and target are locked.
- **Completed** — execution ended after achieving the intended effect.
- **Failed** — active execution ended without achieving the intended effect.
- **Cancelled** — planning stopped before any assigned aircraft took off.
- **Aborted** — an active package or flight stopped its assigned execution and directed surviving aircraft to return to base without accepting another mission.

Flights carry their own lifecycle state. Package state is derived from the states of its flights rather than maintained as a competing source of truth.

A package identifies which owning flights are required for its coordinated effect. If a required flight is cancelled or cannot launch, the whole package stops: before activation it is cancelled; after activation it is aborted, and every airborne package flight returns to base. Aircraft in flights that have not launched are released.

_Avoid_: continuing a coordinated package after a required flight becomes unavailable or describing an RTB abort as airborne retasking.

**Package preparation delay** — in v1, a newly created package requires a fixed 30 minutes of campaign time before any assigned flight may take off. Aircraft are reserved when the package is committed; the delay is a minimum launch lead time, after which route timing may schedule takeoff later.

The fixed delay is separate from transit time and does not introduce detailed arming, taxi, runway, or airport-throughput simulation.

**Package effect window** — the time interval during which a package's coordinated flights are intended to produce the requested effect at their mission location. Effect start is planned time on station, never takeoff time; each flight calculates its takeoff time from its operating base, route, cruise speed, and the package's timing so required flights can synchronize.

If preparation and transit make the requested effect start unreachable, projected coverage begins at the later feasible station-arrival time rather than crediting the outbound flight with an effect it cannot yet provide.

**Package rendezvous** — a shared waypoint and time where required flights converge before conducting their coordinated mission. Required supporting coverage must be active when the package reaches the portion of its route or effect window that depends on that support.

An initial DCA or OCA package with more than one required combat flight uses a package rendezvous before proceeding to the mission area. A single-flight combat package skips rendezvous, and AWACS or tanker flights proceed independently to their own stations rather than joining a supported package's formation.

Rendezvous is a synchronization barrier: an early flight holds at its rendezvous waypoint until every required combat flight has arrived, then the package releases those flights together at their coordinated speed. If a required flight cannot arrive, package-integrity rules abort the package.

An automatically generated rendezvous lies halfway between the participating combat flights' launch-base centroid and the mission-area center. It uses a common altitude limited by the lowest participating aircraft service ceiling and a common arrival time calculated backward from planned mission-area arrival; explicit package route data may override this placeholder.

Rendezvous is a package coordination choice rather than a universal rule for every mission. Future packages may define staggered coordinated segments, such as SEAD reaching a threat area before a strike flight, without changing the shared flight executor.

When suitable aircraft are otherwise comparable, package building prefers flights from squadrons at the same airport to reduce coordination and transit cost. This is a preference rather than a requirement; one package may still combine flights from different operating bases.

Initial generated routes keep ingress and egress distinct with simple laterally offset transit waypoints. DCA, AWACS, and tanker flights then execute their racetrack, while OCA flights traverse their sweep mission waypoint; multi-flight DCA and OCA packages insert their required rendezvous before the shared ingress leg. The initial geometry prevents routine outbound-route reuse but does not claim to minimize operational risk; threat avoidance, fuel-state routing, tanker placement, support timing, assembly patterns, and airspace constraints remain future route-planning improvements.

_Avoid_: creating independent strike, escort, and support flights without recording the operational effort that coordinates them.

### Supporting flight

A **supporting flight** provides an airborne service, such as AWACS coverage or aerial refueling, that may support multiple packages during its mission. It belongs to the package created for its own mission request, while other packages reference the service it provides without taking ownership of the flight.

_Avoid_: duplicating an AWACS or tanker flight for every package that uses the same available coverage.

### Package feasibility

**Package feasibility** is the alliance AI's situation- and doctrine-dependent judgment that a proposed package can acceptably pursue its mission request with the aircraft, support coverage, and risk conditions available to it.

The absence of a supporting flight is not universally blocking or universally acceptable. Depending on the mission request, enemy capability, expected risk, and alliance doctrine, the same lack of AWACS or tanker support may make one package infeasible while another may commit without it.

_Avoid_: declaring a support type mandatory or optional solely from the package's mission category.

### Alliance air doctrine

**Alliance air doctrine** is the per-alliance policy through which an alliance AI evaluates air priorities, acceptable risk, force preservation, support needs, and commitment thresholds. Each alliance begins with a default doctrine authored in the campaign template.

All alliances use the same core air-planning rules; doctrine lets separate alliance AIs reach different decisions from comparable circumstances without requiring alliance-specific rule implementations.

The minimum v1 doctrine profile contains risk tolerance, desired air-combat advantage, a priority weight for each mission-request type, and baseline AWACS and tanker capacity demand.

In v1, doctrine remains fixed during campaign play. The runtime model should permit future doctrine changes, but the rules and triggers for changing it are deferred.

_Avoid_: implementing separate Bluefor and Redfor air-planning algorithms.

### Air-planning intelligence

**Air-planning intelligence** is the alliance-scoped view of friendly readiness, enemy threats, and potential targets available to an alliance AI when it creates and evaluates mission requests.

In v1, air-planning intelligence provides perfect knowledge of relevant campaign state. This is temporary: future intelligence and fog-of-war rules will replace perfect enemy knowledge without changing the air planner's mission-request, package, and flight concepts.

_Avoid_: treating v1 perfect intelligence as permanent alliance knowledge.

### Alliance air plan

An **alliance air plan** is an alliance AI's prioritized statement of its current operational air needs for one air-planning cycle. It coordinates competing demands such as airspace defense, air superiority, strike activity, and supporting-aircraft coverage before individual aircraft are committed.

_Avoid_: allowing independent sortie generators to consume aircraft without first reconciling alliance-wide priorities.

### Mission request

A **mission request** is a prioritized demand within an alliance air plan for a desired air effect against a target, area, or supported operation. Mission requests express what the alliance needs and why; feasible requests are fulfilled by assigning aircraft to sorties.

Mission requests remain distinct from sorties: a request may go unfulfilled when suitable aircraft or support are unavailable. Unfulfilled requests are purged at the next air-planning cadence rather than carried as a backlog; the alliance AI evaluates the new campaign situation and generates a fresh prioritized request set. Purging mission requests does not cancel sorties already created to fulfill them.

The current request collection contains actionable requests for the active planning cycle. A request with committed or active packages is retained until all of its packages reach a terminal state, even when a new global planning cycle begins. Packages reference their originating request through a stable identity.

Terminal packages and snapshots of their originating requests move to bounded campaign history for diagnostics and AI explanation after every associated flight has physically ended. A planning-terminal outcome such as an airborne abort stops projected mission effects immediately but does not permit execution state to be discarded before the returning aircraft land or are otherwise resolved.

_Avoid_: treating a sortie as the source of operational demand, assuming every mission request must produce a sortie, or carrying unfulfilled requests across full planning cycles.

_Avoid_: deleting a request while a package still depends on its identity or retaining an unbounded active planning graph.

### Mission request priority

**Mission request priority** is an alliance AI's situation-dependent estimate of which requested air effect should receive scarce aircraft and support first. Within its applicable fulfillment pass and resource pool, every mission request competes by priority; no combat mission category receives aircraft solely because it is defensive or offensive.

Priority may reflect current threat, operational value, expected effect, risk, doctrine, and dependencies on other missions. A defensive request may outrank every offensive request during an immediate threat or receive no aircraft when the alliance judges another effect more valuable.

Each request retains its total priority and a diagnostic breakdown of the contributing factors so the decision can be reproduced and explained.

_Avoid_: fixed aircraft reservations or guaranteed fulfillment based only on mission category.

### Air superiority

**Air superiority** is an assessed campaign condition describing an alliance's degree of air control in a particular area and time. It is an operational objective that drives concrete mission requests, not a mission category assigned directly to a package or flight.

Defensive counter-air, offensive counter-air, airborne C2, aerial refueling, and DEAD may preserve or improve air superiority through distinct requested effects.

Air superiority is assessed from relative projected **air-combat power**, not raw aircraft counts. Air-combat power reflects active and committed fighter coverage, ready fighters able to reach the area, aircraft capability and loadout, available support, expected hostile forces, losses, and alliance doctrine for the relevant area and time.

_Avoid_: air superiority mission.

_Avoid_: measuring local air control only by counting aircraft without considering their capability or ability to affect the area.

### Initial air-tasking mission requests

**Defensive counter-air patrol** is a request to protect an area from hostile aircraft through planned fighter presence. An intercept may use an available defensive patrol as a tick-level execution response rather than requiring a new full-cadence mission request.

**Offensive counter-air sweep** is a request to contest hostile air activity in an area using fighter aircraft.

**Provide airborne C2** is a request to establish shared airborne command-and-control and surveillance coverage through an AWACS-capable supporting flight.

**Provide aerial refueling** is a request to establish shared refueling coverage through a tanker-capable supporting flight.

These four requests form the initial air-tasking backbone. DEAD is the next target-attack capability intended to build on that backbone.

### Air mission area

An **air mission area** is the dynamic geographic area affected by an area-based mission request, defined in v1 by a center campaign tile and a radius. Defensive patrols and offensive sweeps operate within an air mission area; supporting coverage and risk may be evaluated relative to it.

Mission areas are derived from current campaign needs rather than authored as fixed zones in a campaign template. The actual routes, patrol stations, and support orbits used by flights are derived from the mission area rather than being the mission area itself.

_Avoid_: pre-authoring fixed mission areas, or treating a single target point, a flight route, or a support orbit as the whole area whose air effect was requested.

### Coverage window

A **coverage window** is the bounded interval during which an area-based mission request asks the alliance to maintain an air effect. In the initial model, one flight may cover its entire assigned window because fuel and endurance are ignored; future execution limits may require sequential rotations.

Coverage planning uses a rolling handoff across air-planning cadences. A window may extend beyond the next cadence boundary long enough for the following planning cycle to prepare, launch, and position replacement coverage, preventing a gap while the new alliance air plan is being fulfilled.

The following planning cycle reassesses the need. It may cancel no-longer-needed committed rotations before takeoff, but an active sortie continues its locked mission through its planned coverage.

In v1, handoff does not calculate geometric equivalence between the previous and newly desired mission areas. Existing active coverage finishes in its originally planned area even when the new plan wants coverage elsewhere; the next packages and flights are planned against the new desired area.

_Avoid_: infinite coverage requests, ending coverage exactly at a planning-cadence boundary without allowing for replacement lead time, or adding spatial-overlap optimization to v1 handoff.

### Projected air effect

A **projected air effect** is an effect expected from an active or committed package or flight but not necessarily completed yet. Air planning credits projected effects before generating or fulfilling new demand so that existing commitments are not duplicated.

Outside the v1 cross-cadence handoff simplification, coverage effects apply within their actual mission area and time window. A committed attack projects its intended effect against its target so a later planning pass does not create a duplicate attack merely because the first package is still en route.

_Avoid_: treating only already-completed effects as fulfilled demand.

### Mission request fulfillment pattern

A **sustained mission request** asks for an effect to remain available throughout a coverage window. Tick-level fulfillment checks projected coverage and creates replacement packages only for gaps, such as rotating AWACS, tanker, or defensive patrol flights.

A **discrete mission request** asks for one bounded effect, such as one attack against a target. Once a package is committed to that effect, tick-level fulfillment treats it as in progress and does not repeatedly create packages for the same request.

A discrete request whose package is cancelled before takeoff becomes unfulfilled and may receive a replacement package during the same planning cycle. Once its package becomes active, failure does not automatically create repeated replacement packages; the effect is reconsidered at the next global prioritization unless that failure triggers an explicit urgent local replan.

Support requests accept partial capacity: when the desired number of support slots cannot be provided, the support pass commits whatever useful capacity is available and continues to seek the remaining capacity.

Combat requests require their full desired force strength before a package may commit. Sustained combat coverage may be fulfilled for only part of its desired window when resources are scarce, but each committed rotation must meet the requested force strength. For example, four DCA aircraft for two hours is preferable to two aircraft for four hours when four are required; an OCA sweep requiring four aircraft does not launch with only two.

_Avoid_: interpreting sustained as "create a package every tick" or repeatedly fulfilling a discrete request while its package is still committed.

_Avoid_: stretching combat coverage by launching understrength packages that do not meet the requested effect.

### Tick-level air fulfillment

**Tick-level air fulfillment** is the bounded, deterministic process that turns prioritized unmet mission requests into packages during simulation ticks. It considers requests in priority order, builds the smallest feasible package, reserves the selected campaign aircraft, credits the resulting projected effects, and then continues within that tick's planning budget.

Tick-level fulfillment runs a support pass before a combat pass. The support pass prioritizes airborne-C2 and aerial-refueling requests and allocates their dedicated aircraft. The combat pass then evaluates its requests against the support capacity already active or projected. This ordering coordinates non-substitutable resource pools; it does not give every support request an unconditional priority over every combat effect.

An infeasible request remains available for a later tick in the same planning cycle. Stable tie-breakers are used when priorities are equal so seeded campaign behavior remains reproducible.

_Avoid_: solving a theater-wide global allocation optimization every simulation tick or allowing separate package builders to reserve the same aircraft.

### Air-tasking diagnostics

**Air-tasking diagnostics** are the persisted or inspectable reasons behind autonomous planning decisions. They explain why a mission request exists, how its priority was calculated, whether it was fulfilled, partially fulfilled, deferred, or purged, why a proposed package was feasible or infeasible, and why aircraft or support were selected or rejected.

Diagnostics are part of the foundation's observable behavior rather than optional console noise. They should make deterministic planning decisions testable without requiring a debugger.

_Avoid_: recording only the final package while discarding the reasons competing requests or aircraft were rejected.

### Support capacity

**Support capacity** is the bounded number of friendly aircraft that an AWACS or tanker flight can support within its coverage area and time window. In v1 it is measured in abstract **support slots**: each support-capable aircraft type provides a configured number of simultaneous slots, and each supported combat aircraft reserves one compatible slot during the relevant overlap.

A sustained support request asks for enough projected capacity to meet the area's demand. Multiple packages may share one supporting flight only while their overlapping reservations remain within its available slots; additional packages and flights are created only when existing capacity is insufficient.

Future AWACS control-channel rules and tanker fuel-transfer rules may specialize capacity without changing the shared support-capacity concept.

**Airborne-C2 effect** — in v1, AWACS coverage has no numerical effect on air-combat power, mission risk, or air-planning knowledge because the air planner temporarily uses perfect intelligence and detailed airborne C2 interaction is not yet modeled. Baseline demand authored through the campaign template or alliance doctrine still causes the planner to create airborne-C2 requests, packages, flights, coverage windows, and support-slot reservations so the support-tasking backbone exists.

Observed and forecast mission demand do not escalate AWACS coverage until its operational effect exists. Future AWACS behavior is intended to contribute through alliance IADS and intelligence rules, where its help may not reduce cleanly to one numerical benefit. Do not add a placeholder combat multiplier that would compete with that future model.

**Aerial-refueling effect** — in the initial air-execution model, tanker coverage has no numerical effect on receiver feasibility, range, endurance, or runtime behavior. Aerial-refueling requests, packages, flights, station routes, coverage windows, and support slots still exist so tankers can execute complete missions and the support-tasking backbone is ready for later fuel-transfer rules.

_Avoid_: adding placeholder range multipliers, fuel burn, transfer rates, boom queues, partial offloads, or fuel-driven aborts to the initial air-execution model.

Support demand blends three inputs:

- **Baseline support demand** — template- or doctrine-defined coverage that prevents support from requiring prior usage before it can be requested.
- **Observed support demand** — recent support usage requested by missions in an area, allowing coverage to follow sustained campaign activity.
- **Forecast support demand** — expected usage from current prioritized combat requests, allowing support to prepare before their packages exist.

Global air planning uses the blended demand to adjust future support coverage, such as reinforcing heavily used northern tanker coverage while retaining lower southern coverage.

_Avoid_: treating the presence of one support aircraft as unlimited support for every package in its area.

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

### Air-defense site

An air-defense site is a runtime IADS participant that can contribute sensor, shooter, command, or network roles. SAM sites and radar sites are air-defense sites with different role mixes.

### SAM site

A SAM site is a shooter-capable air-defense site that can contribute sensors, shooters, command/network support, or some combination of those roles to IADS behavior and SAM launch execution.

A SAM site may be hosted by a static placed asset or by a mobile ground formation. Static and mobile hosts affect placement, movement, ownership, capture, damage, and repair rules; the core detection, engagement-assignment, launch-authorization, suppression, and role-status concepts remain shared.

_Avoid_: splitting SAM behavior into unrelated "building that shoots" and "ground unit that shoots" models.

### Radar site

A radar site is an air-defense site that contributes sensor, cueing, command, or network roles without launcher capability. A radar site is not a SAM site unless it also has shooter capability.

### SAM component definition

A SAM component definition is a reusable authored component in a Module catalog. It describes an independently damageable SAM-site component and the air-defense roles it can contribute when a campaign places or instantiates it.

Module authors define component capabilities once, then campaign templates use those component definitions when authoring static SAM sites, mobile SAM sites, standalone radar buildings, or hybrid self-propelled SAM systems.

### SAM component

A SAM component is an independently damageable part of a SAM site that contributes one or more air-defense roles such as search sensing, fire-control quality, shooting, command/network support, ammo, reload, or emissions.

Components are campaign-level damage units instantiated from SAM component definitions, not always literal real-world subassemblies. A static SAM site may expose separate radar, launcher, command, and support components, while an all-in-one self-propelled system may expose a hybrid component that loses multiple roles when destroyed.

SAM components do not use building-style build levels. A component either functions or is damaged enough that it no longer contributes its roles until repaired. Suppression is a temporary SAM-site behavior state in the initial model; component-level suppression may be added later only when a specific effect needs it.

In the first model, damaged SAM components stay damaged indefinitely. Repair and replacement are deferred to a later logistics or repair system.

Air-to-ground strikes against SAM sites target specific known SAM components rather than the site as a single undifferentiated target. The SAM site remains the grouping and behavior actor, but component identity determines weapon suitability and damage effects.

Every SAM component is targetable. Each SAM component definition should describe the target profile that strike planning uses for weapon selection and damage resolution.

SAM component target profiles reuse the shared ordnance target categories for weapon selection and may expand those categories when SAM-specific targets need distinctions the current list cannot express.

_Avoid_: forcing every radar, launcher, or command function into separate damage entries when the real platform or desired campaign abstraction only supports killing the whole combined vehicle.
_Avoid_: modeling a radar or launcher as having a build level when the campaign question is whether that component is functioning, damaged, suppressed, or repaired.
_Avoid_: tasking a strike against a whole SAM site when the planner needs to know whether it is attacking a radar, launcher, command component, or other targetable part.

### Campaign SAM component allowance

Campaign SAM component allowance is the subset of Module SAM component definitions permitted in one campaign story. In v1, SAM component allowance should follow the ordnance allowance pattern and be scoped per alliance unless a future rule needs country-specific access within the same alliance.

A SAM component must be both defined in the Module and allowed for the relevant alliance before it may be used in that alliance's authored SAM sites or mobile SAM attachments.

Component allowance is the lower-level guardrail for custom sites, template overrides, and shared components reused across multiple SAM site templates.

### SAM site host constraint

A SAM site host constraint describes where a SAM site template may be instantiated: static only, mobile only, or static/mobile if a future system genuinely supports both. Host constraints belong to the template because the same shared SAM site behavior can be valid for different placement models.

_Avoid_: creating unrelated static-SAM-template and mobile-SAM-template families when the main difference is where the site can be hosted.

### SAM site template

A SAM site template is a reusable Module catalog definition for a recognizable SAM site arrangement, such as an SA-2 battery, SA-6 battery, or SA-8 platoon. It is built from SAM component definitions and records the default component mix a campaign author can instantiate.

Campaign templates should usually place SAM sites from SAM site templates rather than hand-assembling every radar, launcher, command, or support component. Template-level defaults may still be overridden when a campaign needs a nonstandard site, damaged starting condition, or scenario-specific force structure.

A SAM site template carries a SAM site host constraint rather than belonging to a separate static-template or mobile-template family.

### SAM launcher component

A SAM launcher component is a SAM component definition that contributes shooter capability and includes its interceptor behavior directly: engagement envelope, ready rounds, reload behavior, salvo or launch rate behavior, and guidance dependency.

Launcher ammo is tracked at the launcher component level as abstract ready and remaining counts rather than individual missile objects. Reload delay, reload rate, and simultaneous shot or channel limits may be modeled as component capability when needed.

SAM-launched missiles are not ordinary aircraft ordnance, and in the first model they do not need a separate interceptor catalog definition. Split interceptor definitions out later only if runtime logistics, shared missile stocks, or cross-launcher reuse make that extra catalog layer necessary.

_Avoid_: modeling SAM missiles as aircraft ordnance when they are consumed by SAM launch execution rather than aircraft loadout planning.

### Campaign SAM site template allowance

Campaign SAM site template allowance is the subset of Module SAM site templates permitted in one campaign story. In v1, SAM site template allowance should follow the ordnance allowance pattern and be scoped per alliance unless a future rule needs country-specific access within the same alliance.

Template allowance controls which named SAM systems an alliance can field during ordinary campaign authoring. A template must be allowed for the alliance, and its required SAM component definitions must also be allowed, before that template can be instantiated without overrides.

### Radar definition

A radar definition is a reusable Module catalog definition for a radar capability, such as a Fan Song fire-control radar or an early warning radar. It describes the radar capability once so it can be used by SAM components, static SAM buildings, standalone radar buildings, or future sensor hosts without duplicating radar behavior.

Radar definitions are authored capabilities, not runtime placed assets. Runtime hosts determine where the radar capability exists, who controls it, and whether the hosted radar component or building is damaged, suppressed, or emitting.

### Static SAM site

A static SAM site is a SAM site whose host is a static SAM building on a tile. It behaves like other placed assets for map placement and damage identity, while contributing air-defense roles through the shared SAM site model.

A static SAM building groups the site's SAM components under one placed site identity. Its components can be damaged without every launcher, command post, or support asset becoming a separate building.

Radars are the exception when they need to exist outside a SAM site. A standalone radar building is its own placed asset because radar sites may contribute detection, cueing, command, or network roles without being SAM launch sites.

Standalone radar buildings are not SAM sites when they cannot launch missiles. They may still reuse radar definitions, SAM component definitions where appropriate, target profiles, damage state, suppression concepts, and IADS network contribution rules.

In the first model, hostile tile capture disables a static SAM site's SAM behavior or a standalone radar building's radar behavior rather than transferring it into the captor's IADS. The placed asset may remain, but its components do not become operational for the new controller automatically.

### Mobile SAM site

A mobile SAM site is a SAM site whose host moves with a ground formation or mobile detachment. Self-propelled SAMs are mobile SAM sites when their air-defense behavior follows the shared SAM site model rather than ordinary ground combat rules.

In the first model, mobile SAM sites may be hosted by divisions for position, movement, alliance/country context, supply context, and overrun or capture vulnerability. They remain separate from the division's ground combat stats and do not participate in ground combat as battalion strength.

Mobile SAM sites should keep their own identity so a later transfer rule can move them between host divisions or detach them into independent mobile air-defense units without redefining what a SAM site is.

In the first model, mobile SAM components are damaged or destroyed by aircraft strikes against those components, or automatically destroyed when the host division is overrun. Normal ground combat attrition against the host division does not directly damage attached mobile SAM components.

_Avoid_: treating "self-propelled SAM" as a separate domain category when "mobile SAM site" is the broader host model.
_Avoid_: folding mobile SAMs into battalion definitions or division soft/hard attack values when they are meant to affect the air war.

### Alliance IADS

An alliance IADS is the persistent integrated air-defense actor for one combatant alliance. Bluefor and Redfor each have an alliance IADS in v1; Neutral does not.

An alliance IADS owns that alliance's shared air picture; air-defense sites contribute observations to it, and v1 assumes friendly sites can use the shared air picture automatically.

Air-defense sites contribute to the alliance IADS for their effective site alliance. Mobile SAM sites use their assigned alliance; static air-defense sites derive their effective alliance from their campaign country assignment and stop contributing when disabled by hostile tile capture.

In v1, the alliance IADS owns current tracks and engagement assignments. IADS commander refresh names the decision pass that updates those assignments, even if a future IADS commander becomes a separate durable entity.

In v1, an alliance IADS builds tracks for active airborne hostile flights only. Friendly flights remain known through air operations rather than as IADS current tracks.

Alliance IADS tracks are persistent campaign state across turns. They should be representable as campaign state even before save/load behavior exists.

_Avoid_: treating future network topology as the owner of the v1 shared air picture.

### IADS current track

An IADS current track is an aggregate hostile-flight contact that a site or alliance IADS is currently aware of through direct detection or shared cueing. Current track awareness is not authorization to fire.

Remote cueing may add current track awareness for another site, but remote cueing alone is not enough to authorize a SAM launch.

_Avoid_: creating one co-located track for every member aircraft or treating a package as the tracked object.

An IADS current track records the flight's last known airspace position and may carry an estimated aircraft count. It may reference the true flight entity for simulation bookkeeping, duplicate prevention, and resolution, but that reference is not itself alliance knowledge about aircraft type, squadron, mission, or package.

### Stale IADS track

A stale IADS track is an IADS current track that persists after the tracked flight is no longer currently observed. Stale tracks record that they are stale and remain in the alliance IADS air picture only until their configured expiry threshold is reached.

Stale tracks represent lost sensor contact with a flight that may still be airborne. Their IADS track quality decays while stale, and tracks are removed when their true flight is no longer active and airborne, such as after landing, destruction, or leaving the battlespace.

If a stale track is reacquired before expiry, it keeps the same track identity, clears stale state, refreshes its last known position, and continues building from its decayed quality.

### IADS track quality

IADS track quality is a continuous 0.0 to 1.0 estimate of how useful an IADS current track is for air-defense decisions. Track quality builds over time from radar contributions, is capped by the radar-flight situation, may improve faster when multiple radars contribute with diminishing returns, and is interpreted through gameplay thresholds for awareness, engagement assignment, weapon-quality use, and other actions.

A flight contact must reach at least 0.10 IADS track quality before it becomes an IADS current track. The 0.10 threshold creates tracks; stale tracks below that quality persist until their stale expiry threshold is reached.

Damaged, destroyed, disabled, or suppressed radar capability does not contribute to IADS track quality in v1. Radar emission is binary, and available radars are assumed to emit; future emission modes or graded suppression may add more detailed behavior.

Radar contributions are component-level, while air-defense site status gates whether those components can contribute. A site with multiple radar components may still contribute through intact radar components when other radar components are damaged.

### IADS engagement assignment

An IADS engagement assignment is the command decision that commits a SAM site to fire-control action against a current track. SAM launches should be based on engagement assignments, not merely on current track awareness.

Remote engagement may allow a shooter to receive an engagement assignment against a shared track when doctrine, network quality, and track quality support it.

The IADS commander layer owns engagement assignment. SAM launch execution consumes assignments and resolves whether assigned shots occur; it does not choose targets itself.

### IADS commander refresh

IADS commander refresh is the update of tactical IADS commander decisions for SAM sites: suppression decay, network membership, EMCON and radar posture, track and engagement reset, IADS current track assignment, and IADS engagement assignment. It reflects what a human IADS commander would decide before shooters fire, not threat-field products for route planning, SAM launch resolution, or per-slice emission bookkeeping for debug or EMCON history.

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

Within a tick, sortie movement updates aircraft positions first, the alliance IADS ages existing tracks and applies radar contributions second, IADS engagement assignment refresh runs third, and SAM launch execution resolves assigned shots fourth.

SAM launch execution is site-driven: SAM sites are the actors that consume assigned engagements and fire. Debug output should still make package and sortie exposure legible by showing whether a package was fired at and, when it was not, why assigned or plausible SAM sites did not launch.

Package-level SAM debug should answer why a package was or was not fired on without listing every site unless drill-down detail is requested.

### Air-defense site role status

Air-defense site role status describes the remaining combat contributions of an air-defense site by role rather than by one broad operational flag. A site may still contribute sensors, shooters, or command/network support independently as component damage changes.

Use `CanContributeSensor`, `CanContributeShooter`, `CanContributeCommand`, `IsCombatIneffective`, and `IsTemporarilySuppressed` style concepts for air-defense behavior. Avoid treating a single `IsOperational` flag as the source of truth for detection, engagement assignment, or SAM launch execution.

Suppression is temporary behavior degradation, not permanent combat ineffectiveness. A suppressed site may lose or reduce local sensor/command contribution while still contributing a live launcher if another suitable site provides weapon-quality guidance through remote engagement.

By default, suppression primarily degrades local emissions, search, fire-control contribution, command/network quality, and launch tempo. It should decay over time so intact components can recover without repair.

### Remote cueing

Remote cueing is network-shared track awareness that helps a site acquire current tracks. Remote cueing alone does not authorize a SAM launch.

### Remote engagement

Remote engagement is network-authorized fire-control use of a shared track by a shooter that did not directly detect the target. Remote engagement requires supporting doctrine, network capability, and sufficient track/network quality.
