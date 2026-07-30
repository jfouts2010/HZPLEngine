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
        [SerializeField]
        private List<AirControlTileAssessment> airControlAssessments =
            new List<AirControlTileAssessment>();

        [NonSerialized]
        private Dictionary<Vector3Int, AirControlTileAssessment>
            airControlAssessmentsByTileId;

        public IReadOnlyList<AirMissionRequest> MissionRequests => missionRequests;
        public IReadOnlyList<AirPackage> Packages => packages;
        public IReadOnlyList<SupportDemandSample> SupportDemandHistory => supportDemandHistory;
        public IReadOnlyList<AirTaskingDiagnostic> Diagnostics => diagnostics;
        public IReadOnlyList<AirTaskingHistoryEntry> History => history;
        public IReadOnlyList<AirControlTileAssessment> AirControlAssessments =>
            airControlAssessments;

        public AllianceAirTaskingCommander()
        {
        }

        public AllianceAirTaskingCommander(Alliance alliance, AllianceAirDoctrine doctrine)
        {
            Alliance = alliance;
            Doctrine = doctrine.Clone();
        }

        public void InitializeAirControlAssessments(
            IEnumerable<Vector3Int> tileIds)
        {
            EnsureAirControlAssessmentIndex();
            var desiredTileIds = new HashSet<Vector3Int>(tileIds ?? Array.Empty<Vector3Int>());
            airControlAssessments.RemoveAll(assessment =>
                assessment == null || !desiredTileIds.Contains(assessment.TileId));
            airControlAssessmentsByTileId = null;
            EnsureAirControlAssessmentIndex();

            foreach (var tileId in desiredTileIds
                         .OrderBy(tile => tile.x)
                         .ThenBy(tile => tile.y)
                         .ThenBy(tile => tile.z))
            {
                GetOrCreateAirControlAssessment(tileId);
            }
        }

        public bool TryGetAirControlAssessment(
            Vector3Int tileId,
            out AirControlTileAssessment assessment)
        {
            EnsureAirControlAssessmentIndex();
            return airControlAssessmentsByTileId.TryGetValue(tileId, out assessment);
        }

        public float GetAirActivity(Vector3Int tileId)
        {
            return TryGetAirControlAssessment(tileId, out var assessment)
                ? Mathf.Clamp01(assessment.AirActivity)
                : 0f;
        }

        internal AirControlTileAssessment GetOrCreateAirControlAssessment(
            Vector3Int tileId)
        {
            EnsureAirControlAssessmentIndex();
            if (airControlAssessmentsByTileId.TryGetValue(tileId, out var assessment))
                return assessment;

            assessment = new AirControlTileAssessment(tileId);
            airControlAssessments.Add(assessment);
            airControlAssessmentsByTileId[tileId] = assessment;
            return assessment;
        }

        private void EnsureAirControlAssessmentIndex()
        {
            if (airControlAssessments == null)
                airControlAssessments = new List<AirControlTileAssessment>();
            if (airControlAssessmentsByTileId != null)
                return;

            airControlAssessments = airControlAssessments
                .Where(assessment => assessment != null)
                .GroupBy(assessment => assessment.TileId)
                .Select(group => group.First())
                .OrderBy(assessment => assessment.TileId.x)
                .ThenBy(assessment => assessment.TileId.y)
                .ThenBy(assessment => assessment.TileId.z)
                .ToList();
            airControlAssessmentsByTileId = airControlAssessments
                .ToDictionary(assessment => assessment.TileId);
        }

        public void BeginPlanningCycle(DateTime currentTime)
        {
            PlanningCycle++;

            var retainedRequests = new List<AirMissionRequest>();
            foreach (var request in missionRequests)
            {
                var linkedPackages = packages
                    .Where(package => package.MissionRequestId == request.MissionRequestId)
                    .ToList();
                if (linkedPackages.Any(package => !package.HasPhysicallyEnded))
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
                .Where(package => !package.HasPhysicallyEnded
                                  || retainedRequestIds.Contains(package.MissionRequestId))
                .ToList();
        }

        public void AddMissionRequests(
            IEnumerable<AirMissionRequest> requests,
            DateTime currentTime)
        {
            foreach (var request in requests)
            {
                if (request.Alliance != Alliance
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
                request.State = request.IsSupportRequest
                                || request.RequestType
                                == AirMissionRequestType.BarrierCombatAirPatrol
                    ? AirMissionRequestState.PartiallyFulfilled
                    : AirMissionRequestState.InProgress;

                var tankerSlots = supportReservations
                    .Where(entry =>
                        entry.Flight.MissionType == AirMissionRequestType.ProvideAerialRefueling)
                    .Select(entry => Math.Max(0, entry.Reservation.SlotCount))
                    .DefaultIfEmpty(0)
                    .Max();
                if (tankerSlots > 0)
                {
                    AddSupportDemand(new SupportDemandSample
                    {
                        RecordedAt = currentTime,
                        SupportType = AirMissionRequestType.ProvideAerialRefueling,
                        MissionArea = new AirMissionArea(
                            request.MissionArea.CenterTileId,
                            request.MissionArea.RadiusKm,
                            request.MissionArea.TileDistanceKm),
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
            var package = GetPackage(packageId);
            if (package == null
                || package.Flights
                .All(flight => flight.IsTerminal))
                return false;

            var aborted = package.Flights
                .Any(flight => flight.LifecycleState == AirTaskingLifecycleState.Aborted);
            foreach (var supportFlight in packages
                         .SelectMany(candidate => candidate.Flights))
            {
                supportFlight.SupportReservations.RemoveAll(
                    reservation => reservation.ConsumingPackageId == package.PackageId);
            }
            foreach (var cancelledProvider in package.Flights)
                cancelledProvider.SupportReservations.Clear();

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

            var request = GetRequest(package.MissionRequestId);
            if (request == null)
            {
                throw new InvalidOperationException(
                    $"Package {package.PackageId} references missing mission request "
                    + $"{package.MissionRequestId}.");
            }
            if (!aborted)
                request.State = AirMissionRequestState.Actionable;

            AddDiagnostic(new AirTaskingDiagnostic
            {
                RecordedAt = currentTime,
                MissionRequestId = package.MissionRequestId,
                PackageId = package.PackageId,
                Code = aborted ? "package-aborted" : "package-cancelled",
                Message = reason
            });
            return true;
        }

        public void ValidatePackageIntegrity(
            AircraftReservationService aircraftReservations,
            DateTime currentTime)
        {
            foreach (var package in packages
                         .Where(candidate => candidate.Flights
                                             .Any(flight => !flight.IsTerminal))
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
                var requiredSlots = package.Flights.Sum(
                    flight => flight.AircraftIds.Count);
                if (supportingFlights.Select(flight => flight.FlightId)
                        .Distinct()
                        .Count() == supportingIds.Count
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

        public void ReopenFulfilledRequest(
            Guid requestId,
            DateTime currentTime,
            string reason)
        {
            var request = GetRequest(requestId);
            if (request == null)
                throw new InvalidOperationException(
                    $"Mission request {requestId} does not exist.");
            if (request.State != AirMissionRequestState.Fulfilled)
                return;

            request.State = AirMissionRequestState.Actionable;
            AddDiagnostic(new AirTaskingDiagnostic
            {
                RecordedAt = currentTime,
                MissionRequestId = requestId,
                Code = "request-coverage-reopened",
                Message = reason
            });
        }

        public void RecordRequestDeferred(Guid requestId, DateTime currentTime, string reason)
        {
            AddDiagnostic(new AirTaskingDiagnostic
            {
                RecordedAt = currentTime,
                MissionRequestId = requestId,
                Code = "request-deferred",
                Message = reason
            });
        }

        public void AddDiagnostic(AirTaskingDiagnostic diagnostic)
        {
            diagnostics.Add(diagnostic);
            TrimOldest(diagnostics, MaximumDiagnosticEntries);
        }

        public void AddHistory(AirTaskingHistoryEntry historyEntry)
        {
            history.Add(historyEntry);
            TrimOldest(history, MaximumHistoryEntries);
        }

        public void AddSupportDemand(SupportDemandSample demandSample)
        {
            supportDemandHistory.Add(demandSample);
            TrimOldest(supportDemandHistory, MaximumSupportDemandSamples);
        }

        public AirMissionRequest GetRequest(Guid requestId)
        {
            return missionRequests.FirstOrDefault(
                request => request.MissionRequestId == requestId);
        }

        public AirPackage GetPackage(Guid packageId)
        {
            return packages.FirstOrDefault(package => package.PackageId == packageId);
        }

        private string ValidatePackageProposal(AirPackage package, DateTime currentTime)
        {
            if (package.PackageId == Guid.Empty || packages.Any(candidate =>
                    candidate.PackageId == package.PackageId))
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
                || package.EffectEnd < package.EffectStart
                || request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained
                && package.EffectEnd == package.EffectStart)
                return "The package proposal has an invalid effect window.";

            var flights = package.Flights;
            if (flights.Count == 0)
                return "The package proposal must contain valid flights.";
            foreach (var flight in flights)
            {
                if (!flight.TryValidateRoute(out _))
                    return "A proposed flight has an invalid materialized route.";
                var satisfiesMissionGeometry =
                    SatisfiesMissionGeometry(request, flight);
                if (flight.PlannedTakeoffTime < package.CreatedAt + AirPackage.PreparationDelay
                    || flight.EffectStart < request.EffectStart
                    || flight.EffectEnd > request.EffectEnd
                    || flight.EffectEnd < flight.EffectStart
                    || !satisfiesMissionGeometry)
                {
                    return "A proposed flight route does not satisfy its request.";
                }
            }
            if (flights.Any(flight =>
                    flight.FlightId == Guid.Empty
                    || flight.SquadronId == Guid.Empty
                    || flight.MissionType != request.RequestType
                    || flight.AircraftIds.Count == 0))
                return "A proposed flight is incomplete.";
            var packageFlightIds = flights
                .Select(flight => flight.FlightId)
                .ToHashSet();
            if (flights.Where(flight => flight.IsFighterEscort).Any(escort =>
                    escort.ProtectedFlightIds.Count == 0
                    || escort.ProtectedFlightIds.Any(id =>
                        id == escort.FlightId
                        || !packageFlightIds.Contains(id))
                    || escort.AuthorizedSurfaceThreatSiteId != Guid.Empty))
                return "A proposed fighter escort has an invalid protection assignment.";
            if (flights.Select(flight => flight.FlightId).Distinct().Count() != flights.Count)
                return "The package proposal contains duplicate flight identifiers.";

            var aircraftIds = flights.SelectMany(flight => flight.AircraftIds).ToList();
            if (aircraftIds.Any(id => id == Guid.Empty)
                || aircraftIds.Distinct().Count() != aircraftIds.Count)
                return "The package proposal contains invalid or duplicate aircraft.";
            var supportingFlightIds = package.SupportingFlightIds;
            if (supportingFlightIds.Any(id => id == Guid.Empty)
                || supportingFlightIds.Distinct().Count() != supportingFlightIds.Count)
                return "The package proposal contains invalid or duplicate support flights.";
            var effectAircraftCount = flights
                .Where(flight => !flight.IsFighterEscort)
                .Sum(flight => flight.AircraftIds.Count);
            if (!request.IsSupportRequest
                && effectAircraftCount < (request.RequestType
                    == AirMissionRequestType.BarrierCombatAirPatrol
                        ? 1
                        : Math.Max(1, request.DesiredAircraftStrength)))
                return "The package proposal cannot deliver the requested combat effect.";

            return string.Empty;
        }

        internal static bool SatisfiesMissionGeometry(
            AirMissionRequest request,
            AirFlight flight)
        {
            if (request == null || flight == null)
                return false;
            if (flight.IsFighterEscort)
                return true;

            var spatialBarcap = request.RequestType
                                == AirMissionRequestType.BarrierCombatAirPatrol
                                && request.BarcapBarrier?.BarrierTileIds?.Count > 0;
            return spatialBarcap
                ? HasValidSpatialBarcapCoverage(request, flight)
                : request.MissionArea.Contains(
                    flight.MissionArea.CenterTileId);
        }

        private static bool HasValidSpatialBarcapCoverage(
            AirMissionRequest request,
            AirFlight flight)
        {
            var barrier = request.BarcapBarrier;
            var coverage = flight.PlannedBarcapCoverage;
            if (barrier?.BarrierTileIds == null
                || coverage?.CoveredBarrierTileIds == null
                || coverage.BarrierId != barrier.BarrierId
                || coverage.ThreatReferenceTileId
                != barrier.ThreatReferenceTileId
                || coverage.CoveredBarrierTileIds.Count == 0)
            {
                return false;
            }

            var barrierTiles = barrier.BarrierTileIds.ToHashSet();
            return coverage.CoveredBarrierTileIds
                .All(barrierTiles.Contains);
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

            var requiredSlots = package.Flights.Sum(flight => flight.AircraftIds.Count);
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
                reason = "A required support flight is no longer available.";
                return false;
            }

            if (supportingFlights.Any(flight =>
                    flight.MissionType
                    != AirMissionRequestType.ProvideAerialRefueling
                    || package.Flights.Any(receiver =>
                        !flight.MissionArea.Contains(
                            receiver.MissionArea.CenterTileId))))
            {
                reason = "A required tanker does not cover the receiving package.";
                return false;
            }

            var reservations =
                AirSupportCoveragePlanner.PlanContinuousCoverage(
                    supportingFlights,
                    package.PackageId,
                    requiredSlots,
                    package.EffectStart,
                    package.SupportWindowEnd,
                    out var coveredUntil);
            if (coveredUntil < package.SupportWindowEnd)
            {
                reason = "Required support capacity is no longer available.";
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

        private void SetRequestState(
            Guid requestId,
            AirMissionRequestState state,
            DateTime currentTime,
            string code,
            string reason)
        {
            var request = GetRequest(requestId);
            if (request == null)
                throw new InvalidOperationException(
                    $"Mission request {requestId} does not exist.");
            if (request.IsTerminal)
                return;

            request.State = state;
            AddDiagnostic(new AirTaskingDiagnostic
            {
                RecordedAt = currentTime,
                MissionRequestId = requestId,
                Code = code,
                Message = reason
            });
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
