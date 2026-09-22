using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// Captures evaluated Unity frames at their actual times; no generated or interpolated animation.
public static class MonsterThemeDamageReviewCapture
{
    public static IEnumerator Capture(EnemyThemeDebugUI ui,PlayerInputFacade player)
    {
        string output=Path.Combine(SessionState.GetString("MonsterThemePlayVerifier.output",""),"DamageReview");
        Directory.CreateDirectory(output);
        if(!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform,out var service))throw new Exception("Spawn service");
        foreach(var table in ui.tables)service.RegisterAdditionalCatalog(table.Catalog,out _);
        var names=SessionState.GetString("MonsterThemeDamageReview.species","").Split(',');
        foreach(var definition in ui.tables.SelectMany(t=>t.Entries).Select(e=>e.definition).Distinct().Where(d=>(names.Length==1 && names[0]=="") || names.Contains(d.EnemyId)))
        {
            {
                string folder=Path.Combine(output,definition.EnemyId);Directory.CreateDirectory(folder);
                Vector3 home=player.transform.position+Vector3.forward*8;
                var request=new EnemySpawnRequest(definition,home,Quaternion.identity,player.transform,null,player.transform,null,1,1,77);
                if(!service.TrySpawn(request,out var actor))throw new Exception("Spawn failed");
                actor.AI.enabled=false;var originalCulling=actor.Animator.cullingMode;actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                GameObject fixture=null;var materials=new List<Material>();
                RenderTexture texture=null;Texture2D pixels=null;
                try
                {
                    MonsterThemeFacingVerifier.Reset(actor,home);
                    float settle=Time.time+.35f;while(Time.time<settle)yield return null;
                    fixture=new GameObject("Turn review fixture");
                    int layer=LayerMask.NameToLayer("Enemy");
                    var probes=MonsterThemeStrideCalibration.CreateRuntimeProbes(actor);
                    float floor=probes.Min(p=>p.sample().y)-.008f;
                    Material Material(Color color){var m=new Material(Shader.Find("Universal Render Pipeline/Unlit"));m.SetColor("_BaseColor",color);materials.Add(m);return m;}
                    var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.name="Review floor";ground.transform.SetParent(fixture.transform);ground.layer=layer;
                    ground.GetComponent<Collider>().enabled=false;ground.transform.position=new Vector3(home.x,floor,home.z);ground.transform.localScale=Vector3.one;
                    ground.GetComponent<Renderer>().sharedMaterial=Material(new Color(.21f,.24f,.27f));
                    var gridMaterial=Material(new Color(.34f,.38f,.42f));
                    void Line(Vector3 a,Vector3 b,Material material,float width)
                    {
                        var go=new GameObject("Review reference");go.transform.SetParent(fixture.transform);go.layer=layer;
                        var line=go.AddComponent<LineRenderer>();line.sharedMaterial=material;line.useWorldSpace=true;line.positionCount=2;line.SetPosition(0,a);line.SetPosition(1,b);line.startWidth=line.endWidth=width;line.shadowCastingMode=ShadowCastingMode.Off;
                    }
                    for(int i=-10;i<=10;i++)
                    {
                        float n=i*.5f;Line(new Vector3(home.x+n,floor+.002f,home.z-5),new Vector3(home.x+n,floor+.002f,home.z+5),gridMaterial,.008f);
                        Line(new Vector3(home.x-5,floor+.002f,home.z+n),new Vector3(home.x+5,floor+.002f,home.z+n),gridMaterial,.008f);
                    }
                    var renderers=actor.VisualRoot.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
                    float size=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
                    var cameraObject=new GameObject("Review camera");cameraObject.transform.SetParent(fixture.transform);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=size*.72f;camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.14f,.17f);
                    camera.transform.position=bounds.center+new Vector3(1,.85f,1).normalized*size*3;camera.transform.LookAt(bounds.center);
                    texture=RenderTexture.GetTemporary(448,448,24);pixels=new Texture2D(448,448,TextureFormat.RGB24,false);camera.targetTexture=texture;
                    var frames=new List<object>();float began=Time.time,next=0;int phase=0;float deathTime=1.8f;float total=deathTime+definition.AnimationProfile.Death.length+1.0f;
                    while(Time.time-began<total)
                    {
                        float elapsed=Time.time-began;
                        if(phase==0 && elapsed>=.4f){actor.Health.TakeDamage(new DamageInfo(1,home+Vector3.up*.5f,player.gameObject,Vector3.forward));phase=1;}
                        if(phase==1 && elapsed>=1.1f){actor.Health.TakeDamage(new DamageInfo(1,home+Vector3.up*.5f,player.gameObject,Vector3.forward));phase=2;}
                        if(phase==2 && elapsed>=deathTime){actor.Health.TakeDamage(new DamageInfo(100000,home+Vector3.up*.5f,player.gameObject,Vector3.forward));phase=3;}
                        if(elapsed>=next)
                        {
                            next=elapsed+.066667f;camera.Render();var previous=RenderTexture.active;
                            try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,448,448),0,0);pixels.Apply();}
                            finally{RenderTexture.active=previous;}
                            string name=frames.Count.ToString("D4")+".png";File.WriteAllBytes(Path.Combine(folder,name),pixels.EncodeToPNG());
                            frames.Add(new{time=elapsed,file=name,phase,leased=actor.IsLeased,y=actor.transform.position.y});
                        }
                        yield return null;
                    }
                    camera.targetTexture=null;
                    File.WriteAllText(Path.Combine(folder,"frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{id=definition.EnemyId,footPlanting=false,deathTime,total,frames},Newtonsoft.Json.Formatting.Indented));
                    Debug.Log("[MonsterDamageReview] captured "+definition.EnemyId+" / hit and death / "+frames.Count);
                }
                finally
                {
                    if(fixture!=null)Object.Destroy(fixture);
                    foreach(var material in materials)Object.Destroy(material);
                    if(texture!=null)RenderTexture.ReleaseTemporary(texture);
                    if(pixels!=null)Object.Destroy(pixels);
                    actor.Animator.cullingMode=originalCulling;actor.RequestPoolRelease();
                }
                yield return null;
            }
        }
    }
}
