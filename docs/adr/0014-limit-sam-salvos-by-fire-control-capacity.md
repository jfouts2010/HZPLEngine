# ADR 0014: Limit SAM salvos by fire-control capacity

## Status

Accepted

## Context

SAM site templates contain separate launcher components to represent ready rails or vehicles. The initial launch implementation treated every eligible launcher component as an independent firing decision. A six-rail SA-2 battery therefore fired all six missiles at one track during the same tactical update, even though its single Fan Song radar was the battery-level engagement and guidance resource.

Repeated assignment refreshes also ignored missiles already in flight, and multiple sites could independently select the same hostile flight.

## Decision

Launcher definitions author a preferred engagement salvo size. Weapon-quality radar definitions author maximum simultaneously supported missiles and maximum concurrent target engagements.

Launch execution selects eligible launcher components deterministically and stops when either the preferred salvo or remaining radar support capacity is filled. Ready rails not selected for the salvo retain their missiles.

A radar cannot begin another salvo against a flight while one of its salvos against that flight remains unresolved. Pending SAM effects occupy that radar's missile and target capacities. At the IADS level, a hostile flight with missiles already in flight is reserved, and newly created assignments reserve their selected flights so separate sites do not duplicate the same engagement during that update.

The test-module SA-2 battery retains six ready launcher rails. Its Fan Song may support three missiles against one target, and its preferred engagement salvo is two missiles.

## Consequences

Physical launcher count remains distinct from fire-control capacity and firing doctrine. An SA-2 battery normally releases two missiles rather than all six, can technically support up to three, and may re-engage a surviving flight after the prior salvo resolves.

The v1 deconfliction rule permits only one site-level SAM engagement against a flight at a time. More advanced allocation based on formation size, committed probability of kill, doctrine, or command relationships remains deferred.
