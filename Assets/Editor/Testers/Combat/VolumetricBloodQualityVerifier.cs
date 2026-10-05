using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static partial class SettingsPresentationVerifier
{
    public static string VolumetricQualityAssets(string directory)
    {
        RequireIdle(); var results = new List<string>();
        void Assert(bool value,string text) { if (!value) throw new InvalidOperationException(text); results.Add(text); }
        var catalog = AssetDatabase.LoadAssetAtPath<BloodEffectsPackCatalog>(VolumetricBloodBuilder.CatalogPath);
        for(int i=0;i<catalog.sprays.Length;i++)Assert(Mathf.Approximately(catalog.sprays[i].scale,VolumetricBloodBuilder.DefaultScreenScale(i)),"screen reference base scale " + i);
        foreach (var spray in catalog.sprays)
        {
            Assert(spray.groundPrefab && spray.groundPrefab.GetComponent<VolumetricBloodGroundData>(), "paired native ground " + spray.label);
            Assert(spray.flowing || spray.impactAccent || spray.shapeMask != 7, "intentional attack family " + spray.label);
            foreach (var layer in spray.prefab.GetComponent<VolumetricBloodAnimationData>().layers)
            foreach (var material in layer.renderer.sharedMaterials)
            foreach (var name in new[] { "_posTex", "_nTex" })
            {
                var texture = material.GetTexture(name) as Texture2D;
                Assert(texture && (texture.format == TextureFormat.RGBAHalf || texture.format == TextureFormat.RGB9e5Float), "native floating point VAT data " + spray.label + name);
            }
        }
        foreach (var prefab in catalog.lethalDecals)
        {
            var native = prefab.GetComponent<VolumetricBloodGroundData>();
            Assert(native.cutout.keys.Length > 2 && native.landing.keys.Length > 1 && native.heightMax > 0, "authored reveal and landing curves " + prefab.name);
            Assert(native.CutoutAt(0f) > native.CutoutAt(native.RevealSeconds), "native cutout grows into full mark " + prefab.name);
        }
        foreach (CombatImpactShape shape in Enum.GetValues(typeof(CombatImpactShape)))
            Assert(catalog.sprays.Count(s=>s.Accepts(shape,1)) >= 3, "multiple forms per attack family " + shape);
        Assert(catalog.sprays[7].impactAccent && catalog.sprays[12].impactAccent, "large radial splashes emphasize critical, heavy and lethal hits");
        foreach (CombatImpactShape shape in Enum.GetValues(typeof(CombatImpactShape)))
        for (uint seed=0;seed<100;seed++)
        {
            Assert(catalog.sprays[catalog.ResolveSpray(shape,3,seed,-1)].Accepts(shape,3),"ordinary selection respects attack family " + shape + seed);
            Assert(catalog.sprays[catalog.ResolveSpray(shape,0,seed,-1,false,true)].impactAccent,"accent selection prefers radial " + shape + seed);
        }
        Assert(System.Linq.Enumerable.Range(0,100).All(seed=>!catalog.sprays[catalog.ResolveSpray(CombatImpactShape.Downward,0,(uint)seed,-1)].impactAccent),"ordinary downward hits reserve large radial forms for emphasized contexts");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"quality-assets.json"),JsonConvert.SerializeObject(new {success=true,checks=results},Formatting.Indented));
        return "PASS " + results.Count;
    }
    static IEnumerator VerifyCPlaybackTuning(BloodHitProfile profile, Vector3 point, BloodGroundDecalService ground)
    {
        var blood=Object.FindFirstObjectByType<BloodHitVfxService>();
        var pool=(BloodEffectsPackPool)typeof(BloodHitVfxService).GetField("packPool",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(blood);
        var catalog=Resources.Load<BloodEffectsPackCatalog>(BloodEffectsPackCatalog.VolumetricResourcePath);
        float timeScale=Time.timeScale, baselineScale=0f;var sizes=new List<object>();
        try
        {
            Time.timeScale=0f;
            foreach(float weight in new[]{1f,1.14f,1.28f})
            {
                pool.Clear();ground.ClearForComparison();
                BloodHitVfxService.RequestAt(profile,point,Vector3.forward,CombatImpactShape.Sweep,1f,0,weight);
                yield return Frames(4);
                Check(pool.ActiveCount==1,"C real request plays for weight "+weight);
                if(weight==1f)baselineScale=pool.LastBaseScale;
                else Check(Mathf.Abs(pool.LastBaseScale/baselineScale-(weight==1.14f?1.07f:1.14f))<.001f,"C automatic body growth reduced "+weight);
                sizes.Add(new{weight,scale=pool.LastBaseScale,ratio=pool.LastBaseScale/baselineScale});
            }
            var data=blood.GetComponentsInChildren<VolumetricBloodAnimationData>(false).First();
            var layer=data.layers.OrderByDescending(l=>l.seconds).First();
            pool.Tick(Time.time+layer.seconds/(2f*VolumetricBloodAnimationData.PlaybackSpeed));
            var block=new MaterialPropertyBlock();layer.renderer.GetPropertyBlock(block);
            float frame=layer.speed.Evaluate(.5f)*layer.frames+layer.offset+1.1f;
            Check(layer.renderer.enabled && Mathf.Abs(block.GetFloat("_TimeInFrames")-(Mathf.Ceil(-frame)+1f)/(layer.frames+1f))<.0001f,"C VAT reaches authored halfway frame sooner");
            pool.Tick(Time.time+catalog.sprays[pool.LastVariant].lifetime/VolumetricBloodAnimationData.PlaybackSpeed+.01f);
            Check(pool.ActiveCount==0,"C shortened playback returns its lease");
            File.WriteAllText(Path.Combine(output,"timing-growth.json"),JsonConvert.SerializeObject(new{speed=VolumetricBloodAnimationData.PlaybackSpeed,sizes},Formatting.Indented));
        }
        finally{Time.timeScale=timeScale;pool.Clear();ground.ClearForComparison();}
    }
    static IEnumerator VerifyCQualityFixture()
    {
        OverburstGameSettings.BloodStyle = BloodEffectStyle.Volumetric;
        OverburstGameSettings.BloodUniformRed = true;
        BloodComparisonTuning.ResetCurrent();
        var ground = Object.FindFirstObjectByType<BloodGroundDecalService>();
        var catalog = Resources.Load<BloodEffectsPackCatalog>(BloodEffectsPackCatalog.VolumetricResourcePath);
        var profile = Resources.Load<BloodHitProfile>(BloodHitVfxService.PlayerProfilePath);
        Vector3 center = PlayerInputFacade.Current.transform.position + Vector3.up * 20f;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Owned C Quality Surface"; owned.Add(floor);
        floor.transform.position = center; floor.transform.localScale = new Vector3(30f,.1f,30f);
        var surface = new Material(Shader.Find("Universal Render Pipeline/Lit")) {name="Owned C Quality Surface Material"};
        surface.SetColor("_BaseColor",new Color(.45f,.42f,.39f)); surface.SetFloat("_Smoothness",0f);
        floor.GetComponent<Renderer>().sharedMaterial=surface;
        var root = new GameObject("Owned C Quality Camera"); owned.Add(root);
        var camera = root.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled=false;
        camera.GetUniversalAdditionalCameraData().SetRenderer(new SerializedObject(Camera.main.GetUniversalAdditionalCameraData()).FindProperty("m_RendererIndex").intValue);
        camera.orthographic=true;camera.orthographicSize=6f;camera.transform.position=center+Vector3.up*8f;camera.transform.rotation=Quaternion.Euler(90f,0f,0f);
        RenderTexture rt=null;Texture2D pixels=null;var rows=new List<object>();
        try
        {
            rt=new RenderTexture(960,640,24,RenderTextureFormat.ARGB32);rt.Create();pixels=new Texture2D(960,640,TextureFormat.RGB24,false);camera.targetTexture=rt;
            Color32[] Capture(string name)
            {
                var previous=RenderTexture.active;
                try {camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,960,640),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());return pixels.GetPixels32();}
                finally {RenderTexture.active=previous;}
            }
            Physics.SyncTransforms();ground.ClearForComparison();yield return Frames(3);
            yield return VerifyCPlaybackTuning(profile,PlayerInputFacade.Current.transform.position+Vector3.up,ground);
            var baseline=Capture("quality-baseline");
            foreach (var definition in catalog.sprays.Where(s=>!s.flowing))
            {
                ground.ClearForComparison();var native=definition.groundPrefab.GetComponent<VolumetricBloodGroundData>();
                Vector3 hit=center+Vector3.up;
                ground.Request(profile,hit,Vector3.right,CombatImpactShape.Downward,1f,2,false,.16f,false,definition.groundPrefab,1f,Quaternion.identity);
                float until=Time.unscaledTime+3f;while(ground.ActiveCount==0 && Time.unscaledTime<until)yield return null;
                File.WriteAllText(Path.Combine(output,"landing-diagnostic.json"),JsonConvert.SerializeObject(new{definition.label,ground.ActiveCount,ground.RequestedCount,ground.SkippedNoGroundCount,ground.SkippedSpatialCount,ground.SkippedQueueCount,ground.SkippedLateCount,ground.SkippedPoolCount,cutout=native.CutoutAt(0),offset=native.offset.ToString(),size=native.size.ToString(),timeScale=Time.timeScale},Formatting.Indented));
                Check(ground.ActiveCount==1,"height-aware landing reaches surface "+definition.label);
                var projector=ground.GetComponentsInChildren<DecalProjector>(true).First(p=>p.gameObject.activeSelf);
                float earlyCutout=projector.material.GetFloat("_Cutout");var early=Capture("landing-early-"+definition.label);
                yield return Wait(1.5f);
                float lateCutout=projector.material.GetFloat("_Cutout");var late=Capture("landing-late-"+definition.label);
                int changed=0,moving=0,red=0;
                for(int i=0;i<late.Length;i++)
                {
                    int Difference(Color32 a,Color32 b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
                    if(Difference(late[i],baseline[i])>25){changed++;if(late[i].r>late[i].g*1.3f && late[i].r>late[i].b*1.2f)red++;}
                    if(Difference(early[i],late[i])>25)moving++;
                }
                Check(earlyCutout>lateCutout && moving>40 && red>40,"native ground spread changes actual red pixels "+definition.label);
                rows.Add(new{definition.label,earlyCutout,lateCutout,changed,moving,red,position=projector.transform.position.ToString(),size=projector.size.ToString()});
                File.WriteAllText(Path.Combine(output,"quality-ground.json"),JsonConvert.SerializeObject(rows,Formatting.Indented));
            }
            ground.ClearForComparison();
            // Two marks sharing a source texture must retain independent animation ages.
            var first=catalog.sprays[0].groundPrefab;
            ground.Request(profile,center+Vector3.up+Vector3.left*2f,Vector3.zero,CombatImpactShape.Downward,1f,2,false,0f,false,first,1f,Quaternion.identity);
            yield return Wait(1.2f);
            var older=ground.GetComponentsInChildren<DecalProjector>(true).First(p=>p.gameObject.activeSelf);
            ground.Request(profile,center+Vector3.up+Vector3.right*2f,Vector3.zero,CombatImpactShape.Downward,1f,2,false,0f,false,first,1f,Quaternion.identity);
            float end=Time.unscaledTime+2f;while(ground.ActiveCount<2 && Time.unscaledTime<end)yield return null;
            var newer=ground.GetComponentsInChildren<DecalProjector>(true).First(p=>p.gameObject.activeSelf && p!=older);
            Check(older.material!=newer.material && older.material.GetFloat("_Cutout")<newer.material.GetFloat("_Cutout"),"simultaneous marks have independent reveal materials and ages");
            var materials=ground.GetComponentsInChildren<DecalProjector>(true).Select(p=>p.material).Where(m=>m).ToArray();
            OverburstGameSettings.BloodStyle=BloodEffectStyle.Legacy;yield return Frames(3);
            Check(materials.All(m=>m==null),"C owned ground materials released on style change");
        }
        finally
        {
            camera.targetTexture=null;ground.ClearForComparison();if(pixels)Object.DestroyImmediate(pixels);if(rt){rt.Release();Object.DestroyImmediate(rt);}
            Object.DestroyImmediate(root);owned.Remove(root);Object.DestroyImmediate(floor);owned.Remove(floor);Object.DestroyImmediate(surface);
        }
    }
}
