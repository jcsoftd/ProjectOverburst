using System;
using UnityEngine;

public sealed partial class EnemyAbilityDefinition
{
    [SerializeField] private Vector2[] parryMotionWindows;
    [SerializeField] private bool firstStrikeOnlyParry;
    public bool FirstStrikeOnlyParry => firstStrikeOnlyParry;
    public int ParryStrikeCount => firstStrikeOnlyParry ? 1 : HitCount;
    public bool HasParryMotionWindows => IsParryable && parryMotionWindows != null
        && parryMotionWindows.Length == ParryStrikeCount;
    public bool TryGetParryMotionWindow(int strike, out Vector2 window)
    {
        window = default;
        if (!HasParryMotionWindows || strike < 0 || strike >= ParryStrikeCount) return false;
        window = parryMotionWindows[strike];
        return window.x >= 0f && window.y > window.x && window.y <= GetHitNormalizedTime(strike) + .00001f;
    }
    public void ConfigureParryMotionWindows(params Vector2[] windows)
    {
        if (!IsParryable || windows == null || windows.Length != ParryStrikeCount)
            throw new ArgumentException("패링 구간은 패링을 허용하는 강공 타격과 일치해야 합니다.");
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

    public void ConfigureFirstStrikeOnlyParry(bool enabled)
    {
        if (!IsParryable) throw new InvalidOperationException("패링 가능한 강공에만 첫 타 제한을 설정할 수 있습니다.");
        firstStrikeOnlyParry = enabled;
        if (parryMotionWindows == null || parryMotionWindows.Length == ParryStrikeCount) return;
        parryMotionWindows = enabled && parryMotionWindows.Length > 0
            ? new[] { parryMotionWindows[0] } : null;
    }
}
