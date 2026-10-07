using System;
using UnityEngine;

public sealed partial class EnemyAbilityDefinition
{
    [SerializeField] private string parryCueBonePath;
    // Reviewed attacking pose, in ActorRoot coordinates at visual scale one.
    [SerializeField] private Vector3 parryCueLocalPosition;
    [SerializeField, Min(.01f)] private float parryCueScale = 1f;

    public string ParryCueBonePath => parryCueBonePath;
    public Vector3 ParryCueLocalPosition => parryCueLocalPosition;
    public float ParryCueScale => Mathf.Max(.01f, parryCueScale);
    public bool HasAttackCue => IsParryable && !string.IsNullOrEmpty(parryCueBonePath);

    public bool TryResolveAttackCue(EnemyActor actor, out Transform socket, out Vector3 localPosition)
    {
        socket = null; localPosition = default;
        if (!HasAttackCue || actor == null) return false;
        socket = actor.transform.Find(parryCueBonePath);
        if (socket == null) return false;
        Vector3 scale = actor.VisualRoot != null ? actor.VisualRoot.localScale : Vector3.one;
        localPosition = Vector3.Scale(parryCueLocalPosition, scale);
        return true;
    }

    public void ConfigureAttackCue(string bonePath, Vector3 reviewedLocalPosition, float scale)
    {
        if (!IsParryable || string.IsNullOrWhiteSpace(bonePath) || !Finite(reviewedLocalPosition.x)
            || !Finite(reviewedLocalPosition.y) || !Finite(reviewedLocalPosition.z) || !Finite(scale) || scale <= 0f)
            throw new ArgumentException("공격 부위와 유효한 패링 빛 위치·크기가 필요합니다.");
        parryCueBonePath = bonePath; parryCueLocalPosition = reviewedLocalPosition; parryCueScale = scale;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
