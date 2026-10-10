using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class QuestMerchantPortraitBuilder
{
    public static void Build()
    {
        const string folder = QuestMerchantUIBuilder.Folder;
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/World/20261008_MainTown/Captures"));
        string[] categories = { "Weapon", "Armor", "Potion", "General" };
        string[] locations = { "메인 마을 · 대장간", "메인 마을 · 방어구점", "메인 마을 · 연금술점", "메인 마을 · 잡화점" };
        for (int i = 0; i < categories.Length; i++)
        {
            string file = categories[i] + "MerchantPortrait.png";
            string input = Path.Combine(source, categories[i] + "MerchantInitial.png");
            string output = folder + "/" + file;
            if (!File.Exists(output)) File.Copy(input, output, false);
            AssetDatabase.ImportAsset(output, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(output);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false; importer.alphaIsTransparency = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
            var merchant = AssetDatabase.LoadAssetAtPath<MerchantDefinition>("Assets/ProjectOverburst/03_Features/Items/Data/Merchants/MainTown/TownMerchant_" + categories[i] + ".asset");
            if (!merchant) throw new InvalidOperationException("Missing town merchant: " + categories[i]);
            var so = new SerializedObject(merchant);
            so.FindProperty("portrait").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(output);
            so.FindProperty("locationLabel").stringValue = locations[i]; so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(merchant);
        }
        string path = folder + "/MerchantPortrait.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("OVERBURST/UI Merchant Portrait")); AssetDatabase.CreateAsset(material, path); }
        material.SetFloat("_Brightness", 1.85f); material.SetFloat("_Contrast", .93f);
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
    }
}
