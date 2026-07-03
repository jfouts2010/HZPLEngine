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
        public bool TryFindFirstCoverageGap(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime planningStart,
            out DateTime gapStart,
            out int projectedAmount)
        {
            gapStart = planningStart;
            projectedAmount = 0;
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
                    && origin.MissionArea.CenterTileId == request.MissionArea.CenterTileId);
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
            var reserved = supportingFlight.SupportReservations
                .Where(reservation => reservation.StartTime < end
                                      && reservation.EndTime > start)
                .Sum(reservation => Math.Max(0, reservation.SlotCount));
            return Math.Max(0, supportingFlight.ProvidedSupportSlots - reserved);
        }

        private static IEnumerable<AirFlight> GetProjectedFlights(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request)
        {
            return commander.Packages
                .Where(package => !package.IsTerminal
                                  && package.MissionRequestId == request.MissionRequestId)
                .SelectMany(package => package.Flights)
                .Where(flight => !flight.IsTerminal);
        }
    }

}
