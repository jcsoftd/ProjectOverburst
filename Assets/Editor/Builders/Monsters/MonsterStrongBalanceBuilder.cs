using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class MonsterStrongBalanceBuilder
{
    public static int Apply()
    {
MonsterBlenderParryR6Builder.RequireIdle();
if (IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
    || !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
    || !string.IsNullOrEmpty(UnityEditor.SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
    throw new System.InvalidOperationException("Unoccupied account required.");
string output = MonsterMediumStrongCooldownBuilder.Output("개인파일/코덱스산출/Monsters/20261016_StrongBalance");
if (System.IO.File.Exists(System.IO.Path.Combine(output, "apply-result.json"))) throw new System.InvalidOperationException("Existing receipt requires inspection.");
var plan = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(output, "plan.json")));
var baseline = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(output, "baseline.json")));
var elite = plan["balanceRows"].Where(r => (string)r["grade"] == "elite").ToArray();
var records = new Newtonsoft.Json.Linq.JArray();
var edits = new System.Collections.Generic.List<MonsterStrongParryTimingBuilder.WarningEdit>();
var variantEdits = new System.Collections.Generic.List<(EnemyVariantProfile asset, string path, float oldValue, string savedHash)>();
string Hash(string path) => MonsterBlenderParryR6Builder.Hash(System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", path)));
foreach (var row in elite)
    foreach (string path in new[] { (string)row["path"], (string)row["variantPath"] })
    {
        var original = baseline["files"].Single(r => (string)r["path"] == path);
        var asset = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
        if (Hash(path) != (string)original["sha256"] || UnityEditor.EditorUtility.IsDirty(asset))
            throw new System.InvalidOperationException("Baseline changed: " + path);
    }
try
{
    foreach (var row in elite)
    {
        string path = (string)row["path"];
        var ability = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(path);
        var edit = new MonsterStrongParryTimingBuilder.WarningEdit { ability = ability, path = path,
            before = Newtonsoft.Json.Linq.JObject.Parse(UnityEditor.EditorJsonUtility.ToJson(ability)), diskHash = Hash(path), metaHash = Hash(path + ".meta") };
        edit.written = edit.before; edits.Add(edit);
        var data = new UnityEditor.SerializedObject(ability); data.FindProperty("cooldown").floatValue = (float)row["newCooldown"];
        UnityEditor.Undo.RecordObject(ability, "엘리트 강공 쿨타임 원본 대비 40% 증가");
        data.ApplyModifiedPropertiesWithoutUndo(); UnityEditor.EditorUtility.SetDirty(ability); UnityEditor.AssetDatabase.SaveAssetIfDirty(ability);
        edit.diskHash = Hash(path); edit.written = Newtonsoft.Json.Linq.JObject.Parse(UnityEditor.EditorJsonUtility.ToJson(ability));
        string variantPath = (string)row["variantPath"];
        var variant = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyVariantProfile>(variantPath);
        var variantData = new UnityEditor.SerializedObject(variant);
        var entry = (variant, variantPath, variantData.FindProperty("attackInterval").floatValue, Hash(variantPath)); variantEdits.Add(entry);
        UnityEditor.Undo.RecordObject(variant, "엘리트 공격 종료 후 3초 대기");
        variantData.FindProperty("attackInterval").floatValue = 3;
        variantData.ApplyModifiedPropertiesWithoutUndo(); UnityEditor.EditorUtility.SetDirty(variant); UnityEditor.AssetDatabase.SaveAssetIfDirty(variant);
        variantEdits[variantEdits.Count - 1] = (variant, variantPath, entry.Item3, Hash(variantPath));
        records.Add(new Newtonsoft.Json.Linq.JObject { ["id"] = row["id"], ["cooldown"] = ability.Cooldown, ["originalCooldown"] = row["originalCooldown"],
            ["abilityPath"] = path, ["abilitySha256"] = edit.diskHash, ["variantPath"] = variantPath, ["variantSha256"] = Hash(variantPath) });
    }
}
catch (System.Exception error)
{
    foreach (var entry in variantEdits.AsEnumerable().Reverse())
        if (Hash(entry.path) == entry.savedHash && !UnityEditor.EditorUtility.IsDirty(entry.asset))
        {
            var data = new UnityEditor.SerializedObject(entry.asset); data.FindProperty("attackInterval").floatValue = entry.oldValue;
            data.ApplyModifiedPropertiesWithoutUndo(); UnityEditor.EditorUtility.SetDirty(entry.asset); UnityEditor.AssetDatabase.SaveAssetIfDirty(entry.asset);
        }
    System.IO.File.WriteAllText(System.IO.Path.Combine(output, "rollback-result.json"), new Newtonsoft.Json.Linq.JObject {
        ["error"] = error.ToString(), ["abilities"] = MonsterStrongParryTimingBuilder.RestoreWarningEdits(edits) }.ToString());
    throw;
}
System.IO.File.WriteAllText(System.IO.Path.Combine(output, "apply-result.json"), new Newtonsoft.Json.Linq.JObject {
    ["status"] = "APPLIED_ELITE_BASE_1_40_INTERVAL_3", ["records"] = records }.ToString());
return records.Count;

    }
}
