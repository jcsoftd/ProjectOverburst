// 별도 시안 전용. 정식 HideoutScene에는 저장하지 않는다.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class HideoutTerrainStudyBuilder {
 public const string Output="../개인파일/코덱스산출/Environment/20261004_HideoutTerrainStudy";
 public const string Root="Assets/ProjectOverburst/05_Art/Environment/BarbarianHideout/Prototypes/TerrainStudy_20261004";
 public const string ScenePath="Assets/ProjectOverburst/00_Scenes/Previews/HideoutTerrainStudy_20261004.unity";
 const int CX=216,CZ=152;
 class Prop {public Transform t;public Vector3 p,scale;public Quaternion rot;public Bounds bounds;public float x,z,oldHeight,offset;public bool pad;public float target;}
 static List<Prop> props;
 static List<Vector2[]> routes;
 static float Smooth(float a,float b,float v)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v));
 static float Hill(float x,float z,float cx,float cz,float rx,float rz){float a=(x-cx)/rx,b=(z-cz)/rz;return Mathf.Exp(-(a*a+b*b)*1.55f);}
 static float Base(float x,float z) {
  float height=2.65f*Hill(x,z,-16,8,16,23)+1.2f*Hill(x,z,0,25,24,13)
   +1.1f*Hill(x,z,-19,-13,10,14)+.9f*Hill(x,z,19,13,9,18);
  float center=Smooth(2.8f,8f,Vector2.Distance(new Vector2(x,z),new Vector2(-.65f,-1.4f)));
  float camp=1-Smooth(22,30,x); // Keep the separate catalog/training field at its authored elevation.
  float edge=Mathf.Min(x+36,72-x,z+30,46-z);
  float detail=(Mathf.PerlinNoise((x+90)*.09f,(z+141)*.09f)-.5f)*.19f
   +(Mathf.PerlinNoise((x+24)*.25f,(z+43)*.25f)-.5f)*.06f;
  return (height*center+detail*center)*Smooth(0,8,edge)*camp;
 }
 static float Height(float x,float z) {
  float raw=Base(x,z),total=0,sum=0,strongest=0;
  foreach(var p in props) {
   if(!p.pad)continue;
   float dx=Mathf.Max(p.bounds.min.x-.2f-x,0,x-p.bounds.max.x-.2f);
   float dz=Mathf.Max(p.bounds.min.z-.2f-z,0,z-p.bounds.max.z-.2f);
   float w=1-Smooth(0,6f,Mathf.Sqrt(dx*dx+dz*dz));
   if(w<=0)continue;float priority=Mathf.Pow(w,8);total+=p.target*priority;sum+=priority;strongest=Mathf.Max(strongest,w);
  }
  return sum>0?Mathf.Lerp(raw,total/sum,strongest):raw;
 }
 static Transform[] All(Scene s)=>s.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
 static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;Folder(Path.GetDirectoryName(path).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}
 static string PathOf(Transform t,Transform root){var names=new List<string>();while(t!=root&&t!=null){names.Add(t.name);t=t.parent;}names.Reverse();return string.Join("/",names);}
 public static string Build() {
  if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new Exception("Idle Editor required; no changes made.");
  if(SceneManager.GetSceneByPath(HideoutPerlinGroundBuilder.ScenePath).isLoaded)throw new Exception("Product Hideout is already open; preserve it.");
  if(AssetDatabase.IsValidFolder(Root)||File.Exists(ScenePath))throw new Exception("This prototype already exists; inspect before rebuilding.");
  Directory.CreateDirectory(Output);
  var setup=BarbarianCampUrpVerifier.EditorSnapshot();var active=SceneManager.GetActiveScene();var previousProbe=RenderSettings.ambientProbe;
  var hashes=ProductionHashes();File.WriteAllText(Path.Combine(Output,"production_before.json"),JsonConvert.SerializeObject(hashes,Formatting.Indented));
  var scene=EditorSceneManager.OpenScene(HideoutPerlinGroundBuilder.ScenePath,OpenSceneMode.Additive);
  Mesh generated=null;Material hazeClone=null;
  try {
   SceneManager.SetActiveScene(scene);var all=All(scene);var ground=all.Single(t=>t.name=="Camp Ground");
   // Additive captures do not refresh the ambient probe. Seed it from the camp's authored ambient colors.
   var probe=new SphericalHarmonicsL2();probe.AddAmbientLight(RenderSettings.ambientEquatorColor.linear*.8f);RenderSettings.ambientProbe=probe;
   var layout=all.Single(t=>t.name=="Camp Layout");var collider=ground.GetComponent<MeshCollider>();
   props=new List<Prop>();
   foreach(Transform group in layout)foreach(Transform t in group) {
    var rs=t.GetComponentsInChildren<Renderer>(true);if(rs.Length==0)continue;
    var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);
    // Supplier pivots vary; use the actual mesh footprint for grounded props.
    float x=b.center.x,z=b.center.z;
    if(!collider.Raycast(new Ray(new Vector3(x,15,z),Vector3.down),out var hit,30))continue;
    float offset=b.min.y-hit.point.y;
    if(offset>.65f||offset< -1.2f)continue;
    bool pad=group.name!="Nature" && b.size.x*b.size.z>3.5f && b.size.y>1.2f && Mathf.Max(b.size.x,b.size.z)>2;
    props.Add(new Prop{t=t,p=t.position,rot=t.rotation,scale=t.localScale,bounds=b,x=x,z=z,oldHeight=hit.point.y,offset=offset,pad=pad,target=Base(x,z)});
   }
   var pads=props.Where(p=>p.pad).ToArray();var parent=Enumerable.Range(0,pads.Length).ToArray();
   Func<int,int> find=null;find=i=>parent[i]==i?i:(parent[i]=find(parent[i]));
   for(int i=0;i<pads.Length;i++)for(int j=i+1;j<pads.Length;j++)if(pads[i].bounds.min.x-.25f<=pads[j].bounds.max.x+.25f&&pads[i].bounds.max.x+.25f>=pads[j].bounds.min.x-.25f&&pads[i].bounds.min.z-.25f<=pads[j].bounds.max.z+.25f&&pads[i].bounds.max.z+.25f>=pads[j].bounds.min.z-.25f)parent[find(j)]=find(i);
   foreach(var cluster in Enumerable.Range(0,pads.Length).GroupBy(find)){float elevation=cluster.Average(i=>pads[i].target);foreach(int i in cluster)pads[i].target=elevation;}
   routes=new List<Vector2[]> {
    new[]{new Vector2(0,-18),new Vector2(-.5f,-9),new Vector2(-.65f,-1.4f),new Vector2(-1,6),new Vector2(-2,12)},
    new[]{new Vector2(-.65f,-1.4f),new Vector2(-5,-.7f),new Vector2(-9.5f,1.5f)},
    new[]{new Vector2(-.65f,-1.4f),new Vector2(5,1),new Vector2(10,5)},
    new[]{new Vector2(-1,6),new Vector2(6,8),new Vector2(12,7),new Vector2(24,5)}
   };
   foreach(var p in all.Where(t=>t.name.EndsWith("MerchantObject")||t.name=="StashObject"))routes.Add(new[]{new Vector2(-.65f,-1.4f),new Vector2(p.position.x,p.position.z)});
   var haze=all.Single(t=>t.name=="Hideout Ground Haze").GetComponent<Renderer>();hazeClone=new Material(haze.sharedMaterial);hazeClone.SetFloat("_PreviewTime",12);haze.sharedMaterial=hazeClone;
   foreach(var p in all.Select(t=>t.GetComponent<ParticleSystem>()).Where(p=>p!=null))p.Simulate(18,true,true,true);
   Capture(scene,"before");
   Folder(Root);Folder(Path.GetDirectoryName(ScenePath).Replace('\\','/'));
   var vertices=new Vector3[(CX+1)*(CZ+1)];var uvs=new Vector2[vertices.Length];var triangles=new int[CX*CZ*6];
   for(int z=0;z<=CZ;z++)for(int x=0;x<=CX;x++){
    int i=z*(CX+1)+x;float wx=-36+x*.5f,wz=-30+z*.5f;vertices[i]=ground.InverseTransformPoint(new Vector3(wx,Height(wx,wz),wz));uvs[i]=new Vector2(wx/6,wz/6);
   }
   int n=0;for(int z=0;z<CZ;z++)for(int x=0;x<CX;x++){int a=z*(CX+1)+x,b=a+1,c=a+CX+1,d=c+1;triangles[n++]=a;triangles[n++]=c;triangles[n++]=b;triangles[n++]=b;triangles[n++]=c;triangles[n++]=d;}
   generated=new Mesh{name="Hideout terrain study",vertices=vertices,uv=uvs,triangles=triangles};generated.RecalculateNormals();generated.RecalculateTangents();generated.RecalculateBounds();
   AssetDatabase.CreateAsset(generated,Root+"/Ground.asset");generated=null;
   var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Ground.asset");ground.GetComponent<MeshFilter>().sharedMesh=mesh;collider.sharedMesh=mesh;
   var control=Control();AssetDatabase.CreateAsset(control,Root+"/TerrainMasks.asset");
   var old=ground.GetComponent<Renderer>().sharedMaterial;
   var material=new Material(old){name="Hideout terrain study soil"};AssetDatabase.CreateAsset(material,Root+"/Ground.mat");ground.GetComponent<Renderer>().sharedMaterial=material;
   var changes=new List<object>();foreach(var p in props){float delta=Height(p.x,p.z)-p.oldHeight;p.t.position=p.p+Vector3.up*delta;PrefabUtility.RecordPrefabInstancePropertyModifications(p.t);changes.Add(new{path=PathOf(p.t,layout),delta,x=p.p.x,z=p.p.z});}
   // Service model roots, ambient fire and static anchors follow their new local ground only in the copy.
   foreach(var t in all.Where(t=>t.name.EndsWith("MerchantObject")||t.name=="StashObject"||t.name=="HubReturnPoint"||t.name=="Campfire Flames"||t.name=="Cauldron Smoke"||t.name=="Campfire Embers"||t.name=="Campfire Light")) {
    if(t.IsChildOf(layout))continue;float delta=Height(t.position.x,t.position.z);t.position+=Vector3.up*delta;
   }
   var heights=new Texture2D(217,153,TextureFormat.RFloat,false,true){name="Prototype ground height",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
   var colors=Enumerable.Range(0,217*153).Select(i=>new Color(Height(-36+(i%217)*.5f,-30+(i/217)*.5f),0,0,1)).ToArray();heights.SetPixels(colors);heights.Apply(false,false);AssetDatabase.CreateAsset(heights,Root+"/HazeGroundHeight.asset");
   hazeClone.SetTexture("_GroundHeight",heights);hazeClone.SetFloat("_PreviewTime",-1);AssetDatabase.CreateAsset(hazeClone,Root+"/Haze.mat");hazeClone=null;
   Physics.SyncTransforms();
   var values=vertices.Select(ground.TransformPoint).ToArray();float maxSlope=0;int rays=0;float mismatch=0;
   for(int z=2;z<CZ-2;z+=5)for(int x=2;x<CX-2;x+=5){var p=values[z*(CX+1)+x];if(!collider.Raycast(new Ray(p+Vector3.up*12,Vector3.down),out var hit,24))throw new Exception("Missing ground collision");rays++;mismatch=Mathf.Max(mismatch,Mathf.Abs(hit.point.y-p.y));maxSlope=Mathf.Max(maxSlope,Vector3.Angle(hit.normal,Vector3.up));}
   File.WriteAllText(Path.Combine(Output,"grade_check.json"),JsonConvert.SerializeObject(new{maxSlope,mismatch,rays},Formatting.Indented));
   if(maxSlope>30||mismatch>.005f)throw new Exception("Preview terrain exceeds collision/grade criteria: slope="+maxSlope+", mismatch="+mismatch);
   if(props.Any(p=>p.t.position.x!=p.p.x||p.t.position.z!=p.p.z||p.t.rotation!=p.rot||p.t.localScale!=p.scale))throw new Exception("A prop horizontal pose changed");
   if(All(scene).Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0))throw new Exception("Missing script");
   if(!EditorSceneManager.SaveScene(scene,ScenePath,true))throw new Exception("Native preview scene save failed");
   // Lock the air drift to the same moment in both renders, without persisting it.
   var captureHaze=new Material(haze.sharedMaterial);var savedHaze=haze.sharedMaterial;
   try{captureHaze.SetFloat("_PreviewTime",12);haze.sharedMaterial=captureHaze;Capture(scene,"after");}finally{haze.sharedMaterial=savedHaze;Object.DestroyImmediate(captureHaze);}
   File.WriteAllText(Path.Combine(Output,"preview_result.json"),JsonConvert.SerializeObject(new{status="PASS_PREVIEW_ONLY",scene=ScenePath,assets=Root,vertices=mesh.vertexCount,triangles=mesh.triangles.Length/3,projectedProps=props.Count,foundationPads=props.Count(p=>p.pad),horizontalPosesPreserved=true,rays,maxSlope,mismatch,minHeight=values.Min(p=>p.y),maxHeight=values.Max(p=>p.y),changes},Formatting.Indented));
  } catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.json"),e.ToString());throw;}
  finally{EditorSceneManager.CloseScene(scene,true);if(active.IsValid()&&active.isLoaded)SceneManager.SetActiveScene(active);RenderSettings.ambientProbe=previousProbe;if(generated!=null)Object.DestroyImmediate(generated);if(hazeClone!=null)Object.DestroyImmediate(hazeClone);props=null;routes=null;}
  var after=ProductionHashes();bool preserved=JsonConvert.SerializeObject(hashes)==JsonConvert.SerializeObject(after);bool editor=setup==BarbarianCampUrpVerifier.EditorSnapshot();
  File.WriteAllText(Path.Combine(Output,"preservation.json"),JsonConvert.SerializeObject(new{status=preserved&&editor?"PASS":"FAIL",productionBytesPreserved=preserved,editorSetupPreserved=editor,hashes=after},Formatting.Indented));
  if(!preserved||!editor)throw new Exception("Original asset/editor preservation failed");
  return "PASS: separate native prototype scene and assets, original bytes and dirty Editor preserved.";
 }
 static float Segment(Vector2 p,Vector2 a,Vector2 b){var ab=b-a;return Vector2.Distance(p,a+ab*Mathf.Clamp01(Vector2.Dot(p-a,ab)/Mathf.Max(ab.sqrMagnitude,.001f)));}
 public static string Rebuild(){RollbackPrototype();Build();RefineSurface();return Recapture();}
 public static string RefineSurface() {
  if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new Exception("Idle required");
  var setup=BarbarianCampUrpVerifier.EditorSnapshot();var active=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
  try {
   var all=All(scene);var ground=all.Single(t=>t.name=="Camp Ground");var mesh=ground.GetComponent<MeshFilter>().sharedMesh;var vs=mesh.vertices;var uv=new Vector2[vs.Length];
   for(int i=0;i<vs.Length;i++){var p=ground.TransformPoint(vs[i]);uv[i]=new Vector2(.5f+(p.x+2.7165213f)/150,.5f+(p.z-2)/150);}
   mesh.uv=uv;mesh.RecalculateTangents();EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);
   var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/ThirdParty/04_환경맵/TopDown Barbarian Camp/Materials/Mud.mat");var material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Ground.mat");EditorUtility.CopySerialized(source,material);material.name="Terrain study original camp soil";EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
   var control=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/TerrainMasks.asset");var mask=control.GetPixels();var colors=new Color[mask.Length];
   for(int i=0;i<mask.Length;i++){float worn=mask[i].r*.25f,soil=mask[i].g*.10f,gravel=mask[i].b*.10f,total=worn+soil+gravel;var c=total>0?(new Color(.64f,.57f,.44f)*worn+new Color(.29f,.30f,.25f)*soil+new Color(.42f,.42f,.38f)*gravel)/total:Color.clear;c.a=total;colors[i]=c;}
   var overlayTexture=new Texture2D(control.width,control.height,TextureFormat.RGBA32,true,false){name="Subtle worn soil overlay",filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp};overlayTexture.SetPixels(colors);overlayTexture.Apply(true,false);AssetDatabase.CreateAsset(overlayTexture,Root+"/WornSoil.asset");
   var overlayMesh=Object.Instantiate(mesh);overlayMesh.name="Ground use traces";var ov=overlayMesh.vertices;var ou=new Vector2[ov.Length];
   for(int i=0;i<ov.Length;i++){var p=ground.TransformPoint(ov[i]);p.y+=.003f;ov[i]=ground.InverseTransformPoint(p);ou[i]=new Vector2((p.x+36)/108,(p.z+30)/76);}overlayMesh.vertices=ov;overlayMesh.uv=ou;overlayMesh.RecalculateTangents();overlayMesh.RecalculateBounds();AssetDatabase.CreateAsset(overlayMesh,Root+"/WornSoilMesh.asset");
   var overlayMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Subtle soil use traces",renderQueue=3000};overlayMaterial.SetTexture("_BaseMap",overlayTexture);overlayMaterial.SetColor("_BaseColor",Color.white);overlayMaterial.SetFloat("_Surface",1);overlayMaterial.SetFloat("_Blend",0);overlayMaterial.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);overlayMaterial.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);overlayMaterial.SetFloat("_ZWrite",0);overlayMaterial.SetFloat("_Smoothness",.1f);overlayMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");overlayMaterial.SetOverrideTag("RenderType","Transparent");AssetDatabase.CreateAsset(overlayMaterial,Root+"/WornSoil.mat");
   var go=new GameObject("Prototype soil use traces");SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(ground,false);go.AddComponent<MeshFilter>().sharedMesh=overlayMesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=overlayMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
   var haze=all.Single(t=>t.name=="Hideout Ground Haze").GetComponent<Renderer>();haze.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Haze.mat");PrefabUtility.RecordPrefabInstancePropertyModifications(haze);
   EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Prototype save failed");
   File.WriteAllText(Path.Combine(Output,"surface_refinement.json"),JsonConvert.SerializeObject(new{status="PASS_PREVIEW_ONLY",originalCampShaderRetained=true,originalTexturesRetained=true,groundUseOverlay=true,addedRenderers=1,addedColliders=0,footprintClusters=true,productionApplied=false},Formatting.Indented));
  }finally{EditorSceneManager.CloseScene(scene,true);if(active.IsValid()&&active.isLoaded)SceneManager.SetActiveScene(active);}
  if(setup!=BarbarianCampUrpVerifier.EditorSnapshot())throw new Exception("Editor state changed");
  return "Prototype retains the original camp soil shader, with a faint separate use-trace overlay.";
 }
 public static string QueueRecapture() {
  double deadline=EditorApplication.timeSinceStartup+600;EditorApplication.CallbackFunction tick=null;AssemblyReloadEvents.AssemblyReloadCallback reload=null;
  tick=()=>{
   if(EditorApplication.timeSinceStartup>deadline){EditorApplication.update-=tick;AssemblyReloadEvents.beforeAssemblyReload-=reload;File.WriteAllText(Path.Combine(Output,"queue_expired.txt"),"No idle slot became available.");return;}
   if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
   EditorApplication.update-=tick;AssemblyReloadEvents.beforeAssemblyReload-=reload;
   try{Recapture();}catch(Exception e){File.WriteAllText(Path.Combine(Output,"recapture_failure.txt"),e.ToString());}
  };
  reload=()=>{EditorApplication.update-=tick;AssemblyReloadEvents.beforeAssemblyReload-=reload;File.WriteAllText(Path.Combine(Output,"queue_discarded.txt"),"Domain reload removed this temporary capture callback before it changed any scene.");};
  EditorApplication.update+=tick;AssemblyReloadEvents.beforeAssemblyReload+=reload;
  return "Capture queued for idle EditMode; playing scene remains untouched; callback expires in ten minutes.";
 }
 public static string Recapture() {
  if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new Exception("Idle required");
  var setup=BarbarianCampUrpVerifier.EditorSnapshot();var active=SceneManager.GetActiveScene();var previousProbe=RenderSettings.ambientProbe;var previousSun=RenderSettings.sun;
  foreach(var pair in new[]{new[]{HideoutPerlinGroundBuilder.ScenePath,"before"},new[]{ScenePath,"after"}}) {
   var scene=EditorSceneManager.OpenScene(pair[0],OpenSceneMode.Additive);Material hazeClone=null;
   try {
    SceneManager.SetActiveScene(scene);var all=All(scene);
    if(pair[1]=="after") {var savedRenderer=all.Single(t=>t.name=="Hideout Ground Haze").GetComponent<Renderer>();savedRenderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Haze.mat");PrefabUtility.RecordPrefabInstancePropertyModifications(savedRenderer);EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Prototype haze override save failed");}
    foreach(var t in all)t.gameObject.layer=31;
    RenderSettings.sun=all.Select(t=>t.GetComponent<Light>()).Single(l=>l!=null&&l.type==LightType.Directional);
    RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.65f,.68f,.72f);RenderSettings.ambientEquatorColor=new Color(.5f,.51f,.53f);RenderSettings.ambientGroundColor=new Color(.3f,.27f,.24f);
    var probe=new SphericalHarmonicsL2();probe.AddAmbientLight(new Color(.33f,.34f,.34f));RenderSettings.ambientProbe=probe;
    foreach(var volume in all.Select(t=>t.GetComponent<Volume>()).Where(v=>v!=null))volume.gameObject.layer=31;
    var haze=all.Single(t=>t.name=="Hideout Ground Haze").GetComponent<Renderer>();hazeClone=new Material(haze.sharedMaterial);hazeClone.SetFloat("_PreviewTime",12);haze.sharedMaterial=hazeClone;
    foreach(var p in all.Select(t=>t.GetComponent<ParticleSystem>()).Where(p=>p!=null))p.Simulate(18,true,true,true);
    File.WriteAllText(Path.Combine(Output,pair[1]+"_lighting.json"),JsonConvert.SerializeObject(new {sun=RenderSettings.sun.name,RenderSettings.sun.intensity,ambientMode=RenderSettings.ambientMode.ToString(),sky=RenderSettings.ambientSkyColor.ToString(),probe=RenderSettings.ambientProbe[0,0],volumes=all.Select(t=>t.GetComponent<Volume>()).Where(v=>v!=null).Select(v=>new{v.name,v.gameObject.layer,v.priority})},Formatting.Indented));
    Capture(scene,pair[1]);
   } finally {EditorSceneManager.CloseScene(scene,true);if(hazeClone!=null)Object.DestroyImmediate(hazeClone);if(active.IsValid()&&active.isLoaded)SceneManager.SetActiveScene(active);RenderSettings.ambientProbe=previousProbe;RenderSettings.sun=previousSun;}
  }
  if(setup!=BarbarianCampUrpVerifier.EditorSnapshot())throw new Exception("Editor state changed");
  File.WriteAllText(Path.Combine(Output,"recapture.json"),JsonConvert.SerializeObject(new{status="PASS",editorSetupPreserved=true,isolatedLayer=31,sceneCullingOverride=false,postProcessing=true,environmentProbeSeeded=true,actualPlay="NOT_RUN"},Formatting.Indented));
  return "Both scene captures refreshed with the camp sun and matching ambient lighting; Editor restored.";
 }
 static Texture2D Control() {
  const int w=1536,h=1080;var pixels=new Color[w*h];
  for(int z=0;z<h;z++)for(int x=0;x<w;x++) {
   float wx=-36+(x+.5f)/w*108,wz=-30+(z+.5f)/h*76;
   var p=new Vector2(wx,wz);float distance=100;
   foreach(var route in routes)for(int i=1;i<route.Length;i++)distance=Mathf.Min(distance,Segment(p,route[i-1],route[i]));
   float n=Mathf.PerlinNoise(wx*.28f+43,wz*.28f+79),large=Mathf.PerlinNoise(wx*.075f+63,wz*.075f+19);
   float worn=(1-Smooth(.85f,2.3f,distance+(n-.5f)*.7f))*.9f;
   float shoulder=Hill(wx,wz,-15,7,15,22)+Hill(wx,wz,18,12,8,16)*.6f;
   float soil=Smooth(.36f,.72f,large+shoulder*.12f)*(1-worn*.8f);
   float gravel=Smooth(.50f,.78f,Mathf.PerlinNoise(wx*.14f+90,wz*.14f+44)+shoulder*.17f)*(1-worn);
   pixels[z*w+x]=new Color(worn,soil,gravel,1);
  }
  // Paint small footprints, avoiding a full prop scan for every texture sample.
  foreach(var prop in props.Where(q=>q.t.parent.name=="Nature"&&q.bounds.size.y<1.5f&&q.bounds.size.x*q.bounds.size.z>1)) {
   int x0=Mathf.Max(0,Mathf.FloorToInt((prop.x-3+36)/108*w)),x1=Mathf.Min(w-1,Mathf.CeilToInt((prop.x+3+36)/108*w));
   int z0=Mathf.Max(0,Mathf.FloorToInt((prop.z-3+30)/76*h)),z1=Mathf.Min(h-1,Mathf.CeilToInt((prop.z+3+30)/76*h));
   for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){float dx=-36+(x+.5f)/w*108-prop.x,dz=-30+(z+.5f)/h*76-prop.z;int i=z*w+x;var c=pixels[i];c.g=Mathf.Max(c.g,Mathf.Exp(-(dx*dx+dz*dz)*.55f)*.7f*(1-c.r));pixels[i]=c;}
  }
  var texture=new Texture2D(w,h,TextureFormat.RGBA32,true,true){name="Terrain study masks",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};texture.SetPixels(pixels);texture.Apply(true,false);File.WriteAllBytes(Path.Combine(Output,"terrain_masks.png"),texture.EncodeToPNG());return texture;
 }
 static void Capture(Scene scene,string stage) {
  var scoped=All(scene);var layers=scoped.Select(t=>t.gameObject.layer).ToArray();foreach(var t in scoped)t.gameObject.layer=31;
  var go=new GameObject("Temporary terrain study camera");SceneManager.MoveGameObjectToScene(go,scene);
  var camera=go.AddComponent<Camera>();camera.cullingMask=1<<31;var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.requiresDepthOption=CameraOverrideOption.On;data.volumeLayerMask=1<<31;
  camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.24f,.24f,.22f);camera.nearClipPlane=.1f;camera.farClipPlane=180;
  var rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);rt.Create();var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);var previous=RenderTexture.active;
  var positions=new[]{new Vector3(-8.76f,17,-9.51f),new Vector3(25,33,-37),new Vector3(-22,4.5f,-13)};
  var targets=new[]{new Vector3(-.65f,1,-1.4f),new Vector3(0,0,5),new Vector3(-7,.8f,12)};
  var haze=All(scene).Single(t=>t.name=="Hideout Ground Haze").GetComponent<Renderer>();bool hazeEnabled=haze.enabled;
  try{for(int i=0;i<3;i++){haze.enabled=i==2?false:hazeEnabled;camera.fieldOfView=i==0?38:52;camera.transform.position=positions[i];camera.transform.LookAt(targets[i]);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(Output,stage+"_"+i+".png"),tex.EncodeToPNG());}}
  finally{haze.enabled=hazeEnabled;camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);for(int i=0;i<scoped.Length;i++)if(scoped[i]!=null)scoped[i].gameObject.layer=layers[i];}
 }
 public static SortedDictionary<string,string> ProductionHashes() {
  var paths=new[]{HideoutPerlinGroundBuilder.ScenePath,HideoutPerlinGroundBuilder.MeshPath}.Concat(Directory.GetFiles("Assets/ProjectOverburst/05_Art/Environment/BarbarianHideout/Atmosphere","*",SearchOption.AllDirectories)).Concat(Directory.GetFiles("Assets/ThirdParty/04_환경맵/TopDown Barbarian Camp/Materials","*",SearchOption.AllDirectories));
  var result=new SortedDictionary<string,string>();foreach(var p in paths)result[p.Replace('\\','/')]=HideoutPerlinGroundBuilder.Hash(p);return result;
 }
 public static string RollbackPrototype() {
  if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new Exception("Idle required");
  if(SceneManager.GetSceneByPath(ScenePath).isLoaded)throw new Exception("Close the prototype scene first");
  if(File.Exists(ScenePath)&&!AssetDatabase.DeleteAsset(ScenePath))throw new Exception("Preview scene removal failed");
  if(AssetDatabase.IsValidFolder(Root)&&!AssetDatabase.DeleteAsset(Root))throw new Exception("Preview assets removal failed");
  return "Only this prototype removed; product scene and assets were never replaced.";
 }
}
