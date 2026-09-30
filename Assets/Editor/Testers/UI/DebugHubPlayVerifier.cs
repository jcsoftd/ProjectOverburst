using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.DebugTools;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>
/// 디버그 창 1단계(90C 11절) Play 검증. 격리 계정 하이드아웃에서 F1 열기/닫기, 토글 6개 UI 클릭, 창 위 입력 차단,
/// 배속·일시정지·한 프레임, 검색, 설정 묶음, 핀 오버레이(F2), 확인창, 로그 수집, 닫힌 창 비용을 확인하고 화면을 찍는다.
/// 가상 키보드·마우스 입력이 Game 창 포커스와 상관없이 들어가도록 검증 동안만 InputSettings 사본을 쓴다(설정 에셋은 바꾸지 않는다).
/// OB.Debug.* PlayerPrefs는 시작 전에 적어 두고 비운 뒤, Edit 모드로 돌아오면 되돌린다. 씬·에셋은 저장하지 않는다.
/// 실행: DebugHubPlayVerifier.Run(출력 폴더). 결과는 출력 폴더의 results.json.
/// </summary>
[InitializeOnLoad]
public static class DebugHubPlayVerifier
{
    private const string SessionKey = "DebugHubPlayVerifier";
    private static readonly string[] PrefKeys =
    {
        "OB.Debug.Window", "OB.Debug.Overlay", "OB.Debug.Presets", "OB.Debug.Favorites",
        "OB.Debug.Recent", "OB.Debug.Pins", "OB.Debug.Sections"
    };

    private static readonly (string id, Func<bool> read)[] Toggles =
    {
        ("player.survival.damageReduction", () => CombatDebugSettings.ReduceIncomingPlayerDamageBy99_9Percent),
        ("combat.display.attackPattern", () => CombatDebugSettings.ShowAttackPatternDebug),
        ("combat.display.aimLine", () => UnifiedDebugAimLine.IsActive && UnifiedDebugAimLine.DebugLineEnabled),
        ("enemies.visual.aiState", () => CombatDebugSettings.ShowEnemyAiStateDebug),
        ("enemies.visual.squadGeometry", () => CombatDebugSettings.ShowEnemySquadGeometryDebug),
        ("spawn.mass.hideoutMonsters", () => CombatDebugSettings.SpawnHideoutMonsters),
    };

    private static IEnumerator work;
    private static int frame;
    private static double deadline;
    private static InputSettings originalInputSettings;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static readonly List<string> shots = new List<string>();
    private static readonly List<string> notes = new List<string>();
    private static readonly List<string> errors = new List<string>();
    private static readonly List<object> checks = new List<object>();
    private static int failed;

    private static string Output => SessionState.GetString(SessionKey + ".output", "");
    public static string Status => SessionState.GetString(SessionKey + ".status", "NOT_RUN");

    static DebugHubPlayVerifier()
    {
        EditorApplication.playModeStateChanged += State;
    }

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene")
            throw new InvalidOperationException("Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(SessionKey + ".output", output);
        SessionState.SetString(SessionKey + ".env", Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY") ?? "");
        var saved = new Dictionary<string, string>();
        foreach (string pref in PrefKeys)
        {
            saved[pref] = PlayerPrefs.HasKey(pref) ? PlayerPrefs.GetString(pref) : null;
            PlayerPrefs.DeleteKey(pref);
        }
        SessionState.SetString(SessionKey + ".prefs", JsonConvert.SerializeObject(saved));
        Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(SessionKey, true);
        SessionState.SetString(SessionKey + ".status", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    private static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(SessionKey, false))
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(SessionKey + ".background", Application.runInBackground);
            Application.runInBackground = true;
            originalInputSettings = InputSystem.settings;
            InputSettings temp = Object.Instantiate(originalInputSettings);
            temp.hideFlags = HideFlags.DontSave;
            temp.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            temp.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = temp;
            shots.Clear();
            notes.Clear();
            errors.Clear();
            checks.Clear();
            stack.Clear();
            failed = 0;
            frame = -1;
            deadline = EditorApplication.timeSinceStartup + 300;
            work = Verify();
            Application.logMessageReceived += Log;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            while (stack.Count > 0)
                (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose();
            work = null;
            if (originalInputSettings != null)
            {
                InputSettings temp = InputSystem.settings;
                InputSystem.settings = originalInputSettings;
                if (temp != originalInputSettings)
                    Object.DestroyImmediate(temp);
                originalInputSettings = null;
            }
            Application.runInBackground = SessionState.GetBool(SessionKey + ".background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", SessionState.GetString(SessionKey + ".env", ""));
            var saved = JsonConvert.DeserializeObject<Dictionary<string, string>>(SessionState.GetString(SessionKey + ".prefs", "{}"));
            foreach (KeyValuePair<string, string> pair in saved)
            {
                if (pair.Value == null)
                    PlayerPrefs.DeleteKey(pair.Key);
                else
                    PlayerPrefs.SetString(pair.Key, pair.Value);
            }
            PlayerPrefs.Save();
            SessionState.SetBool(SessionKey, false);
        }
    }

    private static void Log(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message + "\n" + stackTrace);
    }

    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount)
            return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new Exception("Timeout");
            if (Step())
                return;
            Finish(failed == 0 && errors.Count == 0 ? "COMPLETE" : $"FAIL checks={failed} errors={errors.Count}");
        }
        catch (Exception exception)
        {
            Finish("FAIL " + exception);
        }
    }

    private static bool Step()
    {
        if (stack.Count == 0 && work != null)
        {
            stack.Push(work);
            work = null;
        }
        while (stack.Count > 0)
        {
            IEnumerator top = stack.Peek();
            if (top.MoveNext())
            {
                if (top.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                return true;
            }
            stack.Pop();
        }
        return false;
    }

    private static void Finish(string status)
    {
        SessionState.SetString(SessionKey + ".status", status);
        if (Keyboard.current != null)
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonConvert.SerializeObject(new
        {
            status,
            screen = new[] { Screen.width, Screen.height },
            checks,
            shots,
            notes,
            errors
        }, Formatting.Indented));
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }

    private static void Check(string name, bool pass, string detail = null)
    {
        if (!pass)
            failed++;
        checks.Add(new { name, pass, detail });
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private static IEnumerator Wait(float seconds)
    {
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until)
            yield return null;
    }

    private static IEnumerator Shot(string name)
    {
        yield return null;
        string path = Path.Combine(Output, (shots.Count + 1).ToString("00") + "_" + name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        shots.Add(path);
        yield return null;
        yield return null;
    }

    private static IEnumerator Press(params Key[] keys)
    {
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
        yield return Frames(2);
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        yield return Frames(2);
    }

    private static void MouseAt(Vector2 position, bool left = false)
    {
        var mouse = new MouseState { position = position };
        if (left)
            mouse = mouse.WithButton(MouseButton.Left);
        InputSystem.QueueStateEvent(Mouse.current, mouse);
    }

    private static IEnumerator Click(RectTransform target)
    {
        Vector2 position = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.center));
        MouseAt(position);
        yield return Frames(2);
        MouseAt(position, true);
        yield return Frames(2);
        MouseAt(position);
        yield return Frames(3);
    }

    private static Transform Hub => DebugHub.Instance != null ? DebugHub.Instance.transform : null;

    private static RectTransform FindActive(string name)
    {
        Transform hub = Hub;
        if (hub == null)
            return null;
        return hub.GetComponentsInChildren<RectTransform>(true)
            .FirstOrDefault(t => t.name == name && t.gameObject.activeInHierarchy);
    }

    private static RectTransform ByPath(string path)
    {
        Transform hub = Hub;
        return hub != null ? hub.Find(path) as RectTransform : null;
    }

    private static IEnumerator Verify()
    {
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
               || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene")
            yield return null;
        if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory)
                .StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
            throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
        yield return Wait(2f);
        notes.Add($"screen {Screen.width}x{Screen.height}");

        Vector2 away = new Vector2(Screen.width * 0.3f, Screen.height * 0.45f);
        MouseAt(away);
        yield return Frames(3);

        // 1) 시작 상태와 등록
        Check("hub exists", DebugHub.Instance != null);
        Check("window closed at start", !DebugHub.IsOpen);
        Check("no gameplay block before open", !GameplayInputBlocker.IsGameplayInputBlocked);
        string[] expected =
        {
            "player.survival.damageReduction", "combat.display.attackPattern", "combat.display.aimLine",
            "enemies.visual.aiState", "enemies.visual.squadGeometry", "spawn.mass.hideoutMonsters",
            "system.time.speed", "system.time.pause", "system.time.step", "system.perf.frame",
            "system.log.recent", "presets.selected", "presets.actions"
        };
        foreach (string id in expected)
            Check("registered " + id, DebugRegistry.Find(id) != null);

        // 2) F1 열기 + 탭별 화면
        yield return Press(Key.F1);
        yield return Wait(0.4f);
        Check("F1 opens window", DebugHub.IsOpen);
        yield return Shot("combat_tab");
        foreach (string tab in new[] { DebugTabs.Player, DebugTabs.Enemies, DebugTabs.Spawn, DebugTabs.SystemTab, DebugTabs.Favorites })
        {
            DebugHub.OpenTab(tab);
            yield return Wait(0.35f);
            yield return Shot("tab_" + tab.Replace("★ ", string.Empty).Replace("·", "_"));
        }

        // 3) 토글 6개: 실제 UI 클릭(가상 마우스)으로 켜고 끈다. 창 위에 포인터가 있으면 게임 입력이 막혀야 한다.
        bool blockedOverWindow = true;
        foreach ((string id, Func<bool> read) in Toggles)
        {
            DebugItem item = DebugRegistry.Find(id);
            if (item == null)
                continue;
            DebugHub.OpenTab(item.Section.Tab);
            yield return Wait(0.3f);
            RectTransform row = FindActive("Row " + id);
            RectTransform button = row != null ? row.Find("Line/Content/Switch") as RectTransform : null;
            Check("row visible " + id, button != null);
            if (button == null)
                continue;
            bool before = read();
            yield return Click(button);
            blockedOverWindow &= GameplayInputBlocker.IsGameplayInputBlocked;
            bool afterFirst = read();
            yield return Click(button);
            bool afterSecond = read();
            Check("UI click toggles " + id, afterFirst == !before && afterSecond == before,
                $"before={before} first={afterFirst} second={afterSecond}");
        }
        Check("pointer over window blocks gameplay input", blockedOverWindow);
        MouseAt(away);
        yield return Frames(3);
        Check("pointer away releases block", !GameplayInputBlocker.IsGameplayInputBlocked);

        // 4) 배속·일시정지(F6)·한 프레임(F7)
        DebugHub.OpenTab(DebugTabs.SystemTab);
        yield return Wait(0.3f);
        var speed = (DebugOptionsItem)DebugRegistry.Find("system.time.speed");
        speed.Choose(Array.IndexOf(DebugTime.Speeds, 0.5f));
        yield return Frames(3);
        Check("speed 0.5 applied", Mathf.Approximately(Time.timeScale, 0.5f), "timeScale=" + Time.timeScale);
        yield return Press(Key.F6);
        Check("F6 pauses", Time.timeScale == 0f, "timeScale=" + Time.timeScale);
        yield return Wait(0.3f);
        yield return Shot("system_paused");
        float timeBefore = Time.time;
        yield return Press(Key.F7);
        float stepped = Time.time - timeBefore;
        Check("F7 advances exactly one short frame", stepped > 0f && stepped < 0.34f && Time.timeScale == 0f,
            $"deltaTime={stepped:0.0000} timeScale={Time.timeScale}");
        yield return Press(Key.F6);
        Check("F6 resumes at chosen speed", Mathf.Approximately(Time.timeScale, 0.5f), "timeScale=" + Time.timeScale);
        speed.Choose(Array.IndexOf(DebugTime.Speeds, 1f));
        yield return Frames(3);
        Check("speed back to 1", Mathf.Approximately(Time.timeScale, 1f), "timeScale=" + Time.timeScale);

        // 5) 검색
        TMP_InputField search = ByPath("Debug Window/Title/Search")?.GetComponent<TMP_InputField>();
        Check("search field exists", search != null);
        if (search != null)
        {
            search.text = "무적";
            yield return Wait(0.3f);
            Check("search '무적' finds damage reduction", FindActive("Row player.survival.damageReduction") != null);
            yield return Shot("search");
            search.text = string.Empty;
            yield return Frames(3);
        }

        // 6) 설정 묶음
        bool[] baseline = Toggles.Select(t => t.read()).ToArray();
        DebugResult applied = DebugPresets.Apply("전투 테스트");
        yield return Frames(2);
        Check("preset applies 3 values", applied.Success
            && CombatDebugSettings.ReduceIncomingPlayerDamageBy99_9Percent
            && CombatDebugSettings.ShowAttackPatternDebug
            && CombatDebugSettings.ShowEnemyAiStateDebug, applied.Message);
        DebugHub.OpenTab(DebugTabs.Favorites);
        yield return Wait(0.35f);
        yield return Shot("favorites_after_preset");
        DebugResult undone = DebugPresets.Undo();
        yield return Frames(2);
        Check("preset undo restores", undone.Success && Toggles.Select(t => t.read()).SequenceEqual(baseline), undone.Message);

        // 7) 핀 오버레이(F2)
        DebugPrefs.TogglePin("system.perf.frame");
        DebugHub.Close();
        yield return Wait(0.6f);
        RectTransform overlay = ByPath("Debug Pin Overlay");
        string overlayText = overlay != null ? string.Join(" | ", overlay.GetComponentsInChildren<TMP_Text>().Select(t => t.text)) : "";
        Check("overlay shows pinned frame value", overlay != null && overlay.gameObject.activeSelf && overlayText.Contains("프레임"), overlayText);
        yield return Shot("overlay_window_closed");
        yield return Press(Key.F2);
        yield return Wait(0.3f);
        Check("F2 hides overlay", overlay != null && !overlay.gameObject.activeSelf);
        yield return Press(Key.F2);
        yield return Wait(0.3f);
        Check("F2 shows overlay again", overlay != null && overlay.gameObject.activeSelf);
        DebugPrefs.TogglePin("system.perf.frame");

        // 8) 확인창(임시 항목)
        bool ran = false;
        DebugRegistry.Section(DebugTabs.SystemTab, "검증용 임시", 999)
            .Button("확인창 시험", () => ran = true)
            .WithId("verify.confirm")
            .Confirm("시험: 실행을 누르면 동작해요");
        DebugHub.OpenTab(DebugTabs.SystemTab);
        yield return Wait(0.35f);
        ((DebugButtons)DebugRegistry.Find("verify.confirm")).Press(0);
        yield return Frames(3);
        RectTransform confirm = ByPath("Debug Window/Confirm");
        Check("confirm bar shown before running", confirm != null && confirm.gameObject.activeSelf && !ran);
        yield return Shot("confirm");
        RectTransform run = confirm != null ? confirm.Find("Run") as RectTransform : null;
        if (run != null)
            yield return Click(run);
        Check("confirm run executes", ran && confirm != null && !confirm.gameObject.activeSelf);
        MouseAt(away);
        yield return Frames(3);

        // 9) 로그 수집
        int warnings = DebugLogCapture.WarningCount;
        Debug.LogWarning("[DebugHubVerifier] 경고 수집 시험");
        yield return Frames(2);
        Check("log capture counts warning", DebugLogCapture.WarningCount == warnings + 1);

        // 10) Ctrl+F 검색 포커스, Esc는 창을 닫지 않음, F1 닫기
        yield return Press(Key.LeftCtrl, Key.F);
        yield return Frames(3);
        Check("Ctrl+F focuses search", search != null && search.isFocused);
        Check("typing blocks gameplay input", GameplayInputBlocker.IsGameplayInputBlocked);
        yield return Press(Key.Escape);
        Check("Esc only leaves search", DebugHub.IsOpen && search != null && !search.isFocused);
        yield return Press(Key.F1);
        yield return Frames(2);
        Check("F1 closes window", !DebugHub.IsOpen);
        Check("closed window releases block", !GameplayInputBlocker.IsGameplayInputBlocked);

        // 11) 닫힌 창의 프레임 비용: Update를 직접 2000번 불러 1회 평균을 잰다.
        MethodInfo update = typeof(DebugHub).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        for (int i = 0; i < 50; i++)
            update.Invoke(DebugHub.Instance, null);
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 2000; i++)
            update.Invoke(DebugHub.Instance, null);
        watch.Stop();
        double closedMicros = watch.Elapsed.TotalMilliseconds * 1000.0 / 2000.0;
        notes.Add($"closed hub Update avg {closedMicros:0.00}us (reflection call included)");
        Check("closed window Update under 50us", closedMicros < 50.0, $"{closedMicros:0.00}us");
        yield return Frames(2);
    }
}
