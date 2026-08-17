using System;
using System.Collections.Generic;

namespace Models.Module
{
    public static class TestModule
    {
        private const string LongRangeVhfSearchFusionGroup =
            "long_range_vhf_search";
        private const string PrecisionFireControlFusionGroup =
            "precision_fire_control";
        private const string ShortRangeOrganicFusionGroup =
            "short_range_organic";

        public static readonly Guid Id = Guid.Parse("92f96fd1-d2f1-4e28-a047-30b0940dc45f");
        public static readonly Guid BlueCountryId = Guid.Parse("64bfb064-0136-44d3-9983-620cf38d8245");
        public static readonly Guid RedCountryId = Guid.Parse("f6610c73-4f7b-4a71-9695-2f085dc43a7f");
        public static readonly Guid NeutralCountryId = Guid.Parse("ba8ec50b-b305-46ce-b2f6-82a28113f1b5");

        public static readonly Guid BlueInfantryBattalionId = Guid.Parse("1b3d06ec-80a7-4593-93ab-db5d1a44a64d");
        public static readonly Guid BlueArmorBattalionId = Guid.Parse("c71881f4-44ca-4b59-a228-4433f784bbf1");
        public static readonly Guid RedMotorRifleBattalionId = Guid.Parse("9e1716ea-1d90-42ea-806d-1c93cc1cdf22");
        public static readonly Guid RedTankBattalionId = Guid.Parse("ebd352b3-dc92-463d-a3cf-73e8c6de1cd2");

        public static readonly Guid F16AircraftTypeId = Guid.Parse("5084422f-4014-46be-88aa-215a927fc48e");
        public static readonly Guid Mig29AircraftTypeId = Guid.Parse("12e89ecf-ac2e-4329-a21e-55f0fb0666f0");
        public static readonly Guid E3AircraftTypeId = Guid.Parse("c5196101-8f48-45ce-9762-8124ce4503dc");
        public static readonly Guid Kc135AircraftTypeId = Guid.Parse("94bf002d-7a23-48f2-b68a-ad8c655419da");
        public static readonly Guid A50AircraftTypeId = Guid.Parse("bca85d49-3ba3-432f-a9e3-559521f4e120");
        public static readonly Guid Il78AircraftTypeId = Guid.Parse("05863902-90d7-41b0-b0b2-a7871017ae82");

        public static readonly Guid F16Station1Id =
            Guid.Parse("13a7e000-1678-4e1c-90d8-f802454a8f50");
        public static readonly Guid F16Station2Id =
            Guid.Parse("a4d8c828-68e2-419d-9b7a-3e89fce91848");
        public static readonly Guid F16Station3Id =
            Guid.Parse("f6e9bb8c-5aa7-4567-b5fd-0a1a80a45ed6");
        public static readonly Guid F16Station4Id =
            Guid.Parse("d2f002f5-72e0-4e5d-9687-1e8e4c03764e");
        public static readonly Guid F16Station5Id =
            Guid.Parse("1f50509e-e43c-4257-8182-d248d016434b");
        public static readonly Guid F16Station6Id =
            Guid.Parse("c1b5c399-7d72-40cf-9621-7cdb617cfbf7");
        public static readonly Guid F16Station7Id =
            Guid.Parse("b6d3560f-044b-4774-928f-96f11468fc79");
        public static readonly Guid F16Station8Id =
            Guid.Parse("73d106db-ec8f-49ad-a60a-064bdc9e22f5");
        public static readonly Guid F16Station9Id =
            Guid.Parse("5e6e1e94-d6ce-413a-86ac-6401601530c6");
        public static readonly Guid Mig29Station1Id =
            Guid.Parse("9715a229-7e4c-4706-92bc-f6b56a2169c5");
        public static readonly Guid Mig29Station2Id =
            Guid.Parse("bb3f67ec-f7f0-4dc0-8d0d-8391beff311c");
        public static readonly Guid Mig29Station3Id =
            Guid.Parse("a855f97e-fc7a-4651-a58a-b7c7d20f154e");
        public static readonly Guid Mig29Station4Id =
            Guid.Parse("d3f67c21-abc0-43d7-9cde-708221e1352d");
        public static readonly Guid Mig29Station5Id =
            Guid.Parse("31adc4f3-5b0b-4ff9-94bd-dca1f3f89fe1");
        public static readonly Guid Mig29Station6Id =
            Guid.Parse("eab448fa-abd3-46c2-9502-50a6677b5a0f");

        public static readonly Guid F16Aim120CarriageId =
            Guid.Parse("3e91c664-86f3-49c0-a9f0-9b291d9f2dad");
        public static readonly Guid F16Aim9CarriageId =
            Guid.Parse("28652ca3-cd70-4136-9d51-f179bf555347");
        public static readonly Guid F16Agm88CarriageId =
            Guid.Parse("77206db0-3987-48a8-8f0f-3f5cc752137f");
        public static readonly Guid F16Gbu38CarriageId =
            Guid.Parse("76839102-7074-461a-a956-d255f6518d9f");
        public static readonly Guid F16Agm65CarriageId =
            Guid.Parse("fefa5e97-57fe-415a-9cb0-05151972c3e2");
        public static readonly Guid Mig29R27CarriageId =
            Guid.Parse("f6cf2134-f59c-4cc1-a883-1b0e0316d437");
        public static readonly Guid Mig29R73CarriageId =
            Guid.Parse("08055404-439a-4e5b-9baf-f1836005ea77");

        public static readonly Guid Aim120OrdnanceTypeId = Guid.Parse("7486758d-565b-4a19-8d26-29dd717b0e22");
        public static readonly Guid Aim9OrdnanceTypeId = Guid.Parse("5e7975e2-09a8-46f4-bb2d-05de209b60b8");
        public static readonly Guid Agm88OrdnanceTypeId = Guid.Parse("3160216b-64bf-45b8-b245-c7ee2303864e");
        public static readonly Guid Gbu38OrdnanceTypeId = Guid.Parse("37f8f135-55a8-42a1-9478-f583508e344c");
        public static readonly Guid Agm65OrdnanceTypeId = Guid.Parse("24d59f63-9714-4306-9ebb-f79075df0909");
        public static readonly Guid R27OrdnanceTypeId = Guid.Parse("df62234e-e894-4d4e-8d67-e4bc6fe68405");
        public static readonly Guid R73OrdnanceTypeId = Guid.Parse("fc9d6932-ef37-4fc3-b024-12fc1e2d9f1c");
        public static readonly Guid M61GunOrdnanceTypeId =
            Guid.Parse("c46325d2-718b-4f48-93d5-1fe7a8aab001");
        public static readonly Guid Gsh301GunOrdnanceTypeId =
            Guid.Parse("dc253cf1-6218-4c33-a7ad-2f5c0965b002");
        public static readonly Guid Sa2InterceptorOrdnanceTypeId =
            Guid.Parse("b8afb031-30a8-4de0-90a7-0cc958d8813f");
        public static readonly Guid OsaInterceptorOrdnanceTypeId =
            Guid.Parse("713acf12-4569-43cb-bd66-23bbbda24168");

        public static readonly Guid FanSongComponentId = Guid.Parse("0ec0a8c9-dc38-461c-a3f8-1831afdf43ad");
        public static readonly Guid SpoonRestComponentId =
            Guid.Parse("a4f1d813-2f60-42ec-817c-5bcefe160e5d");
        public static readonly Guid Sa2LauncherComponentId = Guid.Parse("1ecf62f4-3034-4be3-86c3-0e8d65ecef6a");
        public static readonly Guid SamCommandPostComponentId = Guid.Parse("0ef1697d-f3fb-49fa-8f54-bac90107b552");
        public static readonly Guid OsaRadarComponentId = Guid.Parse("9daab0e8-3d79-49f3-91b5-feb343f09fac");
        public static readonly Guid OsaLauncherComponentId = Guid.Parse("f7f2f3d3-0e2b-4f6f-9b7e-617b1ec23cb3");
        public static readonly Guid OsaCommandComponentId = Guid.Parse("1ad7d010-b81c-48af-9844-854a864385e6");
        public static readonly Guid Sa2SiteTemplateId = Guid.Parse("9a408a92-9a60-4fb6-bd38-18cb6f2771a5");
        public static readonly Guid OsaSiteTemplateId = Guid.Parse("710226da-3875-4312-81ea-29606aca76c6");
        public static readonly Guid SurveillanceRadarSiteTemplateId =
            Guid.Parse("a7c3f3e8-595a-4266-a2ea-bbadde9f2a16");

        public static readonly Guid BlueArmoredDivisionTemplateId = Guid.Parse("4153b384-6e76-42df-a1e5-d54582022bee");
        public static readonly Guid RedTankDivisionTemplateId = Guid.Parse("2d4ecc7e-8285-4b8b-9281-00cccbf5d2e8");

        public static ModuleDefinition GetTestModule()
        {
            return CreateModule(
                Id,
                "Standalone Test Module",
                "Standalone",
                "HZPL Engine",
                new NoOpSimAdapter());
        }

        internal static ModuleDefinition CreateModule(
            Guid moduleId,
            string displayName,
            string name,
            string gameName,
            ISimAdapter simAdapter,
            IReadOnlyDictionary<Guid, string> thirdPartyIds = null)
        {
            return new ModuleDefinition(
                moduleId,
                displayName,
                name,
                gameName,
                simAdapter,
                CreateCountries(),
                CreateAircraftTypeDefinitions(thirdPartyIds),
                CreateOrdnanceTypeDefinitions(thirdPartyIds),
                CreateSamComponentDefinitions(thirdPartyIds),
                CreateSamSiteTemplates(),
                CreateBattalionDefinitions(),
                CreateDivisionTemplates());
        }

        private static string GetThirdPartyId(
            IReadOnlyDictionary<Guid, string> thirdPartyIds,
            Guid definitionId)
        {
            return thirdPartyIds != null
                   && thirdPartyIds.TryGetValue(definitionId, out var thirdPartyId)
                ? thirdPartyId
                : string.Empty;
        }

        private static List<CountryDefinition> CreateCountries()
        {
            return new List<CountryDefinition>
            {
                new CountryDefinition(BlueCountryId, "Blue Republic"),
                new CountryDefinition(RedCountryId, "Red Federation"),
                new CountryDefinition(NeutralCountryId, "Neutral State")
            };
        }

        private static List<AircraftTypeDefinition> CreateAircraftTypeDefinitions(
            IReadOnlyDictionary<Guid, string> thirdPartyIds)
        {
            return new List<AircraftTypeDefinition>
            {
                new AircraftTypeDefinition(
                    F16AircraftTypeId,
                    "F-16C Fighting Falcon",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, F16AircraftTypeId),
                    cruiseSpeedKnots: 459f,
                    combatSpeedKnots: 529f,
                    climbRateFeetPerMinute: 18000f,
                    descentRateFeetPerMinute: 12000f,
                    turnRateDegreesPerSecond: 3f,
                    nominalCruiseAltitudeFeet: 36000f,
                    serviceCeilingFeet: 50000f,
                    rangeKm: 1400f,
                    enduranceHours: 2.4f,
                    radarQuality: 0.7f,
                    radarDetectability: 0.6f,
                    radarDefense: 0.55f,
                    infraredDefense: 0.5f,
                    gunDefense: 0.7f,
                    survivability: 0.65f,
                    ordnanceCapacity: 12f,
                    canReceiveAerialRefueling: true,
                    airInterferenceCapability: 1f,
                    internalGunOrdnanceTypeDefinitionId: M61GunOrdnanceTypeId,
                    internalGunBurstCount: 6,
                    wvrCombatRating: 0.72f,
                    defensiveTurnRateDegreesPerSecond: 9f,
                    loadoutStations: CreateF16LoadoutStations(thirdPartyIds),
                    carriageConfigurations:
                    CreateF16CarriageConfigurations(thirdPartyIds)),
                new AircraftTypeDefinition(
                    Mig29AircraftTypeId,
                    "MiG-29 Fulcrum",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, Mig29AircraftTypeId),
                    cruiseSpeedKnots: 486f,
                    combatSpeedKnots: 567f,
                    climbRateFeetPerMinute: 20000f,
                    descentRateFeetPerMinute: 12000f,
                    turnRateDegreesPerSecond: 3.2f,
                    nominalCruiseAltitudeFeet: 39000f,
                    serviceCeilingFeet: 59000f,
                    rangeKm: 1100f,
                    enduranceHours: 2.0f,
                    radarQuality: 0.78f,
                    radarDetectability: 0.6f,
                    radarDefense: 0.45f,
                    infraredDefense: 0.5f,
                    gunDefense: 0.72f,
                    survivability: 0.7f,
                    ordnanceCapacity: 10f,
                    canReceiveAerialRefueling: true,
                    airInterferenceCapability: 0.95f,
                    internalGunOrdnanceTypeDefinitionId: Gsh301GunOrdnanceTypeId,
                    internalGunBurstCount: 5,
                    wvrCombatRating: 0.74f,
                    defensiveTurnRateDegreesPerSecond: 9.6f,
                    loadoutStations: CreateMig29LoadoutStations(thirdPartyIds),
                    carriageConfigurations:
                    CreateMig29CarriageConfigurations(thirdPartyIds)),
                new AircraftTypeDefinition(
                    E3AircraftTypeId,
                    "E-3 Sentry",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, E3AircraftTypeId),
                    cruiseSpeedKnots: 432f,
                    combatSpeedKnots: 432f,
                    climbRateFeetPerMinute: 3000f,
                    descentRateFeetPerMinute: 3000f,
                    turnRateDegreesPerSecond: 1.2f,
                    nominalCruiseAltitudeFeet: 35000f,
                    serviceCeilingFeet: 42000f,
                    rangeKm: 7400f,
                    enduranceHours: 8f,
                    radarQuality: 1f,
                    radarDetectability: 1f,
                    radarDefense: 0.6f,
                    infraredDefense: 0.35f,
                    gunDefense: 0.1f,
                    survivability: 0.35f,
                    supportCapability: AirSupportCapability.AirborneC2,
                    supportSlotCapacity: 12,
                    wvrCombatRating: 0.10f),
                new AircraftTypeDefinition(
                    Kc135AircraftTypeId,
                    "KC-135 Stratotanker",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, Kc135AircraftTypeId),
                    cruiseSpeedKnots: 459f,
                    combatSpeedKnots: 459f,
                    climbRateFeetPerMinute: 3500f,
                    descentRateFeetPerMinute: 3500f,
                    turnRateDegreesPerSecond: 1.3f,
                    nominalCruiseAltitudeFeet: 30000f,
                    serviceCeilingFeet: 50000f,
                    rangeKm: 5500f,
                    enduranceHours: 10f,
                    radarQuality: 0.1f,
                    radarDetectability: 0.9f,
                    radarDefense: 0.35f,
                    infraredDefense: 0.3f,
                    gunDefense: 0.05f,
                    survivability: 0.3f,
                    supportCapability: AirSupportCapability.AerialRefueling,
                    supportSlotCapacity: 8,
                    wvrCombatRating: 0.05f),
                new AircraftTypeDefinition(
                    A50AircraftTypeId,
                    "A-50 Mainstay",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, A50AircraftTypeId),
                    cruiseSpeedKnots: 432f,
                    combatSpeedKnots: 432f,
                    climbRateFeetPerMinute: 3000f,
                    descentRateFeetPerMinute: 3000f,
                    turnRateDegreesPerSecond: 1.2f,
                    nominalCruiseAltitudeFeet: 33000f,
                    serviceCeilingFeet: 40000f,
                    rangeKm: 5500f,
                    enduranceHours: 7f,
                    radarQuality: 0.95f,
                    radarDetectability: 1f,
                    radarDefense: 0.5f,
                    infraredDefense: 0.35f,
                    gunDefense: 0.1f,
                    survivability: 0.35f,
                    supportCapability: AirSupportCapability.AirborneC2,
                    supportSlotCapacity: 10,
                    wvrCombatRating: 0.10f),
                new AircraftTypeDefinition(
                    Il78AircraftTypeId,
                    "Il-78 Midas",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, Il78AircraftTypeId),
                    cruiseSpeedKnots: 421f,
                    combatSpeedKnots: 421f,
                    climbRateFeetPerMinute: 3000f,
                    descentRateFeetPerMinute: 3000f,
                    turnRateDegreesPerSecond: 1.2f,
                    nominalCruiseAltitudeFeet: 28000f,
                    serviceCeilingFeet: 39000f,
                    rangeKm: 7300f,
                    enduranceHours: 8f,
                    radarQuality: 0.1f,
                    radarDetectability: 0.9f,
                    radarDefense: 0.3f,
                    infraredDefense: 0.3f,
                    gunDefense: 0.05f,
                    survivability: 0.3f,
                    supportCapability: AirSupportCapability.AerialRefueling,
                    supportSlotCapacity: 8,
                    wvrCombatRating: 0.05f)
            };
        }

        private static List<AircraftCarriageConfigurationDefinition>
            CreateF16CarriageConfigurations(
                IReadOnlyDictionary<Guid, string> thirdPartyIds)
        {
            return new List<AircraftCarriageConfigurationDefinition>
            {
                SingleStoreCarriage(
                    F16Aim120CarriageId,
                    "Single AIM-120",
                    Aim120OrdnanceTypeId,
                    1f,
                    thirdPartyIds),
                SingleStoreCarriage(
                    F16Aim9CarriageId,
                    "Single AIM-9",
                    Aim9OrdnanceTypeId,
                    0.75f,
                    thirdPartyIds),
                SingleStoreCarriage(
                    F16Agm88CarriageId,
                    "Single AGM-88",
                    Agm88OrdnanceTypeId,
                    3f,
                    thirdPartyIds),
                SingleStoreCarriage(
                    F16Gbu38CarriageId,
                    "Single GBU-38",
                    Gbu38OrdnanceTypeId,
                    2f,
                    thirdPartyIds),
                SingleStoreCarriage(
                    F16Agm65CarriageId,
                    "Single AGM-65",
                    Agm65OrdnanceTypeId,
                    2f,
                    thirdPartyIds)
            };
        }

        private static List<AircraftLoadoutStationDefinition>
            CreateF16LoadoutStations(
                IReadOnlyDictionary<Guid, string> thirdPartyIds)
        {
            return new List<AircraftLoadoutStationDefinition>
            {
                Station(F16Station1Id, 1, F16Station9Id, thirdPartyIds,
                    F16Aim120CarriageId, F16Aim9CarriageId),
                Station(F16Station2Id, 2, F16Station8Id, thirdPartyIds,
                    F16Aim9CarriageId),
                Station(F16Station3Id, 3, F16Station7Id, thirdPartyIds,
                    F16Agm88CarriageId, F16Gbu38CarriageId,
                    F16Agm65CarriageId, F16Aim120CarriageId),
                Station(F16Station4Id, 4, F16Station6Id, thirdPartyIds,
                    F16Aim120CarriageId),
                Station(F16Station5Id, 5, Guid.Empty, thirdPartyIds),
                Station(F16Station6Id, 6, F16Station4Id, thirdPartyIds,
                    F16Aim120CarriageId),
                Station(F16Station7Id, 7, F16Station3Id, thirdPartyIds,
                    F16Agm88CarriageId, F16Gbu38CarriageId,
                    F16Agm65CarriageId, F16Aim120CarriageId),
                Station(F16Station8Id, 8, F16Station2Id, thirdPartyIds,
                    F16Aim9CarriageId),
                Station(F16Station9Id, 9, F16Station1Id, thirdPartyIds,
                    F16Aim120CarriageId, F16Aim9CarriageId)
            };
        }

        private static List<AircraftCarriageConfigurationDefinition>
            CreateMig29CarriageConfigurations(
                IReadOnlyDictionary<Guid, string> thirdPartyIds)
        {
            return new List<AircraftCarriageConfigurationDefinition>
            {
                SingleStoreCarriage(
                    Mig29R27CarriageId,
                    "Single R-27",
                    R27OrdnanceTypeId,
                    1.25f,
                    thirdPartyIds),
                SingleStoreCarriage(
                    Mig29R73CarriageId,
                    "Single R-73",
                    R73OrdnanceTypeId,
                    0.75f,
                    thirdPartyIds)
            };
        }

        private static List<AircraftLoadoutStationDefinition>
            CreateMig29LoadoutStations(
                IReadOnlyDictionary<Guid, string> thirdPartyIds)
        {
            return new List<AircraftLoadoutStationDefinition>
            {
                Station(Mig29Station1Id, 1, Mig29Station6Id, thirdPartyIds,
                    Mig29R73CarriageId),
                Station(Mig29Station2Id, 2, Mig29Station5Id, thirdPartyIds,
                    Mig29R27CarriageId),
                Station(Mig29Station3Id, 3, Mig29Station4Id, thirdPartyIds,
                    Mig29R27CarriageId),
                Station(Mig29Station4Id, 4, Mig29Station3Id, thirdPartyIds,
                    Mig29R27CarriageId),
                Station(Mig29Station5Id, 5, Mig29Station2Id, thirdPartyIds,
                    Mig29R27CarriageId),
                Station(Mig29Station6Id, 6, Mig29Station1Id, thirdPartyIds,
                    Mig29R73CarriageId)
            };
        }

        private static AircraftCarriageConfigurationDefinition
            SingleStoreCarriage(
                Guid configurationId,
                string name,
                Guid ordnanceId,
                float externalLoadCost,
                IReadOnlyDictionary<Guid, string> thirdPartyIds)
        {
            return new AircraftCarriageConfigurationDefinition(
                configurationId,
                name,
                externalLoadCost,
                new[]
                {
                    new AircraftCarriageOrdnanceDefinition(ordnanceId, 1)
                },
                GetThirdPartyId(thirdPartyIds, configurationId));
        }

        private static AircraftLoadoutStationDefinition Station(
            Guid stationId,
            int number,
            Guid mirrorStationId,
            IReadOnlyDictionary<Guid, string> thirdPartyIds,
            params Guid[] compatibleConfigurations)
        {
            return new AircraftLoadoutStationDefinition(
                stationId,
                $"Station {number}",
                number,
                compatibleConfigurations,
                mirrorStationId,
                GetThirdPartyId(thirdPartyIds, stationId));
        }

        private static List<OrdnanceTypeDefinition> CreateOrdnanceTypeDefinitions(
            IReadOnlyDictionary<Guid, string> thirdPartyIds)
        {
            return new List<OrdnanceTypeDefinition>
            {
                new OrdnanceTypeDefinition(
                    Aim120OrdnanceTypeId,
                    "AIM-120 AMRAAM",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, Aim120OrdnanceTypeId),
                    weight: 1f,
                    effectPower: 2,
                    effectivenessByTargetCategory: new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Aircraft, 0.95f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.AirToAirRadar,
                    guidanceMode: OrdnanceGuidanceMode.ActiveRadar,
                    minimumRangeKm: 2f,
                    maximumRangeKm: 75f,
                    maximumTargetAltitudeFeet: 80000f,
                    preparationSeconds: 30f,
                    effectSpeedKnots: 2400f,
                    hitProbability: 0.72f,
                    maximumLaunchOffBoresightDegrees: 55f,
                    noEscapeRangeFraction: 0.5f,
                    secondsUntilAutonomous: 35f,
                    requiresSupportUntilAutonomous: true,
                    terminalLethality: 0.82f),
                new OrdnanceTypeDefinition(
                    Aim9OrdnanceTypeId,
                    "AIM-9 Sidewinder",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, Aim9OrdnanceTypeId),
                    weight: 0.75f,
                    effectPower: 1,
                    effectivenessByTargetCategory: new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Aircraft, 0.75f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.AirToAirInfrared,
                    guidanceMode: OrdnanceGuidanceMode.Infrared,
                    minimumRangeKm: 0.5f,
                    maximumRangeKm: 18f,
                    maximumTargetAltitudeFeet: 60000f,
                    preparationSeconds: 15f,
                    effectSpeedKnots: 1600f,
                    hitProbability: 0.68f,
                    maximumLaunchOffBoresightDegrees: 45f,
                    noEscapeRangeFraction: 0.65f,
                    terminalLethality: 0.75f),
                new OrdnanceTypeDefinition(
                    M61GunOrdnanceTypeId,
                    "M61A1 20 mm Gun Burst",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, M61GunOrdnanceTypeId),
                    weight: 0f,
                    effectPower: 1,
                    effectivenessByTargetCategory:
                    new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Aircraft, 0.55f },
                        { OrdnanceTargetCategory.Infantry, 0.7f },
                        { OrdnanceTargetCategory.Vehicle, 0.25f },
                        { OrdnanceTargetCategory.Building, 0.08f },
                        { OrdnanceTargetCategory.Radar, 0.2f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.Gun,
                    guidanceMode: OrdnanceGuidanceMode.None,
                    minimumRangeKm: 0.1f,
                    maximumRangeKm: 1.5f,
                    maximumTargetAltitudeFeet: 80000f,
                    preparationSeconds: 1.5f,
                    effectSpeedKnots: 1700f,
                    hitProbability: 0.24f,
                    maximumLaunchOffBoresightDegrees: 6f,
                    noEscapeRangeFraction: 1f,
                    terminalLethality: 0.32f),
                new OrdnanceTypeDefinition(
                    Agm88OrdnanceTypeId,
                    "AGM-88 HARM",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, Agm88OrdnanceTypeId),
                    weight: 3f,
                    effectPower: 3,
                    effectivenessByTargetCategory: new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Radar, 1f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.AntiRadiation,
                    guidanceMode: OrdnanceGuidanceMode.AntiRadiation,
                    maximumRangeKm: 150f,
                    preparationSeconds: 5f,
                    effectSpeedKnots: 1800f,
                    antiRadiationEmitterMemorySeconds: 40f,
                    antiRadiationSilentQualityFloor: 0.25f),
                new OrdnanceTypeDefinition(
                    Gbu38OrdnanceTypeId,
                    "GBU-38 JDAM",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, Gbu38OrdnanceTypeId),
                    weight: 2f,
                    effectPower: 3,
                    effectivenessByTargetCategory: new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Vehicle, 0.55f },
                        { OrdnanceTargetCategory.Building, 0.8f },
                        { OrdnanceTargetCategory.Aircraft, 0.75f },
                        { OrdnanceTargetCategory.Runway, 0.8f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.AirToGroundPrecision,
                    guidanceMode: OrdnanceGuidanceMode.Gps,
                    maximumRangeKm: 28f,
                    preparationSeconds: 45f,
                    effectSpeedKnots: 550f,
                    maximumGroundTargetsPerWeapon: 2,
                    secondaryGroundEffectMultiplier: 0.35f),
                new OrdnanceTypeDefinition(
                    Agm65OrdnanceTypeId,
                    "AGM-65 Maverick",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, Agm65OrdnanceTypeId),
                    weight: 2f,
                    effectPower: 3,
                    effectivenessByTargetCategory: new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Vehicle, 0.9f },
                        { OrdnanceTargetCategory.Building, 0.45f },
                        { OrdnanceTargetCategory.Aircraft, 0.8f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.AirToGroundPrecision,
                    guidanceMode: OrdnanceGuidanceMode.Imaging,
                    minimumRangeKm: 1f,
                    maximumRangeKm: 22f,
                    preparationSeconds: 30f,
                    effectSpeedKnots: 650f),
                new OrdnanceTypeDefinition(
                    R27OrdnanceTypeId,
                    "R-27 Alamo",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, R27OrdnanceTypeId),
                    weight: 1.25f,
                    effectPower: 2,
                    effectivenessByTargetCategory: new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Aircraft, 0.85f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.AirToAirRadar,
                    guidanceMode: OrdnanceGuidanceMode.SemiActiveRadar,
                    minimumRangeKm: 2f,
                    maximumRangeKm: 55f,
                    maximumTargetAltitudeFeet: 80000f,
                    preparationSeconds: 35f,
                    effectSpeedKnots: 2200f,
                    hitProbability: 0.62f,
                    maximumLaunchOffBoresightDegrees: 45f,
                    noEscapeRangeFraction: 0.48f,
                    requiresSupportUntilAutonomous: true,
                    terminalLethality: 0.8f),
                new OrdnanceTypeDefinition(
                    R73OrdnanceTypeId,
                    "R-73 Archer",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, R73OrdnanceTypeId),
                    weight: 0.75f,
                    effectPower: 1,
                    effectivenessByTargetCategory: new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Aircraft, 0.75f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.AirToAirInfrared,
                    guidanceMode: OrdnanceGuidanceMode.Infrared,
                    minimumRangeKm: 0.3f,
                    maximumRangeKm: 18f,
                    maximumTargetAltitudeFeet: 60000f,
                    preparationSeconds: 15f,
                    effectSpeedKnots: 1700f,
                    hitProbability: 0.65f,
                    maximumLaunchOffBoresightDegrees: 60f,
                    noEscapeRangeFraction: 0.68f,
                    terminalLethality: 0.74f),
                new OrdnanceTypeDefinition(
                    Gsh301GunOrdnanceTypeId,
                    "GSh-30-1 30 mm Gun Burst",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, Gsh301GunOrdnanceTypeId),
                    weight: 0f,
                    effectPower: 1,
                    effectivenessByTargetCategory:
                    new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Aircraft, 0.62f },
                        { OrdnanceTargetCategory.Infantry, 0.65f },
                        { OrdnanceTargetCategory.Vehicle, 0.35f },
                        { OrdnanceTargetCategory.Building, 0.1f },
                        { OrdnanceTargetCategory.Radar, 0.25f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.Gun,
                    guidanceMode: OrdnanceGuidanceMode.None,
                    minimumRangeKm: 0.1f,
                    maximumRangeKm: 1.4f,
                    maximumTargetAltitudeFeet: 80000f,
                    preparationSeconds: 1.5f,
                    effectSpeedKnots: 1600f,
                    hitProbability: 0.26f,
                    maximumLaunchOffBoresightDegrees: 6f,
                    noEscapeRangeFraction: 1f,
                    terminalLethality: 0.4f),
                new OrdnanceTypeDefinition(
                    Sa2InterceptorOrdnanceTypeId,
                    "V-750 SAM",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, Sa2InterceptorOrdnanceTypeId),
                    weight: 0f,
                    effectPower: 3,
                    effectivenessByTargetCategory: new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Aircraft, 0.8f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.SurfaceToAir,
                    guidanceMode: OrdnanceGuidanceMode.SemiActiveRadar,
                    minimumRangeKm: 7f,
                    maximumRangeKm: 35f,
                    minimumTargetAltitudeFeet: 984f,
                    maximumTargetAltitudeFeet: 78740f,
                    effectSpeedKnots: 2000f),
                new OrdnanceTypeDefinition(
                    OsaInterceptorOrdnanceTypeId,
                    "9M33 SAM",
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, OsaInterceptorOrdnanceTypeId),
                    weight: 0f,
                    effectPower: 2,
                    effectivenessByTargetCategory: new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Aircraft, 0.75f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.SurfaceToAir,
                    guidanceMode: OrdnanceGuidanceMode.SemiActiveRadar,
                    minimumRangeKm: 1.5f,
                    maximumRangeKm: 12f,
                    minimumTargetAltitudeFeet: 82f,
                    maximumTargetAltitudeFeet: 16404f,
                    effectSpeedKnots: 1800f)
            };
        }

        private static List<AirDefenseComponentDefinition> CreateSamComponentDefinitions(
            IReadOnlyDictionary<Guid, string> thirdPartyIds)
        {
            return new List<AirDefenseComponentDefinition>
            {
                new RadarAirDefenseComponentDefinition(
                    FanSongComponentId,
                    "Fan Song radar component",
                    OrdnanceTargetCategory.Radar,
                    targetToughness: 2,
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, FanSongComponentId),
                    detectionRangeKm: 75f,
                    maxAltitudeMeters: 24000f,
                    antennaHeightMeters: 5f,
                    fusionCorrelationGroup: PrecisionFireControlFusionGroup,
                    trackQuality: 0.75f,
                    providesWeaponQualityTrack: true,
                    maximumSupportedMissiles: 3,
                    maximumConcurrentTargetEngagements: 1,
                    searchesWhileUnassigned: false),
                new RadarAirDefenseComponentDefinition(
                    SpoonRestComponentId,
                    "Spoon Rest acquisition radar component",
                    OrdnanceTargetCategory.Radar,
                    targetToughness: 2,
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, SpoonRestComponentId),
                    detectionRangeKm: 120f,
                    maxAltitudeMeters: 24000f,
                    antennaHeightMeters: 10f,
                    fusionCorrelationGroup: LongRangeVhfSearchFusionGroup,
                    trackQuality: 0.5f,
                    providesWeaponQualityTrack: false,
                    searchesWhileUnassigned: true),
                new LauncherAirDefenseComponentDefinition(
                    Sa2LauncherComponentId,
                    "SA-2 launcher rail",
                    OrdnanceTargetCategory.Building,
                    targetToughness: 2,
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, Sa2LauncherComponentId),
                    minEngagementRangeKm: 7f,
                    maxEngagementRangeKm: 35f,
                    minEngagementAltitudeMeters: 300f,
                    maxEngagementAltitudeMeters: 24000f,
                    readyRoundCapacity: 1,
                    reserveRoundCapacity: 1,
                    reloadMinutes: 30f,
                    minimumTrackQualityToFire: 0.45f,
                    preferredEngagementSalvoSize: 2,
                    surfaceToAirOrdnanceTypeDefinitionId: Sa2InterceptorOrdnanceTypeId),
                new CommandAirDefenseComponentDefinition(
                    SamCommandPostComponentId,
                    "SAM command post",
                    OrdnanceTargetCategory.Building,
                    targetToughness: 2,
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, SamCommandPostComponentId)),
                new RadarAirDefenseComponentDefinition(
                    OsaRadarComponentId,
                    "SA-8 Osa organic radar",
                    OrdnanceTargetCategory.Radar,
                    targetToughness: 2,
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, OsaRadarComponentId),
                    detectionRangeKm: 35f,
                    maxAltitudeMeters: 12000f,
                    antennaHeightMeters: 4f,
                    fusionCorrelationGroup: ShortRangeOrganicFusionGroup,
                    trackQuality: 0.55f,
                    providesWeaponQualityTrack: true),
                new LauncherAirDefenseComponentDefinition(
                    OsaLauncherComponentId,
                    "SA-8 Osa launcher",
                    OrdnanceTargetCategory.Vehicle,
                    targetToughness: 2,
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, OsaLauncherComponentId),
                    minEngagementRangeKm: 1.5f,
                    maxEngagementRangeKm: 12f,
                    minEngagementAltitudeMeters: 25f,
                    maxEngagementAltitudeMeters: 5000f,
                    readyRoundCapacity: 6,
                    reserveRoundCapacity: 0,
                    reloadMinutes: 0f,
                    minimumTrackQualityToFire: 0.4f,
                    surfaceToAirOrdnanceTypeDefinitionId: OsaInterceptorOrdnanceTypeId),
                new CommandAirDefenseComponentDefinition(
                    OsaCommandComponentId,
                    "SA-8 Osa command component",
                    OrdnanceTargetCategory.Vehicle,
                    targetToughness: 2,
                    thirdPartyId: GetThirdPartyId(thirdPartyIds, OsaCommandComponentId))
            };
        }

        private static List<SamSiteTemplate> CreateSamSiteTemplates()
        {
            return new List<SamSiteTemplate>
            {
                new SamSiteTemplate(
                    Sa2SiteTemplateId,
                    "SA-2 battery",
                    SamSiteHostConstraint.StaticOnly,
                    new List<SamSiteTemplateComponent>
                    {
                        new SamSiteTemplateComponent(SpoonRestComponentId, 1),
                        new SamSiteTemplateComponent(FanSongComponentId, 1),
                        new SamSiteTemplateComponent(Sa2LauncherComponentId, 6),
                        new SamSiteTemplateComponent(SamCommandPostComponentId, 1)
                    }),
                new SamSiteTemplate(
                    OsaSiteTemplateId,
                    "SA-8 Osa section",
                    SamSiteHostConstraint.MobileOnly,
                    new List<SamSiteTemplateComponent>
                    {
                        new SamSiteTemplateComponent(OsaRadarComponentId, 1),
                        new SamSiteTemplateComponent(OsaLauncherComponentId, 1),
                        new SamSiteTemplateComponent(OsaCommandComponentId, 1)
                    }),
                new SamSiteTemplate(
                    SurveillanceRadarSiteTemplateId,
                    "Spoon Rest surveillance radar site",
                    SamSiteHostConstraint.StaticOnly,
                    new List<SamSiteTemplateComponent>
                    {
                        new SamSiteTemplateComponent(SpoonRestComponentId, 1)
                    })
            };
        }

        private static List<BattalionDefinition> CreateBattalionDefinitions()
        {
            return new List<BattalionDefinition>
            {
                new BattalionDefinition(
                    BlueInfantryBattalionId,
                    BlueCountryId,
                    "Blue Infantry Battalion",
                    120,
                    45,
                    0.25f,
                    12f,
                    2f,
                    18,
                    8,
                    0.9f,
                    4f,
                    2,
                    0.5f,
                    0f,
                    groundTargetProfile: new List<GroundTargetProfileEntry>
                    {
                        new GroundTargetProfileEntry(
                            "infantry position",
                            OrdnanceTargetCategory.Infantry,
                            1,
                            0.85f,
                            4),
                        new GroundTargetProfileEntry(
                            "support truck",
                            OrdnanceTargetCategory.Vehicle,
                            1,
                            0.15f,
                            2)
                    }),
                new BattalionDefinition(
                    BlueArmorBattalionId,
                    BlueCountryId,
                    "Blue Armor Battalion",
                    95,
                    55,
                    0.2f,
                    18f,
                    22f,
                    12,
                    20,
                    0.25f,
                    8f,
                    3,
                    1.2f,
                    2.5f,
                    groundTargetProfile: new List<GroundTargetProfileEntry>
                    {
                        new GroundTargetProfileEntry(
                            "tank",
                            OrdnanceTargetCategory.Vehicle,
                            3,
                            0.75f,
                            4),
                        new GroundTargetProfileEntry(
                            "support truck",
                            OrdnanceTargetCategory.Vehicle,
                            1,
                            0.2f,
                            2),
                        new GroundTargetProfileEntry(
                            "dismounted crew",
                            OrdnanceTargetCategory.Infantry,
                            1,
                            0.05f,
                            1)
                    }),
                new BattalionDefinition(
                    RedMotorRifleBattalionId,
                    RedCountryId,
                    "Red Motor Rifle Battalion",
                    100,
                    42,
                    0.22f,
                    13f,
                    4f,
                    16,
                    10,
                    0.75f,
                    6f,
                    2,
                    0.7f,
                    0.6f,
                    groundTargetProfile: new List<GroundTargetProfileEntry>
                    {
                        new GroundTargetProfileEntry(
                            "motor-rifle infantry",
                            OrdnanceTargetCategory.Infantry,
                            1,
                            0.55f,
                            3),
                        new GroundTargetProfileEntry(
                            "infantry fighting vehicle",
                            OrdnanceTargetCategory.Vehicle,
                            2,
                            0.3f,
                            3),
                        new GroundTargetProfileEntry(
                            "support truck",
                            OrdnanceTargetCategory.Vehicle,
                            1,
                            0.15f,
                            2)
                    }),
                new BattalionDefinition(
                    RedTankBattalionId,
                    RedCountryId,
                    "Red Tank Battalion",
                    90,
                    50,
                    0.18f,
                    16f,
                    20f,
                    11,
                    19,
                    0.3f,
                    7f,
                    3,
                    1.1f,
                    2.3f,
                    groundTargetProfile: new List<GroundTargetProfileEntry>
                    {
                        new GroundTargetProfileEntry(
                            "tank",
                            OrdnanceTargetCategory.Vehicle,
                            3,
                            0.7f,
                            4),
                        new GroundTargetProfileEntry(
                            "support truck",
                            OrdnanceTargetCategory.Vehicle,
                            1,
                            0.2f,
                            2),
                        new GroundTargetProfileEntry(
                            "dismounted crew",
                            OrdnanceTargetCategory.Infantry,
                            1,
                            0.1f,
                            1)
                    })
            };
        }

        private static List<DivisionTemplate> CreateDivisionTemplates()
        {
            return new List<DivisionTemplate>
            {
                new DivisionTemplate(
                    BlueArmoredDivisionTemplateId,
                    BlueCountryId,
                    "Blue Armored Division",
                    new List<DivisionTemplateBattalion>
                    {
                        new DivisionTemplateBattalion(BlueInfantryBattalionId, 5),
                        new DivisionTemplateBattalion(BlueArmorBattalionId, 2)
                    },
                    NatoUnitSymbol.Armor),
                new DivisionTemplate(
                    RedTankDivisionTemplateId,
                    RedCountryId,
                    "Red Tank Division",
                    new List<DivisionTemplateBattalion>
                    {
                        new DivisionTemplateBattalion(RedMotorRifleBattalionId, 3),
                        new DivisionTemplateBattalion(RedTankBattalionId, 2)
                    },
                    NatoUnitSymbol.MechanizedInfantry)
            };
        }
    }
}
