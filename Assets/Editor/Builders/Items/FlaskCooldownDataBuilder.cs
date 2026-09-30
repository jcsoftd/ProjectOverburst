using System;
using UnityEditor;
using UnityEngine;

public static class FlaskCooldownDataBuilder
{
    [MenuItem("JC Tool/Items/Set Flask Cooldowns")]
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
            if (Mathf.Approximately(data.cooldown, target)) continue;
            data.cooldown = target;
            EditorUtility.SetDirty(data);
            changed++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[FlaskCooldownDataBuilder] cooldown assets changed=" + changed);
    }
}
