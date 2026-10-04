using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MonsterV3ThemeActivationNativeVerifier
{
    const string Root = "Assets/ProjectOverburst/Resources/Enemies/Themes/";
    static void Require(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    static string Hash(string path)
    {
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
    static JObject Without(JObject data, params string[] fields)
    {
        var result = (JObject)(data["MonoBehaviour"] ?? data).DeepClone();
        foreach (string field in fields) result.Remove(field);
        return result;
    }
    static JObject Native(Object asset) => JObject.Parse(EditorJsonUtility.ToJson(asset));
    static bool Reject(Action action)
    {
        try { action(); return false; } catch (InvalidOperationException) { return true; }
    }
    public static string Run(string planPath, string applyDirectory)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling
            && !EditorApplication.isUpdating && !EditorUtility.scriptCompilationFailed, "Idle compiled Editor required.");
        string project = Directory.GetParent(Application.dataPath).FullName;
        var plan = JObject.Parse(File.ReadAllText(planPath));
        var before = JObject.Parse(File.ReadAllText(Path.Combine(applyDirectory, "native-before.json")));
        var catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalog>(Root + "Catalog.asset");
        var rows = plan["themes"].SelectMany(t => t["entries"]).ToArray();
        var definitions = rows.Select(r => AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)r["definitionPath"])).ToArray();
        string reason;
        Require(catalog.IsApprovedRosterLocked && catalog.Validate(out reason), "Invalid approved catalog.");
        var actualRegular = Enumerable.Range(0, catalog.Count).Select(catalog.GetDefinition)
            .Where(d => AssetDatabase.GetAssetPath(d).StartsWith(Root + "Definitions/", StringComparison.Ordinal)).ToArray();
        Require(actualRegular.Length == definitions.Length && actualRegular.ToHashSet().SetEquals(definitions), "Ready catalog membership differs.");
        var results = new JArray();
        foreach (var theme in plan["themes"])
        {
            string id = (string)theme["runtimeThemeId"];
            var table = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>((string)theme["tablePath"]);
            var preset = AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(Root + "Presets/" + id + ".asset");
            var entries = theme["entries"].ToArray();
            Require(table != null && table.IsApprovedRosterLocked && table.Validate(out reason), "Invalid theme: " + id);
            Require(table.ThemeId == id && table.DisplayName == (string)theme["displayName"] && table.Catalog == catalog
                && table.Entries.Count == entries.Length && MapThemeCatalog.ResolveForRun(id) == table, "Theme routing differs: " + id);
            for (int i = 0; i < entries.Length; i++)
            {
                var row = entries[i]; var entry = table.Entries[i]; var definition = entry.definition;
                var tier = (EnemyThemeTier)Enum.Parse(typeof(EnemyThemeTier), (string)row["tier"], true);
                Require(AssetDatabase.GetAssetPath(definition) == (string)row["definitionPath"] && entry.tier == tier
                    && entry.weight == (float)row["weight"] && definition.DisplayName == (string)row["koreanDisplayName"], "Entry differs: " + id);
                Require(definition.AiPreset == preset && definition.SquadParticipationMode == (tier == EnemyThemeTier.Elite
                    ? EnemySquadParticipationMode.Independent : EnemySquadParticipationMode.SquadMember), "AI binding differs: " + definition.EnemyId);
                Require(JToken.DeepEquals(Without((JObject)before[(string)row["definitionPath"]], "aiPreset", "squadParticipationMode"),
                    Without(Native(definition), "aiPreset", "squadParticipationMode")), "Unrelated definition field changed: " + definition.EnemyId);
                Require(definition.ActorPrefab.GetComponentsInChildren<Transform>(true)
                    .All(tr => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(tr.gameObject) == 0), "Missing actor script.");
            }
            var members = table.Entries.Where(e => e.tier != EnemyThemeTier.Elite).Select(e => e.definition.ActorPrefab.gameObject).ToArray();
            Require(preset.DefaultMonsterCount == members.Length && Enumerable.Range(0, members.Length)
                .All(i => preset.GetDefaultMonsterPrefab(i) == members[i]), "Preset roster differs: " + id);
            string presetPath = AssetDatabase.GetAssetPath(preset);
            string originalPresetPath = before[presetPath] != null ? presetPath : (string)theme["presetSeedPath"];
            string originalPreset = Path.Combine(applyDirectory, "Before", originalPresetPath);
            Require(Hash(originalPreset) == (string)plan["expectedAssets"][originalPresetPath], "Original preset backup differs.");
            string originalText = File.ReadAllText(originalPreset);
            var properties = new SerializedObject(preset).GetIterator(); int numericFields = 0;
            while (properties.NextVisible(true))
            {
                if (properties.depth != 0 || properties.propertyType != SerializedPropertyType.Integer
                    && properties.propertyType != SerializedPropertyType.Float) continue;
                var match = Regex.Match(originalText, "(?m)^  " + Regex.Escape(properties.name) + ": ([^\\r\\n]+)");
                Require(match.Success, "Original numeric preset field missing: " + properties.name);
                bool equal = properties.propertyType == SerializedPropertyType.Integer
                    ? properties.intValue == int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                    : properties.floatValue == float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                Require(equal, "Squad numeric tuning changed: " + id + "/" + properties.name); numericFields++;
            }
            Require(numericFields == 13, "Unexpected numeric preset field coverage: " + id);
            var normal = table.BuildRoster(40, 9, 1, 9815);
            Require(normal.Count == 50 && normal.SequenceEqual(table.BuildRoster(40, 9, 1, 9815))
                && normal.All(d => table.Entries.Any(e => e.definition == d)), "Normal roster differs: " + id);
            var elite = table.BuildRoster(0, 0, 50, 9815);
            Require(elite.Distinct().Count() == table.Entries.Count(e => e.tier == EnemyThemeTier.Elite), "Elite roster loses a species: " + id);
            var touched = new List<Object>(); var old = Native(table); var oldPreset = Native(preset);
            Require(!MonsterThemeAuthoringPolicy.MergeTable(table, false, id, "old", catalog, table.Accent, Array.Empty<EnemyThemeTable.Entry>(), touched)
                && !MonsterThemeAuthoringPolicy.AppendPresetRoster(preset, definitions.Select(d => d.ActorPrefab.gameObject), touched)
                && touched.Count == 0 && JToken.DeepEquals(old, Native(table)) && JToken.DeepEquals(oldPreset, Native(preset)), "Legacy builder changed approved membership.");
            var copy = Object.Instantiate(table);
            try
            {
                var altered = table.Entries.ToArray(); altered[0].tier = altered[0].tier == EnemyThemeTier.Small ? EnemyThemeTier.Medium : EnemyThemeTier.Small;
                Require(Reject(() => copy.Configure(id, table.DisplayName, catalog, table.Accent, altered)), "Tier guard accepted an unapproved edit.");
                var weighted = table.Entries.ToArray(); weighted[0].weight *= 2;
                copy.Configure(id, table.DisplayName, catalog, table.Accent, weighted);
                Require(copy.Entries[0].weight == weighted[0].weight, "Approved weight tuning was blocked.");
            }
            finally { Object.DestroyImmediate(copy); }
            results.Add(new JObject { ["id"] = id, ["entries"] = entries.Length, ["pass"] = true });
        }
        var catalogCopy = Object.Instantiate(catalog);
        try
        {
            var duplicate = Enumerable.Range(0, catalog.Count).Select(catalog.GetDefinition).ToArray(); duplicate[1] = duplicate[0];
            Require(Reject(() => catalogCopy.Configure(duplicate)), "Catalog duplicate guard failed.");
            var touched = new List<Object>();
            Require(!MonsterThemeAuthoringPolicy.MergeCatalog(catalog, Array.Empty<EnemyDefinition>(), touched) && touched.Count == 0, "Legacy catalog changed.");
        }
        finally { Object.DestroyImmediate(catalogCopy); }
        foreach (var property in ((JObject)plan["protectedAssets"]).Properties())
            Require(Hash(Path.Combine(project, property.Name)) == (string)property.Value, "Actor/animation/ability changed: " + property.Name);
        foreach (var property in ((JObject)plan["expectedAssets"]).Properties())
            if (property.Name.EndsWith(".meta", StringComparison.Ordinal) || property.Name.Contains("/Materials/"))
                Require(Hash(Path.Combine(project, property.Name)) == (string)property.Value, "GUID/original marker changed: " + property.Name);
        Require(MapThemeCatalog.Tables.Count == 7 && EnemyThemeTrialService.Entries.Count == 7, "Map/debug catalog did not expose all seven themes.");
        var result = new JObject { ["status"] = "PASS_READY_SUBSET_NATIVE", ["themes"] = results, ["readyActors"] = definitions.Length,
            ["protectedFiles"] = ((JObject)plan["protectedAssets"]).Count, ["held"] = plan["held"].DeepClone(),
            ["fullApprovedRosterComplete"] = false, ["audioApplied"] = false };
        File.WriteAllText(Path.Combine(applyDirectory, "native-validation.json"), result.ToString()); return result.ToString();
    }
}
