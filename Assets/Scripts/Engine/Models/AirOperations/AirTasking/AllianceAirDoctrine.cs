using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AllianceAirDoctrine
    {
        public const float DefaultRiskTolerance = 0.5f;
        public const float DefaultDesiredAirCombatAdvantage = 1.25f;
        public float RiskTolerance = DefaultRiskTolerance;
        public float DesiredAirCombatAdvantage = DefaultDesiredAirCombatAdvantage;
        public float MinimumLaunchQuality = 0.35f;
        public float DesiredExpectedKillsPerHostileFlight = 1f;
        public int MaximumSimultaneousMissilesPerTargetAircraft = 1;
        public int MinimumAirToAirWeaponReserve = 1;
        public float MaximumPursuitMinutes = 12f;
        public int MaximumRecommits = 2;
        public float JokerFuelFraction = 0.35f;
        public float BingoFuelFraction = 0.2f;

        public AllianceAirDoctrine Clone()
        {
            return new AllianceAirDoctrine
            {
                RiskTolerance = Mathf.Clamp01(RiskTolerance),
                DesiredAirCombatAdvantage = Mathf.Max(0.1f, DesiredAirCombatAdvantage),
                MinimumLaunchQuality = Mathf.Clamp01(MinimumLaunchQuality),
                DesiredExpectedKillsPerHostileFlight = Mathf.Max(
                    0.1f,
                    DesiredExpectedKillsPerHostileFlight),
                MaximumSimultaneousMissilesPerTargetAircraft = Math.Max(
                    1,
                    MaximumSimultaneousMissilesPerTargetAircraft),
                MinimumAirToAirWeaponReserve = Math.Max(0, MinimumAirToAirWeaponReserve),
                MaximumPursuitMinutes = Mathf.Max(1f, MaximumPursuitMinutes),
                MaximumRecommits = Math.Max(0, MaximumRecommits),
                JokerFuelFraction = Mathf.Clamp01(JokerFuelFraction),
                BingoFuelFraction = Mathf.Clamp01(Mathf.Min(BingoFuelFraction, JokerFuelFraction))
            };
        }

        public static AllianceAirDoctrine CreateDefault()
        {
            return new AllianceAirDoctrine();
        }
    }
}
