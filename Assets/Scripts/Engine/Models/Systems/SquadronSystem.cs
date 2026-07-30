using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class SquadronSystem
    {
        [SerializeReference] public List<Squadron> Squadrons = new List<Squadron>();

        private Dictionary<Guid, Squadron> squadronsById;
        private Dictionary<Guid, List<Squadron>> squadronsByAirportBuildingId;

        public bool TryGetSquadron(Guid squadronId, out Squadron squadron)
        {
            EnsureIndex();
            return squadronsById.TryGetValue(squadronId, out squadron);
        }

        public List<Squadron> GetSquadronsAtAirport(Guid airportBuildingId)
        {
            EnsureIndex();
            return squadronsByAirportBuildingId.TryGetValue(airportBuildingId, out var squadrons)
                ? squadrons
                : new List<Squadron>();
        }

        public bool TryGetAircraft(
            Guid aircraftId,
            out Squadron squadron,
            out CampaignAircraft aircraft)
        {
            squadron = null;
            aircraft = null;
            if (aircraftId == Guid.Empty)
                return false;

            foreach (var candidateSquadron in Squadrons)
            {
                var candidate = candidateSquadron?.Aircraft?.FirstOrDefault(
                    item => item != null && item.AircraftId == aircraftId);
                if (candidate == null)
                    continue;

                squadron = candidateSquadron;
                aircraft = candidate;
                return true;
            }
            return false;
        }

        public bool ApplyGroundAttackDamage(Guid aircraftId, bool destroyed)
        {
            if (!TryGetAircraft(aircraftId, out _, out var aircraft)
                || aircraft.Status == CampaignAircraftStatus.Lost)
                return false;

            aircraft.Status = destroyed
                ? CampaignAircraftStatus.Lost
                : CampaignAircraftStatus.Damaged;
            if (destroyed)
            {
                aircraft.AssignedFlightId = Guid.Empty;
                aircraft.ClearLoadout();
            }
            return true;
        }

        public void RebuildIndex()
        {
            squadronsById = Squadrons
                .GroupBy(squadron => squadron.SquadronId)
                .ToDictionary(group => group.Key, group => group.First());

            squadronsByAirportBuildingId = Squadrons
                .GroupBy(squadron => squadron.AirportBuildingId)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        private void EnsureIndex()
        {
            if (squadronsById == null || squadronsByAirportBuildingId == null)
                RebuildIndex();
        }
    }
}
