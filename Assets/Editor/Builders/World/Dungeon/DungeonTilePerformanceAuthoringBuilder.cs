using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class DungeonTilePerformanceAuthoringBuilder
{
    private const string LogPath =
        "Logs/DungeonTilePerformanceAuthoring.log";

    [MenuItem(
        "OVERBURST/Codex/Setup/World/Dungeon/"
        + "Apply Tile Performance Policy")]
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
        IReadOnlyList<string> tileNames =
            DungeonTileFamilyWeightUtility.GetProjectTileNames();
        StringBuilder report = new();
        report.AppendLine(
            "[DungeonTilePerformanceAuthoringBuilder] PASS");

        int changedPrefabCount = 0;
        int totalLocalLightCount = 0;
        int totalParticleRendererCount = 0;
        int removedLocalLightShadowCount = 0;
        int removedParticleShadowCount = 0;
        int maximumLocalLightsPerTile = 0;

        for (int i = 0; i < tileNames.Count; i++)
        {
            string tileName = tileNames[i];
            string path =
                DungeonContentAuthoringBuilder.GetRuntimeTilePath(tileName);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                int localLightCount = 0;
                int localShadowChangeCount = 0;
                int particleRendererCount = 0;
                int particleShadowChangeCount = 0;

                Light[] lights =
                    root.GetComponentsInChildren<Light>(true);
                for (int lightIndex = 0;
                     lightIndex < lights.Length;
                     lightIndex++)
                {
                    Light light = lights[lightIndex];
                    if (light == null
                        || light.type == LightType.Directional)
                    {
                        continue;
                    }

                    localLightCount++;
                    if (light.shadows == LightShadows.None)
                        continue;

                    light.shadows = LightShadows.None;
                    EditorUtility.SetDirty(light);
                    localShadowChangeCount++;
                    changed = true;
                }

                ParticleSystemRenderer[] particleRenderers =
                    root.GetComponentsInChildren<
                        ParticleSystemRenderer>(true);
                for (int rendererIndex = 0;
                     rendererIndex < particleRenderers.Length;
                     rendererIndex++)
                {
                    ParticleSystemRenderer renderer =
                        particleRenderers[rendererIndex];
                    if (renderer == null)
                        continue;

                    particleRendererCount++;
                    bool rendererChanged = false;
                    if (renderer.shadowCastingMode
                        != ShadowCastingMode.Off)
                    {
                        renderer.shadowCastingMode =
                            ShadowCastingMode.Off;
                        rendererChanged = true;
                    }

                    if (renderer.receiveShadows)
                    {
                        renderer.receiveShadows = false;
                        rendererChanged = true;
                    }

                    if (!rendererChanged)
                        continue;

                    EditorUtility.SetDirty(renderer);
                    particleShadowChangeCount++;
                    changed = true;
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(
                        root,
                        path,
                        out bool saved);
                    Require(saved, "타일 프리팹 저장 실패: " + path);
                    changedPrefabCount++;
                }

                totalLocalLightCount += localLightCount;
                totalParticleRendererCount += particleRendererCount;
                removedLocalLightShadowCount +=
                    localShadowChangeCount;
                removedParticleShadowCount +=
                    particleShadowChangeCount;
                maximumLocalLightsPerTile = Mathf.Max(
                    maximumLocalLightsPerTile,
                    localLightCount);

                report.AppendLine(
                    $"{tileName}: LocalLights={localLightCount}, "
                    + $"ParticleRenderers={particleRendererCount}, "
                    + $"LightShadowChanges={localShadowChangeCount}, "
                    + $"ParticleShadowChanges="
                    + particleShadowChangeCount);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(
            ImportAssetOptions.ForceSynchronousImport);
        string validation = ValidateOrThrow();

        report.AppendLine($"ChangedPrefabCount={changedPrefabCount}");
        report.AppendLine($"LocalLightCount={totalLocalLightCount}");
        report.AppendLine(
            $"MaximumLocalLightsPerTile={maximumLocalLightsPerTile}");
        report.AppendLine(
            $"ParticleRendererCount={totalParticleRendererCount}");
        report.AppendLine(
            "RemovedLocalLightShadowCount="
            + removedLocalLightShadowCount);
        report.AppendLine(
            "RemovedParticleShadowCount="
            + removedParticleShadowCount);
        report.AppendLine(
            "LocalLightBudget=VisibleRoomsOnly_Depth2");
        report.AppendLine(validation);
        return report.ToString().TrimEnd();
    }

    public static string ValidateOrThrow()
    {
        IReadOnlyList<string> tileNames =
            DungeonTileFamilyWeightUtility.GetProjectTileNames();
        int localLightCount = 0;
        int particleRendererCount = 0;

        for (int i = 0; i < tileNames.Count; i++)
        {
            string path =
                DungeonContentAuthoringBuilder.GetRuntimeTilePath(
                    tileNames[i]);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Light[] lights =
                    root.GetComponentsInChildren<Light>(true);
                for (int lightIndex = 0;
                     lightIndex < lights.Length;
                     lightIndex++)
                {
                    Light light = lights[lightIndex];
                    if (light == null
                        || light.type == LightType.Directional)
                    {
                        continue;
                    }

                    localLightCount++;
                    Require(
                        light.shadows == LightShadows.None,
                        tileNames[i]
                        + ": 지역 조명 실시간 그림자 잔존");
                }

                ParticleSystemRenderer[] particleRenderers =
                    root.GetComponentsInChildren<
                        ParticleSystemRenderer>(true);
                for (int rendererIndex = 0;
                     rendererIndex < particleRenderers.Length;
                     rendererIndex++)
                {
                    ParticleSystemRenderer renderer =
                        particleRenderers[rendererIndex];
                    if (renderer == null)
                        continue;

                    particleRendererCount++;
                    Require(
                        renderer.shadowCastingMode
                            == ShadowCastingMode.Off
                        && !renderer.receiveShadows,
                        tileNames[i]
                        + ": 파티클 그림자 설정 잔존");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        return "[DungeonTilePerformanceValidator] PASS\n"
            + $"TileCount={tileNames.Count}\n"
            + $"LocalLightCount={localLightCount}\n"
            + $"ParticleRendererCount={particleRendererCount}\n"
            + "LocalLightRealtimeShadows=0\n"
            + "ParticleShadowRenderers=0";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
