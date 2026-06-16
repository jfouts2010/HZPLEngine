using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class SimulationSettings
    {
        public const int MinSimulationTickMinutes = 1;
        public const int MaxSimulationTickMinutes = 10;
        public const int DefaultSimulationTickMinutes = 5;

        public const int MinOperationalCadenceHours = 1;
        public const int MaxOperationalCadenceHours = 6;
        public const int DefaultOperationalCadenceHours = 6;

        public int SimulationTickMinutes = DefaultSimulationTickMinutes;
        public int OperationalCadenceHours = DefaultOperationalCadenceHours;

        public void Normalize()
        {
            SimulationTickMinutes = Mathf.Clamp(
                SimulationTickMinutes <= 0 ? DefaultSimulationTickMinutes : SimulationTickMinutes,
                MinSimulationTickMinutes,
                MaxSimulationTickMinutes);

            OperationalCadenceHours = Mathf.Clamp(
                OperationalCadenceHours <= 0 ? DefaultOperationalCadenceHours : OperationalCadenceHours,
                MinOperationalCadenceHours,
                MaxOperationalCadenceHours);
        }
    }
}
