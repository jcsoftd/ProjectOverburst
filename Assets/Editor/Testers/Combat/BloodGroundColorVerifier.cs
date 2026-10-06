using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

public static partial class SettingsPresentationVerifier
{
    // Real runtime pools and projectors share the camera, surface and scene lighting.
    static IEnumerator VerifyBloodGroundColor()
    {
        var blood = Object.FindFirstObjectByType<BloodHitVfxService>();
        var ground = Object.FindFirstObjectByType<BloodGroundDecalService>();
        var sourceProfile = Resources.Load<BloodHitProfile>(BloodHitVfxService.PlayerProfilePath);
        var main = Camera.main;
        Vector3 direction = Vector3.ProjectOnPlane(main.transform.right, Vector3.up).normalized;
        Vector3 point = PlayerInputFacade.Current.transform.position + main.transform.right * 2f + Vector3.up * 1.1f;
        Vector3 center = new Vector3(point.x, point.y - .4f, point.z);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor);
        floor.name = "Owned Blood Color Surface";
        floor.transform.position = center - Vector3.up * .05f;
        floor.transform.localScale = new Vector3(9f, .1f, 9f);
        var surface = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Owned Blood Color Surface Material" };
        surface.SetColor("_BaseColor", new Color(.45f,.42f,.39f));
        surface.SetFloat("_Smoothness", 0f); floor.GetComponent<Renderer>().sharedMaterial = surface;
        var cameraRoot = new GameObject("Owned Blood Color Camera"); owned.Add(cameraRoot);
        var camera = cameraRoot.AddComponent<Camera>(); camera.CopyFrom(main); camera.enabled = false;
        camera.GetUniversalAdditionalCameraData().SetRenderer(new SerializedObject(main.GetUniversalAdditionalCameraData()).FindProperty("m_RendererIndex").intValue);
        camera.orthographic = true; camera.orthographicSize = 3f;
        camera.transform.position = center + Vector3.up * 8f;
        camera.transform.rotation = Quaternion.Euler(90f,0f,0f);
        RenderTexture rt = null; Texture2D pixels = null;
        float previousTimeScale = Time.timeScale;
        bool previousGroundEnabled = ground.enabled;
        var rows = new List<object>();
        bool baselineRun = File.Exists(Path.Combine(output,"expect-dark-baseline")) || File.Exists(Path.Combine(output,"capture-only"));
        try
        {
            rt = new RenderTexture(720,480,24,RenderTextureFormat.ARGB32); rt.Create();
            pixels = new Texture2D(720,480,TextureFormat.RGB24,false); camera.targetTexture = rt;
            Color32[] Capture(string stem)
            {
                var active = RenderTexture.active;
                try { camera.Render(); RenderTexture.active = rt; pixels.ReadPixels(new Rect(0,0,720,480),0,0); pixels.Apply();
                    if(stem!=null)File.WriteAllBytes(Path.Combine(output,stem+".png"),pixels.EncodeToPNG()); return pixels.GetPixels32(); }
                finally { RenderTexture.active = active; }
            }
            (int count, Color mean) Measure(Color32[] image, Color32[] background)
            {
                var samples = new List<Color>();
                for(int i=0;i<image.Length;i++)
                {
                    var c=image[i]; var b=background[i];
                    if(Math.Abs(c.r-b.r)+Math.Abs(c.g-b.g)+Math.Abs(c.b-b.b)<25)continue;
                    if(c.r>c.g*1.25f && c.r>c.b*1.2f)samples.Add(c);
                }
                if(samples.Count==0)return (0,Color.black);
                samples.Sort((a,b)=>a.r.CompareTo(b.r));
                int lo=samples.Count/5, hi=samples.Count-samples.Count/5;
                Color sum=Color.clear; for(int i=lo;i<hi;i++)sum+=samples[i];
                return (samples.Count,sum/Mathf.Max(1,hi-lo));
            }
            Physics.SyncTransforms();
            foreach(var style in new[]{BloodEffectStyle.Legacy,BloodEffectStyle.EffectsPack,BloodEffectStyle.Volumetric})
            {
                OverburstGameSettings.BloodStyle=style; OverburstGameSettings.BloodUniformRed=true;
                BloodComparisonTuning.ResetCurrent(); yield return Frames(3);
                var profile=BloodHitVfxService.ResolveColorProfile(sourceProfile);
                var pool=(BloodEffectsPackPool)typeof(BloodHitVfxService).GetField("packPool",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(blood);
                typeof(BloodHitVfxService).GetMethod("Clear",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(blood,null);
                ground.ClearForComparison(); Time.timeScale=0f; ground.enabled=false;
                yield return Frames(2); var background=Capture(style+"-background");
                var shape=CombatImpactShape.Thrust;
                if(pool!=null)Check(pool.Play(profile,point,direction,shape,1f,0,0,0),"color reference real spray plays "+style);
                else {Check(BloodHitVfxService.RequestAt(profile,point,direction,shape,1f,0),"color reference A request accepted");yield return Frames(3);}
                var graphs=blood.GetComponentsInChildren<VisualEffect>(false).Where(v=>v.gameObject.activeSelf).ToArray();
                var systems=blood.GetComponentsInChildren<ParticleSystem>(false).ToArray();
                float beforeAge=0f; (int count,Color mean) spray=(0,Color.black);
                foreach(float age in new[]{.08f,.2f,.4f,.65f})
                {
                    if(style==BloodEffectStyle.Legacy)foreach(var graph in graphs)graph.Simulate((age-beforeAge)/4f,4);
                    else if(style==BloodEffectStyle.EffectsPack)foreach(var ps in systems)if(!ps.transform.parent || !ps.transform.parent.GetComponent<ParticleSystem>())ps.Simulate(age,true,true,false);
                    else pool.Tick(Time.time+age);
                    beforeAge=age; yield return Frames(2);
                    var sample=Capture(null); var measure=Measure(sample,background);
                    if(measure.count>spray.count){spray=measure;Capture(style+"-spray");}
                }
                Check(spray.count>20,"spray reference has visible real pixels "+style);
                var packCatalog=style==BloodEffectStyle.Legacy?null:Resources.Load<BloodEffectsPackCatalog>(BloodEffectsPackCatalog.ResourceFor(style));
                var template=style==BloodEffectStyle.Volumetric?packCatalog.sprays[pool.LastVariant].groundPrefab:null;
                typeof(BloodHitVfxService).GetMethod("Clear",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(blood,null);
                yield return Frames(3); var groundBackground=Capture(null);
                ground.enabled=true; Time.timeScale=1f;
                ground.Request(profile,center+Vector3.up,direction,shape,1f,2,false,0f,false,template,1f,Quaternion.identity);
                yield return Wait(1.6f);
                Check(ground.ActiveCount==1,"real paired ground mark appears "+style);
                Time.timeScale=0f;
                var landed=Measure(Capture(style+"-ground"),groundBackground);
                var projector=ground.GetComponentsInChildren<DecalProjector>(true).First(p=>p.gameObject.activeSelf);
                var material=projector.material;
                Color.RGBToHSV(spray.mean,out float sprayHue,out float spraySaturation,out float sprayValue);
                Color.RGBToHSV(landed.mean,out float groundHue,out float groundSaturation,out float groundValue);
                float hueDifference=Mathf.Min(Mathf.Abs(sprayHue-groundHue),1f-Mathf.Abs(sprayHue-groundHue));
                rows.Add(new{style=style.ToString(),sprayPixels=spray.count,groundPixels=landed.count,sprayColor=spray.mean.ToString("F4"),groundColor=landed.mean.ToString("F4"),sprayHue,groundHue,hueDifference,sprayValue,groundValue,brightnessRatio=groundValue/Mathf.Max(.001f,sprayValue),shader=material.shader.name});
                File.WriteAllText(Path.Combine(output,"color-comparison.json"),JsonConvert.SerializeObject(rows,Formatting.Indented));
                if(!baselineRun)
                {
                    Check(landed.count>40 && groundValue>.12f,"ground retains visible blood color instead of black "+style);
                    Check(hueDifference<.055f,"ground hue stays close to actual spray "+style);
                    Check(groundValue/sprayValue>.45f && groundValue/sprayValue<1.65f,"ground brightness stays near actual spray "+style);
                    if(style==BloodEffectStyle.Legacy)Check(material.GetColor("_BaseColor")==Color.white,"A neutral multiplier avoids duplicate blood tint");
                    var originalMaterial=material;
                    string tintProperty=style==BloodEffectStyle.Legacy?"_MainColor":style==BloodEffectStyle.EffectsPack?"_BaseColor":"_TintColor";
                    float beforeBrightness=material.GetColor(tintProperty).r;
                    BloodComparisonTuning.Adjust(BloodComparisonTuning.Control.GroundBrightness,1);
                    yield return Frames(3);
                    Check(projector.material==originalMaterial,"live brightness adjustment reuses material "+style);
                    Check(material.GetColor(tintProperty).r>beforeBrightness,"live brightness reaches rendered material "+style);
                    Check(Mathf.Approximately(BloodComparisonTuning.GroundBrightness,style==BloodEffectStyle.EffectsPack?.9f:1.1f),"existing 0.1 brightness adjustment "+style);
                    Color beforeScaleTint=material.GetColor(tintProperty);
                    float beforeSize=projector.size.x;
                    BloodComparisonTuning.Adjust(BloodComparisonTuning.Control.GroundScale,1);
                    yield return Frames(3);
                    Check(projector.material==originalMaterial,"scale adjustment reuses material "+style);
                    Check(material.GetColor(tintProperty)==beforeScaleTint,"size adjustment keeps approved fixed tint "+style);
                    Check(projector.size.x>beforeSize,"live scale adjustment reaches projector "+style);
                }
                ground.ClearForComparison(); yield return Frames(2);
            }
        }
        finally
        {
            Time.timeScale=previousTimeScale; ground.enabled=previousGroundEnabled; ground.ClearForComparison();
            camera.targetTexture=null; if(pixels)Object.DestroyImmediate(pixels); if(rt){rt.Release();Object.DestroyImmediate(rt);}
            Object.DestroyImmediate(cameraRoot);owned.Remove(cameraRoot);
            Object.DestroyImmediate(floor);owned.Remove(floor);Object.DestroyImmediate(surface);
        }
    }
    static IEnumerator VerifyFixedBloodSettings()
    {
        Check(Enum.GetValues(typeof(BloodComparisonTuning.Control)).Length==4,"only four size and brightness controls remain");
        var menu=OverburstGameMenu.Instance; menu.Open(); menu.OpenSettings(); yield return Frames(3);
        var panel=menu.settings; panel.tabs[2].isOn=true; yield return Frames(3);
        Check(panel.bloodRows.Length==4 && panel.combatScroll.content.childCount==18,"formal menu removes three RGB rows");
        Check(Mathf.Approximately(panel.combatScroll.content.sizeDelta.y,2688f),"shorter settings content has no removed-row gap");
        Check(panel.combatScroll.content.GetComponentsInChildren<UnityEngine.UI.Text>(true).All(t=>!t.text.Contains("바닥 빨강") && !t.text.Contains("바닥 초록") && !t.text.Contains("바닥 파랑")),"no RGB captions remain");
        var color=new Color(.5f,.137f,.153f);
        foreach(var style in new[]{BloodEffectStyle.Legacy,BloodEffectStyle.EffectsPack,BloodEffectStyle.Volumetric})
        {
            panel.bloodStyle.SelectOptionByIndex((int)style); yield return Frames(3);
            var fixedColor=BloodComparisonTuning.GroundColor(color);
            var expected=color.linear; expected.r*=style==BloodEffectStyle.EffectsPack?1.7f*.8f:1f; expected.g*=style==BloodEffectStyle.EffectsPack?.8f:1f; expected.b*=style==BloodEffectStyle.EffectsPack?.8f:1f;
            Check((new Vector3(fixedColor.r,fixedColor.g,fixedColor.b)-new Vector3(expected.r,expected.g,expected.b)).sqrMagnitude<.000001f,"legacy extreme RGB ignored; approved tint used "+style);
            Check(!JsonUtility.ToJson(new BloodComparisonTuning.Values(style==BloodEffectStyle.EffectsPack)).Contains("groundRgb"),"RGB field omitted from saved values "+style);
            SetScroll(panel.combatScroll,.2f); yield return Frames(3); float bookmark=panel.combatScroll.verticalNormalizedPosition;
            float scale=BloodComparisonTuning.Scale; yield return Click(panel.bloodRows[0].increase);
            Check(Mathf.Approximately(BloodComparisonTuning.Scale,scale+.1f) && Mathf.Abs(panel.combatScroll.verticalNormalizedPosition-bookmark)<.01f,"remaining size button uses0.1 and keeps scroll "+style);
            SetScroll(panel.combatScroll,0f);yield return Frames(3);
            float brightness=BloodComparisonTuning.GroundBrightness; yield return Click(panel.bloodRows[3].increase);
            Check(Mathf.Approximately(BloodComparisonTuning.GroundBrightness,brightness+.1f),"remaining floor brightness button uses0.1 "+style);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"fixed-settings-"+style+".png"));yield return Frames(2);
            yield return Click(panel.bloodResetButton);
            Check(Mathf.Approximately(BloodComparisonTuning.Scale,scale) && Mathf.Approximately(BloodComparisonTuning.GroundBrightness,brightness),"reset restores approved defaults "+style);
        }
        var temp=TemporaryBloodComparisonToggle.CreateForTesting();owned.Add(temp.gameObject);
        Check(temp.ValueCaptions.Length==4 && temp.DecreaseButtons.Length==4 && temp.IncreaseButtons.Length==4,"explicit preview also has four controls");
        temp.ToggleTuning();yield return Frames(3);ScreenCapture.CaptureScreenshot(Path.Combine(output,"fixed-preview.png"));yield return Frames(2);
        var temporaryRoot=temp.gameObject; owned.Remove(temporaryRoot); Object.DestroyImmediate(temporaryRoot);
        menu.CloseSettings();menu.Close();OverburstGameSettings.SaveIfDirty();yield return Frames(3);
        var json=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(OverburstGameSettings.FilePath));
        Check(new[]{"bloodA","bloodB","bloodC"}.All(k=>json[k]["groundRgb"]==null),"saved file drops RGB while keeping all styles");
        yield return VerifyBloodGroundColor();
    }

}
