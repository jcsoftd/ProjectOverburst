using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum PlayerCombatModeState
{
    Exploration,
    Combat
}

public enum PlayerCombatModeReason
{
    ManualToggle,
    AttackInput,
    Damaged,
    InactivityTimeout,
    System
}

[DisallowMultipleComponent]
public sealed class PlayerCombatModeController : MonoBehaviour
{
    private static PlayerCombatModeController instance;

    [SerializeField] private PlayerCombatModeState currentState = PlayerCombatModeState.Exploration;
    [SerializeField] private float inactivityExitDelay = 30f;
    [SerializeField] private bool handleKeyboardInput = true;
    [SerializeField] private bool ignoreWhenGameplayInputBlocked = true;

    private float lastCombatActivityTime;
    private int combatSessionRevision;
    private int lastManualExitFrame = -1;

    public static PlayerCombatModeController Instance => instance;
    public PlayerCombatModeState CurrentState => currentState;
    public bool IsCombatModeActive => currentState == PlayerCombatModeState.Combat;
    public int CombatSessionRevision => combatSessionRevision;
    public float InactivityExitDelay => inactivityExitDelay;

    public event Action<PlayerCombatModeState, PlayerCombatModeReason> ModeChanged;
    public event Action<PlayerCombatModeReason> CombatActivityNotified;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureSceneController()
    {
        GetOrCreate();
    }

    public static PlayerCombatModeController GetOrCreate()
    {
        if (instance != null)
            return instance;

        PlayerCombatModeController existing = FindFirstObjectByType<PlayerCombatModeController>(FindObjectsInactive.Include);
        if (existing != null)
        {
            instance = existing;
            return instance;
        }

        PlayerContext partyRuntime = PlayerContext.GetOrCreate();
        if (partyRuntime != null)
        {
            instance = partyRuntime.GetComponent<PlayerCombatModeController>();
            if (instance == null)
                instance = partyRuntime.gameObject.AddComponent<PlayerCombatModeController>();

            return instance;
        }

        GameObject controllerObject = new GameObject("PlayerCombatModeController");
        DontDestroyOnLoad(controllerObject);
        instance = controllerObject.AddComponent<PlayerCombatModeController>();
        return instance;
    }

    public static bool IsSharedCombatModeActive()
    {
        return instance != null && instance.IsCombatModeActive;
    }

    public static void EnterSharedCombatMode(PlayerCombatModeReason reason)
    {
        GetOrCreate().EnterCombatMode(reason);
    }

    public static void ExitSharedCombatMode(PlayerCombatModeReason reason)
    {
        if (instance != null)
            instance.ExitCombatMode(reason);
    }

    public static void NotifySharedCombatActivity(PlayerCombatModeReason reason)
    {
        GetOrCreate().NotifyCombatActivity(reason);
    }



    public static bool NotifyPlayerMeleeAccepted(
        PlayerActorRuntime actor,
        int actionRevision)
    {
        return GetOrCreate().AcceptPlayerMeleeAction(actor, actionRevision);
    }



    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        lastCombatActivityTime = Time.time;
        if (currentState == PlayerCombatModeState.Combat)
            BeginNewSession();
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        if (instance == this)
            instance = null;
    }

    private void OnEnable()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        if (instance == this)
            ExitCombatMode(PlayerCombatModeReason.System);
    }

    private void Update()
    {
        if (handleKeyboardInput)
            ReadToggleInput();

        UpdateInactivityExit();
    }

    public void EnterCombatMode(PlayerCombatModeReason reason)
    {
        if (currentState == PlayerCombatModeState.Combat)
        {
            NotifyCombatActivity(reason);
            return;
        }

        BeginNewSession();
        currentState = PlayerCombatModeState.Combat;
        NotifyCombatActivity(reason);
        ModeChanged?.Invoke(currentState, reason);
    }

    public void ExitCombatMode(PlayerCombatModeReason reason)
    {
        if (reason == PlayerCombatModeReason.ManualToggle)
        {
            lastManualExitFrame = Time.frameCount;
            PlayerContext.GetOrCreate()?.CurrentActor?.PlayerKit?.RequireMeleeInputRelease();
        }

        if (reason == PlayerCombatModeReason.System)
            PlayerContext.GetOrCreate()?.CurrentActorKit?.CancelCurrentActions(WeaponActionCancelReason.RuntimeDisabled);

        if (currentState == PlayerCombatModeState.Exploration)
        {
            ResetSessionGate();
            return;
        }

        ResetSessionGate();
        currentState = PlayerCombatModeState.Exploration;
        ModeChanged?.Invoke(currentState, reason);
    }

    public void ToggleCombatMode(PlayerCombatModeReason reason)
    {
        if (IsCombatModeActive)
            ExitCombatMode(reason);
        else
            EnterCombatMode(reason);
    }

    public void NotifyCombatActivity(PlayerCombatModeReason reason)
    {
        lastCombatActivityTime = Time.time;
        CombatActivityNotified?.Invoke(reason);
    }

    public bool AcceptPlayerMeleeAction(
        PlayerActorRuntime actor,
        int actionRevision)
    {
        PlayerContext runtime = PlayerContext.GetOrCreate();
        MeleeRuntime meleeRuntime = actor != null && actor.PlayerKit != null
            ? actor.PlayerKit.MeleeRuntime
            : null;
        if (runtime == null
            || actor == null
            || lastManualExitFrame == Time.frameCount
            || runtime.CurrentActor != actor
            || actor.Authority != ActorControlAuthority.Player
            || meleeRuntime == null
            || !meleeRuntime.IsActivePlayerInputAction(actionRevision))
        {
            return false;
        }

        if (currentState != PlayerCombatModeState.Combat)
        {
            BeginNewSession();
            currentState = PlayerCombatModeState.Combat;
            NotifyCombatActivity(PlayerCombatModeReason.AttackInput);
            ModeChanged?.Invoke(currentState, PlayerCombatModeReason.AttackInput);
            return true;
        }

        NotifyCombatActivity(PlayerCombatModeReason.AttackInput);
        return true;
    }

    private void ReadToggleInput()
    {
        if (ignoreWhenGameplayInputBlocked && GameplayInputBlocker.IsGameplayInputBlocked)
            return;

        // GOAL A2: X 직접 읽기 대신 Gameplay CombatMode를 사용한다.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        if (facade == null || !facade.CombatModePressedThisFrame)
            return;

        ToggleCombatMode(PlayerCombatModeReason.ManualToggle);
    }

    private void UpdateInactivityExit()
    {
        if (!IsCombatModeActive)
            return;

        if (inactivityExitDelay <= 0f)
            return;

        if (Time.time - lastCombatActivityTime < inactivityExitDelay)
            return;

        ExitCombatMode(PlayerCombatModeReason.InactivityTimeout);
    }

    private void BeginNewSession()
    {
        combatSessionRevision = combatSessionRevision == int.MaxValue
            ? 1
            : combatSessionRevision + 1;
    }

    private void ResetSessionGate()
    {
    }



    private void HandleActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        ExitCombatMode(PlayerCombatModeReason.System);
    }
}
