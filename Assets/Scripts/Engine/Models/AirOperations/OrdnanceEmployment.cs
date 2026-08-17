using System;
using System.Collections.Generic;
using Models.Module;
using UnityEngine;

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
        AirDefenseComponent = 1,
        GroundTarget = 2
    }

    public enum GroundAttackTargetKind
    {
        None = 0,
        AirDefenseComponent = 1,
        Division = 2,
        Building = 3,
        GroundedAircraft = 4,
        TileInfrastructure = 5,
        AirportRunway = 6
    }

    public enum GroundAttackOpportunityQuality
    {
        None = 0,
        Fleeting = 1,
        Normal = 2,
        Excellent = 3
    }

    [Serializable]
    public sealed class GroundAttackTargetReference
    {
        public GroundAttackTargetKind Kind;
        public Guid EntityId;
        public Guid ParentEntityId;
        public Vector3Int TileId;
        public int SubtargetIndex = -1;

        public bool Matches(GroundAttackTargetReference other)
        {
            return other != null
                   && Kind == other.Kind
                   && EntityId == other.EntityId
                   && ParentEntityId == other.ParentEntityId
                   && TileId == other.TileId
                   && SubtargetIndex == other.SubtargetIndex;
        }

        public GroundAttackTargetReference Clone()
        {
            return new GroundAttackTargetReference
            {
                Kind = Kind,
                EntityId = EntityId,
                ParentEntityId = ParentEntityId,
                TileId = TileId,
                SubtargetIndex = SubtargetIndex
            };
        }
    }

    [Serializable]
    public sealed class GroundAttackOpportunityTarget
    {
        public GroundAttackTargetReference Target =
            new GroundAttackTargetReference();
        public OrdnanceTargetCategory TargetCategory;
        public int TargetToughness = 1;
        public float MissionPriority = 1f;
        public bool CanBePrimaryTarget = true;
        public bool CanReceiveSecondaryEffect = true;
        public int DamageSlotIndex = -1;
        public string Description = string.Empty;

        public GroundAttackOpportunityTarget Clone()
        {
            return new GroundAttackOpportunityTarget
            {
                Target = Target?.Clone() ?? new GroundAttackTargetReference(),
                TargetCategory = TargetCategory,
                TargetToughness = TargetToughness,
                MissionPriority = MissionPriority,
                CanBePrimaryTarget = CanBePrimaryTarget,
                CanReceiveSecondaryEffect = CanReceiveSecondaryEffect,
                DamageSlotIndex = DamageSlotIndex,
                Description = Description
            };
        }
    }

    [Serializable]
    public sealed class GroundAttackOpportunity
    {
        public Guid OpportunityId = Guid.NewGuid();
        public DateTime GeneratedAt;
        public GroundAttackOpportunityQuality Quality;
        public int MaximumReleases;
        public Vector3Int TargetTileId;
        public List<GroundAttackOpportunityTarget> Targets =
            new List<GroundAttackOpportunityTarget>();
        public string Description = string.Empty;

        public bool HasTargets => MaximumReleases > 0
                                  && Targets != null
                                  && Targets.Count > 0;
    }

    [Serializable]
    public sealed class GroundAttackPassPlan
    {
        public Guid OrdnanceTypeDefinitionId;
        public Vector3Int TargetTileId;
        public List<GroundAttackOpportunityTarget> PrimaryTargets =
            new List<GroundAttackOpportunityTarget>();
        public List<GroundAttackOpportunityTarget> OpportunityTargets =
            new List<GroundAttackOpportunityTarget>();
        public string OpportunityDescription = string.Empty;
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
        public GroundAttackTargetReference GroundTarget;
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
        public GroundAttackTargetReference GroundTarget;
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
        public Vector3Int GroundTargetTileId;
        public List<GroundAttackOpportunityTarget> GroundPrimaryTargets =
            new List<GroundAttackOpportunityTarget>();
        public List<GroundAttackOpportunityTarget> GroundOpportunityTargets =
            new List<GroundAttackOpportunityTarget>();
        public string GroundOpportunityDescription = string.Empty;
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
        public GroundAttackOpportunityTarget GroundPrimaryTarget;
        public List<GroundAttackOpportunityTarget> GroundOpportunityTargets =
            new List<GroundAttackOpportunityTarget>();
        public string GroundOpportunityDescription = string.Empty;
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
        public GroundAttackTargetReference GroundTarget;
        public List<GroundAttackOpportunityTarget> GroundOpportunityTargets =
            new List<GroundAttackOpportunityTarget>();
        public string GroundOpportunityDescription = string.Empty;
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
