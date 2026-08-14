# DCS AI Observation Export

The DCS prototype Module can export a paused campaign moment as a DCS `.miz`
mission. This first mode is deliberately observation-only: every exported
aircraft is controlled by DCS AI and there are no `Player` or `Client` slots.

## Exported scope

| Campaign state | DCS representation |
| --- | --- |
| Currently airborne flights | AI aircraft groups starting at their current position, altitude, heading, speed, loadout, and remaining route, plus a coalition-colored F10 drawing overlay of that route |
| Air mission intent | CAP, SEAD, AWACS, tanker, route, engage, and orbit tasks where supported by the prototype |
| Active, unsuppressed SAM sites | DCS vehicle groups containing mapped, undamaged SAM components |
| Caucasus airports | Existing DCS airbases with campaign coalition ownership and operational warehouse state |

The exporter intentionally excludes ground formations, ground combat,
infrastructure other than airports, campaign targets, result import, triggers,
and player aircraft. Airports are already part of the Caucasus map, so the
mission records their ownership and availability rather than spawning airport
objects.

## Create and run an observation mission

1. Start the **DCS Prototype Module** campaign in Unity.
2. Advance time until at least one flight is airborne.
3. Pause the campaign. The pause makes the exported snapshot internally
   consistent.
4. Open **Air** in the campaign workbench.
5. Select **Export current air picture (.miz)**.
6. Read the export status for the included counts, warnings, and exact output
   path.
7. In DCS, open **Mission > My Missions**, select the generated
   `HZPL AI Observation ... .miz`, and run it. Use the map and external-view
   controls to follow the AI action.

The workbench writes to `Saved Games\DCS\Missions` when that folder exists. It
also recognizes `Saved Games\DCS.openbeta\Missions`. If neither exists, it
writes to Unity's persistent-data directory under `DCS Missions` and displays
that full path.

Repeated exports never overwrite an existing mission; a numeric suffix is
added instead.

## First DCS validation checklist

- The mission loads without a mission-script error.
- DCS begins in observer/map view and simulation time advances.
- Every exported aircraft flies under AI control; no role-selection slot is
  offered.
- Flights begin near their HZPL snapshot positions and follow plausible
  remaining routes.
- Blue and red aircraft routes appear as matching colored drawing overlays on
  the F10 map's common drawing layer without relying on DCS's **Show all
  routes** renderer. DCS's **Show Drawings** toggle must be enabled.
- CAP, sweep, support, and SEAD aircraft attempt their assigned tasks.
- Active SAM sites appear, track targets, and engage when DCS rules permit.
- Airport coalition ownership matches the HZPL map.
- No ordinary divisions or other ground-war units appear.

Record the generated mission name, HZPL snapshot time, and any DCS log error for
each failed check. Mission Editor can also open the `.miz` for inspecting the
generated groups and routes.

## Prototype limitations

- Caucasus is the only theater mapping.
- Aircraft, weapon pylon, country, airport, and SAM mappings cover only the
  current prototype catalog.
- Weather is a fixed clear-weather preset.
- Radio, callsign, payload, task, and formation choices are intentionally
  simple.
- Route drawing overlays are static representations of the paused export
  snapshot; the native DCS route continues to control AI navigation.
- This mode captures airborne flights only. Scheduled or parked flights are not
  exported.
- A DCS mission result is not read back into HZPL.
- Player intervention is not included. A future intervention mode must create
  an explicit `Player`/`Client` slot because such a slot is not an AI aircraft
  while it is waiting for a player.

The `.miz` writer is owned by HZPL. Briefing Room remains a useful reference and
fixture oracle for DCS mission structure, but HZPL does not depend on its code
or runtime.
