using System;
using System.Collections.Generic;
using System.Linq;

namespace Models.Module
{
    public sealed class AircraftCarriageOrdnanceDefinition
    {
        public Guid OrdnanceTypeDefinitionId { get; }
        public int Count { get; }

        public AircraftCarriageOrdnanceDefinition(
            Guid ordnanceTypeDefinitionId,
            int count)
        {
            if (ordnanceTypeDefinitionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Carriage ordnance id is required.",
                    nameof(ordnanceTypeDefinitionId));
            }
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            OrdnanceTypeDefinitionId = ordnanceTypeDefinitionId;
            Count = count;
        }
    }

    public sealed class AircraftCarriageConfigurationDefinition
    {
        public Guid AircraftCarriageConfigurationDefinitionId { get; }
        public string Name { get; }
        public string ThirdPartyId { get; }
        public float ExternalLoadCost { get; }
        public IReadOnlyList<AircraftCarriageOrdnanceDefinition> Contents { get; }

        public AircraftCarriageConfigurationDefinition(
            Guid aircraftCarriageConfigurationDefinitionId,
            string name,
            float externalLoadCost,
            IReadOnlyList<AircraftCarriageOrdnanceDefinition> contents,
            string thirdPartyId = "")
        {
            if (aircraftCarriageConfigurationDefinitionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Aircraft carriage configuration id is required.",
                    nameof(aircraftCarriageConfigurationDefinitionId));
            }
            if (contents == null || contents.Count == 0)
            {
                throw new ArgumentException(
                    "An aircraft carriage configuration requires contents.",
                    nameof(contents));
            }
            if (contents.Any(item => item == null))
            {
                throw new ArgumentException(
                    "Aircraft carriage configuration contents cannot contain null entries.",
                    nameof(contents));
            }

            var duplicateOrdnance = contents
                .GroupBy(item => item.OrdnanceTypeDefinitionId)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateOrdnance != null)
            {
                throw new ArgumentException(
                    "Carriage configuration contents must aggregate each ordnance type into one entry.",
                    nameof(contents));
            }

            AircraftCarriageConfigurationDefinitionId =
                aircraftCarriageConfigurationDefinitionId;
            Name = string.IsNullOrWhiteSpace(name)
                ? aircraftCarriageConfigurationDefinitionId.ToString()
                : name.Trim();
            ThirdPartyId = thirdPartyId ?? string.Empty;
            ExternalLoadCost = Math.Max(0f, externalLoadCost);
            Contents = contents.ToList();
        }
    }

    public sealed class AircraftLoadoutStationDefinition
    {
        public Guid AircraftLoadoutStationDefinitionId { get; }
        public string Name { get; }
        public string ThirdPartyId { get; }
        public int SortOrder { get; }
        public Guid MirrorStationDefinitionId { get; }
        public IReadOnlyList<Guid> CompatibleCarriageConfigurationDefinitionIds
        {
            get;
        }

        public AircraftLoadoutStationDefinition(
            Guid aircraftLoadoutStationDefinitionId,
            string name,
            int sortOrder,
            IReadOnlyList<Guid> compatibleCarriageConfigurationDefinitionIds,
            Guid mirrorStationDefinitionId = default,
            string thirdPartyId = "")
        {
            if (aircraftLoadoutStationDefinitionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Aircraft loadout station id is required.",
                    nameof(aircraftLoadoutStationDefinitionId));
            }
            if (compatibleCarriageConfigurationDefinitionIds == null)
            {
                throw new ArgumentNullException(
                    nameof(compatibleCarriageConfigurationDefinitionIds));
            }
            if (compatibleCarriageConfigurationDefinitionIds.Any(
                    id => id == Guid.Empty)
                || compatibleCarriageConfigurationDefinitionIds.Distinct().Count()
                != compatibleCarriageConfigurationDefinitionIds.Count)
            {
                throw new ArgumentException(
                    "Station carriage configuration ids must be non-empty and unique.",
                    nameof(compatibleCarriageConfigurationDefinitionIds));
            }

            AircraftLoadoutStationDefinitionId =
                aircraftLoadoutStationDefinitionId;
            Name = string.IsNullOrWhiteSpace(name)
                ? aircraftLoadoutStationDefinitionId.ToString()
                : name.Trim();
            ThirdPartyId = thirdPartyId ?? string.Empty;
            SortOrder = sortOrder;
            MirrorStationDefinitionId = mirrorStationDefinitionId;
            CompatibleCarriageConfigurationDefinitionIds =
                compatibleCarriageConfigurationDefinitionIds.ToList();
        }
    }
}
