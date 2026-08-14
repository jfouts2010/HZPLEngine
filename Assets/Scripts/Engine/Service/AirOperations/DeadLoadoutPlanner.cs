using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
using Models.Gameplay.Campaign;
using Models.Module;

namespace Engine.Service
{
    public static class AirToGroundWeaponRules
    {
        public static bool IsAirToGround(OrdnanceTypeDefinition ordnance)
        {
            return ordnance != null
                   && (ordnance.EmploymentCategory
                       == OrdnanceEmploymentCategory.AntiRadiation
                       || ordnance.EmploymentCategory
                       == OrdnanceEmploymentCategory.AirToGroundPrecision
                       || ordnance.EmploymentCategory
                       == OrdnanceEmploymentCategory.AirToGroundUnguided
                       || ordnance.EmploymentCategory
                       == OrdnanceEmploymentCategory.Gun);
        }

        public static bool CanAffect(
            OrdnanceTypeDefinition ordnance,
            OrdnanceTargetCategory targetCategory,
            int targetToughness,
            float effectMultiplier = 1f)
        {
            if (!IsAirToGround(ordnance)
                || effectMultiplier <= 0f
                || ordnance.EffectPower * effectMultiplier
                < Math.Max(1, targetToughness)
                || ordnance.GetEffectiveness(targetCategory) <= 0f)
                return false;

            var isAntiRadiation =
                ordnance.EmploymentCategory
                == OrdnanceEmploymentCategory.AntiRadiation
                || ordnance.GuidanceMode == OrdnanceGuidanceMode.AntiRadiation;
            return !isAntiRadiation
                   || targetCategory == OrdnanceTargetCategory.Radar;
        }
    }

    public sealed class DeadLoadoutPlanner
    {
        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition>
            ordnanceTypes;
        private readonly IReadOnlyDictionary<Guid, AirDefenseComponentDefinition>
            componentDefinitions;
        private readonly Func<Alliance, IReadOnlyCollection<Guid>>
            allowedOrdnanceForAlliance;

        public DeadLoadoutPlanner(
            ModuleDefinition module,
            Func<Alliance, IReadOnlyCollection<Guid>> allowedOrdnanceForAlliance)
        {
            ordnanceTypes = module.OrdnanceTypeDefinitions
                .ToDictionary(definition => definition.OrdnanceTypeDefinitionId);
            componentDefinitions = module.SamComponentDefinitions
                .ToDictionary(definition => definition.SamComponentDefinitionId);
            this.allowedOrdnanceForAlliance = allowedOrdnanceForAlliance
                                              ?? (_ => Array.Empty<Guid>());
        }

        public bool TryPlan(
            AircraftTypeDefinition aircraftType,
            Alliance alliance,
            IReadOnlyList<AirDefenseComponentIntelligenceReport> components,
            out DeadAircraftLoadoutPlan plan,
            out string reason,
            bool requireSelfDefense = true)
        {
            plan = null;
            reason = string.Empty;
            if (aircraftType == null || components == null)
            {
                reason = "A DEAD loadout requires an aircraft type and target components.";
                return false;
            }

            var allowed = new HashSet<Guid>(
                allowedOrdnanceForAlliance(alliance));
            var targetDefinitions = components
                .Where(component => component != null && !component.IsDamaged)
                .Select(component => componentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                    ? new DeadTarget(component.ComponentId, definition)
                    : null)
                .Where(target => target != null)
                .OrderBy(target => GetTargetPriority(target.Definition))
                .ThenBy(target => target.ComponentId)
                .ToList();
            var minimumTargets = targetDefinitions
                .Where(target => target.Definition
                    is RadarAirDefenseComponentDefinition
                    {
                        ProvidesWeaponQualityTrack: true
                    })
                .ToList();
            if (minimumTargets.Count == 0)
            {
                reason = "The known site has no functioning weapon-quality radar target.";
                return false;
            }

            var compatible = aircraftType.CompatibleOrdnanceTypeDefinitionIds
                .Where(allowed.Contains)
                .Select(id => ordnanceTypes.TryGetValue(id, out var ordnance)
                    ? ordnance
                    : null)
                .Where(ordnance => ordnance != null)
                .ToList();
            var counts = new Dictionary<Guid, int>();

            var internalGun = compatible.FirstOrDefault(ordnance =>
                ordnance.OrdnanceTypeDefinitionId
                == aircraftType.InternalGunOrdnanceTypeDefinitionId
                && ordnance.EmploymentCategory == OrdnanceEmploymentCategory.Gun);
            var selfDefenseShots = 0;
            var airToAirCandidates = compatible
                .Where(ordnance => ordnance.EmploymentCategory
                                   != OrdnanceEmploymentCategory.Gun
                                   && AirLoadoutPlanner.IsAirToAir(ordnance)
                                   && ordnance.GetEffectiveness(
                                       OrdnanceTargetCategory.Aircraft) > 0f)
                .OrderByDescending(ordnance => ordnance.GetEffectiveness(
                    OrdnanceTargetCategory.Aircraft))
                .ThenBy(ordnance => ordnance.Weight)
                .ThenBy(ordnance => ordnance.OrdnanceTypeDefinitionId)
                .ToList();
            if (requireSelfDefense)
            {
                AddSelfDefenseStores(
                    aircraftType,
                    counts,
                    airToAirCandidates,
                    ref selfDefenseShots);
                if (selfDefenseShots < AirLoadoutPlanner.MinimumAirCombatShots
                    && internalGun != null
                    && internalGun.GetEffectiveness(
                        OrdnanceTargetCategory.Aircraft) > 0f)
                {
                    selfDefenseShots += aircraftType.InternalGunBurstCount;
                }
                if (selfDefenseShots < AirLoadoutPlanner.MinimumAirCombatShots)
                {
                    reason = "The aircraft cannot carry the minimum self-defense allowance.";
                    return false;
                }
            }

            var minimumEffectStores = 0;
            var cleanupStores = 0;
            var attackSequence = minimumTargets
                .Select(target => (Target: target, Required: true))
                .Concat(minimumTargets.Select(target =>
                    (Target: target, Required: true)))
                .Concat(targetDefinitions
                    .Where(target => !minimumTargets.Contains(target))
                    .Select(target => (Target: target, Required: false)))
                .ToList();
            foreach (var attack in attackSequence)
            {
                var selected = SelectGroundStore(
                    aircraftType,
                    counts,
                    compatible,
                    attack.Target.Definition);
                if (selected == null)
                    continue;

                Add(counts, selected.OrdnanceTypeDefinitionId, 1);
                if (attack.Required)
                    minimumEffectStores++;
                else
                    cleanupStores++;
            }

            if (minimumEffectStores == 0)
            {
                reason = "No target-suitable ground store fits after self-defense.";
                return false;
            }

            if (!requireSelfDefense)
            {
                // An attached escort carries the package's air-to-air burden.
                // DEAD effect therefore receives first claim on capacity, while
                // any remaining capacity may still provide organic protection.
                AddSelfDefenseStores(
                    aircraftType,
                    counts,
                    airToAirCandidates,
                    ref selfDefenseShots);
            }

            if (!AircraftLoadoutStationPlanner.TryFitExact(
                    aircraftType,
                    counts,
                    out var loadout,
                    out var stationReason))
            {
                reason = stationReason;
                return false;
            }

            if (internalGun != null && aircraftType.InternalGunBurstCount > 0)
            {
                loadout.Add(new AircraftLoadoutItem(
                    internalGun.OrdnanceTypeDefinitionId,
                    aircraftType.InternalGunBurstCount));
            }
            var actualSelfDefenseShots = loadout
                .Where(item => item.Count > 0
                               && ordnanceTypes.TryGetValue(
                                   item.OrdnanceTypeDefinitionId,
                                   out var ordnance)
                               && AirLoadoutPlanner.IsAirToAir(ordnance)
                               && ordnance.GetEffectiveness(
                                   OrdnanceTargetCategory.Aircraft) > 0f)
                .Sum(item => item.Count);
            plan = new DeadAircraftLoadoutPlan(
                loadout,
                minimumEffectStores,
                cleanupStores,
                actualSelfDefenseShots);
            return true;
        }

        private static void AddSelfDefenseStores(
            AircraftTypeDefinition aircraftType,
            Dictionary<Guid, int> counts,
            IReadOnlyList<OrdnanceTypeDefinition> candidates,
            ref int selfDefenseShots)
        {
            while (selfDefenseShots < AirLoadoutPlanner.MinimumAirCombatShots)
            {
                var selected = candidates.FirstOrDefault(
                    ordnance => CanFitWithAdditionalStore(
                        aircraftType,
                        counts,
                        ordnance.OrdnanceTypeDefinitionId));
                if (selected == null)
                    return;

                Add(counts, selected.OrdnanceTypeDefinitionId, 1);
                selfDefenseShots++;
            }
        }

        private OrdnanceTypeDefinition SelectGroundStore(
            AircraftTypeDefinition aircraftType,
            IReadOnlyDictionary<Guid, int> counts,
            IReadOnlyList<OrdnanceTypeDefinition> compatible,
            AirDefenseComponentDefinition target)
        {
            return compatible
                .Where(ordnance => ordnance.EmploymentCategory
                                   != OrdnanceEmploymentCategory.Gun
                                   && CanFitWithAdditionalStore(
                                       aircraftType,
                                       counts,
                                       ordnance.OrdnanceTypeDefinitionId)
                                   && CanAttackComponent(ordnance, target))
                .OrderByDescending(ordnance =>
                    target.TargetCategory == OrdnanceTargetCategory.Radar
                    && ordnance.EmploymentCategory
                    == OrdnanceEmploymentCategory.AntiRadiation)
                .ThenByDescending(ordnance => ordnance.MaximumRangeKm)
                .ThenByDescending(ordnance => ordnance.GetEffectiveness(
                    target.TargetCategory))
                .ThenBy(ordnance => ordnance.Weight)
                .ThenBy(ordnance => ordnance.OrdnanceTypeDefinitionId)
                .FirstOrDefault();
        }

        private static bool CanFitWithAdditionalStore(
            AircraftTypeDefinition aircraftType,
            IReadOnlyDictionary<Guid, int> counts,
            Guid ordnanceTypeDefinitionId)
        {
            var candidateCounts = counts.ToDictionary(
                entry => entry.Key,
                entry => entry.Value);
            Add(candidateCounts, ordnanceTypeDefinitionId, 1);
            return AircraftLoadoutStationPlanner.TryFitExact(
                aircraftType,
                candidateCounts,
                out _,
                out _);
        }

        private static int GetTargetPriority(
            AirDefenseComponentDefinition definition)
        {
            return definition switch
            {
                RadarAirDefenseComponentDefinition
                {
                    ProvidesWeaponQualityTrack: true
                } => 0,
                LauncherAirDefenseComponentDefinition => 1,
                RadarAirDefenseComponentDefinition => 2,
                CommandAirDefenseComponentDefinition => 3,
                _ => 4
            };
        }

        public static bool IsAirToGround(OrdnanceTypeDefinition ordnance)
        {
            return AirToGroundWeaponRules.IsAirToGround(ordnance);
        }

        public static bool CanAttackComponent(
            OrdnanceTypeDefinition ordnance,
            AirDefenseComponentDefinition target)
        {
            if (target == null
                || !AirToGroundWeaponRules.CanAffect(
                    ordnance,
                    target.TargetCategory,
                    target.TargetToughness))
                return false;
            return true;
        }

        private static void Add(
            IDictionary<Guid, int> counts,
            Guid ordnanceTypeId,
            int count)
        {
            counts.TryGetValue(ordnanceTypeId, out var existing);
            counts[ordnanceTypeId] = existing + count;
        }

        private sealed class DeadTarget
        {
            public Guid ComponentId { get; }
            public AirDefenseComponentDefinition Definition { get; }

            public DeadTarget(
                Guid componentId,
                AirDefenseComponentDefinition definition)
            {
                ComponentId = componentId;
                Definition = definition;
            }
        }
    }

    public sealed class DeadAircraftLoadoutPlan
    {
        public IReadOnlyList<AircraftLoadoutItem> Loadout { get; }
        public int MinimumEffectStoreCount { get; }
        public int CleanupStoreCount { get; }
        public int SelfDefenseShotCount { get; }

        public DeadAircraftLoadoutPlan(
            IReadOnlyList<AircraftLoadoutItem> loadout,
            int minimumEffectStoreCount,
            int cleanupStoreCount,
            int selfDefenseShotCount)
        {
            Loadout = loadout ?? Array.Empty<AircraftLoadoutItem>();
            MinimumEffectStoreCount = Math.Max(0, minimumEffectStoreCount);
            CleanupStoreCount = Math.Max(0, cleanupStoreCount);
            SelfDefenseShotCount = Math.Max(0, selfDefenseShotCount);
        }
    }
}
