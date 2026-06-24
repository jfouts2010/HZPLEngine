using System;
using System.Collections.Generic;

namespace Models.Module
{
    public sealed class SamSiteTemplate
    {
        public Guid SamSiteTemplateId { get; }
        public string Name { get; }
        public SamSiteHostConstraint HostConstraint { get; }
        public List<SamSiteTemplateComponent> Components { get; }

        public SamSiteTemplate(
            Guid samSiteTemplateId,
            string name,
            SamSiteHostConstraint hostConstraint,
            List<SamSiteTemplateComponent> components = null)
        {
            if (samSiteTemplateId == Guid.Empty)
                throw new ArgumentException("SAM site template id is required.", nameof(samSiteTemplateId));

            SamSiteTemplateId = samSiteTemplateId;
            Name = string.IsNullOrWhiteSpace(name) ? samSiteTemplateId.ToString() : name.Trim();
            HostConstraint = hostConstraint;
            Components = components ?? new List<SamSiteTemplateComponent>();
        }
    }
    public sealed class SamSiteTemplateComponent
    {
        public Guid SamComponentDefinitionId { get; }
        public int Count { get; }

        public SamSiteTemplateComponent(Guid samComponentDefinitionId, int count)
        {
            if (samComponentDefinitionId == Guid.Empty)
                throw new ArgumentException("SAM component definition id is required.", nameof(samComponentDefinitionId));

            SamComponentDefinitionId = samComponentDefinitionId;
            Count = Math.Max(0, count);
        }
    }
    public enum SamSiteHostConstraint
    {
        StaticOnly,
        MobileOnly
    }
}
