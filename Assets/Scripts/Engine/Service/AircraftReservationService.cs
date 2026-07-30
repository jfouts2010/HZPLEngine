using System;
using System.Collections.Generic;
using System.Linq;
using Models.Gameplay.Campaign;
using Models.Module;

namespace Engine.Service
{
    public sealed class AircraftReservationService
    {
        private readonly SquadronSystem squadronSystem;
        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypes;
        private readonly Func<Guid, Alliance> getCountryAlliance;
        private readonly AirLoadoutPlanner loadoutPlanner;

        public AircraftReservationService(
            SquadronSystem _squadronSystem,
            ModuleDefinition module,
            Func<Guid, Alliance> getCountryAlliance,
            Func<Alliance, IReadOnlyCollection<Guid>> allowedOrdnanceForAlliance)
        {
            squadronSystem = _squadronSystem;
            aircraftTypes = module.AircraftTypeDefinitions
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            this.getCountryAlliance = getCountryAlliance;
            loadoutPlanner = new AirLoadoutPlanner(
                module,
                allowedOrdnanceForAlliance);
        }

        public bool TryReserve(AirPackage package, out string reason)
        {
            reason = string.Empty;
            if (package.Flights.Count == 0)
            {
                reason = "A package with flights is required.";
                return false;
            }

            var assignments = new List<AircraftAssignment>();
            var seenAircraft = new HashSet<Guid>();
            foreach (var flight in package.Flights)
            {
                if (!squadronSystem.TryGetSquadron(flight.SquadronId, out var squadron))
                {
                    reason = "A proposed flight references an unavailable squadron.";
                    return false;
                }
                if (!aircraftTypes.TryGetValue(
                        squadron.AircraftTypeDefinitionId,
                        out var aircraftType))
                {
                    reason = "A proposed flight references an unavailable aircraft type.";
                    return false;
                }

                var squadronAircraft = squadron.Aircraft;
                if (squadronAircraft
                    .GroupBy(aircraft => aircraft.AircraftId)
                    .Any(group => group.Key == Guid.Empty || group.Count() > 1))
                {
                    reason = "A squadron contains invalid or duplicate aircraft identifiers.";
                    return false;
                }

                var aircraftById = squadronAircraft.ToDictionary(
                    aircraft => aircraft.AircraftId);
                var plannedLoadoutGroups = flight.PlannedAircraftLoadouts
                    .GroupBy(loadout => loadout.AircraftId)
                    .ToList();
                if (plannedLoadoutGroups.Any(group => group.Count() > 1))
                {
                    reason = "A proposed flight has duplicate planned loadouts.";
                    return false;
                }

                var plannedLoadoutsByAircraftId = plannedLoadoutGroups
                    .ToDictionary(group => group.Key, group => group.First());
                var flightAircraftIds = flight.AircraftIds.ToHashSet();
                var alliance = getCountryAlliance(squadron.CountryId);
                if (plannedLoadoutsByAircraftId.Any(entry =>
                        entry.Key == Guid.Empty
                        || !flightAircraftIds.Contains(entry.Key)
                        || entry.Value == null
                        || entry.Value.Loadout.Any(item =>
                            item.OrdnanceTypeDefinitionId == Guid.Empty
                            || item.Count <= 0)))
                {
                    reason = "A proposed flight has an invalid planned loadout.";
                    return false;
                }

                foreach (var aircraftId in flight.AircraftIds)
                {
                    if (!seenAircraft.Add(aircraftId)
                        || !aircraftById.TryGetValue(aircraftId, out var aircraft)
                        || aircraft.Status != CampaignAircraftStatus.Ready
                        || aircraft.AssignedFlightId != Guid.Empty)
                    {
                        reason = "Aircraft availability changed before the package could be committed.";
                        return false;
                    }

                    plannedLoadoutsByAircraftId.TryGetValue(
                        aircraftId,
                        out var plannedLoadout);
                    if (plannedLoadout == null
                        && IsTimeBasedAirCombatFlight(flight))
                    {
                        reason = "A time-based air-combat flight requires planned loadouts.";
                        return false;
                    }

                    if (plannedLoadout != null
                        && !loadoutPlanner.TryValidateLoadout(
                            aircraftType,
                            alliance,
                            plannedLoadout.Loadout,
                            out reason))
                    {
                        return false;
                    }

                    if (IsTimeBasedAirCombatFlight(flight)
                        && loadoutPlanner.CountMissionUsefulAirCombatShots(
                            plannedLoadout.Loadout)
                        < AirLoadoutPlanner.MinimumAirCombatShots)
                    {
                        reason = $"A time-based air-combat flight requires at least "
                                 + $"{AirLoadoutPlanner.MinimumAirCombatShots} "
                                 + "mission-useful air-to-air shots per aircraft.";
                        return false;
                    }

                    assignments.Add(new AircraftAssignment(
                        flight,
                        aircraft,
                        plannedLoadout == null
                            ? Array.Empty<AircraftLoadoutItem>()
                            : plannedLoadout.Loadout));
                }
            }

            var reserved = new List<AircraftAssignment>();
            foreach (var assignment in assignments)
            {
                if (assignment.Aircraft.TryAssignToFlight(assignment.Flight.FlightId))
                {
                    assignment.Aircraft.SetLoadout(assignment.Loadout);
                    reserved.Add(assignment);
                    continue;
                }

                foreach (var completed in reserved)
                {
                    completed.Aircraft.ClearLoadout();
                    completed.Aircraft.ReleaseFromFlight(completed.Flight.FlightId);
                }
                reason = "Aircraft availability changed before the package could be committed.";
                return false;
            }

            return true;
        }

        public void ReleaseUnlaunched(AirPackage package)
        {
            foreach (var flight in package.Flights)
            {
                if (!flight.IsAirborne)
                    ReleaseFlight(flight);
            }
        }

        public void ReleaseFlight(AirFlight flight)
        {
            if (!squadronSystem.TryGetSquadron(flight.SquadronId, out var squadron))
            {
                throw new InvalidOperationException(
                    $"Flight {flight.FlightId} references missing squadron "
                    + $"{flight.SquadronId}.");
            }

            foreach (var aircraft in squadron.Aircraft)
            {
                if (aircraft.AssignedFlightId == flight.FlightId)
                    aircraft.ClearLoadout();
                aircraft.ReleaseFromFlight(flight.FlightId);
            }
        }

        private static bool IsTimeBasedAirCombatFlight(AirFlight flight)
        {
            return flight != null
                   && (flight.IsFighterEscort
                       || flight.MissionType
                       == AirMissionRequestType.BarrierCombatAirPatrol
                       || flight.MissionType
                       == AirMissionRequestType.OffensiveCounterAirSweep);
        }

        private sealed class AircraftAssignment
        {
            public readonly AirFlight Flight;
            public readonly CampaignAircraft Aircraft;
            public readonly IReadOnlyList<AircraftLoadoutItem> Loadout;

            public AircraftAssignment(
                AirFlight flight,
                CampaignAircraft aircraft,
                IReadOnlyList<AircraftLoadoutItem> loadout)
            {
                Flight = flight;
                Aircraft = aircraft;
                Loadout = loadout;
            }
        }
    }
}
