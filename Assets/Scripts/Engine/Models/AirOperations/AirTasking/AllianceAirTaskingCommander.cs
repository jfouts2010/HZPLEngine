using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Service;
using UnityEngine;
using UnityEngine.Serialization;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AllianceAirTaskingCommander
    {
        public const int MaximumDiagnosticEntries = 256;
        public const int MaximumHistoryEntries = 256;
        public const int MaximumSupportDemandSamples = 256;

        public Alliance Alliance;
        public AllianceAirDoctrine Doctrine = AllianceAirDoctrine.CreateDefault();
        public int PlanningCycle;

        [SerializeField, FormerlySerializedAs("MissionRequests")]
        private List<AirMissionRequest> missionRequests =
            new List<AirMissionRequest>();
        [SerializeField, FormerlySerializedAs("Packages")]
        private List<AirPackage> packages = new List<AirPackage>();
        [SerializeField, FormerlySerializedAs("SupportDemandHistory")]
        private List<SupportDemandSample> supportDemandHistory =
            new List<SupportDemandSample>();
        [SerializeField, FormerlySerializedAs("Diagnostics")]
        private List<AirTaskingDiagnostic> diagnostics =
            new List<AirTaskingDiagnostic>();
        [SerializeField, FormerlySerializedAs("History")]
        private List<AirTaskingHistoryEntry> history =
            new List<AirTaskingHistoryEntry>();

        public IReadOnlyList<AirMissionRequest> MissionRequests => missionRequests;
        public IReadOnlyList<AirPackage> Packages => packages;
        public IReadOnlyList<SupportDemandSample> SupportDemandHistory => supportDemandHistory;
        public IReadOnlyList<AirTaskingDiagnostic> Diagnostics => diagnostics;
        public IReadOnlyList<AirTaskingHistoryEntry> History => history;

        public AllianceAirTaskingCommander()
        {
        }

        public AllianceAirTaskingCommander(Alliance alliance, AllianceAirDoctrine doctrine)
        {
            Alliance = alliance;
            Doctrine = doctrine?.Clone() ?? AllianceAirDoctrine.CreateDefault();
        }

        public void BeginPlanningCycle(DateTime currentTime)
        {
            EnsureCollections();
            PlanningCycle++;

            var retainedRequests = new List<AirMissionRequest>();
            foreach (var request in missionRequests.Where(request => request != null))
            {
                var linkedPackages = packages
                    .Where(package => package != null
                                      && package.MissionRequestId == request.MissionRequestId)
                    .ToList();
                if (linkedPackages.Any(package => !package.IsTerminal))
                {
                    retainedRequests.Add(request);
                    continue;
                }

                if (linkedPackages.Count > 0)
                {
                    AddHistory(new AirTaskingHistoryEntry
                    {
                        RecordedAt = currentTime,
                        MissionRequestId = request.MissionRequestId,
                        RequestType = request.RequestType,
                        RequestState = request.State,
                        PackageIds = linkedPackages.Select(package => package.PackageId).ToList(),
                        RequestSnapshot = request,
                        PackageSnapshots = linkedPackages,
                        Summary = "Mission request and terminal packages archived at global replanning."
                    });
                    continue;
                }

                request.State = AirMissionRequestState.Purged;
                AddDiagnostic(new AirTaskingDiagnostic
                {
                    RecordedAt = currentTime,
                    MissionRequestId = request.MissionRequestId,
                    Code = "request-purged",
                    Message = "Unfulfilled request purged during global reprioritization."
                });
            }

            missionRequests = retainedRequests;
            var retainedRequestIds = retainedRequests
                .Select(request => request.MissionRequestId)
                .ToHashSet();
            packages = packages
                .Where(package => package != null
                                  && (!package.IsTerminal
                                      || retainedRequestIds.Contains(package.MissionRequestId)))
                .ToList();
        }

        public void AddMissionRequests(
            IEnumerable<AirMissionRequest> requests,
            DateTime currentTime)
        {
            EnsureCollections();
            foreach (var request in requests ?? Enumerable.Empty<AirMissionRequest>())
            {
                if (request == null
                    || request.Alliance != Alliance
                    || request.PlanningCycle != PlanningCycle)
                    continue;

                missionRequests.Add(request);
                AddDiagnostic(new AirTaskingDiagnostic
                {
                    RecordedAt = currentTime,
                    MissionRequestId = request.MissionRequestId,
                    Code = "request-generated",
                    Message = request.Rationale,
                    Values = new Dictionary<string, float>(request.PriorityComponents)
                    {
                        { "priority", request.Priority }
                    }
                });
            }
        }

        public bool TryCommitPackage(
            AirPackage package,
            AircraftReservationService aircraftReservations,
            DateTime currentTime,
            out string reason)
        {
            EnsureCollections();
            if (aircraftReservations == null)
            {
                reason = "Aircraft reservations are required.";
                return false;
            }

            reason = ValidatePackageProposal(package, currentTime);
            if (!string.IsNullOrEmpty(reason))
                return false;

            var request = GetRequest(package.MissionRequestId);
            if (!TryPlanSupportReservations(package, out var supportReservations, out reason))
                return false;

            if (!aircraftReservations.TryReserve(package, out reason))
                return false;

            var previousRequestState = request.State;
            var previousDiagnosticCount = diagnostics.Count;
            var previousSupportDemandCount = supportDemandHistory.Count;
            try
            {
                foreach (var reservation in supportReservations)
                    reservation.Flight.SupportReservations.Add(reservation.Reservation);

                packages.Add(package);
                request.PackageIds ??= new List<Guid>();
                request.PackageIds.Add(package.PackageId);
                request.State = request.IsSupportRequest
                    ? AirMissionRequestState.PartiallyFulfilled
                    : AirMissionRequestState.InProgress;

                var tankerSlots = supportReservations
                    .Where(entry =>
                        entry.Flight.MissionType == AirMissionRequestType.ProvideAerialRefueling)
                    .Sum(entry => entry.Reservation.SlotCount);
                if (tankerSlots > 0)
                {
                    AddSupportDemand(new SupportDemandSample
                    {
                        RecordedAt = currentTime,
                        SupportType = AirMissionRequestType.ProvideAerialRefueling,
                        MissionArea = new AirMissionArea(
                            request.MissionArea.CenterTileId,
                            request.MissionArea.RadiusTiles),
                        RequestedSlots = tankerSlots
                    });
                }

                AddDiagnostic(new AirTaskingDiagnostic
                {
                    RecordedAt = currentTime,
                    MissionRequestId = request.MissionRequestId,
                    PackageId = package.PackageId,
                    Code = "package-committed",
                    Message = "Package committed.",
                    Values = new Dictionary<string, float>
                    {
                        { "flightCount", package.Flights.Count },
                        { "aircraftCount", package.Flights.Sum(flight => flight.AircraftIds.Count) }
                    }
                });
                reason = "Package committed.";
                return true;
            }
            catch
            {
                foreach (var reservation in supportReservations)
                    reservation.Flight.SupportReservations.Remove(reservation.Reservation);
                packages.Remove(package);
                request.PackageIds?.Remove(package.PackageId);
                request.State = previousRequestState;
                RemoveEntriesAddedAfter(diagnostics, previousDiagnosticCount);
                RemoveEntriesAddedAfter(supportDemandHistory, previousSupportDemandCount);
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
            EnsureCollections();
            var package = GetPackage(packageId);
            if (package == null
                || (package.Flights ?? new List<AirFlight>())
                .Where(flight => flight != null)
                .All(flight => flight.IsTerminal))
                return false;

            var active = (package.Flights ?? new List<AirFlight>())
                .Where(flight => flight != null)
                .Any(flight =>
                    flight.LifecycleState == AirTaskingLifecycleState.Active
                    || flight.LifecycleState == AirTaskingLifecycleState.Aborted);
            foreach (var supportFlight in packages
                         .Where(candidate => candidate != null)
                         .SelectMany(candidate => candidate.Flights ?? new List<AirFlight>())
                         .Where(flight => flight != null))
            {
                supportFlight.SupportReservations?.RemoveAll(
                    reservation => reservation != null
                                   && reservation.ConsumingPackageId == package.PackageId);
            }

            foreach (var flight in package.Flights ?? new List<AirFlight>())
            {
                if (flight == null || flight.IsTerminal)
                    continue;

                var wasActive = flight.LifecycleState == AirTaskingLifecycleState.Active;
                flight.LifecycleState = active
                    ? AirTaskingLifecycleState.Aborted
                    : AirTaskingLifecycleState.Cancelled;
                if (!wasActive)
                    aircraftReservations.ReleaseFlight(flight);
            }

            var request = GetRequest(package.MissionRequestId);
            if (request != null && !active)
                request.State = AirMissionRequestState.Actionable;

            AddDiagnostic(new AirTaskingDiagnostic
            {
                RecordedAt = currentTime,
                MissionRequestId = package.MissionRequestId,
                PackageId = package.PackageId,
                Code = active ? "package-aborted" : "package-cancelled",
                Message = reason ?? string.Empty
            });
            return true;
        }

        public void ValidatePackageIntegrity(
            AircraftReservationService aircraftReservations,
            DateTime currentTime)
        {
            EnsureCollections();
            foreach (var package in packages
                         .Where(candidate => candidate != null
                                             && (candidate.Flights ?? new List<AirFlight>())
                                             .Any(flight => flight != null && !flight.IsTerminal))
                         .ToList())
            {
                var requiredFlights = (package.Flights ?? new List<AirFlight>())
                    .Where(flight => flight != null && flight.IsRequired)
                    .ToList();
                if (requiredFlights.Count > 0
                    && requiredFlights.All(flight =>
                        flight.LifecycleState != AirTaskingLifecycleState.Cancelled
                        && flight.LifecycleState != AirTaskingLifecycleState.Failed
                        && flight.LifecycleState != AirTaskingLifecycleState.Aborted))
                    continue;

                CancelPackage(
                    package.PackageId,
                    aircraftReservations,
                    currentTime,
                    "A required package flight was cancelled or became unable to launch.");
            }
        }

        public void MarkRequestFulfilled(Guid requestId, DateTime currentTime, string reason)
        {
            SetRequestState(
                requestId,
                AirMissionRequestState.Fulfilled,
                currentTime,
                "request-covered",
                reason);
        }

        public void MarkRequestInProgress(Guid requestId, DateTime currentTime, string reason)
        {
            SetRequestState(
                requestId,
                AirMissionRequestState.InProgress,
                currentTime,
                "request-in-progress",
                reason);
        }

        public void RecordRequestDeferred(Guid requestId, DateTime currentTime, string reason)
        {
            AddDiagnostic(new AirTaskingDiagnostic
            {
                RecordedAt = currentTime,
                MissionRequestId = requestId,
                Code = "request-deferred",
                Message = reason ?? string.Empty
            });
        }

        public void AddDiagnostic(AirTaskingDiagnostic diagnostic)
        {
            if (diagnostic == null)
                return;

            EnsureCollections();
            diagnostics.Add(diagnostic);
            TrimOldest(diagnostics, MaximumDiagnosticEntries);
        }

        public void AddHistory(AirTaskingHistoryEntry historyEntry)
        {
            if (historyEntry == null)
                return;

            EnsureCollections();
            history.Add(historyEntry);
            TrimOldest(history, MaximumHistoryEntries);
        }

        public void AddSupportDemand(SupportDemandSample demandSample)
        {
            if (demandSample == null)
                return;

            EnsureCollections();
            supportDemandHistory.Add(demandSample);
            TrimOldest(supportDemandHistory, MaximumSupportDemandSamples);
        }

        public AirMissionRequest GetRequest(Guid requestId)
        {
            EnsureCollections();
            return missionRequests.FirstOrDefault(
                request => request.MissionRequestId == requestId);
        }

        public AirPackage GetPackage(Guid packageId)
        {
            EnsureCollections();
            return packages.FirstOrDefault(package => package.PackageId == packageId);
        }

        private string ValidatePackageProposal(AirPackage package, DateTime currentTime)
        {
            if (package == null)
                return "A package proposal is required.";
            if (package.PackageId == Guid.Empty || packages.Any(candidate =>
                    candidate != null && candidate.PackageId == package.PackageId))
                return "The package proposal has an invalid or duplicate identifier.";
            if (package.Alliance != Alliance)
                return "The package proposal belongs to another alliance.";

            var request = GetRequest(package.MissionRequestId);
            if (request == null
                || request.IsTerminal
                || request.PlanningCycle != PlanningCycle
                || request.EffectEnd <= currentTime)
                return "The package proposal does not target a current actionable request.";
            if (package.EffectStart < request.EffectStart
                || package.EffectEnd > request.EffectEnd
                || package.EffectEnd <= package.EffectStart)
                return "The package proposal has an invalid effect window.";

            var flights = (package.Flights ?? new List<AirFlight>())
                .Where(flight => flight != null)
                .ToList();
            if (flights.Count == 0 || flights.Count != package.Flights.Count)
                return "The package proposal must contain valid flights.";
            if (flights.Any(flight =>
                    flight.FlightId == Guid.Empty
                    || flight.OwningPackageId != package.PackageId
                    || flight.SquadronId == Guid.Empty
                    || flight.MissionType != request.RequestType
                    || flight.EffectStart != package.EffectStart
                    || flight.EffectEnd != package.EffectEnd
                    || flight.AircraftIds == null
                    || flight.AircraftIds.Count == 0))
                return "A proposed flight is incomplete.";
            if (flights.Select(flight => flight.FlightId).Distinct().Count() != flights.Count)
                return "The package proposal contains duplicate flight identifiers.";

            var aircraftIds = flights.SelectMany(flight => flight.AircraftIds).ToList();
            if (aircraftIds.Any(id => id == Guid.Empty)
                || aircraftIds.Distinct().Count() != aircraftIds.Count)
                return "The package proposal contains invalid or duplicate aircraft.";
            var supportingFlightIds = package.SupportingFlightIds ?? new List<Guid>();
            if (supportingFlightIds.Any(id => id == Guid.Empty)
                || supportingFlightIds.Distinct().Count() != supportingFlightIds.Count)
                return "The package proposal contains invalid or duplicate support flights.";
            if (!request.IsSupportRequest
                && aircraftIds.Count < Math.Max(1, request.DesiredAircraftStrength))
                return "The package proposal cannot deliver the requested combat effect.";

            return string.Empty;
        }

        private bool TryPlanSupportReservations(
            AirPackage package,
            out List<PlannedSupportReservation> planned,
            out string reason)
        {
            planned = new List<PlannedSupportReservation>();
            reason = string.Empty;
            var requiredSlots = package.Flights.Sum(flight => flight.AircraftIds.Count);
            var remainingSlots = requiredSlots;

            foreach (var supportFlightId in package.SupportingFlightIds ?? new List<Guid>())
            {
                var supportFlight = packages
                    .Where(candidate => candidate != null && !candidate.IsTerminal)
                    .SelectMany(candidate => candidate.Flights ?? new List<AirFlight>())
                    .FirstOrDefault(flight => flight != null
                                              && flight.FlightId == supportFlightId
                                              && !flight.IsTerminal);
                if (supportFlight == null
                    || supportFlight.EffectStart > package.EffectStart
                    || supportFlight.EffectEnd < package.EffectEnd)
                {
                    reason = "A required support flight is no longer available.";
                    return false;
                }

                supportFlight.SupportReservations ??= new List<AirSupportReservation>();
                var alreadyReserved = supportFlight.SupportReservations
                    .Where(reservation => reservation != null
                                          && reservation.StartTime < package.EffectEnd
                                          && reservation.EndTime > package.EffectStart)
                    .Sum(reservation => Math.Max(0, reservation.SlotCount));
                var slots = Math.Min(
                    remainingSlots,
                    Math.Max(0, supportFlight.ProvidedSupportSlots - alreadyReserved));
                if (slots <= 0)
                    continue;

                planned.Add(new PlannedSupportReservation(
                    supportFlight,
                    new AirSupportReservation
                    {
                        SupportingFlightId = supportFlight.FlightId,
                        ConsumingPackageId = package.PackageId,
                        SlotCount = slots,
                        StartTime = package.EffectStart,
                        EndTime = package.EffectEnd
                    }));
                remainingSlots -= slots;
                if (remainingSlots == 0)
                    break;
            }

            if ((package.SupportingFlightIds?.Count ?? 0) > 0 && remainingSlots > 0)
            {
                reason = "Required support capacity is no longer available.";
                return false;
            }

            return true;
        }

        private void SetRequestState(
            Guid requestId,
            AirMissionRequestState state,
            DateTime currentTime,
            string code,
            string reason)
        {
            var request = GetRequest(requestId);
            if (request == null || request.IsTerminal)
                return;

            request.State = state;
            AddDiagnostic(new AirTaskingDiagnostic
            {
                RecordedAt = currentTime,
                MissionRequestId = requestId,
                Code = code,
                Message = reason ?? string.Empty
            });
        }

        private void EnsureCollections()
        {
            missionRequests ??= new List<AirMissionRequest>();
            packages ??= new List<AirPackage>();
            supportDemandHistory ??= new List<SupportDemandSample>();
            diagnostics ??= new List<AirTaskingDiagnostic>();
            history ??= new List<AirTaskingHistoryEntry>();
        }

        private static void TrimOldest<T>(List<T> entries, int maximumEntries)
        {
            if (entries.Count <= maximumEntries)
                return;

            entries.RemoveRange(0, entries.Count - maximumEntries);
        }

        private static void RemoveEntriesAddedAfter<T>(List<T> entries, int previousCount)
        {
            if (entries.Count > previousCount)
                entries.RemoveRange(previousCount, entries.Count - previousCount);
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
