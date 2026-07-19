using System;
using System.Collections.Generic;

namespace Models.Gameplay.Campaign
{
    public enum WvrAdvantageLevel
    {
        Neutral = 0,
        Favorable = 1,
        Dominant = 2
    }

    [Serializable]
    public sealed class WvrEngagement
    {
        public Guid EngagementId = Guid.NewGuid();
        public List<Guid> BlueFlightIds = new List<Guid>();
        public List<Guid> RedFlightIds = new List<Guid>();
        public DateTime StartedAt;
        public DateTime NextRoundAt;
        public int RoundNumber;
        public Alliance AdvantageAlliance = Alliance.Neutral;
        public WvrAdvantageLevel AdvantageLevel = WvrAdvantageLevel.Neutral;
        public Guid AdvantageSourceFlightId;
        public Guid PreferredTargetFlightId;
        public bool OpeningTargetWasUnaware;
        public bool ForcedOpportunityPending;
    }

    [Serializable]
    public sealed class WvrDisengagementDiagnostic
    {
        public Guid FlightId;
        public bool Damaged;
        public float EffectiveWvrRating;
        public float EnemyAverageWvrRating;
        public float SpeedRatio;
        public int CoveringFlightCount;
        public float CoverBonus;
        public float ExternalPressureBonus;
        public float AdvantageModifier;
        public float Probability;
        public float Roll;
        public bool Succeeded;
    }

    [Serializable]
    public sealed class WvrAttackDiagnostic
    {
        public Guid SourceFlightId;
        public Guid TargetFlightId;
        public Guid OrdnanceTypeDefinitionId;
        public WvrAdvantageLevel Advantage;
        public bool TargetAware;
        public float HitProbability;
        public bool Released;
    }

    [Serializable]
    public sealed class WvrRoundDiagnostic
    {
        public Guid EngagementId;
        public int RoundNumber;
        public DateTime ResolvedAt;
        public List<Guid> BlueFlightIds = new List<Guid>();
        public List<Guid> RedFlightIds = new List<Guid>();
        public Alliance StartingAdvantageAlliance = Alliance.Neutral;
        public WvrAdvantageLevel StartingAdvantageLevel =
            WvrAdvantageLevel.Neutral;
        public Alliance EndingAdvantageAlliance = Alliance.Neutral;
        public WvrAdvantageLevel EndingAdvantageLevel =
            WvrAdvantageLevel.Neutral;
        public int BlueAircraftCount;
        public int RedAircraftCount;
        public int BlueDamagedAircraftCount;
        public int RedDamagedAircraftCount;
        public float BlueEffectiveCombatWeight;
        public float RedEffectiveCombatWeight;
        public float BlueEffectiveWvrRating;
        public float RedEffectiveWvrRating;
        public bool UsedControlContest;
        public float BlueControlScore;
        public float RedControlScore;
        public string OpportunityReason = string.Empty;
        public List<WvrDisengagementDiagnostic> Disengagements =
            new List<WvrDisengagementDiagnostic>();
        public List<WvrAttackDiagnostic> Attacks =
            new List<WvrAttackDiagnostic>();
        public string Outcome = string.Empty;
    }
}
