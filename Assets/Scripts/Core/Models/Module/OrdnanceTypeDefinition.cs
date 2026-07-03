using System;
using System.Collections.Generic;

namespace Models.Module
{
    public sealed class OrdnanceTypeDefinition
    {
        public Guid OrdnanceTypeDefinitionId { get; }
        public string Name { get; }
        public string ThirdPartyId { get; }
        public float Weight { get; }
        public int EffectPower { get; }
        public Dictionary<OrdnanceTargetCategory, float> EffectivenessByTargetCategory { get; }

        public OrdnanceTypeDefinition(
            Guid ordnanceTypeDefinitionId,
            string name,
            float weight,
            int effectPower,
            Dictionary<OrdnanceTargetCategory, float> effectivenessByTargetCategory = null,
            string thirdPartyId = "")
        {
            if (ordnanceTypeDefinitionId == Guid.Empty)
                throw new ArgumentException("Ordnance type definition id is required.", nameof(ordnanceTypeDefinitionId));

            OrdnanceTypeDefinitionId = ordnanceTypeDefinitionId;
            Name = string.IsNullOrWhiteSpace(name) ? ordnanceTypeDefinitionId.ToString() : name.Trim();
            ThirdPartyId = thirdPartyId;
            Weight = Math.Max(0f, weight);
            EffectPower = Math.Max(0, effectPower);
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
