using System;
using System.Collections.Generic;

// 보스 패턴 선택 전용 최소 행동 트리. 이동·회전·공격 명령은 하지 않고 "지금 허용할 패턴 1개"만 정한다.
public enum EnemyBossNodeStatus { Success, Failure }

public abstract class EnemyBossNode
{
    public abstract EnemyBossNodeStatus Tick();
}

public sealed class EnemyBossSelector : EnemyBossNode
{
    private readonly EnemyBossNode[] children;
    public EnemyBossSelector(params EnemyBossNode[] nodes) => children = nodes;
    public override EnemyBossNodeStatus Tick()
    {
        for (int i = 0; i < children.Length; i++)
            if (children[i].Tick() == EnemyBossNodeStatus.Success) return EnemyBossNodeStatus.Success;
        return EnemyBossNodeStatus.Failure;
    }
}

public sealed class EnemyBossSequence : EnemyBossNode
{
    private readonly EnemyBossNode[] children;
    public EnemyBossSequence(params EnemyBossNode[] nodes) => children = nodes;
    public override EnemyBossNodeStatus Tick()
    {
        for (int i = 0; i < children.Length; i++)
            if (children[i].Tick() == EnemyBossNodeStatus.Failure) return EnemyBossNodeStatus.Failure;
        return EnemyBossNodeStatus.Success;
    }
}

public sealed class EnemyBossCondition : EnemyBossNode
{
    private readonly Func<bool> condition;
    public EnemyBossCondition(Func<bool> check) => condition = check;
    public override EnemyBossNodeStatus Tick() => condition() ? EnemyBossNodeStatus.Success : EnemyBossNodeStatus.Failure;
}

public sealed class EnemyBossAction : EnemyBossNode
{
    private readonly Func<bool> action;
    public EnemyBossAction(Func<bool> run) => action = run;
    public override EnemyBossNodeStatus Tick() => action() ? EnemyBossNodeStatus.Success : EnemyBossNodeStatus.Failure;
}

// 가중 선택에 쓰는 후보 버퍼. 트리 노드가 공유한다.
public sealed class EnemyBossCandidateBuffer
{
    public readonly List<EnemyBossCombatProfile.Pattern> Items = new List<EnemyBossCombatProfile.Pattern>(8);
    public float TotalWeight;
    public void Clear() { Items.Clear(); TotalWeight = 0f; }
    public void Add(EnemyBossCombatProfile.Pattern pattern) { Items.Add(pattern); TotalWeight += pattern.weight; }
    public EnemyBossCombatProfile.Pattern Pick(float zeroToOne)
    {
        float cursor = zeroToOne * TotalWeight;
        for (int i = 0; i < Items.Count; i++)
        {
            cursor -= Items[i].weight;
            if (cursor <= 0f) return Items[i];
        }
        return Items.Count > 0 ? Items[Items.Count - 1] : null;
    }
}
