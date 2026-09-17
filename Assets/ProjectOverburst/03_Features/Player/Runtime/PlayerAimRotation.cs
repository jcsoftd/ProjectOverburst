using UnityEngine;

public class PlayerAimRotation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMovement playerController;
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private Camera aimCamera;
    [SerializeField] private Transform rotateRoot;

    [Header("Update")]
    [SerializeField] private bool autoUpdate = true;

    [Header("Rotate")]
    [SerializeField] private float aimRotationSpeed = 60f;
    [SerializeField] private float fallbackMagicCastHeight = 1.1f;
    [SerializeField] private float fallbackMagicCastForwardOffset = 0.65f;

    private float quickAimUntil;

    private void Awake()
    {
        ResolveReferences();
    }

    private void LateUpdate()
    {
        if (!autoUpdate)
            return;

        RotateAimFrame();
    }

    public void SetAutoUpdate(bool value)
    {
        autoUpdate = value;
    }

    public void RotateAimFrame()
    {
        ResolveReferences();

        if (GameplayInputBlocker.IsGameplayInputBlocked)
            return;

        if (playerController != null && playerController.IsEvading)
            return;

        if (playerController != null && (playerController.IsMeleeCombatLocomotionMode || playerController.IsMeleeCombatStance))
            return;

        bool shouldRotate = playerController != null && (playerController.IsAiming || Time.time < quickAimUntil);
        if (!shouldRotate || !CanRotateCurrentWeapon())
            return;

        if (!TryGetAimDirection(out Vector3 desiredDirection))
            return;

        RotateToward(desiredDirection);
    }

    public void BeginQuickAim(float holdTime)
    {
        quickAimUntil = Mathf.Max(quickAimUntil, Time.time + Mathf.Max(0f, holdTime));
    }

    private void ResolveReferences()
    {
        if (playerController == null)
            playerController = GetComponent<PlayerMovement>();

        if (playerEquipment == null)
            playerEquipment = GetComponent<PlayerEquipment>();

        if (aimCamera == null)
            aimCamera = Camera.main;

        if (rotateRoot == null)
            rotateRoot = transform;
    }

    private bool CanRotateCurrentWeapon()
    {
        return playerEquipment == null
            || (playerEquipment.CanCurrentWeaponRotateToAim
                && (playerEquipment.CanCurrentWeaponUseMagicAim || playerEquipment.CanCurrentWeaponUseMeleeCombatStance));
    }

    private bool TryGetAimDirection(out Vector3 direction)
    {
        direction = Vector3.zero;

        if (playerEquipment == null)
            return MeleeAimCalculator.TryGetMouseDirectionFromPlayer(transform, aimCamera, out direction);

        if (playerEquipment.CanCurrentWeaponUseMagicAim)
        {
            playerEquipment.RefreshCurrentWeaponReferences();
            if (!MagicTargeting.TryGetDirectionalAimLine(
                    transform,
                    playerEquipment,
                    aimCamera,
                    playerEquipment.CurrentWeaponStats,
                    fallbackMagicCastHeight,
                    fallbackMagicCastForwardOffset,
                    out MagicAimLine aimLine))
            {
                return false;
            }

            direction = aimLine.direction;
            return direction.sqrMagnitude > 0.0001f;
        }

        if (playerEquipment.CanCurrentWeaponUseMeleeCombatStance)
        {
            if (!MeleeAimCalculator.TryGetMouseDirectionFromPlayer(transform, aimCamera, out direction))
                return false;

            return direction.sqrMagnitude > 0.0001f;
        }

        return false;
    }

    private void RotateToward(Vector3 desiredDirection)
    {
        if (rotateRoot == null)
            return;

        desiredDirection.y = 0f;
        if (desiredDirection.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(desiredDirection.normalized, Vector3.up);
        float rotationSpeedMultiplier = playerController != null ? playerController.EvadeRotationSpeedMultiplier : 1f;
        float t = Mathf.Clamp01(aimRotationSpeed * rotationSpeedMultiplier * Time.deltaTime);
        rotateRoot.rotation = Quaternion.Slerp(rotateRoot.rotation, targetRotation, t);
    }
}
