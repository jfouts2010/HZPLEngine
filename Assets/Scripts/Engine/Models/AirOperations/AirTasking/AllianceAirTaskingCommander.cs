using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Service;
using UnityEngine;
using UnityEngine.Serialization;

namespace Models.Gameplay.Campaign
{
    /// <summary>
    /// Owns committed packages for one alliance. It does not decide which
    /// operations are needed or compose packages from strategic demand.
    /// </summary>
    [Serializable]
    public sealed class AllianceAirTaskingCommander
    {
        public const int MaximumDiagnosticEntries = 256;

        public Alliance Alliance;
        public AllianceAirDoctrine Doctrine = AllianceAirDoctrine.CreateDefault();

        [SerializeField, FormerlySerializedAs("Packages")]
        private List<AirPackage> packages = new List<AirPackage>();
        [SerializeField, FormerlySerializedAs("Diagnostics")]
        private List<AirTaskingDiagnostic> diagnostics =
            new List<AirTaskingDiagnostic>();

        public IReadOnlyList<AirPackage> Packages => packages;
        public IReadOnlyList<AirTaskingDiagnostic> Diagnostics => diagnostics;

        public AllianceAirTaskingCommander()
        {
        }

        public AllianceAirTaskingCommander(
            Alliance alliance,
            AllianceAirDoctrine doctrine)
        {
            Alliance = alliance;
            Doctrine = (doctrine ?? AllianceAirDoctrine.CreateDefault()).Clone();
        }

        public bool TryCommitPackage(
            AirPackage package,
            AircraftReservationService aircraftReservations,
            DateTime currentTime,
            out string reason)
        {
            reason = ValidatePackage(package, currentTime);
            if (!string.IsNullOrEmpty(reason))
                return false;
            if (!TryPlanSupportReservations(
                    package,
                    out var supportReservations,
                    out reason))
                return false;
            if (!aircraftReservations.TryReserve(package, out reason))
                return false;

            var previousDiagnosticCount = diagnostics.Count;
            try
            {
                foreach (var reservation in supportReservations)
                    reservation.Flight.SupportReservations.Add(
                        reservation.Reservation);
                packages.Add(package);
                AddDiagnostic(new AirTaskingDiagnostic
                {
                    RecordedAt = currentTime,
                    PlanId = package.PlanId,
                    PackageId = package.PackageId,
                    Code = "package-committed",
                    Message = "Explicit package plan committed.",
                    Values = new Dictionary<string, float>
                    {
                        { "flightCount", package.Flights.Count },
                        {
                            "aircraftCount",
                            package.Flights.Sum(flight =>
                                flight.AircraftIds.Count)
                        },
                        {
                            "plannedTakeoffLeadMinutes",
                            (float)Math.Max(
                                0d,
                                (package.EarliestTakeoffTime - currentTime)
                                .TotalMinutes)
                        }
                    }
                });
                reason = "Package committed.";
                return true;
            }
            catch
            {
                foreach (var reservation in supportReservations)
                    reservation.Flight.SupportReservations.Remove(
                        reservation.Reservation);
                packages.Remove(package);
                if (diagnostics.Count > previousDiagnosticCount)
                {
                    diagnostics.RemoveRange(
                        previousDiagnosticCount,
                        diagnostics.Count - previousDiagnosticCount);
                }
                aircraftReservations.ReleaseUnlaunched(package);
                throw;
            }
        }

        public bool CancelPackage(
            Guid packageId,
            AircraftReservationService aircraftReservations,
            DateTime currentTime,
            string reason)
        {
            var package = GetPackage(packageId);
            if (package == null
                || package.Flights.All(flight => flight.IsTerminal))
                return false;

            var aborted = package.Flights.Any(flight =>
                flight.LifecycleState == AirTaskingLifecycleState.Aborted);
            foreach (var supportFlight in packages
                         .SelectMany(candidate => candidate.Flights))
            {
                supportFlight.SupportReservations.RemoveAll(reservation =>
                    reservation.ConsumingPackageId == package.PackageId);
            }
            foreach (var provider in package.Flights)
                provider.SupportReservations.Clear();

            foreach (var flight in package.Flights)
            {
                if (flight.IsTerminal)
                    continue;
                var cancellation = flight.Cancel(currentTime, reason);
                if (cancellation == FlightCancellationResult.Aborted)
                    aborted = true;
                if (cancellation == FlightCancellationResult.Cancelled)
                    aircraftReservations.ReleaseFlight(flight);
            }

            AddDiagnostic(new AirTaskingDiagnostic
            {
                RecordedAt = currentTime,
                PlanId = package.PlanId,
                PackageId = package.PackageId,
                Code = aborted ? "package-aborted" : "package-cancelled",
                Message = reason ?? string.Empty
            });
            return true;
        }

        public void ValidatePackageIntegrity(
            AircraftReservationService aircraftReservations,
            DateTime currentTime)
        {
            foreach (var package in packages
                         .Where(candidate => candidate.Flights.Any(flight =>
                             !flight.IsTerminal))
                         .ToList())
            {
                var requiredFlights = package.Flights
                    .Where(flight => flight.IsRequired)
                    .ToList();
                var requiredFlightFailed = requiredFlights.Count == 0
                                           || requiredFlights.Any(flight =>
                                               flight.LifecycleState
                                               == AirTaskingLifecycleState.Cancelled
                                               || flight.LifecycleState
                                               == AirTaskingLifecycleState.Failed
                                               || flight.LifecycleState
                                               == AirTaskingLifecycleState.Aborted);
                if (requiredFlightFailed)
                {
                    CancelPackage(
                        package.PackageId,
                        aircraftReservations,
                        currentTime,
                        "A required package flight was cancelled or became unable to launch.");
                    continue;
                }

                if (package.SupportingFlightIds.Count == 0
                    || package.Flights.Any(flight =>
                        flight.ExecutionPhase
                        != FlightExecutionPhase.AwaitingTakeoff))
                    continue;

                var supportingIds = package.SupportingFlightIds.ToHashSet();
                var supportingFlights = packages
                    .SelectMany(candidate => candidate.Flights)
                    .Where(flight => supportingIds.Contains(flight.FlightId))
                    .ToList();
                var requiredSlots = package.Flights.Sum(flight =>
                    flight.AircraftIds.Count);
                if (supportingFlights.Select(flight => flight.FlightId)
                        .Distinct().Count() == supportingIds.Count
                    && AirSupportCoveragePlanner.HasContinuousReservedCoverage(
                        supportingFlights,
                        package.PackageId,
                        requiredSlots,
                        package.EffectStart,
                        package.SupportWindowEnd))
                    continue;

                CancelPackage(
                    package.PackageId,
                    aircraftReservations,
                    currentTime,
                    "Reserved tanker coverage became unavailable before takeoff.");
            }
        }

        public void AddDiagnostic(AirTaskingDiagnostic diagnostic)
        {
            if (diagnostic == null)
                return;
            diagnostics.Add(diagnostic);
            if (diagnostics.Count > MaximumDiagnosticEntries)
            {
                diagnostics.RemoveRange(
                    0,
                    diagnostics.Count - MaximumDiagnosticEntries);
            }
        }

        public AirPackage GetPackage(Guid packageId)
        {
            return packages.FirstOrDefault(package =>
                package.PackageId == packageId);
        }

        private string ValidatePackage(AirPackage package, DateTime currentTime)
        {
            if (package == null)
                return "A package is required.";
            if (package.PackageId == Guid.Empty
                || packages.Any(candidate =>
                    candidate.PackageId == package.PackageId))
                return "The package has an invalid or duplicate identifier.";
            if (package.PlanId == Guid.Empty)
                return "The package has no source plan identifier.";
            if (package.Alliance != Alliance)
                return "The package belongs to another alliance.";
            if (package.Flights == null || package.Flights.Count == 0)
                return "The package must contain at least one flight.";
            if (package.Flights.Any(flight => flight == null))
                return "The package contains a null flight.";

            foreach (var flight in package.Flights)
            {
                if (!flight.TryValidateRoute(out var routeReason))
                    return routeReason;
                if (flight.PlannedTakeoffTime
                    < package.CreatedAt + AirPackage.PreparationDelay)
                    return "A flight does not satisfy the package preparation delay.";
                if (flight.EffectEnd < flight.EffectStart)
                    return "A flight has an invalid effect window.";
            }

            if (package.EffectEnd < package.EffectStart
                || package.EarliestTakeoffTime < currentTime)
                return "The package has invalid timing.";
            if (package.Flights.Any(flight =>
                    flight.FlightId == Guid.Empty
                    || flight.SquadronId == Guid.Empty
                    || flight.AircraftIds.Count == 0))
                return "The package contains an incomplete flight.";
            if (package.Flights.Select(flight => flight.FlightId)
                    .Distinct().Count() != package.Flights.Count)
                return "The package contains duplicate flight identifiers.";

            var flightIds = package.Flights.Select(flight => flight.FlightId)
                .ToHashSet();
            if (package.Flights.Where(flight => flight.IsEscort).Any(escort =>
                    escort.ProtectedFlightIds.Count == 0
                    || escort.ProtectedFlightIds.Any(id =>
                        id == escort.FlightId || !flightIds.Contains(id))))
                return "The package contains an invalid escort assignment.";

            var aircraftIds = package.Flights
                .SelectMany(flight => flight.AircraftIds).ToList();
            if (aircraftIds.Any(id => id == Guid.Empty)
                || aircraftIds.Distinct().Count() != aircraftIds.Count)
                return "The package contains invalid or duplicate aircraft.";
            if (package.SupportingFlightIds.Any(id => id == Guid.Empty)
                || package.SupportingFlightIds.Distinct().Count()
                != package.SupportingFlightIds.Count)
                return "The package contains invalid support-flight references.";

            return string.Empty;
        }

        private bool TryPlanSupportReservations(
            AirPackage package,
            out List<PlannedSupportReservation> planned,
            out string reason)
        {
            planned = new List<PlannedSupportReservation>();
            reason = string.Empty;
            if (package.SupportingFlightIds.Count == 0)
                return true;

            var requiredSlots = package.Flights.Sum(flight =>
                flight.AircraftIds.Count);
            var supportingIds = package.SupportingFlightIds.ToHashSet();
            var supportingFlights = packages
                .Where(candidate => !candidate.IsTerminal)
                .SelectMany(candidate => candidate.Flights)
                .Where(flight => supportingIds.Contains(flight.FlightId)
                                 && !flight.IsTerminal)
                .GroupBy(flight => flight.FlightId)
                .Select(group => group.First())
                .ToList();
            if (supportingFlights.Count != supportingIds.Count)
            {
                reason = "A named support flight is unavailable.";
                return false;
            }
            if (supportingFlights.Any(flight =>
                    flight.TaskType != AirFlightTaskType.AerialRefueling
                    || package.Flights.Any(receiver =>
                        !flight.MissionArea.Contains(
                            receiver.MissionArea.CenterTileId))))
            {
                reason = "A named tanker does not cover the receiving package.";
                return false;
            }

            var reservations = AirSupportCoveragePlanner.PlanContinuousCoverage(
                supportingFlights,
                package.PackageId,
                requiredSlots,
                package.EffectStart,
                package.SupportWindowEnd,
                out var coveredUntil);
            if (coveredUntil < package.SupportWindowEnd)
            {
                reason = "Named tanker capacity does not cover the package window.";
                return false;
            }

            var flightsById = supportingFlights.ToDictionary(
                flight => flight.FlightId);
            planned.AddRange(reservations.Select(reservation =>
                new PlannedSupportReservation(
                    flightsById[reservation.SupportingFlightId],
                    reservation)));
            return true;
        }

        private sealed class PlannedSupportReservation
        {
            public readonly AirFlight Flight;
            public readonly AirSupportReservation Reservation;

            public PlannedSupportReservation(
                AirFlight flight,
                AirSupportReservation reservation)
            {
                Flight = flight;
                Reservation = reservation;
            }
        }
    }
}
