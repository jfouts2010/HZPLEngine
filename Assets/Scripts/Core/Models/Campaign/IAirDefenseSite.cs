using System;
using System.Collections.Generic;

namespace Models.Gameplay.Campaign
{
    public interface IAirDefenseSite
    {
        Guid SiteId { get; }
        Guid SamSiteTemplateId { get; }
        IReadOnlyList<AirDefenseComponent> Components { get; }
    }
}
