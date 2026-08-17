using System;
using System.Collections.Generic;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    /// <summary>
    /// An explicit, authored instruction for materializing one air package.
    /// It contains decisions; the package builder only validates and realizes them.
    /// </summary>
    [Serializable]
    public sealed class AirPackagePlan
    {
        public Guid PlanId;
        public Alliance Alliance;
        public AirOperationType OperationType;
        public DateTime AvailableAt;
        public DateTime EffectStart;
        public DateTime EffectEnd;
        public AirMissionArea OperationArea = new AirMissionArea();
        public bool UseRendezvous;
        public Vector3 RendezvousPositionFeet;
        public BarcapBarrierPlan BarcapBarrier;
        public DeadMissionPlan DeadPlan;
        public StrikeMissionPlan StrikePlan;
        public string Rationale = string.Empty;
        public List<AirFlightPlan> Flights = new List<AirFlightPlan>();
    }

    /// <summary>
    /// The fully decided composition and route geometry for one flight in a package.
    /// Empty AircraftIds means take the first ready aircraft in squadron order.
    /// Empty Loadout means use the deterministic default for tasks that define one.
    /// </summary>
    [Serializable]
    public sealed class AirFlightPlan
    {
        public Guid FlightPlanId;
        public Guid SquadronId;
        public AirFlightTaskType TaskType;
        public StrikeAssignment StrikeAssignment;
        public int AircraftCount = 1;
        public bool IsRequired = true;
        public Guid RecoveryAirportBuildingId;
        public List<Guid> AircraftIds = new List<Guid>();
        public List<Guid> ProtectedFlightPlanIds = new List<Guid>();
        public List<AircraftLoadoutItem> Loadout =
            new List<AircraftLoadoutItem>();
        public List<Vector3> IngressWaypointsFeet = new List<Vector3>();
        public List<Vector3> MissionWaypointsFeet = new List<Vector3>();
        public List<Vector3> EgressWaypointsFeet = new List<Vector3>();
        public BarcapStationCoverage BarcapCoverage;
    }
}
