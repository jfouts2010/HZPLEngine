using System;

namespace Models.Module
{
    [Serializable]
    public sealed class DivisionTemplateBattalion
    {
        public Guid BattalionDefinitionId;
        public int Count;

        public DivisionTemplateBattalion()
        {
        }

        public DivisionTemplateBattalion(Guid battalionDefinitionId, int count)
        {
            BattalionDefinitionId = battalionDefinitionId;
            Count = count;
        }
    }
}
