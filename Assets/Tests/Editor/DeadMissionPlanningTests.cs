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
        public void EscortedDeadLoadoutGivesDeadEffectFirstClaimOnCapacity()
        {
            var module = TestModule.GetTestModule();
            var planner = new DeadLoadoutPlanner(
                module,
                _ => module.OrdnanceTypeDefinitions
                    .Select(ordnance => ordnance.OrdnanceTypeDefinitionId)
                    .ToList());
            var aircraft = new AircraftTypeDefinition(
                Guid.NewGuid(),
                "Limited-capacity DEAD aircraft",
                cruiseSpeedKnots: 450f,
                combatSpeedKnots: 520f,
                climbRateFeetPerMinute: 15000f,
                descentRateFeetPerMinute: 10000f,
                turnRateDegreesPerSecond: 3f,
                nominalCruiseAltitudeFeet: 30000f,
                serviceCeilingFeet: 45000f,
                rangeKm: 1200f,
                enduranceHours: 2f,
                radarQuality: 0.6f,
                radarDetectability: 0.9f,
                radarDefense: 0.5f,
                infraredDefense: 0.5f,
                gunDefense: 0.5f,
                survivability: 0.6f,
                ordnanceCapacity: 6f,
                compatibleOrdnanceTypeDefinitionIds: new List<Guid>
                {
                    TestModule.Aim120OrdnanceTypeId,
                    TestModule.Agm88OrdnanceTypeId
                },
                airInterferenceCapability: 0.8f);
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
                }
            };

            var planned = planner.TryPlan(
                aircraft,
                Alliance.Bluefor,
                components,
                out var loadout,
                out var reason,
                requireSelfDefense: false);

            Assert.That(planned, Is.True, reason);
            Assert.That(loadout.MinimumEffectStoreCount, Is.EqualTo(2));
            Assert.That(loadout.SelfDefenseShotCount, Is.Zero);
            Assert.That(
                loadout.Loadout.Single(item =>
                    item.OrdnanceTypeDefinitionId
                    == TestModule.Agm88OrdnanceTypeId).Count,
                Is.EqualTo(2));
        }

        [Test]
        public void EscortedDeadLoadoutCountsEveryMissionUsefulSelfDefenseShot()
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
                }
            };

            var planned = planner.TryPlan(
                aircraft,
                Alliance.Bluefor,
                components,
                out var loadout,
                out var reason,
                requireSelfDefense: false);

            Assert.That(planned, Is.True, reason);
            var actualSelfDefenseShots = loadout.Loadout
                .Where(item => module.OrdnanceTypeDefinitions.Any(ordnance =>
                    ordnance.OrdnanceTypeDefinitionId
                    == item.OrdnanceTypeDefinitionId
                    && AirLoadoutPlanner.IsAirToAir(ordnance)
                    && ordnance.GetEffectiveness(
                        OrdnanceTargetCategory.Aircraft) > 0f))
                .Sum(item => item.Count);
            Assert.That(
                actualSelfDefenseShots,
                Is.GreaterThanOrEqualTo(aircraft.InternalGunBurstCount));
            Assert.That(
                loadout.SelfDefenseShotCount,
                Is.EqualTo(actualSelfDefenseShots));
        }

        [Test]
        public void DeadOrganicCombatPowerRequiresMissionUsefulSelfDefenseShots()
        {
            Assert.That(
                AirPackageBuilder.CalculateOrganicDeadSelfDefenseCombatPower(
                    aircraftCount: 2,
                    combatPowerPerAircraft: 1f,
                    missionUsefulShotsPerAircraft: 0),
                Is.Zero);
            Assert.That(
                AirPackageBuilder.CalculateOrganicDeadSelfDefenseCombatPower(
                    aircraftCount: 2,
                    combatPowerPerAircraft: 1f,
                    missionUsefulShotsPerAircraft:
                    AirLoadoutPlanner.MinimumAirCombatShots),
                Is.EqualTo(2f));
        }

        [Test]
        public void SpatialBarcapGeometryAcceptsExplicitEscortWithoutCoverage()
        {
            var request = new AirMissionRequest
            {
                RequestType = AirMissionRequestType.BarrierCombatAirPatrol,
                BarcapBarrier = new BarcapBarrierPlan
                {
                    BarrierTileIds = new List<Vector3Int> { Vector3Int.zero }
                }
            };
            var escort = new AirFlight
            {
                MissionType = AirMissionRequestType.BarrierCombatAirPatrol,
                Role = AirFlightRole.FighterEscort
            };
            var primary = new AirFlight
            {
                MissionType = AirMissionRequestType.BarrierCombatAirPatrol
            };

            Assert.That(
                AllianceAirTaskingCommander.SatisfiesMissionGeometry(
                    request,
                    escort),
                Is.True);
            Assert.That(
                AllianceAirTaskingCommander.SatisfiesMissionGeometry(
                    request,
                    primary),
                Is.False);
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
                hostileDistanceKm: 110f,
                includeHostileTrack: true);
            var weapon = scenario.OrdnanceTypes[TestModule.Aim120OrdnanceTypeId];
            var doctrine = AllianceAirDoctrine.CreateDefault();
            var envelopeKm = AirCombatRules.EffectiveLaunchEnvelopeKm(
                weapon,
                scenario.Source.Flight,
                scenario.Target.Flight);
            AirCombatRules.EvaluateLaunch(
                scenario.Source.Flight,
                scenario.Source.AircraftType,
                scenario.Target.Flight,
                weapon,
                out var launchQuality);

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                doctrine);

            Assert.That(envelopeKm, Is.GreaterThan(110f));
            Assert.That(launchQuality, Is.LessThan(doctrine.MinimumLaunchQuality));
            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.FollowMission));
            Assert.That(command.Maneuver, Is.EqualTo(AirCombatManeuver.FollowRoute));
            Assert.That(command.Employment, Is.Null);
            StringAssert.Contains("DEAD route", command.Reason);
        }

        [Test]
        public void DeadStaysOnRouteWhenOnlyReserveAirToAirOrdnanceRemains()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 50f,
                includeHostileTrack: true);
            var doctrine = AllianceAirDoctrine.CreateDefault();
            doctrine.MinimumAirToAirWeaponReserve = 2;
            foreach (var aircraft in scenario.Source.LiveAircraft)
            {
                aircraft.SetLoadout(new[]
                {
                    new AircraftLoadoutItem(TestModule.Aim120OrdnanceTypeId, 2)
                });
            }

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                doctrine);

            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.FollowMission));
            Assert.That(command.Maneuver, Is.EqualTo(AirCombatManeuver.FollowRoute));
            Assert.That(command.Employment, Is.Null);
            StringAssert.Contains("No expendable", command.Reason);
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
        public void FighterEscortPressesThreatToProtectedDeadFlight()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 75f,
                includeHostileTrack: true);
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
            AttachProtectedDeadFlight(scenario);

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.EngageTarget));
            Assert.That(
                command.Maneuver,
                Is.EqualTo(AirCombatManeuver.Intercept)
                    .Or.EqualTo(AirCombatManeuver.Press)
                    .Or.EqualTo(AirCombatManeuver.LaunchSetup));
            Assert.That(
                command.TargetFlightId,
                Is.EqualTo(scenario.Target.Flight.FlightId));
        }

        [Test]
        public void FighterEscortFiresValidShotInsidePreferredRange()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 50f,
                includeHostileTrack: true);
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
            AttachProtectedDeadFlight(scenario);

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.Maneuver, Is.EqualTo(AirCombatManeuver.LaunchSetup));
            Assert.That(command.Employment, Is.Not.Null);
            Assert.That(
                command.Employment.TargetFlightId,
                Is.EqualTo(scenario.Target.Flight.FlightId));
        }

        [Test]
        public void FighterEscortTurnsTowardThreatWhenInsidePreferredRangeButOffBoresight()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 50f,
                includeHostileTrack: true);
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
            AttachProtectedDeadFlight(scenario);
            scenario.Source.Flight.UpdateKinematics(
                scenario.Source.Flight.PositionFeet,
                heading: 180f,
                currentSpeedKnots: scenario.Source.Flight.SpeedKnots);

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            var targetDirection = scenario.Target.Flight.PositionFeet
                                  - scenario.Source.Flight.PositionFeet;
            targetDirection.y = 0f;
            var commandDirection = command.AimPointFeet
                                   - scenario.Source.Flight.PositionFeet;
            commandDirection.y = 0f;
            Assert.That(command.Maneuver, Is.EqualTo(AirCombatManeuver.Press));
            Assert.That(command.Employment, Is.Null);
            Assert.That(Vector3.Dot(commandDirection, targetDirection), Is.GreaterThan(0f));
            StringAssert.Contains("launch boresight", command.Reason);
        }

        [Test]
        public void RadarMissileEnvelopeFavorsClosingOverOpeningTarget()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 90f,
                includeHostileTrack: true);
            var weapon = scenario.OrdnanceTypes[TestModule.Aim120OrdnanceTypeId];

            scenario.Target.Flight.UpdateKinematics(
                scenario.Target.Flight.PositionFeet,
                heading: 180f,
                currentSpeedKnots: scenario.Target.Flight.SpeedKnots);
            var closingCanLaunch = AirCombatRules.EvaluateLaunch(
                scenario.Source.Flight,
                scenario.Source.AircraftType,
                scenario.Target.Flight,
                weapon,
                out _);
            var closingRange = AirCombatRules.EffectiveLaunchEnvelopeKm(
                weapon,
                scenario.Source.Flight,
                scenario.Target.Flight);

            scenario.Target.Flight.UpdateKinematics(
                scenario.Target.Flight.PositionFeet,
                heading: 0f,
                currentSpeedKnots: scenario.Target.Flight.SpeedKnots);
            var openingCanLaunch = AirCombatRules.EvaluateLaunch(
                scenario.Source.Flight,
                scenario.Source.AircraftType,
                scenario.Target.Flight,
                weapon,
                out _);
            var openingRange = AirCombatRules.EffectiveLaunchEnvelopeKm(
                weapon,
                scenario.Source.Flight,
                scenario.Target.Flight);

            Assert.That(closingRange, Is.GreaterThan(openingRange));
            Assert.That(closingCanLaunch, Is.True);
            Assert.That(openingCanLaunch, Is.False);
        }

        [Test]
        public void RadarLaunchEnvelopeDiffersFromShooterKinematicRange()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 90f,
                includeHostileTrack: true);
            var weapon = scenario.OrdnanceTypes[TestModule.Aim120OrdnanceTypeId];
            var kinematicRange = AirCombatRules.EffectiveMaximumRangeKm(
                weapon,
                scenario.Source.Flight);

            var closingEnvelope = AirCombatRules.EffectiveLaunchEnvelopeKm(
                weapon,
                scenario.Source.Flight,
                scenario.Target.Flight);
            scenario.Target.Flight.UpdateKinematics(
                scenario.Target.Flight.PositionFeet,
                heading: 0f,
                currentSpeedKnots: scenario.Target.Flight.SpeedKnots);
            var openingEnvelope = AirCombatRules.EffectiveLaunchEnvelopeKm(
                weapon,
                scenario.Source.Flight,
                scenario.Target.Flight);

            Assert.That(closingEnvelope, Is.GreaterThan(kinematicRange));
            Assert.That(openingEnvelope, Is.LessThan(kinematicRange));
        }

        [Test]
        public void RadarLaunchQualityFallsInTheTargetBeam()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 40f,
                includeHostileTrack: true);
            var weapon = scenario.OrdnanceTypes[TestModule.Aim120OrdnanceTypeId];

            AirCombatRules.EvaluateLaunch(
                scenario.Source.Flight,
                scenario.Source.AircraftType,
                scenario.Target.Flight,
                weapon,
                out var headOnQuality);

            scenario.Target.Flight.UpdateKinematics(
                scenario.Target.Flight.PositionFeet,
                heading: 90f,
                currentSpeedKnots: scenario.Target.Flight.SpeedKnots);
            var beamCanLaunch = AirCombatRules.EvaluateLaunch(
                scenario.Source.Flight,
                scenario.Source.AircraftType,
                scenario.Target.Flight,
                weapon,
                out var beamQuality);

            Assert.That(beamCanLaunch, Is.True);
            Assert.That(beamQuality, Is.LessThan(headOnQuality));
            Assert.That(beamQuality, Is.GreaterThan(0f));
        }

        [Test]
        public void FighterEscortDropsRetainedContactForThreatTargetingProtectedDeadFlight()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 75f,
                includeHostileTrack: true);
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
            var protectedFlight = AttachProtectedDeadFlight(scenario);
            scenario.Source.Flight.TacticalState.Intent =
                AirCombatIntent.EngageTarget;
            scenario.Source.Flight.TacticalState.TargetFlightId =
                scenario.Target.Flight.FlightId;

            var attacker = CreateAirCombatFlight(
                Alliance.Redfor,
                AirMissionRequestType.OffensiveCounterAirSweep,
                scenario.Target.AircraftType,
                scenario.Source.Flight.PositionFeet
                + Vector3.right * 60f * AirspaceGeometry.FeetPerKilometer,
                headingDegrees: 270f,
                TestModule.R27OrdnanceTypeId,
                scenario.Frame.Time);
            attacker.Flight.TacticalState.Intent = AirCombatIntent.EngageTarget;
            attacker.Flight.TacticalState.TargetFlightId =
                protectedFlight.Flight.FlightId;
            AddTrackedHostileFlight(scenario, attacker);

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.EngageTarget));
            Assert.That(
                command.TargetFlightId,
                Is.EqualTo(attacker.Flight.FlightId));
        }

        [Test]
        public void FighterEscortCancelsLowerPriorityShotForProtectedFlightAttack()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 75f,
                includeHostileTrack: true);
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
            var protectedFlight = AttachProtectedDeadFlight(scenario);
            var attacker = CreateAirCombatFlight(
                Alliance.Redfor,
                AirMissionRequestType.OffensiveCounterAirSweep,
                scenario.Target.AircraftType,
                scenario.Source.Flight.PositionFeet
                + Vector3.right * 60f * AirspaceGeometry.FeetPerKilometer,
                headingDegrees: 270f,
                TestModule.R27OrdnanceTypeId,
                scenario.Frame.Time);
            AddTrackedHostileFlight(scenario, attacker);
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
                },
                new ActiveOrdnanceEmploymentPass
                {
                    SourceFlightId = attacker.Flight.FlightId,
                    TargetFlightId = protectedFlight.Flight.FlightId,
                    TargetKind = OrdnanceEmploymentTargetKind.AirFlight,
                    OrdnanceTypeDefinitionId = TestModule.R27OrdnanceTypeId,
                    PlannedQuantity = 1,
                    PreparationStartedAt = scenario.Frame.Time,
                    ReleaseAt = scenario.Frame.Time.AddSeconds(20)
                }
            };

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.RequestsAirToAirPassCancellation, Is.True);
            Assert.That(
                command.TargetFlightId,
                Is.EqualTo(attacker.Flight.FlightId));
            StringAssert.Contains("protected flight", command.Reason);
        }

        [Test]
        public void FighterEscortIgnoresContactCommittedAgainstDifferentFlight()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 75f,
                includeHostileTrack: true);
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
            AttachProtectedDeadFlight(scenario);
            scenario.Target.Flight.TacticalState.Intent =
                AirCombatIntent.EngageTarget;
            scenario.Target.Flight.TacticalState.TargetFlightId = Guid.NewGuid();

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.FollowMission));
            Assert.That(command.Maneuver, Is.EqualTo(AirCombatManeuver.FollowRoute));
            Assert.That(command.TargetFlightId, Is.EqualTo(Guid.Empty));
        }

        [Test]
        public void FighterEscortDoesNotPursueThreatTurningAwayFromProtectedFlight()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 75f,
                includeHostileTrack: true);
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
            AttachProtectedDeadFlight(scenario);
            scenario.Frame.CurrentTracksByAlliance[Alliance.Bluefor][
                scenario.Target.Flight.FlightId].EstimatedHeadingDegrees = 0f;

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
        public void FighterEscortCancelsPreparedShotWhenThreatTurnsAway()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 75f,
                includeHostileTrack: true);
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
            AttachProtectedDeadFlight(scenario);
            scenario.Frame.CurrentTracksByAlliance[Alliance.Bluefor][
                scenario.Target.Flight.FlightId].EstimatedHeadingDegrees = 0f;
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
            Assert.That(command.Maneuver, Is.EqualTo(AirCombatManeuver.FollowRoute));
            Assert.That(command.RequestsAirToAirPassCancellation, Is.True);
            Assert.That(command.Employment, Is.Null);
        }

        [Test]
        public void FighterEscortKeepsPreparedShotAgainstAuthorizedThreat()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 75f,
                includeHostileTrack: true);
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
            AttachProtectedDeadFlight(scenario);
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

            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.EngageTarget));
            Assert.That(command.Maneuver, Is.EqualTo(AirCombatManeuver.LaunchSetup));
            Assert.That(command.RequestsAirToAirPassCancellation, Is.False);
            Assert.That(
                command.TargetFlightId,
                Is.EqualTo(scenario.Target.Flight.FlightId));
        }

        [Test]
        public void TrackIdentifiesAircraftTypeAtHalfQuality()
        {
            var track = new IADSTrack(
                Guid.NewGuid(),
                Vector3.zero,
                estimatedAircraftCount: 1,
                estimatedAirCombatPower: 1f,
                estimatedHeadingDegrees: 0f,
                estimatedSpeedKnots: 400f,
                quality: 0.5f,
                observedAt: DateTime.UtcNow);

            track.IdentifyAircraftType(TestModule.Mig29AircraftTypeId);

            Assert.That(track.HasIdentifiedAircraftType, Is.True);
            Assert.That(
                track.IdentifiedAircraftTypeDefinitionId,
                Is.EqualTo(TestModule.Mig29AircraftTypeId));

            var lowerQualityTrack = new IADSTrack(
                Guid.NewGuid(),
                Vector3.zero,
                estimatedAircraftCount: 1,
                estimatedAirCombatPower: 1f,
                estimatedHeadingDegrees: 0f,
                estimatedSpeedKnots: 400f,
                quality: 0.49f,
                observedAt: DateTime.UtcNow);

            lowerQualityTrack.IdentifyAircraftType(TestModule.Mig29AircraftTypeId);

            Assert.That(lowerQualityTrack.HasIdentifiedAircraftType, Is.False);
        }

        [Test]
        public void FighterEscortIgnoresIdentifiedSupportAirframeApproachingPackage()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 75f,
                includeHostileTrack: true);
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
            AttachProtectedDeadFlight(scenario);
            scenario.Frame.CurrentTracksByAlliance[Alliance.Bluefor][
                    scenario.Target.Flight.FlightId]
                .IdentifiedAircraftTypeDefinitionId = TestModule.A50AircraftTypeId;

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.FollowMission));
            Assert.That(command.TargetFlightId, Is.EqualTo(Guid.Empty));
            Assert.That(command.ObservedThreatCandidateFlightId, Is.EqualTo(Guid.Empty));
        }

        [Test]
        public void FighterEscortTreatsUnidentifiedApproachingAirframeConservatively()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 75f,
                includeHostileTrack: true);
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
            AttachProtectedDeadFlight(scenario);
            scenario.Frame.CurrentTracksByAlliance[Alliance.Bluefor][
                    scenario.Target.Flight.FlightId]
                .IdentifiedAircraftTypeDefinitionId = Guid.Empty;

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.EngageTarget));
            Assert.That(
                command.TargetFlightId,
                Is.EqualTo(scenario.Target.Flight.FlightId));
        }

        [Test]
        public void FighterEscortRequiresPersistentNonUrgentConvergence()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 200f,
                includeHostileTrack: true);
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
            AttachProtectedDeadFlight(scenario);
            scenario.Source.Flight.UpdateKinematics(
                scenario.Source.Flight.PositionFeet
                - Vector3.right * 150f * AirspaceGeometry.FeetPerKilometer,
                scenario.Source.Flight.HeadingDegrees,
                scenario.Source.Flight.SpeedKnots);

            var first = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());
            Assert.That(first.Intent, Is.EqualTo(AirCombatIntent.FollowMission));
            Assert.That(
                first.ObservedThreatCandidateFlightId,
                Is.EqualTo(scenario.Target.Flight.FlightId));
            scenario.Source.Flight.TacticalState.ObserveThreatCandidate(
                first.ObservedThreatCandidateFlightId,
                scenario.Frame.Time,
                TimeSpan.FromSeconds(30));

            scenario.Frame.Time = scenario.Frame.Time.AddSeconds(15);
            var second = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());
            Assert.That(second.Intent, Is.EqualTo(AirCombatIntent.FollowMission));
            scenario.Source.Flight.TacticalState.ObserveThreatCandidate(
                second.ObservedThreatCandidateFlightId,
                scenario.Frame.Time,
                TimeSpan.FromSeconds(30));

            scenario.Frame.Time = scenario.Frame.Time.AddSeconds(15);
            var third = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(third.Intent, Is.EqualTo(AirCombatIntent.EngageTarget));
            Assert.That(
                third.TargetFlightId,
                Is.EqualTo(scenario.Target.Flight.FlightId));
        }

        [Test]
        public void ThreatObservationPersistenceExpiresAfterTrackGap()
        {
            var state = new FlightTacticalState();
            var threatFlightId = Guid.NewGuid();
            var firstObservation = DateTime.UtcNow;
            var maximumGap = TimeSpan.FromSeconds(30);

            state.ObserveThreatCandidate(
                threatFlightId,
                firstObservation,
                maximumGap);
            state.ObserveThreatCandidate(
                threatFlightId,
                firstObservation.AddSeconds(15),
                maximumGap);

            Assert.That(
                state.HasPersistentThreatObservation(
                    threatFlightId,
                    firstObservation.AddSeconds(30),
                    TimeSpan.FromSeconds(30),
                    maximumGap),
                Is.True);
            Assert.That(
                state.HasPersistentThreatObservation(
                    threatFlightId,
                    firstObservation.AddSeconds(46),
                    TimeSpan.FromSeconds(30),
                    maximumGap),
                Is.False);
        }

        [Test]
        public void BarcapIgnoresIdentifiedSupportAirframeApproachingPatrolArea()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 70f,
                includeHostileTrack: true);
            var barcap = CreateAirCombatFlight(
                Alliance.Bluefor,
                AirMissionRequestType.BarrierCombatAirPatrol,
                scenario.Source.AircraftType,
                scenario.Source.Flight.PositionFeet,
                headingDegrees: 0f,
                TestModule.Aim120OrdnanceTypeId,
                scenario.Frame.Time,
                barcapStation: true);
            scenario.Frame.Flights = new Dictionary<Guid, AirCombatFlightView>
            {
                { barcap.Flight.FlightId, barcap },
                { scenario.Target.Flight.FlightId, scenario.Target }
            };
            scenario.Frame.CurrentTracksByAlliance[Alliance.Bluefor][
                    scenario.Target.Flight.FlightId]
                .IdentifiedAircraftTypeDefinitionId = TestModule.A50AircraftTypeId;

            var assignments = AirCombatRules.BuildBarcapAssignments(
                scenario.Frame,
                scenario.OrdnanceTypes,
                _ => AllianceAirDoctrine.CreateDefault());

            Assert.That(assignments, Is.Empty);
            scenario.Frame.BarcapTargetByFlightId = assignments;
            var command = AirCombatRules.Decide(
                barcap,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());
            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.FollowMission));
            Assert.That(command.TargetFlightId, Is.EqualTo(Guid.Empty));

            scenario.Target.Flight.TacticalState.Intent =
                AirCombatIntent.EngageTarget;
            scenario.Target.Flight.TacticalState.TargetFlightId =
                barcap.Flight.FlightId;
            var selfDefenseCommand = AirCombatRules.Decide(
                barcap,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());
            Assert.That(
                selfDefenseCommand.Intent,
                Is.EqualTo(AirCombatIntent.EngageTarget));
            Assert.That(
                selfDefenseCommand.TargetFlightId,
                Is.EqualTo(scenario.Target.Flight.FlightId));
        }

        [Test]
        public void BarcapAssignsUrgentFighterApproachingPatrolArea()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 70f,
                includeHostileTrack: true);
            var barcap = CreateAirCombatFlight(
                Alliance.Bluefor,
                AirMissionRequestType.BarrierCombatAirPatrol,
                scenario.Source.AircraftType,
                scenario.Source.Flight.PositionFeet,
                headingDegrees: 0f,
                TestModule.Aim120OrdnanceTypeId,
                scenario.Frame.Time,
                barcapStation: true);
            scenario.Frame.Flights = new Dictionary<Guid, AirCombatFlightView>
            {
                { barcap.Flight.FlightId, barcap },
                { scenario.Target.Flight.FlightId, scenario.Target }
            };

            var assignments = AirCombatRules.BuildBarcapAssignments(
                scenario.Frame,
                scenario.OrdnanceTypes,
                _ => AllianceAirDoctrine.CreateDefault());

            Assert.That(
                assignments.TryGetValue(
                    barcap.Flight.FlightId,
                    out var assignedTargetId),
                Is.True);
            Assert.That(
                assignedTargetId,
                Is.EqualTo(scenario.Target.Flight.FlightId));
        }

        [Test]
        public void BarcapCancelsPreparedShotWhenContactTurnsAwayFromPatrolArea()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 70f,
                includeHostileTrack: true);
            var barcap = CreateAirCombatFlight(
                Alliance.Bluefor,
                AirMissionRequestType.BarrierCombatAirPatrol,
                scenario.Source.AircraftType,
                scenario.Source.Flight.PositionFeet,
                headingDegrees: 0f,
                TestModule.Aim120OrdnanceTypeId,
                scenario.Frame.Time,
                barcapStation: true);
            scenario.Frame.Flights = new Dictionary<Guid, AirCombatFlightView>
            {
                { barcap.Flight.FlightId, barcap },
                { scenario.Target.Flight.FlightId, scenario.Target }
            };
            scenario.Frame.CurrentTracksByAlliance[Alliance.Bluefor][
                scenario.Target.Flight.FlightId].EstimatedHeadingDegrees = 0f;
            scenario.Frame.BarcapTargetByFlightId =
                AirCombatRules.BuildBarcapAssignments(
                    scenario.Frame,
                    scenario.OrdnanceTypes,
                    _ => AllianceAirDoctrine.CreateDefault());
            scenario.Frame.ActivePasses = new[]
            {
                new ActiveOrdnanceEmploymentPass
                {
                    SourceFlightId = barcap.Flight.FlightId,
                    TargetFlightId = scenario.Target.Flight.FlightId,
                    TargetKind = OrdnanceEmploymentTargetKind.AirFlight,
                    OrdnanceTypeDefinitionId = TestModule.Aim120OrdnanceTypeId,
                    PlannedQuantity = 1,
                    PreparationStartedAt = scenario.Frame.Time,
                    ReleaseAt = scenario.Frame.Time.AddSeconds(30)
                }
            };

            var command = AirCombatRules.Decide(
                barcap,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.RequestsAirToAirPassCancellation, Is.True);
            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.FollowMission));
        }

        [Test]
        public void MaximumThreatRadiusUsesLargestApplicableSamEnvelope()
        {
            var siteId = Guid.NewGuid();
            var altitudeFeet = 25000f;
            var shortEnvelope = new KnownSamThreatEnvelope(
                siteId,
                Vector3.zero,
                maximumSlantRangeFeet: 40f * AirspaceGeometry.FeetPerKilometer,
                minimumAltitudeFeet: 0f,
                maximumAltitudeFeet: 60000f);
            var longEnvelope = new KnownSamThreatEnvelope(
                siteId,
                Vector3.zero,
                maximumSlantRangeFeet: 80f * AirspaceGeometry.FeetPerKilometer,
                minimumAltitudeFeet: 0f,
                maximumAltitudeFeet: 60000f);

            var radius = AirPackageBuilder.GetMaximumHorizontalThreatRadiusFeet(
                new[] { shortEnvelope, longEnvelope },
                altitudeFeet,
                maneuverClearanceFeet: 0f);

            Assert.That(
                radius,
                Is.EqualTo(longEnvelope.HorizontalRadiusFeetAtAltitude(
                    altitudeFeet)).Within(0.01f));
        }

        [Test]
        public void FighterEscortCompletesAtSafeReleaseInsteadOfScreenExit()
        {
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var airportId = Guid.NewGuid();
            var area = new AirMissionArea(
                Vector3Int.zero,
                radiusKm: 40f,
                tileDistanceKm: 20f);
            var stationEntry = new AirWaypoint(
                Vector3.forward,
                AirWaypointAction.StationEntry,
                now.AddMinutes(10),
                area);
            var flight = new AirFlight
            {
                MissionType =
                    AirMissionRequestType.DestructionOfEnemyAirDefenses,
                Role = AirFlightRole.FighterEscort
            };
            flight.MaterializeRoute(new[]
            {
                new AirWaypoint(
                    Vector3.zero,
                    AirWaypointAction.Takeoff,
                    now,
                    airportBuildingId: airportId),
                stationEntry,
                new AirWaypoint(
                    Vector3.right,
                    AirWaypointAction.StationEndpoint,
                    now.AddMinutes(25),
                    hasRepeat: true,
                    repeatFromWaypointId: stationEntry.WaypointId,
                    repeatUntil: now.AddMinutes(25)),
                new AirWaypoint(
                    Vector3.back,
                    AirWaypointAction.MissionAction,
                    now.AddMinutes(27),
                    area),
                new AirWaypoint(
                    Vector3.zero,
                    AirWaypointAction.Land,
                    now.AddMinutes(40),
                    airportBuildingId: airportId)
            });

            Assert.That(flight.TryTakeOff(now), Is.True);
            flight.CrossCurrentWaypoint(now.AddMinutes(10));
            flight.CrossCurrentWaypoint(now.AddMinutes(25));
            Assert.That(flight.MissionAchieved, Is.False);

            flight.CrossCurrentWaypoint(now.AddMinutes(27));
            Assert.That(flight.MissionAchieved, Is.True);
        }

        [Test]
        public void FighterEscortNeverInheritsDeadTargetSamPermission()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 50f,
                includeHostileTrack: false);
            var targetSiteId = Guid.NewGuid();
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
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
                scenario.Source.Flight
                    .AuthorizedSurfaceThreatPenetrationGranted,
                Is.False);
            Assert.That(
                command.RequestsSurfaceThreatRecovery
                || command.Maneuver == AirCombatManeuver.AvoidSurfaceThreat,
                Is.True);
        }

        [Test]
        public void CloseEscortFollowsExecutingProtectedElementWhenNoThreatExists()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 50f,
                includeHostileTrack: false);
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
            var protectedFlight = AttachProtectedDeadFlight(scenario);
            Assert.That(
                protectedFlight.Flight.CrossCurrentWaypoint(
                    scenario.Frame.Time),
                Is.EqualTo(FlightWaypointTransition.Advanced));
            Assert.That(
                scenario.Source.Flight.UpdateEscortCoverageMode(
                    AirEscortCoverageMode.CloseCover,
                    scenario.Frame.Time,
                    "Test close-cover transition."),
                Is.True);

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.Intent, Is.EqualTo(AirCombatIntent.FollowMission));
            Assert.That(
                command.Maneuver,
                Is.EqualTo(AirCombatManeuver.Intercept)
                    .Or.EqualTo(AirCombatManeuver.Press));
            Assert.That(command.HasAimPoint, Is.True);
            Assert.That(
                command.AimPointFeet.y,
                Is.GreaterThan(protectedFlight.Flight.PositionFeet.y));
            StringAssert.Contains("close cover", command.Reason);
        }

        [Test]
        public void CloseEscortMayEnterConfirmedClearedSamEnvelope()
        {
            var scenario = CreateDeadAirCombatScenario(
                hostileDistanceKm: 50f,
                includeHostileTrack: false);
            scenario.Source.Flight.Role = AirFlightRole.FighterEscort;
            var protectedFlight = AttachProtectedDeadFlight(scenario);
            protectedFlight.Flight.CrossCurrentWaypoint(scenario.Frame.Time);
            scenario.Source.Flight.UpdateEscortCoverageMode(
                AirEscortCoverageMode.CloseCover,
                scenario.Frame.Time,
                "Test close-cover transition.");
            var clearedSiteId = Guid.NewGuid();
            scenario.Source.Flight.ConfirmSurfaceThreatCleared(
                clearedSiteId,
                scenario.Frame.Time,
                "Test target shooter-chain destruction.");
            SetBlueKnownThreat(
                scenario,
                CreateKnownThreat(
                    clearedSiteId,
                    scenario.Source.Flight.PositionFeet));

            var command = AirCombatRules.Decide(
                scenario.Source,
                scenario.Frame,
                scenario.OrdnanceTypes,
                AllianceAirDoctrine.CreateDefault());

            Assert.That(command.RequestsSurfaceThreatRecovery, Is.False);
            Assert.That(
                command.Maneuver,
                Is.EqualTo(AirCombatManeuver.Intercept)
                    .Or.EqualTo(AirCombatManeuver.Press));
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

            var commander = new AllianceAirTaskingCommander(
                Alliance.Bluefor,
                template.AirDoctrineByAlliance[Alliance.Bluefor]);
            commander.BeginPlanningCycle(template.CampaignStartTime);
            var requestGenerator = new AirMissionRequestGenerator(
                new AirMissionPriorityService(module),
                module,
                alliance => template.OrdnanceAllowances[alliance]);

            var bootstrapRequests = requestGenerator.Generate(
                commander,
                snapshot,
                template.SimulationSettings.OperationalCadenceHours,
                allowOffensiveMissions: false);
            var establishedPictureRequests = requestGenerator.Generate(
                commander,
                snapshot,
                template.SimulationSettings.OperationalCadenceHours,
                allowOffensiveMissions: true);

            Assert.That(bootstrapRequests, Has.None.Matches<AirMissionRequest>(
                request => request.RequestType
                           == AirMissionRequestType.OffensiveCounterAirSweep
                           || request.RequestType
                           == AirMissionRequestType.DestructionOfEnemyAirDefenses));
            Assert.That(establishedPictureRequests, Has.Some.Matches<AirMissionRequest>(
                request => request.RequestType
                           == AirMissionRequestType.DestructionOfEnemyAirDefenses));
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
                var targetTrack = new IADSTrack(
                    target.Flight.FlightId,
                    targetPosition,
                    target.LiveAircraft.Count,
                    target.LiveAircraft.Count
                    * targetType.AirInterferenceCapability,
                    target.Flight.HeadingDegrees,
                    target.Flight.SpeedKnots,
                    quality: 1f,
                    now);
                targetTrack.IdentifyAircraftType(
                    targetType.AircraftTypeDefinitionId);
                tracks[target.Flight.FlightId] = targetTrack;
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
                    AircraftTypes = module.AircraftTypeDefinitions.ToDictionary(
                        definition => definition.AircraftTypeDefinitionId),
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

        private static AirCombatFlightView AttachProtectedDeadFlight(
            DeadAirCombatScenario scenario)
        {
            var protectedFlight = CreateAirCombatFlight(
                Alliance.Bluefor,
                AirMissionRequestType.DestructionOfEnemyAirDefenses,
                scenario.Source.AircraftType,
                scenario.Source.Flight.PositionFeet,
                scenario.Source.Flight.HeadingDegrees,
                TestModule.Aim120OrdnanceTypeId,
                scenario.Frame.Time);
            protectedFlight.Package = scenario.Source.Package;
            scenario.Source.Package.Flights.Add(protectedFlight.Flight);
            scenario.Source.Flight.ProtectedFlightIds.Add(
                protectedFlight.Flight.FlightId);
            scenario.Frame.Flights = scenario.Frame.Flights
                .Append(new KeyValuePair<Guid, AirCombatFlightView>(
                    protectedFlight.Flight.FlightId,
                    protectedFlight))
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            return protectedFlight;
        }

        private static void AddTrackedHostileFlight(
            DeadAirCombatScenario scenario,
            AirCombatFlightView hostile)
        {
            scenario.Frame.Flights = scenario.Frame.Flights
                .Append(new KeyValuePair<Guid, AirCombatFlightView>(
                    hostile.Flight.FlightId,
                    hostile))
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            var blueTracks = scenario.Frame.CurrentTracksByAlliance[
                    Alliance.Bluefor]
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            var hostileTrack = new IADSTrack(
                hostile.Flight.FlightId,
                hostile.Flight.PositionFeet,
                hostile.LiveAircraft.Count,
                hostile.LiveAircraft.Count
                * hostile.AircraftType.AirInterferenceCapability,
                hostile.Flight.HeadingDegrees,
                hostile.Flight.SpeedKnots,
                quality: 1f,
                scenario.Frame.Time);
            hostileTrack.IdentifyAircraftType(
                hostile.AircraftType.AircraftTypeDefinitionId);
            blueTracks[hostile.Flight.FlightId] = hostileTrack;
            scenario.Frame.CurrentTracksByAlliance =
                new Dictionary<Alliance, IReadOnlyDictionary<Guid, IADSTrack>>
                {
                    { Alliance.Bluefor, blueTracks },
                    {
                        Alliance.Redfor,
                        scenario.Frame.CurrentTracksByAlliance[Alliance.Redfor]
                    }
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
            DateTime now,
            bool barcapStation = false)
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
            var missionArea = new AirMissionArea(
                Vector3Int.zero,
                radiusKm: 40f,
                tileDistanceKm: 20f);
            if (barcapStation)
            {
                var stationEntry = new AirWaypoint(
                    position,
                    AirWaypointAction.StationEntry,
                    now,
                    missionArea);
                flight.MaterializeRoute(new[]
                {
                    new AirWaypoint(
                        position,
                        AirWaypointAction.Takeoff,
                        now,
                        airportBuildingId: squadron.AirportBuildingId),
                    stationEntry,
                    new AirWaypoint(
                        position + Vector3.right
                        * 10f * AirspaceGeometry.FeetPerKilometer,
                        AirWaypointAction.StationEndpoint,
                        now.AddMinutes(2),
                        hasRepeat: true,
                        repeatFromWaypointId: stationEntry.WaypointId,
                        repeatUntil: now.AddHours(1)),
                    new AirWaypoint(
                        position,
                        AirWaypointAction.MissionAction,
                        now.AddHours(1),
                        missionArea),
                    new AirWaypoint(
                        position,
                        AirWaypointAction.Land,
                        now.AddHours(2),
                        airportBuildingId: squadron.AirportBuildingId)
                });
            }
            else
            {
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
                        missionArea),
                    new AirWaypoint(
                        position,
                        AirWaypointAction.Land,
                        now.AddHours(2),
                        airportBuildingId: squadron.AirportBuildingId)
                });
            }
            Assert.That(flight.TryTakeOff(now), Is.True);
            flight.UpdateKinematics(
                position,
                headingDegrees,
                aircraftType.CruiseSpeedKnots);
            if (barcapStation)
                flight.CrossCurrentWaypoint(now);
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
