using UnityEngine;

// 보스 실행기 목록 맨 앞에 둔다. 보스 두뇌(EnemyBossCombatDirector)가 고른 패턴 1개만 CanStart를 통과시키고,
// 실제 실행은 같은 오브젝트의 기존 실행기(근접·범위·돌진)에 넘긴다. 이동·회전·공격 명령 경로는 기존 AI 그대로다.
[DisallowMultipleComponent]
public sealed class EnemyBossPatternExecutor : EnemyAbilityExecutor
{
    [SerializeField] private EnemyBossCombatDirector director;
    private EnemyAbilityExecutor[] others;

    public override bool IsExecuting => false; // 실제 실행기가 따로 목록에 있어 진행 여부는 그쪽이 보고한다

    private void Awake() => Resolve();

    private void Resolve()
    {
        if (director == null) director = GetComponent<EnemyBossCombatDirector>();
        if (others != null) return;
        var all = GetComponents<EnemyAbilityExecutor>();
        int count = 0;
        foreach (var executor in all) if (executor != this) count++;
        others = new EnemyAbilityExecutor[count];
        count = 0;
        foreach (var executor in all) if (executor != this) others[count++] = executor;
    }

    private EnemyAbilityExecutor Inner(EnemyAbilityDefinition ability)
    {
        Resolve();
        for (int i = 0; i < others.Length; i++)
            if (others[i] != null && others[i].Supports(ability)) return others[i];
        return null;
    }

    public override bool Supports(EnemyAbilityDefinition ability)
    {
        Resolve();
        return director != null && director.OwnsPattern(ability) && Inner(ability) != null;
    }

    public override bool CanStart(EnemyAbilityDefinition ability, Transform target)
    {
        var inner = Inner(ability);
        return inner != null && director != null && director.Allows(ability, target) && inner.CanStart(ability, target);
    }

    public override bool TryStart(EnemyAbilityDefinition ability, int abilityIndex, Transform target)
    {
        var inner = Inner(ability);
        if (inner == null || director == null || !director.Allows(ability, target)) return false;
        if (!inner.TryStart(ability, abilityIndex, target)) return false;
        director.NotifyCommitted(ability);
        return true;
    }

    public override float ResolveCooldown(float baseCooldown)
    {
        Resolve();
        return others.Length > 0 && others[0] != null ? others[0].ResolveCooldown(baseCooldown) : Mathf.Max(0f, baseCooldown);
    }

    // 취소·재사용 초기화는 EnemyAbilityController가 목록의 실제 실행기에 직접 전달한다.
    public override void Cancel() { }
    public override void ResetForReuse() { }

    public void Configure(EnemyBossCombatDirector bossDirector) { director = bossDirector; others = null; }
}
