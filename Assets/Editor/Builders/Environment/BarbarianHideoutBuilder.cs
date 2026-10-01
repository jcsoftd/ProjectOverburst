using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class BarbarianHideoutBuilder
{
    public const string HideoutPath = "Assets/ProjectOverburst/00_Scenes/HideoutScene.unity";
    public const string VendorRoot = "Assets/ThirdParty/04_환경맵/TopDown Barbarian Camp";
    public const string DemoPath = VendorRoot + "/TopDown_BarbarianCamp_Scene.unity";
    public const string GeneratedRoot = VendorRoot + "/OVERBURST_URP";
    public const string EnvironmentPath = GeneratedRoot + "/PF_BarbarianCamp_Hideout.prefab";

    public static string Build(string output) => EditableBarbarianHideoutBuilder.Build(output);

    static void CopyTexture(Material original, Material target, string from, string to)
    {
        if (!original.HasProperty(from)) return;
        target.SetTexture(to, original.GetTexture(from));
        target.SetTextureScale(to, original.GetTextureScale(from));
        target.SetTextureOffset(to, original.GetTextureOffset(from));
    }

    public static List<string> ValidateLoaded(Scene scene) => EditableBarbarianHideoutBuilder.ValidateLoaded(scene);

    public static bool ClearCapsule(GameObject environment, Vector3 foot)
    {
        var probe = new GameObject("Temporary Capsule Clearance Probe");
        SceneManager.MoveGameObjectToScene(probe, environment.scene);
        try
        {
            var capsule = probe.AddComponent<CapsuleCollider>();
            capsule.radius = .4f; capsule.height = 1.8f; capsule.center = Vector3.up * .9f;
            capsule.isTrigger = true;
            foreach (var obstacle in environment.GetComponentsInChildren<Collider>(true))
            {
                if (obstacle.name == "Camp Ground" || !obstacle.enabled || obstacle.isTrigger) continue;
                if (Physics.ComputePenetration(capsule, foot, Quaternion.identity, obstacle,
                    obstacle.transform.position, obstacle.transform.rotation, out _, out float distance) && distance > .02f) return false;
            }
            return true;
        }
        finally { Object.DestroyImmediate(probe); }
    }

    public static string Validate(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        RequireIdle();
        var scene = EditorSceneManager.OpenPreviewScene(HideoutPath);
        try
        {
            var checks = ValidateLoaded(scene);
            File.WriteAllText(Path.Combine(output, "asset_result.json"), JsonConvert.SerializeObject(new { status = "PASS", checks = checks.Count, details = checks }, Formatting.Indented));
            return checks.Count + " saved-scene checks PASS.";
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    static GameObject NewRoot(string name, Scene scene)
    {
        var root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        return root;
    }
    static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Use an idle Editor.");
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
