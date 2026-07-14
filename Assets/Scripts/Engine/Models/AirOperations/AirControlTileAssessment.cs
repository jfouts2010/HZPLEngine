using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AirControlTileAssessment
    {
        private const float CombatPowerForHalfPresence = 2f;
        private const float MaximumConvertiblePresence = 1f - 0.000001f;
        private const float ControlBaselinePower = 0.25f;

        public Vector3Int TileId;

        public float FriendlyCombatPower;
        public float HostileCombatPower;
        [Range(0f, 1f)] public float FriendlyAirActivity;
        [Range(0f, 1f)] public float HostileAirActivity;

        public float FriendlyCombatPresence => CalculateCombatPresence(
            FriendlyCombatPower);
        public float HostileCombatPresence => CalculateCombatPresence(
            HostileCombatPower);
        [Range(-1f, 1f)] public float AirControlAdvantage
        {
            get
            {
                var friendlyPower = Mathf.Max(0f, FriendlyCombatPower);
                var hostilePower = Mathf.Max(0f, HostileCombatPower);
                return Mathf.Clamp(
                    (friendlyPower - hostilePower)
                    / (friendlyPower + hostilePower + 2f * ControlBaselinePower),
                    -1f,
                    1f);
            }
        }
        [Range(0f, 1f)] public float AirControl =>
            Mathf.Clamp01(AirControlAdvantage * 0.5f + 0.5f);
        public float AirActivity => Mathf.Clamp01(
            Mathf.Max(0f, FriendlyAirActivity) + Mathf.Max(0f, HostileAirActivity));

        public AirControlTileAssessment()
        {
        }

        public AirControlTileAssessment(Vector3Int tileId)
        {
            TileId = tileId;
        }

        internal static float CalculateCombatPresence(float combatPower)
        {
            return Mathf.Clamp01(
                1f - Mathf.Pow(
                    0.5f,
                    Mathf.Max(0f, combatPower) / CombatPowerForHalfPresence));
        }

        internal static float CalculateCombatPower(float combatPresence)
        {
            var presence = Mathf.Clamp(
                combatPresence,
                0f,
                MaximumConvertiblePresence);
            return CombatPowerForHalfPresence
                   * Mathf.Log(1f - presence, 0.5f);
        }
    }
}
