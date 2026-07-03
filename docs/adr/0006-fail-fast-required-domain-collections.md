# Fail fast for required domain collections

Required domain collections and provider functions should be treated as part of a valid campaign/module model rather than repeatedly hidden behind local null fallbacks. Module catalog collections, aircraft ordnance compatibility lists, campaign allowance lookups, planned loadout collections, and similar core planning inputs are expected to exist when the owning object is valid; code that consumes them should allow ordinary runtime failure if that invariant is broken.

This keeps validation focused on domain legality: unknown ordnance, forbidden stores, incompatible stores, overweight loadouts, duplicate aircraft, invalid routes, and other meaningful proposal failures should produce explicit diagnostics. Missing required collections are construction/model defects, not alternative empty states, and substituting `null` with empty lists can silently turn bad data into misleading "no capability" or "no candidate" outcomes.

Optional domain states should still be modeled explicitly. For example, a support aircraft may legitimately have an empty planned loadout, and route validation may still reject missing route inputs with a diagnostic. The convention is to distinguish intentional emptiness from missing required data, not to remove all defensive validation.
