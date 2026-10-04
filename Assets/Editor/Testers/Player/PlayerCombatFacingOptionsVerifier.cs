using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>실제 환경설정 입력·정지 중 반영·저장·두 형태/밝기의 native 렌더를 검사한다.</summary>
public static class PlayerCombatFacingOptionsVerifier
{
    public static void VerifyLive(PlayerCombatFacingVfx effect, OverburstSettingsPanel panel, OverburstGameMenu menu, string output, string runId = null)
    {
        runId = runId ?? Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(output);
        PrepareResult(output, runId, "RUNNING");
        var checks = new List<string>();
        void Check(bool passed, string detail) { if (!passed) throw new Exception(detail); checks.Add(detail); }
        var errors = new List<string>();
        Exception failure = null;
        bool restored = false;
        string settingsPath = OverburstGameSettings.FilePath;
        string isolated = Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY");
        // 옵션 검증은 실제 PC 설정을 대상으로 실행하지 않는다.
        bool ownsSettings = !string.IsNullOrEmpty(isolated)
            && string.Equals(Path.GetDirectoryName(settingsPath), isolated, StringComparison.OrdinalIgnoreCase)
            && IsolatedSavePlayGuard.ValidateDirectory(isolated) == isolated;
        if (!ownsSettings)
        {
            File.WriteAllText(Path.Combine(output, "options_play_validation.json"), JsonConvert.SerializeObject(new { status = "FAIL", runId, checks, errors = new[] { "Owned isolated settings required" }, settingsRestored = false }, Formatting.Indented));
            throw new InvalidOperationException("옵션 검증은 소유 격리 설정에서만 실행합니다.");
        }
        var originalStyle = OverburstGameSettings.CombatFacingStyle;
        float originalBrightness = OverburstGameSettings.CombatFacingBrightness;
        bool originalEnabled = OverburstGameSettings.CombatFacingIndicator;
        float originalShake = OverburstGameSettings.CameraShakeScale, originalHit = OverburstGameSettings.HitEffectScale;
        var dirtyField = typeof(OverburstGameSettings).GetField("dirty", BindingFlags.Static | BindingFlags.NonPublic);
        bool originalDirty = (bool)dirtyField.GetValue(null);
        byte[] originalFile = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        try
        {
            Check(panel != null && panel.combatFacingStyle != null && panel.combatFacingBrightness != null && panel.combatFacingIndicator != null, "실제 환경설정 모양·밝기 컨트롤 존재");
            var renderers = effect.VisualRoot.GetComponentsInChildren<Renderer>(true);
            var originalMaterials = renderers.SelectMany(r => r.sharedMaterials).ToArray();
            bool originalActive = effect.VisualRoot.gameObject.activeSelf;
            float originalVisibility = effect.Visibility, originalTime = effect.FlowTime;
            var block = new MaterialPropertyBlock();
            int brightnessId = Shader.PropertyToID("_SilverBrightness");
            for (int repeat = 0; repeat < 3; repeat++)
            {
                foreach (int style in new[] { 0, 1 })
                {
                    panel.combatFacingStyle.SelectOptionByIndex(style);
                    Check((int)OverburstGameSettings.CombatFacingStyle == style && (int)effect.AppliedStyle == style, "정지 중 형태 즉시 교체 " + repeat + "/" + style);
                    Check(PlayerCombatFacingOptionsBuilder.Nodes.Select((node, i) =>
                        AssetDatabase.GetAssetPath(effect.VisualRoot.Find(node).GetComponent<MeshFilter>().sharedMesh) == PlayerCombatFacingOptionsBuilder.MeshPath(style == 1, i)).All(x => x), "선택한6메시 정확한 연결 " + repeat + "/" + style);
                    foreach (float brightness in new[] { 0f, .25f, 1f, 2f })
                    {
                        panel.combatFacingBrightness.value = brightness;
                        Check(Mathf.Approximately(OverburstGameSettings.CombatFacingBrightness, brightness), "밝기 UI 입력 " + repeat + "/" + style + "/" + brightness);
                        Check(renderers.All(r => { r.GetPropertyBlock(block); return Mathf.Approximately(block.GetFloat(brightnessId), brightness); }), "모든 렌더러 밝기 즉시 반영 " + repeat + "/" + style + "/" + brightness);
                    }
                }
            }
            Check(Mathf.Approximately(Time.timeScale, 0) && Mathf.Approximately(originalTime, effect.FlowTime), "정지 상태와 빛결 시계 보존");
            Check(effect.VisualRoot.gameObject.activeSelf == originalActive && Mathf.Approximately(effect.Visibility, originalVisibility), "형태/밝기 조절 시 가시성·페이드 보존");
            Check(originalMaterials.SequenceEqual(renderers.SelectMany(r => r.sharedMaterials)), "공유 재질 보존·추가 렌더러 없음");
            panel.combatFacingIndicator.isOn = false;
            panel.combatFacingStyle.SelectOptionByIndex(0); panel.combatFacingBrightness.value = .6f;
            Check(!effect.IsVisible && !effect.VisualRoot.gameObject.activeSelf, "꺼진 상태에서 형태/밝기 조절해도 숨김 유지");
            OverburstGameSettings.SaveIfDirty();
            ReloadSettings();
            Check(OverburstGameSettings.CombatFacingStyle == CombatFacingIndicatorStyle.Quiet && Mathf.Approximately(OverburstGameSettings.CombatFacingBrightness, .6f), "형태·밝기 디스크 저장/재로드");
            menu.CloseSettings(); menu.OpenSettings(); panel.tabs[2].isOn = true;
            Check(panel.combatFacingStyle.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.name == "Text" && t.text == "기존 절제형")
                && Mathf.Approximately(panel.combatFacingBrightness.value, .6f), "설정 재진입 선택값/밝기 표시 복원");
            panel.resetButton.onClick.Invoke();
            Check(OverburstGameSettings.CombatFacingIndicator && OverburstGameSettings.CombatFacingStyle == CombatFacingIndicatorStyle.Extended && Mathf.Approximately(OverburstGameSettings.CombatFacingBrightness, 1f), "기본값 복구 ON·끝 연장형·100%");
            OverburstGameSettings.CombatFacingBrightness = -1; Check(OverburstGameSettings.CombatFacingBrightness == 0, "밝기 하한 보정");
            OverburstGameSettings.CombatFacingBrightness = 4; Check(OverburstGameSettings.CombatFacingBrightness == 2, "밝기 상한 보정");
            OverburstGameSettings.CombatFacingBrightness = float.NaN; Check(OverburstGameSettings.CombatFacingBrightness == 1, "잘못된 밝기 기본값 보정");
            CaptureNative(output);
            }
        catch (Exception e) { failure = e; errors.Add(e.ToString()); throw; }
        finally
        {
            try
            {
                if (!string.Equals(OverburstGameSettings.FilePath, settingsPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Owned settings path changed; another path was preserved");
                OverburstGameSettings.CombatFacingStyle = originalStyle;
                OverburstGameSettings.CombatFacingBrightness = originalBrightness;
                OverburstGameSettings.CombatFacingIndicator = originalEnabled;
                OverburstGameSettings.CameraShakeScale = originalShake;
                OverburstGameSettings.HitEffectScale = originalHit;
                if (panel != null) typeof(OverburstSettingsPanel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
                if (originalFile != null) File.WriteAllBytes(settingsPath, originalFile);
                else if (File.Exists(settingsPath)) File.Delete(settingsPath);
                dirtyField.SetValue(null, originalDirty);
                restored = true;
            }
            catch (Exception e) { errors.Add("Option restore: " + e); if (failure == null) throw; }
            finally
            {
                File.WriteAllText(Path.Combine(output, "options_play_validation.json"), JsonConvert.SerializeObject(new { status = errors.Count == 0 ? "PASS" : "FAIL", runId, checks, errors, settingsRestored = restored, repetitions = 3, styles = 2, brightnessLevels = new[] { 0f, .25f, 1f, 2f }, materialsPreserved = true, pausedImmediateApply = true }, Formatting.Indented));
            }
        }
    }

    internal static void PrepareResult(string output, string runId, string status = "NOT_RUN")
    {
        File.WriteAllText(Path.Combine(output, "options_play_validation.json"), JsonConvert.SerializeObject(new { status, runId, checks = Array.Empty<string>(), errors = Array.Empty<string>() }, Formatting.Indented));
    }

    static void ReloadSettings() => typeof(OverburstGameSettings).GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);

    static void CaptureNative(string output)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture rt = null; Texture2D png = null; Camera camera = null;
        bool oldAsync = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            var effect = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCombatFacingVfxBuilder.EffectPath), scene);
            effect.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); effect.transform.localScale = Vector3.one * .45f;
            effect.SetActive(true);
            foreach (var t in effect.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
            var cameraObject = new GameObject("Facing options native camera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.AddComponent<Camera>(); camera.scene = scene; camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .03f, .025f); camera.allowHDR = true;
            camera.orthographic = true; camera.orthographicSize = .72f; camera.aspect = 1;
            camera.transform.position = Vector3.up * 8 + Vector3.forward * .07f; camera.transform.LookAt(Vector3.forward * .07f, Vector3.back);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            rt = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGBHalf); rt.Create();
            camera.targetTexture = rt;
            var block = new MaterialPropertyBlock();
            foreach (bool extended in new[] { false, true })
            {
                for (int i = 0; i < PlayerCombatFacingOptionsBuilder.Nodes.Length; i++)
                    effect.transform.Find(PlayerCombatFacingOptionsBuilder.Nodes[i]).GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(PlayerCombatFacingOptionsBuilder.MeshPath(extended, i));
                foreach (float brightness in new[] { 0f, .25f, 1f, 2f })
                {
                    foreach (var r in effect.GetComponentsInChildren<Renderer>(true))
                    {
                        r.GetPropertyBlock(block); block.SetFloat("_SilverRuntimeClock", 1); block.SetFloat("_SilverRuntimeTime", 2);
                        block.SetFloat("_SilverVisibility", 1); block.SetFloat("_SilverBrightness", brightness); r.SetPropertyBlock(block);
                    }
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                        RenderTexture.active = rt; png = new Texture2D(768, 768, TextureFormat.RGB24, false);
                        png.ReadPixels(new Rect(0, 0, 768, 768), 0, 0); png.Apply();
                        string path = Path.Combine(output, "Native", (extended ? "Extended" : "Quiet") + "_" + Mathf.RoundToInt(brightness * 100) + ".png");
                        Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, png.EncodeToPNG());
                    }
                    finally { RenderTexture.active = previous; if (png != null) { Object.DestroyImmediate(png); png = null; } }
                }
            }
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = oldAsync;
            if (png != null) Object.DestroyImmediate(png);
            if (camera != null) camera.targetTexture = null;
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
