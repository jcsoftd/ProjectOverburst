using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.EditorTools.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Applies only a reviewed, backed-up manifest under the local artifact directory.
public static class VfxLegacyCleanupUtility
{
    const string ReactionCatalog = "Assets/ProjectOverburst/Resources/Combat/VFX/ElementalReactionVfxCatalog.asset";
    const string HitCatalog = "Assets/ProjectOverburst/Resources/Combat/VFX/MeleeElementHitVfxCatalog.asset";
    const string Aura = "Assets/ProjectOverburst/Resources/Combat/VFX/PF_VFX_MeleeElementStatusAura.prefab";
    const string Heavy = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Heavy/GreatswordHeavyAttack.asset";
    const string FreezeId = "FreezeShatter.Freeze_Loop";
    const string ChilledName = "Chilled_Aura";
    static readonly string[] Owned = { ReactionCatalog, HitCatalog, Aura, Heavy };

    public static object Apply(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        RequireIdle();
        var plan = JObject.Parse(File.ReadAllText(Path.Combine(output, "plan.json")));
        var before = JObject.Parse(File.ReadAllText(Path.Combine(output, "asset_before.json")));
        string[] deleted = plan["delete"].ToObject<string[]>();
        string moved = (string)plan["moveSource"];
        string destination = (string)plan["moveDestination"];
        if (destination != CombatElementHitPoolBuilder.AuthoringSourcePath
            || deleted.Length != 15 || deleted.Any(p => !p.StartsWith("Assets/ProjectOverburst/", StringComparison.Ordinal)))
            throw new InvalidOperationException("Unreviewed manifest.");
        foreach (var record in before["backups"])
        {
            string path = (string)record["path"], backup = (string)record["backup"];
            if (Hash(backup) != (string)record["sha256"])
                throw new IOException("Backup hash mismatch: " + path);
            if ((deleted.Contains(path) || path == moved) && Hash(path) != (string)record["sha256"])
                throw new InvalidOperationException("Candidate changed after backup: " + path);
        }
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && (Owned.Contains(stage.assetPath) || deleted.Contains(stage.assetPath) || stage.assetPath == moved))
            throw new InvalidOperationException("Close the owned prefab stage before cleanup.");
        foreach (string path in Owned.Concat(deleted).Append(moved))
            if (EditorUtility.IsDirty(AssetDatabase.LoadMainAssetAtPath(path)))
                throw new InvalidOperationException("Unsaved asset: " + path);

        var journal = new List<string>();
        void Record(string entry)
        {
            journal.Add(entry);
            File.WriteAllText(Path.Combine(output, "apply_journal.json"), JsonConvert.SerializeObject(journal, Formatting.Indented));
        }
        // Remove catalog and nested references before deleting any prefab.
        var reactions = AssetDatabase.LoadAssetAtPath<ElementalReactionVfxCatalog>(ReactionCatalog);
        reactions.RemoveAuthoringEntriesNotIn(new HashSet<string> { FreezeId });
        EditorUtility.SetDirty(reactions);
        AssetDatabase.SaveAssetIfDirty(reactions);
        Record("Retained only freeze Loop in reaction runtime catalog.");
        foreach (string path in new[] { HitCatalog, Heavy })
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            EditorUtility.SetDirty(asset); // Serialize the current schema without removed references.
            AssetDatabase.SaveAssetIfDirty(asset);
            Record("Reserialized " + path);
        }
        var aura = PrefabUtility.LoadPrefabContents(Aura);
        try
        {
            var chilled = aura.transform.Find(ChilledName);
            if (chilled == null) throw new InvalidOperationException("Expected chilled child is absent.");
            string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(chilled.gameObject);
            if (!deleted.Contains(source)) throw new InvalidOperationException("Unexpected chilled source: " + source);
            Object.DestroyImmediate(chilled.gameObject);
            if (PrefabUtility.SaveAsPrefabAsset(aura, Aura) == null) throw new IOException("Aura save failed.");
        }
        finally { PrefabUtility.UnloadPrefabContents(aura); }
        Record("Removed only the retired chilled child from the shared aura.");
        foreach (var record in before["inventory"])
            foreach (string owner in record["incoming"].ToObject<string[]>())
                if (AssetDatabase.GetDependencies(owner, false).Contains((string)record["path"]))
                    throw new InvalidOperationException("Live dependency remains: " + owner + " -> " + record["path"]);
        EnsureFolder(Path.GetDirectoryName(destination).Replace('\\', '/'));
        string movedGuid = AssetDatabase.AssetPathToGUID(moved);
        string error = AssetDatabase.MoveAsset(moved, destination);
        if (!string.IsNullOrEmpty(error)) throw new IOException(error);
        if (AssetDatabase.AssetPathToGUID(destination) != movedGuid) throw new IOException("Authoring GUID changed.");
        Record("Moved authoring source out of the game, preserving GUID.");
        foreach (string path in deleted)
        {
            if (!AssetDatabase.DeleteAsset(path)) throw new IOException("Delete failed: " + path);
            Record("Deleted " + path);
        }
        var result = Validate(output);
        File.WriteAllText(Path.Combine(output, "apply_result.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
        return result;
    }

    public static object Validate(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        var plan = JObject.Parse(File.ReadAllText(Path.Combine(output, "plan.json")));
        var checks = new List<string>();
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks.Add(message);
        }
        foreach (string path in plan["delete"].ToObject<string[]>())
            Check(!File.Exists(path) && !File.Exists(path + ".meta"), "Deleted with meta: " + path);
        Check(!File.Exists((string)plan["moveSource"]), "Shared source removed from the game.");
        var before = JObject.Parse(File.ReadAllText(Path.Combine(output, "asset_before.json")));
        string sourceGuid = (string)before["inventory"].First(r => (string)r["path"] == (string)plan["moveSource"])["guid"];
        Check(AssetDatabase.AssetPathToGUID(CombatElementHitPoolBuilder.AuthoringSourcePath) == sourceGuid, "Authoring GUID preserved.");
        foreach (var row in JArray.Parse(File.ReadAllText(Path.Combine(output, "preserved.json"))))
        {
            string path = (string)row["path"];
            Check(Hash(path) == (string)row["sha256"], "Preserved bytes: " + path);
            Check(AssetDatabase.AssetPathToGUID(path) == (string)row["guid"], "Preserved GUID: " + path);
        }
        var catalog = Resources.Load<ElementalReactionVfxCatalog>(ElementalReactionVfxCatalog.ResourcePath);
        Check(catalog != null && catalog.Entries.Count == 1 && catalog.Entries[0].Id == FreezeId, "Only freeze Loop is in the runtime catalog.");
        Check(!ElementalReactionVfxAuthoringUtility.NeedsCatalogSynchronization(), "Auto-sync cannot recreate retired slots.");
        MeleeElementHitSetupUtility.ValidateFromCommandLine();
        Check(true, "Five playable element hit pools validated.");
        var heavy = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>(Heavy);
        Check(heavy.elementVfx.iceShatter != null && heavy.elementVfx.electricChainLink != null, "Shatter and lightning link remain.");
        Check(new SerializedObject(heavy).FindProperty("elementVfx.iceImpact") != null, "Future ice landing slot remains.");
        Check(Resources.Load<GameObject>("Combat/VFX/VFX_CritHit") != null, "Critical flash remains.");
        foreach (string path in Owned.Concat(new[] { CombatElementHitPoolBuilder.AuthoringSourcePath }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
                foreach (var child in prefab.GetComponentsInChildren<Transform>(true))
                    Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0, "No missing script: " + child.name);
        }
        var slots = VfxSlotCatalog.Collect();
        Check(slots.Any(s => s.PropertyPath == "elementVfx.iceImpact"), "Board keeps ice landing.");
        Check(slots.Any(s => s.Label == "치명타 섬광"), "Board keeps critical flash.");
        Check(slots.Any(s => s.Label == "저주 등급"), "Board keeps cursed pickup.");
        Check(slots.Count(s => s.PropertyPath != null && s.PropertyPath.StartsWith("elementVfx.darkBarrage")) == 4, "Board keeps four corruption barrage slots.");
        Check(slots.All(s => s.PropertyPath != "sharedHitPrefab" && s.PropertyPath != "chilledAura"
            && s.PropertyPath != "elementVfx.electricChainStart" && s.PropertyPath != "elementVfx.electricChainProc"), "Retired board fields absent.");
        var known = new HashSet<string>(slots.Select(s => s.Key));
        var extra = VfxSlotCatalog.DeepScan(known, out bool cancelled);
        Check(!cancelled, "Deep scan completed.");
        Check(extra.All(s => !s.propertyPath.EndsWith(".criticalFlash") && s.objectPath != "UiConfirm_01"), "Empty optional Feel fields excluded.");
        Check(slots.Count == 47 && extra.Count == 3, "Board catalog 47, active extra modules 3.");
        return new { status = "PASS", checks = checks.Count, details = checks, slots = slots.Count, extras = extra.Count };
    }

    static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Use an idle Editor.");
    }
    static string Hash(string path)
    {
        using (var hash = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }
}
