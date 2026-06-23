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

        public void RebuildIndex()
        {
            var squadrons = (Squadrons ?? new List<Squadron>())
                .Where(squadron => squadron != null)
                .ToList();

            squadronsById = squadrons
                .GroupBy(squadron => squadron.SquadronId)
                .ToDictionary(group => group.Key, group => group.First());

            squadronsByAirportBuildingId = squadrons
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
