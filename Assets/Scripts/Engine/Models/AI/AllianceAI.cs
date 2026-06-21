using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models.Ground;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Engine.Models
{
    public class AllianceAI
    {
        private const int MaxDefensivePathLength = 10;
        private const int MaxOffensivePathLength = 12;
        private const int RequiredHoldingDivisionsPerFrontTile = 1;
        private const int MinAssaultDivisionsPerOffensiveTarget = 2;
        private const int MaxAssaultDivisionsPerOffensiveTarget = 4;
        private const float OffensiveFeasibilityThreshold = 0.55f;
        private const float CombatUtilityWeight = 0.65f;
        private const float StrategicValueUtilityWeight = 0.35f;
        private const float OffensivePlanUtilityRandomVariance = 0.2f;
        private const float DefensiveRedeploymentThreatRatio = 1.25f;
        private const float DefensiveStrategicValueThreatWeight = 0.25f;

        private readonly GameManager _gameManager;
        private readonly HashSet<Vector3Int> _frontTileIds = new HashSet<Vector3Int>();
        private Dictionary<Vector3Int, float> _supplyStrategicValueByTileId;

        public Alliance Alliance { get; }
        public OffensivePlan ActiveOffensivePlan { get; private set; }

        public IReadOnlyCollection<Vector3Int> FrontTileIds => _frontTileIds;

        public AllianceAI(GameManager gameManager, Alliance alliance)
        {
            _gameManager = gameManager;
            Alliance = alliance;
        }

        internal void RefreshFront()
        {
            _frontTileIds.Clear();

            if (!TryGetHostileAlliance(Alliance, out var hostileAlliance))
                return;

            var controllersByTileId = BuildControllerLookup(_gameManager.Tiles);
            var neighborsByTileId = BuildNeighborLookup(_gameManager.CampaignTiles);

            foreach (var entry in controllersByTileId)
            {
                if (entry.Value != Alliance)
                    continue;

                if (!neighborsByTileId.TryGetValue(entry.Key, out var neighborTileIds))
                    continue;

                foreach (var neighborTileId in neighborTileIds)
                {
                    if (controllersByTileId.TryGetValue(neighborTileId, out var neighborController)
                        && neighborController == hostileAlliance)
                    {
                        _frontTileIds.Add(entry.Key);
                        break;
                    }
                }
            }
        }

        internal void AssignMovementOrders()
        {
            _supplyStrategicValueByTileId = null;

            if (!TryGetHostileAlliance(Alliance, out var hostileAlliance))
                return;

            AssignIdleDivisionIntents(hostileAlliance);

            var coverageSatisfied = EnsureFrontCoverage();
            if (coverageSatisfied)
                ReinforceThreatenedFrontTiles();

            if (!coverageSatisfied)
            {
                AbortOffensivePlan("Front coverage unavailable");
                return;
            }

            ReconcileOffensivePlan(hostileAlliance);
        }

        internal void AssignAvailableAdjacentOffensiveAssists()
        {
            if (ActiveOffensivePlan == null || ActiveOffensivePlan.Phase != OffensivePlanPhase.Attack)
                return;

            if (IsOffensivePlanComplete())
            {
                ActiveOffensivePlan = null;
                return;
            }

            if (!IsOffensivePlanValid(ActiveOffensivePlan, out var invalidReason))
            {
                AbortOffensivePlan(invalidReason);
                return;
            }

            var targetTileId = ActiveOffensivePlan.TargetTileId;
            var assignedDivisionIds = ActiveOffensivePlan.Assignments
                .Select(assignment => assignment.DivisionId)
                .ToHashSet();

            foreach (var candidate in GetAdjacentOffensiveAssistCandidates(targetTileId, assignedDivisionIds))
            {
                ActiveOffensivePlan.Assignments.Add(new OffensivePlanAssignment(
                    candidate.DivisionId,
                    GroundOrderAIIntent.SupportAttack,
                    candidate.TileId,
                    targetTileId,
                    GroundPath.FromSingleTile(candidate.TileId)));

                candidate.CurrentOrder = new SupportAttackGroundOrder
                {
                    AssignmentSource = GroundOrderAssignmentSource.AI,
                    AIIntent = GroundOrderAIIntent.SupportAttack,
                    CanBeReplaced = true,
                    Rationale = "Late support for active offensive plan",
                    TargetTileId = targetTileId
                };

                assignedDivisionIds.Add(candidate.DivisionId);
            }
        }

        private void AssignIdleDivisionIntents(Alliance hostileAlliance)
        {
            var assignedHoldersByTileId = new Dictionary<Vector3Int, int>();
            foreach (var division in GetAllianceDivisions())
            {
                if (division?.CurrentOrder is not HoldGroundOrder holdOrder)
                    continue;

                if (!CanReplaceOrder(division))
                    continue;

                if (_frontTileIds.Contains(division.TileId))
                {
                    var adjacentHostileTileCount = CountAdjacentHostileTiles(division.TileId, hostileAlliance);
                    assignedHoldersByTileId.TryGetValue(division.TileId, out var assignedHolders);
                    if (assignedHolders >= RequiredHoldingDivisionsPerFrontTile)
                    {
                        holdOrder.AssignmentSource = GroundOrderAssignmentSource.AI;
                        holdOrder.AIIntent = GroundOrderAIIntent.Flex;
                        holdOrder.Rationale = "Available front reserve";
                        continue;
                    }

                    assignedHoldersByTileId[division.TileId] = assignedHolders + 1;
                    holdOrder.AssignmentSource = GroundOrderAssignmentSource.AI;
                    holdOrder.AIIntent = adjacentHostileTileCount == 1
                        ? GroundOrderAIIntent.HoldEdge
                        : GroundOrderAIIntent.HoldFront;
                    holdOrder.Rationale = "Holding front tile";
                    continue;
                }

                holdOrder.AssignmentSource = GroundOrderAssignmentSource.AI;
                holdOrder.AIIntent = GroundOrderAIIntent.Flex;
                holdOrder.Rationale = "Available reserve";
            }
        }

        private bool EnsureFrontCoverage()
        {
            var allCovered = true;
            foreach (var frontTileId in OrderTileIds(_frontTileIds))
            {
                if (HasProjectedDefensiveCoverage(frontTileId))
                    continue;

                allCovered = false;
                TryAssignDefensiveMove(frontTileId, "Refilling uncovered front tile");
            }

            return allCovered || _frontTileIds.All(HasProjectedDefensiveCoverage);
        }

        private void ReinforceThreatenedFrontTiles()
        {
            foreach (var frontTileId in OrderTileIds(_frontTileIds)
                         .OrderByDescending(GetTileStrategicValue))
            {
                var desiredDefenders = CalculateDesiredDefenderCount(frontTileId);
                var projectedDefenders = CountProjectedDefenders(frontTileId);

                while (projectedDefenders < desiredDefenders)
                {
                    if (!TryAssignDefensiveMove(
                            frontTileId,
                            "Reinforcing threatened front tile",
                            requireMaterialThreatAdvantage: true))
                        break;

                    projectedDefenders++;
                }
            }
        }

        private int CalculateDesiredDefenderCount(Vector3Int frontTileId)
        {
            var friendlyPower = SumCombatPower(GetPhysicalDefenders(frontTileId));
            var enemyPower = SumCombatPower(GetAdjacentHostileDivisions(frontTileId));
            var desired = 1;

            /*if (enemyPower > friendlyPower * 1.1f)
                desired++;*/
            if (enemyPower > friendlyPower * 1.6f)
                desired++;
          /*  if (GetTileStrategicValue(frontTileId) >= 8f)
                desired++;*/

            return Mathf.Clamp(desired, 1, 4);
        }

        private bool TryAssignDefensiveMove(
            Vector3Int targetTileId,
            string rationale,
            bool requireMaterialThreatAdvantage = false)
        {
            if (!TryFindBestDefensiveReserve(
                    targetTileId,
                    requireMaterialThreatAdvantage,
                    false,
                    out var reserve,
                    out var path)
                && !TryFindBestDefensiveReserve(
                    targetTileId,
                    requireMaterialThreatAdvantage,
                    true,
                    out reserve,
                    out path))
                return false;

            var removedFromOffensive = TryRemoveDivisionFromOffensivePlan(reserve.DivisionId);
            var moveOrder = new MoveGroundOrder
            {
                AssignmentSource = GroundOrderAssignmentSource.AI,
                AIIntent = GroundOrderAIIntent.RefillFront,
                CanBeReplaced = true,
                Rationale = removedFromOffensive
                    ? "Detached from viable offensive to refill front"
                    : rationale ?? "Refilling front",
                Purpose = MoveGroundOrderPurpose.Normal,
                Path = path,
                MovementProgress = 0f
            };

            if (!path.TryGetNextStep(reserve.TileId, out var nextStep))
                return false;

            moveOrder.CurrentDestinationTileId = nextStep;
            reserve.CurrentOrder = moveOrder;
            return true;
        }

        private bool TryFindBestDefensiveReserve(
            Vector3Int targetTileId,
            bool requireMaterialThreatAdvantage,
            bool allowActiveOffensivePlanAssignments,
            out Division reserve,
            out GroundPath path)
        {
            reserve = null;
            path = null;
            var bestPathLength = int.MaxValue;
            var bestPower = float.MinValue;

            foreach (var candidate in GetAllianceDivisions())
            {
                if (!IsDefensiveReserve(candidate, targetTileId, requireMaterialThreatAdvantage))
                    continue;

                if (IsAssignedToActiveOffensivePlan(candidate.DivisionId)
                    && (!allowActiveOffensivePlanAssignments
                        || !CanRemoveDivisionFromOffensivePlan(candidate.DivisionId)))
                    continue;

                if (!GroundPathfindingService.TryFindFriendlyPath(
                        _gameManager,
                        candidate.TileId,
                        targetTileId,
                        Alliance,
                        out var candidatePath))
                    continue;

                if (candidatePath.StepCount > MaxDefensivePathLength)
                    continue;

                var candidatePower = GroundCombatEstimationService.CalculateDivisionCombatPower(candidate);
                if (candidatePath.StepCount > bestPathLength)
                    continue;

                if (candidatePath.StepCount == bestPathLength && candidatePower <= bestPower)
                    continue;

                reserve = candidate;
                path = candidatePath;
                bestPathLength = candidatePath.StepCount;
                bestPower = candidatePower;
            }

            return reserve != null && path != null;
        }

        private bool IsAssignedToActiveOffensivePlan(Guid divisionId)
        {
            return ActiveOffensivePlan?.Assignments != null
                   && ActiveOffensivePlan.Assignments.Any(assignment => assignment.DivisionId == divisionId);
        }

        private bool CanRemoveDivisionFromOffensivePlan(Guid divisionId)
        {
            if (ActiveOffensivePlan == null)
                return false;

            var remainingAssignments = ActiveOffensivePlan.Assignments
                .Where(assignment => assignment.DivisionId != divisionId)
                .ToList();

            if (remainingAssignments.Count == ActiveOffensivePlan.Assignments.Count)
                return false;

            if (CountAssaultAssignments(remainingAssignments)
                < CalculateRequiredAssaultDivisionCount(ActiveOffensivePlan.TargetTileId))
                return false;

            return EstimateAssignments(ActiveOffensivePlan.TargetTileId, remainingAssignments, out var estimate)
                   && estimate.VictoryLikelihood >= OffensiveFeasibilityThreshold;
        }

        private bool TryRemoveDivisionFromOffensivePlan(Guid divisionId)
        {
            if (!CanRemoveDivisionFromOffensivePlan(divisionId))
                return false;

            ActiveOffensivePlan.Assignments.RemoveAll(assignment => assignment.DivisionId == divisionId);
            return true;
        }

        private bool IsDefensiveReserve(
            Division division,
            Vector3Int targetTileId,
            bool requireMaterialThreatAdvantage)
        {
            if (!CanReceiveAIOrder(division))
                return false;

            if (!IsCombatReady(division))
                return false;

            if (division.TileId == targetTileId)
                return false;

            if (!_frontTileIds.Contains(division.TileId))
                return true;

            return CountAvailablePhysicalDefendersForSourceTile(division.TileId) >= 2
                   && (!requireMaterialThreatAdvantage
                       || IsMateriallyStrongerDefensiveThreat(targetTileId, division.TileId));
        }

        private bool IsMateriallyStrongerDefensiveThreat(Vector3Int targetTileId, Vector3Int currentTileId)
        {
            var targetThreat = CalculateDefensiveThreatScore(targetTileId);
            var currentThreat = CalculateDefensiveThreatScore(currentTileId);
            return targetThreat > currentThreat * DefensiveRedeploymentThreatRatio;
        }

        private float CalculateDefensiveThreatScore(Vector3Int frontTileId)
        {
            var enemyPower = SumCombatPower(GetAdjacentHostileDivisions(frontTileId));
            var strategicValue = GetTileStrategicValue(frontTileId) * DefensiveStrategicValueThreatWeight;
            return enemyPower + strategicValue;
        }

        private int CountAvailablePhysicalDefendersForSourceTile(Vector3Int tileId)
        {
            return CountAvailablePhysicalDefendersForSourceTile(tileId, null);
        }

        private int CountAvailablePhysicalDefendersForSourceTile(
            Vector3Int tileId,
            ISet<Guid> departingDivisionIds)
        {
            return GetPhysicalDefenders(tileId)
                .Count(division => (departingDivisionIds == null
                                    || !departingDivisionIds.Contains(division.DivisionId))
                                   && (division.CurrentOrder is not MoveGroundOrder moveOrder
                                       || moveOrder.DestinationTileId == tileId));
        }

        private void ReconcileOffensivePlan(Alliance hostileAlliance)
        {
            if (ActiveOffensivePlan == null)
            {
                TryCreateOffensivePlan(hostileAlliance);
                return;
            }

            if (IsOffensivePlanComplete())
            {
                ActiveOffensivePlan = null;
                return;
            }

            if (!IsOffensivePlanValid(ActiveOffensivePlan, out var invalidReason))
            {
                AbortOffensivePlan(invalidReason);
                return;
            }

            switch (ActiveOffensivePlan.Phase)
            {
                case OffensivePlanPhase.Assemble:
                    ExecuteAssemblyPhase();
                    break;
                case OffensivePlanPhase.Attack:
                    ExecuteAttackPhase();
                    break;
            }
        }

        private bool TryCreateOffensivePlan(Alliance hostileAlliance)
        {
            OffensivePlan bestPlan = null;
            var bestUtility = float.MinValue;

            foreach (var targetTileId in GetOffensiveTargetCandidates(hostileAlliance))
            {
                var plan = BuildOffensivePlan(targetTileId);
                if (plan == null)
                    continue;

                if (!EstimatePlanAssault(plan, out var estimate))
                    continue;

                if (estimate.VictoryLikelihood < OffensiveFeasibilityThreshold)
                    continue;

                var utility = GetOffensivePlanUtility(estimate, targetTileId);
                if (utility <= bestUtility)
                    continue;

                if(!IsOffensivePlanValid(plan, out var invalidReason))
                    continue;
                
                bestPlan = plan;
                bestUtility = utility;
            }

            ActiveOffensivePlan = bestPlan;
            if (ActiveOffensivePlan != null)
                ExecuteAssemblyPhase();

            return ActiveOffensivePlan != null;
        }

        private OffensivePlan BuildOffensivePlan(Vector3Int targetTileId)
        {
            var stagingTiles = GetFriendlyStagingTiles(targetTileId).ToList();
            if (stagingTiles.Count == 0)
                return null;

            var assignments = new List<OffensivePlanAssignment>();
            var usedDivisionIds = new HashSet<Guid>();
            var departingDivisionIds = new HashSet<Guid>();
            var requiredAssaultCount = CalculateRequiredAssaultDivisionCount(targetTileId);

            foreach (var candidate in GetOffensiveCandidates(targetTileId))
            {
                if (usedDivisionIds.Contains(candidate.DivisionId))
                    continue;

                var needsAssaultAssignment = CountAssaultAssignments(assignments) < requiredAssaultCount;
                if (!TryChooseOffensiveAssignment(
                        candidate,
                        targetTileId,
                        stagingTiles,
                        needsAssaultAssignment,
                        departingDivisionIds,
                        out var intent,
                        out var stagingTileId,
                        out var path))
                    continue;

                assignments.Add(new OffensivePlanAssignment(
                    candidate.DivisionId,
                    intent,
                    stagingTileId,
                    targetTileId,
                    path));
                usedDivisionIds.Add(candidate.DivisionId);
                if (DoesOffensiveAssignmentDepartCurrentTile(candidate, intent, stagingTileId))
                    departingDivisionIds.Add(candidate.DivisionId);

                if (EstimateAssignments(targetTileId, assignments, out var estimate)
                    && CountAssaultAssignments(assignments) >= requiredAssaultCount
                    && estimate.VictoryLikelihood >= OffensiveFeasibilityThreshold)
                    break;
            }

            if (CountAssaultAssignments(assignments) < requiredAssaultCount)
                return null;

            if (!EstimateAssignments(targetTileId, assignments, out var finalEstimate)
                || finalEstimate.VictoryLikelihood < OffensiveFeasibilityThreshold)
                return null;

            AddPinAssignments(targetTileId, stagingTiles, assignments, usedDivisionIds, departingDivisionIds);

            return new OffensivePlan(targetTileId, assignments);
        }

        private bool TryChooseOffensiveAssignment(
            Division division,
            Vector3Int targetTileId,
            IReadOnlyCollection<Vector3Int> stagingTiles,
            bool needsAssaultAssignment,
            ISet<Guid> departingDivisionIds,
            out GroundOrderAIIntent intent,
            out Vector3Int stagingTileId,
            out GroundPath path)
        {
            intent = GroundOrderAIIntent.None;
            stagingTileId = default;
            path = null;

            if (!TryChooseStagingTile(division, stagingTiles, out stagingTileId, out path))
                return false;

            intent = needsAssaultAssignment && !IsSupportOnlyOffensiveCandidate(division, targetTileId)
                ? GroundOrderAIIntent.Assault
                : GroundOrderAIIntent.SupportAttack;

            if (CanAssignOffensivePlanRole(division, intent, stagingTileId, targetTileId, departingDivisionIds))
                return true;

            if (!CanSupportAttackFromCurrentTile(division, targetTileId))
                return false;

            intent = GroundOrderAIIntent.SupportAttack;
            stagingTileId = division.TileId;
            path = GroundPath.FromSingleTile(division.TileId);
            return true;
        }

        private bool CanAssignOffensivePlanRole(
            Division division,
            GroundOrderAIIntent intent,
            Vector3Int stagingTileId,
            Vector3Int targetTileId,
            ISet<Guid> departingDivisionIds)
        {
            if (!DoesOffensiveAssignmentDepartCurrentTile(division, intent, stagingTileId))
                return true;

            if (!_frontTileIds.Contains(division.TileId))
                return true;

            if (DoesCapturingTargetRemoveSourceFromFront(division.TileId, targetTileId))
                return true;

            return CountAvailablePhysicalDefendersForSourceTile(
                division.TileId,
                departingDivisionIds) > RequiredHoldingDivisionsPerFrontTile;
        }

        private bool CanSupportAttackFromCurrentTile(Division division, Vector3Int targetTileId)
        {
            return division != null
                   && _frontTileIds.Contains(division.TileId)
                   && GroundSystemUtility.AreNeighbors(_gameManager, division.TileId, targetTileId);
        }

        private static bool DoesOffensiveAssignmentDepartCurrentTile(
            Division division,
            GroundOrderAIIntent intent,
            Vector3Int stagingTileId)
        {
            return stagingTileId != division.TileId
                   || (intent != GroundOrderAIIntent.SupportAttack
                       && intent != GroundOrderAIIntent.Pin);
        }

        private IEnumerable<Division> GetOffensiveCandidates(Vector3Int targetTileId)
        {
            return GetAllianceDivisions()
                .Where(division => IsEligibleForOffense(division, targetTileId))
                .OrderByDescending(GroundCombatEstimationService.CalculateDivisionCombatPower)
                .ThenBy(division => division.DivisionId);
        }

        private IEnumerable<Division> GetAdjacentOffensiveAssistCandidates(
            Vector3Int targetTileId,
            ISet<Guid> assignedDivisionIds)
        {
            return GetAllianceDivisions()
                .Where(division => division != null
                                   && (assignedDivisionIds == null
                                       || !assignedDivisionIds.Contains(division.DivisionId))
                                   && IsEligibleForOffense(division, targetTileId)
                                   && IsFriendlyControlledLandTile(division.TileId)
                                   && GroundSystemUtility.AreNeighbors(_gameManager, division.TileId, targetTileId)
                                   && CanSupportAttackFromCurrentTile(division, targetTileId))
                .OrderByDescending(GroundCombatEstimationService.CalculateDivisionCombatPower)
                .ThenBy(division => division.DivisionId);
        }

        private bool IsEligibleForOffense(Division division, Vector3Int targetTileId)
        {
            if (!CanReceiveAIOrder(division))
                return false;

            if (!IsCombatReady(division))
                return false;

            switch (division.CurrentOrder?.AIIntent)
            {
                case GroundOrderAIIntent.Flex:
                case GroundOrderAIIntent.None:
                    return !_frontTileIds.Contains(division.TileId)
                           || CountEligiblePhysicalDefenders(division.TileId) > RequiredHoldingDivisionsPerFrontTile;
                case GroundOrderAIIntent.HoldFront:
                    return GroundSystemUtility.AreNeighbors(_gameManager, division.TileId, targetTileId);
                case GroundOrderAIIntent.HoldEdge:
                    return CountAdjacentHostileTiles(division.TileId, GroundSystemUtility.GetHostileAlliance(Alliance)) == 1
                           && GroundSystemUtility.AreNeighbors(_gameManager, division.TileId, targetTileId);
                default:
                    return false;
            }
        }

        private bool IsSupportOnlyOffensiveCandidate(Division division, Vector3Int targetTileId)
        {
            if (division?.CurrentOrder is not HoldGroundOrder)
                return false;

            if (division.CurrentOrder.AIIntent == GroundOrderAIIntent.HoldEdge
                && DoesCapturingTargetRemoveSourceFromFront(division.TileId, targetTileId))
                return false;

            return division.CurrentOrder.AIIntent == GroundOrderAIIntent.HoldFront
                   || division.CurrentOrder.AIIntent == GroundOrderAIIntent.HoldEdge;
        }

        private void AddPinAssignments(
            Vector3Int targetTileId,
            IReadOnlyCollection<Vector3Int> stagingTiles,
            List<OffensivePlanAssignment> assignments,
            HashSet<Guid> usedDivisionIds,
            HashSet<Guid> departingDivisionIds)
        {
            var pinTargets = GetNeighborTileIds(targetTileId)
                .Where(tileId => tileId != targetTileId)
                .Where(IsHostileControlledLandTile)
                .Where(HasHostilePhysicalDefender)
                .ToList();

            foreach (var pinTarget in OrderTileIds(pinTargets))
            {
                var candidate = GetOffensiveCandidates(pinTarget)
                    .FirstOrDefault(division => !usedDivisionIds.Contains(division.DivisionId)
                                                && !IsSupportOnlyOffensiveCandidate(division, pinTarget));
                if (candidate == null)
                    continue;

                var pinStagingTiles = GetFriendlyStagingTiles(pinTarget).ToList();
                if (!TryChooseStagingTile(candidate, pinStagingTiles, out var stagingTileId, out var path))
                    continue;

                if (!CanAssignOffensivePlanRole(
                        candidate,
                        GroundOrderAIIntent.Pin,
                        stagingTileId,
                        pinTarget,
                        departingDivisionIds))
                    continue;

                assignments.Add(new OffensivePlanAssignment(
                    candidate.DivisionId,
                    GroundOrderAIIntent.Pin,
                    stagingTileId,
                    pinTarget,
                    path));
                usedDivisionIds.Add(candidate.DivisionId);
                if (DoesOffensiveAssignmentDepartCurrentTile(candidate, GroundOrderAIIntent.Pin, stagingTileId))
                    departingDivisionIds.Add(candidate.DivisionId);
            }
        }

        private void ExecuteAssemblyPhase()
        {
            var allReady = true;
            foreach (var assignment in ActiveOffensivePlan.Assignments)
            {
                if (!_gameManager.divisionSystem.TryGetDivision(assignment.DivisionId, out var division))
                {
                    allReady = false;
                    continue;
                }

                if (division.TileId == assignment.StagingTileId)
                {
                    if (division.CurrentOrder is HoldGroundOrder)
                    {
                        division.CurrentOrder.AIIntent = assignment.Intent;
                        division.CurrentOrder.AssignmentSource = GroundOrderAssignmentSource.AI;
                        division.CurrentOrder.Rationale = "Assembled for offensive plan";
                    }
                    continue;
                }

                allReady = false;
                if (division.CurrentOrder is MoveGroundOrder moveOrder
                    && moveOrder.DestinationTileId == assignment.StagingTileId
                    && moveOrder.AIIntent == GroundOrderAIIntent.OffensiveStaging)
                    continue;

                var stagingMove = new MoveGroundOrder
                {
                    AssignmentSource = GroundOrderAssignmentSource.AI,
                    AIIntent = GroundOrderAIIntent.OffensiveStaging,
                    CanBeReplaced = true,
                    Rationale = "Staging for offensive plan",
                    Purpose = MoveGroundOrderPurpose.Normal,
                    MovementProgress = 0f
                };

                if (!GroundPathfindingService.TryPrepareMoveGroundOrder(
                        _gameManager,
                        division.TileId,
                        assignment.StagingTileId,
                        Alliance,
                        stagingMove))
                {
                    AbortOffensivePlan("Staging path became invalid");
                    return;
                }

                division.CurrentOrder = stagingMove;
            }

            if (!allReady)
                return;

            ActiveOffensivePlan.Phase = OffensivePlanPhase.Attack;
            ExecuteAttackPhase();
        }

        private void ExecuteAttackPhase()
        {
            foreach (var assignment in ActiveOffensivePlan.Assignments)
            {
                if (!_gameManager.divisionSystem.TryGetDivision(assignment.DivisionId, out var division))
                    continue;

                if (assignment.Intent == GroundOrderAIIntent.SupportAttack
                    || assignment.Intent == GroundOrderAIIntent.Pin)
                {
                    if (division.CurrentOrder is SupportAttackGroundOrder supportOrder
                        && supportOrder.TargetTileId == assignment.EngagementTileId
                        && supportOrder.AIIntent == assignment.Intent)
                        continue;

                    division.CurrentOrder = new SupportAttackGroundOrder
                    {
                        AssignmentSource = GroundOrderAssignmentSource.AI,
                        AIIntent = assignment.Intent,
                        CanBeReplaced = true,
                        Rationale = assignment.Intent == GroundOrderAIIntent.Pin
                            ? "Pinning adjacent defenders for offensive plan"
                            : "Supporting offensive plan",
                        TargetTileId = assignment.EngagementTileId
                    };
                    continue;
                }

                if (division.CurrentOrder is MoveGroundOrder attackOrder
                    && attackOrder.DestinationTileId == assignment.EngagementTileId
                    && attackOrder.AIIntent == assignment.Intent)
                    continue;

                var moveOrder = new MoveGroundOrder
                {
                    AssignmentSource = GroundOrderAssignmentSource.AI,
                    AIIntent = assignment.Intent,
                    CanBeReplaced = true,
                    Rationale = assignment.Intent == GroundOrderAIIntent.Pin
                        ? "Pinning adjacent defenders for offensive plan"
                        : "Assaulting offensive plan target",
                    Purpose = MoveGroundOrderPurpose.Normal
                };

                if (!GroundPathfindingService.TryPrepareMoveGroundOrder(
                        _gameManager,
                        division.TileId,
                        assignment.EngagementTileId,
                        Alliance,
                        moveOrder))
                {
                    AbortOffensivePlan("Attack path became invalid");
                    return;
                }

                division.CurrentOrder = moveOrder;
            }
        }

        private bool IsOffensivePlanValid(OffensivePlan proposedPlan, out string reason)
        {
            reason = string.Empty;

            if (proposedPlan == null)
                return false;

            if (!IsHostileControlledLandTile(proposedPlan.TargetTileId))
            {
                reason = "Target is no longer hostile";
                return false;
            }

            if (CountAssaultAssignments(proposedPlan.Assignments)
                < CalculateRequiredAssaultDivisionCount(proposedPlan.TargetTileId))
            {
                reason = "Offensive plan lacks enough assault divisions to hold captured ground";
                return false;
            }

            if (!EstimatePlanAssault(proposedPlan, out var estimate)
                || estimate.VictoryLikelihood < OffensiveFeasibilityThreshold)
            {
                reason = "Offensive feasibility failed";
                return false;
            }

            foreach (var assignment in proposedPlan.Assignments)
            {
                if (!_gameManager.divisionSystem.TryGetDivision(assignment.DivisionId, out var division)
                    || !IsCombatReady(division)
                    || GroundSystemUtility.IsRetreating(division)
                    || _gameManager.IsDivisionDefendingInGroundCombat(division.DivisionId))
                {
                    reason = "Assigned division became unavailable";
                    return false;
                }

                if (proposedPlan.Phase == OffensivePlanPhase.Assemble
                    && !IsFriendlyControlledLandTile(assignment.StagingTileId))
                {
                    reason = "Staging tile is no longer friendly";
                    return false;
                }
            }

            if (!DoesOffensivePlanPreserveFrontCoverage(proposedPlan))
            {
                reason = "Offensive plan would leave a front tile undefended";
                return false;
            }

            return true;
        }

        private bool DoesOffensivePlanPreserveFrontCoverage(OffensivePlan plan)
        {
            var departingDivisionIds = new HashSet<Guid>();
            foreach (var assignment in plan.Assignments)
            {
                if (!_gameManager.divisionSystem.TryGetDivision(assignment.DivisionId, out var division))
                    continue;

                if (!DoesOffensiveAssignmentDepartCurrentTile(division, assignment.Intent, assignment.StagingTileId))
                    continue;

                if (!_frontTileIds.Contains(division.TileId))
                    continue;

                if (DoesCapturingTargetRemoveSourceFromFront(division.TileId, assignment.EngagementTileId))
                {
                    departingDivisionIds.Add(division.DivisionId);
                    continue;
                }

                var projectedDepartingDivisionIds = new HashSet<Guid>(departingDivisionIds)
                {
                    division.DivisionId
                };

                if (CountAvailablePhysicalDefendersForSourceTile(
                        division.TileId,
                        projectedDepartingDivisionIds) < RequiredHoldingDivisionsPerFrontTile)
                    return false;

                departingDivisionIds.Add(division.DivisionId);
            }

            return true;
        }

        private int CalculateRequiredAssaultDivisionCount(Vector3Int targetTileId)
        {
            if (!HasHostilePhysicalDefender(targetTileId))
                return 1;

            var desired = MinAssaultDivisionsPerOffensiveTarget;

            if (GetAdjacentHostileDivisions(targetTileId).Count() >= 3)
                desired++;

            if (GetTileStrategicValue(targetTileId) >= 8f)
                desired++;

            return Mathf.Clamp(
                desired,
                MinAssaultDivisionsPerOffensiveTarget,
                MaxAssaultDivisionsPerOffensiveTarget);
        }

        private static int CountAssaultAssignments(IEnumerable<OffensivePlanAssignment> assignments)
        {
            return (assignments ?? Enumerable.Empty<OffensivePlanAssignment>())
                .Count(assignment => assignment.Intent == GroundOrderAIIntent.Assault);
        }

        private bool IsOffensivePlanComplete()
        {
            return ActiveOffensivePlan != null && !IsHostileControlledLandTile(ActiveOffensivePlan.TargetTileId);
        }

        private void AbortOffensivePlan(string reason)
        {
            if (ActiveOffensivePlan == null)
                return;

            foreach (var assignment in ActiveOffensivePlan.Assignments)
            {
                if (!_gameManager.divisionSystem.TryGetDivision(assignment.DivisionId, out var division))
                    continue;

                if (division.CurrentOrder == null || !division.CurrentOrder.CanBeReplaced)
                    continue;

                if (!IsOffensiveIntent(division.CurrentOrder.AIIntent))
                    continue;

                division.CurrentOrder = new HoldGroundOrder(
                    GroundOrderAssignmentSource.AI,
                    reason ?? "Offensive plan aborted")
                {
                    AIIntent = _frontTileIds.Contains(division.TileId)
                        ? GroundOrderAIIntent.HoldFront
                        : GroundOrderAIIntent.Flex
                };
            }

            ActiveOffensivePlan = null;
        }

        private bool EstimatePlanAssault(OffensivePlan plan, out GroundCombatEstimate estimate)
        {
            return EstimateAssignments(plan.TargetTileId, plan.Assignments, out estimate);
        }

        private bool EstimateAssignments(
            Vector3Int targetTileId,
            IEnumerable<OffensivePlanAssignment> assignments,
            out GroundCombatEstimate estimate)
        {
            var assaultingDivisionIds = assignments
                .Where(assignment => assignment.Intent == GroundOrderAIIntent.Assault
                                     || assignment.Intent == GroundOrderAIIntent.SupportAttack)
                .Select(assignment => assignment.DivisionId);

            return GroundCombatEstimationService.TryEstimateTileAssault(
                _gameManager,
                assaultingDivisionIds,
                targetTileId,
                GroundCombatAssaultIntent.Capture,
                out estimate);
        }

        private IEnumerable<Vector3Int> GetOffensiveTargetCandidates(Alliance hostileAlliance)
        {
            var candidates = new HashSet<Vector3Int>();
            foreach (var frontTileId in _frontTileIds)
            {
                foreach (var neighborTileId in GetNeighborTileIds(frontTileId))
                {
                    if (IsHostileControlledLandTile(neighborTileId))
                        candidates.Add(neighborTileId);
                }
            }

            return OrderTileIds(candidates)
                .OrderByDescending(GetTileStrategicValue);
        }

        private bool TryChooseStagingTile(
            Division division,
            IReadOnlyCollection<Vector3Int> stagingTiles,
            out Vector3Int stagingTileId,
            out GroundPath path)
        {
            stagingTileId = default;
            path = null;
            var bestPathLength = int.MaxValue;

            foreach (var candidateTileId in OrderTileIds(stagingTiles))
            {
                if (!GroundPathfindingService.TryFindFriendlyPath(
                        _gameManager,
                        division.TileId,
                        candidateTileId,
                        Alliance,
                        out var candidatePath))
                    continue;

                if (candidatePath.StepCount > MaxOffensivePathLength || candidatePath.StepCount >= bestPathLength)
                    continue;

                stagingTileId = candidateTileId;
                path = candidatePath;
                bestPathLength = candidatePath.StepCount;
            }

            return path != null;
        }

        private IEnumerable<Vector3Int> GetFriendlyStagingTiles(Vector3Int targetTileId)
        {
            return GetNeighborTileIds(targetTileId)
                .Where(IsFriendlyControlledLandTile)
                .Where(tileId => GroundPathfindingService.IsSafeFriendlyWaypoint(_gameManager, tileId, Alliance));
        }

        private bool HasProjectedDefensiveCoverage(Vector3Int frontTileId)
        {
            return CountProjectedDefenders(frontTileId) > 0;
        }

        private int CountProjectedDefenders(Vector3Int frontTileId)
        {
            var physicalCount = CountEligiblePhysicalDefenders(frontTileId);
            var committedCount = GetAllianceDivisions()
                .Count(division => division.CurrentOrder is MoveGroundOrder moveOrder
                                   && moveOrder.AIIntent == GroundOrderAIIntent.RefillFront
                                   && moveOrder.DestinationTileId == frontTileId
                                   && IsCombatReady(division));

            return physicalCount + committedCount;
        }

        private int CountEligiblePhysicalDefenders(Vector3Int tileId)
        {
            return GetPhysicalDefenders(tileId).Count();
        }

        private IEnumerable<Division> GetPhysicalDefenders(Vector3Int tileId)
        {
            return _gameManager.divisionSystem.GetDivisionsOnTile(tileId)
                .Where(division => GroundSystemUtility.TryGetDivisionAlliance(_gameManager, division, out var alliance)
                                   && alliance == Alliance
                                   && IsCombatReady(division));
        }

        private IEnumerable<Division> GetAdjacentHostileDivisions(Vector3Int tileId)
        {
            return GetNeighborTileIds(tileId)
                .SelectMany(neighborTileId => _gameManager.divisionSystem.GetDivisionsOnTile(neighborTileId))
                .Where(division => GroundSystemUtility.TryGetDivisionAlliance(_gameManager, division, out var alliance)
                                   && GroundSystemUtility.AreHostile(Alliance, alliance)
                                   && IsCombatReady(division));
        }

        private bool HasHostilePhysicalDefender(Vector3Int tileId)
        {
            return _gameManager.divisionSystem.GetDivisionsOnTile(tileId)
                .Any(division => GroundSystemUtility.TryGetDivisionAlliance(_gameManager, division, out var alliance)
                                 && GroundSystemUtility.AreHostile(Alliance, alliance)
                                 && IsCombatReady(division));
        }

        private float SumCombatPower(IEnumerable<Division> divisions)
        {
            return divisions.Sum(GroundCombatEstimationService.CalculateDivisionCombatPower);
        }

        private float GetTileStrategicValue(Vector3Int tileId)
        {
            var value = 0f;
            if (GroundSystemUtility.TryGetLandTileData(_gameManager, tileId, out var landTileData))
                value += landTileData.Infrastructure?.FunctionalLevel ?? 0;

            foreach (var building in _gameManager.buildingSystem.GetBuildingsOnTile(tileId))
            {
                var functionalLevel = Mathf.Max(1, building.FunctionalLevel);
                value += building.Type switch
                {
                    BuildingType.Airport => 7f,
                    BuildingType.SupplyHub => 7f,
                    BuildingType.Port => 5f,
                    BuildingType.Factory => 4f,
                    BuildingType.Refinery => 4f,
                    BuildingType.PowerPlant => 4f,
                    BuildingType.Railroad => 3f,
                    BuildingType.Fort => 3f,
                    _ => 1f
                } * functionalLevel;
            }

            value += GetSupplyStrategicValue(tileId);
            return value;
        }

        private float GetSupplyStrategicValue(Vector3Int tileId)
        {
            _supplyStrategicValueByTileId ??= SupplyStrategicValueService.BuildSupplyStrategicValueLookup(_gameManager);
            return _supplyStrategicValueByTileId.TryGetValue(tileId, out var value) ? value : 0f;
        }

        private static float NormalizeStrategicValue(float value)
        {
            return Mathf.Clamp01(value / 20f);
        }

        private float GetOffensivePlanUtility(GroundCombatEstimate estimate, Vector3Int targetTileId)
        {
            var baseUtility = estimate.VictoryLikelihood * CombatUtilityWeight
                              + NormalizeStrategicValue(GetTileStrategicValue(targetTileId)) * StrategicValueUtilityWeight;
            var randomMultiplier = Random.Range(
                1f - OffensivePlanUtilityRandomVariance,
                1f + OffensivePlanUtilityRandomVariance);

            return baseUtility * randomMultiplier;
        }

        private bool CanReceiveAIOrder(Division division)
        {
            if (!CanReplaceOrder(division))
                return false;

            if (_gameManager.IsDivisionEngagedInGroundCombat(division.DivisionId))
                return false;

            return division.CurrentOrder is not MoveGroundOrder;
        }

        private static bool CanReplaceOrder(Division division)
        {
            if (division == null)
                return false;

            if (GroundSystemUtility.IsRetreating(division))
                return false;

            return division.CurrentOrder == null || division.CurrentOrder.CanBeReplaced;
        }

        private static bool IsCombatReady(Division division)
        {
            return division != null
                   && division.Strength >= 1f
                   && division.Organization >= 1f
                   && !GroundSystemUtility.IsRetreating(division);
        }

        private static bool IsOffensiveIntent(GroundOrderAIIntent intent)
        {
            return intent == GroundOrderAIIntent.Assault
                   || intent == GroundOrderAIIntent.SupportAttack
                   || intent == GroundOrderAIIntent.Pin
                   || intent == GroundOrderAIIntent.OffensiveStaging;
        }

        private int CountAdjacentHostileTiles(Vector3Int tileId, Alliance hostileAlliance)
        {
            return GetNeighborTileIds(tileId)
                .Count(neighborTileId => GroundSystemUtility.TryGetLandTileData(
                                             _gameManager,
                                             neighborTileId,
                                             out var landTileData)
                                         && landTileData.Controller == hostileAlliance);
        }

        private bool DoesCapturingTargetRemoveSourceFromFront(Vector3Int sourceTileId, Vector3Int targetTileId)
        {
            if (!_frontTileIds.Contains(sourceTileId))
                return false;

            if (!GroundSystemUtility.AreNeighbors(_gameManager, sourceTileId, targetTileId))
                return false;

            if (!IsHostileControlledLandTile(targetTileId))
                return false;

            return GetNeighborTileIds(sourceTileId)
                .Where(neighborTileId => neighborTileId != targetTileId)
                .All(neighborTileId => !IsHostileControlledLandTile(neighborTileId));
        }

        private bool IsFriendlyControlledLandTile(Vector3Int tileId)
        {
            return GroundSystemUtility.TryGetLandTileData(_gameManager, tileId, out var landTileData)
                   && landTileData.Controller == Alliance;
        }

        private bool IsHostileControlledLandTile(Vector3Int tileId)
        {
            return GroundSystemUtility.TryGetLandTileData(_gameManager, tileId, out var landTileData)
                   && GroundSystemUtility.AreHostile(Alliance, landTileData.Controller);
        }

        private IEnumerable<Vector3Int> GetNeighborTileIds(Vector3Int tileId)
        {
            return GroundSystemUtility.GetNeighborTileIds(_gameManager, tileId);
        }

        private IEnumerable<Division> GetAllianceDivisions()
        {
            return _gameManager.divisionSystem.Divisions
                .Where(division => GroundSystemUtility.TryGetDivisionAlliance(_gameManager, division, out var alliance)
                                   && alliance == Alliance);
        }

        private static bool TryGetHostileAlliance(Alliance alliance, out Alliance hostileAlliance)
        {
            switch (alliance)
            {
                case Alliance.Bluefor:
                    hostileAlliance = Alliance.Redfor;
                    return true;
                case Alliance.Redfor:
                    hostileAlliance = Alliance.Bluefor;
                    return true;
                default:
                    hostileAlliance = default;
                    return false;
            }
        }

        private static Dictionary<Vector3Int, Alliance> BuildControllerLookup(IReadOnlyList<TileData> tiles)
        {
            var controllersByTileId = new Dictionary<Vector3Int, Alliance>();
            if (tiles == null)
                return controllersByTileId;

            foreach (var tileData in tiles)
            {
                if (tileData is LandTileData landTileData)
                    controllersByTileId[tileData.TileId] = landTileData.Controller;
            }

            return controllersByTileId;
        }

        private static Dictionary<Vector3Int, List<Vector3Int>> BuildNeighborLookup(IReadOnlyList<Tile> tiles)
        {
            var neighborsByTileId = new Dictionary<Vector3Int, List<Vector3Int>>();
            if (tiles == null)
                return neighborsByTileId;

            foreach (var tile in tiles)
            {
                if (tile == null)
                    continue;

                neighborsByTileId[tile.Coordinates] =
                    tile.NeighborTileIds ?? new List<Vector3Int>();
            }

            return neighborsByTileId;
        }

        private static IOrderedEnumerable<Vector3Int> OrderTileIds(IEnumerable<Vector3Int> tileIds)
        {
            return (tileIds ?? Enumerable.Empty<Vector3Int>())
                .OrderBy(tileId => tileId.x)
                .ThenBy(tileId => tileId.y)
                .ThenBy(tileId => tileId.z);
        }
    }

    [Serializable]
    public sealed class OffensivePlan
    {
        public Vector3Int TargetTileId;
        public OffensivePlanPhase Phase = OffensivePlanPhase.Assemble;
        public List<OffensivePlanAssignment> Assignments = new List<OffensivePlanAssignment>();

        public OffensivePlan(Vector3Int targetTileId, IEnumerable<OffensivePlanAssignment> assignments)
        {
            TargetTileId = targetTileId;
            Assignments = assignments?.ToList() ?? new List<OffensivePlanAssignment>();
        }
    }

    public enum OffensivePlanPhase
    {
        Assemble,
        Attack
    }

    [Serializable]
    public sealed class OffensivePlanAssignment
    {
        public Guid DivisionId;
        public GroundOrderAIIntent Intent;
        public Vector3Int StagingTileId;
        public Vector3Int EngagementTileId;
        public GroundPath StagingPath;

        public OffensivePlanAssignment(
            Guid divisionId,
            GroundOrderAIIntent intent,
            Vector3Int stagingTileId,
            Vector3Int engagementTileId,
            GroundPath stagingPath)
        {
            DivisionId = divisionId;
            Intent = intent;
            StagingTileId = stagingTileId;
            EngagementTileId = engagementTileId;
            StagingPath = stagingPath;
        }
    }
}
