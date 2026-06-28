using System;
using System.Collections.Generic;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    public enum SamSiteHostType
    {
        StaticBuilding,
        MobileDivision
    }

    [Serializable]
    public class SamSite
    {
        public Guid SiteId;
        public Guid SamSiteTemplateId;
        public SamSiteHostType HostType;
        public Guid HostId;
        public Alliance Alliance;
        public bool IsDisabled;
        public bool IsDestroyed;
        public bool IsSuppressed;
        [SerializeReference] public List<AirDefenseComponent> Components = new List<AirDefenseComponent>();

        public SamSite()
        {
        }

        public SamSite(
            MobileSamSiteStartingCondition startingCondition,
            List<AirDefenseComponent> components)
        {
            if (startingCondition == null)
                throw new ArgumentNullException(nameof(startingCondition));

            SiteId = startingCondition.MobileSamSiteId;
            SamSiteTemplateId = startingCondition.SamSiteTemplateId;
            HostType = SamSiteHostType.MobileDivision;
            HostId = startingCondition.HostDivisionId;
            Alliance = startingCondition.Alliance;
            Components = components ?? new List<AirDefenseComponent>();
        }

        public SamSite(
            BuildingStartingCondition startingCondition,
            List<AirDefenseComponent> components)
        {
            if (startingCondition == null)
                throw new ArgumentNullException(nameof(startingCondition));

            SiteId = startingCondition.BuildingId;
            SamSiteTemplateId = startingCondition.SamSiteTemplateId;
            HostType = SamSiteHostType.StaticBuilding;
            HostId = startingCondition.BuildingId;
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

        public void Destroy()
        {
            IsDestroyed = true;
            foreach (var component in Components)
                component?.Damage();
        }
    }
}
