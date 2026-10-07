using System;
using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Crustaspikan Parry Recoil")]
public sealed class CrustaspikanParryRecoilProfile : ScriptableObject
{
    [Serializable]
    public sealed class Motion
    {
        public string attack, state;
        [Min(0)] public int strikeIndex;
        public AnimationClip clip;
        [Range(0f, 1f)] public float contactNormalized;
        public int sourceFrame;
        public string cueBone;
        public Vector3 cueOffset;
        public bool useCueRootPosition;
        public Vector3 cueRootPosition;
        public bool IsValid => !string.IsNullOrEmpty(attack) && !string.IsNullOrEmpty(state) && strikeIndex >= 0
            && clip != null && !clip.isLooping && clip.length > 0f && contactNormalized > 0f && contactNormalized < 1f;
    }
    [Min(.1f)] public float playbackSpeed = 1.5f;
    [Min(0f)] public float poseBlendSeconds = .08f;
    public Motion[] motions = Array.Empty<Motion>();

    public Motion Find(string attack, int strikeIndex)
    {
        foreach (var motion in motions)
            if (motion != null && motion.attack == attack && motion.strikeIndex == strikeIndex && motion.IsValid) return motion;
        return null;
    }
    public Motion FindCancelled(EnemyBossAttackMaterial material, float normalized)
    {
        if (material == null || material.runtimeClip == null || material.strikes == null) return null;
        // Cancellation may precede impact. Resolve the first unconsumed strike from its
        // contact range, rather than the executor's last iterated attack-context phase.
        for (int i = 0; i < material.strikes.Length; i++)
            if (normalized <= material.strikes[i].contactEnd + .0001f) return Find(material.runtimeClip.name, i);
        return null;
    }
    public void ApplyWindows(EnemyBossAttackMaterial material)
    {
        if (material == null || material.tuning?.parries == null) return;
        for (int i = 0; i < material.strikes.Length; i++)
        {
            var motion = Find(material.runtimeClip.name, i);
            var parry = material.tuning.parries[i];
            if (motion == null || parry == null || !parry.canParry) continue;
            parry.overrideWindow = true; parry.cueLeadSeconds = .25f;
            parry.cueBone = motion.cueBone; parry.cueOffset = motion.cueOffset;
            parry.useCueRootPosition = motion.useCueRootPosition; parry.cueRootPosition = motion.cueRootPosition;
            parry.startNormalized = motion.contactNormalized;
            parry.endNormalized = material.strikes[i].impact;
        }
    }
}
