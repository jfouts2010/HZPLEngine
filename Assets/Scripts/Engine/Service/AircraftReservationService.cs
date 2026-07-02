using System;
using System.Collections.Generic;
using System.Linq;
using Models.Gameplay.Campaign;

namespace Engine.Service
{
    public sealed class AircraftReservationService
    {
        private readonly SquadronSystem squadronSystem;

        public AircraftReservationService(SquadronSystem squadronSystem)
        {
            this.squadronSystem = squadronSystem
                                  ?? throw new ArgumentNullException(nameof(squadronSystem));
        }

        public bool TryReserve(AirPackage package, out string reason)
        {
            reason = string.Empty;
            if (package?.Flights == null || package.Flights.Count == 0)
            {
                reason = "A package with flights is required.";
                return false;
            }

            var assignments = new List<AircraftAssignment>();
            var seenAircraft = new HashSet<Guid>();
            foreach (var flight in package.Flights)
            {
                if (flight == null
                    || !squadronSystem.TryGetSquadron(flight.SquadronId, out var squadron))
                {
                    reason = "A proposed flight references an unavailable squadron.";
                    return false;
                }

                var squadronAircraft = (squadron.Aircraft ?? new List<CampaignAircraft>())
                    .Where(aircraft => aircraft != null)
                    .ToList();
                if (squadronAircraft
                    .GroupBy(aircraft => aircraft.AircraftId)
                    .Any(group => group.Key == Guid.Empty || group.Count() > 1))
                {
                    reason = "A squadron contains invalid or duplicate aircraft identifiers.";
                    return false;
                }

                var aircraftById = squadronAircraft.ToDictionary(
                    aircraft => aircraft.AircraftId);
                foreach (var aircraftId in flight.AircraftIds ?? new List<Guid>())
                {
                    if (!seenAircraft.Add(aircraftId)
                        || !aircraftById.TryGetValue(aircraftId, out var aircraft)
                        || aircraft.Status != CampaignAircraftStatus.Ready
                        || aircraft.AssignedFlightId != Guid.Empty)
                    {
                        reason = "Aircraft availability changed before the package could be committed.";
                        return false;
                    }

                    assignments.Add(new AircraftAssignment(flight, aircraft));
                }
            }

            var reserved = new List<AircraftAssignment>();
            foreach (var assignment in assignments)
            {
                if (assignment.Aircraft.TryAssignToFlight(assignment.Flight.FlightId))
                {
                    reserved.Add(assignment);
                    continue;
                }

                foreach (var completed in reserved)
                    completed.Aircraft.ReleaseFromFlight(completed.Flight.FlightId);
                reason = "Aircraft availability changed before the package could be committed.";
                return false;
            }

            return true;
        }

        public void ReleaseUnlaunched(AirPackage package)
        {
            foreach (var flight in package?.Flights ?? new List<AirFlight>())
            {
                if (flight != null
                    && !flight.IsAirborne)
                    ReleaseFlight(flight);
            }
        }

        public void ReleaseFlight(AirFlight flight)
        {
            if (flight == null
                || !squadronSystem.TryGetSquadron(flight.SquadronId, out var squadron))
                return;

            foreach (var aircraft in squadron.Aircraft ?? new List<CampaignAircraft>())
                aircraft?.ReleaseFromFlight(flight.FlightId);
        }

        private sealed class AircraftAssignment
        {
            public readonly AirFlight Flight;
            public readonly CampaignAircraft Aircraft;

            public AircraftAssignment(AirFlight flight, CampaignAircraft aircraft)
            {
                Flight = flight;
                Aircraft = aircraft;
            }
        }
    }
}
