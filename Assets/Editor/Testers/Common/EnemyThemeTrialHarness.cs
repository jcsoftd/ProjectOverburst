using System.Collections.Generic;
using System.Linq;
using Overburst.DebugTools;

/// <summary>
/// 검증기용 적 테마 시험 손잡이. 2026-10-01 옛 HUD 패널(EnemyThemeDebugUI)을 지우면서, 검증기들이 쓰던 번호 기반 호출
/// (tables, Begin, Clear, ToggleArena, InArena, SetTrialMode)을 디버그 창 서비스 <see cref="EnemyThemeTrialService"/>로 넘긴다.
/// 테마 순서·수량·시험장 위치(1000,0,1000)·구역 배치는 옛 패널과 같다(DebugHubPlayVerifier가 확인).
/// 새 검증기는 이 손잡이나 서비스를 바로 쓴다. 사용: <c>var ui = EnemyThemeTrialHarness.Current;</c>
/// </summary>
public sealed class EnemyThemeTrialHarness
{
    public static EnemyThemeTrialHarness Current { get; } = new EnemyThemeTrialHarness();

    private EnemyThemeTrialHarness() { }

    /// <summary>옛 컴포넌트 때 쓰던 <c>if (!ui)</c> 검사가 그대로 돌도록.</summary>
    public static implicit operator bool(EnemyThemeTrialHarness harness) => harness != null;

    private static IReadOnlyList<EnemyThemeTrialEntry> Entries => EnemyThemeTrialService.Entries;

    /// <summary>옛 패널의 <c>tables</c>와 같은 순서(거미·독낭·원시·암굴·사령).</summary>
    public EnemyThemeTable[] tables => Entries.Select(e => e.Table).ToArray();

    public bool InArena => EnemyThemeTrialService.InArena;
    public EnemyThemeTrialMode TrialMode => EnemyThemeTrialService.Mode;
    public string LastMessage { get; private set; } = string.Empty;

    public void ToggleArena() => LastMessage = EnemyThemeTrialService.ToggleArena().Message;

    public void Clear() => LastMessage = EnemyThemeTrialService.Clear().Message;

    public bool Begin(int index, bool waves)
    {
        IReadOnlyList<EnemyThemeTrialEntry> list = Entries;
        if (index < 0 || index >= list.Count)
            return false;
        DebugResult result = EnemyThemeTrialService.Begin(list[index], waves);
        LastMessage = result.Message;
        return result.Success;
    }

    public bool SetTrialMode(EnemyThemeTrialMode mode)
    {
        DebugResult result = EnemyThemeTrialService.SetMode(mode);
        LastMessage = result.Message;
        return result.Success;
    }
}
