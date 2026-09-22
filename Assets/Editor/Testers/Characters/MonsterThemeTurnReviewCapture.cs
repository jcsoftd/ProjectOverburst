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
public static class MonsterThemeTurnReviewCapture
{
    public static IEnumerator Capture(EnemyThemeDebugUI ui,PlayerInputFacade player)
    {
        string output=Path.Combine(SessionState.GetString("MonsterThemePlayVerifier.output",""),"TurnReview");
        Directory.CreateDirectory(output);
        if(!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform,out var service))throw new Exception("Spawn service");
        foreach(var table in ui.tables)service.RegisterAdditionalCatalog(table.Catalog,out _);
        var names=SessionState.GetString("MonsterThemeTurnReview.species","VenomBrood_Venodonte_Tint1,PrimalHunt_Venosaur_Tint_Brown,PrimalHunt_Caniathrox").Split(',');
        foreach(var definition in ui.tables.SelectMany(t=>t.Entries).Select(e=>e.definition).Distinct().Where(d=>names.Contains(d.EnemyId)))
        {
            var contacts=MonsterThemeStrideCalibration.CreateTurnBindings(definition);
            foreach(bool planting in new[]{false,true})
            {
                string folder=Path.Combine(output,definition.EnemyId+(planting?"-after":"-before"));Directory.CreateDirectory(folder);
                Vector3 home=player.transform.position+Vector3.forward*8;
                var request=new EnemySpawnRequest(definition,home,Quaternion.identity,player.transform,null,player.transform,null,1,1,77);
                if(!service.TrySpawn(request,out var actor))throw new Exception("Spawn failed");
                actor.AI.enabled=false;var originalCulling=actor.Animator.cullingMode;actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                EnemyTurnFootPlanting support=null;GameObject fixture=null;var materials=new List<Material>();
                var existingSupport=actor.GetComponent<EnemyTurnFootPlanting>();
                bool originalSupportEnabled=existingSupport!=null && existingSupport.enabled;
                RenderTexture texture=null;Texture2D pixels=null;
                try
                {
                    if(existingSupport!=null)existingSupport.enabled=planting;
                    if(planting){support=existingSupport ?? actor.gameObject.AddComponent<EnemyTurnFootPlanting>();support.Configure(contacts);}
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
                    var arrowMaterial=Material(new Color(.25f,.8f,.65f));Vector3 origin=new Vector3(home.x,floor+.004f,home.z);
                    Vector3 targetDirection=Quaternion.Euler(0,45,0)*Vector3.forward;
                    Line(origin,origin+targetDirection*1.8f,arrowMaterial,.015f);
                    Line(origin+targetDirection*1.8f,origin+targetDirection*1.55f+Quaternion.Euler(0,90,0)*targetDirection*.12f,arrowMaterial,.015f);
                    Line(origin+targetDirection*1.8f,origin+targetDirection*1.55f-Quaternion.Euler(0,90,0)*targetDirection*.12f,arrowMaterial,.015f);
                    var renderers=actor.VisualRoot.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
                    float size=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
                    var cameraObject=new GameObject("Review camera");cameraObject.transform.SetParent(fixture.transform);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=size*.72f;camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.14f,.17f);
                    camera.transform.position=bounds.center+new Vector3(1,.85f,1).normalized*size*3;camera.transform.LookAt(bounds.center);
                    texture=RenderTexture.GetTemporary(448,448,24);pixels=new Texture2D(448,448,TextureFormat.RGB24,false);camera.targetTexture=texture;
                    var frames=new List<object>();float began=Time.time,next=0;
                    while(Time.time-began<2.8f)
                    {
                        float elapsed=Time.time-began;
                        if(elapsed>=.4f)actor.Movement.FacePosition(home+targetDirection*10);
                        if(elapsed>=next)
                        {
                            next=elapsed+.05f;camera.Render();var previous=RenderTexture.active;
                            try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,448,448),0,0);pixels.Apply();}
                            finally{RenderTexture.active=previous;}
                            string name=frames.Count.ToString("D4")+".png";File.WriteAllBytes(Path.Combine(folder,name),pixels.EncodeToPNG());
                            frames.Add(new{time=elapsed,file=name,yaw=Mathf.DeltaAngle(0,actor.transform.eulerAngles.y)});
                        }
                        yield return null;
                    }
                    camera.targetTexture=null;
                    File.WriteAllText(Path.Combine(folder,"frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{id=definition.EnemyId,planting,angle=45,frames},Newtonsoft.Json.Formatting.Indented));
                    Debug.Log("[MonsterTurnReview] captured "+definition.EnemyId+" / "+planting+" / "+frames.Count);
                }
                finally
                {
                    if(support!=null && support!=existingSupport)Object.Destroy(support);
                    if(existingSupport!=null)existingSupport.enabled=originalSupportEnabled;
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
