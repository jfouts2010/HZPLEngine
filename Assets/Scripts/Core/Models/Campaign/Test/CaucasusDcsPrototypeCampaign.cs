using System;
using System.Collections.Generic;
using System.Linq;
using Models.Module;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    /// <summary>
    /// A deliberately sparse DCS Caucasus campaign. The complete playable
    /// theater is tiled so neutral territory can be activated later, while
    /// tapered starting territories follow the western Georgian coast and the
    /// inland routes toward Tbilisi and the northern Caucasus airfields.
    /// </summary>
    public static class CaucasusDcsPrototypeCampaign
    {
        public const string Name = "Caucasus DCS Prototype Campaign";

        private const float FeetPerMeter = 3.2808399f;
        private const float TileSpacingMeters = 20000f;
        private const int MinimumCubeX = 9;
        private const int MaximumCubeX = 57;
        private const float MinimumCampaignNorthMeters = -460000f;
        private const float MaximumCampaignNorthMeters = 80000f;
        private const int MinimumControlledLandTilesPerAlliance = 60;
        private const int ExpectedFrontTilesPerAlliance = 4;
        private const int MinimumSquadronBorderStandoffHexes = 5;

        private static readonly Guid BlueCountryId = TestModule.BlueCountryId;
        private static readonly Guid RedCountryId = TestModule.RedCountryId;
        private static readonly Guid NeutralCountryId = TestModule.NeutralCountryId;

        // The x=39/x=40 edge forms the short initial front line. Capitals and
        // air forces sit in the expanded rear areas rather than in this pocket.
        private static readonly Vector3Int BlueCapitalTileId =
            new Vector3Int(33, -5, -28);
        private static readonly Vector3Int RedCapitalTileId =
            new Vector3Int(52, -10, -42);

        private static readonly Vector3Int[] BlueFrontTileIds =
        {
            new Vector3Int(39, -3, -36),
            new Vector3Int(39, -4, -35),
            new Vector3Int(39, -5, -34),
            new Vector3Int(39, -6, -33)
        };

        private static readonly Vector3Int[] RedFrontTileIds =
        {
            new Vector3Int(40, -4, -36),
            new Vector3Int(40, -5, -35),
            new Vector3Int(40, -6, -34),
            new Vector3Int(40, -7, -33)
        };

        // Coarse land outline derived from Briefing Room's Caucasus theater
        // bounds. Coastal airport tiles are explicitly retained as land below.
        private static readonly Vector2[] CaucasusLandOutlineMeters =
        {
            new Vector2(-449641f, 940728f),
            new Vector2(-445171f, 511016f),
            new Vector2(-353043f, 617207f),
            new Vector2(-322496f, 630633f),
            new Vector2(-288499f, 613388f),
            new Vector2(-228020f, 589862f),
            new Vector2(-224941f, 564980f),
            new Vector2(-199197f, 544040f),
            new Vector2(-197596f, 516942f),
            new Vector2(-192546f, 495263f),
            new Vector2(-164954f, 461759f),
            new Vector2(-93020f, 382434f),
            new Vector2(-50032f, 298059f),
            new Vector2(-38946f, 292516f),
            new Vector2(-29092f, 276134f),
            new Vector2(-41533f, 279706f),
            new Vector2(-10739f, 238689f),
            new Vector2(9831f, 186709f),
            new Vector2(23997f, 214916f),
            new Vector2(28924f, 213315f),
            new Vector2(25967f, 198533f),
            new Vector2(40625f, 195824f),
            new Vector2(31264f, 243492f),
            new Vector2(41241f, 260368f),
            new Vector2(68709f, 261476f),
            new Vector2(67963f, 974529f),
            new Vector2(-311999f, 956677f)
        };

        // DCS Caucasus coordinates are north/east in Vector2 x/y. These
        // authored spines replace the old axis-aligned terrain cutoffs so the
        // operational map follows the theater's actual diagonal relief.
        private static readonly Vector2[] GreaterCaucasusRidgeMeters =
        {
            new Vector2(-75000f, 340000f),
            new Vector2(-95000f, 420000f),
            new Vector2(-125000f, 500000f),
            new Vector2(-145000f, 580000f),
            new Vector2(-165000f, 660000f),
            new Vector2(-185000f, 740000f),
            new Vector2(-205000f, 820000f),
            new Vector2(-225000f, 900000f),
            new Vector2(-240000f, 970000f)
        };

        private static readonly Vector2[] LesserCaucasusHighlandsMeters =
        {
            new Vector2(-425000f, 650000f),
            new Vector2(-405000f, 725000f),
            new Vector2(-395000f, 800000f),
            new Vector2(-405000f, 880000f),
            new Vector2(-425000f, 960000f)
        };

        // The broad lowlands are important exceptions to the surrounding
        // relief: western Georgia stays open through Kutaisi, and Tbilisi sits
        // in the Kura basin rather than on the Greater Caucasus ridge.
        private static readonly Vector2[] ColchisLowlandMeters =
        {
            new Vector2(-370000f, 620000f),
            new Vector2(-335000f, 650000f),
            new Vector2(-285000f, 685000f),
            new Vector2(-245000f, 710000f)
        };

        private static readonly Vector2[] KuraLowlandMeters =
        {
            new Vector2(-335000f, 850000f),
            new Vector2(-315000f, 900000f),
            new Vector2(-300000f, 970000f)
        };

        // Starting control follows transport and settlement corridors. Widths
        // taper near the front so expanding the rear areas does not create an
        // implausibly long, undefended contact line.
        private static readonly Vector2[] BlueCoastalTerritorySpineMeters =
        {
            new Vector2(-165000f, 460000f),
            new Vector2(-195000f, 520000f),
            new Vector2(-225000f, 570000f),
            new Vector2(-280000f, 650000f),
            new Vector2(-300000f, 680000f)
        };

        private static readonly Vector2[] BlueSouthernTerritoryBranchMeters =
        {
            new Vector2(-280000f, 650000f),
            new Vector2(-320000f, 635000f),
            new Vector2(-356000f, 618000f)
        };

        private static readonly Vector2[] RedTbilisiTerritorySpineMeters =
        {
            new Vector2(-300000f, 690000f),
            new Vector2(-315000f, 760000f),
            new Vector2(-320000f, 835000f),
            new Vector2(-315000f, 905000f)
        };

        private static readonly Vector2[] RedNorthernTerritoryBranchMeters =
        {
            new Vector2(-270000f, 700000f),
            new Vector2(-220000f, 730000f),
            new Vector2(-165000f, 780000f),
            new Vector2(-130000f, 840000f),
            new Vector2(-85000f, 850000f)
        };

        private static readonly AirbaseDefinition[] Airbases =
        {
            new AirbaseDefinition(12, "Anapa-Vityazevo", -4448f, 244022f),
            new AirbaseDefinition(13, "Krasnodar-Center", 11653f, 366766f),
            new AirbaseDefinition(14, "Novorossiysk", -40299f, 279854f),
            new AirbaseDefinition(15, "Krymsk", -7349f, 293712f),
            new AirbaseDefinition(16, "Maykop-Khanskaya", -27626f, 457048f),
            new AirbaseDefinition(17, "Gelendzhik", -50996f, 297849f),
            new AirbaseDefinition(18, "Sochi-Adler", -165163f, 460902f),
            new AirbaseDefinition(19, "Krasnodar-Pashkovsky", 8707f, 388986f),
            new AirbaseDefinition(20, "Sukhumi-Babushara", -221382f, 565909f),
            new AirbaseDefinition(21, "Gudauta", -195651f, 515899f),
            new AirbaseDefinition(22, "Batumi", -356437f, 618211f),
            new AirbaseDefinition(23, "Senaki-Kolkhi", -281903f, 648379f),
            new AirbaseDefinition(24, "Kobuleti", -317605f, 636704f),
            new AirbaseDefinition(25, "Kutaisi", -284583f, 685030f),
            new AirbaseDefinition(26, "Mineralnye Vody", -52090f, 707418f),
            new AirbaseDefinition(27, "Nalchik", -125488f, 759560f),
            new AirbaseDefinition(28, "Mozdok", -83330f, 835635f),
            new AirbaseDefinition(29, "Tbilisi-Lochini", -314926f, 895724f),
            new AirbaseDefinition(30, "Soganlug", -318657f, 896275f),
            new AirbaseDefinition(31, "Vaziani", -318192f, 902332f),
            new AirbaseDefinition(32, "Beslan", -148472f, 842252f)
        };

        private static readonly Vector3Int[] NeighborOffsets =
        {
            new Vector3Int(1, -1, 0),
            new Vector3Int(1, 0, -1),
            new Vector3Int(0, 1, -1),
            new Vector3Int(-1, 1, 0),
            new Vector3Int(-1, 0, 1),
            new Vector3Int(0, -1, 1)
        };

        public static CampaignTemplate Create()
        {
            var tiles = CreateTiles();
            var template = new CampaignTemplate(Name)
            {
                ModuleId = DcsPrototypeModule.Id,
                CampaignStartTime = new DateTime(1990, 1, 1, 6, 0, 0),
                SimulationSettings = new SimulationSettings
                {
                    SimulationTickMinutes = 5,
                    OperationalCadenceHours = 6
                },
                ContentHash = "caucasus-dcs-prototype-v3",
                CountryAllianceAssignments = CreateCountryAllianceAssignments(),
                OrdnanceAllowances = CreateOrdnanceAllowances(),
                SamSiteTemplateAllowances = CreateSamSiteTemplateAllowances(),
                Tiles = tiles,
                StartingTileData = CreateStartingTileData(tiles),
                SupplyCapitals = CreateSupplyCapitals(),
                BuildingStartingConditions = CreateBuildingStartingConditions(),
                DivisionStartingConditions = CreateDivisionStartingConditions(),
                MobileSamSiteStartingConditions = CreateMobileSamSiteStartingConditions(),
                SquadronStartingConditions = CreateSquadronStartingConditions()
            };

            template.RebuildDerivedData();
            ValidateTemplate(template);
            return template;
        }

        public static Guid GetAirbaseBuildingId(int dcsAirbaseId)
        {
            return Guid.Parse($"dca00000-0000-0000-0000-{dcsAirbaseId:D12}");
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
                        TestModule.M61GunOrdnanceTypeId,
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
                        TestModule.R73OrdnanceTypeId,
                        TestModule.Gsh301GunOrdnanceTypeId
                    }
                }
            };
        }

        private static Dictionary<Alliance, List<Guid>> CreateSamSiteTemplateAllowances()
        {
            var templates = new List<Guid>
            {
                TestModule.Sa2SiteTemplateId,
                TestModule.OsaSiteTemplateId
            };
            return new Dictionary<Alliance, List<Guid>>
            {
                { Alliance.Bluefor, new List<Guid>(templates) },
                { Alliance.Redfor, new List<Guid>(templates) }
            };
        }

        private static List<SupplyCapitalStartingCondition> CreateSupplyCapitals()
        {
            return new List<SupplyCapitalStartingCondition>
            {
                new SupplyCapitalStartingCondition
                {
                    Alliance = Alliance.Bluefor,
                    TileId = BlueCapitalTileId
                },
                new SupplyCapitalStartingCondition
                {
                    Alliance = Alliance.Redfor,
                    TileId = RedCapitalTileId
                }
            };
        }

        private static List<Tile> CreateTiles()
        {
            var airbaseTiles = new HashSet<Vector3Int>(
                Airbases.Select(airbase =>
                    CampaignMapCoordinates.TileCoordinateFromPositionFeet(airbase.PositionFeet)));
            var tiles = new List<Tile>();

            foreach (var tileId in GenerateFootprintTileIds())
            {
                var center = CampaignMapCoordinates.TileCenterFeet(tileId);
                // DCS Caucasus x runs north/south and y runs east/west.
                // Campaign x is east/west and campaign z is north/south so
                // north appears at the top of the rendered map.
                var dcsPositionMeters = CampaignPositionToDcsMeters(center);
                var isLand = airbaseTiles.Contains(tileId)
                             || PointIsInsideLandOutline(dcsPositionMeters);

                var terrain = isLand
                    ? SelectInteriorTerrain(dcsPositionMeters)
                    : TileTerrain.Ocean;
                tiles.Add(new Tile
                {
                    Coordinates = tileId,
                    Surface = isLand ? TileSurface.Land : TileSurface.Ocean,
                    Terrain = terrain,
                    Urbanization = airbaseTiles.Contains(tileId)
                        ? Urbanization.Suburban
                        : isLand ? Urbanization.Rural : Urbanization.None,
                    ForestCover = isLand
                        ? SelectForestCover(dcsPositionMeters, terrain)
                        : ForestCover.None
                });
            }

            MarkCoastalTiles(tiles);
            return tiles;
        }

        private static IEnumerable<Vector3Int> GenerateFootprintTileIds()
        {
            for (var x = MinimumCubeX; x <= MaximumCubeX; x++)
            {
                var minimumZ = (int)Math.Ceiling(
                    MinimumCampaignNorthMeters / TileSpacingMeters - x * 0.5f);
                var maximumZ = (int)Math.Floor(
                    MaximumCampaignNorthMeters / TileSpacingMeters - x * 0.5f);

                for (var z = minimumZ; z <= maximumZ; z++)
                    yield return new Vector3Int(x, -x - z, z);
            }
        }

        private static bool PointIsInsideLandOutline(Vector2 point)
        {
            var inside = false;
            for (int i = 0, j = CaucasusLandOutlineMeters.Length - 1;
                 i < CaucasusLandOutlineMeters.Length;
                 j = i++)
            {
                var current = CaucasusLandOutlineMeters[i];
                var previous = CaucasusLandOutlineMeters[j];
                var crossesLatitude = current.y > point.y != previous.y > point.y;
                if (!crossesLatitude)
                    continue;

                var boundaryX = (previous.x - current.x)
                                * (point.y - current.y)
                                / (previous.y - current.y)
                                + current.x;
                if (point.x < boundaryX)
                    inside = !inside;
            }

            return inside;
        }

        private static TileTerrain SelectInteriorTerrain(Vector2 positionMeters)
        {
            if (DistanceToPolyline(positionMeters, ColchisLowlandMeters) <= 36000f
                || DistanceToPolyline(positionMeters, KuraLowlandMeters) <= 30000f)
                return TileTerrain.Plains;

            var greaterCaucasusDistance =
                DistanceToPolyline(positionMeters, GreaterCaucasusRidgeMeters);
            var lesserCaucasusDistance =
                DistanceToPolyline(positionMeters, LesserCaucasusHighlandsMeters);
            if (greaterCaucasusDistance <= 43000f || lesserCaucasusDistance <= 40000f)
                return TileTerrain.Mountain;

            // The range drops sharply to the Russian steppe on its northern
            // side, while broader foothills descend toward Georgia.
            var greaterCaucasusHillWidth = IsNorthOfGreaterCaucasusRidge(positionMeters)
                ? 75000f
                : 95000f;
            if (greaterCaucasusDistance <= greaterCaucasusHillWidth
                || lesserCaucasusDistance <= 80000f)
                return TileTerrain.Hills;
            return TileTerrain.Plains;
        }

        private static ForestCover SelectForestCover(
            Vector2 positionMeters,
            TileTerrain terrain)
        {
            // Kuban and the northern approaches are predominantly open steppe.
            if (positionMeters.x > -60000f && positionMeters.y < 680000f)
                return ForestCover.None;

            // The Tbilisi/Kura basin is markedly drier than western Georgia.
            if (positionMeters.y > 820000f && positionMeters.x < -260000f)
                return ForestCover.None;

            if (DistanceToPolyline(positionMeters, ColchisLowlandMeters) <= 50000f)
                return ForestCover.Light;

            // Forest is concentrated on the wetter western slopes and thins
            // toward the central/eastern Caucasus and southern highlands.
            if (terrain == TileTerrain.Mountain)
                return positionMeters.y < 760000f
                    ? ForestCover.Heavy
                    : ForestCover.Light;
            if (terrain == TileTerrain.Hills)
                return positionMeters.y < 780000f
                    ? ForestCover.Heavy
                    : ForestCover.Light;

            var isWesternGeorgianLowland = positionMeters.y >= 430000f
                                           && positionMeters.y <= 760000f
                                           && positionMeters.x < -130000f;
            return isWesternGeorgianLowland
                ? ForestCover.Light
                : ForestCover.None;
        }

        private static float DistanceToPolyline(Vector2 point, Vector2[] polyline)
        {
            var nearestDistance = float.MaxValue;
            for (var index = 1; index < polyline.Length; index++)
            {
                nearestDistance = Mathf.Min(
                    nearestDistance,
                    DistanceToSegment(point, polyline[index - 1], polyline[index]));
            }

            return nearestDistance;
        }

        private static bool IsNorthOfGreaterCaucasusRidge(Vector2 point)
        {
            if (point.y <= GreaterCaucasusRidgeMeters[0].y)
                return point.x >= GreaterCaucasusRidgeMeters[0].x;

            for (var index = 1; index < GreaterCaucasusRidgeMeters.Length; index++)
            {
                var start = GreaterCaucasusRidgeMeters[index - 1];
                var end = GreaterCaucasusRidgeMeters[index];
                if (point.y > end.y)
                    continue;

                var progress = Mathf.InverseLerp(start.y, end.y, point.y);
                return point.x >= Mathf.Lerp(start.x, end.x, progress);
            }

            return point.x >= GreaterCaucasusRidgeMeters[
                GreaterCaucasusRidgeMeters.Length - 1].x;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            var segment = end - start;
            var lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
                return Vector2.Distance(point, start);

            var progress = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * progress);
        }

        private static void MarkCoastalTiles(List<Tile> tiles)
        {
            var byId = tiles.ToDictionary(tile => tile.Coordinates);
            foreach (var tile in tiles)
            {
                if (tile.Surface != TileSurface.Land)
                    continue;

                if (NeighborOffsets.Any(offset =>
                        byId.TryGetValue(tile.Coordinates + offset, out var neighbor)
                        && neighbor.Surface == TileSurface.Ocean))
                    tile.Terrain = TileTerrain.Coast;
            }
        }

        private static List<TileData> CreateStartingTileData(IEnumerable<Tile> tiles)
        {
            var data = new List<TileData>();
            foreach (var tile in tiles)
            {
                if (tile.Surface == TileSurface.Ocean)
                {
                    data.Add(new OceanTileData { TileId = tile.Coordinates });
                    continue;
                }

                var controller = AssignController(tile.Coordinates);
                data.Add(new LandTileData
                {
                    TileId = tile.Coordinates,
                    Controller = controller,
                    Infrastructure = new BuildingLevel(
                        controller == Alliance.Neutral ? 1 : 4)
                });
            }

            return data;
        }

        private static Alliance AssignController(Vector3Int tileId)
        {
            var positionMeters = CampaignPositionToDcsMeters(
                CampaignMapCoordinates.TileCenterFeet(tileId));

            if (tileId.x <= 39)
            {
                var coastalWidth = positionMeters.y >= 640000f
                    ? 35000f
                    : 70000f;
                var southernBranchWidth = positionMeters.y >= 660000f
                    ? 25000f
                    : 50000f;
                if (DistanceToPolyline(
                        positionMeters,
                        BlueCoastalTerritorySpineMeters) <= coastalWidth
                    || DistanceToPolyline(
                        positionMeters,
                        BlueSouthernTerritoryBranchMeters) <= southernBranchWidth)
                    return Alliance.Bluefor;
            }

            if (tileId.x >= 40)
            {
                var inlandWidth = positionMeters.y <= 740000f
                    ? 35000f
                    : 75000f;
                if (DistanceToPolyline(
                        positionMeters,
                        RedTbilisiTerritorySpineMeters) <= inlandWidth
                    || DistanceToPolyline(
                        positionMeters,
                        RedNorthernTerritoryBranchMeters) <= inlandWidth)
                    return Alliance.Redfor;
            }

            return Alliance.Neutral;
        }

        private static Vector2 CampaignPositionToDcsMeters(Vector3 positionFeet)
        {
            return new Vector2(
                positionFeet.z / FeetPerMeter,
                positionFeet.x / FeetPerMeter);
        }

        private static int HexDistance(Vector3Int first, Vector3Int second)
        {
            return Math.Max(
                Math.Max(Math.Abs(first.x - second.x), Math.Abs(first.y - second.y)),
                Math.Abs(first.z - second.z));
        }

        private static List<BuildingStartingCondition> CreateBuildingStartingConditions()
        {
            var buildings = Airbases.Select(airbase => new BuildingStartingCondition
                {
                    BuildingId = GetAirbaseBuildingId(airbase.DcsId),
                    ThirdPartyId = airbase.DcsId.ToString(),
                    PositionFeet = airbase.PositionFeet,
                    Type = BuildingType.Airport,
                    Level = new BuildingLevel(5)
                })
                .ToList();

            buildings.Add(CreateBuilding(
                "dcb00000-0000-0000-0000-000000000001",
                BlueCapitalTileId,
                BuildingType.SupplyHub,
                7));
            buildings.Add(CreateBuilding(
                "dcb00000-0000-0000-0000-000000000002",
                RedCapitalTileId,
                BuildingType.SupplyHub,
                7));
            buildings.Add(CreateBuilding(
                "dcb00000-0000-0000-0000-000000000003",
                BlueCapitalTileId,
                BuildingType.Factory,
                4));
            buildings.Add(CreateBuilding(
                "dcb00000-0000-0000-0000-000000000004",
                RedCapitalTileId,
                BuildingType.PowerPlant,
                4));
            buildings.Add(CreateStaticSamBuilding(
                "dcc00000-0000-0000-0000-000000000001",
                BlueCapitalTileId));
            buildings.Add(CreateStaticSamBuilding(
                "dcc00000-0000-0000-0000-000000000002",
                RedCapitalTileId));

            return buildings;
        }

        private static BuildingStartingCondition CreateBuilding(
            string id,
            Vector3Int tileId,
            BuildingType type,
            int level)
        {
            return new BuildingStartingCondition
            {
                BuildingId = Guid.Parse(id),
                PositionFeet = CampaignMapCoordinates.TileCenterFeet(tileId),
                Type = type,
                Level = new BuildingLevel(level)
            };
        }

        private static BuildingStartingCondition CreateStaticSamBuilding(
            string id,
            Vector3Int tileId)
        {
            return new BuildingStartingCondition
            {
                BuildingId = Guid.Parse(id),
                PositionFeet = CampaignMapCoordinates.TileCenterFeet(tileId),
                Type = BuildingType.AirDefense,
                Level = new BuildingLevel(1),
                SamSiteTemplateId = TestModule.Sa2SiteTemplateId
            };
        }

        private static List<DivisionStartingCondition> CreateDivisionStartingConditions()
        {
            var divisions = new List<DivisionStartingCondition>();
            for (var i = 0; i < BlueFrontTileIds.Length; i++)
            {
                divisions.Add(new DivisionStartingCondition
                {
                    DivisionId = CreateSequencedGuid("dcd00000", i + 1),
                    DivisionTemplateId = TestModule.BlueArmoredDivisionTemplateId,
                    CountryId = BlueCountryId,
                    TileId = BlueFrontTileIds[i],
                    Name = $"Blue Armored Division {i + 1}"
                });
                divisions.Add(new DivisionStartingCondition
                {
                    DivisionId = CreateSequencedGuid("dcd00000", i + 5),
                    DivisionTemplateId = TestModule.RedTankDivisionTemplateId,
                    CountryId = RedCountryId,
                    TileId = RedFrontTileIds[i],
                    Name = $"Red Tank Division {i + 1}"
                });
            }

            return divisions;
        }

        private static List<MobileSamSiteStartingCondition> CreateMobileSamSiteStartingConditions()
        {
            return new List<MobileSamSiteStartingCondition>
            {
                new MobileSamSiteStartingCondition
                {
                    MobileSamSiteId = CreateSequencedGuid("dce00000", 1),
                    SamSiteTemplateId = TestModule.OsaSiteTemplateId,
                    HostDivisionId = CreateSequencedGuid("dcd00000", 2),
                    Alliance = Alliance.Bluefor
                },
                new MobileSamSiteStartingCondition
                {
                    MobileSamSiteId = CreateSequencedGuid("dce00000", 2),
                    SamSiteTemplateId = TestModule.OsaSiteTemplateId,
                    HostDivisionId = CreateSequencedGuid("dcd00000", 6),
                    Alliance = Alliance.Redfor
                }
            };
        }

        private static List<SquadronStartingCondition> CreateSquadronStartingConditions()
        {
            var sochi = GetAirbaseBuildingId(18);
            var sukhumi = GetAirbaseBuildingId(20);
            var gudauta = GetAirbaseBuildingId(21);
            var mozdok = GetAirbaseBuildingId(28);
            var tbilisi = GetAirbaseBuildingId(29);
            return new List<SquadronStartingCondition>
            {
                CreateSquadron(1, BlueCountryId, TestModule.F16AircraftTypeId, sukhumi, 12, "Blue 1st Fighter Squadron"),
                CreateSquadron(2, BlueCountryId, TestModule.F16AircraftTypeId, gudauta, 12, "Blue 2nd Fighter Squadron"),
                CreateSquadron(3, BlueCountryId, TestModule.E3AircraftTypeId, sochi, 2, "Blue Airborne C2 Squadron"),
                CreateSquadron(4, BlueCountryId, TestModule.Kc135AircraftTypeId, sochi, 2, "Blue Tanker Squadron"),
                CreateSquadron(5, RedCountryId, TestModule.Mig29AircraftTypeId, tbilisi, 24, "Red Fighter Regiment"),
                CreateSquadron(6, RedCountryId, TestModule.A50AircraftTypeId, mozdok, 2, "Red Airborne C2 Squadron"),
                CreateSquadron(7, RedCountryId, TestModule.Il78AircraftTypeId, mozdok, 2, "Red Tanker Squadron")
            };
        }

        private static SquadronStartingCondition CreateSquadron(
            int sequence,
            Guid countryId,
            Guid aircraftTypeId,
            Guid airportId,
            int aircraftCount,
            string name)
        {
            return new SquadronStartingCondition
            {
                SquadronId = CreateSequencedGuid("dcf00000", sequence),
                CountryId = countryId,
                AircraftTypeDefinitionId = aircraftTypeId,
                StartingAirportBuildingId = airportId,
                AircraftCount = aircraftCount,
                Name = name
            };
        }

        private static Guid CreateSequencedGuid(string prefix, int sequence)
        {
            return Guid.Parse($"{prefix}-0000-0000-0000-{sequence:D12}");
        }

        private static void ValidateTemplate(CampaignTemplate template)
        {
            var tilesById = template.Tiles.ToDictionary(tile => tile.Coordinates);
            if (tilesById.Count != template.Tiles.Count)
                throw new InvalidOperationException("The Caucasus template contains duplicate tile coordinates.");
            if (template.StartingTileData.Count != template.Tiles.Count)
                throw new InvalidOperationException("Every Caucasus tile must have starting data.");

            var landData = template.StartingTileData.OfType<LandTileData>().ToList();
            var controlled = landData
                .Where(data => data.Controller == Alliance.Bluefor
                               || data.Controller == Alliance.Redfor)
                .ToList();
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var controlledCount = controlled.Count(data => data.Controller == alliance);
                if (controlledCount < MinimumControlledLandTilesPerAlliance)
                {
                    throw new InvalidOperationException(
                        $"{alliance} must control at least "
                        + $"{MinimumControlledLandTilesPerAlliance} land tiles; found "
                        + $"{controlledCount}.");
                }
            }

            if (controlled.Count == 0)
                throw new InvalidOperationException(
                    "The Caucasus template must contain controlled land territory.");

            foreach (var data in controlled)
            {
                if (!tilesById.TryGetValue(data.TileId, out var tile)
                    || tile.Surface != TileSurface.Land)
                    throw new InvalidOperationException("Controlled territory may only contain land tiles.");
            }

            var airports = template.BuildingStartingConditions
                .Where(building => building.Type == BuildingType.Airport)
                .ToList();
            if (airports.Count != Airbases.Length
                || airports.Select(airport => airport.ThirdPartyId).Distinct().Count() != Airbases.Length)
                throw new InvalidOperationException("All DCS Caucasus airbases must be mapped exactly once.");

            foreach (var building in template.BuildingStartingConditions)
            {
                var tileId = CampaignMapCoordinates.TileCoordinateFromPositionFeet(building.PositionFeet);
                if (!tilesById.TryGetValue(tileId, out var tile) || tile.Surface != TileSurface.Land)
                    throw new InvalidOperationException($"Building {building.BuildingId} is not on a land tile.");
            }

            var dataById = landData.ToDictionary(data => data.TileId);
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var hostileAlliance = alliance == Alliance.Bluefor
                    ? Alliance.Redfor
                    : Alliance.Bluefor;
                var frontTileCount = landData.Count(data =>
                    data.Controller == alliance
                    && NeighborOffsets.Any(offset =>
                        dataById.TryGetValue(data.TileId + offset, out var neighbor)
                        && neighbor.Controller == hostileAlliance));
                if (frontTileCount != ExpectedFrontTilesPerAlliance)
                {
                    throw new InvalidOperationException(
                        $"{alliance} must have exactly {ExpectedFrontTilesPerAlliance} "
                        + $"front tiles; found {frontTileCount}.");
                }
            }

            foreach (var capital in template.SupplyCapitals)
            {
                if (!dataById.TryGetValue(capital.TileId, out var data)
                    || data.Controller != capital.Alliance)
                    throw new InvalidOperationException("Supply capitals must begin in friendly territory.");
            }

            var allianceByCountry = template.CountryAllianceAssignments
                .ToDictionary(assignment => assignment.CountryId, assignment => assignment.Alliance);
            foreach (var division in template.DivisionStartingConditions)
            {
                if (!dataById.TryGetValue(division.TileId, out var data)
                    || !allianceByCountry.TryGetValue(division.CountryId, out var alliance)
                    || data.Controller != alliance)
                    throw new InvalidOperationException($"Division {division.DivisionId} is not in friendly territory.");
            }

            var airportsById = airports.ToDictionary(airport => airport.BuildingId);
            foreach (var squadron in template.SquadronStartingConditions)
            {
                if (!airportsById.TryGetValue(squadron.StartingAirportBuildingId, out var airport))
                    throw new InvalidOperationException($"Squadron {squadron.SquadronId} has no starting airport.");

                var airportTileId = CampaignMapCoordinates.TileCoordinateFromPositionFeet(airport.PositionFeet);
                if (!dataById.TryGetValue(airportTileId, out var data)
                    || !allianceByCountry.TryGetValue(squadron.CountryId, out var alliance)
                    || data.Controller != alliance)
                    throw new InvalidOperationException($"Squadron {squadron.SquadronId} is not at a friendly airport.");

                var hostileAlliance = alliance == Alliance.Bluefor
                    ? Alliance.Redfor
                    : Alliance.Bluefor;
                var nearestHostileDistance = landData
                    .Where(tileData => tileData.Controller == hostileAlliance)
                    .Min(tileData => HexDistance(airportTileId, tileData.TileId));
                if (nearestHostileDistance < MinimumSquadronBorderStandoffHexes)
                {
                    throw new InvalidOperationException(
                        $"Squadron {squadron.SquadronId} starts only "
                        + $"{nearestHostileDistance * TileSpacingMeters / 1000f:0} km "
                        + "from hostile territory.");
                }
            }
        }

        private sealed class AirbaseDefinition
        {
            public int DcsId { get; }
            public string Name { get; }
            public Vector3 PositionFeet { get; }

            public AirbaseDefinition(int dcsId, string name, float dcsX, float dcsY)
            {
                DcsId = dcsId;
                Name = name;
                PositionFeet = new Vector3(
                    dcsY * FeetPerMeter,
                    0f,
                    dcsX * FeetPerMeter);
            }
        }
    }
}
