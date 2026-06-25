using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using UnityEngine;

namespace Engine.Models
{
    public class SupplySystem
    {
        private readonly GameManager gameManager;
        private Dictionary<Guid, float> supplyAvailabilityRatioByDivisionId = new Dictionary<Guid, float>();
        private Dictionary<Guid, float> supplyStoreRatioByDivisionId = new Dictionary<Guid, float>();

        public SupplySystem(GameManager gameManager)
        {
            this.gameManager = gameManager;
        }

        public IReadOnlyDictionary<Guid, float> SupplyRatioByDivisionId => supplyStoreRatioByDivisionId;

        public void GameTurn(float elapsedHours = 0f)
        {
            supplyAvailabilityRatioByDivisionId = CalculateSupplyRatios();
            ApplyDivisionSupplyStores(elapsedHours);
            supplyStoreRatioByDivisionId = CalculateSupplyStoreRatios();
        }

        public float GetSupplyRatio(Guid divisionId)
        {
            return supplyStoreRatioByDivisionId.TryGetValue(divisionId, out var supplyRatio)
                ? supplyRatio
                : 0f;
        }

        public float GetSupplyAvailabilityRatio(Guid divisionId)
        {
            return supplyAvailabilityRatioByDivisionId.TryGetValue(divisionId, out var supplyRatio)
                ? supplyRatio
                : 0f;
        }

        private void ApplyDivisionSupplyStores(float elapsedHours)
        {
            if (gameManager?.divisionSystem?.Divisions == null)
                return;

            var elapsedDays = Mathf.Max(0f, elapsedHours) / 24f;
            foreach (var division in gameManager.divisionSystem.Divisions)
            {
                if (division == null)
                    continue;

                division.EnsureSupplyStore();
                if (division.SupplyConsumption <= 0f || division.MaxSupplyStore <= 0f)
                    continue;

                if (elapsedDays <= 0f)
                {
                    if (division.SupplyStore <= 0f)
                        division.SupplyStore = division.MaxSupplyStore;
                    continue;
                }

                var dailySupplyUse = Mathf.Max(0f, division.SupplyConsumption);
                var supplyDelta = dailySupplyUse * elapsedDays;
                var availabilityRatio = GetSupplyAvailabilityRatio(division.DivisionId);

                if (availabilityRatio > 1f)
                    division.SupplyStore = Mathf.Min(division.MaxSupplyStore, division.SupplyStore + supplyDelta);
                else if (availabilityRatio <= 0f)
                    division.SupplyStore = Mathf.Max(0f, division.SupplyStore - supplyDelta);
            }
        }

        private Dictionary<Guid, float> CalculateSupplyStoreRatios()
        {
            var ratios = new Dictionary<Guid, float>();
            if (gameManager?.divisionSystem?.Divisions == null)
                return ratios;

            foreach (var division in gameManager.divisionSystem.Divisions)
            {
                if (division == null)
                    continue;

                division.EnsureSupplyStore();
                ratios[division.DivisionId] = division.MaxSupplyStore <= 0f
                    ? 1f
                    : Mathf.Clamp01(division.SupplyStore / division.MaxSupplyStore);
            }

            return ratios;
        }

        private Dictionary<Guid, float> CalculateSupplyRatios()
        {
            var ratios = new Dictionary<Guid, float>();
            if (gameManager == null || gameManager.divisionSystem?.Divisions == null)
                return ratios;

            var analysis = SupplyNetworkAnalysis.Build(gameManager);
            var assignmentsByHubId = analysis.DivisionAssignments
                .GroupBy(assignment => assignment.Hub.BuildingId)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (var division in gameManager.divisionSystem.Divisions)
            {
                if (division == null)
                    continue;

                ratios[division.DivisionId] = 0f;

                if (division.SupplyConsumption <= 0f)
                {
                    ratios[division.DivisionId] = 1f;
                    continue;
                }
            }

            foreach (var hubAssignments in assignmentsByHubId.Values)
            {
                if (hubAssignments.Count == 0)
                    continue;

                var hubSupply = hubAssignments[0].HubSupply;
                var totalDemand = hubAssignments.Sum(assignment => Mathf.Max(0f, assignment.Division.SupplyConsumption));
                var allocationRatio = totalDemand <= 0f
                    ? 1f
                    : hubSupply / totalDemand;

                foreach (var assignment in hubAssignments)
                    ratios[assignment.Division.DivisionId] = Mathf.Max(0f, allocationRatio * assignment.Falloff);
            }

            return ratios;
        }
    }
}
