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
        Ineffective = 2
    }

    [Serializable]
    public sealed class OrdnanceShotDiagnostic
    {
        public int Sequence;
        public Guid TargetAircraftId;
        public float Probability;
        public float Roll;
        public OrdnanceShotResult Result;
    }

    [Serializable]
    public sealed class ActiveOrdnanceEmploymentPass
    {
        public Guid EmploymentPassId = Guid.NewGuid();
        public Guid SourceFlightId;
        public Guid PreferredSourceAircraftId;
        public Guid TargetFlightId;
        public Guid OrdnanceTypeDefinitionId;
        public int PlannedQuantity;
        public DateTime PreparationStartedAt;
        public DateTime ReleaseAt;
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
        public Guid OrdnanceTypeDefinitionId;
        public int Quantity;
        public float HitProbability;
        public DateTime ReleasedAt;
        public DateTime ResolveAt;
        public float ReleaseRangeKm;
        public UnityEngine.Vector3 SourcePositionFeet;
        public UnityEngine.Vector3 TargetPositionFeet;
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
        public Guid OrdnanceTypeDefinitionId;
        public int Quantity;
        public DateTime OccurredAt;
        public float HitProbability;
        public float ReleaseRangeKm;
        public UnityEngine.Vector3 SourcePositionFeet;
        public UnityEngine.Vector3 TargetPositionFeet;
        public List<OrdnanceShotDiagnostic> Shots = new List<OrdnanceShotDiagnostic>();
        public string Detail = string.Empty;
    }
}
