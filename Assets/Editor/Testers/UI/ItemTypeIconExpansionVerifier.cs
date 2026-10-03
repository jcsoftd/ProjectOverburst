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

/// <summary>실제 프리팹의 분류 확장과 재사용을 검사한다. 임시 정의는 저장 계정에 넣지 않는다.</summary>
public static class ItemTypeIconExpansionVerifier
{
    public const int Version = 1;
    public static readonly string[] Labels = { "Misc", "Quest", "Consumable", "Material", "Key", "Currency", "Recipe", "Container" };
    public static readonly int[] Expected = { 8, 9, 10, 11, 12, 13, 14, 15 };
    static readonly List<string> checks = new List<string>();
    public static string Output => ItemTypeIconBuilder.Output;

    public sealed class Fixtures : IDisposable
    {
        readonly List<BaseItemData> definitions = new List<BaseItemData>();
        public ItemData[] Items { get; }
        public Fixtures()
        {
            var random = UnityEngine.Random.state;
            try
            {
                var types = new[] { typeof(JunkItemData), typeof(QuestItemData), typeof(FlaskItemData), typeof(BaseItemData),
                    typeof(BaseItemData), typeof(CurrencyItemData), typeof(BaseItemData), typeof(BaseItemData) };
                var choices = new[] { InventoryIconCategory.Auto, InventoryIconCategory.Auto, InventoryIconCategory.Auto,
                    InventoryIconCategory.Material, InventoryIconCategory.Key, InventoryIconCategory.Auto, InventoryIconCategory.Recipe, InventoryIconCategory.Container };
                Items = new ItemData[types.Length];
                for (int i = 0; i < types.Length; i++)
                {
                    var original = types[i] == typeof(BaseItemData) ? null : AssetDatabase.FindAssets("t:" + types[i].Name, new[] { "Assets/ProjectOverburst" })
                        .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).Select(AssetDatabase.LoadAssetAtPath<BaseItemData>).FirstOrDefault(d => d && d.icon);
                    var definition = original ? Object.Instantiate(original) : (BaseItemData)ScriptableObject.CreateInstance(types[i]);
                    definitions.Add(definition); definition.hideFlags = HideFlags.HideAndDontSave;
                    definition.name = "Expansion fixture " + Labels[i]; definition.itemName = new[] { "잡동사니", "퀘스트 아이템", "장착형 물약", "제작 재료", "열쇠", "재화", "제작법", "상자" }[i];
                    definition.inventoryIconCategory = choices[i];
                    if (!definition.icon) definition.icon = AssetDatabase.LoadAssetAtPath<Sprite>(ItemTypeIconBuilder.IconRoot + "OB_Type_" + (i + 9).ToString("00") + "_" + Labels[i] + ".png");
                    Items[i] = new ItemData(definition, 20, ItemGrade.Rare);
                }
            }
            catch { Dispose(); throw; }
            finally { UnityEngine.Random.state = random; }
        }
        public void Dispose() { foreach (var definition in definitions) if (definition) Object.DestroyImmediate(definition); definitions.Clear(); }
    }

    [MenuItem("OVERBURST/UI/아이템 분류 확장 검증과 캡처")]
    public static void VerifyAndCapture()
    {
        ItemTypeIconBuilder.RequireIdle(); checks.Clear(); Directory.CreateDirectory(Output);
        string before = Scenes(); var random = UnityEngine.Random.state;
        try
        {
            Check(ItemTypeIconBuilder.Names.Length == 16, "Sixteen authored categories");
            foreach (string path in ItemTypeIconBuilder.Prefabs.Concat(new[] {
                WeaponElementIconBuilder.UiRoot + "PF_OverburstInventory_Rpg11.prefab",
                WeaponElementIconBuilder.UiRoot + "PF_OverburstEquipment_Rpg11.prefab",
                WeaponElementIconBuilder.UiRoot + "PF_OverburstStash_Rpg11.prefab" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); Check(prefab, "Prefab loaded " + path);
                foreach (var view in prefab.GetComponentsInChildren<ItemTypeIconView>(true))
                {
                    var serialized = new SerializedObject(view); var artwork = serialized.FindProperty("artwork");
                    Check(artwork.arraySize == 16, "Inherited catalog has sixteen entries " + path);
                    for (int i = 0; i < 16; i++)
                    {
                        var entry = artwork.GetArrayElementAtIndex(i); var sprite = entry.FindPropertyRelative("sprite").objectReferenceValue as Sprite;
                        Check(sprite && sprite.name.Contains("_" + ItemTypeIconBuilder.Names[i]), "Correct authored sprite " + i);
                        Rect bounds = entry.FindPropertyRelative("bounds").rectValue;
                        Check(bounds.width > 0 && bounds.height > 0 && bounds.xMin >= 0 && bounds.yMin >= 0 && bounds.xMax <= 1 && bounds.yMax <= 1, "Valid alpha bounds");
                    }
                    var rect = (RectTransform)view.transform;
                    Check(rect.anchorMin == new Vector2(1, 0) && rect.anchorMax == rect.anchorMin && rect.pivot == rect.anchorMin
                        && rect.anchoredPosition == new Vector2(-7, 7) && view.ReferenceSize == 23, "Approved size and inset retained");
                }
            }
            Check(ItemTypeIconView.ResolveIndex(null) == -1, "Null item remains hidden");
            using (var fixtures = new Fixtures())
            {
                for (int i = 0; i < 8; i++) Check(ItemTypeIconView.ResolveIndex(fixtures.Items[i]) == Expected[i], "Data route " + Labels[i]);
                Check(fixtures.Items[2].baseData is FlaskItemData, "Permanent flask included in consumable inheritance");
                Render(fixtures.Items, 1); Render(fixtures.Items, 2);
            }
            foreach (Type type in new[] { typeof(WeaponItemData), typeof(GearItemData), typeof(BagItemData), typeof(MapItemData), typeof(ElementGemItemData) })
            {
                var source = AssetDatabase.FindAssets("t:" + type.Name, new[] { "Assets/ProjectOverburst" }).Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<BaseItemData>).First(d => d);
                var clone = Object.Instantiate(source);
                try
                {
                    clone.inventoryIconCategory = InventoryIconCategory.Key;
                    int result = ItemTypeIconView.ResolveIndex(new ItemData(clone, 20, clone is ElementGemItemData gem ? gem.fixedGrade : ItemGrade.Rare));
                    Check(type == typeof(MapItemData) || type == typeof(ElementGemItemData) ? result == -1 : result >= 0 && result < 8, "Existing category priority " + type.Name);
                }
                finally { Object.DestroyImmediate(clone); }
            }
            Check(before == Scenes(), "Open scenes and dirty state preserved");
            Write("expansion-edit.json", new { status = "PASS", checks });
        }
        catch (Exception error) { Write("expansion-edit.json", new { status = "FAIL", checks, error = error.ToString() }); throw; }
        finally { UnityEngine.Random.state = random; }
    }

    public static GameObject Populate(Transform parent, ItemData[] items, Action<bool, string> check)
    {
        var root = new GameObject("Expansion native slots", typeof(RectTransform)); root.transform.SetParent(parent, false);
        var shared = AssetDatabase.LoadAssetAtPath<GameObject>(ItemTypeIconBuilder.SharedSlot);
        var shop = AssetDatabase.LoadAssetAtPath<GameObject>(ItemTypeIconBuilder.Prefabs[1]).GetComponentsInChildren<SlotUI>(true)[0].gameObject;
        for (int row = 0; row < 2; row++)
            for (int i = 0; i < items.Length; i++)
            {
                var go = Object.Instantiate(row == 0 ? shared : shop, root.transform, false);
                var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
                rect.anchoredPosition = new Vector2(-490 + i * 140, row == 0 ? 120 : -60); rect.sizeDelta = Vector2.one * 84; rect.localScale = Vector3.one;
                var slot = go.GetComponent<SlotUI>() ?? go.AddComponent<SlotUI>();
                var type = go.GetComponentInChildren<ItemTypeIconView>(true); var element = go.GetComponentInChildren<WeaponElementIconView>(true);
                slot.SetDisplayItem(items[i]);
                check(type.IsVisible && type.DisplayedSprite.name.Contains("_" + Labels[i]), "Native " + row + "/" + Labels[i]);
                check(!element.IsVisible, "No unrelated element badge");
                slot.SetLocked(true); check(!type.IsVisible, "Locked category hidden");
                slot.SetLocked(false); slot.SetDisplayItem(items[(i + 1) % items.Length]);
                check(type.DisplayedSprite.name.Contains("_" + Labels[(i + 1) % items.Length]), "Reused slot changes category");
                slot.SetDisplayItem(null); check(!type.IsVisible && type.DisplayedSprite == null, "Empty slot clears previous sprite");
                slot.SetDisplayItem(items[i]);
                root.SetActive(false); check(!type.IsVisible, "Closed UI badge hidden"); root.SetActive(true); check(type.IsVisible, "Reopened UI badge retained");
                Label(root.transform, Labels[i], new Vector2(-532 + i * 140, row == 0 ? 57 : -123), 16);
            }
        Label(root.transform, "INVENTORY / EQUIPMENT / STASH - shared 84px slots", new Vector2(-570, 225), 18);
        Label(root.transform, "SHOP / TRADE - same badge size and position", new Vector2(-570, 10), 18);
        Label(root.transform, "Type badge: 23    Visible artwork: 87%    Bottom-right inset: 7 / 7", new Vector2(-570, -193), 16);
        return root;
    }

    static void Render(ItemData[] items, int scale)
    {
        const int width = 1200, height = 500, layer = 30;
        var preview = EditorSceneManager.NewPreviewScene(); GameObject cameraGo = null, canvasGo = null; RenderTexture target = null; Texture2D pixels = null;
        var previous = RenderTexture.active;
        try
        {
            cameraGo = new GameObject("Expansion preview camera", typeof(Camera)); canvasGo = new GameObject("Expansion preview canvas", typeof(RectTransform), typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo, preview); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo, preview);
            var camera = cameraGo.GetComponent<Camera>(); camera.scene = preview; camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true; camera.orthographicSize = height * .5f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .08f, .09f); camera.cullingMask = 1 << layer;
            target = new RenderTexture(width * scale, height * scale, 24, RenderTextureFormat.ARGB32); camera.targetTexture = target;
            var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            var scaler = canvasGo.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = scale;
            Populate(canvasGo.transform, items, Check);
            var tooltip = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ItemTypeIconBuilder.Prefabs[2]), canvasGo.transform, false);
            var view = tooltip.GetComponent<OverburstUITooltipView>();
            for (int i = 0; i < items.Length; i++)
            {
                view.Present(items[i]); var badge = tooltip.transform.Find("Approved Icon Frame/Item Type Badge").GetComponent<ItemTypeIconView>();
                Check(badge.IsVisible && badge.DisplayedSprite.name.Contains("_" + Labels[i]), "Tooltip route " + Labels[i]);
            }
            tooltip.SetActive(false);
            foreach (Transform t in canvasGo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            pixels = new Texture2D(width * scale, height * scale, TextureFormat.RGBA32, false); pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Output, "expansion-comparison-" + scale + "x.png"), pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            if (cameraGo) cameraGo.GetComponent<Camera>().targetTexture = null;
            if (pixels) Object.DestroyImmediate(pixels); if (target) { target.Release(); Object.DestroyImmediate(target); }
            if (canvasGo) Object.DestroyImmediate(canvasGo); if (cameraGo) Object.DestroyImmediate(cameraGo); EditorSceneManager.ClosePreviewScene(preview);
        }
    }
    static void Label(Transform parent, string text, Vector2 position, int size)
    {
        var go = new GameObject("Expansion label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
        var label = go.GetComponent<Text>(); label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.text = text; label.fontSize = size; label.color = new Color(.88f, .84f, .76f); label.raycastTarget = false;
        var rect = label.rectTransform; rect.anchorMin = rect.anchorMax = Vector2.one * .5f; rect.pivot = new Vector2(0, 1); rect.anchoredPosition = position; rect.sizeDelta = new Vector2(1180, 30);
    }
    static string Scenes() => JsonConvert.SerializeObject(Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i => { var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, s.rootCount }; }));
    static void Check(bool pass, string message) { checks.Add((pass ? "PASS " : "FAIL ") + message); if (!pass) throw new InvalidOperationException(message); }
    public static void Write(string file, object value) { Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, file), JsonConvert.SerializeObject(value, Formatting.Indented)); }
}

[InitializeOnLoad]
public static class ItemTypeIconExpansionPlayVerifier
{
    const string Key = "Overburst.ItemTypeIconExpansionPlayVerifier.";
    static string Account => Path.GetFullPath(Path.Combine(ItemTypeIconExpansionVerifier.Output, "IsolatedAccount"));
    static IEnumerator work; static GameObject root; static int frame, checks; static double deadline;
    static readonly List<string> errors = new List<string>();
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");
    static ItemTypeIconExpansionPlayVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetBool(Key + "pending", false) && SessionState.GetBool(Key + "entered", false)) EditorApplication.delayCall += Recover;
        if (SessionState.GetBool(Key + "return", false)) EditorApplication.update += ReturnAccount;
    }
    public static string Run()
    {
        ItemTypeIconBuilder.RequireIdle();
        if (SessionState.GetBool(Key + "pending", false) || SessionState.GetBool(Key + "return", false)
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))) return "DEFERRED: account operation active";
        SessionState.SetInt(Key + "pid", System.Diagnostics.Process.GetCurrentProcess().Id); SessionState.SetBool(Key + "pending", true); SessionState.SetBool(Key + "entered", false);
        SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)); SessionState.SetString(Key + "status", "RUNNING");
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Account); }
        catch { SessionState.SetString(Key + "status", "FAIL: Play rejected"); ScheduleReturn(); throw; }
        return "STARTED";
    }
    static bool OwnAccount() => string.Equals(IsolatedSavePlayGuard.ActiveDirectory, Account, StringComparison.OrdinalIgnoreCase);
    static void Recover()
    {
        if (EditorApplication.isPlaying && OwnAccount()) { SessionState.SetString(Key + "status", "CANCELLED: reload"); EditorApplication.ExitPlaymode(); }
        else if (!EditorApplication.isPlayingOrWillChangePlaymode) ScheduleReturn();
    }
    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "pending", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode && OwnAccount())
        {
            SessionState.SetBool(Key + "entered", true); SessionState.SetBool(Key + "background", Application.runInBackground); Application.runInBackground = true;
            errors.Clear(); checks = 0; frame = -1; deadline = EditorApplication.timeSinceStartup + 90; work = Verify(); EditorApplication.update += Tick; Application.logMessageReceived += Log;
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log; (work as IDisposable)?.Dispose(); work = null;
            if (root) Object.Destroy(root); root = null;
            if (SessionState.GetBool(Key + "entered", false)) Application.runInBackground = SessionState.GetBool(Key + "background", false);
            if (Status == "RUNNING") SessionState.SetString(Key + "status", "CANCELLED: interrupted");
        }
        else if (state == PlayModeStateChange.EnteredEditMode) ScheduleReturn();
    }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate(); if (!EditorApplication.isPlaying || frame == Time.frameCount) return; frame = Time.frameCount;
        try { if (!OwnAccount() || EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Expansion Play unavailable/timed out"); if (!work.MoveNext()) Finish("PASS", null); }
        catch (Exception error) { Finish("FAIL", error.ToString()); }
    }
    static void Finish(string status, string error)
    {
        if (errors.Count > 0) status = "FAIL";
        SessionState.SetString(Key + "status", status); ItemTypeIconExpansionVerifier.Write("expansion-play.json", new { status, checks, error, errors });
        EditorApplication.update -= Tick; if (OwnAccount()) EditorApplication.ExitPlaymode();
    }
    static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    static void ScheduleReturn()
    {
        SessionState.SetBool(Key + "pending", false); SessionState.SetBool(Key + "return", true);
        SessionState.SetString(Key + "returnDeadline", (EditorApplication.timeSinceStartup + 45).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        EditorApplication.update -= ReturnAccount; EditorApplication.update += ReturnAccount;
    }
    static void ReturnAccount()
    {
        double.TryParse(SessionState.GetString(Key + "returnDeadline", "0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double limit);
        if (EditorApplication.timeSinceStartup > limit) { EditorApplication.update -= ReturnAccount; ItemTypeIconExpansionVerifier.Write("expansion-return.json", new { status = "DEFERRED", reason = "Editor/account busy; return key retained; callback removed" }); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || System.Diagnostics.Process.GetCurrentProcess().Id != SessionState.GetInt(Key + "pid", 0)) return;
        string environment = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
        if ((!string.IsNullOrEmpty(environment) && !string.Equals(environment, Account, StringComparison.OrdinalIgnoreCase)) || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || (!string.IsNullOrEmpty(prepared) && !string.Equals(prepared, Account, StringComparison.OrdinalIgnoreCase))) return;
        IsolatedSavePlayGuard.UseRealAccount();
        bool ready = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")) && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""))
            && SessionState.GetString(Key + "startScene", "") == AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
        EditorApplication.update -= ReturnAccount;
        if (ready)
        {
            foreach (string suffix in new[] { "pending", "return", "entered", "background" }) SessionState.EraseBool(Key + suffix);
            foreach (string suffix in new[] { "startScene", "returnDeadline" }) SessionState.EraseString(Key + suffix); SessionState.EraseInt(Key + "pid");
        }
        ItemTypeIconExpansionVerifier.Write("expansion-return.json", new { status = ready ? "PASS" : "FAIL", guard = IsolatedSavePlayGuard.RequiresAccountChoice, environment = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), pending = SessionState.GetBool(Key + "pending", false), callback = SessionState.GetBool(Key + "return", false) });
    }
    static void Check(bool pass, string message) { checks++; if (!pass) throw new InvalidOperationException(message); }
    static IEnumerator Verify()
    {
        while (!PlayerContext.Instance || !PlayerContext.Instance.CurrentActorInventory || !WorldSessionState.IsHideout) yield return null;
        var inventory = PlayerContext.Instance.CurrentActorInventory; string[] before = inventory.Items.Select(i => i?.runtimeInstanceId).ToArray();
        using (var fixtures = new ItemTypeIconExpansionVerifier.Fixtures())
        {
            root = new GameObject("Expansion native Play fixture", typeof(RectTransform), typeof(Canvas)); root.hideFlags = HideFlags.HideAndDontSave;
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            try
            {
                var native = ItemTypeIconExpansionVerifier.Populate(root.transform, fixtures.Items, Check);
                native.transform.localScale = Vector3.one * .8f;
                for (int n = 0; n < 3; n++) yield return null;
                var tooltip = Object.FindFirstObjectByType<OverburstTooltipHybridSkin>(FindObjectsInactive.Include); Check(tooltip, "Actual game tooltip loaded");
                foreach (var item in fixtures.Items)
                {
                    TooltipManager.Instance.ShowTooltip(item); yield return null;
                    var badge = tooltip.transform.Find("Approved Icon Frame/Item Type Badge").GetComponent<ItemTypeIconView>();
                    Check(badge.IsVisible && badge.DisplayedSprite.name.Contains("_" + ItemTypeIconExpansionVerifier.Labels[Array.IndexOf(fixtures.Items, item)]), "Actual game tooltip expansion category");
                    TooltipManager.Instance.HideTooltip();
                }
                var game = Object.FindFirstObjectByType<OverburstGameUI>(); game.inventory.SetVisible(true); yield return null; game.inventory.SetVisible(false);
                game.inventory.SetVisible(true); yield return null; game.inventory.SetVisible(false);
                Check(before.SequenceEqual(inventory.Items.Select(i => i?.runtimeInstanceId)), "Temporary expansion definitions never inserted into saved inventory");
                ScreenCapture.CaptureScreenshot(Path.Combine(ItemTypeIconExpansionVerifier.Output, "expansion-product-play.png")); for (int n = 0; n < 4; n++) yield return null;
            }
            finally { TooltipManager.Instance?.HideTooltip(); if (root) Object.Destroy(root); root = null; }
        }
    }
}
