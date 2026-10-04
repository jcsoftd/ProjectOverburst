using System;

// Running을 명시해 조립 중인 공격을 다음 선택이 덮어쓰지 않게 한다.
public enum CrustaspikanNodeStatus { Failure, Running, Success }
public abstract class CrustaspikanNode { public abstract CrustaspikanNodeStatus Tick(); }
public sealed class CrustaspikanSelector : CrustaspikanNode
{
    private readonly CrustaspikanNode[] children;
    public CrustaspikanSelector(params CrustaspikanNode[] children) { this.children = children; }
    public override CrustaspikanNodeStatus Tick()
    {
        foreach (var child in children) { var result = child.Tick(); if (result != CrustaspikanNodeStatus.Failure) return result; }
        return CrustaspikanNodeStatus.Failure;
    }
}
public sealed class CrustaspikanActionNode : CrustaspikanNode
{
    private readonly Func<bool> condition;
    private readonly Func<CrustaspikanNodeStatus> action;
    public CrustaspikanActionNode(Func<bool> condition, Func<CrustaspikanNodeStatus> action)
    { this.condition = condition; this.action = action; }
    public override CrustaspikanNodeStatus Tick() => condition() ? action() : CrustaspikanNodeStatus.Failure;
}
