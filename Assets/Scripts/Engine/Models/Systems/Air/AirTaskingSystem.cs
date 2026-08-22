using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;

namespace Engine.Models
{
    /// <summary>
    /// Schedules explicit package plans and owns the runtime package collections.
    /// It contains no strategic mission generation or package-composition AI.
    /// </summary>
    public sealed class AirTaskingSystem
    {
        private readonly AirPackageBuilder packageBuilder;
        private readonly AircraftReservationService aircraftReservations;
        private readonly AirportOperationsService airportOperations;
        private readonly AllianceAirTaskingCommander blueforCommander;
        private readonly AllianceAirTaskingCommander redforCommander;
        private readonly GameManager gameManager;
        private readonly IAirPlanProducer planProducer;
        private readonly HashSet<Guid> attemptedPlanIds = new HashSet<Guid>();

        public AirTaskingSystem(
            GameManager gameManager,
            ModuleDefinition module,
            IAirPlanProducer planProducer = null)
        {
            this.gameManager = gameManager
                               ?? throw new ArgumentNullException(nameof(gameManager));
            this.planProducer = planProducer
                                ?? new ScriptedAirPlanProducer(() =>
                                    this.gameManager.CampaignTemplate
                                        ?.AirPackagePlans);
            airportOperations = new AirportOperationsService(gameManager);
            packageBuilder = new AirPackageBuilder(gameManager, module);
            aircraftReservations = new AircraftReservationService(
                gameManager.squadronSystem,
                module,
                gameManager.GetCountryAlliance,
                alliance => gameManager.OrdnanceAllowances.TryGetValue(
                    alliance,
                    out var allowed)
                    ? allowed
                    : Array.Empty<Guid>());
            blueforCommander = new AllianceAirTaskingCommander(
                Alliance.Bluefor,
                GetDoctrine(Alliance.Bluefor));
            redforCommander = new AllianceAirTaskingCommander(
                Alliance.Redfor,
                GetDoctrine(Alliance.Redfor));
        }

        public AllianceAirTaskingCommander GetCommander(Alliance alliance)
        {
            switch (alliance)
            {
                case Alliance.Bluefor:
                    return blueforCommander;
                case Alliance.Redfor:
                    return redforCommander;
                default:
                    return null;
            }
        }

        public IEnumerable<AirPackage> GetPackages()
        {
            return GetCommanders().SelectMany(commander => commander.Packages);
        }

        public IEnumerable<AirFlight> GetAirborneFlights()
        {
            return GetPackages()
                .SelectMany(package => package.Flights)
                .Where(flight => flight.IsAirborne && !flight.HasPhysicallyEnded);
        }

        internal AirportOperationsService AirportOperations => airportOperations;

        public AirportOperationsSnapshot GetAirportOperationsSnapshot(
            Guid airportId)
        {
            return airportOperations.CreateSnapshot(
                airportId,
                gameManager.CurrentTime,
                GetPackages());
        }

        public bool RetainsProjectedBarcapCoverage(AirFlight flight)
        {
            return flight?.PlannedBarcapCoverage != null
                   && !flight.IsTerminal
                   && flight.ExecutionPhase != FlightExecutionPhase.Returning
                   && flight.ExecutionPhase != FlightExecutionPhase.Landing
                   && flight.ExecutionPhase != FlightExecutionPhase.Ended;
        }

        public void Initialize()
        {
            MaterializeDuePlans();
        }

        public void GameTurn()
        {
            foreach (var commander in GetCommanders())
            {
                RevalidateAirportOperations(commander, gameManager.CurrentTime);
                commander.ValidatePackageIntegrity(
                    aircraftReservations,
                    gameManager.CurrentTime);
            }
            MaterializeDuePlans();
        }

        public bool CancelPackage(
            Alliance alliance,
            Guid packageId,
            string reason)
        {
            return CancelPackage(
                alliance,
                packageId,
                gameManager.CurrentTime,
                reason);
        }

        internal bool CancelPackage(
            Alliance alliance,
            Guid packageId,
            DateTime occurredAt,
            string reason)
        {
            var commander = GetCommander(alliance);
            return commander != null && commander.CancelPackage(
                packageId,
                aircraftReservations,
                occurredAt,
                reason);
        }

        internal void RevalidateAirportOperations(DateTime occurredAt)
        {
            foreach (var commander in GetCommanders())
                RevalidateAirportOperations(commander, occurredAt);
        }

        internal void RevalidatePackageIntegrity(DateTime occurredAt)
        {
            foreach (var commander in GetCommanders())
            {
                commander.ValidatePackageIntegrity(
                    aircraftReservations,
                    occurredAt);
            }
        }

        private void MaterializeDuePlans()
        {
            var plans = planProducer.GetAvailablePlans(
                            gameManager.CurrentTime)
                        ?? Enumerable.Empty<AirPackagePlan>();
            foreach (var plan in plans
                         .Where(candidate => candidate != null
                                              && candidate.AvailableAt
                                             <= gameManager.CurrentTime
                                             && !attemptedPlanIds.Contains(
                                                 candidate.PlanId))
                         .OrderBy(candidate => candidate.AvailableAt)
                         .ThenBy(candidate => candidate.PlanId)
                         .ToList())
            {
                attemptedPlanIds.Add(plan.PlanId);
                var commander = GetCommander(plan.Alliance);
                if (commander == null)
                    continue;

                if (!packageBuilder.TryBuild(
                        plan,
                        gameManager.CurrentTime,
                        out var package,
                        out var reason))
                {
                    RecordPlanFailure(commander, plan, "plan-build-failed", reason);
                    continue;
                }
                if (!airportOperations.CanSchedulePackage(
                        package,
                        commander.Packages,
                        TimeSpan.Zero,
                        out reason))
                {
                    RecordPlanFailure(
                        commander,
                        plan,
                        "plan-airport-capacity-rejected",
                        reason);
                    continue;
                }
                if (!commander.TryCommitPackage(
                        package,
                        aircraftReservations,
                        gameManager.CurrentTime,
                        out reason))
                {
                    RecordPlanFailure(
                        commander,
                        plan,
                        "plan-commit-failed",
                        reason);
                }
            }
        }

        private void RecordPlanFailure(
            AllianceAirTaskingCommander commander,
            AirPackagePlan plan,
            string code,
            string reason)
        {
            commander.AddDiagnostic(new AirTaskingDiagnostic
            {
                RecordedAt = gameManager.CurrentTime,
                PlanId = plan.PlanId,
                PackageId = plan.PlanId,
                Code = code,
                Message = reason ?? string.Empty
            });
        }

        private void RevalidateAirportOperations(
            AllianceAirTaskingCommander commander,
            DateTime occurredAt)
        {
            foreach (var package in commander.Packages
                         .Where(package => package != null
                                           && package.Flights.Any(flight =>
                                               !flight.HasPhysicallyEnded))
                         .OrderBy(package => package.EarliestTakeoffTime)
                         .ThenBy(package => package.PackageId)
                         .ToList())
            {
                if (!airportOperations.HasUnusablePendingLaunch(
                        package,
                        out var airportId))
                    continue;
                commander.CancelPackage(
                    package.PackageId,
                    aircraftReservations,
                    occurredAt,
                    $"Launch airport {ShortId(airportId)} is closed or no longer friendly.");
            }

            foreach (var package in airportOperations
                         .FindInvalidGroundedPackages(commander.Packages)
                         .ToList())
            {
                commander.CancelPackage(
                    package.PackageId,
                    aircraftReservations,
                    occurredAt,
                    "Current runway capacity can no longer support the committed package.");
            }
        }

        private AllianceAirDoctrine GetDoctrine(Alliance alliance)
        {
            if (gameManager.CampaignTemplate?.AirDoctrineByAlliance != null
                && gameManager.CampaignTemplate.AirDoctrineByAlliance.TryGetValue(
                    alliance,
                    out var doctrine))
                return doctrine;
            return AllianceAirDoctrine.CreateDefault();
        }

        private IEnumerable<AllianceAirTaskingCommander> GetCommanders()
        {
            yield return blueforCommander;
            yield return redforCommander;
        }

        private static string ShortId(Guid id)
        {
            return id == Guid.Empty
                ? "none"
                : id.ToString("N").Substring(0, 8);
        }
    }
}
