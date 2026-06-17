using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public abstract class GroundOrder
    {
        public GroundOrderAssignmentSource AssignmentSource = GroundOrderAssignmentSource.System;
        public string Rationale = string.Empty;
        public bool CanBeReplaced = true;
    }

    [Serializable]
    public sealed class HoldGroundOrder : GroundOrder
    {
        public HoldGroundOrder()
        {
        }

        public HoldGroundOrder(GroundOrderAssignmentSource assignmentSource, string rationale = "")
        {
            AssignmentSource = assignmentSource;
            Rationale = rationale ?? string.Empty;
            CanBeReplaced = true;
        }
    }

    [Serializable]
    public class MoveGroundOrder : GroundOrder
    {
        public Vector3Int FinalDestinationTileId;
        public Vector3Int CurrentDestinationTileId;
        public float MovementProgress;
        public MoveGroundOrderPurpose Purpose = MoveGroundOrderPurpose.Normal;

        public MoveGroundOrder()
        {
            Purpose = MoveGroundOrderPurpose.Normal;
        }

        public bool IsRetreat => Purpose == MoveGroundOrderPurpose.Retreat;
    }

    [Serializable]
    public sealed class AttackGroundOrder : MoveGroundOrder
    {
        public AttackGroundOrder()
        {
            Purpose = MoveGroundOrderPurpose.Normal;
        }
    }

    [Serializable]
    public sealed class SupportAttackGroundOrder : GroundOrder
    {
        public Vector3Int TargetTileId;
    }
    public enum GroundOrderAssignmentSource
    {
        AI,
        System
    }

    public enum MoveGroundOrderPurpose
    {
        Normal,
        Retreat
    }
}
