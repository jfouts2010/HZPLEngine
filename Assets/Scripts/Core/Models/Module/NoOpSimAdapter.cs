using System;

namespace Models.Module
{
    public sealed class NoOpSimAdapter : ISimAdapter
    {
        public ScenarioExportArtifact ExportScenario(
            ScenarioExportSnapshot snapshot)
        {
            throw new NotSupportedException(
                "Standalone Module does not export third-party scenarios.");
        }

        public void ImportMissionResults()
        {
        }
    }
}
