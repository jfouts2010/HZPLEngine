namespace Models.Module
{
    public interface ISimAdapter
    {
        ScenarioExportArtifact ExportScenario(ScenarioExportSnapshot snapshot);
        void ImportMissionResults();
    }
}
