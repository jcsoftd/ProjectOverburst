using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Applies the ready subset of an approved roster. Held cards stay in the manifest.
public static class MonsterV3ThemeActivationBuilder
{
    public const int Revision = 3;
    const string Root = "Assets/ProjectOverburst/Resources/Enemies/Themes/";
    static string Project => Directory.GetParent(Application.dataPath).FullName;
    static string Workspace => Directory.GetParent(Project).FullName;
    static string Hash(string path)
    {
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
    static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorUtility.scriptCompilationFailed || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Idle Editor with unoccupied account required.");
    }
    static string Output(string path)
    {
        path = Path.GetFullPath(path);
        var allowed = Path.GetFullPath(Path.Combine(Workspace, "개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Private output required.");
        Directory.CreateDirectory(path); return path;
    }
    static void Save(Object asset) { if (EditorUtility.IsDirty(asset)) AssetDatabase.SaveAssetIfDirty(asset); }
    static T Create<T>(string path, HashSet<string> created) where T : ScriptableObject
    {
        var asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path); created.Add(path); return asset;
    }
    static EnemyThemeTier Tier(JToken row) => (EnemyThemeTier)Enum.Parse(typeof(EnemyThemeTier), (string)row["tier"], true);

    public static string Apply(string planPath, string outputDirectory)
    {
        RequireIdle(); outputDirectory = Output(outputDirectory);
        var plan = JObject.Parse(File.ReadAllText(planPath));
        if ((string)plan["schema"] != "overburst.v3.theme-activation.v1" || (bool?)plan["applyAudio"] != false)
            throw new ArgumentException("Approved no-new-audio theme plan required.");
        string approvedPath = (string)plan["approvedPath"];
        if (Hash(approvedPath) != (string)plan["approvedSha256"]) throw new InvalidOperationException("Approved input changed.");
        var approved = JObject.Parse(File.ReadAllText(approvedPath));
        foreach (var input in approved["inputs"])
            if (Hash((string)input["path"]) != (string)input["sha256"]) throw new InvalidOperationException("User selection changed.");
        var held = new HashSet<string>(plan["held"].Select(h => (string)h["cardKey"]));
        var themes = plan["themes"].OfType<JObject>().ToArray();
        if (themes.Length != 7 || themes.Select(t => (string)t["selectionThemeId"]).Distinct().Count() != 7)
            throw new ArgumentException("All seven themes must remain represented.");
        var definitions = new Dictionary<string, EnemyDefinition>();
        foreach (var t in themes)
        {
            var selection = approved["themes"].Single(s => (string)s["id"] == (string)t["selectionThemeId"]);
            var ready = t["entries"].OfType<JObject>().ToArray();
            var expected = selection["entries"].Where(e => (string)e["grade"] != "boss" && !held.Contains((string)e["key"])).Select(e => (string)e["key"]).ToArray();
            if (!ready.Select(r => (string)r["cardKey"]).SequenceEqual(expected)) throw new ArgumentException("Unapproved theme membership.");
            foreach (var row in ready)
            {
                var membership = selection["entries"].Single(e => (string)e["key"] == (string)row["cardKey"]);
                var card = approved["cards"][(string)row["cardKey"]];
                if ((string)membership["grade"] != (string)row["tier"] || (string)card["grade"] != (string)row["tier"]
                    || (string)card["status"] != "confirmed") throw new ArgumentException("Unresolved card included: " + row["cardKey"]);
                var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)row["definitionPath"]);
                if (definition?.IsValid != true || definition.DisplayName != (string)row["koreanDisplayName"])
                    throw new InvalidOperationException("Saved ready actor/name mismatch: " + row["cardKey"]);
                definitions.Add((string)row["cardKey"], definition);
            }
            if (Enum.GetValues(typeof(EnemyThemeTier)).Cast<EnemyThemeTier>().Any(tier => !ready.Any(r => Tier(r) == tier)))
                throw new InvalidOperationException("A ready theme must retain all three tiers.");
        }
        var backups = new Dictionary<string, string>();
        var nativeBefore = new Dictionary<Object, string>();
        foreach (var property in ((JObject)plan["expectedAssets"]).Properties())
        {
            string path = property.Name, source = Path.Combine(Project, path);
            if (Hash(source) != (string)property.Value) throw new InvalidOperationException("Owned asset changed: " + path);
            string backup = Path.Combine(outputDirectory, "Before", path);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(source, backup, true); backups.Add(source, backup);
            if (path.EndsWith(".meta", StringComparison.Ordinal)) continue;
            foreach (var asset in new[] { AssetDatabase.LoadMainAssetAtPath(path) })
            {
                if (asset == null) continue;
                if (EditorUtility.IsDirty(asset)) throw new InvalidOperationException("Unsaved owned asset: " + path);
                nativeBefore.Add(asset, EditorJsonUtility.ToJson(asset));
            }
        }
        foreach (string path in plan["newAssetPaths"].Values<string>())
            if (File.Exists(Path.Combine(Project, path)) || File.Exists(Path.Combine(Project, path + ".meta")))
                throw new InvalidOperationException("New path is occupied: " + path);
        var before = new JObject();
        foreach (var pair in nativeBefore)
            before[AssetDatabase.GetAssetPath(pair.Key)] = JObject.Parse(pair.Value);
        File.WriteAllText(Path.Combine(outputDirectory, "native-before.json"), before.ToString());
        var created = new HashSet<string>(); var results = new JArray();
        AssetDatabase.DisallowAutoRefresh();
        try
        {
            var catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalog>(Root + "Catalog.asset");
            var outside = Enumerable.Range(0, catalog.Count).Select(catalog.GetDefinition)
                .Where(d => d != null && !AssetDatabase.GetAssetPath(d).StartsWith(Root + "Definitions/", StringComparison.Ordinal)).ToArray();
            catalog.ConfigureApproved(definitions.Values.Concat(outside).ToArray()); EditorUtility.SetDirty(catalog); Save(catalog);
            foreach (var t in themes)
            {
                string id = (string)t["runtimeThemeId"], label = (string)t["displayName"];
                var rows = t["entries"].OfType<JObject>().ToArray();
                var entries = rows.Select(row => new EnemyThemeTable.Entry { definition = definitions[(string)row["cardKey"]],
                    tier = Tier(row), weight = (float)row["weight"] }).ToArray();
                string presetPath = Root + "Presets/" + id + ".asset";
                var preset = AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(presetPath);
                if (preset == null)
                {
                    preset = Create<EnemyAiPreset>(presetPath, created);
                    EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<EnemyAiPreset>((string)t["presetSeedPath"]), preset);
                    preset.name = id;
                }
                preset.ConfigureIdentity("Theme_" + id, label,
                    entries.Where(e => e.tier != EnemyThemeTier.Elite).Select(e => e.definition.ActorPrefab.gameObject).ToArray());
                EditorUtility.SetDirty(preset); Save(preset);
                foreach (var entry in entries)
                {
                    var definition = entry.definition;
                    var participation = entry.tier == EnemyThemeTier.Elite ? EnemySquadParticipationMode.Independent : EnemySquadParticipationMode.SquadMember;
                    if (definition.AiPreset == preset && definition.SquadParticipationMode == participation) continue;
                    var serialized = new SerializedObject(definition);
                    serialized.FindProperty("aiPreset").objectReferenceValue = preset;
                    serialized.FindProperty("squadParticipationMode").enumValueIndex = (int)participation;
                    serialized.ApplyModifiedPropertiesWithoutUndo(); Save(definition);
                }
                var table = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>((string)t["tablePath"])
                    ?? Create<EnemyThemeTable>((string)t["tablePath"], created);
                Color color = !string.IsNullOrEmpty(table.ThemeId) ? table.Accent : new Color((float)t["accent"][0], (float)t["accent"][1], (float)t["accent"][2], 1f);
                table.ConfigureApproved(id, label, catalog, color, entries); EditorUtility.SetDirty(table); Save(table);
                string materialPath = Root + "Materials/" + id + ".mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) == null)
                {
                    var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    material.SetColor("_BaseColor", color); AssetDatabase.CreateAsset(material, materialPath); created.Add(materialPath); Save(material);
                }
                if (!table.Validate(out string reason)) throw new InvalidOperationException(reason);
                results.Add(new JObject { ["id"] = id, ["name"] = label, ["entries"] = entries.Length,
                    ["small"] = entries.Count(e => e.tier == EnemyThemeTier.Small), ["medium"] = entries.Count(e => e.tier == EnemyThemeTier.Medium),
                    ["elite"] = entries.Count(e => e.tier == EnemyThemeTier.Elite), ["approvedMembershipProtected"] = table.IsApprovedRosterLocked });
            }
            var result = new JObject { ["status"] = "APPLIED_READY_SUBSET_NATIVE_VALIDATION_PENDING", ["themes"] = results,
                ["readyActors"] = definitions.Count, ["held"] = plan["held"].DeepClone(), ["newAssets"] = JArray.FromObject(created),
                ["audioApplied"] = false, ["fullApprovedRosterComplete"] = held.Count == 0 };
            File.WriteAllText(Path.Combine(outputDirectory, "apply-result.json"), result.ToString()); return result.ToString();
        }
        catch (Exception error)
        {
            foreach (var pair in nativeBefore) if (pair.Key != null) { EditorJsonUtility.FromJsonOverwrite(pair.Value, pair.Key); EditorUtility.SetDirty(pair.Key); Save(pair.Key); }
            foreach (string path in created) AssetDatabase.DeleteAsset(path);
            foreach (var pair in backups) File.Copy(pair.Value, pair.Key, true);
            foreach (var pair in backups.Where(p => !p.Key.EndsWith(".meta", StringComparison.Ordinal)))
                AssetDatabase.ImportAsset(pair.Key.Substring(Project.Length + 1).Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
            File.WriteAllText(Path.Combine(outputDirectory, "apply-result.json"), new JObject { ["status"] = "FAIL_RESTORED_OWNED_BACKUPS", ["error"] = error.ToString() }.ToString());
            throw;
        }
        finally { AssetDatabase.AllowAutoRefresh(); }
    }
}
