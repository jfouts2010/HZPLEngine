using System;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;

namespace Engine.Models.Ground
{
    internal static class GroundSystemUtility
    {
        public static bool TryGetDivisionAlliance(GameManager gameManager, Division division, out Alliance alliance)
        {
            alliance = Alliance.Neutral;
            if (gameManager?.CampaignTemplate?.CountryAllianceAssignments == null || division == null)
                return false;

            var assignment = gameManager.CampaignTemplate.CountryAllianceAssignments
                .FirstOrDefault(candidate => candidate != null && candidate.CountryId == division.CountryId);
            if (assignment == null)
                return false;

            alliance = assignment.Alliance;
            return true;
        }

        public static bool AreHostile(Alliance first, Alliance second)
        {
            return (first == Alliance.Bluefor && second == Alliance.Redfor)
                   || (first == Alliance.Redfor && second == Alliance.Bluefor);
        }

        public static Alliance GetHostileAlliance(Alliance alliance)
        {
            return alliance switch
            {
                Alliance.Bluefor => Alliance.Redfor,
                Alliance.Redfor => Alliance.Bluefor,
                _ => Alliance.Neutral
            };
        }

        public static bool IsRetreating(Division division)
        {
            return division?.CurrentOrder is MoveGroundOrder { Purpose: MoveGroundOrderPurpose.Retreat };
        }

    }
}
