using System;
using System.Collections.Generic;

namespace Models.Module
{
    public static class TestModule
    {
        public static readonly Guid Id = Guid.Parse("92f96fd1-d2f1-4e28-a047-30b0940dc45f");
        public static readonly Guid BlueCountryId = Guid.Parse("64bfb064-0136-44d3-9983-620cf38d8245");
        public static readonly Guid RedCountryId = Guid.Parse("f6610c73-4f7b-4a71-9695-2f085dc43a7f");
        public static readonly Guid NeutralCountryId = Guid.Parse("ba8ec50b-b305-46ce-b2f6-82a28113f1b5");

        public static readonly Guid BlueInfantryBattalionId = Guid.Parse("1b3d06ec-80a7-4593-93ab-db5d1a44a64d");
        public static readonly Guid BlueArmorBattalionId = Guid.Parse("c71881f4-44ca-4b59-a228-4433f784bbf1");
        public static readonly Guid RedMotorRifleBattalionId = Guid.Parse("9e1716ea-1d90-42ea-806d-1c93cc1cdf22");
        public static readonly Guid RedTankBattalionId = Guid.Parse("ebd352b3-dc92-463d-a3cf-73e8c6de1cd2");

        public static readonly Guid BlueArmoredDivisionTemplateId = Guid.Parse("4153b384-6e76-42df-a1e5-d54582022bee");
        public static readonly Guid RedTankDivisionTemplateId = Guid.Parse("2d4ecc7e-8285-4b8b-9281-00cccbf5d2e8");

        public static ModuleDefinition GetTestModule()
        {
            return new ModuleDefinition(
                Id,
                "Standalone Test Module",
                "Standalone",
                "HZPL Engine",
                new NoOpSimAdapter(),
                CreateCountries(),
                CreateBattalionDefinitions(),
                CreateDivisionTemplates());
        }

        private static List<CountryDefinition> CreateCountries()
        {
            return new List<CountryDefinition>
            {
                new CountryDefinition(BlueCountryId, "Blue Republic"),
                new CountryDefinition(RedCountryId, "Red Federation"),
                new CountryDefinition(NeutralCountryId, "Neutral State")
            };
        }

        private static List<BattalionDefinition> CreateBattalionDefinitions()
        {
            return new List<BattalionDefinition>
            {
                new BattalionDefinition(
                    BlueInfantryBattalionId,
                    BlueCountryId,
                    "Blue Infantry Battalion",
                    120,
                    45,
                    0.25f,
                    12f,
                    2f,
                    18,
                    8,
                    0.9f,
                    4f,
                    2,
                    0.5f,
                    0f),
                new BattalionDefinition(
                    BlueArmorBattalionId,
                    BlueCountryId,
                    "Blue Armor Battalion",
                    95,
                    55,
                    0.2f,
                    18f,
                    22f,
                    12,
                    20,
                    0.25f,
                    8f,
                    3,
                    1.2f,
                    2.5f),
                new BattalionDefinition(
                    RedMotorRifleBattalionId,
                    RedCountryId,
                    "Red Motor Rifle Battalion",
                    100,
                    42,
                    0.22f,
                    13f,
                    4f,
                    16,
                    10,
                    0.75f,
                    6f,
                    2,
                    0.7f,
                    0.6f),
                new BattalionDefinition(
                    RedTankBattalionId,
                    RedCountryId,
                    "Red Tank Battalion",
                    90,
                    50,
                    0.18f,
                    16f,
                    20f,
                    11,
                    19,
                    0.3f,
                    7f,
                    3,
                    1.1f,
                    2.3f)
            };
        }

        private static List<DivisionTemplate> CreateDivisionTemplates()
        {
            return new List<DivisionTemplate>
            {
                new DivisionTemplate(
                    BlueArmoredDivisionTemplateId,
                    BlueCountryId,
                    "Blue Armored Division",
                    new List<DivisionTemplateBattalion>
                    {
                        new DivisionTemplateBattalion(BlueInfantryBattalionId, 5),
                        new DivisionTemplateBattalion(BlueArmorBattalionId, 2)
                    }),
                new DivisionTemplate(
                    RedTankDivisionTemplateId,
                    RedCountryId,
                    "Red Tank Division",
                    new List<DivisionTemplateBattalion>
                    {
                        new DivisionTemplateBattalion(RedMotorRifleBattalionId, 3),
                        new DivisionTemplateBattalion(RedTankBattalionId, 2)
                    })
            };
        }
    }
}
