using UnityEngine;

public enum EnemyTacticalDecision { Legacy, Hold, Move, Retreat, Navigate }

// Pure position proposals. The active AI state remains the only movement writer.
public sealed class EnemyTacticalPositioning
{
    private readonly EnemyAIController owner;
    private EnemyEncounterTacticsContext context;
    private Transform target;
    private Vector3 destination, observedTarget, progressPosition, failedPoint, targetAtRetreat;
    private float nextEvaluation, progressAt, failedUntil, retreatResetAt = -1f, holdUntil;
    private bool retreatUsed;
    private EnemyTacticalDecision decision;
    public string Reason { get; private set; } = "Legacy";
    public bool RetreatUsed => retreatUsed;
    public uint ContextGeneration => context != null ? context.Generation : 0;
    public Vector3 Destination => destination;
    public EnemyTacticalPositioning(EnemyAIController owner) { this.owner = owner; }
    public void Bind()
    {
        Dispose();
        if (owner.isActiveAndEnabled && owner.TacticalProfile != null) context = EnemyEncounterTacticsContext.Join(owner);
    }
    public void Reset()
    {
        target = null; nextEvaluation = 0; retreatUsed = false; retreatResetAt = -1;
        decision = EnemyTacticalDecision.Legacy; failedUntil = holdUntil = 0;
        context?.SetDestination(owner, Vector3.zero, false); Reason = "Reset";
    }
    public void Dispose() { Reset(); context?.Leave(owner); context = null; }
    public void CommitRetreat()
    {
        if (!retreatUsed && owner.Target != null) targetAtRetreat = owner.Target.position;
        retreatUsed = true;
    }
    public EnemyTacticalDecision Evaluate(out Vector3 point)
    {
        point = owner.transform.position;
        var profile = owner.TacticalProfile;
        var abilities = owner.AbilityController;
        if (profile == null || !profile.UsesRangedPositioning || owner.Target == null || abilities == null)
            return EnemyTacticalDecision.Legacy;
        if (target != owner.Target) { Reset(); target = owner.Target; }
        float now = Time.time;
        Vector3 current = owner.transform.position, aim = target.position;
        if (!abilities.TryGetRangedPositioningAbility(out var ability))
        { Reason = "NoRangedAbility"; return EnemyTacticalDecision.Legacy; }
        float minimum = Mathf.Max(ability.MinimumRange + .15f, profile.PreferredMin);
        float maximum = Mathf.Min(ability.Range - .2f, profile.PreferredMax);
        if (maximum < minimum) { minimum = ability.MinimumRange + .05f; maximum = ability.Range - .05f; }
        if (maximum < minimum) { Reason = "InvalidRange"; return EnemyTacticalDecision.Legacy; }
        float distance = Horizontal(current, aim);
        Vector3 targetTravel = aim - targetAtRetreat; targetTravel.y = 0;
        Vector3 escapeDirection = aim - current; escapeDirection.y = 0;
        if (retreatUsed && distance >= minimum + .4f && Vector3.Dot(targetTravel, escapeDirection.normalized) > .4f)
        {
            if (retreatResetAt < 0) retreatResetAt = now;
            if (now - retreatResetAt >= .5f) retreatUsed = false;
        }
        else retreatResetAt = -1;

        // A retreat has a fixed endpoint. Re-evaluation cannot extend it every tick.
        if (decision == EnemyTacticalDecision.Retreat)
        {
            if (Horizontal(current, destination) > .15f && now < holdUntil)
            { point = destination; return decision; }
            // Finish before consulting the cached plan, including interrupted or blocked retreats.
            decision = EnemyTacticalDecision.Hold; nextEvaluation = 0;
        }
        if (now < nextEvaluation && Horizontal(observedTarget, aim) < .8f)
        { point = destination; return decision; }
        observedTarget = aim;
        bool clear = abilities.HasRangedPositioningLine(ability, target, current);
        if (distance >= minimum - .4f && distance <= maximum && clear)
            return Store(EnemyTacticalDecision.Hold, current, "FiringPosition", now, out point);
        if (distance < minimum - .4f && (retreatUsed || profile.RetreatDistance <= 0))
            return Store(EnemyTacticalDecision.Hold, current, "CloseResponse", now, out point);

        if (context != null && !context.TrySearch())
        {
            Reason = "SearchBudget";
            // Unserved actors retry next tick; serviced actors sleep for their profile interval.
            point = current;
            return EnemyTacticalDecision.Hold;
        }
        if ((decision == EnemyTacticalDecision.Move || decision == EnemyTacticalDecision.Retreat) && now >= progressAt)
        {
            if (Horizontal(current, progressPosition) < .08f)
            { failedPoint = destination; failedUntil = now + 1f; }
            progressPosition = current; progressAt = now + .75f;
        }
        Vector3 away = current - aim; away.y = 0;
        if (away.sqrMagnitude < .01f) away = -owner.transform.forward;
        away.Normalize();
        bool retreat = distance < minimum - .4f;
        float preferred = Mathf.Lerp(minimum, maximum, .55f);
        float best = float.PositiveInfinity; Vector3 selected = current; bool found = false;
        var crowd = owner.GetComponent<EnemyCrowdAgent>();
        float radius = crowd != null ? Mathf.Max(.2f, crowd.BodyRadius) : .45f;
        for (int i = 0; i < 6; i++)
        {
            float angle = i == 0 ? 0 : (i % 2 == 0 ? -1 : 1) * ((i + 1) / 2) * 30;
            Vector3 candidate;
            if (retreat) candidate = current + Quaternion.Euler(0, angle, 0) * away * profile.RetreatDistance;
            else if (i == 0 && decision == EnemyTacticalDecision.Move && now < holdUntil) candidate = destination;
            else if (distance > maximum) candidate = current + Vector3.ClampMagnitude(aim + Quaternion.Euler(0, angle, 0) * away * preferred - current, 3f);
            else candidate = current + Quaternion.Euler(0, angle == 0 ? 90 : angle, 0) * away * 1.5f;
            if (now < failedUntil && Horizontal(candidate, failedPoint) < .5f) continue;
            if (!TrySafeStep(current, candidate, radius, out candidate)) continue;
            float candidateDistance = Horizontal(candidate, aim);
            if (retreat && candidateDistance <= distance + .15f) continue;
            bool candidateLine = abilities.HasRangedPositioningLine(ability, target, candidate);
            // Permit progress around a wide wall, even if one short step cannot clear its edge.
            float score = Horizontal(current, candidate) + Mathf.Abs(candidateDistance - preferred) * .5f
                + (candidateLine ? 0 : 4f) + (context != null ? context.CrowdingCost(owner, candidate, radius) : 0);
            if (i == 0 && decision == EnemyTacticalDecision.Move && now < holdUntil) score -= 1f;
            if (score >= best) continue;
            best = score; selected = candidate; found = true;
        }
        if (!found) return Store(retreat ? EnemyTacticalDecision.Hold : EnemyTacticalDecision.Navigate,
            current, "NoSafePosition", now, out point);
        if (Horizontal(destination, selected) > .3f)
        { progressPosition = current; progressAt = now + .75f; holdUntil = now + (retreat ? 1.2f : .6f); }
        return Store(retreat ? EnemyTacticalDecision.Retreat : EnemyTacticalDecision.Move, selected,
            retreat ? "ShortRetreat" : clear ? "ApproachRange" : "ClearLine", now, out point);
    }
    private EnemyTacticalDecision Store(EnemyTacticalDecision value, Vector3 position, string reason, float now, out Vector3 point)
    {
        decision = value; destination = point = position; Reason = reason;
        nextEvaluation = now + owner.TacticalProfile.EvaluationInterval;
        context?.SetDestination(owner, position, value != EnemyTacticalDecision.Legacy);
        return value;
    }
    private bool TrySafeStep(Vector3 from, Vector3 candidate, float radius, out Vector3 result)
    {
        result = candidate;
        int mask = ~LayerMask.GetMask("Enemy", "Player", "Ignore Raycast");
        if (owner.Movement == null || !owner.Movement.TryResolveWalkableDestination(candidate, out candidate)
            || Horizontal(candidate, result) > .5f) return false;
        Vector3 delta = candidate - from; delta.y = 0;
        if (delta.magnitude < .15f) return false;
        Vector3 bottom = from + Vector3.up * (radius + .12f);
        Vector3 top = from + Vector3.up * Mathf.Max(radius + .12f, 1.5f - radius);
        if (Physics.CapsuleCast(bottom, top, radius, delta.normalized, delta.magnitude, mask, QueryTriggerInteraction.Ignore)) return false;
        int samples = Mathf.Clamp(Mathf.CeilToInt(delta.magnitude / .4f), 1, 10);
        float previousY = from.y;
        for (int i = 1; i <= samples; i++)
        {
            Vector3 sample = Vector3.Lerp(from, candidate, (float)i / samples);
            if (!owner.Movement.IsWalkablePosition(sample)
                || !Physics.Raycast(sample + Vector3.up * 1.5f, Vector3.down, out var hit, 3f, mask, QueryTriggerInteraction.Ignore)
                || hit.normal.y < .72f || Mathf.Abs(hit.point.y - previousY) > .45f) return false;
            previousY = hit.point.y; result = hit.point + Vector3.up * .035f;
        }
        return true;
    }
    private static float Horizontal(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
}
