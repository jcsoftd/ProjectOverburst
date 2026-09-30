using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class PlayerAmbientLightAuthoringBuilder
{
    public const string PlayerPrefabPath =
        "Assets/ProjectOverburst/03_Features/Player/Prefabs/"
        + "PF_PlayerActor.prefab";
    public const string LightObjectName = "PlayerAmbientLight";
    public const float LightIntensity = 0.3f;
    public const float LightRange = 4f;
    public static readonly Vector3 LocalPosition =
        new(0f, 1.3f, 0f);
    public static readonly Color LightColor =
        new(1f, 0.91f, 0.78f, 1f);

    private const string VisualRootName = "VisualRoot";
    private const string LogPath =
        "Logs/PlayerAmbientLightAuthoring.log";

    [MenuItem(
        "OVERBURST/Codex/Setup/Characters/"
        + "Apply Player Ambient Light")]
    public static void BuildFromMenu()
    {
        Debug.Log(BuildAndValidate());
    }

    public static void RunFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            string report = BuildAndValidate();
            File.WriteAllText(LogPath, report);
            Debug.Log(report);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(LogPath, exception.ToString());
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static string BuildAndValidate()
    {
        GameObject root =
            PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        Require(root != null, "PF_PlayerActor 프리팹을 찾을 수 없습니다.");

        try
        {
            Transform visualRoot = root.transform.Find(VisualRootName);
            Require(
                visualRoot != null,
                "PF_PlayerActor/VisualRoot를 찾을 수 없습니다.");

            Transform lightTransform =
                visualRoot.Find(LightObjectName);
            GameObject lightObject;
            if (lightTransform == null)
            {
                lightObject = new GameObject(LightObjectName);
                lightObject.transform.SetParent(visualRoot, false);
            }
            else
            {
                lightObject = lightTransform.gameObject;
            }

            Light light = lightObject.GetComponent<Light>();
            if (light == null)
                light = lightObject.AddComponent<Light>();

            ApplyPolicy(light);
            PrefabUtility.SaveAsPrefabAsset(
                root,
                PlayerPrefabPath,
                out bool saved);
            Require(saved, "PF_PlayerActor 프리팹 저장에 실패했습니다.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(
            ImportAssetOptions.ForceSynchronousImport);
        return ValidateOrThrow();
    }

    public static string ValidateOrThrow()
    {
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Require(prefab != null, "PF_PlayerActor 프리팹이 누락됐습니다.");

        Transform visualRoot =
            prefab.transform.Find(VisualRootName);
        Require(
            visualRoot != null,
            "PF_PlayerActor/VisualRoot가 누락됐습니다.");
        Transform lightTransform =
            visualRoot.Find(LightObjectName);
        Require(
            lightTransform != null,
            "PlayerAmbientLight 오브젝트가 누락됐습니다.");

        Light light = lightTransform.GetComponent<Light>();
        Require(light != null, "PlayerAmbientLight 컴포넌트가 누락됐습니다.");
        Require(
            IsPolicyCurrent(light),
            "PlayerAmbientLight 설정이 정책과 다릅니다.");

        return "[PlayerAmbientLightAuthoringBuilder] PASS\n"
            + $"Prefab={PlayerPrefabPath}\n"
            + $"LocalPosition={LocalPosition}\n"
            + $"Intensity={LightIntensity:F2}\n"
            + $"Range={LightRange:F2}\n"
            + "RealtimeShadows=0";
    }

    public static bool IsPolicyCurrent(Light light)
    {
        return light != null
            && light.type == LightType.Point
            && Mathf.Approximately(
                light.intensity,
                LightIntensity)
            && Mathf.Approximately(light.range, LightRange)
            && Approximately(light.color, LightColor)
            && light.shadows == LightShadows.None
            && light.lightmapBakeType == LightmapBakeType.Realtime
            && Vector3.Distance(
                light.transform.localPosition,
                LocalPosition) < 0.001f;
    }

    private static void ApplyPolicy(Light light)
    {
        Transform lightTransform = light.transform;
        lightTransform.localPosition = LocalPosition;
        lightTransform.localRotation = Quaternion.identity;
        lightTransform.localScale = Vector3.one;

        light.type = LightType.Point;
        light.color = LightColor;
        light.intensity = LightIntensity;
        light.range = LightRange;
        light.bounceIntensity = 0f;
        light.shadows = LightShadows.None;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        light.renderMode = LightRenderMode.Auto;
        light.cullingMask = Physics.AllLayers;
        light.enabled = true;
        EditorUtility.SetDirty(light);
    }

    private static bool Approximately(Color left, Color right)
    {
        return Mathf.Abs(left.r - right.r) < 0.001f
            && Mathf.Abs(left.g - right.g) < 0.001f
            && Mathf.Abs(left.b - right.b) < 0.001f
            && Mathf.Abs(left.a - right.a) < 0.001f;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
