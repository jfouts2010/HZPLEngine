using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using Models.Module;
using Monobehaviours.Singletons;
using UnityEngine;

namespace Engine.Models
{
    public sealed class IADSSystem
    {
        private readonly GameManager gameManager;
        private readonly AllianceIADS blueforIads;
        private readonly AllianceIADS redforIads;

        public IADSSystem(GameManager gameManager)
            : this(gameManager, new AllianceIADS(Alliance.Bluefor), new AllianceIADS(Alliance.Redfor))
        {
        }

        public IADSSystem(GameManager gameManager, AllianceIADS blueforIads, AllianceIADS redforIads)
        {
            this.gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            this.blueforIads = blueforIads ?? new AllianceIADS(Alliance.Bluefor);
            this.redforIads = redforIads ?? new AllianceIADS(Alliance.Redfor);
            this.blueforIads.Alliance = Alliance.Bluefor;
            this.redforIads.Alliance = Alliance.Redfor;
        }

        public AllianceIADS GetAllianceIADS(Alliance alliance)
        {
            return alliance switch
            {
                Alliance.Bluefor => blueforIads,
                Alliance.Redfor => redforIads,
                _ => null
            };
        }

        public void TacticalTurn()
        {
            var activeModule = ModuleSingleton.Instance.ActiveModule;
            var aircraftTypeDefinitions = activeModule.AircraftTypeDefinitions
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            var radarDefinitionLookup = activeModule.SamComponentDefinitions
                .OfType<RadarAirDefenseComponentDefinition>()
                .ToDictionary(definition => definition.SamComponentDefinitionId);
            var airDefenseSites = gameManager.airDefenseSiteSystem
                .GetAirDefenseSites(gameManager.buildingSystem)
                .ToList();
            var tileDistanceKm = gameManager.SimulationSettings?.TileDistanceKM ?? 0f;
            var activeAircraft = GetActiveSortieAircraft().ToList();
            var aircraftAllianceById = BuildAircraftAllianceLookup(activeAircraft);
            Vector3Int? GetAirDefenseSiteTileId(IAirDefenseSite site)
            {
                return gameManager.airDefenseSiteSystem.TryGetTileId(
                    site,
                    gameManager.divisionSystem,
                    out var tileId)
                    ? tileId
                    : null;
            }

            blueforIads.RefreshTracks(
                activeAircraft,
                aircraftAllianceById,
                airDefenseSites,
                site => gameManager.airDefenseSiteSystem.GetEffectiveAlliance(site, GetCountryAlliance),
                GetAirDefenseSiteTileId,
                gameManager.airDefenseSiteSystem.GetAvailableComponents,
                radarDefinitionLookup,
                aircraftTypeDefinitions,
                tileDistanceKm);
            redforIads.RefreshTracks(
                activeAircraft,
                aircraftAllianceById,
                airDefenseSites,
                site => gameManager.airDefenseSiteSystem.GetEffectiveAlliance(site, GetCountryAlliance),
                GetAirDefenseSiteTileId,
                gameManager.airDefenseSiteSystem.GetAvailableComponents,
                radarDefinitionLookup,
                aircraftTypeDefinitions,
                tileDistanceKm);
        }

        private IEnumerable<CampaignAircraft> GetActiveSortieAircraft()
        {
            return (gameManager.squadronSystem.Squadrons ?? new List<Squadron>())
                .SelectMany(squadron => squadron?.Aircraft ?? new List<CampaignAircraft>())
                .Where(aircraft => aircraft != null
                                   && aircraft.IsActiveInSortie
                                   && aircraft.HasCurrentTileId
                                   && aircraft.Status != CampaignAircraftStatus.Lost);
        }

        private Dictionary<Guid, Alliance> BuildAircraftAllianceLookup(IEnumerable<CampaignAircraft> aircraft)
        {
            var squadronById = (gameManager.squadronSystem.Squadrons ?? new List<Squadron>())
                .Where(squadron => squadron != null)
                .GroupBy(squadron => squadron.SquadronId)
                .ToDictionary(group => group.Key, group => group.First());

            var allianceByAircraftId = new Dictionary<Guid, Alliance>();
            foreach (var campaignAircraft in aircraft ?? Enumerable.Empty<CampaignAircraft>())
            {
                if (campaignAircraft == null
                    || !squadronById.TryGetValue(campaignAircraft.SquadronId, out var squadron))
                    continue;

                allianceByAircraftId[campaignAircraft.AircraftId] = GetCountryAlliance(squadron.CountryId);
            }

            return allianceByAircraftId;
        }

        private Alliance GetCountryAlliance(Guid countryId)
        {
            var assignment = gameManager.CampaignTemplate?.CountryAllianceAssignments?
                .FirstOrDefault(candidate => candidate.CountryId == countryId);
            return assignment?.Alliance ?? Alliance.Neutral;
        }
    }
}
