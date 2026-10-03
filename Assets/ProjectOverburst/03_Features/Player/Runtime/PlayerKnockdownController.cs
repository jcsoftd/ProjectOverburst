using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum PlayerKnockdownPhase { Ready, Falling, Grounded, Rising }

// Health dispatches after actual HP loss and before the ordinary full-body Hit/knockback.
[DefaultExecutionOrder(305)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement), typeof(PlayerStateCoordinator))]
public sealed class PlayerKnockdownController : MonoBehaviour
{
    [SerializeField] private PlayerKnockdownAnimationSet animationSet;
    private PlayerMovement movement;
    private PlayerStateCoordinator state;
    private CombatHealth health;
    private Animator animator;
    private WeaponCombatAnimatorRouter router;
    private PlayerKnockdownAnimationSet.Motion motion;
    private AnimatorOverrideController ownedOverride;
    private RuntimeAnimatorController speedCheckedController;
    private bool hasRiseSpeed;
    private float elapsed, holdElapsed, traveled, protectionUntil, exitElapsed;
    private bool exiting;
    private float playbackWatchdog;
    private int layer = -1;
    private Quaternion bodyFrame;
    private Vector3 fallDirection, travelDirection;
    private PlayerEquipment equipment;
    private PlayerInputFacade inputFacade;
    private Vector2 riseTravelInput;
    private Transform weaponAtStart;
    private Transform visualRoot;
    private Quaternion visualRotation;
    private readonly Queue<(GameObject source, int sequence)> recent = new Queue<(GameObject, int)>(16);
    public PlayerKnockdownPhase Phase { get; private set; }
    public bool IsActive => Phase != PlayerKnockdownPhase.Ready;
    public string ActiveMotionId => motion != null ? motion.id : "";
    public Vector2 PendingRiseInput { get; private set; }
    public bool PendingEvadeRise { get; private set; }
    public bool IsEvadeRise { get; private set; }
    public PlayerKnockdownAnimationSet AnimationSet => animationSet;

    private void Awake() => Resolve();
    private void Resolve()
    {
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (state == null) state = GetComponent<PlayerStateCoordinator>();
        if (health == null) health = GetComponent<CombatHealth>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (router == null) router = GetComponent<WeaponCombatAnimatorRouter>();
        if (equipment == null) equipment = GetComponent<PlayerEquipment>();
        if (inputFacade == null) inputFacade = GetComponent<PlayerInputFacade>();
    }
    private void OnEnable()
    {
        Resolve();
        if (health != null) { health.OnDead += OnDead; health.OnReset += OnReset; }
        SceneManager.activeSceneChanged += OnSceneChanged;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }
    private void OnDisable()
    {
        if (health != null) { health.OnDead -= OnDead; health.OnReset -= OnReset; }
        SceneManager.activeSceneChanged -= OnSceneChanged;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        ResetReaction();
    }
    private void OnDestroy() { if (ownedOverride != null) Destroy(ownedOverride); }
    private void OnDead(CombatHealth source, DamageInfo info) => ResetReaction();
    private void OnReset(CombatHealth source) => ResetReaction();
    private void OnSceneChanged(Scene oldScene, Scene newScene) => ResetReaction();
    private void OnSceneUnloaded(Scene scene) => ResetReaction();

    public static bool IsKnockdownHit(DamageInfo info)
    {
        if (info.isDamageOverTime || !info.triggersOnHitEffects || info.elementalReactionType != ElementalReactionType.None
            || info.enemyAbility == null || !info.enemyAbility.IsMeleeStrongAttack || info.source == null) return false;
        EnemyRank rank = info.source.GetComponentInParent<EnemyRank>();
        return rank != null && (rank.GradeType == EnemyGradeType.Elite || rank.GradeType == EnemyGradeType.GreaterElite);
    }

    // true means this reaction owns the full-body pose and planar displacement for this hit.
    public bool ResolveDamageReaction(DamageInfo info, float actualDamage, bool fatal)
    {
        if (!isActiveAndEnabled) return false;
        if (fatal) { ResetReaction(); return true; }
        if (IsActive) return true; // Additional damage is still resolved by Health, without restarting the pose.
        if (actualDamage <= 0 || Time.time < protectionUntil || !IsKnockdownHit(info)) return false;
        if (info.sourceAttackSequenceId > 0)
            foreach (var hit in recent)
                if (hit.source == info.source && hit.sequence == info.sourceAttackSequenceId) return false;
        Resolve();
        var fall = animationSet != null ? animationSet.SelectFall(info.sourceAttackSequenceId) : null;
        if (fall == null || state == null || state.CurrentCondition == PlayerConditionState.Dead
            || movement == null || !PrepareAnimator(fall.clip)) return false;

        // Begin only after every animation dependency has been checked. All requests belong to this component.
        Phase = PlayerKnockdownPhase.Falling;
        motion = fall;
        state.RequestStun(this);
        state.RequestLocomotion(this, PlayerLocomotionState.ControlledMove);
        var kit = GetComponent<PlayerControlKit>();
        if (kit != null) kit.CancelCurrentActions(WeaponActionCancelReason.Request);
        else GetComponent<MeleeRuntime>()?.CancelCurrentAttackState();
        GetComponent<PlayerEvadeController>()?.CancelForKnockdown();
        GetComponent<PlayerAnimation>()?.CancelWeaponRuntimeState();
        movement.PrepareKnockdownMotion();
        router?.SuspendForKnockdown(true);
        fallDirection = info.direction; fallDirection.y = 0;
        if (fallDirection.sqrMagnitude < .001f) fallDirection = transform.position - info.source.transform.position;
        fallDirection.y = 0;
        if (fallDirection.sqrMagnitude < .001f) fallDirection = -transform.forward;
        fallDirection.Normalize();
        // This family falls backwards; orient while still standing so the fall follows the confirmed impact.
        transform.rotation = Quaternion.LookRotation(-fallDirection, Vector3.up);
        bodyFrame = transform.rotation;
        weaponAtStart = equipment != null ? equipment.CurrentWeaponRoot : null;
        visualRoot = animator.transform != transform && animator.transform.IsChildOf(transform) ? animator.transform : null;
        if (visualRoot != null) visualRotation = visualRoot.localRotation;
        PendingRiseInput = Vector2.zero;
        PendingEvadeRise = IsEvadeRise = false;
        riseTravelInput = Vector2.zero;
        elapsed = holdElapsed = traveled = exitElapsed = 0;
        playbackWatchdog = 0;
        exiting = false;
        ApplyClip(animationSet.fallTemplate, fall.clip);
        animator.SetLayerWeight(layer, 0);
        animator.Play(Animator.StringToHash(PlayerKnockdownAnimationSet.FallState), layer, 0);
        if (info.sourceAttackSequenceId > 0)
        {
            if (recent.Count >= 16) recent.Dequeue();
            recent.Enqueue((info.source, info.sourceAttackSequenceId));
        }
        return true;
    }

    private bool PrepareAnimator(AnimationClip clip)
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null
            || animationSet.fallTemplate == null || animationSet.riseTemplate == null) return false;
        layer = animator.GetLayerIndex(PlayerKnockdownAnimationSet.LayerName);
        if (layer < 0 || !animator.HasState(layer, Animator.StringToHash(PlayerKnockdownAnimationSet.FallState))
            || !animator.HasState(layer, Animator.StringToHash(PlayerKnockdownAnimationSet.HoldState))
            || !animator.HasState(layer, Animator.StringToHash(PlayerKnockdownAnimationSet.RiseState))) return false;
        bool hasFall = false, hasRise = false;
        var current = animator.runtimeAnimatorController;
        if (speedCheckedController != current)
        {
            hasRiseSpeed = false;
            foreach (var parameter in animator.parameters)
                hasRiseSpeed |= parameter.name == PlayerKnockdownAnimationSet.RiseSpeedParameter && parameter.type == AnimatorControllerParameterType.Float;
            speedCheckedController = current;
        }
        var original = current is AnimatorOverrideController existing ? existing.runtimeAnimatorController : current;
        foreach (var template in original.animationClips) { hasFall |= template == animationSet.fallTemplate; hasRise |= template == animationSet.riseTemplate; }
        return clip != null && hasFall && hasRise && hasRiseSpeed;
    }

    private void ApplyClip(AnimationClip template, AnimationClip clip)
    {
        var overrides = animator.runtimeAnimatorController as AnimatorOverrideController;
        if (overrides == null)
        {
            if (ownedOverride == null) ownedOverride = new AnimatorOverrideController(animator.runtimeAnimatorController);
            overrides = ownedOverride;
            animator.runtimeAnimatorController = overrides;
        }
        overrides[template] = clip;
    }

    private void Update()
    {
        if (!IsActive) return;
        if (equipment != null && equipment.CurrentWeaponRoot != weaponAtStart) { ResetReaction(); return; }
        if (health == null || health.IsDead || health.CurrentHp <= 0 || animator == null || !animator.isActiveAndEnabled
            || movement == null || !movement.isActiveAndEnabled || !PrepareAnimator(motion.clip)) { ResetReaction(); return; }
        float dt = Mathf.Max(0, Time.deltaTime);
        if (dt <= 0) return;
        playbackWatchdog += dt;
        elapsed += dt * Mathf.Max(0, animator.speed) * (Phase == PlayerKnockdownPhase.Rising ? Mathf.Max(.01f, motion.playbackSpeed) : 1f);
        if (Phase != PlayerKnockdownPhase.Grounded && playbackWatchdog > motion.clip.length + 2f)
        {
            Debug.LogWarning("[PlayerKnockdown] Reaction playback timed out; released owned locks.", this);
            ResetReaction(); return;
        }
        string expected = Phase == PlayerKnockdownPhase.Falling ? PlayerKnockdownAnimationSet.FallState
            : Phase == PlayerKnockdownPhase.Grounded ? PlayerKnockdownAnimationSet.HoldState : PlayerKnockdownAnimationSet.RiseState;
        if (elapsed > .25f && !animator.GetCurrentAnimatorStateInfo(layer).IsName(expected)
            && !(animator.IsInTransition(layer) && animator.GetNextAnimatorStateInfo(layer).IsName(expected)))
        {
            Debug.LogWarning("[PlayerKnockdown] Reaction state was replaced; released owned locks.", this);
            ResetReaction(); return;
        }
        // Platform rotation is applied by the motor before this Update. Input is resolved in that current frame.
        bodyFrame = transform.rotation;
        if (Phase != PlayerKnockdownPhase.Rising)
        {
            PendingRiseInput = ReadRiseDirection();
            PendingEvadeRise = CanReadRiseInput() && inputFacade != null && inputFacade.EvadeHeld;
        }
        if (Phase == PlayerKnockdownPhase.Falling)
        {
            animator.SetLayerWeight(layer, Mathf.Clamp01(elapsed / Mathf.Max(.001f, animationSet.entryBlend)));
            MoveAlong(fallDirection, animationSet.fallDistance);
            if (elapsed >= motion.clip.length)
            {
                animator.SetFloat(PlayerKnockdownAnimationSet.HoldTimeParameter, .9999f);
                animator.Play(Animator.StringToHash(PlayerKnockdownAnimationSet.HoldState), layer, .9999f);
                Phase = PlayerKnockdownPhase.Grounded;
                holdElapsed = 0;
            }
        }
        else if (Phase == PlayerKnockdownPhase.Grounded)
        {
            animator.SetLayerWeight(layer, 1);
            if (!movement.IsGrounded) { holdElapsed = 0; return; }
            holdElapsed += dt;
            if (holdElapsed >= animationSet.groundedHold) BeginRise();
        }
        else if (Phase == PlayerKnockdownPhase.Rising)
        {
            if (!exiting)
            {
                if (travelDirection.sqrMagnitude > .001f)
                    travelDirection = bodyFrame * new Vector3(riseTravelInput.x, 0, riseTravelInput.y).normalized;
                MoveAlong(travelDirection, travelDirection.sqrMagnitude > .001f
                    ? animationSet.ResolveRiseDistance(motion, IsEvadeRise) : 0);
                if (elapsed >= motion.clip.length) { exiting = true; exitElapsed = 0; }
            }
            if (exiting)
            {
                exitElapsed += dt;
                animator.SetLayerWeight(layer, 1 - Mathf.Clamp01(exitElapsed / Mathf.Max(.001f, animationSet.exitBlend)));
                if (exitElapsed >= animationSet.exitBlend) Finish();
            }
        }
    }

    private void LateUpdate()
    {
        if (!IsActive || visualRoot == null || visualRoot.parent == null) return;
        float blend = Phase == PlayerKnockdownPhase.Grounded ? 1f
            : Phase == PlayerKnockdownPhase.Falling ? Mathf.SmoothStep(0, 1, elapsed / motion.clip.length)
            : 1f - Mathf.SmoothStep(0, 1, elapsed / motion.clip.length);
        Vector3 normal = movement.IsGrounded ? movement.GroundNormal : Vector3.up;
        Quaternion tilt = Quaternion.FromToRotation(Vector3.up, visualRoot.parent.InverseTransformDirection(normal));
        visualRoot.localRotation = Quaternion.Slerp(visualRotation, tilt * visualRotation, blend);
    }

    private Vector2 ReadRiseDirection()
    {
        if (!CanReadRiseInput()) return Vector2.zero;
        var input = GetComponent<PlayerMovementInputSource>();
        Vector2 raw = input != null ? input.RawMoveInput : Vector2.zero;
        if (raw.sqrMagnitude < animationSet.inputDeadzone * animationSet.inputDeadzone) return Vector2.zero;
        Vector3 world = movement.ResolveMoveDirection(raw);
        Vector3 local = Quaternion.Inverse(bodyFrame) * world;
        return new Vector2(local.x, local.z).normalized;
    }

    private bool CanReadRiseInput()
        => !GameplayInputBlocker.IsGameplayInputBlocked && movement.ControlAuthority == ActorControlAuthority.Player
            && (inputFacade == null || inputFacade.IsGameplayEnabled);

    private void BeginRise()
    {
        var rise = animationSet.SelectRise(motion.poseId, PendingRiseInput, PendingEvadeRise);
        if (rise == null) { ResetReaction(); return; }
        IsEvadeRise = PendingEvadeRise;
        motion = rise;
        // A held Evade without Move uses the registered backward get-up. Keep the actual input separately.
        riseTravelInput = PendingRiseInput.sqrMagnitude > .001f ? PendingRiseInput
            : IsEvadeRise ? rise.direction.normalized : Vector2.zero;
        travelDirection = riseTravelInput.sqrMagnitude > .001f
            ? bodyFrame * new Vector3(riseTravelInput.x, 0, riseTravelInput.y).normalized : Vector3.zero;
        ApplyClip(animationSet.riseTemplate, rise.clip);
        animator.SetFloat(PlayerKnockdownAnimationSet.RiseSpeedParameter, Mathf.Max(.01f, rise.playbackSpeed));
        animator.CrossFadeInFixedTime(Animator.StringToHash(PlayerKnockdownAnimationSet.RiseState), animationSet.riseBlend, layer, 0);
        Phase = PlayerKnockdownPhase.Rising;
        elapsed = traveled = playbackWatchdog = 0;
    }

    private void MoveAlong(Vector3 direction, float distance)
    {
        float progress = Mathf.Clamp01(elapsed / Mathf.Max(.01f, motion.clip.length));
        float target = distance * Mathf.Clamp01(motion.travel.Evaluate(progress));
        float delta = Mathf.Max(0, target - traveled);
        traveled = Mathf.Max(traveled, target); // A wall consumes travel too; no stored burst when it clears.
        if (delta > .00001f && movement.CombatMotion != null) movement.CombatMotion.ApplyEvadeDisplacement(direction * delta);
    }

    private void Finish()
    {
        protectionUntil = Time.time + animationSet.recoveryProtection;
        ReleaseReaction();
    }
    public void ResetReaction()
    {
        ReleaseReaction();
        recent.Clear();
        protectionUntil = 0;
    }
    private void ReleaseReaction()
    {
        bool wasActive = IsActive;
        Phase = PlayerKnockdownPhase.Ready;
        if (wasActive && visualRoot != null) visualRoot.localRotation = visualRotation;
        if (animator != null && layer >= 0 && layer < animator.layerCount) animator.SetLayerWeight(layer, 0);
        if (wasActive && animator != null) animator.SetFloat(PlayerKnockdownAnimationSet.RiseSpeedParameter, 1f);
        if (state != null) { state.ReleaseStun(this); state.ReleaseLocomotion(this); }
        if (wasActive) router?.SuspendForKnockdown(false);
        motion = null;
        PendingRiseInput = Vector2.zero;
        PendingEvadeRise = IsEvadeRise = false;
        riseTravelInput = Vector2.zero;
        exiting = false;
    }
}
