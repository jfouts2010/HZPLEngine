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
        private const int MaximumBarcapAircraftStrength = 8;
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

            var selectedBarcapCandidates = SelectNonOverlappingBarcapCandidates(
                BuildBarcapFrontCandidates(
                    commander,
                    snapshot,
                    commander.Doctrine));

            foreach (var candidate in selectedBarcapCandidates)
            {
                var barcapRequest = CreateRequest(
                    commander,
                    AirMissionRequestType.BarrierCombatAirPatrol,
                    AirMissionRequestFulfillmentPattern.Sustained,
                    candidate.MissionArea.CenterTileId,
                    effectStart,
                    effectEnd,
                    desiredAircraftStrength: candidate.DesiredAircraftStrength,
                    rationale: candidate.UsesAirfieldBootstrap
                        ? "Establish initial BARCAP coverage over an operational airfield"
                        : "Hold the friendly-facing air-control frontier",
                    radiusTiles: candidate.MissionArea.RadiusTiles);
                barcapRequest.PriorityComponents["desiredAircraftStrength"] =
                    candidate.DesiredAircraftStrength;
                barcapRequest.PriorityComponents["barcapHostileAirCombatPower"] =
                    candidate.HostileAirCombatPower;
                barcapRequest.PriorityComponents["barcapHostilePressure"] =
                    candidate.HostilePressure;
                barcapRequest.PriorityComponents["barcapAirControlAdvantage"] =
                    candidate.AirControlAdvantage;
                barcapRequest.PriorityComponents["barcapFrontPriority"] =
                    candidate.PriorityScore;
                barcapRequest.PriorityComponents["barcapFighterTransitDistanceTiles"] =
                    candidate.FighterTransitDistanceTiles;
                barcapRequest.PriorityComponents["barcapAirfieldBootstrap"] =
                    candidate.UsesAirfieldBootstrap ? 1f : 0f;
                generated.Add(barcapRequest);
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
                ocaRequest.PriorityComponents["ocaHostileAirCombatPower"] =
                    selectedOcaCandidate.HostileAirCombatPower;
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

        private int CalculateDesiredBarcapStrength(
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
            return Mathf.Clamp(required, 2, MaximumBarcapAircraftStrength);
        }

        private List<BarcapFrontCandidate> BuildBarcapFrontCandidates(
            AllianceAirTaskingCommander commander,
            AirPlanningSnapshot snapshot,
            AllianceAirDoctrine doctrine)
        {
            var friendlyAirCombatOrigins = snapshot.FriendlySquadrons
                .Where(squadron => squadron.ReadyAircraftCount > 0
                                   && priorityService.CalculateAirCombatPower(squadron) > 0f)
                .Select(squadron => squadron.AirportTileId)
                .Distinct()
                .ToList();
            if (friendlyAirCombatOrigins.Count == 0)
                return new List<BarcapFrontCandidate>();

            var airControlCandidates = commander.AirControlAssessments
                .Where(assessment =>
                    assessment.AirControlAdvantage < 0f
                    && (assessment.HostileCombatPresence >= MeaningfulCombatPresence
                        || assessment.HostileAirActivity >= MeaningfulAirActivity))
                .Select(assessment =>
                {
                    var approachOrigin = SelectNearestFighterOrigin(
                        friendlyAirCombatOrigins,
                        assessment.TileId);
                    if (!TrySelectDefensiveBarcapTile(
                            commander,
                            approachOrigin,
                            assessment,
                            out var frontTileId))
                        return null;

                    var missionArea = new AirMissionArea(
                        frontTileId,
                        DefaultMissionRadiusTiles);
                    var airControl = CalculateAreaAirControl(
                        commander,
                        missionArea);
                    var hostilePressure = Mathf.Max(
                        airControl.HostilePresence,
                        airControl.HostileActivity);
                    if (hostilePressure < MeaningfulCombatPresence)
                        return null;

                    var controlDeficit = Mathf.Clamp01(
                        (0.4f - airControl.Advantage) / 1.4f);
                    var fighterTransitDistanceTiles = AirMissionArea.HexDistance(
                        approachOrigin,
                        frontTileId);
                    var proximityFactor = 1f
                                          + 0.25f
                                          / (1f + fighterTransitDistanceTiles);
                    var priorityScore = hostilePressure
                                        * (1f + controlDeficit)
                                        * proximityFactor;
                    return new BarcapFrontCandidate(
                        missionArea,
                        airControl.HostilePower,
                        hostilePressure,
                        airControl.Advantage,
                        priorityScore,
                        fighterTransitDistanceTiles,
                        false,
                        CalculateDesiredBarcapStrength(
                            snapshot,
                            doctrine,
                            airControl.HostilePower,
                            airControl.HostileActivity));
                })
                .Where(candidate => candidate != null)
                .GroupBy(candidate => candidate.MissionArea.CenterTileId)
                .Select(group => group
                    .OrderByDescending(candidate => candidate.PriorityScore)
                    .ThenByDescending(candidate => candidate.HostilePressure)
                    .ThenBy(candidate => candidate.FighterTransitDistanceTiles)
                    .First())
                .OrderByDescending(candidate => candidate.PriorityScore)
                .ThenBy(candidate => candidate.FighterTransitDistanceTiles)
                .ThenBy(candidate => candidate.MissionArea.CenterTileId.x)
                .ThenBy(candidate => candidate.MissionArea.CenterTileId.y)
                .ThenBy(candidate => candidate.MissionArea.CenterTileId.z)
                .ToList();
            return airControlCandidates.Count > 0
                ? airControlCandidates
                : BuildAirfieldBarcapCandidates(
                    snapshot,
                    doctrine,
                    friendlyAirCombatOrigins);
        }

        private List<BarcapFrontCandidate> BuildAirfieldBarcapCandidates(
            AirPlanningSnapshot snapshot,
            AllianceAirDoctrine doctrine,
            IReadOnlyList<Vector3Int> friendlyAirCombatOrigins)
        {
            return snapshot.FriendlyAirfieldTiles
                .Select(airportTileId =>
                {
                    var approachOrigin = SelectNearestFighterOrigin(
                        friendlyAirCombatOrigins,
                        airportTileId);
                    var missionArea = new AirMissionArea(
                        airportTileId,
                        DefaultMissionRadiusTiles);
                    var fighterTransitDistanceTiles = AirMissionArea.HexDistance(
                        approachOrigin,
                        airportTileId);
                    var controlDeficit = Mathf.Clamp01(0.4f / 1.4f);
                    var proximityFactor = 1f
                                          + 0.25f
                                          / (1f + fighterTransitDistanceTiles);
                    var priorityScore = MeaningfulCombatPresence
                                        * (1f + controlDeficit)
                                        * proximityFactor;
                    return new BarcapFrontCandidate(
                        missionArea,
                        0f,
                        MeaningfulCombatPresence,
                        0f,
                        priorityScore,
                        fighterTransitDistanceTiles,
                        true,
                        CalculateDesiredBarcapStrength(
                            snapshot,
                            doctrine,
                            0f,
                            0f));
                })
                .OrderBy(candidate => candidate.FighterTransitDistanceTiles)
                .ThenBy(candidate => candidate.MissionArea.CenterTileId.x)
                .ThenBy(candidate => candidate.MissionArea.CenterTileId.y)
                .ThenBy(candidate => candidate.MissionArea.CenterTileId.z)
                .ToList();
        }

        private static List<BarcapFrontCandidate> SelectNonOverlappingBarcapCandidates(
            IEnumerable<BarcapFrontCandidate> candidates)
        {
            var selected = new List<BarcapFrontCandidate>();
            foreach (var candidate in candidates)
            {
                if (selected.Any(existing =>
                        existing.MissionArea.Contains(candidate.MissionArea.CenterTileId)
                        || candidate.MissionArea.Contains(
                            existing.MissionArea.CenterTileId)))
                    continue;
                selected.Add(candidate);
            }

            return selected;
        }

        private static bool TrySelectDefensiveBarcapTile(
            AllianceAirTaskingCommander commander,
            Vector3Int approachOriginTileId,
            AirControlTileAssessment hostileFrontier,
            out Vector3Int frontTileId)
        {
            frontTileId = default;
            var hostileDistance = AirMissionArea.HexDistance(
                approachOriginTileId,
                hostileFrontier.TileId);
            var friendlyBoundary = AirspaceGeometry.NeighborTiles(hostileFrontier.TileId)
                .Where(neighbor => AirMissionArea.HexDistance(
                    approachOriginTileId,
                    neighbor) < hostileDistance)
                .Select(neighbor => commander.TryGetAirControlAssessment(
                        neighbor,
                        out var assessment)
                    ? assessment
                    : null)
                .Where(assessment => assessment != null
                                     && assessment.AirControlAdvantage >= 0f)
                .OrderBy(assessment => assessment.HostileCombatPresence)
                .ThenByDescending(assessment => assessment.AirControlAdvantage)
                .ThenBy(assessment => AirMissionArea.HexDistance(
                    approachOriginTileId,
                    assessment.TileId))
                .ThenBy(assessment => assessment.TileId.x)
                .ThenBy(assessment => assessment.TileId.y)
                .ThenBy(assessment => assessment.TileId.z)
                .FirstOrDefault();
            if (friendlyBoundary == null)
                return false;

            var boundaryDistance = AirMissionArea.HexDistance(
                approachOriginTileId,
                friendlyBoundary.TileId);
            var defensiveBuffer = AirspaceGeometry.NeighborTiles(
                    friendlyBoundary.TileId)
                .Where(neighbor => AirMissionArea.HexDistance(
                    approachOriginTileId,
                    neighbor) < boundaryDistance)
                .Select(neighbor => commander.TryGetAirControlAssessment(
                        neighbor,
                        out var assessment)
                    ? assessment
                    : null)
                .Where(assessment => assessment != null
                                     && assessment.AirControlAdvantage > 0f)
                .OrderBy(assessment => assessment.HostileCombatPresence)
                .ThenByDescending(assessment => assessment.AirControlAdvantage)
                .ThenBy(assessment => assessment.TileId.x)
                .ThenBy(assessment => assessment.TileId.y)
                .ThenBy(assessment => assessment.TileId.z)
                .FirstOrDefault();

            if (defensiveBuffer == null
                && friendlyBoundary.AirControlAdvantage <= 0f)
                return false;

            frontTileId = defensiveBuffer?.TileId ?? friendlyBoundary.TileId;
            return true;
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
                    var approachOrigin = SelectNearestFighterOrigin(
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
                        airControl.HostileActivity,
                        airControl.HostilePower);
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

        private static Vector3Int SelectNearestFighterOrigin(
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
            public readonly float HostileAirCombatPower;

            public OcaTargetCandidate(
                Vector3Int frontierTileId,
                int desiredAircraftStrength,
                int penetrationDepthTiles,
                float airControlAdvantage,
                float hostileCombatPresence,
                float hostileAirActivity,
                float hostileAirCombatPower)
            {
                FrontierTileId = frontierTileId;
                DesiredAircraftStrength = Math.Max(0, desiredAircraftStrength);
                PenetrationDepthTiles = Math.Max(0, penetrationDepthTiles);
                AirControlAdvantage = Mathf.Clamp(airControlAdvantage, -1f, 1f);
                HostileCombatPresence = Mathf.Clamp01(hostileCombatPresence);
                HostileAirActivity = Mathf.Clamp01(hostileAirActivity);
                HostileAirCombatPower = Mathf.Max(0f, hostileAirCombatPower);
            }
        }

        private sealed class BarcapFrontCandidate
        {
            public readonly AirMissionArea MissionArea;
            public readonly float HostileAirCombatPower;
            public readonly float HostilePressure;
            public readonly float AirControlAdvantage;
            public readonly float PriorityScore;
            public readonly int FighterTransitDistanceTiles;
            public readonly bool UsesAirfieldBootstrap;
            public readonly int DesiredAircraftStrength;

            public BarcapFrontCandidate(
                AirMissionArea missionArea,
                float hostileAirCombatPower,
                float hostilePressure,
                float airControlAdvantage,
                float priorityScore,
                int fighterTransitDistanceTiles,
                bool usesAirfieldBootstrap,
                int desiredAircraftStrength)
            {
                MissionArea = missionArea;
                HostileAirCombatPower = Mathf.Max(0f, hostileAirCombatPower);
                HostilePressure = Mathf.Clamp01(hostilePressure);
                AirControlAdvantage = Mathf.Clamp(airControlAdvantage, -1f, 1f);
                PriorityScore = Mathf.Max(0f, priorityScore);
                FighterTransitDistanceTiles = Math.Max(0, fighterTransitDistanceTiles);
                UsesAirfieldBootstrap = usesAirfieldBootstrap;
                DesiredAircraftStrength = Math.Max(2, desiredAircraftStrength);
            }
        }

    }

}
