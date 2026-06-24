using System;
using System.Collections.Generic;
using Models.Module;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    public static class AdvancedTestCampaign
    {
        public const string Name = "Advanced Mechanics Test Campaign";

        // Radius-3 hex disc: 37 tiles (within the 20-40 target).
        private const int HexRadius = 3;
        private const int DivisionsPerFrontTile = 3;

        private static readonly Guid BlueCountryId = TestModule.BlueCountryId;
        private static readonly Guid RedCountryId = TestModule.RedCountryId;
        private static readonly Guid NeutralCountryId = TestModule.NeutralCountryId;
        private static readonly Guid[] BlueFrontDivisionIds =
        {
            Guid.Parse("a4c2e8f1-3b7d-4e91-9f06-1d5a8c2b0473"),
            Guid.Parse("4aca313d-8ad0-4aa7-bd71-17b0adc3d85c"),
            Guid.Parse("83924aba-bdba-4e35-85ed-1c1e64609bd7"),
            Guid.Parse("c3d4e5f6-7890-4abc-def1-234567890abc"),
            Guid.Parse("f4c047bf-4b74-4aa4-bb4b-dcf1abe53254"),
            Guid.Parse("20981a2a-ba4c-4778-bfe7-fea0fe0b6881"),
            Guid.Parse("d4e5f6a7-8901-4bcd-ef12-345678901bcd"),
            Guid.Parse("1b7902f4-3716-4761-b4fe-ed2a2f4d21c3"),
            Guid.Parse("f6577e75-fed4-4189-8ea6-401dc4f6264e")
        };

        private static readonly Guid[] RedFrontDivisionIds =
        {
            Guid.Parse("b8d3f9a2-6c4e-4f12-8a17-2e6b9d3c1584"),
            Guid.Parse("e4a17cef-7f0e-4748-a88b-f71c42738737"),
            Guid.Parse("6cd06517-ced0-4a33-ac42-36f21124df1e"),
            Guid.Parse("e5f6a7b8-9012-4cde-f123-456789012cde"),
            Guid.Parse("dc41ff3f-ff9c-47ce-918a-4b41d54dfb78"),
            Guid.Parse("d0692c0c-b5f8-4643-b327-339d06559bd9"),
            Guid.Parse("f6a7b8c9-0123-4def-0123-567890123def"),
            Guid.Parse("08edfbcc-222e-4db9-b3d2-852ad0be7d6f"),
            Guid.Parse("95e65782-7075-4400-925a-d4a0c6f462be")
        };

        private static readonly Vector3Int BlueCapitalTileId = new Vector3Int(-3, 3, 0);
        private static readonly Vector3Int RedCapitalTileId = new Vector3Int(3, 0, -3);
        private static readonly Vector3Int NeutralHubTileId = new Vector3Int(0, 0, 0);
        private static readonly Vector3Int[] BlueFrontTileIds =
        {
            new Vector3Int(0, 1, -1),
            new Vector3Int(0, 2, -2),
            new Vector3Int(0, 3, -3)
        };
        private static readonly Vector3Int[] RedFrontTileIds =
        {
            new Vector3Int(1, 0, -1),
            new Vector3Int(1, 1, -2),
            new Vector3Int(1, 2, -3)
        };
        private static readonly Vector3Int BluePortTileId = new Vector3Int(-2, 2, 0);
        private static readonly Vector3Int RedRefineryTileId = new Vector3Int(2, 1, -3);

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
                ContentHash = "advanced-mechanics-test-campaign-v6",
                CountryAllianceAssignments = CreateCountryAllianceAssignments(),
                OrdnanceAllowances = CreateOrdnanceAllowances(),
                Tiles = CreateTiles(),
                StartingTileData = CreateStartingTileData(),
                SupplyCapitals = CreateSupplyCapitals(),
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

        private static Dictionary<Alliance, List<Guid>> CreateOrdnanceAllowances()
        {
            return new Dictionary<Alliance, List<Guid>>
            {
                {
                    Alliance.Bluefor,
                    new List<Guid>
                    {
                        TestModule.Aim120OrdnanceTypeId,
                        TestModule.Aim9OrdnanceTypeId,
                        TestModule.Agm88OrdnanceTypeId,
                        TestModule.Gbu38OrdnanceTypeId,
                        TestModule.Agm65OrdnanceTypeId
                    }
                },
                {
                    Alliance.Redfor,
                    new List<Guid>
                    {
                        TestModule.R27OrdnanceTypeId,
                        TestModule.R73OrdnanceTypeId
                    }
                }
            };
        }

        private static List<SupplyCapitalStartingCondition> CreateSupplyCapitals()
        {
            return new List<SupplyCapitalStartingCondition>
            {
                new SupplyCapitalStartingCondition { Alliance = Alliance.Bluefor, TileId = BlueCapitalTileId },
                new SupplyCapitalStartingCondition { Alliance = Alliance.Redfor, TileId = RedCapitalTileId }
            };
        }

        private static List<Vector3Int> GenerateDiscTileIds()
        {
            var tileIds = new List<Vector3Int>();

            for (var x = -HexRadius; x <= HexRadius; x++)
            {
                var minY = Math.Max(-HexRadius, -x - HexRadius);
                var maxY = Math.Min(HexRadius, -x + HexRadius);

                for (var y = minY; y <= maxY; y++)
                {
                    var z = -x - y;
                    if (Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z)) <= HexRadius)
                        tileIds.Add(new Vector3Int(x, y, z));
                }
            }

            return tileIds;
        }

        // Western block (x <= -1) and a southern x=0 salient are blue.
        // Eastern block (x >= 1) is red. Northern arc (z >= 2) plus the center column are neutral.
        // Blue and red meet along the full southern x=0 / x=1 edge (three contiguous contact tiles).
        private static Alliance AssignAlliance(Vector3Int tileId)
        {
            if (tileId.z >= 2)
                return Alliance.Neutral;

            if (tileId.x == 0 && tileId.z >= 0)
                return Alliance.Neutral;

            if (tileId.x >= 1)
                return Alliance.Redfor;

            if (tileId.x <= -1)
                return Alliance.Bluefor;

            return Alliance.Bluefor;
        }

        private static List<Tile> CreateTiles()
        {
            var tiles = new List<Tile>();

            foreach (var tileId in GenerateDiscTileIds())
            {
                var alliance = AssignAlliance(tileId);
                tiles.Add(CreateLandTile(tileId, alliance));
            }

            var riverTile = new Vector3Int(-1, 1, 0);
            var riverNeighbor = NeutralHubTileId;
            var riverTileDefinition = tiles.Find(tile => tile.Coordinates == riverTile);
            var riverNeighborDefinition = tiles.Find(tile => tile.Coordinates == riverNeighbor);
            riverTileDefinition?.RiverNeighborTileIds.Add(riverNeighbor);
            riverNeighborDefinition?.RiverNeighborTileIds.Add(riverTile);

            return tiles;
        }

        private static List<TileData> CreateStartingTileData()
        {
            var startingTileData = new List<TileData>();

            foreach (var tileId in GenerateDiscTileIds())
            {
                var alliance = AssignAlliance(tileId);
                startingTileData.Add(new LandTileData
                {
                    TileId = tileId,
                    Controller = alliance,
                    Infrastructure = CreateInfrastructure(tileId, alliance)
                });
            }

            return startingTileData;
        }

        private static List<BuildingStartingCondition> CreateBuildingStartingConditions()
        {
            return new List<BuildingStartingCondition>
            {
                CreateBuilding("c1a9f4e2-8d3b-4c17-a6f0-2e7b91d4c308", BlueCapitalTileId, BuildingType.Factory, 7),
                CreateBuilding("d2b0a5f3-9e4c-4d28-b701-3f8ca2e5d419", BlueCapitalTileId, BuildingType.Airport, 5),
                CreateBuilding("6c69a6d3-1b42-4f3a-8b42-06b5f601e86f", BlueCapitalTileId, BuildingType.Railroad, 8),
                CreateBuilding("3bf75c64-7ba8-4ed6-9f9e-1c4d9c2266ce", BlueCapitalTileId, BuildingType.SupplyHub, 8),
                CreateBuilding("e3c1b6a4-0f5d-4e39-c812-4a9db3f6e52a", BluePortTileId, BuildingType.Port, 4),
                CreateBuilding("0b3f2aa6-61b7-4b2f-9d80-5aee3d844f91", BluePortTileId, BuildingType.Railroad, 6),
                CreateBuilding("eb695aca-92bd-4e3d-a8b5-d61cb1f6b28e", new Vector3Int(-1, 2, -1), BuildingType.Railroad, 5),
                CreateBuilding("e8fd115d-a93a-4649-95f3-364d5d986a9d", BlueFrontTileIds[0], BuildingType.Railroad, 4),
                CreateBuilding("6b442e5a-9b54-424f-90fc-f4a27a9b8ed9", BlueFrontTileIds[1], BuildingType.Railroad, 4),
                CreateBuilding("9a11758f-a4ac-4758-91df-a8c793ff55ae", BlueFrontTileIds[1], BuildingType.SupplyHub, 4),
                CreateBuilding("1be9955d-4d8b-4b7c-8f03-29b8f6d9aabd", BlueFrontTileIds[2], BuildingType.Railroad, 3),
                CreateBuilding("f4d2c7b5-1a6e-4f4a-d923-5b0ec407f63b", NeutralHubTileId, BuildingType.SupplyHub, 5),
                CreateBuilding("a5e3d8c6-2b7f-405b-e034-6c1fd518074c", BlueFrontTileIds[1], BuildingType.Fort, 3),
                CreateBuilding("b6f4e9d7-3c8a-416c-f145-7d2ae629185d", RedCapitalTileId, BuildingType.PowerPlant, 4),
                CreateBuilding("8f6b7125-5ab8-4791-93be-a3ccdcf823ad", RedCapitalTileId, BuildingType.Railroad, 8),
                CreateBuilding("8ef77ba7-1ca4-4898-8f8e-afbcbe173d11", RedCapitalTileId, BuildingType.SupplyHub, 8),
                CreateBuilding("c7a5f0e8-4d9b-427d-0256-8e3bf73a296e", RedRefineryTileId, BuildingType.Refinery, 3),
                CreateBuilding("c85658f6-45d4-4b4a-a64f-3e1a10f59991", RedRefineryTileId, BuildingType.Railroad, 6),
                CreateBuilding("8b036491-6c29-432d-98b5-c70fb9326712", new Vector3Int(2, 0, -2), BuildingType.Railroad, 5),
                CreateBuilding("a963e4f6-96f8-413c-b092-1d6f435f1fc0", RedFrontTileIds[0], BuildingType.Railroad, 4),
                CreateBuilding("d8b6a1f9-5e0c-438e-1367-9f4c084b3a7f", RedFrontTileIds[1], BuildingType.Railroad, 4),
                CreateBuilding("b8056bb0-5fb5-4d09-b9d4-a4585c43f0f7", RedFrontTileIds[1], BuildingType.SupplyHub, 4),
                CreateBuilding("2f40d216-7eb7-4362-8f31-d519a6a2d585", RedFrontTileIds[2], BuildingType.Railroad, 3)
            };
        }

        private static List<DivisionStartingCondition> CreateDivisionStartingConditions()
        {
            var divisions = new List<DivisionStartingCondition>();

            for (var i = 0; i < BlueFrontTileIds.Length; i++)
            {
                for (var divisionSlot = 0; divisionSlot < DivisionsPerFrontTile; divisionSlot++)
                {
                    var divisionNumber = i * DivisionsPerFrontTile + divisionSlot + 1;
                    divisions.Add(new DivisionStartingCondition
                    {
                        DivisionId = BlueFrontDivisionIds[divisionNumber - 1],
                        DivisionTemplateId = TestModule.BlueArmoredDivisionTemplateId,
                        CountryId = BlueCountryId,
                        TileId = BlueFrontTileIds[i],
                        Name = $"{divisionNumber}{GetOrdinalSuffix(divisionNumber)} Blue Front Division"
                    });
                }
            }

            for (var i = 0; i < RedFrontTileIds.Length; i++)
            {
                for (var divisionSlot = 0; divisionSlot < DivisionsPerFrontTile; divisionSlot++)
                {
                    var divisionNumber = i * DivisionsPerFrontTile + divisionSlot + 1;
                    divisions.Add(new DivisionStartingCondition
                    {
                        DivisionId = RedFrontDivisionIds[divisionNumber - 1],
                        DivisionTemplateId = TestModule.RedTankDivisionTemplateId,
                        CountryId = RedCountryId,
                        TileId = RedFrontTileIds[i],
                        Name = $"{divisionNumber}{GetOrdinalSuffix(divisionNumber)} Red Front Division"
                    });
                }
            }

            return divisions;
        }

        private static string GetOrdinalSuffix(int value)
        {
            return value switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };
        }

        private static Tile CreateLandTile(Vector3Int tileId, Alliance alliance)
        {
            return new Tile
            {
                Coordinates = tileId,
                Surface = TileSurface.Land,
                Terrain = SelectTerrain(tileId, alliance),
                Urbanization = SelectUrbanization(tileId, alliance),
                ForestCover = SelectForestCover(tileId, alliance)
            };
        }

        private static bool IsBlueFrontTile(Vector3Int tileId)
        {
            for (var i = 0; i < BlueFrontTileIds.Length; i++)
            {
                if (BlueFrontTileIds[i] == tileId)
                    return true;
            }

            return false;
        }

        private static bool IsRedFrontTile(Vector3Int tileId)
        {
            for (var i = 0; i < RedFrontTileIds.Length; i++)
            {
                if (RedFrontTileIds[i] == tileId)
                    return true;
            }

            return false;
        }

        private static bool IsFrontLineTile(Vector3Int tileId)
        {
            return IsBlueFrontTile(tileId) || IsRedFrontTile(tileId);
        }

        private static TileTerrain SelectTerrain(Vector3Int tileId, Alliance alliance)
        {
            if (tileId == BlueCapitalTileId)
                return TileTerrain.Plains;

            if (tileId == BluePortTileId)
                return TileTerrain.Coast;

            if (IsFrontLineTile(tileId))
                return TileTerrain.Hills;

            if (tileId == RedCapitalTileId)
                return TileTerrain.Mountain;

            if (alliance == Alliance.Redfor && tileId.x >= 2)
                return TileTerrain.Hills;

            return TileTerrain.Plains;
        }

        private static Urbanization SelectUrbanization(Vector3Int tileId, Alliance alliance)
        {
            if (tileId == BlueCapitalTileId)
                return Urbanization.Urban;

            if (tileId == RedCapitalTileId)
                return Urbanization.Suburban;

            if (IsFrontLineTile(tileId))
                return Urbanization.Rural;

            if (alliance == Alliance.Neutral)
                return Urbanization.Rural;

            return Urbanization.Suburban;
        }

        private static ForestCover SelectForestCover(Vector3Int tileId, Alliance alliance)
        {
            if (tileId == RedCapitalTileId)
                return ForestCover.Heavy;

            if (alliance == Alliance.Neutral)
                return ForestCover.Light;

            if (IsFrontLineTile(tileId) && alliance == Alliance.Bluefor)
                return ForestCover.Light;

            return ForestCover.None;
        }

        private static BuildingLevel CreateInfrastructure(Vector3Int tileId, Alliance alliance)
        {
            if (tileId == BlueCapitalTileId)
                return new BuildingLevel(8);

            if (tileId == RedCapitalTileId)
                return new BuildingLevel(7);

            if (IsBlueFrontTile(tileId) || IsRedFrontTile(tileId))
                return new BuildingLevel(4, 1);

            if (alliance == Alliance.Neutral)
                return new BuildingLevel(5);

            return new BuildingLevel(6);
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
                Level = new BuildingLevel(level, damage),
            };
        }
    }
}
