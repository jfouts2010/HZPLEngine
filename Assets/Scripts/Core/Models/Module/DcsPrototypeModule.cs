using System;
using System.Collections.Generic;

namespace Models.Module
{
    /// <summary>
    /// Prototype DCS Module built from the Standalone content catalog. The
    /// campaign-facing capabilities intentionally remain identical while the
    /// third-party identifiers describe their initial DCS realizations.
    /// </summary>
    public static class DcsPrototypeModule
    {
        public static readonly Guid Id =
            Guid.Parse("e79902c9-8962-455d-a988-bf95e79fd4c2");

        public const string TheaterId = "Caucasus";

        // DCS country IDs are part of the mission file's coalition structure.
        public static readonly IReadOnlyDictionary<Guid, int> CountryIds =
            new Dictionary<Guid, int>
            {
                { TestModule.BlueCountryId, 2 },
                { TestModule.RedCountryId, 0 },
                { TestModule.NeutralCountryId, 82 }
            };

        private static readonly IReadOnlyDictionary<Guid, string> ThirdPartyIds =
            new Dictionary<Guid, string>
            {
                // Aircraft type names used by DCS mission unit records.
                { TestModule.F16AircraftTypeId, "F-16C_50" },
                { TestModule.Mig29AircraftTypeId, "MiG-29A" },
                { TestModule.E3AircraftTypeId, "E-3A" },
                { TestModule.Kc135AircraftTypeId, "KC-135" },
                { TestModule.A50AircraftTypeId, "A-50" },
                { TestModule.Il78AircraftTypeId, "IL-78M" },

                // DCS pylon CLSIDs for the externally carried stores in the
                // current catalog. Internal guns and SAM rounds are supplied
                // by their aircraft or launcher and therefore need no pylon ID.
                {
                    TestModule.Aim120OrdnanceTypeId,
                    "{C8E06185-7CD6-4C90-959F-044679E90751}"
                },
                {
                    TestModule.Aim9OrdnanceTypeId,
                    "{6CEB49FC-DED8-4DED-B053-E1F033FF72D3}"
                },
                {
                    TestModule.Agm88OrdnanceTypeId,
                    "{B06DD79A-F21E-4EB9-BD9D-AB3844618C93}"
                },
                { TestModule.Gbu38OrdnanceTypeId, "{GBU-38}" },
                {
                    TestModule.Agm65OrdnanceTypeId,
                    "{444BA8AE-82A7-4345-842E-76154EFCCA46}"
                },
                {
                    TestModule.R27OrdnanceTypeId,
                    "{9B25D316-0434-4954-868F-D51DB1A38DF0}"
                },
                {
                    TestModule.R73OrdnanceTypeId,
                    "{FBC29BFE-3D24-4C64-B81D-941239D12249}"
                },

                // DCS ground-unit type names. The Osa's logical radar,
                // launcher, and command components share one physical DCS unit;
                // the future exporter must coalesce them by site and type.
                { TestModule.FanSongComponentId, "SNR_75V" },
                { TestModule.SpoonRestComponentId, "p-19 s-125 sr" },
                { TestModule.Sa2LauncherComponentId, "S_75M_Volhov" },
                { TestModule.SamCommandPostComponentId, "ZIL-131 KUNG" },
                { TestModule.OsaRadarComponentId, "Osa 9A33 ln" },
                { TestModule.OsaLauncherComponentId, "Osa 9A33 ln" },
                { TestModule.OsaCommandComponentId, "Osa 9A33 ln" }
            };

        public static ModuleDefinition GetDcsPrototypeModule()
        {
            return TestModule.CreateModule(
                Id,
                "DCS Prototype Module",
                "DCS Prototype",
                "Digital Combat Simulator",
                new DcsPrototypeSimAdapter(),
                ThirdPartyIds);
        }
    }
}
