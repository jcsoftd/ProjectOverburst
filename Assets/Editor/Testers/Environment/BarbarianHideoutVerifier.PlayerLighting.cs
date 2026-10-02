using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static partial class BarbarianHideoutVerifier
{
    static void VerifyPlayerLighting(Scene scene)
    {
        VerifyMapLighting(scene);
        var own = Actor.GetComponentsInChildren<Light>(true).Single(l => l.name == "PlayerAmbientLight");
        Check(Mathf.Approximately(own.intensity, .3f), "Existing player ambient light retained");
    }

    static void VerifyMapLighting(Scene scene)
    {
        Check(Type.GetType("PlayerLightingScope, Assembly-CSharp") == null &&
            Type.GetType("PlayerLightingRendererFeature, Assembly-CSharp") == null,
            "Retired lighting scope and renderer feature are absent");
        var body = Actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Check(body.Length > 0 && body.All(r => r.renderingLayerMask == 1 && r.lightProbeUsage != LightProbeUsage.CustomProvided),
            "Player uses authored default light layer and map ambient probes");
        var lights = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)).ToArray();
        Check(lights.Length > 0 && lights.All(l => (l.GetUniversalAdditionalLightData().renderingLayers & 1u) != 0),
            "Map lights include default player light layer");
        Check(Camera.main != null && Camera.main.GetUniversalAdditionalCameraData().renderPostProcessing,
            "Player uses ordinary game camera post processing");
    }

    static void VerifyPlayerLightingPersists()
    {
        Check(Type.GetType("PlayerLightingScope, Assembly-CSharp") == null,
            "World reload does not recreate player lighting manager");
        Check(Actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r.renderingLayerMask == 1),
            "World reload retains authored player light layers");
    }
}
