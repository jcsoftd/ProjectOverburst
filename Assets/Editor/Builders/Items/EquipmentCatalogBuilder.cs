using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;

// Local authoring tool. The JSON and source PNGs stay outside the Unity project.
public static class EquipmentCatalogBuilder
{
    [Serializable] private sealed class Manifest { public Entry[] entries; }
    [Serializable] private sealed class Entry
    {
        public string setId, setTitle, story, slot, itemName, sourceFile, sha256;
        public int minLevel, maxLevel, bytes;
        public bool existing;
    }

    private const string GearFolder = "Assets/ProjectOverburst/Resources/Items/Gear";
    private const string IconFolder = "Assets/ProjectOverburst/Resources/Items/GearIcons";
    private const string RegistryPath = "Assets/ProjectOverburst/Resources/Persistence/AccountContentRegistry.asset";
    private static string CatalogRoot => Path.GetFullPath(Path.Combine(Application.dataPath,
        "../../개인파일/코덱스산출/Items/20260928_EquipmentEncyclopedia"));

    [MenuItem("OVERBURST/Items/Build Equipment Catalog")]
    public static void BuildAll() => BuildRange(0, 20);

    public static void BuildRange(int firstSet, int setCount)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Equipment catalog authoring requires Edit mode.");
        if (firstSet < 0 || setCount < 1 || firstSet + setCount > 20)
            throw new ArgumentOutOfRangeException(nameof(firstSet));

        Entry[] entries = ReadManifest();
        var created = new List<GearItemData>();
        for (int i = firstSet * 7; i < (firstSet + setCount) * 7; i++)
        {
            Entry entry = entries[i];
            ValidateSource(entry);
            GearItemData data = BuildEntry(entry);
            if (!entry.existing) created.Add(data);
        }
        Register(created);
        AssetDatabase.SaveAssets();
        Debug.Log($"EQUIPMENT_CATALOG_BATCH_PASS: sets {firstSet}..{firstSet + setCount - 1}, items {setCount * 7}");
    }

    public static void VerifyAll()
    {
        Entry[] entries = ReadManifest();
        AccountContentRegistry registry = AssetDatabase.LoadAssetAtPath<AccountContentRegistry>(RegistryPath);
        if (registry == null) throw new InvalidOperationException("Content registry is missing.");
        var assets = new HashSet<GearItemData>();
        foreach (Entry entry in entries)
        {
            string assetPath = DataPath(entry);
            GearItemData data = AssetDatabase.LoadAssetAtPath<GearItemData>(assetPath);
            if (data == null || data.icon == null || data.itemName != entry.itemName ||
                data.kind != Kind(entry.slot) || data.catalogMinLevel != entry.minLevel ||
                data.catalogMaxLevel != entry.maxLevel || !assets.Add(data))
                throw new InvalidOperationException("Invalid equipment data: " + assetPath);
            if (registry.IdFor(data) != AssetDatabase.AssetPathToGUID(assetPath))
                throw new InvalidOperationException("Equipment content ID mismatch: " + assetPath);
            if (!entry.existing)
            {
                string iconPath = IconPath(entry);
                if (AssetDatabase.GetAssetPath(data.icon) != iconPath ||
                    HashFile(FullAssetPath(iconPath)) != entry.sha256)
                    throw new InvalidOperationException("Equipment icon mismatch: " + iconPath);
            }
            ItemData item = new ItemData(data, entry.minLevel, ItemGrade.Common);
            ItemSnapshot snapshot = ItemSnapshotCodec.Capture(item, registry);
            ItemData restored = ItemSnapshotCodec.Restore(snapshot, registry);
            if (restored.baseData != data || restored.itemName != entry.itemName ||
                restored.icon != data.icon || restored.level != entry.minLevel)
                throw new InvalidOperationException("Equipment save round trip failed: " + assetPath);
        }
        if (assets.Count != 140) throw new InvalidOperationException("Expected 140 distinct equipment assets.");
        for (int level = 1; level <= 100; level++)
        {
            int count = assets.Count(data => data.AppearsAtLevel(level));
            if (count != 14) throw new InvalidOperationException($"Level {level} has {count} equipment definitions, expected 14.");
        }
        Debug.Log("EQUIPMENT_CATALOG_VERIFY_PASS: 140 definitions, 133 new sprites, 100 levels with 14 choices, 140 save round trips");
    }

    private static Entry[] ReadManifest()
    {
        string path = Path.Combine(CatalogRoot, "장비_도입_매니페스트.json");
        Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
        if (manifest?.entries == null || manifest.entries.Length != 140)
            throw new InvalidOperationException("Equipment manifest must contain 140 entries.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < manifest.entries.Length; i++)
        {
            Entry entry = manifest.entries[i];
            int tier = i / 14 + 1;
            string expectedSet = "L" + tier.ToString("00") + (i % 14 >= 7 ? "B" : "");
            string expectedSlot = new[] { "Helmet", "Chest", "Gloves", "Boots", "EarringA", "EarringB", "Necklace" }[i % 7];
            if (entry.setId != expectedSet || entry.slot != expectedSlot ||
                entry.sourceFile != expectedSet + "_" + expectedSlot + ".png" ||
                entry.minLevel != (tier - 1) * 10 + 1 || entry.maxLevel != tier * 10 ||
                entry.existing != (expectedSet == "L06") ||
                string.IsNullOrWhiteSpace(entry.itemName) || !names.Add(entry.itemName) ||
                !Regex.IsMatch(entry.sha256 ?? "", "^[0-9a-f]{64}$"))
                throw new InvalidOperationException("Invalid equipment manifest entry " + i);
        }
        return manifest.entries;
    }

    private static GearItemData BuildEntry(Entry entry)
    {
        string assetPath = DataPath(entry);
        GearItemData data = AssetDatabase.LoadAssetAtPath<GearItemData>(assetPath);
        if (entry.existing)
        {
            if (data == null || data.itemName != entry.itemName || data.icon == null ||
                data.kind != Kind(entry.slot))
                throw new InvalidOperationException("Existing equipment changed: " + assetPath);
        }
        else
        {
            string iconPath = IconPath(entry);
            string source = Path.Combine(CatalogRoot, "images", entry.sourceFile);
            string destination = FullAssetPath(iconPath);
            if (!File.Exists(destination)) File.Copy(source, destination);
            if (HashFile(destination) != entry.sha256)
                throw new InvalidOperationException("Equipment destination icon differs: " + iconPath);
            AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing texture importer: " + iconPath);
            if (importer.textureType != TextureImporterType.Sprite || importer.maxTextureSize != 512 ||
                importer.mipmapEnabled || !importer.alphaIsTransparency)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 512;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
            Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            if (icon == null) throw new InvalidOperationException("Equipment sprite did not import: " + iconPath);
            if (data == null)
            {
                if (File.Exists(FullAssetPath(assetPath)))
                    throw new InvalidOperationException("A different asset owns " + assetPath);
                data = ScriptableObject.CreateInstance<GearItemData>();
                data.itemName = entry.itemName;
                data.description = entry.setTitle + ". " + entry.story;
                data.icon = icon;
                data.kind = Kind(entry.slot);
                data.color = Color.white;
                data.weight = 0f;
                data.sellPrice = 100;
                AssetDatabase.CreateAsset(data, assetPath);
            }
            else if (data.itemName != entry.itemName || data.icon != icon || data.kind != Kind(entry.slot))
                throw new InvalidOperationException("Existing catalog asset differs: " + assetPath);
        }
        data.catalogMinLevel = entry.minLevel;
        data.catalogMaxLevel = entry.maxLevel;
        EditorUtility.SetDirty(data);
        return data;
    }

    private static void Register(List<GearItemData> assets)
    {
        AccountContentRegistry registry = AssetDatabase.LoadAssetAtPath<AccountContentRegistry>(RegistryPath);
        if (registry == null) throw new InvalidOperationException("Content registry is missing.");
        var entries = new List<AccountContentEntry>(registry.Entries);
        var ids = new HashSet<string>(entries.Select(x => x.id), StringComparer.Ordinal);
        bool changed = false;
        foreach (GearItemData data in assets)
        {
            string id = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data));
            if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("Equipment has no GUID: " + data.name);
            if (entries.Any(x => x.asset == data)) continue;
            if (!ids.Add(id)) throw new InvalidOperationException("Content ID collision: " + id);
            entries.Add(new AccountContentEntry { id = id, asset = data });
            changed = true;
        }
        if (changed)
        {
            registry.SetAuthoringEntries(entries);
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssetIfDirty(registry);
        }
    }

    private static void ValidateSource(Entry entry)
    {
        string source = Path.Combine(CatalogRoot, "images", entry.sourceFile);
        if (!File.Exists(source) || new FileInfo(source).Length != entry.bytes || HashFile(source) != entry.sha256)
            throw new InvalidOperationException("Equipment source icon differs: " + source);
    }

    private static GearKind Kind(string slot) => slot.StartsWith("Earring", StringComparison.Ordinal)
        ? GearKind.Earring : (GearKind)Enum.Parse(typeof(GearKind), slot);
    private static string DataPath(Entry entry) => GearFolder + "/Gear_" +
        (entry.existing ? entry.slot : entry.setId + "_" + entry.slot) + ".asset";
    private static string IconPath(Entry entry) => IconFolder + "/Icon_Gear_" + entry.setId + "_" + entry.slot + ".png";
    private static string FullAssetPath(string path) => Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
    private static string HashFile(string path)
    {
        using (var hash = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
