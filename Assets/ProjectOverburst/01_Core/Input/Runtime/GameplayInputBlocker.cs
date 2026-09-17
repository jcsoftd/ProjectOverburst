using System;
using System.Collections.Generic;
using UnityEngine;

public static class GameplayInputBlocker // 입력 잠금
{
    private static readonly HashSet<UnityEngine.Object> blockers = new HashSet<UnityEngine.Object>();
    private static bool lastBroadcastBlocked;

    // GOAL A2: 기존 public API를 유지한 채 차단 전환 이벤트를 추가한다.
    // PlayerStateCoordinator.Condition.InputBlocked 연결용이다.
    public static event Action<bool> BlockStateChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        blockers.Clear();
        lastBroadcastBlocked = false;
        BlockStateChanged = null;
    }

    public static bool IsGameplayInputBlocked
    {
        get
        {
            RemoveInvalidBlockers();
            bool blocked = blockers.Count > 0;
            BroadcastIfChanged(blocked);
            return blocked;
        }
    }

    public static void SetBlocked(UnityEngine.Object source, bool blocked)
    {
        if (source == null)
            return;

        if (blocked)
            blockers.Add(source); // 중복 방지
        else
            blockers.Remove(source); // source 해제
        BroadcastIfChanged(blockers.Count > 0);
    }

    public static void Block(UnityEngine.Object source)
    {
        SetBlocked(source, true);
    }

    public static void Unblock(UnityEngine.Object source)
    {
        SetBlocked(source, false);
    }

    public static void ClearAllForSceneReturn()
    {
        blockers.Clear(); // 전체 해제
        BroadcastIfChanged(false);
    }

    private static void BroadcastIfChanged(bool blocked)
    {
        if (blocked == lastBroadcastBlocked)
            return;
        lastBroadcastBlocked = blocked;
        try
        {
            BlockStateChanged?.Invoke(blocked);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void RemoveInvalidBlockers()
    {
        blockers.RemoveWhere(blocker => blocker == null);
    }
}
