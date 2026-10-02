using System;
using UnityEngine;

[Serializable]
public struct PlayerEvadeMotionSettings
{
    [Min(0f)] public float distance;
    [Min(.01f)] public float duration;
    [Min(0f)] public float invincibleDuration;
    [Min(0f)] public float perfectWindow;
    [Min(0f)] public float cooldown;
    [Range(0f, 1f)] public float moveEase;
}

[CreateAssetMenu(fileName = "PlayerEvadeProfile", menuName = "OVERBURST/Player/Evade Profile")]
public sealed class PlayerEvadeProfile : ScriptableObject
{
    public const string ExplorationLayer = "Player_Evade";
    public const string ExplorationSpeed = "Player_EvadeSpeed";
    public const string ExplorationStandState = "Exploration_Dodge";
    public const string ExplorationRunState = "Exploration_DodgeToRun";
    public const string DodgeLightState = "Melee_DodgeAttack";
    public const string CombatStatePrefix = "Melee_Dodge_";

    [Header("탐험 이동")]
    public PlayerEvadeMotionSettings exploration = new PlayerEvadeMotionSettings
    { distance = 5f, duration = .30f, cooldown = .80f, moveEase = .35f };
    public AnimationClip explorationDodge;
    public AnimationClip explorationDodgeToRun;

    [Header("전투 접근")]
    public PlayerEvadeMotionSettings combatDodge = new PlayerEvadeMotionSettings
    { distance = 4f, duration = .48f, invincibleDuration = .10f, cooldown = .50f, moveEase = .35f };
    public DirectionalAnimationSet8 combatClips;
    [Min(0f)] public float entryBlend = .04f;
    [Min(0f)] public float exitBlend = .08f;

    public AnimationClip ResolveCombatClip(Vector3 localDirection, out string stateName)
    {
        float angle = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
        int sector = Mathf.Clamp(Mathf.RoundToInt(angle / 45f), -2, 2);
        switch (sector)
        {
            case -2: stateName = CombatStatePrefix + "L"; return combatClips.left;
            case -1: stateName = CombatStatePrefix + "FL"; return combatClips.forwardLeft;
            case 1: stateName = CombatStatePrefix + "FR"; return combatClips.forwardRight;
            case 2: stateName = CombatStatePrefix + "R"; return combatClips.right;
            default: stateName = CombatStatePrefix + "F"; return combatClips.forward;
        }
    }
}
