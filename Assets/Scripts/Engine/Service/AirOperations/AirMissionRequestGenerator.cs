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

            var ocaCandidates = BuildOcaTargetCandidates(
                snapshot,
                commander.Doctrine);
            var bestPenetrationLayer = ocaCandidates
                .Select(candidate => candidate.InterveningKnownAirCombatAreas)
                .DefaultIfEmpty(0)
                .Min();
            foreach (var candidate in ocaCandidates
                         .Where(candidate =>
                             candidate.InterveningKnownAirCombatAreas == bestPenetrationLayer))
            {
                var ocaRequest = CreateRequest(
                    commander,
                    AirMissionRequestType.OffensiveCounterAirSweep,
                    AirMissionRequestFulfillmentPattern.Sustained,
                    candidate.ProbeCenterTileId,
                    effectStart,
                    effectStart + TimeSpan.FromHours(2),
                    desiredAircraftStrength: candidate.DesiredAircraftStrength,
                    rationale: candidate.InterveningKnownAirCombatAreas > 0
                        ? "Contest the least-screened known hostile air-combat layer"
                        : "Probe the outer edge of known hostile air-combat airspace");
                ocaRequest.PriorityComponents["desiredAircraftStrength"] =
                    candidate.DesiredAircraftStrength;
                ocaRequest.PriorityComponents["ocaPenetrationDepthTiles"] =
                    candidate.PenetrationDepthTiles;
                ocaRequest.PriorityComponents["ocaProbeDepthTiles"] =
                    candidate.ProbeDepthTiles;
                ocaRequest.PriorityComponents["ocaInterveningKnownAirCombatAreas"] =
                    candidate.InterveningKnownAirCombatAreas;
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

        private List<OcaTargetCandidate> BuildOcaTargetCandidates(
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

            var knownHostileAirCombatTiles = snapshot.HostileSquadrons
                .Where(squadron => priorityService.CalculateAirCombatPower(squadron) > 0f)
                .Select(squadron => squadron.AirportTileId)
                .Distinct()
                .ToList();

            return snapshot.HostileAirportTiles
                .Select(airportTile =>
                {
                    var approachOrigin = SelectBestOcaApproachOrigin(
                        friendlyAirCombatOrigins,
                        knownHostileAirCombatTiles,
                        airportTile);
                    var probeCenter = SelectOcaProbeCenter(
                        approachOrigin,
                        airportTile);
                    var missionArea = new AirMissionArea(
                        airportTile,
                        DefaultMissionRadiusTiles);
                    return new OcaTargetCandidate(
                        probeCenter,
                        CalculateDesiredCombatStrength(
                            snapshot,
                            doctrine,
                            missionArea),
                        CalculatePenetrationDepthTiles(
                            friendlyAirCombatOrigins,
                            airportTile),
                        CalculatePenetrationDepthTiles(
                            friendlyAirCombatOrigins,
                            probeCenter),
                        CalculateInterveningKnownAirCombatAreas(
                            friendlyAirCombatOrigins,
                            knownHostileAirCombatTiles,
                            airportTile));
                })
                .OrderBy(candidate => candidate.InterveningKnownAirCombatAreas)
                .ThenBy(candidate => candidate.PenetrationDepthTiles)
                .ThenBy(candidate => candidate.ProbeDepthTiles)
                .ThenBy(candidate => candidate.ProbeCenterTileId.x)
                .ThenBy(candidate => candidate.ProbeCenterTileId.y)
                .ThenBy(candidate => candidate.ProbeCenterTileId.z)
                .ToList();
        }

        private static Vector3Int SelectBestOcaApproachOrigin(
            IReadOnlyList<Vector3Int> friendlyOrigins,
            IReadOnlyList<Vector3Int> knownHostileAirCombatTiles,
            Vector3Int objectiveTileId)
        {
            return friendlyOrigins
                .OrderBy(origin => knownHostileAirCombatTiles
                    .Where(hostileTile => hostileTile != objectiveTileId)
                    .Count(hostileTile => IsInterveningTile(
                        origin,
                        hostileTile,
                        objectiveTileId)))
                .ThenBy(origin => AirMissionArea.HexDistance(
                    origin,
                    objectiveTileId))
                .ThenBy(origin => origin.x)
                .ThenBy(origin => origin.y)
                .ThenBy(origin => origin.z)
                .FirstOrDefault();
        }

        private static Vector3Int SelectOcaProbeCenter(
            Vector3Int approachOriginTileId,
            Vector3Int objectiveTileId)
        {
            var objectiveDistance = AirMissionArea.HexDistance(
                approachOriginTileId,
                objectiveTileId);
            if (objectiveDistance <= DefaultMissionRadiusTiles)
                return objectiveTileId;

            var fractionFromObjective = DefaultMissionRadiusTiles / (float)objectiveDistance;
            return CubeRound(
                objectiveTileId.x
                + (approachOriginTileId.x - objectiveTileId.x) * fractionFromObjective,
                objectiveTileId.y
                + (approachOriginTileId.y - objectiveTileId.y) * fractionFromObjective,
                objectiveTileId.z
                + (approachOriginTileId.z - objectiveTileId.z) * fractionFromObjective);
        }

        private static Vector3Int CubeRound(float x, float y, float z)
        {
            var roundedX = Mathf.RoundToInt(x);
            var roundedY = Mathf.RoundToInt(y);
            var roundedZ = Mathf.RoundToInt(z);

            var xDifference = Math.Abs(roundedX - x);
            var yDifference = Math.Abs(roundedY - y);
            var zDifference = Math.Abs(roundedZ - z);

            if (xDifference > yDifference && xDifference > zDifference)
                roundedX = -roundedY - roundedZ;
            else if (yDifference > zDifference)
                roundedY = -roundedX - roundedZ;
            else
                roundedZ = -roundedX - roundedY;

            return new Vector3Int(roundedX, roundedY, roundedZ);
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

        private static int CalculateInterveningKnownAirCombatAreas(
            IReadOnlyList<Vector3Int> friendlyOrigins,
            IReadOnlyList<Vector3Int> knownHostileAirCombatTiles,
            Vector3Int targetTileId)
        {
            return friendlyOrigins
                .Select(origin => knownHostileAirCombatTiles
                    .Where(hostileTile => hostileTile != targetTileId)
                    .Count(hostileTile => IsInterveningTile(
                        origin,
                        hostileTile,
                        targetTileId)))
                .DefaultIfEmpty(0)
                .Min();
        }

        private static bool IsInterveningTile(
            Vector3Int originTileId,
            Vector3Int blockerTileId,
            Vector3Int targetTileId)
        {
            var originToTarget = AirMissionArea.HexDistance(
                originTileId,
                targetTileId);
            var originToBlocker = AirMissionArea.HexDistance(
                originTileId,
                blockerTileId);
            if (originToBlocker <= 0 || originToBlocker >= originToTarget)
                return false;

            var blockerToTarget = AirMissionArea.HexDistance(
                blockerTileId,
                targetTileId);
            return originToBlocker + blockerToTarget
                   <= originToTarget + DefaultMissionRadiusTiles;
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
            public readonly Vector3Int ProbeCenterTileId;
            public readonly int DesiredAircraftStrength;
            public readonly int PenetrationDepthTiles;
            public readonly int ProbeDepthTiles;
            public readonly int InterveningKnownAirCombatAreas;

            public OcaTargetCandidate(
                Vector3Int probeCenterTileId,
                int desiredAircraftStrength,
                int penetrationDepthTiles,
                int probeDepthTiles,
                int interveningKnownAirCombatAreas)
            {
                ProbeCenterTileId = probeCenterTileId;
                DesiredAircraftStrength = Math.Max(0, desiredAircraftStrength);
                PenetrationDepthTiles = Math.Max(0, penetrationDepthTiles);
                ProbeDepthTiles = Math.Max(0, probeDepthTiles);
                InterveningKnownAirCombatAreas = Math.Max(0, interveningKnownAirCombatAreas);
            }
        }
    }

}
