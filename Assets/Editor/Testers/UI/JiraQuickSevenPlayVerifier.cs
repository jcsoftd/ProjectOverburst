#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Appearance;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// KAN-99/100/115/120의 실제 UI 경로. 다른 3건의 경계값은 NativeChecks가 검사한다.
[InitializeOnLoad]
public static class JiraQuickSevenPlayVerifier
{
    const string K = "Overburst.JiraQuickSeven.";
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static readonly Dictionary<InputActionAsset, InputDevice[]> masks = new Dictionary<InputActionAsset, InputDevice[]>();
    static string output;
    static InputSettings originalInput, temporaryInput;
    static Mouse mouse;
    static Keyboard keyboard;
    static MouseState pointer;
    static int frame = -1;
    static double deadline;
    static JiraQuickSevenPlayVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (!string.IsNullOrEmpty(SessionState.GetString(K + "return", "")))
        {
            output = SessionState.GetString(K + "return", "");
            string stage = SessionState.GetString(K + "stage", "");
            if (stage == "booting") { deadline = SessionState.GetFloat(K + "deadline", 0); EditorApplication.update += Tick; }
            else if (stage == "returning") EditorApplication.update += Return;
            else EditorApplication.update += Recover;
        }
    }
    static T Field<T>(object o, string name) => (T)(o is Type type ? type : o.GetType()).GetField(name, F).GetValue(o is Type ? null : o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, F).Invoke(o, args);
    static object[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => { var s = SceneManager.GetSceneAt(i); return (object)new { s.path, s.isDirty }; }).ToArray();
    static void Write(string file, object data) => File.WriteAllText(Path.Combine(output, file), JsonConvert.SerializeObject(data, Formatting.Indented));
    static bool OwnPlay() => string.Equals(IsolatedSavePlayGuard.ActiveDirectory, Path.Combine(SessionState.GetString(K + "return", ""), "Account"), StringComparison.OrdinalIgnoreCase);
    static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        checks.Add(name); SessionState.SetString(K + "checks", JsonConvert.SerializeObject(checks));
        Write("play-progress.json", new { utc = DateTime.UtcNow, checks });
    }
    public static string Start(string directory)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if (BuildPipeline.isBuildingPlayer || EditorUtility.scriptCompilationFailed || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
            || !string.IsNullOrEmpty(SessionState.GetString(K + "return", "")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")))
            throw new InvalidOperationException("A session has not returned.");
        output = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Codex output required.");
        Directory.CreateDirectory(output);
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var seed = NewAccountFactory.Create(registry); seed.appearance = new CharacterAppearanceSnapshot(); AccountCharacterCommands.EnsureLegacy(seed);
        string account = Path.Combine(output, "Account"); new EasySaveAccountStore(account).Save(seed, seed.lastTransactionId);
        Write("play-before.json", new { pid = System.Diagnostics.Process.GetCurrentProcess().Id, scenes = Scenes(), input = InputSystem.settings.GetInstanceID(), inputJson = EditorJsonUtility.ToJson(InputSystem.settings), startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) });
        SessionState.SetString(K + "return", output);
        SessionState.SetString(K + "scenes", JsonConvert.SerializeObject(Scenes()));
        SessionState.SetString(K + "start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(K + "inputJson", EditorJsonUtility.ToJson(InputSystem.settings));
        SessionState.SetBool(K + "background", Application.runInBackground);
        SessionState.SetFloat(K + "returnDeadline", (float)EditorApplication.timeSinceStartup + 600);
        SessionState.EraseString(K + "checks"); checks.Clear(); errors.Clear(); frame = -1;
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", Path.Combine(output, "Settings"));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        deadline = EditorApplication.timeSinceStartup + 240; Application.runInBackground = true;
        SessionState.SetFloat(K + "deadline", (float)deadline); SessionState.SetString(K + "stage", "booting");
        EditorApplication.update += Tick;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(account); }
        catch (Exception e) { Finish(e); }
        return "QUEUED";
    }
    static void State(PlayModeStateChange state)
    {
        if (string.IsNullOrEmpty(SessionState.GetString(K + "return", ""))) return;
        if (state == PlayModeStateChange.EnteredPlayMode && OwnPlay()) { SessionState.SetString(K + "stage", "running"); work.Push(Verify()); Application.logMessageReceived += Log; }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            if (SessionState.GetString(K + "stage", "") != "returning") Finish(new InvalidOperationException("Owned Play ended before checks completed"));
            EditorApplication.update -= Return; EditorApplication.update += Return;
        }
    }
    static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup > deadline) { Finish(new TimeoutException("Owned Play timeout")); return; }
        if (!EditorApplication.isPlaying || !OwnPlay() || Time.frameCount == frame) return;
        frame = Time.frameCount;
        try
        {
            while (work.Count > 0)
            {
                var task = work.Peek();
                if (!task.MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                if (task.Current is IEnumerator nested) { work.Push(nested); continue; }
                return;
            }
            Finish(errors.Count == 0 ? null : new InvalidOperationException("Runtime errors: " + string.Join("; ", errors)));
        }
        catch (Exception e) { Finish(e); }
    }
    static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
    static IEnumerator Wait(Func<bool> condition, string name) { int remaining = 1800; while (!condition() && remaining-- > 0) yield return null; Check(condition(), name); }
    static Vector2 Point(RectTransform r) => RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center));
    static IEnumerator Click(Selectable control)
    {
        Canvas.ForceUpdateCanvases(); var hits = new List<RaycastResult>(); Vector2 pos = Point((RectTransform)control.transform);
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = pos }, hits);
        Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Selectable>() == control, "Actual raycast reaches " + control.name);
        pointer = new MouseState { position = pos }; yield return Frames(2); pointer.buttons = 1; yield return Frames(2); pointer.buttons = 0; yield return Frames(4);
    }
    static IEnumerator Press(Key key) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return Frames(2); InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return Frames(4); }
    static IEnumerator Capture(string name) { ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png")); yield return Frames(3); }
    static void Push() { if (mouse != null && mouse.added) InputSystem.QueueStateEvent(mouse, pointer); }
    static void SetupInput()
    {
        originalInput = InputSystem.settings; temporaryInput = Object.Instantiate(originalInput);
        SessionState.SetInt(K + "input", originalInput.GetInstanceID()); SessionState.SetInt(K + "temporary", temporaryInput.GetInstanceID());
        temporaryInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        temporaryInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = temporaryInput; mouse = InputSystem.AddDevice<Mouse>("JiraQuickSevenMouse"); keyboard = InputSystem.AddDevice<Keyboard>("JiraQuickSevenKeyboard");
        SessionState.SetInt(K + "mouse", mouse.deviceId); SessionState.SetInt(K + "keyboard", keyboard.deviceId);
        var module = EventSystem.current.GetComponent<InputSystemUIInputModule>();
        foreach (var asset in new[] { PlayerInputFacade.Current.RuntimeAsset, module.actionsAsset }.Where(a => a != null).Distinct())
        { masks[asset] = asset.devices.HasValue ? asset.devices.Value.ToArray() : null; asset.devices = new InputDevice[] { mouse, keyboard }; }
        SessionState.SetString(K + "masks", JsonConvert.SerializeObject(masks.Select(p => new { asset = p.Key.GetInstanceID(), devices = p.Value?.Select(d => d.deviceId).ToArray() })));
        pointer = new MouseState(); InputSystem.onBeforeUpdate += Push;
    }
    static IEnumerator Approach(Component npc, PlayerActorRuntime actor)
    {
        ActorTeleportUtility.TeleportSafely(actor.transform, npc.transform.position + npc.transform.forward * 1.25f, Quaternion.LookRotation(-npc.transform.forward));
        yield return Frames(12);
        Check(ReferenceEquals(actor.GetComponent<PlayerInteractionController>().Current, npc), "Real approach resolves " + npc.name);
    }
    static IEnumerator Verify()
    {
        yield return Wait(() => PlayerInputFacade.Current && TitleCharacterPanel.Instance && PlayerContext.Instance?.CurrentActor, "Product title boot");
        SetupInput(); yield return Frames(4); yield return Click(TitleCharacterPanel.Instance.start); yield return Click(TitleCharacterPanel.Instance.enter);
        yield return Wait(() => AccountBootstrap.Ready && WorldSessionState.IsHideout && PersistentSceneFlow.Instance?.IsSwitching == false, "Isolated title-to-town boot");
        var actor = PlayerContext.Instance.CurrentActor; var ui = QuestMerchantUI.Current; var account = AccountGameplaySession.Current;
        var merchant = Object.FindObjectsByType<GeneralGoodsMerchantInteractable>(FindObjectsSortMode.None).First(n => n.Definition && n.gameObject.scene == WorldSessionState.ContentScene);
        var action = PlayerInputFacade.Current.GameplayMap.FindAction("Interact"); int binding = OverburstKeyBindingRow.ResolveBindingIndex(action, null);
        action.ApplyBindingOverride(binding, "<Keyboard>/q"); yield return Approach(merchant, actor);
        Check(ui.approach.activeInHierarchy && ui.approachKeys.All(k => k.GetComponentInChildren<Text>(true).text == "Q"), "KAN-100 all live merchant keys show Q");
        Check(ui.approach.transform.Find("Hint").GetComponent<Text>().text.Contains("Q 실행"), "KAN-100 live hint shows Q");
        yield return Capture("merchant-Q"); ui.SelectOption(1); yield return Press(Key.Q);
        var shop = Object.FindObjectsByType<ShopUI>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(s => s.IsOpen);
        Check(shop.IsOpenFor(merchant.Definition), "KAN-100 Q actually opens the original trade flow");
        var stock = Field<SlotUI[]>(shop, "merchantInventorySlots").First(s => s.DisplayItem != null && !(s.DisplayItem.baseData is CurrencyItemData));
        var menu = Field<ShopContextMenuController>(shop, "contextMenu");
        string ownershipBefore = JsonConvert.SerializeObject(account.Read());
        Check(menu.Open(ShopContextMenuTarget.MerchantInventory, stock, new PointerEventData(EventSystem.current) { position = Point((RectTransform)stock.transform) }), "KAN-115 original context menu opens");
        yield return Frames(3);
        var expectedInfo = stock.DisplayItem; var infoButton = Field<Button>(menu, "infoButton");
        bool infoDisplayedAtClick = false; int infoClickCount = 0;
        UnityEngine.Events.UnityAction observedInfo = () =>
        {
            infoClickCount++;
            var shown = Field<ItemData>(TooltipManager.Instance, "currentItem");
            infoDisplayedAtClick = Field<SlotUI>(menu, "selectedSlot") == null && !Field<bool>(TooltipManager.Instance, "suppressed") && ReferenceEquals(shown, expectedInfo);
            Write("info-click.json", new { merchant = merchant.name, expected = expectedInfo.runtimeInstanceId, actual = shown?.runtimeInstanceId, infoDisplayedAtClick, infoClickCount });
        };
        infoButton.onClick.AddListener(observedInfo);
        try { yield return Click(infoButton); }
        finally { infoButton.onClick.RemoveListener(observedInfo); }
        var afterInfo = Field<ItemData>(TooltipManager.Instance, "currentItem");
        var infoHits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = pointer.position }, infoHits);
        Write("info-after-hover.json", new { expected = expectedInfo.runtimeInstanceId, actual = afterInfo?.runtimeInstanceId, suppressed = Field<bool>(TooltipManager.Instance, "suppressed"), pointerX = pointer.position.x, pointerY = pointer.position.y, hits = infoHits.Take(5).Select(h => h.gameObject.name).ToArray() });
        Check(infoClickCount == 1 && infoDisplayedAtClick, "KAN-115 actual info click closes menu and displays selected item");
        Check(JsonConvert.SerializeObject(account.Read()) == ownershipBefore, "KAN-115 viewing info preserves account ownership");
        yield return Capture("shop-item-info"); shop.Close(); yield return Frames(5);
        yield return Approach(merchant, actor);
        foreach (string path in new[] { "<Keyboard>/leftShift", "<Keyboard>/space", "<Keyboard>/pageDown", "<Mouse>/backButton", "" })
        {
            action.ApplyBindingOverride(binding, path); yield return Frames(4); string label = string.IsNullOrEmpty(path) ? "—" : OverburstKeyBindingRow.ShortName(path);
            Check(ui.approachKeys.All(k => k.GetComponentInChildren<Text>(true).text == label), "KAN-100 immediate visible rebind " + label);
            foreach (var keyObject in ui.approachKeys)
            {
                var keyText = keyObject.GetComponentInChildren<Text>(true); var keyRect = (RectTransform)keyObject.transform;
                Check(keyText.preferredWidth <= keyText.rectTransform.rect.width && Mathf.Approximately(keyRect.rect.height, 27f)
                    && keyText.fontSize == 34 && Mathf.Approximately(keyRect.anchoredPosition.x + keyRect.rect.width, 327f), "KAN-100 key fits and keeps height/font/right edge " + label);
            }
            yield return Capture("merchant-key-" + label);
        }
        action.RemoveBindingOverride(binding); yield return Frames(3); Check(ui.approachKeys.All(k => k.GetComponentInChildren<Text>(true).text == "F"), "KAN-100 default restores F");
        var stylist = Object.FindObjectsByType<AppearanceStylistInteractable>(FindObjectsSortMode.None).Single(n => n.gameObject.scene == WorldSessionState.ContentScene);
        action.ApplyBindingOverride(binding, "<Keyboard>/q"); yield return Approach(stylist, actor);
        Check(ui.approach.transform.Find("Hint").GetComponent<Text>().text == "클릭 / Q 실행", "KAN-100 stylist keeps its own hint");
        var stashTarget = Object.FindObjectsByType<StashInteractable>(FindObjectsSortMode.None).First(n => n.gameObject.scene == WorldSessionState.ContentScene);
        ActorTeleportUtility.TeleportSafely(actor.transform, stashTarget.transform.position + Vector3.back * .7f, Quaternion.identity); yield return Frames(12);
        Check(stashTarget.InteractionPrompt == "Q : 창고 열기", "KAN-100 live stash label Q"); yield return Press(Key.Q);
        var stash = Object.FindFirstObjectByType<StashUI>(FindObjectsInactive.Include); Check(stash.IsOpen, "KAN-100 Q opens stash"); stash.Close();
        var inventoryView = Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include); var inventory = PlayerAccountInventoryService.SharedInventory;
        inventoryView.SetVisible(true); var bridge = inventoryView.GetComponent<InventorySlotBridge>();
        var gameUi = Object.FindFirstObjectByType<OverburstGameUI>();
        yield return Click(gameUi.equipmentButton); yield return Frames(3);
        var bagData = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath).Entries.Select(e => e.asset).OfType<BagItemData>().First();
        var bag = new ItemData(bagData, 1, ItemGrade.Common); Check(inventory.AddItem(bag), "KAN-99 isolated bag acquired"); yield return Frames(3);
        var slots = Field<SlotUI[]>(bridge, "inventorySlots"); var bagSlots = Field<SlotUI[]>(bridge, "bagSlots");
        var source = slots[inventory.Items.ToList().IndexOf(bag)]; var target = bagSlots[0];
        Check(target.gameObject.activeInHierarchy && target.GetComponent<DropSlot>().isActiveAndEnabled, "Actual equipment bag drop target is visible");
        DragSlot.RegisterDropTarget(target.GetComponent<DropSlot>()); Check(Field<DropSlot>(typeof(DragSlot), "pendingDropSlot") == null, "KAN-120 unrelated drop cannot become pending");
        bool started = DragSlot.BeginExternalDrag(source, source.DisplayItem, source.GetComponentInParent<Canvas>(true), Point((RectTransform)source.transform));
        Check(started, "KAN-120 actual inventory drag starts");
        var bagDrop = target.GetComponent<DropSlot>(); var sourceCanvas = source.GetComponentInParent<Canvas>(true); var sourcePoint = Point((RectTransform)source.transform);
        DragSlot.RegisterDropTarget(bagDrop);
        Check(Field<DropSlot>(typeof(DragSlot), "pendingDropSlot") == bagDrop, "KAN-120 valid drag registers its target");
        Check(!DragSlot.BeginExternalDrag(null, bag, sourceCanvas, sourcePoint) && DragSlot.OriginSlot == source
            && Field<DropSlot>(typeof(DragSlot), "pendingDropSlot") == bagDrop, "KAN-120 rejected start preserves active drag");
        Check(DragSlot.BeginExternalDrag(source, source.DisplayItem, sourceCanvas, sourcePoint)
            && Field<DropSlot>(typeof(DragSlot), "pendingDropSlot") == null, "KAN-120 accepted external start clears previous target");
        DragSlot.RegisterDropTarget(bagDrop); source.GetComponent<DragSlot>().OnBeginDrag(new PointerEventData(EventSystem.current) { position = sourcePoint });
        Check(DragSlot.OriginSlot == source && Field<DropSlot>(typeof(DragSlot), "pendingDropSlot") == null, "KAN-120 accepted pointer start clears previous target");
        DragSlot.ClearDragState(); Check(!DragSlot.IsDragging && Field<DropSlot>(typeof(DragSlot), "pendingDropSlot") == null, "KAN-120 cancel clears pending target");
        Check(DragSlot.BeginExternalDrag(source, source.DisplayItem, sourceCanvas, sourcePoint), "KAN-120 drag restarts after cancellation");
        bagDrop.OnPointerEnter(new PointerEventData(EventSystem.current));
        Check((bool)Call(bridge, "CanPreviewEquipBagIntoSlot", source, 0), "KAN-99 normal owned bag still previews");
        var dropEvent = new PointerEventData(EventSystem.current) { position = Point((RectTransform)target.transform) };
        target.GetComponent<DropSlot>().OnDrop(dropEvent); DragSlot.CompleteDrag(dropEvent); yield return Frames(3);
        Write("bag-drop.json", new { dragging = DragSlot.IsDragging, expected = bag.runtimeInstanceId, equipped = PlayerAccountInventoryService.EquippedBag?.runtimeInstanceId, inInventory = inventory.Items.Count(i => i?.runtimeInstanceId == bag.runtimeInstanceId) });
        Check(!DragSlot.IsDragging && PlayerAccountInventoryService.EquippedBag?.runtimeInstanceId == bag.runtimeInstanceId, "KAN-99/120 bag completes exactly once and clears drag");
        Check((bool)Call(bridge, "UnequipBagToFirstAvailableSlot", 0), "KAN-99 normal bag unequip preserved");
        Check(inventory.Items.Count(i => i?.runtimeInstanceId == bag.runtimeInstanceId) == 1, "KAN-99 roundtrip preserves one runtime ID");
        inventoryView.SetVisible(false); action.RemoveBindingOverride(binding); Check(account.FlushPendingSave(), "Isolated account flush");
        AccountInvariants.Validate(new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load(), Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath));
        Check(!DragSlot.IsDragging, "All owned drag state released");
    }
    static void RestoreInput()
    {
        InputSystem.onBeforeUpdate -= Push;
        foreach (var p in masks) if (p.Key) p.Key.devices = p.Value; masks.Clear();
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse); if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); mouse = null; keyboard = null;
        if (originalInput) InputSystem.settings = originalInput;
        if (temporaryInput) Object.DestroyImmediate(temporaryInput); originalInput = temporaryInput = null;
        foreach (string name in new[] { "input", "temporary", "mouse", "keyboard" }) SessionState.EraseInt(K + name); SessionState.EraseString(K + "masks");
    }
    static void Finish(Exception error)
    {
        bool own = OwnPlay();
        SessionState.SetString(K + "stage", "returning");
        // 종료를 먼저 요청하고, 같은 프레임의 정리 뒤에는 EditMode 반환만 기다린다.
        if (own && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
        if (own) DragSlot.ClearDragState(); RestoreInput();
        Write("play-result.json", new { status = error == null ? "PASS_SCOPED" : "FAIL", utc = DateTime.UtcNow, checks, errors, error = error?.ToString() });
        EditorApplication.update -= Return; EditorApplication.update += Return;
    }
    static void Recover()
    {
        EditorApplication.update -= Recover; output = SessionState.GetString(K + "return", "");
        if (string.IsNullOrEmpty(output)) return;
        string json = SessionState.GetString(K + "masks", "");
        if (!string.IsNullOrEmpty(json)) foreach (var r in Newtonsoft.Json.Linq.JArray.Parse(json))
        { var asset = EditorUtility.InstanceIDToObject((int)r["asset"]) as InputActionAsset; if (asset) masks[asset] = r["devices"].Type == Newtonsoft.Json.Linq.JTokenType.Null ? null : r["devices"].Select(id => InputSystem.GetDeviceById((int)id)).Where(d => d != null).ToArray(); }
        originalInput = EditorUtility.InstanceIDToObject(SessionState.GetInt(K + "input", 0)) as InputSettings; temporaryInput = EditorUtility.InstanceIDToObject(SessionState.GetInt(K + "temporary", 0)) as InputSettings;
        mouse = InputSystem.GetDeviceById(SessionState.GetInt(K + "mouse", 0)) as Mouse; keyboard = InputSystem.GetDeviceById(SessionState.GetInt(K + "keyboard", 0)) as Keyboard;
        var saved = SessionState.GetString(K + "checks", ""); if (!string.IsNullOrEmpty(saved)) checks.AddRange(JsonConvert.DeserializeObject<List<string>>(saved));
        Finish(new InvalidOperationException("Play interrupted by assembly reload"));
    }
    static void Return()
    {
        output = SessionState.GetString(K + "return", ""); if (string.IsNullOrEmpty(output)) { EditorApplication.update -= Return; return; }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(K + "returnDeadline", 0))
        { EditorApplication.update -= Return; Write("play-return.json", new { status = "BLOCKED_RETURN", reason = "Return timeout; foreign state preserved", pending = output }); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)) return;
        foreach (string path in new[] { Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""), Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY") })
            if (!string.IsNullOrEmpty(path) && !Path.GetFullPath(path).StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        var finishedSession = AccountGameplaySession.Current;
        if (finishedSession != null && !finishedSession.IsEditing && !finishedSession.HasPendingSave
            && string.Equals(AccountBootstrap.SaveDirectory, Path.Combine(output, "Account"), StringComparison.OrdinalIgnoreCase))
            finishedSession.Detach();
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", null); string start = SessionState.GetString(K + "start", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(start) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        Application.runInBackground = SessionState.GetBool(K + "background", Application.runInBackground); IsolatedSavePlayGuard.UseRealAccount();
        bool scenes = JsonConvert.SerializeObject(Scenes()) == SessionState.GetString(K + "scenes", "");
        bool input = EditorJsonUtility.ToJson(InputSystem.settings) == SessionState.GetString(K + "inputJson", "");
        bool success = scenes && input && !IsolatedSavePlayGuard.RequiresAccountChoice && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorUtility.scriptCompilationFailed && EditorApplication.timeSinceStartup <= SessionState.GetFloat(K + "returnDeadline", 0);
        foreach (string name in new[] { "return", "start", "scenes", "checks", "stage", "inputJson" }) SessionState.EraseString(K + name); SessionState.EraseBool(K + "background"); SessionState.EraseFloat(K + "returnDeadline"); SessionState.EraseFloat(K + "deadline"); EditorApplication.update -= Return;
        Write("play-return.json", new { status = success ? "PASS" : "FAIL", stopped = !EditorApplication.isPlayingOrWillChangePlaymode, scenesPreserved = scenes, inputRestored = input, guard = IsolatedSavePlayGuard.RequiresAccountChoice, save = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""), expires = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""), startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) });
    }
}
#endif
