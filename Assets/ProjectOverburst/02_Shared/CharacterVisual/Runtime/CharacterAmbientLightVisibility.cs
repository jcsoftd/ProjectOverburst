using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Character fill lights share one camera callback instead of one Update per actor.
public static class CharacterAmbientLightVisibility
{
    private static readonly Dictionary<Light, Collider> Lights = new Dictionary<Light, Collider>(256);
    private static readonly Plane[] ViewPlanes = new Plane[6];
    private static readonly Unity.Profiling.ProfilerMarker VisibilityMarker =
        new Unity.Profiling.ProfilerMarker("Overburst.Cost.CharacterAmbientLight.Visibility");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        Lights.Clear();
    }

    public static void Register(Light light, Collider body)
    {
        if (!Application.isPlaying || light == null) return;
        if (body == null)
        {
            Unregister(light);
            return;
        }

        if (Lights.Count == 0)
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
        Lights[light] = body;
        light.shadows = LightShadows.None;
        light.bounceIntensity = 0f;
        // Light.lightmapBakeType is Editor-only; bakingOutput is also available in Player.
        var baking = light.bakingOutput;
        baking.lightmapBakeType = LightmapBakeType.Realtime;
        baking.isBaked = false;
        light.bakingOutput = baking;
        SetEnabled(light, false); // The game camera decides before its first cull.
    }

    public static void Unregister(Light light)
    {
        // A destroyed Unity object can still be a dictionary key until its owner disables.
        if (ReferenceEquals(light, null)) return;
        Lights.Remove(light);
        if (light != null) SetEnabled(light, false);
        if (Lights.Count == 0)
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
    }

    private static void BeforeCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null || camera.cameraType != CameraType.Game || camera.targetTexture != null)
            return;
        var rig = QuarterViewCamera.ActiveInstance;
        var output = rig != null && rig.CinemachineRig != null
            ? rig.CinemachineRig.OutputCamera : null;
        if (output == null) output = Camera.main;
        if (camera != output) return;

        using (VisibilityMarker.Auto())
        {
            GeometryUtility.CalculateFrustumPlanes(camera, ViewPlanes);
            foreach (var entry in Lights)
            {
                var light = entry.Key;
                var body = entry.Value;
                if (light == null) continue;
                // Body bounds exclude weapon trails, VFX and the light's much larger range.
                bool visible = body != null && body.enabled && body.gameObject.activeInHierarchy
                    && GeometryUtility.TestPlanesAABB(ViewPlanes, body.bounds);
                SetEnabled(light, visible);
            }
        }
    }

    private static void SetEnabled(Light light, bool value)
    {
        if (light.enabled != value) light.enabled = value;
    }
}
