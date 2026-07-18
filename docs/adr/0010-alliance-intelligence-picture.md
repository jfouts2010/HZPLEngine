# Alliance intelligence picture

Planning knowledge about hostile ground formations, buildings, airports, and static or mobile air-defense sites is represented by a persistent alliance intelligence picture rather than by allowing commanders to read authoritative campaign entities directly. Bluefor and Redfor own separate observer-relative pictures. Friendly command state remains exact and is not duplicated.

Campaign entities remain the source of physical truth. Ground and air planning, targeting, estimates, and future alliance-facing presentation consume hostile intelligence reports. Movement, combat, capture, supply resolution, IADS operation, and damage resolution continue to use authoritative state. Subject IDs retained by reports are opaque references for correlation and eventual target resolution, not permission for planners to dereference hostile entities.

Every report carries a continuous `InformationQuality` value clamped from `0` to `1`. Intermediate semantics are deferred until a real observation or reconnaissance feature needs them. Quality `1` means the most complete realistically obtainable physical picture, including division-template composition and condition, building condition, grounded airport inventory, and air-defense component inventory and condition. It excludes enemy orders, AI intent, movement progress, IADS tracks, engagement assignments, and other private command bookkeeping.

For current autonomous testing, a maximum-information producer refreshes every hostile report immediately to quality `1` before planning. This preserves current test behavior while forcing consumers through the intelligence boundary. Later sensor, reconnaissance, staleness, confidence, and deception rules can replace this producer without changing campaign truth or planning contracts.

Enemy supply-network criticality is deferred. Ground planning values observed hostile hubs and railroads as buildings but does not use authoritative hostile supply assignments or topology.

The operational renderer retains an explicitly omniscient debug mode and may read authoritative state. Tile control and derived front boundaries remain perfect knowledge.
