using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class MonsterStrongParryTimingBuilder
{
    static float CueDamageInterval(EnemyAbilityDefinition ability, float speed)
    {
        if (!ability.TryGetParryMotionWindow(0, out var window)) throw new InvalidDataException("Authored first parry window required.");
        return ability.ResolvePacedTime(window.y, speed) - ability.ResolvePacedTime(window.x, speed);
    }

    static void SetBaseReleaseSeconds(EnemyAbilityDefinition ability, float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < .1f)
            throw new InvalidDataException("Requested baseline is outside the authored release range.");
        var serialized = new SerializedObject(ability);
        serialized.FindProperty("releaseDuration").floatValue = seconds;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static JObject OutsideBaseRelease(JObject fields)
    {
        var copy = (JObject)fields.DeepClone(); ((JObject)(copy["MonoBehaviour"] ?? copy)).Remove("releaseDuration"); return copy;
    }

    static void VerifyBaseTempo(EnemyAbilityDefinition ability, JToken row, JObject before)
    {
        RequireFrame(ability.IsValid && ability.UsesPacedTimeline && ability.FirstStrikeOnlyParry
            && JToken.DeepEquals(OutsideBaseRelease(Fields(ability)), OutsideBaseRelease(before)), "Non-tempo fields changed: " + row["id"]);
        foreach (float speed in new[] { .75f, 1f, 1.5f })
            RequireFrame(Math.Abs(CueDamageInterval(ability, speed) - (float)row["targetSeconds"] / speed) < .00002f,
                "Baseline target or speed scaling mismatch: " + row["id"]);
    }

    static JObject BaseTempoRecord(EnemyAbilityDefinition ability, JToken row, JToken before, float previousSeconds)
    {
        ability.TryGetParryMotionWindow(0, out var window);
        float cue = ability.ResolvePacedTime(window.x, 1f);
        var hits = new JArray(); var afterCue = new JArray();
        for (int i = 0; i < ability.HitCount; i++)
        {
            float time = ability.ResolvePacedTime(ability.GetHitNormalizedTime(i), 1f); hits.Add(time); afterCue.Add(time - cue);
        }
        float strikeSample = (ability.PreparationEnd + (float)before["fields"]["MonoBehaviour"]["motionReleaseReferenceNormalized"]) * .5f;
        return new JObject { ["id"] = row["id"], ["name"] = row["name"], ["grade"] = row["grade"], ["themes"] = row["themes"],
            ["abilityPath"] = row["abilityPath"], ["guid"] = row["guid"], ["clipGuid"] = row["clipGuid"], ["fields"] = Fields(ability),
            ["cueFrame"] = row["cueFrame"], ["impactFrames"] = row["impactFrames"], ["hitCount"] = ability.HitCount,
            ["targetSeconds"] = row["targetSeconds"], ["previousFirstDamageAfterCueSeconds"] = previousSeconds,
            ["cueSecondsAtSpeedOne"] = cue, ["impactSecondsAtSpeedOne"] = hits, ["afterCueSecondsAtSpeedOne"] = afterCue,
            ["firstDamageAfterCueSeconds"] = afterCue[0], ["executionSecondsAtSpeedOne"] = ability.ResolveExecutionDuration(1f),
            ["sourceStrikeRateAtSpeedOne"] = ability.ResolvePhaseAnimationSpeed(strikeSample, 1f) };
    }

    public static string ApplyBaseTempoCalibration(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string directory = Output(outputRelative), receipt = Path.Combine(directory, "apply-result.json");
        if (File.Exists(receipt)) throw new InvalidOperationException("Inspect the existing calibration receipt.");
        var plan = JObject.Parse(File.ReadAllText(Path.Combine(directory, "plan.json")));
        var rows = plan["targets"].ToArray();
        RequireFrame((string)plan["purpose"] == "monster-base-tempo-calibration" && (float)plan["baseAttackSpeedMultiplier"] == 1f
            && rows.Length > 0 && rows.Length == rows.Select(r => (string)r["abilityPath"]).Distinct().Count(), "Explicit unique baseline targets required.");
        var abilities = rows.Select(r => Load((string)r["abilityPath"])).ToArray();
        var before = new JArray(); var proposed = new float[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i]; var ability = abilities[i]; var fields = Fields(ability); string id = (string)row["id"], path = (string)row["abilityPath"];
            float target = (float)row["targetSeconds"];
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)row["definitionPath"]);
            RequireFrame(!float.IsNaN(target) && !float.IsInfinity(target) && target > 0f
                && JToken.DeepEquals(fields, row["fields"]) && AssetDatabase.AssetPathToGUID(path) == (string)row["guid"], "Baseline changed before calibration: " + id);
            RequireFrame(definition != null && definition.EnemyId == id && !EditorUtility.IsDirty(definition)
                && Enumerable.Range(0, definition.AbilitySet.Count).Any(slot => definition.AbilitySet.GetAbility(slot) == ability)
                && Math.Abs(definition.ResolveRuntimeStats().AttackSpeedMultiplier - 1f) < .000001f, "Definition/base attack speed mismatch: " + id);
            var clip = ReviewedRuntimeClip(definition, ability);
            RequireFrame(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)) == (string)row["clipGuid"], "Reviewed motion changed: " + id);
            float previous = CueDamageInterval(ability, 1f);
            before.Add(new JObject { ["id"] = id, ["path"] = path, ["fields"] = fields, ["guid"] = row["guid"],
                ["metaHash"] = MonsterBlenderParryR6Builder.Hash(Path.Combine(Project, path) + ".meta"), ["intervalSeconds"] = previous });
            var copy = UnityEngine.Object.Instantiate(ability); copy.name = ability.name;
            try
            {
                // Solve with the same runtime clock so unchanged preparation/recovery contributions remain intact.
                SetBaseReleaseSeconds(copy, .1f); float low = CueDamageInterval(copy, 1f);
                SetBaseReleaseSeconds(copy, 1.1f); float slope = CueDamageInterval(copy, 1f) - low;
                RequireFrame(slope > .000001f, "Selected cue/hit interval does not include the stroke: " + id);
                proposed[i] = .1f + (target - low) / slope;
                SetBaseReleaseSeconds(copy, proposed[i]); VerifyBaseTempo(copy, row, fields);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
        foreach (var old in before)
        {
            string path = (string)old["path"], backup = Path.Combine(directory, "Recovery", path);
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            foreach (string suffix in new[] { "", ".meta" })
            {
                string source = Path.Combine(Project, path) + suffix, destination = backup + suffix;
                if (File.Exists(destination)) RequireFrame(MonsterBlenderParryR6Builder.Hash(source) == MonsterBlenderParryR6Builder.Hash(destination), "Recovery differs: " + path);
                else File.Copy(source, destination, false);
            }
        }
        File.WriteAllText(Path.Combine(directory, "snapshot-before.json"), new JObject { ["utc"] = DateTime.UtcNow, ["records"] = before }.ToString());
        var saved = new JArray();
        try
        {
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i]; var ability = abilities[i]; string path = (string)row["abilityPath"];
                Undo.RecordObject(ability, "몬스터 공격속도 1의 기본 모션 템포 조정");
                SetBaseReleaseSeconds(ability, proposed[i]); EditorUtility.SetDirty(ability); AssetDatabase.SaveAssetIfDirty(ability);
                VerifyBaseTempo(ability, row, (JObject)before[i]["fields"]);
                RequireFrame(AssetDatabase.AssetPathToGUID(path) == (string)before[i]["guid"]
                    && MonsterBlenderParryR6Builder.Hash(Path.Combine(Project, path) + ".meta") == (string)before[i]["metaHash"], "GUID/meta changed.");
                saved.Add(BaseTempoRecord(ability, row, before[i], (float)before[i]["intervalSeconds"]));
            }
        }
        catch (Exception error)
        {
            for (int i = 0; i < abilities.Length; i++)
            {
                EditorJsonUtility.FromJsonOverwrite(before[i]["fields"].ToString(), abilities[i]);
                EditorUtility.SetDirty(abilities[i]); AssetDatabase.SaveAssetIfDirty(abilities[i]);
            }
            File.WriteAllText(receipt, new JObject { ["status"] = "FAIL_NATIVE_ROLLBACK", ["error"] = error.ToString() }.ToString()); throw;
        }
        File.WriteAllText(receipt, new JObject { ["status"] = "PASS_NATIVE_BASE_TEMPO_CALIBRATION", ["utc"] = DateTime.UtcNow,
            ["actors"] = saved.Count, ["damageHits"] = saved.Sum(r => (int)r["hitCount"]), ["baseAttackSpeedMultiplier"] = 1f,
            ["changedField"] = "releaseDuration", ["normalizedFramesPreserved"] = true, ["preparationAndRecoveryPreserved"] = true,
            ["guidAndUnrelatedFieldsPreserved"] = true, ["attackSpeedsVerified"] = new JArray(.75f, 1f, 1.5f), ["saved"] = saved }.ToString());
        return "SAVED_BASE_TEMPO_" + saved.Count;
    }
}
