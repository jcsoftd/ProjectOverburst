using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class PlayerLevelUpFeedbackBuilder
{
    public const string Source = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstNotifications_Rpg11.prefab";
    public const string Prefab = "Assets/ProjectOverburst/Resources/UI/Notifications/PF_PlayerLevelUpNotification.prefab";
    public const string Audio = "Assets/ProjectOverburst/Resources/Combat/SFX/CombatAction/PlayerLevelUp.wav";

    public static string Build(string output)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        Directory.CreateDirectory(output);
        string sourceHash = AssetDatabase.GetAssetDependencyHash(Source).ToString();
        AssetDatabase.ImportAsset(Audio, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(Audio) as AudioImporter;
        if (importer == null) throw new FileNotFoundException(Audio);
        importer.forceToMono = false; importer.loadInBackground = false;
        var settings = importer.defaultSampleSettings;
        settings.preloadAudioData = true;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        importer.defaultSampleSettings = settings; importer.SaveAndReimport();
        var scene = EditorSceneManager.NewPreviewScene(); GameObject root = null;
        try
        {
            root = new GameObject("PlayerLevelUpNotification", typeof(RectTransform), typeof(Canvas), typeof(PlayerLevelUpNotification));
            SceneManager.MoveGameObjectToScene(root, scene);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(1920, 1080);
            var canvas = root.GetComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingOrder = 500;
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(Source).transform.Find("Notification (Level)");
            if (original == null) throw new InvalidOperationException("Missing authored level notification");
            var panel = (RectTransform)Object.Instantiate(original.gameObject, root.transform, false).transform;
            panel.name = "Notification (Level)"; panel.gameObject.SetActive(true);
            panel.anchoredPosition = new Vector2(0, 260); panel.localScale = Vector3.one * .55f;
            var group = panel.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0;
            group.interactable = false; group.blocksRaycasts = false;
            var level = panel.GetComponentsInChildren<Text>(true).Single(t => t.name == "Level Text");
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
            root.GetComponent<PlayerLevelUpNotification>().Configure(panel, group, level);
            string directory = Path.GetDirectoryName(Prefab); Directory.CreateDirectory(directory);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        }
        finally { if (root != null) Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        if (AssetDatabase.GetAssetDependencyHash(Source).ToString() != sourceHash)
            throw new InvalidOperationException("Source notification changed");
        var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Audio);
        if (loaded == null || clip == null || clip.channels != 2 || clip.frequency != 48000)
            throw new InvalidOperationException("Native asset load failed");
        string json = JsonConvert.SerializeObject(new { status = "PASS", source = Source, sourcePreserved = true,
            prefab = Prefab, guid = AssetDatabase.AssetPathToGUID(Prefab), graphics = loaded.GetComponentsInChildren<Graphic>(true).Length,
            audio = Audio, clip.length, clip.channels, clip.frequency }, Formatting.Indented);
        File.WriteAllText(Path.Combine(output, "assets.json"), json); return json;
    }
}
