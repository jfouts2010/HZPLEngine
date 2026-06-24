using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class AirDefenseSiteSystem
    {
        [SerializeReference] public List<MobileSamSite> MobileSamSites = new List<MobileSamSite>();

        private Dictionary<Guid, MobileSamSite> mobileSitesById;
        private Dictionary<Guid, List<MobileSamSite>> mobileSitesByHostDivisionId;

        public List<AirDefenseBuilding> GetStaticSamSites(BuildingSystem buildingSystem)
        {
            return buildingSystem == null
                ? new List<AirDefenseBuilding>()
                : buildingSystem.GetBuildings<AirDefenseBuilding>();
        }

        public List<MobileSamSite> GetMobileSamSitesForDivision(Guid divisionId)
        {
            EnsureIndex();
            return mobileSitesByHostDivisionId.TryGetValue(divisionId, out var sites)
                ? sites
                : new List<MobileSamSite>();
        }

        public bool TryGetMobileSamSite(Guid mobileSamSiteId, out MobileSamSite mobileSamSite)
        {
            EnsureIndex();
            return mobileSitesById.TryGetValue(mobileSamSiteId, out mobileSamSite);
        }

        public bool DamageStaticComponent(BuildingSystem buildingSystem, Guid buildingId, Guid componentId)
        {
            if (buildingSystem == null || !buildingSystem.TryGetBuilding(buildingId, out var building))
                return false;

            if (building is AirDefenseBuilding staticSamBuilding)
                return staticSamBuilding.DamageComponent(componentId);

            return false;
        }

        public bool DamageMobileSamSite(Guid mobileSamSiteId)
        {
            if (!TryGetMobileSamSite(mobileSamSiteId, out var mobileSamSite))
                return false;

            mobileSamSite.Destroy();
            return true;
        }

        public void DisableAirDefenseOnTileCapture(BuildingSystem buildingSystem, Vector3Int tileId)
        {
            if (buildingSystem == null)
                return;

            foreach (var building in buildingSystem.GetBuildingsOnTile(tileId))
            {
                if (building is AirDefenseBuilding staticSamBuilding)
                    staticSamBuilding.IsAirDefenseDisabled = true;
            }
        }

        public void DestroyMobileSamSitesForOverrun(Guid hostDivisionId)
        {
            foreach (var site in GetMobileSamSitesForDivision(hostDivisionId))
                site?.Destroy();
        }

        public void RebuildIndex()
        {
            var mobileSites = (MobileSamSites ?? new List<MobileSamSite>())
                .ToList();

            mobileSitesById = mobileSites
                .GroupBy(site => site.MobileSamSiteId)
                .ToDictionary(group => group.Key, group => group.First());

            mobileSitesByHostDivisionId = mobileSites
                .GroupBy(site => site.HostDivisionId)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        private void EnsureIndex()
        {
            if (mobileSitesById == null || mobileSitesByHostDivisionId == null)
                RebuildIndex();
        }
    }
}
