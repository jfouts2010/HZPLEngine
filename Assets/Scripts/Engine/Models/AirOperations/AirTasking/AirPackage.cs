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
        public bool HasRendezvous;
        public Vector3Int RendezvousTileId;
        public List<AirFlight> Flights = new List<AirFlight>();
        public List<Guid> SupportingFlightIds = new List<Guid>();
        public string Rationale = string.Empty;

        private IReadOnlyList<AirFlight> RequiredFlights
        {
            get
            {
                var flights = (Flights ?? new List<AirFlight>())
                    .Where(flight => flight != null)
                    .ToList();
                var required = flights.Where(flight => flight.IsRequired).ToList();
                return required.Count > 0 ? required : flights;
            }
        }

        public DateTime EarliestTakeoffTime =>
            RequiredFlights.Count == 0
                ? default
                : RequiredFlights.Min(flight => flight.PlannedTakeoffTime);
        public DateTime EffectStart =>
            RequiredFlights.Count == 0
                ? default
                : RequiredFlights.Max(flight => flight.EffectStart);
        public DateTime EffectEnd =>
            RequiredFlights.Count == 0
                ? default
                : RequiredFlights.Min(flight => flight.EffectEnd);
        internal DateTime SupportWindowEnd =>
            EffectEnd > EffectStart ? EffectEnd : EffectStart.AddTicks(1);

        public AirTaskingLifecycleState LifecycleState
        {
            get
            {
                var flights = (Flights ?? new List<AirFlight>())
                    .Where(flight => flight != null)
                    .ToList();
                if (flights.Count == 0)
                    return AirTaskingLifecycleState.Cancelled;
                if (flights.Any(flight => flight.LifecycleState == AirTaskingLifecycleState.Aborted))
                    return AirTaskingLifecycleState.Aborted;
                if (flights.Any(flight => flight.LifecycleState == AirTaskingLifecycleState.Active))
                    return AirTaskingLifecycleState.Active;
                if (flights.Any(flight =>
                        flight.LifecycleState == AirTaskingLifecycleState.Committed))
                    return AirTaskingLifecycleState.Committed;

                var requiredFlights = flights
                    .Where(flight => flight.IsRequired)
                    .ToList();
                var outcomeFlights = requiredFlights.Count > 0
                    ? requiredFlights
                    : flights;
                if (outcomeFlights.All(flight =>
                        flight.LifecycleState == AirTaskingLifecycleState.Completed))
                    return AirTaskingLifecycleState.Completed;
                if (outcomeFlights.Any(flight =>
                        flight.LifecycleState == AirTaskingLifecycleState.Failed))
                    return AirTaskingLifecycleState.Failed;
                if (outcomeFlights.All(flight =>
                        flight.LifecycleState == AirTaskingLifecycleState.Cancelled))
                    return AirTaskingLifecycleState.Cancelled;

                // A terminal mixture, such as completed and cancelled required
                // flights, did not achieve the package as committed.
                return AirTaskingLifecycleState.Failed;
            }
        }

        public bool IsTerminal =>
            LifecycleState == AirTaskingLifecycleState.Completed
            || LifecycleState == AirTaskingLifecycleState.Failed
            || LifecycleState == AirTaskingLifecycleState.Cancelled
            || LifecycleState == AirTaskingLifecycleState.Aborted;

        public bool HasPhysicallyEnded =>
            (Flights ?? new List<AirFlight>())
            .Where(flight => flight != null)
            .All(flight => flight.HasPhysicallyEnded);
    }
}
