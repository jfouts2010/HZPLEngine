using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public enum TileSurface
    {
        Land,
        Ocean
    }

    [Serializable]
    public enum TileTerrain
    {
        Plains,
        Hills,
        Mountain,
        Desert,
        Tundra,
        Coast,
        Ocean,
        ShallowOcean,
        DeepOcean
    }

    [Serializable]
    public enum Urbanization
    {
        None,
        Rural,
        Suburban,
        Urban
    }

    [Serializable]
    public enum ForestCover
    {
        None,
        Light,
        Heavy
    }
}
