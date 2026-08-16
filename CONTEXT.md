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

### Scenario export snapshot

A **scenario export snapshot** is an immutable, simulator-neutral capture of a
paused runtime campaign moment. It contains only the physical state needed by a
sim adapter: mapped aircraft and loadouts, current positions and remaining
routes, airport state, and eligible SAM sites. The adapter translates this
snapshot into its simulator's mission format; it does not query or mutate live
campaign systems while writing the mission.

### AI observation export

An **AI observation export** is a generated simulator mission in which every
aircraft is AI-controlled and the human watches rather than occupying a pilot
slot. It is the first DCS export mode because it tests placement, routing,
tasking, payloads, airport ownership, and SAM behavior without adding player
intervention semantics.

The DCS AI observation scope contains airborne flights, airports, and active
SAM sites. It excludes ordinary ground formations and infrastructure. A future
player-intervention export is a distinct mode because a DCS `Player` or `Client`
unit is not an AI-controlled aircraft waiting to be taken over.

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
- Aircraft loadout stations and carriage configurations when the target simulator needs explicit pylon and payload IDs
- Ordnance types only when the target simulator needs a munition identity independent of its mounted payload configuration

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

Each division template also authors its NATO unit symbol classification for operational-map display. The UI renders this explicit module metadata and does not infer infantry, armor, mechanized, or other symbology from names or combat statistics.

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

Aircraft type capability data includes flight performance, range and endurance, sensors and defensive qualities, external-load capacity, loadout stations and their legal carriage configurations, and explicit support capabilities where applicable. Required point-mass performance consists of cruise and combat speed in knots, climb and descent rate in feet per minute, normal and defensive turn rate in degrees per second, and nominal cruise altitude and service ceiling in feet; these values are authored per aircraft type rather than inferred from mission role. Beam, break, and drag maneuvers use the defensive turn rate, which defaults to the normal turn rate when not separately authored and cannot be authored below it. The third-party ID remains an opaque export mapping rather than a source of campaign capability.

**Aircraft radar quality** is the normalized quality of an aircraft type's own radar and contributes only when that aircraft is sensing, tracking, or supporting weapon employment against another target.

**Aircraft radar detectability** is the normalized ease with which hostile radar can detect and build track quality against that aircraft type. Higher values are easier to detect. Detectability scales a radar's authored equipment range by its fourth root; its effect on quality buildup follows from that adjusted range rather than a second direct multiplier. Radar detectability is distinct from the aircraft's own radar quality and radar defense; changing onboard radar performance must not change how visible the aircraft is to enemy sensors.

**Aircraft air-to-air defense** — three normalized aircraft-type ratings for defeating an otherwise valid radar-guided missile, infrared-guided missile, or gun attack. Radar defense represents onboard RF self-protection after tracking has occurred, infrared defense represents thermal signature and generic infrared countermeasures, and gun defense represents how difficult the aircraft is to track and hit during a gun opportunity. These ratings modify hit probability, not detection, WVR opportunity control, or damage after a hit. Flares and chaff are implicit and have no separate definitions, inventories, or effectiveness values.

_Avoid_: using "airframe" for the Module catalog concept; in this project, airframe can sound like a physical aircraft body, a flight-model implementation, or an individual tail.

**Squadron** — the campaign air formation that owns aircraft inventory for one aircraft type and is based at a starting airport building instance. Squadrons do not require third-party IDs; their aircraft type and airport references provide the mappable simulator entities when sorties are exported.

**Squadron starting condition** — a campaign-template entry that creates one starting squadron at turn zero. It references a module country, aircraft type, and starting airport building ID, and records the squadron's starting aircraft count. Starting squadrons are assumed to have all aircraft ready; missing, damaged, or unavailable aircraft belong to later runtime state.

_Avoid_: adding air wings before a rule or authoring workflow needs an organizational layer above squadrons.

**Air mission capability** — an aircraft type's authored ability to contribute to a requested air effect. Airborne C2 and aerial refueling require explicit support capabilities; air-to-air and future DEAD suitability are evaluated from relevant aircraft performance, sensors, survivability, and compatible ordnance.

One aircraft type may provide multiple air mission capabilities. Capability does not permanently assign an aircraft type to a mission role; the air planner evaluates suitability for the current request and situation.

**Aircraft employment efficiency** — an aircraft type's authored modifier to the time cost of employing compatible ordnance or ordnance profiles. It represents aircraft, sensor, cockpit, and crew-workload advantages in using certain stores without turning those advantages into fixed mission-role labels.

**Internal aircraft gun** — fixed gun equipment authored on an aircraft type as one compatible gun ordnance type and a full-sortie count of abstract gun bursts. An internal gun is not selected as an external store, does not consume ordnance capacity or the external air-combat shot budget, and is materialized into each assigned aircraft's runtime loadout so normal expenditure, diagnostics, recovery clearing, and later sortie rearming apply. Gun bursts may be fully expended and are not held back by the doctrine reserve intended for discrete air-to-air stores.

**Gun burst** — one abstract firing opportunity from an internal aircraft gun, not one projectile or a literal ammunition count. Its gun ordnance type authors employment range, firing geometry, preparation time, projectile-effect speed, hit probability, lethality, and effectiveness against each supported target category. A gun may therefore support aircraft and ground targets through the same target-effectiveness language rather than belonging exclusively to an air-to-air mission role.

In v1, AWACS and tanker aircraft are dedicated support aircraft: an AWACS-capable aircraft fulfills airborne-C2 requests, a tanker-capable aircraft fulfills aerial-refueling requests, and combat aircraft do not substitute for either capability.

_Avoid_: fixed BARCAP, OCA, or DEAD aircraft-role labels when suitability can be derived from capabilities and loadout.

### Ordnance

**Ordnance type** — reusable authored identity data for one munition, store, gun-burst profile, or SAM interceptor in a Module catalog, such as AIM-120, GBU-38, AGM-88, an internal cannon burst, or a SAM interceptor. Its employment category, target effectiveness, and derived platform compatibility distinguish its uses while allowing all employment to share envelope, guidance, hit-probability, travel, and effect language. An ordnance type may carry an opaque third-party munition identity for import or event correlation, but an aircraft payload or rack ID belongs to its carriage configuration.

**Ordnance weight** — the default capacity cost of one munition when evaluating or constructing an external carriage configuration. A carriage configuration authors its complete external-load cost so racks, adapters, and multi-store configurations are counted once. Fixed internal gun inventory is excluded.

**Ordnance effect power** — the coarse campaign effect strength of one store. Effect power is the v1 stat used to decide whether ordnance can meaningfully affect a target's toughness. It may correlate with warhead size or explosive power, but it is an authored campaign abstraction rather than exact physics.

**Ordnance hit probability** — the authored base chance, expressed from 0 to 1, that one released store produces a meaningful terminal hit. Air-to-air release snapshots launch quality from range, aspect, altitude, speed, track quality, and off-boresight geometry; later required guidance support may modify terminal probability without moving a missile entity. Radar-missile drag and beam defenses are categorical gates before the hit roll rather than continuous probability penalties. A successful terminal hit may destroy or damage its selected aircraft according to ordnance lethality and aircraft survivability; any later successful hit destroys an aircraft that is already damaged.

**Ordnance employment envelope** — the authored base firing or release limits of an ordnance type, such as range, altitude, and target-geometry constraints. Live employment may modify that base envelope from tactical conditions such as shooter speed, shooter altitude, track quality, guidance support, or target aspect.

**Ordnance employment suitability** — how appropriate an available ordnance type is for the live target and engagement geometry after its employment envelope is evaluated. A flight selects one suitable ordnance type for an employment pass rather than treating every carried weapon as interchangeable. A valid missile is preferred over a gun burst; the gun becomes the candidate when other weapons lack valid geometry or are unavailable.

**Ordnance guidance mode** — a subordinate authored classification within an ordnance employment category that states what guidance or support a store uses. Air-to-air infrared weapons require no post-launch support, semi-active-radar weapons require support through terminal resolution, and active-radar weapons may require support until their authored autonomous time. GPS-guided and laser-guided stores remain precision air-to-ground ordnance while retaining distinct employment requirements. Anti-radiation guidance requires an emitting Radar-category target at release; a nonzero effectiveness rating cannot authorize employment against a guidance-incompatible or silent target.

**Anti-radiation emitter memory** — the authored time for which an anti-radiation ordnance type retains useful terminal guidance after its selected radar stops emitting. A pending effect remembers the last time its target emitted. Guidance quality decays linearly from full quality toward the ordnance type's authored silent-quality floor over that memory time, then remains at the floor. This creates differences between anti-radiation missile types without simulating seeker physics or a moving missile entity.

**Ordnance preparation time** — the campaign time normally consumed before a flight releases or fires an ordnance type during one employment pass. Employing additional stores of the same ordnance type in the same pass does not normally add more preparation time; changing ordnance type, target set, guidance mode, or attack geometry usually requires a separate pass. A pass still in preparation may be altered or aborted if the aircraft loadout source is lost or the target set becomes invalid.

**Ordnance effect speed** — the authored abstract rate at which a released ordnance closes the release distance to its target for delayed effect resolution. It determines timing without making the ordnance a moving campaign entity.

**Ordnance effect travel time** — the campaign time between ordnance release and effect resolution, calculated once from release distance divided by the ordnance type's effect speed. Later target or shooter movement does not alter the scheduled resolution time, but it may categorically defeat a radar missile by carrying the target beyond the effective maximum range snapshotted at release or by breaking radar lock through an achieved beam. Once released, an undefeated pending effect continues independently of later mission retasking or source loss, subject to its guidance requirements.

**Pending ordnance effect** — a released ordnance employment awaiting its effect-resolution time. It retains the locked ordnance type, quantity, selected member-aircraft targets, release geometry, effective maximum range at release, guidance stage, accumulated support, accumulated target defense, and any categorical defeat reason. It is not a moving campaign entity. A missile whose selected aircraft is already unavailable at resolution becomes ineffective rather than retargeting another member of the flight.

**Missile defeat** — a categorical outcome that prevents a released radar-guided missile from making a terminal hit roll. A kinematic defeat occurs once the target's slant distance from the missile's launch position exceeds the effective maximum range snapshotted at release. A radar-lock defeat occurs when sustained achieved beam geometry and the target aircraft's aggregate radar defense break guidance lock. Defeated missiles remain pending only until their scheduled resolution record is written; they no longer influence tactical threat response, guidance support, engagement reservation, or fire-control capacity.

**Employment pass** — one continuous ordnance-use action by a flight, such as a missile launch cycle, gun burst, bomb release pass, rocket pass, or guided-weapon attack. An employment pass may span simulation ticks. Once started, the pass keeps its selected ordnance profile, target set, and preferred aircraft loadout source until release. Full employment geometry is validated both when preparation starts and at the exact release time; invalid release geometry aborts without spending ordnance. A lost loadout source may be replaced by another live member carrying the selected ordnance.

**Ground-attack opportunity** — a short-lived abstract set of units, components, structures, aircraft, or infrastructure close enough to be engaged during one employment pass. An opportunity is rolled from the mission target's actual composition and current state; it may expose no useful target, one target, or several. It is not persistent within-tile geometry and does not create individual tank, truck, infantry, or aim-point entities.

**Ground target profile** — a battalion definition's weighted list of abstract target kinds that its parent division can expose, including target category, toughness, relative presence, and maximum representation in one opportunity. A division opportunity is derived only from the battalions in that division template, so an infantry-only formation cannot expose tanks while mixed formations can expose infantry, trucks, and armored vehicles together.

**Ground weapon coverage** — the maximum number of abstract recipients one released store can affect in one opportunity. Coverage one is a point effect. Greater coverage permits secondary effects at the store's authored secondary multiplier; secondary recipients must still pass target-category effectiveness and toughness checks. Coverage controls recipient grouping rather than simulating blast radius or exact distance.

**Ordnance employment record** — a typed campaign record for one explainable stage of employment: preparation started, ordnance released, or effect resolved. All three stages are retained for timelines and debugging; ordinary player-facing presentation emphasizes releases and resolved effects rather than every preparation start.

**Ordnance capacity** — the maximum total external-load cost an aircraft type may carry. It is a whole-aircraft constraint after station legality is satisfied, not proof that a store can be mounted. Fixed internal guns do not consume this capacity.

_Avoid_: separate aircraft–ordnance compatibility lists. Aircraft compatibility is derived from the ordnance contents of the carriage configurations permitted on its loadout stations.

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

**Aircraft loadout station** — one stable, aircraft-type-owned external mounting location. A station is simulator-neutral and has a stable HZPL identity, authoring order, optional mirror station, and an allow-list of carriage configurations. A sim Module may map it to an opaque third-party station ID such as a DCS pylon number. Station identity does not model per-aircraft geometry or maneuver state.

**Aircraft carriage configuration** — one complete external installation that can occupy a loadout station, such as a missile on a rail or several weapons on a rack. It owns the carried ordnance types and full counts, external-load cost, and an optional opaque third-party payload ID. A third-party payload ID belongs to the carriage configuration rather than the ordnance type because the simulator representation may include rails, racks, adapters, and quantity.

**Aircraft loadout** — the station-assigned external ordnance and installed internal ordnance physically carried by one campaign aircraft at a given moment, including remaining counts after expenditure. External station assignments are authoritative; aggregate counts by ordnance type are derived views used by campaign combat. Internal guns remain stationless installed inventory.

A planned external loadout chooses at most one carriage configuration per station. Every choice must be legal for that station, every contained ordnance type must be allowed for the aircraft's alliance, and the sum of configuration external-load costs must not exceed aircraft ordnance capacity. Planning determines desired mission inventory and then selects legal station configurations; export translates those exact assignments and must not guess placement or silently omit stores.

**Mission-useful ordnance** — the carried ordnance that can still contribute to a flight's assigned mission effect. Time-based air-combat missions such as barrier combat air patrols and offensive counter-air sweeps become unable to continue their mission when no air-to-air mission-useful ordnance remains; support missions such as airborne C2 and aerial refueling do not depend on ordnance.

For aggregate BARCAP/OCA execution, mission-useful ordnance exhaustion is evaluated at the flight formation level rather than per aircraft. A flight should continue while any assigned aircraft still carries mission-useful air-to-air ordnance; it should return to base only when the formation as a whole has no mission-useful ordnance remaining.

If a time-based combat flight returns to base early because the formation has exhausted mission-useful ordnance before satisfying its assigned effect, the flight is aborted rather than completed. If the assigned effect has already been satisfied, ordnance exhaustion does not change normal recovery.

V1 loadout planning is demand-driven. The planner should not fill unused ordnance capacity simply because capacity remains; carrying unnecessary stores increases fuel burden and should be avoided.

For a sortie's primary target category, the planned primary ordnance quantity should cover the expected target need plus a reserve of either 25% extra or one additional store, whichever is higher. Self-defense ordnance is planned separately and scales with expected enemy air threat: clear skies need little reserve, while enemy air superiority justifies a higher self-defense allocation.

In v1, self-defense ordnance is based on fixed desired shot counts by air-threat level. Longer term, self-defense planning may derive desired hits from expected aircraft threats. Determining the current air-threat level is outside the ordnance foundation.

For v1 barrier combat air patrol and offensive counter-air sweep loadouts, each assigned combat aircraft plans a fixed mission shot budget of four air-to-air shots when capacity allows. At least two air-to-air shots must fit for the aircraft to be feasible; longer term, this budget may be replaced by expected engagement forecasting.

The preferred v1 air-to-air loadout split is two radar-guided air-to-air shots and two infrared air-to-air shots. If one employment category is unavailable or cannot fit, the planner may fill the remaining budget with the other air-to-air category; it should not exceed the fixed shot budget simply because unused capacity remains.

The game should only create sorties whose required loadout can fit the assigned aircraft. Detailed policy for resolving capacity conflicts between primary ordnance and self-defense ordnance is deferred until sortie generation is designed.

**Sortie target desired hits** — the expected number of successful weapon effects needed against a sortie target. V1 sortie planning may provide desired hits directly per target, usually one for simple point targets such as a tank, radar, or building and more for broad or durable targets such as an airfield runway. Loadout planning treats one desired hit as one planned weapon launch, then applies the primary ordnance reserve to cover misses without modeling hit probability. At execution, the current ground-attack opportunity and the selected store's coverage determine how many carried stores can usefully be released in that pass; desired hits remain a planning abstraction rather than a forced one-store-per-pass rule.

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

**Alliance air tasking commander** — the per-alliance runtime authority that owns committed packages, flights, and tasking diagnostics. Bluefor and Redfor each have one; Neutral has none by default. During the scripted-planning phase it does not generate operational demand or compose packages.

Ground tasking, air tasking, and IADS use separate per-alliance command state while operating under shared core-engine rules.

_Avoid_: using one generic alliance AI object as the owner of every ground, air-tasking, and IADS responsibility.

### Alliance intelligence picture

An **alliance intelligence picture** is the persistent, observer-relative record of hostile ground formations, buildings, airports, and static or mobile air-defense sites available to one alliance. Bluefor and Redfor each own a separate picture. Friendly command state remains exactly known and is not duplicated as intelligence reports.

Campaign entities remain the source of physical truth. Commanders, target selection, planning estimates, and alliance-facing presentation consume intelligence reports when reasoning about hostile entities; movement, combat, damage, capture, IADS behavior, and other resolution systems consume authoritative campaign state. An intelligence report may retain the subject entity ID as an opaque bookkeeping and resolution reference, but that reference does not authorize a planner to dereference hostile truth.

Each report carries **information quality**, a continuous value clamped from `0` to `1`. The meanings of intermediate values are deliberately deferred until observation, reconnaissance, and strike targeting require them. Quality `1` represents the most complete physical information the alliance could realistically gather: current location and condition, division-template composition, building build and damage state, airport ground inventory, and air-defense component inventory and condition. It does not reveal enemy ground orders, AI intent, movement progress, IADS tracks, engagement assignments, or other private command bookkeeping.

During the current autonomous-testing phase, hostile division, building, airport, and air-defense reports refresh immediately to information quality `1`. This maximum-information refresh is a temporary producer behind the durable intelligence boundary; consumers must not bypass reports merely because the producer currently mirrors observable physical truth.

Large fixed infrastructure such as airports, ports, factories, and railroads may be seeded as known from campaign start when partial intelligence is introduced, while their current condition can remain uncertain. Static or mobile air-defense sites may later require detection. The first implementation does not add sensor sources, reconnaissance missions, confidence decay, deception, false contacts, or contact fusion.

Enemy supply-network criticality is not inferred yet. Ground planning may value observed hostile supply hubs and railroads as buildings, but it does not inspect authoritative hostile supply assignments or topology to add hidden supply-criticality value.

Tile control and derived front boundaries remain perfect alliance knowledge. The operational-map renderer may retain an explicitly omniscient debug mode that reads authoritative state.

_Avoid_: treating information quality as a property of the observed division, building, or site; it belongs to one alliance's report about that subject.
_Avoid_: exposing exact hostile fields at a lower information quality before that value's gameplay meaning is defined.
_Avoid_: making combat resolution depend on possibly stale intelligence.

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

A discrete, placed asset at a ground-level campaign position with its own type and identity. Its containing operational-map tile is derived from that position. V1 building types are airport, factory, supply hub, fort, port, railroad, refinery, power plant, and air defense.

Railroad is represented as a building in v1. Future supply and pathfinding work may introduce rail connectivity as tile edge properties or a transport network when rules need connected rail lines rather than tile-local rail presence.

Buildings are authored on the campaign template with campaign-local X/Z positions measured in feet. Runtime state tracks each building instance separately so one asset in a tile can be damaged while another in the same tile is untouched.

Buildings may only be placed on land tiles. Ports cannot be placed on ocean tiles. Specific coastal placement validation for ports is not enforced in v1.

**Building ID** — a stable GUID-based identifier for a building instance. A building also records its ground-level `PositionFeet` and building type. `TileId` is derived by projecting that position onto the campaign hex grid; it is not a second persisted placement. Systems may index buildings by the derived tile for efficient lookup, but building identity and placement belong to the building instance rather than to tile data.

Multiple building instances may exist in the same tile, including multiple buildings of the same type or at the same ground position. Tile-domain rules query the building system's derived tile index and then filter by building type or capabilities. Physical-distance rules use the building position rather than the tile center.

Buildings inherit control from the tile containing their position. In v1, buildings do not carry separate owner, controller, or country state; a building's usable faction is determined by the current derived tile controller and the building's functional level. When tile control changes, every building position inside that tile changes control immediately with no separate capture delay or automatic damage. A captured static air-defense building changes control with the tile, but its hosted SAM site is disabled rather than transferred operationally into the captor's IADS.

Building categories and their runtime classes belong to the core engine. Runtime buildings share an abstract building concept for common identity, placement, build level, damage, and functional level. Specific building categories may have their own runtime classes when their behavior or state differs meaningfully. Airports are expected to be specialized and relatively complex; forts may remain simple specialized buildings.

Buildings are owned by a building collection or building system rather than stored inside tile data. The building system derives and indexes tile membership from position; tiles are not the aggregate root for building state.

Each building is tracked with two persisted integers:

- **Build level** — authored capacity at day zero and the current built capacity during play. Build level never decreases; it may increase during play when construction is implemented. Capped at 0–10.
- **Damage** — runtime wear from bombing and similar effects. Damage is per building. Capped at build level and never goes negative.
- **Functional level** — derived, not persisted: `max(0, buildLevel - damage)`. Used for supply throughput, production, combat modifiers, strike targeting, and export when the building is a **mappable entity**.

For an **airport**, the inherited building level is the integrity of its aggregate runway system in v1. It does not count physical runways. Build levels 1–5 provide one nominal runway-capacity channel and build levels 6–10 provide two. The airport's effective channel count is the nominal count multiplied by its integrity fraction (`functionalLevel / buildLevel`) and rounded up: a two-channel airport retains two channels above fifty-percent integrity, falls to one channel at or below fifty percent, and closes at functional level zero.

An **aircraft movement** is one aircraft taking off or landing. Airport throughput is planned in fixed fifteen-minute **movement windows** that do not depend on simulation tick length. Each effective channel supplies one abstract capacity slot per window, and one slot represents up to four aircraft in the same flight movement. Larger flight movements occupy capacity in consecutive windows. Takeoffs and planned recoveries share the same capacity.

Airport movement reservations are derived from the takeoff and landing waypoints of committed flights rather than persisted on the airport. Cancelling a flight releases its capacity, and replacing a recovery route moves its projected landing demand without synchronizing a second schedule. Package planning shifts the whole package to the earliest capacity-feasible time so rendezvous and effect timing remain coordinated.

An airport can conduct normal air operations only when its tile is friendly-controlled and it has at least one effective capacity channel. Runway closure cancels or aborts packages with aircraft still awaiting takeoff and removes the airport from normal recovery selection. Closure does not destroy grounded aircraft; hostile capture remains the airbase-overrun condition. Airborne recoveries take priority over future departures when reduced capacity invalidates earlier plans, and an open airport accepts an emergency recovery rather than destroying aircraft because its abstract schedule is saturated.

_Avoid_: treating a capacity channel as a physical runway, persisting a competing airport schedule, or adding runway headings, taxi queues, parking, weather limits, and aircraft-specific runway requirements before a rule needs them.

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

**Aircraft operational geometry** uses physical distance rather than tile counts. Horizontal mission-area radii and comparable planning distances are authored and presented in kilometres, altitude is authored and presented in feet, and both are converted into the continuous airspace coordinate frame for calculation. Tiles remain appropriate for campaign indexing and ground-map ownership but do not define an aircraft mission's radius, range, or proximity boundary.

_Avoid_: using tile cube coordinates or Unity scene-transform coordinates as an aircraft's authoritative position.

The center of cube tile `(0,0,0)` is the airspace origin. Tile centers use the operational map's existing flat-top hex orientation and a fixed 20 km center spacing converted to feet, with map east mapped to positive X and map north mapped to positive Z. This scale is an engine invariant, not campaign-template content. Ground assets use their authored campaign positions at zero feet MSL until terrain elevation exists; tile-centered assets use the corresponding tile center.

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

Air tasking currently validates existing commitments and materializes due authored package plans after the tick's current air, ground, and campaign effects resolve. Plans and flights use stable authored identities and ordering so the result remains deterministic.

### Air planning cadence

How often the air-tasking layer performs global planning and lightweight local planning.

**Current development cutover:** autonomous air planning is intentionally disabled while mission execution is developed. Campaign templates author explicit `AirPackagePlan` records containing operation intent, named squadrons, aircraft strength, flight tasks, route geometry, effect timing, escort relationships, and optional BARCAP or DEAD payloads. Development schedules cover at most the first 24 campaign hours. Each due plan is attempted once and flows through the ordinary package builder, reservations, airport scheduling, execution, recovery, logging, and export paths. There is no current mission-request graph, priority scoring, projected-effect fulfillment, air-control assessment, support-demand history, or automatic rematerialization. The autonomous-planning concepts below describe deferred design work, not active runtime behavior.

Global air planning runs on a longer cadence aligned with ground operational cadence — template-configurable within engine bounds (**one to six hours**; **default six hours**). It recalculates theater-wide mission priorities and purges unfulfilled requests from the previous planning cycle.

Every simulation tick, lightweight planning updates projected effects, detects urgent coverage gaps, validates existing commitments, and may make bounded local adjustments. Significant events such as loss of supporting coverage, a major airbase loss, a newly detected raid, target destruction, or a collapsed SAM belt may trigger local replanning before the next global cadence.

**Initial air-picture bootstrap** delays offensive counter-air and DEAD request generation when air tasking first initializes. The initial planning cycle may commit BARCAP and support sorties; offensive planning becomes eligible only after those earliest sorties could launch and one complete air-control assessment interval has closed. Reaching that point triggers one immediate global air replan rather than waiting for the next operational-cadence boundary. This bootstrap delay suppresses request generation, not merely package fulfillment, so an offensive package cannot lock an empty startup threat estimate.

Air **execution** (sortie movement, IADS refresh, engagement assignment, SAM launch resolution, and air-to-ground effects) also runs every simulation tick. Tick-level adjustments may scrub or retask pre-takeoff committed sorties in response to new threats or invalid plans. Active sorties are not cancelled, retasked, or rerouted by the air-planning layer.

**Combat event ordering** — the chronological resolution of ordnance preparation completions, releases, and pending effects at their exact campaign timestamps within a simulation tick. Events sharing a timestamp are validated as one batch before their outcomes are applied in deterministic order, so one simultaneous outcome cannot retroactively prevent another valid release or impact.

**Air tactical checkpoint** — an internal chronological boundary used to resolve air movement and combat inside one public simulation tick. Checkpoints occur at most five campaign seconds apart and also at exact takeoff, ordnance-release, and effect-resolution times. Every airborne flight chooses its command from the same immutable checkpoint snapshot before any flight moves. Checkpoints are resolution within the single simulation clock, not a parallel air clock or public configuration setting.

_Avoid_: full theater-wide air replanning every simulation tick.

### Committed sortie

A **committed sortie** is one aircraft's planned employment after that aircraft and its flight mission have been assigned but before it has taken off. It survives later air-planning cycles, although the alliance AI may cancel or retask it before takeoff.

### Active sortie

An **active sortie** is one aircraft's employment after it has taken off. Takeoff locks its assigned mission intent and any already-assigned target: later air planning may neither cancel nor retask it.

An active sortie may still end unsuccessfully because its aircraft are lost, package-integrity rules force an abort and return to base, fuel reaches a doctrine recovery threshold, or the assigned effect can no longer be achieved.

_Avoid_: using an operational replan to change an airborne sortie's assigned mission or target.

**Execution-level tasking** — a bounded decision made by an active flight's own mission behavior within its already-authorized mission intent, such as a BARCAP flight intercepting a predicted crossing of its assigned barrier segment. Tactical steering temporarily overrides immediate route guidance without replacing the materialized route; recovery diversion or a future lasting mission amendment may replace an authorized unflown segment explicitly. Execution-level tasking is not an air-planning retask.

**Counter-air tactical guidance** — persistent execution-level steering for an active BARCAP or OCA flight against one retained tactical target. Guidance predicts a standoff intercept, presses for valid forward launch geometry, holds launch setup through preparation, supports released weapons with a crank when required, reacts defensively to incoming weapons, and extends or recommits before eventually resuming the unchanged materialized route. OCA proactive engagement additionally requires acceptable live local combat odds. Remembered background-interference risk will be recalculated as part of the planned OCA behavior rewrite. Guidance does not authorize pursuit during recovery, after mission-useful ordnance exhaustion, beyond doctrine pursuit limits, or outside the mission's tactical area. A materialized BARCAP clamps all tactical aim points to the defended side of its threat-facing weapon-release line; even launch support or defensive maneuvering cannot turn into a pursuit through the screen.

**Air-combat intent** — the high-level responsibility currently overriding or preserving route guidance: follow the mission, engage a target, defend, disengage, or recover. Intent is persisted on the flight with its start time and decision rationale.

**Air-combat maneuver** — the immediate steering technique selected under an air-combat intent, such as intercept, press, launch setup, crank, beam, break, drag, extend, recommit, or abstract dogfight. A radar missile released outside its no-escape range produces a drag; other radar-guided terminal threats produce a beam, while infrared terminal threats produce a break. Drag defeats a missile only when actual separation exceeds its snapshotted maximum range. Beam defense earns a radar-lock-break opportunity only from achieved threat-relative geometry rather than from the maneuver label alone. A maneuver owns no polymorphic runtime object; its per-flight start time, minimum commitment, side, target, aim point, and supported effect live in flight tactical state.

**WVR decision range** — the eight-kilometer close-combat range within which a flight explicitly decides whether to merge or extend before ordinary launch geometry is evaluated. An authorized BARCAP defender may merge with an infrared missile or aircraft-effective gun; an OCA sweep may merge only after its radar-guided air-to-air weapons are depleted and a close-range weapon remains. A flight extends when its own unresolved air-to-air effect is already pending against the target, no WVR-capable weapon remains, or its mission does not authorize a discretionary merge.

**WVR engagement** — a persistent aggregate close-combat encounter containing one or more opposing flights. It resolves in twenty-second rounds and hard-locks each participating flight until that flight successfully disengages or one side is defeated. A neutral merge opens with simultaneous opportunities for both sides; later control contests may produce one opportunity or another simultaneous exchange. Engaged flights hold aggregate position and burn combat fuel. One aircraft-type WVR combat rating, effective aircraft numbers, temporary advantage, and bounded deterministic uncertainty decide which side receives each attack opportunity; the resolver does not calculate corner speed, turn radius, or individual fighter geometry. Damage does not remove an aircraft from WVR: it severely reduces WVR rating, effective combat weight, and speed, and causes the flight to seek disengagement. A later successful weapon hit always destroys an already-damaged aircraft rather than applying a second damage result. Disengagement begins after the opening exchange for damaged, bingo, or weapon-empty flights and becomes mandatory after twelve rounds. Relative capability, temporary advantage, covering friendly flights, and outside friendly air or SAM pressure modify each flight's deterministic escape chance. Failed attempts leave the flight locked and grant the opponent an immediate opportunity; successful flights commit to a forty-five-second extension before normal recovery or targeting can resume.

**WVR advantage** — temporary positional control in a WVR engagement: neutral, favorable, or dominant. Rear entry or an unaware target grants favorable entry advantage; both grant dominant entry advantage. Entry advantage supplies the next attack opportunity, dominant advantage then degrades, and later control contests can remove or reverse it. Advantage changes opportunity and weapon hit probability, while awareness strongly penalizes gun and infrared attacks and the target aircraft's matching air-to-air defense rating further reduces an aware-target attack.

**Air-to-air engagement posture** — the active mission's rule for when a flight may spend air-to-air ordnance against hostile aircraft. Strike and other non-air-combat flights continue self-defense passes while a **hot threat** remains, then resume their primary mission when no hot threat remains or no suitable air-to-air ordnance is available. A BARCAP monitors a hostile track whose projected course crosses its threat-facing weapon-release line but commits only at the latest safe time: hostile time to that line must be no greater than the patrol's time to reach effective launch geometry plus a ninety-second tactical margin. A defensive commit authorizes intercept steering but not weapon employment until the inbound course is confirmed. Patrol time accounts for current position, combat speed, remaining air-to-air weapon range, and preparation time. A hostile that has already penetrated the line or has a weapon in flight against the patrol remains an immediate defensive target within the aircraft-specific response depth. Another flight's internal intercept intent is not treated as an observable hostile act. Merely being nose-on and inside weapon range does not let a spatial BARCAP treat an aircraft remaining on the hostile side as a self-defense target. This keeps BARCAP defensive: opposing patrols stationed on their own sides of a front do not hunt one another merely because their broad operating areas are nearby. Authored or legacy BARCAP routes without a materialized barrier retain bounded area authorization for compatibility. Offensive counter-air flights defend themselves during ingress, proactively hunt only while executing inside their assigned sweep corridor, and revert to self-defense during egress.

**Air-track aircraft-type identification** becomes available when a current track reaches quality 0.5 and remains attached to that track after identification. Before that threshold, or when an identified type cannot be resolved from the current Module, defensive threat assessment assumes the worst credible aircraft capability in the Module. Once the type is known, assessment uses the aircraft type's compatible ordnance and combat capability rather than inspecting its actual carried loadout. A known dedicated AWACS or tanker type is not a proactive air threat merely because its course points toward a package or barrier; an active weapon, weapon preparation, or committed attack against a defended flight remains an immediate hostile act regardless of aircraft type.

**Defensive air-threat qualification** combines credible aircraft capability, projected relative motion, observed motion history, and defender response time. A BARCAP may commit immediately inside its latest-safe window, and that entry starts the threatening-course confirmation timer; time spent on a distant approach does not pre-arm it. A non-penetrating contact must then maintain a threatening course for thirty seconds with no observation gap longer than thirty seconds before weapon employment is authorized. An air weapon already in flight or actual defended-line penetration bypasses that confirmation delay. Repeated reciprocal motion with little net displacement is treated as a bounded patrol while the contact remains inside its observed forward limit; movement beyond that limit resumes ordinary qualification without revealing the hostile flight's true mission or route. Fighter escort predicts the first time the hostile track enters a moving threat envelope around each protected flight. That envelope includes the identified aircraft type's maximum compatible air-to-air reach, a five-kilometre protection buffer, and track-quality uncertainty; an unidentified type uses the worst credible reach and combat speed. The escort commits only within a ten-minute prediction horizon and when hostile time to envelope entry is no greater than escort time to useful intercept geometry plus ninety seconds. BARCAP uses the same identification and capability rules, but its mission-specific danger geometry is a projected crossing or penetration of the assigned barrier rather than entry into a moving package envelope.

**Hot threat** — a hostile flight inside the evaluating flight's live air-to-air employment envelope and flying toward it within ±30 degrees. For a spatial BARCAP, this geometry-only rule is additionally bounded by its defensive barrier authorization. A hostile flight with a pending ordnance effect targeting the evaluating flight remains a threat regardless of its later range or aspect.

**Air threat priority** — the relative danger of hot threats, ranked from their range and aspect: a closer and more directly nose-on hostile is more dangerous, while a farther or less directly approaching hostile is less dangerous. Cold hostile flights are not threats; a hostile with ordnance already pending against the evaluating flight takes priority over geometry-only threats.

**OCA target priority** — the order in which an offensive counter-air flight selects eligible hostile flights during its hunting posture. Hostiles already attacking the flight take priority, followed by hot threats, a target already assigned by another flight in the same package, hostile counter-air flights, and then the nearest eligible hostile; the larger hostile flight breaks equal-range ties.

**BARCAP threat allocation** — one deterministic allocation computed from the immutable tactical checkpoint before any flight chooses its command. Self-defense remains local and immediate but cannot bypass a spatial BARCAP's defensive boundary unless hostile ordnance is already inbound. Distant contacts with a projected release-line crossing and contacts assessed as bounded patrols remain monitored rather than assigned. Contacts inside their latest-safe commit window are ordered by predicted crossing time, estimated raid power, and stable identity; eligible patrols are assigned using retained engagement, live interception time to the predicted crossing, fuel, and sufficient available air-combat power until doctrine's desired advantage is met. A patrol receives at most one proactive target per checkpoint, preventing independent flights from swarming the same contact while another raid is uncovered. An unconfirmed contact may receive defensive intercept steering while weapons remain held. Threat capability, observed motion, crossing geometry, and employment authority are continuously revalidated through weapon preparation and again at the exact release checkpoint: an identified support aircraft, bounded patrol, or contact that turns away loses fire authorization and any unreleased pass is cancelled. A contact that penetrates the line remains authorized only while it is on the defended side and within the assigned aircraft's response depth, so the patrol cannot be dragged through the barrier in pursuit.

**OCA air-interference frontier** — the friendly-facing edge of meaningful remembered hostile combat presence or hostile air activity. The current temporary request generator selects a nearby active interference edge without deriving a relative control value. OCA frontier eligibility, concentration, and depth risk will be recalculated as part of the planned OCA behavior rewrite. Quiet airspace does not create an OCA request by itself.

An OCA route enters from the lower-interference neighboring tile, makes one push across the selected frontier toward at most one acceptable hostile-side neighbor, and exits back through the entry. The materialized station entry and non-repeating endpoint are the proactive engagement limits; the exit mission action completes the sweep. Hot threats and incoming weapons may still trigger self-defense outside the corridor. The cached air-interference assessment informs the temporary route heuristic but does not replace live contacts or live force odds during tactical execution.

**Recovery diversion** — an execution-level change to an airborne flight's return and landing destination when its assigned recovery airport is no longer friendly. The flight applies the recovery-airport fallback hierarchy without changing its locked mission or target; because range is ignored initially, any valid alternate is reachable.

### Sortie

A **sortie** is one campaign aircraft's employment from takeoff through landing, loss, or another terminal outcome. It is the individual-aircraft unit of air activity, not necessarily a separately persisted planning object; its assignment and state may be represented by the aircraft's membership in a flight.

### Flight

A **flight** is a persistent air-planning formation containing one or more campaign aircraft that share a mission role, route, and timing. Flights are the aircraft-assignment and execution groups coordinated by packages.

Every flight draws its aircraft from exactly one squadron and therefore has one aircraft type and one operating base. A package may coordinate flights from multiple squadrons and bases.

During aggregate campaign execution, the flight owns the authoritative airspace position, velocity, and route progress shared by its member aircraft. Member aircraft retain their individual identity, condition, loadout, and sortie outcome without independently maneuvering inside the formation.

**Flight-level ordnance employment** is the aggregate combat abstraction where a flight chooses and resolves shots as one formation because all member aircraft share aircraft type, mission context, and campaign position. Individual campaign aircraft still own the carried ordnance counts; flight-level employment selects which aircraft loadout spends a store without modeling separate lead/wingman geometry.

In v1, a flight may have only one active employment pass at a time. Air-to-air employment passes target one hostile flight and plan at most one missile per surviving target aircraft, bounded by the compatible missiles available across the firing flight. Future rules may allow simultaneous employment passes when per-aircraft independence or multi-channel attacks become worth the added complexity.

Each flight belongs to exactly one owning package. A supporting flight may additionally be referenced by other packages that use its service, but those references do not give the flight multiple owning packages.

**Flight route** — the concrete, ordered path assigned to every flight before its package commits, from takeoff through terminal landing, including any transit, rendezvous, station, mission, and return legs needed for that flight's role. Package creation may accept authored route input or generate default geometry, but in both cases it materializes a complete waypoint sequence that becomes the flight executor's single source of movement truth.

**Air route geometry planner** — the replaceable planning policy that selects generated ingress and egress transit geometry before a flight commits. The initial policy places one laterally offset transit waypoint on each leg, puts ingress and egress on opposite geographic sides of the direct route, and varies the selected side by package identity. It does not yet evaluate threats, fuel, weather, tanker placement, support timing, or airspace restrictions; those factors may enrich or replace the policy without changing the flight executor.

**Air waypoint** — an airspace position on a flight route paired with a semantic action or transition, such as takeoff, rendezvous, begin station work, perform a mission action, return to base, or land. A waypoint does not prescribe speed; flight guidance chooses movement performance.

A waypoint carries its planned campaign arrival time when timing is operationally meaningful. Takeoff, rendezvous, station entry, discrete mission action, racetrack release, and landing timing are therefore part of the materialized route; flight-level takeoff and effect timing are derived summaries rather than competing stored execution clocks.

A station-entry waypoint may carry the air mission area affected while its station loop is active. The initial route copies the mission request's area onto its one station entry; a flight's current effect area is derived from its active station waypoint rather than maintained as competing flight-level execution state.

A racetrack uses an explicit station entry, intermediate loop waypoints, and a terminal endpoint in the ordered route. Its terminal endpoint identifies the earlier station-entry waypoint to repeat from and the campaign time when repetition ends; after that time execution advances to the next waypoint. Because each loop instruction and effect area belongs to its waypoints, one flight route may contain multiple sequential stations without a separate station entity, flight-level loop state, or a general waypoint graph.

**Flight execution event** — a bounded record that a flight actually crossed a semantic waypoint or completed an execution transition at a campaign time, such as takeoff, rendezvous, station entry, mission action, station exit, RTB, or landing. Common milestone times are derived from these events; repeated racetrack laps do not create events, and mission-specific outcomes such as damage, suppression, or weapons expended belong to separate typed effect records linked to the relevant execution event.

After commitment, ordinary tick execution does not regenerate direct legs, station geometry, or rendezvous points. The initial model permits only recovery diversion to replace the unflown recovery portion; future execution-level tasking may explicitly replace an authorized unflown mission segment before the executor continues.

_Avoid_: a committed flight without a complete materialized route, competing package- and flight-level route geometry, or opaque waypoint flags whose meaning depends on undocumented combinations.

**Recovery airport** — the friendly airport where a flight intends to land. Recovery selection prefers the flight's assigned recovery airport, then its squadron's current airport if that differs, then the nearest friendly airport; landing at the final fallback reconstitutes the squadron at that airport, while absence of any friendly recovery airport causes the flight to fail and its aircraft to be lost.

An airborne flight preserves its owning squadron even if that squadron loses its previous airport. A squadron is eliminated only when it has no surviving campaign aircraft, not merely because its base was captured or disabled.

Airport runway integrity affects package timing, launch eligibility, recovery selection, and diversion. A friendly airport at functional level zero is closed but not overrun: grounded aircraft remain present while unlaunched packages cancel or abort, and airborne flights use the normal recovery-airport fallback hierarchy. Airport repair remains deferred.

**Approach waypoint** — the final generated navigation waypoint before recovery, placed on the inbound line using the flight's cruise speed, descent rate, and current altitude so descent begins late enough to avoid ground-level transit and reaches the airport near zero altitude. Reaching the following airport landing waypoint ends the flight without runway, pattern, or ATC simulation.

**Racetrack station route** — the shared station-keeping route pattern for sustained air missions: enter the station, follow the ordered loop waypoints, follow the terminal endpoint's repeat instruction until its release time, then continue to the return legs. An automatically derived BARCAP barrier lies across the representative hostile approach, while its continuous world-space racetrack lies along that approach as two parallel legs joined by sampled semicircular turns. Track length is a doctrine leg time converted through the assigned aircraft's cruise speed rather than a fraction of campaign tile scale, and turn radius comes from cruise speed and aircraft turn rate. The package builder evaluates a fixed set of ten world-space positions in a defended-side wedge behind the center of the largest current barrier gap: three useful depths with center, left, and right offsets, plus one maximum-depth retreat option. It orders maximum new coverage first, then favors candidates until they provide doctrine's desired intercept margin; once that margin is met, greater rearward depth breaks ties. Feasibility accounts for every point and segment of the closed loop, aircraft combat speed, planned air-to-air launch range and preparation time, representative hostile speed, functioning friendly IADS radar warning along the approach, command delay, the threat-facing weapon-release line, known hostile SAM coverage, and a planning safety margin. Because intelligence can change during sortie preparation, execution refreshes the known-SAM picture at takeoff and revalidates the complete committed BARCAP route, including each repeat closure. A newly unsafe grounded package is cancelled and its sustained request becomes actionable for rematerialization; it is never launched merely because its route was safe when committed. A displaced station must gain signed rearward depth in its local barrier frame and never moves toward the threat. Initial AWACS and tanker tracks remain centered on their selected low-interference station tiles and retain their simple tile-scaled placeholder tracks. OCA is not a racetrack: its discrete entry, non-repeating push endpoint, and exit currently follow the local hostile-interference gradient. Authored route waypoints override these placeholder geometries.

**Nominal cruise altitude** — the aircraft type's normal transit altitude in feet above mean sea level.

**Service ceiling** — the aircraft type's maximum supported campaign altitude in feet above mean sea level.

**Mission altitude** — the desired altitude in feet above mean sea level selected by a flight's mission behavior. Route generation uses the mission altitude where applicable, clamps it to the assigned aircraft type's service ceiling, and otherwise uses that type's nominal cruise altitude.

Generated v1 mission altitudes use an alliance-doctrine BARCAP station altitude whose default is 40,000 feet, 40,000 feet for OCA, 35,000 feet for AWACS, and 25,000 feet for tanker missions. These are replaceable defaults rather than hard limits; generated BARCAP altitude selection does not yet search ACO blocks, terrain, or surface-threat alternatives.

_Avoid_: treating low, medium, and high altitude bands as exact aircraft performance or route altitudes.

A sustained flight repeats its station route until its already-assigned effect end, then follows its return and landing legs. Unrefueled BARCAP, AWACS, and tanker route planning bounds that effect end by the least-enduring assigned aircraft's doctrine joker point, with a small timing margin, and rejects a sortie that cannot complete one station circuit. Tactical maneuvering may burn fuel faster and force an earlier recovery. A refuel-capable BARCAP may instead extend through the continuous tanker coverage reserved for its package and refuel repeatedly while that coverage remains active. Detailed range, reserve-to-alternate, and physical tanker-offload planning remain later fidelity work.

**Flight execution phase** — the mission-independent stage of a flight's physical journey: awaiting takeoff, outbound, executing, returning, landing, or ended. Execution phase is separate from lifecycle outcome; an aborted flight has stopped its assigned mission and may have a terminal planning outcome while remaining physically in the returning phase until its surviving aircraft land.

_Avoid_: mission-specific execution phases such as BARCAP, AWACS orbit, tanker track, or strike; those are mission behaviors performed during the generic executing phase.

**Air mission behavior** — the mission-specific effect a flight performs during its generic executing phase, separate from the shared rules for takeoff, route movement, return, and landing. Initial behaviors establish BARCAP presence on an assigned defensive barrier segment, airborne-C2 service, aerial-refueling coverage, or a no-effect OCA sweep action; later air-combat and ground-attack behaviors may add their own resolution without replacing flight execution.

_Avoid_: implementing navigation, takeoff, RTB, or landing separately for each mission type.

An initial BARCAP, AWACS, or tanker flight achieves its mission by reaching station and remaining there through its assigned effect end. It becomes completed after returning and landing; a flight that never reaches station fails, while one explicitly directed home before achieving the mission is aborted.

An OCA flight achieves its mission by entering its bounded sweep corridor, making one push to its hostile-side limit, exiting back through the entry, and then recovering. Kills are not required: completing the selected air-interference-frontier pass is the requested effect. If the flight exhausts mission-useful air-to-air ordnance before exiting, it returns to base early as an aborted combat mission. Later air-interference refreshes inform whether another sweep is needed; a sweep does not declare success merely because its own presence changes the assessment.

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

The fixed delay is separate from transit time and does not introduce detailed arming, taxi, runway, or ATC simulation. Aggregate airport movement capacity may move the whole package later after the preparation boundary.

**Package effect window** — the time interval during which a package's coordinated flights are intended to produce the requested effect at their mission location. Effect start is planned time on station, never takeoff time; each flight calculates its takeoff time from its operating base, route, cruise speed, and the package's timing so required flights can synchronize.

If preparation and transit make the requested effect start unreachable, projected coverage begins at the later feasible station-arrival time rather than crediting the outbound flight with an effect it cannot yet provide.

**Package rendezvous** — a shared waypoint and time where required flights converge before conducting their coordinated mission. Required supporting coverage must be active when the package reaches the portion of its route or effect window that depends on that support.

An OCA package with more than one required combat flight uses a package rendezvous before proceeding to the mission area. Spatial BARCAP first assigns one-aircraft station packages so scarce patrols can screen every open barrier before reinforcement. The final open station may receive doctrine's preferred station strength when no other spatial gap remains; later packages reinforce understrength covered segments. A single-flight combat package skips rendezvous, and AWACS or tanker flights proceed independently to their own stations rather than joining a supported package's formation.

Rendezvous is a synchronization barrier: an early flight holds at its rendezvous waypoint until every required combat flight has arrived, then the package releases those flights together at their coordinated speed. If a required flight cannot arrive, package-integrity rules abort the package.

An automatically generated rendezvous lies halfway between the participating combat flights' launch-base centroid and the mission-area center. It uses a common altitude limited by the lowest participating aircraft service ceiling and a common arrival time calculated backward from planned mission-area arrival; explicit package route data may override this placeholder.

Rendezvous is a package coordination choice rather than a universal rule for every mission. Future packages may define staggered coordinated segments, such as SEAD reaching a threat area before a strike flight, without changing the shared flight executor.

When suitable aircraft are otherwise comparable, package building prefers flights from squadrons at the same airport to reduce coordination and transit cost. This is a preference rather than a requirement; one package may still combine flights from different operating bases.

Initial generated routes keep ingress and egress distinct with simple laterally offset transit waypoints. Each BARCAP flight executes a threat-aligned station loop at a route-, fuel-, SAM-, warning-, and response-feasible position in the defended-side wedge behind its assigned portion of the largest current barrier gap. BARCAP commitment and takeoff diagnostics record the station geometry, planning lead time, known-SAM counts, and whether any takeoff-blocking SAM was absent from the planning threat set; airborne avoidance and failed relocation diagnostics identify the blocking site, affected geometry, and rejection causes. AWACS and tanker flights execute ordinary centered station loops on selected low-interference tiles. OCA executes one interference-frontier pass whose entry and push endpoint are selected from neighboring air-interference assessments and whose exit returns through the entry. Multi-flight OCA packages insert their required rendezvous before the shared ingress leg. The initial geometry prevents routine outbound-route reuse but does not claim to minimize operational risk; detailed reserve-to-alternate routing, tanker placement, support timing, assembly patterns, and ACO engagement-zone constraints remain future route-planning improvements.

_Avoid_: creating independent strike, escort, and support flights without recording the operational effort that coordinates them.

### Supporting flight

A **supporting flight** provides an airborne service, such as AWACS coverage or aerial refueling, that may support multiple packages during its mission. It belongs to the package created for its own mission request, while other packages reference the service it provides without taking ownership of the flight.

A receiving package may reserve a continuous support window across multiple sequential supporting flights. Each time-segment reservation belongs to the supporting flight that provides that portion of the window, allowing a long-running receiver to hand off between tanker rotations without owning those tanker flights or changing its locked mission intent.

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

In v1, air-planning intelligence provides exact friendly squadron readiness and airfield state. Hostile airport information is projected from the observing alliance's intelligence picture through deliberately limited, airport-level reports: the airport's broad damage condition, information quality, observation time, and aircraft counts grouped by observed aircraft type. A report distinguishes aircraft observed on the ground from aircraft that appear available, but it does not expose hostile squadron identity, individual aircraft status, assignment, mission, package, route, or loadout.

Hostile airborne strength and activity remain separate from airport intelligence. They come from the alliance's remembered air-interference assessment, which is populated only by current IADS tracks; airborne aircraft are excluded from hostile airport reports.

_Avoid_: allowing air-planning consumers to read hostile airport, squadron, or aircraft truth directly instead of using the alliance intelligence picture's constrained enemy-airport reports and remembered air-interference assessment.

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

**Air superiority** is a higher-level assessed campaign condition describing an alliance's operational freedom in a particular area and time. It is an operational objective that drives concrete mission requests, not a tile value or a mission category assigned directly to a package or flight.

BARCAP, offensive counter-air, airborne C2, aerial refueling, and DEAD may preserve or improve air superiority through distinct requested effects.

Future air-superiority assessment may compare projected **air-combat power**, available support, expected hostile forces, losses, and doctrine for the relevant area and time. The current tile intelligence product records interference rather than declaring superiority or control. Air-interference intelligence does not depend on knowing an aircraft's current weapons or remaining ammunition.

_Avoid_: air superiority mission.

_Avoid_: treating a tile's interference estimate as proof that an alliance controls that airspace.

### Air-interference assessment

An **air-interference assessment** is an alliance-scoped, cached estimate for a campaign tile. It remembers accumulated friendly and hostile combat power and physical activity independently. Each side's normalized **air interference** is the greater of that side's combat-presence rating and air-activity rating. Low friendly and hostile interference means quiet or unknown airspace and is operationally clear for either side; high values for both mean both sides can interfere there. Air interference is not tile ownership, air superiority, or ground control.

The assessment is refreshed every 30 campaign minutes from aircraft dwell accumulated since the previous refresh. Airborne aircraft create physical **air activity** only in the tile they occupy. Friendly and hostile activity are remembered separately; their combined value describes general busyness. Friendly counter-air aircraft project their authored **air-interference capability** across a radar-adjusted combat-speed response envelope. An unidentified hostile IADS contact instead projects estimated combat power from its estimated aircraft count using the v1 unknown-contact capability assumption, across a response envelope derived from its observed speed. Once IADS identifies the airframe, the track uses that airframe's authored air-interference capability and response characteristics. Influence is full through five minutes of response travel, then declines smoothly to zero at ten minutes. Current loadout, weapon characteristics, and remaining ammunition do not affect this intelligence product. These envelopes are accumulated on campaign tiles, and projected presence does not create physical activity outside the contact's occupied tile.

AI safety decisions use absolute hostile air interference. Friendly interference does not cancel hostile interference: a tile where both are high remains hazardous to both. Airborne C2 and tanker station selection chooses the lowest-hostile-interference tile in the requested area and rejects any tile at or above the meaningful-interference threshold. The air-interference overlay uses the relative blue/red balance only to choose hue and absolute interference for opacity, so quiet airspace is transparent and two-sided interference remains visible.

New combat presence is learned quickly with a 30-minute rise half-life, while remembered combat presence decays in normalized presence space with a three-hour half-life so routine takeoff and landing cycles do not cause the interference map to fluctuate sharply. Remembered combat power below `0.05` is discarded, allowing an unreinforced assessment to become exactly clear instead of retaining an insignificant positive trace. Physical air activity uses a two-hour half-life. When neither side reinforces its remembered presence or activity, both interference values decay toward zero.

Each alliance owns its assessment because hostile evidence depends on that alliance's intelligence. Friendly flights use truth, while hostile flights contribute only through that alliance's current IADS tracks. The track supplies contact identity, last-known position, estimated strength, estimated combat power, observed motion, observation quality, and aircraft type only after identification. Hostile mission, loadout, squadron, package, and other flight bookkeeping remain internal to simulation resolution rather than serving as planning knowledge.

Grounded ready aircraft do not contribute to observed air interference. Neutral territory is outside the air-interference assessment domain: aircraft activity and combat presence are neither accumulated nor rendered there, and projected counter-air influence cannot pass through it. A future deck-launch-interceptor or quick-reaction-alert contribution requires an explicit alert assignment; ordinary readiness is not treated as proof that fighters will interfere with a tile. Surface-to-air threat remains a separate route-risk input.

_Avoid_: recalculating interference when an AI asks for it, recording an unbounded aircraft-position history, treating friendly interference as cancellation of hostile interference, or deriving hostile interference directly from hidden enemy campaign objects once track-based intelligence is active.

### Initial air-tasking mission requests

**Barrier combat air patrol (BARCAP)** is a sustained, defensive counter-air request represented by an ordered line of contiguous friendly-side barrier tiles, a representative hostile approach, and the friendly assets screened by that line. The planner first forms barriers across connected front-line division positions for each materially different hostile approach sector and then traces approach lanes from meaningful hostile airborne combat presence and functional hostile airports with apparently available combat-capable aircraft toward every friendly-controlled airport. Disconnected ordered runs become separate barriers rather than visual or operational segments crossing hostile territory. An existing front barrier receives credit when it already blocks an airport's approach lane; a supplemental barrier is created only for an uncovered lane. Airports currently hosting usable squadrons are more valuable than empty, damaged, or otherwise reserve airports that may be needed later. Empty hostile airports do not create airport-defense demand, but when no combat-air threat is known all known hostile airports remain a low-information fallback for orienting barriers that protect front-line divisions; if no hostile airport exists, the nearest hostile-controlled ground provides direction instead. A threat reference on the protected line is replaced by an external hostile reference; geometry without a valid hostile-to-friendly direction is not generated. An isolated division or airport can own a one-tile local barrier. Future CAS and strike packages become additional screened assets when those mission types exist; no placeholder demand is generated for them now.

**BARCAP response coverage** is the portion of a planned barrier that an assigned station flight can defend in time from the worst point of its threat-aligned racetrack. The protected tile line owns a default threat-facing weapon-release standoff of ten nautical miles (18.52 km), representing the deadline before a typical attacker could release against the screened assets; the offset is clamped before the hostile reference on smaller campaign tile scales and never defines the defended normal itself. For each barrier tile, reach is calculated from aircraft combat speed, planned air-to-air launch range and preparation time, functioning friendly IADS radar warning along the hostile approach, representative hostile speed, command delay, and hostile time to that release line. The package builder centers demand on the largest current spatial or defensive-capacity gap, searches a defended-side candidate wedge, and credits only tiles the complete racetrack can still defend. Spatial presence and defensive capacity are separate: zero-coverage tiles are filled before segments below doctrine's preferred aircraft strength are reinforced. Live engagement authorization uses current position, remaining weapons, and the actual hostile track to calculate a latest-safe commit window. Invalid direction geometry fails closed. A P-51 and an F-16 therefore need not cover the same barrier length, use the same station depth, or commit at the same distance. When strike and CAS loadouts exist, the default release standoff may be replaced by known or conservatively estimated hostile weapon reach.

**Offensive counter-air sweep** is a discrete request for fighter aircraft to fly one bounded pass across active hostile air interference. The current temporary request generator concentrates one sweep on the nearest active interference layer per planning cycle. OCA demand, eligibility, depth risk, and priority will be recalculated as part of the planned OCA behavior rewrite; no relative air-control value is retained for that future calculation.

**Provide airborne C2** is a request to establish shared airborne command-and-control and surveillance coverage through an AWACS-capable supporting flight. Its station is selected in assessed airspace below the meaningful hostile-interference threshold.

**Provide aerial refueling** is a request to establish shared refueling coverage through a tanker-capable supporting flight. Its station is selected in assessed airspace below the meaningful hostile-interference threshold.

These four requests form the initial air-tasking backbone. OCA counter-air calculations remain pending its planned rewrite. DEAD is the next target-attack capability intended to build on that backbone.

**Destruction of enemy air defenses (DEAD)** is a discrete target-attack request to permanently render one known hostile SAM site combat ineffective. The SAM site is the operational target; when intelligence permits, the request identifies a target set of one or more known SAM components whose destruction is expected to achieve that site-level effect rather than reducing DEAD to an attack against one preselected component.

**DEAD target set** is the known, undamaged component inventory of the targeted SAM site that the assigned package can meaningfully attack with its planned ordnance. Permanently removing the site's shooter capability achieves the minimum DEAD effect, but surviving components remain authorized targets while the attacking flight retains a valid attack opportunity and suitable mission-useful ordnance.

**Minimum DEAD target intelligence** requires a persistent hostile SAM-site identity, a known location, and enough identified functional components to construct at least one engagement chain. The request targets the site and initializes its target set with every currently known component; a site report with no identifiable component-level engagement chain creates no DEAD request because v1 cannot select suitable stores, allocate attacks, or assess the minimum effect for an area-only target.

**DEAD target-set refresh** reconciles the component target set with current intelligence about the locked SAM site. Before takeoff, newly identified components may change the package plan and loadouts under normal pre-takeoff rematerialization rules. After takeoff, newly identified components may enter the target set only when they belong to the locked site and remain inside the fixed DEAD mission area; a newly identified functioning shooter chain becomes part of the minimum effect, while other components become cleanup opportunities. Removing destroyed, invalid, or no-longer-attackable components and adding same-site components is target refinement rather than airborne retasking.

**DEAD attack sequence** is the bounded series of sequential employment passes a DEAD flight may conduct during its authorized target-area opportunity. A flight carrying suitable anti-radiation ordnance may approach its assigned fire-control radar during outbound ingress; if that radar is silent, the approach may provoke a targetable emission. The flight is treated as having prepared its anti-radiation weapon during ingress, so a valid emission starts one short tactical reaction interval rather than a general-purpose ground-attack setup; defensive maneuvering during that interval does not itself cancel the release. This authorization applies only to the assigned site, while every other known SAM remains a routing constraint and normal avoidance resumes during recovery. Once the flight releases an anti-radiation missile, it offsets laterally and outward while the pending effect resolves instead of stopping at the release point. Beam, break, and drag maneuvers use defensive turn authority, and both beam and drag geometry can contribute to breaking radar-missile guidance while physical range-out remains decisive. After each resolved pass, the flight reassesses the surviving DEAD target set and may continue while suitable ordnance remains and package integrity, fuel, timing, and doctrine permit; once it begins egress, a later attack requires another package.

**DEAD attack flight** is a required combat flight assigned all or part of a DEAD target set with a target-driven mix of compatible anti-radiation and conventional air-to-ground ordnance. A DEAD package contains one or more DEAD attack flights and adapts their aircraft allocation, loadouts, and division of targets to the known site, available aircraft, expected opposition, and acceptable risk rather than requiring separate fixed anti-radar and cleanup roles.

**Initial DEAD objective** is the nearest observed-functional hostile airport to any friendly functional airport hosting ready aircraft capable of carrying meaningful air-to-ground ordnance against airport infrastructure, with stable airport and aircraft identity breaking equal distances. The objective is retained until the airport becomes nonfunctional, is captured, is no longer known, or a feasible attack corridor has been opened; only then does the alliance select another hostile airport.

**Initial DEAD corridor profile** is the provisional v1 representation of the future airport-strike flight whose access DEAD is intended to enable. It begins at the selected strike-capable friendly airport, uses the selected candidate aircraft type's nominal cruise altitude, and applies normal routefinding and maneuver clearance against known three-dimensional SAM envelopes for both ingress and recovery. It creates no airport-strike package and exists only to decide whether a known SAM blocks credible attack access.

**DEAD-supported air-access corridor** is the physical route and representative flight profile whose blockage justifies a DEAD request. The DEAD mission plan retains the locked SAM-site identity, its current component target set, and the physical corridor information needed for revalidation; it does not own or require the identity or type of the higher-level objective at the corridor destination. The initial planner may identify an airport while selecting and explaining v1 demand, but that airport identity remains planner rationale rather than part of the reusable DEAD mission contract.

**Objective-derived DEAD demand** is the future replacement for the airport-specific initial rule. A CAS, interdiction, strike, or other operational objective supplies the supported route, flight profile, acceptable risk, and required access; DEAD becomes one available destructive effect for removing air defenses that prevent that objective rather than permanently belonging to airport attack.

**Initial DEAD demand** selects known hostile SAM sites that block attack access to the initial DEAD objective and have no equivalent projected DEAD effect. The provisional planner sequences the nearest blocking site first so DEAD packages progressively open the corridor; future objective-driven air-campaign planning may replace this rule and select DEAD alongside other mission types as one tool for achieving higher-level operational goals.

**Initial DEAD request concurrency** permits at most one unresolved DEAD mission request per alliance. A request remains unresolved while it is actionable, materializing, linked to a package that has not physically ended, or still has released effects pending against its target. That request may materialize a package with multiple required DEAD attack flights and may execute multiple sequential employment passes; after its terminal outcome and effect resolution, the next blocker is considered only at the next global planning cadence.

**Unfulfilled DEAD demand** is the provisional v1 outcome when the highest-priority DEAD request cannot materialize an acceptably feasible and adequately protected package during its request window. The request ends unfulfilled and, if the blocker remains relevant, is reconsidered at the next global planning cadence; the initial planner does not fall through to a lower-priority corridor during the same cadence. Choosing a different achievable objective belongs to future goal-oriented air-campaign planning.

**Initial DEAD attack corridor** is an acceptable threat-safe ingress from the friendly combat-air base used to select the initial DEAD objective to that hostile airport, together with a threat-safe egress to recovery. Corridor feasibility uses the alliance's known SAM engagement envelopes and shared route-safety rules; when overlapping sites block every acceptable route, the nearest blocking site to the friendly origin is targeted first and the corridor is reassessed after its projected destruction.

**Provisional DEAD corridor completion** occurs when the planner can find an acceptable ingress-and-egress route to the selected hostile airport without another DEAD attack. Until airport-strike missions exist, the planner is satisfied that this corridor is open, creates no placeholder strike request, and continues searching for another relevant airport corridor to open. When no relevant blocked corridor remains, it creates no DEAD request. Future airport-strike planning replaces this provisional transition with exploitation of the opened corridor.

**DEAD corridor revalidation** reassesses every relevant airport corridor from the current known-threat picture on each global planning cadence. An open corridor is a reversible assessment rather than permanent campaign completion: movement, recovery, or discovery of a hostile SAM may close it and make DEAD demand relevant again, while disappearance or displacement of a blocker may open it without an attack.

**DEAD corridor priority** is recomputed during corridor revalidation using the same stable airport ordering as initial objective selection. A newly or repeatedly blocked higher-priority airport corridor takes precedence over an unopened lower-priority corridor, provided an equivalent DEAD effect is not already preparing, active, or pending against its blocker.

**DEAD cross-mission priority** uses the existing doctrine-weighted air-mission priority mechanism, so generating the alliance's highest-priority DEAD candidate does not make it unconditionally more important than BARCAP, OCA, or support demand. Initial DEAD objective and blocker ordering select among DEAD opportunities; doctrine and the shared request score allocate scarce aircraft across mission families. Future goal-oriented air-campaign planning may replace this provisional comparison by evaluating which available mission effects best advance an operational objective.

**Initial DEAD urgency** is the normalized functional level of the hostile airport whose attack corridor the selected SAM blocks. SAM distance, envelope, and lethality determine corridor blockage, blocker ordering, route risk, and package feasibility rather than adding a second strategic-urgency bonus; the shared priority mechanism combines airport urgency with the alliance's DEAD doctrine weight and its normal cross-mission factors.

**DEAD planning explanation** uses HZPL's shared rationale, priority-component, package-lifecycle, and ordnance-employment records rather than a separate DEAD log. The request explains the selected objective airport, friendly origin, representative aircraft and profile, blocked route, chosen SAM site, known target set, urgency inputs, and equivalent-effect check. Package materialization explains selected flights and loadouts, minimum-effect and self-defense coverage, reserve allocation, sequencing, and infeasibility. Execution records each pass's assigned components and stores, resolved effects, target-set changes, attack-window state, and final completion, cancellation, abort, or failure reason.

**DEAD v1 acceptance** requires the planner and executor to demonstrate: no request for an open corridor; stable selection of the highest-priority relevant airport and nearest blocking SAM; at most one unresolved alliance request; no package without minimum intelligence, sufficient DEAD effect, and acceptable fighter protection; threat-driven fighter escort when credible opposition and aircraft are available; conservative deferral when required escort strength is unavailable; adaptive multi-flight and multi-pass employment with deconflicted pending effects; standoff-first attacks followed by situation-permitting conventional cleanup; bounded same-site mobile tracking inside the fixed forty-kilometre area; correct invalidation and retry lifecycle; success only after every functional shooter chain is permanently removed; persistent partial damage otherwise; renewed demand when a corridor closes again; and an explainable record for every material planning and execution decision.

**Attached fighter escort** is a mission-neutral required owning flight within the protected package rather than a separate mission request, an offensive-counter-air sweep, or a reusable supporting service. It names the primary flights it protects and uses shared fighter selection, rendezvous, forward-screen routing, threat authorization, close-cover, safe-release, loadout, and package-integrity behavior. Each escorted mission family remains responsible for deciding whether an escort is needed, how much fighter power is required, and when its mission-specific conditions change escort coverage. For DEAD planning, remembered hostile combat power and air activity along and immediately beside the supported corridor establish expected interceptor power. The planner discounts the DEAD element's air-combat contribution because it must preserve route and attack timing, then selects the smallest available fighter allocation that approaches doctrine's desired air-combat advantage. Tolerable opposition may fall back to organic self-protection when no escort is available; opposition beyond the doctrine-weighted unescorted limit makes the escort mandatory and leaves the request unfulfilled when sufficient escort power cannot be assigned.

**BVR employment** separates shot assessment from maneuver selection. A radar missile has two distinct reaches: a kinematic reach set by shooter energy, which bounds how far a released weapon may chase from its launch point, and a launch envelope that additionally credits or debits the target's radial motion and decides whether a shot may be taken at all. A released weapon is never given less budget than the range it was legitimately fired from, so a target defeats a maximum-range shot by opening rather than by the shot being unreachable at release. Each candidate weapon reports whether it is ready, too far, too close, outside launch boresight, below doctrine quality, or unavailable. A valid shot is employed from the current position; preferred range is only an intercept objective while outside the launch envelope. Inside the envelope, fighters point or continue closing to improve a rejected shot rather than opening range to recreate a preferred distance. Deliberate range opening remains an explicit crank, beam, drag, or extension. Launch quality remains aspect-sensitive: infrared and gun shots favor the target's stern, while radar shots are degraded toward the target's beam, where closure carries the least information. A flight holding only its doctrine weapon reserve stops engaging, but a DEAD attack flight in that state resumes its assigned route instead of extending off it.

An attached DEAD escort shares the package rendezvous, protects the named DEAD attack flights through ingress, the attack window, and initial egress, and initially screens from outside the assigned SAM site's envelope. Only DEAD attack flights receive target-envelope penetration authority while the site retains a functional shooter chain. Once current intelligence confirms that the package has permanently broken that chain, the target no longer contributes a threat envelope and the escort collapses to close cover over DEAD aircraft that remain in the target area destroying equipment; every other known SAM remains an avoidance constraint. The escort may intercept, press, launch, support weapons, defend, and merge against a tracked hostile whose course and timing make it a credible threat to a protected flight. A known engagement, weapon preparation, or released weapon against a protected flight preempts retained predicted threats; target retention is only a tie-breaker among otherwise comparable threats. A hostile known to be committed against an unrelated flight is not a speculative escort target. The escort does not adopt general OCA hunting authority: a hostile that turns away or cannot reach the protected element before the tactical window closes is not an authorized target. Escort duty is achieved at a materialized safe-release point toward friendly territory, bounded by the request window. Because an attached escort participates in the coordinated rendezvous and protection plan, it is a required package flight; loss, abort, or failure before duty completion invokes normal package-integrity rules.

**Self-escorting DEAD package** is the fallback DEAD policy when corridor fighter opposition is negligible or tolerable and no escort is assigned. Required DEAD attack flights carry situation-appropriate air-to-air self-defense ordnance and defend themselves without adopting an offensive counter-air hunting task: a proactive defensive shot requires a current friendly track and valid firing geometry from the assigned DEAD route, does not authorize an intercept or press merely to manufacture that geometry, holds the flight at its present route position during weapon preparation, and is followed only by the support maneuver required until the weapon becomes autonomous before the DEAD route resumes. An incoming weapon still authorizes immediate defensive maneuvering, while an unavoidable close-range threat may force disengagement or recovery.

**DEAD loadout feasibility** requires every proposed package to carry enough target-suitable air-to-ground ordnance for the minimum DEAD effect. Without an attached escort, the DEAD attack element must also satisfy its situation-dependent organic self-defense allowance. With an attached escort, DEAD effect and useful cleanup ordnance receive first claim on attack-aircraft capacity; remaining capacity may carry organic self-defense weapons, but their absence does not invalidate an otherwise protected attack aircraft. Escort aircraft carry normal mission-useful air-to-air loadouts. Aircraft and flights are added adaptively until the package effect and applicable protection need are met; unrelated stores and aircraft are not added merely to fill capacity.

**DEAD component priority** ranks viable components by the operational value of destroying them rather than by raw component count: first collapse the site's immediate engagement chain, then remove remaining shooter capability, wider IADS sensor or command contribution, and finally other useful site equipment. Within the same operational tier, the attacking flight prefers the component for which its suitable carried ordnance has the best expected effect and recalculates the ordering after each resolved employment pass.

**DEAD component allocation** keeps the package's hostile SAM site target locked after activation while allowing its flights to reassign individual components within that site as execution unfolds. Each attack window rolls an opportunity from authorized surviving components, prioritizing exposed weapon-quality fire-control radars while they remain functional. A pass may therefore attack one component or several exposed components with several stores, and an area-capable store may affect additional compatible components in the same opportunity. Flights credit components and weapon quantities already covered by preparing, released, or unresolved employment effects, permit deliberate redundancy only when needed for the planned effect, and redirect later passes when another attack destroys or fails to destroy an allocated component.

**DEAD minimum effect** is permanent component damage that removes every functional engagement chain through which the targeted SAM site could fire surface-to-air ordnance. Temporary suppression, radar shutdown, or ammunition depletion alone does not achieve the minimum effect.

**DEAD result** records how much of the target site was permanently destroyed. A package succeeds when it achieves the DEAD minimum effect and may continue its bounded attack sequence for a stronger result; destruction of every known viable component is total site destruction, while damage that leaves a functional engagement chain is an unsuccessful partial effect that remains part of campaign state.

**DEAD attack window** is the maximum period during which a DEAD package may conduct its sequential employment passes after the first required DEAD flight enters the target area. The initial doctrine ceiling is fifteen campaign minutes and may end earlier for fuel, risk, package integrity, lack of suitable targets, or mission-useful ordnance exhaustion; when it ends, surviving flights egress and any achieved component damage remains.

**DEAD attack sequencing** uses suitable standoff ordnance first against the highest-priority engagement-chain components and reassesses resolved effects before accepting closer exposure. Conventional close-in cleanup attacks occur only when the remaining target-site threat and surrounding known air-defense risk are acceptable under doctrine; carrying cleanup ordnance does not by itself authorize overflight of an operational SAM site.

**DEAD local pass pattern** is the provisional v1 cleanup geometry used after the target site's functional fire-control radar chain has been permanently removed. The flight approaches from its friendly-side standoff position, enters a local attack line five nautical miles before the site, passes through or near the component aim point, and reaches its pass endpoint five nautical miles beyond the site before turning for another reassessed pass. The fixed forty-kilometre DEAD mission area remains the mobile-target pursuit leash and does not determine attack-pass length.

**DEAD staggered entry** coordinates multiple required DEAD attack flights without assigning fixed opening or cleanup roles. Flights with suitable standoff attacks act first while flights requiring closer geometry hold at a safe pre-attack position; resolved effects and current loadouts determine which flights reattack, enter for cleanup, continue holding, or egress during the remaining DEAD attack window.

**DEAD target-envelope penetration** is the doctrine-gated exception that permits a DEAD attack flight to enter the targeted SAM site's surviving engagement envelope during its authorized attack sequence. The package prefers available standoff attacks and accepts closer exposure only when its strength, survivability, suitable remaining ordnance, expected exposure, target capability, and alliance risk tolerance indicate that the minimum effect remains achievable; the exception never authorizes ignoring other known SAM threats.

**Mobile DEAD target tracking** locks a DEAD package to one persistent mobile SAM site identity while allowing its attack aim point to follow the latest known site position within the originally authorized mission area. Before takeoff the package may update and revalidate its route; after takeoff it does not pursue the site beyond that area or continue when the site becomes unlocated or newly infeasible, and any damage already inflicted remains with the moving site.

**DEAD mission area** is a circular real-world distance boundary centered on the target site's latest known position when the package commits. Its initial horizontal radius is forty kilometres rather than a number of tiles; tile coordinates may locate campaign entities but do not define aircraft operational geometry. The area may be recentered while the package is still pre-takeoff, becomes fixed after takeoff, and bounds mobile-target pursuit during the DEAD attack window. Aircraft altitude remains expressed in feet.

**DEAD target invalidation** occurs when the assigned site is destroyed, becomes non-hostile or no longer known, or ceases to block the initial DEAD attack corridor. A committed package cancels before takeoff; an active package cancels any unreleased employment, lets released effects resolve, and aborts without airborne retasking unless its own effects had already achieved the DEAD minimum effect, in which case it completes and egresses.

**DEAD retry** follows the discrete mission-request lifecycle. Cancellation before takeoff may receive a replacement package in the same planning cycle, while an active failure or abort is reconsidered at the next global air-planning cadence using the site's surviving components, current position, and current threat contribution; released unresolved effects continue to prevent duplicate DEAD commitment.

**DEAD component store allocation** plans one primary suitable attack and, when capacity permits, one reserve suitable store for every component required by the DEAD minimum effect. Primary effects normally resolve before reserves are used in later passes, while deliberate multi-store attacks remain valid when timing, target value, or expected hit probability justifies redundancy; optional cleanup components receive single planned attacks after minimum-effect and self-defense needs are covered.

**DEAD mission-useful ordnance** is carried air-to-ground ordnance that can still produce a meaningful effect against at least one surviving component in the flight's current DEAD target set. Exhausting it before the minimum effect causes an unsuccessful recovery, while exhausting it after the minimum effect ends cleanup and permits successful egress.

_Avoid_: using DEAD for temporary suppression or localized escort protection; SEAD escort is a separate future supporting mission.
_Avoid_: fixed DEAD aircraft-role labels or a mandatory anti-radar-flight plus conventional-attack-flight pairing.
_Avoid_: treating an offensive counter-air sweep as if it were a dedicated fighter escort.

### Air mission area

An **air mission area** is the dynamic geographic area affected by an area-based mission request, defined in v1 by a center campaign tile and a radius. Offensive sweeps operate within an air mission area. BARCAP retains a bounding mission area for shared routing and compatibility, but its planning and engagement truth is the materialized barrier and aircraft-specific covered segments.

Mission areas are derived from current campaign needs rather than authored as fixed zones in a campaign template. The actual routes, patrol stations, and support orbits used by flights are derived from the mission area rather than being the mission area itself.

_Avoid_: pre-authoring fixed mission areas, or treating a single target point, a flight route, or a support orbit as the whole area whose air effect was requested.

### Coverage window

A **coverage window** is the bounded interval during which an area-based mission request asks the alliance to maintain an air effect. An unrefueled BARCAP window is fulfilled by sequential fuel-bounded rotations, with replacement station time planned to overlap the preceding patrol by ten minutes when preparation time permits. A tanker-supported BARCAP may cover a longer window while continuous reserved tanker coverage exists. AWACS and tanker requests use their own fuel-bounded rotations to fill the requested window.

Coverage planning uses a rolling handoff across air-planning cadences. A window may extend beyond the next cadence boundary long enough for the following planning cycle to prepare, launch, and position replacement coverage, preventing a gap while the new alliance air plan is being fulfilled.

The following planning cycle reassesses the need. It may cancel no-longer-needed committed rotations before takeoff, but an active sortie continues its locked mission through its planned coverage.

For BARCAP, projected coverage from committed and active flights is credited tile by tile wherever their assigned barrier segments overlap the current requested barrier with a compatible threat-facing direction, including across planning cycles. Geometric overlap from the opposite approach does not count. This avoids duplicating patrols and lets one correctly oriented front barrier screen downstream airports. A flight that has begun returning, landing, or has ended no longer contributes projected BARCAP coverage. An airborne flight also loses its original coverage credit when weapon expenditure or aircraft loss removes the preferred air-to-air range used to plan that segment, allowing a replacement to be scheduled while the depleted patrol recovers or continues limited defense. If cancellation, abort, capability loss, or early recovery opens a spatial or temporal gap after a sustained request was marked fulfilled, that request becomes actionable again. Completion of one BARCAP, AWACS, or tanker rotation never completes the whole sustained request. Existing active BARCAP coverage still finishes on its originally assigned segment when the new plan moves elsewhere.

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

Discrete combat requests require their full desired force strength before a package may commit. OCA therefore does not launch an understrength sweep. Spatial BARCAP is the deliberate exception: the planner commits one-aircraft station packages and orders spatially uncovered barriers ahead of defensive-capacity reinforcement, then repeatedly fills the largest remaining gap. If only two aircraft are available for two barriers, each barrier receives one before either receives a second. When the final spatial gap can be closed without leaving another barrier open, that flight may take doctrine's preferred station aircraft count; subsequent packages reinforce covered tiles whose projected aircraft count remains below that preference. A future committed BARCAP counts toward the spatial or strength gap it is already scheduled to close, preventing the five-minute fulfillment loop from creating duplicate packages while that flight is still preparing or transiting. A gap at or after the committed patrol's effect end remains actionable for handoff planning. This favors a continuous defensive screen without treating a singleton patrol as equal to a mutually supporting station.

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

**Support capacity** is the bounded number of friendly aircraft that an AWACS or tanker flight can support within its coverage area and time window. In v1 it is measured in abstract **support slots**: each support-capable aircraft type provides a configured number of simultaneous slots, and each supported combat aircraft reserves one compatible slot during the relevant overlap. One tanker slot represents continuous access for its receiver during that reservation, not one fuel-transfer event. An airborne support flight's available capacity falls with the number of surviving assigned support aircraft.

A sustained support request asks for enough projected capacity to meet the area's demand. Multiple packages may share one supporting flight only while their overlapping reservations remain within its available slots; additional packages and flights are created only when existing capacity is insufficient.

Future AWACS control-channel rules and physical tanker offload rules may specialize capacity without changing the shared support-capacity concept.

**Airborne-C2 effect** — in v1, AWACS coverage has no numerical effect on air-combat power, mission risk, or air-planning knowledge because airborne C2 does not yet contribute observations to the alliance IADS. Baseline demand authored through the campaign template or alliance doctrine still causes the planner to create airborne-C2 requests, packages, flights, coverage windows, and support-slot reservations so the support-tasking backbone exists.

Observed and forecast mission demand do not escalate AWACS coverage until its operational effect exists. Future AWACS behavior is intended to contribute through alliance IADS and intelligence rules, where its help may not reduce cleanly to one numerical benefit. Do not add a placeholder combat multiplier that would compete with that future model.

**Aerial-refueling effect** — a refuel-capable executing flight with an active package reservation may restore its aggregate fuel fraction to full whenever it reaches doctrine joker fuel inside the reserved tanker's coverage. It may refuel repeatedly while continuous reserved coverage exists. If no reserved tanker is active when the receiver reaches joker, the receiver follows ordinary recovery behavior. A BARCAP planner extends a patrol beyond its unrefueled fuel limit only through the continuous tanker coverage it can reserve; sequential tanker rotations may form that coverage.

Tanker propulsion fuel remains separate from abstract service capacity: tanker sorties are themselves fuel-bounded, while support slots constrain simultaneous receivers. V1 does not track transferable fuel mass or reduce tanker propulsion fuel when a receiver refuels.

_Avoid_: placeholder range multipliers, transfer rates, boom queues, partial offloads, or a fixed per-sortie refueling-count limit.

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

A SAM launcher component is a SAM component definition that contributes shooter capability for compatible surface-to-air ordnance types. The ordnance type owns base interceptor behavior such as its employment envelope, guidance mode, hit probability, effect speed, and effect power; the launcher owns ready rounds, reload behavior, salvo or launch rate behavior, channels, and launcher-specific modifiers. V1 configures one surface-to-air ordnance type and one ammo pool per launcher while preserving the compatibility boundary for future mixed-interceptor loads.

Launcher ammo is tracked at the launcher component level as abstract ready and remaining ordnance counts rather than individual missile objects. Reload delay, reload rate, and simultaneous shot or channel limits may be modeled as component capability when needed.

SAM-launched missiles are surface-to-air ordnance types, not aircraft-compatible stores. Their shared ordnance identity allows SAM launches and aircraft employment to create the same kind of pending ordnance effect without sharing loadout or launcher-ammo ownership.

_Avoid_: making surface-to-air ordnance compatible with aircraft loadouts merely because both use the shared ordnance catalog.

### Campaign SAM site template allowance

Campaign SAM site template allowance is the subset of Module SAM site templates permitted in one campaign story. In v1, SAM site template allowance should follow the ordnance allowance pattern and be scoped per alliance unless a future rule needs country-specific access within the same alliance.

Template allowance controls which named SAM systems an alliance can field during ordinary campaign authoring. A template must be allowed for the alliance, and its required SAM component definitions must also be allowed, before that template can be instantiated without overrides.

### Radar definition

A radar definition is a reusable Module catalog definition for a radar capability, such as a Fan Song fire-control radar or an early warning radar. It describes the radar capability once so it can be used by SAM components, static SAM buildings, standalone radar buildings, or future sensor hosts without duplicating radar behavior.

Radar definitions are authored capabilities, not runtime placed assets. Runtime hosts determine where the radar capability exists, who controls it, and whether the hosted radar component or building is damaged, suppressed, or emitting. A radar definition also authors antenna height above its host and a fusion correlation group. The host supplies ground elevation; v1 hosts are at sea level, while the boundary permits later elevated radar stations without changing radar definitions.

A radar fusion correlation group identifies radar definitions that contribute substantially similar observations to the shared air picture. Successive quality-cap contributions from the same group receive diminishing weight even when they come from different radar definitions. Radars from different groups retain their normal fusion contribution because their observations are treated as complementary at the campaign abstraction.

### Static SAM site

A static SAM site is a SAM site whose host is a static SAM building on a tile. It behaves like other placed assets for map placement and damage identity, while contributing air-defense roles through the shared SAM site model.

A static SAM building groups the site's SAM components under one placed site identity. Its components can be damaged without every launcher, command post, or support asset becoming a separate building.

Radars are the exception when they need to exist outside a SAM site. A standalone radar building is its own placed asset because radar sites may contribute detection, cueing, command, or network roles without being SAM launch sites.

Standalone radar buildings are not SAM sites when they cannot launch missiles. They may still reuse radar definitions, SAM component definitions where appropriate, target profiles, damage state, suppression concepts, and IADS network contribution rules.

In the first model, hostile tile capture disables a static SAM site's SAM behavior or a standalone radar building's radar behavior rather than transferring it into the captor's IADS. The placed asset may remain, but its components do not become operational for the new controller automatically. Recapturing the tile does not reactivate the site; reactivation requires a future explicit mechanic.

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

Air-defense sites contribute to the alliance IADS for their effective site alliance. Mobile SAM sites use their assigned alliance; static air-defense sites derive their effective alliance from the controller of the tile containing their host building and stop contributing when disabled by hostile tile capture.

In v1, the alliance IADS owns current tracks and engagement assignments. IADS commander refresh names the decision pass that updates those assignments, even if a future IADS commander becomes a separate durable entity.

In v1, an alliance IADS builds tracks for active airborne hostile flights only. Friendly flights remain known through air operations rather than as IADS current tracks.

Alliance IADS tracks are persistent campaign state across turns. They should be representable as campaign state even before save/load behavior exists.

_Avoid_: treating future network topology as the owner of the v1 shared air picture.

### IADS current track

An IADS current track is an aggregate hostile-flight contact that a site or alliance IADS is currently aware of through direct detection or shared cueing. Current track awareness is not authorization to fire.

Remote cueing may add current track awareness for another site, but remote cueing alone is not enough to authorize a SAM launch.

_Avoid_: creating one co-located track for every member aircraft or treating a package as the tracked object.

An IADS current track records the flight's last known airspace position, a bounded recent history of observed position and heading, estimated aircraft count, and estimated combat power. The motion history supports course-persistence and bounded-patrol assessment without revealing the hostile flight's true route or mission. At `0.50` track quality, IADS records the identified aircraft type and retains that identification for the remaining lifetime of the track even if quality later decays. Below that threshold, the true aircraft type is not alliance knowledge. A track may reference the true flight entity for simulation bookkeeping, duplicate prevention, and resolution, but that reference is not itself alliance knowledge about squadron, mission, or package.

### Stale IADS track

A stale IADS track is an IADS current track that persists after the tracked flight is no longer currently observed. Stale tracks record that they are stale and remain in the alliance IADS air picture only until their configured expiry threshold is reached.

Stale tracks represent lost sensor contact with a flight that may still be airborne. Their IADS track quality decays while stale, and tracks are removed when their true flight is no longer active and airborne, such as after landing, destruction, or leaving the battlespace.

If a stale track is reacquired before expiry, it keeps the same track identity, clears stale state, refreshes its last known position, and continues building from its decayed quality.

### IADS track quality

IADS track quality is one continuous 0.0 to 1.0 estimate of the alliance's combined certainty about a hostile flight's current position, altitude, heading, and speed. Quality `1.0` means the track contains complete current kinematic information at the campaign abstraction; lower quality means its position and predicted motion are less dependable. HZPL does not persist separate position-, altitude-, velocity-, or confidence-quality fields.

Each observing radar contributes an individual situation-dependent quality cap and build rate. Radar visibility is limited independently by its detectability-adjusted equipment range and a standard refracted-Earth radar horizon calculated from radar and target altitude. Terrain masking is deferred. Target radar detectability scales equipment range by its fourth root and is not applied again to cap or build rate.

Radar caps fuse by combining their remaining uncertainty, so additional radars can raise the achievable shared quality as well as build it faster. Successive cap contributions within one fusion correlation group receive a `0.65` exponential multiplier; radars in different groups retain their normal fusion contribution. Build-rate contributions retain their own global diminishing returns. Quality moves toward the currently fused cap rather than snapping down to it, loses quality when the observed flight changes heading, speed, or altitude, and decays quickly when observation is lost.

A flight contact must reach at least 0.10 IADS track quality before it becomes an IADS current track. Sub-threshold tentative acquisitions retain their accumulated quality internally only while continuously observed, allowing successive radar updates to establish a contact without exposing it to IADS consumers. The 0.10 threshold creates current tracks; established stale tracks below that quality persist until their stale expiry threshold is reached.

Damaged, destroyed, disabled, suppressed, or silent radar capability does not contribute to IADS track quality. Radar emission is binary. A radar definition states whether it searches while unassigned: surveillance/search radars normally emit when available, while fire-control radars normally remain silent until selected for an engagement assignment and continue emitting while they support an unresolved SAM salvo. A fire-control radar selected for an assignment is the same component used for launch and guidance.

An intact radar with an inbound anti-radiation effect may hold its emission until that effect resolves when it is not supporting a SAM already in flight. A radar that is supporting a SAM continues emitting until its guidance obligation ends, then may shut down for the remaining anti-radiation threat time. More detailed EMCON doctrine, detection of inbound missiles, and graded emission modes remain deferred.

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

A weapon-quality track is an IADS current track whose aggregate quality meets the assigned SAM system's authored minimum launch quality. Remote observations may raise the shared track to weapon quality, but a radar-guided SAM launch also requires a live local fire-control-capable radar or equivalent organic shooter sensor inside its applicable envelope.

Search-only and passive-only contributors may improve shared awareness and cueing but cannot replace the shooter's required local fire-control and guidance role.

### Weapon-quality for shot

Weapon-quality for shot is whether a specific SAM site has both a shared track meeting its launcher's authored minimum quality and an operational local fire-control source able to acquire and support the target. The local source is an authorization and guidance-continuity requirement; it does not cut the fused shared quality back to that radar's individual contribution cap.

A weapon-quality track on the air picture does not by itself mean every networked launcher has weapon-quality for shot; remote engagement still requires doctrine, network support, and a valid fire-control source on the network.

Weapon-quality for shot is one input to SAM launch authorization, not the whole decision. Full launch eligibility (ammo, reload, envelope, channels, role status, and other gates) belongs in a separate launch-authorization step that composes multiple checks; it should not be folded into the weapon-quality for shot module.

For a surface-to-air missile released with continuous required guidance and no target maneuver or active countermeasure response, the snapshotted shared track quality is its ideal hit probability: quality `1.0` produces a certain hit, while quality `0.5` produces a fifty-percent ideal hit chance. Post-launch guidance loss and actual target defense modify that probability. A successful hit remains distinct from destruction; ordnance lethality and aircraft survivability determine whether the hit destroys or mission-kills the selected aircraft.

### SAM launch execution

SAM launch execution is the resolution of assigned SAM engagements after sortie movement has updated target positions. It validates whether assigned sites can still fire and resolves launch outcomes without choosing engagements itself.

A SAM site's launcher components represent its ready rails or vehicles, not independent authorization to fire every ready missile. Each launcher definition authors a preferred engagement salvo size, while the selected fire-control radar authors its maximum simultaneously supported missiles and maximum concurrent target engagements. Launch execution selects only enough eligible launcher components to fill the preferred salvo without exceeding the radar's remaining capacity.

An operational fire-control radar does not accept another salvo against a flight while missiles it already launched at that flight remain unresolved. The IADS also reserves a flight with an in-progress SAM engagement so another site is not assigned to duplicate that engagement. Once the existing effects resolve, surviving targets may be assigned for a subsequent engagement.

SAM launch execution happens once per simulation tick after all live sorties have moved for that tick, so all launch decisions use the same updated air picture.

SAM launches do not use the aircraft employment-pass preparation phase. IADS assignment and launch authorization provide the SAM preparation boundary; an authorized launcher spends its surface-to-air ordnance during SAM launch execution and creates a pending ordnance effect. Launcher reload, launch-rate, and channel limits govern later launches.

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

### Ordnance launch diagnostics

Release records include per-store launch diagnostics so the debug UI can show which aircraft or SAM launcher fired which ordnance at which target aircraft during the last completed turn. V1 pending effects snapshot the selected target aircraft at release for deterministic resolution; if that aircraft is no longer a valid survivor when the effect resolves, the store becomes ineffective.
