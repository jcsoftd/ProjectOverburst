using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>실제 플레이어 모델과 투명 VFX가 가림 효과에서 분리되는지 검사한다.</summary>
public static class PlayerOcclusionVfxRendererVerifier
{
    public static void VerifyPrefab(string output)
    {
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
            var facing = root.GetComponentInChildren<PlayerCombatFacingVfx>(true);
            if (facing == null || facing.VisualRoot == null) throw new Exception("플레이어 방향 표시 연결 없음");
            facing.VisualRoot.gameObject.SetActive(true);
            VerifyLive(root.transform, output);
        }
        finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void VerifyLive(Transform actor, string output)
    {
        var facing = actor.GetComponentInChildren<PlayerCombatFacingVfx>(true);
        var legacy = OverburstWorldHighlight.CollectModelRenderers(actor);
        var model = OverburstWorldHighlight.CollectPlayerModelRenderers(actor);
        var checks = new List<string>();
        void Check(bool passed, string detail)
        {
            if (!passed) throw new InvalidOperationException(detail);
            checks.Add(detail);
        }
        Check(model.Length > 0, "실제 캐릭터 모델 유지");
        Check(model.All(r => facing == null || facing.VisualRoot == null || !r.transform.IsChildOf(facing.VisualRoot)), "전투 방향 표시의 모든 렌더러 제외");
        Check(legacy.Where(r => r is SkinnedMeshRenderer).All(model.Contains), "현재 스킨 모델 유지");
        var fixtures = new List<GameObject>();
        Material opaque = null, transparent = null, cutout = null;
        OverburstWorldHighlight visual = null;
        try
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new Exception("검증용 URP 셰이더 없음");
            opaque = new Material(shader) { renderQueue = 2000 };
            opaque.SetOverrideTag("RenderType", "Opaque");
            transparent = new Material(shader) { renderQueue = 3000 };
            transparent.SetOverrideTag("RenderType", "Transparent");
            cutout = new Material(shader) { renderQueue = 2450 };
            cutout.SetOverrideTag("RenderType", "TransparentCutout");
            Renderer Make(string name, Material material)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name; go.transform.SetParent(actor, false); go.transform.localScale = Vector3.one * .01f;
                go.GetComponent<Renderer>().sharedMaterial = material; fixtures.Add(go); return go.GetComponent<Renderer>();
            }
            var solid = Make("Occlusion verifier opaque equipment", opaque);
            var plane = Make("Occlusion verifier transparent effect plane", transparent);
            var leaves = Make("Occlusion verifier alpha-cutout surface", cutout);
            visual = new OverburstWorldHighlight(OverburstWorldHighlightStyle.PlayerOcclusion);
            for (int i = 0; i < 3; i++)
            {
                visual.SetTarget(actor, OverburstWorldHighlight.CollectModelRenderers(actor));
                var accepted = new List<Renderer>(); visual.Effect.GetRenderers(accepted);
                Check(accepted.Count > 0 && accepted.Contains(solid) && accepted.Contains(leaves), "불투명 장비·알파 컷아웃 유지 " + i);
                Check(!accepted.Contains(plane), "투명 이펙트 평면 제외 " + i);
                Check(accepted.All(r => facing == null || facing.VisualRoot == null || !r.transform.IsChildOf(facing.VisualRoot)), "실제 HighlightPlus 대상에서 방향 표시 제외 " + i);
                visual.Clear();
            }
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, Application.isPlaying ? "occlusion_play_validation.json" : "occlusion_prefab_validation.json"), JsonConvert.SerializeObject(new {
                status = "PASS", playing = Application.isPlaying, legacyRenderers = legacy.Length, modelRenderers = model.Length,
                removedNodes = legacy.Except(model).Select(r => r.name).ToArray(), checks, repetitions = 3
            }, Formatting.Indented));
        }
        finally
        {
            visual?.Dispose();
            foreach (var go in fixtures) if (go != null) Object.DestroyImmediate(go);
            if (opaque != null) Object.DestroyImmediate(opaque);
            if (transparent != null) Object.DestroyImmediate(transparent);
            if (cutout != null) Object.DestroyImmediate(cutout);
        }
    }
}
