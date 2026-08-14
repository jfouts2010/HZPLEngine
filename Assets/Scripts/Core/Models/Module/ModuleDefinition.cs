using System;
using System.Collections.Generic;
using System.Linq;

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
            SimAdapter = simAdapter;
            Countries = countries;
            AircraftTypeDefinitions = aircraftTypeDefinitions;
            OrdnanceTypeDefinitions = ordnanceTypeDefinitions;
            SamComponentDefinitions = samComponentDefinitions;
            SamSiteTemplates = samSiteTemplates;
            BattalionDefinitions = battalionDefinitions;
            DivisionTemplates = divisionTemplates;
            ValidateAircraftLoadoutCatalog();
        }

        private void ValidateAircraftLoadoutCatalog()
        {
            if (AircraftTypeDefinitions == null
                || OrdnanceTypeDefinitions == null)
                return;

            var ordnanceById = OrdnanceTypeDefinitions.ToDictionary(
                definition => definition.OrdnanceTypeDefinitionId);
            foreach (var aircraftType in AircraftTypeDefinitions)
            {
                foreach (var content in aircraftType.CarriageConfigurations
                             .SelectMany(configuration => configuration.Contents))
                {
                    if (!ordnanceById.TryGetValue(
                            content.OrdnanceTypeDefinitionId,
                            out var ordnance))
                    {
                        throw new ArgumentException(
                            $"Aircraft type {aircraftType.AircraftTypeDefinitionId} carriage configuration references unknown ordnance {content.OrdnanceTypeDefinitionId}.");
                    }
                    if (ordnance.EmploymentCategory
                        == OrdnanceEmploymentCategory.Gun
                        || ordnance.EmploymentCategory
                        == OrdnanceEmploymentCategory.SurfaceToAir)
                    {
                        throw new ArgumentException(
                            $"Aircraft type {aircraftType.AircraftTypeDefinitionId} carriage configuration contains non-external ordnance {content.OrdnanceTypeDefinitionId}.");
                    }
                }

                if (aircraftType.InternalGunOrdnanceTypeDefinitionId == Guid.Empty)
                    continue;
                if (!ordnanceById.TryGetValue(
                        aircraftType.InternalGunOrdnanceTypeDefinitionId,
                        out var internalGun)
                    || internalGun.EmploymentCategory
                    != OrdnanceEmploymentCategory.Gun)
                {
                    throw new ArgumentException(
                        $"Aircraft type {aircraftType.AircraftTypeDefinitionId} has an invalid internal-gun ordnance reference.");
                }
            }
        }
    }
}
