#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class GraveHuntSoundBuilder
{
    public static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorUtility.scriptCompilationFailed || IsolatedSavePlayGuard.ActiveDirectory != ""
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "") != "")
            throw new InvalidOperationException("Idle unoccupied Editor required");
    }
    public static string Hash(string path)
    { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    private static void Folder(string path)
    {
        path = path.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    public static string Apply(string planPath, string output)
    {
        RequireIdle(); var plan = JObject.Parse(File.ReadAllText(planPath));
        if ((string)plan["theme"] != "GraveHunt" || plan["actors"].Count() != 14) throw new Exception("14 GraveHunt actors required");
        var table = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>("Assets/ProjectOverburst/Resources/Enemies/Themes/Tables/GraveHunt.asset");
        var entries = new SerializedObject(table).FindProperty("entries"); var allowed = new HashSet<string>();
        for (int i = 0; i < entries.arraySize; i++)
            allowed.Add(AssetDatabase.GetAssetPath(entries.GetArrayElementAtIndex(i).FindPropertyRelative("definition").objectReferenceValue));
        foreach (var row in plan["actors"])
        {
            string definition = "Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/" + row["id"] + ".asset";
            if (!allowed.Contains(definition)) throw new Exception("Non-GraveHunt actor: " + row["id"]);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)row["actor"]);
            if (prefab == null || EditorUtility.IsDirty(prefab)) throw new Exception("Missing or dirty prefab");
        }
        var imported = new List<object>();
        foreach (var row in plan["imports"])
        {
            string target = (string)row["asset"], guid = (string)row["guid"], existing = AssetDatabase.GUIDToAssetPath(guid);
            if (existing != "" && existing != target) throw new Exception("GUID collision: " + existing);
            if (File.Exists(target) && Hash(target) != (string)row["sha256"]) throw new Exception("Different target bytes: " + target);
            Folder(Path.GetDirectoryName(target));
            if (!File.Exists(target)) File.Copy((string)row["source"], target);
            if (!File.Exists(target + ".meta")) File.Copy((string)row["meta"], target + ".meta");
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.AssetPathToGUID(target) != guid || AssetDatabase.LoadAssetAtPath<AudioClip>(target) == null)
                throw new Exception("Audio import failed: " + target);
            imported.Add(new { target, guid, sha256 = Hash(target) });
        }
        foreach (var row in plan["actors"])
        {
            string path = (string)row["actor"]; var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var original = root.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null && !(c is EnemySoundEmitter))
                    .ToDictionary(c => c, c => EditorJsonUtility.ToJson(c));
                var emitter = root.GetComponent<EnemySoundEmitter>() ?? root.AddComponent<EnemySoundEmitter>();
                var so = new SerializedObject(emitter); so.FindProperty("pitch").floatValue = (float)row["pitch"];
                foreach (string slot in new[] { "attack", "hurt", "death", "melee" })
                {
                    var choices = (JArray)row["slots"][slot]; var field = so.FindProperty(slot); field.arraySize = choices.Count;
                    for (int i = 0; i < choices.Count; i++)
                    {
                        string asset = (string)choices[i]["asset"];
                        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(asset);
                        if (clip == null || Hash(asset) != (string)plan["audio"][asset]["sha256"]) throw new Exception("Clip mismatch: " + asset);
                        var entry = field.GetArrayElementAtIndex(i);
                        entry.FindPropertyRelative("clip").objectReferenceValue = clip;
                        entry.FindPropertyRelative("volume").floatValue = (float)choices[i]["volume"];
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                foreach (var pair in original) if (EditorJsonUtility.ToJson(pair.Key) != pair.Value) throw new Exception("Existing component changed");
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "native-apply.json"), JsonConvert.SerializeObject(new { status = "APPLIED", actors = 14, imported, baseComponentsPreserved = true }, Formatting.Indented));
        return Verify(planPath, output);
    }
    public static string Verify(string planPath, string output)
    {
        RequireIdle(); var plan = JObject.Parse(File.ReadAllText(planPath)); int checks = 0;
        foreach (var row in plan["actors"])
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>((string)row["actor"]);
            if (root.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c == null)) throw new Exception("Missing script");
            var so = new SerializedObject(root.GetComponent<EnemySoundEmitter>()); checks++;
            foreach (string slot in new[] { "attack", "hurt", "death", "melee" })
            {
                var actual = so.FindProperty(slot); var expected = (JArray)row["slots"][slot];
                if (actual.arraySize != expected.Count) throw new Exception("Slot count"); checks++;
                for (int i = 0; i < actual.arraySize; i++)
                {
                    var choice = actual.GetArrayElementAtIndex(i);
                    if (AssetDatabase.GetAssetPath(choice.FindPropertyRelative("clip").objectReferenceValue) != (string)expected[i]["asset"]
                        || Mathf.Abs(choice.FindPropertyRelative("volume").floatValue - (float)expected[i]["volume"]) > .00001f)
                        throw new Exception("Authored clip or gain mismatch"); checks++;
                }
            }
        }
        string json = JsonConvert.SerializeObject(new { status = "PASS", checks, actors = 14, additionalOnly = true }, Formatting.Indented);
        File.WriteAllText(Path.Combine(output, "native-verification.json"), json); return json;
    }
}
#endif
