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
    public sealed class AirMissionRequestGenerator
    {
        private const int DefaultMissionRadiusTiles = 2;
        private const int DefaultCombatFlightStrength = 4;
        private static readonly TimeSpan HandoffBuffer = TimeSpan.FromMinutes(30);

        private readonly AirMissionPriorityService priorityService;

        public AirMissionRequestGenerator(AirMissionPriorityService priorityService)
        {
            this.priorityService = priorityService;
        }

        public List<AirMissionRequest> Generate(
            AllianceAirTaskingCommander commander,
            AirPlanningSnapshot snapshot,
            int operationalCadenceHours)
        {
            var generated = new List<AirMissionRequest>();
            var effectStart = snapshot.CurrentTime + AirPackage.PreparationDelay;
            var effectEnd = snapshot.CurrentTime
                            + TimeSpan.FromHours(Math.Max(1, operationalCadenceHours))
                            + HandoffBuffer;

            foreach (var airportTile in snapshot.FriendlyAirportTiles)
            {
                var friendlyMissionArea = new AirMissionArea(
                    airportTile,
                    DefaultMissionRadiusTiles);
                if (priorityService.CalculatePowerNear(
                        snapshot.FriendlySquadrons,
                        friendlyMissionArea) > 0f)
                {
                    var desiredStrength = CalculateDesiredCombatStrength(
                        snapshot,
                        commander.Doctrine,
                        friendlyMissionArea);
                    var dcaRequest = CreateRequest(
                        commander,
                        AirMissionRequestType.DefensiveCounterAirPatrol,
                        AirMissionRequestFulfillmentPattern.Sustained,
                        airportTile,
                        effectStart,
                        effectEnd,
                        desiredAircraftStrength: desiredStrength,
                        rationale: "Protect friendly air operations and nearby airspace");
                    dcaRequest.PriorityComponents["desiredAircraftStrength"] = desiredStrength;
                    generated.Add(dcaRequest);
                }

                if (commander.Doctrine.BaselineAirborneC2Slots > 0)
                {
                    generated.Add(CreateRequest(
                        commander,
                        AirMissionRequestType.ProvideAirborneC2,
                        AirMissionRequestFulfillmentPattern.Sustained,
                        airportTile,
                        effectStart,
                        effectEnd,
                        desiredSupportSlots: commander.Doctrine.BaselineAirborneC2Slots,
                        rationale: "Provide baseline airborne C2 coverage"));
                }
            }

            foreach (var airportTile in snapshot.HostileAirportTiles)
            {
                if (!snapshot.FriendlySquadrons.Any(squadron =>
                        squadron.ReadyAircraftCount > 0
                        && priorityService.CalculateAirCombatPower(squadron) > 0f))
                    break;

                var missionArea = new AirMissionArea(airportTile, DefaultMissionRadiusTiles);
                var desiredStrength = CalculateDesiredCombatStrength(
                    snapshot,
                    commander.Doctrine,
                    missionArea);
                var ocaRequest = CreateRequest(
                    commander,
                    AirMissionRequestType.OffensiveCounterAirSweep,
                    AirMissionRequestFulfillmentPattern.Discrete,
                    airportTile,
                    effectStart,
                    effectStart + TimeSpan.FromHours(2),
                    desiredAircraftStrength: desiredStrength,
                    rationale: "Contest hostile air activity near an enemy operating base");
                ocaRequest.PriorityComponents["desiredAircraftStrength"] = desiredStrength;
                generated.Add(ocaRequest);
            }

            var combatRequests = generated
                .Where(request => !request.IsSupportRequest)
                .ToList();
            foreach (var airportTile in snapshot.FriendlyAirportTiles)
            {
                var baseline = commander.Doctrine.BaselineAerialRefuelingSlots;
                var observed = CalculateObservedTankerDemand(commander, airportTile, snapshot.CurrentTime);
                var forecast = combatRequests
                    .Where(request => request.MissionArea.Contains(airportTile)
                                      || new AirMissionArea(airportTile, DefaultMissionRadiusTiles)
                                          .Contains(request.MissionArea.CenterTileId))
                    .Sum(request => request.DesiredAircraftStrength);
                var desiredSlots = Math.Max(0, baseline + observed + forecast);
                if (desiredSlots == 0)
                    continue;

                var tankerRequest = CreateRequest(
                    commander,
                    AirMissionRequestType.ProvideAerialRefueling,
                    AirMissionRequestFulfillmentPattern.Sustained,
                    airportTile,
                    effectStart,
                    effectEnd,
                    desiredSupportSlots: desiredSlots,
                    rationale: "Provide blended baseline, observed, and forecast aerial-refueling capacity");
                tankerRequest.PriorityComponents["baselineDemand"] = baseline;
                tankerRequest.PriorityComponents["observedDemand"] = observed;
                tankerRequest.PriorityComponents["forecastDemand"] = forecast;
                generated.Add(tankerRequest);
            }

            foreach (var request in generated)
                priorityService.Score(request, commander.Doctrine, snapshot);

            return generated
                .OrderByDescending(request => request.Priority)
                .ThenBy(request => request.RequestType)
                .ThenBy(request => request.MissionArea.CenterTileId.x)
                .ThenBy(request => request.MissionArea.CenterTileId.y)
                .ThenBy(request => request.MissionArea.CenterTileId.z)
                .ToList();
        }

        private static AirMissionRequest CreateRequest(
            AllianceAirTaskingCommander commander,
            AirMissionRequestType requestType,
            AirMissionRequestFulfillmentPattern fulfillmentPattern,
            Vector3Int centerTile,
            DateTime effectStart,
            DateTime effectEnd,
            int desiredAircraftStrength = 0,
            int desiredSupportSlots = 0,
            string rationale = "")
        {
            return new AirMissionRequest
            {
                Alliance = commander.Alliance,
                RequestType = requestType,
                FulfillmentPattern = fulfillmentPattern,
                MissionArea = new AirMissionArea(centerTile, DefaultMissionRadiusTiles),
                CreatedAt = effectStart - AirPackage.PreparationDelay,
                EffectStart = effectStart,
                EffectEnd = effectEnd,
                PlanningCycle = commander.PlanningCycle,
                DesiredAircraftStrength = Math.Max(0, desiredAircraftStrength),
                DesiredSupportSlots = Math.Max(0, desiredSupportSlots),
                Rationale = rationale
            };
        }

        private int CalculateDesiredCombatStrength(
            AirPlanningSnapshot snapshot,
            AllianceAirDoctrine doctrine,
            AirMissionArea missionArea)
        {
            var friendlyPower = priorityService.CalculatePowerNear(
                snapshot.FriendlySquadrons,
                missionArea);
            var hostilePower = priorityService.CalculatePowerNear(
                snapshot.HostileSquadrons,
                missionArea);
            if (hostilePower <= 0f)
                return Math.Max(2, DefaultCombatFlightStrength / 2);

            var desiredAdvantage = Math.Max(
                0.1f,
                doctrine.DesiredAirCombatAdvantage);
            var pressureRatio = hostilePower * desiredAdvantage / Math.Max(0.1f, friendlyPower);
            var strengthScale = Mathf.Clamp(pressureRatio, 0.5f, 2f);
            return Math.Max(
                2,
                (int)Math.Ceiling(DefaultCombatFlightStrength * strengthScale));
        }

        private static int CalculateObservedTankerDemand(
            AllianceAirTaskingCommander commander,
            Vector3Int centerTile,
            DateTime currentTime)
        {
            var recentThreshold = currentTime - TimeSpan.FromHours(24);
            var demandArea = new AirMissionArea(centerTile, DefaultMissionRadiusTiles);
            return commander.SupportDemandHistory
                .Where(sample =>
                    sample.SupportType == AirMissionRequestType.ProvideAerialRefueling
                                 && sample.RecordedAt >= recentThreshold
                                 && demandArea.Contains(sample.MissionArea.CenterTileId))
                .Sum(sample => Math.Max(0, sample.RequestedSlots));
        }
    }

}
