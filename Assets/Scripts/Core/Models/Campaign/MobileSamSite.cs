using System;
using System.Collections.Generic;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class MobileSamSite : IAirDefenseSite
    {
        public Guid MobileSamSiteId;
        public Guid SamSiteTemplateId;
        public Guid HostDivisionId;
        public Alliance Alliance;
        public bool IsDestroyed;
        public bool IsSuppressed;
        [SerializeReference] public List<AirDefenseComponent> Components = new List<AirDefenseComponent>();

        Guid IAirDefenseSite.SiteId => MobileSamSiteId;

        Guid IAirDefenseSite.SamSiteTemplateId => SamSiteTemplateId;

        IReadOnlyList<AirDefenseComponent> IAirDefenseSite.Components => Components;

        public MobileSamSite()
        {
        }

        public MobileSamSite(
            MobileSamSiteStartingCondition startingCondition,
            List<AirDefenseComponent> components)
        {
            if (startingCondition == null)
                throw new ArgumentNullException(nameof(startingCondition));

            MobileSamSiteId = startingCondition.MobileSamSiteId;
            SamSiteTemplateId = startingCondition.SamSiteTemplateId;
            HostDivisionId = startingCondition.HostDivisionId;
            Alliance = startingCondition.Alliance;
            Components = components ?? new List<AirDefenseComponent>();
        }

        public void Destroy()
        {
            IsDestroyed = true;
            foreach (var component in Components)
                component?.Damage();
        }
    }
}
