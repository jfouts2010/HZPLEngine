using System;
using System.Collections.Generic;

namespace Models.Module
{
    public enum NatoUnitSymbol
    {
        Unspecified = 0,
        Infantry = 1,
        MechanizedInfantry = 2,
        Armor = 3,
        MotorizedInfantry = 4,
        Airborne = 5,
        Artillery = 6,
        AirDefense = 7,
        Engineer = 8,
        Headquarters = 9
    }

    public sealed class DivisionTemplate
    {
        public Guid DivisionTemplateId { get; }
        public Guid CountryId { get; }
        public string Name { get; }
        public List<DivisionTemplateBattalion> Battalions { get; }
        public NatoUnitSymbol NatoSymbol { get; }

        public DivisionTemplate(
            Guid divisionTemplateId,
            Guid countryId,
            string name,
            List<DivisionTemplateBattalion> battalions = null,
            NatoUnitSymbol natoSymbol = NatoUnitSymbol.Unspecified)
        {
            if (divisionTemplateId == Guid.Empty)
                throw new ArgumentException("Division template id is required.", nameof(divisionTemplateId));

            if (countryId == Guid.Empty)
                throw new ArgumentException("Country id is required.", nameof(countryId));

            DivisionTemplateId = divisionTemplateId;
            CountryId = countryId;
            Name = string.IsNullOrWhiteSpace(name) ? divisionTemplateId.ToString() : name.Trim();
            Battalions = battalions ?? new List<DivisionTemplateBattalion>();
            NatoSymbol = natoSymbol;
        }

        public DivisionCombatStats CalculateFullStrengthStats(
            IReadOnlyDictionary<Guid, BattalionDefinition> battalionDefinitions)
        {
            return DivisionCombatStats.Calculate(CreateFullStrengthCombatBattalions(battalionDefinitions));
        }

        public float CalculateFullStrengthSpeed(
            IReadOnlyDictionary<Guid, BattalionDefinition> battalionDefinitions)
        {
            var speed = 0f;
            var hasSpeed = false;

            foreach (var combatBattalion in CreateFullStrengthCombatBattalions(battalionDefinitions))
            {
                var battalion = combatBattalion.BattalionDefinition;
                if (!hasSpeed || battalion.Speed < speed)
                {
                    speed = battalion.Speed;
                    hasSpeed = true;
                }
            }

            return speed;
        }

        public List<DivisionCombatStatsBattalion> CreateFullStrengthCombatBattalions(
            IReadOnlyDictionary<Guid, BattalionDefinition> battalionDefinitions)
        {
            var combatBattalions = new List<DivisionCombatStatsBattalion>();

            foreach (var templateBattalion in Battalions)
            {
                if (templateBattalion == null || templateBattalion.Count <= 0)
                    continue;

                if (!battalionDefinitions.TryGetValue(templateBattalion.BattalionDefinitionId, out var battalion))
                    throw new KeyNotFoundException(
                        $"Battalion definition {templateBattalion.BattalionDefinitionId} was not found.");

                if (battalion.CountryId != CountryId)
                    throw new InvalidOperationException(
                        $"Battalion definition {battalion.BattalionDefinitionId} belongs to a different country.");

                combatBattalions.Add(new DivisionCombatStatsBattalion(battalion, templateBattalion.Count));
            }

            return combatBattalions;
        }
    }
}
