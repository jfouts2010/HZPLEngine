using System;
using System.Collections.Generic;
using System.Linq;

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

        public void SetLoadout(IEnumerable<AircraftLoadoutItem> loadout)
        {
            Loadout = loadout
                .Select(item => new AircraftLoadoutItem(
                    item.AircraftLoadoutStationDefinitionId,
                    item.AircraftCarriageConfigurationDefinitionId,
                    item.OrdnanceTypeDefinitionId,
                    item.Count))
                .Where(item => item.Count > 0)
                .ToList();
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
