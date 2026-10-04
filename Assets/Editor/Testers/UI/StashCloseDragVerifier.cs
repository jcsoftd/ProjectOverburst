#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 창고 닫기 뒤 포인터 release가 외부 드롭으로 이어지는 경로를 검사한다.
[InitializeOnLoad]
public static class StashCloseDragVerifier
{
    const string Key = "Overburst.KAN14.";
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    static int Phase { get => SessionState.GetInt(Key + "Phase", 0); set => SessionState.SetInt(Key + "Phase", value); }
    public static string Status => SessionState.GetString(Key + "Status", "NOT_RUN");
    static StashCloseDragVerifier() { EditorApplication.update += Tick; }

    public static string StartPlayChecks(string directory)
    {
        if (Phase != 0 || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required.");
        RequireNoForeignSession();
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) ||
            !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) ||
            !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Another account is pending.");
        Scene boot = SceneManager.GetSceneByName(PersistentSceneFlow.PersistentSceneName);
        if (!boot.IsValid() || !boot.isLoaded) throw new InvalidOperationException("Loaded PersistentScene required.");
        directory = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Codex output required.");
        Directory.CreateDirectory(directory);
        SessionState.SetString(Key + "Output", directory);
        SessionState.SetString(Key + "ActiveScene", SceneManager.GetActiveScene().path);
        SessionState.SetBool(Key + "Dirty", SceneManager.GetActiveScene().isDirty);
        SessionState.SetString(Key + "StartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key + "Background", Application.runInBackground);
        SessionState.SetString(Key + "PlayDirectory", Path.GetFullPath(Path.Combine(directory, "Account")));
        SessionState.SetString(Key + "Deadline", (EditorApplication.timeSinceStartup + 180).ToString("R", CultureInfo.InvariantCulture));
        SessionState.SetString(Key + "Status", "RUNNING");
        SessionState.SetInt(Key + "Cycle", 1);
        Phase = 1;
        SceneManager.SetActiveScene(boot);
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(SessionState.GetString(Key + "PlayDirectory", "")); }
        catch { Phase = 2; throw; }
        return "Started two isolated product Play checks";
    }

    static void Tick()
    {
        if (Phase == 0 || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string account = SessionState.GetString(Key + "PlayDirectory", "");
        string directory = SessionState.GetString(Key + "Output", "");
        try
        {
            bool expired = EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + "Deadline", "0"), CultureInfo.InvariantCulture);
            if (Phase == 2 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                RequireNoForeignSession();
                foreach (string value in new[] { Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), IsolatedSavePlayGuard.ActiveDirectory,
                    SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "") })
                    if (!string.IsNullOrEmpty(value) && Path.GetFullPath(value) != account) throw new InvalidOperationException("Foreign account return deferred.");
                IsolatedSavePlayGuard.UseRealAccount();
                if (SessionState.GetInt(Key + "Cycle", 1) == 1 && Status == "RUNNING" && !expired)
                {
                    SessionState.SetInt(Key + "Cycle", 2);
                    Phase = 1;
                    IsolatedSavePlayGuard.EnterIsolatedPlay(account);
                    return;
                }
                Scene original = SceneManager.GetSceneByPath(SessionState.GetString(Key + "ActiveScene", ""));
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                bool normalPlay = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) &&
                    string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) &&
                    string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")) &&
                    string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""));
                bool scenePreserved = original.IsValid() && original.isDirty == SessionState.GetBool(Key + "Dirty", false);
                bool startPreserved = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == SessionState.GetString(Key + "StartScene", "");
                Application.runInBackground = SessionState.GetBool(Key + "Background", false);
                bool backgroundPreserved = Application.runInBackground == SessionState.GetBool(Key + "Background", false);
                if (Status == "RUNNING") SessionState.SetString(Key + "Status", normalPlay && scenePreserved && startPreserved && backgroundPreserved ? "PASS" : "FAIL");
                Phase = 0;
                foreach (string suffix in new[] { "PlayDirectory", "ActiveScene", "StartScene", "Deadline" }) SessionState.EraseString(Key + suffix);
                SessionState.EraseInt(Key + "Cycle");
                SessionState.EraseBool(Key + "Dirty");
                SessionState.EraseBool(Key + "Background");
                File.WriteAllText(Path.Combine(directory, "return.json"), JsonConvert.SerializeObject(new { status = Status, normalPlay, scenePreserved, startPreserved, backgroundPreserved, pending = Phase }, Formatting.Indented));
                return;
            }
            if (expired) throw new TimeoutException("KAN14 Play checks timed out.");
            if (Phase != 1 || !EditorApplication.isPlaying) return;
            if (IsolatedSavePlayGuard.ActiveDirectory != account) throw new InvalidOperationException("Foreign Play; verification deferred.");
            Application.runInBackground = true;
            EditorApplication.QueuePlayerLoopUpdate();
            var flow = UnityEngine.Object.FindFirstObjectByType<PersistentSceneFlow>(FindObjectsInactive.Include);
            if (flow != null && !flow.gameObject.activeInHierarchy) flow.gameObject.SetActive(true);
            if (!Overburst.Persistence.AccountBootstrap.Ready || !WorldSessionState.IsHideout || PlayerContext.Instance?.CurrentActor == null) return;
            string cycleDirectory = Path.Combine(directory, "Cycle" + SessionState.GetInt(Key + "Cycle", 1));
            string json = Run(cycleDirectory);
            var result = Newtonsoft.Json.Linq.JObject.Parse(json);
            foreach (var check in result["checks"]) if (!(bool)check["pass"]) SessionState.SetString(Key + "Status", "FAIL");
            if (!RunLiveFlow(cycleDirectory)) SessionState.SetString(Key + "Status", "FAIL");
            Phase = 2;
            EditorApplication.ExitPlaymode();
        }
        catch (Exception error)
        {
            SessionState.SetString(Key + "Status", "FAIL");
            Phase = 2;
            try { File.WriteAllText(Path.Combine(directory, "failure.txt"), error.ToString()); } catch { }
            if (EditorApplication.isPlaying && IsolatedSavePlayGuard.ActiveDirectory == account) EditorApplication.ExitPlaymode();
            if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + "Deadline", "0"), CultureInfo.InvariantCulture) + 60)
            {
                Phase = 0; // 외부 작업을 건드리지 않고 시간 제한으로 콜백을 유휴화한다.
                SessionState.SetString(Key + "Status", "BLOCKED_RETURN");
            }
        }
    }

    static void RequireNoForeignSession()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString("Overburst.VisualPlay.Session.plan", "")) ||
            !string.IsNullOrEmpty(SessionState.GetString("Overburst.VisualPlay.Session.deferredPlan", "")) ||
            !string.IsNullOrEmpty(SessionState.GetString("Overburst.CombatFacingVerifier.output", "")))
            throw new InvalidOperationException("Another verification is pending.");
    }

    static bool RunLiveFlow(string directory)
    {
        var stash = UnityEngine.Object.FindFirstObjectByType<StashUI>(FindObjectsInactive.Include);
        var inventory = UnityEngine.Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        var model = PlayerContext.Instance?.CurrentActorInventory;
        if (stash == null || inventory == null || model == null || stash.IsOpen || DragSlot.IsDragging)
            throw new InvalidOperationException("Fresh live stash/inventory required.");
        bool wasVisible = inventory.IsVisible;
        var checks = new List<bool>();
        int seedIndex = model.FindFirstEmptySlotWithin(model.UnlockedSlotCount);
        if (seedIndex < 0) throw new InvalidOperationException("Isolated inventory needs an empty slot.");
        var data = AssetDatabase.FindAssets("t:WeaponItemData", new[] { "Assets/ProjectOverburst" })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<WeaponItemData>).FirstOrDefault(item => item != null);
        if (data == null) throw new InvalidOperationException("Authored weapon data required.");
        var probe = new ItemData(data, 1, ItemGrade.Common);
        var ownedPickups = new List<WorldItemPickup>();
        try
        {
            if (!model.SetItemAt(seedIndex, probe)) throw new InvalidOperationException("Isolated item seed failed.");
            inventory.SetVisible(true);
            stash.Open();
            checks.Add(stash.IsOpen && inventory.IsVisible && inventory.InputToggleLocked);
            var slot = inventory.GetComponentsInChildren<SlotUI>(true).FirstOrDefault(s => !s.IsLocked && !s.IsBagSlot && !s.IsWeaponSlot && ReferenceEquals(s.DisplayItem, probe) && s.OwnerBridge is InventorySlotBridge);
            if (slot == null) throw new InvalidOperationException("Live inventory needs a draggable item.");
            ItemData[] before = model.Items.ToArray();
            int pickups = UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Length;
            if (!DragSlot.BeginExternalDrag(slot, slot.DisplayItem, slot.GetComponentInParent<Canvas>(true), Vector2.zero))
                throw new InvalidOperationException("Live drag failed to start.");
            stash.Close();
            checks.Add(!stash.IsOpen && inventory.IsVisible && !inventory.InputToggleLocked);
            checks.Add(!DragSlot.IsDragging && DragSlot.OriginSlot == null && Get(typeof(DragSlot), "dragIcon") == null);
            DragSlot.CompleteDrag(null);
            checks.Add(before.SequenceEqual(model.Items));
            checks.Add(UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Length == pickups);
            int itemsAfterClose = model.Items.Count(item => item != null);
            if (!DragSlot.BeginExternalDrag(slot, probe, slot.GetComponentInParent<Canvas>(true), Vector2.zero))
                throw new InvalidOperationException("Positive-control drag failed.");
            DragSlot.CompleteDrag(null);
            ownedPickups.AddRange(UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Where(p => ReferenceEquals(p.RuntimeItem, probe)));
            checks.Add(!model.ContainsItem(probe));
            checks.Add(ownedPickups.Count == 1);
            File.WriteAllText(Path.Combine(directory, "live_stash.json"), JsonConvert.SerializeObject(new {
                checks, itemCountBefore = before.Count(item => item != null), itemCountAfterClose = itemsAfterClose,
                itemCountAfterNormalDrop = model.Items.Count(item => item != null), spawnedControlPickups = ownedPickups.Count, pickups
            }, Formatting.Indented));
            return checks.All(pass => pass);
        }
        finally
        {
            DragSlot.ClearDragState();
            int remaining = model.FindFirstMatchingItemIndex(probe);
            if (remaining >= 0) model.SetItemAt(remaining, null);
            foreach (var pickup in ownedPickups) if (pickup != null) UnityEngine.Object.DestroyImmediate(pickup.gameObject);
            stash.Close();
            inventory.SetVisible(wasVisible);
        }
    }

    public static string Run(string directory)
    {
        string ownedPlay = SessionState.GetString("Overburst.KAN14.PlayDirectory", "");
        if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            throw new InvalidOperationException("Play transition in progress.");
        if (EditorApplication.isPlaying && (string.IsNullOrEmpty(ownedPlay) || ownedPlay != IsolatedSavePlayGuard.ActiveDirectory))
            throw new InvalidOperationException("Another session owns Play.");
        if (!EditorApplication.isPlaying && (!string.IsNullOrEmpty(SessionState.GetString("Overburst.VisualPlay.Session.plan", "")) ||
            !string.IsNullOrEmpty(SessionState.GetString("Overburst.VisualPlay.Session.deferredPlan", "")) ||
            !string.IsNullOrEmpty(SessionState.GetString("Overburst.CombatFacingVerifier.output", ""))))
            throw new InvalidOperationException("Another verification session is pending.");
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || DragSlot.IsDragging ||
            DragSlot.OriginSlot != null || Get(typeof(DragSlot), "dragIcon") != null)
            throw new InvalidOperationException("Editor must be idle with no existing drag.");

        var checks = new List<object>();
        Scene original = SceneManager.GetActiveScene();
        bool dirty = original.isDirty;
        Scene preview = default;
        GameObject root = null;
        try
        {
            if (!EditorApplication.isPlaying) preview = EditorSceneManager.NewPreviewScene();
            root = new GameObject("KAN14_StashCloseFixture");
            root.SetActive(false);
            root.hideFlags = HideFlags.HideAndDontSave;
            root.AddComponent<Canvas>();
            if (preview.IsValid()) SceneManager.MoveGameObjectToScene(root, preview);
            var inventoryRoot = Child(root, "Inventory");
            var inventory = inventoryRoot.AddComponent<InventoryUI>();
            var inventoryPanel = Child(inventoryRoot, "InventoryPanel");
            Set(inventory, "initialized", true);
            Set(inventory, "inventoryPanel", inventoryPanel);
            var stashRoot = Child(root, "Stash");
            var stash = stashRoot.AddComponent<StashUI>();
            var stashBridge = stashRoot.AddComponent<StashSlotBridge>();
            var stashPanel = Child(stashRoot, "StashPanel");
            Set(stash, "stashPanel", stashPanel);
            Set(stash, "slotBridge", stashBridge);
            Set(stash, "inventoryUI", inventory);
            Set(stash, "initialized", true);
            var inventoryBridge = new ProbeBridge();
            var unrelatedBridge = new ProbeBridge();
            var item = new ItemData(null, 1, ItemGrade.Common);
            SlotUI inventorySlot = Slot(inventoryPanel, inventoryBridge, item);
            SlotUI stashSlot = Slot(stashPanel, stashBridge, item);
            SlotUI unrelatedSlot = Slot(Child(root, "OtherUI"), unrelatedBridge, item);
            var target = Child(root, "PendingDrop").AddComponent<DropSlot>();

            Action<string, bool> check = (name, pass) => checks.Add(new { name, pass });
            Action<bool> open = wasVisible => {
                inventoryPanel.SetActive(true);
                stashPanel.SetActive(true);
                Set(stash, "isOpen", true);
                Set(stash, "inventoryWasOpenBeforeStash", wasVisible);
                inventory.InputToggleLocked = true;
            };

            open(true);
            Begin(inventorySlot, inventoryBridge, item);
            DragSlot.RegisterDropTarget(target);
            // Inventory.Update가 잠금 상태에서 먼저 실행되어 인벤토리가 유지되는 순서.
            typeof(InventoryUI).GetMethod("Update", Fields).Invoke(inventory, null);
            stash.Close();
            check("preopened_inventory_kept_visible", inventory.IsVisible && !inventory.InputToggleLocked);
            check("inventory_drag_cancelled_before_release", !DragSlot.IsDragging && DragSlot.OriginSlot == null);
            check("drag_icon_reference_cleared", Get(typeof(DragSlot), "dragIcon") == null);
            check("pending_target_and_drop_flag_cleared", Get(typeof(DragSlot), "pendingDropSlot") == null && !DragSlot.DropHandled);
            DragSlot.CompleteDrag(null); // UI 밖에서 버튼 release.
            check("close_then_release_does_not_drop_item", inventoryBridge.ExternalDrops == 0 && inventoryBridge.Items == 1);
            stash.Close();
            DragSlot.CompleteDrag(null);
            check("repeated_close_release_safe", inventoryBridge.ExternalDrops == 0);

            open(false);
            Begin(inventorySlot, inventoryBridge, item);
            stash.Close();
            DragSlot.CompleteDrag(null);
            check("initially_hidden_inventory_restored_and_item_kept", !inventory.IsVisible && inventoryBridge.ExternalDrops == 0);

            open(true);
            Begin(stashSlot, stashBridge, item);
            stash.Close();
            check("stash_origin_drag_cancelled", !DragSlot.IsDragging && DragSlot.OriginSlot == null);
            DragSlot.CompleteDrag(null);

            open(true);
            Begin(unrelatedSlot, unrelatedBridge, item);
            stash.Close();
            check("unrelated_ui_drag_preserved", DragSlot.IsDragging && DragSlot.OriginSlot == unrelatedSlot);
            DragSlot.CompleteDrag(null);
            check("unrelated_external_drop_still_works", unrelatedBridge.ExternalDrops == 1 && unrelatedBridge.Items == 0);

            inventoryBridge.Reset();
            Begin(inventorySlot, inventoryBridge, item);
            stash.Close(); // 이미 닫힌 창고의 Close는 일반 인벤토리 드래그를 건드리지 않는다.
            check("closed_stash_keeps_normal_drag", DragSlot.IsDragging);
            DragSlot.CompleteDrag(null);
            check("normal_external_drop_still_works", inventoryBridge.ExternalDrops == 1 && inventoryBridge.Items == 0);
            check("drag_preview_released", inventoryBridge.ClearCalls > 0);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstInventory_Rpg11.prefab");
            check("authored_inventory_prefab_has_slots", prefab != null && prefab.GetComponentsInChildren<SlotUI>(true).Length > 0);
            int nativeInventories = 0;
            bool layoutValid = true;
            foreach (var ui in UnityEngine.Object.FindObjectsByType<InventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (ui == inventory) continue;
                nativeInventories++;
                var panel = typeof(InventoryUI).GetField("inventoryPanel", Fields).GetValue(ui) as GameObject;
                layoutValid &= panel != null && panel.transform.IsChildOf(ui.transform) && panel.GetComponentsInChildren<SlotUI>(true).Length > 0;
            }
            check("native_inventory_panels_inside_inventory_root", nativeInventories > 0 && layoutValid);
        }
        finally
        {
            DragSlot.ClearDragState();
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
        checks.Add(new { name = "original_scene_and_dirty_state_preserved", pass = SceneManager.GetActiveScene() == original && original.isDirty == dirty });
        string json = JsonConvert.SerializeObject(new { mode = EditorApplication.isPlaying ? "PlayMode" : "EditMode", checks }, Formatting.Indented);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(directory).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output must be under Codex outputs.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "drag_checks.json"), json);
        return json;
    }

    static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    static SlotUI Slot(GameObject parent, ISlotInteractionBridge bridge, ItemData item)
    {
        var slot = Child(parent, "Slot").AddComponent<SlotUI>();
        Set(slot, "ownerBridge", bridge);
        typeof(SlotUI).GetProperty("DisplayItem").SetValue(slot, item);
        return slot;
    }

    static void Begin(SlotUI slot, ISlotInteractionBridge bridge, ItemData item)
    {
        if (EditorApplication.isPlaying)
        {
            if (!DragSlot.BeginExternalDrag(slot, item, slot.GetComponentInParent<Canvas>(true), Vector2.zero))
                throw new InvalidOperationException("Runtime drag could not start.");
            return;
        }
        typeof(DragSlot).GetProperty("OriginSlot").SetValue(null, slot);
        typeof(DragSlot).GetProperty("DraggedItem").SetValue(null, item);
        Set(typeof(DragSlot), "originBridge", bridge);
        Set(typeof(DragSlot), "dropHandled", false);
        bridge.BeginDragPreview(slot);
    }

    static object Get(Type type, string field) => type.GetField(field, Fields).GetValue(null);
    static void Set(object target, string field, object value)
    {
        Type type = target as Type ?? target.GetType();
        type.GetField(field, Fields).SetValue(target is Type ? null : target, value);
    }

    sealed class ProbeBridge : ISlotInteractionBridge
    {
        public int ExternalDrops, ClearCalls, Items = 1;
        public void Reset() { ExternalDrops = 0; Items = 1; }
        public void BeginDragPreview(SlotUI slot) { }
        public void ShowDragPreviewForTarget(SlotUI slot) { }
        public void ClearDragPreview() { ClearCalls++; }
        public bool HandleSlotClick(SlotClickContext context) => false;
        public bool HandleSlotDrop(SlotDropContext context) => false;
        public bool HandleExternalDrop(SlotUI slot, Vector2 position) { ExternalDrops++; Items--; return true; }
    }
}
#endif
