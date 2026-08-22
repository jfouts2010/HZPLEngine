using System;
using Models.Gameplay.Campaign;

namespace Engine.Models
{
    /// <summary>
    /// Advances one package-level mission behavior without owning navigation,
    /// tactical arbitration, or physical movement.
    /// </summary>
    internal interface IFlightMissionBehavior
    {
        AirOperationType OperationType { get; }
        void Process(DateTime currentTime);
    }

    internal abstract class FlightMissionBehavior
        : IFlightMissionBehavior
    {
        private readonly Action<DateTime> process;

        public AirOperationType OperationType { get; }

        protected FlightMissionBehavior(
            AirOperationType operationType,
            Action<DateTime> process)
        {
            OperationType = operationType;
            this.process = process
                           ?? throw new ArgumentNullException(nameof(process));
        }

        public void Process(DateTime currentTime)
        {
            process(currentTime);
        }
    }

    internal sealed class DeadFlightMissionBehavior
        : FlightMissionBehavior
    {
        public DeadFlightMissionBehavior(Action<DateTime> process)
            : base(AirOperationType.Dead, process)
        {
        }
    }

    internal sealed class StrikeFlightMissionBehavior
        : FlightMissionBehavior
    {
        public StrikeFlightMissionBehavior(Action<DateTime> process)
            : base(AirOperationType.Strike, process)
        {
        }
    }
}
