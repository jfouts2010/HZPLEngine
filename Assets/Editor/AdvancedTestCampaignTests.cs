using System;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;
using Monobehaviours.Singletons;
using NUnit.Framework;
using UnityEngine;

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

    [Test]
    public void Create_BlueOcaStrikeHasSeadAndFighterProtection()
    {
        var template = AdvancedTestCampaign.Create();
        var plan = template.AirPackagePlans.Single(candidate =>
            candidate.Alliance == Alliance.Bluefor
            && candidate.OperationType == AirOperationType.Strike
            && candidate.StrikePlan?.Purpose
            == StrikePurpose.OffensiveCounterAir);
        var strikeFlights = plan.Flights
            .Where(flight => flight.TaskType == AirFlightTaskType.Strike)
            .ToList();
        var seadEscort = plan.Flights.Single(flight =>
            flight.TaskType == AirFlightTaskType.SeadEscort);
        var fighterEscort = plan.Flights.Single(flight =>
            flight.TaskType == AirFlightTaskType.FighterEscort);
        var strikeIds = strikeFlights
            .Select(flight => flight.FlightPlanId)
            .ToArray();
        var module = TestModule.GetTestModule();
        var f16 = module.AircraftTypeDefinitions.Single(aircraft =>
            aircraft.AircraftTypeDefinitionId == TestModule.F16AircraftTypeId);
        var loadoutPlanner = new AirLoadoutPlanner(
            module,
            alliance => template.OrdnanceAllowances[alliance]);

        Assert.That(strikeFlights, Has.Count.EqualTo(1));
        Assert.That(
            strikeFlights.Select(flight => flight.StrikeAssignment),
            Is.EquivalentTo(new[]
            {
                StrikeAssignment.RunwayDenial
            }));
        Assert.That(
            strikeFlights.Sum(flight => flight.AircraftCount),
            Is.EqualTo(3));
        Assert.That(seadEscort.AircraftCount, Is.EqualTo(2));
        Assert.That(fighterEscort.AircraftCount, Is.EqualTo(4));
        Assert.That(
            seadEscort.ProtectedFlightPlanIds,
            Is.EquivalentTo(strikeIds));
        Assert.That(
            fighterEscort.ProtectedFlightPlanIds,
            Is.SupersetOf(strikeIds));
        Assert.That(
            seadEscort.Loadout.Any(item =>
                item.OrdnanceTypeDefinitionId
                == TestModule.Agm88OrdnanceTypeId),
            Is.True);
        foreach (var flight in strikeFlights.Append(seadEscort))
        {
            Assert.That(
                loadoutPlanner.TryValidateLoadout(
                    f16,
                    Alliance.Bluefor,
                    flight.Loadout,
                    out var reason),
                Is.True,
                reason);
        }
        Assert.That(plan.UseRendezvous, Is.True);
        Assert.That(
            template.BuildingStartingConditions.Any(building =>
                building.BuildingId
                == plan.StrikePlan.TargetAirportBuildingId
                && building.Type == BuildingType.Airport),
            Is.True);
    }

    [Test]
    public void StartCampaign_BlueOcaStrikeCommitsWithoutBarcapConflict()
    {
        var gameObject = new GameObject(
            "Advanced campaign air-schedule regression test");
        var gameManager = gameObject.AddComponent<GameManager>();
        gameManager.AutoStartTestCampaign = false;

        try
        {
            ModuleSingleton.Instance.ResetToDefault();
            var template = AdvancedTestCampaign.Create();
            var strikePlan = template.AirPackagePlans.Single(plan =>
                plan.Alliance == Alliance.Bluefor
                && plan.OperationType == AirOperationType.Strike);

            gameManager.StartCampaign(template);

            var commander = gameManager.GetAllianceAirTaskingCommander(
                Alliance.Bluefor);
            var failure = commander.Diagnostics.FirstOrDefault(diagnostic =>
                diagnostic.PlanId == strikePlan.PlanId
                && diagnostic.Code != "package-committed");
            var package = commander.Packages.SingleOrDefault(candidate =>
                candidate.PlanId == strikePlan.PlanId);
            Assert.That(
                package,
                Is.Not.Null,
                failure == null
                    ? "The authored Blue OCA strike did not commit."
                    : $"{failure.Code}: {failure.Message}");
            Assert.That(
                package.ExecutionPhase,
                Is.EqualTo(AirPackageExecutionPhase.Forming));
            Assert.That(
                package.Flights.Where(flight => flight.IsRequired),
                Is.All.Matches<AirFlight>(flight =>
                    flight.RendezvousState == AirRendezvousState.Enroute
                    && !flight.HasPackageRelease),
                "Required package flights must remain unavailable for offensive support before rendezvous release.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void KnownSamThreatEnvelope_ReturnsRouteEntryAndExitParameters()
    {
        var threat = new KnownSamThreatEnvelope(
            Guid.NewGuid(),
            Vector3.zero,
            maximumSlantRangeFeet: 100f,
            minimumAltitudeFeet: 0f,
            maximumAltitudeFeet: 1000f);

        var intersects = threat.TryGetSegmentIntersectionInterval(
            new Vector3(-200f, 0f, 0f),
            new Vector3(200f, 0f, 0f),
            out var entry,
            out var exit);

        Assert.That(intersects, Is.True);
        Assert.That(entry, Is.EqualTo(0.25f).Within(0.0001f));
        Assert.That(exit, Is.EqualTo(0.75f).Within(0.0001f));
    }
}
