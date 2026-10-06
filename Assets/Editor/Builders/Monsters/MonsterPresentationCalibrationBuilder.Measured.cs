using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MonsterPresentationCalibrationBuilder
{
    const string MeasuredThemeRoot = "Assets/ProjectOverburst/Resources/Enemies/Themes/";
    internal const string MeasuredFixtureRoot = "Assets/Editor/Testers/Monsters/CalibrationFixtures/";
    sealed class MeasuredAsset
    {
        public Object Original, Before, Candidate;
        public string Path, Guid, DiskHash, SavedHash, MetaHash, BeforeState, ExpectedState;
    }
    sealed class MeasuredReadbackUnavailable : Exception
    {
        public MeasuredReadbackUnavailable(string message, Exception inner = null) : base(message, inner) { }
    }
    static void MeasuredIdle()
    {
        if (BuildPipeline.isBuildingPlayer || EditorApplication.isPlayingOrWillChangePlaymode
            || EditorApplication.isCompiling || EditorApplication.isUpdating || IsolatedSavePlayGuard.RequiresAccountChoice
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Idle unoccupied Editor and resolved account required.");
    }
    static string MeasuredHash(string path)
    {
        using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    // Compare native serialized data with stable asset references, even after an import.
    internal static string MeasuredState(Object asset)
    {
        var json = JObject.Parse(EditorJsonUtility.ToJson(asset));
        foreach (var reference in json.Descendants().OfType<JObject>().Where(j => j["instanceID"] != null).ToArray())
        {
            int id = (int)reference["instanceID"];
            var value = EditorUtility.InstanceIDToObject(id);
            reference.RemoveAll();
            if (id == 0) reference["null"] = true;
            else if (value != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId))
            { reference["guid"] = guid; reference["localId"] = localId; }
            else throw new InvalidOperationException("Non-persistent or missing serialized reference in measured asset: " + asset.name);
        }
        return json.ToString(Newtonsoft.Json.Formatting.None);
    }
    static Object MeasuredClone(Object source)
    {
        var clone = Object.Instantiate(source); clone.name = source.name; clone.hideFlags = source.hideFlags; return clone;
    }
    static MeasuredAsset MeasuredCapture(Object source, string root)
    {
        string path = AssetDatabase.GetAssetPath(source);
        if (source == null || !path.StartsWith(root, StringComparison.Ordinal) || !File.Exists(path) || !File.Exists(path + ".meta"))
            throw new InvalidOperationException("Saved owned measured asset required: " + path);
        if (EditorUtility.IsDirty(source)) throw new InvalidOperationException("Unsaved measured asset must be preserved: " + path);
        return new MeasuredAsset { Original = source, Path = path, Guid = AssetDatabase.AssetPathToGUID(path),
            DiskHash = MeasuredHash(path), MetaHash = MeasuredHash(path + ".meta"), BeforeState = MeasuredState(source) };
    }
    static float MeasuredNumber(JToken value, string name)
    {
        if (value == null || value.Type != JTokenType.Float && value.Type != JTokenType.Integer)
            throw new ArgumentException("Numeric measured field required: " + name);
        float number = (float)value;
        if (float.IsNaN(number) || float.IsInfinity(number)) throw new ArgumentException("Finite measured field required: " + name);
        return number;
    }
    static JArray MeasuredArray(JToken value, int count, string name)
    {
        if (!(value is JArray array) || count >= 0 && array.Count != count) throw new ArgumentException("Measured array count mismatch: " + name);
        return array;
    }
    static Vector2 MeasuredWindow(JToken value, string name)
    {
        var array = MeasuredArray(value, 2, name);
        var window = new Vector2(MeasuredNumber(array[0], name), MeasuredNumber(array[1], name));
        if (window.x < 0 || window.y > 1 || window.y <= window.x) throw new ArgumentException("Invalid measured window: " + name);
        return window;
    }
    static void MeasuredMatch(float actual, JToken prior, float tolerance, string name)
    {
        if (Mathf.Abs(actual - MeasuredNumber(prior, name)) > tolerance) throw new InvalidOperationException(name + " changed after measurement.");
    }
    static void MeasuredPrepare(JObject row, EnemyAbilityDefinition original, EnemyWeakAttackExecutionProfile profile,
        EnemyAbilityDefinition abilityClone, EnemyWeakAttackExecutionProfile profileClone)
    {
        if (original.IsTelegraphedStrongAttack) throw new ArgumentException("Only saved weak attacks may be recalibrated.");
        if (row["previousStationaryStartRange"] != null) MeasuredMatch(profile.StationaryStartRange, row["previousStationaryStartRange"], .00001f, "Start range");
        var settings = new SerializedObject(abilityClone);
        if (row["originalHitNormalizedTimes"] != null)
        {
            var previous = MeasuredArray(row["originalHitNormalizedTimes"], original.ReleaseCount, "original strike times");
            var times = MeasuredArray(row["hitNormalizedTimes"], original.ReleaseCount, "result strike times");
            float last = 0f;
            for (int release = 0; release < previous.Count; release++)
            {
                MeasuredMatch(original.GetHitNormalizedTime(release * original.ProjectilesPerRelease), previous[release], .000001f, "Strike time");
                float time = MeasuredNumber(times[release], "strike time");
                if (time < .05f || time > .95f || release > 0 && time <= last) throw new ArgumentException("Strike times must remain ordered within 0.05..0.95.");
                last = time;
            }
            settings.FindProperty("hitNormalizedTime").floatValue = (float)times[0];
            var extra = settings.FindProperty("additionalHitNormalizedTimes"); extra.arraySize = times.Count - 1;
            for (int release = 1; release < times.Count; release++) extra.GetArrayElementAtIndex(release - 1).floatValue = (float)times[release];
        }
        // Legacy rows without an original strike baseline preserve all saved strike times.
        var so = new SerializedObject(profileClone); var groups = so.FindProperty("contactGeometry");
        var phases = MeasuredArray(row["contactGeometry"]?["phases"], groups.arraySize, "contact phases");
        bool replace = (bool?)row["replaceContactWindows"] == true;
        JArray priorWindows = null, windows = null;
        if (replace)
        {
            priorWindows = MeasuredArray(row["originalContactWindowsNormalized"], profile.ContactWindowCount, "original windows");
            windows = MeasuredArray(row["contactWindowsNormalized"], profile.ContactWindowCount, "result windows");
            for (int phase = 0; phase < profile.ContactWindowCount; phase++)
            {
                if (!profile.TryGetContactWindow(phase, out var prior)) throw new ArgumentException("Missing saved contact window.");
                var baseline = MeasuredWindow(priorWindows[phase], "original contact window");
                if ((prior - baseline).sqrMagnitude > .0000000001f) throw new InvalidOperationException("Contact window changed after measurement.");
                so.FindProperty("contactWindows").GetArrayElementAtIndex(phase).vector2Value = MeasuredWindow(windows[phase], "contact window");
            }
        }
        for (int phase = 0; phase < phases.Count; phase++)
        {
            var frames = groups.GetArrayElementAtIndex(phase).FindPropertyRelative("frames");
            var authoredFrames = MeasuredArray(phases[phase]["frames"], replace ? -1 : frames.arraySize, "contact frames");
            if (replace) frames.arraySize = authoredFrames.Count;
            for (int index = 0; index < authoredFrames.Count; index++)
            {
                var frame = frames.GetArrayElementAtIndex(index); var authored = authoredFrames[index];
                float time = MeasuredNumber(authored["normalizedTime"], "contact sample time");
                if (!replace) MeasuredMatch(frame.FindPropertyRelative("normalizedTime").floatValue, authored["normalizedTime"], .000001f, "Contact sampling time");
                else frame.FindPropertyRelative("normalizedTime").floatValue = time;
                var shapes = MeasuredArray(authored["capsules"], -1, "contact capsules");
                var capsules = frame.FindPropertyRelative("capsules"); capsules.arraySize = shapes.Count;
                for (int shapeIndex = 0; shapeIndex < shapes.Count; shapeIndex++)
                {
                    var shape = capsules.GetArrayElementAtIndex(shapeIndex);
                    foreach (string name in new[] { "a", "b" })
                    {
                        var vector = MeasuredArray(shapes[shapeIndex][name], 3, name);
                        shape.FindPropertyRelative(name).vector3Value = new Vector3(MeasuredNumber(vector[0], name), MeasuredNumber(vector[1], name), MeasuredNumber(vector[2], name));
                    }
                    float radius = MeasuredNumber(shapes[shapeIndex]["radius"], "capsule radius");
                    if (radius <= 0) throw new ArgumentException("Positive capsule radius required.");
                    shape.FindPropertyRelative("radius").floatValue = radius;
                }
            }
        }
        if (row["advanceWindow"] != null)
        {
            var prior = MeasuredWindow(row["previousAdvanceWindow"], "previous advance window");
            if (!profile.UsesAdvance || (profile.AdvanceWindow - prior).sqrMagnitude > .000000000001f)
                throw new InvalidOperationException("Advance window changed after measurement.");
            so.FindProperty("advanceWindow").vector2Value = MeasuredWindow(row["advanceWindow"], "advance window");
        }
        if (row["stationaryStartRange"] != null)
        {
            float range = MeasuredNumber(row["stationaryStartRange"], "stationary range");
            if (range <= 0) throw new ArgumentException("Positive stationary range required.");
            so.FindProperty("stationaryStartRange").floatValue = range;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        if (!profileClone.ValidateAuthoring(out string reason)) throw new ArgumentException(reason);
        settings.FindProperty("weakAttackExecution").objectReferenceValue = profileClone;
        if (row["stationaryStartRange"] != null) settings.FindProperty("range").floatValue = profileClone.ApproachStartRange;
        settings.ApplyModifiedPropertiesWithoutUndo();
        if (!abilityClone.IsValid) throw new ArgumentException("Invalid final measured weak attack or strike outside contact window.");
        // Persistent candidates must reference the existing profile GUID, never a temporary clone.
        settings.FindProperty("weakAttackExecution").objectReferenceValue = profile; settings.ApplyModifiedPropertiesWithoutUndo();
    }
    static void MeasuredVerifyAsset(MeasuredAsset asset)
    {
        Object reloaded;
        try
        {
            AssetDatabase.ImportAsset(asset.Path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            reloaded = AssetDatabase.LoadAssetAtPath(asset.Path, asset.Original.GetType());
            if (reloaded == null) throw new IOException("Native load unavailable: " + asset.Path);
        }
        catch (Exception error) { throw new MeasuredReadbackUnavailable("Saved measured asset needs read-only confirmation: " + asset.Path, error); }
        asset.Original = reloaded;
        if (AssetDatabase.AssetPathToGUID(asset.Path) != asset.Guid || MeasuredHash(asset.Path + ".meta") != asset.MetaHash
            || MeasuredState(reloaded) != asset.ExpectedState || EditorUtility.IsDirty(reloaded))
            throw new InvalidOperationException("Saved measured asset differs from validated candidate: " + asset.Path);
    }
    static string MeasuredApply(JArray rows, Func<int, JObject, EnemyAbilityDefinition> resolve,
        string outputDirectory, string root, Action<string, Object> fault = null)
    {
        MeasuredIdle();
        string output = Path.GetFullPath(outputDirectory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Fresh private output required.");
        Directory.CreateDirectory(output);
        var assets = new List<MeasuredAsset>(); var touched = new List<MeasuredAsset>(); var results = new JArray();
        var paths = new HashSet<string>(StringComparer.Ordinal); bool verified = false;
        try
        {
            if (rows.Count == 0) throw new ArgumentException("Measured batch is empty.");
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i] as JObject ?? throw new ArgumentException("Measured row required.");
                var ability = resolve(i, row);
                if (ability == null || ability.WeakAttackExecution == null || ability.WeakAttackExecution.SelectionKey != (string)row["selectionKey"])
                    throw new ArgumentException("Saved measured ability/profile selection required.");
                var profile = ability.WeakAttackExecution;
                var p = MeasuredCapture(profile, root); assets.Add(p);
                var a = MeasuredCapture(ability, root); assets.Add(a);
                if (!paths.Add(p.Path) || !paths.Add(a.Path)) throw new ArgumentException("Duplicate measured asset writer.");
                p.Before = MeasuredClone(profile); p.Candidate = MeasuredClone(profile);
                a.Before = MeasuredClone(ability); a.Candidate = MeasuredClone(ability);
                MeasuredPrepare(row, ability, profile, (EnemyAbilityDefinition)a.Candidate, (EnemyWeakAttackExecutionProfile)p.Candidate);
                p.ExpectedState = MeasuredState(p.Candidate); a.ExpectedState = MeasuredState(a.Candidate);
                results.Add(new JObject { ["id"] = ((string)row["cardKey"])?.Substring("runtime:".Length), ["selectionKey"] = profile.SelectionKey,
                    ["path"] = p.Path, ["basis"] = "Native skinned claw surface at saved actor scale; original joint capsules retained" });
            }
            foreach (var asset in assets)
            {
                foreach (string suffix in new[] { "", ".meta" })
                {
                    string backup = Path.Combine(output, "Backup", asset.Path + suffix);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(asset.Path + suffix, backup, false);
                    if (MeasuredHash(backup) != (suffix == "" ? asset.DiskHash : asset.MetaHash)) throw new IOException("Measured backup mismatch.");
                }
            }
            var prepared = new JObject { ["input"] = rows.DeepClone(), ["results"] = results.DeepClone(), ["assets"] = new JArray(assets.Select(a => new JObject
                { ["path"] = a.Path, ["guid"] = a.Guid, ["diskHash"] = a.DiskHash, ["metaHash"] = a.MetaHash,
                    ["before"] = JObject.Parse(a.BeforeState), ["expected"] = JObject.Parse(a.ExpectedState) })) };
            File.WriteAllText(Path.Combine(output, "prepared.json"), prepared.ToString());
            MeasuredIdle();
            foreach (var asset in assets)
                if (EditorUtility.IsDirty(asset.Original) || MeasuredState(asset.Original) != asset.BeforeState
                    || MeasuredHash(asset.Path) != asset.DiskHash || MeasuredHash(asset.Path + ".meta") != asset.MetaHash
                    || AssetDatabase.AssetPathToGUID(asset.Path) != asset.Guid)
                    throw new InvalidOperationException("Measured baseline changed before first write: " + asset.Path);
            foreach (var asset in assets.Where(a => a.BeforeState != a.ExpectedState))
            {
                touched.Add(asset); // Record ownership before the first native mutation.
                EditorUtility.CopySerialized(asset.Candidate, asset.Original); EditorUtility.SetDirty(asset.Original);
                fault?.Invoke("before-save", asset.Original); AssetDatabase.SaveAssetIfDirty(asset.Original);
                if (EditorUtility.IsDirty(asset.Original))
                    throw new IOException("Measured save did not persist the changed asset: " + asset.Path);
                try { asset.SavedHash = MeasuredHash(asset.Path); }
                catch (IOException error) { throw new MeasuredReadbackUnavailable("Saved measured bytes could not be confirmed: " + asset.Path, error); }
                if (asset.SavedHash == asset.DiskHash) throw new IOException("Measured save did not persist changed bytes: " + asset.Path);
            }
            try { fault?.Invoke("before-verify", null); }
            catch (Exception error) { throw new MeasuredReadbackUnavailable("Native confirmation was unavailable; Apply must not be replayed.", error); }
            foreach (var asset in assets) MeasuredVerifyAsset(asset);
            verified = true;
            fault?.Invoke("before-report", null);
            File.WriteAllText(Path.Combine(output, "applied.json"), results.ToString());
            return "Saved measured claw contacts: " + results.Count;
        }
        catch (Exception error)
        {
            var rollback = new JArray();
            bool pending = error is MeasuredReadbackUnavailable;
            if (!verified && !pending)
                foreach (var asset in touched.AsEnumerable().Reverse())
                {
                    try
                    {
                        string current = MeasuredState(asset.Original);
                        string disk = MeasuredHash(asset.Path);
                        if (AssetDatabase.AssetPathToGUID(asset.Path) != asset.Guid || MeasuredHash(asset.Path + ".meta") != asset.MetaHash
                            || disk != asset.DiskHash && disk != asset.SavedHash
                            || current != asset.BeforeState && current != asset.ExpectedState)
                            throw new InvalidOperationException("Foreign or unknown measured state preserved.");
                        fault?.Invoke("before-rollback", asset.Original);
                        EditorUtility.CopySerialized(asset.Before, asset.Original); EditorUtility.SetDirty(asset.Original); AssetDatabase.SaveAssetIfDirty(asset.Original);
                        string expected = asset.ExpectedState; asset.ExpectedState = asset.BeforeState;
                        try { MeasuredVerifyAsset(asset); } finally { asset.ExpectedState = expected; }
                        rollback.Add(new JObject { ["path"] = asset.Path, ["status"] = "RESTORED_NATIVE", ["exactBytes"] = MeasuredHash(asset.Path) == asset.DiskHash });
                    }
                    catch (Exception restoreError) { rollback.Add(new JObject { ["path"] = asset.Path, ["status"] = "RESTORE_INCOMPLETE", ["error"] = restoreError.ToString() }); }
                }
            var failed = new JObject { ["status"] = verified ? "ASSETS_VERIFIED_REPORT_FAILED" : pending ? "CONFIRMATION_PENDING" : "FAILED",
                ["error"] = error.ToString(), ["touched"] = new JArray(touched.Select(a => a.Path)), ["rollback"] = rollback };
            try { File.WriteAllText(Path.Combine(output, "failure.json"), failed.ToString()); }
            catch (Exception reportError) { Debug.LogError("Measured failure record unavailable: " + reportError); }
            throw;
        }
        finally
        {
            foreach (var asset in assets) { if (asset.Before != null) Object.DestroyImmediate(asset.Before); if (asset.Candidate != null) Object.DestroyImmediate(asset.Candidate); }
        }
    }
    static string ApplyMeasuredBatch(string authoringPath, string outputDirectory)
    {
        var rows = JObject.Parse(File.ReadAllText(authoringPath))["entries"] as JArray ?? throw new ArgumentException("Measured entries required.");
        return MeasuredApply(rows, (i, row) =>
        {
            string key = (string)row["cardKey"];
            if (key == null || !key.StartsWith("runtime:", StringComparison.Ordinal)) throw new ArgumentException("Runtime card key required.");
            string id = key.Substring("runtime:".Length);
            if (id.Length == 0 || id.IndexOfAny(new[] { '/', '\\', '.' }) >= 0) throw new ArgumentException("Owned definition ID required.");
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(MeasuredThemeRoot + "Definitions/" + id + ".asset");
            if (definition == null || definition.AbilitySet == null) throw new ArgumentException("Saved measured definition required.");
            return Enumerable.Range(0, definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility)
                .Single(a => a != null && a.WeakAttackExecution?.SelectionKey == (string)row["selectionKey"]);
        }, outputDirectory, MeasuredThemeRoot);
    }
    internal static string VerifyMeasuredFixtures(JArray rows, EnemyAbilityDefinition[] abilities, string output, Action<string, Object> fault = null)
        => MeasuredApply(rows, (i, row) => abilities[i], output, MeasuredFixtureRoot, fault);

    // Report-only recovery never applies or saves an asset again.
    public static string VerifyAndPublishMeasuredClawContacts(string outputDirectory)
    {
        MeasuredIdle();
        string output = IsolatedSavePlayGuard.ValidateDirectory(outputDirectory);
        var prepared = JObject.Parse(File.ReadAllText(Path.Combine(output, "prepared.json")));
        foreach (var token in prepared["assets"])
        {
            string path = (string)token["path"];
            if (!path.StartsWith(MeasuredThemeRoot, StringComparison.Ordinal) && !path.StartsWith(MeasuredFixtureRoot, StringComparison.Ordinal))
                throw new InvalidOperationException("Owned measured target required.");
            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (asset == null || EditorUtility.IsDirty(asset)) throw new InvalidOperationException("Saved clean measured target required.");
            MeasuredVerifyAsset(new MeasuredAsset { Original = asset, Path = path, Guid = (string)token["guid"], MetaHash = (string)token["metaHash"], ExpectedState = token["expected"].ToString(Newtonsoft.Json.Formatting.None) });
        }
        File.WriteAllText(Path.Combine(output, "applied.json"), prepared["results"].ToString());
        return "Verified and published existing measured assets; no Apply or Save performed.";
    }
}
