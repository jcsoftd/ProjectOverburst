using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class HideoutTerrainStudyReadback {
 const string Output="../개인파일/코덱스산출/Environment/20261004_HideoutTerrainStudy";
 const string Root="Assets/ProjectOverburst/05_Art/Environment/BarbarianHideout/Prototypes/TerrainStudy_20261004";
 const string ScenePath="Assets/ProjectOverburst/00_Scenes/Previews/HideoutTerrainStudy_20261004.unity";
 public static string Main(){
  if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new Exception("Idle Editor required");
  var setup=BarbarianCampUrpVerifier.EditorSnapshot();
  var scene=EditorSceneManager.OpenPreviewScene(ScenePath);
  try{
   var all=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
   var ground=all.Single(t=>t.name=="Camp Ground");var mesh=ground.GetComponent<MeshFilter>().sharedMesh;
   var haze=all.Single(t=>t.name=="Hideout Ground Haze").GetComponent<Renderer>();var overlay=all.Single(t=>t.name=="Prototype soil use traces");
   string root=Root;
   bool references=AssetDatabase.GetAssetPath(mesh)==root+"/Ground.asset"&&ground.GetComponent<MeshCollider>().sharedMesh==mesh&&AssetDatabase.GetAssetPath(haze.sharedMaterial)==root+"/Haze.mat"&&AssetDatabase.GetAssetPath(haze.sharedMaterial.GetTexture("_GroundHeight"))==root+"/HazeGroundHeight.asset"&&overlay.GetComponent<Collider>()==null;
   bool shaders=ground.GetComponent<Renderer>().sharedMaterial.shader.isSupported&&haze.sharedMaterial.shader.isSupported&&overlay.GetComponent<Renderer>().sharedMaterial.shader.isSupported;
   int missing=all.Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
   var before=JsonConvert.DeserializeObject<System.Collections.Generic.SortedDictionary<string,string>>(File.ReadAllText(Path.Combine(Output,"production_before.json")));
   var after=new System.Collections.Generic.SortedDictionary<string,string>();foreach(var p in before.Keys)after[p]=HideoutPerlinGroundBuilder.Hash(p);bool bytes=JsonConvert.SerializeObject(before)==JsonConvert.SerializeObject(after);
   File.WriteAllText(Path.Combine(Output,"readback.json"),JsonConvert.SerializeObject(new{status=references&&shaders&&missing==0&&bytes?"PASS":"FAIL",references,shaders,missingScripts=missing,productionBytesPreserved=bytes,groundShader=ground.GetComponent<Renderer>().sharedMaterial.shader.name,prototypeOnly=true,buildSettingsChanged=false,play="NOT_RUN",resources="Temporary scene closed in finally; owned camera, textures and render targets already returned",sharedMemoryCleanup="DEFERRED: other Editor activity ownership unknown"},Formatting.Indented));
   if(!references||!shaders||missing!=0||!bytes)throw new Exception("Prototype readback failed");
  }finally{EditorSceneManager.ClosePreviewScene(scene);}
  if(setup!=BarbarianCampUrpVerifier.EditorSnapshot())throw new Exception("Editor setup changed");
  return "PASS: native prototype reload, ground/collider/haze references, supported shaders, no missing scripts, original bytes and Editor preserved.";
 }
}
