using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AirPackage
    {
        public static readonly TimeSpan PreparationDelay = TimeSpan.FromMinutes(30);

        public Guid PackageId = Guid.NewGuid();
        public Guid MissionRequestId;
        public Alliance Alliance;
        public DateTime CreatedAt;
        public DateTime EarliestTakeoffTime;
        public DateTime EffectStart;
        public DateTime EffectEnd;
        public bool HasRendezvous;
        public Vector3Int RendezvousTileId;
        public List<AirFlight> Flights = new List<AirFlight>();
        public List<Guid> SupportingFlightIds = new List<Guid>();
        public string Rationale = string.Empty;

        public AirTaskingLifecycleState LifecycleState
        {
            get
            {
                var flights = Flights ?? new List<AirFlight>();
                if (flights.Count == 0)
                    return AirTaskingLifecycleState.Cancelled;
                if (flights.Any(flight => flight.LifecycleState == AirTaskingLifecycleState.Aborted))
                    return AirTaskingLifecycleState.Aborted;
                if (flights.Any(flight => flight.LifecycleState == AirTaskingLifecycleState.Active))
                    return AirTaskingLifecycleState.Active;
                if (flights.All(flight => flight.LifecycleState == AirTaskingLifecycleState.Completed))
                    return AirTaskingLifecycleState.Completed;
                if (flights.Any(flight => flight.LifecycleState == AirTaskingLifecycleState.Failed))
                    return AirTaskingLifecycleState.Failed;
                if (flights.All(flight => flight.LifecycleState == AirTaskingLifecycleState.Cancelled))
                    return AirTaskingLifecycleState.Cancelled;
                return AirTaskingLifecycleState.Committed;
            }
        }

        public bool IsTerminal =>
            LifecycleState == AirTaskingLifecycleState.Completed
            || LifecycleState == AirTaskingLifecycleState.Failed
            || LifecycleState == AirTaskingLifecycleState.Cancelled
            || LifecycleState == AirTaskingLifecycleState.Aborted;
    }
}
