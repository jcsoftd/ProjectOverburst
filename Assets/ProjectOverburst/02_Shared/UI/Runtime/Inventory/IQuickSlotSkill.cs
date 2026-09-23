using UnityEngine;

// Active skills use the same 1-7 bar as items. Skill rules and cooldown stay with the skill.
public interface IQuickSlotSkill
{
    string DisplayName { get; }
    Sprite Icon { get; }
    float CooldownRemaining { get; }
    bool TryUse(out string reason);
}
