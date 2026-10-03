using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class ItemTypeIconBuilder
{
    public const string Output = "../개인파일/코덱스산출/UI/20261004_TypeIconsExpansion";
    public const string IconRoot = "Assets/ProjectOverburst/Resources/UI/ItemTypes/";
    public const string SharedSlot = WeaponElementIconBuilder.UiRoot + "Slots/PF_OverburstItemSlot_Rpg11.prefab";
    public const int CatalogVersion = 2;
    public static readonly string[] Names = { "Weapon", "Helmet", "Armor", "Gloves", "Boots", "Necklace", "Earring", "Bag",
        "Misc", "Quest", "Consumable", "Material", "Key", "Currency", "Recipe", "Container" };
    public static readonly string[] Prefabs = { SharedSlot,
        WeaponElementIconBuilder.UiRoot + "PF_OverburstShopPanel_Rpg11.prefab",
        WeaponElementIconBuilder.UiRoot + "PF_OverburstTooltip_Rpg11.prefab" };

    [MenuItem("OVERBURST/UI/아이템 분류 아이콘 연결")]
    public static void Build()
    {
        RequireIdle();
        Directory.CreateDirectory(Output);
        var artwork = Names.Select((name, index) => Import(index, name)).ToArray();
        foreach (string path in Prefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (path == SharedSlot)
                    root.GetComponent<OverburstUIItemSlotView>().ConfigureTypeIcon(Badge(root.transform, artwork, WeaponElementIconBuilder.SlotBadgeSize));
                else if (path.Contains("ShopPanel"))
                    foreach (SlotUI slot in root.GetComponentsInChildren<SlotUI>(true))
                    {
                        var host = slot.transform.Find("Shared Visual") ?? slot.transform;
                        var badge = Badge(host, artwork, WeaponElementIconBuilder.ShopBadgeSize);
                        slot.GetComponent<OverburstUIItemSlotView>()?.ConfigureTypeIcon(badge);
                        var serialized = new SerializedObject(slot);
                        serialized.FindProperty("itemTypeIcon").objectReferenceValue = badge;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                else
                    Badge(root.transform.Find("Approved Icon Frame"), artwork, WeaponElementIconBuilder.SlotBadgeSize);
                if (!PrefabUtility.SaveAsPrefabAsset(root, path)) throw new InvalidOperationException("Save failed: " + path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var report = new { status = "PASS", prefabs = Prefabs, artwork = artwork.Select(a => new {
            path = AssetDatabase.GetAssetPath(a.sprite), guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a.sprite)),
            bounds = new[] { a.bounds.x, a.bounds.y, a.bounds.width, a.bounds.height }, a.opticalScale }),
            slotReference = WeaponElementIconBuilder.SlotBadgeSize, shopReference = WeaponElementIconBuilder.ShopBadgeSize,
            visibleRatio = .87f, offset = new[] { WeaponElementIconBuilder.BadgeOffset.x, WeaponElementIconBuilder.BadgeOffset.y } };
        File.WriteAllText(Path.Combine(Output, "build-results.json"), JsonConvert.SerializeObject(report, Formatting.Indented));
    }

    [MenuItem("OVERBURST/UI/아이템 분류 아이콘 확장 연결")]
    public static void ExtendArtwork()
    {
        RequireIdle();
        Directory.CreateDirectory(Output);
        var additions = Names.Skip(8).Select((name, index) => Import(index + 8, name)).ToArray();
        int changed = 0;
        foreach (string path in Prefabs)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset) throw new InvalidOperationException("Missing prefab: " + path);
            bool dirty = false;
            foreach (var view in asset.GetComponentsInChildren<ItemTypeIconView>(true))
            {
                var serialized = new SerializedObject(view);
                var array = serialized.FindProperty("artwork");
                if (array.arraySize != 8 && array.arraySize != Names.Length)
                    throw new InvalidOperationException("Unexpected artwork count: " + path + "/" + view.name);
                array.arraySize = Names.Length;
                for (int i = 0; i < additions.Length; i++)
                {
                    var value = array.GetArrayElementAtIndex(8 + i);
                    value.FindPropertyRelative("sprite").objectReferenceValue = additions[i].sprite;
                    value.FindPropertyRelative("bounds").rectValue = additions[i].bounds;
                    value.FindPropertyRelative("opticalScale").floatValue = additions[i].opticalScale;
                }
                if (serialized.ApplyModifiedPropertiesWithoutUndo()) { dirty = true; changed++; }
            }
            // 기존 위치·크기·첫8종·장비창의 다른 변경은 저장 대상으로 건드리지 않는다.
            if (dirty && !PrefabUtility.SavePrefabAsset(asset)) throw new InvalidOperationException("Save failed: " + path);
        }
        File.WriteAllText(Path.Combine(Output, "extension-build.json"), JsonConvert.SerializeObject(new {
            status = "PASS", catalogVersion = CatalogVersion, changed, prefabs = Prefabs,
            added = additions.Select(a => new { path = AssetDatabase.GetAssetPath(a.sprite), bounds = new[] { a.bounds.x, a.bounds.y, a.bounds.width, a.bounds.height }, a.opticalScale })
        }, Formatting.Indented));
    }

    static ItemTypeIconView.Artwork Import(int index, string name)
    {
        string path = IconRoot + "OB_Type_" + (index + 1).ToString("00") + "_" + name + ".png";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing icon: " + path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.maxTextureSize = 128;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        Texture2D pixels = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Rect bounds;
        try
        {
            if (!pixels.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("Invalid PNG: " + path);
            int minX = pixels.width, minY = pixels.height, maxX = -1, maxY = -1;
            Color32[] values = pixels.GetPixels32();
            for (int y = 0; y < pixels.height; y++)
                for (int x = 0; x < pixels.width; x++)
                    if (values[y * pixels.width + x].a > 16)
                    { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
            if (maxX < 0) throw new InvalidOperationException("Empty PNG: " + path);
            bounds = new Rect((float)minX / pixels.width, (float)minY / pixels.height,
                (float)(maxX - minX + 1) / pixels.width, (float)(maxY - minY + 1) / pixels.height);
        }
        finally { Object.DestroyImmediate(pixels); }
        return new ItemTypeIconView.Artwork { sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path)
            ?? throw new InvalidOperationException("Missing sprite: " + path), bounds = bounds, opticalScale = index == 6 ? 1.05f : 1f };
    }

    static ItemTypeIconView Badge(Transform parent, ItemTypeIconView.Artwork[] artwork, float size)
    {
        if (!parent) throw new InvalidOperationException("Badge parent missing");
        Transform child = parent.Find("Item Type Badge");
        if (!child) { child = new GameObject("Item Type Badge", typeof(RectTransform)).transform; child.SetParent(parent, false); }
        child.gameObject.layer = parent.gameObject.layer;
        var rect = (RectTransform)child;
        rect.anchorMin = rect.anchorMax = rect.pivot = WeaponElementIconBuilder.BadgeAnchor;
        rect.anchoredPosition = WeaponElementIconBuilder.BadgeOffset;
        rect.sizeDelta = Vector2.one * size;
        child.SetAsLastSibling();
        Transform imageChild = child.Find("Icon");
        if (!imageChild) { imageChild = new GameObject("Icon", typeof(RectTransform), typeof(Image)).transform; imageChild.SetParent(child, false); }
        imageChild.gameObject.layer = parent.gameObject.layer;
        Image image = imageChild.GetComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        var view = child.GetComponent<ItemTypeIconView>() ?? child.gameObject.AddComponent<ItemTypeIconView>();
        view.Configure(image, artwork, size);
        return view;
    }

    public static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required");
    }
}
