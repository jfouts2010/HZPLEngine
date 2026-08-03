using System;
using System.Collections.Generic;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    public enum IADSTrackDiagnosticEvent
    {
        NotObserved = 0,
        TentativeStarted = 1,
        TentativeUpdated = 2,
        Established = 3,
        Updated = 4,
        Identified = 5,
        Stale = 6,
        StaleUpdated = 7,
        Reacquired = 8,
        Expired = 9,
        Removed = 10
    }

    public enum IADSRadarEvaluationResult
    {
        Contributed = 0,
        SiteDisabled = 1,
        SiteDestroyed = 2,
        SiteSuppressed = 3,
        SitePositionUnavailable = 4,
        RadarDamaged = 5,
        RadarSilent = 6,
        DefinitionMissing = 7,
        DetectionRangeInvalid = 8,
        OutOfRange = 9,
        AboveAltitudeCeiling = 10,
        ZeroQualityCap = 11,
        BelowRadarHorizon = 12,
        TargetUndetectable = 13
    }

    /// <summary>
    /// One radar's complete evaluation against one hostile flight at one IADS
    /// tactical update. Rejected radars are retained so diagnostic consumers
    /// can explain an absent or stale track, not merely describe contributors.
    /// </summary>
    [Serializable]
    public sealed class IADSRadarEvaluation
    {
        public Guid SiteId;
        public Guid RadarComponentId;
        public Guid RadarDefinitionId;
        public string RadarName = string.Empty;
        public IADSRadarEvaluationResult Result;
        public Vector3 SitePositionFeet;
        public bool HasSitePosition;
        public float RadarAntennaHeightMeters;
        public float RadarAltitudeMeters;
        public float HorizontalDistanceKm = -1f;
        public float DistanceKm = -1f;
        public float MaximumRangeKm;
        public float DetectabilityAdjustedRangeKm;
        public float RadarHorizonKm;
        public float DistanceFraction = -1f;
        public float RadarHorizonFraction = -1f;
        public float RangeMarginKm;
        public float RadarHorizonMarginKm;
        public RadarRangeConstraint LimitingConstraint;
        public float TargetAltitudeFeet;
        public float MaximumAltitudeFeet;
        public float AltitudeMarginFeet;
        public float RadarTrackQuality;
        public float TargetDetectability;
        public string FusionCorrelationGroup = string.Empty;
        public float RangeFactor;
        public float QualityCap;
        public float AppliedCapMultiplier;
        public float AdjustedQualityCap;
        public float RawQualityIncrease;
        public float AppliedBuildMultiplier;
        public float AppliedQualityIncrease;

        public bool Contributed => Result == IADSRadarEvaluationResult.Contributed;
    }

    /// <summary>
    /// Diagnostic-only account of how one alliance's shared track changed.
    /// Truth kinematics are included for analysis and must never be consumed by
    /// gameplay decisions or alliance-facing presentation.
    /// </summary>
    [Serializable]
    public sealed class IADSTrackDiagnostic
    {
        public DateTime OccurredAt;
        public Alliance ObserverAlliance;
        public Guid FlightId;
        public Guid AircraftTypeDefinitionId;
        public Guid TrackId;
        public IADSTrackDiagnosticEvent Event;
        public string Reason = string.Empty;
        public float ElapsedSeconds;

        public Vector3 TruthPositionFeet;
        public float TruthHeadingDegrees;
        public float TruthSpeedKnots;
        public bool HasTrackEstimate;
        public Vector3 TrackPositionFeet;
        public float TrackHeadingDegrees;
        public float TrackSpeedKnots;
        public int TruthAircraftCount = -1;
        public int EstimatedAircraftCount = -1;
        public float EstimatedAirCombatPower;
        public float TargetRadarDetectability;

        public float PreviousQuality;
        public float QualityAfterObservation;
        public float NewQuality;
        public float FusedQualityCap;
        public float DiminishedQualityIncrease;
        public float ObservedExcessQualityDecay;
        public float HeadingChangeFraction;
        public float SpeedChangeFraction;
        public float AltitudeChangeFraction;
        public float HeadingQualityPenalty;
        public float SpeedQualityPenalty;
        public float AltitudeQualityPenalty;
        public float AppliedManeuverQualityPenalty;
        public float StaleQualityDecay;
        public float StaleSeconds;

        public bool WasEstablished;
        public bool IsEstablished;
        public bool WasStale;
        public bool IsStale;
        public bool BecameEstablished;
        public bool BecameIdentified;
        public bool HasIdentifiedAircraftType;

        public float CreationQualityThreshold = IADSTrack.MinimumCreationQuality;
        public float IdentificationQualityThreshold =
            IADSTrack.AircraftTypeIdentificationQualityThreshold;

        public List<IADSRadarEvaluation> RadarEvaluations =
            new List<IADSRadarEvaluation>();
    }
}
