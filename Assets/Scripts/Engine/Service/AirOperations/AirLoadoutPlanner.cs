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

            var allowed = new HashSet<Guid>(
                allowedOrdnanceForAlliance(alliance));
            var hasExternalCandidate = aircraftType.CarriageConfigurations.Any(
                configuration => IsAirCombatConfiguration(configuration, allowed));

            var internalGun = GetInternalGun(
                aircraftType,
                allowed);
            if (!hasExternalCandidate && internalGun == null)
            {
                reason = "No allowed compatible air-to-air ordnance is available.";
                return false;
            }

            var best = !hasExternalCandidate
                ? null
                : FindBestAirCombatLoadout(aircraftType, allowed);
            loadout = best?.Items
                .Select(item => new AircraftLoadoutItem(
                    item.AircraftLoadoutStationDefinitionId,
                    item.AircraftCarriageConfigurationDefinitionId,
                    item.OrdnanceTypeDefinitionId,
                    item.Count))
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

            var allowed = new HashSet<Guid>(
                allowedOrdnanceForAlliance(alliance));
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
                    if (item.AircraftLoadoutStationDefinitionId != Guid.Empty
                        || item.AircraftCarriageConfigurationDefinitionId
                        != Guid.Empty)
                    {
                        reason = "Internal-gun inventory cannot occupy an external loadout station.";
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

                if (item.AircraftLoadoutStationDefinitionId == Guid.Empty
                    || item.AircraftCarriageConfigurationDefinitionId
                    == Guid.Empty)
                {
                    reason = "External ordnance must identify its loadout station and carriage configuration.";
                    return false;
                }
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

            var stations = aircraftType.LoadoutStations.ToDictionary(
                station => station.AircraftLoadoutStationDefinitionId);
            var configurations = aircraftType.CarriageConfigurations.ToDictionary(
                configuration => configuration
                    .AircraftCarriageConfigurationDefinitionId);
            var totalLoadCost = 0f;
            foreach (var stationLoad in loadout
                         .Where(item => item.AircraftLoadoutStationDefinitionId
                                        != Guid.Empty)
                         .GroupBy(item => item.AircraftLoadoutStationDefinitionId))
            {
                if (!stations.TryGetValue(stationLoad.Key, out var station))
                {
                    reason = "A planned loadout references an unknown aircraft station.";
                    return false;
                }

                var configurationIds = stationLoad
                    .Select(item => item.AircraftCarriageConfigurationDefinitionId)
                    .Distinct()
                    .ToList();
                if (configurationIds.Count != 1
                    || !configurations.TryGetValue(
                        configurationIds[0],
                        out var configuration)
                    || !station.CompatibleCarriageConfigurationDefinitionIds.Contains(
                        configurationIds[0]))
                {
                    reason = "A planned loadout uses an invalid carriage configuration for its station.";
                    return false;
                }

                var actualContents = stationLoad
                    .GroupBy(item => item.OrdnanceTypeDefinitionId)
                    .ToDictionary(group => group.Key, group => group.Sum(item => item.Count));
                var expectedContents = configuration.Contents.ToDictionary(
                    content => content.OrdnanceTypeDefinitionId,
                    content => content.Count);
                if (actualContents.Count != expectedContents.Count
                    || actualContents.Any(entry =>
                        !expectedContents.TryGetValue(entry.Key, out var expected)
                        || entry.Value != expected))
                {
                    reason = "A planned station load does not match its carriage configuration contents.";
                    return false;
                }

                totalLoadCost += configuration.ExternalLoadCost;
            }

            if (totalLoadCost > aircraftType.OrdnanceCapacity)
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

        private bool IsAirCombatConfiguration(
            AircraftCarriageConfigurationDefinition configuration,
            ISet<Guid> allowed)
        {
            return configuration.Contents.All(content =>
                allowed.Contains(content.OrdnanceTypeDefinitionId)
                && ordnanceTypes.TryGetValue(
                    content.OrdnanceTypeDefinitionId,
                    out var ordnance)
                && ordnance.EmploymentCategory != OrdnanceEmploymentCategory.Gun
                && IsAirToAir(ordnance)
                && ordnance.GetEffectiveness(
                    OrdnanceTargetCategory.Aircraft) > 0f);
        }

        private PlannedLoadout FindBestAirCombatLoadout(
            AircraftTypeDefinition aircraftType,
            ISet<Guid> allowed)
        {
            var configurations = aircraftType.CarriageConfigurations.ToDictionary(
                configuration => configuration
                    .AircraftCarriageConfigurationDefinitionId);
            var stations = AircraftLoadoutStationPlanner.OrderForPlanning(
                aircraftType.LoadoutStations);
            var best = new PlannedLoadout();
            Search(
                stations,
                configurations,
                allowed,
                aircraftType.OrdnanceCapacity,
                0,
                new PlannedLoadout(),
                ref best);
            return best.TotalShots == 0 ? null : best;
        }

        private void Search(
            IReadOnlyList<AircraftLoadoutStationDefinition> stations,
            IReadOnlyDictionary<Guid, AircraftCarriageConfigurationDefinition>
                configurations,
            ISet<Guid> allowed,
            float capacity,
            int index,
            PlannedLoadout current,
            ref PlannedLoadout best)
        {
            if (index >= stations.Count)
            {
                if (IsBetter(current, best, stations))
                    best = current.Clone();
                return;
            }

            Search(
                stations,
                configurations,
                allowed,
                capacity,
                index + 1,
                current,
                ref best);

            var station = stations[index];
            foreach (var configuration in station
                         .CompatibleCarriageConfigurationDefinitionIds
                         .Select(id => configurations[id])
                         .Where(configuration =>
                             IsAirCombatConfiguration(configuration, allowed))
                         .OrderBy(configuration =>
                             configuration.AircraftCarriageConfigurationDefinitionId))
            {
                var configurationShots = configuration.Contents.Sum(
                    content => content.Count);
                if (current.TotalShots + configurationShots
                    > AirCombatShotBudget
                    || current.TotalWeight + configuration.ExternalLoadCost
                    > capacity)
                    continue;

                var next = current.Clone();
                next.Add(station, configuration, ordnanceTypes);
                Search(
                    stations,
                    configurations,
                    allowed,
                    capacity,
                    index + 1,
                    next,
                    ref best);
            }
        }

        private static bool IsBetter(
            PlannedLoadout candidate,
            PlannedLoadout current,
            IReadOnlyList<AircraftLoadoutStationDefinition> stations)
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

            var candidateSymmetry = SymmetryScore(candidate, stations);
            var currentSymmetry = SymmetryScore(current, stations);
            if (candidateSymmetry != currentSymmetry)
                return candidateSymmetry > currentSymmetry;

            if (Math.Abs(candidate.EffectivenessTotal - current.EffectivenessTotal) > 0.0001f)
                return candidate.EffectivenessTotal > current.EffectivenessTotal;

            if (Math.Abs(candidate.TotalWeight - current.TotalWeight) > 0.0001f)
                return candidate.TotalWeight < current.TotalWeight;

            return false;
        }

        private static int SymmetryScore(
            PlannedLoadout loadout,
            IReadOnlyList<AircraftLoadoutStationDefinition> stations)
        {
            var configurationByStation = loadout.Items
                .GroupBy(item => item.AircraftLoadoutStationDefinitionId)
                .ToDictionary(
                    group => group.Key,
                    group => group.First()
                        .AircraftCarriageConfigurationDefinitionId);
            var score = 0;
            foreach (var station in stations.Where(station =>
                         station.MirrorStationDefinitionId != Guid.Empty
                         && station.AircraftLoadoutStationDefinitionId
                         .CompareTo(station.MirrorStationDefinitionId) < 0))
            {
                var hasStation = configurationByStation.TryGetValue(
                    station.AircraftLoadoutStationDefinitionId,
                    out var configuration);
                var hasMirror = configurationByStation.TryGetValue(
                    station.MirrorStationDefinitionId,
                    out var mirrorConfiguration);
                if (hasStation && hasMirror)
                    score += configuration == mirrorConfiguration ? 2 : 1;
                else if (hasStation || hasMirror)
                    score--;
            }

            return score;
        }

        private sealed class PlannedLoadout
        {
            public readonly List<AircraftLoadoutItem> Items =
                new List<AircraftLoadoutItem>();
            public int TotalShots;
            public int RadarShots;
            public int InfraredShots;
            public float TotalWeight;
            public float EffectivenessTotal;
            public bool HasRadarAndInfrared => RadarShots > 0 && InfraredShots > 0;

            public void Add(
                AircraftLoadoutStationDefinition station,
                AircraftCarriageConfigurationDefinition configuration,
                IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
            {
                foreach (var content in configuration.Contents)
                {
                    var ordnanceType =
                        ordnanceTypes[content.OrdnanceTypeDefinitionId];
                    Items.Add(new AircraftLoadoutItem(
                        station.AircraftLoadoutStationDefinitionId,
                        configuration.AircraftCarriageConfigurationDefinitionId,
                        content.OrdnanceTypeDefinitionId,
                        content.Count));
                    TotalShots += content.Count;
                    EffectivenessTotal += ordnanceType.GetEffectiveness(
                        OrdnanceTargetCategory.Aircraft) * content.Count;
                    if (ordnanceType.EmploymentCategory
                        == OrdnanceEmploymentCategory.AirToAirRadar)
                        RadarShots += content.Count;
                    if (ordnanceType.EmploymentCategory
                        == OrdnanceEmploymentCategory.AirToAirInfrared)
                        InfraredShots += content.Count;
                }
                TotalWeight += configuration.ExternalLoadCost;
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
                clone.Items.AddRange(Items.Select(item => new AircraftLoadoutItem(
                    item.AircraftLoadoutStationDefinitionId,
                    item.AircraftCarriageConfigurationDefinitionId,
                    item.OrdnanceTypeDefinitionId,
                    item.Count)));
                return clone;
            }
        }
    }
}
