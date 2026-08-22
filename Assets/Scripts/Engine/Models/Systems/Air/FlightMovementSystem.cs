using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Models
{
    /// <summary>
    /// Executes a previously selected flight command. It owns point-mass motion,
    /// fuel burn during that motion, and waypoint arrival detection. It does not
    /// choose tactical intent or mission behavior.
    /// </summary>
    internal sealed class FlightMovementSystem
    {
        private const double MaximumIntegrationStepSeconds = 1d;
        private const float WaypointCaptureFeet = 100f;

        private readonly Func<
            AirPackage,
            AirFlight,
            AircraftTypeDefinition,
            float> getGuidanceSpeedKnots;
        private readonly Action<AirPackage, AirFlight, DateTime> handleWaypoint;

        public FlightMovementSystem(
            Func<AirPackage, AirFlight, AircraftTypeDefinition, float>
                getGuidanceSpeedKnots,
            Action<AirPackage, AirFlight, DateTime> handleWaypoint)
        {
            this.getGuidanceSpeedKnots = getGuidanceSpeedKnots
                ?? throw new ArgumentNullException(
                    nameof(getGuidanceSpeedKnots));
            this.handleWaypoint = handleWaypoint
                                  ?? throw new ArgumentNullException(
                                      nameof(handleWaypoint));
        }

        public void Advance(
            AirPackage package,
            AirFlight flight,
            Squadron squadron,
            AircraftTypeDefinition aircraftType,
            AirCombatCommand command,
            DateTime intervalStart,
            double elapsedSeconds)
        {
            if (package == null
                || flight == null
                || squadron == null
                || aircraftType == null
                || command == null
                || !flight.IsAirborne)
                return;
            if (flight.IsWaitingAtRendezvous
                && command.Maneuver == AirCombatManeuver.FollowRoute)
                return;

            var remaining = elapsedSeconds;
            var localTime = intervalStart;
            HashSet<Guid> waypointsCrossedWithoutTime = null;
            while (remaining > 0.0001d
                   && flight.IsAirborne
                   && (!flight.IsWaitingAtRendezvous
                       || command.Maneuver
                       != AirCombatManeuver.FollowRoute))
            {
                var followingRoute = command.Maneuver
                                     == AirCombatManeuver.FollowRoute;
                var target = followingRoute
                    ? flight.CurrentWaypoint?.PositionFeet
                      ?? flight.PositionFeet
                    : command.AimPointFeet;
                var speedKnots = followingRoute
                    ? getGuidanceSpeedKnots(package, flight, aircraftType)
                    : Math.Max(1f, command.DesiredSpeedKnots);
                if (squadron.Aircraft.Any(aircraft =>
                        aircraft.AssignedFlightId == flight.FlightId
                        && aircraft.Status
                        == CampaignAircraftStatus.Damaged))
                {
                    speedKnots *=
                        WvrEngagementSystem.DamagedAircraftSpeedMultiplier;
                }

                var routeSegmentStart = default(Vector3);
                var hasRouteSegment = followingRoute
                                      && TryGetCurrentRouteSegmentStart(
                                          flight,
                                          out routeSegmentStart);
                if (followingRoute
                    && HasReached(
                        flight.PositionFeet,
                        target,
                        WaypointCaptureFeet))
                {
                    var maximumReachSeconds = Math.Min(
                        MaximumIntegrationStepSeconds,
                        remaining);
                    var reachSeconds = EstimateReachSeconds(
                        flight,
                        target,
                        aircraftType,
                        speedKnots,
                        maximumReachSeconds);
                    if (reachSeconds >= 0d)
                    {
                        var waypoint = flight.CurrentWaypoint;
                        if (waypoint != null
                            && !(waypointsCrossedWithoutTime
                                 ??= new HashSet<Guid>())
                            .Add(waypoint.WaypointId))
                        {
                            var progressSeconds = Math.Min(
                                MaximumIntegrationStepSeconds,
                                remaining);
                            remaining -= progressSeconds;
                            localTime = localTime.AddSeconds(
                                progressSeconds);
                            BurnFuel(
                                flight,
                                aircraftType,
                                command.Intent,
                                progressSeconds);
                            waypointsCrossedWithoutTime.Clear();
                            continue;
                        }

                        if (reachSeconds > 0.0001d)
                        {
                            IntegrateMotion(
                                flight,
                                target,
                                aircraftType,
                                command.Maneuver,
                                speedKnots,
                                reachSeconds);
                            remaining -= reachSeconds;
                            localTime = localTime.AddSeconds(reachSeconds);
                            BurnFuel(
                                flight,
                                aircraftType,
                                command.Intent,
                                reachSeconds);
                            waypointsCrossedWithoutTime?.Clear();
                        }
                        handleWaypoint(package, flight, localTime);
                        continue;
                    }
                }

                waypointsCrossedWithoutTime?.Clear();
                var step = Math.Min(
                    MaximumIntegrationStepSeconds,
                    remaining);
                var previous = flight.PositionFeet;
                IntegrateMotion(
                    flight,
                    target,
                    aircraftType,
                    command.Maneuver,
                    speedKnots,
                    step);
                remaining -= step;
                localTime = localTime.AddSeconds(step);
                BurnFuel(flight, aircraftType, command.Intent, step);

                if (!followingRoute
                    || !ShouldAdvanceRouteWaypoint(
                        hasRouteSegment ? routeSegmentStart : previous,
                        previous,
                        flight.PositionFeet,
                        target,
                        GetWaypointArrivalCorridorFeet(
                            aircraftType,
                            speedKnots)))
                    continue;

                handleWaypoint(package, flight, localTime);
            }
        }

        internal static void BurnFuel(
            AirFlight flight,
            AircraftTypeDefinition aircraftType,
            AirCombatIntent intent,
            double seconds)
        {
            var consumed = AirFuelRules.CalculateBurnFraction(
                aircraftType,
                intent,
                seconds);
            flight.TacticalState.FuelFraction = Mathf.Clamp01(
                flight.TacticalState.FuelFraction - consumed);
        }

        private static double EstimateReachSeconds(
            AirFlight flight,
            Vector3 target,
            AircraftTypeDefinition aircraftType,
            float speedKnots,
            double maximumStep)
        {
            var from = flight.PositionFeet;
            var horizontal = Vector2.Distance(
                new Vector2(from.x, from.z),
                new Vector2(target.x, target.z));
            var desiredHeading = HeadingTo(from, target);
            var headingDifference = Math.Abs(
                Mathf.DeltaAngle(
                    flight.HeadingDegrees,
                    desiredHeading));
            var feetPerSecond = speedKnots
                                * AirspaceGeometry.FeetPerNauticalMile
                                / 3600f;
            var altitudeDelta = target.y - from.y;
            var verticalRate = (altitudeDelta >= 0f
                                   ? aircraftType.ClimbRateFeetPerMinute
                                   : aircraftType.DescentRateFeetPerMinute)
                               / 60f;
            var horizontalSeconds = horizontal
                                    / Math.Max(1f, feetPerSecond);
            var verticalSeconds = Math.Abs(altitudeDelta)
                                  / Math.Max(1f, verticalRate);
            var needed = Math.Max(horizontalSeconds, verticalSeconds);
            return headingDifference <= 5f && needed <= maximumStep
                ? needed
                : -1d;
        }

        private static void IntegrateMotion(
            AirFlight flight,
            Vector3 target,
            AircraftTypeDefinition aircraftType,
            AirCombatManeuver maneuver,
            float speedKnots,
            double seconds)
        {
            var desiredHeading = HeadingTo(flight.PositionFeet, target);
            var heading = Mathf.MoveTowardsAngle(
                flight.HeadingDegrees,
                desiredHeading,
                GetManeuverTurnRateDegreesPerSecond(
                    aircraftType,
                    maneuver) * (float)seconds);
            var radians = heading * Mathf.Deg2Rad;
            var feetPerSecond = speedKnots
                                * AirspaceGeometry.FeetPerNauticalMile
                                / 3600f;
            var horizontalStep = feetPerSecond * (float)seconds;
            var position = flight.PositionFeet;
            var horizontalRemaining = Vector2.Distance(
                new Vector2(position.x, position.z),
                new Vector2(target.x, target.z));
            horizontalStep = Math.Min(
                horizontalStep,
                horizontalRemaining);
            position += new Vector3(
                Mathf.Sin(radians) * horizontalStep,
                0f,
                Mathf.Cos(radians) * horizontalStep);

            var verticalRate = (target.y >= position.y
                                   ? aircraftType.ClimbRateFeetPerMinute
                                   : aircraftType.DescentRateFeetPerMinute)
                               / 60f;
            position = new Vector3(
                position.x,
                Mathf.MoveTowards(
                    position.y,
                    target.y,
                    Math.Max(1f, verticalRate) * (float)seconds),
                position.z);
            flight.UpdateKinematics(position, heading, speedKnots);
        }

        internal static float GetManeuverTurnRateDegreesPerSecond(
            AircraftTypeDefinition aircraftType,
            AirCombatManeuver maneuver)
        {
            if (aircraftType == null)
                return 0f;

            return maneuver == AirCombatManeuver.BeamLeft
                   || maneuver == AirCombatManeuver.BeamRight
                   || maneuver == AirCombatManeuver.BreakLeft
                   || maneuver == AirCombatManeuver.BreakRight
                   || maneuver == AirCombatManeuver.Drag
                ? aircraftType.DefensiveTurnRateDegreesPerSecond
                : aircraftType.TurnRateDegreesPerSecond;
        }

        internal static bool ShouldAdvanceRouteWaypoint(
            Vector3 routeSegmentStart,
            Vector3 previous,
            Vector3 current,
            Vector3 target,
            float arrivalCorridorFeet)
        {
            if (Vector3.Distance(current, target) <= WaypointCaptureFeet)
                return true;

            var travel = current - previous;
            var travelMagnitudeSquared = travel.sqrMagnitude;
            if (travelMagnitudeSquared > 0.01f)
            {
                var previousToTarget = target - previous;
                var projection = Vector3.Dot(previousToTarget, travel)
                                 / travelMagnitudeSquared;
                if (projection >= 0f && projection <= 1f)
                {
                    var closest = previous + travel * projection;
                    if (Vector3.Distance(closest, target)
                        <= WaypointCaptureFeet)
                        return true;
                }
            }

            var routeLeg = target - routeSegmentStart;
            var routeLegMagnitudeSquared = routeLeg.sqrMagnitude;
            if (routeLegMagnitudeSquared <= 0.01f)
                return false;
            var progress = Vector3.Dot(
                               current - routeSegmentStart,
                               routeLeg)
                           / routeLegMagnitudeSquared;
            return progress >= 1f
                   && Vector3.Distance(current, target)
                   <= Math.Max(
                       WaypointCaptureFeet,
                       arrivalCorridorFeet);
        }

        private static bool HasReached(
            Vector3 current,
            Vector3 target,
            float captureFeet)
        {
            return Vector3.Distance(current, target) <= captureFeet;
        }

        private static float GetWaypointArrivalCorridorFeet(
            AircraftTypeDefinition aircraftType,
            float speedKnots)
        {
            var turnRadiusFeet = AirspaceGeometry.TurnRadiusFeet(
                speedKnots,
                aircraftType.TurnRateDegreesPerSecond);
            var integrationStepFeet = speedKnots
                                      * AirspaceGeometry.FeetPerNauticalMile
                                      / 3600f
                                      * (float)MaximumIntegrationStepSeconds;
            return Math.Max(
                WaypointCaptureFeet,
                turnRadiusFeet + integrationStepFeet);
        }

        private static bool TryGetCurrentRouteSegmentStart(
            AirFlight flight,
            out Vector3 segmentStart)
        {
            segmentStart = default;
            if (flight?.CurrentWaypoint == null)
                return false;

            var waypointIndex = flight.CurrentWaypointIndex;
            var route = flight.Route;
            if (flight.CurrentWaypoint.Action
                    == AirWaypointAction.StationEntry
                && flight.ExecutionPhase
                == FlightExecutionPhase.Executing)
            {
                var repeatEndpoint = route.FirstOrDefault(waypoint =>
                    waypoint.HasRepeat
                    && waypoint.RepeatFromWaypointId
                    == flight.CurrentWaypoint.WaypointId);
                if (repeatEndpoint == null)
                    return false;
                segmentStart = repeatEndpoint.PositionFeet;
                return true;
            }
            if (waypointIndex <= 0 || waypointIndex >= route.Count)
                return false;

            segmentStart = route[waypointIndex - 1].PositionFeet;
            return true;
        }

        private static float HeadingTo(Vector3 from, Vector3 to)
        {
            return Mathf.Atan2(to.x - from.x, to.z - from.z)
                   * Mathf.Rad2Deg;
        }
    }
}
