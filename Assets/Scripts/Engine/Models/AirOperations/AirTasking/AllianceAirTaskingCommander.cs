using System;
using System.Collections.Generic;
using System.Linq;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AllianceAirTaskingCommander
    {
        public const int MaximumDiagnosticEntries = 256;
        public const int MaximumHistoryEntries = 256;
        public const int MaximumSupportDemandSamples = 256;

        public Alliance Alliance;
        public AllianceAirDoctrine Doctrine = AllianceAirDoctrine.CreateDefault();
        public int PlanningCycle;
        public List<AirMissionRequest> MissionRequests = new List<AirMissionRequest>();
        public List<AirPackage> Packages = new List<AirPackage>();
        public List<SupportDemandSample> SupportDemandHistory = new List<SupportDemandSample>();
        public List<AirTaskingDiagnostic> Diagnostics = new List<AirTaskingDiagnostic>();
        public List<AirTaskingHistoryEntry> History = new List<AirTaskingHistoryEntry>();

        public AllianceAirTaskingCommander()
        {
        }

        public AllianceAirTaskingCommander(Alliance alliance, AllianceAirDoctrine doctrine)
        {
            Alliance = alliance;
            Doctrine = doctrine?.Clone() ?? AllianceAirDoctrine.CreateDefault();
        }

        public void AddDiagnostic(AirTaskingDiagnostic diagnostic)
        {
            if (diagnostic == null)
                return;

            Diagnostics ??= new List<AirTaskingDiagnostic>();
            Diagnostics.Add(diagnostic);
            TrimOldest(Diagnostics, MaximumDiagnosticEntries);
        }

        public void AddHistory(AirTaskingHistoryEntry historyEntry)
        {
            if (historyEntry == null)
                return;

            History ??= new List<AirTaskingHistoryEntry>();
            History.Add(historyEntry);
            TrimOldest(History, MaximumHistoryEntries);
        }

        public void AddSupportDemand(SupportDemandSample demandSample)
        {
            if (demandSample == null)
                return;

            SupportDemandHistory ??= new List<SupportDemandSample>();
            SupportDemandHistory.Add(demandSample);
            TrimOldest(SupportDemandHistory, MaximumSupportDemandSamples);
        }

        public AirMissionRequest GetRequest(Guid requestId)
        {
            return (MissionRequests ?? new List<AirMissionRequest>())
                .FirstOrDefault(request => request.MissionRequestId == requestId);
        }

        public AirPackage GetPackage(Guid packageId)
        {
            return (Packages ?? new List<AirPackage>())
                .FirstOrDefault(package => package.PackageId == packageId);
        }

        private static void TrimOldest<T>(List<T> entries, int maximumEntries)
        {
            if (entries.Count <= maximumEntries)
                return;

            entries.RemoveRange(0, entries.Count - maximumEntries);
        }
    }
}
