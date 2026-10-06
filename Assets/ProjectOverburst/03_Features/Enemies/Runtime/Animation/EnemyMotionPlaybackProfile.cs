using System;
using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Motion Playback Profile", fileName = "MPP_Enemy")]
public sealed class EnemyMotionPlaybackProfile : ScriptableObject
{
    public const int ContractVersion = 2;
    [Serializable]
    public struct Contact
    {
        public string bone;
        public Vector3 localSoleOffset;
        [Range(0f, 1f)] public float normalized;
        [Min(0f)] public float strength;
    }

    [Serializable]
    public sealed class Binding
    {
        public string motionId, sourceMotionId, state, rateParameter;
        public EnemyMotionLifetime lifetime = EnemyMotionLifetime.OneShot;
        public EnemyMotionRatePolicy ratePolicy;
        public AnimatorUpdateMode updateMode;
        [Min(.01f)] public float rate = 1f;
        [Min(0f)] public float blendIn = .12f, blendOut = .16f;
        [Range(0f, 1f)] public float completeNormalized = .999f;
        [Min(0f)] public float authoredYaw, settleSeconds = .1f, strideSpeed = 1f;
        public AnimationCurve turnProgress;
        public Contact[] contacts = Array.Empty<Contact>();
    }

    [SerializeField] private EnemyBossMaterialCollection collection;
    [SerializeField] private Binding[] bindings = Array.Empty<Binding>();
    [SerializeField] private int validatedContractVersion;
    [SerializeField] private EnemyMotionConsumers validatedConsumers;
    [SerializeField, Min(.05f)] private float entryTimeout = .65f;
    [SerializeField, Min(0f)] private float timingMargin = .35f;
    [SerializeField, Range(0f, 45f)] private float facingTolerance = 8f;
    [SerializeField, Range(8f, 180f)] private float movingTurnThreshold = 35f;
    public EnemyBossMaterialCollection Collection => collection;
    public Binding[] Bindings => bindings;
    public float EntryTimeout => entryTimeout;
    public float FacingTolerance => facingTolerance;
    public float MovingTurnThreshold => movingTurnThreshold;
    public bool IsValidated => validatedContractVersion == ContractVersion && collection != null && validatedConsumers == EnemyMotionConsumers.All;

    public Binding Find(string motionId)
    {
        for (int i = 0; i < bindings.Length; i++) if (bindings[i] != null && bindings[i].motionId == motionId) return bindings[i];
        return null;
    }

    public AnimationClip ResolveClip(Binding binding)
    {
        var motion = binding != null && collection != null ? collection.FindMotion(binding.sourceMotionId) : null;
        return motion != null && motion.IsPlayable && !motion.id.EndsWith("_RM", StringComparison.Ordinal) ? motion.runtime : null;
    }

    public float DurationBudget(Binding binding, float rate)
    {
        var clip = ResolveClip(binding);
        return clip == null || !FinitePositive(rate) ? 0f : clip.length / rate + binding.blendIn + binding.blendOut + binding.settleSeconds + timingMargin;
    }

    public bool Validate(out string error)
    {
        error = null;
        if (!IsValidated) { error = "Motion profile has not completed native contract validation."; return false; }
        for (int i = 0; i < bindings.Length; i++)
        {
            var b = bindings[i];
            if (b == null || string.IsNullOrEmpty(b.motionId) || string.IsNullOrEmpty(b.state) || ResolveClip(b) == null
                || !FinitePositive(b.rate) || !FiniteNonNegative(b.blendIn) || !FiniteNonNegative(b.blendOut))
            { error = "Missing or invalid motion binding at index " + i; return false; }
            for (int j = 0; j < i; j++) if (bindings[j].motionId == b.motionId) { error = "Duplicate motion ID: " + b.motionId; return false; }
            if (b.lifetime == EnemyMotionLifetime.Continuous && b.ratePolicy != EnemyMotionRatePolicy.LiveLocomotion)
            { error = "Continuous motion must use the locomotion rate policy: " + b.motionId; return false; }
            if (b.completeNormalized < 0f || b.completeNormalized > 1f || float.IsNaN(b.completeNormalized))
            { error = "Invalid completion marker: " + b.motionId; return false; }
        }
        foreach (string id in RequiredBindings)
            if (Find(id) == null) { error = "Missing mandatory binding: " + id; return false; }
        for (int i = 0; i < collection.attacks.Length; i++)
        {
            var attack = collection.attacks[i];
            if (attack == null || !attack.IsValid || Find("Attack_" + attack.ability.AnimatorTrigger) == null)
            { error = "Missing current attack binding at index " + i; return false; }
        }
        return true;
    }

    public static readonly string[] RequiredBindings = { "Locomotion", "CarryLocomotion", "Turn90Left", "Turn90Right", "Turn180Left", "Turn180Right", "UnearthRock", "Death", "IdleBreathe",
        "WalkForward", "WalkBackwards", "WalkLeft", "WalkRight", "WalkForwardWithRock", "WalkBackwardsWithRock", "ReactionPose", "FrozenPose", "GetHitReaction", "Roar1", "Roar2" };
    public static bool FinitePositive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    public static bool FiniteNonNegative(float value) => value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);

    // Builders own native validation and profile assignment; gameplay cannot activate an incomplete profile.
    public void Configure(EnemyBossMaterialCollection source, Binding[] value, int verifiedVersion)
    { collection = source; bindings = value ?? Array.Empty<Binding>(); validatedContractVersion = verifiedVersion; validatedConsumers = verifiedVersion == ContractVersion ? EnemyMotionConsumers.All : EnemyMotionConsumers.None; }
}
