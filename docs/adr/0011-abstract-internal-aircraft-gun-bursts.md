# ADR 0011: Model internal aircraft guns as abstract ordnance bursts

## Status

Accepted

## Context

Gun-only fighters cannot participate in campaign air combat when every usable aircraft weapon must be planned as an external missile or store. Treating an internal gun as an ordinary selectable store would make it compete with missile shot budgets and external ordnance capacity. A gun also is not exclusively air-to-air: the same installed weapon may be effective against aircraft, infantry, vehicles, buildings, or radars.

## Decision

The Module ordnance catalog includes a generic gun employment category. Target-category effectiveness determines which targets a gun can affect. An aircraft type may author one fixed internal gun ordnance type and a full-sortie inventory measured in abstract gun bursts.

The air loadout planner selects external weapons within the existing weight and shot budgets, then adds the installed gun's full burst inventory without consuming either budget. The inventory is materialized as ordinary per-aircraft runtime loadout items, allowing existing preparation, firing, expenditure, pending-effect resolution, individual-aircraft outcomes, diagnostics, recovery clearing, and later sortie rearming to remain authoritative. Gun bursts may be fully expended rather than retaining the doctrine reserve used for discrete air-to-air stores.

One gun burst represents a firing opportunity rather than a projectile or literal ammunition count. Gun ordnance uses the existing envelope, off-boresight geometry, preparation time, effect speed, hit probability, terminal lethality, and target-effectiveness fields. It does not introduce projectile entities or a separate gun-combat resolver.

Outside WVR, guns use the ordinary weapon-envelope resolver. Inside the WVR decision range, an authorized merge routes gun bursts through the abstract WVR engagement while retaining the same inventory, lethality, survivability, and diagnostic pipeline.

## Consequences

Gun-only and missile-armed aircraft can carry persistent, expendable internal-gun capability without misrepresenting it as an external store. The same gun definition can support future air-to-ground employment through target effectiveness. Detailed ammunition belts, rates of fire, convergence, heating, jams, ballistic projectile motion, strafing logic, and geometric WVR maneuver selection remain deferred.
