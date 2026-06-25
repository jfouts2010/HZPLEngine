using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class IADSTrack
    {
        public const float MinimumCreationQuality = 0.10f;

        public Guid TrackId = Guid.NewGuid();
        public Guid AircraftId;
        public Guid AircraftTypeDefinitionId;
        public Vector3Int LastKnownTileId;
        public float Quality;
        public bool IsStale;
        public int StaleTurns;

        public IADSTrack()
        {
        }

        public IADSTrack(
            Guid aircraftId,
            Guid aircraftTypeDefinitionId,
            Vector3Int lastKnownTileId,
            float quality)
        {
            AircraftId = aircraftId;
            AircraftTypeDefinitionId = aircraftTypeDefinitionId;
            LastKnownTileId = lastKnownTileId;
            Quality = Mathf.Clamp01(quality);
            IsStale = false;
            StaleTurns = 0;
        }

        public void Refresh(Vector3Int lastKnownTileId, float quality)
        {
            LastKnownTileId = lastKnownTileId;
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
