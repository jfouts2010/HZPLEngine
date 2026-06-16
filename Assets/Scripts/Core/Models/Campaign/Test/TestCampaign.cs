using System;
using System.Collections.Generic;
using Models.Module;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    public static class TestCampaign
    {
        public const string Name = "Mechanics Test Campaign";

        private static readonly Guid BlueCountryId = TestModule.BlueCountryId;
        private static readonly Guid RedCountryId = TestModule.RedCountryId;
        private static readonly Guid NeutralCountryId = TestModule.NeutralCountryId;
        private static readonly Guid BlueDivisionId = Guid.Parse("2fa9789e-af9a-489f-8ec0-7a2f8d7c0039");
        private static readonly Guid BlueReserveDivisionId = Guid.Parse("f1e7e0ac-0e8d-4d1a-a6c1-00c8a883bb42");
        private static readonly Guid RedDivisionId = Guid.Parse("713f4192-5ed1-4cb5-b399-8d5c5786dc0a");

        private static readonly Vector3Int BlueCapitalTileId = new Vector3Int(0, 0, 0);
        private static readonly Vector3Int BluePortTileId = new Vector3Int(1, -1, 0);
        private static readonly Vector3Int RedBorderTileId = new Vector3Int(2, -2, 0);
        private static readonly Vector3Int RedMountainTileId = new Vector3Int(1, -2, 1);
        private static readonly Vector3Int NeutralHubTileId = new Vector3Int(0, -1, 1);
        private static readonly Vector3Int OceanTileId = new Vector3Int(2, -1, -1);

        public static CampaignTemplate Create()
        {
            var template = new CampaignTemplate(Name)
            {
                ModuleId = TestModule.Id,
                CampaignStartTime = new DateTime(1990, 1, 1, 6, 0, 0),
                SimulationSettings = new SimulationSettings
                {
                    SimulationTickMinutes = 5,
                    OperationalCadenceHours = 6
                },
                ContentHash = "mechanics-test-campaign-v3",
                CountryAllianceAssignments = CreateCountryAllianceAssignments(),
                Tiles = CreateTiles(),
                StartingTileData = CreateStartingTileData(),
                BuildingStartingConditions = CreateBuildingStartingConditions(),
                DivisionStartingConditions = CreateDivisionStartingConditions()
            };

            template.RebuildDerivedData();
            return template;
        }

        private static List<CountryAllianceAssignment> CreateCountryAllianceAssignments()
        {
            return new List<CountryAllianceAssignment>
            {
                new CountryAllianceAssignment { CountryId = BlueCountryId, Alliance = Alliance.Bluefor },
                new CountryAllianceAssignment { CountryId = RedCountryId, Alliance = Alliance.Redfor },
                new CountryAllianceAssignment { CountryId = NeutralCountryId, Alliance = Alliance.Neutral }
            };
        }

        private static List<Tile> CreateTiles()
        {
            var blueCapital = CreateLandTile(
                BlueCapitalTileId,
                TileTerrain.Plains,
                Urbanization.Urban,
                ForestCover.None);

            var bluePort = CreateLandTile(
                BluePortTileId,
                TileTerrain.Coast,
                Urbanization.Suburban,
                ForestCover.Light);

            var redBorder = CreateLandTile(
                RedBorderTileId,
                TileTerrain.Hills,
                Urbanization.Rural,
                ForestCover.Light);

            var redMountain = CreateLandTile(
                RedMountainTileId,
                TileTerrain.Mountain,
                Urbanization.Rural,
                ForestCover.Heavy);

            var neutralHub = CreateLandTile(
                NeutralHubTileId,
                TileTerrain.Plains,
                Urbanization.Rural,
                ForestCover.None);

            var ocean = new Tile
            {
                Coordinates = OceanTileId,
                Surface = TileSurface.Ocean,
                Terrain = TileTerrain.ShallowOcean,
                Urbanization = Urbanization.None,
                ForestCover = ForestCover.None
            };

            var tiles = new List<Tile>
            {
                blueCapital,
                bluePort,
                redBorder,
                redMountain,
                neutralHub,
                ocean
            };

            neutralHub.RiverNeighborTileIds.Add(RedMountainTileId);
            redMountain.RiverNeighborTileIds.Add(NeutralHubTileId);

            return tiles;
        }

        private static List<TileData> CreateStartingTileData()
        {
            return new List<TileData>
            {
                new LandTileData
                {
                    TileId = BlueCapitalTileId,
                    Controller = Alliance.Bluefor,
                    Infrastructure = new BuildingLevel(8)
                },
                new LandTileData
                {
                    TileId = BluePortTileId,
                    Controller = Alliance.Bluefor,
                    Infrastructure = new BuildingLevel(6, 1)
                },
                new LandTileData
                {
                    TileId = RedBorderTileId,
                    Controller = Alliance.Redfor,
                    Infrastructure = new BuildingLevel(4, 2)
                },
                new LandTileData
                {
                    TileId = RedMountainTileId,
                    Controller = Alliance.Redfor,
                    Infrastructure = new BuildingLevel(2)
                },
                new LandTileData
                {
                    TileId = NeutralHubTileId,
                    Controller = Alliance.Neutral,
                    Infrastructure = new BuildingLevel(5)
                },
                new OceanTileData
                {
                    TileId = OceanTileId
                }
            };
        }

        private static List<BuildingStartingCondition> CreateBuildingStartingConditions()
        {
            return new List<BuildingStartingCondition>
            {
                CreateBuilding("eeed9ddf-c652-4cb4-a98b-38848ff10088", BlueCapitalTileId, BuildingType.Factory, 7),
                CreateBuilding("6ffcc54e-747e-487e-9885-dfdb60add354", BlueCapitalTileId, BuildingType.Airport, 5, 1),
                CreateBuilding("0f102fc4-c2cd-4ca8-b4f3-b3c2dfb7e1d4", BluePortTileId, BuildingType.Port, 6),
                CreateBuilding("20bc594a-5599-4df1-896c-819908cd082f", BluePortTileId, BuildingType.Refinery, 4),
                CreateBuilding("f8ca8166-caba-4d24-ac8c-896127b0ec91", NeutralHubTileId, BuildingType.SupplyHub, 5),
                CreateBuilding("873f8560-bf69-47ae-b82f-e1f8b3e989c4", RedBorderTileId, BuildingType.Fort, 3, 1),
                CreateBuilding("31ab8776-4af7-4533-b19f-fc0d4eaf060f", RedBorderTileId, BuildingType.Railroad, 4),
                CreateBuilding("d30ad13b-82a8-4f4c-a990-08614182716d", RedMountainTileId, BuildingType.PowerPlant, 2)
            };
        }

        private static List<DivisionStartingCondition> CreateDivisionStartingConditions()
        {
            return new List<DivisionStartingCondition>
            {
                new DivisionStartingCondition
                {
                    DivisionId = BlueDivisionId,
                    DivisionTemplateId = TestModule.BlueArmoredDivisionTemplateId,
                    CountryId = BlueCountryId,
                    TileId = BlueCapitalTileId,
                    Name = "1st Blue Armored Division"
                },
                new DivisionStartingCondition
                {
                    DivisionId = BlueReserveDivisionId,
                    DivisionTemplateId = TestModule.BlueArmoredDivisionTemplateId,
                    CountryId = BlueCountryId,
                    TileId = BlueCapitalTileId,
                    Name = "2nd Blue Reserve Division"
                },
                new DivisionStartingCondition
                {
                    DivisionId = RedDivisionId,
                    DivisionTemplateId = TestModule.RedTankDivisionTemplateId,
                    CountryId = RedCountryId,
                    TileId = RedMountainTileId,
                    Name = "1st Red Tank Division"
                }
            };
        }

        private static Tile CreateLandTile(
            Vector3Int tileId,
            TileTerrain terrain,
            Urbanization urbanization,
            ForestCover forestCover)
        {
            return new Tile
            {
                Coordinates = tileId,
                Surface = TileSurface.Land,
                Terrain = terrain,
                Urbanization = urbanization,
                ForestCover = forestCover
            };
        }

        private static BuildingStartingCondition CreateBuilding(
            string buildingId,
            Vector3Int tileId,
            BuildingType type,
            int level,
            int damage = 0)
        {
            return new BuildingStartingCondition
            {
                BuildingId = Guid.Parse(buildingId),
                TileId = tileId,
                Type = type,
                Level = new BuildingLevel(level, damage)
            };
        }
    }
}
