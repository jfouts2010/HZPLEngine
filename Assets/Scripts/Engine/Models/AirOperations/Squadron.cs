using System;
using System.Collections.Generic;
using System.Linq;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class Squadron
    {
        public Guid SquadronId;
        public Guid AircraftTypeDefinitionId;
        public Guid CountryId;
        public Guid AirportBuildingId;
        public string Name = string.Empty;
        public List<CampaignAircraft> Aircraft = new List<CampaignAircraft>();

        public int ReadyAircraft => Aircraft.Count(aircraft => aircraft.Status == CampaignAircraftStatus.Ready);
        public int DamagedAircraft => Aircraft.Count(aircraft => aircraft.Status == CampaignAircraftStatus.Damaged);
        public int LostAircraft => Aircraft.Count(aircraft => aircraft.Status == CampaignAircraftStatus.Lost);
        public int AssignedAircraft => Aircraft.Count(aircraft => aircraft.Status == CampaignAircraftStatus.Assigned);

        public Squadron()
        {
        }

        public Squadron(SquadronStartingCondition startingCondition)
        {
            SquadronId = startingCondition.SquadronId;
            AircraftTypeDefinitionId = startingCondition.AircraftTypeDefinitionId;
            CountryId = startingCondition.CountryId;
            AirportBuildingId = startingCondition.StartingAirportBuildingId;
            Name = startingCondition.Name;
            Aircraft = CreateAircraft(Math.Max(0, startingCondition.AircraftCount));
        }

        private List<CampaignAircraft> CreateAircraft(int aircraftCount)
        {
            var aircraft = new List<CampaignAircraft>();
            for (var i = 0; i < Math.Max(0, aircraftCount); i++)
                aircraft.Add(new CampaignAircraft(SquadronId, AircraftTypeDefinitionId));

            return aircraft;
        }
    }
}
