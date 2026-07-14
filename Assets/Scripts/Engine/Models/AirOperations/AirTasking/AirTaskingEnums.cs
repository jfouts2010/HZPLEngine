namespace Models.Gameplay.Campaign
{
    public enum AirMissionRequestType
    {
        BarrierCombatAirPatrol = 0,
        OffensiveCounterAirSweep = 1,
        ProvideAirborneC2 = 2,
        ProvideAerialRefueling = 3
    }

    public enum AirMissionRequestFulfillmentPattern
    {
        Sustained = 0,
        Discrete = 1
    }

    public enum AirMissionRequestState
    {
        Actionable = 0,
        PartiallyFulfilled = 1,
        InProgress = 2,
        Fulfilled = 3,
        Purged = 4
    }

    public enum AirTaskingLifecycleState
    {
        Committed = 0,
        Active = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4,
        Aborted = 5
    }
}
