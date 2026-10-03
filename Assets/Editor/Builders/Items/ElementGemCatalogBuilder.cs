using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Overburst.Persistence;

public static class ElementGemCatalogBuilder
{
    public const string DataRoot = "Assets/ProjectOverburst/Resources/Items/ElementGems";
    public const string IconRoot = "Assets/ProjectOverburst/05_Art/UI/Icons/ElementGems";
    public const string PickupRoot = "Assets/ProjectOverburst/Resources/Items/WorldPickups/ElementGems";
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Editor must be idle.");
        string source = Path.GetFullPath("../개인파일/코덱스산출/Design/20261002_ElementGemIcons/V2_GradeGrowth_32");
        var icons = (JArray)JObject.Parse(File.ReadAllText(Path.Combine(source, "manifest.json")))["icons"];
        AccountContentRegistryBuilder.EnsureFolder(DataRoot);
        AccountContentRegistryBuilder.EnsureFolder(IconRoot);
        AccountContentRegistryBuilder.EnsureFolder(PickupRoot);
        var wrappers = new Dictionary<WeaponElement, GameObject>();
        foreach (var element in new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light })
        {
            string path = PickupRoot + "/PF_ElementGem_" + element + "_Pickup.prefab";
            GameObject wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (wrapper == null)
            {
                var root = new GameObject("ElementGem_" + element + "_Pickup");
                try
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/05_Art/Items/ElementGems/" + element + "/Prefabs/PF_ElementGem_" + element + "_Model.prefab");
                    if (model == null) throw new InvalidOperationException("Missing gem model " + element);
                    var child = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    child.transform.SetParent(root.transform, false);
                    var renderers = child.GetComponentsInChildren<Renderer>();
                    var bounds = new Bounds(child.transform.position, Vector3.zero);
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                    if (size <= 0) throw new InvalidOperationException("Empty model bounds.");
                    child.transform.localScale *= .55f / size;
                    root.AddComponent<WorldItemPickup>();
                    wrapper = PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            wrappers.Add(element, wrapper);
        }
        var entries = new List<AccountContentEntry>();
        foreach (JObject row in icons)
        {
            string elementName = (string)row["elementId"];
            var element = (WeaponElement)Enum.Parse(typeof(WeaponElement), elementName == "Lightning" ? "Electric" : elementName);
            string gradeName = (string)row["gradeId"];
            var grade = (ItemGrade)Enum.Parse(typeof(ItemGrade), gradeName == "Normal" ? "Common" : gradeName);
            string stem = element + "_" + grade;
            string iconPath = IconRoot + "/ElementGem_" + stem + ".png";
            string original = (string)row["path"];
            if (!File.Exists(iconPath)) File.Copy(original, iconPath);
            else if (!File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(iconPath))) throw new InvalidOperationException("Existing icon differs: " + iconPath);
            AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(iconPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
            string dataPath = DataRoot + "/EG_" + stem + ".asset";
            var data = AssetDatabase.LoadAssetAtPath<ElementGemItemData>(dataPath);
            if (data == null) { data = ScriptableObject.CreateInstance<ElementGemItemData>(); AssetDatabase.CreateAsset(data, dataPath); }
            if (EditorUtility.IsDirty(data)) throw new InvalidOperationException("Gem definition has unsaved changes: " + dataPath);
            data.element = element; data.fixedGrade = grade;
            data.itemName = (string)row["elementName"] + " 원소 보석";
            data.icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            data.worldPickupPrefab = wrappers[element];
            data.description = "장착하면 무기에 " + (string)row["elementName"] + " 원소를 부여합니다.";
            data.sellPrice = 10; data.weight = 0;
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
            entries.Add(new AccountContentEntry { id = "item.elementgem." + element.ToString().ToLowerInvariant() + "." + grade.ToString().ToLowerInvariant(), asset = data });
        }
        if (entries.Count != 32) throw new InvalidOperationException("Expected exactly 32 gem definitions.");
        const string registryPath = "Assets/ProjectOverburst/Resources/Persistence/Supplemental/ElementGems.asset";
        AccountContentRegistryBuilder.EnsureFolder(Path.GetDirectoryName(registryPath).Replace('\\', '/'));
        var registry = AssetDatabase.LoadAssetAtPath<AccountContentRegistry>(registryPath);
        if (registry == null) { registry = ScriptableObject.CreateInstance<AccountContentRegistry>(); AssetDatabase.CreateAsset(registry, registryPath); }
        registry.SetAuthoringEntries(entries); EditorUtility.SetDirty(registry); AssetDatabase.SaveAssetIfDirty(registry);
        AccountContentRegistryBuilder.Build();
    }
}
