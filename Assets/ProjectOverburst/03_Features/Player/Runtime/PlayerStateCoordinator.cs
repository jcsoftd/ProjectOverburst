using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// GOAL A2: 플레이어 Condition/Locomotion/Action 세 축을 소유하는 단일 상태 컴포넌트.
// - 한 축 변경으로 다른 축을 자동 추론/덮어쓰기하지 않는다.
// - CanMove/CanAttack 같은 질의는 세 축을 조합할 수 있지만 저장은 축별 명시 요청이다.
// - source별 요청/해제로 중첩 소유자를 처리한다. destroyed source는 정리한다.
// 우선순위(문서화):
// - Condition: Dead > Stunned > InputBlocked > Normal
// - Locomotion: Evading > ControlledMove > Airborne > Moving > Idle
// - Action: Attack > GuardOrAim > Interacting > None
[DisallowMultipleComponent]
public sealed class PlayerStateCoordinator : MonoBehaviour
{
    public static PlayerStateCoordinator Current { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState() => Current = null;

    public PlayerConditionState CurrentCondition { get; private set; } = PlayerConditionState.Normal;
    public PlayerLocomotionState CurrentLocomotion { get; private set; } = PlayerLocomotionState.Idle;
    public PlayerActionState CurrentAction { get; private set; } = PlayerActionState.None;

    public event Action<PlayerConditionState> ConditionChanged;
    public event Action<PlayerLocomotionState> LocomotionChanged;
    public event Action<PlayerActionState> ActionChanged;

    [SerializeField] private PlayerActorRuntime actor;

    private readonly Dictionary<UnityEngine.Object, PlayerConditionState> conditionOwners =
        new Dictionary<UnityEngine.Object, PlayerConditionState>();
    private readonly Dictionary<UnityEngine.Object, PlayerLocomotionState> locomotionOwners =
        new Dictionary<UnityEngine.Object, PlayerLocomotionState>();
    private readonly Dictionary<UnityEngine.Object, PlayerActionState> actionOwners =
        new Dictionary<UnityEngine.Object, PlayerActionState>();

    private bool subscribedBlocker;
    private readonly List<UnityEngine.Object> deadKeyBuffer = new List<UnityEngine.Object>(8);

    // 질의는 세 축 조합. 저장에는 영향을 주지 않는다.
    public bool CanMove => CurrentCondition == PlayerConditionState.Normal
        && (CurrentLocomotion == PlayerLocomotionState.Idle
            || CurrentLocomotion == PlayerLocomotionState.Moving
            || CurrentLocomotion == PlayerLocomotionState.Airborne);

    public bool CanAttack => CurrentCondition == PlayerConditionState.Normal
        && CurrentAction == PlayerActionState.None;

    public bool CanInteract => CurrentCondition == PlayerConditionState.Normal
        && CurrentAction == PlayerActionState.None;

    private void Awake()
    {
        ResolveActor();
        if (Current != null && Current != this)
            Debug.LogWarning("[PlayerStateCoordinator] Current가 이미 있어 교체한다.", this);
        Current = this;
    }

    private void OnEnable()
    {
        if (Current != null && Current != this)
            Debug.LogWarning("[PlayerStateCoordinator] Current가 이미 있어 교체한다.", this);
        Current = this;
        ResolveActor();
        SubscribeBlocker();
        SyncInputBlockedFromBlocker();
        RecomputeConditions(false);
        RecomputeLocomotion(false);
        RecomputeAction(false);
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        UnsubscribeBlocker();
        // transient이 남지 않도록 전체 요청을 폐기한다. Dead도 해제되며,
        // 재활성화 시 차단 상태는 재동기화되고 Dead는 PlayerActorRuntime이 동기화한다.
        ClearAllRequests();
        if (Current == this)
            Current = null;
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        UnsubscribeBlocker();
        ClearAllRequests();
        if (Current == this)
            Current = null;
    }

    private void LateUpdate()
    {
        // 파괴된 소유자는 다음 요청 때까지 stale로 남으므로 매 프레임 정리한다.
        // 비어 있지 않은 사전만 훑고, 공용 버퍼를 재사용하며(사망자 없음 시 할당 없음),
        // 실제로 키를 잃은 축만 재계산/이벤트한다. 재계산 내부에서 재귀 정리하지 않는다.
        if (conditionOwners.Count == 0 && locomotionOwners.Count == 0 && actionOwners.Count == 0)
            return;
        bool conditionPruned = RemoveDestroyedKeys(conditionOwners);
        bool locomotionPruned = RemoveDestroyedKeys(locomotionOwners);
        bool actionPruned = RemoveDestroyedKeys(actionOwners);
        if (conditionPruned)
            RecomputeConditions(true);
        if (locomotionPruned)
            RecomputeLocomotion(true);
        if (actionPruned)
            RecomputeAction(true);
    }

    private bool RemoveDestroyedKeys<T>(Dictionary<UnityEngine.Object, T> owners)
    {
        if (owners.Count == 0)
            return false;
        deadKeyBuffer.Clear();
        foreach (var pair in owners)
        {
            if (pair.Key == null)
                deadKeyBuffer.Add(pair.Key);
        }
        if (deadKeyBuffer.Count == 0)
            return false;
        foreach (UnityEngine.Object key in deadKeyBuffer)
            owners.Remove(key);
        deadKeyBuffer.Clear();
        return true;
    }

    // ---- Condition ----
    public void RequestCondition(UnityEngine.Object source, PlayerConditionState state)
    {
        if (source == null || state == PlayerConditionState.Normal)
            return;
        conditionOwners[source] = state;
        RemoveDestroyedKeys(conditionOwners);
        RecomputeConditions(true);
    }

    public void ReleaseCondition(UnityEngine.Object source)
    {
        if (source == null)
            return;
        bool removed = conditionOwners.Remove(source);
        removed |= RemoveDestroyedKeys(conditionOwners);
        if (removed)
            RecomputeConditions(true);
    }

    public void RequestInputBlocked(UnityEngine.Object source) => RequestCondition(source, PlayerConditionState.InputBlocked);

    // Stunned는 미래 호출용 명시 API다. 새 스턴 기능은 만들지 않는다.
    public void RequestStun(UnityEngine.Object source) => RequestCondition(source, PlayerConditionState.Stunned);
    public void ReleaseStun(UnityEngine.Object source)
    {
        if (source == null)
            return;
        if (conditionOwners.TryGetValue(source, out PlayerConditionState state)
            && state == PlayerConditionState.Stunned)
            ReleaseCondition(source);
    }

    // Dead는 PlayerActorRuntime health 이벤트로 명시 연결한다. 외부 임의 호출용이 아니다.
    public void RequestDeath(UnityEngine.Object source) => RequestCondition(source, PlayerConditionState.Dead);
    public void ReleaseDeath(UnityEngine.Object source)
    {
        if (source == null)
            return;
        if (conditionOwners.TryGetValue(source, out PlayerConditionState state)
            && state == PlayerConditionState.Dead)
            ReleaseCondition(source);
    }

    // ---- Locomotion ----
    public void RequestLocomotion(UnityEngine.Object source, PlayerLocomotionState state)
    {
        if (source == null)
            return;
        locomotionOwners[source] = state;
        RemoveDestroyedKeys(locomotionOwners);
        RecomputeLocomotion(true);
    }

    public void ReleaseLocomotion(UnityEngine.Object source)
    {
        if (source == null)
            return;
        bool removed = locomotionOwners.Remove(source);
        removed |= RemoveDestroyedKeys(locomotionOwners);
        if (removed)
            RecomputeLocomotion(true);
    }

    // ---- Action ----
    public void RequestAction(UnityEngine.Object source, PlayerActionState state)
    {
        if (source == null || state == PlayerActionState.None)
            return;
        actionOwners[source] = state;
        RemoveDestroyedKeys(actionOwners);
        RecomputeAction(true);
    }

    public void ReleaseAction(UnityEngine.Object source)
    {
        if (source == null)
            return;
        bool removed = actionOwners.Remove(source);
        removed |= RemoveDestroyedKeys(actionOwners);
        if (removed)
            RecomputeAction(true);
    }

    // Interacting 확장 API. 포탈/상인/창고 실행 구간에서만 사용하고,
    // stale 우려가 있으면 연결하지 않는다.
    public void RequestInteracting(UnityEngine.Object source) => RequestAction(source, PlayerActionState.Interacting);
    public void ReleaseInteracting(UnityEngine.Object source)
    {
        if (source == null)
            return;
        if (actionOwners.TryGetValue(source, out PlayerActionState state)
            && state == PlayerActionState.Interacting)
            ReleaseAction(source);
    }

    public void ClearTransientStates()
    {
        // 파괴된 소유자 정리로 축이 바뀌었으면 뒤의 재계산이 이벤트를 올리도록 초기값을 살린다.
        // 씬 전환 직전에 유일한 Dead 소유자가 파괴됐어도 stale Dead가 남지 않는다.
        // 축별 재계산은 아래에서 한 번씩만 하므로 중복 이벤트가 없다.
        bool changedCondition = RemoveDestroyedKeys(conditionOwners);
        bool changedLocomotion = RemoveDestroyedKeys(locomotionOwners);
        bool changedAction = RemoveDestroyedKeys(actionOwners);
        var conditionKeys = new List<UnityEngine.Object>(conditionOwners.Keys);
        foreach (UnityEngine.Object key in conditionKeys)
        {
            if (conditionOwners[key] != PlayerConditionState.Dead)
            {
                conditionOwners.Remove(key);
                changedCondition = true;
            }
        }
        if (locomotionOwners.Count > 0)
        {
            locomotionOwners.Clear();
            changedLocomotion = true;
        }
        if (actionOwners.Count > 0)
        {
            actionOwners.Clear();
            changedAction = true;
        }
        if (changedCondition)
            RecomputeConditions(true);
        if (changedLocomotion)
            RecomputeLocomotion(true);
        if (changedAction)
            RecomputeAction(true);
    }

    public void ResetAllStates()
    {
        ClearAllRequests();
    }

    // 사망 소유권은 PlayerActorRuntime이 갖는다. 여기서는 참조만 보관한다.
    public void BindActor(PlayerActorRuntime value)
    {
        actor = value;
    }

    private void ClearAllRequests()
    {
        conditionOwners.Clear();
        locomotionOwners.Clear();
        actionOwners.Clear();
        if (CurrentCondition != PlayerConditionState.Normal)
        {
            CurrentCondition = PlayerConditionState.Normal;
            try { ConditionChanged?.Invoke(CurrentCondition); } catch (Exception e) { Debug.LogException(e); }
        }
        if (CurrentLocomotion != PlayerLocomotionState.Idle)
        {
            CurrentLocomotion = PlayerLocomotionState.Idle;
            try { LocomotionChanged?.Invoke(CurrentLocomotion); } catch (Exception e) { Debug.LogException(e); }
        }
        if (CurrentAction != PlayerActionState.None)
        {
            CurrentAction = PlayerActionState.None;
            try { ActionChanged?.Invoke(CurrentAction); } catch (Exception e) { Debug.LogException(e); }
        }
    }

    private void ResolveActor()
    {
        if (actor == null)
            actor = GetComponent<PlayerActorRuntime>();
    }

    private void SubscribeBlocker()
    {
        if (subscribedBlocker)
            return;
        GameplayInputBlocker.BlockStateChanged += HandleBlockStateChanged;
        subscribedBlocker = true;
    }

    private void UnsubscribeBlocker()
    {
        if (!subscribedBlocker)
            return;
        try { GameplayInputBlocker.BlockStateChanged -= HandleBlockStateChanged; } catch (Exception) { }
        subscribedBlocker = false;
    }

    private void HandleBlockStateChanged(bool blocked)
    {
        if (!isActiveAndEnabled)
            return;
        if (blocked)
            RequestCondition(this, PlayerConditionState.InputBlocked);
        else
            ReleaseInputBlockedIfOwned();
    }

    private void ReleaseInputBlockedIfOwned()
    {
        if (conditionOwners.TryGetValue(this, out PlayerConditionState state)
            && state == PlayerConditionState.InputBlocked)
            ReleaseCondition(this);
    }

    private void SyncInputBlockedFromBlocker()
    {
        try
        {
            if (GameplayInputBlocker.IsGameplayInputBlocked)
                RequestCondition(this, PlayerConditionState.InputBlocked);
            else
                ReleaseInputBlockedIfOwned();
        }
        catch (Exception) { }
    }

    private void HandleActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        ClearTransientStates();
        SyncInputBlockedFromBlocker();
    }

    private void HandleSceneUnloaded(Scene scene)
    {
        ClearTransientStates();
    }

    private void RecomputeConditions(bool notify)
    {
        PlayerConditionState next = PlayerConditionState.Normal;
        foreach (PlayerConditionState state in conditionOwners.Values)
        {
            if (state == PlayerConditionState.Dead)
            {
                next = PlayerConditionState.Dead;
                break;
            }
            if (state == PlayerConditionState.Stunned)
                next = PlayerConditionState.Stunned;
            else if (state == PlayerConditionState.InputBlocked && next == PlayerConditionState.Normal)
                next = PlayerConditionState.InputBlocked;
        }
        if (next == CurrentCondition)
            return;
        CurrentCondition = next;
        if (notify)
        {
            try { ConditionChanged?.Invoke(CurrentCondition); } catch (Exception e) { Debug.LogException(e); }
        }
    }

    private void RecomputeLocomotion(bool notify)
    {
        PlayerLocomotionState next = PlayerLocomotionState.Idle;
        foreach (PlayerLocomotionState state in locomotionOwners.Values)
        {
            if (state == PlayerLocomotionState.Evading)
            {
                next = PlayerLocomotionState.Evading;
                break;
            }
            if (state == PlayerLocomotionState.ControlledMove)
                next = PlayerLocomotionState.ControlledMove;
            else if (state == PlayerLocomotionState.Airborne && next != PlayerLocomotionState.ControlledMove)
                next = PlayerLocomotionState.Airborne;
            else if (state == PlayerLocomotionState.Moving && next == PlayerLocomotionState.Idle)
                next = PlayerLocomotionState.Moving;
        }
        if (next == CurrentLocomotion)
            return;
        CurrentLocomotion = next;
        if (notify)
        {
            try { LocomotionChanged?.Invoke(CurrentLocomotion); } catch (Exception e) { Debug.LogException(e); }
        }
    }

    private void RecomputeAction(bool notify)
    {
        PlayerActionState next = PlayerActionState.None;
        foreach (PlayerActionState state in actionOwners.Values)
        {
            if (state == PlayerActionState.Attack)
            {
                next = PlayerActionState.Attack;
                break;
            }
            if (state == PlayerActionState.GuardOrAim)
                next = PlayerActionState.GuardOrAim;
            else if (state == PlayerActionState.Interacting && next == PlayerActionState.None)
                next = PlayerActionState.Interacting;
        }
        if (next == CurrentAction)
            return;
        CurrentAction = next;
        if (notify)
        {
            try { ActionChanged?.Invoke(CurrentAction); } catch (Exception e) { Debug.LogException(e); }
        }
    }
}
