using System;

namespace Models.Module
{
    public enum AircraftPreferredAltitudeBand
    {
        Low = 0,
        Medium = 1,
        High = 2
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
            string thirdPartyId = "")
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
        }
    }
}
