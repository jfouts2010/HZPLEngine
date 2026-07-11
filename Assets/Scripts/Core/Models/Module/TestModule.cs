using System;
using System.Collections.Generic;

namespace Models.Module
{
    public static class TestModule
    {
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

        public static readonly Guid Aim120OrdnanceTypeId = Guid.Parse("7486758d-565b-4a19-8d26-29dd717b0e22");
        public static readonly Guid Aim9OrdnanceTypeId = Guid.Parse("5e7975e2-09a8-46f4-bb2d-05de209b60b8");
        public static readonly Guid Agm88OrdnanceTypeId = Guid.Parse("3160216b-64bf-45b8-b245-c7ee2303864e");
        public static readonly Guid Gbu38OrdnanceTypeId = Guid.Parse("37f8f135-55a8-42a1-9478-f583508e344c");
        public static readonly Guid Agm65OrdnanceTypeId = Guid.Parse("24d59f63-9714-4306-9ebb-f79075df0909");
        public static readonly Guid R27OrdnanceTypeId = Guid.Parse("df62234e-e894-4d4e-8d67-e4bc6fe68405");
        public static readonly Guid R73OrdnanceTypeId = Guid.Parse("fc9d6932-ef37-4fc3-b024-12fc1e2d9f1c");
        public static readonly Guid Sa2InterceptorOrdnanceTypeId =
            Guid.Parse("b8afb031-30a8-4de0-90a7-0cc958d8813f");
        public static readonly Guid OsaInterceptorOrdnanceTypeId =
            Guid.Parse("713acf12-4569-43cb-bd66-23bbbda24168");

        public static readonly Guid FanSongComponentId = Guid.Parse("0ec0a8c9-dc38-461c-a3f8-1831afdf43ad");
        public static readonly Guid Sa2LauncherComponentId = Guid.Parse("1ecf62f4-3034-4be3-86c3-0e8d65ecef6a");
        public static readonly Guid SamCommandPostComponentId = Guid.Parse("0ef1697d-f3fb-49fa-8f54-bac90107b552");
        public static readonly Guid OsaRadarComponentId = Guid.Parse("9daab0e8-3d79-49f3-91b5-feb343f09fac");
        public static readonly Guid OsaLauncherComponentId = Guid.Parse("f7f2f3d3-0e2b-4f6f-9b7e-617b1ec23cb3");
        public static readonly Guid OsaCommandComponentId = Guid.Parse("1ad7d010-b81c-48af-9844-854a864385e6");
        public static readonly Guid Sa2SiteTemplateId = Guid.Parse("9a408a92-9a60-4fb6-bd38-18cb6f2771a5");
        public static readonly Guid OsaSiteTemplateId = Guid.Parse("710226da-3875-4312-81ea-29606aca76c6");

        public static readonly Guid BlueArmoredDivisionTemplateId = Guid.Parse("4153b384-6e76-42df-a1e5-d54582022bee");
        public static readonly Guid RedTankDivisionTemplateId = Guid.Parse("2d4ecc7e-8285-4b8b-9281-00cccbf5d2e8");

        public static ModuleDefinition GetTestModule()
        {
            return new ModuleDefinition(
                Id,
                "Standalone Test Module",
                "Standalone",
                "HZPL Engine",
                new NoOpSimAdapter(),
                CreateCountries(),
                CreateAircraftTypeDefinitions(),
                CreateOrdnanceTypeDefinitions(),
                CreateSamComponentDefinitions(),
                CreateSamSiteTemplates(),
                CreateBattalionDefinitions(),
                CreateDivisionTemplates());
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

        private static List<AircraftTypeDefinition> CreateAircraftTypeDefinitions()
        {
            return new List<AircraftTypeDefinition>
            {
                new AircraftTypeDefinition(
                    F16AircraftTypeId,
                    "F-16C Fighting Falcon",
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
                    ecmQuality: 0.55f,
                    survivability: 0.65f,
                    ordnanceCapacity: 12f,
                    compatibleOrdnanceTypeDefinitionIds: new List<Guid>
                    {
                        Aim120OrdnanceTypeId,
                        Aim9OrdnanceTypeId,
                        Agm88OrdnanceTypeId,
                        Gbu38OrdnanceTypeId,
                        Agm65OrdnanceTypeId
                    },
                    canReceiveAerialRefueling: true),
                new AircraftTypeDefinition(
                    Mig29AircraftTypeId,
                    "MiG-29 Fulcrum",
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
                    ecmQuality: 0.45f,
                    survivability: 0.7f,
                    ordnanceCapacity: 10f,
                    compatibleOrdnanceTypeDefinitionIds: new List<Guid>
                    {
                        R27OrdnanceTypeId,
                        R73OrdnanceTypeId
                    },
                    canReceiveAerialRefueling: true),
                new AircraftTypeDefinition(
                    E3AircraftTypeId,
                    "E-3 Sentry",
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
                    ecmQuality: 0.6f,
                    survivability: 0.35f,
                    supportCapability: AirSupportCapability.AirborneC2,
                    supportSlotCapacity: 12),
                new AircraftTypeDefinition(
                    Kc135AircraftTypeId,
                    "KC-135 Stratotanker",
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
                    ecmQuality: 0.35f,
                    survivability: 0.3f,
                    supportCapability: AirSupportCapability.AerialRefueling,
                    supportSlotCapacity: 8),
                new AircraftTypeDefinition(
                    A50AircraftTypeId,
                    "A-50 Mainstay",
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
                    ecmQuality: 0.5f,
                    survivability: 0.35f,
                    supportCapability: AirSupportCapability.AirborneC2,
                    supportSlotCapacity: 10),
                new AircraftTypeDefinition(
                    Il78AircraftTypeId,
                    "Il-78 Midas",
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
                    ecmQuality: 0.3f,
                    survivability: 0.3f,
                    supportCapability: AirSupportCapability.AerialRefueling,
                    supportSlotCapacity: 8)
            };
        }

        private static List<OrdnanceTypeDefinition> CreateOrdnanceTypeDefinitions()
        {
            return new List<OrdnanceTypeDefinition>
            {
                new OrdnanceTypeDefinition(
                    Aim120OrdnanceTypeId,
                    "AIM-120 AMRAAM",
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
                    countermeasureResistance: 0.7f,
                    terminalLethality: 0.82f),
                new OrdnanceTypeDefinition(
                    Aim9OrdnanceTypeId,
                    "AIM-9 Sidewinder",
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
                    countermeasureResistance: 0.55f,
                    terminalLethality: 0.75f),
                new OrdnanceTypeDefinition(
                    Agm88OrdnanceTypeId,
                    "AGM-88 HARM",
                    weight: 3f,
                    effectPower: 3,
                    effectivenessByTargetCategory: new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Radar, 1f },
                        { OrdnanceTargetCategory.Building, 0.15f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.AntiRadiation,
                    guidanceMode: OrdnanceGuidanceMode.AntiRadiation,
                    maximumRangeKm: 150f,
                    preparationSeconds: 45f,
                    effectSpeedKnots: 1800f),
                new OrdnanceTypeDefinition(
                    Gbu38OrdnanceTypeId,
                    "GBU-38 JDAM",
                    weight: 2f,
                    effectPower: 3,
                    effectivenessByTargetCategory: new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Vehicle, 0.55f },
                        { OrdnanceTargetCategory.Building, 0.8f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.AirToGroundPrecision,
                    guidanceMode: OrdnanceGuidanceMode.Gps,
                    maximumRangeKm: 28f,
                    preparationSeconds: 45f,
                    effectSpeedKnots: 550f),
                new OrdnanceTypeDefinition(
                    Agm65OrdnanceTypeId,
                    "AGM-65 Maverick",
                    weight: 2f,
                    effectPower: 3,
                    effectivenessByTargetCategory: new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Vehicle, 0.9f },
                        { OrdnanceTargetCategory.Building, 0.45f }
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
                    countermeasureResistance: 0.55f,
                    terminalLethality: 0.8f),
                new OrdnanceTypeDefinition(
                    R73OrdnanceTypeId,
                    "R-73 Archer",
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
                    countermeasureResistance: 0.6f,
                    terminalLethality: 0.74f),
                new OrdnanceTypeDefinition(
                    Sa2InterceptorOrdnanceTypeId,
                    "V-750 SAM",
                    weight: 0f,
                    effectPower: 3,
                    effectivenessByTargetCategory: new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Aircraft, 0.8f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.SurfaceToAir,
                    guidanceMode: OrdnanceGuidanceMode.Radar,
                    minimumRangeKm: 7f,
                    maximumRangeKm: 35f,
                    minimumTargetAltitudeFeet: 984f,
                    maximumTargetAltitudeFeet: 78740f,
                    effectSpeedKnots: 2000f),
                new OrdnanceTypeDefinition(
                    OsaInterceptorOrdnanceTypeId,
                    "9M33 SAM",
                    weight: 0f,
                    effectPower: 2,
                    effectivenessByTargetCategory: new Dictionary<OrdnanceTargetCategory, float>
                    {
                        { OrdnanceTargetCategory.Aircraft, 0.75f }
                    },
                    employmentCategory: OrdnanceEmploymentCategory.SurfaceToAir,
                    guidanceMode: OrdnanceGuidanceMode.Radar,
                    minimumRangeKm: 1.5f,
                    maximumRangeKm: 12f,
                    minimumTargetAltitudeFeet: 82f,
                    maximumTargetAltitudeFeet: 16404f,
                    effectSpeedKnots: 1800f)
            };
        }

        private static List<AirDefenseComponentDefinition> CreateSamComponentDefinitions()
        {
            return new List<AirDefenseComponentDefinition>
            {
                new RadarAirDefenseComponentDefinition(
                    FanSongComponentId,
                    "Fan Song radar component",
                    OrdnanceTargetCategory.Radar,
                    targetToughness: 2,
                    detectionRangeKm: 75f,
                    maxAltitudeMeters: 24000f,
                    trackQuality: 0.75f,
                    providesWeaponQualityTrack: true),
                new LauncherAirDefenseComponentDefinition(
                    Sa2LauncherComponentId,
                    "SA-2 launcher rail",
                    OrdnanceTargetCategory.Building,
                    targetToughness: 2,
                    minEngagementRangeKm: 7f,
                    maxEngagementRangeKm: 35f,
                    minEngagementAltitudeMeters: 300f,
                    maxEngagementAltitudeMeters: 24000f,
                    readyRoundCapacity: 1,
                    reserveRoundCapacity: 1,
                    reloadMinutes: 30f,
                    surfaceToAirOrdnanceTypeDefinitionId: Sa2InterceptorOrdnanceTypeId),
                new CommandAirDefenseComponentDefinition(
                    SamCommandPostComponentId,
                    "SAM command post",
                    OrdnanceTargetCategory.Building,
                    targetToughness: 2),
                new RadarAirDefenseComponentDefinition(
                    OsaRadarComponentId,
                    "SA-8 Osa organic radar",
                    OrdnanceTargetCategory.Radar,
                    targetToughness: 2,
                    detectionRangeKm: 35f,
                    maxAltitudeMeters: 12000f,
                    trackQuality: 0.55f,
                    providesWeaponQualityTrack: true),
                new LauncherAirDefenseComponentDefinition(
                    OsaLauncherComponentId,
                    "SA-8 Osa launcher",
                    OrdnanceTargetCategory.Vehicle,
                    targetToughness: 2,
                    minEngagementRangeKm: 1.5f,
                    maxEngagementRangeKm: 12f,
                    minEngagementAltitudeMeters: 25f,
                    maxEngagementAltitudeMeters: 5000f,
                    readyRoundCapacity: 6,
                    reserveRoundCapacity: 0,
                    reloadMinutes: 0f,
                    surfaceToAirOrdnanceTypeDefinitionId: OsaInterceptorOrdnanceTypeId),
                new CommandAirDefenseComponentDefinition(
                    OsaCommandComponentId,
                    "SA-8 Osa command component",
                    OrdnanceTargetCategory.Vehicle,
                    targetToughness: 2)
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
                    0f),
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
                    2.5f),
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
                    0.6f),
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
                    2.3f)
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
