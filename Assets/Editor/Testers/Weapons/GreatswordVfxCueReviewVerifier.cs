using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.EditorTools.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class GreatswordVfxCueReviewVerifier
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string Output = "../개인파일/코덱스산출/Tools/GreatswordVfxCueReview/20261005_CurrentSlots";
    private const string HeavyPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Heavy/GreatswordHeavyAttack.asset";
    private const string HitPath = "Assets/ProjectOverburst/Resources/Combat/VFX/MeleeElementHitVfxCatalog.asset";
    private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static object[] Cues(object window) => ((IEnumerable)Get(window, "cues")).Cast<object>().ToArray();
    private static string Guard() => string.Join("|", Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), IsolatedSavePlayGuard.RequiresAccountChoice,
        IsolatedSavePlayGuard.ActiveDirectory, SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""), SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""),
        AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), AssetDatabase.DesiredWorkerCount, EditorUserSettings.standbyImportWorkerCount);

    [MenuItem("OVERBURST/테스트/대검 VFX 큐 리뷰/현재 연결과 프리뷰")]
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed
            || BuildPipeline.isBuildingPlayer || IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""))) throw new InvalidOperationException("Safe idle Editor required.");
        Directory.CreateDirectory(Path.Combine(Output, "captures"));
        Directory.CreateDirectory(Path.Combine(Output, "data"));
        var checks = new List<string>();
        void Test(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add("PASS: " + label); }
        var scenes = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt)
            .Select(s => (s.handle, s.path, s.isDirty, s.rootCount)).ToArray();
        int previews = EditorSceneManager.previewSceneCount;
        string guard = Guard();
        var heavy = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>(HeavyPath);
        var hits = AssetDatabase.LoadAssetAtPath<MeleeElementHitVfxCatalog>(HitPath);
        GreatswordVfxCueReviewWindow window = null;
        MeleeHeavyAttackDefinition copy = null;
        var originals = new Dictionary<Object, string>();
        var disk = new Dictionary<string, byte[]>();
        var choices = new Dictionary<string, (bool exists, bool value)>();
        Object[] temporaryResources = Array.Empty<Object>();
        object[] catalogue = Array.Empty<object>();
        string failure = null;
        try
        {
            Test(heavy != null && hits != null, "current heavy and hit catalog loaded");
            window = ScriptableObject.CreateInstance<GreatswordVfxCueReviewWindow>();
            Call(window, "CollectCues", heavy, hits); catalogue = Cues(window);
            var heavyFields = typeof(MeleeHeavyElementVfxSet).GetFields().Where(f => f.FieldType == typeof(GameObject)).ToArray();
            Test(catalogue.Select(c => (string)Get(c, "id")).Distinct().Count() == catalogue.Length, "cue IDs unique");
            Test(catalogue.Count(c => Get(c, "propertyPath") != null) == heavyFields.Length, "all current heavy prefab slots represented");
            using (var serialized = new SerializedObject(heavy))
                foreach (var field in heavyFields)
                {
                    string path = "elementVfx." + field.Name;
                    var cue = catalogue.Single(c => (string)Get(c, "propertyPath") == path);
                    Object expected = serialized.FindProperty(path).objectReferenceValue;
                    if (field.Name == "fireChainExplosion") expected = heavy.elementVfx.FireChainExplosion;
                    Test((Object)Get(cue, "asset") == expected && (Object)Get(cue, "source") == heavy, "actual heavy reference: " + field.Name);
                }
            Test(!catalogue.Any(c => (string)Get(c, "id") == "VFX.DARK.HV.PULL" || (string)Get(c, "id") == "VFX.LIGHT.HV.ECHO"), "obsolete pull and shared echo removed");
            Test(((string)Get(catalogue.Single(c => (string)Get(c, "propertyPath") == "elementVfx.darkBarrageTrail"), "note")).Contains("본체"), "optional separate trail explained");
            var assets = catalogue.SelectMany(c => new[] { (Object)Get(c, "asset"), (Object)Get(c, "source") }).Where(a => a != null).Distinct().ToArray();
            foreach (var asset in assets)
            {
                var objects = asset is GameObject prefab ? new Object[] { prefab }.Concat(prefab.GetComponentsInChildren<Component>(true).Where(c => c != null)) : new[] { asset };
                foreach (Object value in objects) originals[value] = EditorJsonUtility.ToJson(value);
                string path = AssetDatabase.GetAssetPath(asset);
                foreach (string file in new[] { path, path + ".meta" }) if (File.Exists(file)) disk[file] = File.ReadAllBytes(file);
            }
            foreach (string id in catalogue.Select(c => (string)Get(c, "id")).Concat(new[] { "VFX.DARK.HV.PULL", "VFX.LIGHT.HV.ECHO", "VFX.LIGHT.HV.CIRCLE" }))
            {
                string key = "Overburst.GreatswordVfxCueReview." + id;
                choices[key] = (EditorPrefs.HasKey(key), EditorPrefs.GetBool(key));
            }
            copy = Object.Instantiate(heavy); copy.hideFlags = HideFlags.HideAndDontSave;
            copy.elementVfx.fireChainExplosion = null;
            Call(window, "CollectCues", copy, hits);
            var fallback = Cues(window).Single(c => (string)Get(c, "propertyPath") == "elementVfx.fireChainExplosion");
            Test((Object)Get(fallback, "asset") == copy.elementVfx.FireChainExplosion && Get(fallback, "state").ToString() == "Shared", "runtime fire-chain fallback shown");
            copy.elementVfx.iceImpact = heavy.elementVfx.fireImpact;
            Call(window, "CollectCues", copy, hits);
            Test(Get(Cues(window).Single(c => (string)Get(c, "propertyPath") == "elementVfx.iceImpact"), "state").ToString() == "Connected", "newly assigned ice slot reloads without fixed empty label");
            window.Show(); window.CreateGUI();
            Test(window.rootVisualElement.Query<VisualElement>().ToList().Count(e => e.ClassListContains("cue-row")) == catalogue.Length, "native Toolkit rows match current catalog");
            var search = (TextField)Get(window, "searchField"); search.value = "3연타";
            Test(window.rootVisualElement.Query<VisualElement>().ToList().Count(e => e.ClassListContains("cue-row")) == 1, "native search finds light triple");
            search.value = "";
            var filter = (DropdownField)Get(window, "filterField"); filter.value = "빈 자리";
            Test(window.rootVisualElement.Query<VisualElement>().ToList().Count(e => e.ClassListContains("cue-row")) == catalogue.Count(c => Get(c, "state").ToString() == "Missing"), "native empty filter follows actual references");
            filter.value = "전체";
            foreach (var sample in new[] {
                ("VFX.ELEC.HV.DIRECT", .35f), ("VFX.ELEC.HV.CHAIN.LINK", .3f),
                ("VFX.DARK.HV.CIRCLE", 1.1f), ("VFX.DARK.HV.BARRAGE.PROJECTILE", .5f),
                ("VFX.DARK.HV.BARRAGE.TRAIL", .5f), ("VFX.DARK.HV.BARRAGE.HIT", .35f),
                ("VFX.LIGHT.HV.DOUBLE", 2.4f), ("VFX.LIGHT.HV.TRIPLE", 2.4f), ("VFX.ICE.HV.CIRCLE", .4f) })
            {
                var row = window.rootVisualElement.Q<VisualElement>(sample.Item1);
                Test(row != null, "current native row: " + sample.Item1);
                using (var click = ClickEvent.GetPooled()) { click.target = row; row.SendEvent(click); }
                var active = Get(window, "active");
                Test((string)Get(active, "id") == sample.Item1, "actual native selection: " + sample.Item1);
                var prefab = Get(active, "asset") as GameObject;
                var instance = Get(window, "effectRoot") as GameObject;
                Test(prefab == null ? instance == null : PrefabUtility.GetCorrespondingObjectFromSource(instance) == prefab, "isolated preview uses selected source: " + sample.Item1);
                if (prefab == null) continue;
                Call(window, "Seek", sample.Item2);
                Test(Mathf.Abs((float)Get(window, "clock") - sample.Item2) < .001f, "forward scrub: " + sample.Item1);
                Capture((RenderTexture)Get(window, "surface"), Path.Combine(Output, "captures", sample.Item1 + ".png"));
                Call(window, "Seek", .1f);
                Test(Mathf.Abs((float)Get(window, "clock") - .1f) < .001f, "reverse scrub: " + sample.Item1);
            }
            for (int i = 0; i < 4; i++) { Call(window, "ReloadCues"); Test(EditorSceneManager.previewSceneCount == previews + 1, "repeated reload keeps one owned preview " + i); }
            temporaryResources = new[] { (Object)Get(window, "surface"), (Object)Get(window, "floorMaterial"), (Object)Get(window, "volumeProfile") };
        }
        catch (Exception e) { failure = e.ToString(); }
        finally
        {
            if (copy != null) Object.DestroyImmediate(copy);
            if (window != null) { window.Close(); if (window != null) Object.DestroyImmediate(window); }
            try
            {
                foreach (var item in originals) Test(item.Key != null && EditorJsonUtility.ToJson(item.Key) == item.Value, "source memory preserved: " + item.Key?.name);
                foreach (var item in disk) Test(File.ReadAllBytes(item.Key).SequenceEqual(item.Value), "source disk preserved: " + item.Key);
                foreach (var choice in choices) Test(EditorPrefs.HasKey(choice.Key) == choice.Value.exists && EditorPrefs.GetBool(choice.Key) == choice.Value.value, "user choice preserved: " + choice.Key);
                foreach (var before in scenes) { var scene = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt).FirstOrDefault(s => s.handle == before.handle); Test(scene.IsValid() && scene.path == before.path && scene.isDirty == before.isDirty && scene.rootCount == before.rootCount, "user scene and dirty state preserved"); }
                Test(EditorSceneManager.previewSceneCount == previews && temporaryResources.All(r => r == null), "owned preview scenes and render resources released");
                Test(Guard() == guard, "account guard start scene and worker settings preserved");
            }
            catch (Exception e) { failure = (failure ?? "") + "\n" + e; }
            File.WriteAllText(Path.Combine(Output, "data", "verification.json"), JsonConvert.SerializeObject(new {
                success = failure == null, count = checks.Count, failure, checks, heavySlots = typeof(MeleeHeavyElementVfxSet).GetFields().Count(f => f.FieldType == typeof(GameObject)),
                cues = catalogue.Select(c => new { id = Get(c, "id"), propertyPath = Get(c, "propertyPath"), asset = AssetDatabase.GetAssetPath((Object)Get(c, "asset")), state = Get(c, "state").ToString() }),
                play = "NOT_RUN: read-only Editor tool", player = "NOT_RUN: runtime/assets unchanged"
            }, Formatting.Indented));
        }
        if (failure != null) throw new InvalidOperationException(failure);
        return "PASS: " + checks.Count + " checks / " + catalogue.Length + " cues";
    }

    private static void Capture(RenderTexture surface, string path)
    {
        RenderTexture previous = RenderTexture.active;
        var texture = new Texture2D(surface.width, surface.height, TextureFormat.RGB24, false);
        try { RenderTexture.active = surface; texture.ReadPixels(new Rect(0, 0, surface.width, surface.height), 0, 0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
    }
}
