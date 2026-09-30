using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Local proof of the authored controller's continuous idle/walk/run playback.
public static class DeathHarvestKnightLoopCapture
{
    private const string Output = @"D:\JC Program\유니티\개인프로젝트\프로젝트 오버버스트\개인파일\코덱스산출\MonsterThemes\20260924_UndeadHorde\revisions\20260924_lich_animation\frames";
    private const int Width = 960;
    private const int Height = 540;
    private const int Fps = 12;

    [MenuItem("OVERBURST/Enemies/Themes/Capture Death Harvest Knight Loop")]
    public static string Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Edit Mode required.");
        Directory.CreateDirectory(Output);
        Scene previous = SceneManager.GetActiveScene();
        Scene preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(preview);
        RenderTexture target = null;
        Texture2D pixels = null;
        Material groundMaterial = null;
        try
        {
            var cameraObject = new GameObject("Knight loop review camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.11f, .14f, .18f);
            camera.cullingMask = 1 << 31;
            camera.fieldOfView = 35f;
            camera.nearClipPlane = .03f;
            camera.farClipPlane = 50f;
            cameraObject.transform.position = new Vector3(0, 1.55f, 4.5f);
            cameraObject.transform.LookAt(new Vector3(0, 1.0f, 0));

            var key = new GameObject("Knight key light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 2f;
            key.color = new Color(.92f, .95f, 1f);
            key.transform.rotation = Quaternion.Euler(44f, -35f, 0);
            var fill = new GameObject("Knight fill light").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = .8f;
            fill.color = new Color(.65f, .76f, 1f);
            fill.transform.rotation = Quaternion.Euler(25f, 135f, 0);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.layer = 31;
            floor.transform.localScale = Vector3.one;
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMaterial.SetColor("_BaseColor", new Color(.18f, .21f, .24f));
            floor.GetComponent<Renderer>().sharedMaterial = groundMaterial;

            const string prefabPath = "Assets/ProjectOverburst/Resources/Enemies/Themes/Actors/PF_DeathHarvest_DeathKnight.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new InvalidOperationException("Knight prefab missing.");
            var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
            foreach (var child in actor.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            var animator = actor.GetComponentInChildren<Animator>(true);
            // The isolated Editor scene has no Game View visibility pass.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.updateWhenOffscreen = true;
            animator.Rebind();
            animator.SetFloat("Locomotion", 0f);
            animator.SetFloat("MoveAnimSpeed", 1f);
            animator.SetFloat("AttackAnimSpeed", 1f);
            animator.Play("Locomotion", 0, 0f);
            animator.Update(0f);

            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            const int frameCount = Fps * 11;
            for (int frame = 0; frame < frameCount; frame++)
            {
                if (frame == Fps * 2) animator.SetFloat("Locomotion", 1f);
                if (frame == Fps * 6) animator.SetFloat("Locomotion", 2f);
                if (frame == Fps * 8)
                {
                    animator.SetFloat("Locomotion", 0f);
                    animator.SetTrigger("Attack1");
                }
                animator.Update(1f / Fps);
                var baked = new List<GameObject>();
                var disabled = new List<SkinnedMeshRenderer>();
                try
                {
                    foreach (var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (!skin.enabled) continue;
                        var mesh = new Mesh();
                        skin.BakeMesh(mesh);
                        var pose = new GameObject("Baked knight pose", typeof(MeshFilter), typeof(MeshRenderer));
                        pose.layer = 31;
                        pose.transform.SetParent(skin.transform, false);
                        pose.GetComponent<MeshFilter>().sharedMesh = mesh;
                        pose.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                        baked.Add(pose);
                        disabled.Add(skin);
                        skin.enabled = false;
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
                        File.WriteAllBytes(Path.Combine(Output, frame.ToString("D4") + ".png"), pixels.EncodeToPNG());
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
            if (target != null) Object.DestroyImmediate(target);
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (groundMaterial != null) Object.DestroyImmediate(groundMaterial);
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(preview, true);
        }
    }
}
