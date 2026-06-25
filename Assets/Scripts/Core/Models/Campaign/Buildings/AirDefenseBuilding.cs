using System;
using System.Collections.Generic;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class AirDefenseBuilding : Building, IAirDefenseSite
    {
        public Guid SamSiteTemplateId;
        public Guid CountryId;
        public bool IsAirDefenseDisabled;
        public bool IsSuppressed;
        [SerializeReference] public List<AirDefenseComponent> Components = new List<AirDefenseComponent>();

        Guid IAirDefenseSite.SiteId => BuildingId;

        Guid IAirDefenseSite.SamSiteTemplateId => SamSiteTemplateId;

        IReadOnlyList<AirDefenseComponent> IAirDefenseSite.Components => Components;

        public override BuildingType Type
        {
            get { return BuildingType.AirDefense; }
        }

        public AirDefenseBuilding()
        {
        }

        public AirDefenseBuilding(
            BuildingStartingCondition startingCondition,
            List<AirDefenseComponent> components) : base(startingCondition)
        {
            SamSiteTemplateId = startingCondition.SamSiteTemplateId;
            CountryId = startingCondition.CountryId;
            Components = components ?? new List<AirDefenseComponent>();
        }

        public bool DamageComponent(Guid componentId)
        {
            var component = Components.Find(candidate => candidate != null && candidate.ComponentId == componentId);
            if (component == null)
                return false;

            component.Damage();
            return true;
        }
    }
}
