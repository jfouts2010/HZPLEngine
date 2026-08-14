using System;

namespace Models.Module
{
    /// <summary>
    /// Marks the DCS integration boundary while scenario export and mission
    /// result import are still being developed.
    /// </summary>
    public sealed class DcsPrototypeSimAdapter : ISimAdapter
    {
        public ScenarioExportArtifact ExportScenario(
            ScenarioExportSnapshot snapshot)
        {
            return DcsAiObservationMissionExporter.Export(snapshot);
        }

        public void ImportMissionResults()
        {
            throw new NotSupportedException(
                "DCS mission-result import has not been implemented yet.");
        }
    }
}
