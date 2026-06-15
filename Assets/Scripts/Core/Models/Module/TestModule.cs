using System;

namespace Models.Module
{
    public static class TestModule
    {
        public static readonly Guid Id = Guid.Parse("92f96fd1-d2f1-4e28-a047-30b0940dc45f");

        public static ModuleDefinition GetTestModule()
        {
            return new ModuleDefinition(
                Id,
                "Standalone Test Module",
                "Standalone",
                "HZPL Engine",
                new NoOpSimAdapter());
        }
    }
}
