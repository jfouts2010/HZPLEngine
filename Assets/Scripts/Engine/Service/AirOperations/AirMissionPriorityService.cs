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
    public readonly struct AirCombatProjection
    {
        public float Power { get; }
        public float FullResponseRangeKm { get; }
        public float MaximumInterceptRangeKm { get; }

        public AirCombatProjection(
            float power,
            float fullResponseRangeKm,
            float maximumInterceptRangeKm)
        {
            Power = Mathf.Max(0f, power);
            FullResponseRangeKm = Mathf.Max(0f, fullResponseRangeKm);
            MaximumInterceptRangeKm = Mathf.Max(
                FullResponseRangeKm,
                maximumInterceptRangeKm);
        }

        public float CalculateInfluence(float distanceKm)
        {
            if (Power <= 0f || MaximumInterceptRangeKm <= 0f)
                return 0f;

            var distance = Mathf.Max(0f, distanceKm);
            if (distance <= FullResponseRangeKm)
                return Power;
            if (distance >= MaximumInterceptRangeKm)
                return 0f;

            var responseProgress = Mathf.InverseLerp(
                FullResponseRangeKm,
                MaximumInterceptRangeKm,
                distance);
            return Power * (1f - Mathf.SmoothStep(0f, 1f, responseProgress));
        }
    }

    public sealed class AirMissionPriorityService
    {
        private const float FullResponseMinutes = 5f;
        private const float MaximumResponseMinutes = 10f;
        private const float KilometersPerNauticalMile = 1.852f;

        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypes;
        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes;

        public AirMissionPriorityService(ModuleDefinition module)
        {
            aircraftTypes = module.AircraftTypeDefinitions
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            ordnanceTypes = module.OrdnanceTypeDefinitions
                .ToDictionary(definition => definition.OrdnanceTypeDefinitionId);
        }

        public void Score(
            AirMissionRequest request,
            AllianceAirDoctrine doctrine,
            AirPlanningSnapshot snapshot,
            float assessedFriendlyPresence,
            float assessedHostilePresence,
            float assessedAirActivity,
            float assessedHostileAirActivity)
        {
            var doctrineWeight = doctrine.GetPriorityWeight(request.RequestType);
            var friendlyPower = CalculatePowerNear(
                snapshot.FriendlySquadrons,
                request.MissionArea);
            var hostilePower = request.PriorityComponents.TryGetValue(
                "barcapHostileAirCombatPower",
                out var barcapHostilePower)
                ? Mathf.Max(0f, barcapHostilePower)
                : request.PriorityComponents.TryGetValue(
                    "ocaHostileAirCombatPower",
                    out var ocaHostilePower)
                    ? Mathf.Max(0f, ocaHostilePower)
                    : 0f;
            var friendlyDeficit = Mathf.Clamp01(
                (hostilePower * doctrine.DesiredAirCombatAdvantage - friendlyPower)
                / Mathf.Max(0.1f, hostilePower * doctrine.DesiredAirCombatAdvantage));
            var friendlyPresence = Mathf.Clamp01(assessedFriendlyPresence);
            var hostilePresence = Mathf.Clamp01(assessedHostilePresence);
            var airActivity = Mathf.Clamp01(assessedAirActivity);
            var hostileAirActivity = Mathf.Clamp01(assessedHostileAirActivity);
            var observedHostilePressure = Mathf.Max(
                hostilePresence,
                hostileAirActivity * 0.25f);
            var barcapHostilePressure = request.PriorityComponents.TryGetValue(
                "barcapHostilePressure",
                out var rawBarcapHostilePressure)
                ? Mathf.Clamp01(rawBarcapHostilePressure)
                : observedHostilePressure;
            var barcapFrontPriority = request.PriorityComponents.TryGetValue(
                "barcapFrontPriority",
                out var rawBarcapFrontPriority)
                ? Mathf.Clamp01(rawBarcapFrontPriority)
                : 0f;
            var urgency = request.RequestType switch
            {
                AirMissionRequestType.BarrierCombatAirPatrol => Mathf.Max(
                    barcapFrontPriority,
                    barcapHostilePressure),
                AirMissionRequestType.OffensiveCounterAirSweep => Mathf.Max(
                    friendlyDeficit,
                    observedHostilePressure),
                AirMissionRequestType.DestructionOfEnemyAirDefenses =>
                    request.PriorityComponents.TryGetValue(
                        "deadAirportFunctionalLevel",
                        out var deadAirportFunctionalLevel)
                        ? Mathf.Clamp01(deadAirportFunctionalLevel)
                        : 0f,
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
            components["friendlyCombatPresence"] = friendlyPresence;
            components["hostileCombatPresence"] = hostilePresence;
            components["airActivity"] = airActivity;
            components["hostileAirActivity"] = hostileAirActivity;
            components["urgency"] = urgency;
            components["riskTolerance"] = riskAcceptance;
            request.PriorityComponents = components;
            request.Priority = score;
        }

        public float CalculateAirCombatPower(AirPlanningSquadronSnapshot squadron)
        {
            var aircraftType = aircraftTypes[squadron.AircraftTypeDefinitionId];
            return aircraftType.AirInterferenceCapability
                   * Math.Max(
                       0,
                       squadron.ReadyAircraftCount + squadron.AssignedAircraftCount);
        }

        public float CalculateAirborneAirCombatPower(
            AirFlight flight,
            Squadron squadron)
        {
            return CalculateAirborneAirCombatProjections(flight, squadron)
                .Sum(projection => projection.Power);
        }

        public IReadOnlyList<AirCombatProjection>
            CalculateAirborneAirCombatProjections(
            AirFlight flight,
            Squadron squadron)
        {
            if (flight == null
                || squadron == null
                || (!flight.IsFighterEscort
                    && flight.MissionType != AirMissionRequestType.BarrierCombatAirPatrol
                    && flight.MissionType != AirMissionRequestType.OffensiveCounterAirSweep))
                return Array.Empty<AirCombatProjection>();

            var projections = new List<AirCombatProjection>();
            foreach (var aircraft in squadron.Aircraft.Where(aircraft =>
                         aircraft.AssignedFlightId == flight.FlightId
                         && aircraft.Status != CampaignAircraftStatus.Damaged
                         && aircraft.Status != CampaignAircraftStatus.Lost))
            {
                if (!aircraftTypes.TryGetValue(
                        aircraft.AircraftTypeDefinitionId,
                        out var aircraftType)
                    || aircraftType.AirInterferenceCapability <= 0f)
                    continue;

                var organicDetectionFactor = Mathf.Lerp(
                    0.5f,
                    1f,
                    Mathf.Clamp01(aircraftType.RadarQuality));
                var combatSpeedKmPerHour = aircraftType.CombatSpeedKnots
                                           * KilometersPerNauticalMile;
                var fullResponseRange = combatSpeedKmPerHour
                                        * FullResponseMinutes / 60f
                                        * organicDetectionFactor;
                var maximumResponseRange = combatSpeedKmPerHour
                                           * MaximumResponseMinutes / 60f
                                           * organicDetectionFactor;
                projections.Add(new AirCombatProjection(
                    aircraftType.AirInterferenceCapability,
                    fullResponseRange,
                    maximumResponseRange));
            }

            return projections;
        }

        public IReadOnlyList<AirCombatProjection> CalculateTrackedAirCombatProjections(
            IADSTrack track)
        {
            if (track == null
                || track.EstimatedAirCombatPower <= 0f)
                return Array.Empty<AirCombatProjection>();

            var responseSpeedKnots = track.EstimatedSpeedKnots;
            var detectionFactor = 1f;
            if (track.HasIdentifiedAircraftType
                && aircraftTypes.TryGetValue(
                    track.IdentifiedAircraftTypeDefinitionId,
                    out var aircraftType))
            {
                responseSpeedKnots = aircraftType.CombatSpeedKnots;
                detectionFactor = Mathf.Lerp(
                    0.5f,
                    1f,
                    Mathf.Clamp01(aircraftType.RadarQuality));
            }

            if (responseSpeedKnots <= 0f)
                return Array.Empty<AirCombatProjection>();

            var responseSpeedKmPerHour = responseSpeedKnots
                                         * KilometersPerNauticalMile;
            return new[]
            {
                new AirCombatProjection(
                    track.EstimatedAirCombatPower,
                    responseSpeedKmPerHour * FullResponseMinutes / 60f
                    * detectionFactor,
                    responseSpeedKmPerHour * MaximumResponseMinutes / 60f
                    * detectionFactor)
            };
        }

        public bool CanPerformAirCombat(AircraftTypeDefinition aircraftType)
        {
            return aircraftType != null
                   && aircraftType.SupportCapability == AirSupportCapability.None
                   && aircraftType.AirInterferenceCapability > 0f;
        }

        public bool CanPerformCombatMission(AircraftTypeDefinition aircraftType)
        {
            if (aircraftType == null
                || aircraftType.SupportCapability != AirSupportCapability.None)
                return false;
            if (CanPerformAirCombat(aircraftType))
                return true;

            return aircraftType.CompatibleOrdnanceTypeDefinitionIds.Any(id =>
                ordnanceTypes.TryGetValue(id, out var ordnance)
                && (ordnance.EmploymentCategory
                    == OrdnanceEmploymentCategory.AirToGroundPrecision
                    || ordnance.EmploymentCategory
                    == OrdnanceEmploymentCategory.AirToGroundUnguided
                    || ordnance.EmploymentCategory
                    == OrdnanceEmploymentCategory.AntiRadiation));
        }

        public AircraftTypeDefinition GetAircraftType(Guid aircraftTypeDefinitionId)
        {
            return aircraftTypes.TryGetValue(aircraftTypeDefinitionId, out var aircraftType)
                ? aircraftType
                : null;
        }

        public float GetLongestAirToAirWeaponRangeKm(
            AircraftTypeDefinition aircraftType)
        {
            if (aircraftType == null)
                return 0f;

            return aircraftType.CompatibleOrdnanceTypeDefinitionIds
                .Where(ordnanceTypes.ContainsKey)
                .Select(id => ordnanceTypes[id])
                .Where(AirLoadoutPlanner.IsAirToAir)
                .Select(ordnance => Math.Max(0f, ordnance.MaximumRangeKm))
                .DefaultIfEmpty(0f)
                .Max();
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
