# ADR 0012: Resolve WVR combat as persistent abstract rounds

## Status

Accepted

## Context

The chronological air-combat model originally forced every flight without a valid shot to extend inside an eight-kilometer deferred WVR boundary. A later continuation rule let gun-only fighters close, but treating a merge as ordinary intercept motion still let exact instantaneous geometry dominate WVR outcomes and could not represent a temporary positional advantage.

The aggregate flight model does not represent energy state, one-circle or two-circle geometry, individual formation tactics, corner speed, or turn radius. Those details are inappropriate for the operational scale.

## Decision

Inside eight kilometers, air-combat rules decide whether to merge after the existing defense, missile-support, mission-authorization, fuel, pursuit, and recommit gates. The merge decision occurs before ordinary weapon-envelope evaluation. Opposing flights that merge enter one persistent WVR engagement; additional opposing flights may join an existing engagement.

A flight extends rather than merging when its own unresolved air-to-air effect is already pending against the target, when no infrared missile or aircraft-effective gun remains, or when its mission does not authorize a discretionary merge. An authorized BARCAP flight may merge to stop its assigned threat. An OCA sweep may merge only when a WVR-capable weapon remains and all radar-guided air-to-air weapons are depleted.

An engagement resolves every twenty campaign seconds. Engaged flights remain at their aggregate position, burn combat fuel, and stop route and ordinary tactical movement until they individually disengage or one side is defeated. Each aircraft type supplies one normalized WVR combat rating. A neutral merge begins with simultaneous low-probability opportunities for both sides. Later rounds use rating, effective aircraft count, temporary advantage, and bounded deterministic uncertainty to determine which side earns an attack opportunity; a close later contest also gives both sides simultaneous neutral opportunities. Infrared missiles are used before aircraft-effective gun bursts, and each opportunity spends one real round through the existing ordnance inventory and effect resolver.

Advantage has neutral, favorable, and dominant levels. A flight entering from the target's rear or against an unaware target receives favorable advantage; satisfying both conditions gives dominant advantage. Entry aspect is evaluated once and does not become a missile-aspect simulation. Dominant advantage guarantees the opening opportunity and then degrades. Favorable advantage grants the next opportunity. Later control contests can preserve, remove, or reverse advantage, abstracting overshoots and reversals.

Awareness comes from the target's previously retained tactical target or an established non-stale track of sufficient quality. Once combat begins, targets are aware. Hit probability starts from the authored weapon probability and applies only WVR advantage and awareness modifiers. Aware targets sharply reduce both gun and infrared success; infrared weapons additionally use their countermeasure resistance against target ECM. Unaware targets instead receive the intended high-probability opening attack. This WVR value is passed directly into immediate existing effects so generic movement defense and exact release geometry do not double-count the abstraction. Any ordinary air-to-air employment still preparing when its source becomes WVR-locked is aborted; already-released effects remain authoritative.

Damage is a flyable combat impairment rather than an automatic exit from WVR. A damaged aircraft retains its remaining weapons, can still attack and be attacked, contributes only thirty percent of its normal WVR rating and one quarter of an effective aircraft to control contests, and flies at sixty percent of normal combat speed. Any subsequent successful weapon hit destroys an already-damaged aircraft instead of producing another damage result. Damage recovery cannot overwrite a WVR lock; recovery begins only after the flight leaves the engagement.

Flights that are damaged, have no WVR weapon, reach bingo fuel, or remain engaged through twelve rounds attempt deterministic disengagement once per later round. Disengagement is resolved per flight, not per side. Its chance depends on relative effective WVR rating and speed, temporary advantage, covering friendly flights, and outside pressure from a friendly pending weapon or current SAM assignment against a pursuer. A failed attempt gives the opponent an immediate attack opportunity and leaves the flight locked. A successful flight separates for forty-five seconds; covering flights and opponents remain engaged while both sides still have participants. There is no automatic round-limit escape.

## Consequences

Gun-only and short-range fighters can produce credible campaign outcomes without a geometric dogfight simulator. Rear/unaware entry matters strongly but temporarily, head-on aware exchanges are usually ineffective, multiple flights can influence and join the same fight, and simultaneous opportunities can produce mutual losses. A damaged flight is strongly disadvantaged and unlikely to escape an isolated equal fight, while another friendly WVR flight or credible SAM pressure can cover its withdrawal. Modern OCA fighters normally preserve separation while radar-guided weapons remain, while BARCAP defenders can merge when the protected barrier requires it.

The cost is one small persistent WVR encounter and one new aircraft rating. No behavior tree, new doctrine field, aircraft-era flag, corner-speed model, turn geometry, per-aircraft maneuver state, or specialized missile-aspect category is introduced. The existing loadout, expenditure, lethality, survivability, damage, and diagnostic systems remain authoritative.
