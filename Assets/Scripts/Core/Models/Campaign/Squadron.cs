using System;

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
        public int ReadyAircraft;
        public int DamagedAircraft;
        public int LostAircraft;
        public int AssignedAircraft;

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
            ReadyAircraft = Math.Max(0, startingCondition.AircraftCount);
            DamagedAircraft = 0;
            LostAircraft = 0;
            AssignedAircraft = 0;
        }
    }
}
