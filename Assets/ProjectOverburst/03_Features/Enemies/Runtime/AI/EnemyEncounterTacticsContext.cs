using System.Collections.Generic;
using UnityEngine;

// Membership is separate from melee squad assignment. No target or motor writes.
public sealed class EnemyEncounterTacticsContext
{
    private sealed class Member
    { public EnemyActor actor; public uint lease; public Vector3 destination; public bool hasDestination; }
    private static readonly Dictionary<GameObject, EnemyEncounterTacticsContext> contexts = new Dictionary<GameObject, EnemyEncounterTacticsContext>();
    private readonly Dictionary<EnemyAIController, Member> members = new Dictionary<EnemyAIController, Member>();
    private readonly GameObject owner;
    private static uint nextGeneration;
    private int searchFrame = -1, searches;
    public uint Generation { get; }
    public int MemberCount => members.Count;
    public static int ContextCount => contexts.Count;
    private EnemyEncounterTacticsContext(GameObject owner) { this.owner = owner; Generation = ++nextGeneration; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { contexts.Clear(); nextGeneration = 0; }
    public static EnemyEncounterTacticsContext Join(EnemyAIController ai)
    {
        GameObject key = ai.SquadEncounterOwner != null ? ai.SquadEncounterOwner : ai.gameObject;
        if (!contexts.TryGetValue(key, out var context))
        { context = new EnemyEncounterTacticsContext(key); contexts.Add(key, context); }
        var actor = ai.GetComponent<EnemyActor>();
        context.members[ai] = new Member { actor = actor, lease = actor != null ? actor.LeaseVersion : 0 };
        return context;
    }
    public void Leave(EnemyAIController ai)
    {
        members.Remove(ai);
        if (members.Count == 0) contexts.Remove(owner);
    }
    private static bool Current(EnemyAIController ai, Member member) => ai != null && ai.isActiveAndEnabled
        && ai.CurrentStateName != "Dead" && (member.actor == null || member.actor.IsLeased
        && member.actor.LeaseVersion == member.lease && !member.actor.Health.IsDead);
    public void SetDestination(EnemyAIController ai, Vector3 position, bool active)
    {
        if (!members.TryGetValue(ai, out var member)) return;
        member.destination = position; member.hasDestination = active;
    }
    public float CrowdingCost(EnemyAIController ai, Vector3 position, float radius)
    {
        float cost = 0;
        foreach (var pair in members)
        {
            if (pair.Key == ai || !Current(pair.Key, pair.Value) || pair.Key.Target != ai.Target || !pair.Value.hasDestination) continue;
            float distance = Vector3.Distance(position, pair.Value.destination);
            cost += Mathf.Max(0, radius * 2f + .3f - distance) * 3f;
        }
        return cost;
    }
    public bool TrySearch()
    {
        if (searchFrame != Time.frameCount) { searchFrame = Time.frameCount; searches = 0; }
        if (searches >= 2) return false;
        searches++; return true;
    }
}
