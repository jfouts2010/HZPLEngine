using System;
using Models.Module;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public abstract class AirDefenseComponent
    {
        public Guid ComponentId = Guid.NewGuid();
        public Guid SamComponentDefinitionId;
        public bool IsDamaged;

        public AirDefenseComponent()
        {
        }

        protected AirDefenseComponent(Guid samComponentDefinitionId)
        {
            if (samComponentDefinitionId == Guid.Empty)
                throw new ArgumentException("SAM component definition id is required.", nameof(samComponentDefinitionId));

            SamComponentDefinitionId = samComponentDefinitionId;
        }

        public virtual void Damage()
        {
            IsDamaged = true;
        }
    }

    [Serializable]
    public sealed class RadarAirDefenseComponent : AirDefenseComponent
    {
        public RadarAirDefenseComponent()
        {
        }

        public RadarAirDefenseComponent(RadarAirDefenseComponentDefinition definition)
            : base(definition.SamComponentDefinitionId)
        {
        }
    }

    [Serializable]
    public sealed class LauncherAirDefenseComponent : AirDefenseComponent
    {
        public int ReadyRounds;
        public int ReserveRounds;

        public LauncherAirDefenseComponent()
        {
        }

        public LauncherAirDefenseComponent(LauncherAirDefenseComponentDefinition definition)
            : base(definition.SamComponentDefinitionId)
        {
            ReadyRounds = Math.Max(0, definition.ReadyRoundCapacity);
            ReserveRounds = Math.Max(0, definition.ReserveRoundCapacity);
        }

        public override void Damage()
        {
            base.Damage();
            ReadyRounds = 0;
            ReserveRounds = 0;
        }
    }

    [Serializable]
    public sealed class CommandAirDefenseComponent : AirDefenseComponent
    {
        public CommandAirDefenseComponent()
        {
        }

        public CommandAirDefenseComponent(CommandAirDefenseComponentDefinition definition)
            : base(definition.SamComponentDefinitionId)
        {
        }
    }

    [Serializable]
    public sealed class SupportAirDefenseComponent : AirDefenseComponent
    {
        public SupportAirDefenseComponent()
        {
        }

        public SupportAirDefenseComponent(SupportAirDefenseComponentDefinition definition)
            : base(definition.SamComponentDefinitionId)
        {
        }
    }

}
