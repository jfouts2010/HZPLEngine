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

    public sealed class AirMissionRequestGenerator
    {
        private const int DefaultMissionRadiusTiles = 2;
        private const int DefaultCombatFlightStrength = 4;
        private static readonly TimeSpan HandoffBuffer = TimeSpan.FromMinutes(30);

        private readonly AirMissionPriorityService priorityService;

        public AirMissionRequestGenerator(AirMissionPriorityService priorityService)
        {
            this.priorityService = priorityService
                                   ?? throw new ArgumentNullException(nameof(priorityService));
        }

        public List<AirMissionRequest> Generate(
            AllianceAirTaskingCommander commander,
            AirPlanningSnapshot snapshot,
            int operationalCadenceHours)
        {
            if (commander == null)
                throw new ArgumentNullException(nameof(commander));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            var generated = new List<AirMissionRequest>();
            var effectStart = snapshot.CurrentTime + AirPackage.PreparationDelay;
            var effectEnd = snapshot.CurrentTime
                            + TimeSpan.FromHours(Math.Max(1, operationalCadenceHours))
                            + HandoffBuffer;

            foreach (var airportTile in snapshot.FriendlyAirportTiles)
            {
                var friendlyMissionArea = new AirMissionArea(
                    airportTile,
                    DefaultMissionRadiusTiles);
                if (priorityService.CalculatePowerNear(
                        snapshot.FriendlySquadrons,
                        friendlyMissionArea) > 0f)
                {
                    var desiredStrength = CalculateDesiredCombatStrength(
                        snapshot,
                        commander.Doctrine,
                        friendlyMissionArea);
                    var dcaRequest = CreateRequest(
                        commander,
                        AirMissionRequestType.DefensiveCounterAirPatrol,
                        AirMissionRequestFulfillmentPattern.Sustained,
                        airportTile,
                        effectStart,
                        effectEnd,
                        desiredAircraftStrength: desiredStrength,
                        rationale: "Protect friendly air operations and nearby airspace");
                    dcaRequest.PriorityComponents["desiredAircraftStrength"] = desiredStrength;
                    generated.Add(dcaRequest);
                }

                if (commander.Doctrine.BaselineAirborneC2Slots > 0)
                {
                    generated.Add(CreateRequest(
                        commander,
                        AirMissionRequestType.ProvideAirborneC2,
                        AirMissionRequestFulfillmentPattern.Sustained,
                        airportTile,
                        effectStart,
                        effectEnd,
                        desiredSupportSlots: commander.Doctrine.BaselineAirborneC2Slots,
                        rationale: "Provide baseline airborne C2 coverage"));
                }
            }

            foreach (var airportTile in snapshot.HostileAirportTiles)
            {
                if (!snapshot.FriendlySquadrons.Any(squadron =>
                        squadron.ReadyAircraftCount > 0
                        && priorityService.CalculateAirCombatPower(squadron) > 0f))
                    break;

                var missionArea = new AirMissionArea(airportTile, DefaultMissionRadiusTiles);
                var desiredStrength = CalculateDesiredCombatStrength(
                    snapshot,
                    commander.Doctrine,
                    missionArea);
                var ocaRequest = CreateRequest(
                    commander,
                    AirMissionRequestType.OffensiveCounterAirSweep,
                    AirMissionRequestFulfillmentPattern.Discrete,
                    airportTile,
                    effectStart,
                    effectStart + TimeSpan.FromHours(2),
                    desiredAircraftStrength: desiredStrength,
                    rationale: "Contest hostile air activity near an enemy operating base");
                ocaRequest.PriorityComponents["desiredAircraftStrength"] = desiredStrength;
                generated.Add(ocaRequest);
            }

            var combatRequests = generated
                .Where(request => !request.IsSupportRequest)
                .ToList();
            foreach (var airportTile in snapshot.FriendlyAirportTiles)
            {
                var baseline = commander.Doctrine.BaselineAerialRefuelingSlots;
                var observed = CalculateObservedTankerDemand(commander, airportTile, snapshot.CurrentTime);
                var forecast = combatRequests
                    .Where(request => request.MissionArea.Contains(airportTile)
                                      || new AirMissionArea(airportTile, DefaultMissionRadiusTiles)
                                          .Contains(request.MissionArea.CenterTileId))
                    .Sum(request => request.DesiredAircraftStrength);
                var desiredSlots = Math.Max(0, baseline + observed + forecast);
                if (desiredSlots == 0)
                    continue;

                var tankerRequest = CreateRequest(
                    commander,
                    AirMissionRequestType.ProvideAerialRefueling,
                    AirMissionRequestFulfillmentPattern.Sustained,
                    airportTile,
                    effectStart,
                    effectEnd,
                    desiredSupportSlots: desiredSlots,
                    rationale: "Provide blended baseline, observed, and forecast aerial-refueling capacity");
                tankerRequest.PriorityComponents["baselineDemand"] = baseline;
                tankerRequest.PriorityComponents["observedDemand"] = observed;
                tankerRequest.PriorityComponents["forecastDemand"] = forecast;
                generated.Add(tankerRequest);
            }

            foreach (var request in generated)
                priorityService.Score(request, commander.Doctrine, snapshot);

            return generated
                .OrderByDescending(request => request.Priority)
                .ThenBy(request => request.RequestType)
                .ThenBy(request => request.MissionArea.CenterTileId.x)
                .ThenBy(request => request.MissionArea.CenterTileId.y)
                .ThenBy(request => request.MissionArea.CenterTileId.z)
                .ToList();
        }

        private static AirMissionRequest CreateRequest(
            AllianceAirTaskingCommander commander,
            AirMissionRequestType requestType,
            AirMissionRequestFulfillmentPattern fulfillmentPattern,
            Vector3Int centerTile,
            DateTime effectStart,
            DateTime effectEnd,
            int desiredAircraftStrength = 0,
            int desiredSupportSlots = 0,
            string rationale = "")
        {
            return new AirMissionRequest
            {
                Alliance = commander.Alliance,
                RequestType = requestType,
                FulfillmentPattern = fulfillmentPattern,
                MissionArea = new AirMissionArea(centerTile, DefaultMissionRadiusTiles),
                CreatedAt = effectStart - AirPackage.PreparationDelay,
                EffectStart = effectStart,
                EffectEnd = effectEnd,
                PlanningCycle = commander.PlanningCycle,
                DesiredAircraftStrength = Math.Max(0, desiredAircraftStrength),
                DesiredSupportSlots = Math.Max(0, desiredSupportSlots),
                Rationale = rationale ?? string.Empty
            };
        }

        private int CalculateDesiredCombatStrength(
            AirPlanningSnapshot snapshot,
            AllianceAirDoctrine doctrine,
            AirMissionArea missionArea)
        {
            var friendlyPower = priorityService.CalculatePowerNear(
                snapshot.FriendlySquadrons,
                missionArea);
            var hostilePower = priorityService.CalculatePowerNear(
                snapshot.HostileSquadrons,
                missionArea);
            if (hostilePower <= 0f)
                return Math.Max(2, DefaultCombatFlightStrength / 2);

            var desiredAdvantage = Math.Max(
                0.1f,
                doctrine?.DesiredAirCombatAdvantage
                ?? AllianceAirDoctrine.DefaultDesiredAirCombatAdvantage);
            var pressureRatio = hostilePower * desiredAdvantage / Math.Max(0.1f, friendlyPower);
            var strengthScale = Mathf.Clamp(pressureRatio, 0.5f, 2f);
            return Math.Max(
                2,
                (int)Math.Ceiling(DefaultCombatFlightStrength * strengthScale));
        }

        private static int CalculateObservedTankerDemand(
            AllianceAirTaskingCommander commander,
            Vector3Int centerTile,
            DateTime currentTime)
        {
            var recentThreshold = currentTime - TimeSpan.FromHours(24);
            var demandArea = new AirMissionArea(centerTile, DefaultMissionRadiusTiles);
            return (commander.SupportDemandHistory ?? new List<SupportDemandSample>())
                .Where(sample => sample != null
                                 && sample.SupportType == AirMissionRequestType.ProvideAerialRefueling
                                 && sample.RecordedAt >= recentThreshold
                                 && demandArea.Contains(sample.MissionArea.CenterTileId))
                .Sum(sample => Math.Max(0, sample.RequestedSlots));
        }
    }

    public sealed class ProjectedAirEffectService
    {
        public bool TryFindFirstCoverageGap(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime planningStart,
            out DateTime gapStart,
            out int projectedAmount)
        {
            gapStart = planningStart;
            projectedAmount = 0;
            if (commander == null || request == null)
                return false;

            var desiredAmount = request.IsSupportRequest
                ? request.DesiredSupportSlots
                : request.DesiredAircraftStrength;
            if (desiredAmount <= 0)
                return false;

            var intervalStart = planningStart > request.EffectStart
                ? planningStart
                : request.EffectStart;
            if (intervalStart >= request.EffectEnd)
                return false;

            var flights = GetProjectedFlights(commander, request)
                .Where(flight => flight.EffectEnd > intervalStart
                                 && flight.EffectStart < request.EffectEnd)
                .ToList();
            var eventTimes = new SortedSet<DateTime> { intervalStart };
            foreach (var flight in flights)
            {
                if (flight.EffectStart > intervalStart && flight.EffectStart < request.EffectEnd)
                    eventTimes.Add(flight.EffectStart);
                if (flight.EffectEnd > intervalStart && flight.EffectEnd < request.EffectEnd)
                    eventTimes.Add(flight.EffectEnd);
            }

            foreach (var eventTime in eventTimes)
            {
                var amount = flights
                    .Where(flight => flight.EffectStart <= eventTime && flight.EffectEnd > eventTime)
                    .Sum(flight => request.IsSupportRequest
                        ? Math.Max(0, flight.ProvidedSupportSlots)
                        : flight.AircraftIds?.Count ?? 0);
                if (amount >= desiredAmount)
                    continue;

                gapStart = eventTime;
                projectedAmount = amount;
                return true;
            }

            return false;
        }

        public bool HasEquivalentDiscreteCommitment(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request)
        {
            if (commander == null || request == null)
                return false;

            var requestsById = (commander.MissionRequests ?? new List<AirMissionRequest>())
                .Where(candidate => candidate != null)
                .GroupBy(candidate => candidate.MissionRequestId)
                .ToDictionary(group => group.Key, group => group.First());
            return (commander.Packages ?? new List<AirPackage>())
                .Where(package => package != null && !package.IsTerminal)
                .Any(package =>
                    requestsById.TryGetValue(package.MissionRequestId, out var origin)
                    && origin.RequestType == request.RequestType
                    && origin.MissionArea.CenterTileId == request.MissionArea.CenterTileId);
        }

        public IReadOnlyList<AirFlight> GetSupportingFlights(
            AllianceAirTaskingCommander commander,
            AirMissionRequestType supportType,
            AirMissionArea missionArea,
            DateTime start,
            DateTime end)
        {
            if (commander == null)
                return Array.Empty<AirFlight>();

            return (commander.Packages ?? new List<AirPackage>())
                .Where(package => package != null && !package.IsTerminal)
                .SelectMany(package => package.Flights ?? new List<AirFlight>())
                .Where(flight => flight != null
                                 && !flight.IsTerminal
                                 && flight.MissionType == supportType
                                 && flight.EffectStart <= start
                                 && flight.EffectEnd >= end
                                 && flight.MissionArea.Contains(missionArea.CenterTileId))
                .OrderBy(flight => flight.EffectStart)
                .ThenBy(flight => flight.SquadronId)
                .ToList();
        }

        public int GetAvailableSupportSlots(
            AirFlight supportingFlight,
            DateTime start,
            DateTime end)
        {
            if (supportingFlight == null)
                return 0;

            var reserved = (supportingFlight.SupportReservations ?? new List<AirSupportReservation>())
                .Where(reservation => reservation != null
                                      && reservation.StartTime < end
                                      && reservation.EndTime > start)
                .Sum(reservation => Math.Max(0, reservation.SlotCount));
            return Math.Max(0, supportingFlight.ProvidedSupportSlots - reserved);
        }

        private static IEnumerable<AirFlight> GetProjectedFlights(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request)
        {
            var packageIds = new HashSet<Guid>(request.PackageIds ?? new List<Guid>());
            return (commander.Packages ?? new List<AirPackage>())
                .Where(package => package != null
                                  && !package.IsTerminal
                                  && (package.MissionRequestId == request.MissionRequestId
                                      || packageIds.Contains(package.PackageId)))
                .SelectMany(package => package.Flights ?? new List<AirFlight>())
                .Where(flight => flight != null && !flight.IsTerminal);
        }
    }

    public enum AirPackageBuildOutcome
    {
        Built,
        AlreadySatisfied,
        EquivalentCommitment,
        Deferred
    }

    public sealed class AirPackageBuilder
    {
        private const double MaximumFlightDurationHours = 6d;
        private const float AerialRefuelingRangeMultiplier = 2f;

        private readonly GameManager gameManager;
        private readonly ProjectedAirEffectService projectedEffects;
        private readonly AirMissionPriorityService priorityService;
        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypes;

        public AirPackageBuilder(
            GameManager gameManager,
            ModuleDefinition module,
            ProjectedAirEffectService projectedEffects,
            AirMissionPriorityService priorityService)
        {
            this.gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            if (module == null)
                throw new ArgumentNullException(nameof(module));
            this.projectedEffects = projectedEffects
                                    ?? throw new ArgumentNullException(nameof(projectedEffects));
            this.priorityService = priorityService
                                   ?? throw new ArgumentNullException(nameof(priorityService));
            aircraftTypes = module.AircraftTypeDefinitions
                .Where(definition => definition != null)
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
        }

        public AirPackageBuildOutcome TryBuild(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime currentTime,
            out AirPackage package,
            out string reason)
        {
            package = null;
            reason = string.Empty;
            if (commander == null || request == null)
            {
                reason = "Commander and request are required.";
                return AirPackageBuildOutcome.Deferred;
            }

            return request.IsSupportRequest
                ? TryBuildSupportPackage(commander, request, currentTime, out package, out reason)
                : TryBuildCombatPackage(commander, request, currentTime, out package, out reason);
        }

        private AirPackageBuildOutcome TryBuildSupportPackage(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime currentTime,
            out AirPackage package,
            out string reason)
        {
            package = null;
            reason = string.Empty;
            var planningStart = currentTime + AirPackage.PreparationDelay;
            if (!projectedEffects.TryFindFirstCoverageGap(
                    commander,
                    request,
                    planningStart,
                    out var gapStart,
                    out var projectedSlots))
            {
                reason = "Desired support capacity is already projected.";
                return AirPackageBuildOutcome.AlreadySatisfied;
            }

            var requiredCapability = request.RequestType == AirMissionRequestType.ProvideAirborneC2
                ? AirSupportCapability.AirborneC2
                : AirSupportCapability.AerialRefueling;
            var remainingSlots = Math.Max(1, request.DesiredSupportSlots - projectedSlots);
            var candidates = GetFriendlySquadrons(commander.Alliance)
                .Where(squadron =>
                    aircraftTypes.TryGetValue(squadron.AircraftTypeDefinitionId, out var aircraftType)
                    && aircraftType.SupportCapability == requiredCapability
                    && aircraftType.SupportSlotCapacity > 0)
                .Select(squadron => new
                {
                    Squadron = squadron,
                    AircraftType = aircraftTypes[squadron.AircraftTypeDefinitionId],
                    AvailableAircraft = GetAvailableAircraft(squadron)
                })
                .Where(candidate => candidate.AvailableAircraft.Count > 0)
                .OrderBy(candidate => GetAirportDistance(candidate.Squadron, request.MissionArea.CenterTileId))
                .ThenBy(candidate => candidate.Squadron.SquadronId)
                .FirstOrDefault();
            if (candidates == null)
            {
                reason = $"No ready {requiredCapability} aircraft are available.";
                return AirPackageBuildOutcome.Deferred;
            }

            var aircraftCount = Math.Min(
                candidates.AvailableAircraft.Count,
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        remainingSlots / (double)candidates.AircraftType.SupportSlotCapacity)));
            var selectedAircraft = candidates.AvailableAircraft.Take(aircraftCount).ToList();
            var effectStart = gapStart > planningStart ? gapStart : planningStart;
            var flightDuration = Math.Min(
                MaximumFlightDurationHours,
                Math.Max(0.25d, candidates.AircraftType.EnduranceHours));
            var effectEnd = Min(
                request.EffectEnd,
                effectStart + TimeSpan.FromHours(flightDuration));
            package = CreatePackage(request, currentTime, effectStart, effectEnd);
            var flight = CreateFlight(
                package,
                request,
                candidates.Squadron,
                selectedAircraft,
                effectStart,
                effectEnd);
            flight.ProvidedSupportSlots =
                selectedAircraft.Count * candidates.AircraftType.SupportSlotCapacity;
            package.Flights.Add(flight);

            reason = $"Proposed {flight.ProvidedSupportSlots} support slots.";
            return AirPackageBuildOutcome.Built;
        }

        private AirPackageBuildOutcome TryBuildCombatPackage(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime currentTime,
            out AirPackage package,
            out string reason)
        {
            package = null;
            reason = string.Empty;
            if (request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Discrete
                && projectedEffects.HasEquivalentDiscreteCommitment(commander, request))
            {
                reason = "An equivalent discrete effect is already committed.";
                return AirPackageBuildOutcome.EquivalentCommitment;
            }

            var planningStart = currentTime + AirPackage.PreparationDelay;
            DateTime effectStart;
            if (request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained)
            {
                if (!projectedEffects.TryFindFirstCoverageGap(
                        commander,
                        request,
                        planningStart,
                        out effectStart,
                        out _))
                {
                    reason = "Desired combat coverage is already projected.";
                    return AirPackageBuildOutcome.AlreadySatisfied;
                }
            }
            else
            {
                effectStart = planningStart > request.EffectStart
                    ? planningStart
                    : request.EffectStart;
            }

            var desiredStrength = Math.Max(1, request.DesiredAircraftStrength);
            var squadronCandidates = GetFriendlySquadrons(commander.Alliance)
                .Where(squadron =>
                    aircraftTypes.TryGetValue(squadron.AircraftTypeDefinitionId, out var aircraftType)
                    && priorityService.CanPerformAirCombat(aircraftType))
                .Select(squadron => new CombatSquadronCandidate(
                    squadron,
                    aircraftTypes[squadron.AircraftTypeDefinitionId],
                    GetAvailableAircraft(squadron),
                    GetAirportDistance(squadron, request.MissionArea.CenterTileId)))
                .Where(candidate => candidate.AvailableAircraft.Count > 0)
                .OrderBy(candidate => candidate.DistanceTiles)
                .ThenBy(candidate => candidate.Squadron.AirportBuildingId)
                .ThenBy(candidate => candidate.Squadron.SquadronId)
                .ToList();

            var selectedCandidates = SelectCombatAircraft(
                squadronCandidates,
                desiredStrength,
                request,
                commander,
                effectStart,
                out var tankerFlight);
            if (selectedCandidates.Sum(candidate => candidate.Aircraft.Count) < desiredStrength)
            {
                reason = $"Only {selectedCandidates.Sum(candidate => candidate.Aircraft.Count)}"
                         + $" of {desiredStrength} required combat aircraft are feasible.";
                return AirPackageBuildOutcome.Deferred;
            }

            var shortestEndurance = selectedCandidates
                .Select(candidate => GetEffectiveEndurance(candidate.AircraftType, tankerFlight != null))
                .DefaultIfEmpty(0.25d)
                .Min();
            var effectEnd = request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained
                ? Min(request.EffectEnd, effectStart + TimeSpan.FromHours(shortestEndurance))
                : Min(request.EffectEnd, effectStart + TimeSpan.FromHours(Math.Min(2d, shortestEndurance)));
            package = CreatePackage(request, currentTime, effectStart, effectEnd);

            foreach (var selected in selectedCandidates)
            {
                var flight = CreateFlight(
                    package,
                    request,
                    selected.Squadron,
                    selected.Aircraft,
                    effectStart,
                    effectEnd);
                package.Flights.Add(flight);
            }

            if (tankerFlight != null)
            {
                package.SupportingFlightIds.Add(tankerFlight.FlightId);
            }

            reason = $"Proposed {desiredStrength} combat aircraft.";
            return AirPackageBuildOutcome.Built;
        }

        private List<SelectedCombatAircraft> SelectCombatAircraft(
            IReadOnlyList<CombatSquadronCandidate> candidates,
            int desiredStrength,
            AirMissionRequest request,
            AllianceAirTaskingCommander commander,
            DateTime effectStart,
            out AirFlight tankerFlight)
        {
            tankerFlight = null;
            var sameAirportGroup = candidates
                .GroupBy(candidate => candidate.Squadron.AirportBuildingId)
                .Select(group => new
                {
                    Candidates = group.ToList(),
                    Count = group.Sum(candidate => candidate.AvailableAircraft.Count),
                    Distance = group.Min(candidate => candidate.DistanceTiles)
                })
                .Where(group => group.Count >= desiredStrength)
                .OrderBy(group => group.Distance)
                .ThenBy(group => group.Candidates[0].Squadron.AirportBuildingId)
                .FirstOrDefault();
            var orderedCandidates = sameAirportGroup?.Candidates ?? candidates.ToList();
            var selected = TakeAircraft(orderedCandidates, desiredStrength);
            if (selected.Sum(entry => entry.Aircraft.Count) < desiredStrength)
                return selected;

            var requiresTanker = selected.Any(entry =>
                !IsRouteFeasibleWithoutTanker(entry.Squadron, entry.AircraftType, request.MissionArea));
            if (!requiresTanker)
                return selected;

            if (selected.Any(entry => !entry.AircraftType.CanReceiveAerialRefueling))
                return new List<SelectedCombatAircraft>();

            var supportFlights = projectedEffects.GetSupportingFlights(
                commander,
                AirMissionRequestType.ProvideAerialRefueling,
                request.MissionArea,
                effectStart,
                GetRequiredTankerCoverageEnd(selected, request, effectStart));
            tankerFlight = supportFlights.FirstOrDefault(flight =>
                projectedEffects.GetAvailableSupportSlots(
                    flight,
                    effectStart,
                    request.EffectEnd) >= desiredStrength);
            if (tankerFlight == null)
                return new List<SelectedCombatAircraft>();

            return selected.All(entry =>
                    IsRouteFeasibleWithTanker(entry.Squadron, entry.AircraftType, request.MissionArea))
                ? selected
                : new List<SelectedCombatAircraft>();
        }

        private static List<SelectedCombatAircraft> TakeAircraft(
            IEnumerable<CombatSquadronCandidate> candidates,
            int desiredStrength)
        {
            var remaining = desiredStrength;
            var selected = new List<SelectedCombatAircraft>();
            foreach (var candidate in candidates)
            {
                if (remaining <= 0)
                    break;

                var aircraft = candidate.AvailableAircraft.Take(remaining).ToList();
                if (aircraft.Count == 0)
                    continue;

                selected.Add(new SelectedCombatAircraft(
                    candidate.Squadron,
                    candidate.AircraftType,
                    aircraft));
                remaining -= aircraft.Count;
            }

            return selected;
        }

        private AirPackage CreatePackage(
            AirMissionRequest request,
            DateTime currentTime,
            DateTime effectStart,
            DateTime effectEnd)
        {
            return new AirPackage
            {
                MissionRequestId = request.MissionRequestId,
                Alliance = request.Alliance,
                CreatedAt = currentTime,
                EarliestTakeoffTime = currentTime + AirPackage.PreparationDelay,
                EffectStart = effectStart,
                EffectEnd = effectEnd,
                HasRendezvous = false,
                Rationale = request.Rationale
            };
        }

        private static AirFlight CreateFlight(
            AirPackage package,
            AirMissionRequest request,
            Squadron squadron,
            IReadOnlyCollection<CampaignAircraft> aircraft,
            DateTime effectStart,
            DateTime effectEnd)
        {
            return new AirFlight
            {
                OwningPackageId = package.PackageId,
                SquadronId = squadron.SquadronId,
                MissionType = request.RequestType,
                IsRequired = true,
                AircraftIds = aircraft.Select(candidate => candidate.AircraftId).ToList(),
                PlannedTakeoffTime = package.EarliestTakeoffTime,
                EffectStart = effectStart,
                EffectEnd = effectEnd,
                MissionArea = new AirMissionArea(
                    request.MissionArea.CenterTileId,
                    request.MissionArea.RadiusTiles)
            };
        }

        private List<Squadron> GetFriendlySquadrons(Alliance alliance)
        {
            return (gameManager.squadronSystem.Squadrons ?? new List<Squadron>())
                .Where(squadron => squadron != null
                                   && gameManager.GetCountryAlliance(squadron.CountryId) == alliance)
                .OrderBy(squadron => squadron.SquadronId)
                .ToList();
        }

        private static List<CampaignAircraft> GetAvailableAircraft(Squadron squadron)
        {
            return (squadron.Aircraft ?? new List<CampaignAircraft>())
                .Where(aircraft => aircraft != null
                                   && aircraft.Status == CampaignAircraftStatus.Ready
                                   && aircraft.AssignedFlightId == Guid.Empty)
                .ToList();
        }

        private int GetAirportDistance(Squadron squadron, Vector3Int targetTile)
        {
            if (!gameManager.buildingSystem.TryGetBuilding(squadron.AirportBuildingId, out var building))
                return int.MaxValue;

            return AirMissionArea.HexDistance(building.TileId, targetTile);
        }

        private bool IsRouteFeasibleWithoutTanker(
            Squadron squadron,
            AircraftTypeDefinition aircraftType,
            AirMissionArea missionArea)
        {
            var distanceKm = GetRoundTripDistanceKm(squadron, missionArea.CenterTileId);
            return distanceKm <= Math.Max(0f, aircraftType.RangeKm);
        }

        private bool IsRouteFeasibleWithTanker(
            Squadron squadron,
            AircraftTypeDefinition aircraftType,
            AirMissionArea missionArea)
        {
            var distanceKm = GetRoundTripDistanceKm(squadron, missionArea.CenterTileId);
            return distanceKm <= Math.Max(0f, aircraftType.RangeKm) * AerialRefuelingRangeMultiplier;
        }

        private float GetRoundTripDistanceKm(Squadron squadron, Vector3Int targetTile)
        {
            var tileDistanceKm = gameManager.SimulationSettings?.TileDistanceKM
                                 ?? SimulationSettings.DefaultTileDistanceKM;
            return GetAirportDistance(squadron, targetTile) * tileDistanceKm * 2f;
        }

        private static double GetEffectiveEndurance(
            AircraftTypeDefinition aircraftType,
            bool hasTanker)
        {
            var endurance = Math.Max(0.25d, aircraftType.EnduranceHours);
            if (hasTanker && aircraftType.CanReceiveAerialRefueling)
                endurance *= AerialRefuelingRangeMultiplier;
            return Math.Min(MaximumFlightDurationHours, endurance);
        }

        private static DateTime GetRequiredTankerCoverageEnd(
            IReadOnlyCollection<SelectedCombatAircraft> selected,
            AirMissionRequest request,
            DateTime effectStart)
        {
            var tankerEndurance = selected
                .Select(candidate => GetEffectiveEndurance(candidate.AircraftType, hasTanker: true))
                .DefaultIfEmpty(0.25d)
                .Min();
            var durationHours = request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained
                ? tankerEndurance
                : Math.Min(2d, tankerEndurance);
            return Min(
                request.EffectEnd,
                effectStart + TimeSpan.FromHours(durationHours));
        }

        private static DateTime Min(DateTime first, DateTime second)
        {
            return first <= second ? first : second;
        }

        private sealed class CombatSquadronCandidate
        {
            public Squadron Squadron { get; }
            public AircraftTypeDefinition AircraftType { get; }
            public List<CampaignAircraft> AvailableAircraft { get; }
            public int DistanceTiles { get; }

            public CombatSquadronCandidate(
                Squadron squadron,
                AircraftTypeDefinition aircraftType,
                List<CampaignAircraft> availableAircraft,
                int distanceTiles)
            {
                Squadron = squadron;
                AircraftType = aircraftType;
                AvailableAircraft = availableAircraft;
                DistanceTiles = distanceTiles;
            }
        }

        private sealed class SelectedCombatAircraft
        {
            public Squadron Squadron { get; }
            public AircraftTypeDefinition AircraftType { get; }
            public List<CampaignAircraft> Aircraft { get; }

            public SelectedCombatAircraft(
                Squadron squadron,
                AircraftTypeDefinition aircraftType,
                List<CampaignAircraft> aircraft)
            {
                Squadron = squadron;
                AircraftType = aircraftType;
                Aircraft = aircraft;
            }
        }
    }
}
