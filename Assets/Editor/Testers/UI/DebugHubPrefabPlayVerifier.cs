#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>실제 F1/포인터 이벤트로 탭, 선택, 검색, 즐겨찾기와 재열기를 검사한다.
/// TextMeshPro 내부 Caret/SubMesh를 제외한 UI 오브젝트 생성/파괴와 매 프레임 불투명 틴트를 감시한다.</summary>
public static partial class DebugHubPrefabVerifier
{
    public static string RunInCurrentPlay(string output)
    {
        if (!Application.isPlaying || !Overburst.Persistence.AccountBootstrap.Ready)
            throw new InvalidOperationException("Isolated product Play is not ready.");
        if (SessionState.GetBool("OB.Debug.VisualProbe.running", false))
            throw new InvalidOperationException("Visual probe is already running.");
        output = System.IO.Path.GetFullPath(output);
        System.IO.Directory.CreateDirectory(output);
        int cycle = SessionState.GetInt("OB.Debug.VisualProbe.cycle", 0);
        var hub = Overburst.DebugTools.DebugHub.Instance;
        if (hub == null) throw new InvalidOperationException("Debug hub is absent.");
        var root = hub.transform.Find("Debug Window");
        var view = hub.GetComponent<Overburst.DebugTools.DebugHubView>();
        var currentIds = Overburst.DebugTools.DebugRegistry.AllItems.Select(item => item.Id).OrderBy(id => id).ToArray();
        if (!currentIds.SequenceEqual(view.ItemIds)) throw new InvalidOperationException("Runtime registry differs from authored prefab.");
        var authoredObjects = hub.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.GetInstanceID(), t => t.name);
        var originalSettings = UnityEngine.InputSystem.InputSystem.settings;
        var temporarySettings = UnityEngine.Object.Instantiate(originalSettings);
        temporarySettings.hideFlags = HideFlags.DontSave;
        temporarySettings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
        temporarySettings.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        UnityEngine.InputSystem.InputSystem.settings = temporarySettings;
        bool background = Application.runInBackground;
        Application.runInBackground = true;
        var checks = new System.Collections.Generic.List<string>();
        var errors = new System.Collections.Generic.List<string>();
        var shots = new System.Collections.Generic.List<string>();
        var steps = new System.Collections.Generic.List<Action>();
        var names = new System.Collections.Generic.List<string>();
        var tabIds = new System.Collections.Generic.Dictionary<string, int>();
        int samples = 0, ghostSamples = 0;
        float maxGhostAlpha = 0;
        Action<bool, string> require = (condition, label) => { if (!condition) throw new InvalidOperationException(label); checks.Add(label); };
        Action<UnityEngine.InputSystem.Key[]> keyboard = keys => UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current, new UnityEngine.InputSystem.LowLevel.KeyboardState(keys));
        Func<string, UnityEngine.UI.Button> tab = name => root.Find("Body/Tabs/Tab " + name).GetComponent<UnityEngine.UI.Button>();
        Action<UnityEngine.UI.Button> click = button => {
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable) throw new InvalidOperationException("Button unavailable.");
            var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
            UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
        };
        Action sample = () => {
            samples++;
            var alive = hub.GetComponentsInChildren<Transform>(true);
            var aliveIds = new System.Collections.Generic.HashSet<int>(alive.Select(t => t.GetInstanceID()));
            if (authoredObjects.Keys.Any(id => !aliveIds.Contains(id))) throw new InvalidOperationException("Authored UI object was destroyed.");
            foreach (Transform node in alive)
                if (!authoredObjects.ContainsKey(node.GetInstanceID()) && node.name != "Caret" && !node.name.StartsWith("TMP SubMeshUI"))
                    throw new InvalidOperationException("Runtime created UI object: " + node.name);
            if (!Overburst.DebugTools.DebugHub.IsOpen) return;
            foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(false)) {
                if (button.colors.normalColor.a != 0 || button.targetGraphic == null) continue;
                ghostSamples++;
                float alpha = button.targetGraphic.color.a * button.targetGraphic.canvasRenderer.GetColor().a;
                maxGhostAlpha = Mathf.Max(maxGhostAlpha, alpha);
                if (alpha > 0.1601f) throw new InvalidOperationException("Opaque ghost button: " + button.name + " alpha=" + alpha);
                if (button.colors.fadeDuration != 0) throw new InvalidOperationException("Ghost button still fades: " + button.name);
            }
        };
        Action stableTabs = () => {
            foreach (var pair in tabIds) require(tab(pair.Key).GetInstanceID() == pair.Value, "Tab reused: " + pair.Key);
        };
        Action<string> shot = name => {
            string path = System.IO.Path.Combine(output, "cycle" + cycle + "_" + name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            shots.Add(path);
        };
        Action<string, Action> add = (name, action) => { names.Add(name); steps.Add(action); };
        add("F1 down", () => keyboard(new[] { UnityEngine.InputSystem.Key.F1 }));
        add("F1 opens", () => { keyboard(new UnityEngine.InputSystem.Key[0]); require(Overburst.DebugTools.DebugHub.IsOpen, "F1 opens window"); foreach (Transform child in root.Find("Body/Tabs")) if (child.gameObject.activeInHierarchy && child.TryGetComponent<UnityEngine.UI.Button>(out var button)) tabIds[child.name.Substring(4)] = button.GetInstanceID(); require(tabIds.Count == 9, "Nine product tabs"); });
        foreach (string target in new[] { "스폰·시험장", "적·AI", "전투", "플레이어", "★ 즐겨찾기", "전투", "아이템·경제", "UI·연출", "시스템", "던전·씬", "아이템·경제" }) {
            string selectedTab = target;
            add("Click tab " + target, () => { click(tab(selectedTab)); stableTabs(); require(tab(selectedTab).targetGraphic.color != Color.white, "Selected tab has its base color: " + selectedTab); });
        }
        add("Items render", () => shot("items"));
        add("Select category", () => { var row = root.GetComponentsInChildren<RectTransform>(false).First(t => t.name == "Row items.create.category"); var choice = (Overburst.DebugTools.DebugOptionsItem)Overburst.DebugTools.DebugRegistry.Find("items.create.category"); int before = choice.CurrentIndex; int target = before == 0 ? 1 : 0; string control = choice.OptionCount <= 6 ? "Option " + target : before == 0 ? "Next" : "Previous"; click(row.GetComponentsInChildren<UnityEngine.UI.Button>(false).First(b => b.name == control)); require(choice.CurrentIndex == target, "Choice click updates selected value"); stableTabs(); });
        add("Category render", () => shot("category"));
        add("Picker open", () => { var row = root.GetComponentsInChildren<RectTransform>(false).First(t => t.name == "Row items.create.item"); click(row.GetComponentsInChildren<UnityEngine.UI.Button>(false).First(b => b.name == "Open")); });
        add("Picker render", () => shot("picker"));
        add("Search", () => { var search = root.Find("Title/Search").GetComponent<TMPro.TMP_InputField>(); search.text = "무적"; stableTabs(); require(root.GetComponentsInChildren<RectTransform>(false).Any(t => t.name == "Row player.survival.damageReduction"), "Search displays matching authored rows"); });
        add("Clear search", () => { root.Find("Title/Search").GetComponent<TMPro.TMP_InputField>().text = ""; stableTabs(); });
        add("Combat", () => click(tab("전투")));
        bool initialToggle = CombatDebugSettings.ShowAttackPatternDebug;
        add("Toggle on", () => { var row = root.GetComponentsInChildren<RectTransform>(false).First(t => t.name == "Row combat.display.attackPattern"); click(row.GetComponentsInChildren<UnityEngine.UI.Button>(false).First(b => b.name == "Switch")); require(CombatDebugSettings.ShowAttackPatternDebug == !initialToggle, "Toggle responds to click"); });
        add("Toggle restore", () => { var row = root.GetComponentsInChildren<RectTransform>(false).First(t => t.name == "Row combat.display.attackPattern"); click(row.GetComponentsInChildren<UnityEngine.UI.Button>(false).First(b => b.name == "Switch")); require(CombatDebugSettings.ShowAttackPatternDebug == initialToggle, "Toggle restores original value"); });
        add("Favorite toggle", () => { var row = root.GetComponentsInChildren<RectTransform>(false).First(t => t.name == "Row combat.display.attackPattern"); click(row.GetComponentsInChildren<UnityEngine.UI.Button>(false).First(b => b.name == "Favorite")); });
        add("Favorite recent choice", () => { click(tab("아이템·경제")); var row = root.GetComponentsInChildren<RectTransform>(false).First(t => t.name == "Row items.create.category"); click(row.GetComponentsInChildren<UnityEngine.UI.Button>(false).First(b => b.name == "Favorite")); });
        add("Favorites and recent", () => { click(tab("★ 즐겨찾기")); require(root.GetComponentsInChildren<RectTransform>(false).Count(t => t.name == "Row items.create.category") == 2, "Favorite and recent use separate authored rows"); require(root.GetComponentsInChildren<RectTransform>(false).Count(t => t.name == "Row combat.display.attackPattern") == 1, "Toggle favorites preserve original recent policy"); stableTabs(); });
        add("Return combat", () => click(tab("전투")));
        add("Collapse section", () => { var header = root.GetComponentsInChildren<UnityEngine.UI.Button>(false).First(b => b.name == "Header" && b.interactable); click(header); stableTabs(); });
        add("UI render", () => { click(tab("UI·연출")); shot("presentation"); });
        add("F1 close down", () => keyboard(new[] { UnityEngine.InputSystem.Key.F1 }));
        add("F1 closes", () => { keyboard(new UnityEngine.InputSystem.Key[0]); require(!Overburst.DebugTools.DebugHub.IsOpen, "F1 closes window"); });
        add("F1 reopen down", () => keyboard(new[] { UnityEngine.InputSystem.Key.F1 }));
        add("F1 reopens", () => { keyboard(new UnityEngine.InputSystem.Key[0]); require(Overburst.DebugTools.DebugHub.IsOpen, "F1 reopens window"); stableTabs(); });
        Application.LogCallback log = (message, trace, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
        Application.logMessageReceived += log;
        int frame = -1, index = 0, nextStepFrame = Time.frameCount + 2;
        double deadline = EditorApplication.timeSinceStartup + 180;
        UnityEditor.EditorApplication.CallbackFunction tick = null;
        Action<string, string> finish = (status, error) => {
            EditorApplication.update -= tick;
            Application.logMessageReceived -= log;
            keyboard(new UnityEngine.InputSystem.Key[0]);
            Overburst.DebugTools.DebugHub.Close();
            UnityEngine.InputSystem.InputSystem.settings = originalSettings;
            UnityEngine.Object.Destroy(temporarySettings);
            Application.runInBackground = background;
            SessionState.SetBool("OB.Debug.VisualProbe.running", false);
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, "cycle" + cycle + ".json"), Newtonsoft.Json.JsonConvert.SerializeObject(new { status, error, cycle, checks, errors, samples, ghostSamples, maxGhostAlpha, authoredObjects = authoredObjects.Count, shots, screen = new[] { Screen.width, Screen.height } }, Newtonsoft.Json.Formatting.Indented));
        };
        tick = () => {
            try {
                EditorApplication.QueuePlayerLoopUpdate();
                if (!Application.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new InvalidOperationException("Visual Play interrupted or timed out.");
                if (frame == Time.frameCount) return;
                frame = Time.frameCount;
                sample();
                if (frame < nextStepFrame) return;
                if (index == steps.Count) { require(errors.Count == 0, "No runtime errors in visual probe"); require(samples > 40 && ghostSamples > 100, "Multiple rendered frames sampled"); finish("PASS", null); return; }
                steps[index]();
                sample();
                checks.Add("Completed " + names[index]);
                index++;
                nextStepFrame = frame + 3;
            } catch (Exception exception) { finish("FAIL", exception.ToString()); }
        };
        SessionState.SetBool("OB.Debug.VisualProbe.running", true);
        EditorApplication.update += tick;
        return "Frame visual probe started for cycle " + cycle;
    }
}
#endif
