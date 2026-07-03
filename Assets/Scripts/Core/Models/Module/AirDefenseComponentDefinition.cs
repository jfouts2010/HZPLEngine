using System;

namespace Models.Module
{
    public abstract class AirDefenseComponentDefinition
    {
        public Guid SamComponentDefinitionId { get; }
        public string Name { get; }
        public string ThirdPartyId { get; }
        public OrdnanceTargetCategory TargetCategory { get; }
        public int TargetToughness { get; }

        protected AirDefenseComponentDefinition(
            Guid samComponentDefinitionId,
            string name,
            OrdnanceTargetCategory targetCategory,
            int targetToughness,
            string thirdPartyId = "")
        {
            if (samComponentDefinitionId == Guid.Empty)
                throw new ArgumentException("SAM component definition id is required.", nameof(samComponentDefinitionId));

            SamComponentDefinitionId = samComponentDefinitionId;
            Name = string.IsNullOrWhiteSpace(name) ? samComponentDefinitionId.ToString() : name.Trim();
            ThirdPartyId = thirdPartyId;
            TargetCategory = targetCategory;
            TargetToughness = Math.Max(1, targetToughness);
        }
    }

    public sealed class RadarAirDefenseComponentDefinition : AirDefenseComponentDefinition
    {
        public float DetectionRangeKm { get; }
        public float MaxAltitudeMeters { get; }
        public float TrackQuality { get; }
        public bool ProvidesWeaponQualityTrack { get; }
        
        public RadarAirDefenseComponentDefinition(
            Guid samComponentDefinitionId,
            string name,
            OrdnanceTargetCategory targetCategory,
            int targetToughness,
            float detectionRangeKm,
            float maxAltitudeMeters,
            float trackQuality,
            bool providesWeaponQualityTrack = false,
            string thirdPartyId = "")
            : base(samComponentDefinitionId, name, targetCategory, targetToughness, thirdPartyId)
        {
            DetectionRangeKm = Math.Max(0f, detectionRangeKm);
            MaxAltitudeMeters = Math.Max(0f, maxAltitudeMeters);
            TrackQuality = Math.Max(0f, Math.Min(1f, trackQuality));
            ProvidesWeaponQualityTrack = providesWeaponQualityTrack;
        }
    }

    public sealed class LauncherAirDefenseComponentDefinition : AirDefenseComponentDefinition
    {
        public float MinEngagementRangeKm { get; }
        public float MaxEngagementRangeKm { get; }
        public float MinEngagementAltitudeMeters { get; }
        public float MaxEngagementAltitudeMeters { get; }
        public int ReadyRoundCapacity { get; }
        public int ReserveRoundCapacity { get; }
        public float ReloadMinutes { get; }

        public LauncherAirDefenseComponentDefinition(
            Guid samComponentDefinitionId,
            string name,
            OrdnanceTargetCategory targetCategory,
            int targetToughness,
            float minEngagementRangeKm,
            float maxEngagementRangeKm,
            float minEngagementAltitudeMeters,
            float maxEngagementAltitudeMeters,
            int readyRoundCapacity,
            int reserveRoundCapacity,
            float reloadMinutes,
            string thirdPartyId = "")
            : base(samComponentDefinitionId, name, targetCategory, targetToughness, thirdPartyId)
        {
            MinEngagementRangeKm = Math.Max(0f, minEngagementRangeKm);
            MaxEngagementRangeKm = Math.Max(0f, maxEngagementRangeKm);
            MinEngagementAltitudeMeters = Math.Max(0f, minEngagementAltitudeMeters);
            MaxEngagementAltitudeMeters = Math.Max(0f, maxEngagementAltitudeMeters);
            ReadyRoundCapacity = Math.Max(0, readyRoundCapacity);
            ReserveRoundCapacity = Math.Max(0, reserveRoundCapacity);
            ReloadMinutes = Math.Max(0f, reloadMinutes);
        }
    }

    public sealed class CommandAirDefenseComponentDefinition : AirDefenseComponentDefinition
    {
        public CommandAirDefenseComponentDefinition(
            Guid samComponentDefinitionId,
            string name,
            OrdnanceTargetCategory targetCategory,
            int targetToughness,
            string thirdPartyId = "")
            : base(samComponentDefinitionId, name, targetCategory, targetToughness, thirdPartyId)
        {
        }
    }

    public sealed class SupportAirDefenseComponentDefinition : AirDefenseComponentDefinition
    {
        public SupportAirDefenseComponentDefinition(
            Guid samComponentDefinitionId,
            string name,
            OrdnanceTargetCategory targetCategory,
            int targetToughness,
            string thirdPartyId = "")
            : base(samComponentDefinitionId, name, targetCategory, targetToughness, thirdPartyId)
        {
        }
    }
}
