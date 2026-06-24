using System;
using System.Collections.Generic;

namespace Models.Module
{
    public sealed class ModuleDefinition
    {
        public Guid Id { get; }
        public string DisplayName { get; }
        public string Name { get; }
        public string GameName { get; }
        public ISimAdapter SimAdapter { get; }
        public List<CountryDefinition> Countries { get; }
        public List<AircraftTypeDefinition> AircraftTypeDefinitions { get; }
        public List<OrdnanceTypeDefinition> OrdnanceTypeDefinitions { get; }
        public List<AirDefenseComponentDefinition> SamComponentDefinitions { get; }
        public List<SamSiteTemplate> SamSiteTemplates { get; }
        public List<BattalionDefinition> BattalionDefinitions { get; }
        public List<DivisionTemplate> DivisionTemplates { get; }

        public ModuleDefinition(
            Guid id,
            string displayName,
            string name,
            string gameName,
            ISimAdapter simAdapter = null,
            List<CountryDefinition> countries = null,
            List<AircraftTypeDefinition> aircraftTypeDefinitions = null,
            List<OrdnanceTypeDefinition> ordnanceTypeDefinitions = null,
            List<AirDefenseComponentDefinition> samComponentDefinitions = null,
            List<SamSiteTemplate> samSiteTemplates = null,
            List<BattalionDefinition> battalionDefinitions = null,
            List<DivisionTemplate> divisionTemplates = null)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("Module id is required.", nameof(id));

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id.ToString() : displayName.Trim();
            Name = string.IsNullOrWhiteSpace(name) ? DisplayName : name.Trim();
            GameName = string.IsNullOrWhiteSpace(gameName) ? DisplayName : gameName.Trim();
            SimAdapter = simAdapter ?? new NoOpSimAdapter();
            Countries = countries ?? new List<CountryDefinition>();
            AircraftTypeDefinitions = aircraftTypeDefinitions ?? new List<AircraftTypeDefinition>();
            OrdnanceTypeDefinitions = ordnanceTypeDefinitions ?? new List<OrdnanceTypeDefinition>();
            SamComponentDefinitions = samComponentDefinitions ?? new List<AirDefenseComponentDefinition>();
            SamSiteTemplates = samSiteTemplates ?? new List<SamSiteTemplate>();
            BattalionDefinitions = battalionDefinitions ?? new List<BattalionDefinition>();
            DivisionTemplates = divisionTemplates ?? new List<DivisionTemplate>();
        }
    }
}
