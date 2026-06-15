using System;
using System.Collections.Generic;
using System.Linq;
using Models.Module;

namespace Monobehaviours.Singletons
{
    public class ModuleSingleton: Singleton<ModuleSingleton>
    {
        public const string StandaloneModuleId = "standalone";

        private static IReadOnlyList<ModuleDefinition> _modules;
        private static IReadOnlyList<ModuleDefinition> Modules => _modules ??= BuildModules();

        private ModuleDefinition _activeModule;
        private bool _hasActiveModuleSelection;

        public ModuleDefinition ActiveModule => _activeModule ??= GetDefaultModule();
        public bool HasActiveModuleSelection => _hasActiveModuleSelection;

        public IReadOnlyList<ModuleDefinition> GetAll()
        {
            return Modules;
        }

        public bool TryGetById(string moduleId, out ModuleDefinition module)
        {
            module = null;
            if (string.IsNullOrWhiteSpace(moduleId))
                return false;

            module = Modules.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, moduleId.Trim(), StringComparison.OrdinalIgnoreCase));
            return module != null;
        }

        public void SetActive(ModuleDefinition module)
        {
            _activeModule = module ?? throw new ArgumentNullException(nameof(module));
            _hasActiveModuleSelection = true;
        }

        public bool TrySetActive(string moduleId)
        {
            if (!TryGetById(moduleId, out var module))
                return false;

            SetActive(module);
            return true;
        }

        public void ResetToDefault()
        {
            _activeModule = GetDefaultModule();
            _hasActiveModuleSelection = false;
        }

        private ModuleDefinition GetDefaultModule()
        {
            if (TryGetById(StandaloneModuleId, out var module))
                return module;

            if (Modules.Count > 0)
                return Modules[0];

            throw new InvalidOperationException("No modules are registered.");
        }

        private static IReadOnlyList<ModuleDefinition> BuildModules()
        {
            return new List<ModuleDefinition>
            {
                //TestModule.GetTestModule()
            };
        }
    }
}
