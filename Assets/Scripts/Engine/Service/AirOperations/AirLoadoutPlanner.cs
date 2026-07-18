using System;
using System.Collections.Generic;
using System.Linq;
using Models.Gameplay.Campaign;
using Models.Module;

namespace Engine.Service
{
    public sealed class AirLoadoutPlanner
    {
        public const int AirCombatShotBudget = 4;
        public const int MinimumAirCombatShots = 2;

        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes;
        private readonly Func<Alliance, IReadOnlyCollection<Guid>> allowedOrdnanceForAlliance;

        public AirLoadoutPlanner(
            ModuleDefinition module,
            Func<Alliance, IReadOnlyCollection<Guid>> allowedOrdnanceForAlliance)
        {
            ordnanceTypes = module.OrdnanceTypeDefinitions
                .ToDictionary(definition => definition.OrdnanceTypeDefinitionId);
            this.allowedOrdnanceForAlliance = allowedOrdnanceForAlliance;
        }

        public bool TryPlanAirCombatLoadout(
            AircraftTypeDefinition aircraftType,
            Alliance alliance,
            out List<AircraftLoadoutItem> loadout,
            out string reason)
        {
            loadout = new List<AircraftLoadoutItem>();
            reason = string.Empty;

            var compatible = aircraftType.CompatibleOrdnanceTypeDefinitionIds;
            var allowed = new HashSet<Guid>(
                allowedOrdnanceForAlliance(alliance));
            var candidates = compatible
                .Where(allowed.Contains)
                .Select(ordnanceTypeId =>
                    ordnanceTypes.TryGetValue(ordnanceTypeId, out var ordnanceType)
                        ? ordnanceType
                        : null)
                .Where(ordnanceType => ordnanceType != null
                                       && IsAirToAir(ordnanceType)
                                       && ordnanceType.EmploymentCategory
                                       != OrdnanceEmploymentCategory.Gun
                                       && ordnanceType.GetEffectiveness(
                                           OrdnanceTargetCategory.Aircraft) > 0f
                                       && ordnanceType.Weight <= aircraftType.OrdnanceCapacity)
                .OrderByDescending(ordnanceType =>
                    ordnanceType.GetEffectiveness(OrdnanceTargetCategory.Aircraft))
                .ThenBy(ordnanceType => ordnanceType.Weight)
                .ThenBy(ordnanceType => ordnanceType.OrdnanceTypeDefinitionId)
                .ToList();

            var internalGun = GetInternalGun(
                aircraftType,
                allowed);
            if (candidates.Count == 0 && internalGun == null)
            {
                reason = "No allowed compatible air-to-air ordnance is available.";
                return false;
            }

            var best = candidates.Count == 0
                ? null
                : FindBestAirCombatLoadout(candidates, aircraftType.OrdnanceCapacity);
            loadout = best?.CountByOrdnance
                .OrderBy(entry => entry.Key)
                .Select(entry => new AircraftLoadoutItem(entry.Key, entry.Value))
                .ToList()
                ?? new List<AircraftLoadoutItem>();
            if (internalGun != null)
            {
                loadout.Add(new AircraftLoadoutItem(
                    internalGun.OrdnanceTypeDefinitionId,
                    aircraftType.InternalGunBurstCount));
            }

            if (CountMissionUsefulAirCombatShots(loadout) < MinimumAirCombatShots)
            {
                reason = $"At least {MinimumAirCombatShots} air-to-air shots must fit.";
                loadout.Clear();
                return false;
            }

            return true;
        }

        public bool HasMissionUsefulAirCombatOrdnance(CampaignAircraft aircraft)
        {
            return aircraft.Loadout.Any(item =>
                item.Count > 0
                && ordnanceTypes.TryGetValue(item.OrdnanceTypeDefinitionId, out var ordnanceType)
                && IsAirToAir(ordnanceType)
                && ordnanceType.GetEffectiveness(OrdnanceTargetCategory.Aircraft) > 0f);
        }

        public int CountMissionUsefulAirCombatShots(
            IReadOnlyList<AircraftLoadoutItem> loadout)
        {
            return loadout
                .Where(item =>
                    item.Count > 0
                    && ordnanceTypes.TryGetValue(item.OrdnanceTypeDefinitionId, out var ordnanceType)
                    && IsAirToAir(ordnanceType)
                    && ordnanceType.GetEffectiveness(OrdnanceTargetCategory.Aircraft) > 0f)
                .Sum(item => item.Count);
        }

        public bool TryValidateLoadout(
            AircraftTypeDefinition aircraftType,
            Alliance alliance,
            IReadOnlyList<AircraftLoadoutItem> loadout,
            out string reason)
        {
            reason = string.Empty;
            if (loadout == null || loadout.Count == 0)
                return true;

            var compatible = new HashSet<Guid>(
                aircraftType.CompatibleOrdnanceTypeDefinitionIds);
            var allowed = new HashSet<Guid>(
                allowedOrdnanceForAlliance(alliance));
            var totalWeight = 0f;
            var internalGunItems = 0;
            foreach (var item in loadout)
            {
                if (item == null
                    || item.OrdnanceTypeDefinitionId == Guid.Empty
                    || item.Count <= 0)
                {
                    reason = "A planned loadout contains an invalid item.";
                    return false;
                }

                if (!ordnanceTypes.TryGetValue(
                        item.OrdnanceTypeDefinitionId,
                        out var ordnanceType))
                {
                    reason = "A planned loadout references unknown ordnance.";
                    return false;
                }

                if (!compatible.Contains(item.OrdnanceTypeDefinitionId))
                {
                    reason = "A planned loadout contains incompatible ordnance.";
                    return false;
                }

                if (!allowed.Contains(item.OrdnanceTypeDefinitionId))
                {
                    reason = "A planned loadout contains ordnance not allowed for its alliance.";
                    return false;
                }

                var isInternalGun = item.OrdnanceTypeDefinitionId
                                    == aircraftType.InternalGunOrdnanceTypeDefinitionId;
                if (ordnanceType.EmploymentCategory
                    == OrdnanceEmploymentCategory.Gun)
                {
                    if (!isInternalGun)
                    {
                        reason = "A planned loadout contains a gun not installed on its aircraft.";
                        return false;
                    }

                    internalGunItems++;
                    if (item.Count != aircraftType.InternalGunBurstCount)
                    {
                        reason = "A planned loadout has an invalid internal-gun burst count.";
                        return false;
                    }
                    continue;
                }

                if (isInternalGun)
                {
                    reason = "An aircraft internal-gun reference must identify gun ordnance.";
                    return false;
                }

                totalWeight += ordnanceType.Weight * item.Count;
            }

            var requiresInternalGun =
                aircraftType.InternalGunOrdnanceTypeDefinitionId != Guid.Empty
                && aircraftType.InternalGunBurstCount > 0;
            if (internalGunItems != (requiresInternalGun ? 1 : 0))
            {
                reason = requiresInternalGun
                    ? "A planned loadout is missing its aircraft's internal gun."
                    : "A planned loadout contains unexpected internal-gun inventory.";
                return false;
            }

            if (totalWeight > aircraftType.OrdnanceCapacity)
            {
                reason = "A planned loadout exceeds aircraft ordnance capacity.";
                return false;
            }

            return true;
        }

        public static bool IsAirToAir(OrdnanceTypeDefinition ordnanceType)
        {
            return ordnanceType.EmploymentCategory == OrdnanceEmploymentCategory.AirToAirRadar
                   || ordnanceType.EmploymentCategory == OrdnanceEmploymentCategory.AirToAirInfrared
                   || (ordnanceType.EmploymentCategory == OrdnanceEmploymentCategory.Gun
                       && ordnanceType.GetEffectiveness(
                           OrdnanceTargetCategory.Aircraft) > 0f);
        }

        private OrdnanceTypeDefinition GetInternalGun(
            AircraftTypeDefinition aircraftType,
            HashSet<Guid> allowed)
        {
            if (aircraftType.InternalGunOrdnanceTypeDefinitionId == Guid.Empty
                || aircraftType.InternalGunBurstCount <= 0
                || !allowed.Contains(aircraftType.InternalGunOrdnanceTypeDefinitionId)
                || !ordnanceTypes.TryGetValue(
                    aircraftType.InternalGunOrdnanceTypeDefinitionId,
                    out var gun)
                || gun.EmploymentCategory != OrdnanceEmploymentCategory.Gun)
            {
                return null;
            }

            return gun;
        }

        private static PlannedLoadout FindBestAirCombatLoadout(
            IReadOnlyList<OrdnanceTypeDefinition> candidates,
            float capacity)
        {
            var best = new PlannedLoadout();
            Search(candidates, capacity, 0, new PlannedLoadout(), ref best);
            return best.TotalShots == 0 ? null : best;
        }

        private static void Search(
            IReadOnlyList<OrdnanceTypeDefinition> candidates,
            float capacity,
            int index,
            PlannedLoadout current,
            ref PlannedLoadout best)
        {
            if (index >= candidates.Count)
            {
                if (IsBetter(current, best))
                    best = current.Clone();
                return;
            }

            var candidate = candidates[index];
            var remainingShots = AirCombatShotBudget - current.TotalShots;
            var remainingCapacity = capacity - current.TotalWeight;
            var maximumCount = Math.Min(
                remainingShots,
                candidate.Weight <= 0f
                    ? remainingShots
                    : (int)Math.Floor(remainingCapacity / candidate.Weight));
            for (var count = 0; count <= maximumCount; count++)
            {
                var next = current.Clone();
                if (count > 0)
                    next.Add(candidate, count);
                Search(candidates, capacity, index + 1, next, ref best);
            }
        }

        private static bool IsBetter(PlannedLoadout candidate, PlannedLoadout current)
        {
            if (candidate.TotalShots != current.TotalShots)
                return candidate.TotalShots > current.TotalShots;

            var candidatePreferredSplit = Math.Min(2, candidate.RadarShots)
                                          + Math.Min(2, candidate.InfraredShots);
            var currentPreferredSplit = Math.Min(2, current.RadarShots)
                                        + Math.Min(2, current.InfraredShots);
            if (candidatePreferredSplit != currentPreferredSplit)
                return candidatePreferredSplit > currentPreferredSplit;

            if (candidate.HasRadarAndInfrared != current.HasRadarAndInfrared)
                return candidate.HasRadarAndInfrared;

            if (Math.Abs(candidate.EffectivenessTotal - current.EffectivenessTotal) > 0.0001f)
                return candidate.EffectivenessTotal > current.EffectivenessTotal;

            if (Math.Abs(candidate.TotalWeight - current.TotalWeight) > 0.0001f)
                return candidate.TotalWeight < current.TotalWeight;

            return false;
        }

        private sealed class PlannedLoadout
        {
            public readonly Dictionary<Guid, int> CountByOrdnance =
                new Dictionary<Guid, int>();
            public int TotalShots;
            public int RadarShots;
            public int InfraredShots;
            public float TotalWeight;
            public float EffectivenessTotal;
            public bool HasRadarAndInfrared => RadarShots > 0 && InfraredShots > 0;

            public void Add(OrdnanceTypeDefinition ordnanceType, int count)
            {
                CountByOrdnance[ordnanceType.OrdnanceTypeDefinitionId] = count;
                TotalShots += count;
                TotalWeight += ordnanceType.Weight * count;
                EffectivenessTotal += ordnanceType.GetEffectiveness(
                    OrdnanceTargetCategory.Aircraft) * count;
                if (ordnanceType.EmploymentCategory == OrdnanceEmploymentCategory.AirToAirRadar)
                    RadarShots += count;
                if (ordnanceType.EmploymentCategory == OrdnanceEmploymentCategory.AirToAirInfrared)
                    InfraredShots += count;
            }

            public PlannedLoadout Clone()
            {
                var clone = new PlannedLoadout
                {
                    TotalShots = TotalShots,
                    RadarShots = RadarShots,
                    InfraredShots = InfraredShots,
                    TotalWeight = TotalWeight,
                    EffectivenessTotal = EffectivenessTotal
                };
                foreach (var entry in CountByOrdnance)
                    clone.CountByOrdnance[entry.Key] = entry.Value;
                return clone;
            }
        }
    }
}
