using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Service
{
    public sealed class ProjectedAirEffectService
    {
        private const float BarcapPreferredLaunchRangeFraction = 0.78f;

        private readonly GameManager gameManager;
        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition>
            ordnanceTypes;

        public ProjectedAirEffectService(
            GameManager gameManager,
            ModuleDefinition module)
        {
            this.gameManager = gameManager;
            ordnanceTypes = module.OrdnanceTypeDefinitions
                .ToDictionary(definition => definition.OrdnanceTypeDefinitionId);
        }

        public bool TryFindFirstCoverageGap(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime planningStart,
            out DateTime gapStart,
            out int projectedAmount)
        {
            gapStart = planningStart;
            projectedAmount = 0;
            if (request.RequestType == AirMissionRequestType.BarrierCombatAirPatrol
                && request.BarcapBarrier?.BarrierTileIds?.Count > 0)
            {
                if (!TryFindFirstBarcapTaskingGap(
                        commander,
                        request,
                        planningStart,
                        out gapStart,
                        out var uncovered,
                        out _,
                        out _))
                    return false;

                projectedAmount = Math.Max(
                    0,
                    request.BarcapBarrier.BarrierTileIds.Count - uncovered.Count);
                return true;
            }

            var desiredAmount = request.IsSupportRequest
                ? request.DesiredSupportSlots
                : request.DesiredAircraftStrength;
            if (desiredAmount <= 0)
                return false;

            var intervalStart = planningStart > request.EffectStart
                ? planningStart
                : request.EffectStart;
            if (intervalStart >= request.EffectEnd)
                return false;

            var flights = GetProjectedFlights(commander, request)
                .Where(flight => flight.EffectEnd > intervalStart
                                 && flight.EffectStart < request.EffectEnd)
                .ToList();
            var eventTimes = new SortedSet<DateTime> { intervalStart };
            foreach (var flight in flights)
            {
                if (flight.EffectStart > intervalStart && flight.EffectStart < request.EffectEnd)
                    eventTimes.Add(flight.EffectStart);
                if (flight.EffectEnd > intervalStart && flight.EffectEnd < request.EffectEnd)
                    eventTimes.Add(flight.EffectEnd);
            }

            foreach (var eventTime in eventTimes)
            {
                var amount = flights
                    .Where(flight => flight.EffectStart <= eventTime && flight.EffectEnd > eventTime)
                    .Sum(flight => request.IsSupportRequest
                        ? Math.Max(0, flight.ProvidedSupportSlots)
                        : flight.AircraftIds.Count);
                if (amount >= desiredAmount)
                    continue;

                gapStart = eventTime;
                projectedAmount = amount;
                return true;
            }

            return false;
        }

        public bool TryFindFirstBarcapCoverageGap(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime planningStart,
            out DateTime gapStart,
            out IReadOnlyList<Vector3Int> uncoveredBarrierTiles)
        {
            gapStart = planningStart;
            uncoveredBarrierTiles = Array.Empty<Vector3Int>();
            var barrierTiles = request.BarcapBarrier?.BarrierTileIds?
                .Distinct()
                .ToList();
            if (barrierTiles == null || barrierTiles.Count == 0)
                return false;

            var intervalStart = planningStart > request.EffectStart
                ? planningStart
                : request.EffectStart;
            if (intervalStart >= request.EffectEnd)
                return false;

            var flights = GetProjectedFlights(commander, request)
                .Where(flight => flight.EffectEnd > intervalStart
                                 && flight.EffectStart < request.EffectEnd)
                .ToList();
            var eventTimes = new SortedSet<DateTime> { intervalStart };
            foreach (var flight in flights)
            {
                if (flight.EffectStart > intervalStart
                    && flight.EffectStart < request.EffectEnd)
                    eventTimes.Add(flight.EffectStart);
                if (flight.EffectEnd > intervalStart
                    && flight.EffectEnd < request.EffectEnd)
                    eventTimes.Add(flight.EffectEnd);
            }

            foreach (var eventTime in eventTimes)
            {
                var covered = flights
                    .Where(flight => flight.EffectStart <= eventTime
                                     && flight.EffectEnd > eventTime)
                    .Select(flight => flight.PlannedBarcapCoverage)
                    .Where(coverage => coverage != null)
                    .SelectMany(coverage => coverage.CoveredBarrierTileIds)
                    .ToHashSet();
                var uncovered = barrierTiles
                    .Where(tile => !covered.Contains(tile))
                    .ToList();
                if (uncovered.Count == 0)
                    continue;

                gapStart = eventTime;
                uncoveredBarrierTiles = uncovered;
                return true;
            }

            return false;
        }

        public bool TryFindFirstBarcapTaskingGap(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime planningStart,
            out DateTime gapStart,
            out IReadOnlyList<Vector3Int> gapBarrierTiles,
            out bool isSpatialGap,
            out int aircraftDeficit)
        {
            gapStart = planningStart;
            gapBarrierTiles = Array.Empty<Vector3Int>();
            isSpatialGap = false;
            aircraftDeficit = 0;
            var barrierTiles = request.BarcapBarrier?.BarrierTileIds?
                .Distinct()
                .ToList();
            if (barrierTiles == null || barrierTiles.Count == 0)
                return false;

            var intervalStart = planningStart > request.EffectStart
                ? planningStart
                : request.EffectStart;
            if (intervalStart >= request.EffectEnd)
                return false;

            var flights = GetProjectedFlights(commander, request)
                .Where(flight => flight.EffectEnd > intervalStart
                                 && flight.EffectStart < request.EffectEnd)
                .ToList();
            var eventTimes = new SortedSet<DateTime> { intervalStart };
            foreach (var flight in flights)
            {
                if (flight.EffectStart > intervalStart
                    && flight.EffectStart < request.EffectEnd)
                    eventTimes.Add(flight.EffectStart);
                if (flight.EffectEnd > intervalStart
                    && flight.EffectEnd < request.EffectEnd)
                    eventTimes.Add(flight.EffectEnd);
            }

            var preferredStrength = Math.Max(
                1,
                commander.Doctrine.PreferredBarcapStationAircraft);
            foreach (var eventTime in eventTimes)
            {
                var activeFlights = flights
                    .Where(flight => flight.EffectStart <= eventTime
                                     && flight.EffectEnd > eventTime)
                    .Select(flight => new
                    {
                        Flight = flight,
                        Coverage = flight.PlannedBarcapCoverage
                    })
                    .Where(item => item.Coverage != null)
                    .ToList();
                var covered = activeFlights
                    .SelectMany(item => item.Coverage.CoveredBarrierTileIds)
                    .ToHashSet();
                var uncovered = barrierTiles
                    .Where(tile => !covered.Contains(tile))
                    .ToList();
                if (uncovered.Count > 0)
                {
                    var requiredByTile = uncovered.ToDictionary(
                        tile => tile,
                        _ => 1);
                    if (PendingBarcapFlightsCloseGap(
                            flights,
                            eventTime,
                            requiredByTile))
                        continue;

                    gapStart = eventTime;
                    gapBarrierTiles = uncovered;
                    isSpatialGap = true;
                    aircraftDeficit = 1;
                    return true;
                }

                var strengthByTile = barrierTiles.ToDictionary(tile => tile, _ => 0);
                foreach (var item in activeFlights)
                {
                    var aircraftCount = GetProjectedAircraftCount(
                        item.Flight,
                        item.Coverage);
                    if (aircraftCount <= 0)
                        continue;
                    foreach (var tile in item.Coverage.CoveredBarrierTileIds
                                 .Where(strengthByTile.ContainsKey))
                    {
                        strengthByTile[tile] += aircraftCount;
                    }
                }

                var understrength = barrierTiles
                    .Where(tile => strengthByTile[tile] < preferredStrength)
                    .ToList();
                if (understrength.Count == 0)
                    continue;

                var deficitsByTile = understrength.ToDictionary(
                    tile => tile,
                    tile => preferredStrength - strengthByTile[tile]);
                if (PendingBarcapFlightsCloseGap(
                        flights,
                        eventTime,
                        deficitsByTile))
                    continue;

                gapStart = eventTime;
                gapBarrierTiles = understrength;
                aircraftDeficit = deficitsByTile.Values.Max();
                return true;
            }

            return false;
        }

        private bool PendingBarcapFlightsCloseGap(
            IReadOnlyList<AirFlight> projectedFlights,
            DateTime gapStart,
            IReadOnlyDictionary<Vector3Int, int> requiredAircraftByTile)
        {
            if (projectedFlights == null
                || requiredAircraftByTile == null
                || requiredAircraftByTile.Count == 0)
                return false;

            var pendingFlights = projectedFlights
                .Where(flight => flight.EffectStart > gapStart
                                 && flight.EffectEnd > flight.EffectStart)
                .Select(flight => new
                {
                    Coverage = flight.PlannedBarcapCoverage,
                    AircraftCount = GetProjectedAircraftCount(
                        flight,
                        flight.PlannedBarcapCoverage)
                })
                .Where(item => item.Coverage != null
                               && item.AircraftCount > 0)
                .ToList();
            if (pendingFlights.Count == 0)
                return false;

            return requiredAircraftByTile.All(requirement =>
                pendingFlights
                    .Where(item => item.Coverage.CoveredBarrierTileIds
                        .Contains(requirement.Key))
                    .Sum(item => item.AircraftCount)
                >= requirement.Value);
        }

        private int GetProjectedAircraftCount(
            AirFlight flight,
            BarcapStationCoverage coverage)
        {
            if (flight == null)
                return 0;
            if (gameManager == null
                || !gameManager.squadronSystem.TryGetSquadron(
                    flight.SquadronId,
                    out var squadron))
                return flight.AircraftIds.Count;

            var assignedIds = flight.AircraftIds.ToHashSet();
            var surviving = squadron.Aircraft
                .Where(aircraft => assignedIds.Contains(aircraft.AircraftId)
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Lost)
                .ToList();
            if (!flight.IsAirborne
                || coverage == null
                || coverage.PlannedPreferredLaunchRangeKm <= 0f)
                return surviving.Count;

            return surviving.Count(aircraft => aircraft.Loadout
                .Where(item => item.Count > 0
                               && ordnanceTypes.TryGetValue(
                                   item.OrdnanceTypeDefinitionId,
                                   out var ordnance)
                               && AirLoadoutPlanner.IsAirToAir(ordnance))
                .Select(item => ordnanceTypes[item.OrdnanceTypeDefinitionId]
                                .MaximumRangeKm
                                * BarcapPreferredLaunchRangeFraction)
                .DefaultIfEmpty(0f)
                .Max()
                + 0.001f >= coverage.PlannedPreferredLaunchRangeKm);
        }

        public bool HasOtherSpatialBarcapCoverageGap(
            AllianceAirTaskingCommander commander,
            Guid excludedRequestId,
            DateTime planningStart)
        {
            return commander.MissionRequests
                .Where(request => request.MissionRequestId != excludedRequestId
                                  && request.RequestType
                                  == AirMissionRequestType.BarrierCombatAirPatrol
                                  && request.BarcapBarrier?.BarrierTileIds?.Count > 0
                                  && request.EffectEnd > planningStart)
                .Any(request => TryFindFirstBarcapTaskingGap(
                    commander,
                    request,
                    planningStart,
                    out _,
                    out _,
                    out _,
                    out _));
        }

        public float GetProjectedBarcapCoverageFraction(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime planningStart)
        {
            var total = request.BarcapBarrier?.BarrierTileIds?.Distinct().Count() ?? 0;
            if (total == 0)
                return 1f;
            if (!TryFindFirstBarcapCoverageGap(
                    commander,
                    request,
                    planningStart,
                    out _,
                    out var uncovered))
                return 1f;

            return Mathf.Clamp01((total - uncovered.Count) / (float)total);
        }

        public bool HasEquivalentDiscreteCommitment(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request)
        {
            var requestsById = commander.MissionRequests
                .GroupBy(candidate => candidate.MissionRequestId)
                .ToDictionary(group => group.Key, group => group.First());
            return commander.Packages
                .Where(package => !package.IsTerminal)
                .Any(package =>
                    requestsById.TryGetValue(package.MissionRequestId, out var origin)
                    && origin.RequestType == request.RequestType
                    && (request.RequestType
                        == AirMissionRequestType.DestructionOfEnemyAirDefenses
                        ? origin.DeadPlan != null
                          && request.DeadPlan != null
                          && origin.DeadPlan.TargetSiteId
                          == request.DeadPlan.TargetSiteId
                        : origin.MissionArea.CenterTileId
                          == request.MissionArea.CenterTileId));
        }

        public IReadOnlyList<AirFlight> GetSupportingFlights(
            AllianceAirTaskingCommander commander,
            AirMissionRequestType supportType,
            AirMissionArea missionArea,
            DateTime start,
            DateTime end)
        {
            return commander.Packages
                .Where(package => !package.IsTerminal)
                .SelectMany(package => package.Flights)
                .Where(flight => !flight.IsTerminal
                                 && flight.MissionType == supportType
                                 && flight.ExecutionPhase
                                 != FlightExecutionPhase.Returning
                                 && flight.ExecutionPhase
                                 != FlightExecutionPhase.Landing
                                 && flight.ExecutionPhase
                                 != FlightExecutionPhase.Ended
                                 && flight.EffectStart <= start
                                 && flight.EffectEnd >= end
                                 && flight.MissionArea.Contains(missionArea.CenterTileId))
                .OrderBy(flight => flight.EffectStart)
                .ThenBy(flight => flight.SquadronId)
                .ToList();
        }

        public int GetAvailableSupportSlots(
            AirFlight supportingFlight,
            DateTime start,
            DateTime end)
        {
            return Math.Max(
                0,
                AirSupportCoveragePlanner.GetMinimumAvailableSlots(
                    supportingFlight,
                    start,
                    end));
        }

        public IReadOnlyList<AirSupportReservation>
            PlanAerialRefuelingCoverage(
                AllianceAirTaskingCommander commander,
                AirMissionArea receiverArea,
                Guid consumingPackageId,
                int requiredSlots,
                DateTime start,
                DateTime requestedEnd,
                out DateTime coveredUntil)
        {
            var candidates = GetAerialRefuelingCandidates(
                commander,
                receiverArea,
                start,
                requestedEnd);
            return AirSupportCoveragePlanner.PlanContinuousCoverage(
                candidates,
                consumingPackageId,
                requiredSlots,
                start,
                requestedEnd,
                out coveredUntil);
        }

        internal IReadOnlyList<TimeSpan>
            GetAerialRefuelingShiftCandidates(
                AllianceAirTaskingCommander commander,
                AirMissionArea receiverArea,
                DateTime effectStart,
                DateTime effectEnd,
                TimeSpan maximumShift)
        {
            if (maximumShift < TimeSpan.Zero)
                return Array.Empty<TimeSpan>();

            var searchEnd = effectEnd + maximumShift;
            var eventTimes = GetAerialRefuelingCandidates(
                    commander,
                    receiverArea,
                    effectStart,
                    searchEnd)
                .SelectMany(flight =>
                    new[] { flight.EffectStart, flight.EffectEnd }
                        .Concat(flight.SupportReservations.SelectMany(
                            reservation => new[]
                            {
                                reservation.StartTime,
                                reservation.EndTime
                            })))
                .Distinct();
            var candidates = new SortedSet<TimeSpan>();
            foreach (var eventTime in eventTimes)
            {
                AddShiftCandidate(
                    candidates,
                    eventTime - effectStart,
                    maximumShift);
                AddShiftCandidate(
                    candidates,
                    eventTime - effectEnd,
                    maximumShift);
            }

            return candidates.ToList();
        }

        private static void AddShiftCandidate(
            ISet<TimeSpan> candidates,
            TimeSpan candidate,
            TimeSpan maximumShift)
        {
            if (candidate >= TimeSpan.Zero
                && candidate <= maximumShift)
            {
                candidates.Add(candidate);
            }
        }

        private static IReadOnlyList<AirFlight>
            GetAerialRefuelingCandidates(
                AllianceAirTaskingCommander commander,
                AirMissionArea receiverArea,
                DateTime start,
                DateTime requestedEnd)
        {
            return commander.Packages
                .Where(package => !package.IsTerminal)
                .SelectMany(package => package.Flights)
                .Where(flight => !flight.IsTerminal
                                 && flight.MissionType
                                 == AirMissionRequestType.ProvideAerialRefueling
                                 && flight.ExecutionPhase
                                 != FlightExecutionPhase.Returning
                                 && flight.ExecutionPhase
                                 != FlightExecutionPhase.Landing
                                 && flight.ExecutionPhase
                                 != FlightExecutionPhase.Ended
                                 && flight.EffectEnd > start
                                 && flight.EffectStart < requestedEnd
                                 && flight.MissionArea.Contains(
                                     receiverArea.CenterTileId))
                .OrderBy(flight => flight.EffectStart)
                .ThenBy(flight => flight.FlightId)
                .ToList();
        }

        private IEnumerable<AirFlight> GetProjectedFlights(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request)
        {
            if (request.RequestType == AirMissionRequestType.BarrierCombatAirPatrol
                && request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained)
            {
                var barrierTiles = request.BarcapBarrier?.BarrierTileIds?
                    .ToHashSet();
                return commander.Packages
                    .Where(package => !package.IsTerminal)
                    .SelectMany(package => package.Flights)
                    .Where(flight => !flight.IsTerminal
                                     && flight.ExecutionPhase
                                     != FlightExecutionPhase.Returning
                                     && flight.ExecutionPhase
                                     != FlightExecutionPhase.Landing
                                     && flight.ExecutionPhase
                                     != FlightExecutionPhase.Ended
                                     && flight.MissionType
                                     == AirMissionRequestType.BarrierCombatAirPatrol
                                     && IsBarcapCoverageApplicable(
                                         flight,
                                         request,
                                         barrierTiles));
            }

            var projected = commander.Packages
                .Where(package => !package.IsTerminal
                                  && package.MissionRequestId == request.MissionRequestId)
                .SelectMany(package => package.Flights)
                .Where(flight => !flight.IsTerminal);
            return request.FulfillmentPattern
                   == AirMissionRequestFulfillmentPattern.Sustained
                ? projected.Where(flight =>
                    flight.ExecutionPhase != FlightExecutionPhase.Returning
                    && flight.ExecutionPhase != FlightExecutionPhase.Landing
                    && flight.ExecutionPhase != FlightExecutionPhase.Ended)
                : projected;
        }

        private bool IsBarcapCoverageApplicable(
            AirFlight flight,
            AirMissionRequest request,
            HashSet<Vector3Int> barrierTiles)
        {
            var coverage = flight.PlannedBarcapCoverage;
            if (barrierTiles == null || barrierTiles.Count == 0)
            {
                return request.MissionArea.Contains(
                           flight.MissionArea.CenterTileId)
                       && flight.MissionArea.Contains(
                           request.MissionArea.CenterTileId);
            }
            if (coverage == null)
                return false;

            return coverage.CoveredBarrierTileIds.Any(barrierTiles.Contains)
                   && RetainsPlannedBarcapWeaponCapability(flight, coverage)
                   && BarcapInterceptGeometry.IsApproachCompatible(
                       coverage,
                       request.BarcapBarrier);
        }

        internal bool RetainsPlannedBarcapWeaponCapability(
            AirFlight flight,
            BarcapStationCoverage coverage)
        {
            if (!flight.IsAirborne
                || coverage.PlannedPreferredLaunchRangeKm <= 0f)
                return true;
            if (gameManager == null
                || !gameManager.squadronSystem.TryGetSquadron(
                    flight.SquadronId,
                    out var squadron))
                return false;

            var longestRemainingPreferredRange = squadron.Aircraft
                .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Lost)
                .SelectMany(aircraft => aircraft.Loadout)
                .Where(item => item.Count > 0
                               && ordnanceTypes.TryGetValue(
                                   item.OrdnanceTypeDefinitionId,
                                   out var ordnance)
                               && AirLoadoutPlanner.IsAirToAir(ordnance))
                .Select(item => ordnanceTypes[item.OrdnanceTypeDefinitionId]
                                .MaximumRangeKm
                                * BarcapPreferredLaunchRangeFraction)
                .DefaultIfEmpty(0f)
                .Max();
            return longestRemainingPreferredRange + 0.001f
                   >= coverage.PlannedPreferredLaunchRangeKm;
        }
    }

}
