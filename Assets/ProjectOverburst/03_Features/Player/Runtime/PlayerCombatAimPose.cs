using UnityEngine;

// One Humanoid pass applies real-time aim, retaining the authored arms and grip.
[DisallowMultipleComponent]
[DefaultExecutionOrder(650)]
public sealed class PlayerCombatAimPose : MonoBehaviour
{
    private Animator animator;
    private PlayerCombatFacingController facing;
    private readonly HumanBodyBones[] bones = { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest };
    private readonly Transform[] transforms = new Transform[3];
    private static readonly float[] weights = { .20f, .35f, .45f };
    private int pass = -1, appliedFrame = -1;
    private float attackBlendStart, attackBlendDuration, capturedYaw, renderedYaw;
    private bool hasRenderedYaw;
    public float LastCorrection { get; private set; }

    public void Bind(Animator target, PlayerCombatFacingController owner)
    {
        animator = target; facing = owner;
        if (animator == null || !animator.isHuman) return;
        pass = animator.GetLayerIndex("Combat_MeleeWeapon");
        for (int i = 0; i < bones.Length; i++) transforms[i] = animator.GetBoneTransform(bones[i]);
        appliedFrame = -1;
        hasRenderedYaw = false;
        CancelAttackBlend();
    }
    public void BeginAttackBlend(float duration)
    {
        if (!hasRenderedYaw || duration <= 0) return;
        capturedYaw = renderedYaw;
        attackBlendStart = Time.time;
        attackBlendDuration = Mathf.Min(.12f, duration);
    }
    public void CancelAttackBlend() => attackBlendDuration = 0;
    private void LateUpdate()
    {
        Transform chest = transforms[2] != null ? transforms[2] : transforms[1];
        if (chest == null) return;
        Vector3 forward = chest.rotation * Vector3.forward;
        renderedYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        hasRenderedYaw = true;
    }
    private void OnAnimatorIK(int layerIndex)
    {
        bool attackEntry = attackBlendDuration > 0 && Time.time < attackBlendStart + attackBlendDuration;
        if (layerIndex != pass || appliedFrame == Time.frameCount || animator == null || facing == null
            || (!facing.IsPoseActive && !attackEntry)) { LastCorrection = 0; return; }
        appliedFrame = Time.frameCount;
        Transform chest = transforms[2] != null ? transforms[2] : transforms[1];
        if (chest == null || facing.Set == null) return;
        Vector3 forward = chest.rotation * Vector3.forward;
        float currentYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        float targetYaw = facing.LowerYaw + facing.Set.idleChestYaw + facing.UpperDelta;
        float correction = facing.IsStationaryPose ? Mathf.DeltaAngle(currentYaw, targetYaw) : facing.UpperDelta;
        if (attackEntry && !facing.IsPoseActive)
        {
            // Carry the last rendered aim into the incoming full-body attack, then relinquish it.
            // Facing.AimYaw is the locked action direction during this short entry blend.
            float progress = Mathf.SmoothStep(0, 1, (Time.time - attackBlendStart) / attackBlendDuration);
            correction = Mathf.DeltaAngle(currentYaw,
                Mathf.LerpAngle(capturedYaw, currentYaw + facing.UpperDelta, progress));
        }
        LastCorrection = correction;
        float total = 0;
        for (int i = 0; i < transforms.Length; i++) if (transforms[i] != null) total += weights[i];
        for (int i = 0; i < transforms.Length; i++)
        {
            var bone = transforms[i];
            if (bone == null) continue;
            Vector3 localUp = bone.parent.InverseTransformDirection(Vector3.up);
            animator.SetBoneLocalRotation(bones[i],
                Quaternion.AngleAxis(correction * weights[i] / total, localUp) * bone.localRotation);
        }
    }
}
