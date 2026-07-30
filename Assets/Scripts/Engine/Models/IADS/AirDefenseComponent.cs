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
        public bool IsEmitting;
        public DateTime LastEmittedAt;
        public DateTime EmissionHoldUntil;

        public RadarAirDefenseComponent()
        {
        }

        public RadarAirDefenseComponent(RadarAirDefenseComponentDefinition definition)
            : base(definition.SamComponentDefinitionId)
        {
            IsEmitting = definition.SearchesWhileUnassigned;
        }

        public void UpdateEmission(bool shouldEmit, DateTime occurredAt)
        {
            IsEmitting = CanEmitAt(occurredAt) && shouldEmit;
            if (IsEmitting)
                LastEmittedAt = occurredAt;
        }

        public bool CanEmitAt(DateTime occurredAt)
        {
            return !IsDamaged && occurredAt >= EmissionHoldUntil;
        }

        public void HoldEmissionUntil(DateTime releaseAt)
        {
            if (releaseAt > EmissionHoldUntil)
                EmissionHoldUntil = releaseAt;
            IsEmitting = false;
        }

        public override void Damage()
        {
            base.Damage();
            IsEmitting = false;
        }
    }

    [Serializable]
    public sealed class LauncherAirDefenseComponent : AirDefenseComponent
    {
        public int ReadyRounds;
        public int ReserveRounds;
        public DateTime NextReloadAt;

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

        public bool TrySpendRound(
            LauncherAirDefenseComponentDefinition definition,
            DateTime occurredAt)
        {
            ReloadIfReady(definition, occurredAt);
            if (IsDamaged || ReadyRounds <= 0)
                return false;

            ReadyRounds--;
            if (ReadyRounds < definition.ReadyRoundCapacity
                && ReserveRounds > 0
                && NextReloadAt == default)
            {
                NextReloadAt = occurredAt.AddMinutes(definition.ReloadMinutes);
            }
            return true;
        }

        public void ReloadIfReady(
            LauncherAirDefenseComponentDefinition definition,
            DateTime occurredAt)
        {
            if (IsDamaged
                || ReserveRounds <= 0
                || ReadyRounds >= definition.ReadyRoundCapacity
                || NextReloadAt == default
                || occurredAt < NextReloadAt)
                return;

            ReadyRounds++;
            ReserveRounds--;
            NextReloadAt = ReadyRounds < definition.ReadyRoundCapacity
                           && ReserveRounds > 0
                ? occurredAt.AddMinutes(definition.ReloadMinutes)
                : default;
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
