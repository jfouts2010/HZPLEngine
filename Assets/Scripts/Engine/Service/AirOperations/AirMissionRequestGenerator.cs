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
        private const int MaximumDcaAircraftStrength = 8;
        private const int MaximumOcaAircraftStrength = 8;
        private const float StrongFriendlyAdvantage = 0.40f;
        private const float MeaningfulCombatPresence = 0.10f;
        private const float MeaningfulAirActivity = 0.10f;
        private const float MeaningfulFriendlyOperations = 0.15f;
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

            var dcaCandidates = BuildDcaSectorCandidates(
                commander,
                snapshot,
                commander.Doctrine);
            var baselineDcaCandidate = dcaCandidates
                .OrderByDescending(candidate => candidate.PriorityScore)
                .ThenBy(candidate => candidate.MissionArea.CenterTileId.x)
                .ThenBy(candidate => candidate.MissionArea.CenterTileId.y)
                .ThenBy(candidate => candidate.MissionArea.CenterTileId.z)
                .FirstOrDefault();
            var selectedDcaCandidates = dcaCandidates
                .Where(candidate => candidate == baselineDcaCandidate
                                    || candidate.HostilePressure >= MeaningfulCombatPresence
                                    || candidate.FriendlyOperations >= MeaningfulFriendlyOperations)
                .OrderByDescending(candidate => candidate.PriorityScore)
                .ThenBy(candidate => candidate.MissionArea.CenterTileId.x)
                .ThenBy(candidate => candidate.MissionArea.CenterTileId.y)
                .ThenBy(candidate => candidate.MissionArea.CenterTileId.z)
                .ToList();

            foreach (var candidate in selectedDcaCandidates)
            {
                var dcaRequest = CreateRequest(
                    commander,
                    AirMissionRequestType.DefensiveCounterAirPatrol,
                    AirMissionRequestFulfillmentPattern.Sustained,
                    candidate.MissionArea.CenterTileId,
                    effectStart,
                    effectEnd,
                    desiredAircraftStrength: candidate.DesiredAircraftStrength,
                    rationale: candidate == baselineDcaCandidate
                        ? "Maintain baseline defensive counter-air coverage over the alliance's highest-priority fighter sector"
                        : "Reinforce a fighter sector exposed to hostile air pressure or friendly air operations",
                    radiusTiles: candidate.MissionArea.RadiusTiles);
                dcaRequest.PriorityComponents["desiredAircraftStrength"] =
                    candidate.DesiredAircraftStrength;
                dcaRequest.PriorityComponents["dcaFighterBaseCount"] =
                    candidate.FighterBaseCount;
                dcaRequest.PriorityComponents["dcaFriendlyAirCombatPower"] =
                    candidate.FriendlyAirCombatPower;
                dcaRequest.PriorityComponents["dcaHostileAirCombatPower"] =
                    candidate.HostileAirCombatPower;
                dcaRequest.PriorityComponents["dcaHostilePressure"] =
                    candidate.HostilePressure;
                dcaRequest.PriorityComponents["dcaFriendlyOperations"] =
                    candidate.FriendlyOperations;
                dcaRequest.PriorityComponents["dcaSectorPriority"] =
                    candidate.PriorityScore;
                generated.Add(dcaRequest);
            }

            foreach (var airportTile in snapshot.FriendlyAirportTiles)
            {
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
            string rationale = "",
            int radiusTiles = DefaultMissionRadiusTiles)
        {
            return new AirMissionRequest
            {
                Alliance = commander.Alliance,
                RequestType = requestType,
                FulfillmentPattern = fulfillmentPattern,
                MissionArea = new AirMissionArea(centerTile, radiusTiles),
                CreatedAt = effectStart - AirPackage.PreparationDelay,
                EffectStart = effectStart,
                EffectEnd = effectEnd,
                PlanningCycle = commander.PlanningCycle,
                DesiredAircraftStrength = Math.Max(0, desiredAircraftStrength),
                DesiredSupportSlots = Math.Max(0, desiredSupportSlots),
                Rationale = rationale
            };
        }

        private int CalculateDesiredDcaStrength(
            AirPlanningSnapshot snapshot,
            AllianceAirDoctrine doctrine,
            float hostileAirCombatPower,
            float hostileAirActivity)
        {
            if (hostileAirCombatPower <= 0f && hostileAirActivity <= 0f)
                return Math.Max(2, DefaultCombatFlightStrength / 2);

            var averageFriendlyAircraftPower = snapshot.FriendlySquadrons
                .Where(squadron => squadron.ReadyAircraftCount > 0
                                   && priorityService.CalculateAirCombatPower(squadron) > 0f)
                .Select(squadron => priorityService.CalculateAirCombatPower(squadron)
                                     / Math.Max(
                                         1,
                                         squadron.ReadyAircraftCount
                                         + squadron.AssignedAircraftCount))
                .DefaultIfEmpty(1f)
                .Average();
            var powerRequired = Mathf.CeilToInt(
                hostileAirCombatPower
                * Math.Max(0.1f, doctrine.DesiredAirCombatAdvantage)
                / Math.Max(0.1f, averageFriendlyAircraftPower));
            var activityRequired = Mathf.CeilToInt(
                Mathf.Clamp01(hostileAirActivity)
                * 8f
                * Math.Max(0.1f, doctrine.DesiredAirCombatAdvantage));
            var required = Math.Max(powerRequired, activityRequired);
            return Mathf.Clamp(required, 2, MaximumDcaAircraftStrength);
        }

        private List<DcaSectorCandidate> BuildDcaSectorCandidates(
            AllianceAirTaskingCommander commander,
            AirPlanningSnapshot snapshot,
            AllianceAirDoctrine doctrine)
        {
            var fighterSquadrons = snapshot.FriendlySquadrons
                .Where(squadron => priorityService.CalculateAirCombatPower(squadron) > 0f)
                .ToList();
            var fighterAirportTiles = fighterSquadrons
                .Select(squadron => squadron.AirportTileId)
                .Distinct()
                .ToList();
            var totalFriendlyAirCombatPower = fighterSquadrons
                .Sum(priorityService.CalculateAirCombatPower);
            var totalFriendlyAircraft = Math.Max(
                1,
                fighterSquadrons.Sum(squadron =>
                    squadron.ReadyAircraftCount + squadron.AssignedAircraftCount));
            var candidates = new List<DcaSectorCandidate>();

            foreach (var airportGroup in GroupNearbyAirportTiles(
                         fighterAirportTiles))
            {
                var centerTile = SelectSectorCenter(airportGroup);
                var radiusTiles = Math.Max(
                    DefaultMissionRadiusTiles,
                    airportGroup.Max(tile => AirMissionArea.HexDistance(
                        centerTile,
                        tile)));
                var missionArea = new AirMissionArea(centerTile, radiusTiles);
                var friendlyAirCombatPower = priorityService.CalculatePowerNear(
                    snapshot.FriendlySquadrons,
                    missionArea);
                if (friendlyAirCombatPower <= 0f)
                    continue;

                var airControl = CalculateAreaAirControl(commander, missionArea);
                var hostilePressure = Mathf.Max(
                    airControl.HostilePresence,
                    airControl.HostileActivity);
                var friendlyOperationalAircraft = commander.Packages
                    .Where(package => !package.IsTerminal)
                    .SelectMany(package => package.Flights)
                    .Where(flight => !flight.IsTerminal
                                     && flight.MissionType
                                     != AirMissionRequestType.DefensiveCounterAirPatrol
                                     && (missionArea.Contains(
                                             flight.MissionArea.CenterTileId)
                                         || flight.MissionArea.Contains(centerTile)))
                    .Sum(flight => flight.AircraftIds.Count);
                var friendlyOperations = Mathf.Clamp01(
                    friendlyOperationalAircraft / (float)totalFriendlyAircraft);
                var strategicValue = Mathf.Clamp01(
                    friendlyAirCombatPower
                    / Math.Max(0.1f, totalFriendlyAirCombatPower));
                var priorityScore = strategicValue
                                    + hostilePressure * 1.5f
                                    + friendlyOperations * 0.75f;
                candidates.Add(new DcaSectorCandidate(
                    missionArea,
                    airportGroup.Count,
                    friendlyAirCombatPower,
                    airControl.HostilePower,
                    hostilePressure,
                    friendlyOperations,
                    priorityScore,
                    CalculateDesiredDcaStrength(
                        snapshot,
                        doctrine,
                        airControl.HostilePower,
                        airControl.HostileActivity)));
            }

            return candidates;
        }

        private static List<List<Vector3Int>> GroupNearbyAirportTiles(
            IEnumerable<Vector3Int> airportTiles)
        {
            var remaining = airportTiles
                .Distinct()
                .OrderBy(tile => tile.x)
                .ThenBy(tile => tile.y)
                .ThenBy(tile => tile.z)
                .ToList();
            var groups = new List<List<Vector3Int>>();
            while (remaining.Count > 0)
            {
                var seed = remaining[0];
                var group = remaining
                    .Where(tile => AirMissionArea.HexDistance(seed, tile)
                                   <= DefaultMissionRadiusTiles * 2)
                    .ToList();
                groups.Add(group);
                foreach (var tile in group)
                    remaining.Remove(tile);
            }

            return groups;
        }

        private static Vector3Int SelectSectorCenter(
            IReadOnlyCollection<Vector3Int> airportTiles)
        {
            return airportTiles
                .OrderBy(candidate => airportTiles.Sum(tile =>
                    AirMissionArea.HexDistance(candidate, tile)))
                .ThenBy(candidate => candidate.x)
                .ThenBy(candidate => candidate.y)
                .ThenBy(candidate => candidate.z)
                .First();
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

        private sealed class DcaSectorCandidate
        {
            public readonly AirMissionArea MissionArea;
            public readonly int FighterBaseCount;
            public readonly float FriendlyAirCombatPower;
            public readonly float HostileAirCombatPower;
            public readonly float HostilePressure;
            public readonly float FriendlyOperations;
            public readonly float PriorityScore;
            public readonly int DesiredAircraftStrength;

            public DcaSectorCandidate(
                AirMissionArea missionArea,
                int fighterBaseCount,
                float friendlyAirCombatPower,
                float hostileAirCombatPower,
                float hostilePressure,
                float friendlyOperations,
                float priorityScore,
                int desiredAircraftStrength)
            {
                MissionArea = missionArea;
                FighterBaseCount = Math.Max(0, fighterBaseCount);
                FriendlyAirCombatPower = Mathf.Max(0f, friendlyAirCombatPower);
                HostileAirCombatPower = Mathf.Max(0f, hostileAirCombatPower);
                HostilePressure = Mathf.Clamp01(hostilePressure);
                FriendlyOperations = Mathf.Clamp01(friendlyOperations);
                PriorityScore = Mathf.Max(0f, priorityScore);
                DesiredAircraftStrength = Math.Max(2, desiredAircraftStrength);
            }
        }

    }

}
