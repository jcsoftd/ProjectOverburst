using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>현재 pickup 모델 하나에만 inverted-hull 외곽선을 붙였다가 안전하게 제거한다.</summary>
public sealed class WorldItemPickupHoverHighlight
{
    private readonly List<OutlineState> outlineStates = new List<OutlineState>();
    private Material outlineMaterial;
    private WorldItemPickup currentTarget;

    public WorldItemPickup CurrentTarget => currentTarget;
    public int HighlightedRendererCount => outlineStates.Count;

    public void SetOutlineMaterial(Material material)
    {
        if (outlineMaterial == material)
            return;

        Clear();
        outlineMaterial = material;
    }

    public void SetTarget(WorldItemPickup target, IReadOnlyList<Renderer> sourceRenderers)
    {
        if (target != null && ReferenceEquals(target, currentTarget))
            return;

        Clear();
        if (target == null || outlineMaterial == null || sourceRenderers == null)
            return;

        for (int i = 0; i < sourceRenderers.Count; i++)
        {
            Renderer source = sourceRenderers[i];
            if (source == null || source.gameObject.name == WorldItemPickupHoverResolver.OutlineObjectName)
                continue;

            OutlineState state = source switch
            {
                MeshRenderer meshRenderer => CreateMeshOutline(meshRenderer),
                SkinnedMeshRenderer skinnedRenderer => CreateSkinnedOutline(skinnedRenderer),
                _ => default // 등급 Particle/Trail VFX는 모델 외곽선에서 제외한다.
            };
            if (state.OutlineObject != null)
                outlineStates.Add(state);
        }

        currentTarget = target;
        RefreshSourceState();
    }

    public void RefreshSourceState()
    {
        for (int i = 0; i < outlineStates.Count; i++)
        {
            OutlineState state = outlineStates[i];
            bool visible = state.Source != null
                && state.Source.enabled
                && state.Source.gameObject.activeInHierarchy;
            if (state.OutlineRenderer != null)
                state.OutlineRenderer.enabled = visible;
            if (state.OutlineObject != null && state.OutlineObject.activeSelf != visible)
                state.OutlineObject.SetActive(visible);
        }
    }

    public void Clear()
    {
        for (int i = 0; i < outlineStates.Count; i++)
        {
            GameObject outlineObject = outlineStates[i].OutlineObject;
            if (outlineObject == null)
                continue;

            outlineObject.SetActive(false);
            if (Application.isPlaying)
                Object.Destroy(outlineObject);
            else
                Object.DestroyImmediate(outlineObject);
        }

        outlineStates.Clear();
        currentTarget = null;
    }

    private OutlineState CreateMeshOutline(MeshRenderer source)
    {
        MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
        if (sourceFilter == null || sourceFilter.sharedMesh == null)
            return default;

        GameObject outlineObject = CreateOutlineObject(source);
        MeshFilter filter = outlineObject.AddComponent<MeshFilter>();
        filter.sharedMesh = sourceFilter.sharedMesh;
        MeshRenderer renderer = outlineObject.AddComponent<MeshRenderer>();
        ConfigureRenderer(renderer, source);
        return new OutlineState(source, renderer, outlineObject);
    }

    private OutlineState CreateSkinnedOutline(SkinnedMeshRenderer source)
    {
        if (source.sharedMesh == null)
            return default;

        GameObject outlineObject = CreateOutlineObject(source);
        SkinnedMeshRenderer renderer = outlineObject.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = source.sharedMesh;
        renderer.bones = source.bones;
        renderer.rootBone = source.rootBone;
        renderer.localBounds = source.localBounds;
        renderer.quality = source.quality;
        renderer.updateWhenOffscreen = source.updateWhenOffscreen;
        ConfigureRenderer(renderer, source);
        return new OutlineState(source, renderer, outlineObject);
    }

    private static GameObject CreateOutlineObject(Renderer source)
    {
        var outlineObject = new GameObject(WorldItemPickupHoverResolver.OutlineObjectName)
        {
            hideFlags = HideFlags.DontSave,
            layer = source.gameObject.layer
        };
        Transform outlineTransform = outlineObject.transform;
        outlineTransform.SetParent(source.transform, false);
        outlineTransform.localPosition = Vector3.zero;
        outlineTransform.localRotation = Quaternion.identity;
        outlineTransform.localScale = Vector3.one;
        return outlineObject;
    }

    private void ConfigureRenderer(Renderer renderer, Renderer source)
    {
        int materialCount = Mathf.Max(1, source.sharedMaterials.Length);
        var materials = new Material[materialCount];
        for (int i = 0; i < materials.Length; i++)
            materials[i] = outlineMaterial;

        renderer.sharedMaterials = materials;
        renderer.enabled = source.enabled;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.forceRenderingOff = false;
        renderer.allowOcclusionWhenDynamic = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.rendererPriority = source.rendererPriority + 1;
        renderer.sortingLayerID = source.sortingLayerID;
        renderer.sortingOrder = source.sortingOrder + 1;
    }

    private readonly struct OutlineState
    {
        public Renderer Source { get; }
        public Renderer OutlineRenderer { get; }
        public GameObject OutlineObject { get; }

        public OutlineState(Renderer source, Renderer outlineRenderer, GameObject outlineObject)
        {
            Source = source;
            OutlineRenderer = outlineRenderer;
            OutlineObject = outlineObject;
        }
    }
}
