using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class WeaponElementIconBuilder
{
    public const string UiRoot = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/";
    public const string IconRoot = "Assets/ProjectOverburst/Resources/UI/WeaponElements/";
    public const string Output = "../개인파일/코덱스산출/UI/20261001_WeaponElementIcons";
    const string Circle = "Assets/ThirdParty/RPG and MMO UI 11/Textures/HUD/Unit Frames/Unit Frame/UnitFrame_Level_Frame.png";
    static readonly string[] Elements = { "Fire", "Ice", "Electric", "Dark", "Light" };

    [MenuItem("OVERBURST/UI/무기 속성 아이콘 연결")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Editor must be idle");
        Sprite[] sprites = Elements.Select(ImportIcon).ToArray();
        Sprite circle = AssetDatabase.LoadAssetAtPath<Sprite>(Circle);
        if (circle == null) throw new InvalidOperationException("HUD circle frame is missing");
        string[] paths = {
            UiRoot + "Slots/PF_OverburstItemSlot_Rpg11.prefab",
            UiRoot + "PF_OverburstShopPanel_Rpg11.prefab",
            UiRoot + "PF_OverburstTooltip_Rpg11.prefab",
            UiRoot + "PF_OverburstHUD_Rpg11.prefab"
        };
        foreach (string path in paths)
        {
            Backup(path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (path.Contains("ItemSlot"))
                {
                    var badge = Badge(root.transform, sprites, circle, 26f);
                    root.GetComponent<OverburstUIItemSlotView>().ConfigureElementIcon(badge);
                }
                else if (path.Contains("ShopPanel"))
                {
                    foreach (SlotUI slot in root.GetComponentsInChildren<SlotUI>(true))
                    {
                        var badge = Badge(slot.transform, sprites, circle, 24f);
                        var serialized = new SerializedObject(slot);
                        serialized.FindProperty("weaponElementIcon").objectReferenceValue = badge;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                else if (path.Contains("Tooltip"))
                    Badge(root.transform.Find("Approved Icon Frame"), sprites, circle, 26f);
                else
                {
                    Transform role = root.transform.Find("Action Bar Unit Frame/Role Frame");
                    foreach (string old in new[] { "Caster", "Melee", "Defense" })
                        role.Find(old)?.gameObject.SetActive(false);
                    Image image = ImageChild(role, "Weapon Element Icon");
                    RectTransform rect = image.rectTransform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = Vector2.one * 52f;
                    var view = image.GetComponent<WeaponElementIconView>() ?? image.gameObject.AddComponent<WeaponElementIconView>();
                    Configure(view, image, null, sprites);
                    var presenter = role.GetComponent<WeaponElementHudIcon>() ?? role.gameObject.AddComponent<WeaponElementHudIcon>();
                    presenter.Configure(view);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "asset-results.json"), JsonConvert.SerializeObject(
            new { status = "PASS", icons = sprites.Select(AssetDatabase.GetAssetPath), prefabs = paths,
                missingScripts = paths.Sum(p => AssetDatabase.LoadAssetAtPath<GameObject>(p)
                    .GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))) }, Formatting.Indented));
    }

    static Sprite ImportIcon(string element)
    {
        string path = IconRoot + "Icon_WeaponElement_" + element + ".png";
        AssetDatabase.ImportAsset(path);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.maxTextureSize = 256;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path) ?? throw new InvalidOperationException("Sprite missing: " + path);
    }

    static WeaponElementIconView Badge(Transform parent, Sprite[] sprites, Sprite circle, float size)
    {
        Image frame = ImageChild(parent, "Weapon Element Badge");
        frame.sprite = circle;
        frame.color = Color.white;
        var rect = frame.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-5f, -5f);
        rect.sizeDelta = Vector2.one * size;
        frame.transform.SetAsLastSibling();
        Image icon = ImageChild(frame.transform, "Icon");
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = icon.rectTransform.pivot = Vector2.one * .5f;
        icon.rectTransform.anchoredPosition = Vector2.zero;
        icon.rectTransform.sizeDelta = Vector2.one * size * .82f;
        var view = frame.GetComponent<WeaponElementIconView>() ?? frame.gameObject.AddComponent<WeaponElementIconView>();
        Configure(view, icon, frame.gameObject, sprites);
        return view;
    }

    static Image ImageChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            child = new GameObject(name, typeof(RectTransform), typeof(Image)).transform;
            child.SetParent(parent, false);
        }
        var image = child.GetComponent<Image>() ?? child.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }

    static void Configure(WeaponElementIconView view, Image icon, GameObject surface, Sprite[] sprites)
        => view.Configure(icon, surface, sprites[0], sprites[1], sprites[2], sprites[3], sprites[4]);

    static void Backup(string path)
    {
        string target = Path.Combine(Output, "BeforeAuto", path);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        if (!File.Exists(target)) File.Copy(path, target);
        if (File.Exists(path + ".meta") && !File.Exists(target + ".meta")) File.Copy(path + ".meta", target + ".meta");
    }
}
