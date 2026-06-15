using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class BuildingLevel
    {
        public const int MinLevel = 0;
        public const int MaxLevel = 10;

        public int BuildLevel;
        public int Damage;

        public int FunctionalLevel
        {
            get { return Mathf.Max(MinLevel, BuildLevel - Damage); }
        }

        public BuildingLevel()
        {
        }

        public BuildingLevel(int buildLevel, int damage = 0)
        {
            BuildLevel = buildLevel;
            Damage = damage;
            Normalize();
        }

        public void Normalize()
        {
            BuildLevel = Mathf.Clamp(BuildLevel, MinLevel, MaxLevel);
            Damage = Mathf.Clamp(Damage, MinLevel, BuildLevel);
        }
    }
}
