using System;
using System.Collections;
using System.Collections.Generic;
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
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using InputKey = UnityEngine.InputSystem.Key;

[InitializeOnLoad]
public static class UIManagementCompletionVerifier
{
    const string Key = "Overburst.UIManagementCompletion.";
    static readonly List<string> checks = new List<string>();
    static IEnumerator work;
    static int lastFrame;
    static string Output => SessionState.GetString(Key + "output", "");
    static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
    static string Scenes() => JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i => { var s = SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, roots = s.GetRootGameObjects().Select(r => r.GetInstanceID()).OrderBy(id => id).ToArray() }; }));
    static UIManagementCompletionVerifier() { EditorApplication.update += Tick; EditorApplication.playModeStateChanged += State; }

    public static object NativeCatalog()
    {
        AppearanceCustomizationBuilder.RequireIdle(); checks.Clear(); string before = Scenes(); int previews = EditorSceneManager.previewSceneCount;
        var scene = EditorSceneManager.OpenScene(OverburstUIWorkshopBuilder.ScenePath, OpenSceneMode.Additive);
        string output = Path.GetFullPath(UIManagementCompletionBuilder.Output + "/Evidence/Catalog"); Directory.CreateDirectory(output);
        try
        {
            var roots = scene.GetRootGameObjects(); var gallery = roots.Single(r => r.name == OverburstUIWorkshopBuilder.GalleryName).transform;
            var groups = gallery.Cast<Transform>().ToArray(); var boards = groups.SelectMany(g => g.Cast<Transform>()).Cast<RectTransform>().ToArray();
            Check(groups.Length == 13 && groups.Select(g => g.name.Substring(0, 2)).Distinct().Count() == 13, "thirteen distinct category numbers");
            Check(boards.Length == 50 && boards.Select(b => int.Parse(b.name.Substring(0, 2))).SequenceEqual(Enumerable.Range(1, 50)), "fifty sequential unique board numbers");
            Check(boards.All(b => b.rect.size == new Vector2(1920, 1080) && b.localScale == Vector3.one), "all board dimensions remain 1920x1080");
            for (int a = 0; a < boards.Length; a++) for (int b = a + 1; b < boards.Length; b++)
                Check(Mathf.Abs(boards[a].localPosition.x - boards[b].localPosition.x) >= 2120 || Mathf.Abs(boards[a].localPosition.y - boards[b].localPosition.y) >= 1320, "board separation " + a + "/" + b);
            foreach (var board in boards)
            {
                Check(board.Find("Board Title").GetComponent<Text>().text == board.name, "heading matches " + board.name);
                var sample = board.GetComponent<OverburstUIRunGalleryPreview>();
                if (sample)
                {
                    var position = board.anchoredPosition3D; var size = board.sizeDelta;
                    sample.RefreshSample(); Check(board.anchoredPosition3D == position && board.sizeDelta == size, "run regeneration preserves parent canvas " + board.name);
                }
            }
            var catalog = roots.Single(r => r.name == UIManagementCompletionBuilder.SourceCatalog);
            var entries = catalog.transform.Cast<Transform>().SelectMany(g => g.Cast<Transform>()).Select(e => e.name).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            var optional = entries.Where(p => !AssetDatabase.LoadAssetAtPath<GameObject>(p)).ToArray();
            Check(entries.Except(optional).SequenceEqual(UIPrefabStructureBuilder.OwnerPaths()), "all current UI owners registered by source path");
            Check(optional.All(p => p == "Assets/ProjectOverburst/Resources/UI/MapDungeon/PF_MapDungeonPortal.prefab"), "only separately owned local map source may be unavailable");
            Check(catalog.GetComponentsInChildren<Transform>(true).All(t => t.CompareTag("EditorOnly")), "catalog is editor-only");
            Check(roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)) == 0, "entire scene missing scripts 0");
            var instances = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)).ToArray();
            Check(instances.All(t => PrefabUtility.GetPrefabInstanceStatus(t.gameObject) == PrefabInstanceStatus.Connected), "every scene prefab root connected");
            File.WriteAllText(Path.Combine(output, "coverage.json"), JsonConvert.SerializeObject(new { groups = groups.Select(g => new { g.name, boards = g.Cast<Transform>().Select(b => b.name).ToArray() }).ToArray(), entries, instances = instances.Length }, Formatting.Indented));
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
        Check(before == Scenes() && previews == EditorSceneManager.previewSceneCount, "catalog audit preserves user scene and preview count");
        File.WriteAllText(Path.Combine(output, "native-result.json"), JsonConvert.SerializeObject(new { success = true, checks }, Formatting.Indented)); return new { success = true, checks = checks.Count };
    }

    public static object CaptureGroup(int index)
    {
        AppearanceCustomizationBuilder.RequireIdle(); checks.Clear(); string before = Scenes(); int previews = EditorSceneManager.previewSceneCount;
        string output = Path.GetFullPath(UIManagementCompletionBuilder.Output + "/Evidence/AllGallery/" + index.ToString("00")); Directory.CreateDirectory(output);
        var scene = EditorSceneManager.OpenScene(OverburstUIWorkshopBuilder.ScenePath, OpenSceneMode.Additive);
        try
        {
            var gallery = scene.GetRootGameObjects().Single(r => r.name == OverburstUIWorkshopBuilder.GalleryName).transform; var group = gallery.GetChild(index);
            foreach (Transform board in group) foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) })
                CaptureBoard(board.gameObject, size, Path.Combine(output, board.name.Substring(0, 2) + "-" + size.x + ".png"));
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
        Check(before == Scenes() && previews == EditorSceneManager.previewSceneCount, "group captures preserve user scene " + index);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { success = true, checks }, Formatting.Indented)); return new { success = true, images = checks.Count - 1, index };
    }

    public static object Native(string region)
    {
        AppearanceCustomizationBuilder.RequireIdle(); checks.Clear();
        string output = Path.GetFullPath(UIManagementCompletionBuilder.Output + "/Evidence/" + region + "/Native");
        Directory.CreateDirectory(output); string before = Scenes(); int previews = EditorSceneManager.previewSceneCount;
        var scene = EditorSceneManager.OpenScene(OverburstUIWorkshopBuilder.ScenePath, OpenSceneMode.Additive);
        try
        {
            var roots = scene.GetRootGameObjects();
            var gallery = roots.Single(r => r.name == OverburstUIWorkshopBuilder.GalleryName).transform;
            Check(roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)) == 0, "scene missing scripts 0");
            if (region == "SkillTree")
            {
                var group = gallery.Find(UIManagementCompletionBuilder.SkillGroup); Check(group && group.childCount == 3, "three skill-tree states");
                var all = roots.SelectMany(r => r.GetComponentsInChildren<OverburstSkillTreeUI>(true)).ToArray(); Check(all.Length == 4, "three static and one live source");
                foreach (var ui in all)
                {
                    Check(ui.managementPreview, "management-only flag " + ui.name);
                    Check(PrefabUtility.GetPrefabInstanceStatus(ui.gameObject) == PrefabInstanceStatus.Connected && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(ui.gameObject) == OverburstSkillTreeBuilder.PrefabPath, "same connected skill source " + ui.name);
                    Check(ui.catalogAsset == Resources.Load<OverburstSkillTreeUI>(OverburstSkillTreeUI.ResourcePath).catalogAsset, "shared catalog " + ui.name);
                }
                Check(!Resources.Load<OverburstSkillTreeUI>(OverburstSkillTreeUI.ResourcePath).managementPreview, "product preview flag remains false");
                for (int i = 0; i < group.childCount; i++)
                {
                    var board = group.GetChild(i).gameObject;
                    Check(board.GetComponentsInChildren<GraphicRaycaster>(true).All(r => !r.enabled), "static board cannot receive pointer " + i);
                    foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) }) CaptureBoard(board, size, Path.Combine(output, i + "-" + size.x + ".png"));
                }
            }
            if (region == "Boss")
            {
                var group = gallery.Find(UIManagementCompletionBuilder.BossGroup); Check(group && group.childCount == 3, "three boss states");
                var previewsInScene = roots.SelectMany(r => r.GetComponentsInChildren<OverburstUIBossPreview>(true)).ToArray(); Check(previewsInScene.Length == 4, "three boss boards and one live preview");
                foreach (var preview in previewsInScene)
                {
                    Check(preview.view.transform.localScale == Vector3.one, "boss canvas has nonzero scale " + preview.name);
                    Check(preview.view && !preview.view.enabled && preview.view.BoundBoss == null && preview.view.BoundEncounterSource == null, "preview never subscribes to encounter " + preview.name);
                    Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(preview.view.gameObject) == UIManagementCompletionBuilder.BossSource && PrefabUtility.GetPrefabInstanceStatus(preview.view.gameObject) == PrefabInstanceStatus.Connected, "same connected boss source " + preview.name);
                }
                for (int i = 0; i < group.childCount; i++)
                {
                    var board = group.GetChild(i).gameObject; var view = board.GetComponentInChildren<EnemyBossHudView>(true);
                    Check(view.IsVisible && Mathf.Abs(view.DisplayedHealth01 - new[] { 1f, .65f, .28f }[i]) < .001f, "saved HP sample " + i);
                    foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) }) CaptureBoard(board, size, Path.Combine(output, i + "-" + size.x + ".png"));
                }
            }
            if (region == "Run")
            {
                var group = gallery.Find(UIManagementCompletionBuilder.RunGroup); Check(group && group.childCount == 3, "three run states");
                for (int i = 0; i < group.childCount; i++)
                {
                    var board = group.GetChild(i).gameObject; var preview = board.GetComponent<OverburstUIRunGalleryPreview>();
                    Check(preview && preview.Generated && AssetDatabase.GetAssetPath(preview.productionSource) == UIManagementCompletionBuilder.RunSource, "production generation source " + i);
                    Check(PrefabUtility.GetPrefabInstanceStatus(preview.Generated) == PrefabInstanceStatus.Connected, "connected generated run root " + i);
                    Check((preview.Generated.hideFlags & HideFlags.DontSaveInEditor) != 0 && preview.Generated.GetComponentsInChildren<GraphicRaycaster>(true).All(r => !r.enabled), "transient display has no pointer input " + i);
                    Check(preview.Generated.GetComponentsInChildren<OverburstRunCardView>(true).Length == 3, "real lazy card layout " + i);
                    foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) }) CaptureBoard(board, size, Path.Combine(output, i + "-" + size.x + ".png"));
                }
                Check(!File.ReadAllText(OverburstUIWorkshopBuilder.ScenePath).Contains("Production layout • generated, not saved"), "generated layout never serialized in management scene");
            }
            if (region == "World")
            {
                var group = gallery.Find(UIManagementCompletionBuilder.WorldGroup); Check(group && group.childCount == 5, "five world display boards");
                for (int i = 0; i < group.childCount; i++)
                {
                    var board = group.GetChild(i).gameObject;
                    foreach (var source in board.transform.Cast<Transform>().Where(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)))
                        Check(PrefabUtility.GetPrefabInstanceStatus(source.gameObject) == PrefabInstanceStatus.Connected, "connected world source " + source.name);
                    Check(board.GetComponentsInChildren<GraphicRaycaster>(true).All(r => !r.enabled), "static world board input disabled " + i);
                    foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) }) CaptureBoard(board, size, Path.Combine(output, i + "-" + size.x + ".png"));
                }
            }
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { success = false, checks, error = error.ToString() }, Formatting.Indented)); throw; }
        finally { EditorSceneManager.CloseScene(scene, true); }
        Check(before == Scenes() && previews == EditorSceneManager.previewSceneCount, "user scenes and preview count preserved");
        var result = new { success = true, checks = checks.ToArray(), region, utc = DateTime.UtcNow, player = "EXCLUDED_BY_USER" };
        File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(result, Formatting.Indented)); return new { success = true, checks = checks.Count, output };
    }

    public static void CaptureBoard(GameObject board, Vector2Int size, string path)
    {
        var scene = EditorSceneManager.NewPreviewScene(); GameObject root = null, cameraObject = null; RenderTexture rt = null;
        try
        {
            root = Object.Instantiate(board); SceneManager.MoveGameObjectToScene(root, scene); root.transform.SetParent(null, false); root.transform.position = Vector3.zero;
            cameraObject = new GameObject("Owned Completion UI Capture", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>(); camera.scene = scene; camera.enabled = false; camera.transform.position = new Vector3(0, 0, -20); camera.orthographic = true; camera.orthographicSize = 5; camera.nearClipPlane = .01f; camera.farClipPlane = 100; camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            rt = new RenderTexture(size.x, size.y, 24); rt.Create(); camera.targetTexture = rt;
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
            var scaler = root.GetComponent<CanvasScaler>() ?? root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            for (int pass = 0; pass < 3; pass++) { Canvas.ForceUpdateCanvases(); var request = new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = rt }; if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera, request)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request); else camera.Render(); }
            AppearanceCustomizationVerifier.Save(rt, path);
            var pixels = new Texture2D(2, 2);
            try { pixels.LoadImage(File.ReadAllBytes(path)); Check(pixels.GetPixels32().Any(p => p.r > 30 || p.g > 30 || p.b > 30), "visible render " + board.name + " " + size); }
            finally { Object.DestroyImmediate(pixels); }
        }
        finally { if (root) Object.DestroyImmediate(root); if (rt) { rt.Release(); Object.DestroyImmediate(rt); } if (cameraObject) Object.DestroyImmediate(cameraObject); EditorSceneManager.ClosePreviewScene(scene); }
    }

    public static string StartPlay(string region, string label)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if (!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || IsolatedSavePlayGuard.RequiresAccountChoice) throw new InvalidOperationException("Another account owner active.");
        string output = Path.GetFullPath(UIManagementCompletionBuilder.Output + "/Evidence/" + region + "/" + label);
        if (Directory.Exists(output)) throw new InvalidOperationException("Fresh output required."); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "edit-before.json"), JsonConvert.SerializeObject(new { scenes = Scenes(), input = InputSystem.settings.GetInstanceID(), start = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) }, Formatting.Indented));
        SessionState.SetString(Key + "output", output); SessionState.SetString(Key + "region", region); SessionState.SetString(Key + "scenes", Scenes());
        SessionState.SetString(Key + "start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)); SessionState.SetInt(Key + "input", InputSystem.settings.GetInstanceID());
        SessionState.SetBool(Key + "background", Application.runInBackground); SessionState.SetBool(Key + "pending", true); SessionState.SetBool(Key + "entered", false); SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 180);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(OverburstUIWorkshopBuilder.ScenePath);
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount")); }
        catch (Exception error) { Finish(error); throw; }
        return "STARTED " + region;
    }
    static bool Own => string.Equals(IsolatedSavePlayGuard.ActiveDirectory, Path.Combine(Output, "IsolatedAccount"), StringComparison.OrdinalIgnoreCase);
    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "pending", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) { SessionState.SetBool(Key + "entered", true); Application.runInBackground = true; checks.Clear(); work = Verify(SessionState.GetString(Key + "region", "")); lastFrame = -1; }
        if (state == PlayModeStateChange.ExitingPlayMode) { (work as IDisposable)?.Dispose(); work = null; Application.runInBackground = SessionState.GetBool(Key + "background", false); SessionState.SetBool(Key + "return", true); }
        if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key + "return", true);
    }
    static void Tick()
    {
        if (SessionState.GetBool(Key + "return", false)) { Return(); return; }
        if (!SessionState.GetBool(Key + "pending", false)) return;
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0)) { Finish(new TimeoutException("Owned management Play timeout")); return; }
        if (!EditorApplication.isPlaying || !Own) return;
        if (work == null) { if (SessionState.GetBool(Key + "entered", false)) Finish(new InvalidOperationException("Verification interrupted by reload")); return; }
        EditorApplication.QueuePlayerLoopUpdate(); if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
        try { if (!work.MoveNext()) Finish(null); } catch (Exception error) { Finish(error); }
    }
    static IEnumerator Verify(string region)
    {
        yield return null; yield return null;
        var workshop = Object.FindFirstObjectByType<OverburstUIWorkshop>(); Check(workshop, "actual management workshop exists");
        Check(AccountGameplaySession.Current == null, "no gameplay account installed");
        if (region == "All")
        {
            foreach (string scope in new[] { "SkillTree", "Boss", "Run" })
            {
                var test = Verify(scope);
                try { while (test.MoveNext()) yield return null; }
                finally { (test as IDisposable)?.Dispose(); }
            }
            Action[] actions = { workshop.ShowHud, workshop.ShowInventory, workshop.ShowEquipment, workshop.ShowStash, workshop.ShowComparison, workshop.ShowShop,
                workshop.ShowEnemies, workshop.ShowNotifications, workshop.ShowGrades, workshop.ShowTooltips, workshop.ShowQuestApproach, workshop.ShowQuestDialogue,
                workshop.ShowMerchantQuests, workshop.ShowMerchantQuestDetail, workshop.ShowPersonalQuests, workshop.ShowQuestReport, workshop.ShowCornerMenu, workshop.ShowStylistApproach };
            for (int cycle = 0; cycle < 3; cycle++)
            {
                for (int i = 0; i < actions.Length; i++)
                {
                    actions[i](); yield return null;
                    Check(GameplayInputBlocker.IsGameplayInputBlocked == (i == 16), "sample follows product input ownership " + cycle + "/" + i);
                    if (i >= 1 && i <= 5)
                    {
                        Check(workshop.Inventory.gameObject.activeSelf == (i == 1 || i == 3 || i == 4 || i == 5), "inventory visibility " + cycle + "/" + i);
                        Check(workshop.Equipment.gameObject.activeSelf == (i == 2 || i == 4), "equipment visibility " + cycle + "/" + i);
                        Check(workshop.Stash.gameObject.activeSelf == (i == 3), "stash visibility " + cycle + "/" + i);
                    }
                    if (i == 5) Check(workshop.GetComponent<OverburstUIShopPreview>().IsVisible, "shop sample appears " + cycle);
                    if (i >= 10) Check(workshop.GetComponentInChildren<OverburstUIQuestMerchantPreview>(true).IsVisible, "quest/menu/stylist sample appears " + cycle + "/" + i);
                }
                workshop.ShowStash(); var tabs = workshop.Stash.GetComponent<OverburstUIStashPreview>();
                tabs.First(); Check(tabs.SelectedTab == 0, "stash first tab " + cycle); tabs.Second(); Check(tabs.SelectedTab == 1, "stash second tab " + cycle); tabs.Third(); Check(tabs.SelectedTab == 2, "stash third tab " + cycle);
                var rect = workshop.Inventory.WindowRect; var home = rect.anchoredPosition; rect.anchoredPosition += new Vector2(90, 40); workshop.ResetLayout(); Check(rect.anchoredPosition == home, "window position reset " + cycle);
                workshop.ShowHud();
            }
            var keyboard = Keyboard.current; Check(keyboard != null, "real input keyboard available");
            foreach (var route in new[] { InputKey.I, InputKey.C, InputKey.B, InputKey.J, InputKey.K, InputKey.S, InputKey.R, InputKey.T })
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(route)); yield return null; yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                bool visible = route == InputKey.I ? workshop.Inventory.gameObject.activeSelf : route == InputKey.C ? workshop.Equipment.gameObject.activeSelf : route == InputKey.B ? workshop.Stash.gameObject.activeSelf
                    : route == InputKey.J ? workshop.GetComponentInChildren<OverburstUIQuestMerchantPreview>(true).IsVisible : route == InputKey.K ? workshop.GetComponentInChildren<OverburstSkillTreeUI>(true).IsOpen
                    : route == InputKey.S ? workshop.GetComponent<OverburstUIShopPreview>().IsVisible : workshop.GetComponent<OverburstUIRunPreview>().IsOpen;
                Check(visible, "actual management keyboard route " + route);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(InputKey.Escape)); yield return null; yield return null; InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                Check(!GameplayInputBlocker.IsGameplayInputBlocked, "escape releases input " + route); workshop.ShowHud();
            }
            string account = Path.Combine(Output, "IsolatedAccount");
            Check(AccountGameplaySession.Current == null && (!Directory.Exists(account) || !Directory.GetFiles(account, "*.es3", SearchOption.AllDirectories).Any()), "all management samples write no gameplay save");
        }
        if (region == "SkillTree")
        {
            var ui = workshop.GetComponentsInChildren<OverburstSkillTreeUI>(true).Single();
            Check(ui.managementPreview && OverburstSkillTreeUI.Instance == null, "preview never replaces gameplay singleton");
            for (int cycle = 0; cycle < 3; cycle++)
            {
                workshop.ShowSkillTree(); yield return null; Check(ui.IsOpen && ui.CanEdit && ui.Plan.Budget == 20, "open sample cycle " + cycle);
                ui.SelectNode("S_W1", true); var node = ui.nodes.Single(n => n.nodeId == "S_W1");
                var pointer = new PointerEventData(EventSystem.current); ExecuteEvents.Execute(node.gameObject, pointer, ExecuteEvents.pointerEnterHandler); Check(ui.tooltip.gameObject.activeSelf, "node hover " + cycle); ExecuteEvents.Execute(node.gameObject, pointer, ExecuteEvents.pointerExitHandler); Check(!ui.tooltip.gameObject.activeSelf, "node hover exit " + cycle);
                ui.action.onClick.Invoke(); Check(ui.Plan.Changed, "plan edit " + cycle); ui.cancel.onClick.Invoke(); Check(!ui.Plan.Changed, "cancel draft " + cycle);
                ui.action.onClick.Invoke(); ui.apply.onClick.Invoke(); Check(!ui.Plan.Changed && ui.Plan.Applied.Contains("S_W1"), "sample apply without account " + cycle);
                ui.resetAllocation.onClick.Invoke(); ui.apply.onClick.Invoke(); Check(ui.Plan.Applied.Length == 0, "refund sample " + cycle);
                float zoom = ui.Zoom; ui.zoomIn.onClick.Invoke(); Check(ui.Zoom > zoom, "zoom control " + cycle); ui.SetPan(new Vector2(80, 30)); Check(ui.Pan.sqrMagnitude > 0, "pan " + cycle); ui.center.onClick.Invoke();
                ui.close.onClick.Invoke(); Check(!ui.IsOpen && !GameplayInputBlocker.IsGameplayInputBlocked, "close releases ownership " + cycle);
            }
            workshop.ShowCornerMenu(); var menu = workshop.GetComponentInChildren<OverburstUIQuestMerchantPreview>().menu; menu.Rows[2].button.onClick.Invoke(); yield return null; Check(ui.IsOpen && !menu.IsOpen, "actual corner menu skill-tree route"); workshop.ShowInventory(); Check(!ui.IsOpen && workshop.Inventory.gameObject.activeSelf, "switch to inventory closes skill tree");
            string account = Path.Combine(Output, "IsolatedAccount");
            Check(AccountGameplaySession.Current == null && (!Directory.Exists(account) || !Directory.GetFiles(account, "*.es3", SearchOption.AllDirectories).Any()), "preview writes no account generation");
        }
        if (region == "Boss")
        {
            var preview = workshop.GetComponentInChildren<OverburstUIBossPreview>(true);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                workshop.ShowBoss(); yield return null; Check(preview.IsVisible, "boss preview opens " + cycle);
                var panel = (RectTransform)preview.view.transform.Find("BossPanel");
                var corners = new Vector3[4]; panel.GetWorldCorners(corners);
                var camera = preview.view.GetComponent<Canvas>().rootCanvas.worldCamera;
                float width = Vector2.Distance(RectTransformUtility.WorldToScreenPoint(camera, corners[0]), RectTransformUtility.WorldToScreenPoint(camera, corners[3]));
                Check(width > 100, "boss projected panel width " + cycle + ": " + width);
                Check(preview.view.GetComponentsInChildren<Image>(true).Single(i => i.name == "FxBack").material.shader.name == "OVERBURST/UI/HUD Boss Emblem Fx", "boss production FX material " + cycle);
                for (int i = 0; i < 3; i++)
                {
                    int phase = preview.phase;
                    Check(preview.view.BoundBoss == null && preview.view.BoundEncounterSource == null && !preview.view.enabled, "no encounter binding " + cycle + "/" + phase);
                    Check(Mathf.Abs(preview.view.DisplayedHealth01 - new[] { 1f, .65f, .28f }[phase]) < .001f, "HP sample " + cycle + "/" + phase);
                    Check(preview.view.DisplayedLitPhaseGems == 3 - phase, "remaining phases " + cycle + "/" + phase);
                    workshop.NextBossPhase(); yield return null;
                }
                workshop.ShowInventory(); Check(!preview.IsVisible && workshop.Inventory.gameObject.activeSelf, "boss hide on window switch " + cycle);
            }
            Check(AccountGameplaySession.Current == null && !GameplayInputBlocker.IsGameplayInputBlocked, "boss samples keep account and input ownership free");
        }
        if (region == "Run")
        {
            var preview = workshop.GetComponent<OverburstUIRunPreview>(); Check(preview, "existing R/T production preview reused");
            for (int cycle = 0; cycle < 3; cycle++)
            {
                workshop.ShowRunCards(); yield return null;
                var ui = preview.RunUi; var cards = ui.GetComponentsInChildren<OverburstRunCardView>(true);
                Check(ui.IsOpen && GameplayInputBlocker.IsGameplayInputBlocked && cards.Length == 3, "card modal owns input " + cycle);
                var choose = cards[cycle].GetComponentInChildren<Button>(true);
                ExecuteEvents.Execute(choose.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                Check(ui.IsOpen && !choose.interactable, "selection locked until reveal " + cycle);
                float until = Time.unscaledTime + 5;
                while (!cards.All(c => c.GetComponentInChildren<Button>(true).interactable) && Time.unscaledTime < until) yield return null;
                Check(cards.All(c => c.transform.Find("Flip/Front").gameObject.activeSelf && c.GetComponentInChildren<Button>(true).interactable), "all production cards reveal " + cycle);
                var pointer = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(choose.gameObject, pointer, ExecuteEvents.pointerEnterHandler); yield return null;
                Check(choose.interactable, "card pointer hover accepts input " + cycle);
                ExecuteEvents.Execute(choose.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                ExecuteEvents.Execute(choose.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler); yield return null;
                Check(!ui.IsOpen && !GameplayInputBlocker.IsGameplayInputBlocked, "sample selection closes and releases input " + cycle);
                workshop.ShowRunTransfer(); yield return null;
                Check(ui.IsTransferOpen && workshop.Inventory.gameObject.activeSelf && workshop.Equipment.gameObject.activeSelf, "transfer combination opens " + cycle);
                var confirm = ui.transform.Find("Modal/SmallTransfer/ConfirmTransfer").GetComponent<Button>();
                Check(confirm.interactable, "sample staged " + cycle);
                confirm.onClick.Invoke();
                Check(ui.IsTransferOpen && confirm.interactable && ui.transform.Find("Modal/SmallTransfer/Status").GetComponent<TMPro.TMP_Text>().text.Contains("부족"), "no-space failure retains item and retry " + cycle);
                Check(!ui.TryStageTransfer("outside-run") && !confirm.interactable, "invalid item clears staging " + cycle);
                Check(ui.TryStageTransfer("workshop-transfer-sample") && confirm.interactable, "valid item can restage " + cycle);
                var border = ui.transform.Find("Modal/SmallTransfer/DropSlot/DropHighlight").GetComponent<Image>();
                ui.SetTransferHover(true); Check(border.color == RunUiLayout.Gold, "transfer hover color " + cycle);
                ExecuteEvents.Execute(ui.transform.Find("Modal/SmallTransfer/DropSlot").gameObject, pointer, ExecuteEvents.pointerExitHandler); Check(border.color == Color.white, "drop hover exit " + cycle);
                ui.transform.Find("Modal/SmallTransfer/Cancel").GetComponent<Button>().onClick.Invoke(); yield return null;
                Check(!ui.IsOpen && !GameplayInputBlocker.IsGameplayInputBlocked, "cancel releases transfer " + cycle);
                workshop.ShowRunCards(); yield return null; workshop.ShowInventory();
                Check(!ui.IsOpen && workshop.Inventory.gameObject.activeSelf, "window switch cancels run modal " + cycle);
            }
            var product = preview.RunUi; int calls = 0; bool accept = false;
            product.ShowCards(OverburstUIRunPreview.CardSamples(), _ => { calls++; return accept; });
            var first = product.GetComponentsInChildren<OverburstRunCardView>(true)[0].GetComponentInChildren<Button>(true);
            float deadline = Time.unscaledTime + 5;
            while (!first.interactable && Time.unscaledTime < deadline) yield return null;
            ExecuteEvents.Execute(first.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Check(calls == 1 && product.IsOpen && first.interactable, "failed card commit keeps selection for retry");
            accept = true; ExecuteEvents.Execute(first.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            ExecuteEvents.Execute(first.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Check(calls == 2 && !product.IsOpen, "successful card callback commits once");
            foreach (var result in new[] { RunTransferResult.Success, RunTransferResult.SaveFailed, RunTransferResult.StaleItem, RunTransferResult.Unavailable })
            {
                int commands = 0;
                product.ShowTransfer(new[] { new RunTransferPresentation { ItemInstanceId = "sample", Name = "검수용 아이템", Quantity = 2, Grade = ItemGrade.Rare } }, _ => { commands++; return result; });
                Check(product.TryStageTransfer("sample"), "stage callback sample " + result);
                var confirm = product.transform.Find("Modal/SmallTransfer/ConfirmTransfer").GetComponent<Button>(); confirm.onClick.Invoke();
                bool retry = result == RunTransferResult.SaveFailed;
                Check(commands == 1 && product.IsTransferOpen && confirm.interactable == retry, "transfer callback disposition " + result);
                if (!retry) { confirm.onClick.Invoke(); Check(commands == 1, "completed transfer cannot commit twice " + result); }
                product.Cancel(); Check(!product.IsOpen && !GameplayInputBlocker.IsGameplayInputBlocked, "close callback sample " + result);
            }
            string account = Path.Combine(Output, "IsolatedAccount");
            Check(AccountGameplaySession.Current == null && (!Directory.Exists(account) || !Directory.GetFiles(account, "*.es3", SearchOption.AllDirectories).Any()), "samples never save rewards or items");
        }
        workshop.ShowHud();
    }
    static void Finish(Exception error)
    {
        (work as IDisposable)?.Dispose(); work = null;
        File.WriteAllText(Path.Combine(Output, "play-result.json"), JsonConvert.SerializeObject(new { success = error == null, checks, error = error?.ToString(), utc = DateTime.UtcNow }, Formatting.Indented));
        SessionState.SetBool(Key + "pending", false); SessionState.SetBool(Key + "return", true);
        if (EditorApplication.isPlaying && Own) EditorApplication.ExitPlaymode();
    }
    static void Return()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        foreach (var path in new[] { IsolatedSavePlayGuard.ActiveDirectory, Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "") })
            if (!string.IsNullOrEmpty(path) && !string.Equals(Path.GetFullPath(path), Path.Combine(Output, "IsolatedAccount"), StringComparison.OrdinalIgnoreCase)) return;
        string output = Output; string start = SessionState.GetString(Key + "start", ""); EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(start) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start); IsolatedSavePlayGuard.UseRealAccount();
        bool success = Scenes() == SessionState.GetString(Key + "scenes", "") && InputSystem.settings.GetInstanceID() == SessionState.GetInt(Key + "input", 0) && !IsolatedSavePlayGuard.RequiresAccountChoice;
        File.WriteAllText(Path.Combine(output, "return-result.json"), JsonConvert.SerializeObject(new { success, scenes = Scenes(), input = InputSystem.settings.GetInstanceID(), guard = IsolatedSavePlayGuard.RequiresAccountChoice }, Formatting.Indented));
        foreach (string field in new[] { "output", "region", "scenes", "start" }) SessionState.EraseString(Key + field);
        foreach (string field in new[] { "pending", "entered", "return", "background" }) SessionState.EraseBool(Key + field);
        SessionState.EraseInt(Key + "input"); SessionState.EraseFloat(Key + "deadline");
    }
}
