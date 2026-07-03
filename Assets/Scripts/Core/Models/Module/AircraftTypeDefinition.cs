using System;
using System.Collections.Generic;

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
        public float NominalCruiseAltitudeFeet { get; }
        public float ServiceCeilingFeet { get; }
        public float RangeKm { get; }
        public float EnduranceHours { get; }
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
            ThirdPartyId = thirdPartyId;
            CruiseSpeedKnots = Math.Max(0f, cruiseSpeedKnots);
            CombatSpeedKnots = Math.Max(0f, combatSpeedKnots);
            ClimbRateFeetPerMinute = Math.Max(0f, climbRateFeetPerMinute);
            DescentRateFeetPerMinute = Math.Max(0f, descentRateFeetPerMinute);
            TurnRateDegreesPerSecond = Math.Max(0f, turnRateDegreesPerSecond);
            NominalCruiseAltitudeFeet = Math.Max(0f, nominalCruiseAltitudeFeet);
            ServiceCeilingFeet = Math.Max(NominalCruiseAltitudeFeet, serviceCeilingFeet);
            RangeKm = rangeKm;
            EnduranceHours = enduranceHours;
            RadarQuality = radarQuality;
            EcmQuality = ecmQuality;
            Survivability = survivability;
            OrdnanceCapacity = Math.Max(0f, ordnanceCapacity);
            CompatibleOrdnanceTypeDefinitionIds = compatibleOrdnanceTypeDefinitionIds;
            SupportCapability = supportCapability;
            SupportSlotCapacity = supportCapability == AirSupportCapability.None
                ? 0
                : Math.Max(0, supportSlotCapacity);
            CanReceiveAerialRefueling = canReceiveAerialRefueling;
        }
    }
}
