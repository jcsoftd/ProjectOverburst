using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class MonsterStrongWarningRollbackVerifier
{
    const string Fixture = "Assets/Editor/Testers/Characters/StrongWarningRollbackFixture";
    const string Source = "Assets/ProjectOverburst/Resources/Enemies/Themes/Abilities/SpiderBrood_Cavecrawler_StingerAttack.asset";
    static string Full(string path) => Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
    static string Hash(string path) => MonsterBlenderParryR6Builder.Hash(Full(path));
    static JObject Fields(EnemyAbilityDefinition value) => JObject.Parse(EditorJsonUtility.ToJson(value));
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static void Rename(EnemyAbilityDefinition value, string name, bool save)
    {
        value.name = name; EditorUtility.SetDirty(value);
        if (save) AssetDatabase.SaveAssetIfDirty(value);
    }

    public static string VerifyReviewInputs(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string workspace = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        string output = Path.GetFullPath(Path.Combine(workspace, outputRelative));
        string allowed = Path.GetFullPath(Path.Combine(workspace, "개인파일/코덱스산출/Monsters")) + Path.DirectorySeparatorChar;
        Check(output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase), "Private output required.");
        string input = Path.Combine(output, "NativePreflight4/Input");
        var approved = JObject.Parse(File.ReadAllText(Path.Combine(input, "approved.json")));
        var dataset = JObject.Parse(File.ReadAllText(Path.Combine(input, "review-data.json")));
        MonsterWeakAttackPlayerLoopVerifier.ValidateStrongWarningReview(approved, dataset);
        MonsterWeakAttackPlayerLoopVerifier.ValidateStrongWarningReview(
            JObject.Parse(File.ReadAllText(Path.Combine(input, "legacy-approved.json"))),
            JObject.Parse(File.ReadAllText(Path.Combine(input, "legacy-dataset.json"))));
        foreach (string invalid in new[] { "schemaVersion", "parryStartsAtWarning", "parryTimingLocked", "datasetKey", "count" })
        {
            var copy = (JObject)approved.DeepClone();
            if (invalid == "schemaVersion") copy[invalid] = 99;
            else if (invalid == "parryStartsAtWarning") copy[invalid] = false;
            else if (invalid == "parryTimingLocked") copy[invalid] = true;
            else if (invalid == "datasetKey") copy[invalid] = "stale";
            else ((JArray)copy["monsters"]).RemoveAt(0);
            bool rejected = false;
            try { MonsterWeakAttackPlayerLoopVerifier.ValidateStrongWarningReview(copy, dataset); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid review accepted: " + invalid);
        }
        string result = Path.Combine(output, "review-input-verification.json");
        File.WriteAllText(result, new JObject { ["status"] = "PASS_NATIVE_REVIEW_INPUTS_7", ["accepted"] = 2,
            ["rejected"] = 5, ["playStarted"] = false }.ToString());
        return result;
    }

    public static string Run(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string workspace = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        string output = Path.GetFullPath(Path.Combine(workspace, outputRelative));
        string allowed = Path.GetFullPath(Path.Combine(workspace, "개인파일/코덱스산출/Monsters")) + Path.DirectorySeparatorChar;
        Check(output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase), "Private output required.");
        Check(!AssetDatabase.IsValidFolder(Fixture) && !Directory.Exists(Full(Fixture)), "Fixture already exists.");
        var report = new JObject { ["status"] = "FAIL", ["cases"] = new JArray() };
        var cases = (JArray)report["cases"]; bool created = false;
        try
        {
            Check(!string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets/Editor/Testers/Characters", "StrongWarningRollbackFixture")), "Fixture folder creation failed.");
            created = true;
            var paths = new[] { Fixture + "/A.asset", Fixture + "/B.asset", Fixture + "/Untouched.asset" };
            foreach (string path in paths) Check(AssetDatabase.CopyAsset(Source, path), "Fixture copy failed.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var values = paths.Select(AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>).ToArray();
            var original = values.Select(Fields).ToArray();
            float selected = values[0].GetHitNormalizedTime(0) * .25f;

            var edits = new List<MonsterStrongParryTimingBuilder.WarningEdit>();
            MonsterStrongParryTimingBuilder.WriteWarningWindow(values[0], selected, edits);
            MonsterStrongParryTimingBuilder.WriteWarningWindow(values[1], selected, edits);
            string untouchedBefore = Hash(paths[2]);
            Rename(values[2], "ExternalUntouched", true);
            string untouchedAfter = Hash(paths[2]);
            Check(untouchedBefore != untouchedAfter, "Conflict fixture did not change.");
            var restored = MonsterStrongParryTimingBuilder.RestoreWarningEdits(edits);
            Check(restored.Count == 2 && restored.All(r => (string)r["status"] == "RESTORED"), "Own writes did not restore.");
            Check((string)restored[0]["path"] == paths[1] && (string)restored[1]["path"] == paths[0], "Rollback is not reverse order.");
            Check(JToken.DeepEquals(Fields(values[0]), original[0]) && JToken.DeepEquals(Fields(values[1]), original[1]), "Original fields not restored.");
            Check(Hash(paths[2]) == untouchedAfter, "Untouched external edit was overwritten.");
            cases.Add(new JObject { ["case"] = "partial_apply_untouched_conflict", ["pass"] = true, ["restored"] = restored });

            edits.Clear(); MonsterStrongParryTimingBuilder.WriteWarningWindow(values[0], selected, edits);
            Rename(values[0], "ExternalDisk", true); string externalDisk = Hash(paths[0]);
            var disk = MonsterStrongParryTimingBuilder.RestoreWarningEdits(edits);
            Check((string)disk[0]["status"] == "SKIPPED_EXTERNAL_CHANGE" && Hash(paths[0]) == externalDisk && values[0].name == "ExternalDisk", "Disk conflict was overwritten.");
            cases.Add(new JObject { ["case"] = "touched_external_disk_change", ["pass"] = true });

            edits.Clear(); MonsterStrongParryTimingBuilder.WriteWarningWindow(values[1], selected, edits);
            string beforeMemoryEdit = Hash(paths[1]); Rename(values[1], "ExternalUnsaved", false);
            var memory = MonsterStrongParryTimingBuilder.RestoreWarningEdits(edits);
            Check((string)memory[0]["status"] == "SKIPPED_EXTERNAL_CHANGE" && Hash(paths[1]) == beforeMemoryEdit && values[1].name == "ExternalUnsaved" && EditorUtility.IsDirty(values[1]), "Unsaved conflict was overwritten.");
            cases.Add(new JObject { ["case"] = "touched_external_memory_change", ["pass"] = true });

            Check(MonsterStrongParryTimingBuilder.RestoreWarningEdits(new List<MonsterStrongParryTimingBuilder.WarningEdit>()).Count == 0 && Hash(paths[2]) == untouchedAfter, "Prewrite failure changed an asset.");
            cases.Add(new JObject { ["case"] = "failure_before_first_write", ["pass"] = true });

            edits.Clear(); MonsterStrongParryTimingBuilder.WriteWarningWindow(values[0], selected * .8f, edits);
            MonsterStrongParryTimingBuilder.WriteWarningWindow(values[2], selected, edits);
            edits[1].path = Fixture + "/Missing.asset";
            var failed = MonsterStrongParryTimingBuilder.RestoreWarningEdits(edits);
            Check((string)failed[0]["status"] == "FAILED" && (string)failed[1]["status"] == "RESTORED", "One rollback failure prevented other restoration.");
            cases.Add(new JObject { ["case"] = "rollback_failure_continues", ["pass"] = true });
            foreach (var value in values) Undo.ClearUndo(value);
            report["status"] = "PASS_NATIVE_ROLLBACK_5";
        }
        catch (Exception error) { report["error"] = error.ToString(); throw; }
        finally
        {
            bool recycled = !created || AssetDatabase.MoveAssetToTrash(Fixture);
            report["fixtureRecycled"] = recycled && !Directory.Exists(Full(Fixture)) && !File.Exists(Full(Fixture) + ".meta");
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "rollback-verification.json"), report.ToString());
            Check((bool)report["fixtureRecycled"], "Fixture recycle failed; inspect native report.");
        }
        return Path.Combine(output, "rollback-verification.json");
    }
}
