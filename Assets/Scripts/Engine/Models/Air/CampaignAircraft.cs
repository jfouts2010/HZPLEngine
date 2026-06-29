using System;
using System.Collections.Generic;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class CampaignAircraft
    {
        public Guid AircraftId = Guid.NewGuid();
        public Guid SquadronId;
        public Guid AircraftTypeDefinitionId;
        public CampaignAircraftStatus Status = CampaignAircraftStatus.Ready;
        public Guid AssignedFlightId;
        // Legacy execution bridge. The future mission-execution system will derive these from flight state.
        public bool IsActiveInSortie;
        public bool HasCurrentTileId;
        public Vector3Int CurrentTileId;
        
        public List<AircraftLoadoutItem> Loadout = new List<AircraftLoadoutItem>();

        public CampaignAircraft()
        {
        }

        public CampaignAircraft(Guid squadronId, Guid aircraftTypeDefinitionId)
        {
            SquadronId = squadronId;
            AircraftTypeDefinitionId = aircraftTypeDefinitionId;
        }

        public void ClearLoadout()
        {
            Loadout.Clear();
        }

        public bool TryAssignToFlight(Guid flightId)
        {
            if (flightId == Guid.Empty
                || Status != CampaignAircraftStatus.Ready
                || AssignedFlightId != Guid.Empty)
                return false;

            AssignedFlightId = flightId;
            Status = CampaignAircraftStatus.Assigned;
            return true;
        }

        public bool ReleaseFromFlight(Guid flightId)
        {
            if (flightId == Guid.Empty || AssignedFlightId != flightId)
                return false;

            AssignedFlightId = Guid.Empty;
            if (Status == CampaignAircraftStatus.Assigned)
                Status = CampaignAircraftStatus.Ready;
            return true;
        }
    }
}
