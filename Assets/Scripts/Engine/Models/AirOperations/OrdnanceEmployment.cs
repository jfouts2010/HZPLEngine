using System;
using System.Collections.Generic;

namespace Models.Gameplay.Campaign
{
  

    public enum OrdnanceEmploymentSourceKind
    {
        AircraftFlight = 0,
        SamLauncher = 1
    }

    public enum OrdnanceEmploymentRecordStage
    {
        PreparationStarted = 0,
        PreparationAborted = 1,
        OrdnanceReleased = 2,
        EffectResolved = 3
    }

    public enum OrdnanceShotResult
    {
        Hit = 0,
        Miss = 1,
        Ineffective = 2,
        Damaged = 3,
        Defeated = 4
    }

    public enum OrdnanceEmploymentTargetKind
    {
        AirFlight = 0,
        AirDefenseComponent = 1
    }

    public enum OrdnanceDefeatReason
    {
        None = 0,
        KinematicRangeExceeded = 1,
        RadarLockBroken = 2
    }

    [Serializable]
    public sealed class OrdnanceShotDiagnostic
    {
        public int Sequence;
        public Guid SourceAircraftId;
        public Guid TargetAircraftId;
        public float Probability;
        public float Roll;
        public bool TargetWasAlreadyDamaged;
        public float DestructionProbability = -1f;
        public float DestructionRoll = -1f;
        public OrdnanceShotResult Result;
        public OrdnanceDefeatReason DefeatReason;
    }

    [Serializable]
    public sealed class OrdnanceLaunchDiagnostic
    {
        public int Sequence;
        public Guid SourceAircraftId;
        public Guid TargetAircraftId;
        public Guid OrdnanceTypeDefinitionId;
        public DateTime ReleasedAt;
    }

    [Serializable]
    public sealed class ActiveOrdnanceEmploymentPass
    {
        public Guid EmploymentPassId = Guid.NewGuid();
        public Guid SourceFlightId;
        public Guid PreferredSourceAircraftId;
        public Guid TargetFlightId;
        public OrdnanceEmploymentTargetKind TargetKind;
        public Guid TargetSiteId;
        public Guid TargetComponentId;
        public Guid OrdnanceTypeDefinitionId;
        public int PlannedQuantity;
        public DateTime PreparationStartedAt;
        public DateTime ReleaseAt;
        public float LaunchQuality;
    }

    [Serializable]
    public sealed class PendingOrdnanceEffect
    {
        public Guid PendingEffectId = Guid.NewGuid();
        public Guid EmploymentPassId;
        public OrdnanceEmploymentSourceKind SourceKind;
        public Guid SourceFlightId;
        public Guid SourceAircraftId;
        public Guid SourceSiteId;
        public Guid SourceComponentId;
        public Guid TargetFlightId;
        public OrdnanceEmploymentTargetKind TargetKind;
        public Guid TargetSiteId;
        public Guid TargetComponentId;
        public Guid OrdnanceTypeDefinitionId;
        public int Quantity;
        public float HitProbability;
        public DateTime ReleasedAt;
        public DateTime ResolveAt;
        public float ReleaseRangeKm;
        public float MaximumRangeKmAtRelease;
        public UnityEngine.Vector3 SourcePositionFeet;
        public UnityEngine.Vector3 TargetPositionFeet;
        public List<OrdnanceLaunchDiagnostic> Launches = new List<OrdnanceLaunchDiagnostic>();
        public OrdnanceGuidanceStage GuidanceStage = OrdnanceGuidanceStage.Midcourse;
        public DateTime AutonomousAt;
        public bool SupportRequired;
        public Guid SupportSourceFlightId;
        public Guid SupportSourceSiteId;
        public Guid SupportSourceComponentId;
        public DateTime LastGuidanceUpdateAt;
        public DateTime LastTargetEmissionAt;
        public float LaunchQuality = 1f;
        public float SupportSeconds;
        public float SupportedSeconds;
        public float DefensiveSeconds;
        public float PrincipalThreatBearingDegrees;
        public OrdnanceDefeatReason DefeatReason;

        public bool IsDefeated => DefeatReason != OrdnanceDefeatReason.None;
    }

    [Serializable]
    public sealed class OrdnanceEmploymentRecord
    {
        public Guid RecordId = Guid.NewGuid();
        public Guid EmploymentPassId;
        public Guid PendingEffectId;
        public OrdnanceEmploymentRecordStage Stage;
        public OrdnanceEmploymentSourceKind SourceKind;
        public Guid SourceFlightId;
        public Guid SourceAircraftId;
        public Guid SourceSiteId;
        public Guid SourceComponentId;
        public Guid TargetFlightId;
        public OrdnanceEmploymentTargetKind TargetKind;
        public Guid TargetSiteId;
        public Guid TargetComponentId;
        public Guid OrdnanceTypeDefinitionId;
        public int Quantity;
        public DateTime OccurredAt;
        public float HitProbability;
        public float ReleaseRangeKm;
        public UnityEngine.Vector3 SourcePositionFeet;
        public UnityEngine.Vector3 TargetPositionFeet;
        public List<OrdnanceLaunchDiagnostic> Launches = new List<OrdnanceLaunchDiagnostic>();
        public List<OrdnanceShotDiagnostic> Shots = new List<OrdnanceShotDiagnostic>();
        public string Detail = string.Empty;
    }
}
