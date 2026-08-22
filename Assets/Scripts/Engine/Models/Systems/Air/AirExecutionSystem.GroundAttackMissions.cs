using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Models
{
    public sealed partial class AirExecutionSystem
    {
        private void ProcessDeadMissions(DateTime currentTime)
        {
            foreach (var package in airTaskingSystem.GetPackages()
                         .Where(candidate => candidate.LifecycleState
                                             == AirTaskingLifecycleState.Active)
                         .OrderBy(candidate => candidate.PackageId))
            {
                var deadPlan = package.DeadPlan;
                if (package.OperationType != AirOperationType.Dead
                    || deadPlan == null
                    || !gameManager.airDefenseSiteSystem.TryGetSite(
                        deadPlan.TargetSiteId,
                        out var site))
                    continue;

                var currentReport = gameManager.intelligenceSystem
                    ?.GetPicture(package.Alliance)
                    ?.HostileAirDefenseSites
                    ?.FirstOrDefault(report => report != null
                                               && report.SiteId == site.SiteId
                                               && report.InformationQuality > 0f);
                if (currentReport == null)
                {
                    var invalidatedFlightIds = package.Flights
                        .Where(flight => flight.IsDeadAttackFlight
                                         && flight.IsAirborne)
                        .Select(flight => flight.FlightId)
                        .ToList();
                    ordnanceEmploymentSystem.CancelAirToGroundPasses(
                        invalidatedFlightIds,
                        currentTime,
                        "DEAD preparation aborted because the assigned site is no longer known.");
                    foreach (var flight in package.Flights.Where(flight =>
                                 invalidatedFlightIds.Contains(flight.FlightId)))
                    {
                        flight.Cancel(
                            currentTime,
                            "The assigned SAM site is no longer known; the flight will not retarget airborne.");
                    }
                    continue;
                }

                deadPlan.TargetComponentIds = (currentReport.Components
                                                        ?? new List<AirDefenseComponentIntelligenceReport>())
                    .Where(component => component != null
                                        && !component.IsDamaged)
                    .Select(component => component.ComponentId)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();

                var effectiveSiteAlliance = gameManager.airDefenseSiteSystem
                    .GetEffectiveAlliance(site);
                var siteNoLongerHostile = site.IsDisabled
                                          || site.IsDestroyed
                                          || effectiveSiteAlliance
                                          == Alliance.Neutral
                                          || effectiveSiteAlliance
                                          == package.Alliance;
                var hasFunctionalShooterChain = !siteNoLongerHostile
                                                && HasPermanentSamShooterChain(site);
                var minimumEffectAchievedByPackage =
                    DidPackageAchieveDeadMinimumEffect(package, site);
                var corridorStillBlocked = siteNoLongerHostile
                                           || IsDeadCorridorStillBlocked(
                                               package.Alliance,
                                               deadPlan);
                if (!siteNoLongerHostile
                    && minimumEffectAchievedByPackage
                    && !hasFunctionalShooterChain)
                {
                    var protectedDeadFlightIds = package.Flights
                        .Where(flight => flight.IsDeadAttackFlight)
                        .Select(flight => flight.FlightId)
                        .ToHashSet();
                    var coverageChanged = package.Flights
                        .Where(flight => flight.IsFighterEscort
                                         && flight.ProtectedFlightIds.Any(
                                             protectedDeadFlightIds.Contains))
                        .OrderBy(flight => flight.FlightId)
                        .Aggregate(
                            false,
                            (changed, escort) =>
                            {
                                var modeChanged = escort.UpdateEscortCoverageMode(
                                    AirEscortCoverageMode.CloseCover,
                                    currentTime,
                                    "The protected objective can no longer fire; "
                                    + "escort collapsed its forward screen to close cover.");
                                var clearanceChanged =
                                    escort.ConfirmSurfaceThreatCleared(
                                        site.SiteId,
                                        currentTime,
                                        "The protected package permanently broke the "
                                        + "target site's shooter chain; its former "
                                        + "envelope is cleared for close cover.");
                                return changed
                                       || modeChanged
                                       || clearanceChanged;
                            });
                    if (coverageChanged)
                    {
                        // The target envelope is derived from the now-broken
                        // shooter chain. Rebuild the tactical picture before
                        // the close-cover command is evaluated.
                        knownSamThreatCache.Clear();
                    }
                }
                foreach (var flight in package.Flights
                             .Where(candidate => candidate.IsDeadAttackFlight
                                                 && candidate.IsAirborne)
                             .OrderBy(candidate => candidate.FlightId))
                {
                    var canApproachTargetFireControlRadar =
                        TryGetFlightContext(
                            flight,
                            out var probingSquadron,
                            out _)
                        && CanApproachDeadFireControlRadar(
                            flight,
                            probingSquadron,
                            site,
                            deadPlan.TargetComponentIds);
                    flight.UpdateSurfaceThreatPenetrationAuthorization(
                        (minimumEffectAchievedByPackage
                         && !hasFunctionalShooterChain)
                        || canApproachTargetFireControlRadar);
                    flight.UpdateMissionOutcome(
                        minimumEffectAchievedByPackage,
                        currentTime,
                        hasFunctionalShooterChain
                            ? "The target SAM still has a functional shooter chain."
                            : minimumEffectAchievedByPackage
                                ? "The package permanently removed the target SAM's functional shooter chain."
                                : "The target SAM's shooter chain ended without a qualifying package effect.");

                    var targetInsideFixedArea = package.OperationArea.Contains(
                        currentReport.TileId);
                    if (siteNoLongerHostile
                        || !targetInsideFixedArea
                        || (!corridorStillBlocked
                            && !minimumEffectAchievedByPackage))
                    {
                        ordnanceEmploymentSystem.CancelAirToGroundPasses(
                            new[] { flight.FlightId },
                            currentTime,
                            siteNoLongerHostile
                                ? "DEAD preparation aborted because the site is no longer hostile."
                                : !corridorStillBlocked
                                    ? "DEAD preparation aborted because the supported corridor is now open."
                                : "DEAD preparation aborted because the mobile site left the fixed mission area.");
                        if (!minimumEffectAchievedByPackage)
                        {
                            flight.Cancel(
                                currentTime,
                                !targetInsideFixedArea
                                    ? "The assigned SAM left the fixed DEAD mission area; the flight will not pursue it."
                                    : "The assigned SAM no longer blocks the supported corridor; the flight will not retarget airborne.");
                        }
                        else
                        {
                            flight.EndDeadAttackAndBeginRecovery(
                                currentTime,
                                true,
                                "The DEAD minimum effect is complete; ending the attack sequence.");
                        }
                        continue;
                    }

                    if (flight.ExecutionPhase != FlightExecutionPhase.Executing
                        || currentTime >= flight.EffectEnd
                        || !gameManager.airDefenseSiteSystem.TryGetTileId(
                            site,
                            out var siteTileId)
                        || !TryGetFlightContext(
                            flight,
                            out var squadron,
                            out _))
                        continue;

                    TryStartNextDeadAttack(
                        flight,
                        squadron,
                        site,
                        siteTileId,
                        deadPlan.TargetComponentIds,
                        currentTime);

                    var unresolvedGroundEffect = ordnanceEmploymentSystem
                        .ActivePasses.Any(pass =>
                            pass.SourceFlightId == flight.FlightId
                            && pass.TargetKind
                            == OrdnanceEmploymentTargetKind.AirDefenseComponent)
                        || ordnanceEmploymentSystem.PendingEffects.Any(effect =>
                            effect.SourceFlightId == flight.FlightId
                            && effect.TargetKind
                            == OrdnanceEmploymentTargetKind.AirDefenseComponent);
                    if (!unresolvedGroundEffect
                        && !HasDeadMissionUsefulOrdnance(
                            flight,
                            squadron,
                            site,
                            deadPlan.TargetComponentIds))
                    {
                        flight.EndDeadAttackAndBeginRecovery(
                            currentTime,
                            minimumEffectAchievedByPackage,
                            hasFunctionalShooterChain
                                ? "Mission-useful DEAD ordnance is exhausted before the minimum effect."
                                : minimumEffectAchievedByPackage
                                    ? "The DEAD minimum effect is complete and no useful cleanup ordnance remains."
                                    : "The target was invalidated without a qualifying package effect.");
                    }
                }
            }
        }

        private void ProcessStrikeMissions(DateTime currentTime)
        {
            foreach (var package in airTaskingSystem.GetPackages()
                         .Where(candidate => candidate.LifecycleState
                                             == AirTaskingLifecycleState.Active)
                         .OrderBy(candidate => candidate.PackageId))
            {
                var strikePlan = package.StrikePlan;
                if (package.OperationType != AirOperationType.Strike
                    || strikePlan?.Purpose != StrikePurpose.OffensiveCounterAir)
                    continue;

                if (!gameManager.buildingSystem.TryGetBuilding(
                        strikePlan.TargetAirportBuildingId,
                        out var targetBuilding)
                    || targetBuilding is not Airport airport)
                {
                    foreach (var flight in package.Flights
                                 .Where(candidate => candidate.IsStrikeFlight
                                                     && candidate.IsAirborne)
                                 .OrderBy(candidate => candidate.FlightId))
                    {
                        ordnanceEmploymentSystem.CancelAirToGroundPasses(
                            new[] { flight.FlightId },
                            currentTime,
                            "OCA strike preparation aborted because the target airport no longer exists.");
                        flight.EndStrikeAttackAndBeginRecovery(
                            currentTime,
                            flight.MissionAchieved,
                            "The assigned airport no longer exists; ending the strike sequence.");
                    }
                    continue;
                }

                var targetIsHostile = IsHostileAirport(package.Alliance, airport);
                var targetInsideFixedArea = package.OperationArea?.Contains(
                    airport.TileId) == true;
                var targetIsKnown = gameManager.intelligenceSystem
                    ?.GetPicture(package.Alliance)
                    ?.EnemyAirports
                    ?.Any(report => report != null
                                    && report.AirportBuildingId
                                    == airport.BuildingId
                                    && report.InformationQuality > 0f) == true;
                foreach (var flight in package.Flights
                             .Where(candidate => candidate.IsStrikeFlight
                                                 && candidate.IsAirborne)
                             .OrderBy(candidate => candidate.FlightId))
                {
                    var objectiveAchieved = IsStrikeObjectiveAchieved(
                        flight.StrikeAssignment,
                        strikePlan,
                        airport);
                    var targetIsValid = targetIsKnown
                                        && targetIsHostile
                                        && targetInsideFixedArea;
                    var isRecovering = flight.ExecutionPhase
                                           == FlightExecutionPhase.Returning
                                       || flight.ExecutionPhase
                                           == FlightExecutionPhase.Landing;
                    if (targetIsValid
                        && (!isRecovering
                            || objectiveAchieved && !flight.MissionAchieved))
                    {
                        flight.UpdateMissionOutcome(
                            objectiveAchieved,
                            currentTime,
                            GetStrikeOutcomeReason(
                                flight.StrikeAssignment,
                                objectiveAchieved));
                    }
                    if (isRecovering)
                        continue;

                    if (!targetIsValid)
                    {
                        ordnanceEmploymentSystem.CancelAirToGroundPasses(
                            new[] { flight.FlightId },
                            currentTime,
                            !targetIsKnown
                                ? "OCA strike preparation aborted because the airport is no longer known."
                                : !targetIsHostile
                                    ? "OCA strike preparation aborted because the airport is no longer hostile."
                                    : "OCA strike preparation aborted because the airport left the fixed mission area.");
                        flight.EndStrikeAttackAndBeginRecovery(
                            currentTime,
                            flight.MissionAchieved,
                            !targetIsKnown
                                ? "The assigned airport is no longer known; ending the strike sequence."
                                : !targetIsHostile
                                    ? "The assigned airport is no longer hostile; ending the strike sequence."
                                    : "The assigned airport is outside the fixed strike area; ending the strike sequence.");
                        continue;
                    }

                    if (objectiveAchieved)
                    {
                        ordnanceEmploymentSystem.CancelAirToGroundPasses(
                            new[] { flight.FlightId },
                            currentTime,
                            "Strike preparation aborted because the assigned objective is already complete.");
                        flight.EndStrikeAttackAndBeginRecovery(
                            currentTime,
                            true,
                            "The assigned OCA strike objective is complete; ending the attack sequence.");
                        continue;
                    }

                    if (flight.ExecutionPhase != FlightExecutionPhase.Executing)
                        continue;

                    if (currentTime >= flight.EffectEnd)
                    {
                        ordnanceEmploymentSystem.CancelAirToGroundPasses(
                            new[] { flight.FlightId },
                            currentTime,
                            "Strike preparation aborted because the OCA attack window ended.");
                        flight.EndStrikeAttackAndBeginRecovery(
                            currentTime,
                            objectiveAchieved,
                            objectiveAchieved
                                ? "The assigned OCA strike objective was achieved."
                                : HasPendingGroundEffect(flight.FlightId)
                                    ? "The OCA strike window ended; released effects remain pending during recovery."
                                    : "The OCA strike window ended before the assigned objective was achieved.");
                        continue;
                    }
                    if (currentTime < flight.EffectStart
                        || !TryGetFlightContext(
                            flight,
                            out var squadron,
                            out _))
                        continue;

                    TryStartNextStrikeAttack(
                        flight,
                        squadron,
                        strikePlan,
                        airport,
                        currentTime);

                    var unresolvedGroundEffect = HasUnresolvedGroundEffect(
                        flight.FlightId);
                    if (!unresolvedGroundEffect
                        && !HasStrikeMissionUsefulOrdnance(
                            flight,
                            squadron,
                            strikePlan,
                            airport))
                    {
                        objectiveAchieved = IsStrikeObjectiveAchieved(
                            flight.StrikeAssignment,
                            strikePlan,
                            airport);
                        flight.EndStrikeAttackAndBeginRecovery(
                            currentTime,
                            objectiveAchieved,
                            objectiveAchieved
                                ? "The assigned OCA strike objective is complete and no useful cleanup ordnance remains."
                                : "Mission-useful strike ordnance is exhausted before the assigned objective was achieved.");
                    }
                }
            }
        }

        private void TryStartNextStrikeAttack(
            AirFlight flight,
            Squadron squadron,
            StrikeMissionPlan strikePlan,
            Airport airport,
            DateTime currentTime)
        {
            if (flight == null
                || squadron == null
                || strikePlan == null
                || airport == null
                || !flight.CanEvaluateGroundAttackOpportunity(currentTime)
                || HasUnresolvedGroundEffect(flight.FlightId))
                return;

            var sequence = flight.ConsumeGroundAttackOpportunity(
                currentTime,
                retrySeconds: 60d);
            GroundAttackOpportunity opportunity;
            switch (flight.StrikeAssignment)
            {
                case StrikeAssignment.RunwayDenial:
                    opportunity = groundAttackOpportunityService
                        .CreateAirportRunwayOpportunity(
                            flight.FlightId,
                            sequence,
                            airport,
                            strikePlan.DesiredRunwayDamagePerChannel,
                            currentTime,
                            target => ordnanceEmploymentSystem
                                .CountActiveOrPendingPrimaryGroundEffects(target));
                    break;

                case StrikeAssignment.AircraftOnGround:
                    opportunity = groundAttackOpportunityService
                        .RollParkedAircraftOpportunity(
                            flight.FlightId,
                            sequence,
                            airport,
                            GetGroundedAircraftAtAirport(airport.BuildingId),
                            currentTime,
                            target => ordnanceEmploymentSystem
                                .HasActiveOrPendingGroundEffect(target));
                    break;

                case StrikeAssignment.AirbaseFacilities:
                    opportunity = groundAttackOpportunityService
                        .RollAuthorizedFacilityOpportunity(
                            flight.FlightId,
                            sequence,
                            airport,
                            GetAuthorizedStrikeFacilities(strikePlan, airport),
                            currentTime,
                            target => ordnanceEmploymentSystem
                                .HasActiveOrPendingGroundEffect(target));
                    break;

                default:
                    return;
            }
            if (!opportunity.HasTargets)
                return;

            var sourceAircraft = squadron.Aircraft
                .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Lost
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Damaged)
                .ToList();
            if (!groundAttackDecisionService.TryPlan(
                    opportunity,
                    sourceAircraft,
                    ordnanceTypes,
                    (target, ordnance) =>
                        IsSuitableStrikeOpportunityTarget(
                            flight.StrikeAssignment,
                            target)
                        && TryGetStrikeTargetPosition(
                            target?.Target,
                            airport,
                            out var targetPositionFeet)
                        && IsWithinGroundReleaseRange(
                            flight,
                            targetPositionFeet,
                            ordnance),
                    out var plan))
                return;

            ordnanceEmploymentSystem.TryStartGroundAttackPass(
                flight.FlightId,
                plan,
                currentTime);
        }

        private bool TryGetStrikePreflightInvalidationReason(
            AirPackage package,
            out string reason)
        {
            reason = string.Empty;
            var strikePlan = package.StrikePlan;
            if (package.OperationType != AirOperationType.Strike
                || strikePlan == null)
                return false;

            var picture = gameManager.intelligenceSystem
                ?.GetPicture(package.Alliance);
            var report = picture
                ?.EnemyAirports
                ?.FirstOrDefault(candidate => candidate != null
                                              && candidate.AirportBuildingId
                                              == strikePlan.TargetAirportBuildingId
                                              && candidate.InformationQuality > 0f);
            if (report == null)
            {
                reason = "The assigned airport is no longer known before takeoff.";
                return true;
            }
            if (package.OperationArea?.Contains(report.AirportTileId) != true)
            {
                reason =
                    "The assigned airport no longer matches the fixed OCA strike area before takeoff.";
                return true;
            }

            var strikeFlights = package.Flights
                .Where(flight => flight.IsStrikeFlight)
                .ToList();
            if (strikeFlights.Count > 0
                && strikeFlights.All(flight => IsObservedStrikeObjectiveAchieved(
                    flight.StrikeAssignment,
                    strikePlan,
                    report,
                    picture)))
            {
                reason =
                    "Current intelligence indicates every assigned OCA strike objective is already complete.";
                return true;
            }
            return false;
        }

        private static bool IsObservedStrikeObjectiveAchieved(
            StrikeAssignment assignment,
            StrikeMissionPlan strikePlan,
            ObservedEnemyAirportSnapshot report,
            AllianceIntelligencePicture picture)
        {
            switch (assignment)
            {
                case StrikeAssignment.RunwayDenial:
                    var channels = report.RunwayChannels
                                   ?? new List<ObservedRunwayChannel>();
                    return channels.Count > 0
                           && channels.All(channel => channel != null
                               && channel.DamageLevel
                               >= strikePlan.DesiredRunwayDamagePerChannel);
                case StrikeAssignment.AircraftOnGround:
                    return (report.AircraftGroups
                            ?? new List<ObservedAircraftGroup>())
                        .Sum(group => group?.AircraftOnGroundCount ?? 0) == 0;
                case StrikeAssignment.AirbaseFacilities:
                    var authorizedIds = (strikePlan.AuthorizedFacilityTargetIds
                                         ?? new List<Guid>())
                        .Where(id => id != Guid.Empty)
                        .ToHashSet();
                    var buildingReports = picture?.HostileBuildings
                                          ?? new List<
                                              BuildingIntelligenceReport>();
                    return authorizedIds.Count > 0
                           && authorizedIds.All(id => buildingReports.Any(
                               building => building != null
                                           && building.BuildingId == id
                                           && building.InformationQuality > 0f
                                           && building.FunctionalLevel <= 0));
                default:
                    return false;
            }
        }

        private bool IsStrikeObjectiveAchieved(
            StrikeAssignment assignment,
            StrikeMissionPlan strikePlan,
            Airport airport)
        {
            switch (assignment)
            {
                case StrikeAssignment.RunwayDenial:
                    airport.EnsureRunwayChannels();
                    return airport.RunwayChannels.Count > 0
                           && airport.RunwayChannels.All(channel =>
                               channel.DamageLevel
                               >= strikePlan.DesiredRunwayDamagePerChannel);
                case StrikeAssignment.AircraftOnGround:
                    return !GetGroundedAircraftAtAirport(airport.BuildingId).Any();
                case StrikeAssignment.AirbaseFacilities:
                    return !GetAuthorizedStrikeFacilities(strikePlan, airport).Any();
                default:
                    return false;
            }
        }

        private IEnumerable<CampaignAircraft> GetGroundedAircraftAtAirport(
            Guid airportBuildingId)
        {
            var airborneAircraftIds = airTaskingSystem.GetAirborneFlights()
                .SelectMany(flight => flight.AircraftIds)
                .ToHashSet();
            return gameManager.squadronSystem.Squadrons
                .Where(squadron => squadron.AirportBuildingId
                                   == airportBuildingId)
                .SelectMany(squadron => squadron.Aircraft)
                .Where(aircraft => aircraft != null
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Lost
                                   && !airborneAircraftIds.Contains(
                                       aircraft.AircraftId))
                .ToList();
        }

        private IEnumerable<Building> GetAuthorizedStrikeFacilities(
            StrikeMissionPlan strikePlan,
            Airport airport)
        {
            var authorizedIds = (strikePlan.AuthorizedFacilityTargetIds
                                 ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToHashSet();
            return gameManager.buildingSystem.GetBuildingsOnTile(airport.TileId)
                .Where(building => building != null
                                   && building.BuildingId != airport.BuildingId
                                   && authorizedIds.Contains(building.BuildingId)
                                   && building.FunctionalLevel > 0)
                .ToList();
        }

        private bool HasStrikeMissionUsefulOrdnance(
            AirFlight flight,
            Squadron squadron,
            StrikeMissionPlan strikePlan,
            Airport airport)
        {
            var remainingTargets = new List<(OrdnanceTargetCategory Category, int Toughness)>();
            switch (flight.StrikeAssignment)
            {
                case StrikeAssignment.RunwayDenial:
                    airport.EnsureRunwayChannels();
                    remainingTargets.AddRange(airport.RunwayChannels
                        .Where(channel => channel.DamageLevel
                                          < strikePlan.DesiredRunwayDamagePerChannel)
                        .Select(_ => (OrdnanceTargetCategory.Runway,
                                      airport.TargetToughness)));
                    break;
                case StrikeAssignment.AircraftOnGround:
                    if (GetGroundedAircraftAtAirport(airport.BuildingId).Any())
                    {
                        remainingTargets.Add((
                            OrdnanceTargetCategory.Aircraft,
                            1));
                    }
                    break;
                case StrikeAssignment.AirbaseFacilities:
                    remainingTargets.AddRange(
                        GetAuthorizedStrikeFacilities(strikePlan, airport)
                            .Select(building => (
                                OrdnanceTargetCategory.Building,
                                building.TargetToughness)));
                    break;
            }
            if (remainingTargets.Count == 0)
                return false;

            return squadron.Aircraft
                .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Lost
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Damaged)
                .SelectMany(aircraft => aircraft.Loadout)
                .Any(item => item.Count > 0
                             && ordnanceTypes.TryGetValue(
                                 item.OrdnanceTypeDefinitionId,
                                 out var ordnance)
                             && remainingTargets.Any(target =>
                                 AirToGroundWeaponRules.CanAffect(
                                     ordnance,
                                     target.Category,
                                     target.Toughness)));
        }

        private static bool IsSuitableStrikeOpportunityTarget(
            StrikeAssignment assignment,
            GroundAttackOpportunityTarget target)
        {
            return assignment switch
            {
                StrikeAssignment.RunwayDenial => target?.Target?.Kind
                                                  == GroundAttackTargetKind.AirportRunway,
                StrikeAssignment.AircraftOnGround => target?.Target?.Kind
                                                     == GroundAttackTargetKind.GroundedAircraft,
                StrikeAssignment.AirbaseFacilities => target?.Target?.Kind
                                                      == GroundAttackTargetKind.Building,
                _ => false
            };
        }

        private bool TryGetStrikeTargetPosition(
            GroundAttackTargetReference target,
            Airport airport,
            out Vector3 positionFeet)
        {
            positionFeet = airport.PositionFeet;
            if (target == null)
                return false;
            if (target.Kind != GroundAttackTargetKind.Building)
                return true;
            if (!gameManager.buildingSystem.TryGetBuilding(
                    target.EntityId,
                    out var building))
                return false;
            positionFeet = building.PositionFeet;
            return true;
        }

        private bool IsHostileAirport(Alliance attackingAlliance, Airport airport)
        {
            return airport != null
                   && gameManager.tileSystem.TryGetLand(
                       airport.TileId,
                       out var tile)
                   && tile.Controller != Alliance.Neutral
                   && tile.Controller != attackingAlliance;
        }

        private bool HasUnresolvedGroundEffect(Guid flightId)
        {
            return HasActiveGroundPreparation(flightId)
                   || HasPendingGroundEffect(flightId);
        }

        private bool HasActiveGroundPreparation(Guid flightId)
        {
            return ordnanceEmploymentSystem.ActivePasses.Any(pass =>
                       pass.SourceFlightId == flightId
                       && pass.TargetKind
                       != OrdnanceEmploymentTargetKind.AirFlight);
        }

        private bool HasPendingGroundEffect(Guid flightId)
        {
            return ordnanceEmploymentSystem.PendingEffects.Any(effect =>
                       effect.SourceFlightId == flightId
                       && effect.TargetKind
                       != OrdnanceEmploymentTargetKind.AirFlight);
        }

        private static string GetStrikeOutcomeReason(
            StrikeAssignment assignment,
            bool achieved)
        {
            if (achieved)
            {
                return assignment switch
                {
                    StrikeAssignment.RunwayDenial =>
                        "The assigned runway damage level was achieved.",
                    StrikeAssignment.AircraftOnGround =>
                        "No target aircraft remain on the ground at the assigned airport.",
                    StrikeAssignment.AirbaseFacilities =>
                        "All authorized airbase facilities were disabled.",
                    _ => "The assigned OCA strike objective was achieved."
                };
            }
            return "The assigned OCA strike objective remains incomplete.";
        }

        private bool TryGetDeadPreflightInvalidationReason(
            AirPackage package,
            out string reason)
        {
            reason = string.Empty;
            var deadPlan = package.DeadPlan;
            if (package.OperationType != AirOperationType.Dead
                || deadPlan == null)
                return false;

            var report = gameManager.intelligenceSystem
                ?.GetPicture(package.Alliance)
                ?.HostileAirDefenseSites
                ?.FirstOrDefault(candidate => candidate != null
                                              && candidate.SiteId
                                              == deadPlan.TargetSiteId
                                              && candidate.InformationQuality > 0f);
            if (report == null)
            {
                reason = "The assigned SAM site is no longer known before takeoff.";
                return true;
            }

            if (report.IsDisabled || report.IsDestroyed)
            {
                reason = "The assigned SAM site no longer requires a DEAD attack before takeoff.";
                return true;
            }

            var refreshedComponentIds = (report.Components
                                         ?? new List<AirDefenseComponentIntelligenceReport>())
                .Where(component => component != null && !component.IsDamaged)
                .Select(component => component.ComponentId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            var plannedComponentIds = (deadPlan.TargetComponentIds
                                       ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            deadPlan.TargetComponentIds = refreshedComponentIds;
            if (report.TileId != package.OperationArea.CenterTileId)
            {
                reason =
                    "The assigned mobile SAM moved before takeoff; "
                    + "the explicit package plan no longer matches its position.";
                return true;
            }
            if (!plannedComponentIds.SequenceEqual(refreshedComponentIds))
            {
                reason =
                    "The known DEAD component set changed before takeoff; "
                    + "the explicit package plan no longer matches current target coverage.";
                return true;
            }
            if (!IsDeadCorridorStillBlocked(package.Alliance, deadPlan))
            {
                reason = "The supported corridor opened before the DEAD package took off.";
                return true;
            }
            return false;
        }

        private bool IsDeadCorridorStillBlocked(
            Alliance alliance,
            DeadMissionPlan plan)
        {
            if (plan?.SupportedCorridor == null
                || plan.TargetSiteId == Guid.Empty)
                return false;
            var picture = gameManager.intelligenceSystem?.GetPicture(alliance);
            var threats = knownSamThreatAssessment.BuildKnownThreats(picture);
            var targetThreats = threats
                .Where(threat => threat.SiteId == plan.TargetSiteId)
                .ToList();
            if (targetThreats.Count == 0)
                return false;

            aircraftTypes.TryGetValue(
                plan.SupportedCorridor.RepresentativeAircraftTypeDefinitionId,
                out var representativeType);
            var clearanceFeet = AirspaceGeometry
                .ConservativeSamManeuverClearanceFeet(
                    representativeType);
            if (!targetThreats.Any(threat =>
                    threat.IntersectsSegment(
                        plan.SupportedCorridor.OriginPositionFeet,
                        plan.SupportedCorridor.DestinationPositionFeet,
                        clearanceFeet)
                    || threat.IntersectsSegment(
                        plan.SupportedCorridor.DestinationPositionFeet,
                        plan.SupportedCorridor.RecoveryPositionFeet,
                        clearanceFeet)))
                return false;

            var geometry = deadCorridorRoutePlanner.Plan(
                new AirRouteGeometryPlanningContext(
                    plan.SupportedCorridor.OriginPositionFeet,
                    plan.SupportedCorridor.DestinationPositionFeet,
                    plan.SupportedCorridor.DestinationPositionFeet,
                    plan.SupportedCorridor.RecoveryPositionFeet,
                    CampaignMapCoordinates.TileCenterSpacingFeet,
                    plan.SupportedCorridor
                        .RepresentativeAircraftTypeDefinitionId,
                    threats,
                    clearanceFeet));
            return !geometry.IsThreatSafe;
        }

        private void TryStartNextDeadAttack(
            AirFlight flight,
            Squadron squadron,
            SamSite site,
            Vector3Int siteTileId,
            IReadOnlyCollection<Guid> authorizedComponentIds,
            DateTime currentTime)
        {
            if (flight == null
                || squadron == null
                || site == null
                || !flight.CanEvaluateGroundAttackOpportunity(currentTime)
                || ordnanceEmploymentSystem.ActivePasses.Any(pass =>
                    pass.SourceFlightId == flight.FlightId
                    && pass.TargetKind
                    != OrdnanceEmploymentTargetKind.AirFlight)
                || ordnanceEmploymentSystem.PendingEffects.Any(effect =>
                    effect.SourceFlightId == flight.FlightId
                    && effect.TargetKind
                    != OrdnanceEmploymentTargetKind.AirFlight))
                return;

            if (!gameManager.airDefenseSiteSystem.TryGetPositionFeet(
                    site,
                    out var sitePositionFeet))
                return;

            var sequence = flight.ConsumeGroundAttackOpportunity(
                currentTime,
                retrySeconds: 60d);
            var opportunity = groundAttackOpportunityService
                .RollDeadOpportunity(
                    flight.FlightId,
                    sequence,
                    site,
                    siteTileId,
                    authorizedComponentIds,
                    currentTime,
                    ordnanceEmploymentSystem.HasActiveOrPendingEffect);
            if (!opportunity.HasTargets)
                return;

            var sourceAircraft = squadron.Aircraft
                .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Lost
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Damaged)
                .ToList();
            if (!groundAttackDecisionService.TryPlan(
                    opportunity,
                    sourceAircraft,
                    ordnanceTypes,
                    (target, ordnance) =>
                        IsSuitableDeadOpportunityTarget(
                            site,
                            target,
                            ordnance)
                        && IsWithinGroundReleaseRange(
                            flight,
                            sitePositionFeet,
                            ordnance),
                    out var plan))
                return;

            ordnanceEmploymentSystem.TryStartGroundAttackPass(
                flight.FlightId,
                plan,
                currentTime);
        }

        private bool IsSuitableDeadOpportunityTarget(
            SamSite site,
            GroundAttackOpportunityTarget target,
            OrdnanceTypeDefinition ordnance)
        {
            if (site == null
                || target?.Target?.Kind
                != GroundAttackTargetKind.AirDefenseComponent)
                return false;

            var component = site.Components.FirstOrDefault(candidate =>
                candidate != null
                && candidate.ComponentId == target.Target.EntityId
                && !candidate.IsDamaged);
            if (component == null
                || !airDefenseComponentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                || !DeadLoadoutPlanner.CanAttackComponent(
                    ordnance,
                    definition))
                return false;

            var isAntiRadiation = ordnance.EmploymentCategory
                                  == OrdnanceEmploymentCategory.AntiRadiation
                                  || ordnance.GuidanceMode
                                  == OrdnanceGuidanceMode.AntiRadiation;
            return !isAntiRadiation
                   || component is RadarAirDefenseComponent
                   {
                       IsEmitting: true
                   };
        }

        private bool IsWithinGroundReleaseRange(
            AirFlight flight,
            Vector3 targetPositionFeet,
            OrdnanceTypeDefinition ordnance)
        {
            if (flight == null || ordnance == null)
                return false;

            var distanceKm = HorizontalDistanceKm(
                flight.PositionFeet,
                targetPositionFeet);
            return distanceKm >= ordnance.MinimumRangeKm
                   && distanceKm <= ordnance.MaximumRangeKm;
        }

        private bool CanApproachDeadFireControlRadar(
            AirFlight flight,
            Squadron squadron,
            SamSite site,
            IReadOnlyCollection<Guid> authorizedComponentIds)
        {
            if (flight == null
                || squadron == null
                || site == null
                || ordnanceEmploymentSystem.ActivePasses.Any(pass =>
                    pass.SourceFlightId == flight.FlightId
                    && pass.TargetKind
                    == OrdnanceEmploymentTargetKind.AirDefenseComponent)
                || ordnanceEmploymentSystem.PendingEffects.Any(effect =>
                    effect.SourceFlightId == flight.FlightId
                    && effect.TargetKind
                    == OrdnanceEmploymentTargetKind.AirDefenseComponent))
                return false;

            var authorized = new HashSet<Guid>(
                authorizedComponentIds ?? Array.Empty<Guid>());
            var fireControlRadars = site.Components
                .OfType<RadarAirDefenseComponent>()
                .Where(radar => !radar.IsDamaged
                                && authorized.Contains(radar.ComponentId)
                                && airDefenseComponentDefinitions.TryGetValue(
                                    radar.SamComponentDefinitionId,
                                    out var definition)
                                && definition
                                is RadarAirDefenseComponentDefinition
                                {
                                    ProvidesWeaponQualityTrack: true
                                })
                .ToList();
            if (fireControlRadars.Count == 0)
                return false;

            return squadron.Aircraft
                .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Lost
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Damaged)
                .SelectMany(aircraft => aircraft.Loadout)
                .Any(item => item.Count > 0
                             && ordnanceTypes.TryGetValue(
                                 item.OrdnanceTypeDefinitionId,
                                 out var ordnance)
                             && (ordnance.EmploymentCategory
                                 == OrdnanceEmploymentCategory.AntiRadiation
                                 || ordnance.GuidanceMode
                                 == OrdnanceGuidanceMode.AntiRadiation)
                             && fireControlRadars.Any(radar =>
                                 airDefenseComponentDefinitions.TryGetValue(
                                     radar.SamComponentDefinitionId,
                                     out var definition)
                                 && DeadLoadoutPlanner.CanAttackComponent(
                                     ordnance,
                                     definition)));
        }

        private void ApplyDeadPostLaunchManeuvers(
            IReadOnlyCollection<AirCombatCommand> commands,
            AirCombatFrame frame,
            DateTime currentTime)
        {
            foreach (var command in commands.OrderBy(item => item.FlightId))
            {
                if (!frame.Flights.TryGetValue(command.FlightId, out var view)
                    || !view.Flight.IsDeadAttackFlight
                    || view.Flight.ExecutionPhase != FlightExecutionPhase.Executing
                    || view.Flight.AuthorizedSurfaceThreatSiteId == Guid.Empty
                    || command.Intent != AirCombatIntent.FollowMission
                    || command.Employment != null
                    || command.TargetFlightId != Guid.Empty
                    || command.RequestsWvrEngagement
                    || command.RequestsSurfaceThreatRecovery
                    || wvrEngagementSystem.IsFlightEngaged(command.FlightId)
                    || !gameManager.airDefenseSiteSystem.TryGetSite(
                        view.Flight.AuthorizedSurfaceThreatSiteId,
                        out var site)
                    || !HasPermanentSamShooterChain(site))
                    continue;

                var pendingEffect = ordnanceEmploymentSystem.PendingEffects
                    .Where(effect => effect != null
                                     && !effect.IsDefeated
                                     && effect.SourceFlightId
                                     == view.Flight.FlightId
                                     && effect.TargetKind
                                     == OrdnanceEmploymentTargetKind
                                         .AirDefenseComponent
                                     && effect.TargetSiteId == site.SiteId
                                     && effect.ResolveAt > currentTime
                                     && IsWeaponQualityRadarComponent(
                                         site,
                                         effect.TargetComponentId)
                                     && ordnanceTypes.TryGetValue(
                                         effect.OrdnanceTypeDefinitionId,
                                         out var ordnance)
                                     && (ordnance.EmploymentCategory
                                         == OrdnanceEmploymentCategory
                                             .AntiRadiation
                                         || ordnance.GuidanceMode
                                         == OrdnanceGuidanceMode
                                             .AntiRadiation))
                    .OrderBy(effect => effect.ResolveAt)
                    .FirstOrDefault();
                if (pendingEffect == null)
                    continue;

                var away = view.Flight.PositionFeet
                           - pendingEffect.TargetPositionFeet;
                away.y = 0f;
                if (away.sqrMagnitude < 1f)
                    away = pendingEffect.SourcePositionFeet
                           - pendingEffect.TargetPositionFeet;
                away.y = 0f;
                if (away.sqrMagnitude < 1f)
                    away = Vector3.forward;
                away.Normalize();

                var turnLeft = (view.Flight.FlightId.ToByteArray()[0] & 1) == 0;
                var tangent = turnLeft
                    ? new Vector3(-away.z, 0f, away.x)
                    : new Vector3(away.z, 0f, -away.x);
                var offsetDirection = (tangent + away * 0.35f).normalized;
                var secondsRemaining = Math.Max(
                    TacticalDecisionStepSeconds,
                    (pendingEffect.ResolveAt - currentTime).TotalSeconds);
                var cruiseFeetPerSecond =
                    Math.Max(1f, view.AircraftType.CruiseSpeedKnots)
                    * AirspaceGeometry.FeetPerNauticalMile / 3600f;
                var offsetDistanceFeet = Mathf.Clamp(
                    cruiseFeetPerSecond * (float)secondsRemaining,
                    10f * AirspaceGeometry.FeetPerKilometer,
                    25f * AirspaceGeometry.FeetPerKilometer);

                command.Intent = AirCombatIntent.FollowMission;
                command.Maneuver = AirCombatManeuver.Extend;
                command.TargetFlightId = Guid.Empty;
                command.SupportedPendingEffectId = pendingEffect.PendingEffectId;
                command.PreferredSide = turnLeft
                    ? AirCombatManeuverSide.Left
                    : AirCombatManeuverSide.Right;
                command.AimPointFeet = view.Flight.PositionFeet
                                       + offsetDirection * offsetDistanceFeet;
                command.AimPointFeet.y = view.Flight.PositionFeet.y;
                command.HasAimPoint = true;
                command.DesiredSpeedKnots = view.AircraftType.CruiseSpeedKnots;
                command.MinimumManeuverEndAt = currentTime.AddSeconds(
                    TacticalDecisionStepSeconds);
                command.Reason =
                    "Offsetting from the emitter while the anti-radiation missile resolves.";
                view.Flight.TacticalState.Apply(
                    command.Intent,
                    command.Maneuver,
                    currentTime,
                    command.MinimumManeuverEndAt,
                    command.TargetFlightId,
                    command.SupportedPendingEffectId,
                    command.PreferredSide,
                    command.AimPointFeet,
                    command.HasAimPoint,
                    command.Reason);
            }
        }

        private bool IsWeaponQualityRadarComponent(
            SamSite site,
            Guid componentId)
        {
            var component = site.Components.FirstOrDefault(candidate =>
                candidate != null && candidate.ComponentId == componentId);
            return component != null
                   && airDefenseComponentDefinitions.TryGetValue(
                       component.SamComponentDefinitionId,
                       out var definition)
                   && definition is RadarAirDefenseComponentDefinition
                   {
                       ProvidesWeaponQualityTrack: true
                   };
        }

        private bool HasDeadMissionUsefulOrdnance(
            AirFlight flight,
            Squadron squadron,
            SamSite site,
            IReadOnlyCollection<Guid> authorizedComponentIds)
        {
            var authorized = new HashSet<Guid>(
                authorizedComponentIds ?? Array.Empty<Guid>());
            var survivingTargets = site.Components
                .Where(component => component != null
                                    && !component.IsDamaged
                                    && authorized.Contains(component.ComponentId))
                .Select(component => airDefenseComponentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                    ? definition
                    : null)
                .Where(definition => definition != null)
                .ToList();
            if (survivingTargets.Count == 0)
                return false;

            return squadron.Aircraft
                .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                   && aircraft.Status != CampaignAircraftStatus.Lost
                                   && aircraft.Status != CampaignAircraftStatus.Damaged)
                .SelectMany(aircraft => aircraft.Loadout)
                .Any(item => item.Count > 0
                             && ordnanceTypes.TryGetValue(
                                 item.OrdnanceTypeDefinitionId,
                                 out var ordnance)
                             && survivingTargets.Any(target =>
                                 DeadLoadoutPlanner.CanAttackComponent(
                                     ordnance,
                                     target)));
        }

        private bool HasPermanentSamShooterChain(SamSite site)
        {
            var fireControlRadar = site.Components.Any(component =>
                component is RadarAirDefenseComponent
                && !component.IsDamaged
                && airDefenseComponentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                && definition is RadarAirDefenseComponentDefinition
                {
                    ProvidesWeaponQualityTrack: true
                });
            var launcher = site.Components.Any(component =>
                component is LauncherAirDefenseComponent
                && !component.IsDamaged);
            return fireControlRadar && launcher;
        }

        private bool DidPackageAchieveDeadMinimumEffect(
            AirPackage package,
            SamSite site)
        {
            if (HasPermanentSamShooterChain(site))
                return false;

            var packageFlightIds = package.Flights
                .Select(flight => flight.FlightId)
                .ToHashSet();
            var componentsHitByPackage = GetHitAirDefenseComponentIds(
                ordnanceEmploymentSystem.Records,
                site.SiteId,
                packageFlightIds);
            if (componentsHitByPackage.Count == 0)
                return false;

            var wouldHaveFireControlRadar = site.Components.Any(component =>
                component is RadarAirDefenseComponent
                && (!component.IsDamaged
                    || componentsHitByPackage.Contains(component.ComponentId))
                && airDefenseComponentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                && definition is RadarAirDefenseComponentDefinition
                {
                    ProvidesWeaponQualityTrack: true
                });
            var wouldHaveLauncher = site.Components.Any(component =>
                component is LauncherAirDefenseComponent
                && (!component.IsDamaged
                    || componentsHitByPackage.Contains(component.ComponentId)));
            return wouldHaveFireControlRadar && wouldHaveLauncher;
        }

        internal static HashSet<Guid> GetHitAirDefenseComponentIds(
            IEnumerable<OrdnanceEmploymentRecord> records,
            Guid siteId,
            IReadOnlyCollection<Guid> sourceFlightIds)
        {
            var hits = new HashSet<Guid>();
            if (records == null
                || siteId == Guid.Empty
                || sourceFlightIds == null)
                return hits;

            foreach (var record in records.Where(record =>
                         record != null
                         && record.Stage
                         == OrdnanceEmploymentRecordStage.EffectResolved
                         && record.TargetKind
                         == OrdnanceEmploymentTargetKind.AirDefenseComponent
                         && record.TargetSiteId == siteId
                         && sourceFlightIds.Contains(record.SourceFlightId)))
            {
                foreach (var shot in record.Shots
                             ?? new List<OrdnanceShotDiagnostic>())
                {
                    if (shot == null || shot.Result != OrdnanceShotResult.Hit)
                        continue;

                    var groundTarget = shot.GroundTarget;
                    if (groundTarget != null)
                    {
                        if (groundTarget.Kind
                            == GroundAttackTargetKind.AirDefenseComponent
                            && groundTarget.ParentEntityId == siteId
                            && groundTarget.EntityId != Guid.Empty)
                        {
                            hits.Add(groundTarget.EntityId);
                        }
                    }
                    else if (record.TargetComponentId != Guid.Empty)
                    {
                        hits.Add(record.TargetComponentId);
                    }
                }
            }
            return hits;
        }

        private static float HorizontalDistanceKm(Vector3 first, Vector3 second)
        {
            return Vector2.Distance(
                       new Vector2(first.x, first.z),
                       new Vector2(second.x, second.z))
                   / AirspaceGeometry.FeetPerKilometer;
        }

    }
}
