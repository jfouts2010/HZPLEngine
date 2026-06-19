# Ground tactical combat resolution

Ground tactical combat resolves once per simulation tick using lightweight Hearts of Iron-inspired division combat. The combat record supplies attacker identity and join order, defenders are derived fresh from combat-ready divisions on the defending tile, and each round recalculates front-line and reserve assignment rather than persisting tactical slots.

Full-strength combat stats are derived from the division template when each runtime division is created, then carried by the division as stable runtime capability. Current strength and organization remain mutable runtime division state. Current strength percentage is `current strength / full-strength max strength`, clamped between 0 and 1; strength and organization damage are clamped at 0.

Front-line assignment uses the defending tile's terrain frontage, widened by 50% for both attackers and defenders when combat-ready attackers participate from more than one distinct current physical tile. Assignment is first-fit in deterministic order, with attacker order based on combat join order and defender order based on stable tile occupancy order; if a side has combat-ready divisions but none fit, its first combat-ready division fights over-width so every combat-ready side has a front line.

Only front-line divisions fire and can be targeted. Each firing division chooses one opposing front-line target for all of its shots, with target weights favoring softer targets when the shooter has higher soft attack than hard attack and harder targets when the shooter has higher hard attack than soft attack.

Shot count is based on the shooter's soft and hard attack against the target's softness, multiplied by the shooter's current strength percentage. Terrain attack penalty is an attacker fire multiplier applied to normal and support attackers; terrain otherwise affects combat only through frontage in this first model.

Each shot checks the target's current temporary defense points to choose the miss chance, resolves hit or miss, then decrements defense points by 1 while clamping them at 0. Defense points reset each round and are not persisted below zero.

Broken attackers halt on their current tile, receive a hold order, and lose attack movement progress. Broken defenders retreat individually while combat-ready defenders on the same tile continue defending. Support attackers are full combat participants for frontage, firing, return fire, losses, and attack failure, but they cannot capture the target tile by themselves.

Combat records do not accumulate persistent loss history in this model. Randomness should come from campaign/game RNG so seeded simulation and tests can be deterministic.
