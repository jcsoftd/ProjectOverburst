using System;
using UnityEngine;

public enum EnemyMotionRole { Locomotion, Turn, Support, Carry, Attack, Reaction, Introduction, Frozen, Death }
public enum EnemyMotionLifetime { Continuous, OneShot, HeldPose, Terminal }
public enum EnemyMotionRatePolicy { SnapshotAtStart, LiveLocomotion, LivePhased, OwnedPose }
public enum EnemyMotionState { Unknown, Entering, Playing, Holding, BlendingOut, Completed, Cancelled, Failed }
public enum EnemyMotionReason { None, Busy, Transitioning, InvalidBinding, InvalidRate, StaleHandle, ActorDead, Frozen, Preempted, OwnerCancelled, TargetLost, Disabled, Reused, EntryTimeout, UnexpectedExit, InvalidClock, DeadlineExceeded, HandoffAccepted }
public enum EnemyAbilityAimKind { FreshAim, PreparedGroup }
[Flags] public enum EnemyMotionConsumers { None = 0, ContinuousIntent = 1, AttackContext = 2, PreparedAvailability = 4, PreparedCommit = 8, CancelSnapshot = 16, ReactionPose = 32, Introduction = 64, DeathAndFreeze = 128, All = 255 }

public readonly struct EnemyMotionHandle : IEquatable<EnemyMotionHandle>
{
    public readonly uint Lease;
    public readonly int Generation, RequestId, GroupId, StepId;
    public bool IsValid => Generation > 0;
    public EnemyMotionHandle(uint lease, int generation, int request, int group, int step)
    { Lease = lease; Generation = generation; RequestId = request; GroupId = group; StepId = step; }
    public bool Equals(EnemyMotionHandle other) => Lease == other.Lease && Generation == other.Generation && RequestId == other.RequestId;
    public override bool Equals(object obj) => obj is EnemyMotionHandle other && Equals(other);
    public override int GetHashCode() => unchecked((int)Lease * 397 ^ Generation * 31 ^ RequestId);
    public static bool operator ==(EnemyMotionHandle left, EnemyMotionHandle right) => left.Equals(right);
    public static bool operator !=(EnemyMotionHandle left, EnemyMotionHandle right) => !left.Equals(right);
}

public readonly struct EnemyMotionSample
{
    public readonly EnemyMotionHandle Handle;
    public readonly string MotionId;
    public readonly AnimationClip Clip;
    public readonly int StateHash, Frame, Cycle;
    public readonly float Normalized, Weight, FixedTime, ScaledTime, UnscaledTime;
    public readonly bool Transitioning;
    public EnemyMotionSample(EnemyMotionHandle handle, string id, AnimationClip clip, int hash, float normalized, float weight, bool transitioning)
    {
        Handle = handle; MotionId = id; Clip = clip; StateHash = hash; Normalized = normalized;
        Weight = weight; Transitioning = transitioning; Frame = Time.frameCount; Cycle = Mathf.FloorToInt(normalized);
        FixedTime = Time.fixedTime; ScaledTime = Time.time; UnscaledTime = Time.unscaledTime;
    }
}

public readonly struct EnemyMotionCancelSnapshot
{
    public readonly bool Valid;
    public readonly EnemyMotionSample Sample;
    public readonly int AttackSequence, Phase;
    public readonly float ConsumedStrikeEnd;
    public readonly EnemyMotionReason Reason;
    public EnemyMotionCancelSnapshot(in EnemyMotionSample sample, int sequence, int phase, float consumedEnd, EnemyMotionReason reason)
    { Valid = sample.Clip != null && sample.Weight > .001f; Sample = sample; AttackSequence = sequence; Phase = phase; ConsumedStrikeEnd = consumedEnd; Reason = reason; }
}

public readonly struct EnemyMotionResult
{
    public readonly EnemyMotionHandle Handle;
    public readonly EnemyMotionState State;
    public readonly EnemyMotionReason Reason;
    public readonly EnemyMotionCancelSnapshot Snapshot;
    public bool IsTerminal => State == EnemyMotionState.Completed || State == EnemyMotionState.Cancelled || State == EnemyMotionState.Failed;
    public bool ReadyForHandoff => State == EnemyMotionState.Holding;
    public EnemyMotionResult(EnemyMotionHandle handle, EnemyMotionState state, EnemyMotionReason reason = EnemyMotionReason.None, EnemyMotionCancelSnapshot snapshot = default)
    { Handle = handle; State = state; Reason = reason; Snapshot = snapshot; }
}

public struct EnemyMotionRequest
{
    public UnityEngine.Object Owner;
    public EnemyMotionRole Role;
    public string MotionId;
    public int IntentId, GroupId, StepId, AttackSequence, Phase;
    public float Rate, Magnitude, BudgetSeconds, ConsumedStrikeEnd, StartNormalized, BlendInOverride;
    public bool HoldLastPose, ExternalCompletion;
    // Cleanup runs inside the transition gate, before the next owner is installed.
    public Action<EnemyMotionResult> OnInvalidated;
    public Action<EnemyMotionResult> OnTerminated;
}

public readonly struct EnemyMotionIntent
{
    public readonly Vector2 Direction;
    public readonly float Rate;
    public EnemyMotionIntent(Vector2 direction, float rate) { Direction = direction; Rate = rate; }
}

public readonly struct EnemyAbilityStartContext
{
    public readonly EnemyAbilityAimKind Kind;
    public readonly EnemyMotionHandle Preparation;
    public readonly Vector3 AimPosition;
    public readonly Quaternion Facing;
    public readonly uint PayloadLease;
    public readonly bool KeepCurrentFacing;
    public bool IsPrepared => Kind == EnemyAbilityAimKind.PreparedGroup && Preparation.IsValid && Preparation.GroupId != 0;
    public EnemyAbilityStartContext(EnemyMotionHandle preparation, Vector3 aim, Quaternion facing, uint payloadLease)
    { Kind = EnemyAbilityAimKind.PreparedGroup; Preparation = preparation; AimPosition = aim; Facing = facing; PayloadLease = payloadLease; KeepCurrentFacing = false; }
    private EnemyAbilityStartContext(Vector3 aim, Quaternion facing)
    { Kind = EnemyAbilityAimKind.FreshAim; Preparation = default; AimPosition = aim; Facing = facing; PayloadLease = 0; KeepCurrentFacing = true; }
    public static EnemyAbilityStartContext RearCounter(Vector3 aim, Quaternion facing) => new EnemyAbilityStartContext(aim, facing);
}

public readonly struct EnemyMotionContact
{
    public readonly EnemyMotionHandle Handle;
    public readonly string MotionId, Bone;
    public readonly int Cycle, Index;
    public readonly float Strength;
    public readonly Vector3 LocalSoleOffset;
    public EnemyMotionContact(EnemyMotionHandle handle, string id, string bone, int cycle, int index, float strength, Vector3 localSoleOffset = default)
    { Handle = handle; MotionId = id; Bone = bone; Cycle = cycle; Index = index; Strength = strength; LocalSoleOffset = localSoleOffset; }
}
