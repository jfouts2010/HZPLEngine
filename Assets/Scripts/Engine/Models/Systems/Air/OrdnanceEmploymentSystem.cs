using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Models
{
    public sealed class OrdnanceEmploymentSystem
    {
        private const int MaximumEmploymentRecords = 5000;
        private const int RadarLockBreakRollSequence = -1;

        private readonly GameManager gameManager;
        private readonly AirTaskingSystem airTaskingSystem;
        private readonly IADSSystem iadsSystem;
        public List<ActiveOrdnanceEmploymentPass> ActivePasses =
            new List<ActiveOrdnanceEmploymentPass>();
        public List<PendingOrdnanceEffect> PendingEffects =
            new List<PendingOrdnanceEffect>();
        public List<OrdnanceEmploymentRecord> Records =
            new List<OrdnanceEmploymentRecord>();
        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypes;
        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes;
        private readonly IReadOnlyDictionary<Guid, AirDefenseComponentDefinition>
            airDefenseComponentDefinitions;
        private Func<ActiveOrdnanceEmploymentPass, DateTime, bool>
            airToAirEmploymentValidator;

        public OrdnanceEmploymentSystem(
            GameManager gameManager,
            AirTaskingSystem airTaskingSystem,
            IADSSystem iadsSystem,
            ModuleDefinition module)
        {
            this.gameManager = gameManager;
            this.airTaskingSystem = airTaskingSystem;
            this.iadsSystem = iadsSystem;
            aircraftTypes = module.AircraftTypeDefinitions
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            ordnanceTypes = module.OrdnanceTypeDefinitions
                .ToDictionary(definition => definition.OrdnanceTypeDefinitionId);
            airDefenseComponentDefinitions = module.SamComponentDefinitions
                .ToDictionary(definition => definition.SamComponentDefinitionId);
        }

        public void AdvanceScheduledEvents(DateTime currentTime)
        {
            ProcessDueEvents(currentTime);
        }

        internal void SetAirToAirEmploymentValidator(
            Func<ActiveOrdnanceEmploymentPass, DateTime, bool> validator)
        {
            airToAirEmploymentValidator = validator;
        }

        public DateTime? GetNextScheduledEvent(DateTime after, DateTime noLaterThan)
        {
            var release = ActivePasses
                .Where(pass => pass.ReleaseAt > after && pass.ReleaseAt <= noLaterThan)
                .Select(pass => (DateTime?)pass.ReleaseAt)
                .DefaultIfEmpty()
                .Min();
            var effect = PendingEffects
                .Where(item => item.ResolveAt > after && item.ResolveAt <= noLaterThan)
                .Select(item => (DateTime?)item.ResolveAt)
                .DefaultIfEmpty()
                .Min();
            return Earlier(release, effect);
        }

        public bool TryStartAirToAirPass(
            AirCombatEmploymentProposal proposal,
            DateTime currentTime)
        {
            if (proposal == null
                || proposal.SourceFlightId == Guid.Empty
                || proposal.TargetFlightId == Guid.Empty
                || proposal.Quantity <= 0
                || ActivePasses.Any(pass => pass.SourceFlightId == proposal.SourceFlightId))
                return false;

            var contexts = BuildFlightContexts();
            if (!contexts.TryGetValue(proposal.SourceFlightId, out var source)
                || !contexts.TryGetValue(proposal.TargetFlightId, out var target)
                || source.LiveAircraft.Count == 0
                || target.LiveAircraft.Count == 0
                || !source.Flight.IsAirborne
                || !target.Flight.IsAirborne
                || !ordnanceTypes.TryGetValue(
                    proposal.OrdnanceTypeDefinitionId,
                    out var ordnance)
                || !IsAirToAir(ordnance)
                || !AirCombatRules.EvaluateLaunch(
                    source.Flight,
                    source.AircraftType,
                    target.Flight,
                    ordnance,
                    out var launchQuality))
                return false;

            var operationalSourceAircraft = source.LiveAircraft
                .Where(aircraft =>
                    aircraft.Status != CampaignAircraftStatus.Damaged)
                .ToList();
            var preferredAircraft = operationalSourceAircraft
                .Where(aircraft => CountRounds(
                    aircraft,
                    proposal.OrdnanceTypeDefinitionId) > 0)
                .OrderByDescending(aircraft => CountRounds(
                    aircraft,
                    proposal.OrdnanceTypeDefinitionId))
                .ThenBy(aircraft => aircraft.AircraftId)
                .FirstOrDefault();
            if (preferredAircraft == null)
                return false;

            var available = operationalSourceAircraft.Sum(aircraft => CountRounds(
                aircraft,
                proposal.OrdnanceTypeDefinitionId));
            var quantity = Math.Min(proposal.Quantity, available);
            if (quantity <= 0)
                return false;

            var preparationSeconds = ordnance.PreparationSeconds
                                     / source.AircraftType.OrdnanceEmploymentEfficiency;
            var pass = new ActiveOrdnanceEmploymentPass
            {
                SourceFlightId = source.Flight.FlightId,
                PreferredSourceAircraftId = preferredAircraft.AircraftId,
                TargetFlightId = target.Flight.FlightId,
                OrdnanceTypeDefinitionId = ordnance.OrdnanceTypeDefinitionId,
                PlannedQuantity = quantity,
                PreparationStartedAt = currentTime,
                ReleaseAt = currentTime.AddSeconds(preparationSeconds),
                LaunchQuality = Mathf.Min(launchQuality, proposal.LaunchQuality)
            };
            if (airToAirEmploymentValidator != null
                && !airToAirEmploymentValidator(pass, currentTime))
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    currentTime,
                    0,
                    "Employment preparation was not started because current "
                    + "tactical authorization does not permit the engagement.");
                return false;
            }
            ActivePasses.Add(pass);
            AddRecord(
                pass,
                OrdnanceEmploymentRecordStage.PreparationStarted,
                currentTime,
                quantity,
                $"{source.DisplayName} began preparing {quantity} "
                + $"{ordnance.Name} against {target.DisplayName}.");
            return true;
        }

        public bool TryStartAirToGroundPass(
            Guid sourceFlightId,
            Guid targetSiteId,
            Guid targetComponentId,
            Guid ordnanceTypeDefinitionId,
            DateTime currentTime)
        {
            if (sourceFlightId == Guid.Empty
                || targetSiteId == Guid.Empty
                || targetComponentId == Guid.Empty
                || ActivePasses.Any(pass => pass.SourceFlightId == sourceFlightId))
                return false;

            var contexts = BuildFlightContexts();
            if (!contexts.TryGetValue(sourceFlightId, out var source)
                || !source.Flight.IsAirborne
                || source.LiveAircraft.Count == 0
                || !gameManager.airDefenseSiteSystem.TryGetSite(
                    targetSiteId,
                    out var site)
                || gameManager.airDefenseSiteSystem.GetEffectiveAlliance(site)
                is var targetAlliance
                && (targetAlliance == Alliance.Neutral
                    || targetAlliance == source.Alliance)
                || site.IsDisabled
                || site.IsDestroyed
                || !ordnanceTypes.TryGetValue(
                    ordnanceTypeDefinitionId,
                    out var ordnance)
                || !DeadLoadoutPlanner.IsAirToGround(ordnance))
                return false;

            var component = site.Components.FirstOrDefault(candidate =>
                candidate != null
                && candidate.ComponentId == targetComponentId
                && !candidate.IsDamaged);
            if (component == null
                || !airDefenseComponentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var componentDefinition)
                || !DeadLoadoutPlanner.CanAttackComponent(
                    ordnance,
                    componentDefinition))
                return false;
            if (IsAntiRadiation(ordnance)
                && (component is not RadarAirDefenseComponent radar
                    || !radar.IsEmitting))
                return false;

            if (!TryGetSamSitePosition(site, out var targetPosition))
                return false;
            var distanceKm = HorizontalDistanceKm(
                source.Flight.PositionFeet,
                targetPosition);
            if (distanceKm < ordnance.MinimumRangeKm
                || distanceKm > ordnance.MaximumRangeKm)
                return false;

            var preferredAircraft = source.LiveAircraft
                .Where(aircraft => aircraft.Status != CampaignAircraftStatus.Damaged
                                   && CountRounds(
                                       aircraft,
                                       ordnanceTypeDefinitionId) > 0)
                .OrderByDescending(aircraft => CountRounds(
                    aircraft,
                    ordnanceTypeDefinitionId))
                .ThenBy(aircraft => aircraft.AircraftId)
                .FirstOrDefault();
            if (preferredAircraft == null)
                return false;

            var preparationSeconds = ordnance.PreparationSeconds
                                     / source.AircraftType.OrdnanceEmploymentEfficiency;
            var pass = new ActiveOrdnanceEmploymentPass
            {
                SourceFlightId = sourceFlightId,
                PreferredSourceAircraftId = preferredAircraft.AircraftId,
                TargetKind = OrdnanceEmploymentTargetKind.AirDefenseComponent,
                TargetSiteId = targetSiteId,
                TargetComponentId = targetComponentId,
                OrdnanceTypeDefinitionId = ordnanceTypeDefinitionId,
                PlannedQuantity = 1,
                PreparationStartedAt = currentTime,
                ReleaseAt = currentTime.AddSeconds(preparationSeconds),
                LaunchQuality = 1f
            };
            ActivePasses.Add(pass);
            AddRecord(
                pass,
                OrdnanceEmploymentRecordStage.PreparationStarted,
                currentTime,
                1,
                $"{source.DisplayName} began preparing {ordnance.Name} against "
                + $"SAM component {ShortId(targetComponentId)}.");
            return true;
        }

        public bool TryStartGroundAttackPass(
            Guid sourceFlightId,
            GroundAttackPassPlan plan,
            DateTime currentTime)
        {
            if (sourceFlightId == Guid.Empty
                || plan == null
                || plan.OrdnanceTypeDefinitionId == Guid.Empty
                || plan.PrimaryTargets == null
                || plan.PrimaryTargets.Count == 0
                || ActivePasses.Any(pass =>
                    pass.SourceFlightId == sourceFlightId))
                return false;

            var contexts = BuildFlightContexts();
            if (!contexts.TryGetValue(sourceFlightId, out var source)
                || !source.Flight.IsAirborne
                || source.LiveAircraft.Count == 0
                || !ordnanceTypes.TryGetValue(
                    plan.OrdnanceTypeDefinitionId,
                    out var ordnance)
                || !AirToGroundWeaponRules.IsAirToGround(ordnance))
                return false;

            var validTargets = new List<GroundAttackOpportunityTarget>();
            foreach (var target in plan.PrimaryTargets.Where(item => item != null))
            {
                if (!TryResolveGroundTarget(target, out var resolved)
                    || resolved.TileId != plan.TargetTileId
                    || resolved.Alliance == Alliance.Neutral
                    || resolved.Alliance == source.Alliance
                    || !AirToGroundWeaponRules.CanAffect(
                        ordnance,
                        target.TargetCategory,
                        target.TargetToughness)
                    || IsAntiRadiation(ordnance)
                    && (resolved.Component is not RadarAirDefenseComponent radar
                        || !radar.IsEmitting)
                    || !IsGroundTargetInRange(
                        source.Flight.PositionFeet,
                        resolved.PositionFeet,
                        ordnance))
                    continue;

                validTargets.Add(target.Clone());
            }
            if (validTargets.Count == 0)
                return false;

            var operationalAircraft = source.LiveAircraft
                .Where(aircraft => aircraft.Status
                                   != CampaignAircraftStatus.Damaged)
                .ToList();
            var preferredAircraft = operationalAircraft
                .Where(aircraft => CountRounds(
                    aircraft,
                    plan.OrdnanceTypeDefinitionId) > 0)
                .OrderByDescending(aircraft => CountRounds(
                    aircraft,
                    plan.OrdnanceTypeDefinitionId))
                .ThenBy(aircraft => aircraft.AircraftId)
                .FirstOrDefault();
            if (preferredAircraft == null)
                return false;

            var available = operationalAircraft.Sum(aircraft => CountRounds(
                aircraft,
                plan.OrdnanceTypeDefinitionId));
            validTargets = validTargets.Take(available).ToList();
            if (validTargets.Count == 0)
                return false;

            var secondaryCandidates = (plan.OpportunityTargets
                                       ?? new List<
                                           GroundAttackOpportunityTarget>())
                .Where(target => target?.Target != null
                                 && TryResolveGroundTarget(
                                     target,
                                     out var resolved)
                                 && resolved.TileId == plan.TargetTileId
                                 && AirToGroundWeaponRules.CanAffect(
                                     ordnance,
                                     target.TargetCategory,
                                     target.TargetToughness,
                                     ordnance.SecondaryGroundEffectMultiplier))
                .ToList();
            var plannedTargetGroups = BuildGroundEffectTargetGroups(
                validTargets,
                secondaryCandidates,
                ordnance.MaximumGroundTargetsPerWeapon);

            var firstTarget = validTargets[0].Target;
            var preparationSeconds = ordnance.PreparationSeconds
                                     / source.AircraftType.OrdnanceEmploymentEfficiency;
            var pass = new ActiveOrdnanceEmploymentPass
            {
                SourceFlightId = sourceFlightId,
                PreferredSourceAircraftId = preferredAircraft.AircraftId,
                TargetKind = firstTarget.Kind
                             == GroundAttackTargetKind.AirDefenseComponent
                    ? OrdnanceEmploymentTargetKind.AirDefenseComponent
                    : OrdnanceEmploymentTargetKind.GroundTarget,
                TargetSiteId = firstTarget.ParentEntityId,
                TargetComponentId = firstTarget.Kind
                                    == GroundAttackTargetKind
                                        .AirDefenseComponent
                    ? firstTarget.EntityId
                    : Guid.Empty,
                GroundTargetTileId = plan.TargetTileId,
                GroundPrimaryTargets = validTargets,
                GroundOpportunityTargets = plannedTargetGroups
                    .SelectMany(group => group)
                    .Select(target => target.Clone())
                    .ToList(),
                GroundOpportunityDescription =
                    plan.OpportunityDescription ?? string.Empty,
                OrdnanceTypeDefinitionId = plan.OrdnanceTypeDefinitionId,
                PlannedQuantity = validTargets.Count,
                PreparationStartedAt = currentTime,
                ReleaseAt = currentTime.AddSeconds(preparationSeconds),
                LaunchQuality = 1f
            };
            ActivePasses.Add(pass);
            AddRecord(
                pass,
                OrdnanceEmploymentRecordStage.PreparationStarted,
                currentTime,
                validTargets.Count,
                $"{source.DisplayName} began preparing {validTargets.Count} "
                + $"{ordnance.Name} for one ground-attack pass: "
                + pass.GroundOpportunityDescription);
            return true;
        }

        public bool HasActiveOrPendingEffect(Guid targetComponentId)
        {
            return targetComponentId != Guid.Empty
                   && (ActivePasses.Any(pass =>
                           pass.TargetKind
                           != OrdnanceEmploymentTargetKind.AirFlight
                           && (pass.TargetComponentId == targetComponentId
                               || pass.GroundPrimaryTargets?.Any(target =>
                                   target?.Target?.Kind
                                   == GroundAttackTargetKind
                                       .AirDefenseComponent
                                   && target.Target.EntityId
                                   == targetComponentId) == true
                               || pass.GroundOpportunityTargets?.Any(target =>
                                   target?.Target?.Kind
                                   == GroundAttackTargetKind
                                       .AirDefenseComponent
                                   && target.Target.EntityId
                                   == targetComponentId) == true))
                       || PendingEffects.Any(effect =>
                           effect.TargetKind
                           != OrdnanceEmploymentTargetKind.AirFlight
                           && (effect.TargetComponentId == targetComponentId
                               || effect.GroundPrimaryTarget?.Target?.Kind
                               == GroundAttackTargetKind.AirDefenseComponent
                               && effect.GroundPrimaryTarget.Target.EntityId
                               == targetComponentId
                               || effect.GroundOpportunityTargets?.Any(target =>
                                   target?.Target?.Kind
                                   == GroundAttackTargetKind
                                       .AirDefenseComponent
                                   && target.Target.EntityId
                                   == targetComponentId) == true)));
        }

        internal void CancelAirToAirPasses(
            IEnumerable<Guid> sourceFlightIds,
            DateTime currentTime,
            string reason)
        {
            if (sourceFlightIds == null)
                return;
            var sources = sourceFlightIds.ToHashSet();
            foreach (var pass in ActivePasses
                         .Where(candidate => sources.Contains(
                                                 candidate.SourceFlightId)
                                             && candidate.TargetKind
                                             == OrdnanceEmploymentTargetKind
                                                 .AirFlight)
                         .OrderBy(candidate => candidate.EmploymentPassId)
                         .ToList())
            {
                ActivePasses.Remove(pass);
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    currentTime,
                    0,
                    reason);
            }
        }

        internal void CancelAirToGroundPasses(
            IEnumerable<Guid> sourceFlightIds,
            DateTime currentTime,
            string reason)
        {
            if (sourceFlightIds == null)
                return;
            var sources = sourceFlightIds.ToHashSet();
            foreach (var pass in ActivePasses
                         .Where(candidate => sources.Contains(
                                                 candidate.SourceFlightId)
                                             && candidate.TargetKind
                                             != OrdnanceEmploymentTargetKind
                                                 .AirFlight)
                         .OrderBy(candidate => candidate.EmploymentPassId)
                         .ToList())
            {
                ActivePasses.Remove(pass);
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    currentTime,
                    0,
                    reason);
            }
        }

        internal bool TryReleaseWvrAttack(
            Guid sourceFlightId,
            Guid targetFlightId,
            Guid ordnanceTypeDefinitionId,
            float hitProbability,
            DateTime currentTime,
            Guid engagementId,
            int roundNumber,
            WvrAdvantageLevel advantage,
            bool targetAware)
        {
            var contexts = BuildFlightContexts();
            if (!contexts.TryGetValue(sourceFlightId, out var source)
                || !contexts.TryGetValue(targetFlightId, out var target)
                || source.Alliance == target.Alliance
                || source.LiveAircraft.Count == 0
                || target.LiveAircraft.Count == 0
                || !source.Flight.IsAirborne
                || !target.Flight.IsAirborne
                || !ordnanceTypes.TryGetValue(
                    ordnanceTypeDefinitionId,
                    out var ordnance)
                || !IsWvrWeapon(ordnance))
                return false;

            var preferredAircraft = source.LiveAircraft
                .Where(aircraft => CountRounds(
                    aircraft,
                    ordnanceTypeDefinitionId) > 0)
                .OrderBy(aircraft =>
                    aircraft.Status == CampaignAircraftStatus.Damaged ? 1 : 0)
                .ThenByDescending(aircraft => CountRounds(
                    aircraft,
                    ordnanceTypeDefinitionId))
                .ThenBy(aircraft => aircraft.AircraftId)
                .FirstOrDefault();
            if (preferredAircraft == null)
                return false;

            var launches = SpendFlightRounds(
                source,
                target,
                ordnanceTypeDefinitionId,
                preferredAircraft.AircraftId,
                1,
                currentTime,
                preferDamagedTargets: true,
                allowDamagedSources: true);
            if (launches.Count == 0)
                return false;

            var pending = CreatePendingOrdnanceEffect(new AuthorizedOrdnanceRelease
            {
                EmploymentPassId = engagementId,
                SourceKind = OrdnanceEmploymentSourceKind.AircraftFlight,
                SourceFlightId = sourceFlightId,
                SourceAircraftId = launches[0].SourceAircraftId,
                Target = target,
                Ordnance = ordnance,
                Quantity = 1,
                ReleasedAt = currentTime,
                SourcePositionFeet = source.Flight.PositionFeet,
                Launches = launches,
                MaximumRangeKm = ordnance.MaximumRangeKm,
                ShooterSensorQuality = source.AircraftType.RadarQuality,
                LaunchQuality = 1f,
                ReleaseRangeKm = Vector3.Distance(
                                     source.Flight.PositionFeet,
                                     target.Flight.PositionFeet)
                                 / AirspaceGeometry.FeetPerKilometer,
                HitProbabilityOverride = Mathf.Clamp01(hitProbability),
                ResolveImmediately = true
            });
            PendingEffects.Add(pending);
            AddRecord(
                pending,
                OrdnanceEmploymentRecordStage.OrdnanceReleased,
                currentTime,
                1,
                $"{source.DisplayName} used {ordnance.Name} against "
                + $"{target.DisplayName} in WVR round {roundNumber} "
                + $"({advantage}, target {(targetAware ? "aware" : "unaware")}).");
            return true;
        }

        public void UpdateOrdnanceGuidance(DateTime currentTime)
        {
            var contexts = BuildFlightContexts();
            foreach (var effect in PendingEffects
                         .Where(item => item.ResolveAt > item.ReleasedAt)
                         .OrderBy(item => item.PendingEffectId))
            {
                if (!ordnanceTypes.TryGetValue(effect.OrdnanceTypeDefinitionId, out var ordnance))
                    continue;
                if (effect.IsDefeated)
                    continue;

                var previous = effect.LastGuidanceUpdateAt == default
                    ? effect.ReleasedAt
                    : effect.LastGuidanceUpdateAt;
                var boundedCurrent = currentTime > effect.ResolveAt
                    ? effect.ResolveAt
                    : currentTime;
                var seconds = Math.Max(0d, (boundedCurrent - previous).TotalSeconds);
                if (seconds <= 0d)
                    continue;

                if (effect.TargetKind
                        == OrdnanceEmploymentTargetKind.AirDefenseComponent
                    && IsAntiRadiation(ordnance)
                    && TryGetTargetRadar(effect, out var targetRadar)
                    && targetRadar.IsEmitting)
                {
                    effect.LastTargetEmissionAt = boundedCurrent;
                }

                contexts.TryGetValue(effect.TargetFlightId, out var defendedTarget);
                var maximumRangeKm = effect.MaximumRangeKmAtRelease > 0f
                    ? effect.MaximumRangeKmAtRelease
                    : ordnance.MaximumRangeKm;
                if (defendedTarget != null
                    && IsRadarGuided(ordnance.GuidanceMode)
                    && maximumRangeKm > 0f
                    && Vector3.Distance(
                           effect.SourcePositionFeet,
                           defendedTarget.Flight.PositionFeet)
                       / AirspaceGeometry.FeetPerKilometer
                    > maximumRangeKm)
                {
                    effect.DefeatReason =
                        OrdnanceDefeatReason.KinematicRangeExceeded;
                    effect.LastGuidanceUpdateAt = boundedCurrent;
                    continue;
                }

                if (effect.GuidanceStage == OrdnanceGuidanceStage.Midcourse
                    && effect.AutonomousAt != default
                    && boundedCurrent >= effect.AutonomousAt)
                    effect.GuidanceStage = OrdnanceGuidanceStage.Autonomous;
                if ((effect.ResolveAt - boundedCurrent).TotalSeconds <= 10d)
                    effect.GuidanceStage = OrdnanceGuidanceStage.Terminal;

                var supportEnd = effect.SupportRequired
                    ? effect.AutonomousAt < effect.ResolveAt
                        ? effect.AutonomousAt
                        : effect.ResolveAt
                    : previous;
                var supportIntervalEnd = boundedCurrent < supportEnd
                    ? boundedCurrent
                    : supportEnd;
                var supportIntervalSeconds = Math.Max(
                    0d,
                    (supportIntervalEnd - previous).TotalSeconds);
                effect.SupportSeconds += (float)supportIntervalSeconds;
                var supported = !effect.SupportRequired;
                if (supportIntervalSeconds > 0d
                    && contexts.TryGetValue(effect.TargetFlightId, out var target))
                {
                    supported = effect.SourceKind switch
                    {
                        OrdnanceEmploymentSourceKind.AircraftFlight =>
                            CanFlightSupportGuidance(
                                effect,
                                target,
                                ordnance,
                                contexts),
                        OrdnanceEmploymentSourceKind.SamLauncher =>
                            CanSamSupportGuidance(effect, target),
                        _ => false
                    };
                }
                if (supported)
                    effect.SupportedSeconds += (float)supportIntervalSeconds;

                if (defendedTarget != null)
                {
                    if (TryGetGuidanceSourcePosition(
                            effect,
                            contexts,
                            out var threatSourcePosition))
                    {
                        effect.PrincipalThreatBearingDegrees = AirCombatRules.HeadingTo(
                            defendedTarget.Flight.PositionFeet,
                            threatSourcePosition);
                    }
                    if (IsRadarGuided(ordnance.GuidanceMode))
                    {
                        var secondsToImpact =
                            (effect.ResolveAt - boundedCurrent).TotalSeconds;
                        if (secondsToImpact
                            <= AirCombatRules.TerminalDefenseSeconds)
                        {
                            var defensiveStrength =
                                CalculateRadarDefensiveManeuverStrength(
                                    defendedTarget.Flight.TacticalState.Maneuver,
                                    defendedTarget.Flight.HeadingDegrees,
                                    effect.PrincipalThreatBearingDegrees);
                            effect.DefensiveSeconds +=
                                (float)seconds * defensiveStrength;
                            var flightSeconds = Math.Max(
                                1d,
                                (effect.ResolveAt - effect.ReleasedAt)
                                .TotalSeconds);
                            var lockBreakWindowSeconds = Math.Min(
                                AirCombatRules.TerminalDefenseSeconds,
                                flightSeconds);
                            var lockBreakChance =
                                defendedTarget.AircraftType.GetDefenseAgainst(
                                    ordnance)
                                * Mathf.Clamp01(
                                    effect.DefensiveSeconds
                                    / (float)lockBreakWindowSeconds);
                            if (StableRoll(
                                    effect.PendingEffectId,
                                    RadarLockBreakRollSequence)
                                < lockBreakChance)
                            {
                                effect.DefeatReason =
                                    OrdnanceDefeatReason.RadarLockBroken;
                            }
                        }
                    }
                    else
                    {
                        var defensiveStrength =
                            CalculateDefensiveManeuverStrength(
                                defendedTarget.Flight.TacticalState.Maneuver,
                                defendedTarget.Flight.HeadingDegrees,
                                effect.PrincipalThreatBearingDegrees);
                        effect.DefensiveSeconds +=
                            (float)seconds * defensiveStrength;
                    }
                }
                effect.LastGuidanceUpdateAt = boundedCurrent;
            }
        }

        private static bool CanFlightSupportGuidance(
            PendingOrdnanceEffect effect,
            FlightContext target,
            OrdnanceTypeDefinition ordnance,
            IReadOnlyDictionary<Guid, FlightContext> contexts)
        {
            var supportFlightId = effect.SupportSourceFlightId != Guid.Empty
                ? effect.SupportSourceFlightId
                : effect.SourceFlightId;
            return contexts.TryGetValue(supportFlightId, out var source)
                   && AirCombatRules.AngleOffNose(
                       source.Flight,
                       target.Flight.PositionFeet)
                   <= ordnance.MaximumSupportAngleDegrees;
        }

        private bool CanSamSupportGuidance(
            PendingOrdnanceEffect effect,
            FlightContext target)
        {
            var supportSiteId = effect.SupportSourceSiteId != Guid.Empty
                ? effect.SupportSourceSiteId
                : effect.SourceSiteId;
            if (!gameManager.airDefenseSiteSystem.TryGetSite(
                    supportSiteId,
                    out var supportSite)
                || !TryGetSamSitePosition(supportSite, out var supportPosition))
                return false;

            var radar = gameManager.airDefenseSiteSystem
                .GetAvailableComponents(supportSite)
                .OfType<RadarAirDefenseComponent>()
                .FirstOrDefault(component =>
                    !component.IsDamaged
                    && component.IsEmitting
                    && (effect.SupportSourceComponentId == Guid.Empty
                        || component.ComponentId == effect.SupportSourceComponentId));
            if (radar == null
                || !airDefenseComponentDefinitions.TryGetValue(
                    radar.SamComponentDefinitionId,
                    out var componentDefinition)
                || componentDefinition is not RadarAirDefenseComponentDefinition definition
                || !definition.ProvidesWeaponQualityTrack)
                return false;

            return IsTargetInsideRadarEnvelope(
                definition,
                supportPosition,
                target.Flight.PositionFeet,
                target.AircraftType.RadarDetectability);
        }

        private bool TryGetGuidanceSourcePosition(
            PendingOrdnanceEffect effect,
            IReadOnlyDictionary<Guid, FlightContext> contexts,
            out Vector3 position)
        {
            if (effect.SourceKind == OrdnanceEmploymentSourceKind.AircraftFlight)
            {
                var supportFlightId = effect.SupportSourceFlightId != Guid.Empty
                    ? effect.SupportSourceFlightId
                    : effect.SourceFlightId;
                if (contexts.TryGetValue(supportFlightId, out var source))
                {
                    position = source.Flight.PositionFeet;
                    return true;
                }
            }
            else if (effect.SourceKind == OrdnanceEmploymentSourceKind.SamLauncher)
            {
                var supportSiteId = effect.SupportSourceSiteId != Guid.Empty
                    ? effect.SupportSourceSiteId
                    : effect.SourceSiteId;
                if (gameManager.airDefenseSiteSystem.TryGetSite(
                        supportSiteId,
                        out var supportSite)
                    && TryGetSamSitePosition(supportSite, out position))
                    return true;
            }

            position = effect.SourcePositionFeet;
            return true;
        }

        internal static float CalculateDefensiveManeuverStrength(
            AirCombatManeuver maneuver,
            float headingDegrees,
            float threatBearingDegrees)
        {
            var relativeBearing = Math.Abs(Mathf.DeltaAngle(
                headingDegrees,
                threatBearingDegrees));
            var perpendicularStrength = 1f - Mathf.Clamp01(
                Math.Abs(relativeBearing - 90f) / 90f);
            var dragStrength = Mathf.Clamp01(relativeBearing / 180f);

            return maneuver switch
            {
                AirCombatManeuver.BeamLeft => perpendicularStrength,
                AirCombatManeuver.BeamRight => perpendicularStrength,
                AirCombatManeuver.BreakLeft => perpendicularStrength,
                AirCombatManeuver.BreakRight => perpendicularStrength,
                AirCombatManeuver.Drag => 0.75f * dragStrength,
                AirCombatManeuver.Extend => 0.4f,
                _ => 0f
            };
        }

        internal static float CalculateRadarDefensiveManeuverStrength(
            AirCombatManeuver maneuver,
            float headingDegrees,
            float threatBearingDegrees)
        {
            if (maneuver == AirCombatManeuver.Extend)
                return 0f;

            return CalculateDefensiveManeuverStrength(
                maneuver,
                headingDegrees,
                threatBearingDegrees);
        }

        public void RefreshTacticalState(DateTime currentTime)
        {
            var contexts = BuildFlightContexts();
            ProcessDueEvents(currentTime);

            contexts = BuildFlightContexts();
            RefreshSamEngagementAssignments(contexts, currentTime);
            RefreshRadarEmissionPostures(currentTime);
            LaunchAssignedSamShots(contexts, currentTime);
            ProcessDueEvents(currentTime);
            RefreshRadarEmissionPostures(currentTime);

            TrimEmploymentRecords();
        }

        private void ProcessDueEvents(DateTime currentTime)
        {
            while (true)
            {
                var nextRelease = ActivePasses
                    .Where(pass => pass.ReleaseAt <= currentTime)
                    .Select(pass => (DateTime?)pass.ReleaseAt)
                    .DefaultIfEmpty()
                    .Min();
                var nextEffect = PendingEffects
                    .Where(effect => effect.ResolveAt <= currentTime)
                    .Select(effect => (DateTime?)effect.ResolveAt)
                    .DefaultIfEmpty()
                    .Min();
                var nextEventAt = Earlier(nextRelease, nextEffect);
                if (!nextEventAt.HasValue)
                    return;

                var timestamp = nextEventAt.Value;
                var releases = ActivePasses
                    .Where(pass => pass.ReleaseAt == timestamp)
                    .OrderBy(pass => pass.SourceFlightId)
                    .ThenBy(pass => pass.EmploymentPassId)
                    .ToList();
                foreach (var pass in releases)
                    ReleaseFlightPass(pass, timestamp);

                var effects = PendingEffects
                    .Where(effect => effect.ResolveAt == timestamp)
                    .OrderBy(effect => effect.TargetFlightId)
                    .ThenBy(effect => effect.PendingEffectId)
                    .ToList();
                if (effects.Count > 0)
                    ResolveEffectBatch(effects, timestamp);
            }
        }

        private static DateTime? Earlier(DateTime? first, DateTime? second)
        {
            if (!first.HasValue)
                return second;
            if (!second.HasValue)
                return first;
            return first.Value <= second.Value ? first : second;
        }

        private void ReleaseFlightPass(
            ActiveOrdnanceEmploymentPass pass,
            DateTime releaseAt)
        {
            ActivePasses.Remove(pass);
            if (pass.TargetKind != OrdnanceEmploymentTargetKind.AirFlight)
            {
                ReleaseAirToGroundPass(pass, releaseAt);
                return;
            }
            var contexts = BuildFlightContexts();
            if (!contexts.TryGetValue(pass.SourceFlightId, out var source)
                || !contexts.TryGetValue(pass.TargetFlightId, out var target)
                || source.LiveAircraft.Count == 0
                || !source.LiveAircraft.Any(aircraft =>
                    aircraft.Status != CampaignAircraftStatus.Damaged)
                || target.LiveAircraft.Count == 0
                || !ordnanceTypes.TryGetValue(
                    pass.OrdnanceTypeDefinitionId,
                    out var ordnance))
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    "Employment preparation aborted because its source or target was no longer valid.");
                return;
            }

            if (airToAirEmploymentValidator != null
                && !airToAirEmploymentValidator(pass, releaseAt))
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    "Employment preparation aborted because current tactical "
                    + "authorization no longer permits the engagement.");
                return;
            }

            if (!AirCombatRules.EvaluateLaunch(
                    source.Flight,
                    source.AircraftType,
                    target.Flight,
                    ordnance,
                    out var releaseQuality))
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    "Employment preparation aborted because release geometry was no longer valid.");
                return;
            }

            var launches = SpendFlightRounds(
                source,
                target,
                pass.OrdnanceTypeDefinitionId,
                pass.PreferredSourceAircraftId,
                pass.PlannedQuantity,
                releaseAt);
            var released = launches.Count;
            if (released <= 0)
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    "Employment preparation aborted because no selected ordnance remained.");
                return;
            }

            var distanceFeet = Vector3.Distance(
                source.Flight.PositionFeet,
                target.Flight.PositionFeet);
            var releaseRangeKm = distanceFeet / AirspaceGeometry.FeetPerKilometer;
            var launchQuality = Mathf.Clamp01(Mathf.Min(pass.LaunchQuality, releaseQuality));
            var pending = CreatePendingOrdnanceEffect(new AuthorizedOrdnanceRelease
            {
                EmploymentPassId = pass.EmploymentPassId,
                SourceKind = OrdnanceEmploymentSourceKind.AircraftFlight,
                SourceFlightId = source.Flight.FlightId,
                SourceAircraftId = launches[0].SourceAircraftId,
                Target = target,
                Ordnance = ordnance,
                Quantity = released,
                ReleasedAt = releaseAt,
                SourcePositionFeet = source.Flight.PositionFeet,
                Launches = launches,
                SupportSourceFlightId = source.Flight.FlightId,
                MaximumRangeKm = Math.Max(
                    AirCombatRules.EffectiveMaximumRangeKm(
                        ordnance,
                        source.Flight),
                    releaseRangeKm),
                ShooterSensorQuality = source.AircraftType.RadarQuality,
                LaunchQuality = launchQuality,
                ReleaseRangeKm = releaseRangeKm
            });
            PendingEffects.Add(pending);
            AddRecord(
                pending,
                OrdnanceEmploymentRecordStage.OrdnanceReleased,
                releaseAt,
                released,
                $"{source.DisplayName} released {released} {ordnance.Name} " +
                $"against {target.DisplayName}.");
        }

        private void ReleaseAirToGroundPass(
            ActiveOrdnanceEmploymentPass pass,
            DateTime releaseAt)
        {
            if (pass.GroundPrimaryTargets != null
                && pass.GroundPrimaryTargets.Count > 0)
            {
                ReleasePlannedGroundAttackPass(pass, releaseAt);
                return;
            }

            var contexts = BuildFlightContexts();
            if (!contexts.TryGetValue(pass.SourceFlightId, out var source)
                || !source.Flight.IsAirborne
                || !gameManager.airDefenseSiteSystem.TryGetSite(
                    pass.TargetSiteId,
                    out var site)
                || gameManager.airDefenseSiteSystem.GetEffectiveAlliance(site)
                is var targetAlliance
                && (targetAlliance == Alliance.Neutral
                    || targetAlliance == source.Alliance)
                || site.IsDisabled
                || site.IsDestroyed
                || !ordnanceTypes.TryGetValue(
                    pass.OrdnanceTypeDefinitionId,
                    out var ordnance))
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    "Ground-attack preparation aborted because its source or target was no longer valid.");
                return;
            }

            var component = site.Components.FirstOrDefault(candidate =>
                candidate != null
                && candidate.ComponentId == pass.TargetComponentId
                && !candidate.IsDamaged);
            if (component == null
                || !airDefenseComponentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var componentDefinition)
                || !DeadLoadoutPlanner.CanAttackComponent(
                    ordnance,
                    componentDefinition))
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    "Ground-attack preparation aborted because the selected component was no longer functional.");
                return;
            }
            if (IsAntiRadiation(ordnance)
                && (component is not RadarAirDefenseComponent radar
                    || !radar.IsEmitting))
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    "Anti-radiation preparation aborted because the selected radar stopped emitting before release.");
                return;
            }

            if (!TryGetSamSitePosition(site, out var targetPosition))
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    "Ground-attack preparation aborted because the selected site position was unavailable.");
                return;
            }
            var distanceKm = HorizontalDistanceKm(
                source.Flight.PositionFeet,
                targetPosition);
            if (distanceKm < ordnance.MinimumRangeKm
                || distanceKm > ordnance.MaximumRangeKm)
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    "Ground-attack preparation aborted because release range was no longer valid.");
                return;
            }

            var launch = SpendGroundRound(
                source,
                pass.OrdnanceTypeDefinitionId,
                pass.PreferredSourceAircraftId,
                pass.TargetComponentId,
                releaseAt);
            if (launch == null)
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    "Ground-attack preparation aborted because no selected ordnance remained.");
                return;
            }

            var rangeSpan = Math.Max(
                0.01f,
                ordnance.MaximumRangeKm - ordnance.MinimumRangeKm);
            var rangeRatio = Mathf.Clamp01(
                (distanceKm - ordnance.MinimumRangeKm) / rangeSpan);
            var hitProbability = Mathf.Clamp01(
                ordnance.HitProbability
                * ordnance.GetEffectiveness(componentDefinition.TargetCategory)
                * (1f - 0.2f * rangeRatio));
            var travelSeconds = AirspaceGeometry.HorizontalTravelSeconds(
                Vector3.Distance(
                    source.Flight.PositionFeet,
                    targetPosition),
                ordnance.EffectSpeedKnots);
            var pending = new PendingOrdnanceEffect
            {
                EmploymentPassId = pass.EmploymentPassId,
                SourceKind = OrdnanceEmploymentSourceKind.AircraftFlight,
                SourceFlightId = source.Flight.FlightId,
                SourceAircraftId = launch.SourceAircraftId,
                TargetKind = OrdnanceEmploymentTargetKind.AirDefenseComponent,
                TargetSiteId = pass.TargetSiteId,
                TargetComponentId = pass.TargetComponentId,
                OrdnanceTypeDefinitionId = ordnance.OrdnanceTypeDefinitionId,
                Quantity = 1,
                HitProbability = hitProbability,
                ReleasedAt = releaseAt,
                ResolveAt = releaseAt.AddSeconds(travelSeconds),
                ReleaseRangeKm = distanceKm,
                MaximumRangeKmAtRelease = ordnance.MaximumRangeKm,
                SourcePositionFeet = source.Flight.PositionFeet,
                TargetPositionFeet = targetPosition,
                Launches = new List<OrdnanceLaunchDiagnostic> { launch },
                GuidanceStage = OrdnanceGuidanceStage.Autonomous,
                AutonomousAt = releaseAt,
                LastGuidanceUpdateAt = releaseAt,
                LastTargetEmissionAt = IsAntiRadiation(ordnance)
                    ? releaseAt
                    : default,
                LaunchQuality = 1f
            };
            PendingEffects.Add(pending);
            AddRecord(
                pending,
                OrdnanceEmploymentRecordStage.OrdnanceReleased,
                releaseAt,
                1,
                $"{source.DisplayName} released {ordnance.Name} against SAM "
                + $"component {ShortId(pass.TargetComponentId)}.");
        }

        private void ReleasePlannedGroundAttackPass(
            ActiveOrdnanceEmploymentPass pass,
            DateTime releaseAt)
        {
            var contexts = BuildFlightContexts();
            if (!contexts.TryGetValue(pass.SourceFlightId, out var source)
                || !source.Flight.IsAirborne
                || !ordnanceTypes.TryGetValue(
                    pass.OrdnanceTypeDefinitionId,
                    out var ordnance))
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    "Ground-attack preparation aborted because its source or ordnance was no longer valid.");
                return;
            }

            var validTargets = new List<(
                GroundAttackOpportunityTarget Target,
                Vector3 PositionFeet)>();
            var hadOtherwiseValidOutOfRangeTarget = false;
            foreach (var target in pass.GroundPrimaryTargets
                         .Where(item => item != null))
            {
                if (!TryResolveGroundTarget(target, out var resolved)
                    || resolved.TileId != pass.GroundTargetTileId
                    || resolved.Alliance == Alliance.Neutral
                    || resolved.Alliance == source.Alliance
                    || !AirToGroundWeaponRules.CanAffect(
                        ordnance,
                        target.TargetCategory,
                        target.TargetToughness)
                    || IsAntiRadiation(ordnance)
                    && (resolved.Component is not RadarAirDefenseComponent radar
                        || !radar.IsEmitting))
                    continue;

                if (!IsGroundTargetInRange(
                        source.Flight.PositionFeet,
                        resolved.PositionFeet,
                        ordnance))
                {
                    hadOtherwiseValidOutOfRangeTarget = true;
                    continue;
                }

                validTargets.Add((target, resolved.PositionFeet));
            }
            if (validTargets.Count == 0)
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    hadOtherwiseValidOutOfRangeTarget
                        ? "Ground-attack preparation aborted because release range was no longer valid."
                        : "Ground-attack preparation aborted because no selected target remained valid.");
                return;
            }

            var targetReferences = validTargets
                .Select(target => target.Target)
                .ToList();
            var launches = SpendGroundRounds(
                source,
                pass.OrdnanceTypeDefinitionId,
                pass.PreferredSourceAircraftId,
                targetReferences,
                releaseAt);
            if (launches.Count == 0)
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    "Ground-attack preparation aborted because no selected ordnance remained.");
                return;
            }

            validTargets = validTargets.Take(launches.Count).ToList();
            targetReferences = targetReferences.Take(launches.Count).ToList();
            var targetGroups = BuildGroundEffectTargetGroups(
                targetReferences,
                pass.GroundOpportunityTargets,
                ordnance.MaximumGroundTargetsPerWeapon);

            for (var index = 0; index < launches.Count; index++)
            {
                var launch = launches[index];
                launch.Sequence = 1;
                var primary = validTargets[index].Target.Clone();
                var targetPosition = validTargets[index].PositionFeet;
                var distanceKm = HorizontalDistanceKm(
                    source.Flight.PositionFeet,
                    targetPosition);
                var rangeSpan = Math.Max(
                    0.01f,
                    ordnance.MaximumRangeKm - ordnance.MinimumRangeKm);
                var rangeRatio = Mathf.Clamp01(
                    (distanceKm - ordnance.MinimumRangeKm) / rangeSpan);
                var travelSeconds = AirspaceGeometry.HorizontalTravelSeconds(
                    Vector3.Distance(source.Flight.PositionFeet, targetPosition),
                    ordnance.EffectSpeedKnots);
                var hitProbability = Mathf.Clamp01(
                    ordnance.HitProbability
                    * ordnance.GetEffectiveness(primary.TargetCategory)
                    * (1f - 0.2f * rangeRatio));
                var pending = new PendingOrdnanceEffect
                {
                    EmploymentPassId = pass.EmploymentPassId,
                    SourceKind = OrdnanceEmploymentSourceKind.AircraftFlight,
                    SourceFlightId = source.Flight.FlightId,
                    SourceAircraftId = launch.SourceAircraftId,
                    TargetKind = primary.Target.Kind
                                 == GroundAttackTargetKind.AirDefenseComponent
                        ? OrdnanceEmploymentTargetKind.AirDefenseComponent
                        : OrdnanceEmploymentTargetKind.GroundTarget,
                    TargetSiteId = primary.Target.ParentEntityId,
                    TargetComponentId = primary.Target.Kind
                                        == GroundAttackTargetKind
                                            .AirDefenseComponent
                        ? primary.Target.EntityId
                        : Guid.Empty,
                    GroundPrimaryTarget = primary,
                    GroundOpportunityTargets = targetGroups[index],
                    GroundOpportunityDescription =
                        pass.GroundOpportunityDescription,
                    OrdnanceTypeDefinitionId = ordnance.OrdnanceTypeDefinitionId,
                    Quantity = 1,
                    HitProbability = hitProbability,
                    ReleasedAt = releaseAt,
                    ResolveAt = releaseAt.AddSeconds(travelSeconds),
                    ReleaseRangeKm = distanceKm,
                    MaximumRangeKmAtRelease = ordnance.MaximumRangeKm,
                    SourcePositionFeet = source.Flight.PositionFeet,
                    TargetPositionFeet = targetPosition,
                    Launches = new List<OrdnanceLaunchDiagnostic> { launch },
                    GuidanceStage = OrdnanceGuidanceStage.Autonomous,
                    AutonomousAt = releaseAt,
                    LastGuidanceUpdateAt = releaseAt,
                    LastTargetEmissionAt = IsAntiRadiation(ordnance)
                        ? releaseAt
                        : default,
                    LaunchQuality = 1f
                };
                PendingEffects.Add(pending);
                AddRecord(
                    pending,
                    OrdnanceEmploymentRecordStage.OrdnanceReleased,
                    releaseAt,
                    1,
                    $"{source.DisplayName} released {ordnance.Name} against "
                    + primary.Description + ".");
            }
        }

        private static List<List<GroundAttackOpportunityTarget>>
            BuildGroundEffectTargetGroups(
                IReadOnlyList<GroundAttackOpportunityTarget> primaryTargets,
                IReadOnlyList<GroundAttackOpportunityTarget> opportunityTargets,
                int maximumTargetsPerWeapon)
        {
            var groups = primaryTargets
                .Select(primary => new List<GroundAttackOpportunityTarget>
                {
                    primary.Clone()
                })
                .ToList();
            if (groups.Count == 0 || maximumTargetsPerWeapon <= 1)
                return groups;

            var secondaryPool = (opportunityTargets
                                 ?? Array.Empty<GroundAttackOpportunityTarget>())
                .Where(target => target?.Target != null)
                .Select(target => target.Clone())
                .ToList();
            foreach (var primary in primaryTargets)
            {
                var matchIndex = secondaryPool.FindIndex(candidate =>
                    SameOpportunityTarget(candidate, primary));
                if (matchIndex >= 0)
                    secondaryPool.RemoveAt(matchIndex);
            }

            var nextGroup = 0;
            foreach (var secondary in secondaryPool)
            {
                var attempts = 0;
                while (attempts < groups.Count
                       && groups[nextGroup].Count >= maximumTargetsPerWeapon)
                {
                    nextGroup = (nextGroup + 1) % groups.Count;
                    attempts++;
                }
                if (attempts >= groups.Count)
                    break;

                groups[nextGroup].Add(secondary);
                nextGroup = (nextGroup + 1) % groups.Count;
            }
            return groups;
        }

        private static List<OrdnanceLaunchDiagnostic> SpendFlightRounds(
            FlightContext source,
            FlightContext target,
            Guid ordnanceTypeDefinitionId,
            Guid preferredAircraftId,
            int requested,
            DateTime releaseAt,
            bool preferDamagedTargets = false,
            bool allowDamagedSources = false)
        {
            var remaining = Math.Max(0, requested);
            var launches = new List<OrdnanceLaunchDiagnostic>();
            var targetAircraft = target.LiveAircraft
                .OrderBy(aircraft => preferDamagedTargets
                                     && aircraft.Status
                                     == CampaignAircraftStatus.Damaged
                    ? 0
                    : 1)
                .ThenBy(aircraft => aircraft.AircraftId)
                .ToList();
            foreach (var aircraft in source.LiveAircraft
                         .Where(aircraft =>
                             allowDamagedSources
                             || aircraft.Status
                             != CampaignAircraftStatus.Damaged)
                         .OrderBy(aircraft =>
                             aircraft.AircraftId == preferredAircraftId ? 0 : 1)
                         .ThenBy(aircraft =>
                             aircraft.Status == CampaignAircraftStatus.Damaged
                                 ? 1
                                 : 0)
                         .ThenBy(aircraft => aircraft.AircraftId))
            {
                var item = aircraft.Loadout.FirstOrDefault(candidate =>
                    candidate.OrdnanceTypeDefinitionId == ordnanceTypeDefinitionId
                    && candidate.Count > 0);
                if (item == null)
                    continue;

                var spent = Math.Min(item.Count, remaining);
                item.Count -= spent;
                for (var index = 0; index < spent; index++)
                {
                    var sequence = launches.Count + 1;
                    var targetIndex = targetAircraft.Count == 0
                        ? -1
                        : (sequence - 1) % targetAircraft.Count;
                    var targetAircraftId = targetIndex < 0
                        ? Guid.Empty
                        : targetAircraft[targetIndex].AircraftId;
                    launches.Add(new OrdnanceLaunchDiagnostic
                    {
                        Sequence = sequence,
                        SourceAircraftId = aircraft.AircraftId,
                        TargetAircraftId = targetAircraftId,
                        OrdnanceTypeDefinitionId = ordnanceTypeDefinitionId,
                        ReleasedAt = releaseAt
                    });
                }
                remaining -= spent;
                if (remaining == 0)
                    break;
            }
            foreach (var aircraft in source.LiveAircraft)
                aircraft.Loadout.RemoveAll(item => item.Count <= 0);
            return launches;
        }

        private static List<OrdnanceLaunchDiagnostic> SpendGroundRounds(
            FlightContext source,
            Guid ordnanceTypeDefinitionId,
            Guid preferredAircraftId,
            IReadOnlyList<GroundAttackOpportunityTarget> targets,
            DateTime releaseAt)
        {
            var launches = new List<OrdnanceLaunchDiagnostic>();
            if (targets == null || targets.Count == 0)
                return launches;

            foreach (var aircraft in source.LiveAircraft
                         .Where(candidate => candidate.Status
                                             != CampaignAircraftStatus.Damaged)
                         .OrderBy(candidate =>
                             candidate.AircraftId == preferredAircraftId ? 0 : 1)
                         .ThenBy(candidate => candidate.AircraftId))
            {
                var item = aircraft.Loadout.FirstOrDefault(candidate =>
                    candidate.OrdnanceTypeDefinitionId
                    == ordnanceTypeDefinitionId
                    && candidate.Count > 0);
                if (item == null)
                    continue;

                while (item.Count > 0 && launches.Count < targets.Count)
                {
                    var target = targets[launches.Count];
                    item.Count--;
                    launches.Add(new OrdnanceLaunchDiagnostic
                    {
                        Sequence = launches.Count + 1,
                        SourceAircraftId = aircraft.AircraftId,
                        TargetAircraftId = target.Target.EntityId,
                        GroundTarget = target.Target.Clone(),
                        OrdnanceTypeDefinitionId = ordnanceTypeDefinitionId,
                        ReleasedAt = releaseAt
                    });
                }
                if (launches.Count >= targets.Count)
                    break;
            }

            foreach (var aircraft in source.LiveAircraft)
                aircraft.Loadout.RemoveAll(item => item.Count <= 0);
            return launches;
        }

        private static OrdnanceLaunchDiagnostic SpendGroundRound(
            FlightContext source,
            Guid ordnanceTypeDefinitionId,
            Guid preferredAircraftId,
            Guid targetComponentId,
            DateTime releaseAt)
        {
            var aircraft = source.LiveAircraft
                .Where(candidate => candidate.Status
                                    != CampaignAircraftStatus.Damaged)
                .OrderBy(candidate => candidate.AircraftId == preferredAircraftId
                    ? 0
                    : 1)
                .ThenBy(candidate => candidate.AircraftId)
                .FirstOrDefault(candidate => CountRounds(
                    candidate,
                    ordnanceTypeDefinitionId) > 0);
            if (aircraft == null)
                return null;

            var item = aircraft.Loadout.First(candidate =>
                candidate.OrdnanceTypeDefinitionId == ordnanceTypeDefinitionId
                && candidate.Count > 0);
            item.Count--;
            aircraft.Loadout.RemoveAll(candidate => candidate.Count <= 0);
            return new OrdnanceLaunchDiagnostic
            {
                Sequence = 1,
                SourceAircraftId = aircraft.AircraftId,
                TargetAircraftId = targetComponentId,
                OrdnanceTypeDefinitionId = ordnanceTypeDefinitionId,
                ReleasedAt = releaseAt
            };
        }

        private static float CalculateReleaseHitProbability(
            OrdnanceTypeDefinition ordnance,
            float distanceKm,
            float maximumRangeKm,
            float shooterSensorQuality,
            AircraftTypeDefinition targetType,
            float launchQuality)
        {
            var maximumRange = Math.Max(
                ordnance.MinimumRangeKm + 0.01f,
                maximumRangeKm);
            var rangeRatio = Mathf.Clamp01(distanceKm / maximumRange);
            var probability = ordnance.HitProbability * (1f - 0.25f * rangeRatio);
            if (IsRadarGuided(ordnance.GuidanceMode))
            {
                probability *= 0.75f + 0.25f * Mathf.Clamp01(shooterSensorQuality);
                probability *= 1f - 0.35f * targetType.RadarDefense;
            }
            else if (ordnance.GuidanceMode == OrdnanceGuidanceMode.Infrared)
            {
                probability *= 1f - 0.2f * targetType.InfraredDefense;
            }
            return Mathf.Clamp01(probability * launchQuality);
        }

        private static float CalculateSamReleaseHitProbability(
            float launchQuality)
        {
            return Mathf.Clamp01(launchQuality);
        }

        private static PendingOrdnanceEffect CreatePendingOrdnanceEffect(
            AuthorizedOrdnanceRelease release)
        {
            var travelSeconds = release.ResolveImmediately
                ? 0f
                : AirspaceGeometry.HorizontalTravelSeconds(
                    release.ReleaseRangeKm * AirspaceGeometry.FeetPerKilometer,
                    release.Ordnance.EffectSpeedKnots);
            var isActiveRadar = release.Ordnance.GuidanceMode ==
                                OrdnanceGuidanceMode.ActiveRadar;
            var isSemiActiveRadar = release.Ordnance.GuidanceMode ==
                                    OrdnanceGuidanceMode.SemiActiveRadar;
            var supportRequired = isSemiActiveRadar
                                  || release.Ordnance.RequiresSupportUntilAutonomous;
            var autonomousAt = isActiveRadar
                ? release.ReleasedAt.AddSeconds(release.Ordnance.SecondsUntilAutonomous)
                : isSemiActiveRadar
                    ? release.ReleasedAt.AddSeconds(travelSeconds)
                    : release.ReleasedAt;
            var hitProbability = release.HitProbabilityOverride >= 0f
                ? Mathf.Clamp01(release.HitProbabilityOverride)
                : release.SourceKind ==
                                  OrdnanceEmploymentSourceKind.SamLauncher
                                  && IsRadarGuided(release.Ordnance.GuidanceMode)
                ? CalculateSamReleaseHitProbability(
                    release.LaunchQuality)
                : CalculateReleaseHitProbability(
                    release.Ordnance,
                    release.ReleaseRangeKm,
                    release.MaximumRangeKm,
                    release.ShooterSensorQuality,
                    release.Target.AircraftType,
                    release.LaunchQuality);

            return new PendingOrdnanceEffect
            {
                EmploymentPassId = release.EmploymentPassId,
                SourceKind = release.SourceKind,
                SourceFlightId = release.SourceFlightId,
                SourceAircraftId = release.SourceAircraftId,
                SourceSiteId = release.SourceSiteId,
                SourceComponentId = release.SourceComponentId,
                TargetFlightId = release.Target.Flight.FlightId,
                OrdnanceTypeDefinitionId = release.Ordnance.OrdnanceTypeDefinitionId,
                Quantity = release.Quantity,
                HitProbability = hitProbability,
                ReleasedAt = release.ReleasedAt,
                ResolveAt = release.ReleasedAt.AddSeconds(travelSeconds),
                ReleaseRangeKm = release.ReleaseRangeKm,
                MaximumRangeKmAtRelease = release.MaximumRangeKm,
                SourcePositionFeet = release.SourcePositionFeet,
                TargetPositionFeet = release.Target.Flight.PositionFeet,
                Launches = release.Launches,
                GuidanceStage = autonomousAt <= release.ReleasedAt
                    ? OrdnanceGuidanceStage.Autonomous
                    : OrdnanceGuidanceStage.Midcourse,
                AutonomousAt = autonomousAt,
                SupportRequired = supportRequired,
                SupportSourceFlightId = release.SupportSourceFlightId,
                SupportSourceSiteId = release.SupportSourceSiteId,
                SupportSourceComponentId = release.SupportSourceComponentId,
                LastGuidanceUpdateAt = release.ReleasedAt,
                LaunchQuality = release.LaunchQuality,
                PrincipalThreatBearingDegrees = AirCombatRules.HeadingTo(
                    release.Target.Flight.PositionFeet,
                    release.SourcePositionFeet)
            };
        }

        private void ResolveEffectBatch(
            IReadOnlyCollection<PendingOrdnanceEffect> effects,
            DateTime resolveAt)
        {
            ResolveGroundEffects(
                effects.Where(effect => effect.TargetKind
                                        != OrdnanceEmploymentTargetKind
                                            .AirFlight)
                    .ToList(),
                resolveAt);
            var airEffects = effects
                .Where(effect => effect.TargetKind
                                 == OrdnanceEmploymentTargetKind.AirFlight)
                .ToList();
            var losses = new HashSet<Guid>();
            var damages = new HashSet<Guid>();
            var contexts = BuildFlightContexts();
            foreach (var targetGroup in airEffects
                         .GroupBy(effect => effect.TargetFlightId)
                         .OrderBy(group => group.Key))
            {
                var batchTargets = contexts.TryGetValue(targetGroup.Key, out var target)
                    ? target.LiveAircraft
                        .OrderBy(aircraft => aircraft.AircraftId)
                        .ToList()
                    : new List<CampaignAircraft>();

                foreach (var effect in targetGroup.OrderBy(item => item.PendingEffectId))
                {
                    if (!effect.IsDefeated
                        && target != null
                        && ordnanceTypes.TryGetValue(
                            effect.OrdnanceTypeDefinitionId,
                            out var resolvingOrdnance))
                    {
                        effect.HitProbability = CalculateTerminalHitProbability(
                            effect,
                            resolvingOrdnance,
                            target.AircraftType);
                    }
                    effect.GuidanceStage = OrdnanceGuidanceStage.Resolved;
                    var hits = 0;
                    var damaged = 0;
                    var misses = 0;
                    var ineffective = 0;
                    var defeated = 0;
                    var shotDiagnostics = new List<OrdnanceShotDiagnostic>();
                    for (var missileIndex = 0; missileIndex < effect.Quantity; missileIndex++)
                    {
                        if (effect.IsDefeated)
                        {
                            defeated++;
                            shotDiagnostics.Add(new OrdnanceShotDiagnostic
                            {
                                Sequence = missileIndex + 1,
                                SourceAircraftId = GetLaunchSourceAircraftId(
                                    effect,
                                    missileIndex),
                                TargetAircraftId = GetLaunchTargetAircraftId(
                                    effect,
                                    missileIndex),
                                Probability = 0f,
                                Roll = -1f,
                                Result = OrdnanceShotResult.Defeated,
                                DefeatReason = effect.DefeatReason
                            });
                            continue;
                        }

                        if (batchTargets.Count == 0)
                        {
                            ineffective++;
                            shotDiagnostics.Add(new OrdnanceShotDiagnostic
                            {
                                Sequence = missileIndex + 1,
                                SourceAircraftId = GetLaunchSourceAircraftId(
                                    effect,
                                    missileIndex),
                                TargetAircraftId = GetLaunchTargetAircraftId(
                                    effect,
                                    missileIndex),
                                Probability = effect.HitProbability,
                                Roll = -1f,
                                Result = OrdnanceShotResult.Ineffective
                            });
                            continue;
                        }

                        var launch = GetLaunch(effect, missileIndex);
                        CampaignAircraft selectedAircraft;
                        if (launch != null && launch.TargetAircraftId != Guid.Empty)
                        {
                            selectedAircraft = batchTargets.FirstOrDefault(
                                aircraft => aircraft.AircraftId == launch.TargetAircraftId);
                            if (selectedAircraft == null)
                            {
                                ineffective++;
                                shotDiagnostics.Add(new OrdnanceShotDiagnostic
                                {
                                    Sequence = missileIndex + 1,
                                    SourceAircraftId = launch.SourceAircraftId,
                                    TargetAircraftId = launch.TargetAircraftId,
                                    Probability = effect.HitProbability,
                                    Roll = -1f,
                                    Result = OrdnanceShotResult.Ineffective
                                });
                                continue;
                            }
                        }
                        else
                        {
                            var targetIndex = StableIndex(
                                effect.PendingEffectId,
                                missileIndex,
                                batchTargets.Count);
                            selectedAircraft = batchTargets[targetIndex];
                        }
                        var roll = (float)StableRoll(effect.PendingEffectId, missileIndex);
                        var result = roll < effect.HitProbability
                            ? OrdnanceShotResult.Hit
                            : OrdnanceShotResult.Miss;
                        var targetWasAlreadyDamaged =
                            selectedAircraft.Status
                            == CampaignAircraftStatus.Damaged
                            || damages.Contains(selectedAircraft.AircraftId);
                        var destructionProbability = -1f;
                        var destructionRoll = -1f;
                        if (result == OrdnanceShotResult.Hit
                            && target != null
                            && ordnanceTypes.TryGetValue(
                                effect.OrdnanceTypeDefinitionId,
                                out var impactOrdnance))
                        {
                            if (targetWasAlreadyDamaged)
                            {
                                destructionProbability = 1f;
                            }
                            else
                            {
                                destructionProbability = Mathf.Clamp01(
                                    impactOrdnance.TerminalLethality
                                    * (1f - 0.55f * Mathf.Clamp01(
                                        target.AircraftType.Survivability)));
                                destructionRoll = (float)StableRoll(
                                    effect.PendingEffectId,
                                    missileIndex + 10000);
                                if (destructionRoll >= destructionProbability)
                                    result = OrdnanceShotResult.Damaged;
                            }
                        }
                        shotDiagnostics.Add(new OrdnanceShotDiagnostic
                        {
                            Sequence = missileIndex + 1,
                            SourceAircraftId = launch?.SourceAircraftId ?? Guid.Empty,
                            TargetAircraftId = selectedAircraft.AircraftId,
                            Probability = effect.HitProbability,
                            Roll = roll,
                            TargetWasAlreadyDamaged = targetWasAlreadyDamaged,
                            DestructionProbability = destructionProbability,
                            DestructionRoll = destructionRoll,
                            Result = result
                        });
                        if (result == OrdnanceShotResult.Hit)
                        {
                            hits++;
                            losses.Add(selectedAircraft.AircraftId);
                        }
                        else if (result == OrdnanceShotResult.Damaged)
                        {
                            hits++;
                            damaged++;
                            damages.Add(selectedAircraft.AircraftId);
                        }
                        else
                        {
                            misses++;
                        }
                    }

                    AddRecord(
                        effect,
                        OrdnanceEmploymentRecordStage.EffectResolved,
                        resolveAt,
                        effect.Quantity,
                        $"Effect resolved: {hits} hit ({damaged} damaged), "
                        + $"{misses} missed, {defeated} defeated, "
                        + $"{ineffective} became ineffective.",
                        shotDiagnostics);
                    PendingEffects.Remove(effect);
                }
            }

            foreach (var aircraft in gameManager.squadronSystem.Squadrons
                         .SelectMany(squadron => squadron.Aircraft)
                         .Where(aircraft => losses.Contains(aircraft.AircraftId)))
            {
                aircraft.Status = CampaignAircraftStatus.Lost;
                aircraft.ClearLoadout();
            }

            foreach (var aircraft in gameManager.squadronSystem.Squadrons
                         .SelectMany(squadron => squadron.Aircraft)
                         .Where(aircraft => damages.Contains(aircraft.AircraftId)
                                            && !losses.Contains(aircraft.AircraftId)))
            {
                aircraft.Status = CampaignAircraftStatus.Damaged;
            }

            foreach (var targetFlightId in airEffects
                         .Select(effect => effect.TargetFlightId)
                         .Distinct())
            {
                if (!contexts.TryGetValue(targetFlightId, out var target)
                    || target.LiveAircraft.Any(aircraft =>
                        !losses.Contains(aircraft.AircraftId)))
                    continue;

                target.Flight.Fail(resolveAt, "All aircraft were lost to ordnance effects.");
            }

        }

        private void ResolveGroundEffects(
            IReadOnlyCollection<PendingOrdnanceEffect> effects,
            DateTime resolveAt)
        {
            var plannedEffects = effects
                .Where(effect => effect.GroundPrimaryTarget != null)
                .ToList();
            ResolvePlannedGroundEffects(plannedEffects, resolveAt);

            foreach (var effect in effects
                         .Where(item => item.GroundPrimaryTarget == null)
                         .OrderBy(item => item.PendingEffectId))
            {
                effect.GuidanceStage = OrdnanceGuidanceStage.Resolved;
                var result = OrdnanceShotResult.Ineffective;
                var roll = -1f;
                var guidanceQuality = 1f;
                if (gameManager.airDefenseSiteSystem.TryGetSite(
                        effect.TargetSiteId,
                        out var site)
                    && !site.IsDisabled
                    && !site.IsDestroyed)
                {
                    var component = site.Components.FirstOrDefault(candidate =>
                        candidate != null
                        && candidate.ComponentId == effect.TargetComponentId);
                    if (component != null && !component.IsDamaged)
                    {
                        if (ordnanceTypes.TryGetValue(
                                effect.OrdnanceTypeDefinitionId,
                                out var ordnance)
                            && IsAntiRadiation(ordnance))
                        {
                            if (component is RadarAirDefenseComponent
                                {
                                    IsEmitting: true
                                })
                            {
                                effect.LastTargetEmissionAt = resolveAt;
                            }
                            guidanceQuality =
                                CalculateAntiRadiationGuidanceQuality(
                                    effect,
                                    ordnance,
                                    resolveAt);
                            effect.HitProbability = Mathf.Clamp01(
                                effect.HitProbability * guidanceQuality);
                        }
                        roll = (float)StableRoll(effect.PendingEffectId, 0);
                        result = roll < effect.HitProbability
                            ? OrdnanceShotResult.Hit
                            : OrdnanceShotResult.Miss;
                        if (result == OrdnanceShotResult.Hit)
                        {
                            gameManager.airDefenseSiteSystem.DamageComponent(
                                effect.TargetSiteId,
                                effect.TargetComponentId);
                        }
                    }
                }

                var shot = new OrdnanceShotDiagnostic
                {
                    Sequence = 1,
                    SourceAircraftId = effect.SourceAircraftId,
                    TargetAircraftId = effect.TargetComponentId,
                    Probability = effect.HitProbability,
                    Roll = roll,
                    Result = result
                };
                AddRecord(
                    effect,
                    OrdnanceEmploymentRecordStage.EffectResolved,
                    resolveAt,
                    effect.Quantity,
                    result == OrdnanceShotResult.Hit
                        ? $"SAM component {ShortId(effect.TargetComponentId)} was destroyed."
                        : result == OrdnanceShotResult.Miss
                            ? $"Attack on SAM component {ShortId(effect.TargetComponentId)} missed"
                              + (guidanceQuality < 0.999f
                                  ? $" after emitter guidance decayed to {guidanceQuality:P0}."
                                  : ".")
                            : $"Attack on SAM component {ShortId(effect.TargetComponentId)} became ineffective.",
                    new List<OrdnanceShotDiagnostic> { shot });
                PendingEffects.Remove(effect);
            }
        }

        private void ResolvePlannedGroundEffects(
            IReadOnlyCollection<PendingOrdnanceEffect> effects,
            DateTime resolveAt)
        {
            var applications = new List<GroundHitApplication>();
            foreach (var effect in effects.OrderBy(item => item.PendingEffectId))
            {
                effect.GuidanceStage = OrdnanceGuidanceStage.Resolved;
                var shots = new List<OrdnanceShotDiagnostic>();
                var hits = 0;
                var misses = 0;
                var ineffective = 0;
                if (!ordnanceTypes.TryGetValue(
                        effect.OrdnanceTypeDefinitionId,
                        out var ordnance)
                    || !TryResolveGroundTarget(
                        effect.GroundPrimaryTarget,
                        out var primary))
                {
                    ineffective++;
                    shots.Add(CreateGroundShot(
                        effect,
                        effect.GroundPrimaryTarget,
                        0,
                        0f,
                        -1f,
                        OrdnanceShotResult.Ineffective));
                }
                else
                {
                    var directProbability = effect.HitProbability;
                    if (IsAntiRadiation(ordnance))
                    {
                        if (primary.Component is RadarAirDefenseComponent
                            {
                                IsEmitting: true
                            })
                        {
                            effect.LastTargetEmissionAt = resolveAt;
                        }
                        directProbability = Mathf.Clamp01(
                            directProbability
                            * CalculateAntiRadiationGuidanceQuality(
                                effect,
                                ordnance,
                                resolveAt));
                    }

                    var directRoll = (float)StableRoll(
                        effect.PendingEffectId,
                        0);
                    var directResult = directRoll < directProbability
                        ? OrdnanceShotResult.Hit
                        : OrdnanceShotResult.Miss;
                    shots.Add(CreateGroundShot(
                        effect,
                        effect.GroundPrimaryTarget,
                        0,
                        directProbability,
                        directRoll,
                        directResult));
                    if (directResult == OrdnanceShotResult.Hit)
                    {
                        hits++;
                        applications.Add(new GroundHitApplication(
                            effect.GroundPrimaryTarget,
                            ordnance,
                            1f));

                        var primaryEffectiveness = Math.Max(
                            0.01f,
                            ordnance.GetEffectiveness(
                                effect.GroundPrimaryTarget.TargetCategory));
                        var deliveryProbability = Mathf.Clamp01(
                            effect.HitProbability / primaryEffectiveness);
                        var secondaryTargets = SelectSecondaryTargets(
                            effect,
                            ordnance);
                        for (var index = 0;
                             index < secondaryTargets.Count;
                             index++)
                        {
                            var target = secondaryTargets[index];
                            var secondaryProbability = Mathf.Clamp01(
                                deliveryProbability
                                * ordnance.GetEffectiveness(
                                    target.TargetCategory)
                                * ordnance.SecondaryGroundEffectMultiplier);
                            var secondaryRoll = (float)StableRoll(
                                effect.PendingEffectId,
                                index + 1);
                            var result = secondaryRoll < secondaryProbability
                                ? OrdnanceShotResult.Hit
                                : OrdnanceShotResult.Miss;
                            shots.Add(CreateGroundShot(
                                effect,
                                target,
                                index + 1,
                                secondaryProbability,
                                secondaryRoll,
                                result));
                            if (result == OrdnanceShotResult.Hit)
                            {
                                hits++;
                                applications.Add(new GroundHitApplication(
                                    target,
                                    ordnance,
                                    ordnance.SecondaryGroundEffectMultiplier));
                            }
                            else
                            {
                                misses++;
                            }
                        }
                    }
                    else
                    {
                        misses++;
                    }
                }

                AddRecord(
                    effect,
                    OrdnanceEmploymentRecordStage.EffectResolved,
                    resolveAt,
                    1,
                    $"Ground effect resolved: {hits} hit, {misses} missed, "
                    + $"{ineffective} became ineffective.",
                    shots);
                PendingEffects.Remove(effect);
            }

            foreach (var application in applications)
                ApplyGroundHit(application);
        }

        private List<GroundAttackOpportunityTarget> SelectSecondaryTargets(
            PendingOrdnanceEffect effect,
            OrdnanceTypeDefinition ordnance)
        {
            var capacity = Math.Max(
                0,
                ordnance.MaximumGroundTargetsPerWeapon - 1);
            if (capacity == 0
                || ordnance.SecondaryGroundEffectMultiplier <= 0f)
                return new List<GroundAttackOpportunityTarget>();

            var candidates = new List<(GroundAttackOpportunityTarget Target,
                int Index)>();
            var skippedPrimary = false;
            var opportunityTargets = effect.GroundOpportunityTargets
                                     ?? new List<
                                         GroundAttackOpportunityTarget>();
            for (var index = 0; index < opportunityTargets.Count; index++)
            {
                var target = opportunityTargets[index];
                if (target?.Target == null)
                    continue;
                if (!skippedPrimary
                    && SameOpportunityTarget(target, effect.GroundPrimaryTarget))
                {
                    skippedPrimary = true;
                    continue;
                }
                candidates.Add((target, index));
            }

            return candidates
                .Where(candidate =>
                    TryResolveGroundTarget(
                        candidate.Target,
                        out var resolved)
                    && resolved.TileId == candidate.Target.Target.TileId
                    && AirToGroundWeaponRules.CanAffect(
                        ordnance,
                        candidate.Target.TargetCategory,
                        candidate.Target.TargetToughness,
                        ordnance.SecondaryGroundEffectMultiplier))
                .OrderBy(candidate => StableRoll(
                    effect.PendingEffectId,
                    candidate.Index + 1000))
                .ThenBy(candidate => candidate.Index)
                .Take(capacity)
                .Select(candidate => candidate.Target.Clone())
                .ToList();
        }

        private static bool SameOpportunityTarget(
            GroundAttackOpportunityTarget first,
            GroundAttackOpportunityTarget second)
        {
            return first != null
                   && second != null
                   && first.Target?.Matches(second.Target) == true
                   && first.TargetCategory == second.TargetCategory
                   && first.TargetToughness == second.TargetToughness
                   && string.Equals(
                       first.Description,
                       second.Description,
                       StringComparison.Ordinal);
        }

        private static OrdnanceShotDiagnostic CreateGroundShot(
            PendingOrdnanceEffect effect,
            GroundAttackOpportunityTarget target,
            int sequence,
            float probability,
            float roll,
            OrdnanceShotResult result)
        {
            return new OrdnanceShotDiagnostic
            {
                Sequence = sequence + 1,
                SourceAircraftId = effect.SourceAircraftId,
                TargetAircraftId = target?.Target?.EntityId ?? Guid.Empty,
                GroundTarget = target?.Target?.Clone(),
                Probability = probability,
                Roll = roll,
                Result = result
            };
        }

        internal static float CalculateTerminalHitProbability(
            PendingOrdnanceEffect effect,
            OrdnanceTypeDefinition ordnance,
            AircraftTypeDefinition targetType)
        {
            var totalSeconds = Math.Max(
                1d,
                (effect.ResolveAt - effect.ReleasedAt).TotalSeconds);
            var supportRatio = effect.SupportRequired
                ? Mathf.Clamp01(effect.SupportedSeconds / Math.Max(1f, effect.SupportSeconds))
                : 1f;
            var supportMultiplier = effect.SupportRequired
                ? 0.3f + 0.7f * supportRatio
                : 1f;
            if (IsRadarGuided(ordnance.GuidanceMode))
            {
                return Mathf.Clamp01(
                    effect.HitProbability * supportMultiplier);
            }

            var defenseRatio = Mathf.Clamp01(
                effect.DefensiveSeconds / (float)totalSeconds);
            var defenseAuthority = 0.35f
                                   + 0.45f * targetType.GetDefenseAgainst(ordnance);
            var defenseMultiplier = 1f - defenseRatio * defenseAuthority;
            return Mathf.Clamp01(
                effect.HitProbability * supportMultiplier * defenseMultiplier);
        }

        private static OrdnanceLaunchDiagnostic GetLaunch(
            PendingOrdnanceEffect effect,
            int missileIndex)
        {
            return effect.Launches == null || missileIndex < 0 || missileIndex >= effect.Launches.Count
                ? null
                : effect.Launches[missileIndex];
        }

        private static Guid GetLaunchSourceAircraftId(
            PendingOrdnanceEffect effect,
            int missileIndex)
        {
            return GetLaunch(effect, missileIndex)?.SourceAircraftId ?? Guid.Empty;
        }

        private static Guid GetLaunchTargetAircraftId(
            PendingOrdnanceEffect effect,
            int missileIndex)
        {
            return GetLaunch(effect, missileIndex)?.TargetAircraftId ?? Guid.Empty;
        }

        private void RefreshSamEngagementAssignments(
            IReadOnlyDictionary<Guid, FlightContext> contexts,
            DateTime currentTime)
        {
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var iads = iadsSystem.GetAllianceIADS(alliance);
                if (iads == null)
                    continue;

                var tracks = iads.CurrentTracks
                    .Where(track => track != null
                                    && !track.IsStale
                                    && contexts.ContainsKey(track.FlightId))
                    .ToList();
                var reservedTargetFlightIds = PendingEffects
                    .Where(effect => IsPendingSamEffectForAlliance(
                        effect,
                        alliance,
                        currentTime))
                    .Select(effect => effect.TargetFlightId)
                    .ToHashSet();
                var candidates = new List<SamEngagementCandidate>();
                foreach (var site in gameManager.airDefenseSiteSystem.Sites
                             .Where(site => site != null
                                            && gameManager.airDefenseSiteSystem
                                                .GetEffectiveAlliance(site) == alliance))
                {
                    if (!TryGetSamSitePosition(site, out var sitePosition))
                        continue;

                    foreach (var track in tracks.Where(track =>
                                 !reservedTargetFlightIds.Contains(track.FlightId)))
                    {
                        if (!CanAnyLauncherEngage(
                                site,
                                sitePosition,
                                contexts[track.FlightId],
                                track,
                                currentTime,
                                out var fireControlRadarComponentId))
                            continue;

                        candidates.Add(new SamEngagementCandidate(
                            site,
                            track,
                            fireControlRadarComponentId,
                            Vector3.Distance(
                                sitePosition,
                                contexts[track.FlightId].Flight.PositionFeet)));
                    }
                }

                var assignments = new List<IADSEngagementAssignment>();
                var assignedSiteIds = new HashSet<Guid>();
                foreach (var candidate in candidates
                             .OrderBy(item => item.DistanceFeet)
                             .ThenBy(item => item.Site.SiteId)
                             .ThenBy(item => item.Track.FlightId))
                {
                    if (assignedSiteIds.Contains(candidate.Site.SiteId)
                        || reservedTargetFlightIds.Contains(
                            candidate.Track.FlightId))
                        continue;

                    assignments.Add(new IADSEngagementAssignment
                    {
                        SiteId = candidate.Site.SiteId,
                        TrackId = candidate.Track.TrackId,
                        TargetFlightId = candidate.Track.FlightId,
                        FireControlRadarComponentId =
                            candidate.FireControlRadarComponentId,
                        AssignedAt = currentTime
                    });
                    assignedSiteIds.Add(candidate.Site.SiteId);
                    reservedTargetFlightIds.Add(candidate.Track.FlightId);
                }
                iads.ReplaceEngagementAssignments(assignments);
            }
        }

        private void LaunchAssignedSamShots(
            IReadOnlyDictionary<Guid, FlightContext> contexts,
            DateTime currentTime)
        {
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var iads = iadsSystem.GetAllianceIADS(alliance);
                if (iads == null)
                    continue;

                foreach (var assignment in iads.CurrentEngagementAssignments
                             .OrderBy(item => item.SiteId))
                {
                    if (!gameManager.airDefenseSiteSystem.TryGetSite(
                            assignment.SiteId,
                            out var site)
                        || !contexts.TryGetValue(
                            assignment.TargetFlightId,
                            out var target)
                        || !TryGetSamSitePosition(site, out var sitePosition))
                        continue;

                    var track = iads.CurrentTracks.FirstOrDefault(candidate =>
                        candidate.TrackId == assignment.TrackId);
                    if (track == null
                        || !TryGetWeaponQualityRadar(
                            site,
                            target,
                            track,
                            assignment.FireControlRadarComponentId,
                            true,
                            currentTime,
                            out var supportRadar,
                            out var supportRadarDefinition,
                            out var weaponQualityForShot))
                        continue;

                    var remainingSupportSlots =
                        supportRadarDefinition.MaximumSupportedMissiles
                        - CountPendingSupportedMissiles(
                            site.SiteId,
                            supportRadar.ComponentId,
                            currentTime);
                    if (remainingSupportSlots <= 0)
                        continue;

                    var launchesByDefinitionId = new Dictionary<Guid, int>();
                    foreach (var launcher in gameManager.airDefenseSiteSystem
                                 .GetAvailableComponents(site)
                                 .OfType<LauncherAirDefenseComponent>()
                                 .OrderBy(component => component.ComponentId))
                    {
                        if (remainingSupportSlots <= 0)
                            break;
                        if (!TryGetLauncherEmployment(
                                launcher,
                                sitePosition,
                                target,
                                out var launcherDefinition,
                                out var ordnance,
                                out var distanceFeet,
                                out var maximumRangeKm))
                            continue;
                        if (weaponQualityForShot
                            < launcherDefinition.MinimumTrackQualityToFire)
                            continue;
                        launchesByDefinitionId.TryGetValue(
                            launcherDefinition.SamComponentDefinitionId,
                            out var launchesForDefinition);
                        if (launchesForDefinition
                            >= launcherDefinition.PreferredEngagementSalvoSize)
                            continue;
                        if (!launcher.TrySpendRound(launcherDefinition, currentTime))
                            continue;

                        var targetAircraftId = SelectTargetAircraftId(
                            target,
                            launcher.ComponentId,
                            1);
                        var launches = new List<OrdnanceLaunchDiagnostic>
                        {
                            new OrdnanceLaunchDiagnostic
                            {
                                Sequence = 1,
                                TargetAircraftId = targetAircraftId,
                                OrdnanceTypeDefinitionId =
                                    ordnance.OrdnanceTypeDefinitionId,
                                ReleasedAt = currentTime
                            }
                        };
                        var pending = CreatePendingOrdnanceEffect(
                            new AuthorizedOrdnanceRelease
                            {
                                EmploymentPassId = Guid.NewGuid(),
                                SourceKind = OrdnanceEmploymentSourceKind.SamLauncher,
                                SourceSiteId = site.SiteId,
                                SourceComponentId = launcher.ComponentId,
                                Target = target,
                                Ordnance = ordnance,
                                Quantity = 1,
                                ReleasedAt = currentTime,
                                SourcePositionFeet = sitePosition,
                                Launches = launches,
                                SupportSourceSiteId = site.SiteId,
                                SupportSourceComponentId = supportRadar.ComponentId,
                                MaximumRangeKm = maximumRangeKm,
                                ShooterSensorQuality = supportRadarDefinition.TrackQuality,
                                LaunchQuality = weaponQualityForShot,
                                ReleaseRangeKm = distanceFeet
                                                 / AirspaceGeometry.FeetPerKilometer
                            });
                        PendingEffects.Add(pending);
                        launchesByDefinitionId[
                                launcherDefinition.SamComponentDefinitionId] =
                            launchesForDefinition + 1;
                        remainingSupportSlots--;
                        AddRecord(
                            pending,
                            OrdnanceEmploymentRecordStage.OrdnanceReleased,
                            currentTime,
                            1,
                            $"SAM site {ShortId(site.SiteId)} launched " +
                            $"{ordnance.Name} at {target.DisplayName}.");
                    }
                }
            }
        }

        private void RefreshRadarEmissionPostures(DateTime currentTime)
        {
            var assignments = new[] { Alliance.Bluefor, Alliance.Redfor }
                .Select(alliance => iadsSystem.GetAllianceIADS(alliance))
                .Where(iads => iads != null)
                .SelectMany(iads => iads.CurrentEngagementAssignments)
                .ToList();

            foreach (var site in gameManager.airDefenseSiteSystem.Sites
                         .Where(candidate => candidate != null))
            {
                var availableRadarIds = gameManager.airDefenseSiteSystem
                    .GetAvailableComponents(site)
                    .OfType<RadarAirDefenseComponent>()
                    .Select(radar => radar.ComponentId)
                    .ToHashSet();

                foreach (var radar in site.Components
                             .OfType<RadarAirDefenseComponent>())
                {
                    if (!availableRadarIds.Contains(radar.ComponentId)
                        || radar.IsDamaged
                        || !airDefenseComponentDefinitions.TryGetValue(
                            radar.SamComponentDefinitionId,
                            out var componentDefinition)
                        || componentDefinition
                        is not RadarAirDefenseComponentDefinition definition)
                    {
                        radar.UpdateEmission(false, currentTime);
                        continue;
                    }

                    var isAssigned = assignments.Any(assignment =>
                        assignment.SiteId == site.SiteId
                        && assignment.FireControlRadarComponentId
                        == radar.ComponentId);
                    var isSupporting = GetPendingSupportedSamEffects(
                            site.SiteId,
                            radar.ComponentId,
                            currentTime)
                        .Any();
                    var inboundAntiRadiationEffects = PendingEffects
                        .Where(effect => effect != null
                                         && !effect.IsDefeated
                                         && effect.TargetKind
                                         == OrdnanceEmploymentTargetKind
                                             .AirDefenseComponent
                                         && effect.TargetSiteId == site.SiteId
                                         && effect.TargetComponentId
                                         == radar.ComponentId
                                         && effect.ResolveAt > currentTime
                                         && ordnanceTypes.TryGetValue(
                                             effect.OrdnanceTypeDefinitionId,
                                             out var ordnance)
                                         && IsAntiRadiation(ordnance))
                        .ToList();
                    if (!isSupporting && inboundAntiRadiationEffects.Count > 0)
                    {
                        radar.HoldEmissionUntil(
                            inboundAntiRadiationEffects.Max(effect =>
                                effect.ResolveAt));
                    }

                    radar.UpdateEmission(
                        definition.SearchesWhileUnassigned
                        || isAssigned
                        || isSupporting,
                        currentTime);
                }
            }
        }

        private static Guid SelectTargetAircraftId(
            FlightContext target,
            Guid seed,
            int sequence)
        {
            var candidates = target.LiveAircraft
                .OrderBy(aircraft => aircraft.AircraftId)
                .ToList();
            if (candidates.Count == 0)
                return Guid.Empty;
            return candidates[StableIndex(seed, sequence, candidates.Count)].AircraftId;
        }

        private bool CanAnyLauncherEngage(
            SamSite site,
            Vector3 sitePosition,
            FlightContext target,
            IADSTrack track,
            DateTime currentTime,
            out Guid fireControlRadarComponentId)
        {
            fireControlRadarComponentId = Guid.Empty;
            if (!TryGetWeaponQualityRadar(
                    site,
                    target,
                    track,
                    Guid.Empty,
                    false,
                    currentTime,
                    out var supportRadar,
                    out _,
                    out var weaponQualityForShot))
                return false;

            var canEngage = gameManager.airDefenseSiteSystem
                .GetAvailableComponents(site)
                .OfType<LauncherAirDefenseComponent>()
                .Any(launcher =>
                    TryGetLauncherEmployment(
                        launcher,
                        sitePosition,
                        target,
                        out var launcherDefinition,
                        out _,
                        out _,
                        out _)
                    && weaponQualityForShot
                    >= launcherDefinition.MinimumTrackQualityToFire);
            if (canEngage)
                fireControlRadarComponentId = supportRadar.ComponentId;
            return canEngage;
        }

        private bool TryGetLauncherEmployment(
            LauncherAirDefenseComponent launcher,
            Vector3 sitePosition,
            FlightContext target,
            out LauncherAirDefenseComponentDefinition launcherDefinition,
            out OrdnanceTypeDefinition ordnance,
            out float distanceFeet,
            out float maximumRangeKm)
        {
            launcherDefinition = null;
            ordnance = null;
            distanceFeet = 0f;
            maximumRangeKm = 0f;
            if (launcher == null
                || launcher.IsDamaged
                || !airDefenseComponentDefinitions.TryGetValue(
                    launcher.SamComponentDefinitionId,
                    out var componentDefinition)
                || componentDefinition is not LauncherAirDefenseComponentDefinition definition
                || definition.SurfaceToAirOrdnanceTypeDefinitionId == Guid.Empty
                || !ordnanceTypes.TryGetValue(
                    definition.SurfaceToAirOrdnanceTypeDefinitionId,
                    out var interceptor)
                || interceptor.EmploymentCategory !=
                OrdnanceEmploymentCategory.SurfaceToAir)
                return false;

            launcher.ReloadIfReady(definition, gameManager.CurrentTime);
            if (launcher.ReadyRounds <= 0)
                return false;

            distanceFeet = Vector3.Distance(sitePosition, target.Flight.PositionFeet);
            var distanceKm = distanceFeet / AirspaceGeometry.FeetPerKilometer;
            var altitudeFeet = target.Flight.PositionFeet.y;
            var minimumRange = Math.Max(
                definition.MinEngagementRangeKm,
                interceptor.MinimumRangeKm);
            maximumRangeKm = Math.Min(
                definition.MaxEngagementRangeKm,
                interceptor.MaximumRangeKm);
            var minimumAltitude = Math.Max(
                definition.MinEngagementAltitudeMeters
                * AirspaceGeometry.FeetPerKilometer / 1000f,
                interceptor.MinimumTargetAltitudeFeet);
            var maximumAltitude = Math.Min(
                definition.MaxEngagementAltitudeMeters
                * AirspaceGeometry.FeetPerKilometer / 1000f,
                interceptor.MaximumTargetAltitudeFeet);
            if (distanceKm < minimumRange
                || distanceKm > maximumRangeKm
                || altitudeFeet < minimumAltitude
                || altitudeFeet > maximumAltitude)
                return false;

            launcherDefinition = definition;
            ordnance = interceptor;
            return true;
        }

        private bool TryGetWeaponQualityRadar(
            SamSite site,
            FlightContext target,
            IADSTrack track,
            Guid preferredRadarComponentId,
            bool requireEmission,
            DateTime currentTime,
            out RadarAirDefenseComponent radar,
            out RadarAirDefenseComponentDefinition radarDefinition,
            out float weaponQualityForShot)
        {
            if (track == null
                || track.IsStale
                || !TryGetSamSitePosition(site, out var sitePosition))
            {
                radar = null;
                radarDefinition = null;
                weaponQualityForShot = 0f;
                return false;
            }

            var candidate = gameManager.airDefenseSiteSystem
                .GetAvailableComponents(site)
                .OfType<RadarAirDefenseComponent>()
                .Where(component => !component.IsDamaged
                                    && component.CanEmitAt(currentTime)
                                    && (!requireEmission || component.IsEmitting)
                                    && (preferredRadarComponentId == Guid.Empty
                                        || component.ComponentId
                                        == preferredRadarComponentId)
                                    && airDefenseComponentDefinitions.TryGetValue(
                                        component.SamComponentDefinitionId,
                                        out var definition)
                                    && definition is RadarAirDefenseComponentDefinition
                                    {
                                        ProvidesWeaponQualityTrack: true
                                    })
                .Select(component => new
                {
                    Component = component,
                    Definition = (RadarAirDefenseComponentDefinition)
                        airDefenseComponentDefinitions[component.SamComponentDefinitionId],
                    Geometry = RadarDetectionGeometryCalculator.Calculate(
                        (RadarAirDefenseComponentDefinition)
                        airDefenseComponentDefinitions[component.SamComponentDefinitionId],
                        sitePosition,
                        target.Flight.PositionFeet,
                        target.AircraftType.RadarDetectability)
                })
                .Where(item => IsTargetInsideRadarEnvelope(
                    item.Definition,
                    target.Flight.PositionFeet,
                    item.Geometry))
                .Select(item => new
                {
                    item.Component,
                    item.Definition,
                    LocalQualityCap = item.Definition.CalculateTrackQualityCap(
                        item.Geometry.RangeFactor)
                })
                .Where(item => item.LocalQualityCap > 0f)
                .Where(item => CanRadarAcceptNewSalvo(
                    site.SiteId,
                    item.Component.ComponentId,
                    item.Definition,
                    target.Flight.FlightId,
                    currentTime))
                .OrderByDescending(item => item.LocalQualityCap)
                .ThenBy(item => item.Component.ComponentId)
                .FirstOrDefault();
            radar = candidate?.Component;
            radarDefinition = candidate?.Definition;
            weaponQualityForShot = candidate == null
                ? 0f
                : track.Quality;
            return radar != null
                   && radarDefinition != null
                   && weaponQualityForShot > 0f;
        }

        private bool TryGetTargetRadar(
            PendingOrdnanceEffect effect,
            out RadarAirDefenseComponent radar)
        {
            radar = null;
            if (effect == null
                || effect.TargetKind
                != OrdnanceEmploymentTargetKind.AirDefenseComponent
                || effect.TargetSiteId == Guid.Empty
                || effect.TargetComponentId == Guid.Empty
                || !gameManager.airDefenseSiteSystem.TryGetSite(
                    effect.TargetSiteId,
                    out var site))
                return false;

            radar = site.Components
                .OfType<RadarAirDefenseComponent>()
                .FirstOrDefault(component =>
                    component.ComponentId == effect.TargetComponentId);
            return radar != null;
        }

        internal static float CalculateAntiRadiationGuidanceQuality(
            PendingOrdnanceEffect effect,
            OrdnanceTypeDefinition ordnance,
            DateTime resolveAt)
        {
            if (effect == null || !IsAntiRadiation(ordnance))
                return 1f;

            var lastEmission = effect.LastTargetEmissionAt == default
                ? effect.ReleasedAt
                : effect.LastTargetEmissionAt;
            var silentSeconds = Math.Max(
                0d,
                (resolveAt - lastEmission).TotalSeconds);
            if (silentSeconds <= 0d)
                return 1f;

            var floor = Mathf.Clamp01(
                ordnance.AntiRadiationSilentQualityFloor);
            var memorySeconds = Math.Max(
                0f,
                ordnance.AntiRadiationEmitterMemorySeconds);
            if (memorySeconds <= 0f)
                return floor;

            var decay = Mathf.Clamp01((float)silentSeconds / memorySeconds);
            return Mathf.Lerp(1f, floor, decay);
        }

        private bool CanRadarAcceptNewSalvo(
            Guid siteId,
            Guid radarComponentId,
            RadarAirDefenseComponentDefinition definition,
            Guid targetFlightId,
            DateTime currentTime)
        {
            var pendingEffects = GetPendingSupportedSamEffects(
                    siteId,
                    radarComponentId,
                    currentTime)
                .ToList();
            if (pendingEffects.Any(effect =>
                    effect.TargetFlightId == targetFlightId))
                return false;

            var supportedMissiles = pendingEffects.Sum(effect =>
                Math.Max(0, effect.Quantity));
            if (supportedMissiles >= definition.MaximumSupportedMissiles)
                return false;

            return pendingEffects
                       .Select(effect => effect.TargetFlightId)
                       .Where(flightId => flightId != Guid.Empty)
                       .Distinct()
                       .Count()
                   < definition.MaximumConcurrentTargetEngagements;
        }

        private int CountPendingSupportedMissiles(
            Guid siteId,
            Guid radarComponentId,
            DateTime currentTime)
        {
            return GetPendingSupportedSamEffects(
                    siteId,
                    radarComponentId,
                    currentTime)
                .Sum(effect => Math.Max(0, effect.Quantity));
        }

        private IEnumerable<PendingOrdnanceEffect> GetPendingSupportedSamEffects(
            Guid siteId,
            Guid radarComponentId,
            DateTime currentTime)
        {
            return PendingEffects.Where(effect =>
                effect != null
                && !effect.IsDefeated
                && effect.SourceKind == OrdnanceEmploymentSourceKind.SamLauncher
                && effect.ResolveAt > currentTime
                && (effect.SupportSourceSiteId == siteId
                    || effect.SupportSourceSiteId == Guid.Empty
                    && effect.SourceSiteId == siteId)
                && (effect.SupportSourceComponentId == radarComponentId
                    || effect.SupportSourceComponentId == Guid.Empty));
        }

        private bool IsPendingSamEffectForAlliance(
            PendingOrdnanceEffect effect,
            Alliance alliance,
            DateTime currentTime)
        {
            if (effect == null
                || effect.IsDefeated
                || effect.SourceKind != OrdnanceEmploymentSourceKind.SamLauncher
                || effect.ResolveAt <= currentTime
                || effect.TargetFlightId == Guid.Empty)
                return false;

            var siteId = effect.SourceSiteId != Guid.Empty
                ? effect.SourceSiteId
                : effect.SupportSourceSiteId;
            return gameManager.airDefenseSiteSystem.TryGetSite(siteId, out var site)
                   && gameManager.airDefenseSiteSystem.GetEffectiveAlliance(site)
                   == alliance;
        }

        private static bool IsTargetInsideRadarEnvelope(
            RadarAirDefenseComponentDefinition definition,
            Vector3 radarPosition,
            Vector3 targetPosition,
            float targetRadarDetectability)
        {
            var geometry = RadarDetectionGeometryCalculator.Calculate(
                definition,
                radarPosition,
                targetPosition,
                targetRadarDetectability);
            return IsTargetInsideRadarEnvelope(
                definition,
                targetPosition,
                geometry);
        }

        private static bool IsTargetInsideRadarEnvelope(
            RadarAirDefenseComponentDefinition definition,
            Vector3 targetPosition,
            RadarDetectionGeometry geometry)
        {
            var maximumAltitudeFeet = definition.MaxAltitudeMeters
                                      * AirspaceGeometry.FeetPerKilometer
                                      / 1000f;
            return geometry.RangeFactor > 0f
                   && geometry.IsWithinEquipmentRange
                   && geometry.IsWithinRadarHorizon
                   && targetPosition.y <= maximumAltitudeFeet;
        }

        private bool TryGetSamSitePosition(SamSite site, out Vector3 position)
        {
            return gameManager.airDefenseSiteSystem.TryGetPositionFeet(
                site,
                out position);
        }

        private IReadOnlyDictionary<Guid, FlightContext> BuildFlightContexts()
        {
            var contexts = new Dictionary<Guid, FlightContext>();
            foreach (var package in airTaskingSystem.GetPackages())
            {
                foreach (var flight in package.Flights)
                {
                    if (!gameManager.squadronSystem.TryGetSquadron(
                            flight.SquadronId,
                            out var squadron)
                        || !aircraftTypes.TryGetValue(
                            squadron.AircraftTypeDefinitionId,
                            out var aircraftType))
                        continue;

                    var liveAircraft = squadron.Aircraft
                        .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                           && aircraft.Status != CampaignAircraftStatus.Lost)
                        .OrderBy(aircraft => aircraft.AircraftId)
                        .ToList();
                    contexts[flight.FlightId] = new FlightContext(
                        package.Alliance,
                        flight,
                        squadron,
                        aircraftType,
                        liveAircraft);
                }
            }
            return contexts;
        }

        private void AddRecord(
            ActiveOrdnanceEmploymentPass pass,
            OrdnanceEmploymentRecordStage stage,
            DateTime occurredAt,
            int quantity,
            string detail)
        {
            Records.Add(new OrdnanceEmploymentRecord
            {
                EmploymentPassId = pass.EmploymentPassId,
                Stage = stage,
                SourceKind = OrdnanceEmploymentSourceKind.AircraftFlight,
                SourceFlightId = pass.SourceFlightId,
                TargetFlightId = pass.TargetFlightId,
                TargetKind = pass.TargetKind,
                TargetSiteId = pass.TargetSiteId,
                TargetComponentId = pass.TargetComponentId,
                GroundTarget = pass.GroundPrimaryTargets?.FirstOrDefault()
                    ?.Target?.Clone(),
                GroundOpportunityTargets = (pass.GroundOpportunityTargets
                                             ?? new List<
                                                 GroundAttackOpportunityTarget>())
                    .Where(target => target != null)
                    .Select(target => target.Clone())
                    .ToList(),
                GroundOpportunityDescription =
                    pass.GroundOpportunityDescription ?? string.Empty,
                OrdnanceTypeDefinitionId = pass.OrdnanceTypeDefinitionId,
                Quantity = quantity,
                OccurredAt = occurredAt,
                Detail = detail
            });
        }

        private void AddRecord(
            PendingOrdnanceEffect effect,
            OrdnanceEmploymentRecordStage stage,
            DateTime occurredAt,
            int quantity,
            string detail,
            List<OrdnanceShotDiagnostic> shots = null)
        {
            Records.Add(new OrdnanceEmploymentRecord
            {
                EmploymentPassId = effect.EmploymentPassId,
                PendingEffectId = effect.PendingEffectId,
                Stage = stage,
                SourceKind = effect.SourceKind,
                SourceFlightId = effect.SourceFlightId,
                SourceAircraftId = effect.SourceAircraftId,
                SourceSiteId = effect.SourceSiteId,
                SourceComponentId = effect.SourceComponentId,
                TargetFlightId = effect.TargetFlightId,
                TargetKind = effect.TargetKind,
                TargetSiteId = effect.TargetSiteId,
                TargetComponentId = effect.TargetComponentId,
                GroundTarget = effect.GroundPrimaryTarget?.Target?.Clone(),
                GroundOpportunityTargets = (effect.GroundOpportunityTargets
                                             ?? new List<
                                                 GroundAttackOpportunityTarget>())
                    .Where(target => target != null)
                    .Select(target => target.Clone())
                    .ToList(),
                GroundOpportunityDescription =
                    effect.GroundOpportunityDescription ?? string.Empty,
                OrdnanceTypeDefinitionId = effect.OrdnanceTypeDefinitionId,
                Quantity = quantity,
                OccurredAt = occurredAt,
                HitProbability = effect.HitProbability,
                ReleaseRangeKm = effect.ReleaseRangeKm,
                SourcePositionFeet = effect.SourcePositionFeet,
                TargetPositionFeet = effect.TargetPositionFeet,
                Launches = effect.Launches ?? new List<OrdnanceLaunchDiagnostic>(),
                Shots = shots ?? new List<OrdnanceShotDiagnostic>(),
                Detail = detail
            });
        }

        private void TrimEmploymentRecords()
        {
            var records = Records;
            if (records.Count <= MaximumEmploymentRecords)
                return;
            records.RemoveRange(0, records.Count - MaximumEmploymentRecords);
        }

        private static bool IsAirToAir(OrdnanceTypeDefinition definition)
        {
            return definition.EmploymentCategory ==
                   OrdnanceEmploymentCategory.AirToAirRadar
                   || definition.EmploymentCategory ==
                   OrdnanceEmploymentCategory.AirToAirInfrared
                   || (definition.EmploymentCategory ==
                       OrdnanceEmploymentCategory.Gun
                   && definition.GetEffectiveness(
                           OrdnanceTargetCategory.Aircraft) > 0f);
        }

        private static bool IsAntiRadiation(OrdnanceTypeDefinition definition)
        {
            return definition != null
                   && (definition.EmploymentCategory
                       == OrdnanceEmploymentCategory.AntiRadiation
                       || definition.GuidanceMode
                       == OrdnanceGuidanceMode.AntiRadiation);
        }

        private static bool IsWvrWeapon(OrdnanceTypeDefinition definition)
        {
            return definition.EmploymentCategory
                       == OrdnanceEmploymentCategory.AirToAirInfrared
                   || (definition.EmploymentCategory
                       == OrdnanceEmploymentCategory.Gun
                       && definition.GetEffectiveness(
                           OrdnanceTargetCategory.Aircraft) > 0f);
        }

        private static bool IsRadarGuided(OrdnanceGuidanceMode mode)
        {
            return mode == OrdnanceGuidanceMode.Radar
                   || mode == OrdnanceGuidanceMode.ActiveRadar
                   || mode == OrdnanceGuidanceMode.SemiActiveRadar;
        }

        private static int CountRounds(
            CampaignAircraft aircraft,
            Guid ordnanceTypeDefinitionId)
        {
            return aircraft.Loadout
                .Where(item => item.OrdnanceTypeDefinitionId == ordnanceTypeDefinitionId)
                .Sum(item => Math.Max(0, item.Count));
        }

        private static float HorizontalDistanceKm(Vector3 first, Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second)
                   / AirspaceGeometry.FeetPerKilometer;
        }

        private static bool IsGroundTargetInRange(
            Vector3 sourcePositionFeet,
            Vector3 targetPositionFeet,
            OrdnanceTypeDefinition ordnance)
        {
            var distanceKm = HorizontalDistanceKm(
                sourcePositionFeet,
                targetPositionFeet);
            return distanceKm >= ordnance.MinimumRangeKm
                   && distanceKm <= ordnance.MaximumRangeKm;
        }

        private bool TryResolveGroundTarget(
            GroundAttackOpportunityTarget target,
            out ResolvedGroundTarget resolved)
        {
            resolved = null;
            var reference = target?.Target;
            if (reference == null)
                return false;

            switch (reference.Kind)
            {
                case GroundAttackTargetKind.AirDefenseComponent:
                    if (!gameManager.airDefenseSiteSystem.TryGetSite(
                            reference.ParentEntityId,
                            out var site)
                        || site.IsDisabled
                        || site.IsDestroyed
                        || !gameManager.airDefenseSiteSystem.TryGetTileId(
                            site,
                            out var siteTileId)
                        || !gameManager.airDefenseSiteSystem.TryGetPositionFeet(
                            site,
                            out var sitePositionFeet))
                        return false;
                    var component = site.Components.FirstOrDefault(candidate =>
                        candidate != null
                        && candidate.ComponentId == reference.EntityId
                        && !candidate.IsDamaged);
                    if (component == null
                        || !airDefenseComponentDefinitions.TryGetValue(
                            component.SamComponentDefinitionId,
                            out var componentDefinition))
                        return false;
                    resolved = new ResolvedGroundTarget
                    {
                        Alliance = gameManager.airDefenseSiteSystem
                            .GetEffectiveAlliance(site),
                        TileId = siteTileId,
                        PositionFeet = sitePositionFeet,
                        Component = component,
                        ComponentDefinition = componentDefinition
                    };
                    return true;

                case GroundAttackTargetKind.Division:
                    if (!gameManager.divisionSystem.TryGetDivision(
                            reference.EntityId,
                            out var division)
                        || division.Strength < 1f)
                        return false;
                    resolved = new ResolvedGroundTarget
                    {
                        Alliance = gameManager.GetCountryAlliance(
                            division.CountryId),
                        TileId = division.TileId,
                        PositionFeet = AirspaceGeometry.TileCenterFeet(
                            division.TileId),
                        Division = division
                    };
                    return true;

                case GroundAttackTargetKind.Building:
                    if (!gameManager.buildingSystem.TryGetBuilding(
                            reference.EntityId,
                            out var building)
                        || building.FunctionalLevel <= 0
                        || !gameManager.tileSystem.TryGetLand(
                            building.TileId,
                            out var buildingTile))
                        return false;
                    resolved = new ResolvedGroundTarget
                    {
                        Alliance = buildingTile.Controller,
                        TileId = building.TileId,
                        PositionFeet = building.PositionFeet,
                        Building = building
                    };
                    return true;

                case GroundAttackTargetKind.GroundedAircraft:
                    if (!gameManager.squadronSystem.TryGetAircraft(
                            reference.EntityId,
                            out var squadron,
                            out var aircraft)
                        || aircraft.Status == CampaignAircraftStatus.Lost
                        || airTaskingSystem.GetPackages()
                            .SelectMany(package => package.Flights)
                            .Any(flight => flight.FlightId
                                           == aircraft.AssignedFlightId
                                           && flight.IsAirborne)
                        || !gameManager.buildingSystem.TryGetBuilding(
                            squadron.AirportBuildingId,
                            out var airportBuilding))
                        return false;
                    resolved = new ResolvedGroundTarget
                    {
                        Alliance = gameManager.GetCountryAlliance(
                            squadron.CountryId),
                        TileId = airportBuilding.TileId,
                        PositionFeet = airportBuilding.PositionFeet,
                        Aircraft = aircraft
                    };
                    return true;

                case GroundAttackTargetKind.TileInfrastructure:
                    if (!gameManager.tileSystem.TryGetLand(
                            reference.TileId,
                            out var landTile)
                        || landTile.InfrastructureFunctionalLevel <= 0)
                        return false;
                    resolved = new ResolvedGroundTarget
                    {
                        Alliance = landTile.Controller,
                        TileId = reference.TileId,
                        PositionFeet = AirspaceGeometry.TileCenterFeet(
                            reference.TileId),
                        LandTile = landTile
                    };
                    return true;

                default:
                    return false;
            }
        }

        private void ApplyGroundHit(GroundHitApplication application)
        {
            if (application?.Target?.Target == null
                || application.Ordnance == null
                || application.EffectMultiplier <= 0f)
                return;

            var reference = application.Target.Target;
            switch (reference.Kind)
            {
                case GroundAttackTargetKind.AirDefenseComponent:
                    gameManager.airDefenseSiteSystem.DamageComponent(
                        reference.ParentEntityId,
                        reference.EntityId);
                    break;

                case GroundAttackTargetKind.Division:
                    var effectiveness = application.Ordnance.GetEffectiveness(
                        application.Target.TargetCategory);
                    var strengthDamage = Math.Max(
                        1f,
                        application.Ordnance.EffectPower
                        * effectiveness
                        * application.EffectMultiplier);
                    gameManager.divisionSystem.ApplyAirAttackDamage(
                        reference.EntityId,
                        strengthDamage,
                        strengthDamage * 2f);
                    break;

                case GroundAttackTargetKind.Building:
                    gameManager.buildingSystem.ApplyDamage(
                        reference.EntityId,
                        1);
                    break;

                case GroundAttackTargetKind.GroundedAircraft:
                    gameManager.squadronSystem.ApplyGroundAttackDamage(
                        reference.EntityId,
                        destroyed: true);
                    break;

                case GroundAttackTargetKind.TileInfrastructure:
                    gameManager.tileSystem.ApplyInfrastructureDamage(
                        reference.TileId,
                        1);
                    break;
            }
        }

        private static int StableIndex(Guid id, int sequence, int count)
        {
            if (count <= 1)
                return 0;
            var seed = StableSeed(id, sequence);
            return (int)((uint)seed % (uint)count);
        }

        private static double StableRoll(Guid id, int sequence)
        {
            return new System.Random(StableSeed(id, sequence)).NextDouble();
        }

        private static int StableSeed(Guid id, int sequence)
        {
            unchecked
            {
                var seed = 17;
                foreach (var value in id.ToByteArray())
                    seed = seed * 31 + value;
                return seed * 31 + sequence;
            }
        }

        private static string ShortId(Guid id)
        {
            return SimLogNames.ShortId(id);
        }

        private sealed class AuthorizedOrdnanceRelease
        {
            public Guid EmploymentPassId;
            public OrdnanceEmploymentSourceKind SourceKind;
            public Guid SourceFlightId;
            public Guid SourceAircraftId;
            public Guid SourceSiteId;
            public Guid SourceComponentId;
            public FlightContext Target;
            public OrdnanceTypeDefinition Ordnance;
            public int Quantity;
            public DateTime ReleasedAt;
            public Vector3 SourcePositionFeet;
            public List<OrdnanceLaunchDiagnostic> Launches;
            public Guid SupportSourceFlightId;
            public Guid SupportSourceSiteId;
            public Guid SupportSourceComponentId;
            public float MaximumRangeKm;
            public float ShooterSensorQuality;
            public float LaunchQuality;
            public float ReleaseRangeKm;
            public float HitProbabilityOverride = -1f;
            public bool ResolveImmediately;
        }

        private sealed class ResolvedGroundTarget
        {
            public Alliance Alliance;
            public Vector3Int TileId;
            public Vector3 PositionFeet;
            public AirDefenseComponent Component;
            public AirDefenseComponentDefinition ComponentDefinition;
            public Division Division;
            public Building Building;
            public CampaignAircraft Aircraft;
            public RuntimeLandTile LandTile;
        }

        private sealed class GroundHitApplication
        {
            public GroundAttackOpportunityTarget Target { get; }
            public OrdnanceTypeDefinition Ordnance { get; }
            public float EffectMultiplier { get; }

            public GroundHitApplication(
                GroundAttackOpportunityTarget target,
                OrdnanceTypeDefinition ordnance,
                float effectMultiplier)
            {
                Target = target?.Clone();
                Ordnance = ordnance;
                EffectMultiplier = effectMultiplier;
            }
        }

        private sealed class SamEngagementCandidate
        {
            public readonly SamSite Site;
            public readonly IADSTrack Track;
            public readonly Guid FireControlRadarComponentId;
            public readonly float DistanceFeet;

            public SamEngagementCandidate(
                SamSite site,
                IADSTrack track,
                Guid fireControlRadarComponentId,
                float distanceFeet)
            {
                Site = site;
                Track = track;
                FireControlRadarComponentId = fireControlRadarComponentId;
                DistanceFeet = distanceFeet;
            }
        }

        private sealed class FlightContext
        {
            public readonly Alliance Alliance;
            public readonly AirFlight Flight;
            public readonly Squadron Squadron;
            public readonly AircraftTypeDefinition AircraftType;
            public readonly List<CampaignAircraft> LiveAircraft;

            public FlightContext(
                Alliance alliance,
                AirFlight flight,
                Squadron squadron,
                AircraftTypeDefinition aircraftType,
                List<CampaignAircraft> liveAircraft)
            {
                Alliance = alliance;
                Flight = flight;
                Squadron = squadron;
                AircraftType = aircraftType;
                LiveAircraft = liveAircraft;
            }

            public string DisplayName => string.IsNullOrWhiteSpace(Squadron.Name)
                ? $"Flight {ShortId(Flight.FlightId)}"
                : Squadron.Name;
        }

    }
}
