using System;
using System.Collections.Generic;
using System.Linq;
using Models.Gameplay.Campaign;
using Models.Module;

namespace Engine.Service
{
    internal static class AircraftLoadoutStationPlanner
    {
        public static bool TryFitExact(
            AircraftTypeDefinition aircraftType,
            IReadOnlyDictionary<Guid, int> requestedCounts,
            out List<AircraftLoadoutItem> loadout,
            out string reason)
        {
            loadout = new List<AircraftLoadoutItem>();
            reason = string.Empty;
            if (aircraftType == null || requestedCounts == null)
            {
                reason = "A station loadout requires an aircraft type and requested inventory.";
                return false;
            }

            var remaining = requestedCounts
                .Where(entry => entry.Value > 0)
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            if (requestedCounts.Any(entry =>
                    entry.Key == Guid.Empty || entry.Value < 0))
            {
                reason = "A station loadout request contains invalid ordnance inventory.";
                return false;
            }
            if (remaining.Count == 0)
                return true;

            var configurations = aircraftType.CarriageConfigurations
                .ToDictionary(
                    configuration => configuration
                        .AircraftCarriageConfigurationDefinitionId);
            var stations = OrderForPlanning(aircraftType.LoadoutStations);
            if (!Search(
                    stations,
                    configurations,
                    aircraftType.OrdnanceCapacity,
                    0,
                    0f,
                    remaining,
                    new List<AircraftLoadoutItem>(),
                    out loadout))
            {
                reason =
                    "The requested ordnance cannot be assigned to a legal set of aircraft loadout stations.";
                return false;
            }

            return true;
        }

        internal static IReadOnlyList<AircraftLoadoutStationDefinition>
            OrderForPlanning(
                IReadOnlyList<AircraftLoadoutStationDefinition> stations)
        {
            var byId = stations.ToDictionary(
                station => station.AircraftLoadoutStationDefinitionId);
            var ordered = stations
                .OrderBy(station => station.SortOrder)
                .ThenBy(station => station.AircraftLoadoutStationDefinitionId)
                .ToList();
            var result = new List<AircraftLoadoutStationDefinition>();
            var added = new HashSet<Guid>();
            foreach (var station in ordered)
            {
                if (!added.Add(station.AircraftLoadoutStationDefinitionId))
                    continue;
                result.Add(station);
                if (station.MirrorStationDefinitionId != Guid.Empty
                    && byId.TryGetValue(
                        station.MirrorStationDefinitionId,
                        out var mirror)
                    && added.Add(mirror.AircraftLoadoutStationDefinitionId))
                    result.Add(mirror);
            }

            return result;
        }

        private static bool Search(
            IReadOnlyList<AircraftLoadoutStationDefinition> stations,
            IReadOnlyDictionary<Guid, AircraftCarriageConfigurationDefinition>
                configurations,
            float capacity,
            int stationIndex,
            float usedCapacity,
            IReadOnlyDictionary<Guid, int> remaining,
            IReadOnlyList<AircraftLoadoutItem> current,
            out List<AircraftLoadoutItem> result)
        {
            if (remaining.Values.All(count => count == 0))
            {
                result = current.ToList();
                return true;
            }
            if (stationIndex >= stations.Count)
            {
                result = null;
                return false;
            }

            var station = stations[stationIndex];
            var options = station.CompatibleCarriageConfigurationDefinitionIds
                .Select(id => configurations[id])
                .Where(configuration => ConfigurationFits(
                    configuration,
                    remaining,
                    usedCapacity,
                    capacity))
                .OrderByDescending(configuration => configuration.Contents.Sum(
                    content => content.Count))
                .ThenBy(configuration =>
                    configuration.AircraftCarriageConfigurationDefinitionId)
                .ToList();
            foreach (var configuration in options)
            {
                var nextRemaining = remaining.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value);
                var next = current.ToList();
                foreach (var content in configuration.Contents)
                {
                    nextRemaining[content.OrdnanceTypeDefinitionId] -= content.Count;
                    next.Add(new AircraftLoadoutItem(
                        station.AircraftLoadoutStationDefinitionId,
                        configuration.AircraftCarriageConfigurationDefinitionId,
                        content.OrdnanceTypeDefinitionId,
                        content.Count));
                }

                if (Search(
                        stations,
                        configurations,
                        capacity,
                        stationIndex + 1,
                        usedCapacity + configuration.ExternalLoadCost,
                        nextRemaining,
                        next,
                        out result))
                    return true;
            }

            return Search(
                stations,
                configurations,
                capacity,
                stationIndex + 1,
                usedCapacity,
                remaining,
                current,
                out result);
        }

        private static bool ConfigurationFits(
            AircraftCarriageConfigurationDefinition configuration,
            IReadOnlyDictionary<Guid, int> remaining,
            float usedCapacity,
            float capacity)
        {
            if (usedCapacity + configuration.ExternalLoadCost > capacity)
                return false;

            return configuration.Contents.All(content =>
                remaining.TryGetValue(
                    content.OrdnanceTypeDefinitionId,
                    out var count)
                && count >= content.Count);
        }
    }
}
