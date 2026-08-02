using System;
using System.Collections.Generic;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public struct IADSTrackMotionSample
    {
        public DateTime ObservedAt;
        public Vector3 PositionFeet;
        public float HeadingDegrees;

        public IADSTrackMotionSample(
            DateTime observedAt,
            Vector3 positionFeet,
            float headingDegrees)
        {
            ObservedAt = observedAt;
            PositionFeet = positionFeet;
            HeadingDegrees = headingDegrees;
        }
    }

    [Serializable]
    public sealed class IADSTrack
    {
        public const float MinimumCreationQuality = 0.10f;
        public const float AircraftTypeIdentificationQualityThreshold = 0.5f;
        private const double MotionHistoryMinutes = 10d;

        public Guid TrackId = Guid.NewGuid();
        public Guid FlightId;
        public Guid IdentifiedAircraftTypeDefinitionId;
        public Vector3 LastKnownPositionFeet;
        public int EstimatedAircraftCount;
        public float EstimatedAirCombatPower;
        public float EstimatedHeadingDegrees;
        public float EstimatedSpeedKnots;
        public float Quality;
        public bool IsEstablished;
        public bool IsStale;
        public float StaleSeconds;
        public DateTime LastObservedAt;
        public List<IADSTrackMotionSample> RecentMotion =
            new List<IADSTrackMotionSample>();

        public bool HasIdentifiedAircraftType =>
            IdentifiedAircraftTypeDefinitionId != Guid.Empty;

        public IADSTrack()
        {
        }

        public IADSTrack(
            Guid flightId,
            Vector3 lastKnownPositionFeet,
            int estimatedAircraftCount,
            float estimatedAirCombatPower,
            float estimatedHeadingDegrees,
            float estimatedSpeedKnots,
            float quality,
            DateTime observedAt)
        {
            FlightId = flightId;
            LastKnownPositionFeet = lastKnownPositionFeet;
            EstimatedAircraftCount = Math.Max(0, estimatedAircraftCount);
            EstimatedAirCombatPower = Mathf.Max(0f, estimatedAirCombatPower);
            EstimatedHeadingDegrees = estimatedHeadingDegrees;
            EstimatedSpeedKnots = Mathf.Max(0f, estimatedSpeedKnots);
            Quality = Mathf.Clamp01(quality);
            IsEstablished = Quality >= MinimumCreationQuality;
            IsStale = false;
            StaleSeconds = 0f;
            LastObservedAt = observedAt;
            RecordMotion(observedAt, lastKnownPositionFeet, estimatedHeadingDegrees);
        }

        public void Refresh(
            Vector3 lastKnownPositionFeet,
            int estimatedAircraftCount,
            float estimatedAirCombatPower,
            float estimatedHeadingDegrees,
            float estimatedSpeedKnots,
            float quality,
            DateTime observedAt)
        {
            LastKnownPositionFeet = lastKnownPositionFeet;
            EstimatedAircraftCount = Math.Max(0, estimatedAircraftCount);
            EstimatedAirCombatPower = Mathf.Max(0f, estimatedAirCombatPower);
            EstimatedHeadingDegrees = estimatedHeadingDegrees;
            EstimatedSpeedKnots = Mathf.Max(0f, estimatedSpeedKnots);
            Quality = Mathf.Clamp01(quality);
            IsEstablished = IsEstablished || Quality >= MinimumCreationQuality;
            IsStale = false;
            StaleSeconds = 0f;
            LastObservedAt = observedAt;
            RecordMotion(observedAt, lastKnownPositionFeet, estimatedHeadingDegrees);
        }

        public void MarkStale(float elapsedSeconds, float staleQualityDecayPerSecond)
        {
            var elapsed = Mathf.Max(0f, elapsedSeconds);
            IsStale = true;
            StaleSeconds += elapsed;
            Quality = Mathf.Clamp01(
                Quality - Mathf.Max(0f, staleQualityDecayPerSecond) * elapsed);
        }

        public void IdentifyAircraftType(Guid aircraftTypeDefinitionId)
        {
            if (!HasIdentifiedAircraftType
                && Quality >= AircraftTypeIdentificationQualityThreshold
                && aircraftTypeDefinitionId != Guid.Empty)
            {
                IdentifiedAircraftTypeDefinitionId = aircraftTypeDefinitionId;
            }
        }

        private void RecordMotion(
            DateTime observedAt,
            Vector3 positionFeet,
            float headingDegrees)
        {
            RecentMotion ??= new List<IADSTrackMotionSample>();
            var sample = new IADSTrackMotionSample(
                observedAt,
                positionFeet,
                headingDegrees);
            if (RecentMotion.Count > 0
                && RecentMotion[RecentMotion.Count - 1].ObservedAt == observedAt)
            {
                RecentMotion[RecentMotion.Count - 1] = sample;
            }
            else
            {
                RecentMotion.Add(sample);
            }

            var cutoff = observedAt.AddMinutes(-MotionHistoryMinutes);
            RecentMotion.RemoveAll(item => item.ObservedAt < cutoff);
        }
    }
}
