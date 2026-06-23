using System;

namespace Models.Module
{
    public sealed class AircraftTypeDefinition
    {
        public Guid AircraftTypeDefinitionId { get; }
        public string Name { get; }
        public string ThirdPartyId { get; }

        public AircraftTypeDefinition(
            Guid aircraftTypeDefinitionId,
            string name,
            string thirdPartyId = "")
        {
            if (aircraftTypeDefinitionId == Guid.Empty)
                throw new ArgumentException("Aircraft type definition id is required.", nameof(aircraftTypeDefinitionId));

            AircraftTypeDefinitionId = aircraftTypeDefinitionId;
            Name = string.IsNullOrWhiteSpace(name) ? aircraftTypeDefinitionId.ToString() : name.Trim();
            ThirdPartyId = thirdPartyId ?? string.Empty;
        }
    }
}
