using System;

namespace Models.Module
{
    public sealed class CountryDefinition
    {
        public Guid CountryId { get; }
        public string Name { get; }

        public CountryDefinition(Guid countryId, string name)
        {
            if (countryId == Guid.Empty)
                throw new ArgumentException("Country id is required.", nameof(countryId));

            CountryId = countryId;
            Name = string.IsNullOrWhiteSpace(name) ? countryId.ToString() : name.Trim();
        }
    }
}
