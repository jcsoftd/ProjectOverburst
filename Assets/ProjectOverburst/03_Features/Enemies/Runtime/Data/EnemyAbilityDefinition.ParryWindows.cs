using System;
using UnityEngine;

public sealed partial class EnemyAbilityDefinition
{
    [SerializeField] private Vector2[] parryMotionWindows;
    public bool HasParryMotionWindows => IsParryable && parryMotionWindows != null
        && parryMotionWindows.Length == HitCount;
    public bool TryGetParryMotionWindow(int strike, out Vector2 window)
    {
        window = default;
        if (!HasParryMotionWindows || strike < 0 || strike >= HitCount) return false;
        window = parryMotionWindows[strike];
        return window.x >= 0f && window.y > window.x && window.y <= GetHitNormalizedTime(strike) + .00001f;
    }
    public void ConfigureParryMotionWindows(params Vector2[] windows)
    {
        if (!IsParryable || windows == null || windows.Length != HitCount)
            throw new ArgumentException("패링 구간은 패링 가능한 강공의 각 타격과 일치해야 합니다.");
        float previous = -1f;
        for (int i = 0; i < windows.Length; i++)
        {
            var w = windows[i];
            if (float.IsNaN(w.x) || float.IsNaN(w.y) || w.x < 0f || w.y <= w.x
                || w.x <= previous || Mathf.Abs(w.y - GetHitNormalizedTime(i)) > .00001f)
                throw new ArgumentException("패링 구간은 겹치지 않고 해당 타격 순간에 끝나야 합니다.");
            previous = w.y;
        }
        parryMotionWindows = (Vector2[])windows.Clone();
    }
}
