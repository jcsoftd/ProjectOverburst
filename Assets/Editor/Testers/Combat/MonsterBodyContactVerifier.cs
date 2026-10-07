using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static partial class MonsterBodyContactVerifier
{
    static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static IEnumerable<AnimationClip> Clips(EnemyDefinition d)
    {
        var p=d.AnimationProfile;
        var controller=d.ActorPrefab.Animator.runtimeAnimatorController;
        return new[]{p.Idle,p.Walk,p.Run,p.Hit,p.Death,p.ParryCollapse,p.StunnedLoop,p.StunRecover}
            .Concat(Enumerable.Range(0,p.AttackClipCount).Select(p.GetAttackClip))
            .Concat(controller?controller.animationClips:System.Array.Empty<AnimationClip>()).Where(c=>c).Distinct();
    }
    static Vector3 Surface(GameObject root,JToken contact)
    {
        Vector3 sum=Vector3.zero;float total=0;
        foreach(var w in contact["weights"]){var bone=root.transform.Find((string)w["bone"]);Assert(bone,"Surface bone missing");float weight=(float)w["weight"];
            sum+=bone.TransformPoint(MonsterBodyContactBuilder.Vector(w["point"]))*weight;total+=weight;}
        Assert(Mathf.Abs(total-1)<.0001f,"Normalized skin weights");return sum/total;
    }
    public static string Native(string directory,bool candidate=false,bool capture=true,string excludedId="")
    {
        MonsterBodyContactBuilder.RequireIdle();string folder=MonsterBodyContactBuilder.Output(directory);
        var plan=JObject.Parse(File.ReadAllText(Path.Combine(folder,"plan.json")));Assert((string)plan["status"]=="PASS","Complete plan required");
        var baseline=JObject.Parse(File.ReadAllText(Path.Combine(folder,"baseline.json")))["rows"];
        var scene=EditorSceneManager.NewPreviewScene();var results=new JArray();int checks=0,poses=0;float maximumError=0;
        string name=candidate?"candidate":"native";
        try{
            foreach(var row in plan["rows"].Where(r=>(string)r["id"]!=excludedId)){
                var d=AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)row["definition"]);GameObject root=null;PlayableGraph graph=default;var bakes=new List<Mesh>();
                try{
                    root=(GameObject)PrefabUtility.InstantiatePrefab(d.ActorPrefab.gameObject,scene);root.SetActive(true);
                    foreach(var b in root.GetComponentsInChildren<MonoBehaviour>(true))if(b)b.enabled=false;
                    if(candidate)MonsterBodyContactBuilder.Configure(root,row);
                    var placement=root.GetComponent<CombatTargetVfxPlacement>();var target=root.GetComponent<CombatTarget>();
                    Assert(placement && placement.HasBodyContacts,"Authored body contacts "+d.EnemyId);checks++;
                    Assert(root.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"Missing Script "+d.EnemyId);checks++;
                    var actor=root.GetComponent<EnemyActor>();var skins=MonsterBodyContactBuilder.ReadSkins(actor);
                    foreach(var skin in skins)bakes.Add(new Mesh());
                    var transforms=root.GetComponentsInChildren<Transform>(true);var positions=transforms.Select(t=>t.localPosition).ToArray();var rotations=transforms.Select(t=>t.localRotation).ToArray();var scales=transforms.Select(t=>t.localScale).ToArray();
                    int ownPoses=0;float ownError=0;int ownChecks=0;
                    foreach(var clip in Clips(d)){
                        for(int i=0;i<transforms.Length;i++){transforms[i].SetLocalPositionAndRotation(positions[i],rotations[i]);transforms[i].localScale=scales[i];}
                        foreach(float time in new[]{0f,.2f,.4f,.6f,.8f,.98f}){
                            graph=MonsterBodyContactBuilder.Pose(actor,clip,time);
                            for(int s=0;s<skins.Count;s++){
                                // BakeMesh(true) gives renderer-local vertices. Applying the full
                                // renderer transform then matches the engine's posed skin exactly.
                                skins[s].renderer.BakeMesh(bakes[s],true);
                                skins[s].world=bakes[s].vertices.Select(skins[s].renderer.transform.TransformPoint).ToArray();
                            }
                            var allPoints=skins.SelectMany(s=>s.world).ToArray();var bounds=new Bounds(allPoints[0],Vector3.zero);foreach(var p in allPoints)bounds.Encapsulate(p);
                            foreach(var contact in row["contacts"]){
                                string renderer=(string)contact["renderer"];var skin=skins.Single(s=>AnimationUtility.CalculateTransformPath(s.renderer.transform,root.transform)==renderer);
                                int[] ids=contact["vertices"].Select(v=>(int)v).ToArray();Vector3 bary=MonsterBodyContactBuilder.Vector(contact["barycentric"]);
                                Vector3 actual=skin.world[ids[0]]*bary.x+skin.world[ids[1]]*bary.y+skin.world[ids[2]]*bary.z;
                                Vector3 authored=Surface(root,contact);float error=Vector3.Distance(actual,authored);ownError=Mathf.Max(ownError,error);
                                Assert(error<.001f,"Skin surface drift "+d.EnemyId+" "+clip.name+" "+time);checks++;ownChecks++;
                            }
                            for(int sector=0;sector<16;sector++){
                                Vector3 direction=Quaternion.Euler(0,sector*22.5f,0)*Vector3.forward;
                                Vector3 raw=target.CurrentHurtVolume.Center;
                                Vector3 contact=CombatTargetVfxPlacement.ResolveContact(target,raw,direction,out float size);
                                float nearest=row["contacts"].Min(c=>Vector3.Distance(Surface(root,c),contact));
                                Assert(nearest<.001f && bounds.Contains(contact),"Resolved origin is on the current mesh "+d.EnemyId);checks++;ownChecks++;
                                Vector3 high=CombatTargetVfxPlacement.ResolveContact(target,raw+Vector3.up*30,direction,out float highSize);
                                Vector3 low=CombatTargetVfxPlacement.ResolveContact(target,raw-Vector3.up*30,direction,out _);
                                Assert((high-contact).sqrMagnitude<1e-8f && (low-contact).sqrMagnitude<1e-8f && Mathf.Approximately(highSize,size),"Raw height cannot move torso origin "+d.EnemyId);checks++;ownChecks++;
                            }
                            ownPoses++;poses++;
                            graph.Destroy();graph=default;
                        }
                    }
                    maximumError=Mathf.Max(maximumError,ownError);
                    if(capture){
                        graph=MonsterBodyContactBuilder.Pose(actor,d.AnimationProfile.Idle,0);
                        var before=baseline.Single(r=>(string)r["id"]==d.EnemyId);
                        Capture(root,row,before,Path.Combine(folder,"Review",d.EnemyId),scene);
                        graph.Destroy();graph=default;
                    }
                    results.Add(new JObject{{"id",d.EnemyId},{"poses",ownPoses},{"checks",ownChecks},{"maxSurfaceError",ownError}});
                    File.WriteAllText(Path.Combine(folder,name+".json"),new JObject{{"status","RUNNING"},{"checks",checks},{"poses",poses},{"rows",results}}.ToString());
                }finally{if(graph.IsValid())graph.Destroy();foreach(var bake in bakes)if(bake)Object.DestroyImmediate(bake);if(root)Object.DestroyImmediate(root);}
            }
            File.WriteAllText(Path.Combine(folder,name+".json"),new JObject{{"status","PASS"},{"checks",checks},{"poses",poses},{"maxSurfaceError",maximumError},{"rows",results}}.ToString());
            return "PASS "+name+" "+results.Count+" actors / "+poses+" poses / "+checks+" checks";
        }catch(Exception e){File.WriteAllText(Path.Combine(folder,name+".json"),new JObject{{"status","FAIL"},{"error",e.ToString()},{"rows",results}}.ToString());throw;}
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void Capture(GameObject root,JToken row,JToken before,string file,UnityEngine.SceneManagement.Scene scene)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file));var owned=new List<Object>();RenderTexture rt=null;Texture2D pixels=null;Camera cam=null;
        try{
            var actor=root.GetComponent<EnemyActor>();foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            var skins=MonsterBodyContactBuilder.ReadSkins(actor);var points=skins.SelectMany(s=>s.world).ToArray();var bounds=new Bounds(points[0],Vector3.zero);foreach(var p in points)bounds.Encapsulate(p);
            // A static copy of the actual skinned vertices avoids first-camera buffer/culling lag
            // in a preview scene. Originals, materials and imported meshes remain untouched.
            foreach(var skin in skins){
                var mesh=Object.Instantiate(skin.mesh);owned.Add(mesh);skin.renderer.BakeMesh(mesh,true);
                mesh.vertices=mesh.vertices.Select(skin.renderer.transform.TransformPoint).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
                var visible=new GameObject("Owned posed torso mesh",typeof(MeshFilter),typeof(MeshRenderer));owned.Add(visible);EditorSceneManager.MoveGameObjectToScene(visible,scene);visible.layer=31;
                visible.GetComponent<MeshFilter>().sharedMesh=mesh;visible.GetComponent<MeshRenderer>().sharedMaterials=skin.renderer.sharedMaterials;skin.renderer.enabled=false;
            }
            float markerSize=Mathf.Clamp(bounds.size.y*.018f,.018f,.1f);
            void Marker(Vector3 p,Color color,float size){var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);owned.Add(go);EditorSceneManager.MoveGameObjectToScene(go,scene);go.layer=31;go.transform.position=p;go.transform.localScale=Vector3.one*size;
                Object.DestroyImmediate(go.GetComponent<Collider>());var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));owned.Add(material);material.SetColor("_BaseColor",color);go.GetComponent<Renderer>().sharedMaterial=material;}
            foreach(var p in before["contacts"])Marker(MonsterBodyContactBuilder.Vector(p["point"]),new Color(1,.14f,.18f),markerSize*1.65f);
            foreach(var contact in row["contacts"])Marker(Surface(root,contact),new Color(.06f,1f,.9f),markerSize);
            Marker(root.GetComponent<CombatTargetVfxPlacement>().BodyContactCenter,new Color(1,.8f,.15f),markerSize*1.3f);
            var rig=new GameObject("Owned body contact camera");owned.Add(rig);EditorSceneManager.MoveGameObjectToScene(rig,scene);cam=rig.AddComponent<Camera>();cam.scene=scene;cam.enabled=false;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.075f,.085f,.11f);cam.cullingMask=1<<31;
            cam.orthographic=true;cam.orthographicSize=Mathf.Max(bounds.extents.y,bounds.extents.z*.7f)*1.27f;cam.nearClipPlane=.01f;cam.farClipPlane=200;
            cam.GetUniversalAdditionalCameraData().renderShadows=false;
            var lamp=new GameObject("Owned torso preview light");owned.Add(lamp);EditorSceneManager.MoveGameObjectToScene(lamp,scene);var light=lamp.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.cullingMask=1<<31;lamp.transform.rotation=Quaternion.Euler(40,-25,0);
            rt=new RenderTexture(640,480,24,RenderTextureFormat.ARGB32);rt.Create();cam.targetTexture=rt;pixels=new Texture2D(640,480,TextureFormat.RGB24,false);
            for(int view=0;view<2;view++){
                var direction=view==0?new Vector3(.12f,.12f,1f):new Vector3(1f,.10f,.10f);
                cam.transform.position=bounds.center+direction.normalized*Mathf.Max(5,bounds.size.magnitude*2);cam.transform.LookAt(bounds.center);
                var previous=RenderTexture.active;
                try{cam.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,640,480),0,0);pixels.Apply();
                    var colors=pixels.GetPixels32();var background=colors[0];Assert(colors.Count(c=>Math.Abs(c.r-background.r)+Math.Abs(c.g-background.g)+Math.Abs(c.b-background.b)>30)>500,"Capture contains model pixels "+root.name);
                    File.WriteAllBytes(file+(view==0?"_front.png":"_side.png"),pixels.EncodeToPNG());}
                finally{RenderTexture.active=previous;}
            }
        }finally{if(cam)cam.targetTexture=null;if(rt){rt.Release();Object.DestroyImmediate(rt);}if(pixels)Object.DestroyImmediate(pixels);for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);}
    }
    public static string CaptureReview(string directory,bool candidate=true)
    {
        MonsterBodyContactBuilder.RequireIdle();string folder=MonsterBodyContactBuilder.Output(directory);var plan=JObject.Parse(File.ReadAllText(Path.Combine(folder,"plan.json")));
        var baseline=JObject.Parse(File.ReadAllText(Path.Combine(folder,"baseline.json")))["rows"];var scene=EditorSceneManager.NewPreviewScene();int count=0;
        try{foreach(var row in plan["rows"]){var d=AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)row["definition"]);GameObject root=null;PlayableGraph graph=default;
            try{root=(GameObject)PrefabUtility.InstantiatePrefab(d.ActorPrefab.gameObject,scene);root.SetActive(true);foreach(var b in root.GetComponentsInChildren<MonoBehaviour>(true))if(b)b.enabled=false;
                if(candidate)MonsterBodyContactBuilder.Configure(root,row);graph=MonsterBodyContactBuilder.Pose(root.GetComponent<EnemyActor>(),d.AnimationProfile.Idle,0);
                Capture(root,row,baseline.Single(r=>(string)r["id"]==d.EnemyId),Path.Combine(folder,"Review",d.EnemyId),scene);count++;
            }finally{if(graph.IsValid())graph.Destroy();if(root)Object.DestroyImmediate(root);}
        }
        File.WriteAllText(Path.Combine(folder,"captures.json"),new JObject{{"status","PASS"},{"actors",count},{"images",count*2},{"candidate",candidate}}.ToString());return "PASS actual model captures "+count*2;
        }finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
}
