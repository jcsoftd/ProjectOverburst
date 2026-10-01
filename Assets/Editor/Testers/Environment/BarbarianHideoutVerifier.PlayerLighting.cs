using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static partial class BarbarianHideoutVerifier
{
    static void VerifyPlayerLighting(Scene scene)
    {
        var scope = PlayerLightingScope.Active;
        Check(scope != null && scope.gameObject.scene.name == "DontDestroyOnLoad", "Global runtime owns player lighting scope");
        scope.RefreshActors();
        Check(Vector3.Distance(new Vector3(scope.OriginalMainColor.r, scope.OriginalMainColor.g, scope.OriginalMainColor.b), Vector3.one * 2) < .001f,
            "Player uses original white directional light intensity 2");
        var body = Actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Check(body.Length > 0 && body.All(r => r.renderingLayerMask == PlayerLightingScope.PlayerLayer && r.lightProbeUsage == LightProbeUsage.CustomProvided),
            "All character body/face/armor variants exclude camp light layer and use original ambient probe");
        Check(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)).All(l => ((uint)l.GetUniversalAdditionalLightData().renderingLayers & PlayerLightingScope.PlayerLayer) == 0),
            "Camp sun, fire and forge lights exclude playable actors");
        var own = Actor.GetComponentsInChildren<Light>(true).Single(l => l.name == "PlayerAmbientLight");
        Check(own.intensity == .3f && ((uint)own.GetUniversalAdditionalLightData().renderingLayers & PlayerLightingScope.PlayerLayer) != 0, "Existing player ambient light retained");
        var camera = Camera.main;
        SaveCamera(camera, Path.Combine(Output, "player_original_lighting_" + Cycle + ".png"));
        Check(PlayerLightingRendererFeature.LastDrawFrame == Time.frameCount && PlayerLightingRendererFeature.LastDrawCount > 0,
            "Player original materials drawn after camp color grading with scene depth");
        scope.enabled = false;
        try {SaveCamera(camera, Path.Combine(Output, "player_camp_lighting_before_" + Cycle + ".png"));}
        finally {scope.enabled = true; scope.RefreshActors();}
        VerifyCharacterColorPixels();
    }

    static void VerifyCharacterColorPixels()
    {
        var before = new Texture2D(2, 2); var after = new Texture2D(2, 2);
        try
        {
            before.LoadImage(File.ReadAllBytes(Path.Combine(Output, "player_camp_lighting_before_" + Cycle + ".png")));
            after.LoadImage(File.ReadAllBytes(Path.Combine(Output, "player_original_lighting_" + Cycle + ".png")));
            var a = before.GetPixels32(); var b = after.GetPixels32();
            int changed = 0, colored = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (Mathf.Max(Mathf.Abs(a[i].r-b[i].r), Mathf.Abs(a[i].g-b[i].g), Mathf.Abs(a[i].b-b[i].b)) <= 25) continue;
                changed++;
                if (Mathf.Max(b[i].r,b[i].g,b[i].b)-Mathf.Min(b[i].r,b[i].g,b[i].b) > 25) colored++;
            }
            Check(changed > 20 && colored > 20, "Character retains colored texture pixels after lighting separation");
            File.WriteAllText(Path.Combine(Output, "color_pixels_" + Cycle + ".json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {status="PASS",changedPixels=changed,coloredTexturePixels=colored}));
        }
        finally {Object.DestroyImmediate(before); Object.DestroyImmediate(after);}
    }

    static void VerifyPlayerLightingPersists()
    {
        Check(PlayerLightingScope.Active != null, "Leaving Hideout retains global player lighting scope");
        Check(Actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r.renderingLayerMask == PlayerLightingScope.PlayerLayer && r.lightProbeUsage == LightProbeUsage.CustomProvided),
            "Leaving Hideout retains character light layers and original ambient probe");
        Check(Actor.GetComponentsInChildren<Light>(true).Single(l => l.name == "PlayerAmbientLight").GetUniversalAdditionalLightData().renderingLayers == 3,
            "Leaving Hideout retains existing player ambient light on player layer");
    }
}
