using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models.Ground;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models
{
    [Serializable]
    public sealed class DivisionIntelligenceReport
    {
        public Guid DivisionId;
        public Guid DivisionTemplateId;
        public Guid CountryId;
        public Vector3Int TileId;
        public string ObservedName = string.Empty;
        public float InformationQuality;
        public DateTime ObservedAt;
        public int MaxStrength;
        public int MaxOrganization;
        public float Recovery;
        public float SoftAttack;
        public float HardAttack;
        public int Defense;
        public int Toughness;
        public float Softness;
        public float Speed;
        public int CombatWidth;
        public float SupplyConsumption;
        public float FuelConsumption;
        public float MaxSupplyStore;
        public float SupplyStore;
        public float Organization;
        public float Strength;
        public bool IsRetreating;

        public bool IsCombatReady =>
            Strength >= 1f
            && Organization >= 1f
            && !IsRetreating;

        public void Normalize()
        {
            InformationQuality = Mathf.Clamp01(InformationQuality);
        }
    }

    [Serializable]
    public sealed class BuildingIntelligenceReport
    {
        public Guid BuildingId;
        public Vector3Int TileId;
        public Vector3 PositionFeet;
        public BuildingType Type;
        public float InformationQuality;
        public DateTime ObservedAt;
        public int BuildLevel;
        public int Damage;
        public int FunctionalLevel;
        public int TargetToughness;

        public void Normalize()
        {
            InformationQuality = Mathf.Clamp01(InformationQuality);
        }
    }

    [Serializable]
    public sealed class AirDefenseComponentIntelligenceReport
    {
        public Guid ComponentId;
        public Guid SamComponentDefinitionId;
        public bool IsDamaged;
        public int ReadyRounds;
        public int ReserveRounds;
    }

    [Serializable]
    public sealed class AirDefenseSiteIntelligenceReport
    {
        public Guid SiteId;
        public Guid SamSiteTemplateId;
        public SamSiteHostType HostType;
        public Guid HostId;
        public Vector3Int TileId;
        public Vector3 PositionFeet;
        public float InformationQuality;
        public DateTime ObservedAt;
        public bool IsDisabled;
        public bool IsDestroyed;
        public bool IsSuppressed;
        [SerializeReference]
        public List<AirDefenseComponentIntelligenceReport> Components =
            new List<AirDefenseComponentIntelligenceReport>();

        public void Normalize()
        {
            InformationQuality = Mathf.Clamp01(InformationQuality);
            Components ??= new List<AirDefenseComponentIntelligenceReport>();
        }
    }

    public enum ObservedAirportCondition
    {
        Intact = 0,
        Damaged = 1,
        NonFunctional = 2
    }

    [Serializable]
    public sealed class ObservedRunwayChannel
    {
        public int ChannelIndex;
        public int DamageLevel;

        public void Normalize()
        {
            ChannelIndex = Math.Max(0, ChannelIndex);
            DamageLevel = Math.Max(
                0,
                Math.Min(
                    AirportRunwayChannel.MaximumDamageLevel,
                    DamageLevel));
        }
    }

    [Serializable]
    public sealed class ObservedEnemyAirportSnapshot
    {
        public Guid AirportBuildingId;
        public Vector3Int AirportTileId;
        public Vector3 PositionFeet;
        public float InformationQuality;
        public DateTime ObservedAt;
        public ObservedAirportCondition Condition;
        public int BuildLevel;
        public int FunctionalLevel;
        public int TargetToughness;
        public int OperationalRunwayChannelCount;
        [SerializeReference]
        public List<ObservedRunwayChannel> RunwayChannels =
            new List<ObservedRunwayChannel>();
        [SerializeReference]
        public List<ObservedAircraftGroup> AircraftGroups =
            new List<ObservedAircraftGroup>();

        public void Normalize()
        {
            InformationQuality = Mathf.Clamp01(InformationQuality);
            BuildLevel = Math.Max(0, BuildLevel);
            FunctionalLevel = Math.Max(0, Math.Min(BuildLevel, FunctionalLevel));
            TargetToughness = Math.Max(1, TargetToughness);
            OperationalRunwayChannelCount = Math.Max(
                0,
                OperationalRunwayChannelCount);
            RunwayChannels ??= new List<ObservedRunwayChannel>();
            foreach (var channel in RunwayChannels)
                channel?.Normalize();
            AircraftGroups ??= new List<ObservedAircraftGroup>();
            foreach (var group in AircraftGroups)
                group?.Normalize();
        }
    }

    [Serializable]
    public sealed class ObservedAircraftGroup
    {
        public Guid AircraftTypeDefinitionId;
        public int AircraftOnGroundCount;
        public int ApparentlyAvailableCount;

        public void Normalize()
        {
            AircraftOnGroundCount = Math.Max(0, AircraftOnGroundCount);
            ApparentlyAvailableCount = Math.Max(0, ApparentlyAvailableCount);
        }
    }

    [Serializable]
    public sealed class AllianceIntelligencePicture
    {
        public Alliance ObserverAlliance;
        [SerializeReference]
        public List<DivisionIntelligenceReport> HostileDivisions =
            new List<DivisionIntelligenceReport>();
        [SerializeReference]
        public List<BuildingIntelligenceReport> HostileBuildings =
            new List<BuildingIntelligenceReport>();
        [SerializeReference]
        public List<AirDefenseSiteIntelligenceReport> HostileAirDefenseSites =
            new List<AirDefenseSiteIntelligenceReport>();
        [SerializeReference]
        public List<ObservedEnemyAirportSnapshot> EnemyAirports =
            new List<ObservedEnemyAirportSnapshot>();

        private Dictionary<Vector3Int, List<DivisionIntelligenceReport>>
            hostileDivisionsByTileId;
        private Dictionary<Vector3Int, List<BuildingIntelligenceReport>>
            hostileBuildingsByTileId;
        private Dictionary<Vector3Int, List<AirDefenseSiteIntelligenceReport>>
            hostileAirDefenseSitesByTileId;

        public AllianceIntelligencePicture()
        {
        }

        public AllianceIntelligencePicture(Alliance observerAlliance)
        {
            ObserverAlliance = observerAlliance;
        }

        public IReadOnlyList<DivisionIntelligenceReport> GetHostileDivisionsOnTile(
            Vector3Int tileId)
        {
            EnsureIndex();
            return hostileDivisionsByTileId.TryGetValue(tileId, out var reports)
                ? reports
                : Array.Empty<DivisionIntelligenceReport>();
        }

        public IReadOnlyList<BuildingIntelligenceReport> GetHostileBuildingsOnTile(
            Vector3Int tileId)
        {
            EnsureIndex();
            return hostileBuildingsByTileId.TryGetValue(tileId, out var reports)
                ? reports
                : Array.Empty<BuildingIntelligenceReport>();
        }

        public IReadOnlyList<AirDefenseSiteIntelligenceReport>
            GetHostileAirDefenseSitesOnTile(Vector3Int tileId)
        {
            EnsureIndex();
            return hostileAirDefenseSitesByTileId.TryGetValue(
                tileId,
                out var reports)
                ? reports
                : Array.Empty<AirDefenseSiteIntelligenceReport>();
        }

        public void RebuildIndex()
        {
            HostileDivisions ??= new List<DivisionIntelligenceReport>();
            HostileBuildings ??= new List<BuildingIntelligenceReport>();
            HostileAirDefenseSites ??=
                new List<AirDefenseSiteIntelligenceReport>();
            EnemyAirports ??= new List<ObservedEnemyAirportSnapshot>();

            foreach (var report in HostileDivisions)
                report?.Normalize();
            foreach (var report in HostileBuildings)
                report?.Normalize();
            foreach (var report in HostileAirDefenseSites)
                report?.Normalize();
            foreach (var report in EnemyAirports)
                report?.Normalize();

            hostileDivisionsByTileId = HostileDivisions
                .Where(report => report != null)
                .GroupBy(report => report.TileId)
                .ToDictionary(group => group.Key, group => group.ToList());
            hostileBuildingsByTileId = HostileBuildings
                .Where(report => report != null)
                .GroupBy(report => report.TileId)
                .ToDictionary(group => group.Key, group => group.ToList());
            hostileAirDefenseSitesByTileId = HostileAirDefenseSites
                .Where(report => report != null)
                .GroupBy(report => report.TileId)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        private void EnsureIndex()
        {
            if (hostileDivisionsByTileId == null
                || hostileBuildingsByTileId == null
                || hostileAirDefenseSitesByTileId == null)
            {
                RebuildIndex();
            }
        }
    }

    [Serializable]
    public sealed class AllianceIntelligenceSystem
    {
        public const float MaximumInformationQuality = 1f;

        [SerializeReference]
        public List<AllianceIntelligencePicture> Pictures =
            new List<AllianceIntelligencePicture>();

        private Dictionary<Alliance, AllianceIntelligencePicture>
            picturesByAlliance;

        public AllianceIntelligencePicture GetPicture(Alliance observerAlliance)
        {
            EnsureIndex();
            return picturesByAlliance.TryGetValue(observerAlliance, out var picture)
                ? picture
                : null;
        }

        public void RefreshMaximumInformation(
            GameManager gameManager,
            DateTime observedAt,
            ISet<Guid> airborneAircraftIds = null)
        {
            if (gameManager == null)
                throw new ArgumentNullException(nameof(gameManager));

            airborneAircraftIds ??= new HashSet<Guid>();
            EnsurePictures();

            foreach (var picture in Pictures)
            {
                if (picture == null || picture.ObserverAlliance == Alliance.Neutral)
                    continue;

                picture.HostileDivisions = BuildDivisionReports(
                    gameManager,
                    picture.ObserverAlliance,
                    observedAt);
                picture.HostileBuildings = BuildBuildingReports(
                    gameManager,
                    picture.ObserverAlliance,
                    observedAt);
                picture.HostileAirDefenseSites = BuildAirDefenseSiteReports(
                    gameManager,
                    picture.ObserverAlliance,
                    observedAt);
                picture.EnemyAirports = BuildEnemyAirportReports(
                    gameManager,
                    picture.ObserverAlliance,
                    observedAt,
                    airborneAircraftIds);
                picture.RebuildIndex();
            }

            RebuildIndex();
        }

        public void RebuildIndex()
        {
            Pictures ??= new List<AllianceIntelligencePicture>();
            foreach (var picture in Pictures)
                picture?.RebuildIndex();

            picturesByAlliance = Pictures
                .Where(picture => picture != null)
                .GroupBy(picture => picture.ObserverAlliance)
                .ToDictionary(group => group.Key, group => group.First());
        }

        private void EnsurePictures()
        {
            Pictures ??= new List<AllianceIntelligencePicture>();
            EnsurePicture(Alliance.Bluefor);
            EnsurePicture(Alliance.Redfor);
        }

        private void EnsurePicture(Alliance alliance)
        {
            if (Pictures.All(picture =>
                    picture == null || picture.ObserverAlliance != alliance))
            {
                Pictures.Add(new AllianceIntelligencePicture(alliance));
            }
        }

        private void EnsureIndex()
        {
            if (picturesByAlliance == null)
                RebuildIndex();
        }

        private static List<DivisionIntelligenceReport> BuildDivisionReports(
            GameManager gameManager,
            Alliance observerAlliance,
            DateTime observedAt)
        {
            return gameManager.divisionSystem.Divisions
                .Where(division => division != null
                                   && IsHostile(
                                       observerAlliance,
                                       gameManager.GetCountryAlliance(
                                           division.CountryId)))
                .Select(division => new DivisionIntelligenceReport
                {
                    DivisionId = division.DivisionId,
                    DivisionTemplateId = division.DivisionTemplateId,
                    CountryId = division.CountryId,
                    TileId = division.TileId,
                    ObservedName = division.Name,
                    InformationQuality = MaximumInformationQuality,
                    ObservedAt = observedAt,
                    MaxStrength = division.MaxStrength,
                    MaxOrganization = division.MaxOrganization,
                    Recovery = division.Recovery,
                    SoftAttack = division.SoftAttack,
                    HardAttack = division.HardAttack,
                    Defense = division.Defense,
                    Toughness = division.Toughness,
                    Softness = division.Softness,
                    Speed = division.Speed,
                    CombatWidth = division.CombatWidth,
                    SupplyConsumption = division.SupplyConsumption,
                    FuelConsumption = division.FuelConsumption,
                    MaxSupplyStore = division.MaxSupplyStore,
                    SupplyStore = division.SupplyStore,
                    Organization = division.Organization,
                    Strength = division.Strength,
                    IsRetreating = GroundSystemUtility.IsRetreating(division)
                })
                .OrderBy(report => report.TileId.x)
                .ThenBy(report => report.TileId.y)
                .ThenBy(report => report.TileId.z)
                .ThenBy(report => report.DivisionId)
                .ToList();
        }

        private static List<BuildingIntelligenceReport> BuildBuildingReports(
            GameManager gameManager,
            Alliance observerAlliance,
            DateTime observedAt)
        {
            return gameManager.buildingSystem.Buildings
                .Where(building => building != null
                                   && gameManager.tileSystem.TryGetLand(
                                       building.TileId,
                                       out var landTile)
                                   && IsHostile(observerAlliance, landTile.Controller))
                .Select(building => new BuildingIntelligenceReport
                {
                    BuildingId = building.BuildingId,
                    TileId = building.TileId,
                    PositionFeet = building.PositionFeet,
                    Type = building.Type,
                    InformationQuality = MaximumInformationQuality,
                    ObservedAt = observedAt,
                    BuildLevel = building.Level.BuildLevel,
                    Damage = building.Level.Damage,
                    FunctionalLevel = building.FunctionalLevel,
                    TargetToughness = building.TargetToughness
                })
                .OrderBy(report => report.TileId.x)
                .ThenBy(report => report.TileId.y)
                .ThenBy(report => report.TileId.z)
                .ThenBy(report => report.Type)
                .ThenBy(report => report.BuildingId)
                .ToList();
        }

        private static List<AirDefenseSiteIntelligenceReport>
            BuildAirDefenseSiteReports(
                GameManager gameManager,
                Alliance observerAlliance,
                DateTime observedAt)
        {
            var reports = new List<AirDefenseSiteIntelligenceReport>();
            foreach (var site in gameManager.airDefenseSiteSystem.Sites
                         .Where(site => site != null)
                         .OrderBy(site => site.SiteId))
            {
                var subjectAlliance =
                    gameManager.airDefenseSiteSystem.GetEffectiveAlliance(site);
                if (!IsHostile(observerAlliance, subjectAlliance)
                    || !gameManager.airDefenseSiteSystem.TryGetTileId(
                        site,
                        out var tileId)
                    || !gameManager.airDefenseSiteSystem.TryGetPositionFeet(
                        site,
                        out var positionFeet))
                {
                    continue;
                }

                reports.Add(new AirDefenseSiteIntelligenceReport
                {
                    SiteId = site.SiteId,
                    SamSiteTemplateId = site.SamSiteTemplateId,
                    HostType = site.HostType,
                    HostId = site.HostId,
                    TileId = tileId,
                    PositionFeet = positionFeet,
                    InformationQuality = MaximumInformationQuality,
                    ObservedAt = observedAt,
                    IsDisabled = site.IsDisabled,
                    IsDestroyed = site.IsDestroyed,
                    IsSuppressed = site.IsSuppressed,
                    Components = site.Components
                        .Where(component => component != null)
                        .OrderBy(component => component.ComponentId)
                        .Select(CreateComponentReport)
                        .ToList()
                });
            }

            return reports;
        }

        private static AirDefenseComponentIntelligenceReport
            CreateComponentReport(AirDefenseComponent component)
        {
            var launcher = component as LauncherAirDefenseComponent;
            return new AirDefenseComponentIntelligenceReport
            {
                ComponentId = component.ComponentId,
                SamComponentDefinitionId =
                    component.SamComponentDefinitionId,
                IsDamaged = component.IsDamaged,
                ReadyRounds = launcher?.ReadyRounds ?? 0,
                ReserveRounds = launcher?.ReserveRounds ?? 0
            };
        }

        private static List<ObservedEnemyAirportSnapshot>
            BuildEnemyAirportReports(
                GameManager gameManager,
                Alliance observerAlliance,
                DateTime observedAt,
                ISet<Guid> airborneAircraftIds)
        {
            return gameManager.buildingSystem
                .GetBuildings<Airport>()
                .Where(airport =>
                    gameManager.tileSystem.TryGetLand(
                        airport.TileId,
                        out var landTile)
                    && IsHostile(observerAlliance, landTile.Controller))
                .Select(airport => new ObservedEnemyAirportSnapshot
                {
                    AirportBuildingId = airport.BuildingId,
                    AirportTileId = airport.TileId,
                    PositionFeet = airport.PositionFeet,
                    InformationQuality = MaximumInformationQuality,
                    ObservedAt = observedAt,
                    Condition = GetObservedCondition(airport),
                    BuildLevel = airport.Level.BuildLevel,
                    FunctionalLevel = airport.FunctionalLevel,
                    TargetToughness = airport.TargetToughness,
                    OperationalRunwayChannelCount =
                        airport.OperationalRunwayChannelCount,
                    RunwayChannels = airport.RunwayChannels
                        .OrderBy(channel => channel.ChannelIndex)
                        .Select(channel => new ObservedRunwayChannel
                        {
                            ChannelIndex = channel.ChannelIndex,
                            DamageLevel = channel.DamageLevel
                        })
                        .ToList(),
                    AircraftGroups = GetObservedAircraft(
                        gameManager,
                        observerAlliance,
                        airport.BuildingId,
                        airborneAircraftIds)
                })
                .OrderBy(report => report.AirportTileId.x)
                .ThenBy(report => report.AirportTileId.y)
                .ThenBy(report => report.AirportTileId.z)
                .ToList();
        }

        private static List<ObservedAircraftGroup> GetObservedAircraft(
            GameManager gameManager,
            Alliance observerAlliance,
            Guid airportBuildingId,
            ISet<Guid> airborneAircraftIds)
        {
            return gameManager.squadronSystem.Squadrons
                .Where(squadron =>
                    squadron.AirportBuildingId == airportBuildingId
                    && IsHostile(
                        observerAlliance,
                        gameManager.GetCountryAlliance(squadron.CountryId)))
                .SelectMany(squadron => squadron.Aircraft)
                .Where(aircraft =>
                    aircraft.Status != CampaignAircraftStatus.Lost
                    && !airborneAircraftIds.Contains(aircraft.AircraftId))
                .GroupBy(aircraft => aircraft.AircraftTypeDefinitionId)
                .Select(group => new ObservedAircraftGroup
                {
                    AircraftTypeDefinitionId = group.Key,
                    AircraftOnGroundCount = group.Count(),
                    ApparentlyAvailableCount =
                        group.Count(IsApparentlyAvailable)
                })
                .OrderBy(group => group.AircraftTypeDefinitionId)
                .ToList();
        }

        private static bool IsApparentlyAvailable(CampaignAircraft aircraft)
        {
            return aircraft.Status == CampaignAircraftStatus.Ready
                   || aircraft.Status == CampaignAircraftStatus.Assigned;
        }

        private static ObservedAirportCondition GetObservedCondition(
            Airport airport)
        {
            if (!airport.IsRunwaySystemOperational)
                return ObservedAirportCondition.NonFunctional;

            return airport.RunwayDamage > 0
                ? ObservedAirportCondition.Damaged
                : ObservedAirportCondition.Intact;
        }

        private static bool IsHostile(
            Alliance observerAlliance,
            Alliance subjectAlliance)
        {
            return observerAlliance != Alliance.Neutral
                   && subjectAlliance != Alliance.Neutral
                   && observerAlliance != subjectAlliance;
        }
    }
}
