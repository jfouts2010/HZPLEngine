using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class AircraftLoadoutItem
    {
        public Guid AircraftLoadoutStationDefinitionId;
        public Guid AircraftCarriageConfigurationDefinitionId;
        public Guid OrdnanceTypeDefinitionId;
        public int Count;

        public AircraftLoadoutItem()
        {
        }

        public AircraftLoadoutItem(Guid ordnanceTypeDefinitionId, int count)
            : this(Guid.Empty, Guid.Empty, ordnanceTypeDefinitionId, count)
        {
        }

        public AircraftLoadoutItem(
            Guid aircraftLoadoutStationDefinitionId,
            Guid aircraftCarriageConfigurationDefinitionId,
            Guid ordnanceTypeDefinitionId,
            int count)
        {
            AircraftLoadoutStationDefinitionId =
                aircraftLoadoutStationDefinitionId;
            AircraftCarriageConfigurationDefinitionId =
                aircraftCarriageConfigurationDefinitionId;
            OrdnanceTypeDefinitionId = ordnanceTypeDefinitionId;
            Count = Math.Max(0, count);
        }
    }
}
