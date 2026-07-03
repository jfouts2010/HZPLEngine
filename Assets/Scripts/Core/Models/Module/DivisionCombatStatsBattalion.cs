using System;

namespace Models.Module
{
    public sealed class DivisionCombatStatsBattalion
    {
        public BattalionDefinition BattalionDefinition { get; }
        public int Count { get; }

        public DivisionCombatStatsBattalion(BattalionDefinition battalionDefinition, int count)
        {
            BattalionDefinition = battalionDefinition;
            Count = count;
        }
    }
}
