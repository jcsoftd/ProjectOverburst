using System;
using System.Collections.Generic;
using UnityEngine;

public static class FlaskRuntime
{
    public static FlaskInstanceState State(ItemData item)
    {
        if (item == null || !(item.baseData is FlaskItemData data)) return null;
        if (item.grade < ItemGrade.Common || item.grade > ItemGrade.Mythic) return null;
        item.EnsureRuntimeInstanceId();
        if (!FlaskGradeRoller.IsValid(item.flaskState, item.grade))
        {
            int equippedSlot = item.flaskState != null ? item.flaskState.equippedSlot : -1;
            int seed = item.flaskState != null ? item.flaskState.seed : StableSeed(item.runtimeInstanceId);
            if (!TryMigrateLegacy(item.flaskState, item.grade))
            {
                item.flaskState = FlaskGradeRoller.Roll(item.grade, seed);
                item.flaskState.equippedSlot = equippedSlot;
            }
        }
        if (float.IsNaN(item.flaskState.cooldownRemaining) || float.IsInfinity(item.flaskState.cooldownRemaining))
            item.flaskState.cooldownRemaining = 0f;
        item.flaskState.cooldownRemaining = Mathf.Clamp(item.flaskState.cooldownRemaining, 0f,
            FlaskStats.Calculate(data, item.flaskState).cooldown);
        return item.flaskState;
    }

    public static FlaskStats Stats(ItemData item) => FlaskStats.Calculate(item != null ? item.baseData as FlaskItemData : null, State(item));

    private static bool TryMigrateLegacy(FlaskInstanceState state, ItemGrade grade)
    {
        if (state == null || state.rolledGrade != grade || state.rolls == null || state.rolls.Count != 5)
            return false;
        int total = 0;
        for (int i = 0; i < state.rolls.Count; i++)
        {
            FlaskStatRoll row = state.rolls[i];
            if (row == null || (int)row.stat != i || row.stars == null
                || row.stars.Count > FlaskGradeRoller.MaxStarsPerRow) return false;
            foreach (WeaponGradeStarType star in row.stars)
                if (star != WeaponGradeStarType.White && star != WeaponGradeStarType.Green
                    && star != WeaponGradeStarType.Yellow) return false;
            total += row.stars.Count;
        }
        if (total != WeaponGradeStatRoller.GetMeleePositiveStarCount(grade)) return false;

        var rows = new List<FlaskStatRoll>(FlaskGradeRoller.RowCount);
        for (int i = 0; i < FlaskGradeRoller.RowCount; i++)
        {
            FlaskStatRoll row = state.rolls[i];
            row.stat = (FlaskStat)i;
            rows.Add(row);
        }
        foreach (WeaponGradeStarType star in state.rolls[4].stars)
        {
            int target = -1;
            int smallest = int.MaxValue;
            for (int i = 3; i >= 0; i--)
            {
                int count = rows[i].stars.Count;
                if (count >= FlaskGradeRoller.MaxStarsPerRow || count >= smallest) continue;
                smallest = count;
                target = i;
            }
            if (target < 0) return false;
            rows[target].stars.Add(star);
        }
        state.rolls = rows;
        state.cooldownRemaining = 0f;
        return FlaskGradeRoller.IsValid(state, grade);
    }

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
