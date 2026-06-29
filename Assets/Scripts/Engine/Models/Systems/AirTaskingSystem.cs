using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;

namespace Engine.Models
{
    public sealed class AirTaskingSystem
    {
        public const int MaximumRequestEvaluationsPerAlliancePerTick = 8;
        public const int MaximumPackageCreationsPerAlliancePerTick = 4;

        private readonly IAirPlanningIntelligence planningIntelligence;
        private readonly AirMissionRequestGenerator requestGenerator;
        private readonly AirPackageBuilder packageBuilder;
        private readonly ProjectedAirEffectService projectedEffects;
        private readonly AllianceAirTaskingCommander blueforCommander;
        private readonly AllianceAirTaskingCommander redforCommander;
        private readonly GameManager gameManager;

        public AirTaskingSystem(
            GameManager gameManager,
            ModuleDefinition module,
            IAirPlanningIntelligence planningIntelligence = null)
        {
            this.gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            if (module == null)
                throw new ArgumentNullException(nameof(module));

            this.planningIntelligence = planningIntelligence
                                        ?? new PerfectAirPlanningIntelligence(gameManager);
            projectedEffects = new ProjectedAirEffectService();
            var priorityService = new AirMissionPriorityService(module);
            requestGenerator = new AirMissionRequestGenerator(priorityService);
            packageBuilder = new AirPackageBuilder(
                gameManager,
                module,
                projectedEffects,
                priorityService);
            blueforCommander = new AllianceAirTaskingCommander(
                Alliance.Bluefor,
                GetDoctrine(Alliance.Bluefor));
            redforCommander = new AllianceAirTaskingCommander(
                Alliance.Redfor,
                GetDoctrine(Alliance.Redfor));
        }

        public AllianceAirTaskingCommander GetCommander(Alliance alliance)
        {
            return alliance switch
            {
                Alliance.Bluefor => blueforCommander,
                Alliance.Redfor => redforCommander,
                _ => null
            };
        }

        public void Initialize()
        {
            foreach (var commander in GetCommanders())
            {
                RebuildGlobalPlan(commander);
                FulfillRequests(commander);
            }
        }

        public void GameTurn(bool crossedOperationalCadenceBoundary)
        {
            foreach (var commander in GetCommanders())
            {
                ValidatePackageIntegrity(commander);
                if (crossedOperationalCadenceBoundary)
                    RebuildGlobalPlan(commander);
                FulfillRequests(commander);
            }
        }

        public bool CancelPackage(
            Alliance alliance,
            Guid packageId,
            string reason)
        {
            var commander = GetCommander(alliance);
            var package = commander?.GetPackage(packageId);
            if (commander == null || package == null || package.IsTerminal)
                return false;

            packageBuilder.CancelPackage(
                commander,
                package,
                gameManager.CurrentTime,
                reason);
            var request = commander.GetRequest(package.MissionRequestId);
            if (request != null && package.LifecycleState == AirTaskingLifecycleState.Cancelled)
                request.State = AirMissionRequestState.Actionable;
            return true;
        }

        private void RebuildGlobalPlan(AllianceAirTaskingCommander commander)
        {
            commander.PlanningCycle++;
            PurgeUnfulfilledRequests(commander);
            var snapshot = planningIntelligence.CreateSnapshot(commander.Alliance);
            var cadenceHours = gameManager.SimulationSettings?.OperationalCadenceHours
                               ?? SimulationSettings.DefaultOperationalCadenceHours;
            var generatedRequests = requestGenerator.Generate(
                commander,
                snapshot,
                cadenceHours);
            commander.MissionRequests.AddRange(generatedRequests);

            foreach (var request in generatedRequests)
            {
                commander.AddDiagnostic(new AirTaskingDiagnostic
                {
                    RecordedAt = gameManager.CurrentTime,
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

        private void PurgeUnfulfilledRequests(AllianceAirTaskingCommander commander)
        {
            var retained = new List<AirMissionRequest>();
            foreach (var request in commander.MissionRequests ?? new List<AirMissionRequest>())
            {
                if (request == null)
                    continue;

                var linkedPackages = (commander.Packages ?? new List<AirPackage>())
                    .Where(package => package != null
                                      && package.MissionRequestId == request.MissionRequestId)
                    .ToList();
                if (linkedPackages.Any(package => !package.IsTerminal))
                {
                    retained.Add(request);
                    continue;
                }

                if (linkedPackages.Count > 0)
                {
                    commander.AddHistory(new AirTaskingHistoryEntry
                    {
                        RecordedAt = gameManager.CurrentTime,
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
                commander.AddDiagnostic(new AirTaskingDiagnostic
                {
                    RecordedAt = gameManager.CurrentTime,
                    MissionRequestId = request.MissionRequestId,
                    Code = "request-purged",
                    Message = "Unfulfilled request purged during global reprioritization."
                });
            }

            commander.MissionRequests = retained;
            var retainedRequestIds = retained
                .Select(request => request.MissionRequestId)
                .ToHashSet();
            commander.Packages = (commander.Packages ?? new List<AirPackage>())
                .Where(package => package != null
                                  && (!package.IsTerminal
                                      || retainedRequestIds.Contains(package.MissionRequestId)))
                .ToList();
        }

        private void FulfillRequests(AllianceAirTaskingCommander commander)
        {
            var evaluations = 0;
            var packagesCreated = 0;
            var requests = (commander.MissionRequests ?? new List<AirMissionRequest>())
                .Where(request => request != null
                                  && !request.IsTerminal
                                  && request.PlanningCycle == commander.PlanningCycle
                                  && request.EffectEnd > gameManager.CurrentTime)
                .OrderBy(request => request.IsSupportRequest ? 0 : 1)
                .ThenByDescending(request => request.Priority)
                .ThenBy(request => request.RequestType)
                .ThenBy(request => request.MissionArea.CenterTileId.x)
                .ThenBy(request => request.MissionArea.CenterTileId.y)
                .ThenBy(request => request.MissionArea.CenterTileId.z)
                .ToList();

            foreach (var request in requests)
            {
                if (evaluations >= MaximumRequestEvaluationsPerAlliancePerTick
                    || packagesCreated >= MaximumPackageCreationsPerAlliancePerTick)
                    break;

                evaluations++;
                if (!packageBuilder.TryBuild(
                        commander,
                        request,
                        gameManager.CurrentTime,
                        out var package,
                        out var reason))
                {
                    commander.AddDiagnostic(new AirTaskingDiagnostic
                    {
                        RecordedAt = gameManager.CurrentTime,
                        MissionRequestId = request.MissionRequestId,
                        Code = request.State == AirMissionRequestState.Fulfilled
                            ? "request-covered"
                            : "request-deferred",
                        Message = reason
                    });
                    continue;
                }

                commander.Packages.Add(package);
                request.PackageIds ??= new List<Guid>();
                request.PackageIds.Add(package.PackageId);
                request.State = request.IsSupportRequest
                    ? AirMissionRequestState.PartiallyFulfilled
                    : AirMissionRequestState.InProgress;
                packagesCreated++;
                commander.AddDiagnostic(new AirTaskingDiagnostic
                {
                    RecordedAt = gameManager.CurrentTime,
                    MissionRequestId = request.MissionRequestId,
                    PackageId = package.PackageId,
                    Code = "package-committed",
                    Message = reason,
                    Values = new Dictionary<string, float>
                    {
                        { "flightCount", package.Flights.Count },
                        { "aircraftCount", package.Flights.Sum(flight => flight.AircraftIds.Count) }
                    }
                });
            }
        }

        private void ValidatePackageIntegrity(AllianceAirTaskingCommander commander)
        {
            foreach (var package in (commander.Packages ?? new List<AirPackage>())
                         .Where(candidate => candidate != null && !candidate.IsTerminal)
                         .ToList())
            {
                var requiredFlights = (package.Flights ?? new List<AirFlight>())
                    .Where(flight => flight != null && flight.IsRequired)
                    .ToList();
                if (requiredFlights.Count > 0
                    && requiredFlights.All(flight =>
                        flight.LifecycleState != AirTaskingLifecycleState.Cancelled
                        && flight.LifecycleState != AirTaskingLifecycleState.Failed))
                    continue;

                packageBuilder.CancelPackage(
                    commander,
                    package,
                    gameManager.CurrentTime,
                    "A required package flight was cancelled or became unable to launch.");
            }
        }

        private AllianceAirDoctrine GetDoctrine(Alliance alliance)
        {
            if (gameManager.CampaignTemplate?.AirDoctrineByAlliance != null
                && gameManager.CampaignTemplate.AirDoctrineByAlliance.TryGetValue(alliance, out var doctrine))
                return doctrine;

            return AllianceAirDoctrine.CreateDefault();
        }

        private IEnumerable<AllianceAirTaskingCommander> GetCommanders()
        {
            yield return blueforCommander;
            yield return redforCommander;
        }
    }
}
