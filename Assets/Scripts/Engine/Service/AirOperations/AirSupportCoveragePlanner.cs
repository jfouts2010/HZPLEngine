using System;
using System.Collections.Generic;
using System.Linq;
using Models.Gameplay.Campaign;

namespace Engine.Service
{
    public static class AirSupportCoveragePlanner
    {
        public static IReadOnlyList<AirSupportReservation> PlanContinuousCoverage(
            IEnumerable<AirFlight> candidateFlights,
            Guid consumingPackageId,
            int requiredSlots,
            DateTime start,
            DateTime requestedEnd,
            out DateTime coveredUntil)
        {
            coveredUntil = start;
            if (consumingPackageId == Guid.Empty
                || requiredSlots <= 0
                || requestedEnd <= start)
                return Array.Empty<AirSupportReservation>();

            var candidates = candidateFlights?
                .Where(flight => flight != null
                                 && !flight.IsTerminal
                                 && flight.ExecutionPhase
                                 != FlightExecutionPhase.Returning
                                 && flight.ExecutionPhase
                                 != FlightExecutionPhase.Landing
                                 && flight.ExecutionPhase
                                 != FlightExecutionPhase.Ended
                                 && flight.ProvidedSupportSlots >= requiredSlots
                                 && flight.EffectEnd > start
                                 && flight.EffectStart < requestedEnd)
                .GroupBy(flight => flight.FlightId)
                .Select(group => group.First())
                .ToList()
                ?? new List<AirFlight>();
            var planned = new List<AirSupportReservation>();
            var cursor = start;

            while (cursor < requestedEnd)
            {
                var best = candidates
                    .Where(flight => flight.EffectStart <= cursor
                                     && flight.EffectEnd > cursor)
                    .Select(flight => new
                    {
                        Flight = flight,
                        AvailableUntil = GetContinuousAvailabilityEnd(
                            flight,
                            requiredSlots,
                            cursor,
                            requestedEnd)
                    })
                    .Where(candidate => candidate.AvailableUntil > cursor)
                    .OrderByDescending(candidate => candidate.AvailableUntil)
                    .ThenBy(candidate => candidate.Flight.EffectStart)
                    .ThenBy(candidate => candidate.Flight.FlightId)
                    .FirstOrDefault();
                if (best == null)
                    break;

                planned.Add(new AirSupportReservation
                {
                    SupportingFlightId = best.Flight.FlightId,
                    ConsumingPackageId = consumingPackageId,
                    SlotCount = requiredSlots,
                    StartTime = cursor,
                    EndTime = best.AvailableUntil
                });
                cursor = best.AvailableUntil;
            }

            coveredUntil = cursor;
            return planned;
        }

        public static int GetMinimumAvailableSlots(
            AirFlight supportingFlight,
            DateTime start,
            DateTime end)
        {
            if (supportingFlight == null
                || supportingFlight.ProvidedSupportSlots <= 0
                || end <= start)
                return 0;

            var eventTimes = new SortedSet<DateTime> { start };
            foreach (var reservation in supportingFlight.SupportReservations)
            {
                if (reservation.StartTime > start && reservation.StartTime < end)
                    eventTimes.Add(reservation.StartTime);
                if (reservation.EndTime > start && reservation.EndTime < end)
                    eventTimes.Add(reservation.EndTime);
            }

            return eventTimes
                .Select(time => supportingFlight.ProvidedSupportSlots
                                - supportingFlight.SupportReservations
                                    .Where(reservation =>
                                        reservation.StartTime <= time
                                        && reservation.EndTime > time)
                                    .Sum(reservation => Math.Max(
                                        0,
                                        reservation.SlotCount)))
                .DefaultIfEmpty(supportingFlight.ProvidedSupportSlots)
                .Min();
        }

        public static bool HasContinuousReservedCoverage(
            IEnumerable<AirFlight> supportingFlights,
            Guid consumingPackageId,
            int requiredSlots,
            DateTime start,
            DateTime end)
        {
            if (consumingPackageId == Guid.Empty
                || requiredSlots <= 0
                || end <= start)
                return false;

            var providers = supportingFlights?
                .Where(flight => flight != null
                                 && !flight.IsTerminal
                                 && flight.ExecutionPhase
                                 != FlightExecutionPhase.Returning
                                 && flight.ExecutionPhase
                                 != FlightExecutionPhase.Landing
                                 && flight.ExecutionPhase
                                 != FlightExecutionPhase.Ended)
                .GroupBy(flight => flight.FlightId)
                .Select(group => group.First())
                .ToList()
                ?? new List<AirFlight>();
            var cursor = start;
            while (cursor < end)
            {
                var coveredUntil = providers
                    .Where(flight => flight.EffectStart <= cursor
                                     && flight.EffectEnd > cursor)
                    .SelectMany(flight => flight.SupportReservations
                        .Where(reservation =>
                            reservation.SupportingFlightId == flight.FlightId
                            && reservation.ConsumingPackageId
                            == consumingPackageId
                            && reservation.SlotCount >= requiredSlots
                            && reservation.StartTime <= cursor
                            && reservation.EndTime > cursor
                            && HasCapacityForReservation(
                                flight,
                                reservation,
                                cursor))
                        .Select(reservation =>
                            GetCapacitySegmentEnd(
                                flight,
                                reservation,
                                cursor)))
                    .DefaultIfEmpty(cursor)
                    .Max();
                if (coveredUntil <= cursor)
                    return false;

                cursor = coveredUntil < end ? coveredUntil : end;
            }

            return true;
        }

        public static bool HasCapacityForReservation(
            AirFlight supportingFlight,
            AirSupportReservation targetReservation,
            DateTime time)
        {
            if (supportingFlight == null
                || targetReservation == null
                || supportingFlight.ProvidedSupportSlots <= 0
                || targetReservation.SlotCount <= 0
                || targetReservation.SupportingFlightId
                != supportingFlight.FlightId
                || targetReservation.StartTime > time
                || targetReservation.EndTime <= time)
                return false;

            var activeReservations = supportingFlight.SupportReservations
                .Where(candidate => candidate.StartTime <= time
                                    && candidate.EndTime > time
                                    && candidate.SlotCount > 0)
                .ToList();
            return activeReservations.Any(reservation =>
                       ReferenceEquals(reservation, targetReservation))
                   && activeReservations.Sum(reservation =>
                       (long)reservation.SlotCount)
                   <= supportingFlight.ProvidedSupportSlots;
        }

        private static DateTime GetCapacitySegmentEnd(
            AirFlight supportingFlight,
            AirSupportReservation targetReservation,
            DateTime start)
        {
            var end = targetReservation.EndTime < supportingFlight.EffectEnd
                ? targetReservation.EndTime
                : supportingFlight.EffectEnd;
            foreach (var reservation in supportingFlight.SupportReservations)
            {
                if (reservation.StartTime > start
                    && reservation.StartTime < end)
                    end = reservation.StartTime;
                if (reservation.EndTime > start
                    && reservation.EndTime < end)
                    end = reservation.EndTime;
            }

            return end;
        }

        private static DateTime GetContinuousAvailabilityEnd(
            AirFlight supportingFlight,
            int requiredSlots,
            DateTime start,
            DateTime requestedEnd)
        {
            var limit = supportingFlight.EffectEnd < requestedEnd
                ? supportingFlight.EffectEnd
                : requestedEnd;
            if (limit <= start)
                return start;

            var eventTimes = new SortedSet<DateTime> { start, limit };
            foreach (var reservation in supportingFlight.SupportReservations)
            {
                if (reservation.StartTime > start && reservation.StartTime < limit)
                    eventTimes.Add(reservation.StartTime);
                if (reservation.EndTime > start && reservation.EndTime < limit)
                    eventTimes.Add(reservation.EndTime);
            }

            foreach (var time in eventTimes)
            {
                if (time >= limit)
                    break;
                var reserved = supportingFlight.SupportReservations
                    .Where(reservation => reservation.StartTime <= time
                                          && reservation.EndTime > time)
                    .Sum(reservation => Math.Max(0, reservation.SlotCount));
                if (supportingFlight.ProvidedSupportSlots - reserved < requiredSlots)
                    return time;
            }

            return limit;
        }
    }
}
