using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Restores the supplied local media and binds frame timing through native Unity serialization.</summary>
public static class HideoutOiiaReferenceBuilder
{
    public const string Output = "../개인파일/코덱스산출/EasterEgg/20261006_VideoSync";
    public const string MediaRoot = "Assets/ThirdParty/OiiaReference";
    public const string AudioPath = MediaRoot + "/OiiaReference.wav";
    public const string PhotoPath = MediaRoot + "/OiiaCat_Still_Reference.png";
    public const string PhotoMaterialPath = MediaRoot + "/MAT_OiiaReferencePhoto.mat";
    public const string TimelinePath = HideoutSpinningCatBuilder.Root + "/OiiaReferenceTimeline.asset";
    sealed class Timing { public int frameRate; public int frameCount; public int[] transitionFrames; }

    [MenuItem("Tools/Overburst/Hideout/Bind OIIA Reference Audio and Photo")]
    public static void Menu() => Debug.Log(Apply(Output));

    public static string Apply(string sourceDirectory)
    {
        HideoutSpinningCatBuilder.RequireIdle();
        string before = HideoutSpinningCatBuilder.EditorSnapshot();
        var timing = JsonConvert.DeserializeObject<Timing>(File.ReadAllText(Path.Combine(sourceDirectory, "Prepared/timeline.json")));
        if (timing.frameRate != 30 || timing.frameCount != 4039 || timing.transitionFrames.Length == 0
            || timing.transitionFrames.Where((f, i) => f <= 0 || f >= timing.frameCount || (i > 0 && f <= timing.transitionFrames[i - 1])).Any())
            throw new InvalidDataException("Reference timing must cover the supplied 4039-frame video.");
        Folder(MediaRoot);
        CopyMedia(Path.Combine(sourceDirectory, "Prepared/OiiaReference.wav"), AudioPath);
        CopyMedia(Path.Combine(sourceDirectory, "Frames/OiiaCat_Still_Reference.png"), PhotoPath);
        var ai = (AudioImporter)AssetImporter.GetAtPath(AudioPath);
        var settings = ai.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = true;
        ai.defaultSampleSettings = settings; ai.loadInBackground = false; ai.forceToMono = false;
        ai.SaveAndReimport();
        var ti = (TextureImporter)AssetImporter.GetAtPath(PhotoPath);
        ti.textureCompression = TextureImporterCompression.Uncompressed; ti.mipmapEnabled = false;
        ti.maxTextureSize = 1024; ti.npotScale = TextureImporterNPOTScale.None; ti.wrapMode = TextureWrapMode.Clamp;
        ti.SaveAndReimport();
        var audio = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioPath);
        if (audio.frequency != 44100 || audio.channels != 2 || audio.samples != 5937330)
            throw new InvalidDataException("Reference audio sample count/frequency/channels changed.");
        var timeline = AssetDatabase.LoadAssetAtPath<OiiaCatReferenceTimeline>(TimelinePath);
        if (timeline == null) { timeline = ScriptableObject.CreateInstance<OiiaCatReferenceTimeline>(); AssetDatabase.CreateAsset(timeline, TimelinePath); }
        var ts = new SerializedObject(timeline);
        ts.FindProperty("frameRate").intValue = timing.frameRate;
        ts.FindProperty("frameCount").intValue = timing.frameCount;
        var transitions = ts.FindProperty("transitionFrames"); transitions.arraySize = timing.transitionFrames.Length;
        for (int i = 0; i < transitions.arraySize; i++) transitions.GetArrayElementAtIndex(i).intValue = timing.transitionFrames[i];
        ts.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(timeline);
        var shader = Shader.Find("Overburst/OIIA Reference Photo");
        if (shader == null) throw new InvalidOperationException("Photo shader is not imported.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(PhotoMaterialPath);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, PhotoMaterialPath); }
        material.shader = shader;
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(PhotoPath));
        material.SetVector("_UvRect", new Vector4(235f / 640, 71f / 360, 148f / 640, 193f / 360));
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(HideoutSpinningCatBuilder.CatPrefab);
            var gag = root.GetComponent<HideoutSpinningCatEasterEgg>();
            var so = new SerializedObject(gag);
            var photo = root.transform.Find("Reference Still Photo")?.gameObject;
            if (photo == null)
            {
                photo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                photo.name = "Reference Still Photo"; photo.transform.SetParent(root.transform, false);
                Object.DestroyImmediate(photo.GetComponent<Collider>());
            }
            photo.transform.localPosition = new Vector3(0, .3f, 0);
            photo.transform.localRotation = Quaternion.identity;
            photo.transform.localScale = new Vector3(.55f * 148 / 193, .55f, 1);
            var renderer = photo.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            photo.SetActive(true);
            ((GameObject)so.FindProperty("visual").objectReferenceValue).SetActive(false);
            var source = root.GetComponent<AudioSource>();
            source.clip = audio; source.playOnAwake = false; source.loop = true; source.pitch = 1;
            source.spatialBlend = 0; source.volume = .8f; source.mute = false;
            so.FindProperty("music").objectReferenceValue = audio;
            so.FindProperty("stillVisual").objectReferenceValue = photo;
            so.FindProperty("referenceTimeline").objectReferenceValue = timeline;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, HideoutSpinningCatBuilder.CatPrefab);
        }
        finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
        if (before != HideoutSpinningCatBuilder.EditorSnapshot()) throw new InvalidOperationException("Existing scene state changed.");
        string backup = Path.Combine(sourceDirectory, "Restore/Assets/ThirdParty/OiiaReference");
        Directory.CreateDirectory(backup);
        foreach (string path in Directory.GetFiles(MediaRoot)) File.Copy(path, Path.Combine(backup, Path.GetFileName(path)), true);
        File.Copy(MediaRoot + ".meta", Path.Combine(sourceDirectory, "Restore/Assets/ThirdParty/OiiaReference.meta"), true);
        Write("apply.json", new { status = "PASS", audio.samples, audio.frequency, audio.channels, audio.length,
            timing.frameRate, timing.frameCount, transitions = timing.transitionFrames.Length, scenePreserved = before,
            audioGuid = AssetDatabase.AssetPathToGUID(AudioPath), photoGuid = AssetDatabase.AssetPathToGUID(PhotoPath),
            timelineGuid = AssetDatabase.AssetPathToGUID(TimelinePath), publicMediaRedistribution = "NOT_VERIFIED_LOCAL_ONLY" });
        return "PASS: supplied audio, original still frame and 30 FPS reference transitions assigned to the installed Hideout cat.";
    }
    static void CopyMedia(string source, string destination)
    {
        File.Copy(source, destination, true);
        AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
    }
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    public static void Write(string name, object result)
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, name), JsonConvert.SerializeObject(result, Formatting.Indented));
    }
}
