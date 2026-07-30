using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;
using NUnit.Framework;

namespace Tests.Editor
{
    public sealed class RadarEmissionAndAntiRadiationTests
    {
        [Test]
        public void SearchRadarStartsEmittingAndFireControlRadarStartsSilent()
        {
            var module = TestModule.GetTestModule();
            var searchDefinition = module.SamComponentDefinitions
                .OfType<RadarAirDefenseComponentDefinition>()
                .Single(definition => definition.SamComponentDefinitionId
                                      == TestModule.SpoonRestComponentId);
            var fireControlDefinition = module.SamComponentDefinitions
                .OfType<RadarAirDefenseComponentDefinition>()
                .Single(definition => definition.SamComponentDefinitionId
                                      == TestModule.FanSongComponentId);

            var searchRadar = new RadarAirDefenseComponent(searchDefinition);
            var fireControlRadar =
                new RadarAirDefenseComponent(fireControlDefinition);

            Assert.That(searchRadar.IsEmitting, Is.True);
            Assert.That(fireControlRadar.IsEmitting, Is.False);
        }

        [Test]
        public void EmissionHoldPreventsRestartUntilItExpires()
        {
            var definition = new RadarAirDefenseComponentDefinition(
                Guid.NewGuid(),
                "Test radar",
                OrdnanceTargetCategory.Radar,
                1,
                100f,
                20000f,
                0.5f);
            var radar = new RadarAirDefenseComponent(definition);
            var now = new DateTime(2026, 1, 1, 12, 0, 0);

            radar.HoldEmissionUntil(now.AddSeconds(30));
            radar.UpdateEmission(true, now.AddSeconds(29));
            Assert.That(radar.IsEmitting, Is.False);

            radar.UpdateEmission(true, now.AddSeconds(30));
            Assert.That(radar.IsEmitting, Is.True);
            Assert.That(radar.LastEmittedAt, Is.EqualTo(now.AddSeconds(30)));
        }

        [TestCase(0, 1f)]
        [TestCase(10, 0.8125f)]
        [TestCase(20, 0.625f)]
        [TestCase(40, 0.25f)]
        [TestCase(80, 0.25f)]
        public void HarmGuidanceDecaysTowardItsSilentQualityFloor(
            int silentSeconds,
            float expectedQuality)
        {
            var harm = TestModule.GetTestModule().OrdnanceTypeDefinitions
                .Single(definition => definition.OrdnanceTypeDefinitionId
                                      == TestModule.Agm88OrdnanceTypeId);
            var releasedAt = new DateTime(2026, 1, 1, 12, 0, 0);
            var effect = new PendingOrdnanceEffect
            {
                ReleasedAt = releasedAt,
                LastTargetEmissionAt = releasedAt
            };

            var quality =
                OrdnanceEmploymentSystem.CalculateAntiRadiationGuidanceQuality(
                    effect,
                    harm,
                    releasedAt.AddSeconds(silentSeconds));

            Assert.That(quality, Is.EqualTo(expectedQuality).Within(0.0001f));
        }

        [Test]
        public void AntiRadiationTypesCanAuthorDifferentEmitterMemory()
        {
            var harm = TestModule.GetTestModule().OrdnanceTypeDefinitions
                .Single(definition => definition.OrdnanceTypeDefinitionId
                                      == TestModule.Agm88OrdnanceTypeId);
            var shortMemoryMissile = new OrdnanceTypeDefinition(
                Guid.NewGuid(),
                "Short-memory ARM",
                1f,
                1,
                new Dictionary<OrdnanceTargetCategory, float>
                {
                    { OrdnanceTargetCategory.Radar, 1f }
                },
                OrdnanceEmploymentCategory.AntiRadiation,
                guidanceMode: OrdnanceGuidanceMode.AntiRadiation,
                antiRadiationEmitterMemorySeconds: 10f,
                antiRadiationSilentQualityFloor: 0f);
            var releasedAt = new DateTime(2026, 1, 1, 12, 0, 0);
            var effect = new PendingOrdnanceEffect
            {
                ReleasedAt = releasedAt,
                LastTargetEmissionAt = releasedAt
            };

            var harmQuality =
                OrdnanceEmploymentSystem.CalculateAntiRadiationGuidanceQuality(
                    effect,
                    harm,
                    releasedAt.AddSeconds(20));
            var shortMemoryQuality =
                OrdnanceEmploymentSystem.CalculateAntiRadiationGuidanceQuality(
                    effect,
                    shortMemoryMissile,
                    releasedAt.AddSeconds(20));

            Assert.That(harmQuality, Is.EqualTo(0.625f).Within(0.0001f));
            Assert.That(shortMemoryQuality, Is.Zero);
        }

        [Test]
        public void HarmReactsWithinOneTacticalStepBeforeMaximumRangeSa2Impact()
        {
            var module = TestModule.GetTestModule();
            var harm = module.OrdnanceTypeDefinitions.Single(definition =>
                definition.OrdnanceTypeDefinitionId
                == TestModule.Agm88OrdnanceTypeId);
            var sa2 = module.OrdnanceTypeDefinitions.Single(definition =>
                definition.OrdnanceTypeDefinitionId
                == TestModule.Sa2InterceptorOrdnanceTypeId);
            var maximumRangeImpactSeconds =
                AirspaceGeometry.HorizontalTravelSeconds(
                    sa2.MaximumRangeKm
                    * AirspaceGeometry.FeetPerKilometer,
                    sa2.EffectSpeedKnots);

            Assert.That(harm.PreparationSeconds, Is.EqualTo(5f));
            Assert.That(
                harm.PreparationSeconds,
                Is.LessThan(maximumRangeImpactSeconds));
        }

        [Test]
        public void RadarDefenseCreditsBothBeamAndDragGeometry()
        {
            Assert.That(
                OrdnanceEmploymentSystem
                    .CalculateRadarDefensiveManeuverStrength(
                        AirCombatManeuver.BeamLeft,
                        headingDegrees: 90f,
                        threatBearingDegrees: 0f),
                Is.EqualTo(1f));
            Assert.That(
                OrdnanceEmploymentSystem
                    .CalculateRadarDefensiveManeuverStrength(
                        AirCombatManeuver.Drag,
                        headingDegrees: 180f,
                        threatBearingDegrees: 0f),
                Is.EqualTo(0.75f));
            Assert.That(
                OrdnanceEmploymentSystem
                    .CalculateRadarDefensiveManeuverStrength(
                        AirCombatManeuver.Extend,
                        headingDegrees: 180f,
                        threatBearingDegrees: 0f),
                Is.Zero);
        }

        [Test]
        public void DefensiveManeuversUseAuthoredTurnRateWithoutChangingRouteTurns()
        {
            var f16 = TestModule.GetTestModule().AircraftTypeDefinitions
                .Single(definition => definition.AircraftTypeDefinitionId
                                      == TestModule.F16AircraftTypeId);

            Assert.That(f16.DefensiveTurnRateDegreesPerSecond, Is.EqualTo(9f));
            Assert.That(
                AirExecutionSystem.GetManeuverTurnRateDegreesPerSecond(
                    f16,
                    AirCombatManeuver.FollowRoute),
                Is.EqualTo(f16.TurnRateDegreesPerSecond));
            Assert.That(
                AirExecutionSystem.GetManeuverTurnRateDegreesPerSecond(
                    f16,
                    AirCombatManeuver.Drag),
                Is.EqualTo(f16.DefensiveTurnRateDegreesPerSecond));
            Assert.That(
                AirExecutionSystem.GetManeuverTurnRateDegreesPerSecond(
                    f16,
                    AirCombatManeuver.BeamRight),
                Is.EqualTo(f16.DefensiveTurnRateDegreesPerSecond));
        }

        [Test]
        public void UnauthoredDefensiveTurnRateFallsBackToNormalTurnRate()
        {
            var e3 = TestModule.GetTestModule().AircraftTypeDefinitions
                .Single(definition => definition.AircraftTypeDefinitionId
                                      == TestModule.E3AircraftTypeId);

            Assert.That(
                e3.DefensiveTurnRateDegreesPerSecond,
                Is.EqualTo(e3.TurnRateDegreesPerSecond));
        }
    }
}
