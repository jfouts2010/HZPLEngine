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
        private const float DefaultMissionRadiusKm = 40f;
        private const int MaximumOcaAircraftStrength = 8;
        private const float MeaningfulCombatPresence = 0.10f;
        private const float MeaningfulAirActivity = 0.10f;
        private static readonly TimeSpan HandoffBuffer = TimeSpan.FromMinutes(30);

        private readonly AirMissionPriorityService priorityService;
        private readonly BarcapBarrierPlanner barcapBarrierPlanner;
        private readonly DeadCorridorPlanner deadCorridorPlanner;

        public AirMissionRequestGenerator(
            AirMissionPriorityService priorityService,
            ModuleDefinition module,
            Func<Alliance, IReadOnlyCollection<Guid>> allowedOrdnanceForAlliance)
        {
            this.priorityService = priorityService;
            barcapBarrierPlanner = new BarcapBarrierPlanner(priorityService);
            deadCorridorPlanner = new DeadCorridorPlanner(
                module,
                allowedOrdnanceForAlliance);
        }

        public List<AirMissionRequest> Generate(
            AllianceAirTaskingCommander commander,
            AirPlanningSnapshot snapshot,
            int operationalCadenceHours,
            bool allowOffensiveMissions = true)
        {
            var generated = new List<AirMissionRequest>();
            var effectStart = snapshot.CurrentTime + AirPackage.PreparationDelay;
            var effectEnd = snapshot.CurrentTime
                            + TimeSpan.FromHours(Math.Max(1, operationalCadenceHours))
                            + HandoffBuffer;

            var selectedBarcapCandidates = barcapBarrierPlanner.Plan(
                commander,
                snapshot);

            foreach (var candidate in selectedBarcapCandidates)
            {
                var barrier = candidate.Plan;
                var centerTile = barrier.BarrierTileIds[
                    barrier.BarrierTileIds.Count / 2];
                var boundingRadius = barrier.BarrierTileIds
                    .Max(tile => AirMissionArea.HexDistance(centerTile, tile));
                var barcapRequest = CreateRequest(
                    commander,
                    AirMissionRequestType.BarrierCombatAirPatrol,
                    AirMissionRequestFulfillmentPattern.Sustained,
                    centerTile,
                    effectStart,
                    effectEnd,
                    desiredAircraftStrength: barrier.EstimatedAircraftDemand,
                    rationale: BuildBarcapRationale(barrier),
                    radiusKm: (boundingRadius + 1) * snapshot.TileDistanceKm,
                    tileDistanceKm: snapshot.TileDistanceKm);
                barcapRequest.BarcapBarrier = barrier.Clone();
                barcapRequest.PriorityComponents["desiredAircraftStrength"] =
                    barrier.EstimatedAircraftDemand;
                barcapRequest.PriorityComponents["barcapHostileAirCombatPower"] =
                    candidate.HostilePower;
                barcapRequest.PriorityComponents["barcapHostilePressure"] =
                    candidate.HostilePressure;
                barcapRequest.PriorityComponents["barcapFrontPriority"] =
                    candidate.ProtectionValue;
                barcapRequest.PriorityComponents["barcapFighterTransitDistanceTiles"] =
                    candidate.FighterTransitDistanceTiles;
                barcapRequest.PriorityComponents["barcapBarrierTileCount"] =
                    barrier.BarrierTileIds.Count;
                barcapRequest.PriorityComponents["barcapFrontlineDivisions"] =
                    barrier.ProtectedFrontlineDivisionCount;
                barcapRequest.PriorityComponents["barcapActiveAirports"] =
                    barrier.ProtectedActiveAirportCount;
                barcapRequest.PriorityComponents["barcapReserveAirports"] =
                    barrier.ProtectedReserveAirportCount;
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
                        rationale: "Provide baseline airborne C2 coverage",
                        radiusKm: DefaultMissionRadiusKm,
                        tileDistanceKm: snapshot.TileDistanceKm));
                }
            }

            var ocaCandidates = BuildOcaTargetCandidates(
                commander,
                snapshot,
                commander.Doctrine)
                // TODO: Recalculate OCA sweep eligibility when OCA request
                // generation is reworked. The legacy relative air-control
                // advantage filter is intentionally no longer applied.
                .Where(candidate =>
                    (candidate.HostileCombatPresence >= MeaningfulCombatPresence
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
                .ThenBy(candidate => candidate.FrontierTileId.x)
                .ThenBy(candidate => candidate.FrontierTileId.y)
                .ThenBy(candidate => candidate.FrontierTileId.z)
                .FirstOrDefault();
            if (allowOffensiveMissions && selectedOcaCandidate != null)
            {
                var ocaRequest = CreateRequest(
                    commander,
                    AirMissionRequestType.OffensiveCounterAirSweep,
                    AirMissionRequestFulfillmentPattern.Discrete,
                    selectedOcaCandidate.FrontierTileId,
                    effectStart,
                    effectEnd,
                    desiredAircraftStrength: selectedOcaCandidate.DesiredAircraftStrength,
                    rationale: "Contest the nearest active hostile air-interference frontier",
                    radiusKm: DefaultMissionRadiusKm,
                    tileDistanceKm: snapshot.TileDistanceKm);
                ocaRequest.PriorityComponents["desiredAircraftStrength"] =
                    selectedOcaCandidate.DesiredAircraftStrength;
                ocaRequest.PriorityComponents["ocaPenetrationDepthTiles"] =
                    selectedOcaCandidate.PenetrationDepthTiles;
                // TODO: Recalculate OCA-specific relative strength when OCA
                // request generation is reworked. The legacy air-control
                // advantage priority value is intentionally no longer produced.
                ocaRequest.PriorityComponents["ocaHostileCombatPresence"] =
                    selectedOcaCandidate.HostileCombatPresence;
                ocaRequest.PriorityComponents["ocaHostileAirActivity"] =
                    selectedOcaCandidate.HostileAirActivity;
                ocaRequest.PriorityComponents["ocaHostileAirCombatPower"] =
                    selectedOcaCandidate.HostileAirCombatPower;
                generated.Add(ocaRequest);
            }

            var hasUnresolvedDead = commander.MissionRequests.Any(request =>
                request.RequestType
                == AirMissionRequestType.DestructionOfEnemyAirDefenses
                && !request.IsTerminal);
            if (allowOffensiveMissions
                && !hasUnresolvedDead
                && deadCorridorPlanner.TryPlan(snapshot, out var deadCandidate))
            {
                var deadRequest = CreateRequest(
                    commander,
                    AirMissionRequestType.DestructionOfEnemyAirDefenses,
                    AirMissionRequestFulfillmentPattern.Discrete,
                    deadCandidate.TargetSite.TileId,
                    effectStart,
                    effectEnd,
                    desiredAircraftStrength: 1,
                    rationale: deadCandidate.Rationale,
                    radiusKm: DeadCorridorPlanner.MissionRadiusKm,
                    tileDistanceKm: snapshot.TileDistanceKm);
                deadRequest.DeadPlan = deadCandidate.Plan.Clone();
                deadRequest.PriorityComponents["deadAirportFunctionalLevel"] =
                    deadCandidate.Urgency;
                deadRequest.PriorityComponents["deadCorridorBlocked"] = 1f;
                deadRequest.PriorityComponents["deadTargetComponentCount"] =
                    deadCandidate.Plan.TargetComponentIds.Count;
                generated.Add(deadRequest);
            }

            var combatRequests = generated
                .Where(request => !request.IsSupportRequest)
                .ToList();
            foreach (var airportTile in snapshot.FriendlyAirportTiles)
            {
                var baseline = commander.Doctrine.BaselineAerialRefuelingSlots;
                var observed = CalculateObservedTankerDemand(
                    commander,
                    airportTile,
                    snapshot.CurrentTime,
                    snapshot.TileDistanceKm);
                var forecast = combatRequests
                    .Where(request => request.MissionArea.Contains(airportTile)
                                      || new AirMissionArea(
                                              airportTile,
                                              DefaultMissionRadiusKm,
                                              snapshot.TileDistanceKm)
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
                    rationale: "Provide blended baseline, observed, and forecast aerial-refueling capacity",
                    radiusKm: DefaultMissionRadiusKm,
                    tileDistanceKm: snapshot.TileDistanceKm);
                tankerRequest.PriorityComponents["baselineDemand"] = baseline;
                tankerRequest.PriorityComponents["observedDemand"] = observed;
                tankerRequest.PriorityComponents["forecastDemand"] = forecast;
                generated.Add(tankerRequest);
            }

            foreach (var request in generated)
            {
                var airInterference = CalculateAreaAirInterference(
                    commander,
                    request.MissionArea);
                priorityService.Score(
                    request,
                    commander.Doctrine,
                    snapshot,
                    airInterference.FriendlyPresence,
                    airInterference.HostilePresence,
                    airInterference.Activity,
                    airInterference.HostileActivity);
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
            float radiusKm = DefaultMissionRadiusKm,
            float tileDistanceKm = 20f)
        {
            return new AirMissionRequest
            {
                Alliance = commander.Alliance,
                RequestType = requestType,
                FulfillmentPattern = fulfillmentPattern,
                MissionArea = new AirMissionArea(
                    centerTile,
                    radiusKm,
                    tileDistanceKm),
                CreatedAt = effectStart - AirPackage.PreparationDelay,
                EffectStart = effectStart,
                EffectEnd = effectEnd,
                PlanningCycle = commander.PlanningCycle,
                DesiredAircraftStrength = Math.Max(0, desiredAircraftStrength),
                DesiredSupportSlots = Math.Max(0, desiredSupportSlots),
                Rationale = rationale
            };
        }

        private static string BuildBarcapRationale(BarcapBarrierPlan barrier)
        {
            var protectedAssets = new List<string>();
            if (barrier.ProtectedFrontlineDivisionCount > 0)
            {
                protectedAssets.Add(
                    $"{barrier.ProtectedFrontlineDivisionCount} front-line division"
                    + (barrier.ProtectedFrontlineDivisionCount == 1 ? string.Empty : "s"));
            }
            if (barrier.ProtectedActiveAirportCount > 0)
            {
                protectedAssets.Add(
                    $"{barrier.ProtectedActiveAirportCount} active airport"
                    + (barrier.ProtectedActiveAirportCount == 1 ? string.Empty : "s"));
            }
            if (barrier.ProtectedReserveAirportCount > 0)
            {
                protectedAssets.Add(
                    $"{barrier.ProtectedReserveAirportCount} reserve airport"
                    + (barrier.ProtectedReserveAirportCount == 1 ? string.Empty : "s"));
            }

            var prefix = barrier.IsSupplemental
                ? "Close an uncovered defensive approach for "
                : "Establish a defensive air barrier screening ";
            return prefix + (protectedAssets.Count > 0
                ? string.Join(", ", protectedAssets)
                : "friendly forces");
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

            // TODO: Recalculate the OCA sweep frontier when OCA request
            // generation is reworked. Legacy relative air-control advantage
            // filtering is intentionally omitted.
            return commander.AirControlAssessments
                .Where(assessment =>
                    assessment.HostileCombatPresence >= MeaningfulCombatPresence
                        || assessment.HostileAirActivity >= MeaningfulAirActivity)
                .Select(assessment =>
                {
                    var approachOrigin = SelectNearestFighterOrigin(
                        friendlyAirCombatOrigins,
                        assessment.TileId);
                    if (!IsFriendlyFacingInterferenceFrontier(
                            commander,
                            approachOrigin,
                            assessment))
                        return null;

                    var airInterference = CalculateAreaAirInterference(
                        commander,
                        new AirMissionArea(
                            assessment.TileId,
                            DefaultMissionRadiusKm,
                            snapshot.TileDistanceKm));
                    return new OcaTargetCandidate(
                        assessment.TileId,
                        CalculateOcaDesiredStrength(
                            snapshot,
                            doctrine,
                            airInterference.HostilePower),
                        CalculatePenetrationDepthTiles(
                            friendlyAirCombatOrigins,
                            assessment.TileId),
                        airInterference.HostilePresence,
                        airInterference.HostileActivity,
                        airInterference.HostilePower);
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
            float Activity,
            float HostileActivity,
            float HostilePower) CalculateAreaAirInterference(
            AllianceAirTaskingCommander commander,
            AirMissionArea missionArea)
        {
            var assessments = commander.AirControlAssessments
                .Where(assessment => missionArea.Contains(assessment.TileId))
                .ToList();
            if (assessments.Count == 0)
                return (0f, 0f, 0f, 0f, 0f);

            return (
                Mathf.Clamp01(assessments.Average(
                    assessment => assessment.FriendlyCombatPresence)),
                Mathf.Clamp01(assessments.Average(
                    assessment => assessment.HostileCombatPresence)),
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

        private static bool IsFriendlyFacingInterferenceFrontier(
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
                    && neighborAssessment.HostileAirInterference
                    < assessment.HostileAirInterference);
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
            DateTime currentTime,
            float tileDistanceKm)
        {
            var recentThreshold = currentTime - TimeSpan.FromHours(24);
            var demandArea = new AirMissionArea(
                centerTile,
                DefaultMissionRadiusKm,
                tileDistanceKm);
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
            public readonly float HostileCombatPresence;
            public readonly float HostileAirActivity;
            public readonly float HostileAirCombatPower;

            public OcaTargetCandidate(
                Vector3Int frontierTileId,
                int desiredAircraftStrength,
                int penetrationDepthTiles,
                float hostileCombatPresence,
                float hostileAirActivity,
                float hostileAirCombatPower)
            {
                FrontierTileId = frontierTileId;
                DesiredAircraftStrength = Math.Max(0, desiredAircraftStrength);
                PenetrationDepthTiles = Math.Max(0, penetrationDepthTiles);
                HostileCombatPresence = Mathf.Clamp01(hostileCombatPresence);
                HostileAirActivity = Mathf.Clamp01(hostileAirActivity);
                HostileAirCombatPower = Mathf.Max(0f, hostileAirCombatPower);
            }
        }

    }

}
