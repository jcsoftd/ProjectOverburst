using UnityEngine;

public class PlayerLeftHandGrip : MonoBehaviour
{
    private const string LeftHandGripPointName = "LeftHandGripPoint";

    [Header("References")]
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private PlayerMovement playerController;
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private Transform leftHandWeaponSocket;
    [SerializeField] private Transform leftHandGripPoint;

    [Header("Update")]
    [SerializeField] private bool autoUpdate = true;

    [Header("Left Hand IK")]
    [SerializeField] private bool useLeftHandGrip = true;
    [SerializeField] private float blendSpeed = 14f;
    [SerializeField, Range(0f, 1f)] private float positionWeight = 1f;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = true;

    private float currentWeight;
    private Transform cachedWeaponRoot;
    private int lastPreparedFrame = -1;

    public Animator TargetAnimator => targetAnimator;
    public Transform LeftHandGripPoint => leftHandGripPoint;
    public float CurrentWeight => currentWeight;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        if (autoUpdate)
            PrepareAnimatorIkFrame();
    }

    public void SetAutoUpdate(bool value)
    {
        autoUpdate = value;
    }

    public void Bind(
        Animator animator,
        PlayerMovement movement,
        PlayerEquipment equipment,
        Transform handSocket = null)
    {
        targetAnimator = animator;
        playerController = movement;
        playerEquipment = equipment;
        leftHandWeaponSocket = handSocket != null ? handSocket : leftHandWeaponSocket;
    }

    public void ApplyGripFrame()
    {
        PrepareAnimatorIkFrame();
    }

    public void ApplyAnimatorIK(Animator animator)
    {
        if (animator == null)
            return;

        PrepareAnimatorIkFrame();
        float position = 0f;
        if (isActiveAndEnabled
            && animator == targetAnimator
            && animator.isHuman
            && TryResolveHandBonePosition(animator, out Vector3 handPosition))
        {
            position = currentWeight * positionWeight;

            if (position > 0.001f)
                animator.SetIKPosition(AvatarIKGoal.LeftHand, handPosition);
        }

        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, position);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);

        if (drawDebug && leftHandWeaponSocket != null && leftHandGripPoint != null)
            Debug.DrawLine(leftHandWeaponSocket.position, leftHandGripPoint.position, Color.magenta);
    }

    private void PrepareAnimatorIkFrame()
    {
        if (lastPreparedFrame == Time.frameCount)
            return;

        lastPreparedFrame = Time.frameCount;
        ResolveReferences();
        RefreshWeaponGripReference();
        ApplyCurrentWeaponGripSettings();
        float targetWeight = IsGripActive() ? 1f : 0f;
        currentWeight = Mathf.MoveTowards(currentWeight, targetWeight, blendSpeed * Time.deltaTime);
    }

    private bool IsGripActive()
    {
        if (GameplayInputBlocker.IsGameplayInputBlocked
            || !useLeftHandGrip
            || playerController == null
            || playerEquipment == null
            || leftHandWeaponSocket == null
            || leftHandGripPoint == null)
        {
            return false;
        }

        return PlayerCombatModeController.IsSharedCombatModeActive()
            || playerController.IsWeaponAimInputActive
            || playerController.IsMeleeGuarding;
    }

    private void RefreshWeaponGripReference()
    {
        Transform weaponRoot = playerEquipment != null ? playerEquipment.CurrentWeaponRoot : null;
        if (weaponRoot == cachedWeaponRoot)
            return;

        cachedWeaponRoot = weaponRoot;
        leftHandGripPoint = weaponRoot != null
            ? FindDeepChild(weaponRoot, LeftHandGripPointName)
            : null;
    }

    private void ApplyCurrentWeaponGripSettings()
    {
        WeaponItemData weaponData = playerEquipment != null ? playerEquipment.CurrentWeaponData : null;
        if (weaponData == null)
        {
            useLeftHandGrip = false;
            return;
        }

        MeleeGripSettings grip = weaponData.GetMeleeGripSettings();
        useLeftHandGrip = grip.enabled;
        blendSpeed = Mathf.Max(0f, grip.blendSpeed);
        positionWeight = Mathf.Clamp01(grip.positionWeight);
    }

    private void ResolveReferences()
    {
        if (targetAnimator == null)
            targetAnimator = GetComponentInChildren<Animator>(true);
        if (playerController == null)
            playerController = GetComponentInParent<PlayerMovement>();
        if (playerEquipment == null)
            playerEquipment = GetComponentInParent<PlayerEquipment>();
        if (leftHandWeaponSocket == null)
        {
            P09CharacterVisualAdapter adapter = GetComponentInChildren<P09CharacterVisualAdapter>(true);
            if (adapter != null)
            {
                leftHandWeaponSocket = adapter.GetNamedSocket(
                    P09CharacterVisualAdapter.LeftHandWeaponSocketName);
            }
        }
    }

    private bool TryResolveHandBonePosition(
        Animator animator,
        out Vector3 handPosition)
    {
        handPosition = Vector3.zero;
        if (leftHandWeaponSocket == null || leftHandGripPoint == null)
            return false;

        Transform handBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (handBone == null || !leftHandWeaponSocket.IsChildOf(handBone))
            return false;

        Vector3 currentSocketOffset = leftHandWeaponSocket.position - handBone.position;
        handPosition = leftHandGripPoint.position - currentSocketOffset;
        return true;
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null)
            return null;
        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }
}
