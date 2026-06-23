using System;
using System.Collections.Generic;
using UnityEngine;


namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class CampaignTemplate
    {
        public static readonly Guid DefaultModuleId = Guid.Parse("92f96fd1-d2f1-4e28-a047-30b0940dc45f");
        public static readonly DateTime DefaultCampaignStartTime = new DateTime(1990, 1, 1, 6, 0, 0);

        public string Name { get; private set; }
        public Guid ModuleId = DefaultModuleId;
        public DateTime CampaignStartTime = DefaultCampaignStartTime;
        public SimulationSettings SimulationSettings = new SimulationSettings();
        public string ContentHash = string.Empty;
        public List<CountryAllianceAssignment> CountryAllianceAssignments = new List<CountryAllianceAssignment>();
        public List<Tile> Tiles = new List<Tile>();
        [SerializeReference] public List<TileData> StartingTileData = new List<TileData>();
        public List<SupplyCapitalStartingCondition> SupplyCapitals = new List<SupplyCapitalStartingCondition>();
        public List<BuildingStartingCondition> BuildingStartingConditions = new List<BuildingStartingCondition>();
        public List<DivisionStartingCondition> DivisionStartingConditions = new List<DivisionStartingCondition>();
        public List<SquadronStartingCondition> SquadronStartingConditions = new List<SquadronStartingCondition>();
        
        public CampaignTemplate()
        {
            Name = "NewCampaign";
        }

        public CampaignTemplate(string name)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "NewCampaign" : name.Trim();
        }

        public CampaignTemplate(string name, List<Tile> tiles)
            : this(name)
        {
            Tiles = tiles ?? new List<Tile>();
            RebuildDerivedData();
        }

        public void RebuildDerivedData()
        {
            HexGridTopology.AssignNeighbors(Tiles);
        }
    }
}
