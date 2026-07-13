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
        private const int MaximumOcaAircraftStrength = 8;
        private const float StrongFriendlyAdvantage = 0.40f;
        private const float MeaningfulCombatPresence = 0.10f;
        private const float MeaningfulAirActivity = 0.10f;
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
                        rationale: "Maintain defensive counter-air coverage over friendly air operations");
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

            var ocaCandidates = BuildOcaTargetCandidates(
                commander,
                snapshot,
                commander.Doctrine)
                .Where(candidate =>
                    candidate.AirControlAdvantage < StrongFriendlyAdvantage
                    && (candidate.HostileCombatPresence >= MeaningfulCombatPresence
                        || candidate.HostileAirActivity >= MeaningfulAirActivity)
                    && candidate.HostileCombatPresence <= Mathf.Lerp(
                        0.35f,
                        0.85f,
                        commander.Doctrine.RiskTolerance))
                .ToList();
            var bestPenetrationLayer = ocaCandidates
                .Select(candidate => candidate.PenetrationDepthTiles)
                .DefaultIfEmpty(int.MaxValue)
                .Min();
            var selectedOcaCandidate = ocaCandidates
                .Where(candidate => candidate.PenetrationDepthTiles == bestPenetrationLayer)
                .OrderByDescending(candidate => candidate.HostileAirActivity)
                .ThenByDescending(candidate => candidate.HostileCombatPresence)
                .ThenBy(candidate => candidate.AirControlAdvantage)
                .ThenBy(candidate => candidate.FrontierTileId.x)
                .ThenBy(candidate => candidate.FrontierTileId.y)
                .ThenBy(candidate => candidate.FrontierTileId.z)
                .FirstOrDefault();
            if (selectedOcaCandidate != null)
            {
                var ocaRequest = CreateRequest(
                    commander,
                    AirMissionRequestType.OffensiveCounterAirSweep,
                    AirMissionRequestFulfillmentPattern.Discrete,
                    selectedOcaCandidate.FrontierTileId,
                    effectStart,
                    effectEnd,
                    desiredAircraftStrength: selectedOcaCandidate.DesiredAircraftStrength,
                    rationale: "Contest the nearest active hostile air-control frontier");
                ocaRequest.PriorityComponents["desiredAircraftStrength"] =
                    selectedOcaCandidate.DesiredAircraftStrength;
                ocaRequest.PriorityComponents["ocaPenetrationDepthTiles"] =
                    selectedOcaCandidate.PenetrationDepthTiles;
                ocaRequest.PriorityComponents["ocaAirControlAdvantage"] =
                    selectedOcaCandidate.AirControlAdvantage;
                ocaRequest.PriorityComponents["ocaHostileCombatPresence"] =
                    selectedOcaCandidate.HostileCombatPresence;
                ocaRequest.PriorityComponents["ocaHostileAirActivity"] =
                    selectedOcaCandidate.HostileAirActivity;
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
            {
                var airControl = CalculateAreaAirControl(
                    commander,
                    request.MissionArea);
                priorityService.Score(
                    request,
                    commander.Doctrine,
                    snapshot,
                    airControl.FriendlyPresence,
                    airControl.HostilePresence,
                    airControl.Advantage,
                    airControl.Activity,
                    airControl.HostileActivity);
            }

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

        private List<OcaTargetCandidate> BuildOcaTargetCandidates(
            AllianceAirTaskingCommander commander,
            AirPlanningSnapshot snapshot,
            AllianceAirDoctrine doctrine)
        {
            var friendlyAirCombatOrigins = snapshot.FriendlySquadrons
                .Where(squadron =>
                    squadron.ReadyAircraftCount > 0
                    && priorityService.CalculateAirCombatPower(squadron) > 0f)
                .Select(squadron => squadron.AirportTileId)
                .Distinct()
                .ToList();
            if (friendlyAirCombatOrigins.Count == 0)
                return new List<OcaTargetCandidate>();

            return commander.AirControlAssessments
                .Where(assessment =>
                    assessment.AirControlAdvantage < StrongFriendlyAdvantage
                    && (assessment.HostileCombatPresence >= MeaningfulCombatPresence
                        || assessment.HostileAirActivity >= MeaningfulAirActivity))
                .Select(assessment =>
                {
                    var approachOrigin = SelectNearestOcaApproachOrigin(
                        friendlyAirCombatOrigins,
                        assessment.TileId);
                    if (!IsFriendlyFacingControlFrontier(
                            commander,
                            approachOrigin,
                            assessment))
                        return null;

                    var airControl = CalculateAreaAirControl(
                        commander,
                        new AirMissionArea(
                            assessment.TileId,
                            DefaultMissionRadiusTiles));
                    return new OcaTargetCandidate(
                        assessment.TileId,
                        CalculateOcaDesiredStrength(
                            snapshot,
                            doctrine,
                            airControl.HostilePower),
                        CalculatePenetrationDepthTiles(
                            friendlyAirCombatOrigins,
                            assessment.TileId),
                        airControl.Advantage,
                        airControl.HostilePresence,
                        airControl.HostileActivity);
                })
                .Where(candidate => candidate != null)
                .OrderBy(candidate => candidate.PenetrationDepthTiles)
                .ThenBy(candidate => candidate.FrontierTileId.x)
                .ThenBy(candidate => candidate.FrontierTileId.y)
                .ThenBy(candidate => candidate.FrontierTileId.z)
                .ToList();
        }

        private int CalculateOcaDesiredStrength(
            AirPlanningSnapshot snapshot,
            AllianceAirDoctrine doctrine,
            float hostileCombatPower)
        {
            var averageFriendlyAircraftPower = snapshot.FriendlySquadrons
                .Where(squadron => squadron.ReadyAircraftCount > 0)
                .Select(squadron => priorityService.CalculateAirCombatPower(squadron)
                                     / Math.Max(
                                         1,
                                         squadron.ReadyAircraftCount
                                         + squadron.AssignedAircraftCount))
                .DefaultIfEmpty(1f)
                .Average();
            var required = Mathf.CeilToInt(
                Mathf.Max(0f, hostileCombatPower)
                * Math.Max(0.1f, doctrine.DesiredAirCombatAdvantage)
                / Math.Max(0.1f, averageFriendlyAircraftPower));
            return Mathf.Clamp(required, 2, MaximumOcaAircraftStrength);
        }

        private static (
            float FriendlyPresence,
            float HostilePresence,
            float Advantage,
            float Activity,
            float HostileActivity,
            float HostilePower) CalculateAreaAirControl(
            AllianceAirTaskingCommander commander,
            AirMissionArea missionArea)
        {
            var assessments = commander.AirControlAssessments
                .Where(assessment => missionArea.Contains(assessment.TileId))
                .ToList();
            if (assessments.Count == 0)
                return (0f, 0f, 0f, 0f, 0f, 0f);

            return (
                Mathf.Clamp01(assessments.Average(
                    assessment => assessment.FriendlyCombatPresence)),
                Mathf.Clamp01(assessments.Average(
                    assessment => assessment.HostileCombatPresence)),
                Mathf.Clamp(assessments.Average(
                    assessment => assessment.AirControlAdvantage), -1f, 1f),
                Mathf.Clamp01(assessments.Average(assessment => assessment.AirActivity)),
                Mathf.Clamp01(assessments.Average(
                    assessment => assessment.HostileAirActivity)),
                Mathf.Max(0f, assessments.Max(
                    assessment => assessment.HostileCombatPower)));
        }

        private static Vector3Int SelectNearestOcaApproachOrigin(
            IReadOnlyList<Vector3Int> friendlyOrigins,
            Vector3Int objectiveTileId)
        {
            return friendlyOrigins
                .OrderBy(origin => AirMissionArea.HexDistance(
                    origin,
                    objectiveTileId))
                .ThenBy(origin => origin.x)
                .ThenBy(origin => origin.y)
                .ThenBy(origin => origin.z)
                .FirstOrDefault();
        }

        private static bool IsFriendlyFacingControlFrontier(
            AllianceAirTaskingCommander commander,
            Vector3Int approachOriginTileId,
            AirControlTileAssessment assessment)
        {
            var currentDistance = AirMissionArea.HexDistance(
                approachOriginTileId,
                assessment.TileId);
            return AirspaceGeometry.NeighborTiles(assessment.TileId)
                .Where(neighbor => AirMissionArea.HexDistance(
                    approachOriginTileId,
                    neighbor) < currentDistance)
                .Any(neighbor =>
                    commander.TryGetAirControlAssessment(neighbor, out var neighborAssessment)
                    && (neighborAssessment.HostileCombatPresence
                        < assessment.HostileCombatPresence
                        || neighborAssessment.AirControlAdvantage
                        > assessment.AirControlAdvantage));
        }

        private static int CalculatePenetrationDepthTiles(
            IReadOnlyList<Vector3Int> friendlyOrigins,
            Vector3Int targetTileId)
        {
            return friendlyOrigins
                .Select(origin => AirMissionArea.HexDistance(origin, targetTileId))
                .DefaultIfEmpty(0)
                .Min();
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

        private sealed class OcaTargetCandidate
        {
            public readonly Vector3Int FrontierTileId;
            public readonly int DesiredAircraftStrength;
            public readonly int PenetrationDepthTiles;
            public readonly float AirControlAdvantage;
            public readonly float HostileCombatPresence;
            public readonly float HostileAirActivity;

            public OcaTargetCandidate(
                Vector3Int frontierTileId,
                int desiredAircraftStrength,
                int penetrationDepthTiles,
                float airControlAdvantage,
                float hostileCombatPresence,
                float hostileAirActivity)
            {
                FrontierTileId = frontierTileId;
                DesiredAircraftStrength = Math.Max(0, desiredAircraftStrength);
                PenetrationDepthTiles = Math.Max(0, penetrationDepthTiles);
                AirControlAdvantage = Mathf.Clamp(airControlAdvantage, -1f, 1f);
                HostileCombatPresence = Mathf.Clamp01(hostileCombatPresence);
                HostileAirActivity = Mathf.Clamp01(hostileAirActivity);
            }
        }

    }

}
