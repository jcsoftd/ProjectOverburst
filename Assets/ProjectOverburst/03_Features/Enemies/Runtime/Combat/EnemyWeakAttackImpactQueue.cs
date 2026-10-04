using System;

// This queue has no wall-clock timer. Native motion crossings are consumed once.
// Without historical contact poses, at most the newest recoverable crossing can
// be resolved at the next completed physics position; older crossings are misses.
public sealed class EnemyWeakAttackImpactQueue
{
    private readonly float[] times = new float[3];
    private readonly float[] ends = new float[3];
    private int sequence, count, next;
    private uint lease;
    private float progress;
    private int pending = -1;
    public bool IsActive { get; private set; }
    public int ConsumedCount => next;
    public int MissedCount { get; private set; }
    public int LastConsumedIndex => next - 1;
    public bool HasPending => IsActive && pending >= 0;

    public void Begin(int attackSequence, uint actorLease, int hitCount,
        float first, float second, float third, float normalizedContactGrace)
    {
        if (!Finite(normalizedContactGrace) || normalizedContactGrace < 0f || normalizedContactGrace > 1f)
            throw new ArgumentException("약공 사건 접촉 여유가 유효하지 않습니다.");
        Begin(attackSequence, actorLease, hitCount, first, second, third,
            Math.Min(1f, first + normalizedContactGrace), Math.Min(1f, second + normalizedContactGrace),
            Math.Min(1f, third + normalizedContactGrace));
    }

    public void Begin(int attackSequence, uint actorLease, int hitCount,
        float first, float second, float third, float firstEnd, float secondEnd, float thirdEnd)
    {
        if (attackSequence <= 0 || hitCount < 1 || hitCount > 3)
            throw new ArgumentException("약공 사건 스냅샷이 유효하지 않습니다.");
        float a = first, b = second, c = third;
        if (!TimeValid(a) || hitCount > 1 && (!TimeValid(b) || b <= a)
            || hitCount > 2 && (!TimeValid(c) || c <= b))
            throw new ArgumentException("약공 사건은 서로 다른 오름차순 시점이어야 합니다.");
        if (!TimeValid(firstEnd) || firstEnd < a
            || hitCount > 1 && (!TimeValid(secondEnd) || secondEnd < b)
            || hitCount > 2 && (!TimeValid(thirdEnd) || thirdEnd < c))
            throw new ArgumentException("약공 사건의 접촉 종료는 타격 시점 이상이어야 합니다.");
        // Assign only after validation, so a rejected replacement cannot erase
        // the active execution's identity or pending event.
        times[0] = a; times[1] = b; times[2] = c;
        sequence = attackSequence; lease = actorLease; count = hitCount;
        ends[0] = firstEnd; ends[1] = secondEnd; ends[2] = thirdEnd;
        progress = 0f; next = 0; pending = -1;
        MissedCount = 0; IsActive = true;
    }

    public void Advance(float nativeNormalizedTime)
    {
        if (!IsActive) return;
        if (!TimeValid(nativeNormalizedTime) || nativeNormalizedTime < progress)
        { Cancel(); return; }
        progress = nativeNormalizedTime;
        if (pending >= 0 && progress > ends[pending] + EnemyWeakAttackContactGeometry.NormalizedTimeTolerance)
        { pending = -1; MissedCount++; }
        while (next < count && progress >= times[next])
        {
            int index = next++;
            if (pending >= 0) { pending = -1; MissedCount++; }
            if (progress > ends[index] + EnemyWeakAttackContactGeometry.NormalizedTimeTolerance) { MissedCount++; continue; }
            pending = index;
        }
    }

    // Call only after the movement/physics step. A pool lease or successor attack
    // cannot consume the predecessor's pending strike.
    public bool TryTake(int currentSequence, uint currentLease, out int phase)
    {
        phase = -1;
        if (!IsActive) return false;
        if (currentSequence != sequence || currentLease != lease) { Cancel(); return false; }
        if (pending < 0) return false;
        phase = pending; pending = -1;
        return true;
    }

    public void Cancel() { IsActive = false; pending = -1; }
    private static bool TimeValid(float value) => Finite(value) && value >= 0f && value <= 1f;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
