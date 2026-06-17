# Ground orders and combat records

Runtime divisions carry exactly one persistent polymorphic ground order as their current operational responsibility, while GameManager-orchestrated ground systems resolve orders into movement, combat, capture, retreat, and destruction. Orders are data, not behavior: AI or system rules assign them, ground operation and combat systems resolve them, and completed orders are replaced by a system-assigned hold order.

Active ground combat is represented by persistent combat records keyed by defending tile, with at most one combat per defending tile. This was chosen over deriving combat ad hoc each tick because multi-division attacks, support attacks, retreats, UI/debugging, and future air-to-ground interactions need a stable place to track attacking participants and battle identity as the battle changes. Defenders are derived fresh from combat-ready divisions physically on the defending tile each combat round rather than treated as authoritative stored participants.

Attacker participant order is meaningful: the attacker list preserves join order and is used when assigning attackers to the front line or reserve during each combat round.

Defender participant order is derived from stable tile occupancy order when assigning defenders to the front line or reserve. Defenders are not given separate combat join state.

Attack orders are specialized movement orders: attackers can make progress toward the target while combat is unresolved, but they remain physically on their origin tile until defenders no longer hold the target and the attackers arrive. Support attacks are sibling orders that can initiate combat and force defeated defenders to retreat, but never move into or capture the tile. Retreat is a forced system-assigned move order that cannot be replaced, contributes no combat strength, and destroys the division if no valid retreat destination exists or if that destination is captured first.
