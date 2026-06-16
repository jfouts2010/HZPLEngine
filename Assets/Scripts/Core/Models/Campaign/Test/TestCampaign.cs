using System;
using System.Collections.Generic;
using Models.Module;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    public static class TestCampaign
    {
        public const string Name = "Mechanics Test Campaign";

        private static readonly Guid BlueCountryId = Guid.Parse("64bfb064-0136-44d3-9983-620cf38d8245");
        private static readonly Guid RedCountryId = Guid.Parse("f6610c73-4f7b-4a71-9695-2f085dc43a7f");
        private static readonly Guid NeutralCountryId = Guid.Parse("ba8ec50b-b305-46ce-b2f6-82a28113f1b5");

        private static readonly Guid BlueCapitalTileId = Guid.Parse("714bf6db-21df-464f-81b2-7381064b1d07");
        private static readonly Guid BluePortTileId = Guid.Parse("25a3b8d7-bc29-4458-b786-c44d7c15b745");
        private static readonly Guid RedBorderTileId = Guid.Parse("452a96a3-b3df-43df-b6e3-f264898d5f1b");
        private static readonly Guid RedMountainTileId = Guid.Parse("aee314d8-44fc-4035-b77d-65248bded801");
        private static readonly Guid NeutralHubTileId = Guid.Parse("8a4b2ab1-5795-4a2f-9866-65be04ff91e4");
        private static readonly Guid OceanTileId = Guid.Parse("df7b7ffc-5e5e-4400-a878-497e34d1b9bb");

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
                BuildingStartingConditions = CreateBuildingStartingConditions()
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
                new Vector3Int(0, 0, 0),
                TileTerrain.Plains,
                Urbanization.Urban,
                ForestCover.None);

            var bluePort = CreateLandTile(
                BluePortTileId,
                new Vector3Int(1, -1, 0),
                TileTerrain.Coast,
                Urbanization.Suburban,
                ForestCover.Light);

            var redBorder = CreateLandTile(
                RedBorderTileId,
                new Vector3Int(2, -2, 0),
                TileTerrain.Hills,
                Urbanization.Rural,
                ForestCover.Light);

            var redMountain = CreateLandTile(
                RedMountainTileId,
                new Vector3Int(1, -2, 1),
                TileTerrain.Mountain,
                Urbanization.Rural,
                ForestCover.Heavy);

            var neutralHub = CreateLandTile(
                NeutralHubTileId,
                new Vector3Int(0, -1, 1),
                TileTerrain.Plains,
                Urbanization.Rural,
                ForestCover.None);

            var ocean = new Tile
            {
                TileId = OceanTileId,
                Coordinates = new Vector3Int(2, -1, -1),
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

        private static Tile CreateLandTile(
            Guid tileId,
            Vector3Int coordinates,
            TileTerrain terrain,
            Urbanization urbanization,
            ForestCover forestCover)
        {
            return new Tile
            {
                TileId = tileId,
                Coordinates = coordinates,
                Surface = TileSurface.Land,
                Terrain = terrain,
                Urbanization = urbanization,
                ForestCover = forestCover
            };
        }

        private static BuildingStartingCondition CreateBuilding(
            string buildingId,
            Guid tileId,
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
