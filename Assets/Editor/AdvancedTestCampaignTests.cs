using System;
using System.Linq;
using Models.Gameplay.Campaign;
using NUnit.Framework;

public sealed class AdvancedTestCampaignTests
{
    [Test]
    public void Create_BarcapRotationsMaintainTenMinuteHandoffOverlap()
    {
        var template = AdvancedTestCampaign.Create();
        var barrierGroups = template.AirPackagePlans
            .Where(plan => plan.OperationType == AirOperationType.Barcap)
            .GroupBy(plan => new
            {
                plan.Alliance,
                plan.BarcapBarrier.BarrierId
            })
            .ToList();

        Assert.That(barrierGroups, Has.Count.EqualTo(4));
        foreach (var barrierGroup in barrierGroups)
        {
            var rotations = barrierGroup
                .OrderBy(plan => plan.EffectStart)
                .ToList();
            Assert.That(rotations, Has.Count.GreaterThan(1));
            for (var index = 1; index < rotations.Count; index++)
            {
                var overlap = rotations[index - 1].EffectEnd
                              - rotations[index].EffectStart;
                Assert.That(
                    overlap,
                    Is.GreaterThanOrEqualTo(TimeSpan.FromMinutes(10)),
                    $"{barrierGroup.Key.Alliance} barrier "
                    + $"{barrierGroup.Key.BarrierId} has a coverage gap "
                    + $"before rotation {index + 1}.");
            }
        }
    }
}
