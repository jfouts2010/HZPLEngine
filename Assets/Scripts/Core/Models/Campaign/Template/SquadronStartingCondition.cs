using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class SquadronStartingCondition
    {
        public Guid SquadronId = Guid.NewGuid();
        public Guid CountryId;
        public Guid AircraftTypeDefinitionId;
        public Guid StartingAirportBuildingId;
        public int AircraftCount;
        public string Name = string.Empty;
    }
}
