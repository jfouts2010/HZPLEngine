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
                var coverage = ordnance.SecondaryGroundEffectMultiplier > 0f
                    ? ordnance.MaximumGroundTargetsPerWeapon
                    : 1;
                var directOnly = eligible
                    .Where(target => !AirToGroundWeaponRules.CanAffect(
                        ordnance,
                        target.TargetCategory,
                        target.TargetToughness,
                        ordnance.SecondaryGroundEffectMultiplier))
                    .ToList();
                var desiredQuantity = Math.Max(
                    directOnly.Count,
                    (int)Math.Ceiling(eligible.Count / (double)coverage));
                var quantity = Math.Min(
                    Math.Min(entry.Value, opportunity.MaximumReleases),
                    Math.Min(eligible.Count, desiredQuantity));
                if (quantity <= 0)
                    continue;

                var primaryTargets = eligible
                    .OrderByDescending(target => PrimaryAssignmentValue(
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
            if (!AirToGroundWeaponRules.CanAffect(
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
