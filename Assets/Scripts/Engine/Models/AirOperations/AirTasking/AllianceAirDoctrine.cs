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
        public Dictionary<AirMissionRequestType, float> PriorityWeights =
            CreateDefaultPriorityWeights();

        public float GetPriorityWeight(AirMissionRequestType requestType)
        {
            if (PriorityWeights != null
                && PriorityWeights.TryGetValue(requestType, out var weight))
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
                PriorityWeights = PriorityWeights == null
                    ? CreateDefaultPriorityWeights()
                    : PriorityWeights.ToDictionary(entry => entry.Key, entry => Mathf.Max(0f, entry.Value))
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
