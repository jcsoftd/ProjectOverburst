using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

public static partial class SettingsPresentationVerifier
{
    [UnityEditor.MenuItem("OVERBURST/Tests/Blood/Screen scale")]
    public static void StartBloodScreenScale()
    {
        RequireIdle();
        string parent=Path.GetFullPath("../개인파일/코덱스산출/CombatVfx/"+DateTime.Now.ToString("yyyyMMdd")+"_BloodScreenScale");
        string directory;int index=1;do{directory=Path.Combine(parent,"Run"+(index++).ToString("D2"));}while(Directory.Exists(directory));
        Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"only-blood-scale"),"");
        StartVolumetric(directory);
    }
    static IEnumerator VerifyBloodScreenScale()
    {
        var blood=Object.FindFirstObjectByType<BloodHitVfxService>();
        var ground=Object.FindFirstObjectByType<BloodGroundDecalService>();
        var profile=Resources.Load<BloodHitProfile>(BloodHitVfxService.PlayerProfilePath);
        var main=Camera.main;var player=PlayerInputFacade.Current;
        var point=player.transform.position+Vector3.up*1.1f+main.transform.right*2f;
        var direction=Vector3.ProjectOnPlane(main.transform.right,Vector3.up).normalized;
        var root=new GameObject("Owned C Scale Camera");owned.Add(root);var camera=root.AddComponent<Camera>();camera.CopyFrom(main);camera.enabled=false;
        camera.GetUniversalAdditionalCameraData().SetRenderer(new UnityEditor.SerializedObject(main.GetUniversalAdditionalCameraData()).FindProperty("m_RendererIndex").intValue);
        camera.orthographic=true;camera.orthographicSize=4f;camera.transform.position=point-main.transform.forward*12f;camera.transform.rotation=main.transform.rotation;
        RenderTexture rt=null;Texture2D pixels=null;float timeScale=Time.timeScale;bool groundEnabled=ground.enabled;
        var rows=new List<object>();
        try
        {
            Time.timeScale=0f;ground.enabled=false;ground.ClearForComparison();
            rt=new RenderTexture(900,600,24,RenderTextureFormat.ARGB32){name="Owned C Scale Render"};rt.Create();pixels=new Texture2D(900,600,TextureFormat.RGB24,false);camera.targetTexture=rt;
            Color32[] Capture(string name)
            {
                var previous=RenderTexture.active;
                try{camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,900,600),0,0);pixels.Apply();if(name!=null)File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());return pixels.GetPixels32();}
                finally{RenderTexture.active=previous;}
            }
            foreach(var style in new[]{BloodEffectStyle.Legacy,BloodEffectStyle.EffectsPack,BloodEffectStyle.Volumetric})
            {
                OverburstGameSettings.BloodStyle=style;OverburstGameSettings.BloodUniformRed=true;BloodComparisonTuning.ResetCurrent();yield return Frames(4);
                var pool=(BloodEffectsPackPool)typeof(BloodHitVfxService).GetField("packPool",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(blood);
                var catalog=style==BloodEffectStyle.Legacy?null:Resources.Load<BloodEffectsPackCatalog>(BloodEffectsPackCatalog.ResourceFor(style));
                int count=style==BloodEffectStyle.Legacy?6:catalog.sprays.Length;
                for(int form=0;form<count;form++)
                {
                    if(style==BloodEffectStyle.Legacy && form>0){OverburstGameSettings.BloodStyle=BloodEffectStyle.EffectsPack;OverburstGameSettings.BloodStyle=style;yield return Frames(3);}
                    if(pool!=null)pool.Clear();ground.ClearForComparison();yield return Frames(2);var baseline=Capture(null);
                    bool drip=catalog!=null && (style==BloodEffectStyle.Volumetric?form>=17:form>=5);
                    bool accent=style==BloodEffectStyle.Legacy?form>=3:catalog.sprays[form].impactAccent;
                    var shape=style==BloodEffectStyle.Legacy?(CombatImpactShape)(form%3):CombatImpactShape.Sweep;
                    int priority=drip?0:accent?2:catalog!=null?catalog.sprays[form].minimumPriority:0;
                    var samplePoint=drip?point-Vector3.up*.5f:point;var sampleDirection=drip?Vector3.down:direction;
                    if(catalog!=null)
                    {
                        var definition=catalog.sprays[form];
                        if(!drip && !definition.Accepts(shape,priority,accented:accent))shape=definition.Accepts(CombatImpactShape.Thrust,priority,accented:accent)?CombatImpactShape.Thrust:CombatImpactShape.Downward;
                        uint seed=0;while(seed<1000 && catalog.ResolveSpray(shape,priority,seed,-1,drip,accent)!=form)seed++;
                        Check(seed<1000 && pool.Play(BloodHitVfxService.ResolveColorProfile(profile),samplePoint,sampleDirection,shape,drip?.12f:1f,priority,seed,0,drip,accent),"scale capture form plays "+style+form);
                    }
                    else
                    {
                        Check(BloodHitVfxService.RequestAt(profile,point,direction,shape,1f,priority),"A real request for scale capture "+shape+priority);
                        yield return Frames(4);
                    }
                    var graphs=blood.GetComponentsInChildren<VisualEffect>(false).Where(v=>v.gameObject.activeSelf).ToArray();
                    var systems=blood.GetComponentsInChildren<ParticleSystem>(false).Where(p=>p.gameObject.activeInHierarchy).ToArray();
                    float ageBefore=0f;float peakSpan=0f;int peakVisible=0;float peakAge=0f;int peakWidth=0,peakHeight=0;
                    foreach(float age in (drip?new[]{.04f,.1f,.18f,.3f}:new[]{.08f,.2f,.4f,.7f,1f}))
                    {
                        if(style==BloodEffectStyle.Legacy)foreach(var graph in graphs)graph.Simulate((age-ageBefore)/4f,4);
                        else if(style==BloodEffectStyle.EffectsPack)foreach(var ps in systems)if(ps.transform.parent==null || !ps.transform.parent.GetComponent<ParticleSystem>())ps.Simulate(age,true,true,false);
                        else pool.Tick(Time.time+age);
                        ageBefore=age;yield return Frames(2);var sample=Capture(null);var xs=new List<int>();var ys=new List<int>();
                        for(int p=0;p<sample.Length;p++)
                        {
                            var c=sample[p];var b=baseline[p];
                            if(Math.Abs(c.r-b.r)+Math.Abs(c.g-b.g)+Math.Abs(c.b-b.b)>25 && c.r>c.g*1.15f && c.r>c.b*1.15f){xs.Add(p%900);ys.Add(p/900);}
                        }
                        if(xs.Count<4)continue;xs.Sort();ys.Sort();int lo=xs.Count/20,hi=xs.Count-1-lo;int width=xs[hi]-xs[lo]+1,height=ys[hi]-ys[lo]+1;float span=Mathf.Sqrt(width*height);
                        if(span>peakSpan){peakSpan=span;peakWidth=width;peakHeight=height;peakVisible=xs.Count;peakAge=age;Capture("scale_"+style+"_"+form);}
                    }
                    rows.Add(new{style=style.ToString(),form,label=catalog!=null?catalog.sprays[form].label:shape.ToString(),shape=shape.ToString(),drip,accent,priority,baseScale=catalog!=null?catalog.sprays[form].scale:1f,userScale=BloodComparisonTuning.Scale,peakSpan,peakWidth,peakHeight,peakVisible,peakAge});
                    File.WriteAllText(Path.Combine(output,"screen-scale.json"),JsonConvert.SerializeObject(rows,Formatting.Indented));
                    Check(peakSpan>1f,"scale capture has real blood pixels "+style+form);
                }
            }
        }
        finally
        {
            Time.timeScale=timeScale;ground.ClearForComparison();ground.enabled=groundEnabled;camera.targetTexture=null;
            if(pixels)Object.DestroyImmediate(pixels);if(rt){rt.Release();Object.DestroyImmediate(rt);}Object.DestroyImmediate(root);owned.Remove(root);
        }
    }
}
