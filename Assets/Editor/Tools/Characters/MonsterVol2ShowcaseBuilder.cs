using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;

public static class MonsterVol2ShowcaseBuilder
{
    public const string Pack = "Assets/ThirdParty/01_비인간캐릭터/Protofactor 1/Monster Full Pack Vol 2";
    public const string ScenePath = "Assets/ProjectOverburst/00_Scenes/MonsterVol2_Showcase.unity";
    private const string MaterialsPath = "Assets/ProjectOverburst/03_Features/Enemies/Showcase/Materials";
    private static Font font;

    [MenuItem("OVERBURST/Showcase/Create Monster Vol 2 Gallery")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        if (File.Exists(ScenePath)) throw new InvalidOperationException("Showcase already exists. Open it; do not overwrite.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the current modified scene first.");
        string[] paths = AssetDatabase.FindAssets("t:Prefab", new[] { Pack })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => Path.GetFileNameWithoutExtension(p)).ToArray();
        if (paths.Length == 0) throw new InvalidOperationException("Pack has no prefabs.");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Material floor = MaterialAsset("Floor", new Color(0.13f, 0.17f, 0.21f));
        Material plinth = MaterialAsset("Plinth", new Color(0.27f, 0.33f, 0.38f));
        Material ruler = MaterialAsset("ScaleReference", new Color(0.18f, 0.8f, 0.73f));
        var gallery = new GameObject("Monster Vol 2 Gallery").AddComponent<MonsterShowcaseGallery>();
        var actors = new List<MonsterShowcaseActor>();
        float spacing = 8f;
        for (int i = 0; i < paths.Length; i++)
        {
            var station = new GameObject($"{i + 1:00}_{Path.GetFileNameWithoutExtension(paths[i])}");
            station.transform.SetParent(gallery.transform);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]), station.transform);
            Bounds bounds = BoundsOf(instance);
            var actor = station.AddComponent<MonsterShowcaseActor>();
            actor.displayName = Path.GetFileNameWithoutExtension(paths[i]).Replace(" Variant", "");
            actor.sourcePath = paths[i];
            actor.animator = instance.GetComponentInChildren<Animator>(true);
            if (actor.animator == null) throw new InvalidOperationException("No Animator: " + paths[i]);
            if (actor.displayName == "RostrokarckEgg")
            {
                instance.transform.Find("SM_RostrokarckEgg").gameObject.SetActive(false);
                actor.animator.gameObject.SetActive(true);
            }
            actor.clips = ClipsFor(actor.animator, paths[i]);
            if (actor.clips.Length == 0) throw new InvalidOperationException("No clips: " + paths[i]);
            bounds = IdleBounds(instance, actor);
            actor.displaySize = bounds.size;
            instance.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            foreach (var rb in instance.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material == null || material.shader == null || !material.shader.isSupported)
                        throw new InvalidOperationException("Invalid material: " + paths[i]);
            spacing = Mathf.Max(spacing, Mathf.Max(bounds.size.x, bounds.size.z) + 5f);
            actors.Add(actor);
        }
        int columns = 7, rows = Mathf.CeilToInt(actors.Count / (float)columns);
        for (int i = 0; i < actors.Count; i++)
        {
            var actor = actors[i];
            actor.transform.position = new Vector3((i % columns) * spacing, 0, (i / columns) * spacing);
            actor.focusPoint = actor.transform.position + Vector3.up * actor.displaySize.y * 0.5f;
            float radius = Mathf.Max(1.6f, Mathf.Max(actor.displaySize.x, actor.displaySize.z) * 0.6f);
            Primitive("Display base", PrimitiveType.Cylinder, actor.transform, new Vector3(0, -0.12f, 0), new Vector3(radius * 2, 0.12f, radius * 2), plinth);
            Primitive("1.8 m height reference", PrimitiveType.Capsule, actor.transform, new Vector3(radius + 0.65f, 0.9f, 0), new Vector3(0.45f, 0.9f, 0.45f), ruler);
            var label = new GameObject("Name and size").AddComponent<TextMesh>();
            label.transform.SetParent(actor.transform, false);
            label.transform.localPosition = new Vector3(0, actor.displaySize.y + 0.65f, 0);
            label.text = $"{i + 1:00}  {actor.displayName}\nH {actor.displaySize.y:F2} m";
            label.font = font; label.fontSize = 60; label.characterSize = Mathf.Clamp(radius * 0.013f, 0.025f, 0.08f);
            label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
            label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            actor.nameplate = label;
        }
        gallery.actors = actors.ToArray();
        gallery.overviewTarget = new Vector3((columns - 1) * spacing * 0.5f, 0, (rows - 1) * spacing * 0.5f);
        gallery.overviewDistance = Mathf.Max(columns, rows) * spacing * 1.25f;
        Primitive("Gallery floor", PrimitiveType.Cube, null, gallery.overviewTarget + Vector3.down * 0.3f,
            new Vector3(columns * spacing + 10, 0.25f, rows * spacing + 10), floor);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.65f, 0.69f, 0.75f);
        RenderSettings.fog = false;
        var light = new GameObject("Key light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.6f; light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(48, -35, 0);
        var fill = new GameObject("Fill light").AddComponent<Light>();
        fill.type = LightType.Directional; fill.intensity = 0.6f; fill.transform.rotation = Quaternion.Euler(30, 145, 0);
        var camera = new GameObject("Gallery camera").AddComponent<Camera>();
        camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.055f, 0.075f, 0.1f); camera.fieldOfView = 40; camera.nearClipPlane = 0.05f; camera.farClipPlane = 2000;
        camera.gameObject.AddComponent<AudioListener>(); gallery.galleryCamera = camera;
        camera.transform.position = gallery.overviewTarget + new Vector3(0, gallery.overviewDistance, -gallery.overviewDistance * 0.4f);
        camera.transform.LookAt(gallery.overviewTarget);
        BuildUI(gallery);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = gallery.gameObject;
        SceneView.lastActiveSceneView?.Frame(new Bounds(gallery.overviewTarget, new Vector3(columns * spacing, 15, rows * spacing)), false);
        Debug.Log($"[MonsterVol2Showcase] CREATED {actors.Count} prefabs, {actors.Sum(a => a.clips.Length)} clip connections: {ScenePath}");
    }

    private static AnimationClip[] ClipsFor(Animator animator, string prefab)
    {
        var clips = new HashSet<AnimationClip>();
        if (animator.runtimeAnimatorController != null)
            foreach (var clip in animator.runtimeAnimatorController.animationClips) if (clip != null) clips.Add(clip);
        string characterFolder = Path.GetDirectoryName(Path.GetDirectoryName(prefab)).Replace('\\', '/');
        string modelName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(animator.avatar));
        if (modelName.StartsWith("SK_")) modelName = modelName.Substring(3);
        foreach (string path in AssetDatabase.FindAssets("t:AnimationClip", new[] { characterFolder }).Select(AssetDatabase.GUIDToAssetPath).Distinct())
        {
            if (!Path.GetFileNameWithoutExtension(path).StartsWith(modelName + "@", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                if (!clip.name.StartsWith("__preview__")) clips.Add(clip);
        }
        return clips.OrderBy(c => c.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1).ThenBy(c => c.name).ToArray();
    }

    private static Bounds BoundsOf(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException("No model renderer: " + go.name);
        Bounds b = renderers[0].bounds;
        foreach (var renderer in renderers) b.Encapsulate(renderer.bounds);
        return b;
    }

    // Imported skinned bounds include jumps/attacks and are not physical idle size.
    private static Bounds IdleBounds(GameObject model, MonsterShowcaseActor actor)
    {
        if (AnimationMode.InAnimationMode()) throw new InvalidOperationException("Close Animation preview first.");
        AnimationMode.StartAnimationMode();
        try
        {
            Vector3 position = actor.animator.transform.localPosition;
            Quaternion rotation = actor.animator.transform.localRotation;
            Vector3 scale = actor.animator.transform.localScale;
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(actor.animator.gameObject, actor.clips[0], 0f);
            AnimationMode.EndSampling();
            actor.animator.transform.localPosition = position;
            actor.animator.transform.localRotation = rotation;
            actor.animator.transform.localScale = scale;
            Bounds result = default; bool found = false;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Mesh mesh = null; bool temporary = false;
                if (renderer is SkinnedMeshRenderer skin) { mesh = new Mesh(); skin.BakeMesh(mesh, true); temporary = true; }
                else { var filter = renderer.GetComponent<MeshFilter>(); if (filter != null) mesh = filter.sharedMesh; }
                if (mesh == null) continue;
                foreach (var vertex in mesh.vertices)
                {
                    Vector3 point = renderer.transform.TransformPoint(vertex);
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                    else result.Encapsulate(point);
                }
                if (temporary) UnityEngine.Object.DestroyImmediate(mesh);
            }
            if (!found) throw new InvalidOperationException("No visible mesh: " + actor.displayName);
            return result;
        }
        finally { AnimationMode.StopAnimationMode(); }
    }

    private static Material MaterialAsset(string name, Color color)
    {
        EnsureFolder(MaterialsPath);
        string path = MaterialsPath + "/" + name + ".mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) throw new InvalidOperationException("Material already exists: " + path);
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.name = name; m.color = color;
        m.SetFloat("_Smoothness", 0.25f); AssetDatabase.CreateAsset(m, path); return m;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static void Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(width, height); return rt;
    }
    private static Text Label(string name, Transform parent, string text, float x, float y, float w, float h, int size)
    {
        var t = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Text>(); t.font = font; t.text = text;
        t.fontSize = size; t.color = new Color(0.88f, 0.94f, 1); t.raycastTarget = false; t.alignment = TextAnchor.MiddleLeft; return t;
    }
    private static Button Button(string name, Transform parent, string text, float x, float y, float w, float h)
    {
        var rt = Rect(name, parent, x, y, w, h); var image = rt.gameObject.AddComponent<Image>(); image.color = new Color(0.13f, 0.18f, 0.24f);
        var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var label = Label("Label", rt, text, 10, 0, w - 20, h, 17); label.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    private static void BuildUI(MonsterShowcaseGallery gallery)
    {
        var canvas = new GameObject("Showcase UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 1;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        var sidebar = Rect("Catalog", canvas.transform, 0, 0, 300, 900);
        sidebar.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.055f, 0.08f, 0.98f);
        Label("Title", sidebar, "MONSTER ATLAS", 18, 16, 265, 35, 25);
        Label("Subtitle", sidebar, $"FULL PACK VOL. 2  /  {gallery.actors.Length} MODELS", 18, 52, 265, 28, 15);
        Label("Hint", sidebar, "Select a model to inspect", 18, 82, 265, 26, 15);
        var viewport = Rect("Model list", sidebar, 10, 125, 280, 675);
        viewport.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.07f, 0.1f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var content = Rect("Models", viewport, 0, 0, 280, gallery.actors.Length * 44);
        scroll.content = content; gallery.monsterButtons = new Button[gallery.actors.Length];
        for (int i = 0; i < gallery.actors.Length; i++)
        {
            var actor = gallery.actors[i];
            gallery.monsterButtons[i] = Button(actor.displayName, content, $"{i + 1:00}  {actor.displayName}", 0, i * 44, 275, 40);
            var text = gallery.monsterButtons[i].GetComponentInChildren<Text>(); text.fontSize = 14; text.alignment = TextAnchor.MiddleLeft;
        }
        gallery.overviewButton = Button("Overview", sidebar, "VIEW ALL MODELS", 15, 818, 270, 42);
        Label("Scale hint", sidebar, "Teal marker = 1.8 m human height", 15, 863, 280, 24, 13);
        var clipsPanel = Rect("Animations", canvas.transform, 0, 0, 300, 900);
        clipsPanel.anchorMin = clipsPanel.anchorMax = clipsPanel.pivot = Vector2.one;
        clipsPanel.anchoredPosition = Vector2.zero;
        clipsPanel.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.055f, 0.08f, 0.98f);
        Label("Animations title", clipsPanel, "ANIMATIONS", 18, 16, 270, 35, 24);
        Label("Animation hint", clipsPanel, "Click a clip to play / replay", 18, 57, 270, 26, 15);
        var clipViewport = Rect("Clip list", clipsPanel, 10, 98, 280, 780);
        clipViewport.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.07f, 0.1f);
        clipViewport.gameObject.AddComponent<RectMask2D>();
        gallery.clipScroll = clipViewport.gameObject.AddComponent<ScrollRect>();
        gallery.clipScroll.viewport = clipViewport; gallery.clipScroll.horizontal = false;
        gallery.clipScroll.movementType = ScrollRect.MovementType.Clamped;
        int maxClips = gallery.actors.Max(a => a.clips.Length);
        var clipContent = Rect("Clips", clipViewport, 0, 0, 280, maxClips * 38);
        gallery.clipScroll.content = clipContent; gallery.clipButtons = new Button[maxClips];
        for (int i = 0; i < maxClips; i++)
        {
            gallery.clipButtons[i] = Button("Clip " + (i + 1), clipContent, "Clip", 0, i * 38, 275, 34);
            var text = gallery.clipButtons[i].GetComponentInChildren<Text>(); text.fontSize = 14; text.alignment = TextAnchor.MiddleLeft;
        }
        gallery.title = Label("Selected monster", canvas.transform, "", 325, 18, 945, 46, 27);
        gallery.details = Label("Model details", canvas.transform, "", 328, 67, 940, 54, 16);
        Label("Camera instructions", canvas.transform, "RMB: orbit  |  Wheel: zoom  |  Left/Right: model  |  Up/Down: clip  |  Space: pause", 328, 120, 940, 27, 15);
        var bottom = Rect("Playback", canvas.transform, 320, 780, 960, 105);
        bottom.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.055f, 0.08f, 0.94f);
        gallery.clipLabel = Label("Current clip", bottom, "", 18, 2, 920, 38, 18);
        gallery.previousMonster = Button("Previous model", bottom, "< MODEL", 15, 50, 135, 40);
        gallery.nextMonster = Button("Next model", bottom, "MODEL >", 160, 50, 135, 40);
        gallery.previousClip = Button("Previous clip", bottom, "< CLIP", 320, 50, 135, 40);
        gallery.nextClip = Button("Next clip", bottom, "CLIP >", 465, 50, 135, 40);
        gallery.autoButton = Button("Auto", bottom, "AUTO: ON", 635, 50, 150, 40);
        gallery.autoLabel = gallery.autoButton.GetComponentInChildren<Text>();
        gallery.pauseButton = Button("Pause", bottom, "PAUSE", 800, 50, 140, 40);
        gallery.pauseLabel = gallery.pauseButton.GetComponentInChildren<Text>();
    }

    [MenuItem("OVERBURST/Showcase/Refresh Monster Vol 2 Controls")]
    public static void RefreshControls()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath || scene.isDirty)
            throw new InvalidOperationException("Open the saved showcase in Edit Mode first.");
        var gallery = UnityEngine.Object.FindFirstObjectByType<MonsterShowcaseGallery>();
        if (gallery == null) throw new InvalidOperationException("Gallery missing");
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Showcase UI" || root.name == "EventSystem") UnityEngine.Object.DestroyImmediate(root);
        foreach (var actor in gallery.actors)
        {
            if (actor.displayName == "RostrokarckEgg")
            {
                actor.animator.transform.parent.Find("SM_RostrokarckEgg").gameObject.SetActive(false);
                actor.animator.gameObject.SetActive(true);
            }
            Transform model = actor.transform.GetChild(0);
            Bounds bounds = IdleBounds(model.gameObject, actor);
            Vector3 station = actor.transform.position;
            model.position -= new Vector3(bounds.center.x - station.x, bounds.min.y, bounds.center.z - station.z);
            actor.displaySize = bounds.size;
            actor.focusPoint = station + Vector3.up * bounds.size.y * 0.5f;
            float radius = Mathf.Max(1.6f, Mathf.Max(bounds.size.x, bounds.size.z) * 0.6f);
            actor.transform.Find("Display base").localScale = new Vector3(radius * 2, 0.12f, radius * 2);
            actor.transform.Find("1.8 m height reference").localPosition = new Vector3(radius + 0.65f, 0.9f, 0);
            actor.nameplate.transform.localPosition = new Vector3(0, bounds.size.y + 0.65f, 0);
            actor.nameplate.text = $"{Array.IndexOf(gallery.actors, actor) + 1:00}  {actor.displayName}\nH {bounds.size.y:F2} m";
        }
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUI(gallery);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
