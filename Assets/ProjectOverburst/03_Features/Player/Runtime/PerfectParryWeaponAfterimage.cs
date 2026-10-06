using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Same mesh-pose capture, shared shader and squared fade used by PlayerDashVfx.
// Only the equipped weapon is sampled; the actual parry motion owns the emission window.
[DefaultExecutionOrder(460), DisallowMultipleComponent]
public sealed class PerfectParryWeaponAfterimage : MonoBehaviour
{
    private const int Capacity = 4, PieceLimit = 4;
    private static readonly int TintId = Shader.PropertyToID("_Tint");
    private static readonly int AccentId = Shader.PropertyToID("_AccentTint");
    private readonly Frame[] frames = new Frame[Capacity];
    private readonly List<Renderer> sources = new List<Renderer>(PieceLimit);
    private MaterialPropertyBlock properties;
    private PlayerEquipment equipment;
    private PlayerParryController parry;
    private MeleeRuntime melee;
    private PerfectParryContactPresenter contact;
    private Transform sourceWeapon;
    private GameObject root;
    private float clock, nextSample;
    private int nextFrame;
    private bool emitting;
    private Vector3 lastTip;
    private Quaternion lastRotation;
    public int CapturedCount { get; private set; }
    public int ActiveCount { get { int count = 0; foreach (var f in frames) if (f != null && f.active) count++; return count; } }
    public bool IsEmitting => emitting;
    private sealed class Piece
    {
        public Transform transform;
        public MeshFilter filter;
        public MeshRenderer renderer;
        public Mesh baked;
        public int materialCount;
    }
    private sealed class Frame
    {
        public readonly List<Piece> pieces = new List<Piece>(PieceLimit);
        public float born, lifetime, opacity;
        public bool active;
    }
    private void Awake()
    {
        equipment = GetComponent<PlayerEquipment>(); parry = GetComponent<PlayerParryController>();
        melee = GetComponent<MeleeRuntime>(); contact = GetComponent<PerfectParryContactPresenter>();
        properties = new MaterialPropertyBlock();
    }
    private void LateUpdate()
    {
        var profile = PerfectParryContactProfile.Current;
        if (profile == null || !profile.IsReady || !profile.upswingAfterimage || OverburstGameSettings.HitEffectScale <= 0f
            || equipment == null || equipment.CurrentWeaponRoot == null || melee == null || !melee.IsHeavyAttackInProgress)
        { Clear(); return; }
        if (MeleeRuntime.IsHeavyParryClockPaused) return;
        clock += Time.unscaledDeltaTime;
        foreach (var frame in frames)
        {
            if (frame == null || !frame.active) continue;
            float age = Mathf.Clamp01((clock - frame.born) / frame.lifetime);
            if (age >= 1f) { Hide(frame); continue; }
            var tint = new Color(1f, .97f, .84f, frame.opacity * (1f - age) * (1f - age));
            properties.SetColor(TintId, tint); properties.SetColor(AccentId, new Color(1f, .86f, .42f, tint.a));
            foreach (var piece in frame.pieces) if (piece.renderer != null && piece.renderer.enabled) piece.renderer.SetPropertyBlock(properties);
        }
        bool eligible = parry != null && parry.ActionGrade == ParryGrade.Perfect && melee.IsHeavyParryBladeMotion;
        if (!eligible) { emitting = false; return; }
        if (sourceWeapon != equipment.CurrentWeaponRoot) { Clear(); sourceWeapon = equipment.CurrentWeaponRoot; CacheSources(); }
        if (!emitting)
        {
            emitting = true; nextSample = clock + .024f;
            lastTip = equipment.CurrentWeaponTraceBinding?.WeaponTip != null ? equipment.CurrentWeaponTraceBinding.WeaponTip.position : sourceWeapon.position;
            lastRotation = sourceWeapon.rotation;
            Capture(profile);
        }
        if (clock < nextSample) return;
        var tip = equipment.CurrentWeaponTraceBinding?.WeaponTip;
        Vector3 position = tip != null ? tip.position : sourceWeapon.position;
        if ((position - lastTip).sqrMagnitude < .075f * .075f && Quaternion.Angle(lastRotation, sourceWeapon.rotation) < 8f) return;
        Capture(profile);
        nextSample = clock + .024f; lastTip = position; lastRotation = sourceWeapon.rotation;
    }
    private void CacheSources()
    {
        sources.Clear();
        foreach (var renderer in sourceWeapon.GetComponentsInChildren<Renderer>(true))
        {
            if (sources.Count >= PieceLimit) break;
            if (!renderer.enabled || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
            var skin = renderer as SkinnedMeshRenderer;
            var filter = renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>() : null;
            if ((skin != null && skin.sharedMesh != null) || (filter != null && filter.sharedMesh != null)) sources.Add(renderer);
        }
    }
    private void EnsureRoot()
    {
        if (root != null) return;
        // A content-scene unload may destroy the pool between actions.
        ReleaseBakedMeshes();
        for (int i = 0; i < frames.Length; i++) frames[i] = null;
        root = new GameObject("Perfect Parry Weapon Afterimages") { hideFlags = HideFlags.DontSave };
        Scene scene = contact != null ? contact.ContentScene : gameObject.scene;
        if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(root, scene);
    }
    private void Capture(PerfectParryContactProfile profile)
    {
        EnsureRoot();
        var frame = frames[nextFrame] ?? (frames[nextFrame] = new Frame()); nextFrame = (nextFrame + 1) % Capacity; Hide(frame);
        int index = 0;
        foreach (var source in sources)
        {
            if (source == null || !source.enabled || !source.gameObject.activeInHierarchy) continue;
            if (index == frame.pieces.Count)
            {
                var go = new GameObject("Parry Weapon Pose") { hideFlags = HideFlags.DontSave, layer = source.gameObject.layer };
                go.transform.SetParent(root.transform, false);
                var piece = new Piece { transform = go.transform, filter = go.AddComponent<MeshFilter>(), renderer = go.AddComponent<MeshRenderer>() };
                piece.renderer.shadowCastingMode = ShadowCastingMode.Off; piece.renderer.receiveShadows = false;
                piece.renderer.lightProbeUsage = LightProbeUsage.Off; piece.renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                frame.pieces.Add(piece);
            }
            var target = frame.pieces[index++]; var skin = source as SkinnedMeshRenderer;
            if (skin != null)
            {
                if (target.baked == null) { target.baked = new Mesh { name = "Parry Weapon Pose Mesh", hideFlags = HideFlags.DontSave }; target.baked.MarkDynamic(); }
                skin.BakeMesh(target.baked, false); target.filter.sharedMesh = target.baked;
            }
            else target.filter.sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            if (target.materialCount != target.filter.sharedMesh.subMeshCount)
            {
                var materials = new Material[target.filter.sharedMesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = profile.upswingAfterimageMaterial;
                target.renderer.sharedMaterials = materials; target.materialCount = materials.Length;
            }
            target.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation); target.transform.localScale = source.transform.lossyScale;
            target.renderer.enabled = true;
            properties.SetColor(TintId, new Color(1f, .97f, .84f, profile.upswingAfterimageOpacity * OverburstGameSettings.HitEffectScale));
            properties.SetColor(AccentId, new Color(1f, .86f, .42f, profile.upswingAfterimageOpacity)); target.renderer.SetPropertyBlock(properties);
        }
        frame.active = index > 0; frame.born = clock; frame.lifetime = Mathf.Clamp(profile.upswingAfterimageLifetime, .12f, .4f);
        frame.opacity = profile.upswingAfterimageOpacity * OverburstGameSettings.HitEffectScale;
        if (frame.active) CapturedCount++;
    }
    private static void Hide(Frame frame)
    { frame.active = false; foreach (var piece in frame.pieces) if (piece.renderer != null) piece.renderer.enabled = false; }
    private void Clear()
    { emitting = false; foreach (var frame in frames) if (frame != null) Hide(frame); }
    private void OnDisable() { Clear(); }
    private void ReleaseBakedMeshes()
    {
        foreach (var frame in frames) if (frame != null) foreach (var piece in frame.pieces) if (piece.baked != null) DestroyOwned(piece.baked);
    }
    private void OnDestroy()
    {
        ReleaseBakedMeshes();
        if (root != null) DestroyOwned(root);
    }
    private static void DestroyOwned(Object value)
    { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
}
