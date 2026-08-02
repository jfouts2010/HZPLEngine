using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class AirDefenseSiteSystem
    {
        [SerializeReference] public List<SamSite> Sites = new List<SamSite>();

        private Dictionary<Guid, SamSite> sitesById;
        private Dictionary<Guid, List<SamSite>> sitesByHostId;
        private BuildingSystem buildingSystem;
        private DivisionSystem divisionSystem;
        private TileSystem tileSystem;

        public void Configure(
            BuildingSystem buildings,
            DivisionSystem divisions,
            TileSystem tiles)
        {
            buildingSystem = buildings ?? throw new ArgumentNullException(nameof(buildings));
            divisionSystem = divisions ?? throw new ArgumentNullException(nameof(divisions));
            tileSystem = tiles ?? throw new ArgumentNullException(nameof(tiles));
        }

        public Alliance GetEffectiveAlliance(SamSite site)
        {
            if (site == null)
                return Alliance.Neutral;

            if (site.HostType == SamSiteHostType.MobileDivision)
                return site.Alliance;

            if (buildingSystem != null
                && tileSystem != null
                && buildingSystem.TryGetBuilding(site.HostId, out var building)
                && building is AirDefenseBuilding
                && tileSystem.TryGetLand(building.TileId, out var landTile))
            {
                return landTile.Controller;
            }

            return Alliance.Neutral;
        }

        public bool TryGetTileId(SamSite site, out Vector3Int tileId)
        {
            if (site != null)
            {
                if (site.HostType == SamSiteHostType.StaticBuilding
                    && buildingSystem != null
                    && buildingSystem.TryGetBuilding(site.HostId, out var building))
                {
                    tileId = building.TileId;
                    return true;
                }

                if (site.HostType == SamSiteHostType.MobileDivision
                    && divisionSystem != null
                    && divisionSystem.TryGetDivision(site.HostId, out var division))
                {
                    tileId = division.TileId;
                    return true;
                }
            }

            tileId = default;
            return false;
        }

        public bool TryGetPositionFeet(SamSite site, out Vector3 positionFeet)
        {
            if (site != null)
            {
                if (site.HostType == SamSiteHostType.StaticBuilding
                    && buildingSystem != null
                    && buildingSystem.TryGetBuilding(site.HostId, out var building))
                {
                    positionFeet = building.PositionFeet;
                    return true;
                }

                if (site.HostType == SamSiteHostType.MobileDivision
                    && divisionSystem != null
                    && divisionSystem.TryGetDivision(site.HostId, out var division))
                {
                    positionFeet = CampaignMapCoordinates.TileCenterFeet(
                        division.TileId);
                    return true;
                }
            }

            positionFeet = default;
            return false;
        }

        public IEnumerable<AirDefenseComponent> GetAvailableComponents(SamSite site)
        {
            if (site == null
                || site.IsDisabled
                || site.IsDestroyed
                || site.IsSuppressed)
            {
                return Enumerable.Empty<AirDefenseComponent>();
            }

            return site.Components;
        }

        public List<SamSite> GetSitesForHost(Guid hostId)
        {
            EnsureIndex();
            return sitesByHostId.TryGetValue(hostId, out var sites)
                ? sites
                : new List<SamSite>();
        }

        public bool TryGetSite(Guid siteId, out SamSite site)
        {
            EnsureIndex();
            return sitesById.TryGetValue(siteId, out site);
        }

        public bool DamageComponent(Guid siteId, Guid componentId)
        {
            return TryGetSite(siteId, out var site) && site.DamageComponent(componentId);
        }

        public bool DestroySite(Guid siteId)
        {
            if (!TryGetSite(siteId, out var site))
                return false;

            site.Destroy();
            return true;
        }

        public void DisableSitesOnTileCapture(Vector3Int tileId)
        {
            foreach (var site in Sites)
            {
                if (site == null
                    || site.HostType != SamSiteHostType.StaticBuilding
                    || !TryGetTileId(site, out var siteTileId)
                    || siteTileId != tileId)
                {
                    continue;
                }

                site.IsDisabled = true;
            }
        }

        public void DestroySitesForOverrun(Guid hostDivisionId)
        {
            foreach (var site in GetSitesForHost(hostDivisionId))
            {
                if (site?.HostType == SamSiteHostType.MobileDivision)
                    site.Destroy();
            }
        }

        public void RebuildIndex()
        {
            sitesById = Sites
                .GroupBy(site => site.SiteId)
                .ToDictionary(group => group.Key, group => group.First());

            sitesByHostId = Sites
                .GroupBy(site => site.HostId)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        private void EnsureIndex()
        {
            if (sitesById == null || sitesByHostId == null)
                RebuildIndex();
        }
    }
}
