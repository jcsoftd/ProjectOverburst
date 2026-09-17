using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Animation Profile", fileName = "EAP_Enemy")]
public sealed class EnemyAnimationProfile : ScriptableObject
{
    [SerializeField] private string profileId;
    [SerializeField] private RuntimeAnimatorController runtimeController;
    [SerializeField] private AnimationClip idle;
    [SerializeField] private AnimationClip walk;
    [SerializeField] private AnimationClip run;
    [SerializeField] private AnimationClip[] attackClips;
    [SerializeField] private AnimationClip hit;
    [SerializeField] private AnimationClip death;
    [SerializeField] private AnimationClip[] optional;
    [SerializeField] private string[] excludedRootMotionClipPaths;

    public string ProfileId => profileId;
    public RuntimeAnimatorController RuntimeController => runtimeController;
    public AnimationClip Idle => idle;
    public AnimationClip Walk => walk;
    public AnimationClip Run => run;
    public AnimationClip Hit => hit;
    public AnimationClip Death => death;
    public int AttackClipCount => attackClips != null ? attackClips.Length : 0;
    public int OptionalClipCount => optional != null ? optional.Length : 0;
    public int ExcludedRootMotionClipCount => excludedRootMotionClipPaths != null
        ? excludedRootMotionClipPaths.Length
        : 0;
    public bool IsValid => !string.IsNullOrWhiteSpace(profileId)
        && runtimeController != null
        && idle != null
        && walk != null
        && run != null
        && AttackClipCount > 0
        && hit != null
        && death != null;

    public AnimationClip GetAttackClip(int index)
    {
        return index >= 0 && index < AttackClipCount ? attackClips[index] : null;
    }

    public AnimationClip GetOptionalClip(int index)
    {
        return index >= 0 && index < OptionalClipCount ? optional[index] : null;
    }

    public string GetExcludedRootMotionClipPath(int index)
    {
        return index >= 0 && index < ExcludedRootMotionClipCount
            ? excludedRootMotionClipPaths[index]
            : string.Empty;
    }

    public void Configure(
        string id,
        RuntimeAnimatorController controller,
        AnimationClip idleClip,
        AnimationClip walkClip,
        AnimationClip runClip,
        AnimationClip[] attacks,
        AnimationClip hitClip,
        AnimationClip deathClip,
        AnimationClip[] optionalClips,
        string[] rootMotionExclusions)
    {
        profileId = id != null ? id.Trim() : string.Empty;
        runtimeController = controller;
        idle = idleClip;
        walk = walkClip;
        run = runClip;
        attackClips = attacks != null ? (AnimationClip[])attacks.Clone() : new AnimationClip[0];
        hit = hitClip;
        death = deathClip;
        optional = optionalClips != null ? (AnimationClip[])optionalClips.Clone() : new AnimationClip[0];
        excludedRootMotionClipPaths = rootMotionExclusions != null
            ? (string[])rootMotionExclusions.Clone()
            : new string[0];
    }
}
