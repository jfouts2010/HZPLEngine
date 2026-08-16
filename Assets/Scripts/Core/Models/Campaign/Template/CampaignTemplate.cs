using System;
using System.Collections.Generic;
using UnityEngine;


namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class CampaignTemplate
    {
        public const int ScriptedAirPlanHorizonHours = 24;

        public static readonly Guid DefaultModuleId = Guid.Parse("92f96fd1-d2f1-4e28-a047-30b0940dc45f");
        public static readonly DateTime DefaultCampaignStartTime = new DateTime(1990, 1, 1, 6, 0, 0);

        public string Name { get; private set; }
        public Guid ModuleId = DefaultModuleId;
        public DateTime CampaignStartTime = DefaultCampaignStartTime;
        public SimulationSettings SimulationSettings = new SimulationSettings();
        public string ContentHash = string.Empty;
        public List<CountryAllianceAssignment> CountryAllianceAssignments = new List<CountryAllianceAssignment>();
        public Dictionary<Alliance, List<Guid>> OrdnanceAllowances = new Dictionary<Alliance, List<Guid>>();
        public Dictionary<Alliance, List<Guid>> SamSiteTemplateAllowances = new Dictionary<Alliance, List<Guid>>();
        public Dictionary<Alliance, AllianceAirDoctrine> AirDoctrineByAlliance =
            CreateDefaultAirDoctrineByAlliance();
        public List<AirPackagePlan> AirPackagePlans = new List<AirPackagePlan>();
        public List<Tile> Tiles = new List<Tile>();
        [SerializeReference] public List<TileData> StartingTileData = new List<TileData>();
        public List<SupplyCapitalStartingCondition> SupplyCapitals = new List<SupplyCapitalStartingCondition>();
        public List<BuildingStartingCondition> BuildingStartingConditions = new List<BuildingStartingCondition>();
        public List<DivisionStartingCondition> DivisionStartingConditions = new List<DivisionStartingCondition>();
        public List<MobileSamSiteStartingCondition> MobileSamSiteStartingConditions =
            new List<MobileSamSiteStartingCondition>();
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
            Tiles = tiles;
            RebuildDerivedData();
        }

        public void RebuildDerivedData()
        {
            ValidateAirPackagePlanHorizon();
            HexGridTopology.AssignNeighbors(Tiles);
        }

        private void ValidateAirPackagePlanHorizon()
        {
            if (AirPackagePlans == null)
                return;

            var horizonEnd = CampaignStartTime.AddHours(
                ScriptedAirPlanHorizonHours);
            foreach (var plan in AirPackagePlans)
            {
                if (plan == null)
                    continue;
                if (plan.AvailableAt >= CampaignStartTime
                    && plan.EffectStart >= plan.AvailableAt
                    && plan.EffectEnd > plan.EffectStart
                    && plan.EffectEnd <= horizonEnd)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Campaign '{Name}' air plan {plan.PlanId} must fit "
                    + $"within its first {ScriptedAirPlanHorizonHours} hours.");
            }
        }

        private static Dictionary<Alliance, AllianceAirDoctrine> CreateDefaultAirDoctrineByAlliance()
        {
            return new Dictionary<Alliance, AllianceAirDoctrine>
            {
                { Alliance.Bluefor, AllianceAirDoctrine.CreateDefault() },
                { Alliance.Redfor, AllianceAirDoctrine.CreateDefault() }
            };
        }
    }
}
