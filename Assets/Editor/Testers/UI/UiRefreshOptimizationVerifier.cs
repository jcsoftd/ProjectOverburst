using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Isolated presenter regression and warmed allocation checks; does not enter Play or mutate an account.</summary>
[InitializeOnLoad]
public static class UiRefreshOptimizationVerifier
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<object> checks = new List<object>();
    const string Key = "Overburst.UiRefreshOptimizationVerifier.";
    const string Guard = "Overburst.IsolatedSavePlayGuard.";
    static IEnumerator play;
    static int lastFrame;
    static string Output => SessionState.GetString(Key + "output", "");
    static bool Pending => SessionState.GetBool(Key + "pending", false);
    static UiRefreshOptimizationVerifier() { EditorApplication.update += Tick; EditorApplication.playModeStateChanged += State; }
    static void Check(bool pass, string name, object evidence = null)
    {
        checks.Add(new { name, pass, evidence });
        if (!pass) throw new InvalidOperationException(name);
    }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    static T Bind<T>(object target, string method) where T : Delegate => (T)Delegate.CreateDelegate(typeof(T), target, target.GetType().GetMethod(method, Private));
    static long Allocations(Action action)
    {
        for (int i = 0; i < 128; i++) action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 5000; i++) action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
    public static object Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle EditMode required; no Play or account changes are made.");
        string full = Path.GetFullPath(output);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Codex artifact directory required.");
        Directory.CreateDirectory(full);
        checks.Clear();
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        ConsumableItemData data = null;
        Texture2D texture = null;
        Sprite sprite = null;
        OverburstGameUI game = null;
        var acquisitionOrder = typeof(ItemData).GetField("nextAcquisitionOrder", BindingFlags.Static | BindingFlags.NonPublic);
        object previousOrder = acquisitionOrder.GetValue(null);
        string error = null;
        try
        {
            root = new GameObject("Owned UI Refresh Fixture"); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview);
            T Child<T>(string name) where T : Component
            {
                var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(root.transform, false); return go.AddComponent<T>();
            }
            game = Child<OverburstGameUI>("Presenter"); game.inventoryCapacity = Child<Text>("Capacity");
            var stats = new Text[12]; for (int i = 0; i < stats.Length; i++) stats[i] = Child<Text>("Stat " + i); Set(game, "stats", stats);
            var stat = Bind<Action<int, float?, string, string, string>>(game, "SetStat");
            stat(0, 1234f, "N0", "", ""); Check(stats[0].text == 1234f.ToString("N0"), "Stat preserves number format");
            stat(0, 12.5f, "0.##", "+", "%"); Check(stats[0].text == "+12.5%", "Changed value and percent decoration");
            stat(0, null, "0.##", "", "×"); Check(stats[0].text == "—", "Missing stat clears prior value");
            stat(0, 1.5f, "0.##", "", "×"); Check(stats[0].text == "1.5×", "Restored stat and multiplier decoration");
            long statBytes = Allocations(() => stat(0, 1.5f, "0.##", "", "×"));
            long oldStatBytes = Allocations(() => stats[0].text = 1.5f.ToString("0.##") + "×");
            Check(statBytes == 0 && oldStatBytes > statBytes, "Unchanged stat avoids managed allocation", new { iterations = 5000, beforeBytes = oldStatBytes, afterBytes = statBytes });

            data = ScriptableObject.CreateInstance<ConsumableItemData>(); data.itemName = "Owned fixture";
            var item = new ItemData(data, 1, default);
            var inv = Child<PlayerInventory>("Inventory"); var replacement = Child<PlayerInventory>("Replacement inventory");
            var items = Enumerable.Repeat<ItemData>(null, 64).ToList(); items[0] = item;
            Call(inv, "ApplyAccountItems", items, 64, 16);
            var capacity = Bind<Action<PlayerInventory>>(game, "RefreshInventoryCapacity"); capacity(inv);
            Check(game.inventoryCapacity.text == "1 / 16", "Initial inventory count");
            long capacityBytes = Allocations(() => capacity(inv));
            long oldCapacityBytes = Allocations(() => { int used = 0; foreach (var value in inv.Items) if (value != null) used++; game.inventoryCapacity.text = $"{used} / {inv.UnlockedSlotCount}"; });
            Check(capacityBytes == 0 && oldCapacityBytes > capacityBytes, "Unchanged inventory avoids enumeration and formatting", new { iterations = 5000, beforeBytes = oldCapacityBytes, afterBytes = capacityBytes });
            items[1] = item; Call(inv, "RaiseChanged"); capacity(inv); Check(game.inventoryCapacity.text == "2 / 16", "Inventory Changed recounts");
            Call(inv, "ApplyAccountItems", items, 64, 24); Call(inv, "RaiseChanged"); capacity(inv); Check(game.inventoryCapacity.text == "2 / 24", "Unlocked capacity change");
            Call(replacement, "ApplyAccountItems", Enumerable.Repeat<ItemData>(null, 64).ToList(), 64, 8); capacity(replacement);
            Check(game.inventoryCapacity.text == "0 / 8", "Account inventory replacement rebinds");
            Call(inv, "RaiseChanged"); Check(!(bool)typeof(OverburstGameUI).GetField("inventoryDirty", Private).GetValue(game), "Old inventory is unsubscribed");
            Call(game, "UnbindInventory"); Set(game, "inventoryDirty", false); Call(replacement, "RaiseChanged");
            Check(!(bool)typeof(OverburstGameUI).GetField("inventoryDirty", Private).GetValue(game), "Disable cleanup releases subscription"); capacity(replacement);

            var panel = Child<FlaskEquipmentPanelUI>("Flask panel"); var icon = Child<Image>("Icon"); var name = Child<TextMeshProUGUI>("Name"); var key = Child<TextMeshProUGUI>("Key");
            panel.Configure(new Button[3], new[] { icon }, new TMP_Text[] { name }, new TMP_Text[] { key }, null, new Button[10], new TMP_Text[10], null);
            var present = Bind<Action<int, bool, Sprite, string, int, string>>(panel, "PresentSlot");
            texture = new Texture2D(1, 1); sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * .5f);
            present(0, true, sprite, "회복 물약", 4, "4번"); Check(icon.enabled && icon.sprite == sprite && name.text == "회복 물약" && key.text == "4번 등록", "Equipped flask presentation");
            long flaskBytes = Allocations(() => present(0, true, sprite, "회복 물약", 4, "4번"));
            string legacyLabel = "4번"; long oldFlaskBytes = Allocations(() => key.text = legacyLabel + " 등록");
            Check(flaskBytes == 0 && oldFlaskBytes > flaskBytes, "Unchanged flask avoids registration string allocation", new { iterations = 5000, beforeBytes = oldFlaskBytes, afterBytes = flaskBytes });
            present(0, true, sprite, "회복 물약", 4, "F번"); Check(key.text == "F번 등록", "Same numbered slot with rebound key label");
            Object.DestroyImmediate(sprite); present(0, true, sprite, "회복 물약", 4, "F번"); Check(!icon.enabled, "Destroyed icon disables stale image");
            present(0, true, null, "새 물약", 0, null); Check(!icon.enabled && icon.sprite == null && name.text == "새 물약" && key.text == "번호 미등록", "Item replacement and unbound key");
            present(0, false, null, "물약 1", 0, null); Check(name.text == "물약 1" && key.text == "빈 장착칸", "Unequip resets empty presentation");
            name.text = "외부 변경"; Call(panel, "InvalidatePresentation"); present(0, false, null, "물약 1", 0, null);
            Check(name.text == "물약 1", "Reopen invalidation restores presentation");

            var stash = Child<PlayerStash>("Stash"); var bridge = Child<StashSlotBridge>("Bridge");
            var slots = new[] { Child<SlotUI>("Slot 0"), Child<SlotUI>("Slot 1"), Child<SlotUI>("Slot 2") };
            Set(bridge, "stash", stash); Set(bridge, "stashSlots", slots);
            var tabs = new[] { new List<ItemData> { item, null, item }, new List<ItemData> { null, item, null }, new List<ItemData> { null, null, null } };
            for (int tab = 0; tab < 3; tab++)
            {
                Call(stash, "ApplyAccountItems", tabs, 3, tab); bridge.RefreshSlots();
                Check(bridge.OccupiedSlotCount == slots.Count(s => s.DisplayItem != null), "Stash cached count matches displayed tab " + tab, new { bridge.OccupiedSlotCount });
            }
            var invalid = (ItemData)Activator.CreateInstance(typeof(ItemData), true); tabs[0][0] = invalid;
            Call(stash, "ApplyAccountItems", tabs, 3, 0); bridge.RefreshSlots();
            Check(bridge.OccupiedSlotCount == slots.Count(s => s.DisplayItem != null), "Invalid stash item retains displayed-count contract");
        }
        catch (Exception e) { error = e.ToString(); }
        finally
        {
            if (game) Call(game, "UnbindInventory");
            if (root) Object.DestroyImmediate(root);
            if (sprite) Object.DestroyImmediate(sprite);
            if (texture) Object.DestroyImmediate(texture);
            if (data) Object.DestroyImmediate(data);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            acquisitionOrder.SetValue(null, previousOrder);
        }
        var result = new { status = error == null ? "PASS_SCOPED" : "FAIL", checks, error, scope = "Native isolated presenter paths, 5000 warmed identical calls each; not whole-frame GC or FPS", play = "NOT_RUN", playerBuild = "NOT_RUN", cleanup = root == null && sprite == null && texture == null && data == null && !preview.IsValid() };
        File.WriteAllText(Path.Combine(full, "native-results.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
        return result;
    }

    static string Scenes() => JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
    {
        var s = SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, roots = s.GetRootGameObjects().Select(r => r.GetInstanceID()).OrderBy(x => x).ToArray() };
    }).ToArray());
    public static string RunWhenIdle(string output)
    {
        if (Pending || SessionState.GetBool(Key + "waiting", false)) throw new InvalidOperationException("Owned verification already pending.");
        string full = Path.GetFullPath(output);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Codex artifact directory required.");
        Directory.CreateDirectory(full); SessionState.SetString(Key + "requested", full);
        SessionState.SetString(Key + "waitDeadline", (EditorApplication.timeSinceStartup + 300).ToString(System.Globalization.CultureInfo.InvariantCulture));
        SessionState.SetBool(Key + "waiting", true);
        return "QUEUED native + isolated product Play; waits for fully returned idle Editor for up to 300 seconds.";
    }
    public static string RunPlay(string output)
    {
        if (Pending || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Healthy idle Editor required.");
        if (IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) || !string.IsNullOrEmpty(SessionState.GetString(Guard + "prepared", "")))
            throw new InvalidOperationException("Account return required.");
        foreach (var owner in new[] { "Overburst.WeakAttackPlayerLoop.plan", "Overburst.CombatPerformance.folder", "Overburst.CombatPerformance.phase", "Overburst.VisualPlay.Session.plan", "Overburst.VisualPlay.Session.deferredPlan", "Overburst.KANGoal.BuildOwner" })
            if (!string.IsNullOrEmpty(SessionState.GetString(owner, ""))) throw new InvalidOperationException("Foreign Editor owner: " + owner);
        if (SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Existing PersistentScene required; no user scene is opened or saved.");
        string full = Path.GetFullPath(output);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Codex artifact directory required.");
        Directory.CreateDirectory(full);
        SessionState.SetString(Key + "output", full); SessionState.SetString(Key + "scenes", Scenes());
        SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key + "background", Application.runInBackground);
        SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 180).ToString(System.Globalization.CultureInfo.InvariantCulture));
        SessionState.SetBool(Key + "pending", true); SessionState.SetBool(Key + "return", false);
        File.WriteAllText(Path.Combine(full, "play-results.json"), "{\"status\":\"RUNNING\"}");
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(full, "IsolatedAccount")); }
        catch { SessionState.SetBool(Key + "return", true); throw; }
        return "STARTED owned UI refresh isolated Play";
    }
    static void State(PlayModeStateChange state)
    {
        if (!Pending) return;
        if (state == PlayModeStateChange.EnteredPlayMode) { checks.Clear(); lastFrame = -1; Application.runInBackground = true; play = VerifyPlay(); }
        if (state == PlayModeStateChange.ExitingPlayMode) { (play as IDisposable)?.Dispose(); play = null; SessionState.SetBool(Key + "return", true); }
        if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key + "return", true);
    }
    static void Tick()
    {
        if (SessionState.GetBool(Key + "waiting", false))
        {
            bool expired = double.TryParse(SessionState.GetString(Key + "waitDeadline", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double untilWait) && EditorApplication.timeSinceStartup > untilWait;
            bool busy = Pending || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || IsolatedSavePlayGuard.RequiresAccountChoice
                || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) || !string.IsNullOrEmpty(SessionState.GetString(Guard + "prepared", ""));
            foreach (var owner in new[] { "Overburst.WeakAttackPlayerLoop.plan", "Overburst.CombatPerformance.folder", "Overburst.CombatPerformance.phase", "Overburst.VisualPlay.Session.plan", "Overburst.VisualPlay.Session.deferredPlan", "Overburst.KANGoal.BuildOwner" }) busy |= !string.IsNullOrEmpty(SessionState.GetString(owner, ""));
            if (!expired && busy) return;
            if (!expired)
            {
                var runtimeDll = typeof(OverburstGameUI).Assembly.Location; var editorDll = typeof(IsolatedSavePlayGuard).Assembly.Location;
                bool outdated = new[] { "Assets/ProjectOverburst/02_Shared/UI/Runtime/Workshop/OverburstGameUI.cs", "Assets/ProjectOverburst/02_Shared/UI/Runtime/Inventory/FlaskEquipmentPanelUI.cs", "Assets/ProjectOverburst/02_Shared/UI/Runtime/Stash/StashSlotBridge.cs" }
                    .Any(p => File.GetLastWriteTimeUtc(p) > File.GetLastWriteTimeUtc(runtimeDll))
                    || File.GetLastWriteTimeUtc("Assets/Editor/Testers/UI/UiRefreshOptimizationVerifier.cs") > File.GetLastWriteTimeUtc(editorDll);
                if (outdated)
                {
                    if (double.TryParse(SessionState.GetString(Key + "compileRequested", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double requestedAt) && EditorApplication.timeSinceStartup < requestedAt + 30) return;
                    SessionState.SetString(Key + "compileRequested", EditorApplication.timeSinceStartup.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    AssetDatabase.Refresh(); UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(); return;
                }
            }
            string requested = SessionState.GetString(Key + "requested", ""); SessionState.EraseBool(Key + "waiting"); SessionState.EraseString(Key + "requested"); SessionState.EraseString(Key + "waitDeadline");
            SessionState.EraseString(Key + "compileRequested");
            try
            {
                if (expired) throw new TimeoutException("Shared Editor did not return within 300 seconds; no Play or account was changed.");
                if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0) throw new InvalidOperationException("Domain reload required by this verifier; settings were preserved.");
                var native = Newtonsoft.Json.Linq.JObject.FromObject(Run(requested));
                if ((string)native["status"] != "PASS_SCOPED") throw new InvalidOperationException("Native verification failed; Play was not started.");
                RunPlay(requested);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(requested, "wait-result.json"), JsonConvert.SerializeObject(new { status = "DEFERRED_OR_FAIL", error = e.ToString() }, Formatting.Indented)); }
            return;
        }
        if (!Pending) return;
        if (SessionState.GetBool(Key + "return", false)) { ReturnAccount(); return; }
        if (double.TryParse(SessionState.GetString(Key + "deadline", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double until) && EditorApplication.timeSinceStartup > until)
        { FinishPlay(new TimeoutException("Owned UI refresh verification exceeded 180 seconds.")); return; }
        if (!EditorApplication.isPlaying) return;
        if (play == null) { FinishPlay(new InvalidOperationException("Owned verification interrupted by script reload.")); return; }
        EditorApplication.QueuePlayerLoopUpdate(); if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
        try { if (!play.MoveNext()) FinishPlay(null); } catch (Exception e) { FinishPlay(e); }
    }
    static void FinishPlay(Exception error)
    {
        (play as IDisposable)?.Dispose(); play = null;
        File.WriteAllText(Path.Combine(Output, "play-results.json"), JsonConvert.SerializeObject(new { status = error == null ? "PASS_SCOPED" : "FAIL", checks, error = error?.ToString(), scope = "Actual product UI, isolated account, repeated windows and model changes", playerBuild = "NOT_RUN", humanFeel = "NOT_RUN" }, Formatting.Indented));
        SessionState.SetBool(Key + "return", true);
        if (EditorApplication.isPlaying && string.Equals(IsolatedSavePlayGuard.ActiveDirectory, Path.Combine(Output, "IsolatedAccount"), StringComparison.OrdinalIgnoreCase)) EditorApplication.ExitPlaymode();
    }
    static void ReturnAccount()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        bool Own(string path) => string.IsNullOrEmpty(path) || path.StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!Own(IsolatedSavePlayGuard.ActiveDirectory) || !Own(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) || !Own(SessionState.GetString(Guard + "prepared", ""))) return;
        try
        {
            IsolatedSavePlayGuard.UseRealAccount(); Application.runInBackground = SessionState.GetBool(Key + "background", false);
            bool scenes = Scenes() == SessionState.GetString(Key + "scenes", "");
            bool start = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == SessionState.GetString(Key + "startScene", "");
            SessionState.SetBool(Key + "pending", false); SessionState.SetBool(Key + "return", false);
            File.WriteAllText(Path.Combine(Output, "play-return.json"), JsonConvert.SerializeObject(new { status = scenes && start && !IsolatedSavePlayGuard.RequiresAccountChoice ? "PASS_SCOPED" : "FAIL", userScenesPreserved = scenes, startScenePreserved = start, blocked = IsolatedSavePlayGuard.RequiresAccountChoice, active = IsolatedSavePlayGuard.ActiveDirectory, environment = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "", prepared = SessionState.GetString(Guard + "prepared", ""), expires = SessionState.GetString(Guard + "expires", ""), pending = Pending, returnPending = SessionState.GetBool(Key + "return", false), inputSettings = "UNCHANGED" }, Formatting.Indented));
            foreach (var suffix in new[] { "output", "scenes", "startScene", "deadline" }) SessionState.EraseString(Key + suffix);
            SessionState.EraseBool(Key + "pending"); SessionState.EraseBool(Key + "return"); SessionState.EraseBool(Key + "background");
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(Output, "play-return.json"), JsonConvert.SerializeObject(new { status = "FAIL", error = e.ToString() }, Formatting.Indented)); SessionState.SetBool(Key + "return", false); }
    }
    static IEnumerator VerifyPlay()
    {
        while (!WorldSessionState.IsHideout || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PlayerContext.Instance?.CurrentActor == null) yield return null;
        Check(Overburst.Persistence.AccountBootstrap.SaveDirectory.StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Own isolated product account");
        var game = Object.FindFirstObjectByType<OverburstGameUI>(); var context = PlayerContext.Instance; var inv = context.CurrentActorInventory;
        var bridge = game.stash.GetComponent<StashSlotBridge>(); var panel = game.flaskEquipment;
        var slots = (SlotUI[])typeof(StashSlotBridge).GetField("stashSlots", Private).GetValue(bridge);
        var keys = (TMP_Text[])typeof(FlaskEquipmentPanelUI).GetField("flaskKeys", Private).GetValue(panel);
        var names = (TMP_Text[])typeof(FlaskEquipmentPanelUI).GetField("flaskNames", Private).GetValue(panel);
        var bindings = game.GetComponent<InventoryQuickSlotBindingController>(); var flasks = PlayerFlaskController.Current;
        Check(game && inv && bridge && panel && bindings && flasks, "Authored game UI and account models ready");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            game.inventory.SetVisible(true); yield return null; game.Refresh();
            Check(game.inventory.IsVisible && game.inventoryCapacity.text == $"{inv.Items.Count(i => i != null)} / {inv.UnlockedSlotCount}", "Inventory reopen count " + cycle);
            game.inventory.SetVisible(false); game.ToggleEquipment(); yield return null; game.Refresh(); panel.Refresh();
            Check(game.equipmentWindow.gameObject.activeInHierarchy, "Equipment and flask panel reopen " + cycle);
            for (int i = 0; i < PlayerFlaskController.SlotCount; i++)
            {
                var item = flasks.GetItem(i); int key = bindings.GetFlaskKey(item);
                Check(names[i].text == (item != null ? item.itemName : "물약 " + (i + 1)) && keys[i].text == (item == null ? "빈 장착칸" : key > 0 ? QuickSlotKeyLabels.Numbered(key) + " 등록" : "번호 미등록"), "Actual flask presentation " + cycle + ":" + i);
            }
            game.CloseEquipment(); game.stash.Open(); yield return null;
            for (int tab = 0; tab < 3; tab++)
            {
                bridge.SwitchTab(tab); game.Refresh();
                Check(game.stash.IsOpen && bridge.OccupiedSlotCount == slots.Count(s => s != null && s.DisplayItem != null) && game.stashCapacity.text == $"{bridge.OccupiedSlotCount} / 63", "Stash tab count " + cycle + ":" + tab);
            }
            game.stash.Close(); yield return null;
            Check(!game.stash.IsOpen && !game.inventory.IsVisible && !game.equipmentWindow.gameObject.activeSelf && !GameplayInputBlocker.IsGameplayInputBlocked, "Repeated close releases UI input " + cycle);
        }
        int occupied = inv.Items.Count(i => i != null);
        var weaponBase = inv.Items.First(i => i != null && i.baseData is WeaponItemData).baseData;
        var movable = new ItemData(weaponBase, 1, default);
        Check(inv.AddItem(movable), "Product inventory adds fixture through account transaction"); yield return null; game.Refresh();
        Check(game.inventoryCapacity.text == $"{occupied + 1} / {inv.UnlockedSlotCount}", "Inventory event addition reflected");
        Check(inv.RemoveItem(movable), "Product inventory removes fixture through account transaction"); yield return null; game.Refresh();
        Check(game.inventoryCapacity.text == $"{occupied} / {inv.UnlockedSlotCount}", "Inventory event removal reflected");
        var flaskItem = flasks.GetItem(0); Check(flaskItem != null, "Starter flask equipped");
        Check(bindings.Bind(7, flaskItem), "Product flask key reassignment"); panel.Refresh(); Check(keys[0].text == QuickSlotKeyLabels.Numbered(7) + " 등록", "Flask key change reflected");
        Check(flasks.TryUnequip(0, out var unequipReason), "Product flask unequip", unequipReason); panel.Refresh(); Check(keys[0].text == "빈 장착칸", "Flask unequip reflected");
        Check(flasks.TryEquip(0, flaskItem, out var equipReason), "Product flask equip", equipReason); panel.Refresh(); Check(names[0].text == flaskItem.itemName, "Flask re-equip reflected");
        var health = context.CurrentActorHealth; float oldMax = health.MaxHp; health.SetMaxHp(oldMax + 37, true); game.Refresh();
        var hp = (Text)typeof(OverburstGameUI).GetField("hpText", Private).GetValue(game);
        Check(hp.text == $"{health.CurrentHp:N0} / {health.MaxHp:N0}", "HUD health change reflected"); health.SetMaxHp(oldMax, true); game.Refresh();
        var progression = PlayerProgression.Current; int experience = progression.Experience; progression.AddExperience(1);
        Check(progression.FlushPendingExperience(), "Product experience commit"); game.Refresh();
        var xp = (Text)typeof(OverburstGameUI).GetField("xpText", Private).GetValue(game);
        Check(progression.Experience != experience && xp.text == $"{progression.Experience:N0} / {progression.ExperienceToNext:N0}", "HUD experience change reflected");
        game.enabled = false; yield return null; Check(typeof(OverburstGameUI).GetField("observedInventory", Private).GetValue(game) == null, "Presenter disable unsubscribes");
        game.enabled = true; yield return null; game.Refresh(); Check(game.inventoryCapacity.text == $"{inv.Items.Count(i => i != null)} / {inv.UnlockedSlotCount}", "Presenter enable rebinds");
    }
}
