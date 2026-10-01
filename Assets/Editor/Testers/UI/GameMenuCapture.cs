using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// 2026-10-01 ESC 메뉴 검증: 격리 계정 Play(은신처)에서 ESC 규칙(창이 열려 있으면 창만 닫힘), 일시정지(timeScale·소리·게임 입력),
// 멈춘 동안 패링 슬로우·재사용 대기가 줄지 않는지, 설정 저장, 키 다시 정하기(겹치면 맞바꿈)를 확인하고 화면을 찍는다.
// ESC·키 입력은 실제 키보드 상태 이벤트로 넣는다. 계정·씬을 저장하지 않고, 설정 파일도 격리 폴더에만 쓴다.
[InitializeOnLoad]
public static class GameMenuCapture
{
    const string Key = "GameMenuCapture";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> shots = new List<string>(), notes = new List<string>(), errors = new List<string>();
    static readonly Dictionary<string, bool> checks = new Dictionary<string, bool>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static GameMenuCapture() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".status", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground); Application.runInBackground = true;
            shots.Clear(); notes.Clear(); errors.Clear(); checks.Clear(); stack.Clear(); frame = -1;
            deadline = EditorApplication.timeSinceStartup + 240;
            work = Capture();
            Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", "");
            SessionState.SetBool(Key, false);
        }
    }

    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(m + "\n" + s); }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout"); if (Step()) return; Finish(checks.Values.All(v => v) ? "COMPLETE" : "COMPLETE_WITH_FAILED_CHECKS"); }
        catch (Exception e) { Finish("FAIL " + e); }
    }

    static bool Step()
    {
        if (stack.Count == 0 && work != null) { stack.Push(work); work = null; }
        while (stack.Count > 0)
        {
            IEnumerator top = stack.Peek();
            if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } return true; }
            stack.Pop();
        }
        return false;
    }

    static void Finish(string status)
    {
        if (OverburstGameMenu.IsOpen) OverburstGameMenu.Instance?.Close();
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonConvert.SerializeObject(new { status, checks, shots, notes, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static void Check(string name, bool ok, string detail = "") { checks[name] = ok; notes.Add((ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? " — " + detail : "")); }

    static IEnumerator WaitReal(float seconds) { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }

    static IEnumerator Shot(string name)
    {
        yield return null; yield return null;
        string path = Path.Combine(Output, (shots.Count + 1).ToString("00") + "_" + name + ".png");
        ScreenCapture.CaptureScreenshot(path); shots.Add(path);
        yield return null; yield return null;
    }

    // 실제 키보드 상태 이벤트로 한 번 눌렀다 뗀다.
    static IEnumerator Press(Key key)
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) { notes.Add("no keyboard"); yield break; }
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null; yield return null;
    }

    static IEnumerator Capture()
    {
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
        if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
            throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
        yield return WaitReal(2f);
        var menu = OverburstGameMenu.Instance;
        Check("menu installed", menu != null);
        if (menu == null) yield break;
        notes.Add("settings file=" + OverburstGameSettings.FilePath);
        Check("settings isolated", Path.GetFullPath(OverburstGameSettings.FilePath).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase));
        var facade = PlayerInputFacade.Current;
        var ui = UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>();

        // 1) 인벤토리가 열린 상태의 ESC: 인벤토리만 닫히고 메뉴는 열리지 않는다.
        ui.inventory.SetVisible(true);
        yield return WaitReal(.3f);
        yield return Press(UnityEngine.InputSystem.Key.Escape);
        yield return WaitReal(.2f);
        bool keyboardWorks = !ui.inventory.IsVisible;
        Check("ESC closes inventory only", keyboardWorks && !OverburstGameMenu.IsOpen, "inventoryVisible=" + ui.inventory.IsVisible + " menuOpen=" + OverburstGameMenu.IsOpen);
        if (!keyboardWorks) { notes.Add("keyboard events not processed; using direct calls"); ui.inventory.SetVisible(false); yield return WaitReal(.2f); }

        // 2) 아무 창도 없을 때 ESC → 메뉴 열림·멈춤
        float scaleBefore = Time.timeScale;
        if (keyboardWorks) yield return Press(UnityEngine.InputSystem.Key.Escape); else menu.Open();
        yield return WaitReal(.3f);
        Check("ESC opens menu", OverburstGameMenu.IsOpen);
        Check("paused timeScale 0", Time.timeScale == 0f, "timeScale=" + Time.timeScale);
        Check("audio paused", AudioListener.pause);
        Check("gameplay input blocked", GameplayInputBlocker.IsGameplayInputBlocked && facade != null && !facade.IsGameplayEnabled);
        yield return Shot("menu_main_hideout");

        // 3) 메뉴 안 ESC → 닫힘·복구
        if (keyboardWorks) yield return Press(UnityEngine.InputSystem.Key.Escape); else menu.Back();
        yield return WaitReal(.3f);
        Check("ESC closes menu", !OverburstGameMenu.IsOpen);
        Check("timeScale restored", Mathf.Approximately(Time.timeScale, scaleBefore), "timeScale=" + Time.timeScale + " before=" + scaleBefore);
        Check("gameplay restored", !AudioListener.pause && facade.IsGameplayEnabled);
        Check("hud default key labels", HudLabels() == "1 2 3 4 5 6 7 8 9 0", HudLabels());
        yield return Shot("hud_default");

        // 4) 패링 슬로우 도중 멈춤: 남은 슬로우가 멈춘 만큼 늘어난다.
        var owner = new GameObject("MenuCaptureSlowOwner");
        OverburstTimeEffectArbiter.Request(owner, OverburstTimeEffectKind.ParrySlow, .5f, 1f);
        yield return null;
        menu.Open();
        yield return WaitReal(1.5f);
        Check("slow frozen while paused", Time.timeScale == 0f && OverburstTimeEffectArbiter.ActiveRequestCount == 1);
        menu.Close();
        yield return null;
        Check("slow continues after resume", Mathf.Approximately(Time.timeScale, .5f) && OverburstTimeEffectArbiter.ActiveKind == OverburstTimeEffectKind.ParrySlow, "timeScale=" + Time.timeScale);
        yield return WaitReal(1.2f);
        Check("slow ends after remaining time", Mathf.Approximately(Time.timeScale, scaleBefore), "timeScale=" + Time.timeScale);
        UnityEngine.Object.Destroy(owner);

        // 5) 재사용 대기는 멈춘 동안 줄지 않는다.
        var cooldowns = UnityEngine.Object.FindFirstObjectByType<ItemUseCooldownController>();
        if (cooldowns != null)
        {
            cooldowns.StartCooldown("menu-capture", 5f);
            float before = cooldowns.GetRemaining("menu-capture");
            menu.Open();
            yield return WaitReal(1.5f);
            float during = cooldowns.GetRemaining("menu-capture");
            menu.Close();
            Check("cooldown frozen while paused", Mathf.Abs(before - during) < .1f, "before=" + before.ToString("0.00") + " during=" + during.ToString("0.00"));
        }
        else notes.Add("no ItemUseCooldownController");

        // 6) 설정 창 네 탭과 저장
        menu.Open();
        yield return WaitReal(.2f);
        menu.OpenSettings();
        yield return WaitReal(.3f);
        var panel = menu.settings;
        string[] tabNames = { "sound", "screen", "combat", "controls" };
        for (int i = 0; i < panel.tabs.Length; i++)
        {
            panel.tabs[i].isOn = true;
            yield return WaitReal(.25f);
            Check("tab " + tabNames[i] + " shows page", panel.pages[i].activeInHierarchy && panel.pages.Where((p, j) => j != i).All(p => !p.activeSelf));
            yield return Shot("settings_" + tabNames[i]);
        }
        panel.tabs[2].isOn = true;
        yield return null;
        panel.cameraShake.value = .4f;
        yield return null;
        Check("camera shake applied", Mathf.Approximately(OverburstGameSettings.CameraShakeScale, .4f));
        OverburstGameSettings.SaveIfDirty();
        string saved = File.Exists(OverburstGameSettings.FilePath) ? File.ReadAllText(OverburstGameSettings.FilePath) : "";
        Check("settings saved", saved.Contains("\"cameraShake\": 0.4"), saved.Length + " chars");

        // 7) 키 다시 정하기: 상호작용(F) → K, 그다음 상호작용 → Tab(인벤토리와 겹침) → 맞바꿈
        panel.tabs[3].isOn = true;
        yield return WaitReal(.2f);
        var asset = facade.RuntimeAsset;
        var interact = panel.keyRows.First(r => r.actionName == "Interact");
        var inventory = panel.keyRows.First(r => r.actionName == "Inventory");
        notes.Add("interact before=" + interact.CurrentPath(asset) + " inventory before=" + inventory.CurrentPath(asset));
        interact.keyButton.onClick.Invoke();
        yield return WaitReal(.25f);
        bool prompt = panel.keyPrompt.activeInHierarchy;
        Check("key prompt shown", prompt);
        yield return Shot("settings_key_prompt");
        yield return Press(UnityEngine.InputSystem.Key.K);
        yield return WaitReal(.3f);
        Check("rebind interact to K", interact.CurrentPath(asset) == "<Keyboard>/k", interact.CurrentPath(asset));
        interact.keyButton.onClick.Invoke();
        yield return WaitReal(.25f);
        yield return Press(UnityEngine.InputSystem.Key.Tab);
        yield return WaitReal(.3f);
        Check("conflict swaps keys", interact.CurrentPath(asset) == "<Keyboard>/tab" && inventory.CurrentPath(asset) == "<Keyboard>/k",
            "interact=" + interact.CurrentPath(asset) + " inventory=" + inventory.CurrentPath(asset) + " status=" + panel.statusText.text);
        yield return Shot("settings_controls_swapped");
        interact.keyButton.onClick.Invoke();
        yield return WaitReal(.25f);
        yield return Press(UnityEngine.InputSystem.Key.Escape);
        yield return WaitReal(.3f);
        Check("ESC cancels rebind only", !panel.keyPrompt.activeSelf && panel.gameObject.activeSelf && OverburstGameMenu.IsOpen && interact.CurrentPath(asset) == "<Keyboard>/tab");
        saved = File.ReadAllText(OverburstGameSettings.FilePath);
        Check("binding overrides saved", saved.Contains("<Keyboard>/tab") && saved.Contains("<Keyboard>/k"));

        // 빈 칸만 비교해서 통과하지 않도록 격리 계정에 서로 다른 물약 세 개를 실제 장착한다.
        var fixtureFlasks = PlayerFlaskController.Current;
        var fixtureInventory = UnityEngine.Object.FindFirstObjectByType<PlayerInventory>();
        var fixtureBindings = ui.GetComponent<InventoryQuickSlotBindingController>();
        string[] fixtureNames = { "Life", "Ironclad", "Regeneration" };
        int[] fixtureKeys = { 1, 2, 10 };
        for (int i = 0; i < fixtureNames.Length; i++)
        {
            var data = AssetDatabase.LoadAssetAtPath<FlaskItemData>("Assets/ProjectOverburst/Resources/Items/Flasks/Flask_" + fixtureNames[i] + ".asset");
            if (data == null || fixtureInventory == null || fixtureFlasks == null || fixtureBindings == null)
                throw new InvalidOperationException("Flask fixture unavailable: " + fixtureNames[i]);
            var item = new ItemData(data, 1, ItemGrade.Common);
            if (!fixtureInventory.AddItem(item) || !fixtureFlasks.TryEquip(i, item, out string reason) || !fixtureBindings.Bind(fixtureKeys[i], item))
                throw new InvalidOperationException("Flask fixture failed: " + fixtureNames[i]);
        }
        yield return WaitReal(.2f);
        Check("three equipped flask fixtures", Enumerable.Range(0, 3).All(i => fixtureFlasks.GetItem(i) != null && fixtureBindings.GetFlaskKey(fixtureFlasks.GetItem(i)) == fixtureKeys[i]));

        // 7-2) 퀵슬롯 키를 바꾸면 HUD 칸 글자·장비창 물약 칸·등록 메뉴가 따라간다. 왼쪽 Shift는 회피와 겹쳐 맞바꿈이 일어난다.
        var slot1 = panel.keyRows.First(r => r.actionName == "QuickSlot1");
        var slot2 = panel.keyRows.First(r => r.actionName == "QuickSlot2");
        var slot10 = panel.keyRows.First(r => r.actionName == "QuickSlot10");
        yield return Rebind(slot1, UnityEngine.InputSystem.Key.Q);
        yield return Rebind(slot2, UnityEngine.InputSystem.Key.LeftShift);
        yield return Rebind(slot10, UnityEngine.InputSystem.Key.Minus);
        notes.Add("quickslot paths=" + slot1.CurrentPath(asset) + " " + slot2.CurrentPath(asset) + " " + slot10.CurrentPath(asset)
            + " evade=" + panel.keyRows.First(r => r.actionName == "Evade").CurrentPath(asset));
        yield return Shot("settings_controls_quickslots");
        menu.Close();
        yield return WaitReal(.4f);
        string expected = string.Join(" ", Enumerable.Range(1, 10).Select(k =>
            OverburstKeyBindingRow.ShortName(panel.keyRows.First(r => r.actionName == "QuickSlot" + k).CurrentPath(asset))));
        Check("hud labels follow rebind", HudLabels() == expected && HudLabels().StartsWith("Q Shift 3 "), "hud=" + HudLabels() + " expected=" + expected);
        Check("numbered label", QuickSlotKeyLabels.Numbered(1) == "Q 키" && QuickSlotKeyLabels.Numbered(3) == "3번" && !QuickSlotKeyLabels.AllDefault,
            QuickSlotKeyLabels.Numbered(1) + " / " + QuickSlotKeyLabels.Numbered(3));
        var actionService = UnityEngine.Object.FindFirstObjectByType<InventoryItemActionService>(FindObjectsInactive.Include);
        string menuLabel = actionService != null ? actionService.GetQuickSlotLabel(1) : "no service";
        Check("context menu label follows rebind", menuLabel.StartsWith("Q : "), menuLabel);
        yield return Shot("hud_rebound");
        ui.equipmentButton.onClick.Invoke();
        yield return WaitReal(1f);
        var flaskController = PlayerFlaskController.Current;
        var bindings = ui.GetComponent<InventoryQuickSlotBindingController>();
        var keyField = typeof(OverburstUIItemSlotView).GetField("keyLabel", BindingFlags.NonPublic | BindingFlags.Instance);
        var flaskReport = new List<string>();
        bool flaskOk = flaskController != null && bindings != null && ui.flasks.Length == 3;
        for (int i = 0; flaskOk && i < ui.flasks.Length; i++)
        {
            var item = flaskController.GetItem(i);
            int key = bindings.GetFlaskKey(item);
            string want = key > 0 ? QuickSlotKeyLabels.Short(key) : "";
            var text = keyField.GetValue(ui.flasks[i]) as UnityEngine.UI.Text;
            string shown = text == null ? "null" : text.gameObject.activeSelf ? text.text : "";
            flaskReport.Add(i + ":" + (item != null ? item.itemName : "-") + " key=" + key + " shown='" + shown + "' want='" + want + "'");
            flaskOk &= item != null && key == fixtureKeys[i] && text != null && text.gameObject.activeInHierarchy && shown == want;
        }
        Check("equipment flask labels follow rebind", flaskOk, string.Join(", ", flaskReport));
        yield return Shot("equipment_flask_keys");
        ui.equipmentClose.onClick.Invoke();
        yield return WaitReal(.3f);
        menu.Open();
        yield return WaitReal(.2f);
        menu.OpenSettings();
        yield return WaitReal(.3f);
        panel.tabs[3].isOn = true;
        yield return WaitReal(.2f);
        panel.resetButton.onClick.Invoke();
        yield return WaitReal(.2f);
        Check("reset restores default keys", interact.CurrentPath(asset) == "<Keyboard>/f" && inventory.CurrentPath(asset) == "<Keyboard>/tab");

        // 8) 확인창(게임 종료)과 ESC 한 단계 뒤로
        yield return Press(UnityEngine.InputSystem.Key.Escape); // 설정 → 메인
        yield return WaitReal(.3f);
        Check("ESC settings -> main", !panel.gameObject.activeSelf && menu.mainPanel.gameObject.activeSelf && OverburstGameMenu.IsOpen);
        menu.quitButton.onClick.Invoke();
        yield return WaitReal(.3f);
        Check("quit confirm shown", menu.ModalOpen);
        yield return Shot("menu_quit_confirm");
        yield return Press(UnityEngine.InputSystem.Key.Escape); // 확인창 → 메인(종료하지 않음)
        yield return WaitReal(.3f);
        Check("ESC cancels confirm", !menu.ModalOpen && OverburstGameMenu.IsOpen && EditorApplication.isPlaying);
        menu.Close();
        yield return WaitReal(.3f);
        Check("closed at end", !OverburstGameMenu.IsOpen && Mathf.Approximately(Time.timeScale, scaleBefore));
        yield return WaitReal(.2f);
        Check("hud labels back to default after reset", HudLabels() == "1 2 3 4 5 6 7 8 9 0" && QuickSlotKeyLabels.AllDefault, HudLabels());
    }

    static IEnumerator Rebind(OverburstKeyBindingRow row, Key key)
    {
        row.keyButton.onClick.Invoke();
        yield return WaitReal(.25f);
        yield return Press(key);
        yield return WaitReal(.3f);
    }

    // HUD 퀵슬롯 칸에 실제로 보이는 키 글자(1~10 순서). RPG11 칸은 OverburstUIItemSlotView 글자가 보이고, 없으면 칸 자체 글자를 읽는다.
    static string HudLabels()
    {
        var hud = UnityEngine.Object.FindFirstObjectByType<ActionSlotHudUI>(FindObjectsInactive.Include);
        if (hud == null) return "no hud";
        const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var slots = typeof(ActionSlotHudUI).GetField("quickSlots", Flags).GetValue(hud) as ActionSlotHudSlotUI[];
        var legacy = typeof(ActionSlotHudSlotUI).GetField("legacyKeyText", Flags);
        var tmp = typeof(ActionSlotHudSlotUI).GetField("keyText", Flags);
        var viewKey = typeof(OverburstUIItemSlotView).GetField("keyLabel", Flags);
        if (slots == null) return "no slots";
        return string.Join(" ", slots.Select(s =>
        {
            if (s == null) return "null";
            var shownTexts = new List<string>();
            var view = s.GetComponent<OverburstUIItemSlotView>();
            if (view != null && viewKey.GetValue(view) is UnityEngine.UI.Text v) shownTexts.Add(v.gameObject.activeInHierarchy && v.enabled ? v.text : "");
            if (legacy.GetValue(s) is UnityEngine.UI.Text l && l.gameObject.activeInHierarchy && l.enabled) shownTexts.Add(l.text);
            if (tmp.GetValue(s) is TMPro.TMP_Text k && k.gameObject.activeInHierarchy && k.enabled) shownTexts.Add(k.text);
            return shownTexts.Count == 0 ? "none" : string.Join("/", shownTexts.Distinct());
        }));
    }
}
