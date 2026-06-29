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

        public int ReadyAircraft => Aircraft?.Count(aircraft => aircraft.Status == CampaignAircraftStatus.Ready) ?? 0;
        public int DamagedAircraft => Aircraft?.Count(aircraft => aircraft.Status == CampaignAircraftStatus.Damaged) ?? 0;
        public int LostAircraft => Aircraft?.Count(aircraft => aircraft.Status == CampaignAircraftStatus.Lost) ?? 0;
        public int AssignedAircraft => Aircraft?.Count(aircraft => aircraft.Status == CampaignAircraftStatus.Assigned) ?? 0;

        public Squadron()
        {
        }

        public Squadron(SquadronStartingCondition startingCondition)
        {
            if (startingCondition == null)
                throw new ArgumentNullException(nameof(startingCondition));

            SquadronId = startingCondition.SquadronId;
            AircraftTypeDefinitionId = startingCondition.AircraftTypeDefinitionId;
            CountryId = startingCondition.CountryId;
            AirportBuildingId = startingCondition.StartingAirportBuildingId;
            Name = startingCondition.Name ?? string.Empty;
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
