namespace Models.Gameplay.Campaign
{
    public enum AirOperationType
    {
        Barcap = 0,
        OcaSweep = 1,
        AirborneC2 = 2,
        AerialRefueling = 3,
        Dead = 4,
        Strike = 5
    }

    public enum AirFlightTaskType
    {
        Barcap = 0,
        OcaSweep = 1,
        AirborneC2 = 2,
        AerialRefueling = 3,
        DeadAttack = 4,
        Strike = 5,
        FighterEscort = 6,
        SeadEscort = 7
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
