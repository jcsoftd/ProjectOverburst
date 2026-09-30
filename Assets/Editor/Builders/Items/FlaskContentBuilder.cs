using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class FlaskContentBuilder
{
    private const string Root = "Assets/ProjectOverburst";
    private const string DataFolder = Root + "/Resources/Items/Flasks";
    private const string IconFolder = Root + "/03_Features/Items/Art/Icons/Flasks";
    private const string PrefabFolder = Root + "/03_Features/Items/Prefabs/Flasks";
    public const string PlayerPath = Root + "/03_Features/Player/Prefabs/PF_PlayerActor.prefab";

    [MenuItem("JC Tool/Items/Build Flask Content")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Build Flask Content requires Edit Mode.");
        Directory.CreateDirectory(DataFolder); Directory.CreateDirectory(IconFolder); Directory.CreateDirectory(PrefabFolder);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Flasks/20260922_GOAL/Icons"));
        foreach (FlaskKind kind in Enum.GetValues(typeof(FlaskKind)))
        {
            string path = IconFolder + "/Flask_" + kind + ".png";
            if (!File.Exists(path)) File.Copy(Path.Combine(source, "Flask_" + kind + ".png"), path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 1024;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            if (!importer.DoesSourceTextureHaveAlpha()) throw new InvalidOperationException("Flask icon lacks alpha: " + kind);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            string dataPath = DataFolder + "/Flask_" + kind + ".asset";
            FlaskItemData data = AssetDatabase.LoadAssetAtPath<FlaskItemData>(dataPath);
            if (data == null) { data = ScriptableObject.CreateInstance<FlaskItemData>(); data.Configure(kind); AssetDatabase.CreateAsset(data, dataPath); }
            data.icon = sprite;
            string prefabPath = PrefabFolder + "/PF_Flask_" + kind + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                var pickup = new GameObject("PF_Flask_" + kind);
                try
                {
                    pickup.AddComponent<BoxCollider>().size = new Vector3(.5f,.65f,.35f);
                    pickup.AddComponent<WorldItemPickup>();
                    pickup.AddComponent<WorldItemDropMotion>();
                    pickup.AddComponent<WorldPickupPresentation>();
                    var visual = new GameObject("FlaskIcon"); visual.transform.SetParent(pickup.transform, false);
                    visual.transform.localScale = Vector3.one * .65f;
                    visual.AddComponent<SpriteRenderer>().sprite = sprite;
                    PrefabUtility.SaveAsPrefabAsset(pickup, prefabPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(pickup); }
            }
            data.worldPickupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            EditorUtility.SetDirty(data);
        }
        var player = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            if (player.GetComponent<PlayerFlaskController>() == null) player.AddComponent<PlayerFlaskController>();
            if (player.GetComponent<FlaskGhostCollision>() == null) player.AddComponent<FlaskGhostCollision>();
            PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        AssetDatabase.SaveAssets();
        Debug.Log("FLASK_CONTENT_PASS: 12 data + transparent sprites + authored pickups + player components");
    }
}
