using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(250)]
public sealed class InteractionDirector : MonoBehaviour
{
    private const float DistanceEpsilon = 0.0001f;
    private const float AngleEpsilon = 0.001f;

    private readonly List<IInteractable> candidates = new List<IInteractable>(32);
    private IInteractable current;
    private int eligibleCandidateCount;

    public IInteractable Current => IsAlive(current) ? current : null;
    public int EligibleCandidateCount => eligibleCandidateCount;
    public event Action<IInteractable, IInteractable> SelectionChanged;

    public IInteractable Refresh(PlayerActorRuntime actor)
    {
        IInteractable previous = Current;
        IInteractable next = ResolveBest(actor);
        current = next;
        if (!ReferenceEquals(previous, next))
            SelectionChanged?.Invoke(previous, next);
        return next;
    }

    public InteractionExecutionResult ExecuteCurrent(PlayerActorRuntime actor)
    {
        IInteractable selected = Refresh(actor);
        if (!IsAlive(selected))
            return InteractionExecutionResult.Rejected;
        return selected.TryInteract(actor);
    }

    public void ClearSelection()
    {
        IInteractable previous = Current;
        current = null;
        eligibleCandidateCount = 0;
        if (previous != null)
            SelectionChanged?.Invoke(previous, null);
    }

    private IInteractable ResolveBest(PlayerActorRuntime actor)
    {
        eligibleCandidateCount = 0;
        if (actor == null || actor.Health == null || actor.Health.IsDead)
            return null;

        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        if (flow != null && flow.IsSwitching)
            return null;

        PlayerStateCoordinator coordinator = actor.GetComponent<PlayerStateCoordinator>();
        bool inputBlocked = GameplayInputBlocker.IsGameplayInputBlocked;
        Vector3 actorPosition = actor.transform.position;
        Vector3 actorForward = Vector3.ProjectOnPlane(actor.transform.forward, Vector3.up);
        if (actorForward.sqrMagnitude <= DistanceEpsilon)
            actorForward = Vector3.forward;
        else
            actorForward.Normalize();

        InteractionRegistry.CopyTo(candidates);
        CandidateScore bestScore = default;
        IInteractable best = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            IInteractable candidate = candidates[i];
            if (!IsAlive(candidate))
                continue;
            Component component = candidate.InteractionComponent;
            if (!component.gameObject.activeInHierarchy)
                continue;
            if (component is Behaviour behaviour && !behaviour.isActiveAndEnabled)
                continue;
            if (inputBlocked && !candidate.AllowsInteractionWhileInputBlocked)
                continue;
            if (coordinator != null
                && coordinator.CurrentCondition != PlayerConditionState.Normal
                && !(coordinator.CurrentCondition == PlayerConditionState.InputBlocked
                    && candidate.AllowsInteractionWhileInputBlocked))
            {
                continue;
            }
            if (coordinator != null
                && coordinator.CurrentAction != PlayerActionState.None
                && !candidate.AllowsInteractionWhileInputBlocked)
            {
                continue;
            }
            if (!candidate.IsInteractionAvailable(actor))
                continue;

            Transform target = candidate.InteractionTransform;
            if (target == null)
                continue;
            Vector3 delta = target.position - actorPosition;
            Vector3 horizontal = Vector3.ProjectOnPlane(delta, Vector3.up);
            float distanceSqr = candidate.DistanceMode == InteractionDistanceMode.Horizontal
                ? horizontal.sqrMagnitude
                : delta.sqrMagnitude;
            float range = Mathf.Max(0f, candidate.InteractionRange);
            if (distanceSqr > range * range)
                continue;

            float angle = horizontal.sqrMagnitude <= DistanceEpsilon
                ? 0f
                : Vector3.Angle(actorForward, horizontal);
            CandidateScore score = new CandidateScore(
                candidate.InteractionPriority,
                angle,
                distanceSqr,
                candidate.StableInteractionId ?? string.Empty);
            eligibleCandidateCount++;
            if (best == null || score.IsBetterThan(bestScore))
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool IsAlive(IInteractable interactable)
    {
        return interactable != null && interactable.InteractionComponent != null;
    }

    private readonly struct CandidateScore
    {
        private readonly int priority;
        private readonly float angle;
        private readonly float distanceSqr;
        private readonly string stableId;

        public CandidateScore(int priority, float angle, float distanceSqr, string stableId)
        {
            this.priority = priority;
            this.angle = angle;
            this.distanceSqr = distanceSqr;
            this.stableId = stableId;
        }

        public bool IsBetterThan(CandidateScore other)
        {
            if (priority != other.priority)
                return priority > other.priority;
            if (Mathf.Abs(angle - other.angle) > AngleEpsilon)
                return angle < other.angle;
            if (Mathf.Abs(distanceSqr - other.distanceSqr) > DistanceEpsilon)
                return distanceSqr < other.distanceSqr;
            return string.CompareOrdinal(stableId, other.stableId) < 0;
        }
    }
}
