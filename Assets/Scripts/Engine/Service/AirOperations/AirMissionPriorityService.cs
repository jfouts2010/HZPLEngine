using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Service
{
    public sealed class AirMissionPriorityService
    {
        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypes;
        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes;

        public AirMissionPriorityService(ModuleDefinition module)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));

            aircraftTypes = module.AircraftTypeDefinitions
                .Where(definition => definition != null)
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            ordnanceTypes = module.OrdnanceTypeDefinitions
                .Where(definition => definition != null)
                .ToDictionary(definition => definition.OrdnanceTypeDefinitionId);
        }

        public void Score(
            AirMissionRequest request,
            AllianceAirDoctrine doctrine,
            AirPlanningSnapshot snapshot)
        {
            if (request == null)
                return;

            doctrine ??= AllianceAirDoctrine.CreateDefault();
            var doctrineWeight = doctrine.GetPriorityWeight(request.RequestType);
            var friendlyPower = CalculatePowerNear(
                snapshot.FriendlySquadrons,
                request.MissionArea);
            var hostilePower = CalculatePowerNear(
                snapshot.HostileSquadrons,
                request.MissionArea);
            var localPowerTotal = Mathf.Max(0.1f, friendlyPower + hostilePower);
            var hostilePressure = Mathf.Clamp01(hostilePower / localPowerTotal);
            var friendlyDeficit = Mathf.Clamp01(
                (hostilePower * doctrine.DesiredAirCombatAdvantage - friendlyPower)
                / Mathf.Max(0.1f, hostilePower * doctrine.DesiredAirCombatAdvantage));
            var urgency = request.RequestType switch
            {
                AirMissionRequestType.DefensiveCounterAirPatrol => hostilePressure,
                AirMissionRequestType.OffensiveCounterAirSweep => friendlyDeficit,
                AirMissionRequestType.ProvideAirborneC2 =>
                    Mathf.Clamp01(request.DesiredSupportSlots / 12f),
                AirMissionRequestType.ProvideAerialRefueling =>
                    Mathf.Clamp01(request.DesiredSupportSlots / 12f),
                _ => 0f
            };
            var riskAcceptance = Mathf.Clamp01(doctrine.RiskTolerance);
            var score = doctrineWeight * (1f + urgency + riskAcceptance * 0.25f);

            var components = request.PriorityComponents == null
                ? new Dictionary<string, float>()
                : new Dictionary<string, float>(request.PriorityComponents);
            components["doctrineWeight"] = doctrineWeight;
            components["friendlyAirCombatPower"] = friendlyPower;
            components["hostileAirCombatPower"] = hostilePower;
            components["urgency"] = urgency;
            components["riskTolerance"] = riskAcceptance;
            request.PriorityComponents = components;
            request.Priority = score;
        }

        public float CalculateAirCombatPower(AirPlanningSquadronSnapshot squadron)
        {
            if (squadron == null
                || !aircraftTypes.TryGetValue(squadron.AircraftTypeDefinitionId, out var aircraftType)
                || !CanPerformAirCombat(aircraftType))
                return 0f;

            var bestAirWeaponEffectiveness = aircraftType.CompatibleOrdnanceTypeDefinitionIds
                .Where(ordnanceTypes.ContainsKey)
                .Select(ordnanceTypeId =>
                    ordnanceTypes[ordnanceTypeId].GetEffectiveness(OrdnanceTargetCategory.Aircraft))
                .DefaultIfEmpty(0f)
                .Max();
            var perAircraftPower =
                0.25f
                + Mathf.Clamp01(aircraftType.RadarQuality) * 0.35f
                + Mathf.Clamp01(aircraftType.EcmQuality) * 0.15f
                + Mathf.Clamp01(aircraftType.Survivability) * 0.15f
                + bestAirWeaponEffectiveness * 0.35f;
            return perAircraftPower * Math.Max(0, squadron.ReadyAircraftCount + squadron.AssignedAircraftCount);
        }

        public bool CanPerformAirCombat(AircraftTypeDefinition aircraftType)
        {
            if (aircraftType == null || aircraftType.SupportCapability != AirSupportCapability.None)
                return false;

            return aircraftType.CompatibleOrdnanceTypeDefinitionIds
                .Where(ordnanceTypes.ContainsKey)
                .Any(ordnanceTypeId =>
                    ordnanceTypes[ordnanceTypeId].GetEffectiveness(OrdnanceTargetCategory.Aircraft) > 0f);
        }

        public float CalculatePowerNear(
            IEnumerable<AirPlanningSquadronSnapshot> squadrons,
            AirMissionArea missionArea)
        {
            return (squadrons ?? Enumerable.Empty<AirPlanningSquadronSnapshot>())
                .Where(squadron => missionArea == null || missionArea.Contains(squadron.AirportTileId))
                .Sum(CalculateAirCombatPower);
        }
    }

}
