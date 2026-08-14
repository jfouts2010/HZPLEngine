using System;
using System.Collections.Generic;
using System.Linq;
using Models.Module;

namespace Monobehaviours.Singletons
{
    public class ModuleSingleton: Singleton<ModuleSingleton>
    {
        public static readonly Guid StandaloneModuleId = Guid.Parse("92f96fd1-d2f1-4e28-a047-30b0940dc45f");

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

        public bool TryGetById(Guid moduleId, out ModuleDefinition module)
        {
            module = null;
            if (moduleId == Guid.Empty)
                return false;

            module = Modules.FirstOrDefault(candidate => candidate.Id == moduleId);
            return module != null;
        }

        public void SetActive(ModuleDefinition module)
        {
            _activeModule = module;
            _hasActiveModuleSelection = true;
        }

        public bool TrySetActive(Guid moduleId)
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
                TestModule.GetTestModule(),
                DcsPrototypeModule.GetDcsPrototypeModule()
            };
        }
    }
}
