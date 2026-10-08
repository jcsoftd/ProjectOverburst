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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Two isolated product boots, real F-key sessions, typed stock, stash persistence and run return.</summary>
[InitializeOnLoad]
public static class MainTownPlayVerifier
{
    const string Key = "Overburst.MainTownVerifier.";
    static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    static string Output => SessionState.GetString(Key + "output", "");
    static int Cycle { get => SessionState.GetInt(Key + "cycle", 1); set => SessionState.SetInt(Key + "cycle", value); }
    static IEnumerator work;
    static int lastFrame = -1;
    static InputSettings originalInput, ownedInput;
    static Keyboard keyboard;
    static string pendingShot;
    static double shotDeadline;
    static readonly List<string> checks = new List<string>();
    static readonly List<string> errors = new List<string>();
    static readonly List<object> merchantReports = new List<object>();
    static readonly List<object> inputReports = new List<object>();
    static int inputTraceFrames;
    static double Deadline => double.Parse(SessionState.GetString(Key + "deadline", "0"), CultureInfo.InvariantCulture);

    static MainTownPlayVerifier()
    {
        if (Phase != 0)
        {
            if (Phase == 2) SessionState.SetString(Key + "failure", "Domain reload interrupted the runtime probe.");
            EditorApplication.update += Tick;
        }
    }

    public static string Start(string output)
    {
        MainTownBuilder.RequireIdle();
        if (Phase != 0 || EditorUtility.scriptCompilationFailed) throw new InvalidOperationException("Verifier busy or compilation failed.");
        if (IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Return the previous account session first.");
        output = Path.GetFullPath(output);
        var allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("A fresh Codex artifact directory is required.");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key + "background", Application.runInBackground);
        SessionState.SetInt(Key + "pid", System.Diagnostics.Process.GetCurrentProcess().Id);
        SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 720).ToString("R", CultureInfo.InvariantCulture));
        SessionState.SetString(Key + "failure", ""); Cycle = 1; Phase = 1;
        Application.runInBackground = true;
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainTownBuilder.ScenePath);
        var gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gameView != null) { var window = EditorWindow.GetWindow(gameView); window.Show(); window.Focus(); }
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output, "Account"));
        return "STARTED_DIRECT_MAIN_SCENE_BOOT";
    }

    public static string Cancel()
    {
        if (Phase == 0) return "IDLE";
        Fail(new OperationCanceledException("Main town verification cancelled.")); return "RETURN_REQUESTED";
    }

    static void Tick()
    {
        try
        {
            if (Phase == 0) { EditorApplication.update -= Tick; return; }
            if (EditorApplication.timeSinceStartup > Deadline) throw new TimeoutException("Main town verification deadline.");
            if (Phase == 3)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (SessionState.GetString(Key + "failure", "") != "") { ReturnEditor(); return; }
                Cycle = 2; Phase = 1;
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
                IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output, "Account")); return;
            }
            if (Phase == 4)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating) ReturnEditor();
                return;
            }
            EditorApplication.QueuePlayerLoopUpdate();
            if (!EditorApplication.isPlaying) return;
            if (Phase == 1)
            {
                if (!AccountBootstrap.Ready || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                    || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.MainSceneName) return;
                checks.Clear(); errors.Clear(); merchantReports.Clear(); inputReports.Clear(); inputTraceFrames = 0;
                originalInput = InputSystem.settings; ownedInput = Object.Instantiate(originalInput); ownedInput.hideFlags = HideFlags.DontSave;
                ownedInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                ownedInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings = ownedInput; keyboard = Keyboard.current;
                Application.logMessageReceived += Log;
                work = Probe(); Phase = 2; lastFrame = -1;
            }
            if (Phase == 2)
            {
                if (work == null) throw new InvalidOperationException(SessionState.GetString(Key + "failure", "Probe lost on reload."));
                if (!string.IsNullOrEmpty(pendingShot))
                {
                    if (!File.Exists(pendingShot) || new FileInfo(pendingShot).Length == 0)
                    {
                        if (EditorApplication.timeSinceStartup > shotDeadline) throw new TimeoutException("GameView capture did not finish.");
                        var gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                        if (gameView != null) EditorWindow.GetWindow(gameView).Repaint();
                        return;
                    }
                    pendingShot = null;
                }
                if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
                if (inputTraceFrames > 0) { RecordInput("PlayerFrame"); inputTraceFrames--; }
                if (work.MoveNext()) return;
                WriteCycle("PASS", null); CleanupRuntime();
                Phase = Cycle == 1 ? 3 : 4; EditorApplication.isPlaying = false;
            }
        }
        catch (Exception error) { Fail(error); }
    }

    static IEnumerator Probe()
    {
        var actor = PlayerContext.Instance.CurrentActor;
        var scene = SceneManager.GetSceneByName(PersistentSceneFlow.MainSceneName);
        Check(scene.isLoaded && WorldSessionState.Phase == WorldPhase.Hideout, "MainScene is the playable hub");
        Check(Mathf.Abs(Mathf.DeltaAngle(QuarterViewCamera.ActiveInstance.CurrentYaw, 346f)) < .1f, "Town camera yaw applied");
        Check(Objects<MainTownPlacementAnchor>(scene).All(x => x.CompareTag("EditorOnly") && x.GetComponentsInChildren<Renderer>(true).Length == 0
            && x.Placement != null && !x.Placement.CompareTag("EditorOnly")), "Markers have no game renderers; linked services survive stripping");
        var spawn = Objects<HubReturnPoint>(scene).Single(x => x.ReturnPointId == "Default");
        Check(Vector3.Distance(actor.transform.position, spawn.transform.position) < 1f, "Player placed at the courtyard spawn");
        for (int i = 0; i < 30; i++) yield return null;
        Check(WorldMinimapController.Instance != null && WorldMinimapController.Instance.IsVisible, "Minimap uses the current main hub");
        Shot("Spawn"); for (int i = 0; i < 3; i++) yield return null;
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var settings = Objects<MainTownSceneSettings>(scene).Single();
        Check(settings.Merchants.Count == 4, "Four separate town merchants");
        foreach (var definition in settings.Merchants)
        {
            var inventory = MerchantStockRefreshService.GetOrCreateInventory(definition);
            var stock = inventory.Items.Where(x => x != null).ToArray();
            Check(stock.Length > 0, definition.Category + " has stock");
            Check(stock.All(x => MatchesCategory(definition.Category, x.baseData)), definition.Category + " stock is separated");
            foreach (var item in stock) registry.IdFor(item.baseData);
            merchantReports.Add(new { definition.MerchantName, category = definition.Category.ToString(), id = registry.IdFor(definition), stock = stock.Select(x => new { x.baseData.name, type = x.baseData.GetType().Name, x.stackCount, instance = x.runtimeInstanceId }).ToArray() });
            var npc = Objects<GeneralGoodsMerchantInteractable>(scene).Single(x => Definition(x) == definition);
            ActorTeleportUtility.TeleportSafely(actor.transform, npc.transform.position + npc.transform.forward * 2.2f + Vector3.up * .12f, Quaternion.LookRotation(-npc.transform.forward));
            for (int i = 0; i < 20; i++) yield return null;
            Shot("Npc_" + definition.Category); for (int i = 0; i < 3; i++) yield return null;
            CaptureNpcPortrait(npc.transform, definition.Category.ToString());
            PressF(); for (int i = 0; i < 3; i++) yield return null; ReleaseKeys();
            for (int i = 0; i < 7; i++) yield return null;
            var shop = Object.FindObjectsByType<ShopUI>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(x => x.HasUsableCanvasRoot);
            Check(shop.IsOpenFor(definition), "F opens " + definition.Category + " shop"); Shot("Shop_" + definition.Category); for (int i = 0; i < 3; i++) yield return null;
            PressF(); for (int i = 0; i < 3; i++) yield return null; ReleaseKeys();
            for (int i = 0; i < 7; i++) yield return null;
            Check(!shop.IsOpen, "F closes " + definition.Category + " shop");
        }
        var stylist = Objects<AppearanceStylistInteractable>(scene).Single();
        ActorTeleportUtility.TeleportSafely(actor.transform, stylist.transform.position + stylist.transform.forward * 2f + Vector3.up * .12f, Quaternion.LookRotation(-stylist.transform.forward));
        for (int i = 0; i < 25; i++) yield return null;
        Shot("Stylist"); for (int i = 0; i < 3; i++) yield return null;
        CaptureNpcPortrait(stylist.transform, "Stylist");
        for (int repeat = 0; repeat < 2; repeat++)
        {
            PressF(); for (int i = 0; i < 3; i++) yield return null; ReleaseKeys();
            for (int i = 0; i < 20; i++) yield return null;
            Check(Overburst.Appearance.AppearanceCustomizationPanel.IsOpen && Overburst.Appearance.AppearanceCustomizationPanel.Instance.Owner == stylist, "F opens the town stylist, attempt " + repeat);
            Shot("StylistUI_" + repeat); for (int i = 0; i < 3; i++) yield return null;
            PressF(); for (int i = 0; i < 3; i++) yield return null; ReleaseKeys();
            for (int i = 0; i < 10; i++) yield return null;
            Check(!Overburst.Appearance.AppearanceCustomizationPanel.IsOpen, "F closes the town stylist, attempt " + repeat);
            Check(PlayerInputFacade.Current.IsGameplayEnabled && !GameplayInputBlocker.IsGameplayInputBlocked, "Stylist close returns gameplay input, attempt " + repeat);
        }
        PressF(); for (int i = 0; i < 3; i++) yield return null; ReleaseKeys();
        for (int i = 0; i < 10; i++) yield return null;
        var appearance = Overburst.Appearance.AppearanceCustomizationPanel.Instance;
        Check(Overburst.Appearance.AppearanceCustomizationPanel.IsOpen, "Stylist reopens before the Esc check");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.Escape));
        for (int i = 0; i < 3; i++) yield return null; ReleaseKeys();
        for (int i = 0; i < 10; i++) yield return null;
        Check(!Overburst.Appearance.AppearanceCustomizationPanel.IsOpen && PlayerInputFacade.Current.IsGameplayEnabled
            && !GameplayInputBlocker.IsGameplayInputBlocked, "Esc closes the stylist and returns gameplay input");
        PressF(); for (int i = 0; i < 3; i++) yield return null; ReleaseKeys();
        for (int i = 0; i < 10; i++) yield return null;
        Check(Overburst.Appearance.AppearanceCustomizationPanel.IsOpen, "Stylist reopens before the discard check");
        appearance.choices.First(x => x.kind == Overburst.Appearance.AppearanceChoiceKind.Face
            && x.optionId != appearance.Session.Draft.faceId).button.onClick.Invoke();
        PressF(); for (int i = 0; i < 3; i++) yield return null; ReleaseKeys();
        for (int i = 0; i < 10; i++) yield return null;
        Check(Overburst.Appearance.AppearanceCustomizationPanel.IsOpen && appearance.discardConfirmation.activeSelf
            && !PlayerInputFacade.Current.IsGameplayEnabled, "F preserves unsaved appearance behind discard confirmation");
        appearance.discardAndClose.onClick.Invoke();
        for (int i = 0; i < 10; i++) yield return null;
        Check(!Overburst.Appearance.AppearanceCustomizationPanel.IsOpen && PlayerInputFacade.Current.IsGameplayEnabled
            && !GameplayInputBlocker.IsGameplayInputBlocked, "Discard closes the stylist and returns gameplay input");
        var stashInteraction = Objects<StashInteractable>(scene).Single();
        ActorTeleportUtility.TeleportSafely(actor.transform, stashInteraction.transform.position + Vector3.right * 1.8f + Vector3.up * .12f, Quaternion.identity);
        for (int i = 0; i < 15; i++) yield return null;
        PressF(); for (int i = 0; i < 3; i++) yield return null; ReleaseKeys(); for (int i = 0; i < 7; i++) yield return null;
        Check(stashInteraction.IsOpen, "F opens account stash"); Shot("Stash"); for (int i = 0; i < 3; i++) yield return null;
        var playerInventory = PlayerAccountInventoryService.SharedInventory;
        var stash = PlayerAccountInventoryService.SharedStash; stash.SetCurrentTab(0);
        if (Cycle == 1)
        {
            var bag = AssetDatabase.LoadAssetAtPath<BagItemData>("Assets/ProjectOverburst/03_Features/Items/Data/Items/Bags/Bag_Grade1.asset");
            var item = new ItemData(bag, 1, ItemGrade.Common); Check(playerInventory.AddItem(item), "Fixture enters account inventory");
            int from = playerInventory.FindFirstMatchingItemIndex(item);
            int to = Enumerable.Range(0, stash.Capacity).First(x => stash.GetItemAt(x) == null);
            Check(stash.TryMoveFromInventory(playerInventory, from, to, item, null), "Inventory deposits into the town stash");
            Check(stash.TryMoveToInventory(playerInventory, to, from, item, null), "Town stash withdraws into inventory");
            Check(stash.TryMoveFromInventory(playerInventory, from, to, item, null), "Stored fixture prepared for a second boot");
            SessionState.SetString(Key + "fixtureId", item.runtimeInstanceId); SessionState.SetInt(Key + "stashIndex", to);
        }
        else
        {
            int index = SessionState.GetInt(Key + "stashIndex", -1); var restored = stash.GetItemAt(index);
            Check(restored != null && restored.runtimeInstanceId == SessionState.GetString(Key + "fixtureId", ""), "Stash item survives a fresh PersistentScene boot");
            Check(stash.TryMoveToInventory(playerInventory, index, playerInventory.FindFirstEmptySlot(), restored, null), "Restored stash item can be withdrawn");
            var prior = JsonConvert.DeserializeObject<List<object>>(File.ReadAllText(Path.Combine(Output, "cycle1-merchants.json")));
            var before = Newtonsoft.Json.Linq.JArray.FromObject(prior);
            var now = Newtonsoft.Json.Linq.JArray.FromObject(merchantReports);
            Check(Newtonsoft.Json.Linq.JToken.DeepEquals(before, now), "All four merchant stock instances survive Play restart");
        }
        PressF(); for (int i = 0; i < 3; i++) yield return null; ReleaseKeys(); for (int i = 0; i < 7; i++) yield return null;
        Check(!stashInteraction.IsOpen, "F closes stash");
        Check(AccountGameplaySession.Current.FlushPendingSave(), "Account changes flushed to isolated ES3 storage");
        Check(AccountGameplaySession.Current.Read().merchants.Count(x => x.contentId.StartsWith("merchant.main-town.")) == 4, "Supplemental merchants included in account capture");
        if (Cycle == 1)
        {
            File.WriteAllText(Path.Combine(Output, "cycle1-merchants.json"), JsonConvert.SerializeObject(merchantReports, Formatting.Indented));
            var dungeon = Objects<MapDungeonPortal>(scene).Single();
            ActorTeleportUtility.TeleportSafely(actor.transform, dungeon.transform.position + Vector3.right * 2f + Vector3.up * .12f, Quaternion.identity);
            for (int i = 0; i < 15; i++) yield return null;
            PressF(); for (int i = 0; i < 3; i++) yield return null; ReleaseKeys(); for (int i = 0; i < 7; i++) yield return null;
            Check(Object.FindFirstObjectByType<MapDungeonPortalPanel>() != null, "F opens map selection"); Shot("MapSelection"); for (int i = 0; i < 3; i++) yield return null;
            Check(dungeon.EnterLevelOne(), "Town portal requests level-one dungeon");
            while (PersistentSceneFlow.Instance.IsSwitching) yield return null;
            Check(PersistentSceneFlow.Instance.CurrentSubSceneName == "DiamondDungeon01" && WorldSessionState.Phase == WorldPhase.Run, "Dungeon entry completes");
            var driver = Object.FindFirstObjectByType<RunLifetimeDriver>(); driver.RequestAbandon();
            while (WorldSessionState.Phase != WorldPhase.Hideout || PersistentSceneFlow.Instance.IsSwitching) yield return null;
            Check(PersistentSceneFlow.Instance.CurrentSubSceneName == PersistentSceneFlow.MainSceneName, "Run abandonment returns to MainScene");
            scene = SceneManager.GetSceneByName(PersistentSceneFlow.MainSceneName);
            var house = Objects<HubScenePortal>(scene).Single();
            ActorTeleportUtility.TeleportSafely(actor.transform, house.transform.position + house.transform.forward * 1.8f + Vector3.up * .12f, Quaternion.identity);
            for (int i = 0; i < 20; i++) yield return null;
            PressF(); for (int i = 0; i < 3; i++) yield return null; ReleaseKeys();
            while (PersistentSceneFlow.Instance.IsSwitching) yield return null;
            Check(PersistentSceneFlow.Instance.CurrentSubSceneName == PersistentSceneFlow.HideoutSceneName, "F at the inn loads the preserved HideoutScene");
            PersistentSceneFlow.Instance.SwitchHubScene(PersistentSceneFlow.MainSceneName);
            while (PersistentSceneFlow.Instance.IsSwitching) yield return null;
            Check(PersistentSceneFlow.Instance.CurrentSubSceneName == PersistentSceneFlow.MainSceneName, "MainScene reloads with four NPCs");
            Check(Objects<GeneralGoodsMerchantInteractable>(SceneManager.GetSceneByName(PersistentSceneFlow.MainSceneName)).Count() == 4, "Repeated entry has no duplicated merchants");
        }
        Check(errors.Count == 0, "No errors during the scoped runtime probe");
    }

    static bool MatchesCategory(ShopCategory category, BaseItemData item) => category switch
    {
        ShopCategory.Weapon => item is WeaponItemData,
        ShopCategory.Armor => item is GearItemData gear && (gear.kind == GearKind.Helmet || gear.kind == GearKind.Chest || gear.kind == GearKind.Gloves || gear.kind == GearKind.Boots),
        ShopCategory.Potion => item is ConsumableItemData,
        ShopCategory.GeneralGoods => item is BagItemData || item is GearItemData jewelry && (jewelry.kind == GearKind.Earring || jewelry.kind == GearKind.Necklace),
        _ => false
    };
    static MerchantDefinition Definition(GeneralGoodsMerchantInteractable npc) => new SerializedObject(npc).FindProperty("merchantDefinition").objectReferenceValue as MerchantDefinition;
    static IEnumerable<T> Objects<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<T>(true));
    static void PressF()
    {
        RecordInput("BeforeF"); inputTraceFrames = 15;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.F));
    }
    static void RecordInput(string moment)
    {
        var actor = PlayerContext.Instance?.CurrentActor;
        var facade = actor != null ? actor.GetComponent<PlayerInputFacade>() : null;
        var controller = actor != null ? actor.GetComponent<PlayerInteractionController>() : null;
        var selected = controller?.Current;
        var interact = facade?.GameplayMap?.FindAction("Interact", false);
        var p = actor != null ? actor.transform.position : Vector3.zero;
        inputReports.Add(new {
            moment, frame = Time.frameCount, focused = Application.isFocused,
            actor = new[] { p.x, p.y, p.z },
            keyboardEnabled = keyboard != null && keyboard.enabled,
            keyboardPressed = keyboard != null && keyboard.fKey.isPressed,
            keyboardWasPressed = keyboard != null && keyboard.fKey.wasPressedThisFrame,
            gameplayEnabled = facade != null && facade.IsGameplayEnabled,
            actionEnabled = interact != null && interact.enabled,
            actionPressed = interact != null && interact.WasPressedThisFrame(),
            actionHeld = interact != null && interact.IsPressed(),
            controls = interact?.controls.Select(x => x.path).ToArray(),
            blocked = GameplayInputBlocker.IsGameplayInputBlocked,
            selected = selected?.InteractionComponent?.name,
            distance = selected != null ? Vector3.Distance(p, selected.InteractionTransform.position) : -1f,
            range = selected?.InteractionRange,
            executions = controller?.ExecutionCount, result = controller?.LastResult.ToString()
        });
    }
    static void ReleaseKeys() { if (keyboard != null && keyboard.added) InputSystem.QueueStateEvent(keyboard, new KeyboardState()); }
    static void Shot(string name)
    {
        pendingShot = Path.Combine(Output, "cycle" + Cycle + "_" + name + ".png");
        shotDeadline = EditorApplication.timeSinceStartup + 15;
        ScreenCapture.CaptureScreenshot(pendingShot);
    }
    static void CaptureNpcPortrait(Transform npc, string category)
    {
        var root = new GameObject("MainTown owned portrait camera") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, npc.gameObject.scene);
        var camera = root.AddComponent<Camera>(); camera.enabled = false; camera.scene = npc.gameObject.scene;
        camera.fieldOfView = 32; camera.aspect = .75f; camera.nearClipPlane = .1f; camera.farClipPlane = 120; camera.allowHDR = true;
        var data = root.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.renderShadows = true;
        var rotation = Quaternion.Euler(12, npc.transform.eulerAngles.y + 180, 0);
        camera.transform.SetPositionAndRotation(npc.transform.position + Vector3.up - rotation * Vector3.forward * 4.8f, rotation);
        var rt = new RenderTexture(600, 800, 24, RenderTextureFormat.ARGB32); var texture = new Texture2D(600, 800, TextureFormat.RGB24, false);
        var prior = RenderTexture.active;
        var player = PlayerContext.Instance?.CurrentActor;
        var playerRenderers = player != null ? player.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
        var visible = playerRenderers.Select(x => x.enabled).ToArray();
        try
        {
            foreach (var renderer in playerRenderers) renderer.enabled = false;
            rt.Create(); camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 600, 800), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(Output, "cycle" + Cycle + "_Portrait_" + category + ".png"), texture.EncodeToPNG());
        }
        finally
        {
            for (int i = 0; i < playerRenderers.Length; i++) if (playerRenderers[i] != null) playerRenderers[i].enabled = visible[i];
            camera.targetTexture = null; RenderTexture.active = prior; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(texture); Object.DestroyImmediate(root);
        }
    }
    static void Check(bool pass, string label) { if (!pass) throw new InvalidOperationException(label); checks.Add(label); }
    static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    static void WriteCycle(string status, string error) => File.WriteAllText(Path.Combine(Output, "cycle" + Cycle + ".json"), JsonConvert.SerializeObject(new { status, error, cycle = Cycle, checks, errors, merchants = merchantReports, inputReports }, Formatting.Indented));
    static void CleanupRuntime()
    {
        Application.logMessageReceived -= Log; ReleaseKeys();
        if (originalInput != null) InputSystem.settings = originalInput;
        if (ownedInput != null) Object.DestroyImmediate(ownedInput);
        ownedInput = null; originalInput = null; work = null; pendingShot = null;
    }
    static void Fail(Exception error)
    {
        if (Phase == 0) return;
        SessionState.SetString(Key + "failure", error.ToString());
        if (Directory.Exists(Output)) WriteCycle("FAIL", error.ToString());
        CleanupRuntime(); Phase = 4;
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
    }
    static void ReturnEditor()
    {
        if (System.Diagnostics.Process.GetCurrentProcess().Id != SessionState.GetInt(Key + "pid", 0)) throw new InvalidOperationException("Editor owner changed.");
        string env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
        if (!string.IsNullOrEmpty(env) && !Path.GetFullPath(env).Equals(Path.Combine(Output, "Account"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Another account session owns the Editor.");
        IsolatedSavePlayGuard.UseRealAccount();
        string start = SessionState.GetString(Key + "startScene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(start) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        Application.runInBackground = SessionState.GetBool(Key + "background", false);
        var result = new { status = SessionState.GetString(Key + "failure", "") == "" ? "PASS" : "FAIL", failure = SessionState.GetString(Key + "failure", ""), requiresAccountChoice = IsolatedSavePlayGuard.RequiresAccountChoice, environment = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), compiling = EditorApplication.isCompiling, playing = EditorApplication.isPlaying, compilationFailed = EditorUtility.scriptCompilationFailed };
        File.WriteAllText(Path.Combine(Output, "return.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
        foreach (var name in new[] { "output", "startScene", "deadline", "failure", "fixtureId" }) SessionState.EraseString(Key + name);
        foreach (var name in new[] { "phase", "cycle", "pid", "stashIndex" }) SessionState.EraseInt(Key + name);
        SessionState.EraseBool(Key + "background"); Phase = 0; EditorApplication.update -= Tick;
    }
}
