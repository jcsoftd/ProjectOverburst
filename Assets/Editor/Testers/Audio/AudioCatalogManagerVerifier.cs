using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class AudioCatalogManagerVerifier
{
    private static string Output = "../개인파일/코덱스산출/Tools/AudioCatalogManager/20261005_RuntimeCatalogs";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    [MenuItem("OVERBURST/테스트/오디오/카탈로그 관리자")]
    public static void Run() => Run("../개인파일/코덱스산출/Tools/AudioCatalogManager/20261005_RuntimeCatalogs", true);
    public static void Run(string output, bool render = true)
    {
        RequireIdle(); Output=output;
        Directory.CreateDirectory(Output + "/data");
        Directory.CreateDirectory(Output + "/captures");
        checks = 0;
        int previewSamples = 0;
        string failure = null;
        AudioCatalogManagerWindow window = null;
        List<IAudioCatalogEditorProvider> providers = AudioCatalogEditorProviderRegistry.CreateProviders();
        var originals = new List<(Object asset, string json, bool dirty, string path, byte[] bytes, byte[] meta)>();
        string scenes = SceneState(), guard = GuardState();
        int previews = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
        Object selection = Selection.activeObject;
        int desired = AssetDatabase.DesiredWorkerCount, standby = EditorUserSettings.standbyImportWorkerCount;
        var summaries = new List<object>();
        try
        {
            Check(providers.Count == 4, "Four runtime providers");
            foreach (IAudioCatalogEditorProvider provider in providers)
            {
                provider.Refresh();
                Check(provider.CatalogAsset != null, provider.DisplayName + " runtime asset");
                originals.Add((provider.CatalogAsset, EditorJsonUtility.ToJson(provider.CatalogAsset),
                    EditorUtility.IsDirty(provider.CatalogAsset), provider.AssetPath,
                    File.ReadAllBytes(provider.AssetPath), File.ReadAllBytes(provider.AssetPath + ".meta")));
                Check(!provider.CollectIssues().Any(issue => issue.Type == MessageType.Error), provider.DisplayName + " baseline errors");
                Check(!provider.HasMissingReference, provider.DisplayName + " optional blanks excluded");
                Check(provider.ReadClipCount() > 0, provider.DisplayName + " clip count");
                Check(!provider.SupportsConfirmedDefaults, "Legacy default replacement unavailable");
                summaries.Add(new { name = provider.DisplayName, path = provider.AssetPath,
                    rows = provider.ReadElementCount(), clips = provider.ReadClipCount(),
                    issues = provider.CollectIssues().Select(issue => new { type = issue.Type.ToString(), message = issue.Message }).ToArray() });
            }
            Check(providers.Select(p => p.AssetPath).Distinct().Count() == 4, "Unique runtime paths");
            VerifyMelee((MeleeElementSfxCatalog)providers[0].CatalogAsset);
            VerifyCombat((CombatActionSfxCatalog)providers[1].CatalogAsset);
            VerifyReaction((ElementalReactionSfxCatalog)providers[2].CatalogAsset);
            VerifyDrop((ItemDropSfxCatalog)providers[3].CatalogAsset);
            AudioCatalogManagerCommandLineValidation.ValidateFromCommandLine();
            Check(true, "Current catalog command-line regression");

            if(render)
            {
            window = ScriptableObject.CreateInstance<AudioCatalogManagerWindow>();
            window.position = new Rect(80f, 80f, 1100f, 820f);
            window.ShowUtility();
            for (int index = 0; index < 4; index++)
            {
                Set(window, "selectedIndex", index);
                Set(window, "selectedProviderName", providers[index].DisplayName);
                Set(window, "inspectorScroll", Vector2.zero);
                window.CreateGUI();
                Capture(window, Output + "/captures/catalog-" + index + ".png");
                Check(Get<int>(window, "selectedIndex") == index, "Native inspector renders " + providers[index].DisplayName);
                Set(window, "search", providers[index].DisplayName);
                Check((bool)Call(window, "MatchesFilter", providers[index]), "Korean search " + index);
                Check(!(bool)Call(window, "MatchesFilter", providers[(index + 1) % 4]), "Search excludes other catalog " + index);
                Set(window, "search", string.Empty);
                Set(window, "missingOnly", true);
                Check(!(bool)Call(window, "MatchesFilter", providers[index]), "Optional blank is outside missing filter " + index);
                Set(window, "missingOnly", false);
                Call(window, "RefreshProviders");
                Check(Get<int>(window, "selectedIndex") == index, "Selected provider survives refresh " + index);
            }
            }
            // The native AudioUtil call is checked separately from a human listening review.
            Type audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            MethodInfo playing = audioUtil.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "IsPreviewClipPlaying" || m.Name == "IsClipPlaying");
            Check(playing != null, "Native preview state API available");
            foreach (Object asset in providers.Select(p => p.CatalogAsset))
            {
                using (var source = new SerializedObject(asset))
                {
                    var property = source.GetIterator(); AudioClip clip = null;
                    while (property.NextVisible(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is AudioClip found)
                        { clip = found; break; }
                    Check(clip != null, "Preview sample exists " + asset.name);
                    AudioCatalogEditorPreview.Play(clip);
                    object[] args = playing.GetParameters().Length == 0 ? null : new object[] { clip };
                    Check((bool)playing.Invoke(null, args), "Native preview playing " + asset.name);
                    AudioCatalogEditorPreview.StopAll();
                    Check(!(bool)playing.Invoke(null, args), "Native preview stopped " + asset.name);
                    previewSamples++;
                }
            }
        }
        catch (Exception exception) { failure = exception.ToString(); }
        finally
        {
            AudioCatalogEditorPreview.StopAll();
            if (window != null) window.Close();
            foreach (IAudioCatalogEditorProvider provider in providers) (provider as IDisposable)?.Dispose();
            try
            {
                foreach (var original in originals)
                {
                    Check(EditorJsonUtility.ToJson(original.asset) == original.json, "Source memory unchanged " + original.path);
                    Check(EditorUtility.IsDirty(original.asset) == original.dirty, "Source dirty unchanged " + original.path);
                    Check(File.ReadAllBytes(original.path).SequenceEqual(original.bytes), "Source disk unchanged " + original.path);
                    Check(File.ReadAllBytes(original.path + ".meta").SequenceEqual(original.meta), "Source GUID unchanged " + original.path);
                }
                Check(SceneState() == scenes, "User scene and dirty preserved");
                Check(GuardState() == guard, "Account Guard and start scene preserved");
                Check(UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount == previews, "Preview scenes preserved");
                Check(Selection.activeObject == selection, "Selection preserved");
                Check(AssetDatabase.DesiredWorkerCount == desired && EditorUserSettings.standbyImportWorkerCount == standby, "Worker settings preserved");
            }
            catch (Exception exception) { failure = (failure ?? "") + "\nRETURN: " + exception; }
            File.WriteAllText(Output + "/data/verification.json", Newtonsoft.Json.JsonConvert.SerializeObject(new
            { utc = DateTime.UtcNow, status = failure == null ? "PASS" : "FAIL", checks, catalogs = summaries,
                nativePreviewSamples = previewSamples, humanAudition = "NOT_RUN", failure,
                scenes = SceneState(), guard = GuardState(), previewScenes = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount
            }, Newtonsoft.Json.Formatting.Indented));
        }
        if (failure != null) throw new InvalidOperationException(failure);
        Debug.Log("[OVERBURST] 오디오 카탈로그 관리자 " + checks + "검사 PASS.");
    }

    private static void VerifyMelee(MeleeElementSfxCatalog original)
    {
        var clone = Object.Instantiate(original); var provider = new MeleeElementSfxCatalogEditorProvider();
        try
        {
            Bind(provider, clone);
            Check(Errors(provider) == 0 && !provider.HasMissingReference, "Current six elements do not require deprecated water/wind");
            int total = 0;
            using (var source = new SerializedObject(clone))
            {
                SerializedProperty property = source.GetIterator();
                while (property.NextVisible(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is AudioClip) total++;
            }
            Check(provider.ReadClipCount() == total, "All melee five cue types and upper stages counted");
            clone.entries[0].criticalHit.clips = Array.Empty<AudioClip>();
            Check(Errors(provider) == 0 && !provider.HasMissingReference, "Empty critical cue keeps Hit fallback");
            clone.entries[0].slash.clips = new AudioClip[] { null };
            Check(Errors(provider) > 0 && provider.HasMissingReference, "Broken slash detected");
            clone.entries[0].slash.clips = original.entries[0].slash.clips;
            clone.upperHeavy.darkBarrageLaunch.clips = new AudioClip[] { null };
            Check(Errors(provider) > 0 && provider.HasMissingReference, "Broken barrage stage detected");
            clone.upperHeavy.darkBarrageLaunch.clips = Array.Empty<AudioClip>();
            clone.entries[0].hit.maxVoices = -1;
            Check(Errors(provider) > 0, "Negative voice limit detected");
            clone.entries = clone.entries.Where(entry => entry.element != WeaponElement.Fire).ToArray();
            Check(Errors(provider) > 0 && provider.HasMissingReference, "Missing current element detected");
        }
        finally { provider.Dispose(); Undo.ClearUndo(clone); Object.DestroyImmediate(clone); }
    }
    private static void VerifyCombat(CombatActionSfxCatalog original)
    {
        var clone = Object.Instantiate(original); var provider = new CombatActionSfxCatalogEditorProvider();
        try
        {
            Bind(provider, clone);
            clone.entries[1].name = clone.entries[0].name;
            Check(Errors(provider) > 0, "Duplicate combat key detected");
            clone.entries[1].name = "";
            Check(Errors(provider) > 0, "Empty combat key detected");
            clone.entries = new[] { new CombatActionSfxCatalog.Entry { name = "GreatswordLight01" } };
            Check(Resources.Load<AudioClip>("Combat/SFX/CombatAction/GreatswordLight01") != null, "Existing fallback clip");
            Check(Errors(provider) == 0 && !provider.HasMissingReference, "Runtime Resources fallback recognized");
            clone.entries[0].name = "__VerifierMissingClip__";
            Check(Errors(provider) > 0 && provider.HasMissingReference, "Missing direct and fallback clip detected");
        }
        finally { provider.Dispose(); Undo.ClearUndo(clone); Object.DestroyImmediate(clone); }
    }
    private static void VerifyReaction(ElementalReactionSfxCatalog original)
    {
        var clone = Object.Instantiate(original); var provider = new ElementalReactionSfxCatalogEditorProvider();
        try
        {
            Bind(provider, clone);
            Check(Errors(provider) == 0 && !provider.HasMissingReference, "Deferred fracture remains optional");
            clone.shatter.clips = new AudioClip[] { null };
            Check(provider.HasMissingReference && Errors(provider) > 0, "Broken reaction candidate detected");
            clone.shatter.clips = original.shatter.clips;
            clone.vaporize.minPitch = 2f; clone.vaporize.maxPitch = 1f;
            Check(Errors(provider) > 0, "Reversed reaction pitch detected");
            clone.vaporize.minPitch = original.vaporize.minPitch;
            clone.vaporize.volume = float.NaN;
            Check(Errors(provider) > 0, "Nonfinite reaction volume detected");
        }
        finally { provider.Dispose(); Undo.ClearUndo(clone); Object.DestroyImmediate(clone); }
    }
    private static void VerifyDrop(ItemDropSfxCatalog original)
    {
        float originalVolume = original.dropVolume;
        var clone = Object.Instantiate(original); var provider = new ItemDropSfxCatalogEditorProvider();
        try
        {
            Bind(provider, clone);
            Check(Errors(provider) == 0 && !provider.HasMissingReference, "Deferred gold remains optional");
            clone.weapon = Array.Empty<AudioClip>();
            Check(provider.HasMissingReference && Errors(provider) > 0, "Missing drop candidate detected");
            clone.weapon = original.weapon; clone.mythic = null;
            Check(provider.HasMissingReference && Errors(provider) > 0, "Missing grade reveal detected");
            clone.mythic = original.mythic; clone.minDistance = 40f; clone.maxDistance = 30f;
            Check(Errors(provider) > 0, "Invalid drop distance detected");
            using (var source = new SerializedObject(clone))
            {
                source.FindProperty("dropVolume").floatValue = .42f;
                source.ApplyModifiedProperties();
            }
            Check(Mathf.Approximately(clone.dropVolume, .42f), "Serialized editing applies on working copy");
            Check(Mathf.Approximately(original.dropVolume, originalVolume), "Working copy edit leaves original volume unchanged");
        }
        finally { provider.Dispose(); Undo.ClearUndo(clone); Object.DestroyImmediate(clone); }
    }
    private static int Errors(IAudioCatalogEditorProvider provider) => provider.CollectIssues().Count(issue => issue.Type == MessageType.Error);
    private static void Bind<T>(SerializedAudioCatalogEditorProvider<T> provider, T clone) where T : ScriptableObject
    {
        typeof(SerializedAudioCatalogEditorProvider<T>).GetField("catalog", Private).SetValue(provider, clone);
        typeof(SerializedAudioCatalogEditorProvider<T>).GetField("serializedCatalog", Private).SetValue(provider, new SerializedObject(clone));
    }
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static T Get<T>(Object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    private static void Set(Object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static object Call(Object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static string SceneState() => Newtonsoft.Json.JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount)
        .Select(SceneManager.GetSceneAt).Select(s => new { s.path, s.isDirty, s.rootCount }));
    private static string GuardState() => Newtonsoft.Json.JsonConvert.SerializeObject(new
    { environment = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), active = IsolatedSavePlayGuard.ActiveDirectory,
        choice = IsolatedSavePlayGuard.RequiresAccountChoice, prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""),
        expires = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""),
        startScene = AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene) });
    private static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || BuildPipeline.isBuildingPlayer || EditorUtility.scriptCompilationFailed || IsolatedSavePlayGuard.RequiresAccountChoice
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", "")))
            throw new InvalidOperationException("Safe idle shared Editor required; verifier did not run.");
        MethodInfo playing = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil")
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "IsPreviewClipPlaying" && m.GetParameters().Length == 0);
        if (playing != null && (bool)playing.Invoke(null, null))
            throw new InvalidOperationException("An existing audio preview is active; verifier did not interrupt it.");
    }
    private static void Capture(EditorWindow window, string path)
    {
        object view = typeof(EditorWindow).GetField("m_Parent", Private).GetValue(window);
        Type type = view.GetType();
        MethodInfo Find(string name, params Type[] parameters)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, parameters, null);
                if (method != null) return method;
            }
            throw new NotSupportedException("GUIView capture method unavailable: " + name);
        }
        int width = Mathf.RoundToInt(window.position.width), height = Mathf.RoundToInt(window.position.height);
        var surface = new RenderTexture(width, height, 24); surface.Create();
        RenderTexture previous = RenderTexture.active; Texture2D texture = null;
        try
        {
            Find("RepaintImmediately").Invoke(view, null);
            Find("RepaintImmediately").Invoke(view, null);
            Find("GrabPixels", typeof(RenderTexture), typeof(Rect))
                .Invoke(view, new object[] { surface, new Rect(0, 0, width, height) });
            RenderTexture.active = surface;
            texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
            Color32[] pixels = texture.GetPixels32();
            for (int y = 0; y < height / 2; y++)
                for (int x = 0; x < width; x++)
                { int a = y * width + x, b = (height - 1 - y) * width + x; Color32 p = pixels[a]; pixels[a] = pixels[b]; pixels[b] = p; }
            texture.SetPixels32(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; if (texture != null) Object.DestroyImmediate(texture); surface.Release(); Object.DestroyImmediate(surface); }
    }
}
