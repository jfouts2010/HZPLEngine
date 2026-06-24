using System;
using System.Collections.Generic;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class CampaignAircraft
    {
        public Guid AircraftId = Guid.NewGuid();
        public Guid SquadronId;
        public Guid AircraftTypeDefinitionId;
        public CampaignAircraftStatus Status = CampaignAircraftStatus.Ready;
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
