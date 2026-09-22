using System;
using UnityEngine;

public static class FlaskRuntime
{
    public static FlaskInstanceState State(ItemData item)
    {
        if (item == null || !(item.baseData is FlaskItemData data)) return null;
        if (item.grade < ItemGrade.Common || item.grade > ItemGrade.Mythic) return null;
        item.EnsureRuntimeInstanceId();
        if (item.flaskState == null)
            item.flaskState = FlaskGradeRoller.Roll(item.grade, StableSeed(item.runtimeInstanceId));
        if (!FlaskGradeRoller.IsValid(item.flaskState, item.grade)) return null;
        if (float.IsNaN(item.flaskState.charge) || float.IsInfinity(item.flaskState.charge)) item.flaskState.charge = 0f;
        FlaskChargeRules.Initialize(item.flaskState, FlaskStats.Calculate(data, item.flaskState));
        return item.flaskState;
    }

    public static FlaskStats Stats(ItemData item) => FlaskStats.Calculate(item != null ? item.baseData as FlaskItemData : null, State(item));
    private static int StableSeed(string id)
    {
        unchecked { uint hash = 2166136261; foreach (char c in id) { hash ^= c; hash *= 16777619; } return (int)hash; }
    }

    public static WeaponElement RequiredElement(FlaskKind kind)
    {
        switch (kind)
        {
            case FlaskKind.Fire: return WeaponElement.Fire;
            case FlaskKind.Ice: return WeaponElement.Ice;
            case FlaskKind.Lightning: return WeaponElement.Electric;
            case FlaskKind.Water: return WeaponElement.Water;
            default: return WeaponElement.None;
        }
    }
}
