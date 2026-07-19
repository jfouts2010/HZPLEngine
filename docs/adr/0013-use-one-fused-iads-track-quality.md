# ADR 0013: Use one fused IADS track-quality value

## Status

Accepted

## Context

The first IADS implementation used one normalized track-quality value but capped an alliance track at the best individual radar contribution. Additional radars only accelerated growth toward that unchanged cap. SAM launch execution then cut the shared track back to the local fire-control radar's individual cap and applied separate radar-, range-, and ECM-derived release penalties that duplicated information already represented by track quality.

The campaign needs uncertainty to affect air-defense decisions without introducing a covariance tracker or separate persisted confidence, position, altitude, and velocity quality fields.

## Decision

One `IADSTrack.Quality` value represents combined certainty about a hostile flight's current position, altitude, heading, and speed. All created tracks are known hostile. Aircraft type becomes known at the existing quality threshold and remains known for the track lifetime.

Each live radar contributes an individual cap and build rate. Independent caps fuse by combining remaining uncertainty:

`fused cap = 1 - product(1 - individual cap)`

Build-rate contributions retain diminishing returns. Observed quality moves toward the fused cap, degrades rather than snapping downward when the available cap falls, loses quality from heading, speed, and altitude changes, and decays rapidly while stale.

Sub-threshold tentative tracks retain accumulated quality internally while continuously observed but are not exposed as IADS current tracks until they reach the existing creation threshold.

A radar-guided SAM launch uses the fused shared quality after an operational local fire-control source and all launcher envelopes and doctrine thresholds authorize the shot. The local radar remains required for acquisition and guidance continuity but does not replace the shared quality with its individual cap.

For SAM releases, snapshotted track quality is the ideal hit probability when required guidance remains continuous and the target does not maneuver or employ countermeasures. Post-launch support and defense modify hit probability; lethality and target survivability separately resolve destruction after a hit.

## Consequences

Multiple radar observations can create a better track than any individual sensor while still exhibiting diminishing acquisition speed benefits. A fully informed `1.0` track produces a certain ideal SAM hit, and lower quality has a direct, legible meaning.

HZPL retains one campaign-scale uncertainty value rather than a full covariance model. Radar correlation, false contacts, IFF, VID, identity ambiguity, active ECM state, and detailed measurement noise remain deferred.
