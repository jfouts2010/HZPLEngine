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
    public sealed class GroundAttackPlanningTests
    {
        [Test]
        public void InfantryProfileCannotProduceArmoredTargets()
        {
            var module = TestModule.GetTestModule();
            var infantry = module.BattalionDefinitions.Single(definition =>
                definition.BattalionDefinitionId
                == TestModule.BlueInfantryBattalionId);
            var armor = module.BattalionDefinitions.Single(definition =>
                definition.BattalionDefinitionId
                == TestModule.BlueArmorBattalionId);

            Assert.That(infantry.GroundTargetProfile, Is.Not.Empty);
            Assert.That(
                infantry.GroundTargetProfile.Any(target =>
                    target.TargetCategory == OrdnanceTargetCategory.Vehicle
                    && target.TargetToughness >= 3),
                Is.False);
            Assert.That(
                armor.GroundTargetProfile.Any(target =>
                    target.TargetCategory == OrdnanceTargetCategory.Vehicle
                    && target.TargetToughness >= 3),
                Is.True);
        }

        [Test]
        public void DivisionOpportunityOnlyUsesBattalionsInItsTemplate()
        {
            var module = TestModule.GetTestModule();
            var battalions = module.BattalionDefinitions.ToDictionary(
                definition => definition.BattalionDefinitionId);
            var componentDefinitions = module.SamComponentDefinitions
                .ToDictionary(definition =>
                    definition.SamComponentDefinitionId);
            var infantryTemplate = new DivisionTemplate(
                Guid.NewGuid(),
                TestModule.BlueCountryId,
                "Infantry only",
                new List<DivisionTemplateBattalion>
                {
                    new DivisionTemplateBattalion(
                        TestModule.BlueInfantryBattalionId,
                        4)
                });
            var division = new Division
            {
                DivisionId = Guid.NewGuid(),
                DivisionTemplateId = infantryTemplate.DivisionTemplateId,
                CountryId = TestModule.BlueCountryId,
                TileId = new Vector3Int(3, 0, 4),
                Strength = 100f
            };
            var service = new GroundAttackOpportunityService(
                componentDefinitions);
            var flightId = Guid.NewGuid();

            var targets = Enumerable.Range(1, 100)
                .SelectMany(sequence => service.RollDivisionOpportunity(
                    flightId,
                    sequence,
                    division,
                    infantryTemplate,
                    battalions,
                    new DateTime(2026, 7, 29, 12, 0, 0)).Targets)
                .ToList();

            Assert.That(targets, Is.Not.Empty);
            Assert.That(
                targets.Any(target => target.TargetCategory
                                      == OrdnanceTargetCategory.Vehicle
                                      && target.TargetToughness >= 3),
                Is.False);
            Assert.That(
                targets.All(target => target.Target.EntityId
                                      == division.DivisionId),
                Is.True);
        }

        [Test]
        public void DeadOpportunityIsDeterministicAndCanExposeSeveralComponents()
        {
            var module = TestModule.GetTestModule();
            var launcherDefinition = module.SamComponentDefinitions
                .OfType<LauncherAirDefenseComponentDefinition>()
                .First();
            var components = Enumerable.Range(0, 4)
                .Select(_ => (AirDefenseComponent)
                    new LauncherAirDefenseComponent(launcherDefinition))
                .ToList();
            var site = new SamSite
            {
                SiteId = Guid.Parse(
                    "10000000-0000-0000-0000-000000000001"),
                Alliance = Alliance.Redfor,
                Components = components
            };
            var definitions = module.SamComponentDefinitions.ToDictionary(
                definition => definition.SamComponentDefinitionId);
            var service = new GroundAttackOpportunityService(definitions);
            var flightId = Guid.Parse(
                "20000000-0000-0000-0000-000000000001");
            var authorized = components
                .Select(component => component.ComponentId)
                .ToList();
            var occurredAt = new DateTime(2026, 7, 29, 12, 0, 0);

            var first = service.RollDeadOpportunity(
                flightId,
                7,
                site,
                Vector3Int.zero,
                authorized,
                occurredAt);
            var repeated = service.RollDeadOpportunity(
                flightId,
                7,
                site,
                Vector3Int.zero,
                authorized,
                occurredAt);

            Assert.That(repeated.Quality, Is.EqualTo(first.Quality));
            Assert.That(repeated.MaximumReleases, Is.EqualTo(first.MaximumReleases));
            Assert.That(
                repeated.Targets.Select(target => target.Target.EntityId),
                Is.EqualTo(first.Targets.Select(target => target.Target.EntityId)));

            var opportunities = Enumerable.Range(1, 100)
                .Select(sequence => service.RollDeadOpportunity(
                    flightId,
                    sequence,
                    site,
                    Vector3Int.zero,
                    authorized,
                    occurredAt))
                .ToList();
            Assert.That(opportunities.Any(item => !item.HasTargets), Is.True);
            Assert.That(
                opportunities.Any(item => item.Targets.Count > 1),
                Is.True);

            var excludedComponentId = components[0].ComponentId;
            var deconflicted = Enumerable.Range(1, 25)
                .SelectMany(sequence => service.RollDeadOpportunity(
                    flightId,
                    sequence,
                    site,
                    Vector3Int.zero,
                    authorized,
                    occurredAt,
                    componentId => componentId == excludedComponentId).Targets)
                .ToList();
            Assert.That(
                deconflicted.Any(target => target.Target.EntityId
                                           == excludedComponentId),
                Is.False);
        }

        [Test]
        public void CoveredFireControlRadarStillBlocksLauncherOpportunities()
        {
            var module = TestModule.GetTestModule();
            var radarDefinition = module.SamComponentDefinitions
                .OfType<RadarAirDefenseComponentDefinition>()
                .First(definition => definition.ProvidesWeaponQualityTrack);
            var launcherDefinition = module.SamComponentDefinitions
                .OfType<LauncherAirDefenseComponentDefinition>()
                .First();
            var radar = new RadarAirDefenseComponent(radarDefinition);
            var launcher = new LauncherAirDefenseComponent(
                launcherDefinition);
            var site = new SamSite
            {
                SiteId = Guid.Parse(
                    "10000000-0000-0000-0000-000000000002"),
                Alliance = Alliance.Redfor,
                Components = new List<AirDefenseComponent>
                {
                    radar,
                    launcher
                }
            };
            var service = new GroundAttackOpportunityService(
                module.SamComponentDefinitions.ToDictionary(definition =>
                    definition.SamComponentDefinitionId));
            var authorized = site.Components
                .Select(component => component.ComponentId)
                .ToList();
            var flightId = Guid.Parse(
                "20000000-0000-0000-0000-000000000002");
            var occurredAt = new DateTime(2026, 7, 30, 12, 0, 0);

            var whileRadarEffectIsPending = Enumerable.Range(1, 25)
                .Select(sequence => service.RollDeadOpportunity(
                    flightId,
                    sequence,
                    site,
                    Vector3Int.zero,
                    authorized,
                    occurredAt,
                    componentId => componentId == radar.ComponentId))
                .ToList();

            Assert.That(
                whileRadarEffectIsPending.All(opportunity =>
                    !opportunity.HasTargets),
                Is.True);

            radar.IsDamaged = true;
            var afterRadarDamage = Enumerable.Range(1, 100)
                .Select(sequence => service.RollDeadOpportunity(
                    flightId,
                    sequence,
                    site,
                    Vector3Int.zero,
                    authorized,
                    occurredAt))
                .FirstOrDefault(opportunity => opportunity.HasTargets);
            Assert.That(afterRadarDamage, Is.Not.Null);
            Assert.That(
                afterRadarDamage.Targets.All(target =>
                    target.Target.EntityId == launcher.ComponentId),
                Is.True);
        }

        [Test]
        public void DecisionUsesOpportunityAndInventoryToChooseQuantity()
        {
            var weaponId = Guid.NewGuid();
            var weapon = new OrdnanceTypeDefinition(
                weaponId,
                "Test precision weapon",
                weight: 1f,
                effectPower: 3,
                effectivenessByTargetCategory:
                new Dictionary<OrdnanceTargetCategory, float>
                {
                    { OrdnanceTargetCategory.Vehicle, 0.8f }
                },
                employmentCategory:
                OrdnanceEmploymentCategory.AirToGroundPrecision,
                hitProbability: 0.75f);
            var divisionId = Guid.NewGuid();
            var opportunity = new GroundAttackOpportunity
            {
                Quality = GroundAttackOpportunityQuality.Excellent,
                MaximumReleases = 2,
                TargetTileId = Vector3Int.zero,
                Targets = Enumerable.Range(0, 3)
                    .Select(index => new GroundAttackOpportunityTarget
                    {
                        Target = new GroundAttackTargetReference
                        {
                            Kind = GroundAttackTargetKind.Division,
                            EntityId = divisionId,
                            TileId = Vector3Int.zero
                        },
                        TargetCategory = OrdnanceTargetCategory.Vehicle,
                        TargetToughness = 3,
                        MissionPriority = 10f,
                        Description = $"tank {index + 1}"
                    })
                    .ToList()
            };
            var aircraft = new CampaignAircraft
            {
                Status = CampaignAircraftStatus.Assigned,
                Loadout = new List<AircraftLoadoutItem>
                {
                    new AircraftLoadoutItem(weaponId, 3)
                }
            };

            var planned = new GroundAttackDecisionService().TryPlan(
                opportunity,
                new[] { aircraft },
                new Dictionary<Guid, OrdnanceTypeDefinition>
                {
                    { weaponId, weapon }
                },
                null,
                out var plan);

            Assert.That(planned, Is.True);
            Assert.That(plan.OrdnanceTypeDefinitionId, Is.EqualTo(weaponId));
            Assert.That(plan.PrimaryTargets, Has.Count.EqualTo(2));
            Assert.That(plan.OpportunityTargets, Has.Count.EqualTo(3));
        }

        [Test]
        public void SecondaryEffectsRespectCoverageAndToughness()
        {
            var pointWeapon = new OrdnanceTypeDefinition(
                Guid.NewGuid(),
                "Point weapon",
                1f,
                2,
                maximumGroundTargetsPerWeapon: 0,
                secondaryGroundEffectMultiplier: -1f);
            var areaWeapon = new OrdnanceTypeDefinition(
                Guid.NewGuid(),
                "Area weapon",
                1f,
                4,
                new Dictionary<OrdnanceTargetCategory, float>
                {
                    { OrdnanceTargetCategory.Vehicle, 1f }
                },
                OrdnanceEmploymentCategory.AirToGroundUnguided,
                maximumGroundTargetsPerWeapon: 4,
                secondaryGroundEffectMultiplier: 0.5f);

            Assert.That(pointWeapon.MaximumGroundTargetsPerWeapon, Is.EqualTo(1));
            Assert.That(pointWeapon.SecondaryGroundEffectMultiplier, Is.Zero);
            Assert.That(
                AirToGroundWeaponRules.CanAffect(
                    areaWeapon,
                    OrdnanceTargetCategory.Vehicle,
                    2,
                    areaWeapon.SecondaryGroundEffectMultiplier),
                Is.True);
            Assert.That(
                AirToGroundWeaponRules.CanAffect(
                    areaWeapon,
                    OrdnanceTargetCategory.Vehicle,
                    3,
                    areaWeapon.SecondaryGroundEffectMultiplier),
                Is.False);

            var divisionId = Guid.NewGuid();
            var opportunity = new GroundAttackOpportunity
            {
                MaximumReleases = 3,
                Targets = Enumerable.Range(0, 3)
                    .Select(index => new GroundAttackOpportunityTarget
                    {
                        Target = new GroundAttackTargetReference
                        {
                            Kind = GroundAttackTargetKind.Division,
                            EntityId = divisionId
                        },
                        TargetCategory = OrdnanceTargetCategory.Vehicle,
                        TargetToughness = 2,
                        MissionPriority = 10f,
                        Description = $"truck {index + 1}"
                    })
                    .ToList()
            };
            var aircraft = new CampaignAircraft
            {
                Status = CampaignAircraftStatus.Assigned,
                Loadout = new List<AircraftLoadoutItem>
                {
                    new AircraftLoadoutItem(
                        areaWeapon.OrdnanceTypeDefinitionId,
                        3)
                }
            };
            var planned = new GroundAttackDecisionService().TryPlan(
                opportunity,
                new[] { aircraft },
                new Dictionary<Guid, OrdnanceTypeDefinition>
                {
                    { areaWeapon.OrdnanceTypeDefinitionId, areaWeapon }
                },
                null,
                out var plan);

            Assert.That(planned, Is.True);
            Assert.That(plan.PrimaryTargets, Has.Count.EqualTo(1));
        }

        [Test]
        public void AreaWeaponDoesNotTreatHardTargetAsPossibleSplash()
        {
            var weapon = new OrdnanceTypeDefinition(
                Guid.NewGuid(),
                "Area weapon",
                1f,
                4,
                new Dictionary<OrdnanceTargetCategory, float>
                {
                    { OrdnanceTargetCategory.Vehicle, 1f }
                },
                OrdnanceEmploymentCategory.AirToGroundUnguided,
                maximumGroundTargetsPerWeapon: 2,
                secondaryGroundEffectMultiplier: 0.5f);
            var divisionId = Guid.NewGuid();
            var hardTarget = new GroundAttackOpportunityTarget
            {
                Target = new GroundAttackTargetReference
                {
                    Kind = GroundAttackTargetKind.Division,
                    EntityId = divisionId
                },
                TargetCategory = OrdnanceTargetCategory.Vehicle,
                TargetToughness = 3,
                MissionPriority = 10f,
                Description = "low-priority tank"
            };
            var softTarget = new GroundAttackOpportunityTarget
            {
                Target = new GroundAttackTargetReference
                {
                    Kind = GroundAttackTargetKind.Division,
                    EntityId = divisionId
                },
                TargetCategory = OrdnanceTargetCategory.Vehicle,
                TargetToughness = 1,
                MissionPriority = 100f,
                Description = "high-priority truck"
            };
            var opportunity = new GroundAttackOpportunity
            {
                MaximumReleases = 1,
                Targets = new List<GroundAttackOpportunityTarget>
                {
                    hardTarget,
                    softTarget
                }
            };
            var aircraft = new CampaignAircraft
            {
                Status = CampaignAircraftStatus.Assigned,
                Loadout = new List<AircraftLoadoutItem>
                {
                    new AircraftLoadoutItem(
                        weapon.OrdnanceTypeDefinitionId,
                        1)
                }
            };

            var planned = new GroundAttackDecisionService().TryPlan(
                opportunity,
                new[] { aircraft },
                new Dictionary<Guid, OrdnanceTypeDefinition>
                {
                    { weapon.OrdnanceTypeDefinitionId, weapon }
                },
                null,
                out var plan);

            Assert.That(planned, Is.True);
            Assert.That(
                plan.PrimaryTargets.Single().Description,
                Is.EqualTo(softTarget.Description));
        }

        [Test]
        public void DeadEffectCreditIncludesSecondaryComponentHits()
        {
            var siteId = Guid.NewGuid();
            var flightId = Guid.NewGuid();
            var primaryId = Guid.NewGuid();
            var secondaryId = Guid.NewGuid();
            var record = new OrdnanceEmploymentRecord
            {
                Stage = OrdnanceEmploymentRecordStage.EffectResolved,
                TargetKind = OrdnanceEmploymentTargetKind.AirDefenseComponent,
                TargetSiteId = siteId,
                TargetComponentId = primaryId,
                SourceFlightId = flightId,
                Shots = new List<OrdnanceShotDiagnostic>
                {
                    new OrdnanceShotDiagnostic
                    {
                        Result = OrdnanceShotResult.Hit,
                        GroundTarget = new GroundAttackTargetReference
                        {
                            Kind = GroundAttackTargetKind.AirDefenseComponent,
                            EntityId = primaryId,
                            ParentEntityId = siteId
                        }
                    },
                    new OrdnanceShotDiagnostic
                    {
                        Result = OrdnanceShotResult.Hit,
                        GroundTarget = new GroundAttackTargetReference
                        {
                            Kind = GroundAttackTargetKind.AirDefenseComponent,
                            EntityId = secondaryId,
                            ParentEntityId = siteId
                        }
                    }
                }
            };

            var hitIds = AirExecutionSystem.GetHitAirDefenseComponentIds(
                new[] { record },
                siteId,
                new[] { flightId });

            Assert.That(hitIds, Is.EquivalentTo(new[]
            {
                primaryId,
                secondaryId
            }));
        }
    }
}
