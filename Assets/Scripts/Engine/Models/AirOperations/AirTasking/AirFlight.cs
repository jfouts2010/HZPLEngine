using System;
using System.Collections.Generic;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AirFlight
    {
        public Guid FlightId = Guid.NewGuid();
        public Guid OwningPackageId;
        public Guid SquadronId;
        public AirMissionRequestType MissionType;
        public bool IsRequired = true;
        public List<Guid> AircraftIds = new List<Guid>();
        public AirTaskingLifecycleState LifecycleState = AirTaskingLifecycleState.Committed;
        public DateTime PlannedTakeoffTime;
        public DateTime EffectStart;
        public DateTime EffectEnd;
        public AirMissionArea MissionArea = new AirMissionArea();
        public int ProvidedSupportSlots;
        public List<AirSupportReservation> SupportReservations = new List<AirSupportReservation>();

        public bool IsTerminal =>
            LifecycleState == AirTaskingLifecycleState.Completed
            || LifecycleState == AirTaskingLifecycleState.Failed
            || LifecycleState == AirTaskingLifecycleState.Cancelled
            || LifecycleState == AirTaskingLifecycleState.Aborted;
    }
}
