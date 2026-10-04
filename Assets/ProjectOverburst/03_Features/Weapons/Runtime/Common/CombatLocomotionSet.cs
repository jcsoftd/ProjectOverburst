using System;
using UnityEngine;

[Serializable]
public sealed class CombatTurnMotion
{
    public AnimationClip clip;
    public string stateName;
    public float angle;
    // Measured from the same unbaked RootQ as the clip; consumed only in visual yaw.
    public AnimationCurve yaw;
    public float Duration => clip != null ? clip.length : 0f;
}

[Serializable]
public sealed class CombatMoveMotion
{
    public AnimationClip start, loop, stop;
    public string startState, stopState;
    public float loopCycleOffset, startLoopPhase, authoredSpeed;
    // Native source Stop displacement projected onto this direction, in source-avatar metres.
    public AnimationCurve stopDistance;
    [Min(.001f)] public float stopSourceHumanScale = 1f;
    public bool stopMatchEntrySpeed;
    [Range(0.05f, 1f)] public float stopTravelMultiplier = 1f;
}

[CreateAssetMenu(menuName = "OVERBURST/Weapons/Combat Locomotion Set")]
public sealed class CombatLocomotionSet : ScriptableObject
{
    public string idleState = "Melee_SwordIdle";
    // Clockwise: F, FR, R, BR, B, BL, L, FL.
    public CombatMoveMotion[] directions = new CombatMoveMotion[8];
    [Tooltip("전투 이동 보폭 배속에 Start/Stop 재생과 전환 시계를 함께 맞춥니다.")]
    public bool scaleStartStopWithLocomotionSpeed;
    public CombatTurnMotion left90, right90, left180, right180;
    [Range(40f, 75f)] public float turnThreshold = 58f;
    [Range(65f, 85f)] public float maximumUpperTwist = 75f;
    [Range(100f, 170f)] public float largeTurnThreshold = 135f;
    [Min(0f)] public float turnIntentSeconds = .06f;
    [Min(0f)] public float turnBlendSeconds = .08f;
    [Min(0f)] public float moveBlendSeconds = .12f;
    [Min(0f)] public float recoverySeconds = .12f;
    [Min(0f)] public float stopEntrySeconds = .08f;
    public bool scaleStopWithMoveDuration;
    [Range(.05f, 1f)] public float shortStopTravelMultiplier = .2f;
    [Min(0f)] public float shortMoveSeconds = .18f;
    [Min(.01f)] public float fullMomentumSeconds = .8f;
    [Min(0f)] public float stopBrakeSeconds = .12f;
    [Min(0f)] public float stopBrakeMaxDistance = .18f;
    public float idleChestYaw;

    public CombatTurnMotion SelectTurn(float delta, int previousSign)
    {
        int sign = Mathf.Abs(delta) > 179f ? (previousSign == 0 ? 1 : previousSign) : (delta < 0 ? -1 : 1);
        return Mathf.Abs(delta) >= largeTurnThreshold
            ? (sign < 0 ? left180 : right180) : (sign < 0 ? left90 : right90);
    }
    public CombatMoveMotion GetDirection(Vector3 localDirection)
    {
        if (directions == null || directions.Length != 8) return null;
        int sector = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg, 360f) / 45f) % 8;
        return directions[sector];
    }
}
