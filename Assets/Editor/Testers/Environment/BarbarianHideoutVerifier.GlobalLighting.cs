using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class BarbarianHideoutVerifier
{
    static void BeginLightingRun()
    {
        var account = AccountGameplaySession.Current;
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        var registry = (AccountContentRegistry)typeof(AccountGameplaySession).GetProperty("ContentRegistry",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(account);
        var map = new MapInstanceState {mapContentId=registry.IdFor(definition),level=1,grade=ItemGrade.Common,monsterThemeId="CavernMutants"};
        Check(PersistentSceneFlow.Instance.EnterDebugRun(DiamondDungeonWorld.SceneName, map), "Global lighting real dungeon entry: " + PersistentSceneFlow.Instance.RunEntryError);
        Phase = 30; Deadline();
    }

    static bool TickLightingRun()
    {
        if (Phase == 30)
        {
            if (PersistentSceneFlow.Instance.IsSwitching || WorldSessionState.Phase != WorldPhase.Run) return true;
            var scene = SceneManager.GetSceneByName(DiamondDungeonWorld.SceneName);
            Check(scene.isLoaded && SceneManager.GetActiveScene() == scene, "Global lighting actual dungeon loaded");
            var world = Object.FindFirstObjectByType<DiamondDungeonWorld>();
            foreach (var field in world.Fields) field.enabled = false;
            foreach (var item in world.EventDirector.Events) item.enabled = false;
            Actor.Health.SetMaxHp(1000000, true);
            VerifyGlobalLighting(scene);
            TestMapTintIsolation(scene);
            var driver = Object.FindFirstObjectByType<RunLifetimeDriver>();
            Check(driver != null, "Product run return driver ready");
            driver.RequestAbandon(); Phase=31; Deadline();
            return true;
        }
        if (Phase == 31)
        {
            if (!Ready) return true;
            Check(PlayerLightingScope.Active != null && !SceneManager.GetSceneByName(DiamondDungeonWorld.SceneName).isLoaded,
                "Global lighting survives dungeon return");
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(PersistentSceneFlow.PersistentSceneName));
            SceneManager.UnloadSceneAsync(PersistentSceneFlow.HideoutSceneName);
            Phase=5; Deadline(); return true;
        }
        return false;
    }

    static void VerifyGlobalLighting(Scene scene)
    {
        var scope=PlayerLightingScope.Active; scope.RefreshActors();
        Check(scope != null && scope.gameObject.scene.name == "DontDestroyOnLoad", "Scene-independent lighting owner survives world change");
        Check(Actor.GetComponentsInChildren<Renderer>(true).Where(r=>r is MeshRenderer || r is SkinnedMeshRenderer)
            .All(r=>r.renderingLayerMask==PlayerLightingScope.PlayerLayer && r.lightProbeUsage==LightProbeUsage.CustomProvided), "Dungeon body and equipment retain original lighting");
        Check(scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Light>(true))
            .All(l=>((uint)l.GetUniversalAdditionalLightData().renderingLayers&PlayerLightingScope.PlayerLayer)==0), "All dungeon lights exclude player");
        SaveCamera(Camera.main,Path.Combine(Output,"dungeon_player_lighting_"+Cycle+".png"));
        Check(PlayerLightingRendererFeature.LastDrawFrame==Time.frameCount && PlayerLightingRendererFeature.LastDrawCount>0,
            "Dungeon camera executes original player material pass");
        var gear=GameObject.CreatePrimitive(PrimitiveType.Cube);
        gear.name="Lighting late-equipment fixture"; gear.transform.SetParent(Actor.transform,false); gear.transform.localScale=Vector3.one*.1f;
        try {scope.RefreshActors(); Check(gear.GetComponent<Renderer>().renderingLayerMask==PlayerLightingScope.PlayerLayer,
            "Late equipment automatically inherits player light isolation");}
        finally {Object.DestroyImmediate(gear);}
    }

    static void TestMapTintIsolation(Scene previous)
    {
        var scope=PlayerLightingScope.Active;
        var fixture=SceneManager.CreateScene("PlayerLighting_RuntimeFixture_"+Cycle);
        var root=new GameObject("Runtime lighting fixture"); SceneManager.MoveGameObjectToScene(root,fixture);
        var sun=root.AddComponent<Light>(); sun.type=LightType.Directional; sun.intensity=6; sun.transform.rotation=Quaternion.Euler(25,110,0);
        var volume=root.AddComponent<Volume>(); volume.isGlobal=true; volume.priority=1000;
        var profile=ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile=profile;
        var tone=profile.Add<Tonemapping>(); tone.mode.Override(TonemappingMode.ACES);
        var grade=profile.Add<ColorAdjustments>(); grade.postExposure.Override(1.5f); grade.saturation.Override(-70);
        var balance=profile.Add<WhiteBalance>();
        var animations=Actor.GetComponentsInChildren<Animator>(true);
        var speeds=animations.Select(a=>a.speed).ToArray();
        try
        {
            SceneManager.SetActiveScene(fixture);
            foreach(var animation in animations) animation.speed=0;
            var data=sun.GetUniversalAdditionalLightData(); data.renderingLayers=uint.MaxValue;
            scope.RefreshActors();
            Check(((uint)data.renderingLayers&PlayerLightingScope.PlayerLayer)==0,"New all-layer map light automatically excludes player");
            Check(data.customShadowLayers && ((uint)data.shadowRenderingLayers&PlayerLightingScope.PlayerLayer)!=0,"Player still casts map shadows");
            var mask=CapturePlayerMask(fixture);
            RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientIntensity=2;
            RenderSettings.fog=true; RenderSettings.fogMode=FogMode.ExponentialSquared; RenderSettings.fogDensity=.06f;
            sun.color=Color.red; RenderSettings.ambientLight=Color.red; RenderSettings.fogColor=new Color(.6f,.05f,.05f); balance.temperature.Override(80);
            string red=Path.Combine(Output,"map_red_lighting_"+Cycle+".png"); SaveCamera(Camera.main,red);
            sun.color=Color.blue; RenderSettings.ambientLight=Color.blue; RenderSettings.fogColor=new Color(.05f,.05f,.6f); balance.temperature.Override(-80);
            string blue=Path.Combine(Output,"map_blue_lighting_"+Cycle+".png"); SaveCamera(Camera.main,blue);
            ComparePlayerPixels(mask,red,blue,"Opposite map light, ambient, grading and fog preserve player texture colors");
            var directionals=Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>l.type==LightType.Directional && l.enabled).ToArray();
            try
            {
                foreach(var light in directionals) light.enabled=false;
                string noSun=Path.Combine(Output,"map_no_sun_"+Cycle+".png"); SaveCamera(Camera.main,noSun);
                ComparePlayerPixels(mask,blue,noSun,"Scene without directional light retains original player lighting");
            }
            finally {foreach(var light in directionals) if(light!=null) light.enabled=true;}
        }
        finally
        {
            for(int i=0;i<animations.Length;i++) if(animations[i]!=null) animations[i].speed=speeds[i];
            SceneManager.SetActiveScene(previous); Object.DestroyImmediate(root); Object.DestroyImmediate(profile);
            SceneManager.UnloadSceneAsync(fixture);
        }
    }

    static Color32[] CapturePlayerMask(Scene scene)
    {
        var root=new GameObject("Player mask capture"); SceneManager.MoveGameObjectToScene(root,scene);
        var camera=root.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.transform.SetPositionAndRotation(Camera.main.transform.position,Camera.main.transform.rotation);
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black; camera.cullingMask=1<<29;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
        var renderers=PlayerLightingScope.Active.Renderers.Where(r=>r!=null).ToArray();
        var layers=renderers.Select(r=>r.gameObject.layer).ToArray();
        var texture=new Texture2D(2,2);
        try
        {
            foreach(var renderer in renderers) renderer.gameObject.layer=29;
            string path=Path.Combine(Output,"player_color_mask_"+Cycle+".png"); SaveCamera(camera,path);
            texture.LoadImage(File.ReadAllBytes(path)); return texture.GetPixels32();
        }
        finally
        {
            for(int i=0;i<renderers.Length;i++) if(renderers[i]!=null) renderers[i].gameObject.layer=layers[i];
            Object.DestroyImmediate(texture); Object.DestroyImmediate(root);
        }
    }

    static void ComparePlayerPixels(Color32[] mask,string first,string second,string label)
    {
        var a=new Texture2D(2,2); var b=new Texture2D(2,2);
        try
        {
            a.LoadImage(File.ReadAllBytes(first)); b.LoadImage(File.ReadAllBytes(second));
            var p=a.GetPixels32(); var q=b.GetPixels32(); int samples=0,changed=0,colored=0;
            bool Visible(int i)=>i>=0 && i<mask.Length && Mathf.Max(mask[i].r,mask[i].g,mask[i].b)>20;
            for(int i=a.width+1;i<mask.Length-a.width-1;i++)
            {
                if(!Visible(i)||!Visible(i-1)||!Visible(i+1)||!Visible(i-a.width)||!Visible(i+a.width))continue;
                samples++; if(Mathf.Max(Mathf.Abs(p[i].r-q[i].r),Mathf.Abs(p[i].g-q[i].g),Mathf.Abs(p[i].b-q[i].b))>12)changed++;
                if(Mathf.Max(q[i].r,q[i].g,q[i].b)-Mathf.Min(q[i].r,q[i].g,q[i].b)>25)colored++;
            }
            File.WriteAllText(Path.Combine(Output,Path.GetFileNameWithoutExtension(second)+"_pixels.json"),JsonConvert.SerializeObject(new {samples,changed,colored},Formatting.Indented));
            Check(samples>80 && colored>20 && changed<=samples*.15f,label+" ("+changed+"/"+samples+")");
        }
        finally {Object.DestroyImmediate(a); Object.DestroyImmediate(b);}
    }
}
