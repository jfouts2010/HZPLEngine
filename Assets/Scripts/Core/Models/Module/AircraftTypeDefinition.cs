using System;
using System.Collections.Generic;

namespace Models.Module
{
    public enum AircraftPreferredAltitudeBand
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

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
        public float CruiseSpeedKph { get; }
        public float CombatSpeedKph { get; }
        public float RangeKm { get; }
        public float EnduranceHours { get; }
        public AircraftPreferredAltitudeBand PreferredAltitudeBand { get; }
        public float RadarQuality { get; }
        public float EcmQuality { get; }
        public float Survivability { get; }
        public float OrdnanceCapacity { get; }
        public List<Guid> CompatibleOrdnanceTypeDefinitionIds { get; }
        public AirSupportCapability SupportCapability { get; }
        public int SupportSlotCapacity { get; }
        public bool CanReceiveAerialRefueling { get; }

        public AircraftTypeDefinition(
            Guid aircraftTypeDefinitionId,
            string name,
            float cruiseSpeedKph,
            float combatSpeedKph,
            float rangeKm,
            float enduranceHours,
            AircraftPreferredAltitudeBand preferredAltitudeBand,
            float radarQuality,
            float ecmQuality,
            float survivability,
            float ordnanceCapacity = 0f,
            List<Guid> compatibleOrdnanceTypeDefinitionIds = null,
            string thirdPartyId = "",
            AirSupportCapability supportCapability = AirSupportCapability.None,
            int supportSlotCapacity = 0,
            bool canReceiveAerialRefueling = false)
        {
            if (aircraftTypeDefinitionId == Guid.Empty)
                throw new ArgumentException("Aircraft type definition id is required.", nameof(aircraftTypeDefinitionId));

            AircraftTypeDefinitionId = aircraftTypeDefinitionId;
            Name = string.IsNullOrWhiteSpace(name) ? aircraftTypeDefinitionId.ToString() : name.Trim();
            ThirdPartyId = thirdPartyId ?? string.Empty;
            CruiseSpeedKph = cruiseSpeedKph;
            CombatSpeedKph = combatSpeedKph;
            RangeKm = rangeKm;
            EnduranceHours = enduranceHours;
            PreferredAltitudeBand = preferredAltitudeBand;
            RadarQuality = radarQuality;
            EcmQuality = ecmQuality;
            Survivability = survivability;
            OrdnanceCapacity = Math.Max(0f, ordnanceCapacity);
            CompatibleOrdnanceTypeDefinitionIds = compatibleOrdnanceTypeDefinitionIds ?? new List<Guid>();
            SupportCapability = supportCapability;
            SupportSlotCapacity = supportCapability == AirSupportCapability.None
                ? 0
                : Math.Max(0, supportSlotCapacity);
            CanReceiveAerialRefueling = canReceiveAerialRefueling;
        }
    }
}
