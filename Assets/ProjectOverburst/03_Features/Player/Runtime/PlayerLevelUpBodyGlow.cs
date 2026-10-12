using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The package demo animates a second Glow material. Keep its colour curves without playing its pose.
public sealed class PlayerLevelUpBodyGlow : MonoBehaviour
{
    public const string RendererName = "LevelUpBodyGlow";
    [SerializeField] private Material sourceMaterial;
    [SerializeField] private AnimationCurve red, green, blue;
    private readonly List<GameObject> shells = new List<GameObject>();
    private Material material;
    private float elapsed, duration;

    public void Configure(Material source, AnimationCurve r, AnimationCurve g, AnimationCurve b)
    { sourceMaterial = source; red = r; green = g; blue = b; }

    public void Begin(PlayerActorRuntime actor)
    {
        Clear();
        if (actor == null || sourceMaterial == null || red == null || red.length == 0) return;
        duration = red.keys[red.length - 1].time;
        material = new Material(sourceMaterial) { name = "PlayerLevelUpBodyGlow (Runtime)" };
        material.SetColor("_Color", Evaluate(0f));
        foreach (var body in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!body.enabled || !body.gameObject.activeInHierarchy || body.sharedMesh == null) continue;
            var shell = new GameObject(RendererName); shell.layer = body.gameObject.layer;
            shell.transform.SetParent(body.transform, false); shells.Add(shell);
            var renderer = shell.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = body.sharedMesh; renderer.bones = body.bones; renderer.rootBone = body.rootBone;
            renderer.localBounds = body.localBounds; renderer.quality = body.quality;
            var materials = new Material[body.sharedMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            for (int i = 0; i < body.sharedMesh.blendShapeCount; i++) renderer.SetBlendShapeWeight(i, body.GetBlendShapeWeight(i));
        }
    }
    private Color Evaluate(float time) => new Color(red.Evaluate(time), green.Evaluate(time), blue.Evaluate(time), 1f);
    private void LateUpdate()
    {
        if (material == null) return;
        elapsed += Time.deltaTime;
        if (elapsed >= duration) { Clear(); return; }
        material.SetColor("_Color", Evaluate(elapsed));
    }
    private void Clear()
    {
        foreach (var shell in shells) if (shell != null) { shell.SetActive(false); Destroy(shell); }
        shells.Clear();
        if (material != null) Destroy(material);
        material = null; elapsed = 0f;
    }
    private void OnDisable() => Clear();
}
