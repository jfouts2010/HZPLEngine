# ADR 0016: Use categorical radar-missile defeat

## Status

Accepted

## Context

Radar-missile defense previously reduced terminal hit probability continuously from accumulated beam, break, drag, or extend maneuvering. This allowed a missile to retain a hit chance after its target had moved beyond the missile's effective maximum range, and it represented a successful beam and implicit chaff employment as a partial accuracy penalty rather than a broken radar lock.

The engine intentionally represents released ordnance as aggregate pending effects rather than moving missile entities. A deeper missile-energy, seeker, or countermeasure simulation is not yet justified.

## Decision

This decision supersedes the continuous terminal radar-missile defense portion of ADR 0015. Infrared terminal defense remains governed by ADR 0015.

The effective maximum range used to authorize a radar-guided missile release is snapshotted onto its pending effect. The missile is categorically defeated when its target's slant distance from the stored launch position exceeds that range. The defeat is irreversible.

Radar-guided pending effects accumulate only achieved beam geometry during the existing terminal-defense window. The target aircraft's aggregate radar-defense rating and one stable effect-level roll determine whether that cumulative opportunity breaks radar lock. Reusing one roll prevents tactical checkpoint frequency from creating additional independent chances.

A defeated effect no longer drives threat response, guidance support, duplicate-shot reservation, or fire-control capacity. It remains pending until its scheduled effect time so resolution diagnostics retain chronological ordering. At resolution, every store in the aggregate effect is recorded as defeated and makes no hit roll.

Drag and beam no longer reduce terminal hit probability for an undefeated radar missile. Drag either carries the target beyond the snapshotted range or provides no terminal benefit; a beam either breaks radar lock or provides no terminal benefit. Existing infrared defensive probability behavior remains unchanged.

## Consequences

Radar-missile defense now produces explainable kinematic or guidance defeats without adding missile position, remaining energy, seeker entities, chaff inventory, countermeasure programs, or per-missile state inside an aggregate salvo. Maximum range remains an abstract launch-time travel budget, and scheduled effect time remains based on release geometry.

All missiles represented by one pending effect share its categorical defeat result. Independent active-seeker outcomes, exact pursuit distance, radar reacquisition, and detailed active-versus-semi-active lock timing remain deferred until an implemented consumer requires them.
