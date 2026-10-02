using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class LoadingScreenArtworkBuilder
{
    public const string ArtRoot = "Assets/ProjectOverburst/05_Art/UI/Loading";
    public const string CatalogPath = "Assets/ProjectOverburst/Resources/UI/Loading/LoadingScreenArtworkCatalog.asset";
    public const string MaterialPath = ArtRoot + "/LoadingArtwork.mat";
    public static readonly string[] ArtworkNames =
    {
        "Loading_SpiderCavern_Fire", "Loading_SpiderCavern_Lightning", "Loading_CavernBoss_Fire",
        "Loading_Hideout_Camp", "Loading_Arena_Dark", "Loading_FrostRuins_Ice",
        "Loading_Cemetery_Knight", "Loading_Crypt_Ice", "Loading_Ruins_Lightning"
    };

    [MenuItem("OVERBURST/UI/Update Loading Artwork Catalog")]
    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Loading artwork authoring requires an idle Editor.");
        var textures = ArtworkNames.Select(name =>
        {
            string path = ArtRoot + "/" + name + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing artwork: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }).ToArray();
        if (textures.Any(texture => texture == null)) throw new InvalidOperationException("An artwork did not load.");
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ArtRoot + "/LoadingArtwork.shader");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Loading shader is unavailable or has compile errors.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = shader;
        material.SetTexture("_ArtworkTex", textures[3]);
        material.SetFloat("_ArtworkAspect", (float)textures[3].width / textures[3].height);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);

        if (!AssetDatabase.IsValidFolder("Assets/ProjectOverburst/Resources/UI/Loading"))
            AssetDatabase.CreateFolder("Assets/ProjectOverburst/Resources/UI", "Loading");
        var catalog = AssetDatabase.LoadAssetAtPath<LoadingScreenArtworkCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<LoadingScreenArtworkCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        var serialized = new SerializedObject(catalog);
        var artworks = serialized.FindProperty("randomArtworks");
        artworks.arraySize = textures.Length;
        for (int i = 0; i < textures.Length; i++) artworks.GetArrayElementAtIndex(i).objectReferenceValue = textures[i];
        serialized.FindProperty("hideoutReturnArtwork").objectReferenceValue = textures[3];
        serialized.FindProperty("artworkMaterial").objectReferenceValue = material;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(catalog);
        return "Authored 9 loading artworks; dungeon return uses Loading_Hideout_Camp. No scenes saved.";
    }
}
