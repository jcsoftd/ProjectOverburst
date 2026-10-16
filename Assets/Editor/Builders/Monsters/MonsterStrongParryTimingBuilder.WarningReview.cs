using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MonsterStrongParryTimingBuilder
{
    static JObject OutsideCueWindow(JObject fields)
    {
        var copy = (JObject)fields.DeepClone();
        ((JObject)(copy["MonoBehaviour"] ?? copy)).Remove("parryMotionWindows");
        return copy;
    }

    internal sealed class WarningEdit
    {
        internal EnemyAbilityDefinition ability;
        internal string path, diskHash, metaHash;
        internal JObject before, written;
    }

    internal static void WriteWarningWindow(EnemyAbilityDefinition ability, float selected, List<WarningEdit> edits)
    {
        string path = AssetDatabase.GetAssetPath(ability), full = Path.Combine(Project, path);
        var edit = new WarningEdit { ability = ability, path = path, before = Fields(ability),
            diskHash = MonsterBlenderParryR6Builder.Hash(full), metaHash = MonsterBlenderParryR6Builder.Hash(full + ".meta") };
        edit.written = edit.before;
        edits.Add(edit); // Include the current row even if its save fails.
        Undo.RecordObject(ability, "승인한 강공 핑부터 첫 타격까지 패링 허용");
        ability.ConfigureParryMotionWindows(new Vector2(selected, ability.GetHitNormalizedTime(0)));
        edit.written = Fields(ability);
        EditorUtility.SetDirty(ability); AssetDatabase.SaveAssetIfDirty(ability);
        edit.diskHash = MonsterBlenderParryR6Builder.Hash(full);
        edit.written = Fields(ability);
    }

    internal static JArray RestoreWarningEdits(List<WarningEdit> edits)
    {
        var result = new JArray();
        for (int i = edits.Count - 1; i >= 0; i--)
        {
            var edit = edits[i]; var row = new JObject { ["path"] = edit.path };
            try
            {
                string full = Path.Combine(Project, edit.path);
                // Never replace a disk or in-memory change made after our own write.
                if (MonsterBlenderParryR6Builder.Hash(full) != edit.diskHash
                    || MonsterBlenderParryR6Builder.Hash(full + ".meta") != edit.metaHash
                    || !JToken.DeepEquals(Fields(edit.ability), edit.written))
                    row["status"] = "SKIPPED_EXTERNAL_CHANGE";
                else
                {
                    EditorJsonUtility.FromJsonOverwrite(edit.before.ToString(), edit.ability);
                    EditorUtility.SetDirty(edit.ability); AssetDatabase.SaveAssetIfDirty(edit.ability);
                    RequireFrame(JToken.DeepEquals(Fields(edit.ability), edit.before), "Rollback fields mismatch.");
                    row["status"] = "RESTORED";
                }
            }
            catch (Exception error) { row["status"] = "FAILED"; row["error"] = error.ToString(); }
            result.Add(row);
        }
        return result;
    }

    public static string ApplyApprovedWarningParryFrames(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string directory = Output(outputRelative), receipt = Path.Combine(directory, "apply-result.json");
        RequireFrame(!File.Exists(receipt), "Inspect the existing warning application receipt.");
        var input = JObject.Parse(File.ReadAllText(Path.Combine(directory, "Input/approved.json")));
        var dataset = JObject.Parse(File.ReadAllText(Path.Combine(directory, "Input/review-data.json")));
        var contract = JObject.Parse(File.ReadAllText(Path.Combine(directory, "Input/contract.json")));
        var currentHashes = JArray.Parse(File.ReadAllText(Path.Combine(directory, "Input/current-baseline.json")))
            .ToDictionary(r => (string)r["path"], r => (string)r["sha256"]);
        RequireFrame(((int)input["schemaVersion"] == 3 && (string)input["purpose"] == "monster-danger-warning-cue-only-review"
                || (int)input["schemaVersion"] == 4 && (string)input["purpose"] == "monster-warning-parry-window-review"
                    && (bool)input["parryStartsAtWarning"] && !(bool)input["parryTimingLocked"])
            && (string)input["datasetKey"] == (string)dataset["datasetKey"]
            && (bool)input["animationTimingLocked"] && (bool)input["damageTimingLocked"]
            && (bool)contract["parryStartsAtSelectedWarning"] && (bool)contract["animationAndDamageLocked"], "Explicit ping-to-impact parry contract required.");
        var rows = input["monsters"].ToArray();
        RequireFrame(rows.Length == 35 && rows.Select(r => (string)r["id"]).Distinct().Count() == 35
            && rows.Select(r => (string)r["abilityPath"]).Distinct().Count() == 35, "35 unique strong attacks required.");
        var baseline = dataset["rows"].ToDictionary(r => (string)r["id"]);
        var catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalog>("Assets/ProjectOverburst/Resources/Enemies/Themes/Catalog.asset");
        var definitions = Enumerable.Range(0, catalog.Count).Select(catalog.GetDefinition).ToDictionary(d => d.EnemyId);
        var abilities = rows.Select(r => Load((string)r["abilityPath"])).ToArray();
        var before = new JArray(); int checks = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i]; string id = (string)row["id"], path = (string)row["abilityPath"];
            var ability = abilities[i]; var original = baseline[id]; var definition = definitions[id];
            RequireFrame(!EditorUtility.IsDirty(ability), "Unsaved attack edits: " + id);
            RequireFrame((string)row["abilityPath"] == (string)original["abilityPath"]
                && currentHashes[path] == MonsterBlenderParryR6Builder.Hash(Path.Combine(Project, path))
                && AssetDatabase.AssetPathToGUID(path) == (string)row["abilityGuid"]
                && Enumerable.Range(0, definition.AbilitySet.Count).Any(n => definition.AbilitySet.GetAbility(n) == ability), "Saved attack changed: " + id);
            var clip = ReviewedRuntimeClip(definition, ability);
            RequireFrame(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)) == (string)row["clipGuid"]
                && Math.Abs(clip.length - (double)original["clipDuration"]) < .00001
                && Math.Abs(clip.frameRate - (double)row["nativeFps"]) < .001, "Reviewed clip changed: " + id);
            RequireFrame(ability.FirstStrikeOnlyParry && ability.ParryStrikeCount == 1
                && ability.TryGetParryMotionWindow(0, out var window)
                && Math.Abs(window.x - (double)row["currentParryWindow"][0]) < .000001
                && Math.Abs(window.y - (double)row["currentParryWindow"][1]) < .000001
                && ability.HitCount == row["impactNormalized"].Count(), "Parry/hit contract changed: " + id);
            for (int n = 0; n < ability.HitCount; n++)
                RequireFrame(Math.Abs(ability.GetHitNormalizedTime(n) - (double)row["impactNormalized"][n]) < .000001, "Hit changed: " + id);
            float selected = (float)row["selectedWarningNormalized"];
            RequireFrame(!float.IsNaN(selected) && !float.IsInfinity(selected) && selected >= 0
                && selected < ability.GetHitNormalizedTime(0)
                && Math.Abs((double)row["selectedWarningFrame"] - (double)row["selectedWarningNormalized"]
                    * (double)original["clipDuration"] * (double)row["nativeFps"]) < .00001, "Invalid reviewed warning: " + id);
            var fields = Fields(ability);
            var copy = Object.Instantiate(ability); copy.name = ability.name;
            try
            {
                copy.ConfigureParryMotionWindows(new Vector2(selected, copy.GetHitNormalizedTime(0)));
                RequireFrame(copy.TryGetParryMotionWindow(0, out var selectedWindow) && Math.Abs(selectedWindow.x - selected) < .000001
                    && JToken.DeepEquals(OutsideCueWindow(Fields(copy)), OutsideCueWindow(fields)), "Cue window edit affected another field: " + id);
                foreach (float growth in new[] { 1f, 1.12f, 1.2f })
                {
                    float speed = (float)original["authoredSpeed"] * growth;
                    for (int frame = 0; frame <= 100; frame++)
                    {
                        float phase = frame / 100f;
                        RequireFrame(Math.Abs(copy.ResolvePacedTime(phase, speed) - ability.ResolvePacedTime(phase, speed)) < .000001, "Animation timing changed: " + id);
                        checks++;
                    }
                    double expectedLead = (double)row["leadBaseSeconds"] / growth;
                    RequireFrame(Math.Abs(copy.ResolvePacedTime(copy.GetHitNormalizedTime(0), speed)
                        - copy.ResolvePacedTime(selected, speed) - expectedLead) < .00002, "Reviewed interval mismatch: " + id);
                    checks++;
                }
            }
            finally { Object.DestroyImmediate(copy); }
            before.Add(new JObject { ["id"] = id, ["path"] = path, ["fields"] = fields,
                ["metaHash"] = MonsterBlenderParryR6Builder.Hash(Path.Combine(Project, path) + ".meta") });
        }
        File.WriteAllText(Path.Combine(directory, "native-before.json"), before.ToString());
        var saved = new JArray();
        var edits = new List<WarningEdit>();
        try
        {
            for (int i = 0; i < rows.Length; i++)
            {
                var ability = abilities[i]; var row = rows[i]; string path = (string)row["abilityPath"];
                RequireFrame(MonsterBlenderParryR6Builder.Hash(Path.Combine(Project, path)) == currentHashes[path], "Attack changed after preflight.");
                RequireFrame(!EditorUtility.IsDirty(ability) && JToken.DeepEquals(Fields(ability), before[i]["fields"]), "Attack changed in memory after preflight.");
                WriteWarningWindow(ability, (float)row["selectedWarningNormalized"], edits);
                RequireFrame(ability.TryGetParryMotionWindow(0, out var selectedWindow)
                    && Math.Abs(selectedWindow.x - (double)row["selectedWarningNormalized"]) < .000001
                    && JToken.DeepEquals(OutsideCueWindow(Fields(ability)), OutsideCueWindow((JObject)before[i]["fields"]))
                    && MonsterBlenderParryR6Builder.Hash(Path.Combine(Project, path) + ".meta") == (string)before[i]["metaHash"], "Saved warning/preservation mismatch.");
                saved.Add(new JObject { ["id"] = row["id"], ["path"] = path, ["normalized"] = selectedWindow.x,
                    ["leadBaseSeconds"] = row["leadBaseSeconds"], ["leadGrowthCapSeconds"] = row["leadGrowthCapSeconds"],
                    ["sha256"] = MonsterBlenderParryR6Builder.Hash(Path.Combine(Project, path)), ["fields"] = Fields(ability) });
            }
        }
        catch (Exception error)
        {
            var rollback = RestoreWarningEdits(edits);
            bool complete = rollback.All(r => (string)r["status"] == "RESTORED");
            File.WriteAllText(Path.Combine(directory, "rollback-result.json"), new JObject {
                ["status"] = complete ? "RESTORED_OWN_EDITS" : "MANUAL_REVIEW_REQUIRED",
                ["originalError"] = error.ToString(), ["records"] = rollback }.ToString());
            if (!complete) throw new InvalidOperationException("Warning apply failed; external changes were preserved. Inspect rollback-result.json.", error);
            throw;
        }
        File.WriteAllText(receipt, new JObject { ["status"] = "APPLIED_WARNING_AND_PARRY", ["utc"] = DateTime.UtcNow,
            ["checks"] = checks, ["approvedSha256"] = MonsterBlenderParryR6Builder.Hash(Path.Combine(directory, "Input/approved.json")),
            ["animationAndDamagePreserved"] = true, ["parryStartsAtSelectedWarning"] = true, ["records"] = saved }.ToString());
        return receipt;
    }
}
