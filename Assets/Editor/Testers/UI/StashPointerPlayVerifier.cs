#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

// 슬롯 이벤트를 직접 호출하지 않고 제품 EventSystem과 InputSystem으로 닫기/드롭을 검증한다.
[InitializeOnLoad]
public static class StashPointerPlayVerifier
{
    const string KeyPrefix = "Overburst.KAN14GUI.";
    static int Phase { get => SessionState.GetInt(KeyPrefix + "Phase", 0); set => SessionState.SetInt(KeyPrefix + "Phase", value); }
    static string Output => SessionState.GetString(KeyPrefix + "Output", "");
    static string Account => Path.Combine(Output, "Account");
    static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
    static readonly List<object> checks = new List<object>();
    static readonly HashSet<string> fixtureIds = new HashSet<string>();
    static Keyboard keyboard;
    static Mouse mouse;
    static Key? heldKey;
    static bool heldButton;
    static Vector2 pointer;
    static int frame = -1;
    static string cycleOutput;
    static StashUI stash;
    static InventoryUI inventory;
    static StashInteractable stashInteraction;
    static SlotUI lockedFixture;
    static AccountGameplaySession session;
    static InputSettings originalSettings, ownedSettings;
    static string originalSettingsJson;
    static bool reloadLocked, refreshLocked;
    static int readySinceFrame = -1;
    static bool lostWorkOnReload;
    static readonly List<StashUiVisibilityTrace> visibilityTraces = new List<StashUiVisibilityTrace>();
    static readonly Dictionary<InputActionAsset, InputDevice[]> originalDevices = new Dictionary<InputActionAsset, InputDevice[]>();

    static string QueuePath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Jira/20261005_KAN_Goal/gui-auto-start.json"));
    static StashPointerPlayVerifier()
    {
        if (Phase != 0) { lostWorkOnReload = Phase == 2; EditorApplication.update += Tick; }
        else if (File.Exists(QueuePath)) EditorApplication.update += TryQueuedStart;
    }
    static void TryQueuedStart()
    {
        if (Phase != 0 || !File.Exists(QueuePath)) { EditorApplication.update -= TryQueuedStart; return; }
        var plan = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(QueuePath));
        if ((string)plan["status"] != "QUEUED") { EditorApplication.update -= TryQueuedStart; return; }
        if (DateTime.UtcNow > plan["deadlineUtc"].ToObject<DateTime>().ToUniversalTime())
        { plan["status"] = "DEFERRED_TIMEOUT"; File.WriteAllText(QueuePath, plan.ToString()); EditorApplication.update -= TryQueuedStart; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || EditorUtility.scriptCompilationFailed) return;
        try
        {
            var result = Start((string)plan["output"]);
            plan["status"] = "STARTED"; plan["result"] = Newtonsoft.Json.Linq.JToken.FromObject(result);
            File.WriteAllText(QueuePath, plan.ToString()); EditorApplication.update -= TryQueuedStart;
        }
        catch (Exception error)
        {
            if (Phase != 0 || Directory.Exists((string)plan["output"]))
            { plan["status"] = "FAILED_START"; plan["error"] = error.ToString(); File.WriteAllText(QueuePath, plan.ToString()); EditorApplication.update -= TryQueuedStart; }
        }
    }
    public static object Start(string directory)
    {
        if (Phase != 0 || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Healthy idle Editor required.");
        RequireNoForeignSession();
        if (IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Previous account return required.");
        string output = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Fresh Codex artifact directory required.");
        var boot = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        if (boot == null) throw new InvalidOperationException("Product boot scene required.");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "before.json"), JsonConvert.SerializeObject(Scenes(), Formatting.Indented));
        SessionState.SetString(KeyPrefix + "Output", output);
        SessionState.SetString(KeyPrefix + "StartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(KeyPrefix + "Background", Application.runInBackground);
        SessionState.SetString(KeyPrefix + "Deadline", (EditorApplication.timeSinceStartup + 900).ToString("R", CultureInfo.InvariantCulture));
        SessionState.SetInt(KeyPrefix + "Cycle", 1);
        SessionState.SetString(KeyPrefix + "Status", "RUNNING");
        EditorSceneManager.playModeStartScene = boot;
        Application.runInBackground = true;
        Phase = 1;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Account); }
        catch { Phase = 3; throw; }
        return new { status = "STARTED", output, cycles = 2, inputPath = "InputSystem -> EventSystem -> product handlers" };
    }
    static void RequireNoForeignSession()
    {
        foreach (string key in new[] { "Overburst.WeakAttackPlayerLoop.plan", "Overburst.CombatPerformance.folder", "Overburst.CombatPerformance.phase", "Overburst.VisualPlay.Session.plan", "Overburst.VisualPlay.Session.deferredPlan", "Overburst.PlayerFootstepProductVerifier.Pending", "Overburst.CombatFacingVerifier.output", "Overburst.CrustaspikanMaterialVerifier.plan", "Overburst.KANGoal.BuildOwner" })
            if (!string.IsNullOrEmpty(SessionState.GetString(key, ""))) throw new InvalidOperationException("Foreign owner: " + key);
    }
    static void Tick()
    {
        if (Phase == 0) { EditorApplication.update -= Tick; return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (Phase == 3) { ReturnToEditor(); return; }
            if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(KeyPrefix + "Deadline", "0"), CultureInfo.InvariantCulture)) throw new TimeoutException("Pointer verification deadline.");
            if (!EditorApplication.isPlaying || IsolatedSavePlayGuard.ActiveDirectory != Account) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (Phase == 1)
            {
                if (!AccountBootstrap.Ready || !WorldSessionState.IsHideout || PlayerContext.Instance?.CurrentActor == null || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching) { readySinceFrame = -1; return; }
                if (readySinceFrame < 0) { readySinceFrame = Time.frameCount; return; }
                if (Time.frameCount - readySinceFrame < 10) return;
                cycleOutput = Path.Combine(Output, "Cycle" + SessionState.GetInt(KeyPrefix + "Cycle", 1));
                Directory.CreateDirectory(cycleOutput); checks.Clear(); frame = -1;
                EditorApplication.LockReloadAssemblies(); reloadLocked = true;
                AssetDatabase.DisallowAutoRefresh(); refreshLocked = true;
                AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
                work.Push(Run()); Phase = 2;
            }
            if (lostWorkOnReload) throw new InvalidOperationException("Owned UI coroutine was lost during forced domain reload; no GUI PASS can be inferred.");
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            while (work.Count > 0)
            {
                var current = work.Peek();
                if (current.MoveNext()) { if (current.Current is IEnumerator nested) { work.Push(nested); continue; } return; }
                (work.Pop() as IDisposable)?.Dispose();
            }
            Finish("PASS", null);
        }
        catch (Exception error) { Finish("FAIL", error); }
    }
    static void Finish(string status, Exception error)
    {
        AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
        while (work.Count > 0) { try { (work.Pop() as IDisposable)?.Dispose(); } catch { } }
        if (refreshLocked) { AssetDatabase.AllowAutoRefresh(); refreshLocked = false; }
        if (reloadLocked) { EditorApplication.UnlockReloadAssemblies(); reloadLocked = false; }
        SessionState.SetString(KeyPrefix + "Status", status);
        Phase = 3;
        string path = cycleOutput ?? Output;
        try
        {
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(Path.Combine(path, "pointer-result.json"), JsonConvert.SerializeObject(new { status, checks, error = error?.ToString(), scope = "Automated real UI input; human pointer feel not asserted." }, Formatting.Indented));
        }
        catch (Exception writeError) { SessionState.SetString(KeyPrefix + "Status", "FAIL_RESULT_WRITE"); Debug.LogWarning("[KAN14 검사] 결과 저장 실패. 소유 Play와 계정 반환을 계속합니다: " + writeError.Message); }
        finally { if (EditorApplication.isPlaying && IsolatedSavePlayGuard.ActiveDirectory == Account) EditorApplication.ExitPlaymode(); }
    }
    static void BeforeReload() { if (Phase == 2) Finish("FAIL_RELOADED", new InvalidOperationException("UI verification interrupted by domain reload.")); }
    static void ReturnToEditor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try { RequireNoForeignSession(); } catch { return; }
        foreach (string value in new[] { Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), IsolatedSavePlayGuard.ActiveDirectory, SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "") })
            if (!string.IsNullOrEmpty(value) && Path.GetFullPath(value) != Account) return;
        IsolatedSavePlayGuard.UseRealAccount();
        bool guard = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")) && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""));
        if (guard && SessionState.GetString(KeyPrefix + "Status", "") == "PASS" && SessionState.GetInt(KeyPrefix + "Cycle", 1) == 1)
        {
            SessionState.SetInt(KeyPrefix + "Cycle", 2); Phase = 1; cycleOutput = null; readySinceFrame = -1;
            IsolatedSavePlayGuard.EnterIsolatedPlay(Account); return;
        }
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(KeyPrefix + "StartScene", ""));
        Application.runInBackground = SessionState.GetBool(KeyPrefix + "Background", false);
        bool scenePreserved = File.ReadAllText(Path.Combine(Output, "before.json")) == JsonConvert.SerializeObject(Scenes(), Formatting.Indented);
        string result = guard && scenePreserved && SessionState.GetString(KeyPrefix + "Status", "") == "PASS" ? "PASS" : "FAIL";
        File.WriteAllText(Path.Combine(Output, "return.json"), JsonConvert.SerializeObject(new { status = result, guard, scenePreserved, scenes = Scenes(), startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), inputRestored = originalSettings == null }, Formatting.Indented));
        foreach (string suffix in new[] { "Output", "StartScene", "Deadline", "Status" }) SessionState.EraseString(KeyPrefix + suffix);
        SessionState.EraseInt(KeyPrefix + "Cycle"); SessionState.EraseBool(KeyPrefix + "Background"); Phase = 0;
        EditorApplication.update -= Tick;
    }
    static object[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => (object)new { s.path, s.isDirty, s.rootCount }).ToArray();
    static void Check(bool pass, string label, object evidence = null)
    {
        checks.Add(new { label, pass, frame = Time.frameCount, evidence });
        File.WriteAllText(Path.Combine(cycleOutput, "pointer-progress.json"), JsonConvert.SerializeObject(checks, Formatting.Indented));
        if (!pass) throw new InvalidOperationException(label);
    }
    static IEnumerator Frames(int n = 3) { for (int i = 0; i < n; i++) yield return null; }
    static void InputTick()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic || keyboard == null || mouse == null) return;
        if (!keyboard.enabled) InputSystem.EnableDevice(keyboard); if (!mouse.enabled) InputSystem.EnableDevice(mouse);
        InputSystem.QueueStateEvent(keyboard, heldKey.HasValue ? new KeyboardState(heldKey.Value) : new KeyboardState());
        InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer, buttons = (ushort)(heldButton ? 1 : 0) });
    }
    static IEnumerator Run()
    {
        PlayerActorRuntime actor = null;
        Vector3 previousPosition = default;
        Quaternion previousRotation = default;
        try
        {
            stash = UnityEngine.Object.FindFirstObjectByType<StashUI>(FindObjectsInactive.Include);
            inventory = UnityEngine.Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
            session = AccountGameplaySession.Current;
            Check(stash != null && inventory != null && session != null && EventSystem.current != null, "Authored UI and product account ready");
            foreach (var pair in new[] { (stash.gameObject, "stash"), ((GameObject)typeof(InventoryUI).GetField("inventoryPanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(inventory), "inventory") })
            {
                var trace = pair.Item1.AddComponent<StashUiVisibilityTrace>(); trace.Output = cycleOutput; trace.Role = pair.Item2; trace.Stage = "setup"; visibilityTraces.Add(trace);
            }
            originalSettings = InputSystem.settings; originalSettingsJson = EditorJsonUtility.ToJson(originalSettings);
            ownedSettings = UnityEngine.Object.Instantiate(originalSettings); ownedSettings.hideFlags = HideFlags.HideAndDontSave;
            ownedSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            ownedSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = ownedSettings;
            keyboard = InputSystem.AddDevice<Keyboard>("KAN14OwnedKeyboard"); mouse = InputSystem.AddDevice<Mouse>("KAN14OwnedMouse");
            var module = EventSystem.current.GetComponent<InputSystemUIInputModule>();
            Check(module != null, "Actual InputSystem UI module");
            foreach (var asset in new[] { PlayerInputFacade.Current.RuntimeAsset, module.actionsAsset }.Where(a => a != null).Distinct())
            { originalDevices[asset] = asset.devices.HasValue ? asset.devices.Value.ToArray() : null; asset.devices = new InputDevice[] { keyboard, mouse }; }
            InputSystem.onBeforeUpdate += InputTick;
            yield return Frames(4);
            actor = PlayerContext.Instance.CurrentActor;
            previousPosition = actor.transform.position; previousRotation = actor.transform.rotation;
            stashInteraction = UnityEngine.Object.FindObjectsByType<StashInteractable>(FindObjectsSortMode.None)
                .Where(s => s.gameObject.scene == WorldSessionState.ContentScene)
                .OrderBy(s => Vector3.Distance(s.transform.position, actor.transform.position)).FirstOrDefault();
            Check(stashInteraction != null, "Actual content-scene stash interaction exists");
            Vector3 approach = stashInteraction.transform.position + Vector3.back * Mathf.Min(1f, stashInteraction.InteractionRange * .35f);
            ActorTeleportUtility.TeleportSafely(actor.transform, approach, Quaternion.identity);
            yield return Frames(4);
            var playerPoint = actor.transform.position; var stashPoint = stashInteraction.transform.position;
            Check(stashInteraction.IsPlayerInRange(), "Real actor is inside product stash interaction distance", new { player = new[] { playerPoint.x, playerPoint.y, playerPoint.z }, stash = new[] { stashPoint.x, stashPoint.y, stashPoint.z }, radius = stashInteraction.InteractionRange });
            inventory.SetVisible(false);
            Check(stashInteraction.TryInteract(actor) == InteractionExecutionResult.Succeeded && stash.IsOpen, "Actual world stash interaction opens the tested UI");
            yield return Frames(4); stash.Close();
            foreach (bool initiallyVisible in new[] { false, true })
                foreach (bool fromStash in new[] { false, true })
                    foreach (Key closeKey in new[] { Key.Escape, Key.Tab })
                        foreach (string order in new[] { "close-first", "release-first-on-source", "same-frame-outside", "same-frame-on-target" })
                            yield return CloseCase(initiallyVisible, fromStash, closeKey, order);
            yield return NormalAndLocked();
            yield return ExternalDropControl();
        }
        finally
        {
            InputSystem.onBeforeUpdate -= InputTick; heldButton = false; heldKey = null;
            foreach (var trace in visibilityTraces) if (trace != null) trace.Stage = "cleanup";
            if (lockedFixture != null) lockedFixture.SetLocked(false); lockedFixture = null;
            DragSlot.ClearDragState(); stash?.Close(); inventory?.SetVisible(false);
            foreach (var pair in originalDevices) if (pair.Key != null) pair.Key.devices = pair.Value; originalDevices.Clear();
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (originalSettings != null)
            {
                InputSystem.settings = originalSettings;
                bool restored = EditorJsonUtility.ToJson(originalSettings) == originalSettingsJson;
                File.WriteAllText(Path.Combine(cycleOutput, "input-return.json"), JsonConvert.SerializeObject(new { status = restored ? "PASS" : "FAIL", originalUnchanged = restored, ownedDevicesRemoved = keyboard == null || !keyboard.added, mouseRemoved = mouse == null || !mouse.added }));
            }
            if (ownedSettings != null) UnityEngine.Object.DestroyImmediate(ownedSettings);
            foreach (var trace in visibilityTraces) if (trace != null) { trace.Output = null; UnityEngine.Object.DestroyImmediate(trace); } visibilityTraces.Clear();
            if (actor != null) ActorTeleportUtility.TeleportSafely(actor.transform, previousPosition, previousRotation);
            stashInteraction = null;
            originalSettings = ownedSettings = null; keyboard = null; mouse = null;
            CleanupOwnedPickups();
            fixtureIds.Clear(); session = null; stash = null; inventory = null;
        }
    }
    static (string inv, string stash, string merge) Seed(bool externalDrop = false)
    {
        heldButton = false; heldKey = null; stash.Close(); inventory.SetVisible(false);
        CleanupOwnedPickups();
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var data = registry.Entries.Select(e => e.asset).OfType<CurrencyItemData>().First(c => c.maxStack >= 32);
        BaseItemData inventoryData = externalDrop ? registry.Entries.Select(e => e.asset).OfType<WeaponItemData>().First() : (BaseItemData)data;
        var a = ItemSnapshotCodec.Capture(new ItemData(inventoryData, 1, ItemGrade.Common, externalDrop ? 1 : 7), registry);
        var b = ItemSnapshotCodec.Capture(new ItemData(data, 1, ItemGrade.Common, 5), registry);
        var c = ItemSnapshotCodec.Capture(new ItemData(data, 1, ItemGrade.Common, 3), registry);
        Check(session.ExecuteState("kan14-gui-seed", s =>
        {
            s.items.RemoveAll(i => fixtureIds.Contains(i.instanceId));
            for (int i = 0; i < s.inventory.Count; i++) if (fixtureIds.Contains(s.inventory[i] ?? "")) s.inventory[i] = null;
            foreach (var tab in s.stashTabs) for (int i = 0; i < tab.slots.Count; i++) if (fixtureIds.Contains(tab.slots[i] ?? "")) tab.slots[i] = null;
            s.items.Add(a); s.items.Add(b); s.items.Add(c);
            s.inventory[s.inventory.FindIndex(0, s.unlockedSlots, string.IsNullOrEmpty)] = a.instanceId;
            var slots = s.stashTabs[s.currentStashTab].slots;
            slots[slots.FindIndex(string.IsNullOrEmpty)] = b.instanceId;
            slots[slots.FindIndex(string.IsNullOrEmpty)] = c.instanceId;
        }), "Durable isolated fixture seed");
        fixtureIds.Add(a.instanceId); fixtureIds.Add(b.instanceId); fixtureIds.Add(c.instanceId);
        return (a.instanceId, b.instanceId, c.instanceId);
    }
    static string ObjectPath(Transform t) => t.parent == null ? t.name : ObjectPath(t.parent) + "/" + t.name;
    static SlotUI FindSlot(string id)
    {
        var slots = UnityEngine.Object.FindObjectsByType<SlotUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var result = slots.FirstOrDefault(s => s.gameObject.activeInHierarchy && s.DisplayItem?.runtimeInstanceId == id && !s.IsWeaponSlot && !s.IsBagSlot);
        if (result == null)
            File.WriteAllText(Path.Combine(cycleOutput, "missing-slot.json"), JsonConvert.SerializeObject(new
            { id, inventoryVisible = inventory.IsVisible, stashOpen = stash.IsOpen, usableStashCanvas = stash.HasUsableCanvasRoot,
                slots = slots.Select(s => new { path = ObjectPath(s.transform), active = s.gameObject.activeInHierarchy, selfActive = s.gameObject.activeSelf, index = s.SlotIndex, locked = s.IsLocked, itemId = s.DisplayItem?.runtimeInstanceId, bridge = s.OwnerBridge?.GetType().Name }).ToArray(),
                inventories = UnityEngine.Object.FindObjectsByType<PlayerInventory>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(m => new { path = ObjectPath(m.transform), ids = m.Items.Where(i => i != null).Select(i => i.runtimeInstanceId).ToArray(), isShared = ReferenceEquals(m, PlayerAccountInventoryService.SharedInventory), isActor = ReferenceEquals(m, PlayerContext.Instance.CurrentActorInventory) }).ToArray(),
                canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(c => new { path = ObjectPath(c.transform), active = c.gameObject.activeInHierarchy, selfActive = c.gameObject.activeSelf, enabled = c.enabled }).ToArray()
            }, Formatting.Indented));
        if (result == null) throw new InvalidOperationException("Actual UI has no visible slot for isolated fixture " + id);
        return result;
    }
    static Vector2 Point(SlotUI slot)
    {
        var rect = slot.transform as RectTransform; var canvas = slot.GetComponentInParent<Canvas>();
        return RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, rect.TransformPoint(rect.rect.center));
    }
    static List<RaycastResult> Rays(Vector2 p)
    {
        var results = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = p }, results); return results;
    }
    static Vector2 Outside()
    {
        for (int y = 1; y < 10; y++) for (int x = 1; x < 10; x++)
        { var point = new Vector2(Screen.width * x / 10f, Screen.height * y / 10f); if (Rays(point).Count == 0) return point; }
        throw new InvalidOperationException("No raycast-clear outside-UI coordinate found.");
    }
    static IEnumerator Drag(SlotUI source, Vector2 destination, bool expectDrag = true)
    {
        heldButton = false; pointer = Point(source); yield return Frames();
        Check(Rays(pointer).FirstOrDefault().gameObject?.GetComponentInParent<SlotUI>() == source, "Real source graphic raycast", new { source = source.name, pointer = new[] { pointer.x, pointer.y } });
        heldButton = true; yield return Frames(); pointer = destination; yield return Frames();
        Check(DragSlot.IsDragging == expectDrag, "Product drag begin", new { source = source.name, expectDrag, draggedId = DragSlot.DraggedItem?.runtimeInstanceId, currentModule = EventSystem.current.currentInputModule?.GetType().Name });
    }
    static string Ownership()
    {
        var s = session.Read();
        return string.Join("|", s.items.Where(i => fixtureIds.Contains(i.instanceId)).OrderBy(i => i.instanceId).Select(i => i.instanceId + ":" + i.count + ":" + (s.inventory.Contains(i.instanceId) ? "inventory" : "stash" + s.stashTabs.FindIndex(t => t.slots.Contains(i.instanceId)))));
    }
    static ItemData CurrencyItem(CurrencyWorldPickup pickup) => (ItemData)typeof(CurrencyWorldPickup).GetField("runtimeCurrencyItem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(pickup);
    static int Pickups() => UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Count(p => p.RuntimeItem != null && fixtureIds.Contains(p.RuntimeItem.runtimeInstanceId)) + UnityEngine.Object.FindObjectsByType<CurrencyWorldPickup>(FindObjectsSortMode.None).Count(p => CurrencyItem(p) != null && fixtureIds.Contains(CurrencyItem(p).runtimeInstanceId));
    static void CleanupOwnedPickups()
    {
        foreach (var pickup in UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None)) if (pickup.RuntimeItem != null && fixtureIds.Contains(pickup.RuntimeItem.runtimeInstanceId)) UnityEngine.Object.Destroy(pickup.gameObject);
        foreach (var pickup in UnityEngine.Object.FindObjectsByType<CurrencyWorldPickup>(FindObjectsSortMode.None)) if (CurrencyItem(pickup) != null && fixtureIds.Contains(CurrencyItem(pickup).runtimeInstanceId)) UnityEngine.Object.Destroy(pickup.gameObject);
    }
    static IEnumerator CloseCase(bool visible, bool fromStash, Key key, string order)
    {
        foreach (var trace in visibilityTraces) trace.Stage = "seed";
        var ids = Seed(); foreach (var trace in visibilityTraces) trace.Stage = $"{visible}/{fromStash}/{key}/{order}";
        Check(stashInteraction.IsPlayerInRange(), "Stash range remains valid before drag", new { visible, fromStash, key = key.ToString(), order });
        inventory.SetVisible(visible); stash.Open();
        Check(stash.IsOpen && inventory.IsVisible, "Product stash open accepted after boot settled", new { visible, fromStash, key = key.ToString(), order, switching = PersistentSceneFlow.Instance.IsSwitching, world = WorldSessionState.Phase.ToString() });
        yield return Frames();
        var source = FindSlot(fromStash ? ids.stash : ids.inv); string before = Ownership(); int pickups = Pickups();
        Vector2 outside = Outside(); yield return Drag(source, outside);
        if (order == "release-first-on-source") { pointer = Point(source); yield return Frames(); heldButton = false; yield return Frames(); }
        if (order == "same-frame-on-target")
        {
            var target = UnityEngine.Object.FindObjectsByType<SlotUI>(FindObjectsSortMode.None).First(s => s.gameObject.activeInHierarchy && ReferenceEquals(s.OwnerBridge, FindSlot(ids.stash).OwnerBridge) && s.DisplayItem == null && !s.IsLocked);
            pointer = Point(target); yield return Frames();
        }
        heldKey = key; if (order.StartsWith("same-frame-", StringComparison.Ordinal)) heldButton = false;
        yield return Frames();
        Check(!stash.IsOpen && !DragSlot.IsDragging, "Close key cancels drag", new { visible, fromStash, key = key.ToString(), order, facadeInventory = PlayerInputFacade.Current.InventoryPressedThisFrame, inventoryVisible = inventory.IsVisible });
        heldKey = null; heldButton = false; yield return Frames();
        Check(Ownership() == before && Pickups() == pickups && DragSlot.OriginSlot == null, "No accidental floor drop or ownership change", new { visible, fromStash, key = key.ToString(), order, before, after = Ownership(), pickupsBefore = pickups, pickupsAfter = Pickups() });
    }
    static IEnumerator NormalAndLocked()
    {
        var ids = Seed(); stash.Open(); yield return Frames();
        var source = FindSlot(ids.stash); var bridge = source.OwnerBridge;
        var target = UnityEngine.Object.FindObjectsByType<SlotUI>(FindObjectsSortMode.None).First(s => s.gameObject.activeInHierarchy && ReferenceEquals(s.OwnerBridge, bridge) && s.DisplayItem == null && !s.IsLocked);
        int targetIndex = target.SlotIndex;
        yield return Drag(source, Point(target)); heldButton = false; yield return Frames();
        Check(PlayerAccountInventoryService.SharedStash.GetItemAt(targetIndex)?.runtimeInstanceId == ids.stash && !DragSlot.IsDragging, "Normal stash internal move", new { targetIndex });
        source = FindSlot(ids.stash); target = FindSlot(ids.merge);
        yield return Drag(source, Point(target)); heldButton = false; yield return Frames();
        Check(PlayerAccountInventoryService.SharedStash.Items.Any(i => i != null && i.runtimeInstanceId == ids.merge && i.stackCount == 8) && !session.Read().items.Any(i => i.instanceId == ids.stash), "Normal stack merge preserves eight items and one owner");
        ids = Seed(); stash.Open(); yield return Frames();
        source = FindSlot(ids.inv); string before = Ownership();
        lockedFixture = source; source.SetLocked(true);
        yield return Drag(source, Outside(), false); heldButton = false; yield return Frames();
        Check(Ownership() == before && Pickups() == 0, "Locked source cannot drag"); source.SetLocked(false); lockedFixture = null;
        target = UnityEngine.Object.FindObjectsByType<SlotUI>(FindObjectsSortMode.None).First(s => s.gameObject.activeInHierarchy && ReferenceEquals(s.OwnerBridge, source.OwnerBridge) && s.IsLocked && !s.IsWeaponSlot && !s.IsBagSlot);
        yield return Drag(source, Point(target)); heldButton = false; yield return Frames();
        Check(Ownership() == before && Pickups() == 0 && !DragSlot.IsDragging, "Authored locked inventory target rejects drop");
    }
    static IEnumerator ExternalDropControl()
    {
        var ids = Seed(true); inventory.SetVisible(true); yield return Frames();
        var source = FindSlot(ids.inv); yield return Drag(source, Outside()); heldButton = false; yield return Frames(5);
        Check(!session.Read().items.Any(i => i.instanceId == ids.inv) && Pickups() == 1, "Normal inventory outside drop remains available", new { pickups = Pickups(), id = ids.inv });
    }
}
public sealed class StashUiVisibilityTrace : MonoBehaviour
{
    public string Output, Role, Stage;
    void OnDisable()
    {
        if (!string.IsNullOrEmpty(Output) && Directory.Exists(Output))
            File.AppendAllText(Path.Combine(Output, "ui-disabled.jsonl"), JsonConvert.SerializeObject(new { role = Role, stage = Stage, frame = Time.frameCount, trace = StackTraceUtility.ExtractStackTrace() }) + "\n");
    }
}
#endif
