#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.DebugTools;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[InitializeOnLoad]
public static class PlayerVoiceAuditionVerifier
{
    private const string K = "Overburst.PlayerVoiceAuditionVerifier.";
    private static readonly List<string> checks = new List<string>();
    private static readonly Stack<IEnumerator> tasks = new Stack<IEnumerator>();
    private static VisualPlayContext context;
    private static double deadline;
    private static int frame = -1;
    private static string output;
    static PlayerVoiceAuditionVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetString(K + "pending", "") != "") EditorApplication.update += Begin;
        if (SessionState.GetString(K + "return", "") != "") EditorApplication.update += Return;
    }
    private static void Check(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
        checks.Add(message);
    }
    private static object Scenes() => Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
        .Select(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i)).Select(s => new { s.path, s.isDirty }).ToArray();
    private static void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented));

    public static void Start(string directory)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if (IsolatedSavePlayGuard.ActiveDirectory != "" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY"))) throw new InvalidOperationException("Another account owns Editor");
        output = Path.GetFullPath(directory);
        if (Directory.Exists(output)) throw new InvalidOperationException("Use a new owned validation directory");
        Directory.CreateDirectory(output);
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var seed = NewAccountFactory.Create(registry); AccountCharacterCommands.EnsureLegacy(seed);
        new EasySaveAccountStore(Path.Combine(output, "Account")).Save(seed, "voice-audition-fixture");
        SessionState.SetString(K + "pending", output); SessionState.SetString(K + "return", output);
        SessionState.SetString(K + "start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(K + "background", Application.runInBackground);
        SessionState.SetFloat(K + "deadline", (float)EditorApplication.timeSinceStartup + 180);
        Write("before.json", new { scenes = Scenes(), input = InputSystem.settings.GetInstanceID(), start = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) });
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", Path.Combine(output, "Settings"));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update -= Begin; EditorApplication.update += Begin;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "Account")); }
        catch (Exception error) { Finish(error); throw; }
    }
    private static bool OwnPlay => IsolatedSavePlayGuard.ActiveDirectory == Path.Combine(SessionState.GetString(K + "return", ""), "Account");
    private static void Begin()
    {
        output = SessionState.GetString(K + "pending", "");
        if (output == "") { EditorApplication.update -= Begin; return; }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(K + "deadline", 0)) { Finish(new TimeoutException("Boot")); return; }
        if (!EditorApplication.isPlaying || !OwnPlay || TitleCharacterPanel.Instance == null) return;
        EditorApplication.update -= Begin; SessionState.EraseString(K + "pending");
        Application.runInBackground = true; deadline = EditorApplication.timeSinceStartup + 120; checks.Clear();
        tasks.Push(Verify()); EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (Time.frameCount == frame && EditorApplication.timeSinceStartup < deadline) return;
        frame = Time.frameCount;
        try
        {
            if (!OwnPlay || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Owned Play ended or stalled");
            while (tasks.Count > 0)
            {
                IEnumerator task = tasks.Peek();
                if (!task.MoveNext()) { (tasks.Pop() as IDisposable)?.Dispose(); continue; }
                if (task.Current is IEnumerator nested) { tasks.Push(nested); continue; }
                return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    private static IEnumerator Frames(int count) { while (count-- > 0) yield return null; }
    private static IEnumerator Wait(Func<bool> condition, string label)
    { int frames = 1800; while (!condition() && frames-- > 0) yield return null; Check(condition(), label); }

    private static IEnumerator Verify()
    {
        yield return Frames(6); TitleCharacterPanel.Instance.start.onClick.Invoke();
        yield return Frames(3); TitleCharacterPanel.Instance.enter.onClick.Invoke();
        yield return Wait(() => AccountBootstrap.Ready && WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching, "Title-to-town isolated boot");
        context = new VisualPlayContext(new VisualPlayEntry { caseId = "VT05-02", label = "Player voice audition" }, new VisualPlayState());
        yield return context.Prepare(); context.Aim(context.Actor.transform.position + Vector3.forward * 4); yield return context.Wait(.3f);
        var view = PlayerVoiceService.Current;
        Check(view != null && UnityEngine.Object.FindObjectsByType<PlayerVoiceService>(FindObjectsSortMode.None).Length == 1, "One production voice player");
        Check(UnityEngine.Object.FindObjectsByType<PlayerVoiceAudition>(FindObjectsSortMode.None).Length == 0, "No temporary audition overlay");
        Check(OverburstGameSettings.PlayerVoice == PlayerVoiceChoice.Karen1, "New settings default to Karen1");
        var audio = view.GetComponent<AudioSource>();
        var menu = OverburstGameMenu.Instance; menu.Open(); menu.OpenSettings(); yield return Frames(5);
        var panel = menu.settings; panel.tabs[0].isOn = true; yield return Frames(3);
        var presentation = panel.GetComponent<OverburstSettingsGothicView>();
        var row = panel.playerVoice.GetComponentInParent<OverburstSettingsGothicRow>();
        Check(row && row.owner == presentation && row.tabIndex == 0, "Voice row reuses sound-tab presentation");
        presentation.SelectRow(row);
        var buttons = panel.playerVoice.GetComponentsInChildren<Button>(true);
        Check(buttons.Length == 2, "Existing previous/next voice selector");
        buttons.Single(b=>b.name.Contains("Next")).onClick.Invoke();
        Check(OverburstGameSettings.PlayerVoice==PlayerVoiceChoice.Karen2, "Next arrow selects Karen2");
        buttons.Single(b=>b.name.Contains("Previous")).onClick.Invoke();
        Check(OverburstGameSettings.PlayerVoice==PlayerVoiceChoice.Karen1, "Previous arrow restores Karen1");
        Canvas.ForceUpdateCanvases();
        var point = RectTransformUtility.WorldToScreenPoint(null, buttons[0].transform.position);
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position=point }, hits);
        Check(hits.Any(h=>h.gameObject.GetComponentInParent<Button>()==buttons[0]), "Voice selector receives pointer raycast");
        Check(presentation.scrolls[0].content.rect.height <= presentation.scrolls[0].viewport.rect.height + 2, "Sound controls fit viewport");
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(row.transform, row.transform.Find("Label"));
        var controls = RectTransformUtility.CalculateRelativeRectTransformBounds(row.transform, panel.playerVoice.transform);
        Check(bounds.max.x <= controls.min.x, "Voice label and selector do not overlap");
        Write("settings-controls.json", new { width=Screen.width,height=Screen.height, buttons=buttons.Select(b=>new {b.name,rect=ScreenRect((RectTransform)b.transform)}), reset=ScreenRect((RectTransform)panel.resetButton.transform) });
        for (int candidate = 0; candidate < 4; candidate++)
        {
            panel.playerVoice.SelectOptionByIndex(candidate); yield return Frames(3);
            Check((int)OverburstGameSettings.PlayerVoice == candidate, "Setting changes immediately " + candidate);
            Check(candidate == 0 || view.LastClip != null && view.Candidates[candidate].attack.Contains(view.LastClip), "Selection previews chosen attack " + candidate);
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "settings-" + candidate + ".png")); yield return Frames(4);
            for (int kind = 0; kind < 4; kind++)
            {
                var used = new HashSet<AudioClip>(); AudioClip previous = null;
                var pool = view.Candidates[candidate].Clips((PlayerVoiceService.VoiceKind)kind);
                for (int sample = 0; sample < 24; sample++)
                {
                    Check(view.Preview((PlayerVoiceService.VoiceKind)kind), "Playback " + candidate + "/" + kind + "/" + sample);
                    AudioClip clip = view.LastClip;
                    if ((pool.Length>1 && clip==previous) || !pool.Contains(clip)) throw new Exception("Wrong voice or avoidable repeated clip");
                    previous=clip; used.Add(clip);
                }
                Check(used.Count >= Math.Min(2,pool.Length), "Curated random pool " + candidate + "/" + kind);
            }
        }
        panel.playerVoice.SelectOptionByIndex(4); yield return Frames(3);
        Check(OverburstGameSettings.PlayerVoice == PlayerVoiceChoice.Off && !audio.isPlaying && !view.Preview(PlayerVoiceService.VoiceKind.Attack), "Off stops current audio and future voices");
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "settings-4.png")); yield return Frames(4);
        panel.playerVoice.SelectOptionByIndex(2); menu.CloseSettings();
        string persisted = File.ReadAllText(OverburstGameSettings.FilePath);
        Check((int)Newtonsoft.Json.Linq.JObject.Parse(persisted)["playerVoice"]==2, "Closing settings saves selected voice");
        typeof(OverburstGameSettings).GetField("loaded",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,false);
        Check(OverburstGameSettings.PlayerVoice==PlayerVoiceChoice.Kimlinh, "Saved voice survives a fresh settings load");
        menu.OpenSettings();yield return Frames(3);
        Check(panel.playerVoice.selectedOptionIndex==2, "Reopening settings displays saved voice");
        panel.resetButton.onClick.Invoke();
        Check(OverburstGameSettings.PlayerVoice==PlayerVoiceChoice.Karen1 && panel.playerVoice.selectedOptionIndex==0, "Sound reset restores Karen1");
        menu.CloseSettings(); menu.Close(); yield return Frames(4);
        int count;
        var weapon = VisualPlayContext.Items.OfType<WeaponItemData>().First(w => w.weaponClass == WeaponClass.Greatsword);
        context.Equip(weapon); yield return context.Wait(.5f);
        count = view.PlayedCount; context.MouseButton(true, false); yield return context.Wait(3.5f); context.ReleaseInput();
        Check(view.PlayedCount >= count + 4 && view.LastKind == PlayerVoiceService.VoiceKind.Attack, "Actual held light combo triggers each swing voice");
        context.Melee.CancelCurrentAttackState(); yield return context.Wait(.5f);
        count = view.PlayedCount; context.MouseButton(false, true); yield return context.Wait(2f); context.ReleaseInput();
        Check(view.PlayedCount > count && view.LastKind == PlayerVoiceService.VoiceKind.Attack, "Actual heavy attack triggers voice");
        context.Melee.CancelCurrentAttackState(); yield return context.Wait(.5f);
        yield return Wait(() => context.Actor.Movement.IsGrounded, "Player grounded for jump");
        count = view.PlayedCount; context.Input(Vector2.zero, Key.Space); yield return context.Wait(.15f); context.ReleaseInput();
        Check(view.PlayedCount == count + 1 && view.LastKind == PlayerVoiceService.VoiceKind.Jump, "Accepted actual jump triggers one voice");
        yield return context.Wait(1.5f);
        var health = context.Actor.Health; count = view.PlayedCount;
        health.TakeDamage(new DamageInfo(0, context.Actor.transform.position, isDamageOverTime: true));
        Check(view.PlayedCount == count, "Zero HP loss stays silent");
        health.TakeDamage(new DamageInfo(5, context.Actor.transform.position, isDamageOverTime: true));
        Check(view.PlayedCount == count + 1 && view.LastKind == PlayerVoiceService.VoiceKind.Hurt, "Actual nonfatal damage triggers hurt");
        yield return context.Wait(.3f);
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "in-game.png")); yield return Frames(5);
        count = view.PlayedCount;
        health.TakeDamage(new DamageInfo(100000, context.Actor.transform.position, isDamageOverTime: true));
        Check(health.IsDead && view.PlayedCount == count + 1 && view.LastKind == PlayerVoiceService.VoiceKind.Death, "Lethal damage plays only death, replacing previous voice");
        PlayerVoiceService.Attack();
        Check(view.PlayedCount == count + 1, "Dead actor cannot trigger attack voice");
    }
    private static float[] ScreenRect(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var a=RectTransformUtility.WorldToScreenPoint(null,corners[0]); var b=RectTransformUtility.WorldToScreenPoint(null,corners[2]);
        return new[]{a.x,Screen.height-b.y,b.x-a.x,b.y-a.y};
    }
    private static void Finish(Exception error)
    {
        EditorApplication.update -= Begin; EditorApplication.update -= Tick;
        try { context?.Dispose(); }
        catch (Exception cleanup) { error = error == null ? cleanup : new AggregateException(error, cleanup); }
        finally
        {
            context = null; tasks.Clear(); SessionState.EraseString(K + "pending");
            SessionState.SetString(K + "result", JsonConvert.SerializeObject(new { status = error == null ? "PASS" : "FAIL", count = checks.Count, checks, error = error?.ToString() }, Formatting.Indented));
            if (EditorApplication.isPlayingOrWillChangePlaymode && OwnPlay) EditorApplication.ExitPlaymode();
            EditorApplication.update -= Return; EditorApplication.update += Return;
        }
    }
    private static void State(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || SessionState.GetString(K + "return", "") == "") return;
        if (SessionState.GetString(K + "result", "") == "") Finish(new Exception("Owned verification interrupted"));
        EditorApplication.update -= Return; EditorApplication.update += Return;
    }
    private static void Return()
    {
        output = SessionState.GetString(K + "return", ""); if (output == "") { EditorApplication.update -= Return; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || IsolatedSavePlayGuard.ActiveDirectory != "") return;
        string env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
        if (env.Length > 0 && env != Path.Combine(output, "Account")) { EditorApplication.update -= Return; return; }
        string start = SessionState.GetString(K + "start", "");
        EditorSceneManager.playModeStartScene = start == "" ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        if (Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY") == Path.Combine(output, "Settings")) Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", null);
        Application.runInBackground = SessionState.GetBool(K + "background", false); IsolatedSavePlayGuard.UseRealAccount();
        File.WriteAllText(Path.Combine(output, "play.json"), SessionState.GetString(K + "result", ""));
        Write("return.json", new { status = "PASS", playing = EditorApplication.isPlaying, transitioning = EditorApplication.isPlayingOrWillChangePlaymode,
            guardReady = !IsolatedSavePlayGuard.RequiresAccountChoice, save = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
            scenes = Scenes(), input = InputSystem.settings.GetInstanceID(), start = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) });
        foreach (string key in new[] { "return", "start", "result", "pending" }) SessionState.EraseString(K + key);
        SessionState.EraseFloat(K + "deadline"); SessionState.EraseBool(K + "background"); EditorApplication.update -= Return;
    }
}
#endif
