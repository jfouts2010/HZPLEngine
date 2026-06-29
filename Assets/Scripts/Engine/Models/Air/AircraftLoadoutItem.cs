using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class AircraftLoadoutItem
    {
        public Guid OrdnanceTypeDefinitionId;
        public int Count;

        public AircraftLoadoutItem()
        {
        }

        public AircraftLoadoutItem(Guid ordnanceTypeDefinitionId, int count)
        {
            OrdnanceTypeDefinitionId = ordnanceTypeDefinitionId;
            Count = Math.Max(0, count);
        }
    }
}
