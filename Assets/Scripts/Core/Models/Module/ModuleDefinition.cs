using System;
using System.Collections.Generic;
using System.Linq;
using Models.Gameplay.Campaign;

namespace Models.Module
{
    public sealed class ModuleDefinition
    {
        public ModuleDefinition(
            string id,
            string displayName,
            string name,
            string gameName,
            ISimAdapter simAdapter = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Module id is required.", nameof(id));

            Id = id.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName.Trim();
            Name = string.IsNullOrWhiteSpace(name) ? DisplayName : name.Trim();
            GameName = string.IsNullOrWhiteSpace(gameName) ? DisplayName : gameName.Trim();
            SimAdapter = simAdapter ?? new NoOpSimAdapter();
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Name { get; }
        public string GameName { get; }
        public ISimAdapter SimAdapter { get; }
    }
}
