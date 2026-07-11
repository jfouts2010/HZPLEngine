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
        AirToGroundUnguided = 5,
        SurfaceToAir = 6
    }

    public enum OrdnanceGuidanceMode
    {
        None = 0,
        Infrared = 1,
        Radar = 2,
        Gps = 3,
        Laser = 4,
        Imaging = 5,
        AntiRadiation = 6,
        ActiveRadar = 7,
        SemiActiveRadar = 8
    }

    public sealed class OrdnanceTypeDefinition
    {
        public Guid OrdnanceTypeDefinitionId { get; }
        public string Name { get; }
        public string ThirdPartyId { get; }
        public float Weight { get; }
        public int EffectPower { get; }
        public OrdnanceEmploymentCategory EmploymentCategory { get; }
        public OrdnanceGuidanceMode GuidanceMode { get; }
        public float MinimumRangeKm { get; }
        public float MaximumRangeKm { get; }
        public float MinimumTargetAltitudeFeet { get; }
        public float MaximumTargetAltitudeFeet { get; }
        public float PreparationSeconds { get; }
        public float EffectSpeedKnots { get; }
        public float HitProbability { get; }
        public float MaximumLaunchOffBoresightDegrees { get; }
        public float NoEscapeRangeFraction { get; }
        public float SecondsUntilAutonomous { get; }
        public bool RequiresSupportUntilAutonomous { get; }
        public float CountermeasureResistance { get; }
        public float TerminalLethality { get; }
        public float MaximumSupportAngleDegrees { get; }
        public Dictionary<OrdnanceTargetCategory, float> EffectivenessByTargetCategory { get; }

        public OrdnanceTypeDefinition(
            Guid ordnanceTypeDefinitionId,
            string name,
            float weight,
            int effectPower,
            Dictionary<OrdnanceTargetCategory, float> effectivenessByTargetCategory = null,
            OrdnanceEmploymentCategory employmentCategory = OrdnanceEmploymentCategory.None,
            string thirdPartyId = "",
            OrdnanceGuidanceMode guidanceMode = OrdnanceGuidanceMode.None,
            float minimumRangeKm = 0f,
            float maximumRangeKm = 0f,
            float minimumTargetAltitudeFeet = 0f,
            float maximumTargetAltitudeFeet = float.MaxValue,
            float preparationSeconds = 0f,
            float effectSpeedKnots = 1f,
            float hitProbability = 1f,
            float maximumLaunchOffBoresightDegrees = 60f,
            float noEscapeRangeFraction = 0.55f,
            float secondsUntilAutonomous = 0f,
            bool requiresSupportUntilAutonomous = false,
            float countermeasureResistance = 0.5f,
            float terminalLethality = 1f,
            float maximumSupportAngleDegrees = 70f)
        {
            if (ordnanceTypeDefinitionId == Guid.Empty)
                throw new ArgumentException("Ordnance type definition id is required.", nameof(ordnanceTypeDefinitionId));

            OrdnanceTypeDefinitionId = ordnanceTypeDefinitionId;
            Name = string.IsNullOrWhiteSpace(name) ? ordnanceTypeDefinitionId.ToString() : name.Trim();
            ThirdPartyId = thirdPartyId;
            Weight = Math.Max(0f, weight);
            EffectPower = Math.Max(0, effectPower);
            EmploymentCategory = employmentCategory;
            GuidanceMode = guidanceMode;
            MinimumRangeKm = Math.Max(0f, minimumRangeKm);
            MaximumRangeKm = Math.Max(MinimumRangeKm, maximumRangeKm);
            MinimumTargetAltitudeFeet = Math.Max(0f, minimumTargetAltitudeFeet);
            MaximumTargetAltitudeFeet = Math.Max(
                MinimumTargetAltitudeFeet,
                maximumTargetAltitudeFeet);
            PreparationSeconds = Math.Max(0f, preparationSeconds);
            EffectSpeedKnots = Math.Max(1f, effectSpeedKnots);
            HitProbability = Math.Max(0f, Math.Min(1f, hitProbability));
            MaximumLaunchOffBoresightDegrees = Math.Max(
                0f,
                Math.Min(180f, maximumLaunchOffBoresightDegrees));
            NoEscapeRangeFraction = Math.Max(0.05f, Math.Min(1f, noEscapeRangeFraction));
            SecondsUntilAutonomous = Math.Max(0f, secondsUntilAutonomous);
            RequiresSupportUntilAutonomous = requiresSupportUntilAutonomous;
            CountermeasureResistance = Math.Max(0f, Math.Min(1f, countermeasureResistance));
            TerminalLethality = Math.Max(0f, Math.Min(1f, terminalLethality));
            MaximumSupportAngleDegrees = Math.Max(
                0f,
                Math.Min(180f, maximumSupportAngleDegrees));
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
