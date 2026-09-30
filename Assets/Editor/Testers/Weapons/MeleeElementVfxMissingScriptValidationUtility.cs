using System;
using UnityEditor;
using UnityEngine;

public static class MeleeElementVfxMissingScriptValidationUtility
{
    private static readonly string[] SearchRoots =
    {
        "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/ElementSlash",
        "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/ElementHit",
        "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/ElementStatusAura"
    };

    [MenuItem("OVERBURST/Codex/Validate/Melee Element VFX Missing Scripts")]
    public static void ValidateFromMenu()
    {
        ValidateFromCommandLine();
    }

    public static void ValidateFromCommandLine()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", SearchRoots);
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException($"VFX 프리팹 로드 실패: {path}");

            Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);
            for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                int missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                    transforms[transformIndex].gameObject);
                if (missingCount > 0)
                {
                    throw new InvalidOperationException(
                        $"VFX Missing Script: {path} / {GetPath(prefab.transform, transforms[transformIndex])}");
                }
            }
        }

        Debug.Log($"[ProjectVTP] 근접 원소 VFX Missing Script 검증 완료: {guids.Length} prefabs, 0 missing.");
    }

    private static string GetPath(Transform root, Transform target)
    {
        string path = target.name;
        while (target != root && target.parent != null)
        {
            target = target.parent;
            if (target != root)
                path = target.name + "/" + path;
        }
        return path;
    }
}
