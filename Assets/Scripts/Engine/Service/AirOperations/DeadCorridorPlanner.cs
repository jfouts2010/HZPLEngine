using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Service
{
    public sealed class DeadCorridorPlanner
    {
        public const float MissionRadiusKm = 40f;

        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition>
            aircraftTypes;
        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition>
            ordnanceTypes;
        private readonly IReadOnlyDictionary<Guid, AirDefenseComponentDefinition>
            componentDefinitions;
        private readonly Func<Alliance, IReadOnlyCollection<Guid>>
            allowedOrdnanceForAlliance;
        private readonly KnownSamThreatAssessment threatAssessment;
        private readonly IAirRouteGeometryPlanner routePlanner;

        public DeadCorridorPlanner(
            ModuleDefinition module,
            Func<Alliance, IReadOnlyCollection<Guid>> allowedOrdnanceForAlliance,
            IAirRouteGeometryPlanner routePlanner = null)
        {
            aircraftTypes = module.AircraftTypeDefinitions
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            ordnanceTypes = module.OrdnanceTypeDefinitions
                .ToDictionary(definition => definition.OrdnanceTypeDefinitionId);
            componentDefinitions = module.SamComponentDefinitions
                .ToDictionary(definition => definition.SamComponentDefinitionId);
            this.allowedOrdnanceForAlliance = allowedOrdnanceForAlliance
                                              ?? (_ => Array.Empty<Guid>());
            threatAssessment = new KnownSamThreatAssessment(
                module.SamComponentDefinitions,
                module.OrdnanceTypeDefinitions);
            this.routePlanner = routePlanner
                                ?? new SeparatedIngressEgressRouteGeometryPlanner();
        }

        public bool TryPlan(
            AirPlanningSnapshot snapshot,
            out DeadMissionCandidate candidate)
        {
            candidate = null;
            if (snapshot == null
                || snapshot.EnemyAirports.Count == 0
                || snapshot.HostileAirDefenseSites.Count == 0)
                return false;

            var origins = BuildOrigins(snapshot);
            if (origins.Count == 0)
                return false;

            var picture = new AllianceIntelligencePicture(snapshot.Alliance)
            {
                HostileAirDefenseSites = snapshot.HostileAirDefenseSites
                    .Where(report => report != null)
                    .ToList()
            };
            var threats = threatAssessment.BuildKnownThreats(picture);
            if (threats.Count == 0)
                return false;

            foreach (var objective in BuildObjectives(snapshot, origins))
            {
                var origin = objective.Origin;
                var altitudeFeet = Math.Max(
                    0f,
                    origin.AircraftType.NominalCruiseAltitudeFeet);
                var originPosition = WithAltitude(
                    origin.Squadron.AirportPositionFeet,
                    altitudeFeet);
                var destinationPosition = WithAltitude(
                    objective.Airport.PositionFeet,
                    altitudeFeet);
                var maneuverClearanceFeet = AirspaceGeometry
                    .ConservativeSamManeuverClearanceFeet(
                        origin.AircraftType);
                var geometry = routePlanner.Plan(
                    new AirRouteGeometryPlanningContext(
                        originPosition,
                        destinationPosition,
                        destinationPosition,
                        originPosition,
                        CampaignMapCoordinates.TileCenterSpacingFeet,
                        origin.AircraftType.AircraftTypeDefinitionId,
                        threats,
                        maneuverClearanceFeet));
                if (geometry.IsThreatSafe)
                    continue;

                var blockingSiteId = SelectNearestBlockingSite(
                    threats,
                    originPosition,
                    destinationPosition,
                    maneuverClearanceFeet);
                var report = snapshot.HostileAirDefenseSites
                    .FirstOrDefault(site => site != null
                                            && site.SiteId == blockingSiteId);
                if (!HasFunctionalShooterChain(report))
                    continue;

                var targetComponents = report.Components
                    .Where(component => component != null && !component.IsDamaged)
                    .Select(component => component.ComponentId)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();
                if (targetComponents.Count == 0)
                    continue;

                var normalizedFunctionalLevel = objective.Airport.BuildLevel <= 0
                    ? 0f
                    : Mathf.Clamp01(
                        objective.Airport.FunctionalLevel
                        / (float)objective.Airport.BuildLevel);
                candidate = new DeadMissionCandidate(
                    objective.Airport,
                    report,
                    normalizedFunctionalLevel,
                    new DeadMissionPlan
                    {
                        TargetSiteId = report.SiteId,
                        TargetComponentIds = targetComponents,
                        SupportedCorridor = new DeadAirAccessCorridor
                        {
                            OriginPositionFeet = originPosition,
                            DestinationPositionFeet = destinationPosition,
                            RecoveryPositionFeet = originPosition,
                            RepresentativeAltitudeFeet = altitudeFeet,
                            RepresentativeAircraftTypeDefinitionId =
                                origin.AircraftType.AircraftTypeDefinitionId
                        }
                    },
                    $"Destroy SAM {ShortId(report.SiteId)} blocking access from "
                    + $"airport {ShortId(origin.Squadron.AirportBuildingId)} using "
                    + $"{origin.AircraftType.Name} at {altitudeFeet:0} ft to "
                    + $"functional hostile airport "
                    + $"{ShortId(objective.Airport.AirportBuildingId)} "
                    + $"({objective.Airport.FunctionalLevel}/"
                    + $"{objective.Airport.BuildLevel} functional), with "
                    + $"{targetComponents.Count} known SAM components in the target set.");
                return true;
            }

            return false;
        }

        private List<DeadOrigin> BuildOrigins(AirPlanningSnapshot snapshot)
        {
            var allowed = new HashSet<Guid>(
                allowedOrdnanceForAlliance(snapshot.Alliance));
            return snapshot.FriendlySquadrons
                .Where(squadron => squadron.ReadyAircraftCount > 0
                                   && aircraftTypes.ContainsKey(
                                       squadron.AircraftTypeDefinitionId))
                .Select(squadron => new DeadOrigin(
                    squadron,
                    aircraftTypes[squadron.AircraftTypeDefinitionId]))
                .Where(origin => CanAttackAirportInfrastructure(
                    origin.AircraftType,
                    allowed))
                .OrderBy(origin => origin.Squadron.AirportBuildingId)
                .ThenBy(origin => origin.AircraftType.AircraftTypeDefinitionId)
                .ThenBy(origin => origin.Squadron.SquadronId)
                .ToList();
        }

        private IEnumerable<DeadObjective> BuildObjectives(
            AirPlanningSnapshot snapshot,
            IReadOnlyList<DeadOrigin> origins)
        {
            return snapshot.EnemyAirports
                .Where(airport => airport != null
                                  && airport.InformationQuality > 0f
                                  && airport.Condition
                                  != ObservedAirportCondition.NonFunctional
                                  && airport.FunctionalLevel > 0)
                .SelectMany(airport => origins.Select(origin => new
                {
                    Airport = airport,
                    Origin = origin,
                    DistanceKm = HorizontalDistanceKm(
                        origin.Squadron.AirportPositionFeet,
                        airport.PositionFeet)
                }))
                .Where(value => value.Origin.AircraftType.RangeKm <= 0f
                                || value.DistanceKm * 2f
                                <= value.Origin.AircraftType.RangeKm)
                .OrderBy(value => value.DistanceKm)
                .ThenBy(value => value.Airport.AirportBuildingId)
                .ThenBy(value => value.Origin.Squadron.AirportBuildingId)
                .ThenBy(value => value.Origin.AircraftType.AircraftTypeDefinitionId)
                .GroupBy(value => value.Airport.AirportBuildingId)
                .Select(group => group.First())
                .OrderBy(value => value.DistanceKm)
                .ThenBy(value => value.Airport.AirportBuildingId)
                .Select(value => new DeadObjective(
                    value.Airport,
                    value.Origin));
        }

        private bool CanAttackAirportInfrastructure(
            AircraftTypeDefinition aircraftType,
            HashSet<Guid> allowed)
        {
            return aircraftType.CompatibleOrdnanceTypeDefinitionIds
                .Where(allowed.Contains)
                .Select(id => ordnanceTypes.TryGetValue(id, out var ordnance)
                    ? ordnance
                    : null)
                .Any(ordnance => ordnance != null
                                 && IsAirToGround(ordnance)
                                 && ordnance.EffectPower > 0
                                 && ordnance.GetEffectiveness(
                                     OrdnanceTargetCategory.Building) > 0f);
        }

        private bool HasFunctionalShooterChain(
            AirDefenseSiteIntelligenceReport report)
        {
            if (report == null
                || report.InformationQuality <= 0f
                || report.IsDisabled
                || report.IsDestroyed
                || report.Components == null)
                return false;

            var hasRadar = report.Components.Any(component =>
                component != null
                && !component.IsDamaged
                && componentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                && definition is RadarAirDefenseComponentDefinition
                {
                    ProvidesWeaponQualityTrack: true
                });
            var hasLauncher = report.Components.Any(component =>
                component != null
                && !component.IsDamaged
                && componentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                && definition is LauncherAirDefenseComponentDefinition);
            return hasRadar && hasLauncher;
        }

        private static Guid SelectNearestBlockingSite(
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            Vector3 origin,
            Vector3 destination,
            float maneuverClearanceFeet)
        {
            var intersecting = threats
                .Where(threat => threat != null
                                 && (threat.IntersectsSegment(
                                         origin,
                                         destination,
                                         maneuverClearanceFeet)
                                     || threat.Contains(
                                         destination,
                                         maneuverClearanceFeet)))
                .ToList();
            var candidates = intersecting.Count > 0
                ? intersecting
                : threats.Where(threat => threat != null).ToList();
            return candidates
                .OrderBy(threat => Vector2.Distance(
                    new Vector2(origin.x, origin.z),
                    new Vector2(threat.CenterFeet.x, threat.CenterFeet.z)))
                .ThenBy(threat => threat.SiteId)
                .Select(threat => threat.SiteId)
                .FirstOrDefault();
        }

        private static bool IsAirToGround(OrdnanceTypeDefinition ordnance)
        {
            return ordnance.EmploymentCategory
                       == OrdnanceEmploymentCategory.AntiRadiation
                   || ordnance.EmploymentCategory
                       == OrdnanceEmploymentCategory.AirToGroundPrecision
                   || ordnance.EmploymentCategory
                       == OrdnanceEmploymentCategory.AirToGroundUnguided
                   || ordnance.EmploymentCategory
                       == OrdnanceEmploymentCategory.Gun;
        }

        private static float HorizontalDistanceKm(
            Vector3 first,
            Vector3 second)
        {
            return Vector2.Distance(
                       new Vector2(first.x, first.z),
                       new Vector2(second.x, second.z))
                   / AirspaceGeometry.FeetPerKilometer;
        }

        private static Vector3 WithAltitude(Vector3 positionFeet, float altitudeFeet)
        {
            positionFeet.y = altitudeFeet;
            return positionFeet;
        }

        private static string ShortId(Guid id)
        {
            return id == Guid.Empty ? "none" : id.ToString("N").Substring(0, 8);
        }

        private sealed class DeadOrigin
        {
            public AirPlanningSquadronSnapshot Squadron { get; }
            public AircraftTypeDefinition AircraftType { get; }

            public DeadOrigin(
                AirPlanningSquadronSnapshot squadron,
                AircraftTypeDefinition aircraftType)
            {
                Squadron = squadron;
                AircraftType = aircraftType;
            }
        }

        private sealed class DeadObjective
        {
            public ObservedEnemyAirportSnapshot Airport { get; }
            public DeadOrigin Origin { get; }

            public DeadObjective(
                ObservedEnemyAirportSnapshot airport,
                DeadOrigin origin)
            {
                Airport = airport;
                Origin = origin;
            }
        }
    }

    public sealed class DeadMissionCandidate
    {
        public ObservedEnemyAirportSnapshot ObjectiveAirport { get; }
        public AirDefenseSiteIntelligenceReport TargetSite { get; }
        public float Urgency { get; }
        public DeadMissionPlan Plan { get; }
        public string Rationale { get; }

        public DeadMissionCandidate(
            ObservedEnemyAirportSnapshot objectiveAirport,
            AirDefenseSiteIntelligenceReport targetSite,
            float urgency,
            DeadMissionPlan plan,
            string rationale)
        {
            ObjectiveAirport = objectiveAirport;
            TargetSite = targetSite;
            Urgency = Mathf.Clamp01(urgency);
            Plan = plan;
            Rationale = rationale ?? string.Empty;
        }
    }
}
