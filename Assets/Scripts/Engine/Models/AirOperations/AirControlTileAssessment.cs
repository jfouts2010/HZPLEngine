using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AirControlTileAssessment
    {
        private const float CombatPowerForHalfPresence = 2f;
        private const float MaximumConvertiblePresence = 1f - 0.000001f;

        public Vector3Int TileId;

        public float FriendlyCombatPower;
        public float HostileCombatPower;
        [Range(0f, 1f)] public float FriendlyAirActivity;
        [Range(0f, 1f)] public float HostileAirActivity;

        public float FriendlyCombatPresence => CalculateCombatPresence(
            FriendlyCombatPower);
        public float HostileCombatPresence => CalculateCombatPresence(
            HostileCombatPower);
        public float FriendlyAirInterference => Mathf.Clamp01(
            Mathf.Max(FriendlyCombatPresence, FriendlyAirActivity));
        public float HostileAirInterference => Mathf.Clamp01(
            Mathf.Max(HostileCombatPresence, HostileAirActivity));
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
