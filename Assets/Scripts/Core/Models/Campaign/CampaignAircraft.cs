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
        //placeholder code, IsActiveInSortie, HasCurrentTileId, CurrentTileId will be replaced once we add sortie system
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
    }
}
