using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AirSupportReservation
    {
        public Guid SupportingFlightId;
        public Guid ConsumingPackageId;
        public int SlotCount;
        public DateTime StartTime;
        public DateTime EndTime;
    }
}
