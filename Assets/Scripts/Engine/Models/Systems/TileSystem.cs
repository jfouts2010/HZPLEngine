using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models
{
    public sealed class TileSystem
    {
        private readonly IReadOnlyList<RuntimeTile> tiles;
        private readonly IReadOnlyList<RuntimeLandTile> landTiles;
        private readonly IReadOnlyDictionary<Vector3Int, RuntimeTile> tilesById;
        private readonly IReadOnlyDictionary<Vector3Int, RuntimeLandTile> landTilesById;

        private TileSystem(
            IReadOnlyList<RuntimeTile> tiles,
            IReadOnlyList<RuntimeLandTile> landTiles,
            IReadOnlyDictionary<Vector3Int, RuntimeTile> tilesById,
            IReadOnlyDictionary<Vector3Int, RuntimeLandTile> landTilesById)
        {
            this.tiles = tiles;
            this.landTiles = landTiles;
            this.tilesById = tilesById;
            this.landTilesById = landTilesById;
        }

        public int Count => tiles.Count;
        public IReadOnlyList<RuntimeTile> Tiles => tiles;
        public IReadOnlyList<RuntimeLandTile> LandTiles => landTiles;

        public static TileSystem Create(
            IReadOnlyList<Tile> templateTiles,
            IReadOnlyList<TileData> startingTileState)
        {
            if (templateTiles == null)
                throw new ArgumentNullException(nameof(templateTiles));
            if (startingTileState == null)
                throw new ArgumentNullException(nameof(startingTileState));

            var errors = new List<string>();
            var geography = CopyGeography(templateTiles, errors);
            var state = CopyState(startingTileState, errors);

            HexGridTopology.AssignNeighbors(geography);
            errors.AddRange(HexGridTopology.Validate(geography));
            ValidateGeography(geography, errors);
            ValidateState(geography, state, errors);

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Runtime Tile data is invalid:\n- "
                    + string.Join("\n- ", errors.Distinct()));
            }

            var stateById = state.ToDictionary(tileState => tileState.TileId);
            foreach (var tile in geography.Where(tile => tile.Surface == TileSurface.Ocean))
            {
                if (!stateById.ContainsKey(tile.Coordinates))
                    stateById[tile.Coordinates] = new OceanTileData { TileId = tile.Coordinates };
            }

            var runtimeTiles = new List<RuntimeTile>(geography.Count);
            foreach (var tile in geography)
            {
                RuntimeTile runtimeTile = tile.Surface == TileSurface.Land
                    ? new RuntimeLandTile(tile, (LandTileData)stateById[tile.Coordinates])
                    : new RuntimeOceanTile(tile);
                runtimeTiles.Add(runtimeTile);
            }

            var runtimeLandTiles = runtimeTiles.OfType<RuntimeLandTile>().ToList();
            return new TileSystem(
                new ReadOnlyCollection<RuntimeTile>(runtimeTiles),
                new ReadOnlyCollection<RuntimeLandTile>(runtimeLandTiles),
                new ReadOnlyDictionary<Vector3Int, RuntimeTile>(
                    runtimeTiles.ToDictionary(tile => tile.TileId)),
                new ReadOnlyDictionary<Vector3Int, RuntimeLandTile>(
                    runtimeLandTiles.ToDictionary(tile => tile.TileId)));
        }

        public RuntimeTile Get(Vector3Int tileId)
        {
            if (!tilesById.TryGetValue(tileId, out var tile))
                throw new KeyNotFoundException($"Tile {tileId} does not exist.");

            return tile;
        }

        public bool TryGet(Vector3Int tileId, out RuntimeTile tile)
        {
            return tilesById.TryGetValue(tileId, out tile);
        }

        public RuntimeLandTile GetLand(Vector3Int tileId)
        {
            if (landTilesById.TryGetValue(tileId, out var landTile))
                return landTile;
            if (tilesById.ContainsKey(tileId))
                throw new InvalidOperationException($"Tile {tileId} is not land.");

            throw new KeyNotFoundException($"Tile {tileId} does not exist.");
        }

        public bool TryGetLand(Vector3Int tileId, out RuntimeLandTile tile)
        {
            return landTilesById.TryGetValue(tileId, out tile);
        }

        public void ChangeControl(Vector3Int tileId, Alliance controller)
        {
            if (!Enum.IsDefined(typeof(Alliance), controller))
                throw new ArgumentOutOfRangeException(nameof(controller), controller, null);

            GetLand(tileId).ChangeControl(controller);
        }

        private static List<Tile> CopyGeography(
            IReadOnlyList<Tile> templateTiles,
            ICollection<string> errors)
        {
            var result = new List<Tile>(templateTiles.Count);
            for (var index = 0; index < templateTiles.Count; index++)
            {
                var tile = templateTiles[index];
                if (tile == null)
                {
                    errors.Add($"Template Tile at index {index} is null.");
                    continue;
                }

                result.Add(new Tile
                {
                    Coordinates = tile.Coordinates,
                    NeighborTileIds = new List<Vector3Int>(),
                    RiverNeighborTileIds = tile.RiverNeighborTileIds == null
                        ? new List<Vector3Int>()
                        : new List<Vector3Int>(tile.RiverNeighborTileIds),
                    Surface = tile.Surface,
                    Terrain = tile.Terrain,
                    Urbanization = tile.Urbanization,
                    ForestCover = tile.ForestCover
                });
            }

            return result;
        }

        private static List<TileData> CopyState(
            IReadOnlyList<TileData> startingTileState,
            ICollection<string> errors)
        {
            var result = new List<TileData>(startingTileState.Count);
            for (var index = 0; index < startingTileState.Count; index++)
            {
                var tileState = startingTileState[index];
                switch (tileState)
                {
                    case LandTileData land:
                        if (land.Infrastructure == null)
                        {
                            errors.Add($"Land Tile state {land.TileId} has no infrastructure state.");
                            continue;
                        }

                        result.Add(new LandTileData
                        {
                            TileId = land.TileId,
                            Controller = land.Controller,
                            Infrastructure = new BuildingLevel(
                                land.Infrastructure.BuildLevel,
                                land.Infrastructure.Damage),
                            InfrastructureTargetToughness = Math.Max(
                                1,
                                land.InfrastructureTargetToughness)
                        });
                        break;
                    case OceanTileData ocean:
                        result.Add(new OceanTileData { TileId = ocean.TileId });
                        break;
                    case null:
                        errors.Add($"Tile state at index {index} is null.");
                        break;
                    default:
                        errors.Add(
                            $"Tile state {tileState.TileId} uses unsupported type "
                            + $"{tileState.GetType().Name}.");
                        break;
                }
            }

            return result;
        }

        private static void ValidateGeography(
            IReadOnlyList<Tile> geography,
            ICollection<string> errors)
        {
            foreach (var tile in geography)
            {
                var oceanTerrain = tile.Terrain == TileTerrain.Ocean
                                   || tile.Terrain == TileTerrain.ShallowOcean
                                   || tile.Terrain == TileTerrain.DeepOcean;
                if (tile.Surface == TileSurface.Land && oceanTerrain)
                    errors.Add($"Land Tile {tile.Coordinates} uses ocean terrain {tile.Terrain}.");
                if (tile.Surface == TileSurface.Ocean && !oceanTerrain)
                    errors.Add($"Ocean Tile {tile.Coordinates} uses land terrain {tile.Terrain}.");
                if (tile.Surface == TileSurface.Ocean
                    && (tile.Urbanization != Urbanization.None
                        || tile.ForestCover != ForestCover.None))
                {
                    errors.Add(
                        $"Ocean Tile {tile.Coordinates} has land-only settlement or forest data.");
                }

                foreach (var riverNeighborId in tile.RiverNeighborTileIds)
                {
                    var neighbor = geography.FirstOrDefault(
                        candidate => candidate.Coordinates == riverNeighborId);
                    if (neighbor != null
                        && (neighbor.RiverNeighborTileIds == null
                            || !neighbor.RiverNeighborTileIds.Contains(tile.Coordinates)))
                    {
                        errors.Add(
                            $"River crossing {tile.Coordinates} to {riverNeighborId} is not symmetric.");
                    }
                }
            }
        }

        private static void ValidateState(
            IReadOnlyList<Tile> geography,
            IReadOnlyList<TileData> state,
            ICollection<string> errors)
        {
            var duplicateStateIds = state
                .GroupBy(tileState => tileState.TileId)
                .Where(group => group.Count() > 1);
            foreach (var duplicate in duplicateStateIds)
                errors.Add($"Multiple Tile states use Tile ID {duplicate.Key}.");

            var geographyById = geography
                .GroupBy(tile => tile.Coordinates)
                .ToDictionary(group => group.Key, group => group.First());
            var stateById = state
                .GroupBy(tileState => tileState.TileId)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (var tileState in state)
            {
                if (!geographyById.ContainsKey(tileState.TileId))
                    errors.Add($"Tile state {tileState.TileId} has no Template Tile.");
            }

            foreach (var tile in geography)
            {
                if (!stateById.TryGetValue(tile.Coordinates, out var tileState))
                {
                    if (tile.Surface == TileSurface.Land)
                        errors.Add($"Land Tile {tile.Coordinates} has no starting Tile state.");
                    continue;
                }

                if (tile.Surface == TileSurface.Land && tileState is not LandTileData)
                {
                    errors.Add(
                        $"Land Tile {tile.Coordinates} has {tileState.GetType().Name} state.");
                }
                else if (tile.Surface == TileSurface.Ocean && tileState is not OceanTileData)
                {
                    errors.Add(
                        $"Ocean Tile {tile.Coordinates} has {tileState.GetType().Name} state.");
                }
            }
        }
    }

    public abstract class RuntimeTile
    {
        private readonly HashSet<Vector3Int> neighborTileIds;
        private readonly HashSet<Vector3Int> riverNeighborTileIds;

        protected RuntimeTile(Tile geography)
        {
            TileId = geography.Coordinates;
            Surface = geography.Surface;
            Terrain = geography.Terrain;
            Urbanization = geography.Urbanization;
            ForestCover = geography.ForestCover;

            var neighbors = geography.NeighborTileIds.ToArray();
            var riverNeighbors = geography.RiverNeighborTileIds.ToArray();
            NeighborTileIds = Array.AsReadOnly(neighbors);
            RiverNeighborTileIds = Array.AsReadOnly(riverNeighbors);
            neighborTileIds = new HashSet<Vector3Int>(neighbors);
            riverNeighborTileIds = new HashSet<Vector3Int>(riverNeighbors);
        }

        public Vector3Int TileId { get; }
        public TileSurface Surface { get; }
        public TileTerrain Terrain { get; }
        public Urbanization Urbanization { get; }
        public ForestCover ForestCover { get; }
        public IReadOnlyList<Vector3Int> NeighborTileIds { get; }
        public IReadOnlyList<Vector3Int> RiverNeighborTileIds { get; }

        public bool IsNeighbor(Vector3Int otherTileId)
        {
            return neighborTileIds.Contains(otherTileId);
        }

        public bool HasRiverCrossingTo(Vector3Int neighborTileId)
        {
            return riverNeighborTileIds.Contains(neighborTileId);
        }
    }

    public sealed class RuntimeLandTile : RuntimeTile
    {
        private readonly LandTileData state;

        internal RuntimeLandTile(Tile geography, LandTileData state)
            : base(geography)
        {
            this.state = state;
        }

        public Alliance Controller => state.Controller;
        public int InfrastructureBuildLevel => state.Infrastructure.BuildLevel;
        public int InfrastructureDamage => state.Infrastructure.Damage;
        public int InfrastructureFunctionalLevel => state.Infrastructure.FunctionalLevel;
        public int InfrastructureTargetToughness => state.InfrastructureTargetToughness;

        internal void ChangeControl(Alliance controller)
        {
            state.Controller = controller;
        }
    }

    public sealed class RuntimeOceanTile : RuntimeTile
    {
        internal RuntimeOceanTile(Tile geography)
            : base(geography)
        {
        }
    }
}
