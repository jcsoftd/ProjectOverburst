using System;
using System.Collections.Generic;
using HighlightPlus;
using UnityEngine;

public enum OverburstWorldHighlightStyle { ItemHover, InteractionHover, PlayerOcclusion }

/// <summary>입력과 대상 선택을 소유하지 않는 월드 하이라이트 표현 어댑터.</summary>
public sealed class OverburstWorldHighlight : IDisposable
{
    private readonly OverburstWorldHighlightStyle style;
    private GameObject owner;
    private HighlightEffect effect;
    private Transform target;
    private Renderer[] targetRenderers;
    public int RendererCount => effect != null && owner.activeSelf ? effect.includedObjectsCount : 0;
    public HighlightEffect Effect => effect;

    public OverburstWorldHighlight(OverburstWorldHighlightStyle style) => this.style = style;
    public static string ProfileResource(OverburstWorldHighlightStyle style) => "UI/World/HighlightPlus/HP_" + style;

    public void SetTarget(Transform next, Renderer[] renderers)
    {
        if (style == OverburstWorldHighlightStyle.PlayerOcclusion && next != null && renderers != null)
            renderers = FilterPlayerModelRenderers(next, renderers);
        if (next == null || renderers == null || renderers.Length == 0) { Clear(); return; }
        if (next == target && effect != null && owner.activeSelf && SameRenderers(renderers)) return;
        EnsureEffect();
        effect.SetHighlighted(false);
        owner.SetActive(true);
        // NPCs use the renderer's current skinned pose. Baking for instanced outlines
        // can duplicate vendor skeleton scale and produce a flattened ground silhouette.
        if (style == OverburstWorldHighlightStyle.InteractionHover)
            effect.optimizeSkinnedMesh = !Array.Exists(renderers, renderer => renderer is SkinnedMeshRenderer);
        effect.SetTargets(next, renderers);
        target = next;
        targetRenderers = (Renderer[])renderers.Clone();
        effect.SetHighlighted(style != OverburstWorldHighlightStyle.PlayerOcclusion);
    }

    private bool SameRenderers(Renderer[] renderers)
    {
        if (targetRenderers == null || targetRenderers.Length != renderers.Length) return false;
        for (int i = 0; i < renderers.Length; i++)
            if (targetRenderers[i] != renderers[i]) return false;
        return true;
    }

    private void EnsureEffect()
    {
        if (effect != null) return;
        owner = new GameObject("__OverburstHighlight_" + style) { hideFlags = HideFlags.DontSave };
        if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(owner);
        effect = owner.AddComponent<HighlightEffect>();
        HighlightProfile profile = Resources.Load<HighlightProfile>(ProfileResource(style));
        if (profile != null) effect.ProfileLoad(profile);
        else Configure(effect, style);
        effect.effectGroup = TargetOptions.Scripting;
    }

    public static void Configure(HighlightEffect effect, OverburstWorldHighlightStyle style)
    {
        effect.reflectionProbes = false;
        effect.ignoreObjectVisibility = false;
        effect.combineMeshes = false;
        effect.constantWidth = true;
        effect.fadeInDuration = 0.06f;
        effect.fadeOutDuration = 0.06f;
        effect.outline = style == OverburstWorldHighlightStyle.PlayerOcclusion ? 0f : 1f;
        effect.outlineWidth = style == OverburstWorldHighlightStyle.ItemHover ? 0.22f : 0.16f;
        effect.outlineColor = new Color(1f, 0.86f, 0.58f, 1f);
        effect.outlineQuality = HighlightPlus.QualityLevel.High;
        effect.outlineVisibility = Visibility.Normal;
        effect.glow = 0f;
        effect.innerGlow = 0f;
        effect.overlay = 0f;
        effect.focus = 0f;
        effect.targetFX = false;
        effect.iconFX = false;
        effect.labelEnabled = false;
        effect.seeThrough = style == OverburstWorldHighlightStyle.PlayerOcclusion
            ? SeeThroughMode.AlwaysWhenOccluded : SeeThroughMode.Never;
        effect.seeThroughOccluderMask = LayerMask.GetMask("Default", "Ground", "PlayerBoundary");
        effect.seeThroughOccluderMaskAccurate = true;
        effect.seeThroughOccluderCheckInterval = 0.1f;
        effect.seeThroughIntensity = 0.42f;
        effect.seeThroughTintAlpha = 0.85f;
        effect.seeThroughTintColor = new Color(0.72f, 0.89f, 0.87f, 1f);
        effect.seeThroughNoise = 0.15f;
        effect.seeThroughBorder = 0.35f;
        effect.seeThroughBorderColor = new Color(0.85f, 1f, 0.97f, 1f);
        effect.seeThroughBorderWidth = 0.18f;
        effect.UpdateMaterialProperties();
    }

    public static Renderer[] CollectModelRenderers(Transform root)
    {
        if (root == null) return Array.Empty<Renderer>();
        var result = new List<Renderer>();
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer) continue;
            if (renderer.GetComponentInParent<ParticleSystem>() != null
                || renderer.gameObject.name == WorldItemPickupHoverResolver.OutlineObjectName
                || renderer.gameObject.name.StartsWith("__OverburstHighlight_", StringComparison.Ordinal)) continue;
            result.Add(renderer);
        }
        return result.ToArray();
    }

    /// <summary>캐릭터 가림 표시는 모델 표면만 포함하고 투명 이펙트의 평면은 제외한다.</summary>
    public static Renderer[] CollectPlayerModelRenderers(Transform root) =>
        root != null ? FilterPlayerModelRenderers(root, CollectModelRenderers(root)) : Array.Empty<Renderer>();

    private static Renderer[] FilterPlayerModelRenderers(Transform root, Renderer[] candidates)
    {
        var facingEffects = root.GetComponentsInChildren<PlayerCombatFacingVfx>(true);
        var result = new List<Renderer>(candidates.Length);
        foreach (Renderer renderer in candidates)
        {
            if (renderer == null) continue;
            bool isFacingEffect = false;
            foreach (PlayerCombatFacingVfx facing in facingEffects)
                if (facing.VisualRoot != null && renderer.transform.IsChildOf(facing.VisualRoot))
                { isFacingEffect = true; break; }
            if (isFacingEffect) continue;
            // Skinned character surfaces can use transparent hair/cloth materials.
            // Static effect planes only have transparent or additive materials.
            if (renderer is SkinnedMeshRenderer || HasModelSurface(renderer)) result.Add(renderer);
        }
        return result.ToArray();
    }

    private static bool HasModelSurface(Renderer renderer)
    {
        foreach (Material material in renderer.sharedMaterials)
        {
            if (material == null || material.shader == null) continue;
            string renderType = material.GetTag("RenderType", true, "");
            bool transparent = renderType.StartsWith("Transparent", StringComparison.OrdinalIgnoreCase)
                && !renderType.Equals("TransparentCutout", StringComparison.OrdinalIgnoreCase);
            if (material.renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent && !transparent) return true;
        }
        return false;
    }

    public void Clear()
    {
        target = null;
        targetRenderers = null;
        if (effect != null) { effect.SetHighlighted(false); owner.SetActive(false); }
    }

    public void Dispose()
    {
        Clear();
        if (owner != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(owner);
            else UnityEngine.Object.DestroyImmediate(owner);
        }
        owner = null;
        effect = null;
    }
}
