using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Creates a project-owned town from the licensed URP composition. Existing authored placement is never reset.</summary>
public static class MainTownBuilder
{
    public const string ScenePath = "Assets/ProjectOverburst/00_Scenes/MainScene.unity";
    public const string SourceScene = "Assets/ThirdParty/04_환경맵/Dark Fantasy Bandit Camp & Wilderness/Scenes/DF_Demo.unity";
    public const string MerchantFolder = "Assets/ProjectOverburst/03_Features/Items/Data/Merchants/MainTown";
    public const string CatalogPath = "Assets/ProjectOverburst/Resources/Persistence/Supplemental/MainTownContentRegistry.asset";
    public const string MarkerRoot = "__EDITOR_MAIN_TOWN_LOCATIONS__";
    const string NpcRoot = "Assets/ProjectOverburst/02_Shared/CharacterVisual/Prefabs/Medieval_NPC_Pack_2/";
    const string GeneratedNpcRoot = "Assets/ThirdParty/02_인간캐릭터/Medieval_NPC_Pack_2/OVERBURST_Merchants/";
    const string PromptPrefab = "Assets/ProjectOverburst/02_Shared/Interaction/Prefabs/PF_WorldInteractionKeyPrompt.prefab";
    const string PresentationFolder = "Assets/ProjectOverburst/05_Art/Environment/MainTown";

    [MenuItem("OVERBURST/World/Main Town/Create or Open MainScene")]
    public static void CreateOrOpen() => Build();

    public static string Build()
    {
        RequireIdle();
        bool sceneExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;
        if (sceneExists && HasTownServices())
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "CANCELLED";
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            FocusLocations();
            return "OPENED_EXISTING_PLACEMENT_PRESERVED";
        }
        if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Save the open scenes before creating MainScene.");
        var previous = EditorSceneManager.GetSceneManagerSetup();
        Scene scene = default;
        try
        {
            EnsureFolder(MerchantFolder);
            EnsureFolder(Path.GetDirectoryName(CatalogPath).Replace('\\', '/'));
            var merchants = CreateMerchantDefinitions();
            CreateCatalog(merchants);
            if (!sceneExists && !AssetDatabase.CopyAsset(SourceScene, ScenePath)) throw new IOException("Could not copy the URP scene.");
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            foreach (var camera in Components<Camera>(scene)) camera.gameObject.SetActive(false);
            foreach (var terrain in Components<Terrain>(scene)) terrain.gameObject.layer = LayerMask.NameToLayer("Ground");

            var services = NewObject("Main Town Services", scene).transform;
            var settings = services.gameObject.AddComponent<MainTownSceneSettings>();
            Set(settings, "cameraYaw", 346f);
            var settingsSo = new SerializedObject(settings);
            var list = settingsSo.FindProperty("merchants"); list.arraySize = merchants.Length;
            for (int i = 0; i < merchants.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = merchants[i];
            settingsSo.ApplyModifiedPropertiesWithoutUndo();
            var markers = NewObject(MarkerRoot, scene).transform;
            markers.gameObject.tag = "EditorOnly";

            var spawn = NewObject("Town Spawn", scene, services);
            Set(spawn.AddComponent<HubReturnPoint>(), "returnPointId", "Default");
            Bind("00 · 중앙 스폰", spawn.transform, markers, scene, new Vector3(154.8f, 0, 107.5f), 346, Color.cyan, 1.5f);

            var weapon = Merchant("Weapon Merchant · 대장간", merchants[0], GeneratedNpcRoot + "PF_HideoutWeaponMerchantVisual.prefab", scene, services);
            Bind("01 · 무기상인 / 모루 뒤", weapon, markers, scene, new Vector3(161.3f, 0, 97.05f), 166, new Color(1f, .55f, .2f), 1.1f);
            var armor = Merchant("Armor Merchant · 방어구점", merchants[1], NpcRoot + "Peasant_man/Prefab/Character/Peasant_man_skin1.prefab", scene, services);
            Bind("02 · 방어구상인 / 진열대 앞", armor, markers, scene, new Vector3(140.85f, 0, 112.15f), 166, new Color(.8f, .8f, 1f), 1.1f);
            var general = Merchant("General Merchant · 노점", merchants[2], GeneratedNpcRoot + "PF_HideoutProvisionMerchantVisual.prefab", scene, services);
            Bind("03 · 잡화상인 / 왼쪽 아래 노점", general, markers, scene, new Vector3(143.45f, 0, 97.7f), 166, new Color(1f, .85f, .3f), 1.1f);
            var potion = Merchant("Potion Merchant · 마법 천막", merchants[3], NpcRoot + "Peasant_woman/Prefab/SK_Peasant_woman skin2.prefab", scene, services);
            Bind("04 · 물약상인 / 오른쪽 천막", potion, markers, scene, new Vector3(168.05f, 0, 117.5f), 196, new Color(.9f, .4f, 1f), 1.1f);

            var stash = NewObject("Account Stash · 보관함", scene, services);
            var stashInteraction = stash.AddComponent<StashInteractable>();
            AddPrompt(stashInteraction, stash.transform, 1.7f);
            Instantiate("Assets/ProjectOverburst/05_Art/Environment/AnimatedChests/Hideout_Storage_Chest/Prefabs/PF_HideoutStorageChest.prefab", scene, stash.transform);
            Bind("05 · 계정 보관함", stash.transform, markers, scene, new Vector3(149.15f, 0, 107.3f), 110, new Color(.35f, 1f, .55f), 1.1f);

            var stylist = Instantiate("Assets/ProjectOverburst/03_Features/World/Prefabs/PF_HideoutAppearanceStylist.prefab", scene, services);
            stylist.name = "Appearance Stylist · 치장 천막";
            foreach (var label in stylist.GetComponentsInChildren<TextMeshPro>(true).Where(x => x.name == "Stylist Name"))
            { label.text = "치장사"; PrefabUtility.RecordPrefabInstancePropertyModifications(label); }
            Bind("06 · 치장사 / 천막 앞 통로", stylist.transform, markers, scene, new Vector3(173f, 0, 104.8f), 260, new Color(1f, .6f, .8f), 1.1f);

            var portal = NewObject("Dungeon Entrance · 지도 선택", scene, services);
            portal.AddComponent<MapDungeonPortal>();
            Set(portal.AddComponent<HubReturnPoint>(), "returnPointId", "DungeonPortal");
            portal.AddComponent<HideoutPortalVisual>().Configure(HideoutPortalKind.Dungeon);
            Bind("07 · 던전 입장 / 푸른 받침", portal.transform, markers, scene, new Vector3(143f, 0, 105.37f), 0, new Color(.2f, .7f, 1f), 1.5f);

            var house = NewObject("Private Hideout Entrance · 여관", scene, services);
            var hubPortal = house.AddComponent<HubScenePortal>();
            Set(hubPortal, "targetSceneName", PersistentSceneFlow.HideoutSceneName);
            Set(hubPortal, "promptLabel", "F : 개인 하이드아웃으로 이동");
            AddPrompt(hubPortal, house.transform, 2.2f);
            Bind("08 · 집 / 기존 하이드아웃 이동", house.transform, markers, scene, new Vector3(160.6f, 0, 127.3f), 180, new Color(.8f, 1f, .4f), 1.3f);

            var buildScenes = EditorBuildSettings.scenes.ToList();
            if (!buildScenes.Any(x => x.path == ScenePath)) buildScenes.Insert(Math.Min(1, buildScenes.Count), new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            RefinePresentation(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("MainScene save failed.");
            SaveTownAssets();
            WriteBuildResult(scene, merchants);
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            EditorSceneManager.RestoreSceneManagerSetup(previous);
        }
        return "CREATED_MAIN_TOWN";
    }

    [MenuItem("OVERBURST/World/Main Town/Select Placement Markers")]
    public static void FocusLocations()
    {
        var markers = Object.FindObjectsByType<MainTownPlacementAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(x => x.gameObject.scene.path == ScenePath).OrderBy(x => x.name).ToArray();
        Selection.objects = markers.Select(x => (Object)x.gameObject).ToArray();
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    public static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Main town authoring requires an idle Editor.");
    }

    static bool HasTownServices()
    {
        var preview = EditorSceneManager.OpenPreviewScene(ScenePath);
        try { return Components<MainTownSceneSettings>(preview).Any(); }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    [MenuItem("OVERBURST/World/Main Town/Apply Town Presentation")]
    public static void ApplyTownPresentation()
    {
        RequireIdle();
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open MainScene first.");
        RefinePresentation(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene); SaveTownAssets();
    }

    static void RefinePresentation(Scene scene)
    {
        EnsureFolder(PresentationFolder);
        string lightingPath = PresentationFolder + "/LightingData.asset";
        if (Lightmapping.lightingDataAsset != null)
        {
            if (AssetDatabase.LoadAssetAtPath<LightingDataAsset>(lightingPath) == null)
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset), lightingPath)) throw new IOException("Town lighting copy failed.");
            var lighting = AssetDatabase.LoadAssetAtPath<LightingDataAsset>(lightingPath);
            var so = new SerializedObject(lighting);
            so.FindProperty("m_Scene").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(lighting); Lightmapping.lightingDataAsset = lighting;
        }
        foreach (var merchant in Components<GeneralGoodsMerchantInteractable>(scene))
        {
            var definition = new SerializedObject(merchant).FindProperty("merchantDefinition").objectReferenceValue as MerchantDefinition;
            if (definition == null) continue;
            var visual = merchant.transform.Find("Merchant Visual");
            if (definition.Category == ShopCategory.Weapon)
            {
                var unused = new HashSet<string> { "SK_Cape", "SK_Blouse_Cut", "M_Hair2", "M_Cap", "SK_Bag3", "SK_Fur_Down", "SK_FurShape" };
                foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (unused.Contains(renderer.name)) renderer.enabled = false;
            }
            if (definition.Category == ShopCategory.Armor)
                foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var source = materials[i]; if (source == null || !source.name.Contains("Skin1_Blouse")) continue;
                        string sourceName = source.name.StartsWith("ArmorVendor_") ? source.name.Substring("ArmorVendor_".Length) : source.name;
                        string path = PresentationFolder + "/ArmorVendor_" + sourceName + ".mat";
                        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (material == null) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
                        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(.46f, .34f, .23f));
                        EditorUtility.SetDirty(material); materials[i] = material;
                    }
                    renderer.sharedMaterials = materials; PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
        }
        foreach (var stash in Components<StashInteractable>(scene))
        {
            var animator = stash.GetComponentInChildren<Animator>(); if (animator == null) continue;
            var sync = stash.GetComponent<StashChestAnimator>() ?? stash.gameObject.AddComponent<StashChestAnimator>();
            Set(sync, "stash", stash); Set(sync, "animator", animator);
        }
    }

    static void SaveTownAssets()
    {
        foreach (var path in AssetDatabase.FindAssets("", new[] { MerchantFolder, PresentationFolder })
                     .Select(AssetDatabase.GUIDToAssetPath).Append(CatalogPath).Distinct())
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
        }
    }

    static MerchantDefinition[] CreateMerchantDefinitions()
    {
        var definitions = new[]
        {
            Definition("Weapon", "무기상인", ShopCategory.Weapon, false, "대장간에서 무기 구매·조합·강화를 진행합니다."),
            Definition("Armor", "방어구상인", ShopCategory.Armor, true, "투구·갑옷·장갑·장화를 판매합니다."),
            Definition("General", "잡화상인", ShopCategory.GeneralGoods, true, "가방과 장신구를 판매합니다."),
            Definition("Potion", "물약상인", ShopCategory.Potion, true, "회복·이동 물약과 전투 플라스크를 판매합니다.")
        };
        var armor = new List<(BaseItemData, int)>();
        foreach (var level in new[] { "", "L01_", "L02_" })
            foreach (var slot in new[] { "Helmet", "Chest", "Gloves", "Boots" })
                armor.Add((Item("Assets/ProjectOverburst/Resources/Items/Gear/Gear_" + level + slot + ".asset"), 1));
        Stock(definitions[1], armor);
        var goods = new List<(BaseItemData, int)>();
        for (int i = 1; i <= 4; i++) goods.Add((Item("Assets/ProjectOverburst/03_Features/Items/Data/Items/Bags/Bag_Grade" + i + ".asset"), 1));
        foreach (var slot in new[] { "EarringA", "EarringB", "Necklace" }) goods.Add((Item("Assets/ProjectOverburst/Resources/Items/Gear/Gear_" + slot + ".asset"), 1));
        Stock(definitions[2], goods);
        var potions = new List<(BaseItemData, int)>();
        foreach (var name in new[] { "SmallHealPotion", "MediumHealPotion", "LargeHealPotion", "ExtraLargeHealPotion", "MoveSpeedPotion" })
            potions.Add((Item("Assets/ProjectOverburst/03_Features/Items/Data/Items/Consumables/" + name + ".asset"), 6));
        potions.AddRange(FlaskLootPolicy.GameplayCatalog.Select(x => ((BaseItemData)x, 1)));
        Stock(definitions[3], potions);
        return definitions;
    }

    static MerchantDefinition Definition(string id, string label, ShopCategory category, bool authored, string description)
    {
        string path = MerchantFolder + "/TownMerchant_" + id + ".asset";
        var data = AssetDatabase.LoadAssetAtPath<MerchantDefinition>(path);
        if (data != null) return data;
        data = ScriptableObject.CreateInstance<MerchantDefinition>();
        AssetDatabase.CreateAsset(data, path);
        var so = new SerializedObject(data);
        so.FindProperty("merchantName").stringValue = label;
        so.FindProperty("category").intValue = (int)category;
        so.FindProperty("description").stringValue = description;
        so.FindProperty("useAuthoredStock").boolValue = authored;
        so.FindProperty("inventoryCapacity").intValue = 24;
        so.FindProperty("merchantGold").intValue = 1500;
        var tabs = so.FindProperty("customTabs");
        if (category != ShopCategory.Weapon) { tabs.arraySize = 1; tabs.GetArrayElementAtIndex(0).intValue = (int)ShopTab.Trade; }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
        return data;
    }

    static BaseItemData Item(string path) => AssetDatabase.LoadAssetAtPath<BaseItemData>(path) ?? throw new InvalidDataException("Missing town stock item: " + path);
    static void Stock(MerchantDefinition definition, List<(BaseItemData item, int count)> items)
    {
        var so = new SerializedObject(definition); var stock = so.FindProperty("stockItems");
        if (stock.arraySize != 0) return;
        stock.arraySize = items.Count;
        for (int i = 0; i < items.Count; i++)
        {
            var entry = stock.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("itemData").objectReferenceValue = items[i].item;
            entry.FindPropertyRelative("stackCount").intValue = items[i].count;
            entry.FindPropertyRelative("grade").intValue = (int)ItemGrade.Common;
        }
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(definition);
    }

    static void CreateCatalog(MerchantDefinition[] definitions)
    {
        if (AssetDatabase.LoadAssetAtPath<AccountContentRegistry>(CatalogPath) != null) return;
        var catalog = ScriptableObject.CreateInstance<AccountContentRegistry>();
        AssetDatabase.CreateAsset(catalog, CatalogPath);
        var entries = definitions.Select(x => new AccountContentEntry { id = "merchant.main-town." + x.Category.ToString().ToLowerInvariant(), asset = x }).ToList();
        catalog.SetAuthoringEntries(entries); EditorUtility.SetDirty(catalog);
    }

    static Transform Merchant(string name, MerchantDefinition definition, string visualPath, Scene scene, Transform parent)
    {
        var root = NewObject(name, scene, parent);
        var interaction = root.AddComponent<GeneralGoodsMerchantInteractable>();
        Set(interaction, "merchantDefinition", definition); Set(interaction, "interactRadius", 3.2f);
        var visual = Instantiate(visualPath, scene, root.transform); visual.name = "Merchant Visual";
        var animator = visual.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            animator = visual.AddComponent<Animator>();
            animator.avatar = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Select(x => AssetDatabase.GetAssetPath(x.sharedMesh)).Distinct()
                .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<Avatar>().FirstOrDefault(x => x.isValid && x.isHuman);
        }
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            throw new InvalidDataException("Merchant requires a valid humanoid avatar: " + visualPath);
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(GeneratedNpcRoot + "AC_HideoutMerchantIdle.controller");
        animator.applyRootMotion = false; animator.Rebind(); animator.Update(0f);
        var renderers = visual.GetComponentsInChildren<Renderer>(true).Where(x => x.enabled && x.gameObject.activeInHierarchy).ToArray();
        if (renderers.Length == 0) throw new InvalidDataException("Invisible NPC: " + visualPath);
        var bounds = renderers[0].bounds; foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
        visual.transform.localScale *= 1.82f / Mathf.Max(.1f, bounds.size.y);
        visual.transform.localPosition = Vector3.up * (-bounds.min.y * visual.transform.localScale.y);
        var collider = root.AddComponent<CapsuleCollider>(); collider.center = new Vector3(0, .9f, 0); collider.height = 1.8f; collider.radius = .28f;
        AddPrompt(interaction, root.transform, 2.35f);
        return root.transform;
    }

    static void AddPrompt(Component component, Transform parent, float height)
    {
        var prompt = Instantiate(PromptPrefab, parent.gameObject.scene, parent);
        prompt.name = "PromptRoot"; prompt.transform.localPosition = Vector3.up * height; prompt.SetActive(false);
        Set(component, "promptRoot", prompt);
        var text = prompt.GetComponent<TextMeshPro>(); if (text != null) Set(component, "promptText", text);
    }

    static void Bind(string label, Transform placement, Transform markers, Scene scene, Vector3 position, float yaw, Color color, float radius)
    {
        position.y = Ground(scene, position);
        var marker = NewObject(label, scene, markers); marker.tag = "EditorOnly";
        marker.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        marker.AddComponent<MainTownPlacementAnchor>().Configure(label, placement, color, radius);
        EditorGUIUtility.SetIconForObject(marker, EditorGUIUtility.IconContent("sv_label_0").image as Texture2D);
    }

    static float Ground(Scene scene, Vector3 position)
    {
        var terrain = Components<Terrain>(scene).FirstOrDefault();
        return terrain != null ? terrain.SampleHeight(position) + terrain.transform.position.y : position.y;
    }
    static GameObject NewObject(string name, Scene scene, Transform parent = null)
    {
        var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene);
        if (parent != null) go.transform.SetParent(parent, false); return go;
    }
    static GameObject Instantiate(string path, Scene scene, Transform parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new FileNotFoundException("Missing town prefab: " + path);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; return go;
    }
    static IEnumerable<T> Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<T>(true));
    static void Set(Component component, string field, Object value) { var so = new SerializedObject(component); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    static void Set(Component component, string field, string value) { var so = new SerializedObject(component); so.FindProperty(field).stringValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    static void Set(Component component, string field, float value) { var so = new SerializedObject(component); so.FindProperty(field).floatValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    static void WriteBuildResult(Scene scene, MerchantDefinition[] merchants)
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/World/20261008_MainTown/Data"));
        Directory.CreateDirectory(root);
        var result = new { status = "CREATED", scene = ScenePath, guid = AssetDatabase.AssetPathToGUID(ScenePath), markers = Components<MainTownPlacementAnchor>(scene).Select(x => new { x.LocationLabel, position = x.transform.position.ToString(), target = x.Placement.name }).ToArray(), merchants = merchants.Select(x => new { x.MerchantName, category = x.Category.ToString(), authored = x.UseAuthoredStock, stockTypes = x.StockItems.Count }).ToArray() };
        File.WriteAllText(Path.Combine(root, "native-build.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
    }
}
