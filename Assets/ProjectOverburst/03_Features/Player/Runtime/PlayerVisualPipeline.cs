using UnityEngine;

public class PlayerVisualPipeline : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMovement playerController;
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private WeaponPose weaponPose;
    [SerializeField] private PlayerAimRotation aimRotator;
    [SerializeField] private PlayerLeftHandGrip leftHandGripController;
    [SerializeField] private PlayerHumanoidLeftHandIKDriver leftHandIkDriver;
    [SerializeField] private PlayerFootLock footLock;

    private void Awake()
    {
        RefreshReferences();
        EnablePipelineControl();
    }

    private void LateUpdate()
    {
        if (weaponPose != null)
            weaponPose.ApplyPoseFrame();

        if (aimRotator != null)
            aimRotator.RotateAimFrame();
    }

    public void RefreshReferences()
    {
        if (playerController == null)
            playerController = GetComponent<PlayerMovement>();

        if (aimRotator == null)
            aimRotator = GetComponent<PlayerAimRotation>();

        if (targetAnimator == null)
            targetAnimator = GetComponentInChildren<Animator>(true);

        if (weaponPose == null)
            weaponPose = GetComponentInChildren<WeaponPose>(true);

        if (leftHandGripController == null)
            leftHandGripController = GetComponentInChildren<PlayerLeftHandGrip>(true);

        if (leftHandIkDriver == null && targetAnimator != null)
            leftHandIkDriver = targetAnimator.GetComponent<PlayerHumanoidLeftHandIKDriver>();

        if (footLock == null && targetAnimator != null)
            footLock = targetAnimator.GetComponent<PlayerFootLock>();
    }

    public void EnablePipelineControl()
    {
        if (weaponPose != null)
            weaponPose.SetAutoUpdate(false);

        if (aimRotator != null)
            aimRotator.SetAutoUpdate(false);

        if (leftHandGripController != null)
            leftHandGripController.SetAutoUpdate(false);

        EnsureLeftHandIkDriver();
        EnsureFootLock();
    }

    private void EnsureLeftHandIkDriver()
    {
        if (targetAnimator == null || leftHandGripController == null)
            return;

        if (leftHandIkDriver == null)
            leftHandIkDriver = targetAnimator.GetComponent<PlayerHumanoidLeftHandIKDriver>();
        if (leftHandIkDriver == null)
            leftHandIkDriver = targetAnimator.gameObject.AddComponent<PlayerHumanoidLeftHandIKDriver>();

        leftHandIkDriver.Bind(targetAnimator, leftHandGripController);
        P09CharacterVisualAdapter adapter = GetComponentInChildren<P09CharacterVisualAdapter>(true);
        Transform leftHandSocket = adapter != null
            ? adapter.GetNamedSocket(P09CharacterVisualAdapter.LeftHandWeaponSocketName)
            : null;
        leftHandGripController.Bind(
            targetAnimator,
            playerController,
            GetComponent<PlayerEquipment>(),
            leftHandSocket);
    }

    private void EnsureFootLock()
    {
        if (targetAnimator == null)
            targetAnimator = GetComponentInChildren<Animator>(true);

        if (targetAnimator == null)
            return;

        if (footLock == null)
            footLock = targetAnimator.GetComponent<PlayerFootLock>();

        if (footLock == null)
            footLock = targetAnimator.gameObject.AddComponent<PlayerFootLock>();

        footLock.Bind(playerController, targetAnimator);
    }
}
