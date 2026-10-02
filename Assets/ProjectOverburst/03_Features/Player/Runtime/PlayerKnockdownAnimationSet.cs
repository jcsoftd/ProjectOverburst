using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Overburst/Player/Knockdown Animation Set")]
public sealed class PlayerKnockdownAnimationSet : ScriptableObject
{
    public const string LayerName = "Player_Knockdown";
    public const string FallState = "Player_Falling";
    public const string HoldState = "Player_DownHold";
    public const string RiseState = "Player_Rise";
    public const string HoldTimeParameter = "PlayerDownPoseTime";

    [Serializable]
    public sealed class Motion
    {
        public string id;
        public string poseId;
        public AnimationClip clip;
        public Vector2 direction;
        // Authored planar travel sampled by the builder; monotonically consumed by the motor.
        public AnimationCurve travel = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public bool IsValid => clip != null && clip.isHumanMotion && clip.length > .01f;
    }

    public AnimationClip fallTemplate;
    public AnimationClip riseTemplate;
    public Motion[] falls = Array.Empty<Motion>();
    public Motion defaultRise;
    public Motion[] directionalRises = Array.Empty<Motion>();
    [Min(0)] public float groundedHold = .25f;
    [Min(0)] public float recoveryProtection = .6f;
    [Range(0, 1)] public float inputDeadzone = .2f;
    [Min(0)] public float fallDistance = 2.2f;
    [Min(0)] public float riseDistance = .35f;
    [Min(0)] public float entryBlend = .06f;
    [Min(0)] public float riseBlend = .08f;
    [Min(0)] public float exitBlend = .1f;

    public Motion SelectFall(int sequence)
    {
        if (falls == null || falls.Length == 0 || defaultRise == null || !defaultRise.IsValid) return null;
        int start = (int)((uint)sequence % (uint)falls.Length);
        for (int i = 0; i < falls.Length; i++)
        {
            Motion motion = falls[(start + i) % falls.Length];
            if (motion != null && motion.IsValid && motion.poseId == defaultRise.poseId) return motion;
        }
        return null;
    }

    public Motion SelectRise(string pose, Vector2 inputDirection)
    {
        Motion selected = defaultRise != null && defaultRise.IsValid && defaultRise.poseId == pose ? defaultRise : null;
        if (inputDirection.sqrMagnitude < .0001f || directionalRises == null) return selected;
        float best = -1f;
        foreach (Motion motion in directionalRises)
        {
            if (motion == null || !motion.IsValid || motion.poseId != pose || motion.direction.sqrMagnitude < .001f) continue;
            float dot = Vector2.Dot(inputDirection.normalized, motion.direction.normalized);
            if (dot <= best + .0001f) continue; // Stable ties across camera/local floating-point conversion.
            best = dot;
            selected = motion;
        }
        return selected;
    }
}
