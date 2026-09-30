using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Local-only, close-up capture of the authored medium models and their real clips.
public static class DeathHarvestMotionCapture
{
    private const string Output = @"D:\JC Program\유니티\개인프로젝트\프로젝트 오버버스트\개인파일\코덱스산출\MonsterThemes\20260924_UndeadHorde\play\medium-motion-frames";
    private const int Width = 960;
    private const int Height = 540;
    private const int Fps = 15;

    private sealed class Subject
    {
        public GameObject actor;
        public Animator animator;
        public AnimationClip[] clips;
        public PlayableGraph graph;
        public AnimationClipPlayable playable;
        public int current = -1;
    }

    [MenuItem("OVERBURST/Enemies/Themes/Capture Death Harvest Medium Motion")]
    public static string Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Capture requires Edit Mode.");
        var table = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(
            "Assets/ProjectOverburst/Resources/Enemies/Themes/Tables/DeathHarvest.asset");
        if (table == null) throw new InvalidOperationException("DeathHarvest table missing.");
        Directory.CreateDirectory(Output);
        Scene previous = SceneManager.GetActiveScene();
        Scene preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(preview);
        var subjects = new List<Subject>();
        RenderTexture target = null;
        Texture2D pixels = null;
        Material groundMaterial = null;
        try
        {
            var cameraRoot = new GameObject("DeathHarvest motion camera");
            var camera = cameraRoot.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.11f, .14f, .18f);
            camera.cullingMask = 1 << 31;
            camera.fieldOfView = 36f;
            camera.nearClipPlane = .03f;
            camera.farClipPlane = 50f;
            cameraRoot.transform.position = new Vector3(0, 2.6f, 9f);
            cameraRoot.transform.LookAt(new Vector3(0, 1.2f, 0));

            var lightRoot = new GameObject("Key light");
            var light = lightRoot.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.8f;
            light.color = new Color(.88f, .93f, 1f);
            lightRoot.transform.rotation = Quaternion.Euler(48f, -35f, 0);
            var fillRoot = new GameObject("Fill light");
            var fill = fillRoot.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = .65f;
            fill.color = new Color(.55f, .70f, .78f);
            fillRoot.transform.rotation = Quaternion.Euler(28f, 135f, 0);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Review floor";
            floor.layer = 31;
            floor.transform.localScale = new Vector3(1.2f, 1f, 1.2f);
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMaterial.SetColor("_BaseColor", new Color(.18f, .21f, .24f));
            floor.GetComponent<Renderer>().sharedMaterial = groundMaterial;

            string[] ids = { "DeathHarvest_BoneWarden", "DeathHarvest_RakeBrute" };
            for (int i = 0; i < ids.Length; i++)
            {
                var entry = table.Entries.First(e => e.definition.EnemyId == ids[i]);
                var actor = (GameObject)PrefabUtility.InstantiatePrefab(entry.definition.ActorPrefab.gameObject, preview);
                actor.transform.position = new Vector3(i == 0 ? -1.7f : 1.7f, 0, 0);
                foreach (Transform child in actor.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                var animator = actor.GetComponentInChildren<Animator>(true);
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    skin.updateWhenOffscreen = true;
                var animation = entry.definition.AnimationProfile;
                subjects.Add(new Subject
                {
                    actor = actor, animator = animator,
                    clips = new[] { animation.Idle, animation.Walk,
                        animation.GetAttackClip(0), animation.GetAttackClip(1) }
                });
            }

            float[] boundaries = { 0f, 1.5f, 3.9f, 8.75f, 11.45f };
            int frameCount = Mathf.CeilToInt(boundaries[boundaries.Length - 1] * Fps);
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            for (int frame = 0; frame < frameCount; frame++)
            {
                float time = frame / (float)Fps;
                int segment = 0;
                while (segment < 3 && time >= boundaries[segment + 1]) segment++;
                float localTime = time - boundaries[segment];
                var baked = new List<GameObject>();
                var disabled = new List<SkinnedMeshRenderer>();
                try
                {
                    foreach (var subject in subjects)
                    {
                        if (subject.current != segment)
                        {
                            if (subject.graph.IsValid()) subject.graph.Destroy();
                            subject.graph = PlayableGraph.Create("DeathHarvest medium motion");
                            subject.playable = AnimationClipPlayable.Create(subject.graph, subject.clips[segment]);
                            var output = AnimationPlayableOutput.Create(subject.graph, "Model", subject.animator);
                            output.SetSourcePlayable(subject.playable);
                            subject.graph.Play();
                            subject.current = segment;
                        }
                        float clipTime = localTime % subject.clips[segment].length;
                        subject.playable.SetTime(clipTime);
                        subject.graph.Evaluate(.0001f);
                        foreach (var skin in subject.actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        {
                            if (!skin.enabled) continue;
                            var mesh = new Mesh();
                            skin.BakeMesh(mesh);
                            var pose = new GameObject("Baked pose " + skin.name,
                                typeof(MeshFilter), typeof(MeshRenderer));
                            pose.layer = 31;
                            pose.transform.SetParent(skin.transform, false);
                            pose.GetComponent<MeshFilter>().sharedMesh = mesh;
                            pose.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                            baked.Add(pose); disabled.Add(skin); skin.enabled = false;
                        }
                    }
                    var previousTarget = camera.targetTexture;
                    var previousActive = RenderTexture.active;
                    try
                    {
                        camera.targetTexture = target;
                        camera.Render();
                        RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                        pixels.Apply();
                        File.WriteAllBytes(Path.Combine(Output, frame.ToString("D4") + ".png"),
                            pixels.EncodeToPNG());
                    }
                    finally
                    {
                        camera.targetTexture = previousTarget;
                        RenderTexture.active = previousActive;
                    }
                }
                finally
                {
                    foreach (var skin in disabled) skin.enabled = true;
                    foreach (var pose in baked)
                    {
                        var mesh = pose.GetComponent<MeshFilter>().sharedMesh;
                        Object.DestroyImmediate(pose);
                        Object.DestroyImmediate(mesh);
                    }
                }
            }
            return "frames=" + frameCount + " path=" + Output;
        }
        finally
        {
            foreach (var subject in subjects)
                if (subject.graph.IsValid()) subject.graph.Destroy();
            if (target != null) Object.DestroyImmediate(target);
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (groundMaterial != null) Object.DestroyImmediate(groundMaterial);
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(preview, true);
        }
    }
}
