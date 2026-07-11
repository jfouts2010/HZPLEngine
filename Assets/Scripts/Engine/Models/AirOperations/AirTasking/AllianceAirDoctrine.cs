using System;
using System.Collections.Generic;
using System.Linq;
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
        public int BaselineAirborneC2Slots = 4;
        public int BaselineAerialRefuelingSlots = 4;
        public float MinimumLaunchQuality = 0.35f;
        public float DesiredExpectedKillsPerHostileFlight = 1f;
        public int MaximumSimultaneousMissilesPerTargetAircraft = 1;
        public int MinimumAirToAirWeaponReserve = 1;
        public float MaximumPursuitMinutes = 12f;
        public int MaximumRecommits = 2;
        public float JokerFuelFraction = 0.35f;
        public float BingoFuelFraction = 0.2f;
        public Dictionary<AirMissionRequestType, float> PriorityWeights =
            CreateDefaultPriorityWeights();

        public float GetPriorityWeight(AirMissionRequestType requestType)
        {
            if (PriorityWeights.TryGetValue(requestType, out var weight))
                return Mathf.Max(0f, weight);

            return 1f;
        }

        public AllianceAirDoctrine Clone()
        {
            return new AllianceAirDoctrine
            {
                RiskTolerance = Mathf.Clamp01(RiskTolerance),
                DesiredAirCombatAdvantage = Mathf.Max(0.1f, DesiredAirCombatAdvantage),
                BaselineAirborneC2Slots = Math.Max(0, BaselineAirborneC2Slots),
                BaselineAerialRefuelingSlots = Math.Max(0, BaselineAerialRefuelingSlots),
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
                BingoFuelFraction = Mathf.Clamp01(Mathf.Min(BingoFuelFraction, JokerFuelFraction)),
                PriorityWeights = PriorityWeights.ToDictionary(
                    entry => entry.Key,
                    entry => Mathf.Max(0f, entry.Value))
            };
        }

        public static AllianceAirDoctrine CreateDefault()
        {
            return new AllianceAirDoctrine();
        }

        private static Dictionary<AirMissionRequestType, float> CreateDefaultPriorityWeights()
        {
            return new Dictionary<AirMissionRequestType, float>
            {
                { AirMissionRequestType.DefensiveCounterAirPatrol, 1f },
                { AirMissionRequestType.OffensiveCounterAirSweep, 0.85f },
                { AirMissionRequestType.ProvideAirborneC2, 0.7f },
                { AirMissionRequestType.ProvideAerialRefueling, 0.75f }
            };
        }
    }
}
