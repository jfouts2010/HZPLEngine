using System;
using System.Collections.Generic;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class DecisionScoreFactor
    {
        public string Name = string.Empty;
        public float Value;

        public DecisionScoreFactor()
        {
        }

        public DecisionScoreFactor(string name, float value)
        {
            Name = name ?? string.Empty;
            Value = value;
        }
    }

    /// <summary>
    /// Serializable explanation for a utility-style choice. The total remains
    /// available for comparison while named factors explain where it came from.
    /// </summary>
    [Serializable]
    public sealed class DecisionScore
    {
        public float Total;
        public List<DecisionScoreFactor> Factors =
            new List<DecisionScoreFactor>();

        public DecisionScore()
        {
        }

        public DecisionScore(IEnumerable<DecisionScoreFactor> factors)
        {
            Factors = factors == null
                ? new List<DecisionScoreFactor>()
                : new List<DecisionScoreFactor>(factors);
            foreach (var factor in Factors)
                Total += factor?.Value ?? 0f;
        }
    }
}
