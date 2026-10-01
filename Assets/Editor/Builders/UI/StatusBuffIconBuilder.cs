using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class StatusBuffIconBuilder
{
    public const string IconRoot = "Assets/ProjectOverburst/Resources/UI/StatusBuffs";
    public const string Output = "../개인파일/코덱스산출/UI/20261001_StatusBuffIcons";
    public const string HudPath = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstHUD_Rpg11.prefab";
    public const string CardPath = "Assets/ProjectOverburst/Resources/OverburstUI/Run/CardIcons.asset";
    public static readonly string[] Prefabs = {
        HudPath, "Assets/ProjectOverburst/Resources/UI/HUD/PF_EnemyBossHud.prefab",
        "Assets/ProjectOverburst/Resources/UI/World/MonsterHpBars/PF_EnemyHpBar_Elite.prefab",
        "Assets/ProjectOverburst/Resources/UI/World/MonsterHpBars/PF_EnemyHpBar_Elite_Tier.prefab",
        "Assets/ProjectOverburst/Resources/UI/World/MonsterHpBars/PF_EnemyHpBar_Medium.prefab",
        "Assets/ProjectOverburst/Resources/UI/World/MonsterHpBars/PF_EnemyHpBar_Normal.prefab",
        "Assets/ProjectOverburst/Resources/UI/World/MonsterHpBars/PF_EnemyHpBar_Small.prefab"
    };

    [MenuItem("OVERBURST/UI/상태와 버프 아이콘 연결")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Editor must be idle");
        foreach (string path in Directory.GetFiles(IconRoot, "*.png", SearchOption.AllDirectories))
        {
            string asset = path.Replace('\\', '/');
            AssetDatabase.ImportAsset(asset);
            var importer = (TextureImporter)AssetImporter.GetAtPath(asset);
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
        }
        foreach (string path in Prefabs)
        {
            Backup(path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (ElementalStatusIconStrip strip in root.GetComponentsInChildren<ElementalStatusIconStrip>(true))
                {
                    var so = new SerializedObject(strip);
                    Set(so, "fireIcon", StatusBuffIcons.Status("burning"));
                    Set(so, "iceIcon", StatusBuffIcons.Status("chill"));
                    Set(so, "electricIcon", StatusBuffIcons.Status("shock"));
                    Set(so, "darkIcon", StatusBuffIcons.Status("corrosion"));
                    Set(so, "lightIcon", StatusBuffIcons.Status("radiance"));
                    Set(so, "freezeIcon", StatusBuffIcons.Status("freeze"));
                    Set(so, "stunIcon", StatusBuffIcons.Status("stun"));
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                foreach (EnemyTargetStatusRow row in root.GetComponentsInChildren<EnemyTargetStatusRow>(true)) ExtendStatusRow(row);
                if (path == HudPath) ConfigureBuffBar(root.GetComponentInChildren<BuffBarUI>(true));
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Backup(CardPath);
        var cards = AssetDatabase.LoadAssetAtPath<RunCardIconSet>(CardPath);
        foreach (RunCardIconSet.Entry entry in cards.Entries)
            if (entry.Key.StartsWith("buff_") && Enum.TryParse(entry.Key.Substring(5), out MapBuffKind kind))
                entry.Icon = StatusBuffIcons.Map(kind);
        EditorUtility.SetDirty(cards);
        AssetDatabase.SaveAssetIfDirty(cards);
        File.WriteAllText(Path.Combine(Output, "builder-complete.txt"), "23 sprites, 7 prefabs and 8 buff-card entries connected. Scenes were not saved.");
    }

    static void ExtendStatusRow(EnemyTargetStatusRow row)
    {
        var so = new SerializedObject(row);
        SerializedProperty cells = so.FindProperty("cells");
        if (cells.arraySize >= 6) return;
        var last = (RectTransform)cells.GetArrayElementAtIndex(cells.arraySize - 1).FindPropertyRelative("root").objectReferenceValue;
        var cell = Object.Instantiate(last.gameObject, last.parent, false);
        cell.name = "Cell 5";
        var rect = (RectTransform)cell.transform;
        rect.anchoredPosition = new Vector2(5 * so.FindProperty("cellSpacing").floatValue, 0f);
        cells.arraySize = 6;
        SerializedProperty added = cells.GetArrayElementAtIndex(5);
        added.FindPropertyRelative("root").objectReferenceValue = rect;
        added.FindPropertyRelative("icon").objectReferenceValue = cell.transform.Find("Icon").GetComponent<Image>();
        added.FindPropertyRelative("sweep").objectReferenceValue = cell.transform.Find("Sweep").GetComponent<Image>();
        added.FindPropertyRelative("stack").objectReferenceValue = cell.transform.Find("Stack").GetComponent<TMP_Text>();
        so.ApplyModifiedPropertiesWithoutUndo();
        var parent = (RectTransform)last.parent;
        parent.sizeDelta = new Vector2(6 * so.FindProperty("cellSpacing").floatValue, parent.sizeDelta.y);
    }

    static void ConfigureBuffBar(BuffBarUI bar)
    {
        if (bar == null) throw new InvalidOperationException("Buff bar missing");
        var root = (RectTransform)bar.transform;
        root.sizeDelta = new Vector2(400f, 72f);
        foreach (string legacy in new[] { "HealingBuff_Regen", "Debuff_Slow" }) root.Find(legacy)?.gameObject.SetActive(false);
        var template = root.Find("BuffIconSlot_01");
        var more = root.Find("MoreIndicator").GetComponent<TextMeshProUGUI>();
        var so = new SerializedObject(bar);
        SerializedProperty slots = so.FindProperty("slots");
        slots.arraySize = BuffBarUI.SlotCount;
        for (int i = 0; i < slots.arraySize; i++)
        {
            string name = "BuffIconSlot_" + (i + 1).ToString("00");
            Transform slot = root.Find(name);
            if (slot == null) { slot = Object.Instantiate(template.gameObject, root, false).transform; slot.name = name; }
            var rect = (RectTransform)slot;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2((i < BuffBarUI.TimedSlotCount ? i : i - BuffBarUI.TimedSlotCount) * 34f, i < BuffBarUI.TimedSlotCount ? 0f : -36f);
            rect.sizeDelta = Vector2.one * 30f;
            slot.gameObject.SetActive(true);
            foreach (Image image in slot.GetComponentsInChildren<Image>(true))
            { image.raycastTarget = false; image.preserveAspect = true; image.color = Color.white; }
            Transform number = slot.Find("Value");
            if (number == null) { number = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI)).transform; number.SetParent(slot, false); }
            var text = number.GetComponent<TextMeshProUGUI>();
            text.font = more.font;
            text.fontSize = 12f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.BottomRight;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = string.Empty;
            var labelRect = (RectTransform)number;
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(0f, -2f); labelRect.offsetMax = new Vector2(3f, 0f);
            var component = slot.GetComponent<BuffIconSlotUI>();
            var slotSo = new SerializedObject(component);
            Set(slotSo, "valueText", text);
            slotSo.ApplyModifiedPropertiesWithoutUndo();
            slots.GetArrayElementAtIndex(i).objectReferenceValue = component;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        var moreRect = (RectTransform)more.transform;
        moreRect.anchorMin = moreRect.anchorMax = moreRect.pivot = new Vector2(0f, 1f);
        moreRect.anchoredPosition = new Vector2(310f, -4f);
    }

    static void Set(SerializedObject so, string field, Object value) => so.FindProperty(field).objectReferenceValue = value;
    static void Backup(string path)
    {
        string target = Path.Combine(Output, "Before", path);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        if (!File.Exists(target)) File.Copy(path, target);
        if (File.Exists(path + ".meta") && !File.Exists(target + ".meta")) File.Copy(path + ".meta", target + ".meta");
    }
}
