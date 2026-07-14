# HZPL Engine — Product Vision

This document describes **what the project is aiming to accomplish** and **what is in scope for each phase**. It is the north star for design and implementation decisions.

For domain terms, see [`CONTEXT.md`](CONTEXT.md). For how the Unity codebase works today, see [`Assets/docs/hzpl-project-context.md`](Assets/docs/hzpl-project-context.md). For places where code lags behind this vision, see [`docs/implementation-gaps.md`](docs/implementation-gaps.md).

## Read order for LLMs and contributors

1. **This file** — intent, phases, and architectural goals
2. **`CONTEXT.md`** — canonical vocabulary
3. **`docs/adr/`** — irreversible trade-offs when they exist

---

## What we are building

HZPL Engine is a **dynamic campaign** for modern warfare (roughly 1950 through the present) with a primary focus on **complex air warfare**.

The long-term product loop:

1. A living, unscripted war runs on an operational hex map — the **dynamic campaign** archetype.
2. The player may optionally enter individual missions in a **third-party flight simulator** (for example Digital Combat Simulator).
3. Outcomes in the flight sim feed back into the campaign: losses, damage, and (eventually) mission performance affecting the wider war.

The project is **inspired by** the FreeFalcon lineage (see local reference at `freefalcon-central-ref` if available) — a similar idea from an older codebase — but **is not bound** to FreeFalcon’s architecture or implementation. The goal is a modern, high-quality application that learns from that inspiration without copying it.

---

## Core architectural split

### Core engine (rules)

One rule system for all Modules:

- Air operations (tasking, packages, sorties, IADS, SEAD/DEAD, SAM execution)
- Ground operations at operational fidelity
- Supply, intelligence, turn/time progression
- AI-driven faction behavior

Rules do **not** change when the player switches Module. A Korean War SAM and a modern SAM use the same engagement logic; only catalog stats and third-party mappings differ.

### Module (content + integration)

A **Module** is chosen before creating or loading a **campaign template**. It defines:

- **Module content catalog** — maps, countries, and unit definitions available for that third-party simulator
- **Sim adapter** — third-party ID mappings, scenario export, and (eventually) mission result import

Module unit definitions are **human-authored** representations of how a unit behaves in our simulation. Stats are researched and entered by module authors; they are **not** pulled from the third-party game at runtime. A **third-party ID** on each mappable entity is used only when exporting a playable scenario.

If module authoring is wrong, the same named unit can behave differently in our war than in the flight sim. That is an accepted trade-off.

**Standalone Module** is a first-class Module for development: same catalog and adapter shape as future DCS/MSFS Modules, but the sim adapter is a no-op. Current test content (`TestModule`) is Standalone in domain terms.

Organizational constructs (wings, squadrons, battalions, divisions) do **not** need third-party mappings. **Mappable entities** (see `CONTEXT.md`) do — including aircraft types, airports, SAM systems, ground vehicles, infrastructure, map/theater binding, and ordnance when the sim requires explicit IDs.

**Airspace zones** (CAP, ROZ, etc.) are **live**: generated at export from runtime state, not prebuilt in the module catalog.

---

## Campaign artifacts

| Artifact | Role |
|----------|------|
| **Campaign template** | Authored in the campaign editor under a Module. Static premise (map, terrain, sides, infrastructure) plus starting conditions (initial units, wings, control). |
| **Runtime campaign state** | The live war after play begins. Current positions, losses, control, air ops, supply — not the day-zero order of battle. |

Geography is **immutable** during play (mountains do not become plains). Control, damage, force disposition, and infrastructure status **evolve**.

---

## Simulation model

### Dynamic campaign archetype

The war progresses independently. Units, logistics, and infrastructure are tracked. Victories and failures reshape the conflict — destroyed bridges, depleted supply, lost airbases, and shifted frontlines change what happens next.

### Air-primary, ground-supporting

- **Air** is the detailed simulation: sorties, packages, IADS, track quality, SAM execution.
- **Ground** is operational and abstract: hex movement, combat, territory, supply at lower fidelity. Ground exists mainly to create air targets, frontlines, and consequences (supply, airbase capture). Deepening ground simulation is far-future scope.

Air and ground must interact both ways.

### Single simulation clock (target)

One high-fidelity **simulation tick** drives the war:

| Setting | Range | Default |
|---------|-------|---------|
| Simulation tick | 1–10 minutes | 5 minutes |
| Ground operational cadence | 1–6 hours | 6 hours |
| Air planning cadence | 1–6 hours (aligned with ground operational) | 6 hours |

Template-configurable within those bounds.

- **Every tick:** air execution; ground tactical combat when forces are in contact; tick-level air adjustments.
- **Operational cadence (~6 h):** expensive ground work (movement, retreat, objective selection); full air planning.
- **Event-driven:** significant events (major airbase loss, collapsed SAM belt) may trigger air replan before the next cadence boundary.

The current **turns + slices** implementation is a stepping stone toward this model, not the long-term shape. See [`docs/implementation-gaps.md`](docs/implementation-gaps.md).

Resolution order within a tick should stay **deterministic** (air effects before ground movement when causality requires it).

---

## Player role by phase

| Phase | Player role |
|-------|-------------|
| **Now (Standalone testing)** | **Autonomous observer** — all factions AI-controlled, no human input on the war. Validate realistic behavior. |
| **Later** | Human command, pilot intervention via exported sorties, and other modes layered on the same core engine. |

---

## Third-party flight sim (future)

Not in scope for current autonomous testing. Documented here so design stays compatible.

### Exported scenario

A third-party flight-simulator mission generated from runtime campaign state when a player flies instead of watching autonomous simulation.

Export scope is the **active battlespace** — the **full campaign map**: every aircraft currently flying and every ground unit on the map, with exactly **one** player aircraft and the rest AI in the third-party sim.

While the player flies, the **campaign clock pauses** until mission results are imported.

The sim adapter maps **mappable entities** to third-party IDs and derives **live airspace zones** from runtime state at export time.

Not in scope for current autonomous testing. Standalone Module uses a no-op adapter.

### Mission result import

After the player flies an **exported scenario**:

1. **Literal outcomes** — losses, damage to mapped targets, ordnance expended, and other reported facts applied to runtime state.
2. **Mission performance evaluation** — graded success/partial/failure driving player-facing decisions or effects beyond raw damage (tasking priority, restrictions, rewards). **Specific rules are not designed yet** and must not block autonomous simulation work.

---

## Current development focus

**In scope now:**

- Realistic autonomous air-and-ground war on the Standalone Module
- Campaign template authoring and campaign save/load
- Air-primary simulation depth (IADS, tasking, execution, effects)
- Initial air-tasking backbone for barrier combat air patrols, offensive counter-air sweeps, airborne C2, and aerial refueling; DEAD follows as the first target-attack capability
- Operational ground layer supporting the air war
- Alliance-scoped air-planning intelligence, using perfect campaign knowledge in v1 behind a boundary intended for later fog-of-war and intelligence rules

**Out of scope now (design for later, do not block on):**

- Scenario export to DCS or other sims
- Mission result import and performance rewards
- Detailed air-mission route construction and mission-conduct resolution beyond the initial tasking foundation
- Deep ground warfare beyond operational abstraction
- Player command of a faction or mandatory human input each tick

---

## What “success” looks like

A campaign template loaded under Standalone can run for many ticks with **no player input**, producing a believable modern air war: AI tasking, IADS behavior, SAM engagements, air-to-ground effects, and a living frontline that reacts to outcomes — all using core engine rules and module catalog stats.

Future Modules (DCS, MSFS, etc.) swap catalog and sim adapter without rewriting how the war is simulated.
