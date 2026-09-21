using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// One pending press per action, owned by the actor's facade. Unscaled deadlines
// prevent hitstop from turning a short tap into a long-lived queued action.
public sealed class PlayerCombatInputBuffer : IDisposable
{
    private readonly PlayerInputFacade input;
    private PlayerStateCoordinator state;
    private PlayerEquipment equipment;
    private CombatHealth health;
    private MeleeRuntime melee;
    private PlayerEvadeController evade;
    private PlayerMovement movement;
    private bool attackPending;
    private bool evadePending;
    private float attackExpiresAt;
    private float evadeExpiresAt;
    private int attackFrame;
    private int evadeFrame;
    private int sampledFrame = -1;
    private bool attackNeedsRelease;
    private bool evadeNeedsRelease;
    private int attackSuppressedFrame = -1;

    public PlayerCombatInputBuffer(PlayerInputFacade owner)
    {
        input = owner;
        Bind();
        GameplayInputBlocker.BlockStateChanged += OnBlocked;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        Invalidate();
    }

    public bool HasAttack { get { Refresh(); return attackPending; } }
    public bool HasEvade { get { Refresh(); return evadePending; } }
    public bool AllowsHeldAttack
    {
        get
        {
            Refresh();
            return CanRead() && !attackNeedsRelease
                && attackSuppressedFrame != Time.frameCount && input.AttackHeld;
        }
    }

    private void Bind()
    {
        if (state == null)
        {
            state = input.GetComponent<PlayerStateCoordinator>();
            if (state != null) state.ConditionChanged += OnConditionChanged;
        }
        if (equipment == null)
        {
            equipment = input.GetComponent<PlayerEquipment>();
            if (equipment != null) equipment.WeaponSlotsChanged += Invalidate;
        }
        if (health == null)
        {
            health = input.GetComponent<CombatHealth>();
            if (health != null)
            {
                health.OnDamaged += OnDamaged;
                health.OnDead += OnDamaged;
                health.OnReset += OnHealthReset;
            }
        }
        if (melee == null) melee = input.GetComponent<MeleeRuntime>();
        if (evade == null) evade = input.GetComponent<PlayerEvadeController>();
        if (movement == null) movement = input.GetComponent<PlayerMovement>();
    }

    private bool CanRead()
    {
        return input != null && input.isActiveAndEnabled && input.IsGameplayEnabled
            && !GameplayInputBlocker.IsGameplayInputBlocked
            && (health == null || !health.IsDead)
            && (state == null || state.CurrentCondition == PlayerConditionState.Normal);
    }

    private void Refresh()
    {
        Bind();
        if (!CanRead()) { Invalidate(); return; }
        if (PlayerPickupInteractor.IsPrimaryAttackSuppressed)
        {
            attackPending = false;
            attackNeedsRelease = true;
        }
        if (sampledFrame == Time.frameCount) return;
        sampledFrame = Time.frameCount;
        float now = Time.unscaledTime;
        if (attackFrame != Time.frameCount && now >= attackExpiresAt) attackPending = false;
        if (evadeFrame != Time.frameCount && now >= evadeExpiresAt) evadePending = false;
        if (!input.AttackHeld && !PlayerPickupInteractor.IsPrimaryAttackSuppressed) attackNeedsRelease = false;
        if (!input.EvadeHeld) evadeNeedsRelease = false;
        bool canAttack = melee != null && melee.isActiveAndEnabled
            && equipment != null && equipment.CanCurrentWeaponUseMeleeSlash;
        bool canEvade = evade != null && evade.isActiveAndEnabled
            && movement != null && movement.IsMeleeCombatLocomotionMode;
        if (!canAttack) attackPending = false;
        if (!canEvade) evadePending = false;
        if (canAttack && !attackNeedsRelease && input.AttackPressedThisFrame)
        {
            attackPending = true;
            attackFrame = Time.frameCount;
            attackExpiresAt = now + Mathf.Max(0f, input.AttackBufferDuration);
        }
        if (canEvade && !evadeNeedsRelease && input.EvadePressedThisFrame)
        {
            evadePending = true;
            evadeFrame = Time.frameCount;
            evadeExpiresAt = now + Mathf.Max(0f, input.EvadeBufferDuration);
        }
    }

    public void ConsumeAttack() { Refresh(); attackPending = false; }

    public void ConsumeEvade()
    {
        Refresh();
        evadePending = false;
        attackPending = false;
        attackSuppressedFrame = Time.frameCount;
    }

    // Cancellation must not poll input again and recreate the press this frame.
    public void ClearAttack()
    {
        Refresh();
        attackPending = false;
        attackSuppressedFrame = Time.frameCount;
    }

    public void Invalidate()
    {
        attackPending = evadePending = false;
        attackNeedsRelease = evadeNeedsRelease = true;
        sampledFrame = Time.frameCount;
        attackSuppressedFrame = Time.frameCount;
    }

    private void OnBlocked(bool blocked) { if (blocked) Invalidate(); }
    private void OnConditionChanged(PlayerConditionState condition)
    { if (condition != PlayerConditionState.Normal) Invalidate(); }
    private void OnDamaged(CombatHealth source, DamageInfo damage) => Invalidate();
    private void OnHealthReset(CombatHealth source) => Invalidate();
    private void OnActiveSceneChanged(Scene previous, Scene next) => Invalidate();
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Invalidate();
    private void OnSceneUnloaded(Scene scene) => Invalidate();

    public void Dispose()
    {
        Invalidate();
        GameplayInputBlocker.BlockStateChanged -= OnBlocked;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        if (state != null) state.ConditionChanged -= OnConditionChanged;
        if (equipment != null) equipment.WeaponSlotsChanged -= Invalidate;
        if (health != null)
        {
            health.OnDamaged -= OnDamaged;
            health.OnDead -= OnDamaged;
            health.OnReset -= OnHealthReset;
        }
    }
}
