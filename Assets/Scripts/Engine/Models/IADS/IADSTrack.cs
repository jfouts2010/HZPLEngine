using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class IADSTrack
    {
        public const float MinimumCreationQuality = 0.10f;

        public Guid TrackId = Guid.NewGuid();
        public Guid FlightId;
        public Guid AircraftTypeDefinitionId;
        public Vector3 LastKnownPositionFeet;
        public int EstimatedAircraftCount;
        public float Quality;
        public bool IsStale;
        public int StaleTurns;

        public IADSTrack()
        {
        }

        public IADSTrack(
            Guid flightId,
            Guid aircraftTypeDefinitionId,
            Vector3 lastKnownPositionFeet,
            int estimatedAircraftCount,
            float quality)
        {
            FlightId = flightId;
            AircraftTypeDefinitionId = aircraftTypeDefinitionId;
            LastKnownPositionFeet = lastKnownPositionFeet;
            EstimatedAircraftCount = Math.Max(0, estimatedAircraftCount);
            Quality = Mathf.Clamp01(quality);
            IsStale = false;
            StaleTurns = 0;
        }

        public void Refresh(
            Vector3 lastKnownPositionFeet,
            int estimatedAircraftCount,
            float quality)
        {
            LastKnownPositionFeet = lastKnownPositionFeet;
            EstimatedAircraftCount = Math.Max(0, estimatedAircraftCount);
            Quality = Mathf.Clamp01(quality);
            IsStale = false;
            StaleTurns = 0;
        }

        public void MarkStale(float staleQualityDecayPerTurn)
        {
            IsStale = true;
            StaleTurns++;
            Quality = Mathf.Clamp01(Quality - Mathf.Max(0f, staleQualityDecayPerTurn));
        }
    }
}
