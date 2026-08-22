using System;
using System.Collections.Generic;
using System.Linq;
using Models.Gameplay.Campaign;

namespace Engine.Service
{
    /// <summary>
    /// Produces package plans. The execution pipeline only consumes the plan
    /// contract and does not need to know whether plans were authored or built
    /// by a future operational planner.
    /// </summary>
    public interface IAirPlanProducer
    {
        IEnumerable<AirPackagePlan> GetAvailablePlans(DateTime currentTime);
    }

    public sealed class ScriptedAirPlanProducer : IAirPlanProducer
    {
        private readonly Func<IEnumerable<AirPackagePlan>> getPlans;

        public ScriptedAirPlanProducer(
            Func<IEnumerable<AirPackagePlan>> getPlans)
        {
            this.getPlans = getPlans
                            ?? throw new ArgumentNullException(nameof(getPlans));
        }

        public IEnumerable<AirPackagePlan> GetAvailablePlans(
            DateTime currentTime)
        {
            return (getPlans() ?? Enumerable.Empty<AirPackagePlan>())
                .Where(plan => plan != null && plan.AvailableAt <= currentTime);
        }
    }
}
