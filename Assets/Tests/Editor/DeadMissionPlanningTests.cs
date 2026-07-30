using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    public sealed class DeadMissionPlanningTests
    {
        [Test]
        public void MissionAreaUsesPhysicalRadius()
        {
            var area = new AirMissionArea(
                new Vector3Int(0, 0, 0),
                radiusKm: 40f,
                tileDistanceKm: 20f);

            Assert.That(area.Contains(new Vector3Int(2, -2, 0)), Is.True);
            Assert.That(area.Contains(new Vector3Int(3, -3, 0)), Is.False);
        }

        [Test]
        public void MissionAreaUsesEuclideanDistanceBetweenDiagonalHexes()
        {
            var area = new AirMissionArea(
                Vector3Int.zero,
                radiusKm: 35f,
                tileDistanceKm: 20f);

            Assert.That(area.Contains(new Vector3Int(1, 1, -2)), Is.True);
            Assert.That(area.Contains(new Vector3Int(2, -2, 0)), Is.False);
        }

        [Test]
        public void LegacyDoctrineUsesConservativeDeadPriority()
        {
            var doctrine = AllianceAirDoctrine.CreateDefault();
            doctrine.PriorityWeights.Remove(
                AirMissionRequestType.DestructionOfEnemyAirDefenses);

            Assert.That(
                doctrine.GetPriorityWeight(
                    AirMissionRequestType.DestructionOfEnemyAirDefenses),
                Is.EqualTo(0.85f));
        }

        [Test]
        public void DeadCleanupPassUsesFiveNauticalMileRadius()
        {
            Assert.That(
                AirPackageBuilder.DeadLocalPassRadiusNauticalMiles,
                Is.EqualTo(5f));
            Assert.That(
                AirPackageBuilder.DeadLocalPassRadiusNauticalMiles
                * AirspaceGeometry.FeetPerNauticalMile
                / AirspaceGeometry.FeetPerKilometer,
                Is.EqualTo(9.26f).Within(0.01f));
        }

        [Test]
        public void DeadMissionPlanDoesNotOwnItsPlanningObjective()
        {
            var componentId = Guid.NewGuid();
            var original = new DeadMissionPlan
            {
                TargetSiteId = Guid.NewGuid(),
                TargetComponentIds = new List<Guid> { componentId },
                SupportedCorridor = new DeadAirAccessCorridor
                {
                    OriginPositionFeet = Vector3.one,
                    DestinationPositionFeet = Vector3.right,
                    RecoveryPositionFeet = Vector3.back,
                    RepresentativeAltitudeFeet = 25000f,
                    RepresentativeAircraftTypeDefinitionId = Guid.NewGuid()
                }
            };

            var clone = original.Clone();
            clone.TargetComponentIds.Clear();

            Assert.That(clone.TargetSiteId, Is.EqualTo(original.TargetSiteId));
            Assert.That(original.TargetComponentIds, Is.EqualTo(new[] { componentId }));
            Assert.That(
                typeof(DeadMissionPlan).GetFields()
                    .Any(field => field.Name.Contains(
                        "Airport",
                        StringComparison.OrdinalIgnoreCase)),
                Is.False);
        }

        [Test]
        public void DeadLoadoutCarriesHarmsForRadarAndJdamsForFixedCleanup()
        {
            var module = TestModule.GetTestModule();
            var planner = new DeadLoadoutPlanner(
                module,
                _ => module.OrdnanceTypeDefinitions
                    .Select(ordnance => ordnance.OrdnanceTypeDefinitionId)
                    .ToList());
            var aircraft = module.AircraftTypeDefinitions.First(definition =>
                definition.AircraftTypeDefinitionId == TestModule.F16AircraftTypeId);
            var components = new List<AirDefenseComponentIntelligenceReport>
            {
                new AirDefenseComponentIntelligenceReport
                {
                    ComponentId = Guid.NewGuid(),
                    SamComponentDefinitionId = TestModule.FanSongComponentId
                },
                new AirDefenseComponentIntelligenceReport
                {
                    ComponentId = Guid.NewGuid(),
                    SamComponentDefinitionId = TestModule.Sa2LauncherComponentId
                },
                new AirDefenseComponentIntelligenceReport
                {
                    ComponentId = Guid.NewGuid(),
                    SamComponentDefinitionId = TestModule.SamCommandPostComponentId
                }
            };

            var planned = planner.TryPlan(
                aircraft,
                Alliance.Bluefor,
                components,
                out var loadout,
                out var reason);

            Assert.That(planned, Is.True, reason);
            Assert.That(loadout.SelfDefenseShotCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(loadout.MinimumEffectStoreCount, Is.EqualTo(2));
            Assert.That(loadout.CleanupStoreCount, Is.EqualTo(2));
            Assert.That(
                loadout.Loadout.Single(item =>
                    item.OrdnanceTypeDefinitionId
                    == TestModule.Agm88OrdnanceTypeId).Count,
                Is.EqualTo(2));
            Assert.That(
                loadout.Loadout.Single(item =>
                    item.OrdnanceTypeDefinitionId
                    == TestModule.Gbu38OrdnanceTypeId).Count,
                Is.EqualTo(2));
        }

        [Test]
        public void HarmCannotAttackNonRadarSamComponents()
        {
            var module = TestModule.GetTestModule();
            var harm = module.OrdnanceTypeDefinitions.Single(definition =>
                definition.OrdnanceTypeDefinitionId
                == TestModule.Agm88OrdnanceTypeId);
            var radar = module.SamComponentDefinitions.Single(definition =>
                definition.SamComponentDefinitionId
                == TestModule.FanSongComponentId);
            var launcher = module.SamComponentDefinitions.Single(definition =>
                definition.SamComponentDefinitionId
                == TestModule.Sa2LauncherComponentId);
            var commandPost = module.SamComponentDefinitions.Single(definition =>
                definition.SamComponentDefinitionId
                == TestModule.SamCommandPostComponentId);

            Assert.That(
                DeadLoadoutPlanner.CanAttackComponent(harm, radar),
                Is.True);
            Assert.That(
                DeadLoadoutPlanner.CanAttackComponent(harm, launcher),
                Is.False);
            Assert.That(
                DeadLoadoutPlanner.CanAttackComponent(harm, commandPost),
                Is.False);
            Assert.That(
                harm.GetEffectiveness(OrdnanceTargetCategory.Building),
                Is.Zero);
        }

        [Test]
        public void DeadIgnoresUntrackedHotHostile()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 50f,
                includeHostileTrack: false);

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.FollowMission));
            Assert.That(command.Maneuver, Is.EqualTo(AirCombatManeuver.FollowRoute));
            Assert.That(command.Employment, Is.Null);
        }

        [Test]
        public void DeadDoesNotPressForPoorDefensiveShot()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 90f,
                includeHostileTrack: true);

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.FollowMission));
            Assert.That(command.Maneuver, Is.EqualTo(AirCombatManeuver.FollowRoute));
            Assert.That(command.Employment, Is.Null);
        }

        [Test]
        public void DeadPreparesValidDefensiveShotWhileFollowingMissionRoute()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 50f,
                includeHostileTrack: true);

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.FollowMission));
            Assert.That(command.Maneuver, Is.EqualTo(AirCombatManeuver.LaunchSetup));
            Assert.That(
                command.AimPointFeet,
                Is.EqualTo(scenario.Source.Flight.PositionFeet));
            Assert.That(command.Employment, Is.Not.Null);
            Assert.That(
                command.Employment.TargetFlightId,
                Is.EqualTo(scenario.Target.Flight.FlightId));
        }

        [Test]
        public void DeadContinuesRouteDuringDefensiveShotPreparation()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 50f,
                includeHostileTrack: true);
            scenario.Frame.ActivePasses = new[]
            {
                new ActiveOrdnanceEmploymentPass
                {
                    SourceFlightId = scenario.Source.Flight.FlightId,
                    TargetFlightId = scenario.Target.Flight.FlightId,
                    TargetKind = OrdnanceEmploymentTargetKind.AirFlight,
                    OrdnanceTypeDefinitionId = TestModule.Aim120OrdnanceTypeId,
                    PlannedQuantity = 1,
                    PreparationStartedAt = scenario.Frame.Time,
                    ReleaseAt = scenario.Frame.Time.AddSeconds(30)
                }
            };

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.FollowMission));
            Assert.That(command.Maneuver, Is.EqualTo(AirCombatManeuver.LaunchSetup));
            Assert.That(
                command.AimPointFeet,
                Is.EqualTo(scenario.Source.Flight.PositionFeet));
        }

        [Test]
        public void DeadSupportsDefensiveShotUntilItBecomesAutonomous()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 50f,
                includeHostileTrack: true);
            scenario.Frame.PendingEffects = new[]
            {
                CreateDefensivePendingEffect(
                    scenario,
                    scenario.Frame.Time.AddSeconds(20))
            };

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.EngageTarget));
            Assert.That(
                command.Maneuver,
                Is.EqualTo(AirCombatManeuver.CrankLeft)
                    .Or.EqualTo(AirCombatManeuver.CrankRight));
            Assert.That(
                command.SupportedPendingEffectId,
                Is.EqualTo(scenario.Frame.PendingEffects[0].PendingEffectId));
        }

        [Test]
        public void DeadResumesRouteAfterDefensiveShotBecomesAutonomous()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 50f,
                includeHostileTrack: true);
            scenario.Frame.PendingEffects = new[]
            {
                CreateDefensivePendingEffect(
                    scenario,
                    scenario.Frame.Time)
            };

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.FollowMission));
            Assert.That(command.Maneuver, Is.EqualTo(AirCombatManeuver.FollowRoute));
            Assert.That(command.Employment, Is.Null);
        }

        [Test]
        public void DeadDoesNotIgnoreLiveTargetSamWithoutAnAuthorizedAttack()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 50f,
                includeHostileTrack: false);
            var targetSiteId = Guid.NewGuid();
            scenario.Source.Flight.AuthorizedSurfaceThreatSiteId = targetSiteId;
            Assert.That(
                scenario.Source.Flight.CrossCurrentWaypoint(
                    scenario.Frame.Time),
                Is.EqualTo(FlightWaypointTransition.Advanced));
            SetBlueKnownThreat(
                scenario,
                CreateKnownThreat(
                    targetSiteId,
                    scenario.Source.Flight.PositionFeet));

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(
                command.RequestsSurfaceThreatRecovery
                || command.Maneuver
                == AirCombatManeuver.AvoidSurfaceThreat,
                Is.True);
        }

        [Test]
        public void AuthorizedDeadIngressIgnoresAssignedSamWhileOutbound()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 50f,
                includeHostileTrack: false);
            var targetSiteId = Guid.NewGuid();
            scenario.Source.Flight.AuthorizedSurfaceThreatSiteId = targetSiteId;
            scenario.Source.Flight
                .UpdateSurfaceThreatPenetrationAuthorization(true);
            SetBlueKnownThreat(
                scenario,
                CreateKnownThreat(
                    targetSiteId,
                    scenario.Source.Flight.PositionFeet));

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(
                scenario.Source.Flight.ExecutionPhase,
                Is.EqualTo(FlightExecutionPhase.Outbound));
            Assert.That(command.RequestsSurfaceThreatRecovery, Is.False);
            Assert.That(
                command.Maneuver,
                Is.EqualTo(AirCombatManeuver.FollowRoute));
        }

        [Test]
        public void DeadMayHoldTargetSamDuringActiveGroundAttack()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 50f,
                includeHostileTrack: false);
            var targetSiteId = Guid.NewGuid();
            scenario.Source.Flight.AuthorizedSurfaceThreatSiteId = targetSiteId;
            Assert.That(
                scenario.Source.Flight.CrossCurrentWaypoint(
                    scenario.Frame.Time),
                Is.EqualTo(FlightWaypointTransition.Advanced));
            SetBlueKnownThreat(
                scenario,
                CreateKnownThreat(
                    targetSiteId,
                    scenario.Source.Flight.PositionFeet));
            scenario.Frame.ActivePasses = new[]
            {
                new ActiveOrdnanceEmploymentPass
                {
                    SourceFlightId = scenario.Source.Flight.FlightId,
                    TargetKind =
                        OrdnanceEmploymentTargetKind.AirDefenseComponent,
                    TargetSiteId = targetSiteId,
                    TargetComponentId = Guid.NewGuid(),
                    OrdnanceTypeDefinitionId =
                        TestModule.Agm88OrdnanceTypeId,
                    PlannedQuantity = 1,
                    PreparationStartedAt = scenario.Frame.Time,
                    ReleaseAt = scenario.Frame.Time.AddSeconds(45)
                }
            };

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.RequestsSurfaceThreatRecovery, Is.False);
            Assert.That(command.Maneuver, Is.EqualTo(
                AirCombatManeuver.FollowRoute));
        }

        [Test]
        public void AdvancedCampaignProvidesBlueDeadCorridor()
        {
            var template = AdvancedTestCampaign.Create();
            var module = TestModule.GetTestModule();
            var buildingsById = template.BuildingStartingConditions
                .ToDictionary(building => building.BuildingId);
            var divisionsById = template.DivisionStartingConditions
                .ToDictionary(division => division.DivisionId);
            var blueSquadrons = template.SquadronStartingConditions
                .Where(squadron => squadron.CountryId == TestModule.BlueCountryId)
                .Select(squadron => new AirPlanningSquadronSnapshot(
                    squadron.SquadronId,
                    Alliance.Bluefor,
                    squadron.AircraftTypeDefinitionId,
                    squadron.StartingAirportBuildingId,
                    buildingsById[squadron.StartingAirportBuildingId].TileId,
                    squadron.AircraftCount,
                    0))
                .ToList();
            var enemyAirports = template.BuildingStartingConditions
                .Where(building => building.Type == BuildingType.Airport)
                .Where(building => template.StartingTileData
                    .OfType<LandTileData>()
                    .Any(tile => tile.TileId == building.TileId
                                 && tile.Controller == Alliance.Redfor))
                .Select(building => new ObservedEnemyAirportSnapshot
                {
                    AirportBuildingId = building.BuildingId,
                    AirportTileId = building.TileId,
                    InformationQuality = 1f,
                    ObservedAt = template.CampaignStartTime,
                    Condition = ObservedAirportCondition.Intact,
                    BuildLevel = building.Level.BuildLevel,
                    FunctionalLevel = building.Level.FunctionalLevel,
                    TargetToughness = 1
                })
                .ToList();
            var hostileSites = template.BuildingStartingConditions
                .Where(building => building.Type == BuildingType.AirDefense
                                   && building.CountryId == TestModule.RedCountryId)
                .Select(building => BuildKnownSite(
                    module,
                    building.BuildingId,
                    building.SamSiteTemplateId,
                    SamSiteHostType.StaticBuilding,
                    building.BuildingId,
                    building.TileId,
                    template.CampaignStartTime))
                .Concat(template.MobileSamSiteStartingConditions
                    .Where(site => site.Alliance == Alliance.Redfor)
                    .Select(site => BuildKnownSite(
                        module,
                        site.MobileSamSiteId,
                        site.SamSiteTemplateId,
                        SamSiteHostType.MobileDivision,
                        site.HostDivisionId,
                        divisionsById[site.HostDivisionId].TileId,
                        template.CampaignStartTime)))
                .ToList();
            var snapshot = new AirPlanningSnapshot(
                Alliance.Bluefor,
                template.CampaignStartTime,
                template.SimulationSettings.TileDistanceKM,
                blueSquadrons,
                blueSquadrons.Select(squadron => squadron.AirportTileId)
                    .Distinct()
                    .ToList(),
                blueSquadrons.Select(squadron => squadron.AirportTileId)
                    .Distinct()
                    .ToList(),
                enemyAirports,
                hostileSites,
                Array.Empty<Vector3Int>(),
                Array.Empty<Vector3Int>(),
                Array.Empty<Vector3Int>());
            var planner = new DeadCorridorPlanner(
                module,
                alliance => template.OrdnanceAllowances[alliance]);

            var planned = planner.TryPlan(snapshot, out var candidate);

            Assert.That(planned, Is.True);
            Assert.That(candidate.TargetSite.TileId, Is.EqualTo(
                new Vector3Int(2, 0, -2)));
            Assert.That(
                template.AirDoctrineByAlliance[Alliance.Bluefor]
                    .GetPriorityWeight(
                        AirMissionRequestType.DestructionOfEnemyAirDefenses),
                Is.EqualTo(0.85f));
        }

        private static DeadAirCombatScenario CreateDeadAirCombatScenario(
            float hostileDistanceKm,
            bool includeHostileTrack)
        {
            var now = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var module = TestModule.GetTestModule();
            var ordnanceTypes = module.OrdnanceTypeDefinitions
                .ToDictionary(
                    ordnance => ordnance.OrdnanceTypeDefinitionId);
            var sourceType = module.AircraftTypeDefinitions.First(definition =>
                definition.AircraftTypeDefinitionId == TestModule.F16AircraftTypeId);
            var targetType = module.AircraftTypeDefinitions.First(definition =>
                definition.AircraftTypeDefinitionId == TestModule.Mig29AircraftTypeId);
            var sourcePosition = new Vector3(0f, 40000f, 0f);
            var targetPosition = sourcePosition
                                 + Vector3.forward
                                 * hostileDistanceKm
                                 * AirspaceGeometry.FeetPerKilometer;
            var source = CreateAirCombatFlight(
                Alliance.Bluefor,
                AirMissionRequestType.DestructionOfEnemyAirDefenses,
                sourceType,
                sourcePosition,
                headingDegrees: 0f,
                TestModule.Aim120OrdnanceTypeId,
                now);
            var target = CreateAirCombatFlight(
                Alliance.Redfor,
                AirMissionRequestType.OffensiveCounterAirSweep,
                targetType,
                targetPosition,
                headingDegrees: 180f,
                TestModule.R27OrdnanceTypeId,
                now);
            var flights = new Dictionary<Guid, AirCombatFlightView>
            {
                { source.Flight.FlightId, source },
                { target.Flight.FlightId, target }
            };
            var tracks = new Dictionary<Guid, IADSTrack>();
            if (includeHostileTrack)
            {
                tracks[target.Flight.FlightId] = new IADSTrack(
                    target.Flight.FlightId,
                    targetPosition,
                    target.LiveAircraft.Count,
                    target.LiveAircraft.Count
                    * targetType.AirInterferenceCapability,
                    target.Flight.HeadingDegrees,
                    target.Flight.SpeedKnots,
                    quality: 1f,
                    now);
            }

            return new DeadAirCombatScenario
            {
                Source = source,
                Target = target,
                OrdnanceTypes = ordnanceTypes,
                Frame = new AirCombatFrame
                {
                    Time = now,
                    TileDistanceKm = 20f,
                    Flights = flights,
                    AirCommanders =
                        new Dictionary<Alliance, AllianceAirTaskingCommander>(),
                    CurrentTracksByAlliance =
                        new Dictionary<Alliance, IReadOnlyDictionary<Guid, IADSTrack>>
                        {
                            { Alliance.Bluefor, tracks },
                            {
                                Alliance.Redfor,
                                new Dictionary<Guid, IADSTrack>()
                            }
                        },
                    ActivePasses = Array.Empty<ActiveOrdnanceEmploymentPass>(),
                    PendingEffects = Array.Empty<PendingOrdnanceEffect>(),
                    BarcapTargetByFlightId = new Dictionary<Guid, Guid>(),
                    KnownSamThreatsByAlliance =
                        new Dictionary<Alliance, IReadOnlyList<KnownSamThreatEnvelope>>
                        {
                            {
                                Alliance.Bluefor,
                                Array.Empty<KnownSamThreatEnvelope>()
                            },
                            {
                                Alliance.Redfor,
                                Array.Empty<KnownSamThreatEnvelope>()
                            }
                        }
                }
            };
        }

        private static PendingOrdnanceEffect CreateDefensivePendingEffect(
            DeadAirCombatScenario scenario,
            DateTime autonomousAt)
        {
            return new PendingOrdnanceEffect
            {
                SourceKind = OrdnanceEmploymentSourceKind.AircraftFlight,
                SourceFlightId = scenario.Source.Flight.FlightId,
                TargetFlightId = scenario.Target.Flight.FlightId,
                TargetKind = OrdnanceEmploymentTargetKind.AirFlight,
                OrdnanceTypeDefinitionId = TestModule.Aim120OrdnanceTypeId,
                Quantity = 1,
                ReleasedAt = scenario.Frame.Time.AddSeconds(-5),
                ResolveAt = scenario.Frame.Time.AddMinutes(1),
                AutonomousAt = autonomousAt,
                SupportRequired = true,
                SupportSourceFlightId = scenario.Source.Flight.FlightId
            };
        }

        private static KnownSamThreatEnvelope CreateKnownThreat(
            Guid siteId,
            Vector3 flightPosition)
        {
            var center = flightPosition;
            center.y = 0f;
            return new KnownSamThreatEnvelope(
                siteId,
                center,
                maximumSlantRangeFeet:
                80f * AirspaceGeometry.FeetPerKilometer,
                minimumAltitudeFeet: 0f,
                maximumAltitudeFeet: 80000f);
        }

        private static void SetBlueKnownThreat(
            DeadAirCombatScenario scenario,
            KnownSamThreatEnvelope threat)
        {
            scenario.Frame.KnownSamThreatsByAlliance =
                new Dictionary<Alliance, IReadOnlyList<KnownSamThreatEnvelope>>
                {
                    {
                        Alliance.Bluefor,
                        new[] { threat }
                    },
                    {
                        Alliance.Redfor,
                        Array.Empty<KnownSamThreatEnvelope>()
                    }
                };
        }

        private static AirCombatFlightView CreateAirCombatFlight(
            Alliance alliance,
            AirMissionRequestType missionType,
            AircraftTypeDefinition aircraftType,
            Vector3 position,
            float headingDegrees,
            Guid airToAirOrdnanceTypeId,
            DateTime now)
        {
            var squadron = new Squadron
            {
                SquadronId = Guid.NewGuid(),
                AircraftTypeDefinitionId =
                    aircraftType.AircraftTypeDefinitionId,
                CountryId = Guid.NewGuid(),
                AirportBuildingId = Guid.NewGuid()
            };
            var flight = new AirFlight
            {
                SquadronId = squadron.SquadronId,
                MissionType = missionType
            };
            var aircraft = new CampaignAircraft(
                squadron.SquadronId,
                aircraftType.AircraftTypeDefinitionId);
            aircraft.TryAssignToFlight(flight.FlightId);
            aircraft.SetLoadout(new[]
            {
                new AircraftLoadoutItem(airToAirOrdnanceTypeId, 2)
            });
            squadron.Aircraft.Add(aircraft);
            flight.AircraftIds.Add(aircraft.AircraftId);
            flight.MaterializeRoute(new[]
            {
                new AirWaypoint(
                    position,
                    AirWaypointAction.Takeoff,
                    now,
                    airportBuildingId: squadron.AirportBuildingId),
                new AirWaypoint(
                    position + Vector3.forward
                    * 200f
                    * AirspaceGeometry.FeetPerKilometer,
                    AirWaypointAction.MissionAction,
                    now.AddHours(1),
                    new AirMissionArea(
                        Vector3Int.zero,
                        radiusKm: 40f,
                        tileDistanceKm: 20f)),
                new AirWaypoint(
                    position,
                    AirWaypointAction.Land,
                    now.AddHours(2),
                    airportBuildingId: squadron.AirportBuildingId)
            });
            Assert.That(flight.TryTakeOff(now), Is.True);
            flight.UpdateKinematics(
                position,
                headingDegrees,
                aircraftType.CruiseSpeedKnots);
            var package = new AirPackage
            {
                Alliance = alliance,
                CreatedAt = now
            };
            package.Flights.Add(flight);
            return new AirCombatFlightView
            {
                Alliance = alliance,
                Package = package,
                Flight = flight,
                Squadron = squadron,
                AircraftType = aircraftType,
                LiveAircraft = new List<CampaignAircraft> { aircraft },
                WvrAircraft = new List<CampaignAircraft> { aircraft }
            };
        }

        private static AirDefenseSiteIntelligenceReport BuildKnownSite(
            ModuleDefinition module,
            Guid siteId,
            Guid siteTemplateId,
            SamSiteHostType hostType,
            Guid hostId,
            Vector3Int tileId,
            DateTime observedAt)
        {
            var siteTemplate = module.SamSiteTemplates.First(template =>
                template.SamSiteTemplateId == siteTemplateId);
            return new AirDefenseSiteIntelligenceReport
            {
                SiteId = siteId,
                SamSiteTemplateId = siteTemplateId,
                HostType = hostType,
                HostId = hostId,
                TileId = tileId,
                InformationQuality = 1f,
                ObservedAt = observedAt,
                Components = siteTemplate.Components
                    .SelectMany(component => Enumerable.Range(0, component.Count)
                        .Select(_ => new AirDefenseComponentIntelligenceReport
                        {
                            ComponentId = Guid.NewGuid(),
                            SamComponentDefinitionId =
                                component.SamComponentDefinitionId,
                            ReadyRounds = 1,
                            ReserveRounds = 1
                        }))
                    .ToList()
            };
        }

        private sealed class DeadAirCombatScenario
        {
            public AirCombatFlightView Source;
            public AirCombatFlightView Target;
            public AirCombatFrame Frame;
            public IReadOnlyDictionary<Guid, OrdnanceTypeDefinition>
                OrdnanceTypes;
        }
    }
}
