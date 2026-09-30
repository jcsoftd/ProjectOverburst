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
            deadline = EditorApplication.timeSinceStartup + 600;
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
        // 4단계: 옛 HUD 디버그 패널(HUDCanvas/DebugPanel)과 분대 오버레이가 씬에서 지워졌고, 빠진 스크립트가 없다.
        GameObject hudCanvas = GameObject.Find("HUDCanvas");
        Check("old debug panel removed", hudCanvas != null && hudCanvas.transform.Find("DebugPanel") == null);
        Check("old squad overlay removed", hudCanvas != null && hudCanvas.transform.Find("EnemySquadDebugOverlayUI") == null);
        int missingScripts = hudCanvas == null ? -1 : hudCanvas.GetComponentsInChildren<Transform>(true)
            .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
        Check("HUD has no missing scripts", missingScripts == 0, "missing " + missingScripts);
        Check("notepad still reachable", Object.FindFirstObjectByType<DeveloperNotepadUI>(FindObjectsInactive.Include) != null);
        yield return Shot("hideout_old_debug_ui_removed");
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
        var notBlocked = new List<string>();
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
            if (!GameplayInputBlocker.IsGameplayInputBlocked)
                notBlocked.Add(id);
            blockedOverWindow &= GameplayInputBlocker.IsGameplayInputBlocked;
            bool afterFirst = read();
            yield return Click(button);
            bool afterSecond = read();
            Check("UI click toggles " + id, afterFirst == !before && afterSecond == before,
                $"before={before} first={afterFirst} second={afterSecond}");
        }
        Check("pointer over window blocks gameplay input", blockedOverWindow, "not blocked at: " + string.Join(",", notBlocked)
            + " · pointer " + (Mouse.current != null ? Mouse.current.position.ReadValue().ToString() : "-"));
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

        // 2단계: 플레이어·아이템·던전·적·시스템 추가 항목
        yield return Stage2();

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

    // ── 2단계 ────────────────────────────────────────────────────

    private static T Item<T>(string id) where T : DebugItem => DebugRegistry.Find(id) as T;

    private static void Pick(string id, string label)
    {
        var item = Item<DebugOptionsItem>(id);
        if (item == null)
            return;
        for (int i = 0; i < item.OptionCount; i++)
        {
            if (item.OptionLabel(i) == label)
            {
                item.Choose(i);
                return;
            }
        }
    }

    private static DebugRuntime.Record LastRecord => DebugRuntime.History.Count > 0
        ? DebugRuntime.History[DebugRuntime.History.Count - 1]
        : default;

    private static IEnumerator Press(string id, int index, float wait = 0.2f)
    {
        Item<DebugButtons>(id)?.Press(index);
        yield return Wait(wait);
    }

    private static int FloorPickups()
    {
        var list = new List<WorldItemPickup>();
        WorldItemPickup.CopyActivePickups(list);
        return list.Count;
    }

    private static int CurrencyPickups() => Object.FindObjectsByType<CurrencyWorldPickup>(FindObjectsSortMode.None)
        .Count(p => p != null && p.isActiveAndEnabled);

    private static int InventoryItems()
    {
        PlayerInventory inventory = PlayerContext.Instance?.CurrentActorInventory;
        return inventory != null ? inventory.Items.Count(i => i != null && i.HasValidBaseData) : -1;
    }

    private static int StashItems()
    {
        PlayerStash stash = PlayerAccountInventoryService.SharedStash;
        if (stash == null)
            return -1;
        int count = 0;
        for (int tab = 0; tab < stash.TabCount; tab++)
            count += stash.GetItemsInTab(tab).Count(i => i != null && i.HasValidBaseData);
        return count;
    }

    private static IEnumerator WaitUntil(Func<bool> condition, float timeout)
    {
        float until = Time.unscaledTime + timeout;
        while (!condition() && Time.unscaledTime < until)
            yield return null;
    }

    private static IEnumerator Stage2()
    {
        string[] expected =
        {
            "player.survival.fillHealth", "player.move.teleportCursor", "player.growth.experience", "player.growth.raise",
            "player.gear.grant", "player.flask.actions", "items.create.go", "items.drop.roll", "items.currency.add",
            "items.floor.actions", "items.merchant.actions", "world.dungeon.enter", "world.return.hideout",
            "enemies.control.killAll", "spawn.mass.spawn", "presentation.screen.reset", "system.perf.record",
            "system.save.account", "system.window.notepad"
        };
        foreach (string id in expected)
            Check("stage2 registered " + id, DebugRegistry.Find(id) != null);

        PlayerActorRuntime actor = PlayerContext.Instance?.CurrentActor;
        Check("stage2 player exists", actor != null);
        if (actor == null)
            yield break;

        // 체력 가득
        CombatHealth health = actor.Health;
        health.TakeDamage(new DamageInfo(health.MaxHp * 0.3f, actor.transform.position));
        yield return Frames(2);
        float damaged = health.CurrentHp;
        Item<DebugButtons>("player.survival.fillHealth")?.Press(0);
        float filled = health.CurrentHp;
        yield return Wait(0.2f);
        Check("fill health", damaged < health.MaxHp && Mathf.Approximately(filled, health.MaxHp),
            $"{damaged:0} -> {filled:0}/{health.MaxHp:0} (0.2초 뒤 {health.CurrentHp:0})");

        // F3 순간이동(창 닫힌 상태, 화면 오른쪽 아래 바닥)
        Vector3 before = actor.transform.position;
        MouseAt(new Vector2(Screen.width * 0.72f, Screen.height * 0.3f));
        yield return Frames(3);
        yield return Press(Key.F3);
        yield return Frames(2);
        float moved = Vector3.Distance(before, actor.transform.position);
        Check("F3 teleports to cursor", moved > 1f, $"moved {moved:0.0}m, {LastRecord.Text} {LastRecord.Message}");
        MouseAt(new Vector2(Screen.width * 0.3f, Screen.height * 0.45f));
        yield return Frames(2);

        // 경험치와 목표 레벨
        PlayerProgression progression = PlayerProgression.Current;
        int levelBefore = progression.Level;
        int expBefore = progression.Experience;
        yield return Press("player.growth.experience", 0, 0.1f);
        yield return WaitUntil(() => progression.Level > levelBefore || progression.Experience > expBefore, 3f);
        Check("experience +100", progression.Level > levelBefore || progression.Experience > expBefore,
            $"Lv{levelBefore}/{expBefore} -> Lv{progression.Level}/{progression.Experience}");
        int target = Mathf.Min(OverburstGrowthRules.MaximumLevel, progression.Level + 3);
        Item<DebugNumber>("player.growth.targetLevel")?.SetValue(target);
        yield return Frames(2);
        yield return Press("player.growth.raise", 0, 0.1f);
        yield return WaitUntil(() => progression.Level == target, 3f);
        Check("raise to target level", progression.Level == target, $"target {target}, now {progression.Level}");

        // 물약·장비 버튼은 예외 없이 결과를 남겨야 한다
        int historyBefore = DebugRuntime.History.Count;
        yield return Press("player.flask.actions", 0);
        yield return Press("player.gear.grant", 1);
        Check("flask/gear buttons record results", DebugRuntime.History.Count >= Math.Min(DebugRuntime.HistoryLimit, historyBefore + 2));

        // 아이템 만들기: 인벤토리 2개 · 바닥 3개 · 창고 1개
        DebugHub.OpenTab(DebugTabs.Items);
        yield return Wait(0.35f);
        Pick("items.create.category", "무기");
        Pick("items.create.destination", "인벤토리");
        Item<DebugNumber>("items.create.count")?.SetValue(2);
        Item<DebugNumber>("items.create.level")?.SetValue(12);
        yield return Wait(0.3f);
        yield return Shot("items_create");
        int inventoryBefore = InventoryItems();
        yield return Press("items.create.go", 0, 0.4f);
        Check("create 2 weapons in inventory", InventoryItems() == inventoryBefore + 2,
            $"{inventoryBefore} -> {InventoryItems()} · {LastRecord.Message}");
        Item<DebugNumber>("items.create.count")?.SetValue(3);
        Pick("items.create.destination", "바닥");
        int floorBefore = FloorPickups();
        yield return Press("items.create.go", 0, 0.5f);
        Check("create 3 items on floor", FloorPickups() == floorBefore + 3, $"{floorBefore} -> {FloorPickups()}");
        Item<DebugNumber>("items.create.count")?.SetValue(1);
        Pick("items.create.destination", "창고");
        int stashBefore = StashItems();
        yield return Press("items.create.go", 0, 0.4f);
        Check("create 1 item in stash", StashItems() == stashBefore + 1, $"{stashBefore} -> {StashItems()}");
        Pick("items.create.category", "지도");
        Pick("items.create.destination", "인벤토리");
        inventoryBefore = InventoryItems();
        yield return Press("items.create.go", 0, 0.4f);
        Check("create map item with map state", InventoryItems() == inventoryBefore + 1
            && PlayerContext.Instance.CurrentActorInventory.Items.Any(i => i != null && i.baseData is MapItemData && i.mapState != null),
            LastRecord.Message);

        // 목록 고르기: UI로 열어 검색 → 결과 클릭
        Pick("items.create.category", "전체");
        yield return Wait(0.3f);
        RectTransform openButton = FindActive("Row items.create.item")?.Find("Line/Content/Open") as RectTransform;
        if (openButton != null)
        {
            MouseAt(new Vector2(Screen.width * 0.3f, Screen.height * 0.45f));
            yield return Click(openButton);
            yield return Wait(0.3f);
        }
        RectTransform pickerPanel = FindActive("Row items.create.item")?.Find("Picker") as RectTransform;
        Check("picker opens with options", pickerPanel != null && pickerPanel.gameObject.activeSelf
            && pickerPanel.GetComponentsInChildren<UnityEngine.UI.Button>().Length > 0);
        yield return Shot("items_picker");
        if (pickerPanel != null)
        {
            TMP_InputField pickerSearch = pickerPanel.Find("Search")?.GetComponent<TMP_InputField>();
            if (pickerSearch != null)
                pickerSearch.text = "가방";
            yield return Wait(0.2f);
            RectTransform first = pickerPanel.Find("List")?.Cast<Transform>().FirstOrDefault(t => t.gameObject.activeSelf && t.name.StartsWith("Option")) as RectTransform;
            if (first != null)
                yield return Click(first);
            yield return Wait(0.3f);
            string chosen = Item<DebugOptionsItem>("items.create.item")?.CurrentLabel ?? "";
            Check("picker search + click selects item", !pickerPanel.gameObject.activeSelf && chosen.Contains("가방"), chosen);
        }
        MouseAt(new Vector2(Screen.width * 0.3f, Screen.height * 0.45f));
        yield return Frames(2);

        // 드롭 시험: 보스(장비 100%) 1회 굴림 + 1000회 모의(난수 상태 보존)
        Pick("items.drop.monster", "보스");
        Item<DebugNumber>("items.drop.mapLevel")?.SetValue(20);
        floorBefore = FloorPickups();
        yield return Press("items.drop.roll", 0, 0.4f);
        Check("boss drop roll places items", FloorPickups() >= floorBefore + 1, $"{floorBefore} -> {FloorPickups()} · {LastRecord.Message}");
        // 모의는 같은 프레임 안에서 동기로 끝나므로, 게임의 다른 난수 사용이 끼기 전에 바로 비교한다.
        string stateBefore = JsonUtility.ToJson(UnityEngine.Random.state);
        Item<DebugButtons>("items.drop.roll")?.Press(1);
        bool restored = JsonUtility.ToJson(UnityEngine.Random.state) == stateBefore;
        yield return Wait(0.3f);
        string summary = Item<DebugReadout>("items.drop.summary")?.Value ?? "";
        var bar = Item<DebugBar>("items.drop.distribution");
        Check("1000 simulation fills distribution", summary.Contains("1000마리") && bar != null && bar.Segments.Length > 0, summary);
        Check("simulation restores random state", restored);
        DebugHub.OpenTab(DebugTabs.Items);
        yield return Wait(0.35f);
        RectTransform dropRow = FindActive("Row items.drop.distribution");
        if (dropRow != null)
        {
            yield return FocusRow("items.drop.distribution");
        }
        yield return Shot("items_drop_simulation");

        // 재화: 격리 계정은 바로 더하고, 실제 계정이면 확인창을 띄운다
        StashCurrencyService currency = PlayerAccountInventoryService.SharedCurrencyService;
        int goldBefore = currency != null ? currency.GetAmount(CurrencyType.Gold) : -1;
        yield return Press("items.currency.add", 1, 0.3f);
        int goldAfter = currency != null ? currency.GetAmount(CurrencyType.Gold) : -1;
        Check("currency +1000 on isolated account", goldAfter == goldBefore + 1000, $"{goldBefore} -> {goldAfter}");
        string isolatedDirectory = Environment.GetEnvironmentVariable(DebugAccount.SaveDirectoryVariable);
        Environment.SetEnvironmentVariable(DebugAccount.SaveDirectoryVariable, null);
        yield return Press("items.currency.add", 0, 0.3f);
        RectTransform confirmBar = ByPath("Debug Window/Confirm");
        bool asked = confirmBar != null && confirmBar.gameObject.activeSelf;
        int goldAsked = currency != null ? currency.GetAmount(CurrencyType.Gold) : -1;
        Environment.SetEnvironmentVariable(DebugAccount.SaveDirectoryVariable, isolatedDirectory);
        yield return Shot("real_account_confirm");
        RectTransform cancel = confirmBar != null ? confirmBar.Find("Cancel") as RectTransform : null;
        if (cancel != null)
            yield return Click(cancel);
        MouseAt(new Vector2(Screen.width * 0.3f, Screen.height * 0.45f));
        yield return Frames(3);
        Check("real account asks before currency and cancel keeps amount", asked && goldAsked == goldAfter
            && currency.GetAmount(CurrencyType.Gold) == goldAfter && !confirmBar.gameObject.activeSelf);
        int coinsBefore = CurrencyPickups();
        yield return Press("items.currency.drop", 0, 0.4f);
        Check("currency dropped on floor", CurrencyPickups() == coinsBefore + 1, $"{coinsBefore} -> {CurrencyPickups()}");

        // 바닥 정리
        int floorNow = FloorPickups();
        yield return Press("items.floor.actions", 0, 0.5f);
        Check("pickup all reduces floor items", FloorPickups() < floorNow || floorNow == 0, $"{floorNow} -> {FloorPickups()} · {LastRecord.Message}");
        yield return Press("items.floor.actions", 1, 0.5f);
        Check("clear all empties floor", FloorPickups() == 0 && CurrencyPickups() == 0, $"items {FloorPickups()} coins {CurrencyPickups()}");

        // 상인·화면·메모장: 예외 없이 결과를 남긴다
        historyBefore = DebugRuntime.History.Count;
        yield return Press("items.merchant.actions", 0);
        yield return Press("items.merchant.actions", 1);
        yield return Press("presentation.screen.reset", 0);
        yield return Press("presentation.screen.reset", 1);
        yield return Press("system.window.notepad", 0);
        bool notepadOpened = LastRecord.Success;
        if (notepadOpened)
            yield return Press("system.window.notepad", 0);
        Check("merchant/screen/notepad buttons record results",
            DebugRuntime.History.Count >= Math.Min(DebugRuntime.HistoryLimit, historyBefore + 5), $"notepad {(notepadOpened ? "toggled" : LastRecord.Message)}");

        // 대량 스폰 30 → 모두 처치
        int enemiesBefore = EnemyAIController.AliveEnemyCount;
        yield return Press("spawn.mass.spawn", 0, 0.1f);
        yield return WaitUntil(() => EnemyAIController.AliveEnemyCount >= enemiesBefore + 25, 6f);
        int spawned = EnemyAIController.AliveEnemyCount - enemiesBefore;
        Check("mass spawn 30", spawned >= 25, $"+{spawned}");
        Pick("enemies.control.range", "전체");
        yield return Press("enemies.control.killAll", 0, 0.2f);
        yield return WaitUntil(() => EnemyAIController.AliveEnemyCount <= 2, 6f);
        Check("kill all enemies (boss excluded)", EnemyAIController.AliveEnemyCount <= 2,
            $"alive {EnemyAIController.AliveEnemyCount} · {LastRecord.Message}");

        // 성능 측정 10초
        yield return Press("system.perf.record", 0, 0.1f);
        yield return WaitUntil(() => !DebugPerfRecorder.Running, 14f);
        Check("perf recorder 10s summary + csv", DebugPerfRecorder.LastSummary.Contains("평균")
            && !string.IsNullOrEmpty(DebugPerfRecorder.LastCsvPath) && File.Exists(DebugPerfRecorder.LastCsvPath),
            DebugPerfRecorder.LastSummary);
        notes.Add("perf: " + DebugPerfRecorder.LastSummary);

        // 던전 입장(Lv.3, 장비 8개) → 도착 → 포기 귀환(확인창) → 하이드아웃
        DebugHub.OpenTab(DebugTabs.World);
        yield return Wait(0.35f);
        yield return Shot("world_tab");
        Item<DebugNumber>("world.dungeon.level")?.SetValue(3);
        yield return Frames(2);
        yield return Press("world.dungeon.enter", 0, 0.3f);
        Check("dungeon entry accepted", LastRecord.Success, LastRecord.Message);
        yield return WaitUntil(() => WorldSessionState.Phase == WorldPhase.Run && !PersistentSceneFlow.Instance.IsSwitching
            && FloorPickups() >= 8, 60f);
        yield return Wait(1f);
        Check("arrived in dungeon with 8 drops", WorldSessionState.Phase == WorldPhase.Run && FloorPickups() >= 8,
            $"phase {WorldSessionState.Phase} drops {FloorPickups()}");
        DebugHub.Close();
        yield return Wait(0.4f);
        yield return Shot("dungeon_arrival");
        DebugHub.OpenTab(DebugTabs.World);
        yield return Wait(0.35f);
        yield return Press("world.return.hideout", 0, 0.3f);
        confirmBar = ByPath("Debug Window/Confirm");
        Check("return from run asks first", confirmBar != null && confirmBar.gameObject.activeSelf && WorldSessionState.Phase == WorldPhase.Run);
        yield return Shot("return_confirm");
        RectTransform run = confirmBar != null ? confirmBar.Find("Run") as RectTransform : null;
        if (run != null)
            yield return Click(run);
        MouseAt(new Vector2(Screen.width * 0.3f, Screen.height * 0.45f));
        yield return WaitUntil(() => WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching, 60f);
        yield return Wait(1f);
        Check("abandon returns to hideout", WorldSessionState.IsHideout, "phase " + WorldSessionState.Phase);
        Check("window survives scene round trip", DebugHub.Instance != null && DebugRegistry.Find("world.dungeon.enter") != null);
        DebugHub.Close();
        yield return Wait(0.3f);
        yield return Stage3();
    }

    // ── 3단계 ────────────────────────────────────────────────────

    private static IEnumerator Stage3()
    {
        MouseAt(new Vector2(Screen.width * 0.3f, Screen.height * 0.45f));
        yield return Frames(2);

        // 기본 핀 = 옛 분대 오버레이 값(Run에서 핀 설정을 비웠으므로 기본값이 쓰인다)
        foreach (string id in DebugPrefs.DefaultPins)
            Check("default pin registered " + id, DebugRegistry.Find(id) != null && DebugPrefs.IsPinned(id));
        yield return Wait(0.5f);
        RectTransform overlay = ByPath("Debug Pin Overlay");
        string overlayText = overlay != null ? string.Join(" | ", overlay.GetComponentsInChildren<TMP_Text>().Select(t => t.text)) : "";
        Check("overlay shows squad values like old overlay", overlayText.Contains("총 몬스터") && overlayText.Contains("어그로")
            && overlayText.Contains(EnemyAIController.AliveEnemyCount.ToString()), overlayText);
        yield return Shot("default_pins");

        // 테마 5종 × 규모 4종: 서비스 숫자가 규칙(EnemyThemeTrialPresets)과 같고, 옛 UI 표와 같은 순서
        var entries = EnemyThemeTrialService.Entries;
        Check("theme catalog has 5 themes", entries.Count == 5, string.Join(",", entries.Select(e => e.ShortName)));
        // 옛 HUD 패널 순서(거미·독낭·원시·암굴, Awake 때 사령을 5번째로 붙임). 검증기 손잡이도 같은 표를 준다.
        string[] oldOrder = { "SpiderBrood", "VenomBrood", "PrimalHunt", "CavernMutants", "DeathHarvest" };
        EnemyThemeTable[] harnessTables = EnemyThemeTrialHarness.Current.tables;
        bool sameOrder = entries.Count == oldOrder.Length && harnessTables.Length == entries.Count
            && Enumerable.Range(0, entries.Count).All(i => entries[i].Id == oldOrder[i] && harnessTables[i] == entries[i].Table);
        Check("theme order matches old debug UI", sameOrder, string.Join(",", entries.Select(e => e.Id)));
        int mismatches = 0;
        foreach (EnemyThemeTrialMode mode in Enum.GetValues(typeof(EnemyThemeTrialMode)))
        {
            EnemyThemeTrialService.SetMode(mode);
            foreach (var entry in entries)
                if (EnemyThemeTrialService.CountOf(entry) != EnemyThemeTrialPresets.Resolve(entry.Table, mode).Total)
                    mismatches++;
        }
        EnemyThemeTrialService.SetMode(EnemyThemeTrialMode.Normal);
        Check("theme counts 5x4 match presets", mismatches == 0, "mismatches " + mismatches);

        // 테마 1회 시험 → 소환 → 진행 중 잠금 → 정리
        DebugHub.OpenTab(DebugTabs.Spawn);
        yield return Wait(0.35f);
        int before = EnemyAIController.AliveEnemyCount;
        yield return Press("spawn.theme.SpiderBrood", 0, 0.1f);
        yield return WaitUntil(() => EnemyAIController.AliveEnemyCount > before, 8f);
        Check("theme trial spawns", EnemyAIController.AliveEnemyCount > before, LastRecord.Message);
        Check("buttons lock while busy", EnemyThemeTrialService.Busy && !(DebugRegistry.Find("spawn.theme.VenomBrood")?.IsEnabled ?? true));
        yield return Wait(0.5f);
        yield return Shot("theme_trial_running");
        yield return Press("spawn.theme.actions", 0, 0.5f);
        yield return WaitUntil(() => !EnemyThemeTrialService.Busy, 5f);
        Check("theme trial clear", !EnemyThemeTrialService.Busy, "alive " + EnemyAIController.AliveEnemyCount);

        // 독립 시험장: 입장(구역 5개, 하이드아웃 스폰 꺼짐, 보호) → 복귀(값 복원)
        CombatDebugSettings.SetHideoutMonsterSpawn(false);
        Vector3 home = DebugTeleport.Player.position;
        yield return Press("spawn.theme.actions", 1, 0.8f);
        Check("arena entered with 5 zones", EnemyThemeTrialService.InArena && EnemyThemeTrialService.ArenaZoneCount == 5
            && Vector3.Distance(home, DebugTeleport.Player.position) > 100f, LastRecord.Message);
        CombatHealth hp = PlayerContext.Instance.CurrentActorHealth;
        hp.TakeDamage(new DamageInfo(hp.MaxHp * 10f, hp.transform.position));
        yield return Frames(2);
        Check("arena protects player (min hp 1)", !hp.IsDead && hp.CurrentHp >= 1f, $"hp {hp.CurrentHp:0}");
        hp.ResetHealth();
        yield return Shot("theme_arena");
        yield return Press("spawn.theme.actions", 1, 0.8f);
        Check("arena exit restores position", !EnemyThemeTrialService.InArena && Vector3.Distance(home, DebugTeleport.Player.position) < 1f);

        // 데미지 숫자: A/B 슬롯 전환이 5개 값을 한 번에 바꾼다
        var preset = Item<DebugOptionsItem>("presentation.damage.preset");
        var styled = Item<DebugToggle>("presentation.damage.styled");
        DamageNumberFeelPreset presetBefore = DamageNumberPopup.SelectedPreset;
        bool styledBefore = DamageNumberStyleSettings.Enabled;
        yield return Press("presentation.damage.slots", 0);
        preset?.Choose((int)(presetBefore == DamageNumberFeelPreset.Pop ? DamageNumberFeelPreset.Burst : DamageNumberFeelPreset.Pop));
        styled?.Flip();
        yield return Frames(2);
        DamageNumberFeelPreset presetB = DamageNumberPopup.SelectedPreset;
        yield return Press("presentation.damage.slots", 1);
        yield return Press("presentation.damage.slots", 2);
        bool atB = DamageNumberPopup.SelectedPreset == presetB && DamageNumberStyleSettings.Enabled != styledBefore;
        yield return Press("presentation.damage.slots", 2);
        bool atA = DamageNumberPopup.SelectedPreset == presetBefore && DamageNumberStyleSettings.Enabled == styledBefore;
        Check("damage number A/B swap applies all values", atB && atA, $"A {presetBefore}/{styledBefore} B {presetB}");
        DebugHub.OpenTab(DebugTabs.Presentation);
        yield return Wait(0.35f);
        yield return Shot("presentation_damage");

        // DPS 측정기: 1마리, 10마리
        foreach (int count in new[] { 1, 10 })
        {
            Pick("combat.dps.count", count + "마리");
            yield return Press("combat.dps.spawn", 0, 1.0f);
            Check($"dps spawns {count} dummies", DpsMeterDebugModule.AliveDummies == count, LastRecord.Message);
            var targets = UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Where(e => e.IsLeased && e.AI != null && !e.AI.enabled).ToList();
            for (int k = 0; k < 5; k++)
            {
                foreach (EnemyActor target in targets)
                    target.Health.TakeDamage(new DamageInfo(1000f, target.transform.position, DebugTeleport.Player.gameObject,
                        Vector3.forward, isCritical: k == 0, playerAttackKind: PlayerAttackKind.Weak));
                yield return Wait(0.2f);
            }
            string dps = Item<DebugReadout>("combat.dps.value")?.Value ?? "";
            string totalText = Item<DebugReadout>("combat.dps.total")?.Value ?? "";
            Check($"dps meter records {count} targets", DpsMeterDebugModule.Hits == count * 5 && DpsMeterDebugModule.TotalDamage > 0
                && totalText.Contains($"맞은 대상 {count}/{count}") && targets.All(t => !t.Health.IsDead), $"{dps} | {totalText}");
            notes.Add($"dps {count}: {dps} | {totalText}");
            DebugHub.OpenTab(DebugTabs.Combat);
            yield return Wait(0.35f);
            yield return Shot($"dps_{count}");
        }
        yield return Press("combat.dps.spawn", 1, 0.5f);
        Check("dps dummies cleared", DpsMeterDebugModule.AliveDummies == 0);

        // 5단계: 런 드롭 확률 강제(기본값은 규칙 그대로)
        var lootOverride = Item<DebugOptionsItem>("items.drop.override");
        Check("loot override registered, default rule", lootOverride != null && CombatDebugSettings.RunLootOverride == RunLootDebugOverride.Default);
        Pick("items.drop.override", "항상");
        yield return Frames(1);
        int always = Enumerable.Range(0, 50).Count(_ => FlaskLootPolicy.Roll(EnemyGradeType.Normal, 10) != null && GearLootPolicy.Roll(EnemyGradeType.Normal, 10) != null);
        Pick("items.drop.override", "안 떨어짐");
        yield return Frames(1);
        int never = Enumerable.Range(0, 50).Count(_ => FlaskLootPolicy.Roll(EnemyGradeType.Boss, 10) != null || GearLootPolicy.Roll(EnemyGradeType.Boss, 10) != null);
        Pick("items.drop.override", "규칙대로");
        yield return Frames(1);
        Check("loot override always/never/default", always == 50 && never == 0 && CombatDebugSettings.RunLootOverride == RunLootDebugOverride.Default,
            $"always {always}/50 never {never}/50");

        // 5단계: 원소 에너지 가득·비우기·가득 유지(강공 방출 뒤에도 남는지), 끄면 규칙대로 0
        PlayerEquipment equipment = PlayerContext.Instance.CurrentActor.Equipment;
        if (equipment != null && equipment.CurrentWeaponItem == null)
            yield return Press("player.gear.grant", 2, 0.4f); // 인벤토리 첫 무기 장착(2단계에서 만든 무기)
        if (equipment != null && equipment.CurrentWeaponItem == null)
        {
            yield return Press("player.gear.grant", 0, 0.4f); // 시작 무기 지급
            yield return Press("player.gear.grant", 2, 0.4f);
        }
        ItemData weaponItem = equipment != null ? equipment.CurrentWeaponItem : null;
        string elementBefore = weaponItem != null ? weaponItem.ResolvedElement.ToString() : "no weapon";
        if (weaponItem != null && !OverburstElementRules.IsActive(weaponItem.ResolvedElement))
            weaponItem.TryAssignElementOnce(WeaponElement.Fire); // 격리 계정 무기에 원소가 없을 때만 시험용 불 원소
        Check("energy readout before first hit", (Item<DebugReadout>("combat.element.energy")?.Value ?? "") != "", Item<DebugReadout>("combat.element.energy")?.Value);
        DebugHub.OpenTab(DebugTabs.Combat);
        yield return Press("combat.element.energyActions", 0);
        OverburstElementEnergy energy = equipment != null ? equipment.GetComponent<OverburstElementEnergy>() : null;
        notes.Add($"energy weapon element {elementBefore} -> {(energy != null ? energy.Element.ToString() : "no energy")}");
        float filled = energy != null ? energy.Amount : -1f;
        string energyText = Item<DebugReadout>("combat.element.energy")?.Value ?? "";
        Check("energy fill to base max", energy != null && OverburstElementRules.IsActive(energy.Element)
            && Mathf.Approximately(filled, energy.BaseMaximum) && energyText.Contains("/ " + energy.BaseMaximum.ToString("0")),
            $"{filled:0} · {energyText}");
        yield return Press("combat.element.energyActions", 1);
        Check("energy empty", energy != null && energy.Amount == 0f, energy != null ? energy.Amount.ToString("0") : "-");
        Item<DebugToggle>("combat.element.holdFull")?.Flip();
        yield return Frames(1);
        bool heldOnEnable = energy != null && Mathf.Approximately(energy.Amount, energy.BaseMaximum);
        bool committed = energy != null && EndDischarge(energy.TryCommitDischarge(100f, out OverburstElementDischarge held) ? held : null);
        bool keptAfterDischarge = energy != null && Mathf.Approximately(energy.Amount, energy.BaseMaximum);
        yield return Wait(0.3f);
        yield return Shot("combat_element");
        Item<DebugToggle>("combat.element.holdFull")?.Flip();
        yield return Frames(1);
        bool committedOff = energy != null && EndDischarge(energy.TryCommitDischarge(100f, out OverburstElementDischarge rule) ? rule : null);
        bool zeroAfterOff = energy != null && energy.Amount == 0f;
        Check("hold keeps energy full through discharge, off follows rule", heldOnEnable && committed && keptAfterDischarge
            && committedOff && zeroAfterOff && !ElementEnergyDebug.Hold,
            $"on {heldOnEnable} commit {committed} kept {keptAfterDischarge} off {committedOff}/{zeroAfterOff}");
        yield return Press("combat.element.clearStatuses", 0);
        Check("clear enemy element statuses runs", LastRecord.Success, LastRecord.Message);
        DebugHub.Close();
        yield return Wait(0.3f);
    }

    private static bool EndDischarge(OverburstElementDischarge discharge)
    {
        discharge?.End();
        return discharge != null;
    }

    private static IEnumerator FocusRow(string id)
    {
        DebugItem item = DebugRegistry.Find(id);
        if (item == null)
            yield break;
        MethodInfo jump = typeof(DebugHub).GetMethod("JumpTo", BindingFlags.Instance | BindingFlags.NonPublic);
        jump?.Invoke(DebugHub.Instance, new object[] { id });
        yield return Wait(0.4f);
    }
}
