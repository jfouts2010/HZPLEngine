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

### Campaign template

What an author creates in the campaign editor after choosing a Module. A campaign template defines the starting premise for play under that Module. The Module is fixed for the lifetime of an edit session; it cannot be changed while editing an open template.

**Static premise** — fixed across every play from this template: map layout and extent, **template tile** geography (terrain, rivers, land/water), sides, Module-scoped unit availability, airport and static SAM site definitions, and other geography that should not change because the story shifted.

**Starting conditions** — fixed every time a player starts from this template: **starting tile** data (initial tile control and per-tile infrastructure as authored at day zero), initial unit locations and strengths, starting air wings and squadrons, **campaign start calendar** (`CampaignStartTime` — in-world date and time at turn zero, authored on the template not the Module), and other day-zero force dispositions.

Infrastructure, airports, and SAM sites are **undamaged at day zero** in v1. Authoring initial damage on the template may be added later.

Once play begins, the starting order of battle is historical context only. Runtime simulation cares about current unit locations, strength, destruction, tile control, airport operational status, and the rest of the live war state — not re-reading the template’s starting conditions each tick.

### Infrastructure property

A leveled tile asset (roads, supply lines, ports, factories, and similar) or airport runtime state tracked with two persisted integers:

- **Build level** — authored capacity at day zero (campaign editor / airport editor) and the current built capacity during play. Build level never decreases; it may increase during play when construction is implemented. Capped at 0–10.
- **Damage** — runtime-only wear from bombing and similar effects. Damage is per asset (a port can be damaged while a fort on the same tile is untouched). Damage is capped at build level and never goes negative.
- **Functional level** — derived, not persisted: `max(0, buildLevel - damage)`. Used for supply throughput, movement, combat modifiers, production, and strike targeting health.

`cityType` and `isSupplyHub` are not leveled properties in v1. Supply hubs are not damaged directly; supply-line damage is the mechanism for degrading hub effectiveness.

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

### Ground operational cadence

How often expensive ground logic runs on the simulation tick clock: retreat decisions, division movement, and tile/objective selection.

Ground operational cadence is template-configurable within engine bounds (**one to six hours** of in-game time; **default six hours**). Many simulation ticks pass between ground operational updates.

Between ground operational updates, frontline tiles may still exchange abstract combat damage when opposing forces are in contact, without full movement or strategic replanning every tick.

### Ground tactical combat

Lightweight ground resolution that may run on most simulation ticks when opposing forces are engaged — for example, attrition or control pressure while two tiles are fighting. This keeps the frontline alive between operational movement decisions without running full division AI every tick.

### Tick resolution order

Within each simulation tick, resolution order should stay deterministic. Air picture, air execution, and air-to-ground effects participate every tick. Ground tactical combat runs when contact conditions apply. Ground operational movement and planning run only on ticks that fall on a ground operational cadence boundary.

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
