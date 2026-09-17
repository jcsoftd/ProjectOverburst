using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(260)]
[DisallowMultipleComponent]
public sealed class PlayerLootAutoMoveDriver : MonoBehaviour, IWorldLootAutoMoveDriver // 플레이어 아이템 자동 접근
{
    [SerializeField] private PlayerContext playerContext;

    private PlayerContext subscribedRuntime;
    private PlayerActorRuntime boundActor;
    private PlayerPickupInteractor boundInteractor;
    private WorldLootAutoMoveRequest activeRequest;
    private PlayerActorRuntime activeActor;
    private PlayerControlKit activeActorKit;
    private PlayerMovement activeMovement;

    public bool HasActiveRequest => activeRequest != null;
    public WorldItemPickup ActiveTarget => activeRequest != null ? activeRequest.Target : null;

    private void Awake()
    {
        Bind(playerContext != null ? playerContext : GetComponent<PlayerContext>());
    }

    private void OnEnable()
    {
        Bind(playerContext != null ? playerContext : PlayerContext.GetOrCreate());
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        CompleteActive(WorldLootAutoMoveDriverResult.Cancelled);
        BindInteractor(null);
        UnsubscribeRuntime();
    }

    private void Update()
    {
        ResolveRuntime();
        BindCurrentActorInteractor();

        if (activeRequest == null)
            return;

        WorldItemPickup target = activeRequest.Target;
        if (target == null || !target.isActiveAndEnabled || !target.CanPickup)
        {
            CompleteActive(WorldLootAutoMoveDriverResult.TargetInvalid);
            return;
        }

        if (!IsActiveActorValid())
        {
            CompleteActive(WorldLootAutoMoveDriverResult.Cancelled);
            return;
        }

        if (GameplayInputBlocker.IsGameplayInputBlocked
            || HasRawMoveInput()
            || HasEvadeInput()
            || HasUnsuppressedPrimaryAttackInput())
        {
            CompleteActive(WorldLootAutoMoveDriverResult.Cancelled);
            return;
        }

        if (GetFlatDistanceSqr(activeActor.transform.position, target.transform.position)
            <= activeRequest.PickupRadius * activeRequest.PickupRadius)
        {
            CompleteActive(WorldLootAutoMoveDriverResult.Arrived);
            return;
        }

        if (activeMovement == null || !activeMovement.IsLootAutoMoveActive)
        {
            CompleteActive(WorldLootAutoMoveDriverResult.Failed);
            return;
        }

        activeMovement.UpdateLootAutoMoveDestination(target.transform.position); // 이동 대상 추적
    }

    public void Bind(PlayerContext runtime)
    {
        if (playerContext == runtime && subscribedRuntime == runtime)
        {
            BindCurrentActorInteractor();
            return;
        }

        CompleteActive(WorldLootAutoMoveDriverResult.Cancelled);
        UnsubscribeRuntime();
        playerContext = runtime != null ? runtime : PlayerContext.GetOrCreate();
        SubscribeRuntime();
        BindCurrentActorInteractor();
    }

    public bool TryBegin(WorldLootAutoMoveRequest request)
    {
        if (request == null)
            return false;

        ResolveRuntime();
        BindCurrentActorInteractor();
        CompleteActive(WorldLootAutoMoveDriverResult.Cancelled); // 새 라벨 요청은 기존 접근을 먼저 교체

        WorldItemPickup target = request.Target;
        if (target == null || !target.isActiveAndEnabled || !target.CanPickup)
        {
            request.Complete(WorldLootAutoMoveDriverResult.TargetInvalid);
            return true;
        }

        PlayerActorRuntime actor = playerContext != null ? playerContext.CurrentActor : null;
        PlayerControlKit actorKit = actor != null ? actor.PlayerKit : null;
        if (!TryResolveActorControl(actor, actorKit, out PlayerMovement movement))
            return false;

        if (PlayerPickupInteractor.IsPrimaryAttackSuppressed)
        {
            actorKit.CancelCurrentActions(WeaponActionCancelReason.Request); // PointerDown 공격 누출 제거
            actorKit.RequireMeleeInputRelease(); // release까지 신규 공격 차단
        }

        if (GetFlatDistanceSqr(actor.transform.position, target.transform.position)
            <= request.PickupRadius * request.PickupRadius)
        {
            request.Complete(WorldLootAutoMoveDriverResult.Arrived);
            return true;
        }

        if (!movement.BeginLootAutoMove(target.transform.position))
            return false;

        activeRequest = request;
        activeActor = actor;
        activeActorKit = actorKit;
        activeMovement = movement;
        return true;
    }

    public void Cancel(WorldItemPickup target)
    {
        if (activeRequest == null || !ReferenceEquals(activeRequest.Target, target))
            return;

        CompleteActive(WorldLootAutoMoveDriverResult.Cancelled);
    }

    private void CompleteActive(WorldLootAutoMoveDriverResult result)
    {
        WorldLootAutoMoveRequest request = activeRequest;
        PlayerMovement movement = activeMovement;
        activeRequest = null; // callback 재진입 전에 종료
        activeActor = null;
        activeActorKit = null;
        activeMovement = null;
        movement?.CancelLootAutoMove();
        request?.Complete(result);
    }

    private bool IsActiveActorValid()
    {
        if (playerContext == null
            || activeActor == null
            || playerContext.CurrentActor != activeActor
            || !activeActor.isActiveAndEnabled
            || !activeActor.gameObject.activeInHierarchy
            || activeActorKit == null
            || activeActorKit.Authority != ActorControlAuthority.Player
            || activeActorKit.IsIncapacitated
            || activeMovement == null
            || !activeMovement.isActiveAndEnabled)
        {
            return false;
        }

        CombatHealth health = activeActor.Health;
        return health != null && !health.IsDead;
    }

    private bool HasRawMoveInput()
    {
        PlayerMovementInputSource inputSource = activeActorKit != null
            ? activeActorKit.MovementInputSource
            : null;
        return inputSource != null && inputSource.RawMoveInput.sqrMagnitude > 0.001f;
    }

    private bool HasEvadeInput()
    {
        if (activeActorKit != null
            && activeActorKit.EvadeController != null
            && activeActorKit.EvadeController.IsEvading)
        {
            return true;
        }

        // GOAL A2: Shift 직접 읽기 대신 Gameplay Evade 눌림을 사용한다.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        return facade != null && facade.EvadePressedThisFrame;
    }

    private static bool HasUnsuppressedPrimaryAttackInput()
    {
        if (PlayerPickupInteractor.IsPrimaryAttackSuppressed)
            return false;

        // GOAL A2: 좌클릭 edge 직접 읽기 대신 Gameplay Attack 눌림을 사용한다. 홀드 아닌 edge 유지.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        return facade != null && facade.AttackPressedThisFrame;
    }

    private void HandleActorChanged(PlayerActorRuntime actor)
    {
        CompleteActive(WorldLootAutoMoveDriverResult.Cancelled);
        BindActorInteractor(actor);
    }

    private void HandleActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        CompleteActive(WorldLootAutoMoveDriverResult.Cancelled);
    }

    private void HandleSceneUnloaded(Scene scene)
    {
        CompleteActive(WorldLootAutoMoveDriverResult.Cancelled);
    }

    private void BindCurrentActorInteractor()
    {
        BindActorInteractor(playerContext != null ? playerContext.CurrentActor : null);
    }

    private void BindActorInteractor(PlayerActorRuntime actor)
    {
        if (boundActor == actor && boundInteractor != null)
            return;

        boundActor = actor;
        PlayerControlKit kit = actor != null ? actor.PlayerKit : null;
        if (kit != null)
            kit.ResolveReferences();

        PlayerPickupInteractor interactor = kit != null ? kit.PickupInteractor : null;
        if (interactor == null)
            interactor = PlayerPickupInteractor.ActiveCore; // 초기 조립 전 보조

        BindInteractor(interactor);
    }

    private void BindInteractor(PlayerPickupInteractor interactor)
    {
        if (boundInteractor == interactor)
            return;

        if (boundInteractor != null)
            boundInteractor.BindAutoMoveDriver(null);

        boundInteractor = interactor;
        if (boundInteractor != null)
            boundInteractor.BindAutoMoveDriver(this); // 50 코어 명시 연결
    }

    private bool TryResolveActorControl(
        PlayerActorRuntime actor,
        PlayerControlKit actorKit,
        out PlayerMovement movement)
    {
        movement = null;
        if (actor == null || !actor.isActiveAndEnabled || actorKit == null)
            return false;

        actorKit.ResolveReferences();
        movement = actorKit.Movement;
        CombatHealth health = actor.Health;
        return actorKit.Authority == ActorControlAuthority.Player
            && !actorKit.IsIncapacitated
            && health != null
            && !health.IsDead
            && movement != null
            && movement.isActiveAndEnabled;
    }

    private void ResolveRuntime()
    {
        if (playerContext == null)
            playerContext = PlayerContext.GetOrCreate();

        SubscribeRuntime();
    }

    private void SubscribeRuntime()
    {
        if (playerContext == null || subscribedRuntime == playerContext)
            return;

        UnsubscribeRuntime();
        subscribedRuntime = playerContext;
        subscribedRuntime.CurrentActorChanged += HandleActorChanged;
    }

    private void UnsubscribeRuntime()
    {
        if (subscribedRuntime != null)
            subscribedRuntime.CurrentActorChanged -= HandleActorChanged;

        subscribedRuntime = null;
    }

    private static float GetFlatDistanceSqr(Vector3 origin, Vector3 target)
    {
        Vector3 delta = target - origin;
        delta.y = 0f;
        return delta.sqrMagnitude;
    }
}
