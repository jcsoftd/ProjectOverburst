using System;
using UnityEditor;
using UnityEngine;

public static class FlaskCooldownDataBuilder
{
    [MenuItem("JC Tool/Items/Set Flask Cooldowns (Missing Only)")]
    public static void Apply()
    {
        int changed = 0;
        foreach (FlaskKind kind in Enum.GetValues(typeof(FlaskKind)))
        {
            string path = "Assets/ProjectOverburst/Resources/Items/Flasks/Flask_" + kind + ".asset";
            FlaskItemData data = AssetDatabase.LoadAssetAtPath<FlaskItemData>(path);
            if (data == null) throw new InvalidOperationException("Missing flask asset: " + path);
            float target = kind == FlaskKind.Life || kind == FlaskKind.Regeneration || kind == FlaskKind.Overcharge
                ? 30f : 24f;
            if (data.cooldown > 0f) continue; // 2026-10-01: 이미 정한 쿨다운은 조정값이라 유지하고 비어 있는(0) 것만 채운다.
            data.cooldown = target;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data); // 2026-10-01: 바꾼 물약만 저장
            changed++;
        }
        Debug.Log("[FlaskCooldownDataBuilder] cooldown assets changed=" + changed);
    }
}
