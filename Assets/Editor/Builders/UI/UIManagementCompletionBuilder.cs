using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Extend the existing gallery one region at a time; never rebuild its other exhibits.
public static class UIManagementCompletionBuilder
{
    public const string Output = "../개인파일/코덱스산출/UI/20261011_UIManagementGoal";
    public const string SkillGroup = "10_SkillTree • 스킬트리";
    public const string SkillLive = "Skill Tree • Connected Production Preview";
    public const string BossGroup = "11_BossHud • 보스 HUD";
    public const string BossLive = "Boss HUD • Connected Production Preview";
    public const string BossSource = "Assets/ProjectOverburst/Resources/UI/HUD/PF_EnemyBossHud.prefab";
    public const string RunGroup = "12_Run • 카드·전송";
    public const string RunSource = "Assets/ProjectOverburst/Resources/OverburstUI/Run/PF_OverburstRunUI.prefab";
    public const string WorldGroup = "13_World • 이름표·피해·상호작용";
    public const string SourceCatalog = "UI_SourceCatalog • 원본·공용부품·개발표본";

    [MenuItem("Overburst/UI/관리씬 추가 정리/5 번호·배치·원본 목록")]
    public static void FinalizeCatalog() => Edit("Catalog", scene =>
    {
        var gallery = scene.GetRootGameObjects().Single(r => r.name == OverburstUIWorkshopBuilder.GalleryName).transform;
        var title = gallery.Find("08_Title • 타이틀·캐릭터·메뉴");
        if (title) title.name = TitleCharacterManagementBuilder.GroupName;
        int number = 1, row = 0;
        foreach (var group in gallery.Cast<Transform>().OrderBy(g => g.name, StringComparer.Ordinal).ToArray())
        {
            for (int i = 0; i < group.childCount; i++)
            {
                var board = (RectTransform)group.GetChild(i);
                board.name = (number++).ToString("00") + board.name.Substring(board.name.IndexOf(' '));
                board.localScale = Vector3.one;
                board.anchoredPosition3D = new Vector3(i % 3 * 2120, -(row + i / 3) * 1320, 0);
                var heading = board.Find("Board Title");
                if (heading) heading.GetComponent<Text>().text = board.name;
                else Label(board, "Board Title", board.name, 600, 28);
            }
            row += (group.childCount + 2) / 3;
        }
        foreach (var ui in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<OverburstSkillTreeUI>(true))) RestoreSkillFonts(ui);
        var catalog = scene.GetRootGameObjects().SingleOrDefault(r => r.name == SourceCatalog);
        if (!catalog) { catalog = new GameObject(SourceCatalog); SceneManager.MoveGameObjectToScene(catalog, scene); }
        catalog.tag = "EditorOnly"; catalog.layer = 31;
        foreach (var category in UIPrefabStructureBuilder.OwnerPaths().GroupBy(UIPrefabStructureWindow.Group))
        {
            var group = catalog.transform.Find(category.Key);
            if (!group) { group = new GameObject(category.Key).transform; group.SetParent(catalog.transform, false); }
            group.gameObject.tag = "EditorOnly"; group.gameObject.layer = 31;
            foreach (string path in category)
            {
                if (group.Cast<Transform>().Any(t => t.name == path)) continue;
                var entry = new GameObject(path); entry.transform.SetParent(group, false); entry.tag = "EditorOnly"; entry.layer = 31;
            }
        }
    });

    [MenuItem("Overburst/UI/관리씬 추가 정리/1 스킬트리")]
    public static void ApplySkillTree() => Edit("SkillTree", InstallSkillTree);
    [MenuItem("Overburst/UI/관리씬 추가 정리/2 보스 HUD")]
    public static void ApplyBoss() => Edit("Boss", InstallBoss);
    [MenuItem("Overburst/UI/관리씬 추가 정리/3 런 카드·전송")]
    public static void ApplyRun() => Edit("Run", InstallRun);
    [MenuItem("Overburst/UI/관리씬 추가 정리/4 월드 표시")]
    public static void ApplyWorld() => Edit("World", InstallWorld);

    [MenuItem("Overburst/UI/관리씬 추가 정리/6 현행 상태 표본")]
    public static void NormalizeSamples() => Edit("CurrentSamples", scene =>
    {
        var gallery = scene.GetRootGameObjects().Single(r => r.name == OverburstUIWorkshopBuilder.GalleryName).transform;
        var boards = gallery.Cast<Transform>().SelectMany(g => g.Cast<Transform>()).ToArray();
        var hud = boards.Single(b => b.name.StartsWith("02 ")).GetComponentsInChildren<Image>(true)
            .Single(i => i.transform.name == "Fill" && i.transform.parent.name == "Bar (Health)");
        hud.type = Image.Type.Filled; hud.fillMethod = Image.FillMethod.Horizontal; hud.fillAmount = .28f; Record(hud);
        var hp = hud.transform.parent.Find("Text Group/Percentage Text").GetComponent<Text>();
        hp.text = "336 / 1,200"; Record(hp);
        foreach (var board in boards.Where(b => new[] { "01 ", "02 ", "11 ", "12 " }.Any(prefix => b.name.StartsWith(prefix))))
        {
            var fill = board.GetComponentsInChildren<Image>(true).Single(i => i.transform.name == "Fill" && i.transform.parent.name == "Bar (Power)");
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillAmount = .65f; Record(fill);
            var value = fill.transform.parent.Find("Text Group/Percentage Text").GetComponent<Text>();
            value.text = "65 / 100"; Record(value);
        }
        var contextBoard = boards.Single(b => b.name.StartsWith("15 "));
        var context = contextBoard.Find("PF_OverburstInventoryContext_Rpg11");
        ContextSample(context, contextBoard.GetComponent<Canvas>(), false);
        var split = contextBoard.Find("Context Split • production state");
        if (!split)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(context.gameObject);
            split = ((GameObject)PrefabUtility.InstantiatePrefab(source, contextBoard)).transform;
            split.name = "Context Split • production state";
        }
        ContextSample(split, contextBoard.GetComponent<Canvas>(), true);
        var picker = contextBoard.Find("PF_OverburstQuickKeyPicker_Rpg11");
        ((RectTransform)picker).anchoredPosition = new Vector2(600, 0); Record(picker);
        foreach (var text in picker.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            string name = text.transform.parent.name;
            if (name.StartsWith("RPG11 Key ")) { int key = int.Parse(name.Substring(10)) + 1; text.text = QuickSlotKeyLabels.Short(key) + "번"; Record(text); }
        }
        Freeze((RectTransform)contextBoard);
        var world = boards.Single(b => b.name.StartsWith("46 "));
        foreach (var label in world.GetComponentsInChildren<Text>(true))
            if (label.name.StartsWith("Source "))
            { label.alignment = TextAnchor.MiddleCenter; label.rectTransform.sizeDelta = new Vector2(600, 40); label.rectTransform.anchoredPosition = new Vector2(-400, label.rectTransform.anchoredPosition.y); Record(label); Record(label.rectTransform); }
        foreach (var source in world.Cast<Transform>().Where(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)))
        { var rect = (RectTransform)source; rect.anchoredPosition = new Vector2(250, rect.anchoredPosition.y); Record(rect); }
        var floating = boards.Single(b => b.name.StartsWith("48 "));
        foreach (var old in floating.Cast<Transform>().Where(t => t.GetComponent<DamageNumberPopup>()).ToArray()) Object.DestroyImmediate(old.gameObject);
        floating.name = "48 피해 숫자 · 현행 종류별 스타일";
        floating.Find("Board Title").GetComponent<Text>().text = floating.name;
        var preview = floating.GetComponent<OverburstUIFloatingGalleryPreview>() ?? floating.gameObject.AddComponent<OverburstUIFloatingGalleryPreview>();
        preview.productionSource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/Resources/UI/World/PF_DamageNumberPopup.prefab");
        preview.RefreshSample();
    });

    static void ContextSample(Transform root, Canvas canvas, bool split)
    {
        var host = new GameObject("Owned context controller fixture", typeof(InventoryContextMenuController));
        SceneManager.MoveGameObjectToScene(host, root.gameObject.scene);
        host.hideFlags = HideFlags.DontSave;
        var controller = host.GetComponent<InventoryContextMenuController>();
        var serialized = new SerializedObject(controller);
        Action<string, Object> assign = (name, value) => serialized.FindProperty(name).objectReferenceValue = value;
        var menu = root.Find("InventoryContextMenu"); var popup = root.Find("InventorySplitStackPopup");
        assign("blockerRoot", root); assign("menuRoot", menu); assign("splitPopupRoot", popup);
        var buttons = menu.GetComponentsInChildren<Button>(true);
        foreach (string name in new[] { "menuButtons", "menuButtonTexts" })
        {
            var array = serialized.FindProperty(name); array.arraySize = buttons.Length;
            for (int i = 0; i < buttons.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = name == "menuButtons" ? (Object)buttons[i] : buttons[i].GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        }
        assign("splitTitleText", popup.Find("Title").GetComponent<TMPro.TextMeshProUGUI>());
        assign("splitHintText", popup.Find("HintText").GetComponent<TMPro.TextMeshProUGUI>());
        assign("splitAmountInput", popup.GetComponentInChildren<TMPro.TMP_InputField>(true));
        assign("splitIncreaseButton", popup.Find("InputRow/IncreaseButton").GetComponent<Button>());
        assign("splitDecreaseButton", popup.Find("InputRow/DecreaseButton").GetComponent<Button>());
        assign("splitOkButton", popup.Find("ButtonRow/OkButton").GetComponent<Button>());
        assign("splitCancelButton", popup.Find("ButtonRow/CancelButton").GetComponent<Button>());
        serialized.ApplyModifiedPropertiesWithoutUndo();
        var slotObject = new GameObject("Owned context item fixture", typeof(SlotUI));
        SceneManager.MoveGameObjectToScene(slotObject, root.gameObject.scene); slotObject.hideFlags = HideFlags.DontSave;
        try
        {
            var inventory = Object.FindObjectsByType<InventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(i => i.KoreanFontAsset);
            if (!inventory) throw new InvalidOperationException("Product inventory font reference missing.");
            controller.Init(canvas, null, null, null, null, inventory.KoreanFontAsset);
            var slot = slotObject.GetComponent<SlotUI>(); slot.Init(null, 0, false);
            var definition = Resources.Load<Overburst.Persistence.AccountContentRegistry>(Overburst.Persistence.AccountContentRegistry.ResourcePath).Entries.Select(e => e.asset).OfType<ConsumableItemData>().First(c => !(c is FlaskItemData) && !c.IsPermanentSingleItem);
            var item = new ItemData(definition, 1, ItemGrade.Rare, 8);
            slot.SetDisplayItem(item);
            controller.OpenForSlot(slot, null);
            if (split)
            {
                // A gallery has no inventory policy service. Reuse the product's view method without committing an item action.
                var show = typeof(InventoryContextMenuController).GetMethod("TryShowObjectizedSplitPopup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (!(bool)show.Invoke(controller, new object[] { item })) throw new InvalidOperationException("Product split view missing.");
            }
            ((RectTransform)(split ? popup : menu)).anchoredPosition = new Vector2(split ? -150 : -510, 90);
            // Remove fixture references before destruction so OnDisable cannot close the saved sample.
            serialized.Update(); var property = serialized.GetIterator();
            while (property.Next(true)) if (property.propertyType == SerializedPropertyType.ObjectReference && property.name != "m_Script") property.objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        finally { Object.DestroyImmediate(host); Object.DestroyImmediate(slotObject); }
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        { Record(t.gameObject); foreach (var component in t.GetComponents<Component>()) if (component) Record(component); }
    }

    public static void InstallWorld(Scene scene)
    {
        var gallery = scene.GetRootGameObjects().Single(r => r.name == OverburstUIWorkshopBuilder.GalleryName).transform;
        if (gallery.Find(WorldGroup)) throw new InvalidOperationException("World exhibits already exist.");
        var group = NewGroup(gallery, WorldGroup); float bottom = Bottom(gallery);
        var board = Board(group, "46 월드 체력바 · 정식 5종", 0, bottom);
        string folder = "Assets/ProjectOverburst/Resources/UI/World/MonsterHpBars/";
        string[] bars = { "Small", "Medium", "Normal", "Elite", "Elite_Tier" };
        for (int i = 0; i < bars.Length; i++)
        {
            var go = Source(folder + "PF_EnemyHpBar_" + bars[i] + ".prefab", board);
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
            rect.anchoredPosition = new Vector2(120, 250 - i * 125); rect.localScale = Vector3.one;
            foreach (var view in go.GetComponentsInChildren<OverburstEnemyHealthBarView>(true)) view.Present("전시 " + bars[i], 20, .65f);
            foreach (var cg in go.GetComponentsInChildren<CanvasGroup>(true)) cg.alpha = 1;
            foreach (var image in go.GetComponentsInChildren<Image>(true).Where(im => im.type == Image.Type.Filled)) image.fillAmount = image.name.Contains("Trail") ? .8f : .65f;
            Label(board, "Source " + i, bars[i] + " · 원본 크기", 250 - i * 125, 22);
        }
        Freeze(board);
        board = Board(group, "47 드롭 이름표 · 등급과 상태", 1, bottom);
        for (int i = 0; i < 7; i++)
        {
            var row = Source("Assets/ProjectOverburst/Resources/UI/World/PF_WorldItemNameplateRow.prefab", board).GetComponent<WorldItemNameplateRowView>();
            row.Show(new WorldItemNameplateLayoutCandidate(null, "서광 수호 투구 · " + ItemTooltipFormatter.GetGradeName((ItemGrade)i), Vector2.zero, new Vector2(320, 30), 0, i + 1, i, (ItemGrade)i, i == 2, i == 3, false, true, i == 4), new Vector2(0, 240 - i * 70), false);
            row.enabled = false;
        }
        Freeze(board);
        board = Board(group, "48 피해 숫자 · 원본 글꼴", 2, bottom);
        for (int i = 0; i < 4; i++)
        {
            var go = Source("Assets/ProjectOverburst/Resources/UI/World/PF_DamageNumberPopup.prefab", board);
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = Vector2.one * .5f; rect.anchoredPosition = new Vector2(0, 150 - i * 100);
            var text = go.GetComponent<TMPro.TMP_Text>(); text.text = new[] { "123", "246", "완벽 방어", "증발" }[i];
            text.color = i == 1 ? DamageNumberPopup.CriticalAttackColor : Color.white;
        }
        Freeze(board);
        board = Board(group, "49 NPC 이름 · 상호작용 키", 3, bottom);
        var label = Source("Assets/ProjectOverburst/02_Shared/Interaction/Prefabs/PF_NpcNameLabel.prefab", board);
        label.transform.localScale = Vector3.one * 90; ((RectTransform)label.transform).anchoredPosition = new Vector2(0, 40); label.GetComponent<TMPro.TMP_Text>().text = "치장사 · 상인";
        var prompt = Source("Assets/ProjectOverburst/02_Shared/Interaction/Prefabs/PF_WorldInteractionKeyPrompt.prefab", board);
        var key = (RectTransform)prompt.transform.Find("Prompt Canvas/Key Root"); key.anchorMin = key.anchorMax = Vector2.one * .5f; key.anchoredPosition = new Vector2(0, -100);
        Freeze(board);
        board = Board(group, "50 동굴 포탈 · 선택", 4, bottom);
        var cave = Source("Assets/ProjectOverburst/Resources/World/Caves/PF_CavePortalPanel.prefab", board).GetComponent<Overburst.Caves.CavePortalPanel>();
        var caveRoot = (RectTransform)cave.transform; caveRoot.anchorMin = Vector2.zero; caveRoot.anchorMax = Vector2.one; caveRoot.offsetMin = caveRoot.offsetMax = Vector2.zero;
        cave.countLabel.text = "10개"; cave.status.text = "입장할 때 새로운 동굴을 생성합니다.\n입구의 포탈에서 마을로 돌아올 수 있습니다.";
        Freeze(board);
    }
    static GameObject Source(string path, Transform parent)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!asset) throw new InvalidOperationException("Source missing: " + path);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent); go.SetActive(true);
        foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(behaviour is Graphic) && !(behaviour is Selectable) && !(behaviour is UnityEngine.UI.Mask) && !(behaviour is RectMask2D) && !(behaviour is LayoutGroup) && !(behaviour is ContentSizeFitter)) behaviour.enabled = false;
        return go;
    }

    public static void InstallRun(Scene scene)
    {
        var gallery = scene.GetRootGameObjects().Single(r => r.name == OverburstUIWorkshopBuilder.GalleryName).transform;
        if (gallery.Find(RunGroup)) throw new InvalidOperationException("Run exhibits already exist.");
        var group = NewGroup(gallery, RunGroup); float bottom = Bottom(gallery);
        for (int i = 0; i < 3; i++)
        {
            var board = Board(group, new[] { "43 런 카드 · 공개", "44 런 카드 · 뒷면", "45 장비 전송 · 선택" }[i], i, bottom);
            var preview = board.gameObject.AddComponent<OverburstUIRunGalleryPreview>();
            preview.productionSource = AssetDatabase.LoadAssetAtPath<GameObject>(RunSource);
            preview.sample = (OverburstUIRunGalleryPreview.Sample)i;
            preview.RefreshSample(); Freeze(board);
        }
    }

    public static void InstallBoss(Scene scene)
    {
        var gallery = scene.GetRootGameObjects().Single(r => r.name == OverburstUIWorkshopBuilder.GalleryName).transform;
        var workshop = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<OverburstUIWorkshop>(true)).Single();
        if (gallery.Find(BossGroup) || workshop.transform.Find(BossLive)) throw new InvalidOperationException("Boss exhibits already exist.");
        var group = NewGroup(gallery, BossGroup); float bottom = Bottom(gallery);
        for (int i = 0; i < 3; i++)
        {
            var board = Board(group, new[] { "40 보스 HUD · 1단계", "41 보스 HUD · 2단계", "42 보스 HUD · 3단계" }[i], i, bottom);
            var preview = Boss(board); preview.galleryOnly = true; preview.phase = i; preview.Show(); Freeze(board);
        }
        var live = Boss(workshop.transform); live.name = BossLive; live.Hide();
        var serialized = new SerializedObject(workshop); serialized.FindProperty("bossPreview").objectReferenceValue = live; serialized.ApplyModifiedPropertiesWithoutUndo();
    }
    static OverburstUIBossPreview Boss(Transform parent)
    {
        var holder = Rect("Boss HUD Preview", parent, Vector2.zero, new Vector2(1920, 1080));
        holder.anchorMin = Vector2.zero; holder.anchorMax = Vector2.one; holder.offsetMin = holder.offsetMax = Vector2.zero;
        var preview = holder.gameObject.AddComponent<OverburstUIBossPreview>();
        preview.view = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BossSource), holder)).GetComponent<EnemyBossHudView>();
        preview.view.enabled = false;
        var rect = (RectTransform)preview.view.transform;
        rect.pivot = new Vector2(.5f, .5f); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one;
        var canvas = preview.view.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.overrideSorting = false;
        var panel = (RectTransform)rect.Find("BossPanel"); panel.anchoredPosition = new Vector2(0, -150);
        Record(rect); Record(panel); Record(canvas); Record(preview.view); return preview;
    }

    public static void InstallSkillTree(Scene scene)
    {
        var gallery = scene.GetRootGameObjects().Single(r => r.name == OverburstUIWorkshopBuilder.GalleryName).transform;
        var workshop = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<OverburstUIWorkshop>(true)).Single();
        if (gallery.Find(SkillGroup) || workshop.transform.Find(SkillLive))
            throw new InvalidOperationException("Skill-tree exhibits already exist; verify instead of replacing them.");
        var group = NewGroup(gallery, SkillGroup);
        float bottom = Bottom(gallery);
        for (int i = 0; i < 3; i++)
        {
            var board = Board(group, new[] { "37 스킬트리 · 기본", "38 스킬트리 · 강화 선택", "39 스킬트리 · 포인트 부족" }[i], i, bottom);
            var ui = SkillTree(board);
            ui.managementPoints = i == 2 ? 0 : 20;
            ui.managementAllocation = i == 1 ? new[] { "S_W1", "S_H1", "S_D1", "S_Q1" } : Array.Empty<string>();
            ui.Open();
            ui.Plan.Load(ui.managementPoints, ui.managementAllocation);
            ui.Refresh(); ui.SelectNode(i == 1 ? "S_W2" : "S_W1", true);
            if (i == 2) ui.ShowTooltip("S_W1");
            ui.enabled = false;
            RestoreSkillFonts(ui);
            Freeze(board);
        }
        var live = SkillTree(workshop.transform, false);
        live.name = SkillLive; live.Close();
        var serialized = new SerializedObject(workshop);
        serialized.FindProperty("skillTreePreview").objectReferenceValue = live;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static OverburstSkillTreeUI SkillTree(Transform parent, bool initialize = true)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(OverburstSkillTreeBuilder.PrefabPath);
        if (!source) throw new InvalidOperationException("Production skill-tree source missing.");
        var ui = ((GameObject)PrefabUtility.InstantiatePrefab(source, parent)).GetComponent<OverburstSkillTreeUI>();
        ui.managementPreview = true;
        if (initialize) ui.Initialize();
        ui.UseExternalEntry(true);
        Record(ui);
        return ui;
    }
    public static void RestoreSkillFonts(OverburstSkillTreeUI ui)
    {
        foreach (var text in ui.GetComponentsInChildren<Text>(true))
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(text);
            if (source) { text.fontSize = source.fontSize; Record(text); }
        }
    }

    static void Edit(string region, Action<Scene> install)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        var old = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(OverburstUIWorkshopBuilder.ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (!opened && scene.isDirty) throw new InvalidOperationException("Preserve unsaved management-scene changes first.");
        string backup = Path.GetFullPath(Output + "/Recovery/" + region + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        foreach (string path in new[] { OverburstUIWorkshopBuilder.ScenePath, OverburstUIWorkshopBuilder.ScenePath + ".meta" })
            File.Copy(path, Path.Combine(backup, Path.GetFileName(path)), false);
        if (opened) scene = EditorSceneManager.OpenScene(OverburstUIWorkshopBuilder.ScenePath, OpenSceneMode.Additive);
        try
        {
            install(scene); Canvas.ForceUpdateCanvases();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Management-scene save failed.");
        }
        finally
        {
            if (old.IsValid() && old.isLoaded) SceneManager.SetActiveScene(old);
            if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    static Transform NewGroup(Transform gallery, string name)
    {
        var group = new GameObject(name); group.tag = "EditorOnly";
        SceneManager.MoveGameObjectToScene(group, gallery.gameObject.scene);
        group.transform.SetParent(gallery, false); return group.transform;
    }
    static float Bottom(Transform gallery) => gallery.GetComponentsInChildren<Canvas>(true)
        .Where(c => c.transform.parent && c.transform.parent.parent == gallery)
        .Select(c => c.transform.localPosition.y).DefaultIfEmpty(0).Min() - 1320;

    static RectTransform Board(Transform group, string name, int index, float bottom)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        var board = (RectTransform)go.transform; board.SetParent(group, false);
        board.sizeDelta = new Vector2(1920, 1080); board.localScale = Vector3.one;
        board.anchoredPosition = new Vector2(index % 3 * 2120, bottom - index / 3 * 1320);
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var backdrop = Rect("Backdrop", board, Vector2.zero, new Vector2(1920, 1080)).gameObject.AddComponent<Image>();
        backdrop.color = new Color(.055f, .048f, .043f); backdrop.raycastTarget = false;
        Label(board, "Board Title", name, 600, 28);
        Label(board, "Board Note", "1920 × 1080 · 정식 원본 · 표본 상태만 override · 게임 저장 없음", 562, 16);
        return board;
    }
    static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false); rect.sizeDelta = size; rect.anchoredPosition = position;
        return rect;
    }
    static void Label(Transform board, string name, string value, float y, int size)
    {
        var text = Rect(name, board, new Vector2(0, y), new Vector2(1920, 40)).gameObject.AddComponent<Text>();
        text.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");
        text.text = value; text.fontSize = size; text.color = new Color32(224, 189, 122, 255);
        text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
    }
    static void Record(Object target)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
    static void Freeze(RectTransform board)
    {
        foreach (var canvas in board.GetComponentsInChildren<Canvas>(true)) { canvas.renderMode = RenderMode.WorldSpace; canvas.overrideSorting = false; }
        foreach (var rect in board.GetComponentsInChildren<RectTransform>(true).Where(r => r.GetComponent<Canvas>() && r != board))
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; }
        foreach (var raycaster in board.GetComponentsInChildren<GraphicRaycaster>(true)) raycaster.enabled = false;
        foreach (var t in board.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.layer = 31; t.gameObject.tag = "EditorOnly"; Record(t.gameObject);
            foreach (var component in t.GetComponents<Component>()) if (component) Record(component);
        }
    }
}
