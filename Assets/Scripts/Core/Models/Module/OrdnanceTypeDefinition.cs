using System;
using System.Collections.Generic;

namespace Models.Module
{
    public enum OrdnanceEmploymentCategory
    {
        None = 0,
        AirToAirRadar = 1,
        AirToAirInfrared = 2,
        AntiRadiation = 3,
        AirToGroundPrecision = 4,
        AirToGroundUnguided = 5
    }

    public sealed class OrdnanceTypeDefinition
    {
        public Guid OrdnanceTypeDefinitionId { get; }
        public string Name { get; }
        public string ThirdPartyId { get; }
        public float Weight { get; }
        public int EffectPower { get; }
        public OrdnanceEmploymentCategory EmploymentCategory { get; }
        public Dictionary<OrdnanceTargetCategory, float> EffectivenessByTargetCategory { get; }

        public OrdnanceTypeDefinition(
            Guid ordnanceTypeDefinitionId,
            string name,
            float weight,
            int effectPower,
            Dictionary<OrdnanceTargetCategory, float> effectivenessByTargetCategory = null,
            OrdnanceEmploymentCategory employmentCategory = OrdnanceEmploymentCategory.None,
            string thirdPartyId = "")
        {
            if (ordnanceTypeDefinitionId == Guid.Empty)
                throw new ArgumentException("Ordnance type definition id is required.", nameof(ordnanceTypeDefinitionId));

            OrdnanceTypeDefinitionId = ordnanceTypeDefinitionId;
            Name = string.IsNullOrWhiteSpace(name) ? ordnanceTypeDefinitionId.ToString() : name.Trim();
            ThirdPartyId = thirdPartyId;
            Weight = Math.Max(0f, weight);
            EffectPower = Math.Max(0, effectPower);
            EmploymentCategory = employmentCategory;
            EffectivenessByTargetCategory = ClampEffectiveness(effectivenessByTargetCategory);
        }

        public float GetEffectiveness(OrdnanceTargetCategory targetCategory)
        {
            return EffectivenessByTargetCategory.TryGetValue(targetCategory, out var effectiveness)
                ? effectiveness
                : 0f;
        }

        private static Dictionary<OrdnanceTargetCategory, float> ClampEffectiveness(
            Dictionary<OrdnanceTargetCategory, float> effectivenessByTargetCategory)
        {
            var clamped = new Dictionary<OrdnanceTargetCategory, float>();
            if (effectivenessByTargetCategory == null)
                return clamped;

            foreach (var rating in effectivenessByTargetCategory)
                clamped[rating.Key] = Math.Max(0f, Math.Min(1f, rating.Value));

            return clamped;
        }
    }
}
