using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class DivisionSystem
    {
        [SerializeReference] public List<Division> Divisions = new List<Division>();

        private Dictionary<Vector3Int, List<Division>> divisionsByTileId;
        private Dictionary<Guid, Division> divisionsById;

        private const float StrengthRecoveryFractionOfMaxPerDay = 0.2f;
        private const float OrganizationRecoveryPerMaxOrganizationPerHour = 0.03f;

        public void ApplyOutOfCombatRecovery(
            float elapsedHours,
            Func<Guid, bool> isDivisionEngagedInCombat,
            Func<Guid, float> getSupplyRatio,
            Func<Division, bool> canApplySupplyEffect)
        {
            if (elapsedHours <= 0f || isDivisionEngagedInCombat == null || getSupplyRatio == null)
                return;

            foreach (var division in Divisions)
            {
                if (division == null)
                    continue;

                if (isDivisionEngagedInCombat(division.DivisionId))
                    continue;

                if (canApplySupplyEffect != null && !canApplySupplyEffect(division))
                    continue;

                ApplyRecovery(division, elapsedHours, getSupplyRatio(division.DivisionId));
            }
        }

        private static void ApplyRecovery(Division division, float elapsedHours, float supplyRatio)
        {
            var supplyEffect = ((Mathf.Clamp01(supplyRatio) - 0.5f) * 2f);

            if (division.MaxStrength > 0)
            {
                var strengthDelta = division.MaxStrength
                                    * StrengthRecoveryFractionOfMaxPerDay
                                    * (elapsedHours / 24f)
                                    * supplyEffect;
                division.Strength = ApplySupplyDelta(division.Strength, division.MaxStrength, strengthDelta);
            }

            if (division.MaxOrganization > 0)
            {
                var organizationDelta = division.MaxOrganization
                                        * OrganizationRecoveryPerMaxOrganizationPerHour
                                        * elapsedHours
                                        * supplyEffect;
                division.Organization = ApplySupplyDelta(
                    division.Organization,
                    division.MaxOrganization,
                    organizationDelta);
            }
        }

        private static float ApplySupplyDelta(float currentValue, int maxValue, float delta)
        {
            if (delta >= 0f)
                return Mathf.Min(maxValue, currentValue + delta);

            return Mathf.Max(1f, currentValue + delta);
        }

        public List<Division> GetDivisionsOnTile(Vector3Int tileId)
        {
            EnsureIndex();
            return divisionsByTileId.TryGetValue(tileId, out var divisions)
                ? divisions
                : new List<Division>();
        }

        public bool TryGetDivision(Guid divisionId, out Division division)
        {
            EnsureIndex();
            return divisionsById.TryGetValue(divisionId, out division);
        }

        public bool MoveDivision(Guid divisionId, Vector3Int tileId)
        {
            EnsureIndex();
            if (!divisionsById.TryGetValue(divisionId, out var division))
                return false;

            if (division.TileId == tileId)
                return true;

            if (divisionsByTileId.TryGetValue(division.TileId, out var oldTileDivisions))
            {
                oldTileDivisions.Remove(division);
                if (oldTileDivisions.Count == 0)
                    divisionsByTileId.Remove(division.TileId);
            }

            division.TileId = tileId;

            if (!divisionsByTileId.TryGetValue(tileId, out var newTileDivisions))
            {
                newTileDivisions = new List<Division>();
                divisionsByTileId[tileId] = newTileDivisions;
            }

            newTileDivisions.Add(division);
            return true;
        }

        public bool RemoveDivision(Guid divisionId)
        {
            EnsureIndex();
            if (!divisionsById.TryGetValue(divisionId, out var division))
                return false;

            divisionsById.Remove(divisionId);
            if (divisionsByTileId.TryGetValue(division.TileId, out var tileDivisions))
            {
                tileDivisions.Remove(division);
                if (tileDivisions.Count == 0)
                    divisionsByTileId.Remove(division.TileId);
            }

            return Divisions.Remove(division);
        }

        public void RebuildIndex()
        {
            var divisions = (Divisions ?? new List<Division>())
                .Where(division => division != null)
                .ToList();

            foreach (var division in divisions)
                division.CurrentOrder ??= new HoldGroundOrder();

            divisionsByTileId = divisions
                .GroupBy(division => division.TileId)
                .ToDictionary(group => group.Key, group => group.ToList());

            divisionsById = divisions
                .GroupBy(division => division.DivisionId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        private void EnsureIndex()
        {
            if (divisionsByTileId == null || divisionsById == null)
                RebuildIndex();
        }
    }
}