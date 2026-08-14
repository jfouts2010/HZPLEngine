using System;
using System.Collections.Generic;
using System.Linq;

namespace Models.Module
{
    public enum AirSupportCapability
    {
        None = 0,
        AirborneC2 = 1,
        AerialRefueling = 2
    }

    public sealed class AircraftTypeDefinition
    {
        public Guid AircraftTypeDefinitionId { get; }
        public string Name { get; }
        public string ThirdPartyId { get; }
        public float CruiseSpeedKnots { get; }
        public float CombatSpeedKnots { get; }
        public float ClimbRateFeetPerMinute { get; }
        public float DescentRateFeetPerMinute { get; }
        public float TurnRateDegreesPerSecond { get; }
        public float DefensiveTurnRateDegreesPerSecond { get; }
        public float NominalCruiseAltitudeFeet { get; }
        public float ServiceCeilingFeet { get; }
        public float RangeKm { get; }
        public float EnduranceHours { get; }
        public float RadarQuality { get; }
        public float RadarDetectability { get; }
        public float RadarDefense { get; }
        public float InfraredDefense { get; }
        public float GunDefense { get; }
        public float Survivability { get; }
        public float AirInterferenceCapability { get; }
        public float WvrCombatRating { get; }
        public float OrdnanceCapacity { get; }
        public IReadOnlyList<Guid> CompatibleOrdnanceTypeDefinitionIds { get; }
        public IReadOnlyList<AircraftLoadoutStationDefinition> LoadoutStations
        {
            get;
        }
        public IReadOnlyList<AircraftCarriageConfigurationDefinition>
            CarriageConfigurations { get; }
        public Guid InternalGunOrdnanceTypeDefinitionId { get; }
        public int InternalGunBurstCount { get; }
        public AirSupportCapability SupportCapability { get; }
        public int SupportSlotCapacity { get; }
        public bool CanReceiveAerialRefueling { get; }
        public float OrdnanceEmploymentEfficiency { get; }

        public AircraftTypeDefinition(
            Guid aircraftTypeDefinitionId,
            string name,
            float cruiseSpeedKnots,
            float combatSpeedKnots,
            float climbRateFeetPerMinute,
            float descentRateFeetPerMinute,
            float turnRateDegreesPerSecond,
            float nominalCruiseAltitudeFeet,
            float serviceCeilingFeet,
            float rangeKm,
            float enduranceHours,
            float radarQuality,
            float radarDetectability,
            float radarDefense,
            float infraredDefense,
            float gunDefense,
            float survivability,
            float ordnanceCapacity = 0f,
            string thirdPartyId = "",
            AirSupportCapability supportCapability = AirSupportCapability.None,
            int supportSlotCapacity = 0,
            bool canReceiveAerialRefueling = false,
            float ordnanceEmploymentEfficiency = 1f,
            float airInterferenceCapability = 0f,
            Guid internalGunOrdnanceTypeDefinitionId = default,
            int internalGunBurstCount = 0,
            float wvrCombatRating = 0.5f,
            float defensiveTurnRateDegreesPerSecond = 0f,
            IReadOnlyList<AircraftLoadoutStationDefinition> loadoutStations = null,
            IReadOnlyList<AircraftCarriageConfigurationDefinition>
                carriageConfigurations = null)
        {
            if (aircraftTypeDefinitionId == Guid.Empty)
                throw new ArgumentException("Aircraft type definition id is required.",
                    nameof(aircraftTypeDefinitionId));

            AircraftTypeDefinitionId = aircraftTypeDefinitionId;
            Name = string.IsNullOrWhiteSpace(name) ? aircraftTypeDefinitionId.ToString() : name.Trim();
            ThirdPartyId = thirdPartyId;
            CruiseSpeedKnots = Math.Max(0f, cruiseSpeedKnots);
            CombatSpeedKnots = Math.Max(0f, combatSpeedKnots);
            ClimbRateFeetPerMinute = Math.Max(0f, climbRateFeetPerMinute);
            DescentRateFeetPerMinute = Math.Max(0f, descentRateFeetPerMinute);
            TurnRateDegreesPerSecond = Math.Max(0f, turnRateDegreesPerSecond);
            DefensiveTurnRateDegreesPerSecond =
                defensiveTurnRateDegreesPerSecond <= 0f
                    ? TurnRateDegreesPerSecond
                    : Math.Max(
                        TurnRateDegreesPerSecond,
                        defensiveTurnRateDegreesPerSecond);
            NominalCruiseAltitudeFeet = Math.Max(0f, nominalCruiseAltitudeFeet);
            ServiceCeilingFeet = Math.Max(NominalCruiseAltitudeFeet, serviceCeilingFeet);
            RangeKm = rangeKm;
            EnduranceHours = enduranceHours;
            RadarQuality = Math.Max(0f, Math.Min(1f, radarQuality));
            RadarDetectability = Math.Max(0f, Math.Min(1f, radarDetectability));
            RadarDefense = Math.Max(0f, Math.Min(1f, radarDefense));
            InfraredDefense = Math.Max(0f, Math.Min(1f, infraredDefense));
            GunDefense = Math.Max(0f, Math.Min(1f, gunDefense));
            Survivability = Math.Max(0f, Math.Min(1f, survivability));
            AirInterferenceCapability = Math.Max(0f, airInterferenceCapability);
            WvrCombatRating = Math.Max(0f, Math.Min(1f, wvrCombatRating));
            OrdnanceCapacity = Math.Max(0f, ordnanceCapacity);
            LoadoutStations = (loadoutStations
                               ?? Array.Empty<AircraftLoadoutStationDefinition>())
                .ToList();
            CarriageConfigurations = (carriageConfigurations
                                      ?? Array.Empty<
                                          AircraftCarriageConfigurationDefinition>())
                .ToList();
            ValidateLoadoutDefinitions();
            InternalGunOrdnanceTypeDefinitionId =
                internalGunOrdnanceTypeDefinitionId;
            InternalGunBurstCount =
                internalGunOrdnanceTypeDefinitionId == Guid.Empty
                    ? 0
                    : Math.Max(0, internalGunBurstCount);
            var compatibleOrdnance = CarriageConfigurations
                .SelectMany(configuration => configuration.Contents)
                .Select(content => content.OrdnanceTypeDefinitionId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (InternalGunOrdnanceTypeDefinitionId != Guid.Empty
                && !compatibleOrdnance.Contains(
                    InternalGunOrdnanceTypeDefinitionId))
                compatibleOrdnance.Add(InternalGunOrdnanceTypeDefinitionId);
            CompatibleOrdnanceTypeDefinitionIds = compatibleOrdnance;
            SupportCapability = supportCapability;
            SupportSlotCapacity = supportCapability == AirSupportCapability.None
                ? 0
                : Math.Max(0, supportSlotCapacity);
            CanReceiveAerialRefueling = canReceiveAerialRefueling;
            OrdnanceEmploymentEfficiency = Math.Max(0.01f, ordnanceEmploymentEfficiency);
        }

        private void ValidateLoadoutDefinitions()
        {
            var configurationIds = CarriageConfigurations
                .Select(configuration =>
                    configuration.AircraftCarriageConfigurationDefinitionId)
                .ToList();
            if (configurationIds.Distinct().Count() != configurationIds.Count)
            {
                throw new ArgumentException(
                    "Aircraft carriage configuration ids must be unique.",
                    nameof(CarriageConfigurations));
            }

            var stationIds = LoadoutStations
                .Select(station => station.AircraftLoadoutStationDefinitionId)
                .ToList();
            if (stationIds.Distinct().Count() != stationIds.Count)
            {
                throw new ArgumentException(
                    "Aircraft loadout station ids must be unique.",
                    nameof(LoadoutStations));
            }

            var knownConfigurations = configurationIds.ToHashSet();
            var knownStations = stationIds.ToHashSet();
            var stationsById = LoadoutStations.ToDictionary(
                station => station.AircraftLoadoutStationDefinitionId);
            foreach (var station in LoadoutStations)
            {
                if (station.CompatibleCarriageConfigurationDefinitionIds.Any(
                        id => !knownConfigurations.Contains(id)))
                {
                    throw new ArgumentException(
                        "An aircraft loadout station references an unknown carriage configuration.",
                        nameof(LoadoutStations));
                }
                if (station.MirrorStationDefinitionId != Guid.Empty
                    && (!knownStations.Contains(station.MirrorStationDefinitionId)
                        || station.MirrorStationDefinitionId
                        == station.AircraftLoadoutStationDefinitionId
                        || stationsById[station.MirrorStationDefinitionId]
                            .MirrorStationDefinitionId
                        != station.AircraftLoadoutStationDefinitionId))
                {
                    throw new ArgumentException(
                        "An aircraft loadout station has an invalid mirror station.",
                        nameof(LoadoutStations));
                }
            }

            var referencedConfigurations = LoadoutStations
                .SelectMany(station => station
                    .CompatibleCarriageConfigurationDefinitionIds)
                .ToHashSet();
            if (configurationIds.Any(id =>
                    !referencedConfigurations.Contains(id)))
            {
                throw new ArgumentException(
                    "Every aircraft carriage configuration must be legal on at least one loadout station.",
                    nameof(CarriageConfigurations));
            }

            var mappedStationIds = LoadoutStations
                .Where(station => !string.IsNullOrWhiteSpace(
                    station.ThirdPartyId))
                .Select(station => station.ThirdPartyId)
                .ToList();
            if (mappedStationIds.Distinct(StringComparer.Ordinal).Count()
                != mappedStationIds.Count)
            {
                throw new ArgumentException(
                    "Aircraft loadout station third-party ids must be unique.",
                    nameof(LoadoutStations));
            }
        }

        public float GetDefenseAgainst(OrdnanceTypeDefinition weapon)
        {
            if (weapon == null)
                return 0f;
            if (weapon.EmploymentCategory == OrdnanceEmploymentCategory.Gun)
                return GunDefense;

            return weapon.GuidanceMode switch
            {
                OrdnanceGuidanceMode.Infrared => InfraredDefense,
                OrdnanceGuidanceMode.Radar => RadarDefense,
                OrdnanceGuidanceMode.ActiveRadar => RadarDefense,
                OrdnanceGuidanceMode.SemiActiveRadar => RadarDefense,
                _ => 0f
            };
        }
    }
}
