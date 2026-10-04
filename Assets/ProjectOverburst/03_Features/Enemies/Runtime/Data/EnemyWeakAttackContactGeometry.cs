using System;
using UnityEngine;

// Positions and radii are already measured at the final game size, in actor-local metres.
// Actor scale, threat-tier bonuses and approach distance must not enlarge these shapes.
[Serializable]
public struct EnemyWeakAttackContactCapsule
{
    [SerializeField] private Vector3 a, b;
    [SerializeField] private float radius;
    public Vector3 A => a;
    public Vector3 B => b;
    public float Radius => radius;
    public EnemyWeakAttackContactCapsule(Vector3 start, Vector3 end, float radiusMeters)
    { a = start; b = end; radius = radiusMeters; }
    public bool IsValid => Finite(a) && Finite(b) && Finite(radius) && radius > 0f;
    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
}

[Serializable]
public struct EnemyWeakAttackContactFrame
{
    [SerializeField] private float normalizedTime;
    [SerializeField] private EnemyWeakAttackContactCapsule[] capsules;
    public float NormalizedTime => normalizedTime;
    public int CapsuleCount => capsules != null ? capsules.Length : 0;
    public EnemyWeakAttackContactFrame(float time, params EnemyWeakAttackContactCapsule[] shapes)
    { normalizedTime = time; capsules = shapes != null ? (EnemyWeakAttackContactCapsule[])shapes.Clone() : null; }
    public EnemyWeakAttackContactCapsule CapsuleAt(int index) => capsules[index];
    internal EnemyWeakAttackContactFrame Copy() => new EnemyWeakAttackContactFrame(normalizedTime, capsules);
}

[Serializable]
public sealed class EnemyWeakAttackContactGeometry
{
    [SerializeField] private EnemyWeakAttackContactFrame[] frames;
    [SerializeField] private float maximumPlanarReach;
    public int CapsuleCount => frames != null && frames.Length > 0 ? frames[0].CapsuleCount : 0;
    public float MaximumPlanarReach => maximumPlanarReach;
    public bool HasData => frames != null && frames.Length >= 2 && CapsuleCount > 0 && CapsuleCount <= 16
        && EnemyWeakAttackContactCapsule.Finite(maximumPlanarReach) && maximumPlanarReach > 0f;

    public static EnemyWeakAttackContactGeometry Create(EnemyWeakAttackContactFrame[] source, Vector2 window)
    {
        var result = new EnemyWeakAttackContactGeometry();
        if (source == null) throw new ArgumentException("접촉 자세가 없습니다.");
        result.frames = new EnemyWeakAttackContactFrame[source.Length];
        for (int i = 0; i < source.Length; i++) result.frames[i] = source[i].Copy();
        if (!result.Validate(window, out string reason)) throw new ArgumentException(reason);
        return result;
    }

    public bool Validate(Vector2 window, out string reason)
    {
        reason = "접촉 판정의 자세·좌표·반경이 유효하지 않습니다.";
        if (frames == null || frames.Length < 2 || CapsuleCount < 1 || CapsuleCount > 16
            || !EnemyWeakAttackContactCapsule.Finite(window.x) || !EnemyWeakAttackContactCapsule.Finite(window.y)
            || window.x < 0f || window.y > 1f || window.y <= window.x) return false;
        float reach = 0f;
        for (int f = 0; f < frames.Length; f++)
        {
            var frame = frames[f];
            if (!EnemyWeakAttackContactCapsule.Finite(frame.NormalizedTime) || frame.NormalizedTime < 0f
                || frame.NormalizedTime > 1f || frame.CapsuleCount != CapsuleCount
                || f > 0 && frame.NormalizedTime <= frames[f - 1].NormalizedTime) return false;
            for (int c = 0; c < CapsuleCount; c++)
            {
                var capsule = frame.CapsuleAt(c);
                if (!capsule.IsValid) return false;
                reach = Mathf.Max(reach, new Vector2(capsule.A.x, capsule.A.z).magnitude + capsule.Radius,
                    new Vector2(capsule.B.x, capsule.B.z).magnitude + capsule.Radius);
            }
        }
        if (Mathf.Abs(frames[0].NormalizedTime - window.x) > .0001f
            || Mathf.Abs(frames[frames.Length - 1].NormalizedTime - window.y) > .0001f)
        { reason = "접촉 자세는 실제 접촉 시작부터 종료까지 포함해야 합니다."; return false; }
        maximumPlanarReach = reach;
        reason = string.Empty; return true;
    }

    public bool TryEvaluateCapsule(int capsuleIndex, float time, out EnemyWeakAttackContactCapsule capsule)
    {
        capsule = default;
        if (!HasData || (uint)capsuleIndex >= (uint)CapsuleCount || !EnemyWeakAttackContactCapsule.Finite(time)
            || time < frames[0].NormalizedTime || time > frames[frames.Length - 1].NormalizedTime) return false;
        int right = 1;
        while (right < frames.Length - 1 && time > frames[right].NormalizedTime) right++;
        var first = frames[right - 1]; var second = frames[right];
        float duration = second.NormalizedTime - first.NormalizedTime;
        if (duration <= 0f || first.CapsuleCount != CapsuleCount || second.CapsuleCount != CapsuleCount) return false;
        var a = first.CapsuleAt(capsuleIndex); var b = second.CapsuleAt(capsuleIndex);
        float t = Mathf.Clamp01((time - first.NormalizedTime) / duration);
        capsule = new EnemyWeakAttackContactCapsule(Vector3.Lerp(a.A, b.A, t), Vector3.Lerp(a.B, b.B, t),
            Mathf.Lerp(a.Radius, b.Radius, t));
        return capsule.IsValid;
    }
}

public static class EnemyWeakAttackContactQuery
{
    public static int Overlap(PhysicsScene physics, EnemyWeakAttackContactGeometry geometry, int capsuleIndex,
        float nativeTime, Vector3 actorPosition, Quaternion facing, Collider[] buffer, int layerMask, out Vector3 center)
    {
        center = actorPosition;
        if (!physics.IsValid() || geometry == null || buffer == null || buffer.Length == 0
            || !EnemyWeakAttackContactCapsule.Finite(actorPosition)
            || !geometry.TryEvaluateCapsule(capsuleIndex, nativeTime, out var shape)) return 0;
        Vector3 a = actorPosition + facing * shape.A, b = actorPosition + facing * shape.B;
        center = (a + b) * .5f;
        if (!EnemyWeakAttackContactCapsule.Finite(a) || !EnemyWeakAttackContactCapsule.Finite(b)) return 0;
        return (b - a).sqrMagnitude < .00000001f
            ? physics.OverlapSphere(a, shape.Radius, buffer, layerMask, QueryTriggerInteraction.Ignore)
            : physics.OverlapCapsule(a, b, shape.Radius, buffer, layerMask, QueryTriggerInteraction.Ignore);
    }
}
