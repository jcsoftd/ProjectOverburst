using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MeasuredClawBatchVerifier
{
    const string Source = "Assets/ProjectOverburst/Resources/Enemies/Themes/Abilities/V3_Deinodonte_2HitComboClawsAttack.asset";
    static string Hash(string path)
    {
        using (var hash = SHA256.Create()) using (var input = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(input));
    }
    static JObject Row(EnemyAbilityDefinition ability)
    {
        var p = ability.WeakAttackExecution;
        var geometry = JObject.Parse(EditorJsonUtility.ToJson(p))["MonoBehaviour"]["contactGeometry"].DeepClone();
        foreach (var capsule in geometry.SelectTokens("$..capsules[*]"))
            foreach (string name in new[] { "a", "b" })
            { var vector = capsule[name]; capsule[name] = new JArray(vector["x"], vector["y"], vector["z"]); }
        var times = new JArray(Enumerable.Range(0, ability.ReleaseCount).Select(i => ability.GetHitNormalizedTime(i * ability.ProjectilesPerRelease)));
        return new JObject { ["cardKey"] = "runtime:OwnedFixture", ["selectionKey"] = p.SelectionKey,
            ["previousStationaryStartRange"] = p.StationaryStartRange, ["stationaryStartRange"] = p.StationaryStartRange + .01f,
            ["contactGeometry"] = new JObject { ["phases"] = geometry }, ["originalHitNormalizedTimes"] = times, ["hitNormalizedTimes"] = times.DeepClone() };
    }
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/')); Folder(parent); AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }
    public static string Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || BuildPipeline.isBuildingPlayer || IsolatedSavePlayGuard.RequiresAccountChoice
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))) throw new InvalidOperationException("Idle returned Editor required.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        if (Directory.Exists(output)) throw new InvalidOperationException("Fresh verification output required.");
        Directory.CreateDirectory(output);
        var checks = new JArray(); Exception failure = null;
        string fixture = MonsterPresentationCalibrationBuilder.MeasuredFixtureRoot + "Run" + DateTime.UtcNow.Ticks;
        var source = AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(Source);
        if (source == null || source.ReleaseCount != 2) throw new InvalidOperationException("Saved two-release source required.");
        string sourceProfile = AssetDatabase.GetAssetPath(source.WeakAttackExecution);
        var products = new[] { Source, Source + ".meta", sourceProfile, sourceProfile + ".meta" };
        var productHashes = products.Select(Hash).ToArray();
        var productState = new[] { MonsterPresentationCalibrationBuilder.MeasuredState(source), MonsterPresentationCalibrationBuilder.MeasuredState(source.WeakAttackExecution) };
        var abilities = new EnemyAbilityDefinition[2]; var originals = new List<Object>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        Object[] Assets() => abilities.SelectMany(a => new Object[] { a.WeakAttackExecution, a }).ToArray();
        string[] States() => Assets().Select(MonsterPresentationCalibrationBuilder.MeasuredState).ToArray();
        string[] Disk() => Assets().SelectMany(a => new[] { AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(a) + ".meta" }).Select(Hash).ToArray();
        void Restore()
        {
            var targets = Assets();
            for (int i = 0; i < targets.Length; i++) { EditorUtility.CopySerialized(originals[i], targets[i]); EditorUtility.SetDirty(targets[i]); AssetDatabase.SaveAssetIfDirty(targets[i]); }
        }
        void Reject(string name, JArray rows, EnemyAbilityDefinition[] selected)
        {
            string[] beforeState = States(), beforeDisk = Disk(); string destination = Path.Combine(output, name); bool rejected = false;
            try { MonsterPresentationCalibrationBuilder.VerifyMeasuredFixtures(rows, selected, destination); } catch (Exception) { rejected = true; }
            Check(rejected && States().SequenceEqual(beforeState) && Disk().SequenceEqual(beforeDisk)
                && Assets().All(a => !EditorUtility.IsDirty(a)) && !File.Exists(Path.Combine(destination, "applied.json")), name + " rejects before native or disk mutation");
        }
        try
        {
            Folder(fixture);
            for (int i = 0; i < abilities.Length; i++)
            {
                string profilePath = fixture + "/Profile" + i + ".asset", abilityPath = fixture + "/Ability" + i + ".asset";
                if (!AssetDatabase.CopyAsset(sourceProfile, profilePath) || !AssetDatabase.CopyAsset(Source, abilityPath)) throw new IOException("Native fixture copy failed.");
                abilities[i] = AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(abilityPath);
                var so = new SerializedObject(abilities[i]); so.FindProperty("weakAttackExecution").objectReferenceValue = AssetDatabase.LoadAssetAtPath<EnemyWeakAttackExecutionProfile>(profilePath);
                so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(abilities[i]); AssetDatabase.SaveAssetIfDirty(abilities[i]);
            }
            foreach (var asset in Assets()) { var copy = Object.Instantiate(asset); copy.name = asset.name; copy.hideFlags = asset.hideFlags; originals.Add(copy); }
            var bad = Row(abilities[0]); bad["originalHitNormalizedTimes"] = new JArray(abilities[0].HitNormalizedTime);
            Reject("InvalidCount", new JArray(bad), new[] { abilities[0] });
            var later = Row(abilities[1]); later["originalHitNormalizedTimes"] = new JArray(abilities[1].HitNormalizedTime);
            Reject("LaterInvalid", new JArray(Row(abilities[0]), later), abilities);
            var invalid = Row(abilities[0]); invalid["hitNormalizedTimes"][0] = .05f;
            Reject("InvalidFinalCandidate", new JArray(invalid), new[] { abilities[0] });
            Reject("DuplicateWriter", new JArray(Row(abilities[0]), Row(abilities[0])), new[] { abilities[0], abilities[0] });
            var stale = Row(abilities[0]); stale["previousStationaryStartRange"] = abilities[0].WeakAttackExecution.StationaryStartRange + 1;
            Reject("StaleBaseline", new JArray(stale), new[] { abilities[0] });
            var nan = Row(abilities[0]); nan["stationaryStartRange"] = float.NaN;
            Reject("NonFinite", new JArray(nan), new[] { abilities[0] });
            var legacy = Row(abilities[0]); legacy.Remove("originalHitNormalizedTimes"); legacy["hitNormalizedTimes"] = new JArray(.05f);
            MonsterPresentationCalibrationBuilder.VerifyMeasuredFixtures(new JArray(legacy), new[] { abilities[0] }, Path.Combine(output, "Legacy"));
            Check(abilities[0].ReleaseCount == 2 && Mathf.Approximately(abilities[0].GetHitNormalizedTime(1), source.GetHitNormalizedTime(1)), "Legacy row preserves saved strike times"); Restore();
            var baseline = States(); int saves = 0;
            string saveFailure = Path.Combine(output, "SecondSaveFailure"); bool threw = false;
            try { MonsterPresentationCalibrationBuilder.VerifyMeasuredFixtures(new JArray(Row(abilities[0]), Row(abilities[1])), abilities, saveFailure,
                (stage, asset) => { if (stage == "before-save" && ++saves == 2) throw new IOException("Owned second native save failure"); }); }
            catch (IOException) { threw = true; }
            Check(threw && saves == 2 && States().SequenceEqual(baseline) && Assets().All(a => !EditorUtility.IsDirty(a))
                && !File.Exists(Path.Combine(saveFailure, "applied.json")), "Second save failure restores all touched native data and GUIDs");
            string incomplete = Path.Combine(output, "RollbackContinues"); saves = 0;
            try { MonsterPresentationCalibrationBuilder.VerifyMeasuredFixtures(new JArray(Row(abilities[0]), Row(abilities[1])), abilities, incomplete,
                (stage, asset) => { if (stage == "before-save" && ++saves == 3) throw new IOException("Owned third save failure");
                    if (stage == "before-rollback" && asset == abilities[1].WeakAttackExecution) throw new IOException("Owned one rollback failure"); }); }
            catch (IOException) { }
            var rollback = (JArray)JObject.Parse(File.ReadAllText(Path.Combine(incomplete, "failure.json")))["rollback"];
            Check(rollback.Any(r => (string)r["status"] == "RESTORE_INCOMPLETE") && rollback.Count(r => (string)r["status"] == "RESTORED_NATIVE") == 2,
                "One rollback failure is recorded and remaining touched assets still restore"); Restore();
            string conflict = Path.Combine(output, "ForeignConflict"); saves = 0;
            try { MonsterPresentationCalibrationBuilder.VerifyMeasuredFixtures(new JArray(Row(abilities[0])), new[] { abilities[0] }, conflict,
                (stage, asset) => { if (stage == "before-save" && ++saves == 2) { var so = new SerializedObject(asset); so.FindProperty("range").floatValue += .123f; so.ApplyModifiedPropertiesWithoutUndo(); throw new IOException("Owned simulated foreign change"); } }); }
            catch (IOException) { }
            Check(JObject.Parse(File.ReadAllText(Path.Combine(conflict, "failure.json")))["rollback"].Any(r => (string)r["status"] == "RESTORE_INCOMPLETE"), "Unexpected concurrent native value is preserved and reported"); Restore();
            string pending = Path.Combine(output, "ConfirmationPending");
            try { MonsterPresentationCalibrationBuilder.VerifyMeasuredFixtures(new JArray(Row(abilities[0])), new[] { abilities[0] }, pending,
                (stage, asset) => { if (stage == "before-verify") throw new IOException("Owned unavailable verification"); }); }
            catch (Exception) { }
            Check((string)JObject.Parse(File.ReadAllText(Path.Combine(pending, "failure.json")))["status"] == "CONFIRMATION_PENDING"
                && !File.Exists(Path.Combine(pending, "applied.json")), "Unavailable confirmation preserves saved candidates without blind rollback");
            var pendingDisk = Disk(); MonsterPresentationCalibrationBuilder.VerifyAndPublishMeasuredClawContacts(pending);
            Check(Disk().SequenceEqual(pendingDisk) && File.Exists(Path.Combine(pending, "applied.json")), "Read-only confirmation retry publishes without another Save"); Restore();
            string report = Path.Combine(output, "ReportFailure");
            try { MonsterPresentationCalibrationBuilder.VerifyMeasuredFixtures(new JArray(Row(abilities[0])), new[] { abilities[0] }, report,
                (stage, asset) => { if (stage == "before-report") Directory.CreateDirectory(Path.Combine(report, "applied.json")); }); }
            catch (Exception) { }
            Check((string)JObject.Parse(File.ReadAllText(Path.Combine(report, "failure.json")))["status"] == "ASSETS_VERIFIED_REPORT_FAILED", "Report failure retains native-verified assets");
            var reportDisk = Disk(); Directory.Delete(Path.Combine(report, "applied.json")); MonsterPresentationCalibrationBuilder.VerifyAndPublishMeasuredClawContacts(report);
            Check(Disk().SequenceEqual(reportDisk) && File.Exists(Path.Combine(report, "applied.json")), "Report-only recovery writes no asset"); Restore();
            var grouping = new SerializedObject(abilities[0]);
            grouping.FindProperty("executionMode").enumValueIndex = (int)EnemyAbilityExecutionMode.Projectile;
            grouping.FindProperty("projectilesPerRelease").intValue = 2; grouping.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(abilities[0]); AssetDatabase.SaveAssetIfDirty(abilities[0]);
            Check(abilities[0].ProjectilesPerRelease == 2 && abilities[0].GetHitNormalizedTime(1) == abilities[0].HitNormalizedTime, "Grouped projectile fixture uses expanded hit indexing");
            MonsterPresentationCalibrationBuilder.VerifyMeasuredFixtures(new JArray(Row(abilities[0])), new[] { abilities[0] }, Path.Combine(output, "GroupedRelease"));
            Check(abilities[0].ReleaseCount == 2 && abilities[0].ProjectilesPerRelease == 2
                && Mathf.Approximately(abilities[0].GetHitNormalizedTime(2), source.GetHitNormalizedTime(1)), "Grouped release baseline reads each release instead of repeating first hit"); Restore();
            string valid = Path.Combine(output, "ValidBatch"); float beforeRange = abilities[0].WeakAttackExecution.StationaryStartRange;
            MonsterPresentationCalibrationBuilder.VerifyMeasuredFixtures(new JArray(Row(abilities[0]), Row(abilities[1])), abilities, valid);
            Check(abilities.All(a => a.IsValid && Mathf.Approximately(a.Range, a.WeakAttackExecution.ApproachStartRange))
                && Mathf.Approximately(abilities[0].WeakAttackExecution.StationaryStartRange, beforeRange + .01f)
                && JArray.Parse(File.ReadAllText(Path.Combine(valid, "applied.json"))).Count == 2, "Valid two-row/two-hit batch natively saves final profile and ability together");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            foreach (var copy in originals) Object.DestroyImmediate(copy);
            if (AssetDatabase.IsValidFolder(fixture)) AssetDatabase.DeleteAsset(fixture);
            string fixtureRoot = MonsterPresentationCalibrationBuilder.MeasuredFixtureRoot.TrimEnd('/');
            if (Directory.Exists(fixtureRoot) && !Directory.EnumerateFileSystemEntries(fixtureRoot).Any()) AssetDatabase.DeleteAsset(fixtureRoot);
        }
        bool productPreserved = products.Select(Hash).SequenceEqual(productHashes)
            && MonsterPresentationCalibrationBuilder.MeasuredState(source) == productState[0]
            && MonsterPresentationCalibrationBuilder.MeasuredState(source.WeakAttackExecution) == productState[1];
        var result = new JObject { ["status"] = failure == null && productPreserved ? "PASS_SCOPED" : "FAIL", ["checks"] = checks,
            ["productAssetsPreserved"] = productPreserved, ["fixturesRemoved"] = !AssetDatabase.IsValidFolder(fixture), ["error"] = failure?.ToString() };
        File.WriteAllText(Path.Combine(output, "measured-batch.json"), result.ToString());
        if (failure != null) throw failure;
        if (!productPreserved) throw new InvalidOperationException("Product measured assets changed during verification.");
        return "PASS_SCOPED measured batch: " + checks.Count;
    }
}
