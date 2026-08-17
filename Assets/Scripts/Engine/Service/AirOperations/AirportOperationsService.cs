using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;

namespace Engine.Service
{
    public static class AirportOperationsRules
    {
        public const int DualChannelBuildLevel = 6;
        public const int AircraftPerChannelSlot = 4;
        public static readonly TimeSpan MovementWindow = TimeSpan.FromMinutes(15);

        public static int GetNominalCapacityChannels(Airport airport)
        {
            return airport?.NominalRunwayChannelCount ?? 0;
        }

        public static int GetEffectiveCapacityChannels(Airport airport)
        {
            return airport?.OperationalRunwayChannelCount ?? 0;
        }

        public static bool IsOperational(Airport airport)
        {
            return GetEffectiveCapacityChannels(airport) > 0;
        }

        public static int GetAircraftMovementCapacity(Airport airport)
        {
            return GetEffectiveCapacityChannels(airport)
                   * AircraftPerChannelSlot;
        }

        public static int GetRequiredChannelSlots(int aircraftCount)
        {
            return aircraftCount <= 0
                ? 0
                : (int)Math.Ceiling(
                    aircraftCount / (double)AircraftPerChannelSlot);
        }

        public static DateTime GetWindowStart(DateTime time)
        {
            var windowTicks = MovementWindow.Ticks;
            return new DateTime(
                time.Ticks - time.Ticks % windowTicks,
                time.Kind);
        }
    }

    public readonly struct AirportOperationsSnapshot
    {
        public readonly int MaximumRunwayDamage;
        public readonly int RunwayDamage;
        public readonly IReadOnlyList<int> RunwayDamageByChannel;
        public readonly int NominalCapacityChannels;
        public readonly int EffectiveCapacityChannels;
        public readonly int AircraftMovementCapacity;
        public readonly DateTime WindowStart;
        public readonly int ReservedChannelSlots;

        public bool IsOperational => EffectiveCapacityChannels > 0;
        public bool IsReduced =>
            IsOperational
            && EffectiveCapacityChannels < NominalCapacityChannels;
        public bool IsSaturated =>
            IsOperational
            && ReservedChannelSlots >= EffectiveCapacityChannels;

        public AirportOperationsSnapshot(
            int maximumRunwayDamage,
            int runwayDamage,
            IReadOnlyList<int> runwayDamageByChannel,
            int nominalCapacityChannels,
            int effectiveCapacityChannels,
            int aircraftMovementCapacity,
            DateTime windowStart,
            int reservedChannelSlots)
        {
            MaximumRunwayDamage = Math.Max(0, maximumRunwayDamage);
            RunwayDamage = Math.Max(0, runwayDamage);
            RunwayDamageByChannel = runwayDamageByChannel
                                     ?? Array.Empty<int>();
            NominalCapacityChannels = Math.Max(0, nominalCapacityChannels);
            EffectiveCapacityChannels = Math.Max(0, effectiveCapacityChannels);
            AircraftMovementCapacity = Math.Max(0, aircraftMovementCapacity);
            WindowStart = windowStart;
            ReservedChannelSlots = Math.Max(0, reservedChannelSlots);
        }
    }

    public sealed class AirportOperationsService
    {
        private readonly GameManager gameManager;

        public AirportOperationsService(GameManager gameManager)
        {
            this.gameManager = gameManager
                               ?? throw new ArgumentNullException(
                                   nameof(gameManager));
        }

        public bool TryGetAirport(Guid airportId, out Airport airport)
        {
            airport = null;
            if (airportId == Guid.Empty
                || !gameManager.buildingSystem.TryGetBuilding(
                    airportId,
                    out var building)
                || building is not Airport resolved)
            {
                return false;
            }

            airport = resolved;
            return true;
        }

        public bool IsAirportControlledBy(Guid airportId, Alliance alliance)
        {
            if (!TryGetAirport(airportId, out var airport))
                return false;

            return gameManager.tileSystem.TryGetLand(airport.TileId, out var landTile)
                   && landTile.Controller == alliance;
        }

        public bool CanConductAirOperations(
            Guid airportId,
            Alliance alliance)
        {
            return TryGetAirport(airportId, out var airport)
                   && AirportOperationsRules.IsOperational(airport)
                   && IsAirportControlledBy(airportId, alliance);
        }

        public AirportOperationsSnapshot CreateSnapshot(
            Guid airportId,
            DateTime at,
            IEnumerable<AirPackage> packages)
        {
            if (!TryGetAirport(airportId, out var airport))
                return default;

            var windowStart = AirportOperationsRules.GetWindowStart(at);
            var occupancy = BuildOccupancy(packages);
            occupancy.TryGetValue(
                new ScheduleKey(airportId, windowStart),
                out var reservedSlots);
            return new AirportOperationsSnapshot(
                airport.MaximumRunwayDamage,
                airport.RunwayDamage,
                airport.RunwayChannels
                    .OrderBy(channel => channel.ChannelIndex)
                    .Select(channel => channel.DamageLevel)
                    .ToList(),
                AirportOperationsRules.GetNominalCapacityChannels(airport),
                AirportOperationsRules.GetEffectiveCapacityChannels(airport),
                AirportOperationsRules.GetAircraftMovementCapacity(airport),
                windowStart,
                reservedSlots);
        }

        public bool TryFindFeasibleShift(
            AirPackage package,
            IEnumerable<AirPackage> existingPackages,
            DateTime latestEffectEnd,
            out TimeSpan shift,
            out string reason)
        {
            return TryFindFeasibleShift(
                package,
                existingPackages,
                latestEffectEnd,
                null,
                null,
                out shift,
                out reason);
        }

        internal bool TryFindFeasibleShift(
            AirPackage package,
            IEnumerable<AirPackage> existingPackages,
            DateTime latestEffectEnd,
            IEnumerable<TimeSpan> additionalCandidates,
            Func<TimeSpan, bool> additionalConstraint,
            out TimeSpan shift,
            out string reason)
        {
            shift = TimeSpan.Zero;
            reason = string.Empty;
            if (package == null || package.Flights.Count == 0)
            {
                reason = "A package with flights is required for airport scheduling.";
                return false;
            }

            var maximumShift = latestEffectEnd - package.EffectEnd;
            if (maximumShift < TimeSpan.Zero)
            {
                reason =
                    "The proposed package already ends after its requested effect window.";
                return false;
            }

            foreach (var candidate in GetFeasibleShiftCandidates(
                         package,
                         maximumShift,
                         additionalCandidates))
            {
                if (CanSchedulePackage(
                        package,
                        existingPackages,
                        candidate,
                        out reason))
                {
                    if (additionalConstraint == null
                        || additionalConstraint(candidate))
                    {
                        shift = candidate;
                        return true;
                    }
                }
            }

            reason =
                "No compatible runway-capacity window is available before the requested effect ends. "
                + reason;
            return false;
        }

        private IEnumerable<TimeSpan> GetFeasibleShiftCandidates(
            AirPackage package,
            TimeSpan maximumShift,
            IEnumerable<TimeSpan> additionalCandidates)
        {
            var candidates = new SortedSet<TimeSpan>
            {
                TimeSpan.Zero
            };
            foreach (var candidate in additionalCandidates
                         ?? Array.Empty<TimeSpan>())
            {
                if (candidate >= TimeSpan.Zero
                    && candidate <= maximumShift)
                {
                    candidates.Add(candidate);
                }
            }

            foreach (var movement in GetMovements(
                         package,
                         TimeSpan.Zero))
            {
                var nextWindow =
                    AirportOperationsRules.GetWindowStart(movement.At)
                    + AirportOperationsRules.MovementWindow;
                var candidate = nextWindow - movement.At;
                while (candidate <= maximumShift)
                {
                    candidates.Add(candidate);
                    candidate += AirportOperationsRules.MovementWindow;
                }
            }

            return candidates;
        }

        public bool CanSchedulePackage(
            AirPackage package,
            IEnumerable<AirPackage> existingPackages,
            TimeSpan shift,
            out string reason)
        {
            reason = string.Empty;
            if (package == null || package.Flights.Count == 0)
            {
                reason = "A package with flights is required for airport scheduling.";
                return false;
            }
            if (shift < TimeSpan.Zero)
            {
                reason = "Airport scheduling cannot move a package earlier.";
                return false;
            }

            var movements = GetMovements(package, shift).ToList();
            foreach (var movement in movements)
            {
                if (CanConductAirOperations(
                        movement.AirportId,
                        package.Alliance))
                {
                    continue;
                }

                reason =
                    $"Airport {ShortId(movement.AirportId)} is not open for "
                    + $"{package.Alliance} air operations.";
                return false;
            }

            var occupancy = BuildOccupancy(existingPackages);
            foreach (var demand in ExpandDemands(movements))
            {
                if (!TryGetAirport(demand.Key.AirportId, out var airport))
                {
                    reason =
                        $"Airport {ShortId(demand.Key.AirportId)} is unavailable.";
                    return false;
                }

                var capacity =
                    AirportOperationsRules.GetEffectiveCapacityChannels(airport);
                occupancy.TryGetValue(demand.Key, out var reserved);
                if (reserved + demand.ChannelSlots > capacity)
                {
                    reason =
                        $"Airport {ShortId(demand.Key.AirportId)} has "
                        + $"{reserved}/{capacity} runway-capacity channels reserved "
                        + $"for the {demand.Key.WindowStart:yyyy-MM-dd HH:mm} window.";
                    return false;
                }

                occupancy[demand.Key] = reserved + demand.ChannelSlots;
            }

            return true;
        }

        public IReadOnlyList<AirPackage> FindInvalidGroundedPackages(
            IEnumerable<AirPackage> packages)
        {
            var live = (packages ?? Array.Empty<AirPackage>())
                .Where(package =>
                    package != null
                    && package.Flights.Any(flight => !flight.HasPhysicallyEnded))
                .ToList();
            var accepted = live
                .Where(package => package.Flights.Any(flight => flight.IsAirborne))
                .OrderBy(package => package.EarliestTakeoffTime)
                .ThenBy(package => package.PackageId)
                .ToList();
            var grounded = live
                .Where(package => package.Flights.All(flight =>
                    flight.ExecutionPhase == FlightExecutionPhase.AwaitingTakeoff))
                .OrderBy(package => package.EarliestTakeoffTime)
                .ThenBy(package => package.PackageId)
                .ToList();
            var invalid = new List<AirPackage>();

            foreach (var package in grounded)
            {
                if (!CanSchedulePackage(
                        package,
                        accepted,
                        TimeSpan.Zero,
                        out _))
                {
                    invalid.Add(package);
                    continue;
                }

                accepted.Add(package);
            }

            return invalid;
        }

        public bool HasUnusablePendingLaunch(
            AirPackage package,
            out Guid airportId)
        {
            airportId = Guid.Empty;
            if (package == null)
                return false;

            foreach (var flight in package.Flights.Where(flight =>
                         flight.ExecutionPhase
                         == FlightExecutionPhase.AwaitingTakeoff
                         && !flight.HasPhysicallyEnded))
            {
                if (CanConductAirOperations(
                        flight.LaunchAirportBuildingId,
                        package.Alliance))
                {
                    continue;
                }

                airportId = flight.LaunchAirportBuildingId;
                return true;
            }

            return false;
        }

        private Dictionary<ScheduleKey, int> BuildOccupancy(
            IEnumerable<AirPackage> packages)
        {
            var occupancy = new Dictionary<ScheduleKey, int>();
            var movements = (packages ?? Array.Empty<AirPackage>())
                .Where(package => package != null)
                .SelectMany(package => GetMovements(package, TimeSpan.Zero));
            foreach (var demand in ExpandDemands(movements))
            {
                occupancy.TryGetValue(demand.Key, out var reserved);
                occupancy[demand.Key] = reserved + demand.ChannelSlots;
            }

            return occupancy;
        }

        private IEnumerable<Movement> GetMovements(
            AirPackage package,
            TimeSpan shift)
        {
            foreach (var flight in package.Flights)
            {
                if (flight == null
                    || flight.HasPhysicallyEnded
                    || flight.AircraftIds.Count <= 0
                    || flight.Route.Count == 0)
                {
                    continue;
                }

                if (flight.ExecutionPhase
                    == FlightExecutionPhase.AwaitingTakeoff)
                {
                    yield return new Movement(
                        flight.LaunchAirportBuildingId,
                        flight.PlannedTakeoffTime + shift,
                        flight.AircraftIds.Count);
                }

                var landing = flight.Route.LastOrDefault(waypoint =>
                    waypoint.Action == AirWaypointAction.Land);
                if (landing != null)
                {
                    yield return new Movement(
                        landing.AirportBuildingId,
                        landing.PlannedArrivalTime + shift,
                        flight.AircraftIds.Count);
                }
            }
        }

        private IEnumerable<ScheduleDemand> ExpandDemands(
            IEnumerable<Movement> movements)
        {
            foreach (var movement in movements)
            {
                if (!TryGetAirport(movement.AirportId, out var airport))
                    continue;

                var channels =
                    AirportOperationsRules.GetEffectiveCapacityChannels(airport);
                if (channels <= 0)
                    continue;

                var remaining =
                    AirportOperationsRules.GetRequiredChannelSlots(
                        movement.AircraftCount);
                var window = AirportOperationsRules.GetWindowStart(
                    movement.At);
                while (remaining > 0)
                {
                    var slots = Math.Min(channels, remaining);
                    yield return new ScheduleDemand(
                        new ScheduleKey(movement.AirportId, window),
                        slots);
                    remaining -= slots;
                    window += AirportOperationsRules.MovementWindow;
                }
            }
        }

        private static string ShortId(Guid id)
        {
            return id == Guid.Empty
                ? "none"
                : id.ToString("N").Substring(0, 8);
        }

        private readonly struct Movement
        {
            public readonly Guid AirportId;
            public readonly DateTime At;
            public readonly int AircraftCount;

            public Movement(
                Guid airportId,
                DateTime at,
                int aircraftCount)
            {
                AirportId = airportId;
                At = at;
                AircraftCount = Math.Max(0, aircraftCount);
            }
        }

        private readonly struct ScheduleDemand
        {
            public readonly ScheduleKey Key;
            public readonly int ChannelSlots;

            public ScheduleDemand(
                ScheduleKey key,
                int channelSlots)
            {
                Key = key;
                ChannelSlots = Math.Max(0, channelSlots);
            }
        }

        private readonly struct ScheduleKey : IEquatable<ScheduleKey>
        {
            public readonly Guid AirportId;
            public readonly DateTime WindowStart;

            public ScheduleKey(
                Guid airportId,
                DateTime windowStart)
            {
                AirportId = airportId;
                WindowStart = windowStart;
            }

            public bool Equals(ScheduleKey other)
            {
                return AirportId == other.AirportId
                       && WindowStart == other.WindowStart;
            }

            public override bool Equals(object obj)
            {
                return obj is ScheduleKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (AirportId.GetHashCode() * 397)
                           ^ WindowStart.GetHashCode();
                }
            }
        }
    }
}
