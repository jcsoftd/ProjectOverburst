using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Isolated visual review. Sampling never invokes combat or changes the production camera.
public sealed class CrustaspikanMaterialPreview : IDisposable
{
    PreviewRenderUtility renderer;
    GameObject visual, rig, comparison, boulder;
    Mesh groundMesh, comparisonMesh;
    SkinGeometry[] geometry;
    MeshFilter[] staticMeshes;
    Material groundMaterial, comparisonMaterial;
    RenderTexture surface;
    readonly EnemyBossMaterialCollection collection;
    readonly Vector3 rootPosition, rootScale;
    readonly Quaternion rootRotation;
    bool counted;
    bool firstRender = true;
    public static int LiveStages { get; private set; }
    public Camera Camera => renderer?.camera;
    public Transform AnimationRoot => rig != null ? rig.transform : null;
    public void SetHeldRockPreview(bool visible) { if (boulder != null) boulder.SetActive(visible); }
    public void OffsetActor(Vector3 offset)
    {
        if (visual != null) visual.transform.position += offset;
        if (boulder != null) boulder.transform.position += offset;
        var body = BodyBounds; body.center += offset; BodyBounds = body;
        var frame = body;
        if (comparison != null) frame.Encapsulate(comparison.GetComponent<Renderer>().bounds);
        if (boulder != null && boulder.activeSelf) frame.Encapsulate(boulder.GetComponent<Renderer>().bounds);
        FrameBounds = frame;
    }
    public Bounds BodyBounds { get; private set; }
    public Bounds FrameBounds { get; private set; }
    public float Yaw { get; set; } = 25f;
    public float Pitch { get; set; } = 18f;
    public AnimationClip Clip { get; private set; }
    public float Time { get; private set; }

    sealed class SkinGeometry : IDisposable
    {
        public readonly SkinnedMeshRenderer Renderer;
        public readonly Vector3[] Vertices;
        public readonly Matrix4x4[] Bindposes, Matrices;
        public readonly Transform[] Bones;
        public readonly byte[] Counts;
        public readonly BoneWeight1[] Weights;
        readonly Vector3[] normals, posed, posedNormals;
        readonly Vector4[] tangents, posedTangents;
        readonly Matrix4x4[] normalMatrices;
        readonly Mesh mesh;
        readonly GameObject holder;
        readonly bool initialEnabled;
        public SkinGeometry(SkinnedMeshRenderer skin, CrustaspikanMaterialPreview owner)
        {
            Renderer = skin; var mesh = skin.sharedMesh;
            Vertices = mesh.vertices; Bindposes = mesh.bindposes; Bones = skin.bones; Matrices = new Matrix4x4[Bones.Length];
            normalMatrices = new Matrix4x4[Bones.Length]; normals = mesh.normals; tangents = mesh.tangents;
            posed = new Vector3[Vertices.Length]; posedNormals = new Vector3[normals.Length]; posedTangents = new Vector4[tangents.Length];
            var counts = mesh.GetBonesPerVertex(); var weights = mesh.GetAllBoneWeights();
            try { Counts = counts.ToArray(); Weights = weights.ToArray(); }
            finally { counts.Dispose(); weights.Dispose(); }
            this.mesh = Object.Instantiate(mesh); this.mesh.name = "Crustaspikan review sampled skin"; this.mesh.hideFlags = HideFlags.HideAndDontSave; this.mesh.MarkDynamic();
            holder = owner.MeshObject("Sampled pose " + skin.name, this.mesh, skin.sharedMaterials.FirstOrDefault());
            holder.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials; holder.transform.SetParent(skin.transform, false);
            initialEnabled = skin.enabled;
        }
        public void ResetVisibility() { Renderer.enabled = initialEnabled; }
        public void Encapsulate(ref Bounds bounds, ref bool found)
        {
            bool visible = Renderer.enabled && Renderer.gameObject.activeInHierarchy;
            holder.SetActive(visible); Renderer.enabled = false;
            if (!visible) return;
            bool skinned = Bones.Length > 0 && Bones.Length == Bindposes.Length;
            for (int i = 0; i < Matrices.Length && skinned; i++)
            {
                Matrices[i] = Bones[i] == null ? Renderer.transform.localToWorldMatrix : Bones[i].localToWorldMatrix * Bindposes[i];
                normalMatrices[i] = Matrices[i].inverse.transpose;
            }
            int cursor = 0;
            // GPU skinning updates once per Editor tick. A static CPU snapshot keeps rendering and bounds on the same scrubbed pose.
            var inverse = Renderer.transform.worldToLocalMatrix;
            for (int i = 0; i < Vertices.Length; i++)
            {
                Vector3 point = Vector3.zero, normal = Vector3.zero, tangent = Vector3.zero; int count = skinned ? Counts[i] : 0;
                for (int j = 0; j < count; j++)
                {
                    var weight = Weights[cursor++]; var matrix = Matrices[weight.boneIndex];
                    point += matrix.MultiplyPoint3x4(Vertices[i]) * weight.weight;
                    if (normals.Length == Vertices.Length) normal += normalMatrices[weight.boneIndex].MultiplyVector(normals[i]) * weight.weight;
                    if (tangents.Length == Vertices.Length) tangent += matrix.MultiplyVector((Vector3)tangents[i]) * weight.weight;
                }
                if (count == 0) { point = Renderer.transform.TransformPoint(Vertices[i]); if (normals.Length == Vertices.Length) normal = Renderer.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(normals[i]); if (tangents.Length == Vertices.Length) tangent = Renderer.transform.TransformVector((Vector3)tangents[i]); }
                posed[i] = inverse.MultiplyPoint3x4(point);
                if (normals.Length == Vertices.Length) posedNormals[i] = Renderer.transform.localToWorldMatrix.transpose.MultiplyVector(normal).normalized;
                if (tangents.Length == Vertices.Length) { tangent = inverse.MultiplyVector(tangent).normalized; posedTangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w); }
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; } else bounds.Encapsulate(point);
            }
            mesh.vertices = posed;
            if (normals.Length == Vertices.Length) mesh.normals = posedNormals; else mesh.RecalculateNormals();
            if (tangents.Length == Vertices.Length) mesh.tangents = posedTangents;
            mesh.RecalculateBounds();
        }
        public void Dispose()
        {
            if (holder != null) Object.DestroyImmediate(holder);
            if (mesh != null) Object.DestroyImmediate(mesh);
        }
    }

    public CrustaspikanMaterialPreview(EnemyBossMaterialCollection data)
    {
        collection = data ?? throw new ArgumentNullException(nameof(data));
        var sourceActor = data.actorDefinition != null ? data.actorDefinition.ActorPrefab : null;
        if (sourceActor == null || sourceActor.Animator == null) throw new InvalidOperationException("검토 모델 연결을 확인하세요.");
        try
        {
            renderer = new PreviewRenderUtility();
            counted = true; LiveStages++;
            var camera = renderer.camera;
            camera.enabled = false; camera.cameraType = CameraType.Game;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.09f, .12f, .16f);
            camera.fieldOfView = 38f; camera.nearClipPlane = .02f; camera.farClipPlane = 200f;
            camera.useOcclusionCulling = false; camera.allowHDR = true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            renderer.ambientColor = new Color(.48f, .51f, .58f);
            for (int i = 0; i < renderer.lights.Length; i++)
            {
                var light = renderer.lights[i]; light.type = LightType.Directional; light.shadows = LightShadows.None;
                light.enabled = true; light.intensity = i == 0 ? 1.8f : 1.1f;
                light.color = i == 0 ? new Color(1f, .94f, .85f) : new Color(.66f, .78f, 1f);
                light.transform.rotation = i == 0 ? Quaternion.Euler(45f, -35f, 0f) : Quaternion.Euler(320f, 140f, 0f);
            }
            var source = sourceActor.Animator.transform;
            var sourceVisual = sourceActor.VisualRoot;
            rootPosition = source.localPosition;
            rootRotation = source.localRotation;
            rootScale = source.localScale;
            // The parent already belongs to the preview scene, so no object is created in an open game scene.
            visual = Object.Instantiate(sourceVisual.gameObject, camera.transform, false);
            visual.name = "Crustaspikan material preview model"; visual.transform.SetParent(null, false);
            visual.transform.SetPositionAndRotation(sourceActor.transform.InverseTransformPoint(sourceVisual.position), Quaternion.Inverse(sourceActor.transform.rotation) * sourceVisual.rotation);
            visual.transform.localScale = sourceVisual.lossyScale;
            var path = AnimationUtility.CalculateTransformPath(source, sourceVisual);
            rig = string.IsNullOrEmpty(path) ? visual : visual.transform.Find(path).gameObject;
            foreach (var node in visual.GetComponentsInChildren<Transform>(true)) node.gameObject.hideFlags = HideFlags.HideAndDontSave;
            foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true)) if (behaviour != null) Object.DestroyImmediate(behaviour);
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var animator in visual.GetComponentsInChildren<Animator>(true))
            { animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.fireEvents = false; animator.enabled = false; }
            foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
            renderer.AddSingleGO(visual);
            staticMeshes = visual.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null).ToArray();
            var skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null).ToArray();
            geometry = new SkinGeometry[skins.Length];
            for (int i = 0; i < skins.Length; i++) geometry[i] = new SkinGeometry(skins[i], this);
            groundMesh = new Mesh { name = "Crustaspikan review ground", hideFlags = HideFlags.HideAndDontSave,
                vertices = new[] { new Vector3(-20,0,-20), new Vector3(-20,0,20), new Vector3(20,0,20), new Vector3(20,0,-20) },
                triangles = new[] { 0,1,2,0,2,3 } };
            groundMesh.RecalculateNormals(); groundMesh.RecalculateBounds();
            groundMaterial = Material("Crustaspikan preview ground", new Color(.29f, .34f, .4f));
            var ground = MeshObject("Crustaspikan review ground", groundMesh, groundMaterial); ground.transform.position = Vector3.down * .045f;
            comparisonMesh = new Mesh { name = "Crustaspikan 1.8m reference", hideFlags = HideFlags.HideAndDontSave,
                vertices = new[] { new Vector3(-.2f,0,-.2f), new Vector3(.2f,0,-.2f), new Vector3(.2f,1.8f,-.2f), new Vector3(-.2f,1.8f,-.2f),
                    new Vector3(-.2f,0,.2f), new Vector3(.2f,0,.2f), new Vector3(.2f,1.8f,.2f), new Vector3(-.2f,1.8f,.2f) },
                triangles = new[] { 0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5 } };
            comparisonMesh.RecalculateNormals(); comparisonMesh.RecalculateBounds();
            comparisonMaterial = Material("Crustaspikan preview reference", new Color(.2f, .85f, .87f));
            comparison = MeshObject("1.8m height reference", comparisonMesh, comparisonMaterial);
            if (data.boulderMesh != null && data.boulderMaterial != null)
            { boulder = MeshObject("Crustaspikan preview held rock", data.boulderMesh, data.boulderMaterial); boulder.transform.localScale = Vector3.one * data.boulderVisualRadius; }
            Sample(data.FindMotion("IdleBreathe")?.runtime, 0f);
        }
        catch { Dispose(); throw; }
    }
    static Material Material(string name, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? throw new InvalidOperationException("URP Lit 연결을 확인하세요.");
        var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
        material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .12f); return material;
    }
    GameObject MeshObject(string name, Mesh mesh, Material material)
    {
        // Clone a preview-owned light object as an empty scene-local template.
        var root = Object.Instantiate(renderer.lights[0].gameObject, renderer.camera.transform, false);
        root.name = name; root.hideFlags = HideFlags.HideAndDontSave; root.transform.SetParent(null, false);
        Object.DestroyImmediate(root.GetComponent<Light>());
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.transform.localScale = Vector3.one;
        root.AddComponent<MeshFilter>().sharedMesh = mesh; root.AddComponent<MeshRenderer>().sharedMaterial = material;
        renderer.AddSingleGO(root); return root;
    }
    public void Sample(AnimationClip clip, float seconds)
    {
        if (renderer == null || clip == null) return;
        Clip = clip; Time = Mathf.Clamp(seconds, 0, clip.length);
        foreach (var skin in geometry) skin.ResetVisibility();
        rig.transform.localPosition = rootPosition; rig.transform.localRotation = rootRotation; rig.transform.localScale = rootScale;
        clip.SampleAnimation(rig, Time);
        bool found = false; Bounds bounds = default;
        foreach (var skin in geometry) skin.Encapsulate(ref bounds, ref found);
        foreach (var mesh in staticMeshes.Where(m => m.gameObject.activeInHierarchy && m.sharedMesh != null))
            if (mesh.TryGetComponent<MeshRenderer>(out var visible) && visible.enabled) Encapsulate(ref bounds, ref found, mesh.sharedMesh.bounds, mesh.transform.localToWorldMatrix);
        if (!found) throw new InvalidOperationException("검토 모델의 표시 메시가 없습니다.");
        BodyBounds = bounds;
        comparison.transform.position = new Vector3(bounds.min.x - 1f, 0, bounds.center.z);
        bool rock = clip.name.Contains("WithRock") || clip.name == "UnearthRock" && Time * clip.frameRate >= 100f || clip.name == "ThrowRock" && Time * clip.frameRate < 30f;
        if (boulder != null)
        {
            boulder.SetActive(rock);
            if (rock)
            {
                var nodes = rig.GetComponentsInChildren<Transform>(true);
                var left = nodes.FirstOrDefault(t => t.name == collection.boulderLeftHandBone);
                var right = nodes.FirstOrDefault(t => t.name == collection.boulderRightHandBone);
                if (left != null && right != null) boulder.transform.position = (left.position + right.position) * .5f + rig.transform.rotation * collection.boulderOffset;
                bounds.Encapsulate(boulder.GetComponent<Renderer>().bounds);
            }
        }
        bounds.Encapsulate(comparison.GetComponent<Renderer>().bounds); FrameBounds = bounds;
    }
    static void Encapsulate(ref Bounds bounds, ref bool found, Bounds local, Matrix4x4 matrix)
    {
        for (int i = 0; i < 8; i++)
        {
            var point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
            if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; } else bounds.Encapsulate(point);
        }
    }
    public RenderTexture Render(int width, int height)
    {
        if (renderer == null) throw new ObjectDisposedException(nameof(CrustaspikanMaterialPreview));
        width = Mathf.Max(2, width); height = Mathf.Max(2, height);
        if (surface == null || surface.width != width || surface.height != height)
        {
            if (surface != null) { surface.Release(); Object.DestroyImmediate(surface); }
            surface = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "Crustaspikan material review surface", hideFlags = HideFlags.HideAndDontSave };
            surface.Create();
        }
        var camera = renderer.camera; camera.aspect = (float)width / height;
        camera.transform.rotation = Quaternion.Euler(Pitch, Yaw + 180f, 0);
        float radius = FrameBounds.extents.magnitude;
        float vertical = camera.fieldOfView * .5f * Mathf.Deg2Rad;
        float horizontal = Mathf.Atan(Mathf.Tan(vertical) * camera.aspect);
        float distance = .1f;
        var inverse = Quaternion.Inverse(camera.transform.rotation);
        for (int i = 0; i < 8; i++)
        {
            var offset = inverse * Vector3.Scale(FrameBounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            distance = Mathf.Max(distance, Mathf.Abs(offset.x) / (Mathf.Tan(horizontal) * .88f) - offset.z,
                Mathf.Abs(offset.y) / (Mathf.Tan(vertical) * .88f) - offset.z, .1f - offset.z);
        }
        camera.transform.position = FrameBounds.center - camera.transform.forward * distance;
        camera.nearClipPlane = Mathf.Max(.02f, distance - radius * 1.5f); camera.farClipPlane = Mathf.Max(100f, distance + radius * 3f);
        camera.targetTexture = surface;
        var previous = RenderTexture.active;
        try
        {
            if (firstRender) { RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = surface }); firstRender = false; }
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = surface });
        }
        finally { RenderTexture.active = previous; }
        return surface;
    }
    public void Dispose()
    {
        try { if (renderer != null) { renderer.camera.targetTexture = null; renderer.Cleanup(); } }
        finally
        {
            renderer = null;
            if (geometry != null) foreach (var skin in geometry) skin?.Dispose();
            if (surface != null) { surface.Release(); Object.DestroyImmediate(surface); surface = null; }
            foreach (var item in new Object[] { groundMesh, comparisonMesh, groundMaterial, comparisonMaterial }) if (item != null) Object.DestroyImmediate(item);
            groundMesh = comparisonMesh = null; groundMaterial = comparisonMaterial = null; visual = rig = comparison = boulder = null; geometry = null; staticMeshes = null;
            if (counted) { LiveStages--; counted = false; }
        }
    }
}
