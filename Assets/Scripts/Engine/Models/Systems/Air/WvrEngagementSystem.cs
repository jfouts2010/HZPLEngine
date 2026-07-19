using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Models
{
    internal sealed class WvrEngagementSystem
    {
        private const double RoundSeconds = 20d;
        private const int MandatoryDisengagementRound = 12;
        private const float AwarenessTrackQuality = 0.5f;
        private const float RearEntryAngleDegrees = 120f;
        private const float AttackerEntryAngleDegrees = 60f;
        private const float CloseControlMargin = 0.05f;
        private const float AdvantageControlBonus = 0.15f;
        internal const float DamagedWvrRatingMultiplier = 0.3f;
        internal const float DamagedCombatWeight = 0.25f;
        internal const float DamagedAircraftSpeedMultiplier = 0.6f;
        private const float BaseDisengagementChance = 0.3f;
        private const float FirstCoveringFlightBonus = 0.15f;
        private const float AdditionalCoveringFlightBonus = 0.08f;
        private const float SamAssignmentBonus = 0.15f;
        private const float PendingThreatBonus = 0.25f;

        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes;
        private readonly Func<Guid, Alliance> samAllianceForSite;
        private readonly Func<Alliance, Guid, bool> hasSamAssignment;
        private readonly List<WvrEngagement> activeEngagements =
            new List<WvrEngagement>();
        private readonly Dictionary<Guid, WvrRoundDiagnostic>
            latestRoundByFlightId =
                new Dictionary<Guid, WvrRoundDiagnostic>();

        public WvrEngagementSystem(
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            Func<Guid, Alliance> samAllianceForSite,
            Func<Alliance, Guid, bool> hasSamAssignment)
        {
            this.ordnanceTypes = ordnanceTypes
                ?? throw new ArgumentNullException(nameof(ordnanceTypes));
            this.samAllianceForSite = samAllianceForSite
                ?? throw new ArgumentNullException(nameof(samAllianceForSite));
            this.hasSamAssignment = hasSamAssignment
                ?? throw new ArgumentNullException(nameof(hasSamAssignment));
        }

        public bool IsFlightEngaged(Guid flightId)
        {
            return activeEngagements.Any(engagement =>
                engagement.BlueFlightIds.Contains(flightId)
                || engagement.RedFlightIds.Contains(flightId));
        }

        public bool TryGetLatestRound(
            Guid flightId,
            out WvrRoundDiagnostic diagnostic)
        {
            return latestRoundByFlightId.TryGetValue(flightId, out diagnostic);
        }

        public DateTime? GetNextScheduledEvent(DateTime after, DateTime noLaterThan)
        {
            return activeEngagements
                .Where(engagement => engagement.NextRoundAt > after
                                     && engagement.NextRoundAt <= noLaterThan)
                .Select(engagement => (DateTime?)engagement.NextRoundAt)
                .DefaultIfEmpty()
                .Min();
        }

        public void ProcessRequests(
            IEnumerable<AirCombatCommand> commands,
            AirCombatFrame frame,
            DateTime currentTime)
        {
            foreach (var command in commands
                         .Where(candidate => candidate.RequestsWvrEngagement)
                         .OrderBy(candidate => candidate.FlightId)
                         .ThenBy(candidate => candidate.TargetFlightId))
            {
                if (!frame.Flights.TryGetValue(command.FlightId, out var source)
                    || !frame.Flights.TryGetValue(command.TargetFlightId, out var target)
                    || source.Alliance == target.Alliance
                    || source.Alliance == Alliance.Neutral
                    || target.Alliance == Alliance.Neutral
                    || GetWvrAircraft(source).Count == 0
                    || GetWvrAircraft(target).Count == 0)
                    continue;

                var sourceEngagement = Find(command.FlightId);
                var targetEngagement = Find(command.TargetFlightId);
                if (sourceEngagement != null && targetEngagement != null)
                {
                    if (sourceEngagement != targetEngagement)
                        MergeEngagements(sourceEngagement, targetEngagement);
                    continue;
                }

                if (sourceEngagement != null)
                {
                    AddParticipant(sourceEngagement, target);
                    ApplyJoiningAdvantage(
                        sourceEngagement,
                        target,
                        source,
                        frame);
                    SetDogfightState(target.Flight, source.Flight.FlightId, currentTime);
                    continue;
                }

                if (targetEngagement != null)
                {
                    AddParticipant(targetEngagement, source);
                    ApplyJoiningAdvantage(
                        targetEngagement,
                        source,
                        target,
                        frame);
                    SetDogfightState(source.Flight, target.Flight.FlightId, currentTime);
                    continue;
                }

                var engagement = new WvrEngagement
                {
                    StartedAt = currentTime,
                    NextRoundAt = currentTime,
                    ForcedOpportunityPending = true
                };
                AddParticipant(engagement, source);
                AddParticipant(engagement, target);
                EstablishOpeningAdvantage(engagement, source, target, frame);
                activeEngagements.Add(engagement);
                SetDogfightState(source.Flight, target.Flight.FlightId, currentTime);
                SetDogfightState(target.Flight, source.Flight.FlightId, currentTime);
            }
        }

        public void AdvanceDueRounds(
            AirCombatFrame frame,
            Func<Alliance, AllianceAirDoctrine> doctrineFor,
            OrdnanceEmploymentSystem employmentSystem,
            DateTime currentTime)
        {
            foreach (var engagement in activeEngagements
                         .Where(candidate => candidate.NextRoundAt <= currentTime)
                         .OrderBy(candidate => candidate.EngagementId)
                         .ToList())
            {
                RemoveInvalidParticipants(engagement, frame);
                if (!HasBothSides(engagement))
                {
                    EndEngagement(engagement, frame, currentTime,
                        "WVR engagement ended because one side could no longer fight.");
                    continue;
                }

                engagement.RoundNumber++;
                var diagnostic = new WvrRoundDiagnostic
                {
                    EngagementId = engagement.EngagementId,
                    RoundNumber = engagement.RoundNumber,
                    ResolvedAt = currentTime,
                    BlueFlightIds = engagement.BlueFlightIds.ToList(),
                    RedFlightIds = engagement.RedFlightIds.ToList(),
                    StartingAdvantageAlliance = engagement.AdvantageAlliance,
                    StartingAdvantageLevel = engagement.AdvantageLevel
                };
                var escapedFlights = AttemptRequiredDisengagements(
                    engagement,
                    frame,
                    doctrineFor,
                    currentTime,
                    diagnostic);
                CaptureRoundState(diagnostic, engagement, frame);
                if (!HasBothSides(engagement))
                {
                    diagnostic.EndingAdvantageAlliance =
                        engagement.AdvantageAlliance;
                    diagnostic.EndingAdvantageLevel =
                        engagement.AdvantageLevel;
                    diagnostic.OpportunityReason =
                        "No attack opportunity was generated after one side disengaged.";
                    diagnostic.Outcome =
                        "Round ended the engagement after a successful disengagement.";
                    StoreLatestRound(diagnostic);
                    EndEngagement(engagement, frame, currentTime,
                        "WVR engagement ended after a successful disengagement.",
                        separate: true,
                        separationReference: escapedFlights.LastOrDefault());
                    continue;
                }

                var opportunities = BuildAttackOpportunities(
                    engagement,
                    frame,
                    diagnostic);
                foreach (var opportunity in opportunities)
                {
                    var weapon = SelectWeapon(opportunity.Source);
                    if (weapon == null)
                        continue;

                    var hitProbability = CalculateHitProbability(
                        weapon,
                        opportunity.Target.AircraftType,
                        opportunity.Advantage,
                        opportunity.TargetAware);
                    var released = employmentSystem.TryReleaseWvrAttack(
                        opportunity.Source.Flight.FlightId,
                        opportunity.Target.Flight.FlightId,
                        weapon.OrdnanceTypeDefinitionId,
                        hitProbability,
                        currentTime,
                        engagement.EngagementId,
                        engagement.RoundNumber,
                        opportunity.Advantage,
                        opportunity.TargetAware);
                    diagnostic.Attacks.Add(new WvrAttackDiagnostic
                    {
                        SourceFlightId = opportunity.Source.Flight.FlightId,
                        TargetFlightId = opportunity.Target.Flight.FlightId,
                        OrdnanceTypeDefinitionId =
                            weapon.OrdnanceTypeDefinitionId,
                        Advantage = opportunity.Advantage,
                        TargetAware = opportunity.TargetAware,
                        HitProbability = hitProbability,
                        Released = released
                    });
                }

                diagnostic.EndingAdvantageAlliance =
                    engagement.AdvantageAlliance;
                diagnostic.EndingAdvantageLevel =
                    engagement.AdvantageLevel;
                diagnostic.Outcome = diagnostic.Attacks.Count == 0
                    ? "Round completed without an available WVR attack."
                    : diagnostic.Attacks.Count == 1
                        ? "Round completed with 1 WVR attack opportunity."
                        : $"Round completed with {diagnostic.Attacks.Count} "
                          + "WVR attack opportunities.";
                StoreLatestRound(diagnostic);
                engagement.NextRoundAt = currentTime.AddSeconds(RoundSeconds);
            }
        }

        public void Reconcile(
            AirCombatFrame frame,
            DateTime currentTime)
        {
            foreach (var engagement in activeEngagements.ToList())
            {
                RemoveInvalidParticipants(engagement, frame);
                if (!HasBothSides(engagement))
                {
                    EndEngagement(engagement, frame, currentTime,
                        "WVR engagement ended because one side could no longer fight.");
                }
            }
        }

        private List<AttackOpportunity> BuildAttackOpportunities(
            WvrEngagement engagement,
            AirCombatFrame frame,
            WvrRoundDiagnostic diagnostic)
        {
            if (engagement.ForcedOpportunityPending
                && engagement.AdvantageLevel == WvrAdvantageLevel.Neutral)
            {
                engagement.ForcedOpportunityPending = false;
                diagnostic.OpportunityReason =
                    "Neutral opening generated mutual attack opportunities.";
                return CreateMutualOpportunities(engagement, frame);
            }

            if (engagement.AdvantageLevel == WvrAdvantageLevel.Dominant
                && engagement.AdvantageAlliance != Alliance.Neutral)
            {
                var targetAware = !engagement.OpeningTargetWasUnaware;
                var openingSource = SelectSource(
                    engagement,
                    frame,
                    engagement.AdvantageAlliance,
                    engagement.AdvantageSourceFlightId);
                var openingTarget = SelectTarget(
                    engagement,
                    frame,
                    OpponentOf(engagement.AdvantageAlliance),
                    engagement.PreferredTargetFlightId);
                engagement.AdvantageLevel = WvrAdvantageLevel.Favorable;
                engagement.OpeningTargetWasUnaware = false;
                engagement.ForcedOpportunityPending = false;
                diagnostic.OpportunityReason =
                    "Dominant advantage generated a forced attack opportunity.";
                return openingSource == null || openingTarget == null
                    ? new List<AttackOpportunity>()
                    : new List<AttackOpportunity>
                    {
                        new AttackOpportunity(
                            openingSource,
                            openingTarget,
                            WvrAdvantageLevel.Dominant,
                            targetAware)
                    };
            }

            if (engagement.ForcedOpportunityPending
                && engagement.AdvantageLevel == WvrAdvantageLevel.Favorable
                && engagement.AdvantageAlliance != Alliance.Neutral)
            {
                var openingSource = SelectSource(
                    engagement,
                    frame,
                    engagement.AdvantageAlliance,
                    engagement.AdvantageSourceFlightId);
                var openingTarget = SelectTarget(
                    engagement,
                    frame,
                    OpponentOf(engagement.AdvantageAlliance),
                    engagement.PreferredTargetFlightId);
                var targetAware = !engagement.OpeningTargetWasUnaware;
                engagement.OpeningTargetWasUnaware = false;
                engagement.ForcedOpportunityPending = false;
                diagnostic.OpportunityReason =
                    "Favorable advantage generated a forced attack opportunity.";
                return openingSource == null || openingTarget == null
                    ? new List<AttackOpportunity>()
                    : new List<AttackOpportunity>
                    {
                        new AttackOpportunity(
                            openingSource,
                            openingTarget,
                            WvrAdvantageLevel.Favorable,
                            targetAware)
                    };
            }

            var blueScore = CalculateControlScore(
                engagement,
                frame,
                Alliance.Bluefor);
            var redScore = CalculateControlScore(
                engagement,
                frame,
                Alliance.Redfor);
            diagnostic.UsedControlContest = true;
            diagnostic.BlueControlScore = blueScore;
            diagnostic.RedControlScore = redScore;
            var margin = blueScore - redScore;
            if (Math.Abs(margin) <= CloseControlMargin)
            {
                engagement.AdvantageAlliance = Alliance.Neutral;
                engagement.AdvantageLevel = WvrAdvantageLevel.Neutral;
                diagnostic.OpportunityReason =
                    "Close control scores generated mutual attack opportunities.";
                return CreateMutualOpportunities(engagement, frame);
            }

            var winner = margin > 0f ? Alliance.Bluefor : Alliance.Redfor;
            var source = SelectSource(engagement, frame, winner, Guid.Empty);
            var target = SelectTarget(
                engagement,
                frame,
                OpponentOf(winner),
                Guid.Empty);
            engagement.AdvantageAlliance = winner;
            engagement.AdvantageLevel = WvrAdvantageLevel.Favorable;
            engagement.AdvantageSourceFlightId = source?.Flight.FlightId ?? Guid.Empty;
            engagement.PreferredTargetFlightId = target?.Flight.FlightId ?? Guid.Empty;
            diagnostic.OpportunityReason =
                $"{winner} won the control contest and generated an attack opportunity.";
            return source == null || target == null
                ? new List<AttackOpportunity>()
                : new List<AttackOpportunity>
                {
                    new AttackOpportunity(
                        source,
                        target,
                        WvrAdvantageLevel.Favorable,
                        true)
                };
        }

        private List<AttackOpportunity> CreateMutualOpportunities(
            WvrEngagement engagement,
            AirCombatFrame frame)
        {
            var opportunities = new List<AttackOpportunity>();
            var blue = SelectSource(engagement, frame, Alliance.Bluefor, Guid.Empty);
            var red = SelectSource(engagement, frame, Alliance.Redfor, Guid.Empty);
            var blueTarget = SelectTarget(engagement, frame, Alliance.Redfor, Guid.Empty);
            var redTarget = SelectTarget(engagement, frame, Alliance.Bluefor, Guid.Empty);
            if (blue != null && blueTarget != null)
                opportunities.Add(new AttackOpportunity(
                    blue, blueTarget, WvrAdvantageLevel.Neutral, true));
            if (red != null && redTarget != null)
                opportunities.Add(new AttackOpportunity(
                    red, redTarget, WvrAdvantageLevel.Neutral, true));
            return opportunities;
        }

        private float CalculateControlScore(
            WvrEngagement engagement,
            AirCombatFrame frame,
            Alliance alliance)
        {
            var participants = GetParticipants(engagement, alliance)
                .Where(frame.Flights.ContainsKey)
                .Select(id => frame.Flights[id])
                .Where(view => GetWvrAircraft(view).Count > 0)
                .ToList();
            var physicalAircraftCount = participants.Sum(view =>
                GetWvrAircraft(view).Count);
            var combatWeight = participants.Sum(GetFlightCombatWeight);
            if (physicalAircraftCount <= 0 || combatWeight <= 0f)
                return 0f;

            var weightedRating = participants.Sum(view =>
                GetWvrAircraft(view).Sum(aircraft =>
                    view.AircraftType.WvrCombatRating
                    * DamageRatingMultiplier(aircraft)))
                                 / physicalAircraftCount;
            var numbersBonus = 0.1f * Mathf.Log(combatWeight, 2f);
            var advantageBonus = engagement.AdvantageAlliance == alliance
                                 && engagement.AdvantageLevel
                                 == WvrAdvantageLevel.Favorable
                ? AdvantageControlBonus
                : 0f;
            var uncertainty = (float)(StableRoll(
                engagement.EngagementId,
                engagement.RoundNumber * 10 + (int)alliance) * 0.2d - 0.1d);
            return weightedRating + numbersBonus + advantageBonus + uncertainty;
        }

        private static void CaptureRoundState(
            WvrRoundDiagnostic diagnostic,
            WvrEngagement engagement,
            AirCombatFrame frame)
        {
            CaptureAllianceState(
                engagement,
                frame,
                Alliance.Bluefor,
                out diagnostic.BlueAircraftCount,
                out diagnostic.BlueDamagedAircraftCount,
                out diagnostic.BlueEffectiveCombatWeight,
                out diagnostic.BlueEffectiveWvrRating);
            CaptureAllianceState(
                engagement,
                frame,
                Alliance.Redfor,
                out diagnostic.RedAircraftCount,
                out diagnostic.RedDamagedAircraftCount,
                out diagnostic.RedEffectiveCombatWeight,
                out diagnostic.RedEffectiveWvrRating);
        }

        private static void CaptureAllianceState(
            WvrEngagement engagement,
            AirCombatFrame frame,
            Alliance alliance,
            out int aircraftCount,
            out int damagedAircraftCount,
            out float combatWeight,
            out float effectiveRating)
        {
            var participants = GetParticipants(engagement, alliance)
                .Where(frame.Flights.ContainsKey)
                .Select(id => frame.Flights[id])
                .ToList();
            aircraftCount = participants.Sum(view =>
                GetWvrAircraft(view).Count);
            damagedAircraftCount = participants.Sum(view =>
                GetWvrAircraft(view).Count(aircraft =>
                    aircraft.Status == CampaignAircraftStatus.Damaged));
            combatWeight = participants.Sum(GetFlightCombatWeight);
            effectiveRating = aircraftCount <= 0
                ? 0f
                : participants.Sum(view =>
                    GetWvrAircraft(view).Sum(aircraft =>
                        view.AircraftType.WvrCombatRating
                        * DamageRatingMultiplier(aircraft)))
                  / aircraftCount;
        }

        private void StoreLatestRound(WvrRoundDiagnostic diagnostic)
        {
            foreach (var flightId in diagnostic.BlueFlightIds
                         .Concat(diagnostic.RedFlightIds)
                         .Distinct())
            {
                latestRoundByFlightId[flightId] = diagnostic;
            }
        }

        private List<AirCombatFlightView> AttemptRequiredDisengagements(
            WvrEngagement engagement,
            AirCombatFrame frame,
            Func<Alliance, AllianceAirDoctrine> doctrineFor,
            DateTime currentTime,
            WvrRoundDiagnostic diagnostic)
        {
            var escaped = new List<AirCombatFlightView>();
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                foreach (var flightId in GetParticipants(engagement, alliance).ToList())
                {
                    if (!frame.Flights.TryGetValue(flightId, out var view))
                        continue;
                    var doctrine = doctrineFor(alliance);
                    var damaged = HasDamagedAircraft(view);
                    var mustLeave = damaged
                                    || SelectWeapon(view) == null
                                    || view.Flight.TacticalState.FuelFraction
                                    <= doctrine.BingoFuelFraction
                                    || engagement.RoundNumber
                                    >= MandatoryDisengagementRound;
                    if (!mustLeave || engagement.RoundNumber < 2)
                        continue;

                    var opponents = GetParticipants(
                            engagement,
                            OpponentOf(alliance))
                        .Where(frame.Flights.ContainsKey)
                        .Select(id => frame.Flights[id])
                        .Where(candidate => GetWvrAircraft(candidate).Count > 0)
                        .ToList();
                    if (opponents.Count == 0)
                        continue;
                    var enemyRating = opponents.Average(GetEffectiveWvrRating);
                    var enemySpeed = opponents.Max(GetEffectiveCombatSpeed);
                    var ownSpeed = GetEffectiveCombatSpeed(view);
                    var speedRatio = ownSpeed / Math.Max(1f, enemySpeed);
                    var coveringFlights = GetParticipants(engagement, alliance)
                        .Where(id => id != flightId && frame.Flights.ContainsKey(id))
                        .Select(id => frame.Flights[id])
                        .Count(candidate => GetWvrAircraft(candidate).Any(aircraft =>
                                                aircraft.Status
                                                != CampaignAircraftStatus.Damaged)
                                            && SelectWeapon(candidate) != null);
                    coveringFlights = Math.Min(2, coveringFlights);
                    var coverBonus = coveringFlights <= 0
                        ? 0f
                        : FirstCoveringFlightBonus
                          + AdditionalCoveringFlightBonus
                          * Math.Max(0, coveringFlights - 1);
                    var externalPressure = CalculateExternalPressure(
                        frame,
                        alliance,
                        opponents);
                    var advantageModifier = engagement.AdvantageAlliance == alliance
                        ? 0.1f
                        : engagement.AdvantageAlliance == OpponentOf(alliance)
                            ? -0.15f
                            : 0f;
                    var chance = Mathf.Clamp(
                        BaseDisengagementChance
                        + 0.2f * (GetEffectiveWvrRating(view) - enemyRating)
                        + 0.2f * (speedRatio - 1f)
                        + coverBonus
                        + externalPressure
                        + advantageModifier,
                        0.05f,
                        0.8f);
                    var roll = (float)StableRoll(
                        engagement.EngagementId,
                        engagement.RoundNumber * 100 + StableCode(flightId));
                    var succeeded = roll <= chance;
                    diagnostic.Disengagements.Add(
                        new WvrDisengagementDiagnostic
                        {
                            FlightId = flightId,
                            Damaged = damaged,
                            EffectiveWvrRating =
                                GetEffectiveWvrRating(view),
                            EnemyAverageWvrRating = enemyRating,
                            SpeedRatio = speedRatio,
                            CoveringFlightCount = coveringFlights,
                            CoverBonus = coverBonus,
                            ExternalPressureBonus = externalPressure,
                            AdvantageModifier = advantageModifier,
                            Probability = chance,
                            Roll = roll,
                            Succeeded = succeeded
                        });
                    if (succeeded)
                    {
                        var nearestOpponent = opponents
                            .OrderBy(candidate => Vector3.SqrMagnitude(
                                candidate.Flight.PositionFeet
                                - view.Flight.PositionFeet))
                            .ThenBy(candidate => candidate.Flight.FlightId)
                            .First();
                        GetParticipants(engagement, alliance).Remove(flightId);
                        SetSeparationState(
                            view,
                            nearestOpponent,
                            currentTime,
                            damaged
                                ? $"Damaged flight successfully disengaged from WVR combat "
                                  + $"at {chance:P0} probability."
                                : $"Flight successfully disengaged from WVR combat "
                                  + $"at {chance:P0} probability.");
                        escaped.Add(view);
                        if (!HasBothSides(engagement))
                            return escaped;
                    }
                    else
                    {
                        engagement.AdvantageAlliance = OpponentOf(alliance);
                        engagement.AdvantageLevel = damaged
                            ? WvrAdvantageLevel.Dominant
                            : WvrAdvantageLevel.Favorable;
                        engagement.AdvantageSourceFlightId = Guid.Empty;
                        engagement.PreferredTargetFlightId = flightId;
                        engagement.OpeningTargetWasUnaware = false;
                        engagement.ForcedOpportunityPending = true;
                        view.Flight.TacticalState.DecisionReason =
                            $"WVR disengagement failed at {chance:P0} probability; "
                            + $"the opponent gained {engagement.AdvantageLevel} advantage.";
                    }
                }
            }
            return escaped;
        }

        private float CalculateExternalPressure(
            AirCombatFrame frame,
            Alliance escapingAlliance,
            IReadOnlyCollection<AirCombatFlightView> opponents)
        {
            var opponentIds = opponents
                .Select(view => view.Flight.FlightId)
                .ToHashSet();
            var hasPendingThreat = frame.PendingEffects.Any(effect =>
                opponentIds.Contains(effect.TargetFlightId)
                && IsEffectFriendlyTo(effect, escapingAlliance, frame));
            if (hasPendingThreat)
                return PendingThreatBonus;
            return opponentIds.Any(targetFlightId =>
                    hasSamAssignment(escapingAlliance, targetFlightId))
                ? SamAssignmentBonus
                : 0f;
        }

        private bool IsEffectFriendlyTo(
            PendingOrdnanceEffect effect,
            Alliance alliance,
            AirCombatFrame frame)
        {
            if (effect.SourceKind == OrdnanceEmploymentSourceKind.SamLauncher)
            {
                var siteId = effect.SourceSiteId != Guid.Empty
                    ? effect.SourceSiteId
                    : effect.SupportSourceSiteId;
                return samAllianceForSite(siteId) == alliance;
            }
            return effect.SourceKind
                       == OrdnanceEmploymentSourceKind.AircraftFlight
                   && frame.Flights.TryGetValue(
                       effect.SourceFlightId,
                       out var source)
                   && source.Alliance == alliance;
        }

        private static List<CampaignAircraft> GetWvrAircraft(
            AirCombatFlightView view)
        {
            return view.WvrAircraft ?? view.LiveAircraft
                ?? new List<CampaignAircraft>();
        }

        private static bool HasDamagedAircraft(AirCombatFlightView view)
        {
            return GetWvrAircraft(view).Any(aircraft =>
                aircraft.Status == CampaignAircraftStatus.Damaged);
        }

        private static float DamageRatingMultiplier(
            CampaignAircraft aircraft)
        {
            return aircraft.Status == CampaignAircraftStatus.Damaged
                ? DamagedWvrRatingMultiplier
                : 1f;
        }

        private static float GetEffectiveWvrRating(AirCombatFlightView view)
        {
            var aircraft = GetWvrAircraft(view);
            if (aircraft.Count == 0)
                return 0f;
            return aircraft.Average(item =>
                view.AircraftType.WvrCombatRating
                * DamageRatingMultiplier(item));
        }

        private static float GetFlightCombatWeight(AirCombatFlightView view)
        {
            return GetWvrAircraft(view).Sum(aircraft =>
                aircraft.Status == CampaignAircraftStatus.Damaged
                    ? DamagedCombatWeight
                    : 1f);
        }

        private static float GetEffectiveCombatSpeed(AirCombatFlightView view)
        {
            return view.AircraftType.CombatSpeedKnots
                   * (HasDamagedAircraft(view)
                       ? DamagedAircraftSpeedMultiplier
                       : 1f);
        }

        private AirCombatFlightView SelectSource(
            WvrEngagement engagement,
            AirCombatFrame frame,
            Alliance alliance,
            Guid preferredId)
        {
            return GetParticipants(engagement, alliance)
                .Where(frame.Flights.ContainsKey)
                .Select(id => frame.Flights[id])
                .Where(view => GetWvrAircraft(view).Count > 0
                               && SelectWeapon(view) != null)
                .OrderBy(view => view.Flight.FlightId == preferredId ? 0 : 1)
                .ThenByDescending(GetEffectiveWvrRating)
                .ThenBy(view => view.Flight.FlightId)
                .FirstOrDefault();
        }

        private AirCombatFlightView SelectTarget(
            WvrEngagement engagement,
            AirCombatFrame frame,
            Alliance alliance,
            Guid preferredId)
        {
            return GetParticipants(engagement, alliance)
                .Where(frame.Flights.ContainsKey)
                .Select(id => frame.Flights[id])
                .Where(view => GetWvrAircraft(view).Count > 0)
                .OrderBy(view => view.Flight.FlightId == preferredId ? 0 : 1)
                .ThenBy(view => GetWvrAircraft(view).Count)
                .ThenByDescending(view => GetWvrAircraft(view).Count(aircraft =>
                    aircraft.Status == CampaignAircraftStatus.Damaged))
                .ThenBy(view => view.Flight.FlightId)
                .FirstOrDefault();
        }

        private OrdnanceTypeDefinition SelectWeapon(AirCombatFlightView source)
        {
            var availableIds = GetWvrAircraft(source)
                .SelectMany(aircraft => aircraft.Loadout)
                .Where(item => item.Count > 0)
                .Select(item => item.OrdnanceTypeDefinitionId)
                .Distinct();
            return availableIds
                .Where(ordnanceTypes.ContainsKey)
                .Select(id => ordnanceTypes[id])
                .Where(IsWvrWeapon)
                .OrderBy(definition =>
                    definition.EmploymentCategory == OrdnanceEmploymentCategory.Gun
                        ? 1
                        : 0)
                .ThenByDescending(definition => definition.HitProbability)
                .ThenByDescending(definition => definition.CountermeasureResistance)
                .ThenBy(definition => definition.OrdnanceTypeDefinitionId)
                .FirstOrDefault();
        }

        internal static float CalculateHitProbability(
            OrdnanceTypeDefinition weapon,
            AircraftTypeDefinition targetType,
            WvrAdvantageLevel advantage,
            bool targetAware)
        {
            var baseProbability = Mathf.Clamp(weapon.HitProbability, 0.01f, 0.99f);
            var logOdds = Mathf.Log(baseProbability / (1f - baseProbability));
            logOdds += advantage == WvrAdvantageLevel.Dominant
                ? 1.5f
                : advantage == WvrAdvantageLevel.Favorable
                    ? 0.5f
                    : -1f;
            logOdds += targetAware ? -0.75f : 1f;
            if (targetAware
                && weapon.EmploymentCategory
                == OrdnanceEmploymentCategory.AirToAirInfrared)
            {
                logOdds -= (1f - weapon.CountermeasureResistance)
                           * targetType.EcmQuality
                           * 0.75f;
            }
            return Mathf.Clamp(1f / (1f + Mathf.Exp(-logOdds)), 0.02f, 0.98f);
        }

        private void EstablishOpeningAdvantage(
            WvrEngagement engagement,
            AirCombatFlightView first,
            AirCombatFlightView second,
            AirCombatFrame frame)
        {
            var firstScore = OpeningAdvantageScore(first, second, frame);
            var secondScore = OpeningAdvantageScore(second, first, frame);
            if (firstScore == secondScore || Math.Max(firstScore, secondScore) <= 0)
                return;

            var winner = firstScore > secondScore ? first : second;
            var target = firstScore > secondScore ? second : first;
            var score = Math.Max(firstScore, secondScore);
            engagement.AdvantageAlliance = winner.Alliance;
            engagement.AdvantageLevel = score >= 2
                ? WvrAdvantageLevel.Dominant
                : WvrAdvantageLevel.Favorable;
            engagement.AdvantageSourceFlightId = winner.Flight.FlightId;
            engagement.PreferredTargetFlightId = target.Flight.FlightId;
            engagement.OpeningTargetWasUnaware = !IsAwareOf(target, winner, frame);
            engagement.ForcedOpportunityPending = true;
        }

        private void ApplyJoiningAdvantage(
            WvrEngagement engagement,
            AirCombatFlightView entrant,
            AirCombatFlightView target,
            AirCombatFrame frame)
        {
            var score = OpeningAdvantageScore(entrant, target, frame);
            if (score <= 0)
                return;
            var level = score >= 2
                ? WvrAdvantageLevel.Dominant
                : WvrAdvantageLevel.Favorable;
            if (level < engagement.AdvantageLevel)
                return;

            engagement.AdvantageAlliance = entrant.Alliance;
            engagement.AdvantageLevel = level;
            engagement.AdvantageSourceFlightId = entrant.Flight.FlightId;
            engagement.PreferredTargetFlightId = target.Flight.FlightId;
            engagement.OpeningTargetWasUnaware = !IsAwareOf(target, entrant, frame);
            engagement.ForcedOpportunityPending = true;
        }

        private int OpeningAdvantageScore(
            AirCombatFlightView attacker,
            AirCombatFlightView target,
            AirCombatFrame frame)
        {
            var score = IsRearEntry(attacker, target) ? 1 : 0;
            if (!IsAwareOf(target, attacker, frame))
                score++;
            return score;
        }

        private static bool IsRearEntry(
            AirCombatFlightView attacker,
            AirCombatFlightView target)
        {
            var targetToAttacker = AirCombatRules.HeadingTo(
                target.Flight.PositionFeet,
                attacker.Flight.PositionFeet);
            var attackerToTarget = AirCombatRules.HeadingTo(
                attacker.Flight.PositionFeet,
                target.Flight.PositionFeet);
            return Math.Abs(Mathf.DeltaAngle(
                       target.Flight.HeadingDegrees,
                       targetToAttacker)) >= RearEntryAngleDegrees
                   && Math.Abs(Mathf.DeltaAngle(
                       attacker.Flight.HeadingDegrees,
                       attackerToTarget)) <= AttackerEntryAngleDegrees;
        }

        private static bool IsAwareOf(
            AirCombatFlightView observer,
            AirCombatFlightView hostile,
            AirCombatFrame frame)
        {
            if (observer.PreviousTargetFlightId == hostile.Flight.FlightId)
                return true;
            return frame.TryGetCurrentTrack(
                       observer.Alliance,
                       hostile.Flight.FlightId,
                       out var track)
                   && track.Quality >= AwarenessTrackQuality;
        }

        private void RemoveInvalidParticipants(
            WvrEngagement engagement,
            AirCombatFrame frame)
        {
            engagement.BlueFlightIds.RemoveAll(id => !IsValidParticipant(id, frame));
            engagement.RedFlightIds.RemoveAll(id => !IsValidParticipant(id, frame));
        }

        private static bool IsValidParticipant(Guid id, AirCombatFrame frame)
        {
            return frame.Flights.TryGetValue(id, out var view)
                   && view.Flight.IsAirborne
                   && GetWvrAircraft(view).Count > 0;
        }

        private void EndEngagement(
            WvrEngagement engagement,
            AirCombatFrame frame,
            DateTime currentTime,
            string reason,
            bool separate = false,
            AirCombatFlightView separationReference = null)
        {
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                foreach (var flightId in GetParticipants(engagement, alliance))
                {
                    if (!frame.Flights.TryGetValue(flightId, out var view))
                        continue;
                    var opponent = GetParticipants(
                            engagement,
                            OpponentOf(alliance))
                        .Where(frame.Flights.ContainsKey)
                        .Select(id => frame.Flights[id])
                        .Where(candidate => GetWvrAircraft(candidate).Count > 0)
                        .OrderBy(candidate => Vector3.SqrMagnitude(
                            candidate.Flight.PositionFeet
                            - view.Flight.PositionFeet))
                        .ThenBy(candidate => candidate.Flight.FlightId)
                        .FirstOrDefault();
                    opponent ??= separationReference;
                    if (separate && opponent != null)
                        SetSeparationState(view, opponent, currentTime, reason);
                    else
                        view.Flight.TacticalState.ClearCombat(currentTime, reason);
                }
            }
            activeEngagements.Remove(engagement);
        }

        private static void SetSeparationState(
            AirCombatFlightView flight,
            AirCombatFlightView opponent,
            DateTime currentTime,
            string reason)
        {
            var direction = flight.Flight.PositionFeet
                            - opponent.Flight.PositionFeet;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
            {
                var headingRadians = flight.Flight.HeadingDegrees * Mathf.Deg2Rad;
                direction = new Vector3(
                    Mathf.Sin(headingRadians),
                    0f,
                    Mathf.Cos(headingRadians));
            }
            direction.Normalize();
            var aimPoint = flight.Flight.PositionFeet
                           + direction
                           * 80f
                           * AirspaceGeometry.FeetPerKilometer;
            aimPoint.y = flight.Flight.PositionFeet.y;
            flight.Flight.TacticalState.Apply(
                AirCombatIntent.Disengage,
                AirCombatManeuver.Extend,
                currentTime,
                currentTime.AddSeconds(45),
                Guid.Empty,
                Guid.Empty,
                AirCombatManeuverSide.None,
                aimPoint,
                true,
                reason);
        }

        private static void SetDogfightState(
            AirFlight flight,
            Guid targetFlightId,
            DateTime currentTime)
        {
            flight.TacticalState.Apply(
                AirCombatIntent.EngageTarget,
                AirCombatManeuver.Dogfight,
                currentTime,
                currentTime.AddSeconds(RoundSeconds),
                targetFlightId,
                Guid.Empty,
                AirCombatManeuverSide.None,
                default,
                false,
                "Locked into abstract WVR combat.");
        }

        private WvrEngagement Find(Guid flightId)
        {
            return activeEngagements.FirstOrDefault(engagement =>
                engagement.BlueFlightIds.Contains(flightId)
                || engagement.RedFlightIds.Contains(flightId));
        }

        private void MergeEngagements(
            WvrEngagement retained,
            WvrEngagement merged)
        {
            foreach (var flightId in merged.BlueFlightIds)
            {
                if (!retained.BlueFlightIds.Contains(flightId))
                    retained.BlueFlightIds.Add(flightId);
            }
            foreach (var flightId in merged.RedFlightIds)
            {
                if (!retained.RedFlightIds.Contains(flightId))
                    retained.RedFlightIds.Add(flightId);
            }

            retained.StartedAt = retained.StartedAt <= merged.StartedAt
                ? retained.StartedAt
                : merged.StartedAt;
            retained.NextRoundAt = retained.NextRoundAt <= merged.NextRoundAt
                ? retained.NextRoundAt
                : merged.NextRoundAt;
            retained.RoundNumber = Math.Max(
                retained.RoundNumber,
                merged.RoundNumber);

            if (merged.AdvantageLevel > retained.AdvantageLevel)
            {
                CopyAdvantage(merged, retained);
            }
            else if (merged.AdvantageLevel == retained.AdvantageLevel
                     && merged.AdvantageAlliance != retained.AdvantageAlliance)
            {
                retained.AdvantageAlliance = Alliance.Neutral;
                retained.AdvantageLevel = WvrAdvantageLevel.Neutral;
                retained.AdvantageSourceFlightId = Guid.Empty;
                retained.PreferredTargetFlightId = Guid.Empty;
                retained.OpeningTargetWasUnaware = false;
                retained.ForcedOpportunityPending = true;
            }
            activeEngagements.Remove(merged);
        }

        private static void CopyAdvantage(
            WvrEngagement source,
            WvrEngagement destination)
        {
            destination.AdvantageAlliance = source.AdvantageAlliance;
            destination.AdvantageLevel = source.AdvantageLevel;
            destination.AdvantageSourceFlightId = source.AdvantageSourceFlightId;
            destination.PreferredTargetFlightId = source.PreferredTargetFlightId;
            destination.OpeningTargetWasUnaware =
                source.OpeningTargetWasUnaware;
            destination.ForcedOpportunityPending =
                source.ForcedOpportunityPending;
        }

        private static void AddParticipant(
            WvrEngagement engagement,
            AirCombatFlightView view)
        {
            var participants = GetParticipants(engagement, view.Alliance);
            if (!participants.Contains(view.Flight.FlightId))
                participants.Add(view.Flight.FlightId);
        }

        private static List<Guid> GetParticipants(
            WvrEngagement engagement,
            Alliance alliance)
        {
            return alliance == Alliance.Bluefor
                ? engagement.BlueFlightIds
                : engagement.RedFlightIds;
        }

        private static bool HasBothSides(WvrEngagement engagement)
        {
            return engagement.BlueFlightIds.Count > 0
                   && engagement.RedFlightIds.Count > 0;
        }

        private static Alliance OpponentOf(Alliance alliance)
        {
            return alliance == Alliance.Bluefor
                ? Alliance.Redfor
                : Alliance.Bluefor;
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

        private static double StableRoll(Guid id, int sequence)
        {
            unchecked
            {
                var seed = 17;
                foreach (var value in id.ToByteArray())
                    seed = seed * 31 + value;
                return new System.Random(seed * 31 + sequence).NextDouble();
            }
        }

        private static int StableCode(Guid id)
        {
            unchecked
            {
                var code = 17;
                foreach (var value in id.ToByteArray())
                    code = code * 31 + value;
                return code & 0x7fffffff;
            }
        }

        private sealed class AttackOpportunity
        {
            public readonly AirCombatFlightView Source;
            public readonly AirCombatFlightView Target;
            public readonly WvrAdvantageLevel Advantage;
            public readonly bool TargetAware;

            public AttackOpportunity(
                AirCombatFlightView source,
                AirCombatFlightView target,
                WvrAdvantageLevel advantage,
                bool targetAware)
            {
                Source = source;
                Target = target;
                Advantage = advantage;
                TargetAware = targetAware;
            }
        }
    }
}
