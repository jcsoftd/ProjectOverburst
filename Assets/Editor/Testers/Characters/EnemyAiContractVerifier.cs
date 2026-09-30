using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 2026-10-01: EnemyStatePatternPlayModeVerifier에서 옮긴 AI 계약 검사. 옛 머록·StageMonster 프리팹이 없어도 성립하는
// 순수 계산기·서비스 계약만 모았다(틱 LOD, 접근·우회·부채꼴 조향, 분대 추격 계획, Flow Field, 옛 포위 계획 제거, 파티 대상 단계).
// 에디트 모드에서 실행하며 씬·자산을 쓰지 않는다. 임시 오브젝트는 HideAndDontSave로 만들고 끝나면 지운다.
public static class EnemyAiContractVerifier
{
    [MenuItem("OVERBURST/Codex/Validation/Verify Enemy AI Contracts (Edit Mode)")]
    public static void RunFromMenu()
    {
        RunAll();
        Debug.Log("[EnemyAiContractVerifier] PASS all AI contracts");
    }

    public static void RunAll()
    {
        VerifyAiTickPolicy();
        VerifyReservationFreeApproachSteeringCalculator();
        VerifySquadPursuitPlannerContract();
        VerifyClusterFanOutSteeringCalculator();
        VerifyChaseBypassSteeringCalculator();
        VerifyFlowFieldCalculatorContract();
        VerifySharedFlowFieldServiceContract();
        VerifyLegacySurroundRemovalContract();
        VerifyPartyTargetPhasePolicy();
    }

    public static void VerifySquadPursuitPlannerPureContract()
    {
        VerifySquadPursuitPlannerContract();
    }

    private static void VerifyAiTickPolicy()
    {
        float farSqr = 80f * 80f;
        if (EnemyAiTickScheduler.ResolveInterval(40, farSqr, false, false) != 0f)
            throw new System.InvalidOperationException("40-enemy AI policy must remain full rate");
        if (Mathf.Abs(EnemyAiTickScheduler.ResolveInterval(100, farSqr, false, false) - 0.1f) > 0.001f)
            throw new System.InvalidOperationException("100-enemy hidden AI policy must use 0.1s ticks");
        if (Mathf.Abs(EnemyAiTickScheduler.ResolveInterval(200, farSqr, false, false) - 0.25f) > 0.001f)
            throw new System.InvalidOperationException("200-enemy hidden AI policy must use 0.25s ticks");
        if (Mathf.Abs(EnemyAiTickScheduler.ResolveInterval(201, farSqr, false, false) - 0.5f) > 0.001f)
            throw new System.InvalidOperationException("200+ very-far hidden AI policy must use 0.5s ticks");
        if (EnemyAiTickScheduler.ResolveInterval(200, 10f * 10f, false, false) != 0f)
            throw new System.InvalidOperationException("near combat AI must remain full rate");
        if (EnemyAiTickScheduler.ResolveInterval(200, farSqr, false, true) != 0f)
            throw new System.InvalidOperationException("urgent AI event must bypass tick LOD");

        HashSet<int> staggerSlots = new HashSet<int>();
        for (int i = 0; i < 200; i++)
        {
            float delay = EnemyAiTickScheduler.ResolveStaggerDelay(0.25f, i + 1);
            staggerSlots.Add(Mathf.FloorToInt(delay / 0.01f));
        }
        if (staggerSlots.Count < 12)
            throw new System.InvalidOperationException("AI tick staggering produced too few time slots");

        Debug.Log("[EnemyAiContractVerifier] Passed 40/100/200 AI tick LOD policy and stable staggering");
    }
    private static void VerifyReservationFreeApproachSteeringCalculator()
    {
        List<EnemyApproachNeighbor> neighbors = new List<EnemyApproachNeighbor>(4);
        EnemyApproachSteeringResult farResult = EnemyApproachSteering.Resolve(
            new EnemyApproachSteeringInput(
                new Vector3(0f, 0f, 8f),
                Vector3.zero,
                Vector3.back,
                Vector3.zero,
                1.7f,
                0.6f,
                101),
            neighbors);
        if (farResult.LocalBlend > 0.0001f || Vector3.Dot(farResult.Direction, Vector3.back) < 0.999f)
        {
            throw new System.InvalidOperationException(
                "Far approach did not preserve navigation direction blend=" + farResult.LocalBlend
                + " direction=" + farResult.Direction);
        }

        EnemyApproachSteeringResult closeResult = EnemyApproachSteering.Resolve(
            new EnemyApproachSteeringInput(
                new Vector3(0f, 0f, 1f),
                Vector3.zero,
                Vector3.back,
                Vector3.zero,
                1.7f,
                0.6f,
                102),
            neighbors);
        if (Vector3.Dot(closeResult.RadialCorrection, Vector3.forward) < 0.8f
            || Vector3.Dot(closeResult.Direction, Vector3.forward) < 0.5f)
        {
            throw new System.InvalidOperationException(
                "Too-close approach did not steer outward radial=" + closeResult.RadialCorrection
                + " direction=" + closeResult.Direction);
        }

        neighbors.Add(new EnemyApproachNeighbor(new Vector3(0.45f, 0f, 1.6f), 0.6f));
        neighbors.Add(new EnemyApproachNeighbor(new Vector3(0.7f, 0f, 2f), 0.8f));
        EnemyApproachSteeringInput densityInput = new EnemyApproachSteeringInput(
            new Vector3(0f, 0f, 2.2f),
            Vector3.zero,
            Vector3.back,
            Vector3.zero,
            1.7f,
            0.6f,
            103);
        EnemyApproachSteeringResult leftCrowded = EnemyApproachSteering.Resolve(densityInput, neighbors);
        if (leftCrowded.LeftDensity <= leftCrowded.RightDensity
            || leftCrowded.TurnSign != -1
            || Vector3.Dot(leftCrowded.Direction, Vector3.left) < 0.2f)
        {
            throw new System.InvalidOperationException(
                "Left density did not produce a right tangent left=" + leftCrowded.LeftDensity
                + " right=" + leftCrowded.RightDensity
                + " sign=" + leftCrowded.TurnSign
                + " direction=" + leftCrowded.Direction);
        }

        neighbors.Clear();
        neighbors.Add(new EnemyApproachNeighbor(new Vector3(-0.45f, 0f, 1.6f), 0.6f));
        neighbors.Add(new EnemyApproachNeighbor(new Vector3(-0.7f, 0f, 2f), 0.8f));
        EnemyApproachSteeringResult rightCrowded = EnemyApproachSteering.Resolve(densityInput, neighbors);
        if (rightCrowded.RightDensity <= rightCrowded.LeftDensity
            || rightCrowded.TurnSign != 1
            || Vector3.Dot(rightCrowded.Direction, Vector3.right) < 0.2f)
        {
            throw new System.InvalidOperationException(
                "Right density did not produce a left tangent left=" + rightCrowded.LeftDensity
                + " right=" + rightCrowded.RightDensity
                + " sign=" + rightCrowded.TurnSign
                + " direction=" + rightCrowded.Direction);
        }

        if (EnemyApproachSteering.ResolveTurnSign(1, 4f, 0f, false, 104) != 1)
            throw new System.InvalidOperationException("Turn lock did not preserve the current side");
        if (EnemyApproachSteering.ResolveTurnSign(1, 1f, 0.8f, true, 104) != 1)
            throw new System.InvalidOperationException("Turn hysteresis switched on less than 25 percent improvement");
        if (EnemyApproachSteering.ResolveTurnSign(1, 1f, 0.7f, true, 104) != -1)
            throw new System.InvalidOperationException("Turn hysteresis did not switch on more than 25 percent improvement");
        int stableTurn = EnemyApproachSteering.ResolveStableTurnSign(105);
        if (EnemyApproachSteering.ResolveTurnSign(0, 1f, 1f, true, 105) != stableTurn)
            throw new System.InvalidOperationException("Equal density did not use the stable turn side");

        EnemyApproachSteeringResult noSeparation = EnemyApproachSteering.Resolve(
            new EnemyApproachSteeringInput(
                new Vector3(0f, 0f, 4f),
                Vector3.zero,
                Vector3.back,
                Vector3.right * 0.25f,
                1.7f,
                0.6f,
                106,
                separationWeight: 0f),
            null);
        EnemyApproachSteeringResult weightedSeparation = EnemyApproachSteering.Resolve(
            new EnemyApproachSteeringInput(
                new Vector3(0f, 0f, 4f),
                Vector3.zero,
                Vector3.back,
                Vector3.right * 0.25f,
                1.7f,
                0.6f,
                106,
                separationWeight: 2f),
            null);
        if (Mathf.Abs(noSeparation.Direction.x) > 0.001f
            || Vector3.Dot(weightedSeparation.Direction, Vector3.right) < 0.2f)
        {
            throw new System.InvalidOperationException(
                "Separation pressure or role weight was discarded zero=" + noSeparation.Direction
                + " weighted=" + weightedSeparation.Direction);
        }

        if (Mathf.Abs(EnemyApproachSteering.ResolveNeighborQueryRadius(0.3f) - 2f) > 0.0001f
            || Mathf.Abs(EnemyApproachSteering.ResolveNeighborQueryRadius(0.8f) - 3.2f) > 0.0001f)
        {
            throw new System.InvalidOperationException("Body-radius neighbor query contract changed");
        }

        Vector3 origin = new Vector3(2f, 3f, 4f);
        Vector3 minimumDestination = EnemyApproachSteering.ResolveShortHorizonDestination(
            origin,
            new Vector3(10f, 5f, 0f),
            0f);
        Vector3 maximumDestination = EnemyApproachSteering.ResolveShortHorizonDestination(
            origin,
            Vector3.right,
            10f);
        Vector3 stoppedDestination = EnemyApproachSteering.ResolveShortHorizonDestination(
            origin,
            Vector3.zero,
            10f);
        if (Mathf.Abs(Vector3.Distance(origin, minimumDestination)
                - EnemyApproachSteering.MinimumShortHorizonDistance) > 0.0001f
            || Mathf.Abs(Vector3.Distance(origin, maximumDestination)
                - EnemyApproachSteering.MaximumShortHorizonDistance) > 0.0001f
            || Mathf.Abs(minimumDestination.y - origin.y) > 0.0001f
            || stoppedDestination != origin)
        {
            throw new System.InvalidOperationException(
                "Short-horizon destination contract failed min=" + minimumDestination
                + " max=" + maximumDestination
                + " stopped=" + stoppedDestination);
        }

        Debug.Log(
            "[EnemyAiContractVerifier] Passed reservation-free approach steering density, radius, hysteresis and short horizon");
    }
    private static void VerifyFlowFieldCalculatorContract()
    {
        const int width = 12;
        const int height = 7;
        const int wallX = 5;
        const int gapZ = 6;
        bool[,] cells = CreateFlowFieldCells(width, height, wallX, gapZ);
        RunWalkableArea area =
            new RunWalkableArea(cells, width, height, 0f, 0f, 1f);
        EnemyFlowField field = new EnemyFlowField(area, 10, 3);
        int firstAdvance = field.Advance(3);
        if (firstAdvance != 3 || field.IsComplete)
            throw new System.InvalidOperationException("Flow Field incremental build budget was ignored");

        int guard = width * height + 1;
        while (!field.IsComplete && guard-- > 0)
        {
            if (field.Advance(7) > 7)
                throw new System.InvalidOperationException("Flow Field exceeded one incremental build budget");
        }
        if (!field.IsComplete || guard <= 0 || field.ProcessedCellCount > width * height)
            throw new System.InvalidOperationException("Flow Field did not complete inside the cell count bound");

        Vector3 current = ResolveCellCenter(area, 1, 3);
        if (!field.TryGetDirection(current, out Vector3 firstDirection, out Vector3 firstWaypoint)
            || firstDirection.z <= 0.1f
            || !area.IsWalkable(firstWaypoint))
        {
            throw new System.InvalidOperationException(
                "Flow Field did not route toward the wall gap direction=" + firstDirection
                + " waypoint=" + firstWaypoint);
        }

        bool reachedTarget = false;
        for (int step = 0; step < width * height; step++)
        {
            if (area.TryGetCell(current, out int currentX, out int currentZ)
                && currentX == field.TargetX
                && currentZ == field.TargetZ)
            {
                reachedTarget = true;
                break;
            }

            if (!field.TryGetDirection(current, out _, out Vector3 waypoint)
                || !area.IsWalkable(waypoint))
            {
                throw new System.InvalidOperationException("Flow Field path left the walkable cells step=" + step);
            }
            current = waypoint;
        }
        if (!reachedTarget)
            throw new System.InvalidOperationException("Flow Field path did not reach its target cell");

        bool[,] blockedCells = CreateFlowFieldCells(width, height, wallX, -1);
        RunWalkableArea blockedArea = new RunWalkableArea(
            blockedCells,
            width,
            height,
            0f,
            0f,
            1f);
        EnemyFlowField blockedField = new EnemyFlowField(blockedArea, 10, 3);
        blockedField.Advance(width * height);
        if (blockedField.TryGetDirection(ResolveCellCenter(blockedArea, 1, 3), out _, out _))
            throw new System.InvalidOperationException("Disconnected Flow Field returned a false route");

        if (!field.Rebuild(10, 4))
            throw new System.InvalidOperationException("Flow Field target-cell rebuild failed");
        field.Advance(width * height);
        if (!field.TryGetIntegrationCost(10, 4, out int targetCost) || targetCost != 0)
            throw new System.InvalidOperationException("Flow Field target-cell rebuild retained stale integration data");

        Debug.Log(
            "[EnemyAiContractVerifier] Passed incremental Flow Field detour, corner safety, unreachable and rebuild contracts");
    }
    private static void VerifyChaseBypassSteeringCalculator()
    {
        Vector3 agentPosition = new Vector3(0f, 0f, 8f);
        List<EnemyApproachNeighbor> blockers = new List<EnemyApproachNeighbor>
        {
            new EnemyApproachNeighbor(new Vector3(0f, 0f, 6.5f), 0.6f),
            new EnemyApproachNeighbor(new Vector3(0f, 0f, 5f), 0.6f)
        };
        EnemyChaseBypassInput input = new EnemyChaseBypassInput(
            agentPosition,
            Vector3.zero,
            Vector3.back,
            Vector3.zero,
            0.6f,
            201);
        EnemyChaseBypassResult result = EnemyChaseBypassSteering.Resolve(input, blockers);
        if (!result.IsActive
            || result.ForwardBlockerCount != 2
            || Mathf.Abs(result.Direction.x) < 0.5f
            || Vector3.Dot(result.Direction, Vector3.back) <= 0.1f
            || result.SpeedMultiplier < EnemyChaseBypassSteering.MinimumSpeedMultiplier
            || result.SpeedMultiplier > EnemyChaseBypassSteering.MaximumSpeedMultiplier)
        {
            throw new System.InvalidOperationException(
                "Chase bypass did not produce a fast spiral direction blockers=" + result.ForwardBlockerCount
                + " direction=" + result.Direction
                + " speed=" + result.SpeedMultiplier);
        }

        blockers.Clear();
        blockers.Add(new EnemyApproachNeighbor(new Vector3(0.5f, 0f, 6.5f), 0.6f));
        blockers.Add(new EnemyApproachNeighbor(new Vector3(0.8f, 0f, 5f), 0.6f));
        EnemyChaseBypassResult leftCrowded = EnemyChaseBypassSteering.Resolve(input, blockers);
        if (!leftCrowded.IsActive
            || leftCrowded.LeftDensity <= leftCrowded.RightDensity
            || leftCrowded.TurnSign != -1
            || Vector3.Dot(leftCrowded.Direction, Vector3.left) < 0.5f)
        {
            throw new System.InvalidOperationException(
                "Left queue did not bypass toward the open right side left=" + leftCrowded.LeftDensity
                + " right=" + leftCrowded.RightDensity
                + " sign=" + leftCrowded.TurnSign
                + " direction=" + leftCrowded.Direction);
        }

        blockers.RemoveAt(1);
        EnemyChaseBypassResult oneBlocker = EnemyChaseBypassSteering.Resolve(input, blockers);
        EnemyChaseBypassResult farResult = EnemyChaseBypassSteering.Resolve(
            new EnemyChaseBypassInput(
                new Vector3(0f, 0f, 12f),
                Vector3.zero,
                Vector3.back,
                Vector3.zero,
                0.6f,
                202),
            new List<EnemyApproachNeighbor>
            {
                new EnemyApproachNeighbor(new Vector3(0f, 0f, 10.5f), 0.6f),
                new EnemyApproachNeighbor(new Vector3(0f, 0f, 9f), 0.6f)
            });
        blockers.Add(new EnemyApproachNeighbor(new Vector3(0.8f, 0f, 5f), 0.6f));
        EnemyChaseBypassResult detourResult = EnemyChaseBypassSteering.Resolve(
            new EnemyChaseBypassInput(
                agentPosition,
                Vector3.zero,
                Vector3.right,
                Vector3.zero,
                0.6f,
                203),
            blockers);
        if (oneBlocker.IsActive || farResult.IsActive || detourResult.IsActive)
        {
            throw new System.InvalidOperationException(
                "Chase bypass ignored activation guards one=" + oneBlocker.IsActive
                + " far=" + farResult.IsActive
                + " detour=" + detourResult.IsActive);
        }

        Debug.Log(
            "[EnemyAiContractVerifier] Passed Chase forward blockage, spiral bypass, side choice and speed boost contracts");
    }
    private static void VerifyClusterFanOutSteeringCalculator()
    {
        EnemyClusterFanOutMemberData rearMember = new EnemyClusterFanOutMemberData(
            50,
            new Vector3(0f, 0f, 6f),
            Vector3.back,
            Vector3.left,
            -2f,
            0.4f,
            0.9f,
            1.2f,
            4.9f,
            1f,
            1);
        EnemyClusterFanOutResult rearResult = EnemyClusterFanOutSteering.Resolve(
            new EnemyClusterFanOutInput(
                rearMember,
                Vector3.back,
                Vector3.zero,
                0,
                301));
        if (!rearResult.IsActive
            || Vector3.Dot(rearResult.Direction, Vector3.back) <= 0.1f
            || Vector3.Dot(rearResult.Direction, Vector3.left) <= 0.5f
            || rearResult.SpeedMultiplier < EnemyClusterFanOutSteering.MinimumSpeedMultiplier
            || rearResult.SpeedMultiplier > EnemyClusterFanOutSteering.MaximumSpeedMultiplier)
        {
            throw new System.InvalidOperationException(
                "Cluster rear row did not fan out direction=" + rearResult.Direction
                + " speed=" + rearResult.SpeedMultiplier);
        }

        EnemyClusterFanOutResult lockedSideResult = EnemyClusterFanOutSteering.Resolve(
            new EnemyClusterFanOutInput(
                rearMember,
                Vector3.back,
                Vector3.zero,
                -1,
                301));
        if (!lockedSideResult.IsActive
            || lockedSideResult.SideSign != -1
            || Vector3.Dot(lockedSideResult.Direction, Vector3.right) <= 0.5f)
        {
            throw new System.InvalidOperationException(
                "Cluster fan-out did not preserve its assigned side sign="
                + lockedSideResult.SideSign
                + " direction=" + lockedSideResult.Direction);
        }

        EnemyClusterFanOutMemberData frontMember = new EnemyClusterFanOutMemberData(
            50,
            Vector3.zero,
            Vector3.back,
            Vector3.left,
            2f,
            -0.4f,
            0.1f,
            1.2f,
            4.9f,
            1f,
            -1);
        EnemyClusterFanOutResult frontResult = EnemyClusterFanOutSteering.Resolve(
            new EnemyClusterFanOutInput(
                frontMember,
                Vector3.back,
                Vector3.zero,
                0,
                302));
        EnemyClusterFanOutMemberData smallMember = new EnemyClusterFanOutMemberData(
            7,
            Vector3.zero,
            Vector3.back,
            Vector3.left,
            -1f,
            0.2f,
            0.9f,
            0.5f,
            2f,
            1f,
            1);
        EnemyClusterFanOutResult smallResult = EnemyClusterFanOutSteering.Resolve(
            new EnemyClusterFanOutInput(
                smallMember,
                Vector3.back,
                Vector3.zero,
                0,
                303));
        if (frontResult.IsActive || smallResult.IsActive)
        {
            throw new System.InvalidOperationException(
                "Cluster fan-out ignored front-row or minimum-size guards front="
                + frontResult.IsActive
                + " small=" + smallResult.IsActive);
        }

        Debug.Log(
            "[EnemyAiContractVerifier] Passed cluster front hold, rear fan-out, side lock and speed contracts");
    }
    private static void VerifySquadPursuitPlannerContract()
    {
        VerifyMaximumFirstSquadSizes(4, 8, 0);
        VerifyMaximumFirstSquadSizes(4, 8, 3);
        VerifyMaximumFirstSquadSizes(4, 8, 4, 4);
        VerifyMaximumFirstSquadSizes(4, 8, 7, 7);
        VerifyMaximumFirstSquadSizes(4, 8, 8, 8);
        VerifyMaximumFirstSquadSizes(4, 8, 9, 8);
        VerifyMaximumFirstSquadSizes(4, 8, 11, 8);
        VerifyMaximumFirstSquadSizes(4, 8, 12, 8, 4);
        VerifyMaximumFirstSquadSizes(4, 8, 15, 8, 7);
        VerifyMaximumFirstSquadSizes(4, 8, 16, 8, 8);
        VerifyMaximumFirstSquadSizes(4, 8, 20, 8, 8, 4);
        VerifyMaximumFirstSquadSizes(4, 8, 41, 8, 8, 8, 8, 8);

        VerifyMaximumFirstSquadSizes(8, 12, 0);
        VerifyMaximumFirstSquadSizes(8, 12, 7);
        VerifyMaximumFirstSquadSizes(8, 12, 8, 8);
        VerifyMaximumFirstSquadSizes(8, 12, 11, 11);
        VerifyMaximumFirstSquadSizes(8, 12, 12, 12);
        VerifyMaximumFirstSquadSizes(8, 12, 13, 12);
        VerifyMaximumFirstSquadSizes(8, 12, 19, 12);
        VerifyMaximumFirstSquadSizes(8, 12, 20, 12, 8);
        VerifyMaximumFirstSquadSizes(8, 12, 23, 12, 11);
        VerifyMaximumFirstSquadSizes(8, 12, 24, 12, 12);
        VerifyMaximumFirstSquadSizes(8, 12, 25, 12, 12);
        VerifyMaximumFirstSquadSizes(8, 12, 41, 12, 12, 12);

        VerifyMaximumFirstSquadRange(4, 8);
        VerifyMaximumFirstSquadRange(8, 12);

        Vector3 player = Vector3.zero;
        Vector3 directOutward = Vector3.right;
        Vector3 changedOutward = EnemySquadPursuitPlanner.ResolveDirectOutward(
            player,
            Vector3.forward * 12f);
        if (Vector3.Dot(changedOutward, Vector3.forward) < 0.99f
            || Mathf.Abs(Vector3.Dot(changedOutward, directOutward)) > 0.01f)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit direct axis did not follow the current nearest squad center");
        }
        List<EnemySquadPursuitSlot> slots = new List<EnemySquadPursuitSlot>(8);
        EnemySquadPursuitPlanner.BuildSlots(
            player,
            directOutward,
            EnemySquadPursuitPlanner.DefaultSlotRadius,
            slots);
        EnemySquadPursuitSlot directSlot = FindSlotByKind(slots, EnemySquadPursuitSlotKind.Direct);
        EnemySquadPursuitSlot rightSlot = FindSlotByKind(slots, EnemySquadPursuitSlotKind.RightBypass);
        EnemySquadPursuitSlot rearSlot = FindSlotByKind(slots, EnemySquadPursuitSlotKind.Rear);
        if (slots.Count != 8
            || Vector3.Dot(directSlot.OutwardDirection, directOutward) < 0.99f
            || Mathf.Abs(Vector3.Dot(rightSlot.OutwardDirection, directOutward)) > 0.01f
            || Vector3.Dot(rearSlot.OutwardDirection, directOutward) > -0.99f)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit slots did not preserve direct, side and rear axes");
        }
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].Index != i
                || slots[i].PointIndex != i
                || Mathf.Abs(slots[i].Radius - EnemySquadPursuitPlanner.DefaultSlotRadius) > 0.0001f)
            {
                throw new System.InvalidOperationException(
                    "Squad pursuit physical point order or uniform radius changed index="
                    + i + " radius=" + slots[i].Radius);
            }
        }

        List<EnemySquadPursuitSlot> changedSlots = new List<EnemySquadPursuitSlot>(8);
        EnemySquadPursuitPlanner.BuildSlots(
            player,
            changedOutward,
            EnemySquadPursuitPlanner.DefaultSlotRadius,
            changedSlots);
        EnemySquadPursuitSlot changedDirectSlot = FindSlotByKind(
            changedSlots,
            EnemySquadPursuitSlotKind.Direct);
        if (changedSlots.Count != 8
            || changedDirectSlot.PointIndex == directSlot.PointIndex
            || Vector3.Dot(changedDirectSlot.OutwardDirection, changedOutward) < 0.99f)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit direct role did not move to the nearest fixed point");
        }
        for (int pointIndex = 0; pointIndex < 8; pointIndex++)
        {
            EnemySquadPursuitSlot before = FindSlotAtPoint(slots, pointIndex);
            EnemySquadPursuitSlot after = FindSlotAtPoint(changedSlots, pointIndex);
            if ((before.Position - after.Position).sqrMagnitude > 0.0001f)
            {
                throw new System.InvalidOperationException(
                    "Squad pursuit fixed point moved while its role changed point=P" + pointIndex);
            }
        }
        for (int directPointIndex = 0; directPointIndex < 8; directPointIndex++)
        {
            Vector3 axis = Quaternion.AngleAxis(directPointIndex * 45f, Vector3.up) * Vector3.right;
            EnemySquadPursuitPlanner.BuildSlots(
                player,
                axis,
                EnemySquadPursuitPlanner.DefaultSlotRadius,
                changedSlots);
            changedDirectSlot = FindSlotByKind(changedSlots, EnemySquadPursuitSlotKind.Direct);
            if (changedDirectSlot.PointIndex != directPointIndex)
            {
                throw new System.InvalidOperationException(
                    "Squad pursuit direct role selected the wrong fixed point expected=P"
                    + directPointIndex + " actual=P" + changedDirectSlot.PointIndex);
            }
            for (int pointIndex = 0; pointIndex < changedSlots.Count; pointIndex++)
            {
                EnemySquadPursuitSlot fixedPoint = FindSlotAtPoint(slots, pointIndex);
                EnemySquadPursuitSlot remappedPoint = FindSlotAtPoint(changedSlots, pointIndex);
                EnemySquadPursuitSlotKind expectedKind = (EnemySquadPursuitSlotKind)(
                    (pointIndex - directPointIndex + 8) % 8);
                if ((fixedPoint.Position - remappedPoint.Position).sqrMagnitude > 0.0001f
                    || remappedPoint.Kind != expectedKind)
                {
                    throw new System.InvalidOperationException(
                        "Squad pursuit 8-direction role sweep changed a fixed point or role mapping axis=P"
                        + directPointIndex + " point=P" + pointIndex);
                }
            }
        }

        EnemySquadPursuitPlanner.BuildSlots(
            player,
            directOutward,
            EnemySquadPursuitPlanner.DefaultSlotRadius,
            slots);
        List<int> balancedSlots = new List<int>(7);
        EnemySquadPursuitPlanner.BuildBalancedSlotIndices(slots, 5, balancedSlots);
        if (balancedSlots.Count != 5
            || !balancedSlots.Contains(FindSlotByKind(slots, EnemySquadPursuitSlotKind.Direct).Index)
            || balancedSlots.Contains(FindSlotByKind(slots, EnemySquadPursuitSlotKind.Rear).Index)
            || ResolveMaximumSelectedPointGap(balancedSlots) > 2)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit balanced subset did not distribute five slots around the player");
        }
        EnemySquadPursuitPlanner.BuildBalancedSlotIndices(slots, 7, balancedSlots);
        if (balancedSlots.Count != 7
            || balancedSlots.Contains(FindSlotByKind(slots, EnemySquadPursuitSlotKind.Rear).Index))
        {
            throw new System.InvalidOperationException(
                "Squad pursuit far assignment must use every non-rear slot and exclude rear center");
        }
        Vector3 rearClaimInside = player
            + Quaternion.AngleAxis(29f, Vector3.up) * rearSlot.OutwardDirection * 13f;
        Vector3 rearClaimOutside = player
            + Quaternion.AngleAxis(31f, Vector3.up) * rearSlot.OutwardDirection * 13f;
        Vector3 rearReleaseInside = player
            + Quaternion.AngleAxis(44f, Vector3.up) * rearSlot.OutwardDirection * 13f;
        if (EnemySquadPursuitPlanner.DefaultRearSlotClaimAngle
                >= EnemySquadPursuitPlanner.DefaultRearSlotReleaseAngle
            || !EnemySquadPursuitPlanner.IsWithinSlotAngularSector(
                player,
                rearClaimInside,
                rearSlot.OutwardDirection,
                EnemySquadPursuitPlanner.DefaultRearSlotClaimAngle)
            || EnemySquadPursuitPlanner.IsWithinSlotAngularSector(
                player,
                rearClaimOutside,
                rearSlot.OutwardDirection,
                EnemySquadPursuitPlanner.DefaultRearSlotClaimAngle)
            || !EnemySquadPursuitPlanner.IsWithinSlotAngularSector(
                player,
                rearReleaseInside,
                rearSlot.OutwardDirection,
                EnemySquadPursuitPlanner.DefaultRearSlotReleaseAngle))
        {
            throw new System.InvalidOperationException(
                "Squad conditional rear-orbit sector or release hysteresis contract failed");
        }
        EnemySquadPursuitPlanner.BuildBalancedSlotIndices(slots, 1, balancedSlots);
        if (balancedSlots.Count != 1
            || balancedSlots[0] != FindSlotByKind(slots, EnemySquadPursuitSlotKind.Direct).Index
            || EnemySquadPursuitPlanner.DefaultNearReleaseDistance
                >= EnemySquadPursuitPlanner.DefaultSlotRadius
            || EnemySquadPursuitPlanner.DefaultSlotRadius
                >= EnemySquadPursuitPlanner.DefaultDirectCommitRadius
            || EnemySquadPursuitPlanner.DefaultDirectCommitRadius
                >= EnemySquadPursuitPlanner.DefaultFarActivationDistance)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit direct priority or near/slot/commit/far distance ordering changed");
        }

        Vector3 sideSquadCenter = new Vector3(15f, 0f, 1f);
        EnemySquadPursuitSlot sideSlot = FindSlotByKind(slots, EnemySquadPursuitSlotKind.RightBypass);
        EnemySquadPursuitRoute sideRoute = EnemySquadPursuitPlanner.BuildRoute(
            sideSquadCenter,
            player,
            directOutward,
            sideSlot,
            slots);
        Vector3 rightDirection = new Vector3(directOutward.z, 0f, -directOutward.x);
        Vector3 previousPoint = sideSquadCenter;
        Vector3 previousSegment = Vector3.zero;
        float accumulatedTurn = 0f;
        for (int waypointIndex = 0; waypointIndex < sideRoute.WaypointCount; waypointIndex++)
        {
            Vector3 waypoint = sideRoute.GetWaypoint(waypointIndex);
            Vector3 segment = waypoint - previousPoint;
            segment.y = 0f;
            if (segment.sqrMagnitude <= 0.0001f
                || (waypointIndex > 0 && Vector3.Dot(previousSegment.normalized, segment.normalized) <= 0.25f))
            {
                throw new System.InvalidOperationException(
                    "Squad side pursuit curve contained a zero or sharp reverse segment index="
                    + waypointIndex);
            }
            if (waypointIndex > 0)
                accumulatedTurn += Mathf.Abs(Vector3.Cross(previousSegment.normalized, segment.normalized).y);
            previousSegment = segment;
            previousPoint = waypoint;
        }
        if (sideRoute.WaypointCount != 5
            || (sideRoute.GetWaypoint(4) - sideSlot.Position).sqrMagnitude > 0.0001f
            || Vector3.Dot(sideRoute.GetWaypoint(2) - player, rightDirection) <= 0f
            || accumulatedTurn <= 0.1f)
        {
            throw new System.InvalidOperationException(
                "Squad right pursuit route did not form a smooth right-side quadratic curve");
        }

        EnemySquadPursuitSlot leftSlot = FindSlotByKind(slots, EnemySquadPursuitSlotKind.LeftBypass);
        EnemySquadPursuitRoute leftRoute = EnemySquadPursuitPlanner.BuildRoute(
            new Vector3(15f, 0f, -1f),
            player,
            directOutward,
            leftSlot,
            slots);
        if (leftRoute.WaypointCount != 5
            || Vector3.Dot(leftRoute.GetWaypoint(2) - player, rightDirection) >= 0f
            || (leftRoute.GetWaypoint(4) - leftSlot.Position).sqrMagnitude > 0.0001f)
        {
            throw new System.InvalidOperationException(
                "Squad left pursuit route did not form a smooth left-side quadratic curve");
        }

        EnemySquadPursuitSlot frontDiagonalSlot = FindSlotByKind(
            slots,
            EnemySquadPursuitSlotKind.FrontRightDiagonal);
        EnemySquadPursuitSlot rearDiagonalSlot = FindSlotByKind(
            slots,
            EnemySquadPursuitSlotKind.RearRightDiagonal);
        EnemySquadPursuitSlot frontLeftDiagonalSlot = FindSlotByKind(
            slots,
            EnemySquadPursuitSlotKind.FrontLeftDiagonal);
        EnemySquadPursuitSlot rearLeftDiagonalSlot = FindSlotByKind(
            slots,
            EnemySquadPursuitSlotKind.RearLeftDiagonal);
        EnemySquadPursuitRoute frontDiagonalRoute = EnemySquadPursuitPlanner.BuildRoute(
            sideSquadCenter,
            player,
            directOutward,
            frontDiagonalSlot,
            slots);
        EnemySquadPursuitRoute rearDiagonalRoute = EnemySquadPursuitPlanner.BuildRoute(
            sideSquadCenter,
            player,
            directOutward,
            rearDiagonalSlot,
            slots);
        EnemySquadPursuitRoute frontLeftDiagonalRoute = EnemySquadPursuitPlanner.BuildRoute(
            sideSquadCenter,
            player,
            directOutward,
            frontLeftDiagonalSlot,
            slots);
        EnemySquadPursuitRoute rearLeftDiagonalRoute = EnemySquadPursuitPlanner.BuildRoute(
            sideSquadCenter,
            player,
            directOutward,
            rearLeftDiagonalSlot,
            slots);
        float frontDiagonalBulge = MeasureRouteBulge(
            sideSquadCenter,
            frontDiagonalSlot.Position,
            frontDiagonalRoute);
        float sideBypassBulge = MeasureRouteBulge(
            sideSquadCenter,
            sideSlot.Position,
            sideRoute);
        EnemySquadPursuitRoute compactSideRoute = EnemySquadPursuitPlanner.BuildRoute(
            sideSquadCenter,
            player,
            directOutward,
            sideSlot,
            slots,
            0.8f);
        float compactSideBulge = MeasureRouteBulge(
            sideSquadCenter,
            sideSlot.Position,
            compactSideRoute);
        float rearDiagonalBulge = MeasureRouteBulge(
            sideSquadCenter,
            rearDiagonalSlot.Position,
            rearDiagonalRoute);
        if (frontDiagonalRoute.WaypointCount != 5
            || rearDiagonalRoute.WaypointCount != 5
            || frontLeftDiagonalRoute.WaypointCount != 5
            || rearLeftDiagonalRoute.WaypointCount != 5
            || frontDiagonalBulge >= sideBypassBulge
            || sideBypassBulge >= rearDiagonalBulge
            || compactSideBulge >= sideBypassBulge
            || Vector3.Dot(frontLeftDiagonalRoute.GetWaypoint(2) - player, rightDirection) >= 0f
            || Vector3.Dot(rearLeftDiagonalRoute.GetWaypoint(2) - player, rightDirection) >= 0f)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit curve tiers must remain small front-diagonal, large side and largest rear-diagonal");
        }

        EnemySquadPursuitRoute rearRoute = EnemySquadPursuitPlanner.BuildRoute(
            new Vector3(15f, 0f, -2f),
            player,
            directOutward,
            FindSlotByKind(slots, EnemySquadPursuitSlotKind.Rear),
            slots);
        int[] expectedPriority = { 0, 2, 6, 1, 7, 3, 5, 4 };
        HashSet<int> preferredSlots = new HashSet<int>();
        HashSet<Color32> fixedRoleColors = new HashSet<Color32>();
        for (int order = 0; order < expectedPriority.Length; order++)
        {
            int actual = EnemySquadPursuitPlanner.GetPreferredSlotIndex(order);
            preferredSlots.Add(actual);
            fixedRoleColors.Add((Color32)EnemySquadPursuitSimulatorWindow.ResolveSlotRoleColor(
                (EnemySquadPursuitSlotKind)actual));
            if (actual != expectedPriority[order])
            {
                throw new System.InvalidOperationException(
                    "Squad pursuit slot priority changed order=" + order
                    + " expected=" + expectedPriority[order] + " actual=" + actual);
            }
        }

        List<Vector3> distancePrioritySquads = new List<Vector3>
        {
            new Vector3(1f, 0f, 0f),
            new Vector3(-2f, 0f, 0f)
        };
        List<Vector3> distancePrioritySlots = new List<Vector3>
        {
            Vector3.zero,
            new Vector3(10f, 0f, 0f)
        };
        List<int> squadIndexBySlot = new List<int>();
        EnemySquadPursuitPlanner.BuildMinimumDistanceAssignment(
            distancePrioritySquads,
            distancePrioritySlots,
            squadIndexBySlot);
        if (squadIndexBySlot.Count != 2
            || squadIndexBySlot[0] != 1
            || squadIndexBySlot[1] != 0)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit slots must use minimum-total-distance pairing instead of slot-first greedy pairing");
        }

        distancePrioritySquads.Clear();
        distancePrioritySquads.Add(new Vector3(9f, 0f, 0f));
        distancePrioritySlots.Clear();
        distancePrioritySlots.Add(new Vector3(-10f, 0f, 0f));
        distancePrioritySlots.Add(new Vector3(10f, 0f, 0f));
        EnemySquadPursuitPlanner.BuildMinimumDistanceAssignment(
            distancePrioritySquads,
            distancePrioritySlots,
            squadIndexBySlot);
        if (squadIndexBySlot.Count != 2
            || squadIndexBySlot[0] != -1
            || squadIndexBySlot[1] != 0)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit must leave a farther priority slot vacant when a nearer slot is available");
        }

        distancePrioritySquads[0] = Vector3.zero;
        distancePrioritySlots[0] = Vector3.left;
        distancePrioritySlots[1] = Vector3.right;
        EnemySquadPursuitPlanner.BuildMinimumDistanceAssignment(
            distancePrioritySquads,
            distancePrioritySlots,
            squadIndexBySlot);
        if (squadIndexBySlot[0] != 0 || squadIndexBySlot[1] != -1)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit role priority must break an exact distance tie deterministically");
        }

        if (rearRoute.WaypointCount != 1
            || (rearRoute.GetWaypoint(0) - rearSlot.Position).sqrMagnitude > 0.0001f
            || preferredSlots.Count != 8
            || fixedRoleColors.Count != 8
            || !preferredSlots.Contains(0)
            || !preferredSlots.Contains(2)
            || !preferredSlots.Contains(6)
            || !preferredSlots.Contains(4))
        {
            throw new System.InvalidOperationException(
                "Squad pursuit direct rear route, slot priority or fixed role color contract failed");
        }

        Vector3 reserveDirectionSum = Vector3.zero;
        int reserveQuadrants = 0;
        bool[] occupiedQuadrants = new bool[4];
        for (int squadId = 1; squadId <= 16; squadId++)
        {
            Vector3 reserveDirection = EnemySquadPursuitPlanner.ResolveReserveOrbitDirection(401, squadId);
            reserveDirectionSum += reserveDirection;
            int quadrant = reserveDirection.x >= 0f
                ? reserveDirection.z >= 0f ? 0 : 1
                : reserveDirection.z < 0f ? 2 : 3;
            if (!occupiedQuadrants[quadrant])
            {
                occupiedQuadrants[quadrant] = true;
                reserveQuadrants++;
            }
            float reserveRadius = EnemySquadPursuitPlanner.ResolveReserveOrbitRadius(
                EnemySquadPursuitPlanner.DefaultReserveOrbitRadius,
                squadId);
            if (reserveRadius < EnemySquadPursuitPlanner.DefaultFarActivationDistance + 2f)
            {
                throw new System.InvalidOperationException(
                    "Squad reserve orbit entered the active slot ring squad=" + squadId);
            }
        }

        float oppositeRadius = EnemySquadPursuitPlanner.ResolveReserveOrbitRadius(
            EnemySquadPursuitPlanner.DefaultReserveOrbitRadius,
            2);
        Vector3 clockwiseDestination = EnemySquadPursuitPlanner.ResolveReserveOrbitDestination(
            player,
            player + Vector3.right * oppositeRadius,
            Vector3.left,
            oppositeRadius,
            2);
        Vector3 counterClockwiseDestination = EnemySquadPursuitPlanner.ResolveReserveOrbitDestination(
            player,
            player + Vector3.right * oppositeRadius,
            Vector3.left,
            oppositeRadius,
            3);
        Vector3 clockwiseDirection = (clockwiseDestination - player).normalized;
        Vector3 counterClockwiseDirection = (counterClockwiseDestination - player).normalized;
        Vector3 reserveStartDirection = EnemySquadPursuitPlanner.ResolveReserveOrbitDirection(401, 2, 0f);
        Vector3 reserveQuarterTurnDirection = EnemySquadPursuitPlanner.ResolveReserveOrbitDirection(401, 2, 90f);
        float clockwiseProjectedClearance = Vector3.Dot(
            clockwiseDestination - player,
            Vector3.right);
        float counterClockwiseProjectedClearance = Vector3.Dot(
            counterClockwiseDestination - player,
            Vector3.right);
        if (reserveQuadrants != 4
            || reserveDirectionSum.magnitude >= 2f
            || clockwiseProjectedClearance < oppositeRadius - 0.001f
            || counterClockwiseProjectedClearance < oppositeRadius - 0.001f
            || Vector3.Dot(clockwiseDirection, Vector3.right) <= 0.8f
            || Vector3.Dot(counterClockwiseDirection, Vector3.right) <= 0.8f
            || clockwiseDestination.z >= 0f
            || counterClockwiseDestination.z <= 0f
            || Mathf.Abs(Vector3.Dot(reserveStartDirection, reserveQuarterTurnDirection)) > 0.001f
            || Mathf.Abs(reserveQuarterTurnDirection.magnitude - 1f) > 0.001f)
        {
            throw new System.InvalidOperationException(
                "Squad reserve orbit did not distribute, rotate or preserve player clearance");
        }

        Vector3 formationDestination = new Vector3(4f, 0f, -2f);
        Vector3 formationOffset = new Vector3(1.5f, 0f, 0.5f);
        Vector3 arrivedMemberPosition = formationDestination + formationOffset * 0.52f;
        if (!EnemySquadPursuitSimulatorWindow.IsMemberAtFormationDestination(
                arrivedMemberPosition,
                formationDestination,
                formationOffset,
                0.52f,
                0.2f)
            || EnemySquadPursuitSimulatorWindow.IsMemberAtFormationDestination(
                arrivedMemberPosition + Vector3.right,
                formationDestination,
                formationOffset,
                0.52f,
                0.2f))
        {
            throw new System.InvalidOperationException(
                "Squad pursuit member-first formation arrival contract failed");
        }

        if (!EnemySquadPursuitSimulatorWindow.IsOutsidePlayerDistance(
                Vector3.right * 11.1f,
                Vector3.zero,
                11f)
            || EnemySquadPursuitSimulatorWindow.IsOutsidePlayerDistance(
                Vector3.right * 11f,
                Vector3.zero,
                11f))
        {
            throw new System.InvalidOperationException(
                "Squad Rush Far-area release boundary contract failed");
        }

        Vector3 forwardPreserved = EnemySquadPursuitSimulatorWindow.PreserveMinimumForwardProgress(
            Vector3.left,
            Vector3.right * 0.8f + Vector3.forward,
            0.2f);
        if (Vector3.Dot(forwardPreserved, Vector3.left) < 0.199f
            || forwardPreserved.magnitude > 1.001f)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit separation cancelled the guaranteed forward progress");
        }

        EnemySquadPursuitRoute translatedSideRoute = EnemySquadPursuitPlanner.TranslateRoute(
            sideRoute,
            new Vector3(2f, 9f, -3f));
        if (EnemySquadPursuitPlanner.ShouldRefreshRoute(0.99f, 1f, 1f, false)
            || !EnemySquadPursuitPlanner.ShouldRefreshRoute(1f, 1f, 1f, false)
            || EnemySquadPursuitPlanner.ShouldRefreshRoute(1f, 0.99f, 1f, false)
            || EnemySquadPursuitPlanner.ShouldRefreshRoute(1f, 1f, 1f, true)
            || EnemySquadPursuitPlanner.ShouldRefreshFullReformation(11.99f, 9.99f, 10f, 0f)
            || !EnemySquadPursuitPlanner.ShouldRefreshFullReformation(0f, 10f, 10f, 0f)
            || EnemySquadPursuitPlanner.ShouldRefreshFullReformation(12f, 4.99f, 10f, 0f)
            || !EnemySquadPursuitPlanner.ShouldRefreshFullReformation(12f, 5f, 10f, 0f)
            || Vector3.Distance(
                translatedSideRoute.GetWaypoint(0),
                sideRoute.GetWaypoint(0) + new Vector3(2f, 0f, -3f)) > 0.001f)
        {
            throw new System.InvalidOperationException(
                "Squad route translation, limited rebuild, or full reformation contract failed");
        }

        Vector3 cohesionDestination = EnemySquadPursuitPlanner.ResolveCohesionDestination(
            new Vector3(0f, 2f, 3f),
            Vector3.zero,
            Vector3.zero,
            Vector3.right * 10f,
            0.52f);
        Vector3 cohesionDeadZoneDestination = EnemySquadPursuitPlanner.ResolveCohesionDestination(
            new Vector3(0f, 2f, 0.5f),
            Vector3.zero,
            Vector3.zero,
            Vector3.right * 10f,
            0.52f);
        float trailingBoost = EnemySquadPursuitPlanner.ResolveCohesionSpeedMultiplier(
            Vector3.left * 5f,
            Vector3.zero,
            Vector3.right * 10f);
        float lateralBoost = EnemySquadPursuitPlanner.ResolveCohesionSpeedMultiplier(
            Vector3.forward * 5f,
            Vector3.zero,
            Vector3.right * 10f);
        if (cohesionDestination.x <= 0f
            || cohesionDestination.z >= 3f
            || !Mathf.Approximately(cohesionDestination.y, 2f)
            || Vector3.Distance(cohesionDeadZoneDestination, new Vector3(10f, 2f, 0f)) > 0.001f
            || trailingBoost < 1.119f
            || trailingBoost > 1.121f
            || !Mathf.Approximately(lateralBoost, 1f))
        {
            throw new System.InvalidOperationException(
                "Squad moving cohesion steering or catch-up speed contract failed");
        }

        if (EnemySquadPursuitSimulatorWindow.ResolveMovePriorityValue(true, false, false) != 3
            || EnemySquadPursuitSimulatorWindow.ResolveMovePriorityValue(false, true, false) != 2
            || EnemySquadPursuitSimulatorWindow.ResolveMovePriorityValue(false, false, true) != 1
            || EnemySquadPursuitSimulatorWindow.ResolveMovePriorityValue(false, false, false) != 0
            || !EnemySquadPursuitSimulatorWindow.ShouldUseRemnantPattern(false, 3, 10, 0.4f)
            || EnemySquadPursuitSimulatorWindow.ShouldUseRemnantPattern(false, 4, 10, 0.4f)
            || !EnemySquadPursuitSimulatorWindow.ShouldUseRemnantPattern(true, 10, 10, 0.4f)
            || EnemySquadPursuitSimulatorWindow.ShouldUseRemnantPattern(true, 0, 10, 0.4f))
        {
            throw new System.InvalidOperationException(
                "Squad Near/Remnant priority or permanent slot-revocation contract failed");
        }

        float priorityOwnerShare = EnemySquadPursuitSimulatorWindow.ResolvePriorityCorrectionShare(
            1f,
            1f,
            2,
            0);
        float reserveShare = EnemySquadPursuitSimulatorWindow.ResolvePriorityCorrectionShare(
            1f,
            1f,
            0,
            2);
        float weightedShare = EnemySquadPursuitSimulatorWindow.ResolvePriorityCorrectionShare(
            1f,
            2f,
            0,
            0);
        Vector3 stablePairA = EnemySquadPursuitSimulatorWindow.ResolveStablePairDirection(17, 29);
        Vector3 stablePairB = EnemySquadPursuitSimulatorWindow.ResolveStablePairDirection(29, 17);
        if (Mathf.Abs(priorityOwnerShare - 0.1f) > 0.0001f
            || Mathf.Abs(reserveShare - 0.9f) > 0.0001f
            || Mathf.Abs(weightedShare - 2f / 3f) > 0.0001f
            || Mathf.Abs(EnemySquadPursuitSimulatorWindow.ResolvePrioritySeparationScale(2, 0) - 0.15f) > 0.0001f
            || Mathf.Abs(EnemySquadPursuitSimulatorWindow.ResolvePrioritySeparationScale(0, 2) - 1.35f) > 0.0001f
            || (stablePairA + stablePairB).sqrMagnitude > 0.0001f)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit priority Hard Overlap or stable pair-direction contract failed");
        }

        EnemyCrowdPriorityBody highPriorityBody = new EnemyCrowdPriorityBody
        {
            StableId = 101,
            DesiredPosition = Vector3.zero,
            ResolvedPosition = Vector3.zero,
            BodyRadius = 0.5f,
            CrowdWeight = 1f,
            MovePriority = 2,
            CanMove = true
        };
        EnemyCrowdPriorityBody reserveBody = new EnemyCrowdPriorityBody
        {
            StableId = 202,
            DesiredPosition = Vector3.right * 0.8f,
            ResolvedPosition = Vector3.right * 0.8f,
            BodyRadius = 0.5f,
            CrowdWeight = 1f,
            MovePriority = 0,
            CanMove = true
        };
        if (!EnemyCrowdPrioritySolver.TryCalculatePair(
                highPriorityBody,
                reserveBody,
                out EnemyCrowdPairCorrection centralPair)
            || Mathf.Abs(centralPair.CorrectionA.magnitude - 0.02f) > 0.0001f
            || Mathf.Abs(centralPair.CorrectionB.magnitude - 0.18f) > 0.0001f
            || centralPair.YieldCorrectionA.sqrMagnitude > 0.000001f
            || centralPair.YieldCorrectionB.sqrMagnitude <= 0.000001f)
        {
            throw new System.InvalidOperationException(
                "Central crowd solver bidirectional 10:90 or Reserve Yield contract failed");
        }

        if (!EnemyCrowdPrioritySolver.TryCalculatePair(
                reserveBody,
                highPriorityBody,
                out EnemyCrowdPairCorrection reversedCentralPair)
            || (reversedCentralPair.CorrectionA - centralPair.CorrectionB).sqrMagnitude > 0.000001f
            || (reversedCentralPair.CorrectionB - centralPair.CorrectionA).sqrMagnitude > 0.000001f)
        {
            throw new System.InvalidOperationException(
                "Central crowd solver pair-order independence contract failed");
        }

        EnemyCrowdPriorityBody lockedBody = highPriorityBody;
        lockedBody.CanMove = false;
        if (!EnemyCrowdPrioritySolver.TryCalculatePair(
                lockedBody,
                reserveBody,
                out EnemyCrowdPairCorrection lockedPair)
            || lockedPair.CorrectionA.sqrMagnitude > 0.000001f
            || Mathf.Abs(lockedPair.CorrectionB.magnitude - 0.2f) > 0.0001f)
        {
            throw new System.InvalidOperationException(
                "Central crowd solver position-lock contract failed");
        }

        EnemyCrowdPriorityBody forcedBody = highPriorityBody;
        forcedBody.IsForcedMotion = true;
        if (!EnemyCrowdPrioritySolver.TryCalculatePair(
                forcedBody,
                reserveBody,
                out EnemyCrowdPairCorrection forcedPair)
            || forcedPair.CorrectionA.sqrMagnitude > 0.000001f
            || Mathf.Abs(forcedPair.CorrectionB.magnitude - 0.2f) > 0.0001f)
        {
            throw new System.InvalidOperationException(
                "Central crowd solver forced-motion authority contract failed");
        }

        EnemyCrowdPriorityBody cappedBody = highPriorityBody;
        Vector3 cappedCorrection = EnemyCrowdPrioritySolver.ApplyAccumulatedCorrection(
            ref cappedBody,
            Vector3.left,
            EnemyCrowdService.MaximumCentralCorrection);
        if (Mathf.Abs(cappedCorrection.magnitude - EnemyCrowdService.MaximumCentralCorrection) > 0.0001f)
        {
            throw new System.InvalidOperationException(
                "Central crowd solver per-FixedUpdate correction cap failed");
        }

        Debug.Log(
            "[EnemyAiContractVerifier] Passed maximum-first squad formation (4~8, 8~12, 0~200), uniform physical slots, conditional rear Reserve, route translation, full reformation, central priority pair solver, member-first Rush, all-outside release and reserve contracts");
    }
    private static void VerifyMaximumFirstSquadSizes(
        int minimum,
        int maximum,
        int monsterCount,
        params int[] expected)
    {
        List<int> sizes = new List<int>();
        EnemySquadPursuitPlanner.BuildMaximumFirstSquadSizes(
            monsterCount,
            minimum,
            maximum,
            sizes);
        if (sizes.Count != expected.Length)
        {
            throw new System.InvalidOperationException(
                "Maximum-first squad count mismatch range=" + minimum + "~" + maximum
                + " monsters=" + monsterCount + " actual=" + sizes.Count
                + " expected=" + expected.Length);
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (sizes[i] != expected[i])
            {
                throw new System.InvalidOperationException(
                    "Maximum-first squad size mismatch range=" + minimum + "~" + maximum
                    + " monsters=" + monsterCount + " index=" + i
                    + " actual=" + sizes[i] + " expected=" + expected[i]);
            }
        }
    }
    private static void VerifyMaximumFirstSquadRange(int minimum, int maximum)
    {
        List<int> sizes = new List<int>();
        for (int monsterCount = 0; monsterCount <= 200; monsterCount++)
        {
            EnemySquadPursuitPlanner.BuildMaximumFirstSquadSizes(
                monsterCount,
                minimum,
                maximum,
                sizes);
            int assignedCount = 0;
            for (int i = 0; i < sizes.Count; i++)
            {
                if (sizes[i] < minimum
                    || sizes[i] > maximum
                    || i < sizes.Count - 1 && sizes[i] != maximum)
                {
                    throw new System.InvalidOperationException(
                        "Maximum-first range sweep produced invalid size range="
                        + minimum + "~" + maximum + " monsters=" + monsterCount
                        + " index=" + i + " size=" + sizes[i]);
                }
                assignedCount += sizes[i];
            }

            int remainder = monsterCount - assignedCount;
            int rawRemainder = monsterCount % maximum;
            int expectedAssigned = monsterCount / maximum * maximum
                + (rawRemainder >= minimum ? rawRemainder : 0);
            if (assignedCount != expectedAssigned || remainder < 0 || remainder >= minimum)
            {
                throw new System.InvalidOperationException(
                    "Maximum-first range sweep assignment mismatch range="
                    + minimum + "~" + maximum + " monsters=" + monsterCount
                    + " assigned=" + assignedCount + " expected=" + expectedAssigned
                    + " remainder=" + remainder);
            }
        }
    }
    private static float MeasureRouteBulge(
        Vector3 start,
        Vector3 end,
        EnemySquadPursuitRoute route)
    {
        if (route.WaypointCount < 3)
            return 0f;

        Vector3 linearPoint = Vector3.Lerp(start, end, 0.6f);
        Vector3 delta = route.GetWaypoint(2) - linearPoint;
        delta.y = 0f;
        return delta.magnitude;
    }
    private static EnemySquadPursuitSlot FindSlotAtPoint(
        List<EnemySquadPursuitSlot> slots,
        int pointIndex)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].PointIndex == pointIndex)
                return slots[i];
        }

        throw new System.InvalidOperationException(
            "Squad pursuit fixed point mapping is missing point=P" + pointIndex);
    }
    private static EnemySquadPursuitSlot FindSlotByKind(
        List<EnemySquadPursuitSlot> slots,
        EnemySquadPursuitSlotKind kind)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].Kind == kind)
                return slots[i];
        }

        throw new System.InvalidOperationException(
            "Squad pursuit role mapping is missing kind=" + kind);
    }
    private static int ResolveMaximumSelectedPointGap(List<int> selectedPointIndices)
    {
        bool[] selected = new bool[8];
        for (int i = 0; i < selectedPointIndices.Count; i++)
        {
            int pointIndex = selectedPointIndices[i];
            if (pointIndex >= 0 && pointIndex < selected.Length)
                selected[pointIndex] = true;
        }

        int first = -1;
        int previous = -1;
        int maximumGap = 0;
        for (int pointIndex = 0; pointIndex < selected.Length; pointIndex++)
        {
            if (!selected[pointIndex])
                continue;
            if (first < 0)
                first = pointIndex;
            if (previous >= 0)
                maximumGap = Mathf.Max(maximumGap, pointIndex - previous);
            previous = pointIndex;
        }

        if (first >= 0 && previous >= 0)
            maximumGap = Mathf.Max(maximumGap, first + 8 - previous);
        return maximumGap;
    }
    private static void VerifySharedFlowFieldServiceContract()
    {
        GameObject firstTarget = null;
        GameObject secondTarget = null;
        try
        {
            bool[,] cells = CreateFlowFieldCells(6, 4, -1, -1);
            RunWalkableArea firstArea =
                new RunWalkableArea(cells, 6, 4, 0f, 0f, 1f);
            int revisionBefore = RunWalkableContext.Revision;
            RunWalkableContext.SetCurrent(firstArea);
            if (RunWalkableContext.Revision == revisionBefore)
                throw new System.InvalidOperationException("Walkable context revision did not advance");

            EnemyFlowFieldService.SetEnabled(true);
            EnemyFlowFieldService.ClearCache();
            firstTarget = EditorUtility.CreateGameObjectWithHideFlags("FlowFieldSharedTargetA", HideFlags.HideAndDontSave);
            firstTarget.transform.position = ResolveCellCenter(firstArea, 4, 1);
            Vector3 firstAgent = ResolveCellCenter(firstArea, 1, 1);
            if (!EnemyFlowFieldService.TryGetDirection(
                    firstTarget.transform,
                    firstAgent,
                    out _,
                    out _)
                || EnemyFlowFieldService.CachedTargetCount != 1
                || EnemyFlowFieldService.BuildCount != 1)
            {
                throw new System.InvalidOperationException("Shared Flow Field did not create one target cache");
            }

            if (!EnemyFlowFieldService.TryGetDirection(
                    firstTarget.transform,
                    ResolveCellCenter(firstArea, 1, 2),
                    out _,
                    out _)
                || EnemyFlowFieldService.BuildCount != 1
                || EnemyFlowFieldService.CacheHitCount <= 0)
            {
                throw new System.InvalidOperationException("Shared Flow Field was rebuilt per enemy request");
            }

            firstTarget.transform.position = ResolveCellCenter(firstArea, 4, 2);
            if (!EnemyFlowFieldService.TryGetDirection(firstTarget.transform, firstAgent, out _, out _)
                || EnemyFlowFieldService.BuildCount != 2
                || EnemyFlowFieldService.CachedTargetCount != 1)
            {
                throw new System.InvalidOperationException("Shared Flow Field target-cell rebuild contract failed");
            }

            RunWalkableArea secondArea = new RunWalkableArea(
                cells,
                6,
                4,
                10f,
                0f,
                1f);
            RunWalkableContext.SetCurrent(secondArea);
            firstTarget.transform.position = ResolveCellCenter(secondArea, 4, 2);
            Vector3 secondAreaAgent = ResolveCellCenter(secondArea, 1, 2);
            if (!EnemyFlowFieldService.TryGetDirection(firstTarget.transform, secondAreaAgent, out _, out _)
                || EnemyFlowFieldService.BuildCount != 3
                || EnemyFlowFieldService.CachedTargetCount != 1)
            {
                throw new System.InvalidOperationException("Shared Flow Field map revision invalidation failed");
            }

            secondTarget = EditorUtility.CreateGameObjectWithHideFlags("FlowFieldSharedTargetB", HideFlags.HideAndDontSave);
            secondTarget.transform.position = ResolveCellCenter(secondArea, 4, 1);
            if (!EnemyFlowFieldService.TryGetDirection(secondTarget.transform, secondAreaAgent, out _, out _)
                || EnemyFlowFieldService.BuildCount != 4
                || EnemyFlowFieldService.CachedTargetCount != 2
                || EnemyFlowFieldService.BuiltCellCountThisFrame > EnemyFlowFieldService.MaximumBuildCellsPerFrame)
            {
                throw new System.InvalidOperationException("Shared Flow Field multi-target cache or frame budget failed");
            }

            EnemyFlowFieldService.SetEnabled(false);
            if (EnemyFlowFieldService.TryGetDirection(firstTarget.transform, secondAreaAgent, out _, out _)
                || EnemyFlowFieldService.CachedTargetCount != 0)
            {
                throw new System.InvalidOperationException("Shared Flow Field rollback switch did not clear and disable caches");
            }

            Debug.Log(
                "[EnemyAiContractVerifier] Passed shared Flow Field cache, target/map rebuild, multi-target and rollback contracts");
        }
        finally
        {
            EnemyFlowFieldService.SetEnabled(true);
            EnemyFlowFieldService.ClearCache();
            RunWalkableContext.Clear();
            if (firstTarget != null)
                Object.DestroyImmediate(firstTarget);
            if (secondTarget != null)
                Object.DestroyImmediate(secondTarget);
        }
    }
    private static bool[,] CreateFlowFieldCells(int width, int height, int wallX, int gapZ)
    {
        bool[,] cells = new bool[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
                cells[x, z] = wallX < 0 || x != wallX || z == gapZ;
        }
        return cells;
    }
    private static Vector3 ResolveCellCenter(
        RunWalkableArea area,
        int x,
        int z,
        float y = 0f)
    {
        Vector2 center = area.GetCellCenter(x, z);
        return new Vector3(center.x, y, center.y);
    }
    private static void VerifyLegacySurroundRemovalContract()
    {
        const string plannerPath = "Assets/ProjectOverburst/03_Features/Enemies/Runtime/AI/EnemySurroundRingPlanner.cs";
        if (AssetDatabase.LoadAssetAtPath<MonoScript>(plannerPath) != null)
            throw new System.InvalidOperationException("Legacy surrounding planner asset still exists");

        string[] removedMembers =
        {
            "AdaptiveSurroundingRingsEnabled",
            "useAdaptiveSurroundingRings",
            "UsesAdaptiveSurroundingRings",
            "surroundDirection",
            "surroundRingRadius",
            "currentSurroundRingIndex",
            "currentSurroundInnerCapacity",
            "CurrentSurroundRingIndex",
            "CurrentSurroundInnerCapacity",
            "CurrentSurroundDestination",
            "SetAdaptiveSurroundingRingsEnabled",
            "ClearSurroundPlan"
        };
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.DeclaredOnly;
        System.Type controllerType = typeof(EnemyAIController);
        for (int i = 0; i < removedMembers.Length; i++)
        {
            if (controllerType.GetMember(removedMembers[i], flags).Length > 0)
                throw new System.InvalidOperationException("Legacy surrounding member still exists: " + removedMembers[i]);
        }

        Debug.Log("[EnemyAiContractVerifier] Passed legacy surrounding planner, fields and toggle removal");
    }
    private static void VerifyPartyTargetPhasePolicy()
    {
        const float enemyRadius = 0.4f;
        const float memberRadius = 0.5f;
        const float memberEngageRange = 2.5f;
        float engageCenterRadius = EnemyCombatCoordinator.ResolveMemberEngageCenterRadius(
            enemyRadius,
            memberRadius,
            memberEngageRange);
        if (!Mathf.Approximately(engageCenterRadius, 3.4f)
            || !EnemyCombatCoordinator.IsInsideMemberEngageRange(
                Vector3.zero,
                enemyRadius,
                Vector3.right * engageCenterRadius,
                memberRadius,
                memberEngageRange)
            || EnemyCombatCoordinator.IsInsideMemberEngageRange(
                Vector3.zero,
                enemyRadius,
                Vector3.right * (engageCenterRadius + 0.001f),
                memberRadius,
                memberEngageRange))
        {
            throw new System.InvalidOperationException(
                "MemberEngage orange-zone center radius does not match the runtime surface-distance boundary");
        }

        var farCandidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(0, true, false, false, 7f),
            new EnemyPartyTargetCandidate(1, true, false, false, 8f),
            new EnemyPartyTargetCandidate(2, true, false, false, 9f)
        };
        EnemyPartyTargetDecision farDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            new EnemyPartyTargetDecision(EnemyPartyTargetPhase.None, -1),
            0,
            farCandidates);
        RequirePartyTargetDecision(
            farDecision,
            EnemyPartyTargetPhase.LeaderApproach,
            0,
            "Far party candidates must all approach P1");

        var nearP2Candidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(0, true, false, false, 7f),
            new EnemyPartyTargetCandidate(1, true, true, false, 0.25f),
            new EnemyPartyTargetCandidate(2, true, false, false, 8f)
        };
        EnemyPartyTargetDecision nearP2Decision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            farDecision,
            0,
            nearP2Candidates);
        RequirePartyTargetDecision(
            nearP2Decision,
            EnemyPartyTargetPhase.MemberEngaged,
            1,
            "Only the enemy inside P2 engage range may lock P2");

        var nearDirectAttackerCandidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(0, true, true, false, 0.1f),
            new EnemyPartyTargetCandidate(1, true, true, true, 0.8f),
            new EnemyPartyTargetCandidate(2, true, true, false, 0.4f)
        };
        EnemyPartyTargetDecision nearDirectAttackerDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            farDecision,
            0,
            nearDirectAttackerCandidates);
        RequirePartyTargetDecision(
            nearDirectAttackerDecision,
            EnemyPartyTargetPhase.MemberEngaged,
            1,
            "An in-range direct attacker must beat closer non-attacker candidates");

        var nearestSurfaceCandidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(0, true, true, false, 1.2f),
            new EnemyPartyTargetCandidate(1, true, true, false, 0.3f),
            new EnemyPartyTargetCandidate(2, true, true, false, 0.8f)
        };
        EnemyPartyTargetDecision nearestSurfaceDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            farDecision,
            0,
            nearestSurfaceCandidates);
        RequirePartyTargetDecision(
            nearestSurfaceDecision,
            EnemyPartyTargetPhase.MemberEngaged,
            1,
            "Without a direct attacker the nearest surface-distance candidate must win");

        var equalDistanceCandidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(2, true, true, false, 0.5f),
            new EnemyPartyTargetCandidate(1, true, true, false, 0.5f),
            new EnemyPartyTargetCandidate(0, true, false, false, 7f)
        };
        EnemyPartyTargetDecision equalDistanceDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            farDecision,
            0,
            equalDistanceCandidates);
        RequirePartyTargetDecision(
            equalDistanceDecision,
            EnemyPartyTargetPhase.MemberEngaged,
            1,
            "Equal surface-distance candidates must use the lowest MemberIndex");

        var farDirectAttackerCandidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(0, true, false, false, 7f),
            new EnemyPartyTargetCandidate(1, true, false, true, 8f),
            new EnemyPartyTargetCandidate(2, true, false, false, 9f)
        };
        EnemyPartyTargetDecision farDirectAttackerDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            farDecision,
            0,
            farDirectAttackerCandidates);
        RequirePartyTargetDecision(
            farDirectAttackerDecision,
            EnemyPartyTargetPhase.LeaderApproach,
            0,
            "A direct attacker outside engage range must not replace P1");

        EnemyPartyTargetDecision invalidLock = new EnemyPartyTargetDecision(
            EnemyPartyTargetPhase.MemberEngaged,
            1);
        var fallbackNearCandidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(0, true, false, false, 7f),
            new EnemyPartyTargetCandidate(1, false, false, false, float.PositiveInfinity),
            new EnemyPartyTargetCandidate(2, true, true, false, 0.4f)
        };
        EnemyPartyTargetDecision nearFallbackDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            invalidLock,
            0,
            fallbackNearCandidates);
        RequirePartyTargetDecision(
            nearFallbackDecision,
            EnemyPartyTargetPhase.MemberEngaged,
            2,
            "An invalid lock must prefer another nearby party member");

        var invalidNoNearCandidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(0, true, false, false, 7f),
            new EnemyPartyTargetCandidate(1, false, false, false, float.PositiveInfinity),
            new EnemyPartyTargetCandidate(2, true, false, false, 9f)
        };
        EnemyPartyTargetDecision leaderFallbackDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            invalidLock,
            2,
            invalidNoNearCandidates);
        RequirePartyTargetDecision(
            leaderFallbackDecision,
            EnemyPartyTargetPhase.LeaderApproach,
            2,
            "An invalid lock without a nearby candidate must use the current leader");

        EnemyPartyTargetDecision reboundLeaderDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            farDecision,
            2,
            farCandidates);
        RequirePartyTargetDecision(
            reboundLeaderDecision,
            EnemyPartyTargetPhase.LeaderApproach,
            2,
            "Leader change must rebind LeaderApproach");

        EnemyPartyTargetDecision preservedLockDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            nearP2Decision,
            2,
            nearP2Candidates);
        RequirePartyTargetDecision(
            preservedLockDecision,
            EnemyPartyTargetPhase.MemberEngaged,
            1,
            "Leader change must preserve a valid MemberEngaged lock");

        Debug.Log(
            "[EnemyAiContractVerifier] Passed party target direct-attacker, surface-distance and MemberIndex priority");
    }
    private static void RequirePartyTargetDecision(
        EnemyPartyTargetDecision decision,
        EnemyPartyTargetPhase expectedPhase,
        int expectedMemberIndex,
        string message)
    {
        if (decision.Phase != expectedPhase || decision.TargetMemberIndex != expectedMemberIndex)
        {
            throw new System.InvalidOperationException(
                message
                + ". phase=" + decision.Phase
                + " member=" + decision.TargetMemberIndex);
        }
    }
}
