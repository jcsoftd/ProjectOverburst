using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// A renderer-only studio mirrors the real Play actor's evaluated geometry.
// It has no actor scripts, Animator, input, colliders or gameplay state.
public sealed class SwordCleanCaptureStage : IDisposable
{
    sealed class Part
    {
        public Renderer source;
        public MeshRenderer target;
        public Mesh mesh;
        public bool particleTrails;
        public bool actorGeometry;
        public readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    }
    readonly List<Part> parts = new List<Part>();
    readonly List<Mesh> ownedMeshes = new List<Mesh>();
    readonly HashSet<Renderer> registered = new HashSet<Renderer>();
    GameObject root;
    Material floorMaterial;
    Camera effectBakeCamera;
    Vector3 origin;
    public Scene Scene { get; private set; }

    public SwordCleanCaptureStage(Animator animator, Transform actor)
    {
        Scene = EditorSceneManager.NewPreviewScene();
        try
        {
            origin = actor.position;
            root = new GameObject("Owned clean Sword capture studio");
            SceneManager.MoveGameObjectToScene(root, Scene);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.name = "Owned studio floor"; floor.transform.SetParent(root.transform);
            float ground = Physics.Raycast(origin + Vector3.up * .5f, Vector3.down, out var hit, 3f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.point.y - origin.y : -.02f;
            floor.transform.localPosition = Vector3.up * ground;
            floor.transform.localScale = Vector3.one * 20;
            floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.name = "Owned matte studio floor";
            floorMaterial.SetColor("_BaseColor", new Color(.22f,.25f,.28f));
            floorMaterial.SetFloat("_Smoothness", 0);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
            floor.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var lightObject = new GameObject("Owned studio key light");
            lightObject.transform.SetParent(root.transform);
            lightObject.transform.rotation = Quaternion.Euler(48,-35,0);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.3f;
            light.color = new Color(1f,.96f,.90f); light.shadows = LightShadows.Soft;
            AddVisibleActor(animator.transform);
        }
        catch { Dispose(); throw; }
    }
    public Vector3 Position(Vector3 world) => world - origin;
    public void AddVisibleActor(Transform actor)
    {
        foreach (var source in actor.GetComponentsInChildren<Renderer>(true))
            if ((source is SkinnedMeshRenderer || source is MeshRenderer) && source.GetComponentInParent<Canvas>() == null) AddPart(source, false, true);
    }
    void AddPart(Renderer source, bool particleTrails = false, bool actorGeometry = false)
    {
        if (!particleTrails && !registered.Add(source)) return;
        bool dynamic = !(source is MeshRenderer);
        var mesh = dynamic ? new Mesh {name="Owned studio evaluated geometry"}
            : source.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh == null) return;
        if (dynamic) {mesh.MarkDynamic();ownedMeshes.Add(mesh);}
        var go = new GameObject("Owned studio part " + source.name);
        go.transform.SetParent(root.transform);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var target = go.AddComponent<MeshRenderer>();
        target.sharedMaterials = particleTrails && source is ParticleSystemRenderer pr
            ? new[]{pr.trailMaterial} : source.sharedMaterials;
        target.shadowCastingMode = source.shadowCastingMode;
        target.receiveShadows = source.receiveShadows;
        target.lightProbeUsage = LightProbeUsage.Off;
        parts.Add(new Part {source=source,target=target,mesh=mesh,particleTrails=particleTrails,actorGeometry=actorGeometry});
    }
    public void Sync(Camera camera, bool includeEffects = false)
    {
        if (includeEffects)
        {
            if (effectBakeCamera == null)
            {
                var go = new GameObject("Owned studio source-space bake camera");
                go.transform.SetParent(root.transform);
                effectBakeCamera = go.AddComponent<Camera>();
            }
            effectBakeCamera.CopyFrom(camera); effectBakeCamera.enabled = false;
            effectBakeCamera.transform.SetPositionAndRotation(camera.transform.position + origin, camera.transform.rotation);
        }
        var geometryCamera = includeEffects ? effectBakeCamera : camera;
        if (includeEffects)
            foreach (var source in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
            {
                if (source.gameObject.scene == Scene || (source.transform.position-origin).sqrMagnitude > 256) continue;
                if (!(source is ParticleSystemRenderer) && !(source is TrailRenderer) && !(source is LineRenderer)
                    && !(source is MeshRenderer && source.transform.root.name == "Player Dash VFX")) continue;
                if (registered.Contains(source)) continue;
                AddPart(source);
                if (source is ParticleSystemRenderer ps && ps.GetComponent<ParticleSystem>().trails.enabled && ps.trailMaterial != null)
                    AddPart(source,true);
            }
        foreach (var part in parts)
        {
            bool visible = part.source != null && part.source.enabled && part.source.gameObject.activeInHierarchy;
            if (part.source is ParticleSystemRenderer psVisible && !part.particleTrails)
                visible &= psVisible.GetComponent<ParticleSystem>().particleCount > 0;
            if (part.source is TrailRenderer trailVisible) visible &= trailVisible.positionCount > 1;
            part.target.enabled = visible;
            if (!visible) continue;
            if (part.source is SkinnedMeshRenderer skin)
            {
                skin.BakeMesh(part.mesh, true);
                part.mesh.RecalculateBounds();
            }
            else if (part.source is ParticleSystemRenderer particles)
            {
                if (part.particleTrails) particles.BakeTrailsMesh(part.mesh,geometryCamera,ParticleSystemBakeMeshOptions.BakeRotationAndScale | ParticleSystemBakeMeshOptions.BakePosition);
                else particles.BakeMesh(part.mesh,geometryCamera,ParticleSystemBakeMeshOptions.BakeRotationAndScale | ParticleSystemBakeMeshOptions.BakePosition);
            }
            else if (part.source is TrailRenderer trail) trail.BakeMesh(part.mesh,geometryCamera,false);
            else if (part.source is LineRenderer line) line.BakeMesh(part.mesh,geometryCamera,false);
            var sourceTransform = part.source.transform;
            part.target.transform.SetPositionAndRotation(Position(sourceTransform.position), sourceTransform.rotation);
            part.target.transform.localScale = sourceTransform.lossyScale;
            if (part.source is ParticleSystemRenderer)
            {
                part.target.transform.SetPositionAndRotation(-origin, Quaternion.identity);
                part.target.transform.localScale = Vector3.one;
            }
            if (!part.particleTrails) part.target.sharedMaterials = part.source.sharedMaterials;
            part.source.GetPropertyBlock(part.properties); part.target.SetPropertyBlock(part.properties);
            if (!part.particleTrails)
                for (int slot=0;slot<part.source.sharedMaterials.Length;slot++)
                {
                    part.source.GetPropertyBlock(part.properties,slot);
                    part.target.SetPropertyBlock(part.properties,slot);
                }
        }
    }
    public object[] ActorFraming(Camera camera)
    {
        var result = new List<object>();
        foreach (var part in parts)
        {
            if (!part.actorGeometry || !part.target.enabled) continue;
            Bounds bounds = part.mesh.bounds;
            Vector3 low = Vector3.one * float.PositiveInfinity;
            Vector3 high = Vector3.one * float.NegativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1)==0?-1:1,(corner & 2)==0?-1:1,(corner & 4)==0?-1:1));
                Vector3 world = part.target.transform.TransformPoint(local);
                Vector3 view = camera.WorldToViewportPoint(world);
                low = Vector3.Min(low,view); high = Vector3.Max(high,view);
            }
            result.Add(new { name=part.source.name, rect=new[]{low.x,low.y,high.x,high.y} });
        }
        return result.ToArray();
    }
    public void Dispose()
    {
        if (root != null) UnityEngine.Object.DestroyImmediate(root);
        foreach (var mesh in ownedMeshes) if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
        ownedMeshes.Clear(); parts.Clear(); registered.Clear();
        if (floorMaterial != null) UnityEngine.Object.DestroyImmediate(floorMaterial);
        if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene);
        root = null; floorMaterial = null; Scene = default;
    }
}
