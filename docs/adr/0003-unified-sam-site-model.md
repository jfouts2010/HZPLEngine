# Unified SAM site model

SAM behavior is modeled through shared SAM site identities with static or mobile hosts, rather than as separate "building that shoots" and "division attachment that shoots" implementations. Static SAM sites are hosted by static SAM buildings, while mobile SAM sites may be hosted by divisions without becoming battalion stats or participating in ground combat. This keeps detection, component damage, suppression, engagement assignment, launch execution, and future IADS behavior unified while still letting each host model own placement, movement, capture, overrun, and repair concerns.

Runtime SAM state has one source of truth: `SamSite` instances owned by `AirDefenseSiteSystem`. A site records its identity, template, components, operational state, and a host reference. An `AirDefenseBuilding` is only a static host; it does not duplicate the site's components or implement the air-defense-site contract. Mobile and static placement are resolved through the referenced division or building respectively.

`AirDefenseSiteSystem` is the query boundary for effective alliance, current tile, and available components. These facts are derived from the site and its current host rather than copied into a separate view model. Alliance IADS behavior consumes that query boundary and therefore does not need concrete-host type switches or a bundle of resolution delegates.
