using UnityEngine;

[DefaultExecutionOrder(640)]
public sealed class PlayerFootLock : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private WeaponCombatAnimatorRouter combatAnimatorRouter;
    [SerializeField] private HumanoidFootContactRig footContactRig;

    [Header("Lock Conditions")]
    [SerializeField] private bool enableFootLock = true;
    [SerializeField] private bool idleOnly = true;
    [SerializeField] private float idleMoveInputThreshold = 0.01f;
    [SerializeField] private float idleAnimationThreshold = 0.05f;

    [Header("Animator Settling")]
    [SerializeField] private int baseLayerIndex;
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string moveXParameter = "MoveX";
    [SerializeField] private string moveYParameter = "MoveY";
    [SerializeField] private float animatorIdleSpeedThreshold = 0.03f;
    [SerializeField] private float animatorMoveBlendThreshold = 0.03f;

    [Header("Two Point Ground Probe")]
    [SerializeField] private LayerMask groundLayer = ~0;
    [SerializeField] private float probeUpDistance = 0.12f;
    [SerializeField] private float probeDownDistance = 0.30f;
    [SerializeField] private float probeRadius = 0.015f;
    [SerializeField] private float flatContactHeightTolerance = 0.005f;
    [SerializeField] private float flatParallelAngleTolerance = 0.5f;
    [SerializeField] private float maxGroundAngle = 50f;
    [SerializeField] private float maxHorizontalCorrection = 0.20f;

    [Header("Rotation Release")]
    [SerializeField] private float rotationUnlockDegreesPerSecond = 8f;
    [SerializeField] private float rotationUnlockMinimumDelta = 0.05f;

    [Header("Blend")]
    [SerializeField] private float lockBlendInDuration = 0.18f;
    [SerializeField] private float lockBlendOutDuration = 0.08f;
    [SerializeField, Range(0f, 1f)] private float positionWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float rotationWeight = 1f;

    private readonly FootState leftFoot = new FootState(AvatarIKGoal.LeftFoot);
    private readonly FootState rightFoot = new FootState(AvatarIKGoal.RightFoot);
    private int speedParameterHash;
    private int moveXParameterHash;
    private int moveYParameterHash;
    private bool hasSpeedParameter;
    private bool hasMoveXParameter;
    private bool hasMoveYParameter;
    private float previousRotationYaw;
    private bool hasRotationSample;
    private bool isRotationActive;

    private void Awake()
    {
        ResolveReferences();
    }

    public void Bind(PlayerMovement movement, Animator animator)
    {
        playerMovement = movement != null ? movement : playerMovement;
        targetAnimator = animator != null ? animator : targetAnimator;

        if (playerMovement != null)
            groundLayer = playerMovement.GroundLayerMask;

        ResolveReferences();
        RefreshAnimatorParameterCache();
        ResetRotationSample();
    }

    private void Update()
    {
        UpdateRotationActivity();
    }

    private void ResolveReferences()
    {
        if (targetAnimator == null)
            targetAnimator = GetComponent<Animator>();

        if (targetAnimator == null)
            targetAnimator = GetComponentInChildren<Animator>(true);

        if (playerMovement == null)
            playerMovement = GetComponentInParent<PlayerMovement>();

        if (combatAnimatorRouter == null && playerMovement != null)
            combatAnimatorRouter = playerMovement.GetComponent<WeaponCombatAnimatorRouter>();

        if (combatAnimatorRouter == null)
            combatAnimatorRouter = GetComponentInParent<WeaponCombatAnimatorRouter>();

        if (footContactRig == null)
            footContactRig = GetComponent<HumanoidFootContactRig>();

        if (footContactRig == null)
            footContactRig = GetComponentInParent<HumanoidFootContactRig>();

        if (playerMovement != null)
            groundLayer = playerMovement.GroundLayerMask;

        CacheFootData();
        RefreshAnimatorParameterCache();
    }

    private void CacheFootData()
    {
        if (targetAnimator == null || !targetAnimator.isHuman)
            return;

        leftFoot.Bone = targetAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot.Bone = targetAnimator.GetBoneTransform(HumanBodyBones.RightFoot);

        if (footContactRig == null || !footContactRig.IsConfigured)
            return;

        leftFoot.Heel = footContactRig.LeftHeel;
        leftFoot.Toe = footContactRig.LeftToe;
        rightFoot.Heel = footContactRig.RightHeel;
        rightFoot.Toe = footContactRig.RightToe;
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (!CanUseAnimatorIk())
            return;

        bool releaseForRotation = isRotationActive;
        bool shouldLock = !releaseForRotation && ShouldLockFeet();
        UpdateFoot(leftFoot, shouldLock, releaseForRotation);
        UpdateFoot(rightFoot, shouldLock, releaseForRotation);
    }

    private bool CanUseAnimatorIk()
    {
        if (!enableFootLock)
            return false;

        if (targetAnimator == null || footContactRig == null)
            ResolveReferences();

        return targetAnimator != null
            && targetAnimator.enabled
            && targetAnimator.isActiveAndEnabled
            && targetAnimator.isHuman
            && footContactRig != null
            && footContactRig.IsConfigured
            && leftFoot.IsConfigured
            && rightFoot.IsConfigured;
    }

    private bool ShouldLockFeet()
    {
        return IsFootLockEligible() && IsAnimatorIdleBlendSettled();
    }

    private bool IsFootLockEligible()
    {
        if (!idleOnly)
            return true;

        if (playerMovement == null)
            return true;

        if (!playerMovement.IsGrounded)
            return false;

        if (playerMovement.IsEvading || playerMovement.IsMeleeAttackMoveLocked)
            return false;

        if (playerMovement.IsMeleeCombatLocomotionMode)
        {
            if (combatAnimatorRouter == null || !combatAnimatorRouter.CanApplyStationaryFootIk)
                return false;
        }
        else if (playerMovement.IsWeaponAimInputActive)
        {
            return false;
        }

        if (playerMovement.MoveInput.sqrMagnitude > idleMoveInputThreshold * idleMoveInputThreshold)
            return false;

        return playerMovement.AnimationMoveAmount <= idleAnimationThreshold;
    }

    private bool IsAnimatorIdleBlendSettled()
    {
        if (targetAnimator == null)
            return true;

        int layerIndex = Mathf.Clamp(baseLayerIndex, 0, targetAnimator.layerCount - 1);
        if (targetAnimator.IsInTransition(layerIndex))
            return false;

        if (hasSpeedParameter && Mathf.Abs(targetAnimator.GetFloat(speedParameterHash)) > animatorIdleSpeedThreshold)
            return false;

        if (hasMoveXParameter && Mathf.Abs(targetAnimator.GetFloat(moveXParameterHash)) > animatorMoveBlendThreshold)
            return false;

        if (hasMoveYParameter && Mathf.Abs(targetAnimator.GetFloat(moveYParameterHash)) > animatorMoveBlendThreshold)
            return false;

        return true;
    }

    private void RefreshAnimatorParameterCache()
    {
        speedParameterHash = Animator.StringToHash(speedParameter);
        moveXParameterHash = Animator.StringToHash(moveXParameter);
        moveYParameterHash = Animator.StringToHash(moveYParameter);
        hasSpeedParameter = HasFloatParameter(speedParameterHash);
        hasMoveXParameter = HasFloatParameter(moveXParameterHash);
        hasMoveYParameter = HasFloatParameter(moveYParameterHash);
    }

    private bool HasFloatParameter(int parameterHash)
    {
        if (targetAnimator == null)
            return false;

        AnimatorControllerParameter[] parameters = targetAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Float && parameter.nameHash == parameterHash)
                return true;
        }

        return false;
    }

    private void UpdateFoot(FootState foot, bool shouldLock, bool releaseImmediately)
    {
        if (!foot.IsConfigured)
            return;

        if (releaseImmediately)
        {
            ReleaseFootImmediately(foot);
            return;
        }

        if (shouldLock && (!foot.HasTarget || IsFootTooFarFromLock(foot)))
            CaptureFootTarget(foot);

        float targetWeight = shouldLock && foot.HasTarget ? 1f : 0f;
        float blendDuration = targetWeight > foot.Weight
            ? lockBlendInDuration
            : lockBlendOutDuration;
        SmoothFootWeight(foot, targetWeight, blendDuration);

        if (foot.Weight <= 0.001f)
        {
            foot.Weight = 0f;
            if (!shouldLock)
                foot.HasTarget = false;
        }

        float appliedWeight = Mathf.SmoothStep(0f, 1f, foot.Weight);
        targetAnimator.SetIKPositionWeight(foot.Goal, appliedWeight * positionWeight);
        targetAnimator.SetIKRotationWeight(foot.Goal, appliedWeight * rotationWeight);

        if (foot.Weight <= 0f || !foot.HasTarget)
            return;

        Vector3 currentAnimatedPosition = targetAnimator.GetIKPosition(foot.Goal);
        Vector3 targetPosition = foot.TargetPosition;
        targetPosition.y = currentAnimatedPosition.y;

        targetAnimator.SetIKPosition(foot.Goal, targetPosition);
        targetAnimator.SetIKRotation(foot.Goal, foot.TargetRotation);
    }

    private void ReleaseFootImmediately(FootState foot)
    {
        foot.Weight = 0f;
        foot.WeightVelocity = 0f;
        foot.HasTarget = false;
        targetAnimator.SetIKPositionWeight(foot.Goal, 0f);
        targetAnimator.SetIKRotationWeight(foot.Goal, 0f);
    }

    private static void SmoothFootWeight(FootState foot, float targetWeight, float blendDuration)
    {
        if (blendDuration <= 0f)
        {
            foot.Weight = targetWeight;
            foot.WeightVelocity = 0f;
            return;
        }

        foot.Weight = Mathf.SmoothDamp(
            foot.Weight,
            targetWeight,
            ref foot.WeightVelocity,
            blendDuration,
            Mathf.Infinity,
            Time.deltaTime);

        if (Mathf.Abs(foot.Weight - targetWeight) <= 0.001f)
        {
            foot.Weight = targetWeight;
            foot.WeightVelocity = 0f;
        }
    }

    private bool IsFootTooFarFromLock(FootState foot)
    {
        float maxDistance = Mathf.Max(0.01f, maxHorizontalCorrection);
        Vector3 currentAnimatedPosition = targetAnimator.GetIKPosition(foot.Goal);
        Vector3 offset = currentAnimatedPosition - foot.TargetPosition;
        offset.y = 0f;
        return offset.sqrMagnitude > maxDistance * maxDistance;
    }

    private void CaptureFootTarget(FootState foot)
    {
        Vector3 animatedPosition = targetAnimator.GetIKPosition(foot.Goal);
        Quaternion animatedRotation = targetAnimator.GetIKRotation(foot.Goal);
        Vector3 upDirection = playerMovement != null ? playerMovement.transform.up : transform.up;
        TwoPointFootGroundSolver.Settings settings = new TwoPointFootGroundSolver.Settings(
            groundLayer,
            upDirection,
            probeUpDistance,
            probeDownDistance,
            probeRadius,
            flatContactHeightTolerance,
            maxGroundAngle);

        if (!TwoPointFootGroundSolver.TrySolve(
            animatedPosition,
            animatedRotation,
            foot.Heel,
            foot.Toe,
            settings,
            out TwoPointFootGroundSolver.Solution solution))
        {
            foot.HasTarget = false;
            return;
        }

        if (solution.IsFlatSurface && solution.ParallelAngleError > flatParallelAngleTolerance)
        {
            foot.HasTarget = false;
            return;
        }

        foot.TargetPosition = animatedPosition;
        foot.TargetRotation = solution.TargetRotation;
        foot.HasTarget = true;
    }

    private void UpdateRotationActivity()
    {
        Transform rotationRoot = playerMovement != null
            ? playerMovement.transform
            : targetAnimator != null ? targetAnimator.transform : null;
        if (rotationRoot == null)
        {
            isRotationActive = false;
            hasRotationSample = false;
            return;
        }

        float currentYaw = rotationRoot.eulerAngles.y;
        if (!hasRotationSample)
        {
            previousRotationYaw = currentYaw;
            hasRotationSample = true;
            isRotationActive = false;
            return;
        }

        float yawDelta = Mathf.Abs(Mathf.DeltaAngle(previousRotationYaw, currentYaw));
        float safeDeltaTime = Mathf.Max(0.0001f, Time.deltaTime);
        float yawSpeed = yawDelta / safeDeltaTime;
        isRotationActive = yawDelta >= Mathf.Max(0f, rotationUnlockMinimumDelta)
            && yawSpeed >= Mathf.Max(0f, rotationUnlockDegreesPerSecond);
        previousRotationYaw = currentYaw;
    }

    private void ResetRotationSample()
    {
        hasRotationSample = false;
        isRotationActive = false;
    }

    private sealed class FootState
    {
        public readonly AvatarIKGoal Goal;
        public Transform Bone;
        public Transform Heel;
        public Transform Toe;
        public Vector3 TargetPosition;
        public Quaternion TargetRotation;
        public float Weight;
        public float WeightVelocity;
        public bool HasTarget;

        public bool IsConfigured => Bone != null && Heel != null && Toe != null;

        public FootState(AvatarIKGoal goal)
        {
            Goal = goal;
            TargetRotation = Quaternion.identity;
        }
    }
}
