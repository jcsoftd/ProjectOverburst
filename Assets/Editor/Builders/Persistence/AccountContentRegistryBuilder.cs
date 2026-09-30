using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Overburst.Persistence;

public sealed class AccountContentRegistryBuilder : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;
    public void OnPreprocessBuild(BuildReport report) => Build();

    [MenuItem("OVERBURST/Persistence/Rebuild Content Registry")]
    public static AccountContentRegistry Build()
    {
        const string path = "Assets/ProjectOverburst/Resources/Persistence/AccountContentRegistry.asset";
        EnsureFolder("Assets/ProjectOverburst/Resources/Persistence");
        var entries = new List<AccountContentEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string type in new[] { "BaseItemData", "MerchantDefinition" })
            foreach (var guid in AssetDatabase.FindAssets("t:" + type, new[] { "Assets/ProjectOverburst" }))
            {
                if (!seen.Add(guid)) continue;
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) entries.Add(new AccountContentEntry { id = guid, asset = asset });
            }
        var registry = AssetDatabase.LoadAssetAtPath<AccountContentRegistry>(path);
        if (registry == null)
        {
            registry = ScriptableObject.CreateInstance<AccountContentRegistry>();
            AssetDatabase.CreateAsset(registry, path);
        }
        registry.SetAuthoringEntries(entries.OrderBy(e => e.id, StringComparer.Ordinal).ToList());
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssetIfDirty(registry);
        return registry;
    }

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }
}
