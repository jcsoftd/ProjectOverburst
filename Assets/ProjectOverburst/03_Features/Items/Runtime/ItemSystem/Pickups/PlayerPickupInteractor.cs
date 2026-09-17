using System;
using System.Collections.Generic;
using UnityEngine;

public enum WorldLootInteractionMode // 월드 아이템 표시/획득 모드
{
    CombatAutoLegendary,
    LootFocus,
    CombatForcedHidden
}

public enum WorldLootPickupRequestResult // UI/자동 이동 요청 결과
{
    None,
    Succeeded,
    InputBlocked,
    ModeRejected,
    InvalidTarget,
    InventoryUnavailable,
    InventoryRejected,
    AutoMoveUnavailable,
    AutoMoveRejected,
    AutoMovePending,
    AutoMoveCancelled,
    AutoMoveFailed,
    OutOfRange
}

public enum WorldLootAutoMoveDriverResult // 40번대 이동 종료 결과
{
    Arrived,
    Cancelled,
    Failed,
    TargetInvalid
}

public interface IWorldLootAutoMoveDriver // 40번대 자동 이동 경계
{
    bool TryBegin(WorldLootAutoMoveRequest request);
    void Cancel(WorldItemPickup target);
}

public sealed class WorldLootAutoMoveRequest
{
    private readonly Action<WorldLootAutoMoveDriverResult> completion;
    private bool completed;

    public WorldItemPickup Target { get; }
    public float PickupRadius { get; }

    public WorldLootAutoMoveRequest(
        WorldItemPickup target,
        float pickupRadius,
        Action<WorldLootAutoMoveDriverResult> onCompleted)
    {
        Target = target;
        PickupRadius = Mathf.Max(0f, pickupRadius);
        completion = onCompleted;
    }

    public void Complete(WorldLootAutoMoveDriverResult result)
    {
        if (completed)
            return;

        completed = true;
        completion?.Invoke(result); // 종료 1회 전달
    }
}

public readonly struct WorldLootPickupSnapshot
{
    public WorldItemPickup Pickup { get; }
    public ItemData Item { get; }
    public float Distance { get; }
    public bool CanPickup { get; }
    public bool IsWithinPickupRange { get; }
    public bool IsPersistentLabelTarget { get; }

    public WorldLootPickupSnapshot(
        WorldItemPickup pickup,
        ItemData item,
        float distance,
        bool canPickup,
        bool isWithinPickupRange,
        bool isPersistentLabelTarget)
    {
        Pickup = pickup;
        Item = item;
        Distance = distance;
        CanPickup = canPickup;
        IsWithinPickupRange = isWithinPickupRange;
        IsPersistentLabelTarget = isPersistentLabelTarget;
    }
}

public readonly struct WorldLootSelectionCandidate
{
    public readonly WorldItemPickup Pickup;
    public readonly int InstanceId;
    public readonly float DistanceSqr;
    public readonly bool IsWithinRange;
    public readonly bool IsVisiblePriority;

    public WorldLootSelectionCandidate(
        WorldItemPickup pickup,
        int instanceId,
        float distanceSqr,
        bool isWithinRange,
        bool isVisiblePriority)
    {
        Pickup = pickup;
        InstanceId = instanceId;
        DistanceSqr = distanceSqr;
        IsWithinRange = isWithinRange;
        IsVisiblePriority = isVisiblePriority;
    }
}

public static class WorldLootNearestSelectionPolicy
{
    public static int ResolveNearestCandidateIndex(
        IReadOnlyList<WorldLootSelectionCandidate> candidates,
        bool inputBlocked)
    {
        if (inputBlocked || candidates == null)
            return -1; // 입력 차단

        int visibleIndex = FindNearest(candidates, true);
        return visibleIndex >= 0 ? visibleIndex : FindNearest(candidates, false); // 표시 우선 후 전체 fallback
    }

    private static int FindNearest(
        IReadOnlyList<WorldLootSelectionCandidate> candidates,
        bool visiblePriorityOnly)
    {
        int nearestIndex = -1;
        float nearestDistanceSqr = float.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            WorldLootSelectionCandidate candidate = candidates[i];
            if (!candidate.IsWithinRange
                || (visiblePriorityOnly && !candidate.IsVisiblePriority))
            {
                continue;
            }

            if (candidate.DistanceSqr < nearestDistanceSqr
                && !Mathf.Approximately(candidate.DistanceSqr, nearestDistanceSqr))
            {
                nearestIndex = i;
                nearestDistanceSqr = candidate.DistanceSqr;
                continue;
            }

            if (Mathf.Approximately(candidate.DistanceSqr, nearestDistanceSqr)
                && (nearestIndex < 0 || candidate.InstanceId < candidates[nearestIndex].InstanceId))
            {
                nearestIndex = i;
                nearestDistanceSqr = candidate.DistanceSqr;
            }
        }

        return nearestIndex;
    }

    public static bool IsVisiblePriorityCandidate(bool isActuallyVisible)
    {
        return isActuallyVisible; // 최종 표시 집합을 모드·등급과 중복 필터링하지 않음
    }
}

public sealed class WorldLootActiveSetRevisionTracker
{
    private readonly HashSet<int> activeIds = new HashSet<int>(); // 현재 유효 ID 집합
    private readonly HashSet<int> candidateIds = new HashSet<int>(); // 다음 유효 ID 집합

    public int Revision { get; private set; }

    public bool Update(IReadOnlyList<int> instanceIds)
    {
        candidateIds.Clear();
        if (instanceIds != null)
        {
            for (int i = 0; i < instanceIds.Count; i++)
                candidateIds.Add(instanceIds[i]);
        }

        if (candidateIds.SetEquals(activeIds))
            return false; // 거리/선택 변화는 무시

        activeIds.Clear();
        foreach (int instanceId in candidateIds)
            activeIds.Add(instanceId);

        Revision++;
        return true; // 유효 멤버십 변경
    }
}

#if UNITY_EDITOR
public static class WorldLootActiveSetRevisionFixture
{
    public static bool Validate()
    {
        WorldLootActiveSetRevisionTracker tracker = new WorldLootActiveSetRevisionTracker();
        if (tracker.Update(Array.Empty<int>()) || tracker.Revision != 0)
            return false; // 공중 빈 목록

        if (!tracker.Update(new[] { 101 }) || tracker.Revision != 1)
            return false; // 착지 1개

        if (tracker.Update(new[] { 101 }) || tracker.Revision != 1)
            return false; // 이동/거리/선택 변화

        if (!tracker.Update(Array.Empty<int>()) || tracker.Revision != 2)
            return false; // 제거

        return true;
    }
}

public static class WorldLootPickupLifecycleFixture
{
    public static bool Validate()
    {
        WorldLootActiveSetRevisionTracker tracker = new WorldLootActiveSetRevisionTracker();

        // 생성 직후에는 등록돼도 ItemData가 없어 유효 집합이 아니다.
        if (Update(tracker, true, false, false) || tracker.Revision != 0)
            return false;

        // 지연 주입과 착지 뒤 한 번만 유효 집합에 들어온다.
        if (!Update(tracker, true, true, true) || tracker.Revision != 1)
            return false;

        // 비활성화는 이름표 입력 집합에서 즉시 빠진다.
        if (!Update(tracker, false, true, true) || tracker.Revision != 2)
            return false;

        // 재활성화된 기존 런타임 아이템은 다시 같은 집합에 들어온다.
        if (!Update(tracker, true, true, true) || tracker.Revision != 3)
            return false;

        // 획득·파괴는 등록 해제로 처리된다.
        if (!Update(tracker, false, true, true) || tracker.Revision != 4)
            return false;

        return true;
    }

    private static bool Update(
        WorldLootActiveSetRevisionTracker tracker,
        bool registered,
        bool hasRuntimeItem,
        bool canPickup)
    {
        bool valid = registered && hasRuntimeItem && canPickup;
        return tracker.Update(valid ? new[] { 701 } : Array.Empty<int>());
    }
}

public static class WorldLootActivePickupCountFixture
{
    public static bool Validate()
    {
        const int simultaneousPickupCount = 32;
        GameObject coreObject = null;
        BaseItemData baseItemData = null;
        List<GameObject> pickupObjects = new List<GameObject>(simultaneousPickupCount);

        try
        {
            coreObject = new GameObject("WorldLootActivePickupCountFixture_Core");
            PlayerPickupInteractor core = coreObject.AddComponent<PlayerPickupInteractor>();
            if (!Application.isPlaying)
            {
                InvokeFixtureLifecycle(core, "Awake");
                InvokeFixtureLifecycle(core, "OnEnable");
            }

            baseItemData = ScriptableObject.CreateInstance<BaseItemData>();
            baseItemData.itemName = "WorldLootActivePickupCountFixture_Item";

            for (int i = 0; i < simultaneousPickupCount; i++)
            {
                GameObject pickupObject = new GameObject("WorldLootActivePickupCountFixture_Pickup_" + i);
                pickupObjects.Add(pickupObject);
                WorldItemPickup pickup = pickupObject.AddComponent<WorldItemPickup>();
                if (!Application.isPlaying)
                {
                    InvokeFixtureLifecycle(pickup, "Awake");
                    InvokeFixtureLifecycle(pickup, "OnEnable");
                }

                if (i == 0)
                {
                    if (!Application.isPlaying)
                        InvokeFixtureLifecycle(pickup, "OnDisable");
                    pickup.enabled = false; // 비활성 컴포넌트 초기화 경계
                }

                pickup.Initialize(
                    new ItemData(baseItemData, 1, ItemGrade.Common),
                    null,
                    null);

                if (!Application.isPlaying && i == 0)
                    InvokeFixtureLifecycle(pickup, "OnEnable");

                WorldItemDropMotion motion = pickup.GetComponent<WorldItemDropMotion>();
                if (motion != null)
                    motion.SettleImmediately();
            }

            if (!Application.isPlaying)
                InvokeFixtureLifecycle(core, "RebuildSnapshot");

            int validPickupCount = 0;
            for (int i = 0; i < pickupObjects.Count; i++)
            {
                WorldItemPickup pickup = pickupObjects[i].GetComponent<WorldItemPickup>();
                if (pickup != null && pickup.CanPickup)
                    validPickupCount++;
            }

            WorldLootInteractionSnapshot snapshot = core.CurrentSnapshot;
            int snapshotPickupCount = snapshot != null ? snapshot.ActivePickups.Count : -1;
            bool passed = snapshot != null
                && validPickupCount == simultaneousPickupCount
                && snapshotPickupCount == validPickupCount;
            if (!passed)
            {
                Debug.LogError(
                    "WorldLootActivePickupCountFixture failed. "
                    + "valid=" + validPickupCount
                    + ", snapshot=" + snapshotPickupCount
                    + ", expected=" + simultaneousPickupCount);
            }

            return passed;
        }
        finally
        {
            for (int i = pickupObjects.Count - 1; i >= 0; i--)
            {
                if (pickupObjects[i] != null)
                {
                    if (!Application.isPlaying)
                        InvokeFixtureLifecycle(pickupObjects[i].GetComponent<WorldItemPickup>(), "OnDisable");
                    UnityEngine.Object.DestroyImmediate(pickupObjects[i]);
                }
            }

            if (baseItemData != null)
                UnityEngine.Object.DestroyImmediate(baseItemData);

            if (coreObject != null)
            {
                if (!Application.isPlaying)
                    InvokeFixtureLifecycle(coreObject.GetComponent<PlayerPickupInteractor>(), "OnDisable");
                UnityEngine.Object.DestroyImmediate(coreObject);
            }
        }
    }

    private static void InvokeFixtureLifecycle(object target, string methodName)
    {
        if (target == null)
            return;

        System.Reflection.MethodInfo method = target.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
        if (method == null)
            throw new InvalidOperationException("Fixture lifecycle method missing: " + methodName);

        method.Invoke(target, null);
    }
}

public static class WorldLootNearestSelectionFixture
{
    public static bool Validate()
    {
        List<WorldLootSelectionCandidate> candidates = new List<WorldLootSelectionCandidate>
        {
            new WorldLootSelectionCandidate(null, 20, 1f, true, false),
            new WorldLootSelectionCandidate(null, 30, 4f, true, true)
        };
        if (!ExpectInstance(candidates, false, 30))
            return false; // 먼 visible 우선

        candidates[1] = new WorldLootSelectionCandidate(null, 30, 4f, true, false);
        if (!ExpectInstance(candidates, false, 20))
            return false; // visible 없음 fallback

        candidates.Clear();
        candidates.Add(new WorldLootSelectionCandidate(null, 40, 3f, true, false));
        candidates.Add(new WorldLootSelectionCandidate(null, 10, 3f, true, false));
        if (!ExpectInstance(candidates, false, 10))
            return false; // 거리·instanceId 동률

        candidates.Clear();
        bool combatAutoLegendaryHoverVisible =
            WorldLootNearestSelectionPolicy.IsVisiblePriorityCandidate(true);
        candidates.Add(new WorldLootSelectionCandidate(null, 50, 1f, true, false));
        candidates.Add(new WorldLootSelectionCandidate(null, 60, 4f, true,
            combatAutoLegendaryHoverVisible));
        if (!ExpectInstance(candidates, false, 60))
            return false; // CombatAutoLegendary 일반 hover visible 우선

        candidates.Clear();
        bool combatForcedHiddenHoverVisible =
            WorldLootNearestSelectionPolicy.IsVisiblePriorityCandidate(true);
        candidates.Add(new WorldLootSelectionCandidate(null, 70, 1f, true, false));
        candidates.Add(new WorldLootSelectionCandidate(null, 80, 4f, true,
            combatForcedHiddenHoverVisible));
        if (!ExpectInstance(candidates, false, 80))
            return false; // CombatForcedHidden hover visible 우선

        if (!WorldLootNearestSelectionPolicy.IsVisiblePriorityCandidate(true)
            || WorldLootNearestSelectionPolicy.IsVisiblePriorityCandidate(false))
        {
            return false; // 실제 표시 여부만 사용
        }

        if (!ExpectInstance(candidates, true, -1))
            return false; // inputBlocked 차단

        return true;
    }

    private static bool ExpectInstance(
        IReadOnlyList<WorldLootSelectionCandidate> candidates,
        bool inputBlocked,
        int expectedInstanceId)
    {
        int index = WorldLootNearestSelectionPolicy.ResolveNearestCandidateIndex(candidates, inputBlocked);
        return expectedInstanceId < 0
            ? index < 0
            : index >= 0 && candidates[index].InstanceId == expectedInstanceId;
    }
}
#endif

public sealed class WorldLootInteractionSnapshot
{
    public int Revision { get; }
    public int ActiveSetRevision { get; }
    public WorldLootInteractionMode Mode { get; }
    public IReadOnlyList<WorldLootPickupSnapshot> ActivePickups { get; }
    public WorldItemPickup SelectedPickup { get; }
    public WorldItemPickup PendingAutoMovePickup { get; }
    public float PickupRadius { get; }
    public bool IsInputBlocked { get; }
    public bool SuppressPrimaryAttackUntilRelease { get; }
    public WorldLootPickupRequestResult LastRequestResult { get; }

    public WorldLootInteractionSnapshot(
        int revision,
        WorldLootInteractionMode mode,
        WorldLootPickupSnapshot[] activePickups,
        WorldItemPickup selectedPickup,
        WorldItemPickup pendingAutoMovePickup,
        float pickupRadius,
        bool isInputBlocked,
        bool suppressPrimaryAttackUntilRelease,
        WorldLootPickupRequestResult lastRequestResult)
        : this(
            0,
            revision,
            mode,
            activePickups,
            selectedPickup,
            pendingAutoMovePickup,
            pickupRadius,
            isInputBlocked,
            suppressPrimaryAttackUntilRelease,
            lastRequestResult)
    {
    }

    public WorldLootInteractionSnapshot(
        int activeSetRevision,
        int revision,
        WorldLootInteractionMode mode,
        WorldLootPickupSnapshot[] activePickups,
        WorldItemPickup selectedPickup,
        WorldItemPickup pendingAutoMovePickup,
        float pickupRadius,
        bool isInputBlocked,
        bool suppressPrimaryAttackUntilRelease,
        WorldLootPickupRequestResult lastRequestResult)
    {
        ActiveSetRevision = activeSetRevision;
        Revision = revision;
        Mode = mode;
        ActivePickups = activePickups ?? Array.Empty<WorldLootPickupSnapshot>();
        SelectedPickup = selectedPickup;
        PendingAutoMovePickup = pendingAutoMovePickup;
        PickupRadius = pickupRadius;
        IsInputBlocked = isInputBlocked;
        SuppressPrimaryAttackUntilRelease = suppressPrimaryAttackUntilRelease;
        LastRequestResult = lastRequestResult;
    }
}

[DisallowMultipleComponent]
public class PlayerPickupInteractor : MonoBehaviour, IInteractable // 월드 아이템 상호작용 코어 호스트
{
    [Header("References")]
    [SerializeField] private PlayerInventory inventory;

    [Header("Pickup")]
    [SerializeField] private float pickupRadius = 2.2f;

    private static WorldLootInteractionMode sessionMode = WorldLootInteractionMode.CombatAutoLegendary; // 세션 최초 모드
    private static PlayerPickupInteractor activeCore; // 현재 플레이어 코어

    private readonly List<WorldItemPickup> registryBuffer = new List<WorldItemPickup>(); // 레지스트리 복사 버퍼
    private readonly List<WorldLootPickupSnapshot> snapshotBuffer = new List<WorldLootPickupSnapshot>(); // 스냅샷 빌드 버퍼
    private readonly List<int> activeMembershipIds = new List<int>(); // 유효 멤버십 ID
    private readonly List<WorldLootSelectionCandidate> selectionCandidateBuffer = new List<WorldLootSelectionCandidate>(); // F 선택 후보
    private WorldLootPickupSnapshot[] activePickupSnapshotCache = Array.Empty<WorldLootPickupSnapshot>(); // 활성 배열 재사용
    private readonly WorldLootActiveSetRevisionTracker activeSetTracker = new WorldLootActiveSetRevisionTracker(); // UI 구성 revision
    private IWorldLootAutoMoveDriver autoMoveDriver; // 40번대 연결
    private WorldItemPickup selectedPickup; // F 최근접 대상
    private WorldItemPickup pendingAutoMovePickup; // 이동 중 동일 대상
    private bool suppressPrimaryAttackUntilRelease; // 라벨 클릭 공격 누출 차단
    private bool observedSuppressedPointerPress; // release 확인용
    private bool isRebuildingSnapshot; // 이벤트 재진입 차단
    private bool hasPublishedSnapshot; // 최초 발행 여부
    private int publishedSignature; // 마지막 변경 서명
    private int revision;
    private WorldLootPickupRequestResult lastRequestResult;

    public static PlayerPickupInteractor ActiveCore
    {
        get
        {
            if (activeCore == null || !activeCore.isActiveAndEnabled)
                activeCore = FindFirstObjectByType<PlayerPickupInteractor>(); // 현재 활성 리더 재탐색

            return activeCore;
        }
    }

    public static bool IsPrimaryAttackSuppressed => ActiveCore != null && ActiveCore.suppressPrimaryAttackUntilRelease;
    public WorldLootInteractionMode CurrentMode => sessionMode;
    public WorldItemPickup SelectedPickup => selectedPickup;
    public WorldItemPickup PendingAutoMovePickup => pendingAutoMovePickup;
    public float PickupRadius => Mathf.Max(0f, pickupRadius);
    public bool SuppressPrimaryAttackUntilRelease => suppressPrimaryAttackUntilRelease;
    public WorldLootInteractionSnapshot CurrentSnapshot { get; private set; }
    public bool HasActivePickupCandidates => false; // 구 휠 선택은 제거됨
    public Component InteractionComponent => this;
    public Transform InteractionTransform => selectedPickup != null ? selectedPickup.transform : transform;
    public int InteractionPriority => 40;
    public string InteractionPrompt => "F : 아이템 획득";
    public InteractionDistanceMode DistanceMode => InteractionDistanceMode.Horizontal;
    public float InteractionRange => PickupRadius;
    public string StableInteractionId => selectedPickup != null
        ? "WorldLoot|" + selectedPickup.GetInstanceID()
        : "WorldLoot|None";
    public bool AllowsInteractionWhileInputBlocked => false;
    public bool WantsInteractionPrompt => false; // 월드 아이템은 기존 네임플레이트가 안내를 소유한다.

    public event Action<WorldLootInteractionSnapshot> SnapshotChanged; // UI 단일 변경 알림

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSessionState()
    {
        sessionMode = WorldLootInteractionMode.CombatAutoLegendary;
        activeCore = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsurePlayerInteractor()
    {
        if (FindFirstObjectByType<PlayerPickupInteractor>() != null)
            return;

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
            playerObject.AddComponent<PlayerPickupInteractor>(); // Player 런타임 보강
    }

#if UNITY_EDITOR
    public static void RunActiveSetRevisionFixture()
    {
        if (!WorldLootActiveSetRevisionFixture.Validate())
            throw new InvalidOperationException("WorldLoot fixture failed: ActiveSetRevision.");

        if (!WorldLootPickupLifecycleFixture.Validate())
            throw new InvalidOperationException("WorldLoot fixture failed: Lifecycle.");

        if (!WorldLootActivePickupCountFixture.Validate())
            throw new InvalidOperationException("WorldLoot fixture failed: ActivePickupCount.");

        if (!WorldLootNearestSelectionFixture.Validate())
            throw new InvalidOperationException("WorldLoot fixture failed: NearestSelection.");
    }
#endif

    private void Awake()
    {
        ResolveInventory();
        RebuildSnapshot();
    }

    private void OnEnable()
    {
        activeCore = this;
        InteractionRegistry.Register(this);
        WorldItemPickup.RegistryChanged += HandleWorldPickupRegistryChanged;
        RebuildSnapshot();
    }

    private void OnDisable()
    {
        InteractionRegistry.Unregister(this);
        WorldItemPickup.RegistryChanged -= HandleWorldPickupRegistryChanged;
        CancelPendingAutoMove(WorldLootPickupRequestResult.AutoMoveCancelled);
        suppressPrimaryAttackUntilRelease = false;
        observedSuppressedPointerPress = false;

        if (activeCore == this)
            activeCore = null;
    }

    private void Update()
    {
        ResolveInventory();
        HandleSuppressedPointerRelease();

        bool inputBlocked = GameplayInputBlocker.IsGameplayInputBlocked;
        if (inputBlocked)
            CancelPendingAutoMove(WorldLootPickupRequestResult.InputBlocked);
        else
            HandleModeInput();

        ValidatePendingAutoMove();
        RebuildSnapshot(); // 프레임당 한 번 일관된 스냅샷 발행
    }

    public void BindAutoMoveDriver(IWorldLootAutoMoveDriver driver)
    {
        if (ReferenceEquals(autoMoveDriver, driver))
            return;

        CancelPendingAutoMove(WorldLootPickupRequestResult.AutoMoveCancelled);
        autoMoveDriver = driver; // 40번대 명시 연결
        RebuildSnapshot();
    }

    public WorldLootPickupRequestResult RequestPickupByLabelPointerDown(WorldItemPickup target)
    {
        if (GameplayInputBlocker.IsGameplayInputBlocked)
            return SetRequestResult(WorldLootPickupRequestResult.InputBlocked);

        if (sessionMode != WorldLootInteractionMode.LootFocus)
            return SetRequestResult(WorldLootPickupRequestResult.ModeRejected);

        BeginPrimaryAttackSuppression(); // PointerDown 공격 누출 차단

        if (!IsValidPickup(target))
            return SetRequestResult(WorldLootPickupRequestResult.InvalidTarget);

        if (IsWithinPickupRange(target))
            return TryPickupTarget(target);

        if (autoMoveDriver == null)
            return SetRequestResult(WorldLootPickupRequestResult.AutoMoveUnavailable);

        CancelPendingAutoMove(WorldLootPickupRequestResult.AutoMoveCancelled);
        pendingAutoMovePickup = target; // 요청 대상 고정
        WorldLootAutoMoveRequest request = new WorldLootAutoMoveRequest(
            target,
            PickupRadius,
            OnAutoMoveCompleted);

        if (!autoMoveDriver.TryBegin(request))
        {
            pendingAutoMovePickup = null;
            return SetRequestResult(WorldLootPickupRequestResult.AutoMoveRejected);
        }

        if (pendingAutoMovePickup != target)
            return lastRequestResult; // 동기 완료 결과 보존

        return SetRequestResult(WorldLootPickupRequestResult.AutoMovePending);
    }

    public void NotifyPrimaryPointerReleased()
    {
        suppressPrimaryAttackUntilRelease = false;
        observedSuppressedPointerPress = false;
        RebuildSnapshot();
    }

    public static WorldLootInteractionMode GetNextMode(WorldLootInteractionMode current)
    {
        return current == WorldLootInteractionMode.CombatAutoLegendary
            ? WorldLootInteractionMode.LootFocus
            : current == WorldLootInteractionMode.LootFocus
                ? WorldLootInteractionMode.CombatForcedHidden
                : WorldLootInteractionMode.LootFocus;
    }

    public static bool IsPersistentLabelTarget(WorldLootInteractionMode mode, WorldItemPickup pickup)
    {
        if (!IsValidPickup(pickup))
            return false;

        if (mode == WorldLootInteractionMode.LootFocus)
            return true;

        return mode == WorldLootInteractionMode.CombatAutoLegendary
            && pickup.Grade >= ItemGrade.Legendary;
    }

    private void HandleModeInput()
    {
        // GOAL A2: CapsLock 직접 읽기 대신 Gameplay LootModeCycle을 사용한다.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        if (facade == null)
            return;

        if (facade.LootModeCyclePressedThisFrame)
        {
            sessionMode = GetNextMode(sessionMode); // 최초 Auto 이후 2상태 전환
            if (sessionMode != WorldLootInteractionMode.LootFocus)
                CancelPendingAutoMove(WorldLootPickupRequestResult.AutoMoveCancelled);
        }
    }

    public bool IsInteractionAvailable(PlayerActorRuntime actor)
    {
        if (actor == null || GameplayInputBlocker.IsGameplayInputBlocked)
            return false;

        WorldItemPickup nearest = ResolveNearestSelectablePickup();
        if (nearest == null || !IsWithinPickupRange(nearest))
            return false;

        selectedPickup = nearest;
        return true;
    }

    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if (!IsInteractionAvailable(actor) || selectedPickup == null)
            return InteractionExecutionResult.Rejected;

        WorldLootPickupRequestResult result = TryPickupTarget(selectedPickup);
        return result == WorldLootPickupRequestResult.Succeeded
            ? InteractionExecutionResult.Succeeded
            : InteractionExecutionResult.Rejected;
    }

    public void SetInteractionPromptVisible(bool visible)
    {
        // 기존 네임플레이트 표시 정책을 유지한다. 전역 프롬프트는 생성하지 않는다.
    }

    private void RebuildSnapshot()
    {
        if (isRebuildingSnapshot)
            return;

        isRebuildingSnapshot = true;
        try
        {
            RebuildSnapshotCore();
        }
        finally
        {
            isRebuildingSnapshot = false;
        }
    }

    private void RebuildSnapshotCore()
    {
        bool inputBlocked = GameplayInputBlocker.IsGameplayInputBlocked;
        WorldItemPickup.CopyActivePickups(registryBuffer); // 전체 검색 없이 활성 목록 사용
        snapshotBuffer.Clear();
        activeMembershipIds.Clear();
        selectionCandidateBuffer.Clear();

        for (int i = 0; i < registryBuffer.Count; i++)
        {
            WorldItemPickup pickup = registryBuffer[i];
            if (!IsValidPickup(pickup))
                continue;

            float distanceSqr = GetFlatDistanceSqr(pickup);
            bool withinRange = distanceSqr <= PickupRadius * PickupRadius;
            bool persistent = IsPersistentLabelTarget(sessionMode, pickup);
            activeMembershipIds.Add(pickup.GetInstanceID());
            selectionCandidateBuffer.Add(new WorldLootSelectionCandidate(
                pickup,
                pickup.GetInstanceID(),
                distanceSqr,
                withinRange,
                WorldLootNearestSelectionPolicy.IsVisiblePriorityCandidate(
                    IsNameplateVisible(pickup))));
            snapshotBuffer.Add(new WorldLootPickupSnapshot(
                pickup,
                pickup.RuntimeItem,
                Mathf.Sqrt(distanceSqr),
                true,
                withinRange,
                persistent));
        }

        snapshotBuffer.Sort(CompareSnapshotDistance); // UI 결정적 가까운 순
        selectedPickup = ResolveNearestSelectablePickup(selectionCandidateBuffer, inputBlocked); // F와 동일 정책
        activeSetTracker.Update(activeMembershipIds); // 착지/제거 멤버십 감지
        EnsureSnapshotCache(snapshotBuffer.Count);
        for (int i = 0; i < snapshotBuffer.Count; i++)
            activePickupSnapshotCache[i] = snapshotBuffer[i]; // 최신 거리/반경 반영

        int signature = ComputeSnapshotSignature(inputBlocked);
        bool changed = !hasPublishedSnapshot || signature != publishedSignature;
        if (changed)
        {
            hasPublishedSnapshot = true;
            publishedSignature = signature;
            revision++;
        }

        CurrentSnapshot = new WorldLootInteractionSnapshot(
            activeSetTracker.Revision,
            revision,
            sessionMode,
            activePickupSnapshotCache,
            selectedPickup,
            pendingAutoMovePickup,
            PickupRadius,
            inputBlocked,
            suppressPrimaryAttackUntilRelease,
            lastRequestResult);
        if (changed)
            SnapshotChanged?.Invoke(CurrentSnapshot); // 변경 프레임당 단일 알림
    }

    private void HandleWorldPickupRegistryChanged()
    {
        if (!isActiveAndEnabled || isRebuildingSnapshot)
            return;

        RebuildSnapshot(); // 생성·초기화·비활성·획득 경계 즉시 반영
    }

    private void EnsureSnapshotCache(int requiredCount)
    {
        if (activePickupSnapshotCache.Length == requiredCount)
            return;

        activePickupSnapshotCache = new WorldLootPickupSnapshot[requiredCount]; // 구성 수 변경 시에만 할당
    }

    private WorldLootPickupRequestResult TryPickupTarget(WorldItemPickup target)
    {
        if (GameplayInputBlocker.IsGameplayInputBlocked)
            return SetRequestResult(WorldLootPickupRequestResult.InputBlocked);

        if (!IsValidPickup(target))
            return SetRequestResult(WorldLootPickupRequestResult.InvalidTarget);

        ResolveInventory();
        if (inventory == null)
            return SetRequestResult(WorldLootPickupRequestResult.InventoryUnavailable);

        if (!IsWithinPickupRange(target))
            return SetRequestResult(WorldLootPickupRequestResult.OutOfRange);

        if (!target.TryPickup(inventory))
            return SetRequestResult(WorldLootPickupRequestResult.InventoryRejected); // 실패 시 월드 소유 유지

        pendingAutoMovePickup = null;
        return SetRequestResult(WorldLootPickupRequestResult.Succeeded);
    }

    private void OnAutoMoveCompleted(WorldLootAutoMoveDriverResult result)
    {
        WorldItemPickup target = pendingAutoMovePickup;
        pendingAutoMovePickup = null;

        if (result == WorldLootAutoMoveDriverResult.Cancelled)
        {
            SetRequestResult(WorldLootPickupRequestResult.AutoMoveCancelled);
            return;
        }

        if (result == WorldLootAutoMoveDriverResult.Failed)
        {
            SetRequestResult(WorldLootPickupRequestResult.AutoMoveFailed);
            return;
        }

        if (result == WorldLootAutoMoveDriverResult.TargetInvalid || !IsValidPickup(target))
        {
            SetRequestResult(WorldLootPickupRequestResult.InvalidTarget);
            return;
        }

        TryPickupTarget(target); // 도착 후 같은 대상만 획득
    }

    private void ValidatePendingAutoMove()
    {
        if (pendingAutoMovePickup == null)
            return;

        if (sessionMode != WorldLootInteractionMode.LootFocus || !IsValidPickup(pendingAutoMovePickup))
            CancelPendingAutoMove(WorldLootPickupRequestResult.InvalidTarget);
    }

    private void CancelPendingAutoMove(WorldLootPickupRequestResult result)
    {
        WorldItemPickup target = pendingAutoMovePickup;
        pendingAutoMovePickup = null;
        if (target == null)
            return;

        autoMoveDriver?.Cancel(target);
        lastRequestResult = result;
    }

    private void BeginPrimaryAttackSuppression()
    {
        suppressPrimaryAttackUntilRelease = true;
        // GOAL A2: 좌클릭 홀드 직접 읽기 대신 Gameplay Attack 유지를 사용한다. release 억제 의미 유지.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        observedSuppressedPointerPress = facade != null && facade.AttackHeld;
    }

    private void HandleSuppressedPointerRelease()
    {
        if (!suppressPrimaryAttackUntilRelease)
            return;

        PlayerInputFacade facade = PlayerInputFacade.Current;
        if (facade == null)
            return;

        if (facade.AttackHeld)
        {
            observedSuppressedPointerPress = true;
            return;
        }

        if (observedSuppressedPointerPress)
            NotifyPrimaryPointerReleased(); // 실제 release 뒤 해제
    }

    private WorldLootPickupRequestResult SetRequestResult(WorldLootPickupRequestResult result)
    {
        lastRequestResult = result;
        return result;
    }

    private void ResolveInventory()
    {
        if (inventory == null)
            inventory = PlayerAccountInventoryService.FindSharedInventory();

        if (inventory == null)
            inventory = GetComponent<PlayerInventory>();
    }

    private bool IsWithinPickupRange(WorldItemPickup pickup)
    {
        return GetFlatDistanceSqr(pickup) <= PickupRadius * PickupRadius;
    }

    private float GetFlatDistanceSqr(WorldItemPickup pickup)
    {
        if (pickup == null)
            return float.MaxValue;

        Vector3 delta = pickup.transform.position - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude;
    }

    private WorldItemPickup ResolveNearestSelectablePickup()
    {
        WorldItemPickup.CopyActivePickups(registryBuffer);
        selectionCandidateBuffer.Clear();

        for (int i = 0; i < registryBuffer.Count; i++)
        {
            WorldItemPickup pickup = registryBuffer[i];
            if (!IsValidPickup(pickup))
                continue;

            float distanceSqr = GetFlatDistanceSqr(pickup);
            selectionCandidateBuffer.Add(new WorldLootSelectionCandidate(
                pickup,
                pickup.GetInstanceID(),
                distanceSqr,
                distanceSqr <= PickupRadius * PickupRadius,
                WorldLootNearestSelectionPolicy.IsVisiblePriorityCandidate(
                    IsNameplateVisible(pickup))));
        }

        return ResolveNearestSelectablePickup(
            selectionCandidateBuffer,
            GameplayInputBlocker.IsGameplayInputBlocked);
    }

    private static WorldItemPickup ResolveNearestSelectablePickup(
        IReadOnlyList<WorldLootSelectionCandidate> candidates,
        bool inputBlocked)
    {
        int index = WorldLootNearestSelectionPolicy.ResolveNearestCandidateIndex(candidates, inputBlocked);
        return index >= 0 && index < candidates.Count
            ? candidates[index].Pickup
            : null;
    }

    private static bool IsNameplateVisible(WorldItemPickup pickup)
    {
        return pickup != null && WorldItemNameplateVisibilityRegistry.IsVisible(pickup); // 90 표시 집합 읽기
    }

    private static bool IsValidPickup(WorldItemPickup pickup)
    {
        return pickup != null && pickup.CanPickup && pickup.RuntimeItem != null;
    }

    private static int CompareSnapshotDistance(WorldLootPickupSnapshot left, WorldLootPickupSnapshot right)
    {
        int distanceCompare = left.Distance.CompareTo(right.Distance);
        if (distanceCompare != 0)
            return distanceCompare;

        int leftId = left.Pickup != null ? left.Pickup.GetInstanceID() : int.MaxValue;
        int rightId = right.Pickup != null ? right.Pickup.GetInstanceID() : int.MaxValue;
        return leftId.CompareTo(rightId);
    }

    private int ComputeSnapshotSignature(bool inputBlocked)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + activeSetTracker.Revision;
            hash = hash * 31 + (int)sessionMode;
            hash = hash * 31 + (inputBlocked ? 1 : 0);
            hash = hash * 31 + (suppressPrimaryAttackUntilRelease ? 1 : 0);
            hash = hash * 31 + (int)lastRequestResult;
            hash = hash * 31 + (selectedPickup != null ? selectedPickup.GetInstanceID() : 0);
            hash = hash * 31 + (pendingAutoMovePickup != null ? pendingAutoMovePickup.GetInstanceID() : 0);
            hash = hash * 31 + snapshotBuffer.Count;

            for (int i = 0; i < snapshotBuffer.Count; i++)
            {
                WorldLootPickupSnapshot entry = snapshotBuffer[i];
                hash = hash * 31 + (entry.Pickup != null ? entry.Pickup.GetInstanceID() : 0);
                hash = hash * 31 + (entry.IsWithinPickupRange ? 1 : 0);
                hash = hash * 31 + (entry.IsPersistentLabelTarget ? 1 : 0);
            }

            return hash;
        }
    }
}
