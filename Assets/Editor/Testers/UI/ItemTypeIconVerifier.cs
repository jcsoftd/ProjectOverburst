using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class ItemTypeIconVerifier
{
    static readonly List<string> checks = new List<string>();

    [MenuItem("OVERBURST/UI/아이템 분류 아이콘 검증과 비교 캡처")]
    public static void VerifyAndCapture()
    {
        ItemTypeIconBuilder.RequireIdle();
        checks.Clear();
        var random = UnityEngine.Random.state;
        var scenesBefore = Scenes();
        try
        {
            ItemData[] samples = Samples();
            Check(samples.Length == 13, "Eight types and five element references loaded");
            string[] paths = ItemTypeIconBuilder.Prefabs.Concat(new[] {
                WeaponElementIconBuilder.UiRoot + "PF_OverburstEquipment_Rpg11.prefab",
                WeaponElementIconBuilder.UiRoot + "PF_OverburstInventory_Rpg11.prefab",
                WeaponElementIconBuilder.UiRoot + "PF_OverburstStash_Rpg11.prefab" }).ToArray();
            foreach (string path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Check(prefab && prefab.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "No missing scripts: " + Path.GetFileName(path));
                var badges = prefab.GetComponentsInChildren<ItemTypeIconView>(true);
                Check(badges.Length > 0, "Authored type badges: " + Path.GetFileName(path) + " = " + badges.Length);
                foreach (var badge in badges)
                {
                    var rect = (RectTransform)badge.transform;
                    var element = badge.transform.parent.Find("Weapon Element Badge") as RectTransform;
                    Check(rect.anchorMin == WeaponElementIconBuilder.BadgeAnchor && rect.anchorMax == rect.anchorMin
                        && rect.pivot == rect.anchorMin && rect.anchoredPosition == WeaponElementIconBuilder.BadgeOffset,
                        "Bottom right position: " + badge.transform.parent.name);
                    if (element) Check(rect.sizeDelta == element.sizeDelta, "Same reference size as element: " + badge.transform.parent.name);
                    Check(badge.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), "Badge does not intercept input");
                }
            }
            Render(samples, 1);
            Render(samples, 2);
            Check(Scenes() == scenesBefore, "Open scenes and dirty states preserved");
            Write("edit-results.json", new { status = "PASS", checks });
        }
        catch (Exception e) { Write("edit-results.json", new { status = "FAIL", checks, error = e.ToString() }); throw; }
        finally { UnityEngine.Random.state = random; }
    }

    public static ItemData[] Samples()
    {
        var values = new List<ItemData>();
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS024_TrainingIronGreatsword/GRS024_TrainingIronGreatsword.asset");
        values.Add(new ItemData(weapon, 20, ItemGrade.Rare));
        foreach (string kind in new[] { "Helmet", "Chest", "Gloves", "Boots", "Necklace", "EarringA" })
            values.Add(new ItemData(Resources.Load<GearItemData>("Items/Gear/Gear_" + kind), 20, ItemGrade.Rare));
        values.Add(new ItemData(AssetDatabase.LoadAssetAtPath<BagItemData>("Assets/ProjectOverburst/03_Features/Items/Data/Items/Bags/Bag_Grade3.asset"), 20, ItemGrade.Rare));
        var gems = AssetDatabase.FindAssets("t:ElementGemItemData", new[] { "Assets/ProjectOverburst" })
            .Select(g => AssetDatabase.LoadAssetAtPath<ElementGemItemData>(AssetDatabase.GUIDToAssetPath(g))).Where(g => g).ToArray();
        foreach (var element in new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light })
        {
            var gem = gems.First(g => g.element == element);
            values.Add(new ItemData(gem, 20, gem.fixedGrade));
        }
        if (values.Any(v => !v.HasValidBaseData)) throw new InvalidOperationException("Sample definition missing");
        return values.ToArray();
    }

    static void Render(ItemData[] samples, int scale)
    {
        const int width = 1280, height = 360, layer = 30;
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject cameraObject = null, canvasObject = null;
        RenderTexture target = null;
        Texture2D pixels = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            cameraObject = new GameObject("Item type comparison camera", typeof(Camera));
            canvasObject = new GameObject("Item type comparison canvas", typeof(RectTransform), typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, preview);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = preview;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true; camera.orthographicSize = height * .5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .08f, .09f);
            camera.cullingMask = 1 << layer;
            target = new RenderTexture(width * scale, height * scale, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            // Keep UI units identical at both capture resolutions.
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = scale;
            var shared = AssetDatabase.LoadAssetAtPath<GameObject>(ItemTypeIconBuilder.SharedSlot);
            var shop = AssetDatabase.LoadAssetAtPath<GameObject>(ItemTypeIconBuilder.Prefabs[1]).GetComponentsInChildren<SlotUI>(true)[0].gameObject;
            Label(canvas.transform, "84px slots / type badge 23 / existing element badge 23", new Vector2(-610, 155), 14);
            Label(canvas.transform, "65px shop slots / type badge 21 / existing element badge 21", new Vector2(-610, -20), 14);
            string[] labels = ItemTypeIconBuilder.Names.Concat(new[] { "Fire", "Ice", "Electric", "Dark", "Light" }).ToArray();
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < samples.Length; i++)
                {
                    var slot = Object.Instantiate(row == 0 ? shared : shop, canvas.transform, false);
                    var rect = (RectTransform)slot.transform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
                    rect.anchoredPosition = new Vector2(-564 + i * 94, row == 0 ? 77 : -99);
                    rect.localScale = Vector3.one; rect.sizeDelta = Vector2.one * (row == 0 ? 84 : 65);
                    SlotUI runtime = slot.GetComponent<SlotUI>();
                    if (!runtime && row == 0) runtime = slot.AddComponent<SlotUI>();
                    runtime.SetDisplayItem(samples[i]);
                    var type = slot.GetComponentInChildren<ItemTypeIconView>(true);
                    var element = slot.GetComponentInChildren<WeaponElementIconView>(true);
                    Check(type && type.IsVisible == (i < 8), "Category visibility " + row + "/" + labels[i]);
                    Check(element && element.IsVisible == (i >= 8), "Element visibility " + row + "/" + labels[i]);
                    if (i < 8) Check(type.DisplayedSprite.name.Contains("_" + ItemTypeIconBuilder.Names[i]), "Correct type sprite " + labels[i]);
                    runtime.SetLocked(true); Check(!type.IsVisible && !element.IsVisible, "Locked badges hidden");
                    runtime.SetLocked(false); runtime.SetDisplayItem(samples[(i + 1) % samples.Length]);
                    runtime.SetDisplayItem(null); Check(!type.IsVisible && !element.IsVisible, "Reused empty slot clears badges");
                    runtime.SetDisplayItem(samples[i]);
                    runtime.SetNewItemMarker(i == 0);
                    if (i == 0) Check(((RectTransform)slot.transform.Find("NewItemMarker")).anchorMin == Vector2.one, "New marker top right");
                    Label(canvas.transform, labels[i], new Vector2(-606 + i * 94, row == 0 ? 12 : -150), 12);
                }
            foreach (Transform t in canvasObject.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            Canvas.ForceUpdateCanvases();
            camera.Render(); RenderTexture.active = target;
            pixels = new Texture2D(width * scale, height * scale, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(ItemTypeIconBuilder.Output, "type-element-comparison-" + scale + "x.png"), pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            if (cameraObject) cameraObject.GetComponent<Camera>().targetTexture = null;
            if (pixels) Object.DestroyImmediate(pixels);
            if (target) { target.Release(); Object.DestroyImmediate(target); }
            if (canvasObject) Object.DestroyImmediate(canvasObject);
            if (cameraObject) Object.DestroyImmediate(cameraObject);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    static void Label(Transform parent, string text, Vector2 position, int size)
    {
        var go = new GameObject("Comparison label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
        var label = go.GetComponent<Text>(); label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = text; label.fontSize = size; label.color = Color.white; label.raycastTarget = false;
        var rect = label.rectTransform; rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
        rect.pivot = new Vector2(0, 1); rect.anchoredPosition = position; rect.sizeDelta = new Vector2(1250, 24);
    }

    static string Scenes() => JsonConvert.SerializeObject(Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
        .Select(i => { var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, s.rootCount }; }));
    static void Check(bool pass, string description) { checks.Add((pass ? "PASS " : "FAIL ") + description); if (!pass) throw new InvalidOperationException(description); }
    static void Write(string name, object value) { Directory.CreateDirectory(ItemTypeIconBuilder.Output); File.WriteAllText(Path.Combine(ItemTypeIconBuilder.Output, name), JsonConvert.SerializeObject(value, Formatting.Indented)); }
}

[InitializeOnLoad]
public static class ItemTypeIconPlayVerifier
{
    const string Key = "ItemTypeIconPlayVerifier.";
    static IEnumerator work;
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static double deadline;
    static int lastFrame;
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");
    static string Account => Path.GetFullPath(Path.Combine(ItemTypeIconBuilder.Output, "IsolatedAccount"));
    static string Report => SessionState.GetBool(Key + "restore", false) ? "play02-results.json" : "play01-results.json";

    static ItemTypeIconPlayVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetBool(Key + "return", false)) EditorApplication.update += ReturnAccount;
    }

    public static void Run(bool restore)
    {
        ItemTypeIconBuilder.RequireIdle();
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("PersistentScene required");
        SessionState.SetInt(Key + "pid", System.Diagnostics.Process.GetCurrentProcess().Id);
        SessionState.SetBool(Key + "restore", restore);
        SessionState.SetBool(Key + "pending", true);
        SessionState.SetString(Key + "status", "RUNNING");
        SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Account); }
        catch { SessionState.SetBool(Key + "return", true); EditorApplication.update += ReturnAccount; throw; }
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "pending", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); errors.Clear(); deadline = EditorApplication.timeSinceStartup + 180; lastFrame = -1;
            SessionState.SetBool(Key + "background", Application.runInBackground); Application.runInBackground = true;
            work = Verify(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + "background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            // Run after every play-mode listener, including the guard that blocks ordinary Play on exit.
            SessionState.SetBool(Key + "return", true);
            EditorApplication.update -= ReturnAccount; EditorApplication.update += ReturnAccount;
        }
    }

    static void ReturnAccount()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (System.Diagnostics.Process.GetCurrentProcess().Id != SessionState.GetInt(Key + "pid", 0)) return;
        string environment = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
        if ((!string.IsNullOrEmpty(environment) && Path.GetFullPath(environment) != Account)
            || (!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) && Path.GetFullPath(IsolatedSavePlayGuard.ActiveDirectory) != Account)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
        { SessionState.SetString(Key + "status", "RETURN_DEFERRED"); EditorApplication.update -= ReturnAccount; return; }
        IsolatedSavePlayGuard.UseRealAccount();
        bool ready = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""))
            && SessionState.GetString(Key + "startScene", "") == AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
        SessionState.SetBool(Key + "pending", false); SessionState.SetBool(Key + "return", false);
        SessionState.EraseString(Key + "startScene"); SessionState.EraseInt(Key + "pid");
        EditorApplication.update -= ReturnAccount;
        File.WriteAllText(Path.Combine(ItemTypeIconBuilder.Output, Report.Replace("results", "return")), JsonConvert.SerializeObject(new {
            status = ready ? "PASS" : "FAIL", guard = IsolatedSavePlayGuard.RequiresAccountChoice,
            environment = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), pending = SessionState.GetBool(Key + "pending", false),
            callback = SessionState.GetBool(Key + "return", false), playing = EditorApplication.isPlayingOrWillChangePlaymode }, Formatting.Indented));
        if (!ready) SessionState.SetString(Key + "status", "RETURN_FAILED");
    }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Product UI verification timed out"); if (!work.MoveNext()) Finish(); }
        catch (Exception e) { checks.Add("FAIL " + e); Finish(); }
    }

    static void Finish()
    {
        string status = errors.Count == 0 && checks.All(c => c.StartsWith("PASS ")) ? "PASS" : "FAIL";
        SessionState.SetString(Key + "status", status);
        File.WriteAllText(Path.Combine(ItemTypeIconBuilder.Output, Report), JsonConvert.SerializeObject(new { status, checks, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static IEnumerator Verify()
    {
        while (!PlayerContext.Instance || !PlayerContext.Instance.CurrentActorInventory || !WorldSessionState.IsHideout) yield return null;
        var game = Object.FindFirstObjectByType<OverburstGameUI>();
        Check(game != null, "Product UI loaded");
        var inventory = PlayerContext.Instance.CurrentActorInventory;
        ItemData[] samples;
        if (SessionState.GetBool(Key + "restore", false))
        {
            string[] ids = JsonConvert.DeserializeObject<string[]>(SessionState.GetString(Key + "ids", "[]"));
            samples = ids.Select(id => inventory.Items.FirstOrDefault(i => i != null && i.runtimeInstanceId == id)).ToArray();
            Check(samples.Length == 13 && samples.All(i => i != null), "Same 13 saved inventory instances restored");
        }
        else
        {
            samples = ItemTypeIconVerifier.Samples();
            foreach (var sample in samples) Check(inventory.AddItem(sample), "Added isolated sample " + sample.baseData.name);
            SessionState.SetString(Key + "ids", JsonConvert.SerializeObject(samples.Select(s => s.runtimeInstanceId).ToArray()));
        }
        game.inventory.SetVisible(true);
        for (int i = 0; i < 8; i++) yield return null;
        var slots = game.inventoryWindow.GetComponentsInChildren<SlotUI>(true);
        foreach (var sample in samples)
        {
            var slot = slots.FirstOrDefault(s => s.DisplayItem == sample);
            Check(slot != null, "Bound inventory sample " + sample.baseData.name);
            var type = slot.GetComponentInChildren<ItemTypeIconView>(true);
            var element = slot.GetComponentInChildren<WeaponElementIconView>(true);
            bool category = ItemTypeIconView.ResolveIndex(sample) >= 0;
            Check(type != null && type.IsVisible == category && element != null && element.IsVisible != category, "Type and element visibility " + sample.baseData.name);
            slot.SetLocked(true); Check(!type.IsVisible && !element.IsVisible, "Locked runtime badge hidden");
            slot.SetLocked(false); slot.SetDisplayItem(null); Check(!type.IsVisible && !element.IsVisible, "Cleared runtime badge hidden");
            slot.SetDisplayItem(sample);
            TooltipManager.Instance?.ShowTooltip(sample);
            var tooltip = Object.FindFirstObjectByType<OverburstTooltipHybridSkin>(FindObjectsInactive.Include);
            var tipType = tooltip.transform.Find("Approved Icon Frame/Item Type Badge").GetComponent<ItemTypeIconView>();
            Check(tipType.IsVisible == category, "Tooltip category " + sample.baseData.name);
            TooltipManager.Instance?.HideTooltip();
        }
        game.inventory.SetVisible(false); game.inventory.SetVisible(true);
        for (int i = 0; i < 4; i++) yield return null;
        Check(slots.Where(s => samples.Contains(s.DisplayItem)).Count() == 13, "Inventory re-open retains bindings");
        ScreenCapture.CaptureScreenshot(Path.Combine(ItemTypeIconBuilder.Output, SessionState.GetBool(Key + "restore", false) ? "product-play02.png" : "product-play01.png"));
        for (int i = 0; i < 4; i++) yield return null;
        if (!SessionState.GetBool(Key + "restore", false))
        {
            foreach (var sample in samples.Where(s => s.baseData is GearItemData))
            {
                int slotIndex = Enumerable.Range(0, inventory.Items.Count).First(n => inventory.GetItemAt(n) == sample);
                Check(GearEquipmentService.EquipFromInventorySlot(slotIndex), "Equip sample " + sample.baseData.name);
            }
            game.ToggleEquipment();
            for (int i = 0; i < 15; i++) yield return null;
            var gearViews = game.equipmentWindow.GetComponentsInChildren<OverburstUIItemSlotView>(true);
            Check(gearViews.Count(v => v.GetComponentInChildren<ItemTypeIconView>(true)?.IsVisible == true) >= 6, "Six equipped gear type badges shown");
            game.CloseEquipment(); game.ToggleEquipment();
            for (int i = 0; i < 4; i++) yield return null;
            Check(gearViews.Count(v => v.GetComponentInChildren<ItemTypeIconView>(true)?.IsVisible == true) >= 6, "Equipment re-open keeps type badges");
            ScreenCapture.CaptureScreenshot(Path.Combine(ItemTypeIconBuilder.Output, "product-equipment.png"));
            for (int i = 0; i < 4; i++) yield return null;
            game.CloseEquipment();
            foreach (var sample in samples.Where(s => s.baseData is GearItemData)) Check(GearEquipmentService.UnequipToInventory((int)((GearItemData)sample.baseData).kind), "Unequip sample");
            for (int i = 0; i < 12; i++) yield return null;
        }
    }

    static void Check(bool pass, string message) { checks.Add((pass ? "PASS " : "FAIL ") + message); if (!pass) throw new InvalidOperationException(message); }
    static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
}
