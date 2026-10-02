using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>정식 카탈로그의 판매·명찰·월드 획득·장착·저장 재진입을 격리 계정에서 확인한다.</summary>
[InitializeOnLoad]
public static class WeaponCatalogConnectionsVerifier
{
    const string Key = "Overburst.WeaponCatalogConnectionsVerifier.";
    static readonly List<object> checks = new List<object>();
    static readonly List<string> errors = new List<string>();
    static IEnumerator work;
    static int lastFrame, failures;
    static double deadline;
    static bool queuedNextPlay;
    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static string Output => SessionState.GetString(Key + "output", "");
    static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");

    static WeaponCatalogConnectionsVerifier() { EditorApplication.playModeStateChanged += StateChanged; }

    public static string Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || Phase != 0)
            throw new InvalidOperationException("Return the shared Editor before running this verifier.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "sceneSetup", HideoutCatalogLayoutBuilder.EditorSnapshot());
        SessionState.SetString(Key + "startScene", EditorSceneManager.playModeStartScene != null ? AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) : "");
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        Phase = 1;
        SessionState.SetString(Key + "status", "RUNNING");
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount")); }
        catch { RestoreEditor(); Phase = 0; throw; }
        return "RUNNING: all catalog weapons, nameplate capacity and fresh Play reentry.";
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (Phase == 0) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); errors.Clear(); failures = 0; lastFrame = -1;
            SessionState.SetBool(Key + "background", Application.runInBackground);
            Application.runInBackground = true;
            deadline = EditorApplication.timeSinceStartup + 180;
            work = Verify();
            Application.logMessageReceived += Log;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + "background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, null);
            bool preserved = HideoutCatalogLayoutBuilder.EditorSnapshot() == SessionState.GetString(Key + "sceneSetup", "");
            File.WriteAllText(Path.Combine(Output, "editor_restored_" + Phase + ".json"), JsonConvert.SerializeObject(new {
                status = preserved ? "PASS" : "FAIL", sceneSetupPreserved = preserved,
                isolatedEnvironmentCleared = string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            }, Formatting.Indented));
            if (Phase == 1 && preserved && SessionState.GetBool(Key + "phasePassed", false))
            {
                Phase = 2;
                queuedNextPlay = true;
                deadline = EditorApplication.timeSinceStartup + 60;
                EditorApplication.update += StartNextPlay;
            }
            else
            {
                if (!preserved) SessionState.SetString(Key + "status", "FAIL");
                RestoreEditor(); Phase = 0;
            }
        }
    }

    static void StartNextPlay()
    {
        if (!queuedNextPlay) { EditorApplication.update -= StartNextPlay; return; }
        if (EditorApplication.timeSinceStartup > deadline)
        {
            queuedNextPlay = false; EditorApplication.update -= StartNextPlay;
            SessionState.SetString(Key + "status", "FAIL: second Play could not start while Editor was busy.");
            RestoreEditor(); Phase = 0; return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        queuedNextPlay = false; EditorApplication.update -= StartNextPlay;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output, "IsolatedAccount")); }
        catch (Exception error)
        {
            SessionState.SetString(Key + "status", "FAIL: " + error.Message);
            RestoreEditor(); Phase = 0;
        }
    }

    static void RestoreEditor()
    {
        queuedNextPlay = false;
        EditorApplication.update -= StartNextPlay;
        string previous = SessionState.GetString(Key + "startScene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, null);
    }

    static void Log(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Weapon catalog verification timed out.");
            if (!work.MoveNext()) Finish();
        }
        catch (Exception error) { Check(false, error.ToString()); Finish(); }
    }

    static void Finish()
    {
        (work as IDisposable)?.Dispose(); work = null;
        bool passed = failures == 0 && errors.Count == 0;
        SessionState.SetBool(Key + "phasePassed", passed);
        SessionState.SetString(Key + "status", passed ? (Phase == 1 ? "RUNNING: fresh Play reentry" : "PASS") : "FAIL");
        File.WriteAllText(Path.Combine(Output, "Result_" + Phase + ".json"), JsonConvert.SerializeObject(new {
            status = passed ? "PASS" : "FAIL", phase = Phase, failures, checks, errors, account = AccountBootstrap.SaveDirectory
        }, Formatting.Indented));
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }

    static void Check(bool passed, string name)
    { checks.Add(new { name, passed }); if (!passed) failures++; }

    static void Require(bool passed, string name)
    { Check(passed, name); if (!passed) throw new InvalidOperationException(name); }

    static IEnumerator Verify()
    {
        while (AccountGameplaySession.Current == null || !WorldSessionState.IsHideout
            || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
            || PlayerContext.Instance == null || PlayerContext.Instance.CurrentActor == null
            || WorldItemNameplatePresenter.Active == null || GameplayInputBlocker.IsGameplayInputBlocked) yield return null;
        for (int i = 0; i < 8; i++) yield return null;
        Require(!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory), "isolated account guard active");
        var actor = PlayerContext.Instance.CurrentActor;
        var session = AccountGameplaySession.Current;
        var catalog = WeaponLevelCatalog.Current.entries.Where(e => e.weapon != null).ToArray();
        Require(catalog.Length == 34, "all 34 completed catalog weapons loaded");
        foreach (var entry in catalog)
        {
            var weapon = entry.weapon;
            var pickup = weapon.worldPickupPrefab != null ? weapon.worldPickupPrefab.GetComponent<WorldItemPickup>() : null;
            Require(WeaponContentPolicy.IsActiveWeapon(weapon) && weapon.icon != null && weapon.weaponRootPrefab != null
                && pickup != null && pickup.enabled && weapon.worldPickupPrefab.activeSelf
                && new SerializedObject(pickup).FindProperty("itemDataAsset").objectReferenceValue == weapon,
                "complete asset bindings: " + weapon.name);
            Require(!string.IsNullOrEmpty(Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath).IdFor(weapon)), "stable save ID: " + weapon.name);
        }
        VerifyMerchant(catalog.Select(e => e.weapon).ToArray());
        if (Phase == 2)
        {
            var expected = JsonConvert.DeserializeObject<List<ItemSnapshot>>(File.ReadAllText(Path.Combine(Output, "acquired-weapons.json")));
            var restored = session.Read();
            foreach (var saved in expected)
            {
                var found = restored.items.SingleOrDefault(item => item.instanceId == saved.instanceId);
                Require(found != null && JsonConvert.SerializeObject(found) == JsonConvert.SerializeObject(saved), "fresh Play restores complete instance: " + saved.contentId);
            }
            Require(actor.Equipment.HasCurrentWeapon && expected.Any(item => item.instanceId == actor.Equipment.CurrentWeaponItem.runtimeInstanceId), "last catalog weapon visual restored on fresh Play");
            yield break;
        }

        var all = new List<WorldItemPickup>(); WorldItemPickup.CopyActivePickups(all);
        Require(all.Count >= 204, "product hideout catalog retains all 204 definitions");
        var camera = Camera.main;
        Require(camera != null && Mouse.current != null && Keyboard.current != null, "product camera and input devices available");
        var brain = camera.GetComponent<Unity.Cinemachine.CinemachineBrain>();
        bool brainEnabled = brain != null && brain.enabled;
        var cameraDriver = QuarterViewCamera.ActiveInstance;
        bool cameraDriverEnabled = cameraDriver != null && cameraDriver.enabled;
        bool orthographic = camera.orthographic;
        float size = camera.orthographicSize;
        Vector3 position = camera.transform.position;
        Quaternion rotation = camera.transform.rotation;
        Vector2 pointer = Mouse.current.position.ReadValue();
        try
        {
            if (cameraDriver != null) cameraDriver.enabled = false;
            if (brain != null) brain.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 10;
            camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            var capacity = VerifyCapacity(all, camera);
            try { while (capacity.MoveNext()) yield return capacity.Current; }
            finally { (capacity as IDisposable)?.Dispose(); }
            var cases = VerifyWeapons(catalog, all, actor, camera);
            try { while (cases.MoveNext()) yield return cases.Current; }
            finally { (cases as IDisposable)?.Dispose(); }
        }
        finally
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            InputSystem.QueueDeltaStateEvent(Mouse.current.position, pointer);
            camera.orthographic = orthographic; camera.orthographicSize = size;
            camera.transform.SetPositionAndRotation(position, rotation);
            if (brain != null) brain.enabled = brainEnabled;
            if (cameraDriver != null) cameraDriver.enabled = cameraDriverEnabled;
        }
        Require(session.FlushPendingSave(), "all acquired catalog weapons checkpoint to isolated account");
        var savedItems = session.Read().items.Where(item => acquiredIds.Contains(item.instanceId)).ToList();
        Require(savedItems.Count == 34, "all 34 acquired weapon instances owned after equip and stash");
        File.WriteAllText(Path.Combine(Output, "acquired-weapons.json"), JsonConvert.SerializeObject(savedItems, Formatting.Indented));
    }

    static void VerifyMerchant(WeaponItemData[] weapons)
    {
        var service = Object.FindFirstObjectByType<MerchantStockRefreshService>();
        Require(service != null, "product merchant stock service present");
        var context = (MerchantStockGenerationContext)typeof(MerchantStockRefreshService)
            .GetMethod("BuildGenerationContext", PrivateInstance).Invoke(service, null);
        Require(context.WeaponCandidates.Count == weapons.Length && context.WeaponCandidates.ToHashSet().SetEquals(weapons), "merchant resolves all 34 unique catalog weapons");
        var definition = AssetDatabase.LoadAssetAtPath<MerchantDefinition>("Assets/ProjectOverburst/03_Features/Items/Data/Merchants/WeaponMerchant.asset");
        var seen = new HashSet<WeaponItemData>();
        var random = UnityEngine.Random.state;
        try
        {
            for (int seed = 0; seed < 64; seed++)
            {
                UnityEngine.Random.InitState(seed);
                var inventory = service.CreateInventory(definition);
                int stockCount = inventory.Items.Count(item => item != null);
                Require(stockCount >= context.WeaponMinStockCount && stockCount <= context.WeaponMaxStockCount + 1,
                    "existing base stock count and optional artifact contract: " + seed);
                foreach (var item in inventory.Items)
                {
                    if (item == null) continue;
                    var weapon = item.baseData as WeaponItemData;
                    Require(weapon != null && weapons.Contains(weapon) && item.level == 1 && ItemGradeAvailabilityPolicy.IsEnabled(item.grade), "existing stock grade and level contract: " + seed);
                    seen.Add(weapon);
                    var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
                    ItemSnapshotCodec.Validate(ItemSnapshotCodec.Capture(item, registry), registry);
                }
            }
        }
        finally { UnityEngine.Random.state = random; }
        Require(seen.SetEquals(weapons), "actual stock generation can select every catalog weapon");
    }

    static IEnumerator VerifyCapacity(List<WorldItemPickup> all, Camera camera)
    {
        var presenter = WorldItemNameplatePresenter.Active;
        var core = PlayerPickupInteractor.ActiveCore;
        var display = (WorldItemNameplateDisplaySet)typeof(WorldItemNameplatePresenter).GetField("displaySet", PrivateInstance).GetValue(presenter);
        var weapons = all.Where(p => p.RuntimeItem.baseData is WeaponItemData).ToArray();
        var other = all.Where(p => p.RuntimeItem.baseData is not WeaponItemData).Take(34).ToArray();
        Require(weapons.Length == 34 && other.Length == 34, "production pickup catalog has 34 weapons and comparison models");
        foreach (var pickup in all) pickup.transform.position += new Vector3(2000, 0, 2000);
        Vector3 groupA = new Vector3(100, 0, 100), groupB = new Vector3(160, 0, 100);
        for (int i = 0; i < weapons.Length; i++) { weapons[i].transform.position = groupA + Grid(i, 6, 2.8f); weapons[i].RuntimeItem.grade = ItemGrade.Common; }
        for (int i = 0; i < other.Length; i++) { other[i].transform.position = groupB + Grid(i, 6, 2.8f); other[i].RuntimeItem.grade = ItemGrade.Mythic; }
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(UnityEngine.InputSystem.Key.CapsLock));
        yield return null; InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        for (int i = 0; i < 4; i++) yield return null;
        Require(core.CurrentMode == WorldLootInteractionMode.LootFocus, "CapsLock enters full loot display through input action");
        camera.transform.position = groupA + new Vector3(7, 30, 7);
        for (int i = 0; i < 4; i++) yield return null;
        WriteNameplateDiagnostics("onscreen-weapons", camera, all, weapons);
        Require(display.Count == 34 && weapons.All(p => display.Contains(p.GetInstanceID())), "offscreen higher grades cannot consume the 34 onscreen weapon rows");
        int revision = core.CurrentSnapshot.ActiveSetRevision;
        var weaponCapture = CaptureFrame("nameplates_weapons.png");
        while (weaponCapture.MoveNext()) yield return weaponCapture.Current;
        camera.transform.position = groupB + new Vector3(7, 30, 7);
        for (int i = 0; i < 4; i++) yield return null;
        WriteNameplateDiagnostics("camera-moved", camera, all, other);
        Require(core.CurrentSnapshot.ActiveSetRevision == revision && display.Count == 34 && other.All(p => display.Contains(p.GetInstanceID())), "camera movement updates rows without active pickup membership change");
        camera.transform.position = groupA + new Vector3(7, 30, 7);
        for (int i = 0; i < 4; i++) yield return null;
        Require(display.Count == 34 && weapons.All(p => display.Contains(p.GetInstanceID())), "camera return restores every onscreen weapon candidate");

        var crowded = all.Take(120).ToArray();
        foreach (var pickup in all) pickup.transform.position += new Vector3(2000, 0, 2000);
        for (int i = 0; i < crowded.Length; i++) { crowded[i].transform.position = groupA + Grid(i, 16, 1.5f); crowded[i].RuntimeItem.grade = ItemGrade.Common; }
        camera.orthographicSize = 10;
        camera.transform.position = groupA + new Vector3(11.25f, 30, 5.25f);
        for (int i = 0; i < 4; i++) yield return null;
        WriteNameplateDiagnostics("capacity", camera, all, crowded);
        Require(display.Count == 96, "more than 96 onscreen models retain the authored row limit");
        WorldItemPickup hover = null;
        Vector2 hoverPoint = default;
        foreach (var pickup in crowded.Where(p => !display.Contains(p.GetInstanceID())))
        {
            Vector2 point = ModelPoint(camera, pickup);
            if (presenter.ResolveModelPointerTarget(point) != pickup) continue;
            hover = pickup; hoverPoint = point; break;
        }
        Require(hover != null, "a capacity-excluded model remains targetable");
        InputSystem.QueueDeltaStateEvent(Mouse.current.position, hoverPoint);
        for (int i = 0; i < 5; i++) yield return null;
        Require(presenter.HighlightedPickup == hover && WorldItemNameplateVisibilityRegistry.IsVisible(hover), "full display mode guarantees hovered model label beyond capacity");
        var capacityCapture = CaptureFrame("nameplates_capacity_hover.png");
        while (capacityCapture.MoveNext()) yield return capacityCapture.Current;
        foreach (var pickup in all) pickup.transform.position += new Vector3(2000, 0, 2000);
        InputSystem.QueueDeltaStateEvent(Mouse.current.position, Vector2.zero);
        for (int i = 0; i < 3; i++) yield return null;
    }

    static readonly HashSet<string> acquiredIds = new HashSet<string>();

    static IEnumerator CaptureFrame(string filename)
    {
        string path = Path.Combine(Output, filename);
        ScreenCapture.CaptureScreenshot(path);
        for (int frame = 0; frame < 3; frame++) yield return null;
        float end = Time.realtimeSinceStartup + 5;
        while ((!File.Exists(path) || new FileInfo(path).Length == 0) && Time.realtimeSinceStartup < end)
            yield return null;
        Require(File.Exists(path) && new FileInfo(path).Length > 0, "rendered screenshot written: " + filename);
    }

    static void WriteNameplateDiagnostics(string scenario, Camera camera, List<WorldItemPickup> all,
        IEnumerable<WorldItemPickup> expected)
    {
        var presenter = WorldItemNameplatePresenter.Active;
        var display = (WorldItemNameplateDisplaySet)typeof(WorldItemNameplatePresenter).GetField("displaySet", PrivateInstance).GetValue(presenter);
        var visible = (HashSet<int>)typeof(WorldItemNameplatePresenter).GetField("screenVisiblePickupIds", PrivateInstance).GetValue(presenter);
        var resolver = (WorldItemPickupHoverResolver)typeof(WorldItemNameplatePresenter).GetField("pickupHoverResolver", PrivateInstance).GetValue(presenter);
        File.WriteAllText(Path.Combine(Output, scenario + ".json"), JsonConvert.SerializeObject(new {
            cameraPosition = camera.transform.position.ToString("F3"), cameraRotation = camera.transform.eulerAngles.ToString("F3"),
            camera.orthographic, camera.orthographicSize, camera.aspect, pixelRect = camera.pixelRect.ToString(),
            inputBlocked = PlayerPickupInteractor.ActiveCore.CurrentSnapshot.IsInputBlocked,
            displayCount = display.Count, visibleCount = visible.Count, cachedModels = resolver.EntryCount,
            displayedNames = all.Where(p => display.Contains(p.GetInstanceID())).Select(p => p.DisplayName).ToArray(),
            expected = expected.Select(p => new { p.name, p.CanPickup, displayed = display.Contains(p.GetInstanceID()),
                modelVisible = visible.Contains(p.GetInstanceID()), position = p.transform.position.ToString("F3"),
                viewport = camera.WorldToViewportPoint(p.transform.position).ToString("F3") }).ToArray()
        }, Formatting.Indented));
    }

    static IEnumerator VerifyWeapons(WeaponLevelCatalog.Entry[] entries, List<WorldItemPickup> all, PlayerActorRuntime actor, Camera camera)
    {
        acquiredIds.Clear();
        var inventory = PlayerAccountInventoryService.SharedInventory;
        var stash = PlayerAccountInventoryService.SharedStash;
        var core = PlayerPickupInteractor.ActiveCore;
        var presenter = WorldItemNameplatePresenter.Active;
        var view = presenter.GetComponent<WorldItemNameplateView>();
        camera.orthographicSize = 5;
        camera.transform.position = actor.transform.position + Vector3.up * 25;
        int index = 0;
        foreach (var entry in entries)
        {
            var previous = actor.Equipment.GetWeaponSlotItem(0);
            if (previous != null)
            {
                int slot = stash.FindFirstEmptySlot();
                Require(slot >= 0 && AccountGameplaySession.Run(() => stash.SetItemAt(slot, previous) && actor.Equipment.ClearWeaponSlot(0)), "preserve prior equipped instance in stash");
            }
            ItemGrade grade = ItemGradeAvailabilityPolicy.GetEnabledGrades()[index % ItemGradeAvailabilityPolicy.GetEnabledGrades().Length];
            var item = new ItemData(entry.weapon, entry.minimumLevel, grade, 1);
            var pickup = WorldItemDropFactory.CreateWorldPickup(item, actor.transform.position + new Vector3(1.3f, 0.3f, 0), inventory, actor.transform);
            Require(pickup != null && ReferenceEquals(pickup.RuntimeItem, item), "factory retains original instance: " + entry.weapon.name);
            float end = Time.unscaledTime + 3;
            while (!pickup.CanPickup && Time.unscaledTime < end) yield return null;
            Require(pickup.CanPickup, "scripted drop lands and enables pickup: " + entry.weapon.name);
            for (int i = 0; i < 3; i++) yield return null;
            Require(WorldItemNameplateVisibilityRegistry.IsVisible(pickup), "landed weapon nameplate visible: " + entry.weapon.name);
            var row = view.AuthoredRows.FirstOrDefault(r => r != null && r.gameObject.activeInHierarchy
                && (WorldItemPickup)typeof(WorldItemNameplateRowView).GetField("pickup", PrivateInstance).GetValue(r) == pickup);
            Require(row != null && row.Label.text == item.itemName, "nameplate shows actual weapon name: " + entry.weapon.name);
            if (index == 0 || index == entries.Length - 1)
            {
                var capture = CaptureFrame(index == 0 ? "first_weapon_drop.png" : "original_weapon_drop.png");
                while (capture.MoveNext()) yield return capture.Current;
            }
            if (index % 3 == 0)
            {
                var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                row.OnPointerDown(pointer); row.OnPointerUp(pointer);
            }
            else if (index % 3 == 1)
            {
                var target = presenter.ResolveModelPointerTarget(ModelPoint(camera, pickup));
                Require(target == pickup, "real model bounds resolves clicked weapon: " + entry.weapon.name);
                Require(core.RequestPickupByModelPointerDown(target) == WorldLootPickupRequestResult.Succeeded, "model pickup route succeeds: " + entry.weapon.name);
                core.NotifyPrimaryPointerReleased();
            }
            else core.TryInteract(actor);
            yield return null;
            Require(pickup == null && inventory.ContainsItem(item), "world pickup transfers same instance to inventory: " + entry.weapon.name);
            Require(actor.Equipment.EquipWeaponItem(item), "equip acquired weapon: " + entry.weapon.name);
            yield return null;
            Require(actor.Equipment.CurrentWeaponItem == item && actor.Equipment.HasCurrentWeapon
                && actor.Equipment.CurrentWeaponRoot.GetComponentsInChildren<Renderer>(true).Any(r => r is MeshRenderer || r is SkinnedMeshRenderer)
                && actor.Equipment.HasCurrentMeleeDefinition, "equipped visual and combat definition: " + entry.weapon.name);
            acquiredIds.Add(item.runtimeInstanceId);
            index++;
        }
    }

    static Vector3 Grid(int index, int columns, float spacing) => new Vector3(index % columns * spacing, 0, index / columns * spacing);

    static Vector2 ModelPoint(Camera camera, WorldItemPickup pickup)
    {
        var presentation = pickup.GetComponent<WorldPickupPresentation>();
        var root = presentation != null ? presentation.VisualRoot : pickup.transform;
        var renderers = root.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return camera.WorldToScreenPoint(bounds.center);
    }
}
