using UnityEngine;

[DefaultExecutionOrder(340)]
[DisallowMultipleComponent]
public sealed class PlayerCombatFacingController : MonoBehaviour
{
    private PlayerMovement movement;
    private MeleeWeaponCombatAnimatorDriver driver;
    private Animator animator;
    private PlayerCombatAimPose aimPose;
    private Transform visualRoot;
    private Quaternion originalLocalRotation;
    private bool bound, tracking;
    private float lowerYaw, aimYaw, intentTime, settledTime;
    private int previousSign = 1;
    private CombatLocomotionSet lastSet;
    private CombatTurnMotion previousTurn;
    private float turnStartYaw;
    private float upperDelta;
    private bool upperReversing;

    public float LowerYaw => tracking ? lowerYaw : transform.eulerAngles.y;
    public float AimYaw => aimYaw;
    public CombatLocomotionSet Set => lastSet;
    public bool IsPoseActive => tracking && driver != null && driver.CanUseCombatFacing
        && movement != null && !movement.IsConditionMovementBlocked && movement.IsGrounded;
    public bool IsTurning => driver != null && driver.ActiveFacingTurn != null;
    public bool IsStationaryPose => driver != null && driver.IsStationaryFacingPose;
    public float UpperDelta => lastSet != null ? upperDelta : 0f;

    public void Bind(PlayerMovement owner, MeleeWeaponCombatAnimatorDriver animationDriver, Animator mainAnimator)
    {
        movement = owner; driver = animationDriver;
        if (animator == mainAnimator && bound) return;
        RestoreVisualRoot();
        animator = mainAnimator;
        if (animator == null || movement == null) return;
        // Rotate the parent, leaving the Animator transform free for its authored body motion.
        visualRoot = animator.transform.parent;
        if (visualRoot == null || visualRoot == transform || !visualRoot.IsChildOf(transform))
            visualRoot = animator.transform;
        if (visualRoot == transform) return;
        originalLocalRotation = visualRoot.localRotation;
        bound = true;
        aimPose = animator.GetComponent<PlayerCombatAimPose>();
        if (aimPose == null) aimPose = animator.gameObject.AddComponent<PlayerCombatAimPose>();
        aimPose.Bind(animator, this);
    }

    public void BeginAttackPoseBlend(float duration) => aimPose?.BeginAttackBlend(duration);
    public void CancelAttackPoseBlend() => aimPose?.CancelAttackBlend();

    public void ResetAfterTeleport()
    {
        driver?.CancelFacingTurn();
        aimPose?.CancelAttackBlend();
        RestoreVisualRoot();
        aimYaw = transform.eulerAngles.y;
    }

    public bool AcceptAim(Vector3 direction)
    {
        if (!bound || driver == null || driver.FacingSet == null || movement == null
            || movement.ControlAuthority != ActorControlAuthority.Player) return false;
        direction.y = 0;
        if (direction.sqrMagnitude < .0001f) return false;
        aimYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        if (!tracking) lowerYaw = transform.eulerAngles.y;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        return true;
    }

    private void Update()
    {
        if (!bound || movement == null || driver == null) return;
        var set = driver.FacingSet;
        bool enabledForWeapon = set != null && movement.IsMeleeCombatLocomotionMode
            && movement.ControlAuthority == ActorControlAuthority.Player && !movement.IsLootAutoMoveActive;
        if (!enabledForWeapon) { RestoreVisualRoot(); return; }
        if (!tracking || lastSet != set)
        {
            lowerYaw = visualRoot.eulerAngles.y;
            lastSet = set; tracking = true; previousTurn = null;
            intentTime = settledTime = 0;
        }
        float dt = Time.deltaTime;
        var turn = driver.ActiveFacingTurn;
        if (turn != null)
        {
            if (previousTurn != turn)
            {
                turnStartYaw = lowerYaw; previousTurn = turn;
                previousSign = turn.angle < 0 ? -1 : 1;
            }
            lowerYaw = turnStartYaw + turn.yaw.Evaluate(driver.FacingTurnElapsed);
        }
        bool moving = movement.MoveInput.sqrMagnitude > .0025f;
        if (!driver.CanUseCombatFacing || moving || !movement.IsGrounded || movement.IsConditionMovementBlocked)
        {
            driver.CancelFacingTurn();
            previousTurn = null; intentTime = settledTime = 0;
            // Logical action direction is already fixed; return the model over its entry blend.
            lowerYaw = Mathf.MoveTowardsAngle(lowerYaw, transform.eulerAngles.y, 1080f * dt);
            aimYaw = transform.eulerAngles.y;
        }
        else if (turn == null)
        {
            previousTurn = null;
            float delta = Mathf.DeltaAngle(lowerYaw, aimYaw);
            if (Mathf.Abs(delta) >= set.turnThreshold && !GameplayInputBlocker.IsGameplayInputBlocked)
            {
                intentTime += dt;
                if ((intentTime >= set.turnIntentSeconds || Mathf.Abs(delta) >= set.maximumUpperTwist)
                    && driver.CanStartFacingTurn && settledTime >= set.recoverySeconds)
                {
                    var next = set.SelectTurn(delta, previousSign);
                    if (driver.TryStartFacingTurn(next))
                    {
                        turnStartYaw = lowerYaw; previousTurn = next;
                        previousSign = next.angle < 0 ? -1 : 1;
                        intentTime = settledTime = 0;
                    }
                }
            }
            else intentTime = 0;
            settledTime += dt;
        }
        else settledTime = 0;
        UpdateUpperDelta(dt);
        ApplyVisualYaw();
    }

    private void UpdateUpperDelta(float dt)
    {
        float delta = Mathf.DeltaAngle(lowerYaw, aimYaw);
        float desired = Mathf.Clamp(delta, -lastSet.maximumUpperTwist, lastSet.maximumUpperTwist);
        // The shortest-angle representation flips at 180 degrees. Keep the existing
        // anatomical twist side there while the selected footstep catches up.
        if (Mathf.Abs(delta) >= 165f && Mathf.Abs(upperDelta) > 1f)
            desired = Mathf.Sign(upperDelta) * lastSet.maximumUpperTwist;
        if (Mathf.Abs(desired - upperDelta) > 90f) upperReversing = true;
        upperDelta = upperReversing ? Mathf.MoveTowards(upperDelta, desired, 600f * dt) : desired;
        if (Mathf.Abs(upperDelta - desired) < .01f) upperReversing = false;
    }

    private void LateUpdate()
    {
        // Attack direction may change later in Update; compute an absolute compensation.
        if (tracking) ApplyVisualYaw();
    }
    private void ApplyVisualYaw()
    {
        if (visualRoot == null) return;
        float parentYaw = visualRoot.parent != null ? visualRoot.parent.eulerAngles.y : 0;
        visualRoot.localRotation = Quaternion.AngleAxis(Mathf.DeltaAngle(parentYaw, lowerYaw), Vector3.up) * originalLocalRotation;
    }
    private void RestoreVisualRoot()
    {
        if (bound && visualRoot != null) visualRoot.localRotation = originalLocalRotation;
        tracking = false; lastSet = null; previousTurn = null;
        intentTime = settledTime = 0;
        upperDelta = 0; upperReversing = false;
    }
    private void OnDisable() { ResetAfterTeleport(); }
}
