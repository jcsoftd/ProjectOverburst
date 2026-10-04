using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerFootstepAnimationRelay : MonoBehaviour
{
    private Animator source;
    private FootstepEmitter emitter;

    public void Bind(Animator animator, FootstepEmitter target)
    {
        source = animator;
        emitter = target;
    }

    public void Unbind(FootstepEmitter target)
    {
        if (emitter != target) return;
        emitter = null;
        source = null;
    }

    public void OnWalkFootstep(AnimationEvent contact) => Receive(contact, FootstepMotionKind.Walk);
    public void OnRunFootstep(AnimationEvent contact) => Receive(contact, FootstepMotionKind.Run);

    private void Receive(AnimationEvent contact, FootstepMotionKind kind)
    {
        if (contact == null || !contact.isFiredByAnimator || emitter == null || source == null)
            return;
        // Unity owns the AnimationEvent object. Keep only copied values in the emitter's frame buffer.
        emitter.QueueAnimationContact(source, contact.animatorClipInfo.clip,
            contact.animatorStateInfo.fullPathHash, contact.animatorStateInfo.normalizedTime,
            contact.animatorClipInfo.weight, contact.time, contact.intParameter, kind);
    }
}
