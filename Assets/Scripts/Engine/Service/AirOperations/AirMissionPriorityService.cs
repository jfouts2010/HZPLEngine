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
        private readonly AirLoadoutPlanner loadoutPlanner;

        public AirMissionPriorityService(
            ModuleDefinition module,
            Func<Alliance, IReadOnlyCollection<Guid>> allowedOrdnanceForAlliance)
        {
            aircraftTypes = module.AircraftTypeDefinitions
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            ordnanceTypes = module.OrdnanceTypeDefinitions
                .ToDictionary(definition => definition.OrdnanceTypeDefinitionId);
            loadoutPlanner = new AirLoadoutPlanner(
                module,
                allowedOrdnanceForAlliance);
        }

        public void Score(
            AirMissionRequest request,
            AllianceAirDoctrine doctrine,
            AirPlanningSnapshot snapshot)
        {
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

            var components = new Dictionary<string, float>(request.PriorityComponents);
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
            var aircraftType = aircraftTypes[squadron.AircraftTypeDefinitionId];
            if (!loadoutPlanner.TryPlanAirCombatLoadout(
                    aircraftType,
                    squadron.Alliance,
                    out var loadout,
                    out _))
                return 0f;

            var bestAirWeaponEffectiveness = loadout
                .Select(item => ordnanceTypes[item.OrdnanceTypeDefinitionId]
                    .GetEffectiveness(OrdnanceTargetCategory.Aircraft))
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

        public bool CanPerformAirCombat(
            AircraftTypeDefinition aircraftType,
            Alliance alliance)
        {
            if (aircraftType.SupportCapability != AirSupportCapability.None)
                return false;

            return loadoutPlanner.TryPlanAirCombatLoadout(
                aircraftType,
                alliance,
                out _,
                out _);
        }

        public float CalculatePowerNear(
            IEnumerable<AirPlanningSquadronSnapshot> squadrons,
            AirMissionArea missionArea)
        {
            return squadrons
                .Where(squadron => missionArea.Contains(squadron.AirportTileId))
                .Sum(CalculateAirCombatPower);
        }
    }

}
