# ADR 0012: Add a minimal WVR continuation decision

## Status

Accepted

## Context

The chronological air-combat model originally forced every flight without a valid shot to extend inside an eight-kilometer deferred WVR boundary. That prevented gun-only fighters from closing to their weapon envelope and made defensive counter-air flights abandon threats solely because the engagement reached close range. Removing the boundary unconditionally would create the opposite problem: modern sweep aircraft would accept unnecessary merges while standoff weapons or already-flying ordnance offered safer choices.

The aggregate flight model does not yet represent energy state, one-circle or two-circle geometry, individual formation tactics, visual identification requirements, or a hostile minimum-abort-range estimate. Adding those concepts only to decide whether to cross one range threshold would exceed the current need.

## Decision

Inside eight kilometers, air-combat rules make one stateless WVR continuation decision after evaluating any immediately valid firing opportunity and after the existing defense, missile-support, mission-authorization, fuel, pursuit, and recommit gates.

A flight extends rather than continuing when its own unresolved air-to-air effect is already pending against the target, when no infrared missile or aircraft-effective gun remains, or when its mission does not authorize a discretionary merge.

An authorized BARCAP flight may continue into WVR because stopping the assigned threat is defensive mission necessity. An OCA sweep may continue only when a WVR-capable weapon remains and all radar-guided air-to-air weapons are depleted. Aircraft era is inferred from the useful options in the live loadout rather than from a new historical or technology flag.

Continuation reuses the existing intercept and press maneuvers and predicted intercept point. When no weapon has an immediately employable firing opportunity inside the WVR decision range, setup preference is infrared missile, then gun, then radar-guided missile. Doctrine-reserved missile rounds do not displace an employable gun burst. An employable missile remains preferable to an employable gun burst.

## Consequences

Gun-only and short-range fighters can close and eventually employ their weapons. Modern OCA fighters normally preserve separation while radar-guided weapons remain, while BARCAP defenders can continue when the protected barrier requires it. Existing BARCAP boundary clamping still limits the defensive chase.

No WVR encounter entity, persistent merge phase, behavior tree, new doctrine field, aircraft-era flag, execution cadence, energy model, or specialized dogfight maneuver is introduced. Dynamic minimum-abort-range logic remains deferred until the simulation has a non-cheating source of hostile weapon-envelope knowledge and motion fidelity that can support it.
