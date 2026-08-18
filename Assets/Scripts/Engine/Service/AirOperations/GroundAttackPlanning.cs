using System;
using System.Collections.Generic;
using System.Linq;
using Models.Gameplay.Campaign;
using Models.Module;

namespace Engine.Service
{
    public sealed class GroundAttackOpportunityService
    {
        private readonly IReadOnlyDictionary<Guid, AirDefenseComponentDefinition>
            componentDefinitions;

        public GroundAttackOpportunityService(
            IReadOnlyDictionary<Guid, AirDefenseComponentDefinition>
                componentDefinitions)
        {
            this.componentDefinitions = componentDefinitions
                                        ?? throw new ArgumentNullException(
                                            nameof(componentDefinitions));
        }

        public GroundAttackOpportunity RollDeadOpportunity(
            Guid sourceFlightId,
            int opportunitySequence,
            SamSite site,
            UnityEngine.Vector3Int siteTileId,
            IReadOnlyCollection<Guid> authorizedComponentIds,
            DateTime currentTime,
            Func<Guid, bool> isAlreadyCovered = null)
        {
            var opportunity = new GroundAttackOpportunity
            {
                GeneratedAt = currentTime,
                TargetTileId = siteTileId,
                Description = "No useful SAM component was exposed during this attack window."
            };
            if (site == null || sourceFlightId == Guid.Empty)
                return opportunity;

            var authorized = new HashSet<Guid>(
                authorizedComponentIds ?? Array.Empty<Guid>());
            var candidates = site.Components
                .Where(component => component != null
                                    && !component.IsDamaged
                                    && authorized.Contains(component.ComponentId)
                                    && componentDefinitions.ContainsKey(
                                        component.SamComponentDefinitionId))
                .Select(component => new
                {
                    Component = component,
                    Definition = componentDefinitions[
                        component.SamComponentDefinitionId]
                })
                .ToList();
            var fireControl = candidates
                .Where(candidate => candidate.Definition
                    is RadarAirDefenseComponentDefinition
                    {
                        ProvidesWeaponQualityTrack: true
                    })
                .ToList();
            if (fireControl.Count > 0)
                candidates = fireControl;
            candidates = candidates
                .Where(candidate => isAlreadyCovered == null
                                    || !isAlreadyCovered(
                                        candidate.Component.ComponentId))
                .ToList();
            if (candidates.Count == 0)
                return opportunity;

            var opportunityRoll = StableRoll(
                sourceFlightId,
                site.SiteId,
                opportunitySequence,
                0);
            if (opportunityRoll < 0.1d)
                return opportunity;

            var sizeRoll = StableRoll(
                sourceFlightId,
                site.SiteId,
                opportunitySequence,
                1);
            var desiredCount = sizeRoll < 0.5d
                ? 1
                : sizeRoll < 0.8d
                    ? 2
                    : sizeRoll < 0.95d
                        ? 3
                        : 4;
            var selected = candidates
                .OrderBy(candidate => StableRoll(
                    sourceFlightId,
                    candidate.Component.ComponentId,
                    opportunitySequence,
                    2))
                .ThenBy(candidate => candidate.Component.ComponentId)
                .Take(Math.Min(desiredCount, candidates.Count))
                .Select(candidate => CreateComponentTarget(
                    site,
                    siteTileId,
                    candidate.Component,
                    candidate.Definition))
                .OrderByDescending(target => target.MissionPriority)
                .ThenBy(target => target.Target.EntityId)
                .ToList();

            opportunity.Targets.AddRange(selected);
            opportunity.MaximumReleases = selected.Count;
            opportunity.Quality = selected.Count == 1
                ? GroundAttackOpportunityQuality.Fleeting
                : selected.Count >= 3
                    ? GroundAttackOpportunityQuality.Excellent
                    : GroundAttackOpportunityQuality.Normal;
            opportunity.Description = selected.Count == 1
                ? $"A single {selected[0].Description} was exposed."
                : $"{selected.Count} SAM components were exposed together.";
            return opportunity;
        }

        /// <summary>
        /// Creates a deterministic attack opportunity from an emitter that the
        /// SEAD escort has actually detected. Emission itself is the exposure;
        /// unlike a DEAD component search, this opportunity is not rolled.
        /// </summary>
        public GroundAttackOpportunity CreateSeadEmitterOpportunity(
            DetectedEmitter emitter,
            SamSite site,
            RadarAirDefenseComponent radar,
            UnityEngine.Vector3Int siteTileId,
            DateTime currentTime)
        {
            var opportunity = new GroundAttackOpportunity
            {
                GeneratedAt = currentTime,
                TargetTileId = siteTileId,
                Description = "The detected emitter was no longer targetable."
            };
            if (emitter == null
                || site == null
                || radar == null
                || emitter.SiteId != site.SiteId
                || emitter.RadarComponentId != radar.ComponentId
                || radar.IsDamaged
                || !radar.IsEmitting
                || !componentDefinitions.TryGetValue(
                    radar.SamComponentDefinitionId,
                    out var definition))
                return opportunity;

            var target = CreateComponentTarget(
                site,
                siteTileId,
                radar,
                definition);
            target.MissionPriority = Math.Max(
                target.MissionPriority,
                400f - Math.Max(0, emitter.ThreatPriority) * 50f);
            target.CanReceiveSecondaryEffect = false;
            target.Description = $"detected emitting {definition.Name}";
            opportunity.Targets.Add(target);
            opportunity.MaximumReleases = 1;
            opportunity.Quality = GroundAttackOpportunityQuality.Fleeting;
            opportunity.Description =
                $"{definition.Name} was emitting while threatening "
                + $"{emitter.ThreatenedFlightIds.Count} protected flight(s).";
            return opportunity;
        }

        public GroundAttackOpportunity RollDivisionOpportunity(
            Guid sourceFlightId,
            int opportunitySequence,
            Division division,
            DivisionTemplate divisionTemplate,
            IReadOnlyDictionary<Guid, BattalionDefinition>
                battalionDefinitions,
            DateTime currentTime)
        {
            var opportunity = new GroundAttackOpportunity
            {
                GeneratedAt = currentTime,
                TargetTileId = division?.TileId ?? default,
                Description = "No useful element of the division was exposed during this attack window."
            };
            if (division == null
                || division.Strength < 1f
                || divisionTemplate == null
                || divisionTemplate.DivisionTemplateId
                != division.DivisionTemplateId
                || battalionDefinitions == null
                || sourceFlightId == Guid.Empty)
                return opportunity;

            var candidates = divisionTemplate.Battalions
                .Where(item => item != null && item.Count > 0)
                .SelectMany(item => battalionDefinitions.TryGetValue(
                        item.BattalionDefinitionId,
                        out var battalion)
                    ? battalion.GroundTargetProfile
                        .Where(profile => profile != null)
                        .Select(profile => new DivisionProfileCandidate(
                            profile,
                            profile.PresenceWeight * item.Count,
                            profile.MaximumPerOpportunity * item.Count))
                    : Enumerable.Empty<DivisionProfileCandidate>())
                .Where(candidate => candidate.Weight > 0f
                                    && candidate.Remaining > 0)
                .ToList();
            if (candidates.Count == 0
                || StableRoll(
                    sourceFlightId,
                    division.DivisionId,
                    opportunitySequence,
                    0) < 0.1d)
                return opportunity;

            var sizeRoll = StableRoll(
                sourceFlightId,
                division.DivisionId,
                opportunitySequence,
                1);
            var desiredCount = sizeRoll < 0.5d
                ? 1
                : sizeRoll < 0.8d
                    ? 2
                    : sizeRoll < 0.95d
                        ? 3
                        : 4;
            for (var index = 0; index < desiredCount; index++)
            {
                var available = candidates
                    .Where(candidate => candidate.Remaining > 0)
                    .ToList();
                var totalWeight = available.Sum(candidate => candidate.Weight);
                if (available.Count == 0 || totalWeight <= 0f)
                    break;

                var selection = StableRoll(
                                    sourceFlightId,
                                    division.DivisionId,
                                    opportunitySequence,
                                    index + 2)
                                * totalWeight;
                var selected = available[available.Count - 1];
                var cumulative = 0d;
                foreach (var candidate in available)
                {
                    cumulative += candidate.Weight;
                    if (selection >= cumulative)
                        continue;
                    selected = candidate;
                    break;
                }
                selected.Remaining--;
                opportunity.Targets.Add(CreateDivisionTarget(
                    division,
                    selected.Profile));
            }

            opportunity.MaximumReleases = opportunity.Targets.Count;
            opportunity.Quality = opportunity.Targets.Count == 1
                ? GroundAttackOpportunityQuality.Fleeting
                : opportunity.Targets.Count >= 3
                    ? GroundAttackOpportunityQuality.Excellent
                    : GroundAttackOpportunityQuality.Normal;
            opportunity.Description = opportunity.Targets.Count == 1
                ? $"A single {opportunity.Targets[0].Description} was exposed."
                : $"{opportunity.Targets.Count} division elements were exposed together.";
            return opportunity;
        }

        public GroundAttackOpportunity CreateAirportRunwayOpportunity(
            Guid sourceFlightId,
            int opportunitySequence,
            Airport airport,
            int desiredDamagePerChannel,
            DateTime currentTime,
            Func<GroundAttackTargetReference, int> countPendingEffects)
        {
            var opportunity = new GroundAttackOpportunity
            {
                GeneratedAt = currentTime,
                TargetTileId = airport?.TileId ?? default,
                Description = "No useful runway damage remained for this attack window."
            };
            if (airport == null || sourceFlightId == Guid.Empty)
                return opportunity;

            airport.EnsureRunwayChannels();
            var desired = Math.Max(
                1,
                Math.Min(
                    AirportRunwayChannel.MaximumDamageLevel,
                    desiredDamagePerChannel));
            var pendingByChannel = airport.RunwayChannels.ToDictionary(
                channel => channel.ChannelIndex,
                channel =>
                {
                    var reference = CreateRunwayReference(
                        airport,
                        channel.ChannelIndex);
                    return Math.Max(
                        0,
                        countPendingEffects?.Invoke(reference) ?? 0);
                });

            // Interleave channels at every depth. All still-open channels get a
            // denial attempt before any runway receives deeper damage.
            for (var depth = 1; depth <= desired; depth++)
            {
                foreach (var channel in airport.RunwayChannels
                             .OrderBy(item => item.ChannelIndex))
                {
                    var projectedDamage = Math.Min(
                        AirportRunwayChannel.MaximumDamageLevel,
                        channel.DamageLevel
                        + pendingByChannel[channel.ChannelIndex]);
                    if (projectedDamage >= depth)
                        continue;

                    opportunity.Targets.Add(new GroundAttackOpportunityTarget
                    {
                        Target = CreateRunwayReference(
                            airport,
                            channel.ChannelIndex),
                        TargetCategory = OrdnanceTargetCategory.Runway,
                        TargetToughness = airport.TargetToughness,
                        MissionPriority = depth == 1
                            ? 100f
                            : Math.Max(10f, 80f - depth * 10f),
                        CanReceiveSecondaryEffect = false,
                        DamageSlotIndex = depth - 1,
                        Description = depth == 1
                            ? $"runway channel {channel.ChannelIndex + 1} denial aim point"
                            : $"runway channel {channel.ChannelIndex + 1} damage aim point {depth}"
                    });
                }
            }

            opportunity.MaximumReleases = opportunity.Targets.Count;
            opportunity.Quality = GetQuality(opportunity.Targets.Count);
            if (opportunity.HasTargets)
            {
                opportunity.Description =
                    $"{opportunity.Targets.Count} useful runway aim points were available across "
                    + $"{airport.NominalRunwayChannelCount} runway channels.";
            }
            return opportunity;
        }

        public GroundAttackOpportunity RollParkedAircraftOpportunity(
            Guid sourceFlightId,
            int opportunitySequence,
            Airport airport,
            IEnumerable<CampaignAircraft> groundedAircraft,
            DateTime currentTime,
            Func<GroundAttackTargetReference, bool> isAlreadyCovered)
        {
            var opportunity = new GroundAttackOpportunity
            {
                GeneratedAt = currentTime,
                TargetTileId = airport?.TileId ?? default,
                Description = "No useful parked-aircraft group was exposed during this attack window."
            };
            if (airport == null || sourceFlightId == Guid.Empty)
                return opportunity;

            var candidates = (groundedAircraft
                              ?? Enumerable.Empty<CampaignAircraft>())
                .Where(aircraft => aircraft != null
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Lost)
                .GroupBy(aircraft => aircraft.AircraftId)
                .Select(group => group.First())
                .Select(aircraft => new
                {
                    Aircraft = aircraft,
                    Target = new GroundAttackTargetReference
                    {
                        Kind = GroundAttackTargetKind.GroundedAircraft,
                        EntityId = aircraft.AircraftId,
                        TileId = airport.TileId
                    }
                })
                .Where(candidate => isAlreadyCovered == null
                                    || !isAlreadyCovered(candidate.Target))
                .ToList();
            if (candidates.Count == 0
                || StableRoll(
                    sourceFlightId,
                    airport.BuildingId,
                    opportunitySequence,
                    20) < 0.1d)
                return opportunity;

            var sizeRoll = StableRoll(
                sourceFlightId,
                airport.BuildingId,
                opportunitySequence,
                21);
            var desiredCount = sizeRoll < 0.5d
                ? 1
                : sizeRoll < 0.8d
                    ? 2
                    : sizeRoll < 0.95d
                        ? 3
                        : 4;
            var selected = candidates
                .OrderBy(candidate => StableRoll(
                    sourceFlightId,
                    candidate.Aircraft.AircraftId,
                    opportunitySequence,
                    22))
                .ThenBy(candidate => candidate.Aircraft.AircraftId)
                .Take(Math.Min(desiredCount, candidates.Count))
                .Select(candidate => new GroundAttackOpportunityTarget
                {
                    Target = candidate.Target,
                    TargetCategory = OrdnanceTargetCategory.Aircraft,
                    TargetToughness = 1,
                    MissionPriority = candidate.Aircraft.Status
                                      == CampaignAircraftStatus.Assigned
                        ? 110f
                        : candidate.Aircraft.Status
                          == CampaignAircraftStatus.Ready
                            ? 100f
                            : 50f,
                    CanReceiveSecondaryEffect = true,
                    Description = candidate.Aircraft.Status
                                  == CampaignAircraftStatus.Assigned
                        ? "committed aircraft awaiting takeoff"
                        : candidate.Aircraft.Status
                          == CampaignAircraftStatus.Ready
                            ? "ready aircraft parked at the airbase"
                            : "damaged aircraft parked at the airbase"
                })
                .ToList();

            opportunity.Targets.AddRange(selected);
            opportunity.MaximumReleases = selected.Count;
            opportunity.Quality = GetQuality(selected.Count);
            opportunity.Description = selected.Count == 1
                ? "A single parked aircraft was exposed."
                : $"{selected.Count} aircraft were exposed together on an airbase ramp.";
            return opportunity;
        }

        public GroundAttackOpportunity RollAuthorizedFacilityOpportunity(
            Guid sourceFlightId,
            int opportunitySequence,
            Airport airport,
            IEnumerable<Building> facilities,
            DateTime currentTime,
            Func<GroundAttackTargetReference, bool> isAlreadyCovered)
        {
            var opportunity = new GroundAttackOpportunity
            {
                GeneratedAt = currentTime,
                TargetTileId = airport?.TileId ?? default,
                Description = "No authorized airbase facility was exposed during this attack window."
            };
            if (airport == null || sourceFlightId == Guid.Empty)
                return opportunity;

            var candidates = (facilities ?? Enumerable.Empty<Building>())
                .Where(building => building != null
                                   && building.TileId == airport.TileId
                                   && building.FunctionalLevel > 0)
                .Select(building => new
                {
                    Building = building,
                    Target = new GroundAttackTargetReference
                    {
                        Kind = GroundAttackTargetKind.Building,
                        EntityId = building.BuildingId,
                        TileId = building.TileId
                    }
                })
                .Where(candidate => isAlreadyCovered == null
                                    || !isAlreadyCovered(candidate.Target))
                .ToList();
            if (candidates.Count == 0
                || StableRoll(
                    sourceFlightId,
                    airport.BuildingId,
                    opportunitySequence,
                    30) < 0.1d)
                return opportunity;

            var sizeRoll = StableRoll(
                sourceFlightId,
                airport.BuildingId,
                opportunitySequence,
                31);
            var desiredCount = sizeRoll < 0.5d
                ? 1
                : sizeRoll < 0.8d
                    ? 2
                    : sizeRoll < 0.95d
                        ? 3
                        : 4;
            candidates = candidates
                .OrderBy(candidate => StableRoll(
                    sourceFlightId,
                    candidate.Building.BuildingId,
                    opportunitySequence,
                    32))
                .ThenBy(candidate => candidate.Building.BuildingId)
                .Take(Math.Min(desiredCount, candidates.Count))
                .ToList();
            foreach (var candidate in candidates)
            {
                opportunity.Targets.Add(new GroundAttackOpportunityTarget
                {
                    Target = candidate.Target,
                    TargetCategory = OrdnanceTargetCategory.Building,
                    TargetToughness = candidate.Building.TargetToughness,
                    MissionPriority = 100f,
                    CanReceiveSecondaryEffect = false,
                    Description = $"authorized {candidate.Building.Type} facility"
                });
            }
            opportunity.MaximumReleases = opportunity.Targets.Count;
            opportunity.Quality = GetQuality(opportunity.Targets.Count);
            if (opportunity.HasTargets)
            {
                opportunity.Description = opportunity.Targets.Count == 1
                    ? "An authorized airbase facility was exposed."
                    : $"{opportunity.Targets.Count} authorized airbase facilities were exposed together.";
            }
            return opportunity;
        }

        private static GroundAttackTargetReference CreateRunwayReference(
            Airport airport,
            int channelIndex)
        {
            return new GroundAttackTargetReference
            {
                Kind = GroundAttackTargetKind.AirportRunway,
                EntityId = airport.BuildingId,
                TileId = airport.TileId,
                SubtargetIndex = channelIndex
            };
        }

        private static GroundAttackOpportunityQuality GetQuality(int count)
        {
            return count <= 0
                ? GroundAttackOpportunityQuality.None
                : count == 1
                    ? GroundAttackOpportunityQuality.Fleeting
                    : count >= 3
                        ? GroundAttackOpportunityQuality.Excellent
                        : GroundAttackOpportunityQuality.Normal;
        }

        private static GroundAttackOpportunityTarget CreateComponentTarget(
            SamSite site,
            UnityEngine.Vector3Int siteTileId,
            AirDefenseComponent component,
            AirDefenseComponentDefinition definition)
        {
            return new GroundAttackOpportunityTarget
            {
                Target = new GroundAttackTargetReference
                {
                    Kind = GroundAttackTargetKind.AirDefenseComponent,
                    EntityId = component.ComponentId,
                    ParentEntityId = site.SiteId,
                    TileId = siteTileId
                },
                TargetCategory = definition.TargetCategory,
                TargetToughness = definition.TargetToughness,
                MissionPriority = GetDeadPriority(definition),
                Description = definition.Name
            };
        }

        private static GroundAttackOpportunityTarget CreateDivisionTarget(
            Division division,
            GroundTargetProfileEntry profile)
        {
            return new GroundAttackOpportunityTarget
            {
                Target = new GroundAttackTargetReference
                {
                    Kind = GroundAttackTargetKind.Division,
                    EntityId = division.DivisionId,
                    TileId = division.TileId
                },
                TargetCategory = profile.TargetCategory,
                TargetToughness = profile.TargetToughness,
                MissionPriority = GetGroundTargetPriority(
                    profile.TargetCategory,
                    profile.TargetToughness),
                Description = profile.Description
            };
        }

        private static float GetGroundTargetPriority(
            OrdnanceTargetCategory category,
            int toughness)
        {
            return category switch
            {
                OrdnanceTargetCategory.Radar => 90f,
                OrdnanceTargetCategory.Vehicle when toughness >= 3 => 80f,
                OrdnanceTargetCategory.Vehicle => 60f,
                OrdnanceTargetCategory.Building => 55f,
                OrdnanceTargetCategory.Infantry => 45f,
                _ => 30f
            };
        }

        private static float GetDeadPriority(
            AirDefenseComponentDefinition definition)
        {
            return definition switch
            {
                RadarAirDefenseComponentDefinition
                {
                    ProvidesWeaponQualityTrack: true
                } => 100f,
                LauncherAirDefenseComponentDefinition => 70f,
                RadarAirDefenseComponentDefinition => 55f,
                CommandAirDefenseComponentDefinition => 40f,
                _ => 25f
            };
        }

        internal static double StableRoll(
            Guid first,
            Guid second,
            int sequence,
            int salt)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                foreach (var value in first.ToByteArray())
                    hash = (hash ^ value) * 1099511628211UL;
                foreach (var value in second.ToByteArray())
                    hash = (hash ^ value) * 1099511628211UL;
                hash = (hash ^ (uint)sequence) * 1099511628211UL;
                hash = (hash ^ (uint)salt) * 1099511628211UL;
                return (hash & 0x1FFFFFFFFFFFFFUL)
                       / (double)0x20000000000000UL;
            }
        }

        private sealed class DivisionProfileCandidate
        {
            public GroundTargetProfileEntry Profile { get; }
            public float Weight { get; }
            public int Remaining { get; set; }

            public DivisionProfileCandidate(
                GroundTargetProfileEntry profile,
                float weight,
                int remaining)
            {
                Profile = profile;
                Weight = weight;
                Remaining = remaining;
            }
        }
    }

    public sealed class GroundAttackDecisionService
    {
        public bool TryPlan(
            GroundAttackOpportunity opportunity,
            IEnumerable<CampaignAircraft> sourceAircraft,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            Func<GroundAttackOpportunityTarget, OrdnanceTypeDefinition, bool>
                additionalSuitability,
            out GroundAttackPassPlan plan)
        {
            plan = null;
            if (opportunity == null
                || !opportunity.HasTargets
                || sourceAircraft == null
                || ordnanceTypes == null)
                return false;

            var available = sourceAircraft
                .Where(aircraft => aircraft != null
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Lost
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Damaged)
                .SelectMany(aircraft => aircraft.Loadout)
                .Where(item => item != null && item.Count > 0)
                .GroupBy(item => item.OrdnanceTypeDefinitionId)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Count));

            WeaponPlanCandidate best = null;
            foreach (var entry in available.OrderBy(item => item.Key))
            {
                if (!ordnanceTypes.TryGetValue(entry.Key, out var ordnance)
                    || !AirToGroundWeaponRules.IsAirToGround(ordnance))
                    continue;

                var eligible = opportunity.Targets
                    .Where(target => target?.Target != null
                                     && target.Target.Kind
                                     != GroundAttackTargetKind.None
                                     && target.CanBePrimaryTarget
                                     && AirToGroundWeaponRules.CanAffect(
                                         ordnance,
                                         target.TargetCategory,
                                         target.TargetToughness)
                                     && (additionalSuitability == null
                                         || additionalSuitability(target, ordnance)))
                    .OrderByDescending(target => DirectValue(target, ordnance))
                    .ThenBy(target => target.Target.EntityId)
                    .ToList();
                var directOnly = eligible
                    .Where(target => !target.CanReceiveSecondaryEffect
                                     || !AirToGroundWeaponRules.CanAffect(
                                         ordnance,
                                         target.TargetCategory,
                                         target.TargetToughness,
                                         ordnance.SecondaryGroundEffectMultiplier))
                    .ToList();
                var coverage = ordnance.SecondaryGroundEffectMultiplier > 0f
                    ? ordnance.MaximumGroundTargetsPerWeapon
                    : 1;
                var desiredQuantity = Math.Max(
                    directOnly.Count,
                    (int)Math.Ceiling(eligible.Count / (double)coverage));
                var quantity = Math.Min(
                    Math.Min(entry.Value, opportunity.MaximumReleases),
                    Math.Min(eligible.Count, desiredQuantity));
                if (quantity <= 0)
                    continue;

                var directOnlySet = new HashSet<GroundAttackOpportunityTarget>(
                    directOnly);
                var primaryTargets = eligible
                    .OrderByDescending(target => directOnlySet.Contains(target))
                    .ThenByDescending(target => PrimaryAssignmentValue(
                        target,
                        ordnance))
                    .ThenByDescending(target => DirectValue(target, ordnance))
                    .ThenBy(target => target.Target.EntityId)
                    .Take(quantity)
                    .ToList();
                var score = primaryTargets.Sum(target => DirectValue(
                    target,
                    ordnance));
                if (ordnance.MaximumGroundTargetsPerWeapon > 1
                    && ordnance.SecondaryGroundEffectMultiplier > 0f)
                {
                    var primarySet = new HashSet<GroundAttackOpportunityTarget>(
                        primaryTargets);
                    var secondaryCapacity = quantity
                                            * (ordnance.MaximumGroundTargetsPerWeapon - 1);
                    score += eligible
                        .Where(target => !primarySet.Contains(target))
                        .Where(target => target.CanReceiveSecondaryEffect)
                        .Where(target => AirToGroundWeaponRules.CanAffect(
                            ordnance,
                            target.TargetCategory,
                            target.TargetToughness,
                            ordnance.SecondaryGroundEffectMultiplier))
                        .Take(secondaryCapacity)
                        .Sum(target => DirectValue(target, ordnance)
                                       * ordnance.SecondaryGroundEffectMultiplier);
                }

                var candidate = new WeaponPlanCandidate(
                    ordnance,
                    primaryTargets,
                    score);
                var scoresAreEqual = best != null
                                     && Math.Abs(candidate.Score - best.Score)
                                     <= 0.0001f;
                if (best == null
                    || candidate.Score > best.Score + 0.0001f
                    || (scoresAreEqual
                        && candidate.PrimaryTargets.Count
                        < best.PrimaryTargets.Count)
                    || (scoresAreEqual
                        && candidate.PrimaryTargets.Count
                        == best.PrimaryTargets.Count
                        && candidate.Ordnance.OrdnanceTypeDefinitionId
                        .CompareTo(best.Ordnance.OrdnanceTypeDefinitionId) < 0))
                {
                    best = candidate;
                }
            }

            if (best == null)
                return false;

            plan = new GroundAttackPassPlan
            {
                OrdnanceTypeDefinitionId =
                    best.Ordnance.OrdnanceTypeDefinitionId,
                TargetTileId = opportunity.TargetTileId,
                OpportunityDescription = opportunity.Description,
                PrimaryTargets = best.PrimaryTargets
                    .Select(target => target.Clone())
                    .ToList(),
                OpportunityTargets = opportunity.Targets
                    .Where(target => target != null)
                    .Select(target => target.Clone())
                    .ToList()
            };
            return true;
        }

        private static float DirectValue(
            GroundAttackOpportunityTarget target,
            OrdnanceTypeDefinition ordnance)
        {
            return Math.Max(0f, target.MissionPriority)
                   * ordnance.HitProbability
                   * ordnance.GetEffectiveness(target.TargetCategory);
        }

        private static float PrimaryAssignmentValue(
            GroundAttackOpportunityTarget target,
            OrdnanceTypeDefinition ordnance)
        {
            var directValue = DirectValue(target, ordnance);
            if (!target.CanReceiveSecondaryEffect
                || !AirToGroundWeaponRules.CanAffect(
                    ordnance,
                    target.TargetCategory,
                    target.TargetToughness,
                    ordnance.SecondaryGroundEffectMultiplier))
                return directValue;

            return directValue
                   * (1f - ordnance.SecondaryGroundEffectMultiplier);
        }

        private sealed class WeaponPlanCandidate
        {
            public OrdnanceTypeDefinition Ordnance { get; }
            public List<GroundAttackOpportunityTarget> PrimaryTargets { get; }
            public float Score { get; }

            public WeaponPlanCandidate(
                OrdnanceTypeDefinition ordnance,
                List<GroundAttackOpportunityTarget> primaryTargets,
                float score)
            {
                Ordnance = ordnance;
                PrimaryTargets = primaryTargets;
                Score = score;
            }
        }
    }
}
