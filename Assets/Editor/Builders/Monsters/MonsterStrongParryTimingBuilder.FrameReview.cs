using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Overburst.EditorTools.MonsterTuner;
using UnityEditor;
using UnityEngine;

public static partial class MonsterStrongParryTimingBuilder
{
    static readonly string[] FrameTimingFields = {
        "preparationEndNormalized", "preparationDuration", "releaseDuration", "recoveryDuration",
        "motionReleaseReferenceNormalized", "motionRecoveryStartNormalized", "hitDelay", "hitNormalizedTime",
        "additionalHitNormalizedTimes", "parryMotionWindows", "firstStrikeOnlyParry"
    };

    public static string QueueApprovedFrames(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string directory = Output(outputRelative), queue = Path.Combine(directory, "apply-queue.json");
        if (File.Exists(Path.Combine(directory, "apply-result.json"))) throw new InvalidOperationException("Inspect the existing application receipt.");
        File.WriteAllText(queue, new JObject { ["status"] = "QUEUED", ["utc"] = DateTime.UtcNow }.ToString());
        double deadline = EditorApplication.timeSinceStartup + 60;
        EditorApplication.update += Run; AssemblyReloadEvents.beforeAssemblyReload += Cancel;
        return "APPROVED_FRAME_TIMING_QUEUED";
        void Detach() { EditorApplication.update -= Run; AssemblyReloadEvents.beforeAssemblyReload -= Cancel; }
        void Cancel() { Detach(); File.WriteAllText(queue, new JObject { ["status"] = "CANCELLED_RELOAD" }.ToString()); }
        void Run()
        {
            Detach();
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Application queue expired.");
                string result = ApplyApprovedFrames(outputRelative);
                File.WriteAllText(queue, new JObject { ["status"] = result, ["utc"] = DateTime.UtcNow }.ToString());
            }
            catch (Exception error) { File.WriteAllText(queue, new JObject { ["status"] = "FAIL", ["error"] = error.ToString() }.ToString()); }
        }
    }

    static JObject OutsideFrameTiming(JObject fields)
    {
        var result = (JObject)fields.DeepClone(); var body = (JObject)(result["MonoBehaviour"] ?? result);
        foreach (string field in FrameTimingFields) body.Remove(field);
        return result;
    }

    static void RequireFrame(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    static AnimationClip ReviewedRuntimeClip(EnemyDefinition definition, EnemyAbilityDefinition ability)
    {
        string trigger = ability.AnimatorTrigger;
        string state = trigger.StartsWith("Attack", StringComparison.Ordinal) ? "Attack_" + trigger.Substring(6) : trigger;
        var matches = MonsterTunerAnimationBindings.Read(definition.AnimationProfile)
            .Where(b => b.StatePath.EndsWith("." + state, StringComparison.Ordinal)).ToArray();
        RequireFrame(matches.Length == 1 && matches[0].Actual != null, "Ambiguous runtime attack: " + definition.EnemyId);
        return matches[0].Actual;
    }

    static void ConfigureApprovedFrames(EnemyAbilityDefinition ability, JToken row, JToken baseline)
    {
        // The export does not repeat clipDuration; the immutable frame dataset owns it.
        double duration = (double)baseline["clipDuration"];
        double prep = (double)baseline["originalStrikeSeconds"];
        var sourceHits = baseline["impactSourceSeconds"].Values<double>().ToArray();
        var tempo = row["proposalTempo"];
        var hits = row["selectedImpactNormalized"].Values<float>().ToArray();
        var serialized = new SerializedObject(ability);
        serialized.FindProperty("preparationEndNormalized").floatValue = (float)(prep / duration);
        serialized.FindProperty("preparationDuration").floatValue = (float)tempo["preparationSeconds"];
        serialized.FindProperty("releaseDuration").floatValue = (float)((sourceHits[0] - prep) / (double)tempo["strikeRate"]);
        serialized.FindProperty("recoveryDuration").floatValue = (float)tempo["recoverySeconds"];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        ability.ConfigureMotionPacing((float)(sourceHits[0] / duration), (float)(sourceHits.Last() / duration));
        serialized.Update();
        serialized.FindProperty("hitNormalizedTime").floatValue = hits[0];
        serialized.FindProperty("hitDelay").floatValue = (float)row["selectedImpactSourceSeconds"][0];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        ability.ConfigureAdditionalHits(hits.Skip(1).ToArray());
        ability.ConfigureFirstStrikeOnlyParry(true);
        ability.ConfigureParryMotionWindows(new Vector2((float)row["selectedCueNormalized"], hits[0]));
    }

    static double PreviewTime(double sourceSeconds, JToken row, JToken baseline)
    {
        double prep = (double)baseline["originalStrikeSeconds"], duration = (double)baseline["clipDuration"];
        double recoveryStart = baseline["impactSourceSeconds"].Values<double>().Last();
        var tempo = row["proposalTempo"];
        double preparation = (double)tempo["preparationSeconds"], rate = (double)tempo["strikeRate"];
        if (sourceSeconds <= prep) return preparation * sourceSeconds / prep;
        double release = (Math.Min(sourceSeconds, recoveryStart) - prep) / rate;
        return preparation + release + (sourceSeconds <= recoveryStart ? 0
            : (sourceSeconds - recoveryStart) * (double)tempo["recoverySeconds"] / (duration - recoveryStart));
    }

    static int VerifyApprovedFrames(EnemyAbilityDefinition ability, JToken row, JToken baseline)
    {
        RequireFrame(ability.IsValid && ability.UsesPacedTimeline && ability.IsParryable && ability.FirstStrikeOnlyParry
            && ability.ParryStrikeCount == 1, "Strong/first-only contract: " + row["id"]);
        float[] hits = row["selectedImpactNormalized"].Values<float>().ToArray();
        RequireFrame(ability.HitCount == hits.Length, "Hit count changed: " + row["id"]);
        for (int i = 0; i < hits.Length; i++) RequireFrame(Math.Abs(ability.GetHitNormalizedTime(i) - hits[i]) < .000001f, "Impact selection mismatch.");
        RequireFrame(ability.TryGetParryMotionWindow(0, out var window)
            && Math.Abs(window.x - (float)row["selectedCueNormalized"]) < .000001f
            && Math.Abs(window.y - hits[0]) < .000001f, "Parry selection mismatch.");
        int checks = 0; double duration = (double)baseline["clipDuration"], fps = (double)row["sourceFps"];
        foreach (float speed in new[] { .75f, 1f, 1.5f })
        {
            for (int frame = 0; frame <= (int)baseline["maximumFrame"]; frame++)
            {
                double source = Math.Min(duration, frame / fps);
                double expected = PreviewTime(source, row, baseline) / speed;
                RequireFrame(Math.Abs(ability.ResolvePacedTime((float)(source / duration), speed) - expected) < .00002,
                    "Motion clock differs from reviewed preview: " + row["id"] + " frame " + frame);
                checks++;
            }
            double expectedWindow = (double)row["windowAtProposalTempo"] / speed;
            RequireFrame(Math.Abs(ability.ResolvePacedTime(window.y, speed) - ability.ResolvePacedTime(window.x, speed) - expectedWindow) < .00002,
                "Cue/damage interval differs from export: " + row["id"]);
        }
        return checks;
    }

    public static string ApplyApprovedFrames(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string directory = Output(outputRelative), receipt = Path.Combine(directory, "apply-result.json");
        if (File.Exists(receipt)) throw new InvalidOperationException("Inspect the existing application receipt.");
        var input = JObject.Parse(File.ReadAllText(Path.Combine(directory, "Input/approved.json")));
        var dataset = JObject.Parse(File.ReadAllText(Path.Combine(directory, "Input/editor-baseline.json")));
        var rows = input["monsters"].ToArray();
        var baseline = dataset["rows"].ToDictionary(r => (string)r["id"]);
        var earlier = JArray.Parse(File.ReadAllText(Path.Combine(directory, "native-before.json"))).ToDictionary(r => (string)r["id"]);
        RequireFrame((int)input["schemaVersion"] == 2 && (string)input["purpose"] == "monster-first-parry-cue-frame-review"
            && (string)input["datasetKey"] == (string)dataset["datasetKey"] && (bool)input["firstStrikeOnlyParry"], "Wrong frame export.");
        RequireFrame(rows.Length == 35 && rows.Select(r => (string)r["id"]).Distinct().Count() == 35, "35 unique reviewed monsters required.");
        var abilities = rows.Select(r => Load((string)r["abilityPath"])).ToArray();
        var before = new JArray(); int checks = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i]; var ability = abilities[i]; string id = (string)row["id"], path = (string)row["abilityPath"];
            var original = baseline[id];
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)row["definitionPath"]);
            RequireFrame(definition != null && definition.EnemyId == id && !EditorUtility.IsDirty(definition)
                && Enumerable.Range(0, definition.AbilitySet.Count).Any(slot => definition.AbilitySet.GetAbility(slot) == ability), "Definition/ability mismatch: " + id);
            var clip = ReviewedRuntimeClip(definition, ability);
            double duration = (double)original["clipDuration"], fps = (double)row["sourceFps"];
            RequireFrame(AssetDatabase.AssetPathToGUID(path) == (string)row["abilityGuid"]
                && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)) == (string)row["clipGuid"]
                && Math.Abs(clip.frameRate - fps) < .001 && Math.Abs(clip.length - duration) < .00001
                && Math.Abs(ability.AttackAnimationDuration - duration) < .00001, "Runtime clip/GUID/FPS mismatch: " + id);
            var fields = Fields(ability);
            var currentBody = (JObject)(fields["MonoBehaviour"] ?? fields);
            var oldBody = (JObject)(earlier[id]["fields"]["MonoBehaviour"] ?? earlier[id]["fields"]);
            foreach (var field in oldBody.Properties()) RequireFrame(JToken.DeepEquals(currentBody[field.Name], field.Value), "Ability changed since snapshot: " + id + "/" + field.Name);
            RequireFrame(row["originalImpactSourceSeconds"].Values<double>().SequenceEqual(original["impactSourceSeconds"].Values<double>())
                && new[] { "preparationSeconds", "strikeRate", "recoverySeconds" }.All(key =>
                    (double)row["proposalTempo"][key] == (double)original["proposalTempo"][key]), "Motion/tempo baseline changed: " + id);
            var selected = row["selectedImpactSourceSeconds"].Values<double>().ToArray();
            var normalized = row["selectedImpactNormalized"].Values<double>().ToArray();
            RequireFrame(selected.Length == ability.HitCount && normalized.Length == selected.Length && selected.Length > 0, "Selected hit coverage.");
            double previous = -1;
            for (int hit = 0; hit < selected.Length; hit++)
            {
                RequireFrame(normalized[hit] >= .05 && normalized[hit] <= .95 && selected[hit] > previous
                    && Math.Abs(normalized[hit] - selected[hit] / duration) < .000001, "Invalid selected hit: " + id);
                previous = selected[hit];
            }
            double cue = (int)row["selectedCueFrame"] / fps;
            RequireFrame(cue >= 0 && cue < selected[0] && Math.Abs(cue / duration - (double)row["selectedCueNormalized"]) < .000001, "Invalid selected cue: " + id);
            before.Add(new JObject { ["id"] = id, ["path"] = path, ["fields"] = fields,
                ["guid"] = AssetDatabase.AssetPathToGUID(path), ["metaHash"] = MonsterBlenderParryR6Builder.Hash(Path.Combine(Project, path) + ".meta"),
                ["clipPath"] = AssetDatabase.GetAssetPath(clip), ["clipGuid"] = row["clipGuid"] });
            var copy = UnityEngine.Object.Instantiate(ability); copy.name = ability.name;
            try
            {
                ConfigureApprovedFrames(copy, row, original); checks += VerifyApprovedFrames(copy, row, original);
                RequireFrame(JToken.DeepEquals(OutsideFrameTiming(Fields(copy)), OutsideFrameTiming(fields)), "Unrelated attack field changed: " + id);
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
                if (File.Exists(destination)) RequireFrame(MonsterBlenderParryR6Builder.Hash(source) == MonsterBlenderParryR6Builder.Hash(destination), "Existing recovery file differs.");
                else File.Copy(source, destination, false);
            }
        }
        File.WriteAllText(Path.Combine(directory, "snapshot-before.json"), new JObject { ["utc"] = DateTime.UtcNow, ["records"] = before }.ToString());
        var saved = new JArray();
        try
        {
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i]; var ability = abilities[i]; string path = (string)row["abilityPath"], id = (string)row["id"];
                Undo.RecordObject(ability, "승인한 몬스터 패링·타격 프레임 적용");
                ConfigureApprovedFrames(ability, row, baseline[id]);
                EditorUtility.SetDirty(ability); AssetDatabase.SaveAssetIfDirty(ability);
                RequireFrame(AssetDatabase.AssetPathToGUID(path) == (string)before[i]["guid"]
                    && MonsterBlenderParryR6Builder.Hash(Path.Combine(Project, path) + ".meta") == (string)before[i]["metaHash"]
                    && JToken.DeepEquals(OutsideFrameTiming(Fields(ability)), OutsideFrameTiming((JObject)before[i]["fields"])), "Save preservation failed: " + id);
                VerifyApprovedFrames(ability, row, baseline[id]);
                ability.TryGetParryMotionWindow(0, out var window);
                float cueTime = ability.ResolvePacedTime(window.x, 1f);
                var hits = new JArray(); var afterCue = new JArray();
                for (int hit = 0; hit < ability.HitCount; hit++)
                {
                    float time = ability.ResolvePacedTime(ability.GetHitNormalizedTime(hit), 1f);
                    hits.Add(time); afterCue.Add(time - cueTime);
                }
                saved.Add(new JObject { ["id"] = id, ["name"] = row["name"], ["grade"] = row["grade"], ["themes"] = row["themes"],
                    ["abilityPath"] = path, ["guid"] = before[i]["guid"], ["clipGuid"] = before[i]["clipGuid"], ["fields"] = Fields(ability),
                    ["cueFrame"] = row["selectedCueFrame"], ["impactFrames"] = row["selectedImpactFrames"], ["hitCount"] = ability.HitCount,
                    ["cueSecondsAtSpeedOne"] = cueTime, ["impactSecondsAtSpeedOne"] = hits, ["afterCueSecondsAtSpeedOne"] = afterCue,
                    ["firstDamageAfterCueSeconds"] = afterCue[0], ["executionSecondsAtSpeedOne"] = ability.ResolveExecutionDuration(1f) });
            }
        }
        catch (Exception error)
        {
            for (int i = 0; i < abilities.Length; i++)
            {
                EditorJsonUtility.FromJsonOverwrite(before[i]["fields"].ToString(), abilities[i]);
                EditorUtility.SetDirty(abilities[i]); AssetDatabase.SaveAssetIfDirty(abilities[i]);
            }
            File.WriteAllText(receipt, new JObject { ["status"] = "FAIL_NATIVE_ROLLBACK", ["error"] = error.ToString() }.ToString());
            throw;
        }
        File.WriteAllText(receipt, new JObject { ["status"] = "PASS_NATIVE_APPROVED_FRAME_TIMING", ["utc"] = DateTime.UtcNow,
            ["inputSha256"] = MonsterBlenderParryR6Builder.Hash(Path.Combine(directory, "Input/approved.json")),
            ["actors"] = saved.Count, ["damageHits"] = saved.Sum(r => (int)r["hitCount"]), ["previewClockChecks"] = checks,
            ["attackSpeedsVerified"] = new JArray(.75f, 1f, 1.5f), ["tempo"] = "proposalTempo", ["firstStrikeOnlyParry"] = true,
            ["guidAndUnrelatedFieldsPreserved"] = true, ["saved"] = saved }.ToString());
        return "SAVED_APPROVED_FRAME_TIMING_" + saved.Count;
    }
}
