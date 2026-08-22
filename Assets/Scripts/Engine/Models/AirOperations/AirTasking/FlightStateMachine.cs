using System;

namespace Models.Gameplay.Campaign
{
    internal enum FlightStateEvent
    {
        Takeoff = 0,
        EnterMission = 1,
        BeginRecovery = 2,
        BeginApproach = 3,
        RelocateMission = 4,
        CancelBeforeTakeoff = 5,
        AbortAirborne = 6,
        Land = 7,
        Fail = 8
    }

    internal readonly struct FlightStateTransition
    {
        public AirTaskingLifecycleState PreviousLifecycleState { get; }
        public FlightExecutionPhase PreviousExecutionPhase { get; }
        public AirTaskingLifecycleState LifecycleState { get; }
        public FlightExecutionPhase ExecutionPhase { get; }
        public FlightStateEvent Event { get; }

        public FlightStateTransition(
            AirTaskingLifecycleState previousLifecycleState,
            FlightExecutionPhase previousExecutionPhase,
            AirTaskingLifecycleState lifecycleState,
            FlightExecutionPhase executionPhase,
            FlightStateEvent stateEvent)
        {
            PreviousLifecycleState = previousLifecycleState;
            PreviousExecutionPhase = previousExecutionPhase;
            LifecycleState = lifecycleState;
            ExecutionPhase = executionPhase;
            Event = stateEvent;
        }
    }

    /// <summary>
    /// Owns the valid relationship between a flight's commitment outcome and
    /// its physical execution phase. Mission and tactical behavior may request
    /// transitions, but they do not decide the resulting state combination.
    /// </summary>
    internal static class FlightStateMachine
    {
        public static bool TryResolve(
            AirTaskingLifecycleState lifecycleState,
            FlightExecutionPhase executionPhase,
            FlightStateEvent stateEvent,
            bool missionAchieved,
            out FlightStateTransition transition)
        {
            var nextLifecycle = lifecycleState;
            var nextExecution = executionPhase;
            var valid = false;

            switch (stateEvent)
            {
                case FlightStateEvent.Takeoff:
                    valid = lifecycleState == AirTaskingLifecycleState.Committed
                            && executionPhase
                            == FlightExecutionPhase.AwaitingTakeoff;
                    nextLifecycle = AirTaskingLifecycleState.Active;
                    nextExecution = FlightExecutionPhase.Outbound;
                    break;

                case FlightStateEvent.EnterMission:
                    valid = lifecycleState == AirTaskingLifecycleState.Active
                            && IsAirborne(executionPhase)
                            && executionPhase != FlightExecutionPhase.Returning
                            && executionPhase != FlightExecutionPhase.Landing;
                    nextExecution = FlightExecutionPhase.Executing;
                    break;

                case FlightStateEvent.BeginRecovery:
                    valid = IsActiveOrAborted(lifecycleState)
                            && IsAirborne(executionPhase);
                    nextExecution = FlightExecutionPhase.Returning;
                    break;

                case FlightStateEvent.BeginApproach:
                    valid = IsActiveOrAborted(lifecycleState)
                            && IsAirborne(executionPhase);
                    nextExecution = FlightExecutionPhase.Landing;
                    break;

                case FlightStateEvent.RelocateMission:
                    valid = lifecycleState == AirTaskingLifecycleState.Active
                            && IsAirborne(executionPhase)
                            && executionPhase != FlightExecutionPhase.Returning
                            && executionPhase != FlightExecutionPhase.Landing;
                    nextExecution = FlightExecutionPhase.Outbound;
                    break;

                case FlightStateEvent.CancelBeforeTakeoff:
                    valid = executionPhase
                            == FlightExecutionPhase.AwaitingTakeoff
                            && !IsTerminal(lifecycleState);
                    nextLifecycle = AirTaskingLifecycleState.Cancelled;
                    nextExecution = FlightExecutionPhase.Ended;
                    break;

                case FlightStateEvent.AbortAirborne:
                    valid = IsAirborne(executionPhase)
                            && lifecycleState
                            != AirTaskingLifecycleState.Completed
                            && lifecycleState
                            != AirTaskingLifecycleState.Failed
                            && lifecycleState
                            != AirTaskingLifecycleState.Cancelled;
                    nextLifecycle = AirTaskingLifecycleState.Aborted;
                    if (executionPhase != FlightExecutionPhase.Returning
                        && executionPhase != FlightExecutionPhase.Landing)
                    {
                        nextExecution = FlightExecutionPhase.Returning;
                    }
                    break;

                case FlightStateEvent.Land:
                    valid = IsAirborne(executionPhase)
                            && IsActiveOrAborted(lifecycleState);
                    nextLifecycle = lifecycleState
                                    == AirTaskingLifecycleState.Aborted
                        ? AirTaskingLifecycleState.Aborted
                        : missionAchieved
                            ? AirTaskingLifecycleState.Completed
                            : AirTaskingLifecycleState.Failed;
                    nextExecution = FlightExecutionPhase.Ended;
                    break;

                case FlightStateEvent.Fail:
                    valid = true;
                    nextLifecycle = AirTaskingLifecycleState.Failed;
                    nextExecution = FlightExecutionPhase.Ended;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(stateEvent),
                        stateEvent,
                        null);
            }

            transition = valid
                ? new FlightStateTransition(
                    lifecycleState,
                    executionPhase,
                    nextLifecycle,
                    nextExecution,
                    stateEvent)
                : default;
            return valid;
        }

        private static bool IsAirborne(FlightExecutionPhase phase)
        {
            return phase == FlightExecutionPhase.Outbound
                   || phase == FlightExecutionPhase.Executing
                   || phase == FlightExecutionPhase.Returning
                   || phase == FlightExecutionPhase.Landing;
        }

        private static bool IsActiveOrAborted(
            AirTaskingLifecycleState lifecycleState)
        {
            return lifecycleState == AirTaskingLifecycleState.Active
                   || lifecycleState == AirTaskingLifecycleState.Aborted;
        }

        private static bool IsTerminal(AirTaskingLifecycleState lifecycleState)
        {
            return lifecycleState == AirTaskingLifecycleState.Completed
                   || lifecycleState == AirTaskingLifecycleState.Failed
                   || lifecycleState == AirTaskingLifecycleState.Cancelled
                   || lifecycleState == AirTaskingLifecycleState.Aborted;
        }
    }
}
