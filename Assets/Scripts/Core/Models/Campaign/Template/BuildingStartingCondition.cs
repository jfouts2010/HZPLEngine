using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class BuildingStartingCondition
    {
        public Guid BuildingId = Guid.NewGuid();
        public string ThirdPartyId = string.Empty;
        public Vector3 PositionFeet;
        public BuildingType Type;
        public BuildingLevel Level = new BuildingLevel();
        public Guid SamSiteTemplateId;
    }
}
