using System;
using System.Collections.Generic;
using System.Linq;
using Models.Module;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    public static class AdvancedTestCampaign
    {
        public const string Name = "Advanced Mechanics Test Campaign";

        // Radius-7 hex disc: 169 tiles, large enough to separate rear and forward air operations.
        private const int HexRadius = 7;
        private const int RearFighterAircraftPerSquadron = 6;
        private const int ForwardFighterAircraftPerSquadron = 3;
        private const int InitialBarcapPreparationHours = 2;
        private const int BarcapHandoffOverlapMinutes = 10;
        // Both rear-base routes reach their authored station in under ten minutes.
        private const int BarcapTransitFuelAllowanceMinutes = 10;
        // Preserve fuel for the higher burn incurred by defensive commits.
        private const int BarcapCombatFuelAllowanceMinutes = 15;
        // Thirty-minute package preparation, transit, and one five-minute tick.
        private const int BarcapPlanMaterializationLeadMinutes = 45;
        private const float BarcapAltitudeFeet = 40000f;
        private const float BarcapResponseRadiusKm = 80f;
        private const int BarcapAircraftPerFlight = 2;

        private static readonly DateTime CampaignStartTime =
            new DateTime(1990, 1, 1, 6, 0, 0);

        private static readonly Guid BlueCountryId = TestModule.BlueCountryId;
        private static readonly Guid RedCountryId = TestModule.RedCountryId;
        private static readonly Guid NeutralCountryId = TestModule.NeutralCountryId;
        private static readonly Guid BlueCapitalAirportBuildingId =
            Guid.Parse("d2b0a5f3-9e4c-4d28-b701-3f8ca2e5d419");
        private static readonly Guid RedCapitalAirportBuildingId =
            Guid.Parse("83579abc-3953-4381-b0b8-cee7b0280505");
        private static readonly Guid BlueDefensiveAirportBuildingId =
            Guid.Parse("ea312787-f865-4d56-9a4a-ef97c211d32e");
        private static readonly Guid BlueVulnerableAirportBuildingId =
            Guid.Parse("71154e5e-ee6b-4864-a711-71e71c87dc74");
        private static readonly Guid RedDefensiveAirportBuildingId =
            Guid.Parse("2a6049a3-4d97-41cf-9003-7bb9b9918e16");
        private static readonly Guid RedVulnerableAirportBuildingId =
            Guid.Parse("2b20d683-2cd4-41c7-bca9-6cf9d438ef05");
        private static readonly Guid[] BlueRearFighterSquadronIds =
        {
            Guid.Parse("a5e8370f-b340-4070-8269-4d0f8095aa02"),
            Guid.Parse("29478271-e9b9-43df-ae1a-01c83e194549"),
            Guid.Parse("6e990f9e-a868-43ca-be69-a5dfef2d8de8"),
            Guid.Parse("6db4699f-bdf6-45b2-87a2-64e4be92f9ed")
        };
        private static readonly Guid[] BlueForwardFighterSquadronIds =
        {
            Guid.Parse("66a5e719-c94c-4861-a55e-a5206f57cc07"),
            Guid.Parse("4eff0609-ebac-41c1-b90f-972f7e8072fb")
        };
        private static readonly Guid BlueAwacsSquadronId =
            Guid.Parse("c371bed7-5d5b-4f6e-9028-9ef91a5b4a0f");
        private static readonly Guid BlueTankerSquadronId =
            Guid.Parse("1c23f0c2-8dc6-4e35-b6aa-d82864cdfe7c");
        private static readonly Guid[] RedRearFighterSquadronIds =
        {
            Guid.Parse("f5a98443-9c5f-4e74-a371-5a089e950cba"),
            Guid.Parse("0f29ea7b-5075-4719-885f-54b879c1c010"),
            Guid.Parse("2d78dca9-5915-4db9-aa99-e25c31ea8914"),
            Guid.Parse("66fa6573-707d-4f39-804d-81248a7d9875")
        };
        private static readonly Guid[] RedForwardFighterSquadronIds =
        {
            Guid.Parse("275e3b2d-9b79-40de-8703-0e9db37de998"),
            Guid.Parse("a91bc34a-80e4-43d6-a44f-e3f02ae6125a")
        };
        private static readonly Guid RedAwacsSquadronId =
            Guid.Parse("7088f0da-cf99-46f8-b177-d7e266a4abfa");
        private static readonly Guid RedTankerSquadronId =
            Guid.Parse("ab56abe6-261f-483d-8a1a-fcda7d7e61a8");
        private static readonly Guid[] BlueFrontDivisionIds =
        {
            Guid.Parse("a4c2e8f1-3b7d-4e91-9f06-1d5a8c2b0473"),
            Guid.Parse("4aca313d-8ad0-4aa7-bd71-17b0adc3d85c"),
            Guid.Parse("83924aba-bdba-4e35-85ed-1c1e64609bd7"),
            Guid.Parse("c3d4e5f6-7890-4abc-def1-234567890abc"),
            Guid.Parse("f4c047bf-4b74-4aa4-bb4b-dcf1abe53254"),
            Guid.Parse("20981a2a-ba4c-4778-bfe7-fea0fe0b6881"),
            Guid.Parse("d4e5f6a7-8901-4bcd-ef12-345678901bcd"),
            Guid.Parse("6d142536-789a-4456-e89a-23456789abcd"),
            Guid.Parse("7e253647-89ab-4567-f9ab-3456789abcde"),
            Guid.Parse("8f364758-9abc-4678-8abc-456789abcdef"),
            Guid.Parse("90475869-abcd-4789-9bcd-56789abcdef0")
        };

        private static readonly Guid[] RedFrontDivisionIds =
        {
            Guid.Parse("b8d3f9a2-6c4e-4f12-8a17-2e6b9d3c1584"),
            Guid.Parse("e4a17cef-7f0e-4748-a88b-f71c42738737"),
            Guid.Parse("6cd06517-ced0-4a33-ac42-36f21124df1e"),
            Guid.Parse("e5f6a7b8-9012-4cde-f123-456789012cde"),
            Guid.Parse("dc41ff3f-ff9c-47ce-918a-4b41d54dfb78"),
            Guid.Parse("d0692c0c-b5f8-4643-b327-339d06559bd9"),
            Guid.Parse("f6a7b8c9-0123-4def-0123-567890123def"),
            Guid.Parse("29d0e1f2-3456-4012-a456-890123456cde"),
            Guid.Parse("3ae1f203-4567-4123-b567-901234567def"),
            Guid.Parse("4bf20314-5678-4234-c678-012345678efa"),
            Guid.Parse("5c031425-6789-4345-d789-123456789fab")
        };

        private static readonly Vector3Int BlueCapitalTileId = new Vector3Int(-3, 3, 0);
        private static readonly Vector3Int RedCapitalTileId = new Vector3Int(3, 0, -3);
        private static readonly Vector3Int BlueDefensiveAirbaseTileId = new Vector3Int(-6, 6, 0);
        private static readonly Vector3Int BlueVulnerableAirbaseTileId = new Vector3Int(-3, 4, -1);
        private static readonly Vector3Int RedDefensiveAirbaseTileId = new Vector3Int(6, 0, -6);
        private static readonly Vector3Int RedVulnerableAirbaseTileId = new Vector3Int(4, 0, -4);
        private static readonly Vector3Int RedDeadCorridorSamTileId =
            new Vector3Int(2, 0, -2);
        private static readonly Vector3Int RedNorthernSurveillanceRadarTileId =
            new Vector3Int(6, -3, -3);
        private static readonly Vector3Int RedSouthernSurveillanceRadarTileId =
            new Vector3Int(2, 5, -7);
        private static readonly Vector3Int NeutralHubTileId = new Vector3Int(0, 0, 0);
        private static readonly Vector3Int[] BlueFrontTileIds =
        {
            new Vector3Int(0, 1, -1),
            new Vector3Int(0, 2, -2),
            new Vector3Int(0, 3, -3),
            new Vector3Int(0, 4, -4),
            new Vector3Int(0, 5, -5),
            new Vector3Int(0, 6, -6),
            new Vector3Int(0, 7, -7)
        };
        private static readonly Vector3Int[] RedFrontTileIds =
        {
            new Vector3Int(1, 0, -1),
            new Vector3Int(1, 1, -2),
            new Vector3Int(1, 2, -3),
            new Vector3Int(1, 3, -4),
            new Vector3Int(1, 4, -5),
            new Vector3Int(1, 5, -6),
            new Vector3Int(1, 6, -7)
        };
        private static readonly Vector3Int BluePortTileId = new Vector3Int(-2, 2, 0);
        private static readonly Vector3Int RedRefineryTileId = new Vector3Int(2, 1, -3);

        public static CampaignTemplate Create()
        {
            var airDoctrineByAlliance = CreateAirDoctrineByAlliance();
            var template = new CampaignTemplate(Name)
            {
                ModuleId = TestModule.Id,
                CampaignStartTime = CampaignStartTime,
                SimulationSettings = new SimulationSettings
                {
                    SimulationTickMinutes = 5,
                    OperationalCadenceHours = 6
                },
                ContentHash = "advanced-mechanics-test-campaign-v19",
                CountryAllianceAssignments = CreateCountryAllianceAssignments(),
                OrdnanceAllowances = CreateOrdnanceAllowances(),
                SamSiteTemplateAllowances = CreateSamSiteTemplateAllowances(),
                AirDoctrineByAlliance = airDoctrineByAlliance,
                AirPackagePlans = CreateAirPackagePlans(
                    airDoctrineByAlliance),
                Tiles = CreateTiles(),
                StartingTileData = CreateStartingTileData(),
                SupplyCapitals = CreateSupplyCapitals(),
                BuildingStartingConditions = CreateBuildingStartingConditions(),
                DivisionStartingConditions = CreateDivisionStartingConditions(),
                MobileSamSiteStartingConditions = CreateMobileSamSiteStartingConditions(),
                SquadronStartingConditions = CreateSquadronStartingConditions()
            };

            template.RebuildDerivedData();
            return template;
        }

        private static List<CountryAllianceAssignment> CreateCountryAllianceAssignments()
        {
            return new List<CountryAllianceAssignment>
            {
                new CountryAllianceAssignment { CountryId = BlueCountryId, Alliance = Alliance.Bluefor },
                new CountryAllianceAssignment { CountryId = RedCountryId, Alliance = Alliance.Redfor },
                new CountryAllianceAssignment { CountryId = NeutralCountryId, Alliance = Alliance.Neutral }
            };
        }

        private static Dictionary<Alliance, List<Guid>> CreateOrdnanceAllowances()
        {
            return new Dictionary<Alliance, List<Guid>>
            {
                {
                    Alliance.Bluefor,
                    new List<Guid>
                    {
                        TestModule.Aim120OrdnanceTypeId,
                        TestModule.Aim9OrdnanceTypeId,
                        TestModule.M61GunOrdnanceTypeId,
                        TestModule.Agm88OrdnanceTypeId,
                        TestModule.Gbu38OrdnanceTypeId,
                        TestModule.Agm65OrdnanceTypeId
                    }
                },
                {
                    Alliance.Redfor,
                    new List<Guid>
                    {
                        TestModule.R27OrdnanceTypeId,
                        TestModule.R73OrdnanceTypeId,
                        TestModule.Gsh301GunOrdnanceTypeId
                    }
                }
            };
        }

        private static Dictionary<Alliance, List<Guid>> CreateSamSiteTemplateAllowances()
        {
            var permittedTemplates = new List<Guid>
            {
                TestModule.Sa2SiteTemplateId,
                TestModule.OsaSiteTemplateId,
                TestModule.SurveillanceRadarSiteTemplateId
            };
            return new Dictionary<Alliance, List<Guid>>
            {
                { Alliance.Bluefor, new List<Guid>(permittedTemplates) },
                { Alliance.Redfor, new List<Guid>(permittedTemplates) }
            };
        }

        private static Dictionary<Alliance, AllianceAirDoctrine> CreateAirDoctrineByAlliance()
        {
            return new Dictionary<Alliance, AllianceAirDoctrine>
            {
                { Alliance.Bluefor, CreateAirDoctrine() },
                { Alliance.Redfor, CreateAirDoctrine() }
            };
        }

        private static AllianceAirDoctrine CreateAirDoctrine()
        {
            return new AllianceAirDoctrine
            {
                RiskTolerance = AllianceAirDoctrine.DefaultRiskTolerance,
                DesiredAirCombatAdvantage =
                    AllianceAirDoctrine.DefaultDesiredAirCombatAdvantage
            };
        }

        private static List<AirPackagePlan> CreateAirPackagePlans(
            IReadOnlyDictionary<Alliance, AllianceAirDoctrine>
                airDoctrineByAlliance)
        {
            var plans = new List<AirPackagePlan>();
            var aircraftTypes = TestModule.GetTestModule()
                .AircraftTypeDefinitions
                .ToDictionary(type => type.AircraftTypeDefinitionId);
            AddBarcapRotations(
                plans,
                Alliance.Bluefor,
                aircraftTypes[TestModule.F16AircraftTypeId],
                airDoctrineByAlliance[Alliance.Bluefor],
                new[]
                {
                    BlueRearFighterSquadronIds[0],
                    BlueRearFighterSquadronIds[2]
                },
                "northern",
                new[]
                {
                    BlueFrontTileIds[0],
                    BlueFrontTileIds[1],
                    BlueFrontTileIds[2],
                    BlueFrontTileIds[3]
                },
                RedFrontTileIds[1],
                new Vector3Int(-1, 3, -2),
                new Vector3Int(-2, 4, -2),
                Guid.Parse("a1bb0000-0000-0000-0000-000000000001"),
                "a1bc0000",
                "a1bf0000",
                0,
                58.5f);
            AddBarcapRotations(
                plans,
                Alliance.Bluefor,
                aircraftTypes[TestModule.F16AircraftTypeId],
                airDoctrineByAlliance[Alliance.Bluefor],
                new[]
                {
                    BlueRearFighterSquadronIds[1],
                    BlueRearFighterSquadronIds[3]
                },
                "southern",
                new[]
                {
                    BlueFrontTileIds[3],
                    BlueFrontTileIds[4],
                    BlueFrontTileIds[5],
                    BlueFrontTileIds[6]
                },
                RedFrontTileIds[5],
                new Vector3Int(-1, 6, -5),
                new Vector3Int(-2, 7, -5),
                Guid.Parse("a1bb0000-0000-0000-0000-000000000002"),
                "a1bc0000",
                "a1bf0000",
                100,
                58.5f);
            AddBarcapRotations(
                plans,
                Alliance.Redfor,
                aircraftTypes[TestModule.Mig29AircraftTypeId],
                airDoctrineByAlliance[Alliance.Redfor],
                new[]
                {
                    RedRearFighterSquadronIds[0],
                    RedRearFighterSquadronIds[2]
                },
                "northern",
                new[]
                {
                    RedFrontTileIds[0],
                    RedFrontTileIds[1],
                    RedFrontTileIds[2],
                    RedFrontTileIds[3]
                },
                BlueFrontTileIds[1],
                new Vector3Int(2, 0, -2),
                new Vector3Int(3, -1, -2),
                Guid.Parse("b2bb0000-0000-0000-0000-000000000001"),
                "b2bc0000",
                "b2bf0000",
                0,
                42.9f);
            AddBarcapRotations(
                plans,
                Alliance.Redfor,
                aircraftTypes[TestModule.Mig29AircraftTypeId],
                airDoctrineByAlliance[Alliance.Redfor],
                new[]
                {
                    RedRearFighterSquadronIds[1],
                    RedRearFighterSquadronIds[3]
                },
                "southern",
                new[]
                {
                    RedFrontTileIds[3],
                    RedFrontTileIds[4],
                    RedFrontTileIds[5],
                    RedFrontTileIds[6]
                },
                BlueFrontTileIds[5],
                new Vector3Int(2, 4, -6),
                new Vector3Int(3, 3, -6),
                Guid.Parse("b2bb0000-0000-0000-0000-000000000002"),
                "b2bc0000",
                "b2bf0000",
                100,
                42.9f);
            ValidateScriptedAirPlans(plans);
            return plans;
        }

        private static void ValidateScriptedAirPlans(
            IReadOnlyList<AirPackagePlan> plans)
        {
            var horizonEnd = CampaignStartTime.AddHours(
                CampaignTemplate.ScriptedAirPlanHorizonHours);
            var planIds = new HashSet<Guid>();
            var flightPlanIds = new HashSet<Guid>();
            foreach (var plan in plans)
            {
                if (plan.AvailableAt < CampaignStartTime
                    || plan.EffectStart < plan.AvailableAt
                    || plan.EffectEnd <= plan.EffectStart
                    || plan.EffectEnd > horizonEnd)
                {
                    throw new InvalidOperationException(
                        $"Air plan {plan.PlanId} falls outside the scripted "
                        + $"{CampaignTemplate.ScriptedAirPlanHorizonHours}-hour "
                        + "horizon.");
                }

                if (!planIds.Add(plan.PlanId))
                {
                    throw new InvalidOperationException(
                        $"Duplicate scripted air plan ID {plan.PlanId}.");
                }

                foreach (var flight in plan.Flights)
                {
                    if (!flightPlanIds.Add(flight.FlightPlanId))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate scripted flight plan ID "
                            + $"{flight.FlightPlanId}.");
                    }
                }
            }

            ValidateBarcapCoverageContinuity(plans);
        }

        private static void ValidateBarcapCoverageContinuity(
            IReadOnlyList<AirPackagePlan> plans)
        {
            var requiredOverlap = TimeSpan.FromMinutes(
                BarcapHandoffOverlapMinutes);
            foreach (var barrierPlans in plans
                         .Where(plan => plan.OperationType
                                        == AirOperationType.Barcap
                                        && plan.BarcapBarrier != null)
                         .GroupBy(plan => new
                         {
                             plan.Alliance,
                             plan.BarcapBarrier.BarrierId
                         }))
            {
                var ordered = barrierPlans
                    .OrderBy(plan => plan.EffectStart)
                    .ToList();
                for (var index = 1; index < ordered.Count; index++)
                {
                    var previous = ordered[index - 1];
                    var current = ordered[index];
                    if (current.EffectStart
                        <= previous.EffectEnd - requiredOverlap)
                        continue;

                    throw new InvalidOperationException(
                        $"Scripted BARCAP barrier {barrierPlans.Key.BarrierId} "
                        + $"for {barrierPlans.Key.Alliance} lacks the required "
                        + $"{BarcapHandoffOverlapMinutes}-minute handoff overlap "
                        + $"between {previous.PlanId} and {current.PlanId}.");
                }
            }
        }

        private static void AddBarcapRotations(
            ICollection<AirPackagePlan> plans,
            Alliance alliance,
            AircraftTypeDefinition aircraftType,
            AllianceAirDoctrine doctrine,
            IReadOnlyList<Guid> squadronIds,
            string segmentName,
            IReadOnlyList<Vector3Int> barrierTiles,
            Vector3Int threatReferenceTile,
            Vector3Int stationStartTile,
            Vector3Int stationEndTile,
            Guid barrierId,
            string planIdPrefix,
            string flightIdPrefix,
            int sequenceOffset,
            float preferredLaunchRangeKm)
        {
            var firstEffectStart = CampaignStartTime.AddHours(
                InitialBarcapPreparationHours);
            var planHorizonEnd = CampaignStartTime.AddHours(
                CampaignTemplate.ScriptedAirPlanHorizonHours);
            var usableFuelDuration = TimeSpan.FromHours(
                aircraftType.EnduranceHours
                * Math.Max(0f, 1f - doctrine.JokerFuelFraction));
            var plannedStationDuration = usableFuelDuration
                                         - TimeSpan.FromMinutes(
                                             BarcapTransitFuelAllowanceMinutes
                                             + BarcapCombatFuelAllowanceMinutes);
            var handoffOverlap = TimeSpan.FromMinutes(
                BarcapHandoffOverlapMinutes);
            var rotationCadence = plannedStationDuration - handoffOverlap;
            if (rotationCadence <= TimeSpan.Zero)
            {
                throw new InvalidOperationException(
                    $"{aircraftType.Name} does not have enough usable fuel "
                    + $"to sustain the scripted {alliance} {segmentName} "
                    + "BARCAP with its required allowances and overlap.");
            }

            var rotation = 0;
            for (var effectStart = firstEffectStart;
                 effectStart < planHorizonEnd;
                 effectStart += rotationCadence)
            {
                var effectEnd = effectStart + plannedStationDuration;
                if (effectEnd > planHorizonEnd)
                    effectEnd = planHorizonEnd;
                var sequence = sequenceOffset + rotation + 1;
                var squadronId = squadronIds[rotation % squadronIds.Count];
                var availableAt = rotation == 0
                    ? CampaignStartTime
                    : effectStart - TimeSpan.FromMinutes(
                        BarcapPlanMaterializationLeadMinutes);
                plans.Add(CreateBarcapPlan(
                    CreateSequencedAirPlanId(planIdPrefix, sequence),
                    CreateSequencedAirPlanId(flightIdPrefix, sequence),
                    barrierId,
                    alliance,
                    squadronId,
                    segmentName,
                    barrierTiles,
                    threatReferenceTile,
                    stationStartTile,
                    stationEndTile,
                    preferredLaunchRangeKm,
                    availableAt,
                    effectStart,
                    effectEnd,
                    rotation + 1));
                rotation++;
            }
        }

        private static AirPackagePlan CreateBarcapPlan(
            Guid planId,
            Guid flightId,
            Guid barrierId,
            Alliance alliance,
            Guid squadronId,
            string segmentName,
            IReadOnlyList<Vector3Int> barrierTiles,
            Vector3Int threatReferenceTile,
            Vector3Int stationStartTile,
            Vector3Int stationEndTile,
            float preferredLaunchRangeKm,
            DateTime availableAt,
            DateTime effectStart,
            DateTime effectEnd,
            int rotation)
        {
            var assignedTiles = new List<Vector3Int>(barrierTiles);
            var stationStart = PositionAtAltitude(
                stationStartTile,
                BarcapAltitudeFeet);
            var stationEnd = PositionAtAltitude(
                stationEndTile,
                BarcapAltitudeFeet);
            var stationCenter = (stationStart + stationEnd) * 0.5f;
            var stationTrackHalfLengthKm = HorizontalDistanceKm(
                stationStart,
                stationEnd) * 0.5f;
            var barrier = new BarcapBarrierPlan
            {
                BarrierId = barrierId,
                BarrierTileIds = assignedTiles,
                ThreatReferenceTileId = threatReferenceTile,
                RepresentativeThreatSpeedKnots = 600f,
                EstimatedAircraftDemand = 2
            };
            return new AirPackagePlan
            {
                PlanId = planId,
                Alliance = alliance,
                OperationType = AirOperationType.Barcap,
                AvailableAt = availableAt,
                EffectStart = effectStart,
                EffectEnd = effectEnd,
                OperationArea = new AirMissionArea(
                    assignedTiles[assignedTiles.Count / 2],
                    BarcapResponseRadiusKm),
                BarcapBarrier = barrier,
                Rationale = $"24-hour scripted {alliance} {segmentName} "
                            + $"BARCAP rotation {rotation}.",
                Flights = new List<AirFlightPlan>
                {
                    new AirFlightPlan
                    {
                        FlightPlanId = flightId,
                        SquadronId = squadronId,
                        TaskType = AirFlightTaskType.Barcap,
                        AircraftCount = BarcapAircraftPerFlight,
                        IsRequired = true,
                        MissionWaypointsFeet = new List<Vector3>
                        {
                            stationStart,
                            stationEnd
                        },
                        BarcapCoverage = new BarcapStationCoverage
                        {
                            BarrierId = barrierId,
                            CoveredBarrierTileIds = assignedTiles,
                            ThreatReferenceTileId = threatReferenceTile,
                            StationCenterFeet = stationCenter,
                            StationHeadingDegrees = HeadingDegrees(
                                stationStart,
                                stationEnd),
                            StationTrackHalfLengthKm =
                                stationTrackHalfLengthKm,
                            PlannedResponseRadiusKm = BarcapResponseRadiusKm,
                            PlannedPreferredLaunchRangeKm =
                                preferredLaunchRangeKm,
                            RepresentativeThreatSpeedKnots = 600f,
                            PlannedAircraftCount = BarcapAircraftPerFlight,
                            PreferredAircraftCount = BarcapAircraftPerFlight
                        }
                    }
                }
            };
        }

        private static Guid CreateSequencedAirPlanId(
            string prefix,
            int sequence)
        {
            return Guid.Parse($"{prefix}-0000-0000-0000-{sequence:D12}");
        }

        private static float HorizontalDistanceKm(Vector3 first, Vector3 second)
        {
            return Vector2.Distance(
                       new Vector2(first.x, first.z),
                       new Vector2(second.x, second.z))
                   / CampaignMapCoordinates.FeetPerKilometer;
        }

        private static float HeadingDegrees(Vector3 first, Vector3 second)
        {
            return Mathf.Repeat(
                Mathf.Atan2(second.x - first.x, second.z - first.z)
                * Mathf.Rad2Deg,
                360f);
        }

        private static Vector3 PositionAtAltitude(
            Vector3Int tileId,
            float altitudeFeet)
        {
            var position = CampaignMapCoordinates.TileCenterFeet(tileId);
            position.y = altitudeFeet;
            return position;
        }

        private static List<SupplyCapitalStartingCondition> CreateSupplyCapitals()
        {
            return new List<SupplyCapitalStartingCondition>
            {
                new SupplyCapitalStartingCondition { Alliance = Alliance.Bluefor, TileId = BlueCapitalTileId },
                new SupplyCapitalStartingCondition { Alliance = Alliance.Redfor, TileId = RedCapitalTileId }
            };
        }

        private static List<Vector3Int> GenerateDiscTileIds()
        {
            var tileIds = new List<Vector3Int>();

            for (var x = -HexRadius; x <= HexRadius; x++)
            {
                var minY = Math.Max(-HexRadius, -x - HexRadius);
                var maxY = Math.Min(HexRadius, -x + HexRadius);

                for (var y = minY; y <= maxY; y++)
                {
                    var z = -x - y;
                    if (Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z)) <= HexRadius)
                        tileIds.Add(new Vector3Int(x, y, z));
                }
            }

            return tileIds;
        }

        // Western block (x <= -1) and a southern x=0 salient are blue.
        // Eastern block (x >= 1) is red. Northern arc (z >= 2) plus the center column are neutral.
        // Blue and red meet along the full southern x=0 / x=1 edge (seven contiguous contact tiles).
        private static Alliance AssignAlliance(Vector3Int tileId)
        {
            if (tileId.z >= 2)
                return Alliance.Neutral;

            if (tileId.x == 0 && tileId.z >= 0)
                return Alliance.Neutral;

            if (tileId.x >= 1)
                return Alliance.Redfor;

            if (tileId.x <= -1)
                return Alliance.Bluefor;

            return Alliance.Bluefor;
        }

        private static List<Tile> CreateTiles()
        {
            var tiles = new List<Tile>();

            foreach (var tileId in GenerateDiscTileIds())
            {
                var alliance = AssignAlliance(tileId);
                tiles.Add(CreateLandTile(tileId, alliance));
            }

            var riverTile = new Vector3Int(-1, 1, 0);
            var riverNeighbor = NeutralHubTileId;
            var riverTileDefinition = tiles.Find(tile => tile.Coordinates == riverTile);
            var riverNeighborDefinition = tiles.Find(tile => tile.Coordinates == riverNeighbor);
            riverTileDefinition?.RiverNeighborTileIds.Add(riverNeighbor);
            riverNeighborDefinition?.RiverNeighborTileIds.Add(riverTile);

            return tiles;
        }

        private static List<TileData> CreateStartingTileData()
        {
            var startingTileData = new List<TileData>();

            foreach (var tileId in GenerateDiscTileIds())
            {
                var alliance = AssignAlliance(tileId);
                startingTileData.Add(new LandTileData
                {
                    TileId = tileId,
                    Controller = alliance,
                    Infrastructure = CreateInfrastructure(tileId, alliance)
                });
            }

            return startingTileData;
        }

        private static List<BuildingStartingCondition> CreateBuildingStartingConditions()
        {
            return new List<BuildingStartingCondition>
            {
                CreateBuilding("c1a9f4e2-8d3b-4c17-a6f0-2e7b91d4c308", BlueCapitalTileId, BuildingType.Factory, 7),
                CreateBuilding(BlueCapitalAirportBuildingId.ToString(), BlueCapitalTileId, BuildingType.Airport, 5),
                CreateBuilding(BlueDefensiveAirportBuildingId.ToString(), BlueDefensiveAirbaseTileId, BuildingType.Airport, 7),
                CreateBuilding(BlueVulnerableAirportBuildingId.ToString(), BlueVulnerableAirbaseTileId, BuildingType.Airport, 3),
                CreateBuilding("6c69a6d3-1b42-4f3a-8b42-06b5f601e86f", BlueCapitalTileId, BuildingType.Railroad, 8),
                CreateBuilding("3bf75c64-7ba8-4ed6-9f9e-1c4d9c2266ce", BlueCapitalTileId, BuildingType.SupplyHub, 8),
                CreateBuilding("e3c1b6a4-0f5d-4e39-c812-4a9db3f6e52a", BluePortTileId, BuildingType.Port, 4),
                CreateBuilding("0b3f2aa6-61b7-4b2f-9d80-5aee3d844f91", BluePortTileId, BuildingType.Railroad, 6),
                CreateBuilding("eb695aca-92bd-4e3d-a8b5-d61cb1f6b28e", new Vector3Int(-1, 2, -1), BuildingType.Railroad, 5),
                CreateBuilding("e8fd115d-a93a-4649-95f3-364d5d986a9d", BlueFrontTileIds[0], BuildingType.Railroad, 4),
                CreateBuilding("6b442e5a-9b54-424f-90fc-f4a27a9b8ed9", BlueFrontTileIds[1], BuildingType.Railroad, 4),
                CreateBuilding("9a11758f-a4ac-4758-91df-a8c793ff55ae", BlueFrontTileIds[1], BuildingType.SupplyHub, 4),
                CreateBuilding("1be9955d-4d8b-4b7c-8f03-29b8f6d9aabd", BlueFrontTileIds[2], BuildingType.Railroad, 3),
                CreateBuilding("f4d2c7b5-1a6e-4f4a-d923-5b0ec407f63b", NeutralHubTileId, BuildingType.SupplyHub, 5),
                CreateBuilding("a5e3d8c6-2b7f-405b-e034-6c1fd518074c", BlueFrontTileIds[1], BuildingType.Fort, 3),
                CreateStaticSamBuilding("921dda97-8caf-4e7a-9803-af07ef13d2d8", BlueDefensiveAirbaseTileId),
                CreateStaticSamBuilding("c9c22074-fea8-497c-9691-c0a23ed1f5db", BlueCapitalTileId),
                CreateStaticSamBuilding("d352b464-f4b1-4ece-866f-eac4d2c31f88", BlueVulnerableAirbaseTileId),
                CreateStaticSamBuilding("4ce0420b-3559-41a2-96ee-933220a83a81", new Vector3Int(-1, 2, -1)),
                CreateStaticSamBuilding("4f146295-ea92-4de1-90cd-0da593c14fa7", new Vector3Int(-1, 5, -4)),
                CreateBuilding("b6f4e9d7-3c8a-416c-f145-7d2ae629185d", RedCapitalTileId, BuildingType.PowerPlant, 4),
                CreateBuilding(RedCapitalAirportBuildingId.ToString(), RedCapitalTileId, BuildingType.Airport, 5),
                CreateBuilding(RedDefensiveAirportBuildingId.ToString(), RedDefensiveAirbaseTileId, BuildingType.Airport, 7),
                CreateBuilding(RedVulnerableAirportBuildingId.ToString(), RedVulnerableAirbaseTileId, BuildingType.Airport, 3),
                CreateBuilding("8f6b7125-5ab8-4791-93be-a3ccdcf823ad", RedCapitalTileId, BuildingType.Railroad, 8),
                CreateBuilding("8ef77ba7-1ca4-4898-8f8e-afbcbe173d11", RedCapitalTileId, BuildingType.SupplyHub, 8),
                CreateBuilding("c7a5f0e8-4d9b-427d-0256-8e3bf73a296e", RedRefineryTileId, BuildingType.Refinery, 3),
                CreateBuilding("c85658f6-45d4-4b4a-a64f-3e1a10f59991", RedRefineryTileId, BuildingType.Railroad, 6),
                CreateBuilding("8b036491-6c29-432d-98b5-c70fb9326712", RedDeadCorridorSamTileId, BuildingType.Railroad, 5),
                CreateBuilding("a963e4f6-96f8-413c-b092-1d6f435f1fc0", RedFrontTileIds[0], BuildingType.Railroad, 4),
                CreateBuilding("d8b6a1f9-5e0c-438e-1367-9f4c084b3a7f", RedFrontTileIds[1], BuildingType.Railroad, 4),
                CreateBuilding("b8056bb0-5fb5-4d09-b9d4-a4585c43f0f7", RedFrontTileIds[1], BuildingType.SupplyHub, 4),
                CreateBuilding("2f40d216-7eb7-4362-8f31-d519a6a2d585", RedFrontTileIds[2], BuildingType.Railroad, 3),
                CreateStaticSamBuilding("e765cf9b-4220-49d6-a4ce-001ac06f0fae", RedDefensiveAirbaseTileId),
                // This isolated battery blocks Blue's first airport-strike corridor.
                // Nearby Red SAMs intentionally remain outside its 40 km DEAD area
                // so the assigned package can enter this site's envelope without
                // being rejected by an overlapping, non-target threat.
                CreateStaticSamBuilding(
                    "4e854b37-5479-4473-9daf-338cc7b01b69",
                    RedDeadCorridorSamTileId),
                CreateStaticSamBuilding("7c526362-4d00-466d-aa43-63535843ae99", new Vector3Int(2, 4, -6)),
                // Blue's five SA-2 batteries already provide five Spoon Rest
                // acquisition radars. These two radar-only sites give Red the
                // same number of long-range surveillance sensors without
                // adding launchers or changing the intended SAM threat layout.
                CreateSurveillanceRadarBuilding(
                    "7603f203-51ce-4ceb-ab40-83fc9d2975a9",
                    RedNorthernSurveillanceRadarTileId),
                CreateSurveillanceRadarBuilding(
                    "4c804d69-61ef-496a-a54d-cd4a3ac6e3bf",
                    RedSouthernSurveillanceRadarTileId)
            };
        }

        private static List<SquadronStartingCondition> CreateSquadronStartingConditions()
        {
            var squadrons = new List<SquadronStartingCondition>();

            AddFighterSquadrons(
                squadrons,
                BlueRearFighterSquadronIds,
                BlueCountryId,
                TestModule.F16AircraftTypeId,
                BlueDefensiveAirportBuildingId,
                RearFighterAircraftPerSquadron,
                "BLUE-REAR-FTR");
            AddFighterSquadrons(
                squadrons,
                BlueForwardFighterSquadronIds,
                BlueCountryId,
                TestModule.F16AircraftTypeId,
                BlueVulnerableAirportBuildingId,
                ForwardFighterAircraftPerSquadron,
                "BLUE-FWD-FTR");
            AddFighterSquadrons(
                squadrons,
                RedRearFighterSquadronIds,
                RedCountryId,
                TestModule.Mig29AircraftTypeId,
                RedDefensiveAirportBuildingId,
                RearFighterAircraftPerSquadron,
                "RED-REAR-FTR");
            AddFighterSquadrons(
                squadrons,
                RedForwardFighterSquadronIds,
                RedCountryId,
                TestModule.Mig29AircraftTypeId,
                RedVulnerableAirportBuildingId,
                ForwardFighterAircraftPerSquadron,
                "RED-FWD-FTR");

            squadrons.AddRange(new[]
            {
                new SquadronStartingCondition
                {
                    SquadronId = BlueAwacsSquadronId,
                    CountryId = BlueCountryId,
                    AircraftTypeDefinitionId = TestModule.E3AircraftTypeId,
                    StartingAirportBuildingId = BlueDefensiveAirportBuildingId,
                    AircraftCount = 3,
                    Name = "BLUE-REAR-C2"
                },
                new SquadronStartingCondition
                {
                    SquadronId = BlueTankerSquadronId,
                    CountryId = BlueCountryId,
                    AircraftTypeDefinitionId = TestModule.Kc135AircraftTypeId,
                    StartingAirportBuildingId = BlueDefensiveAirportBuildingId,
                    AircraftCount = 4,
                    Name = "BLUE-REAR-TKR"
                },
                new SquadronStartingCondition
                {
                    SquadronId = RedAwacsSquadronId,
                    CountryId = RedCountryId,
                    AircraftTypeDefinitionId = TestModule.A50AircraftTypeId,
                    StartingAirportBuildingId = RedDefensiveAirportBuildingId,
                    AircraftCount = 5,
                    Name = "RED-REAR-C2"
                },
                new SquadronStartingCondition
                {
                    SquadronId = RedTankerSquadronId,
                    CountryId = RedCountryId,
                    AircraftTypeDefinitionId = TestModule.Il78AircraftTypeId,
                    StartingAirportBuildingId = RedDefensiveAirportBuildingId,
                    AircraftCount = 4,
                    Name = "RED-REAR-TKR"
                }
            });

            return squadrons;
        }

        private static void AddFighterSquadrons(
            ICollection<SquadronStartingCondition> squadrons,
            IReadOnlyList<Guid> squadronIds,
            Guid countryId,
            Guid aircraftTypeDefinitionId,
            Guid airportBuildingId,
            int aircraftPerSquadron,
            string namePrefix)
        {
            for (var i = 0; i < squadronIds.Count; i++)
            {
                squadrons.Add(new SquadronStartingCondition
                {
                    SquadronId = squadronIds[i],
                    CountryId = countryId,
                    AircraftTypeDefinitionId = aircraftTypeDefinitionId,
                    StartingAirportBuildingId = airportBuildingId,
                    AircraftCount = aircraftPerSquadron,
                    Name = $"{namePrefix}-{(char)('A' + i)}"
                });
            }
        }

        private static List<DivisionStartingCondition> CreateDivisionStartingConditions()
        {
            var divisions = new List<DivisionStartingCondition>();
            var blueDivisionNumber = 1;

            for (var i = 0; i < BlueFrontTileIds.Length; i++)
            {
                for (var divisionSlot = 0; divisionSlot < GetFrontTileDivisionCount(i); divisionSlot++)
                {
                    divisions.Add(new DivisionStartingCondition
                    {
                        DivisionId = BlueFrontDivisionIds[blueDivisionNumber - 1],
                        DivisionTemplateId = TestModule.BlueArmoredDivisionTemplateId,
                        CountryId = BlueCountryId,
                        TileId = BlueFrontTileIds[i],
                        Name = $"{blueDivisionNumber}{GetOrdinalSuffix(blueDivisionNumber)} Blue Front Division"
                    });
                    blueDivisionNumber++;
                }
            }

            var redDivisionNumber = 1;
            for (var i = 0; i < RedFrontTileIds.Length; i++)
            {
                for (var divisionSlot = 0; divisionSlot < GetFrontTileDivisionCount(i); divisionSlot++)
                {
                    divisions.Add(new DivisionStartingCondition
                    {
                        DivisionId = RedFrontDivisionIds[redDivisionNumber - 1],
                        DivisionTemplateId = TestModule.RedTankDivisionTemplateId,
                        CountryId = RedCountryId,
                        TileId = RedFrontTileIds[i],
                        Name = $"{redDivisionNumber}{GetOrdinalSuffix(redDivisionNumber)} Red Front Division"
                    });
                    redDivisionNumber++;
                }
            }

            return divisions;
        }

        private static List<MobileSamSiteStartingCondition> CreateMobileSamSiteStartingConditions()
        {
            return new List<MobileSamSiteStartingCondition>
            {
                new MobileSamSiteStartingCondition
                {
                    MobileSamSiteId = Guid.Parse("fce6b590-8b95-4687-bbbe-e85868319572"),
                    SamSiteTemplateId = TestModule.OsaSiteTemplateId,
                    HostDivisionId = BlueFrontDivisionIds[2],
                    Alliance = Alliance.Bluefor
                },
                new MobileSamSiteStartingCondition
                {
                    MobileSamSiteId = Guid.Parse("74857b0e-956d-48a2-a3cc-26f99cdcc006"),
                    SamSiteTemplateId = TestModule.OsaSiteTemplateId,
                    HostDivisionId = BlueFrontDivisionIds[6],
                    Alliance = Alliance.Bluefor
                },
                new MobileSamSiteStartingCondition
                {
                    MobileSamSiteId = Guid.Parse("07f07acf-78f5-4afd-9c8c-663d3120b82a"),
                    SamSiteTemplateId = TestModule.OsaSiteTemplateId,
                    HostDivisionId = RedFrontDivisionIds[10],
                    Alliance = Alliance.Redfor
                },
                new MobileSamSiteStartingCondition
                {
                    MobileSamSiteId = Guid.Parse("9b2e7f58-f5a0-4b01-9b9c-162596203e07"),
                    SamSiteTemplateId = TestModule.OsaSiteTemplateId,
                    HostDivisionId = RedFrontDivisionIds[6],
                    Alliance = Alliance.Redfor
                }
            };
        }

        // Two interior front tiles retain a defender while contributing two divisions to assaults.
        // Both alliances use the same distribution: 1, 3, 1, 3, 1, 1, 1 (11 divisions each).
        private static int GetFrontTileDivisionCount(int frontTileIndex)
        {
            return frontTileIndex == 1 || frontTileIndex == 3 ? 3 : 1;
        }

        private static string GetOrdinalSuffix(int value)
        {
            return value switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };
        }

        private static Tile CreateLandTile(Vector3Int tileId, Alliance alliance)
        {
            return new Tile
            {
                Coordinates = tileId,
                Surface = TileSurface.Land,
                Terrain = SelectTerrain(tileId, alliance),
                Urbanization = SelectUrbanization(tileId, alliance),
                ForestCover = SelectForestCover(tileId, alliance)
            };
        }

        private static bool IsBlueFrontTile(Vector3Int tileId)
        {
            for (var i = 0; i < BlueFrontTileIds.Length; i++)
            {
                if (BlueFrontTileIds[i] == tileId)
                    return true;
            }

            return false;
        }

        private static bool IsRedFrontTile(Vector3Int tileId)
        {
            for (var i = 0; i < RedFrontTileIds.Length; i++)
            {
                if (RedFrontTileIds[i] == tileId)
                    return true;
            }

            return false;
        }

        private static bool IsFrontLineTile(Vector3Int tileId)
        {
            return IsBlueFrontTile(tileId) || IsRedFrontTile(tileId);
        }

        private static TileTerrain SelectTerrain(Vector3Int tileId, Alliance alliance)
        {
            if (tileId == BlueCapitalTileId)
                return TileTerrain.Plains;

            if (tileId == BluePortTileId)
                return TileTerrain.Coast;

            if (IsFrontLineTile(tileId))
                return TileTerrain.Hills;

            if (tileId == RedCapitalTileId)
                return TileTerrain.Mountain;

            if (alliance == Alliance.Redfor && tileId.x >= 2)
                return TileTerrain.Hills;

            return TileTerrain.Plains;
        }

        private static Urbanization SelectUrbanization(Vector3Int tileId, Alliance alliance)
        {
            if (tileId == BlueCapitalTileId)
                return Urbanization.Urban;

            if (tileId == RedCapitalTileId)
                return Urbanization.Suburban;

            if (IsFrontLineTile(tileId))
                return Urbanization.Rural;

            if (alliance == Alliance.Neutral)
                return Urbanization.Rural;

            return Urbanization.Suburban;
        }

        private static ForestCover SelectForestCover(Vector3Int tileId, Alliance alliance)
        {
            if (tileId == RedCapitalTileId)
                return ForestCover.Heavy;

            if (alliance == Alliance.Neutral)
                return ForestCover.Light;

            if (IsFrontLineTile(tileId) && alliance == Alliance.Bluefor)
                return ForestCover.Light;

            return ForestCover.None;
        }

        private static BuildingLevel CreateInfrastructure(Vector3Int tileId, Alliance alliance)
        {
            if (tileId == BlueCapitalTileId)
                return new BuildingLevel(8);

            if (tileId == RedCapitalTileId)
                return new BuildingLevel(7);

            if (IsBlueFrontTile(tileId) || IsRedFrontTile(tileId))
                return new BuildingLevel(4, 1);

            if (alliance == Alliance.Neutral)
                return new BuildingLevel(5);

            return new BuildingLevel(6);
        }

        private static BuildingStartingCondition CreateBuilding(
            string buildingId,
            Vector3Int tileId,
            BuildingType type,
            int level,
            int damage = 0)
        {
            return new BuildingStartingCondition
            {
                BuildingId = Guid.Parse(buildingId),
                PositionFeet = CampaignMapCoordinates.TileCenterFeet(tileId),
                Type = type,
                Level = new BuildingLevel(level, damage),
            };
        }

        private static BuildingStartingCondition CreateStaticSamBuilding(
            string buildingId,
            Vector3Int tileId)
        {
            return new BuildingStartingCondition
            {
                BuildingId = Guid.Parse(buildingId),
                PositionFeet = CampaignMapCoordinates.TileCenterFeet(tileId),
                Type = BuildingType.AirDefense,
                Level = new BuildingLevel(1),
                SamSiteTemplateId = TestModule.Sa2SiteTemplateId
            };
        }

        private static BuildingStartingCondition CreateSurveillanceRadarBuilding(
            string buildingId,
            Vector3Int tileId)
        {
            return new BuildingStartingCondition
            {
                BuildingId = Guid.Parse(buildingId),
                PositionFeet = CampaignMapCoordinates.TileCenterFeet(tileId),
                Type = BuildingType.AirDefense,
                Level = new BuildingLevel(1),
                SamSiteTemplateId = TestModule.SurveillanceRadarSiteTemplateId
            };
        }
    }
}
