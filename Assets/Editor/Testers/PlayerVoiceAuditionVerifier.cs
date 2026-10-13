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
        var view = PlayerVoiceAudition.Current;
        Check(view != null && UnityEngine.Object.FindObjectsByType<PlayerVoiceAudition>(FindObjectsSortMode.None).Length == 1, "One automatic persistent audition panel");
        Button[] buttons = view.GetComponentsInChildren<Button>(true);
        Check(buttons.Length == 9 && view.GetComponent<GraphicRaycaster>() != null && EventSystem.current != null, "Nine buttons, raycaster and existing EventSystem");
        var audio = view.GetComponent<AudioSource>();
        for (int candidate = 0; candidate < 4; candidate++)
        {
            buttons[candidate].onClick.Invoke(); Check(view.SelectedIndex == candidate, "Selection button " + view.Candidates[candidate].name);
            for (int kind = 0; kind < 4; kind++)
            {
                var used = new HashSet<AudioClip>(); AudioClip previous = null;
                for (int sample = 0; sample < 24; sample++)
                {
                    Check(view.Preview((PlayerVoiceAudition.VoiceKind)kind), "Playback request " + candidate + "/" + kind + "/" + sample);
                    AudioClip clip = view.LastClip;
                    if (clip == previous || !view.Candidates[candidate].Clips((PlayerVoiceAudition.VoiceKind)kind).Contains(clip)) throw new Exception("Cross-candidate/category or consecutive duplicate");
                    previous = clip; used.Add(clip);
                }
                Check(used.Count > 1, "Random variation and no consecutive repeat " + candidate + "/" + kind);
                buttons[5 + kind].onClick.Invoke();
                Check(view.LastKind == (PlayerVoiceAudition.VoiceKind)kind && audio.isPlaying, "Preview button/source playing " + candidate + "/" + kind);
                yield return context.Wait(Mathf.Min(.05f, audio.clip.length * .25f));
            }
        }
        buttons[4].onClick.Invoke(); int count = view.PlayedCount;
        Check(view.SelectedIndex == -1 && !audio.isPlaying && buttons.Skip(5).All(b => !b.interactable), "Off stops voice and disables previews");
        Check(!view.Preview(PlayerVoiceAudition.VoiceKind.Attack), "Off ignores playback");
        buttons[0].onClick.Invoke();
        var results = new List<RaycastResult>(); var buttonPoint = RectTransformUtility.WorldToScreenPoint(null, buttons[2].transform.position);
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = buttonPoint }, results);
        Check(results.Any(r => r.gameObject == buttons[2].gameObject), "Visible candidate button receives UI raycast");
        var weapon = VisualPlayContext.Items.OfType<WeaponItemData>().First(w => w.weaponClass == WeaponClass.Greatsword);
        context.Equip(weapon); yield return context.Wait(.5f);
        count = view.PlayedCount; context.MouseButton(true, false); yield return context.Wait(3.5f); context.ReleaseInput();
        Check(view.PlayedCount >= count + 4 && view.LastKind == PlayerVoiceAudition.VoiceKind.Attack, "Actual held light combo triggers each swing voice");
        context.Melee.CancelCurrentAttackState(); yield return context.Wait(.5f);
        count = view.PlayedCount; context.MouseButton(false, true); yield return context.Wait(2f); context.ReleaseInput();
        Check(view.PlayedCount > count && view.LastKind == PlayerVoiceAudition.VoiceKind.Attack, "Actual heavy attack triggers voice");
        context.Melee.CancelCurrentAttackState(); yield return context.Wait(.5f);
        yield return Wait(() => context.Actor.Movement.IsGrounded, "Player grounded for jump");
        count = view.PlayedCount; context.Input(Vector2.zero, Key.Space); yield return context.Wait(.15f); context.ReleaseInput();
        Check(view.PlayedCount == count + 1 && view.LastKind == PlayerVoiceAudition.VoiceKind.Jump, "Accepted actual jump triggers one voice");
        yield return context.Wait(1.5f);
        var health = context.Actor.Health; count = view.PlayedCount;
        health.TakeDamage(new DamageInfo(0, context.Actor.transform.position, isDamageOverTime: true));
        Check(view.PlayedCount == count, "Zero HP loss stays silent");
        health.TakeDamage(new DamageInfo(5, context.Actor.transform.position, isDamageOverTime: true));
        Check(view.PlayedCount == count + 1 && view.LastKind == PlayerVoiceAudition.VoiceKind.Hurt, "Actual nonfatal damage triggers hurt");
        yield return context.Wait(.3f);
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "in-game.png")); yield return Frames(5);
        count = view.PlayedCount;
        health.TakeDamage(new DamageInfo(100000, context.Actor.transform.position, isDamageOverTime: true));
        Check(health.IsDead && view.PlayedCount == count + 1 && view.LastKind == PlayerVoiceAudition.VoiceKind.Death, "Lethal damage plays only death, replacing previous voice");
        PlayerVoiceAudition.Attack();
        Check(view.PlayedCount == count + 1, "Dead actor cannot trigger attack voice");
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
