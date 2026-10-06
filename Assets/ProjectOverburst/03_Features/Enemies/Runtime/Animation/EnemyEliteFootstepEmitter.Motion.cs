using System.Collections.Generic;
using UnityEngine;

public sealed partial class EnemyEliteFootstepEmitter
{
    private EnemyAnimationBridge motionContactBridge;
    private readonly Dictionary<string, Transform> motionFeet = new Dictionary<string, Transform>();
    private EnemyMotionContact lastContact;
    private bool hasMotionContact;
    private float contactStrength = 1f;
    public float CurrentContactStrength => contactStrength;
    private void BindMotionContacts()
    {
        if (motionContactBridge != null) motionContactBridge.MotionContact -= MotionFoot;
        if (actor == null) actor = GetComponent<EnemyActor>();
        motionContactBridge = actor != null ? actor.AnimationBridge : null;
        motionFeet.Clear(); hasMotionContact = false;
        if (actor != null && actor.Animator != null)
            foreach (var bone in actor.Animator.GetComponentsInChildren<Transform>(true))
                if (bone.name.Contains("Foot")) motionFeet[bone.name] = bone;
        if (motionContactBridge != null) motionContactBridge.MotionContact += MotionFoot;
    }
    private void UnbindMotionContacts()
    { if (motionContactBridge != null) motionContactBridge.MotionContact -= MotionFoot; hasMotionContact = false; motionFeet.Clear(); }
    private void MotionFoot(EnemyMotionContact contact)
    {
        if (motionContactBridge == null || !motionContactBridge.UsesOwnedMotion || !motionContactBridge.OwnsMotion(contact.Handle)
            || actor == null || !actor.IsLeased || actor.LeaseVersion != contact.Handle.Lease || actor.Health == null || actor.Health.IsDead
            || profile == null || !profile.IsValid || actor.Movement.IsStatusMovementLocked) return;
        bool turning = motionContactBridge.CurrentMotionRole == EnemyMotionRole.Turn;
        if (!turning && !actor.Movement.HasDestination) return;
        if (hasMotionContact && lastContact.Handle == contact.Handle && lastContact.MotionId == contact.MotionId && lastContact.Cycle == contact.Cycle && lastContact.Index == contact.Index) return;
        if (!motionFeet.TryGetValue(contact.Bone, out var foot) || foot == null) return;
        lastContact = contact; hasMotionContact = true;
        contactStrength = contact.Strength;
        try { EmitContact(transform.position, transform.forward, false, 0, foot.TransformPoint(contact.LocalSoleOffset)); }
        finally { contactStrength = 1f; }
    }
}
