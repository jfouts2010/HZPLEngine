using System;

namespace Models.Module
{
    public sealed class ModuleDefinition
    {
        public Guid Id { get; }
        public string DisplayName { get; }
        public string Name { get; }
        public string GameName { get; }
        public ISimAdapter SimAdapter { get; }
        public ModuleDefinition(
            Guid id,
            string displayName,
            string name,
            string gameName,
            ISimAdapter simAdapter = null)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("Module id is required.", nameof(id));

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id.ToString() : displayName.Trim();
            Name = string.IsNullOrWhiteSpace(name) ? DisplayName : name.Trim();
            GameName = string.IsNullOrWhiteSpace(gameName) ? DisplayName : gameName.Trim();
            SimAdapter = simAdapter ?? new NoOpSimAdapter();
        }
    }
}
