using UnityEngine;

/// <summary>
/// 2026-10-01 ESC 메뉴 일시정지를 반영한 실제 시간. 히트스톱·패링 슬로우처럼 timeScale을 무시해야 하는
/// 게임 타이머(회피, 아이템 재사용 대기 등)가 Time.unscaledTime 대신 쓴다. 메뉴로 멈춘 동안에는 흐르지 않는다.
/// </summary>
public static class OverburstGameClock
{
    public static float UnscaledTime => Time.unscaledTime - OverburstTimeEffectArbiter.TotalPausedUnscaled;
    public static float UnscaledDeltaTime => OverburstTimeEffectArbiter.IsPaused ? 0f : Time.unscaledDeltaTime;
}
