using System;
using Models.Gameplay.Campaign;
using Models.Module;

namespace Engine.Service
{
    public static class AirFuelRules
    {
        public static float CalculateBurnFraction(
            AircraftTypeDefinition aircraftType,
            AirCombatIntent intent,
            double elapsedSeconds)
        {
            if (aircraftType == null
                || aircraftType.EnduranceHours <= 0f
                || elapsedSeconds <= 0d)
                return 0f;

            var multiplier = intent switch
            {
                AirCombatIntent.EngageTarget => 1.8f,
                AirCombatIntent.Defend => 2.5f,
                AirCombatIntent.Disengage => 1.4f,
                AirCombatIntent.Recover => 0.9f,
                _ => 1f
            };
            return (float)(elapsedSeconds
                           / (aircraftType.EnduranceHours * 3600d))
                   * multiplier;
        }

        public static double CalculateUsableSecondsUntilJoker(
            AircraftTypeDefinition aircraftType,
            AllianceAirDoctrine doctrine)
        {
            if (aircraftType == null
                || doctrine == null
                || aircraftType.EnduranceHours <= 0f)
                return 0d;

            return aircraftType.EnduranceHours
                   * 3600d
                   * Math.Max(0f, 1f - doctrine.JokerFuelFraction);
        }

        public static bool CanReceiveFuel(
            AirFlight receiver,
            AircraftTypeDefinition receiverType,
            AirFlight tanker,
            AircraftTypeDefinition tankerType,
            AirSupportReservation reservation,
            Guid consumingPackageId,
            DateTime currentTime)
        {
            if (receiver == null
                || receiverType == null
                || tanker == null
                || tankerType == null
                || reservation == null
                || !receiverType.CanReceiveAerialRefueling
                || tankerType.SupportCapability
                != AirSupportCapability.AerialRefueling
                || tanker.MissionType
                != AirMissionRequestType.ProvideAerialRefueling
                || receiver.LifecycleState != AirTaskingLifecycleState.Active
                || receiver.ExecutionPhase != FlightExecutionPhase.Executing
                || tanker.LifecycleState != AirTaskingLifecycleState.Active
                || tanker.ExecutionPhase != FlightExecutionPhase.Executing
                || receiver.TacticalState.FuelFraction >= 1f
                || tanker.ProvidedSupportSlots <= 0
                || reservation.SlotCount <= 0
                || reservation.SupportingFlightId != tanker.FlightId
                || reservation.ConsumingPackageId != consumingPackageId
                || reservation.StartTime > currentTime
                || reservation.EndTime <= currentTime)
                return false;

            var receiverArea = receiver.ActiveEffectArea;
            var tankerArea = tanker.ActiveEffectArea;
            return receiverArea != null
                   && tankerArea != null
                   && tankerArea.Contains(receiverArea.CenterTileId);
        }
    }
}
