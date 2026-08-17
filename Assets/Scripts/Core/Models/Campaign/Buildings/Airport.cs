using System;
using System.Collections.Generic;
using System.Linq;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AirportRunwayChannel
    {
        public const int MaximumDamageLevel = 5;

        public int ChannelIndex;
        public int DamageLevel;

        public bool IsOperational => DamageLevel <= 0;
        public bool IsSaturated =>
            DamageLevel >= MaximumDamageLevel;

        public void Normalize()
        {
            ChannelIndex = Math.Max(0, ChannelIndex);
            DamageLevel = Math.Max(
                0,
                Math.Min(MaximumDamageLevel, DamageLevel));
        }
    }

    [Serializable]
    public class Airport : Building
    {
        public override BuildingType Type
        {
            get { return BuildingType.Airport; }
        }

        public List<AirportRunwayChannel> RunwayChannels =
            new List<AirportRunwayChannel>();

        public int NominalRunwayChannelCount
        {
            get
            {
                EnsureRunwayChannels();
                return RunwayChannels.Count;
            }
        }

        public int OperationalRunwayChannelCount
        {
            get
            {
                EnsureRunwayChannels();
                return RunwayChannels.Count(channel => channel.IsOperational);
            }
        }

        public int RunwayDamage
        {
            get
            {
                EnsureRunwayChannels();
                return RunwayChannels.Sum(channel => channel.DamageLevel);
            }
        }

        public int MaximumRunwayDamage
        {
            get
            {
                EnsureRunwayChannels();
                return RunwayChannels.Count
                       * AirportRunwayChannel.MaximumDamageLevel;
            }
        }

        public bool IsRunwaySystemOperational =>
            OperationalRunwayChannelCount > 0;

        public Airport()
        {
        }

        public Airport(BuildingStartingCondition startingCondition) : base(startingCondition)
        {
            TargetToughness = 3;
            EnsureRunwayChannels();
        }

        public bool TryGetRunwayChannel(
            int channelIndex,
            out AirportRunwayChannel channel)
        {
            EnsureRunwayChannels();
            channel = RunwayChannels.FirstOrDefault(candidate =>
                candidate.ChannelIndex == channelIndex);
            return channel != null;
        }

        public bool ApplyRunwayDamage(int channelIndex, int damage = 1)
        {
            if (damage <= 0
                || !TryGetRunwayChannel(channelIndex, out var channel)
                || channel.IsSaturated)
                return false;

            channel.DamageLevel = Math.Min(
                AirportRunwayChannel.MaximumDamageLevel,
                channel.DamageLevel + damage);
            return true;
        }

        public void EnsureRunwayChannels()
        {
            RunwayChannels ??= new List<AirportRunwayChannel>();
            var buildLevel = Level?.BuildLevel ?? 0;
            var desiredCount = buildLevel <= 0
                ? 0
                : buildLevel >= 6
                    ? 2
                    : 1;
            foreach (var channel in RunwayChannels.Where(item => item != null))
                channel.Normalize();

            RunwayChannels = RunwayChannels
                .Where(item => item != null)
                .GroupBy(item => item.ChannelIndex)
                .Select(group => group.First())
                .Where(item => item.ChannelIndex < desiredCount)
                .OrderBy(item => item.ChannelIndex)
                .ToList();

            var migrateLegacyIntegrity = RunwayChannels.Count == 0
                                         && desiredCount > 0;
            for (var index = 0; index < desiredCount; index++)
            {
                if (RunwayChannels.All(item => item.ChannelIndex != index))
                {
                    RunwayChannels.Add(new AirportRunwayChannel
                    {
                        ChannelIndex = index
                    });
                }
            }
            RunwayChannels = RunwayChannels
                .OrderBy(item => item.ChannelIndex)
                .ToList();

            if (!migrateLegacyIntegrity)
                return;

            // Preserve the capacity of airports authored under the previous
            // integrity-fraction model. Old building damage did not represent
            // individual craters, so only translate capacity that was already
            // lost; do not turn every legacy damage point into runway damage.
            var legacyMaximum = Math.Max(0, buildLevel);
            var legacyFunctional = Math.Max(
                0,
                legacyMaximum - Math.Max(0, Level?.Damage ?? 0));
            var legacyEffectiveChannels = legacyFunctional <= 0
                || legacyMaximum <= 0
                    ? 0
                    : Math.Min(
                        desiredCount,
                        Math.Max(
                            1,
                            (int)Math.Ceiling(
                                desiredCount
                                * legacyFunctional
                                / (double)legacyMaximum)));
            var closedChannels = desiredCount - legacyEffectiveChannels;
            for (var index = 0; index < closedChannels; index++)
            {
                RunwayChannels[RunwayChannels.Count - 1 - index].DamageLevel = 1;
            }
        }
    }
}
