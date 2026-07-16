using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class IADSTrack
    {
        public const float MinimumCreationQuality = 0.10f;
        public const float AircraftTypeIdentificationQualityThreshold = 0.75f;

        public Guid TrackId = Guid.NewGuid();
        public Guid FlightId;
        public Guid IdentifiedAircraftTypeDefinitionId;
        public Vector3 LastKnownPositionFeet;
        public int EstimatedAircraftCount;
        public float EstimatedAirCombatPower;
        public float EstimatedHeadingDegrees;
        public float EstimatedSpeedKnots;
        public float Quality;
        public bool IsStale;
        public float StaleSeconds;
        public DateTime LastObservedAt;

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
            IsStale = false;
            StaleSeconds = 0f;
            LastObservedAt = observedAt;
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
            IsStale = false;
            StaleSeconds = 0f;
            LastObservedAt = observedAt;
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
    }
}
