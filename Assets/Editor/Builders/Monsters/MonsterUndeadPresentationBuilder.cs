using System;using System.IO;using System.Linq;using System.Collections.Generic;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEditor.Animations;using UnityEngine;using UnityEngine.Playables;using UnityEngine.Animations;
public static class MonsterUndeadPresentationBuilder
{
 const string Root="Assets/ProjectOverburst/Resources/Enemies/Themes";
 public static string Apply(string outputDirectory)=>ApplyCore(outputDirectory,new[]{"DeathHarvest_Reaper","V3_darkKnight2","V3_SkeletonKnight_Small","V3_SkeletonKnight_Medium"},true);
 public static string ApplyDeathOnly(string enemyId,string outputDirectory){if(enemyId!="V3_darkKnight2")throw new ArgumentException("Only the measured Death Knight follow-up is supported");return ApplyCore(outputDirectory,new[]{enemyId},false);}
 static string ApplyCore(string outputDirectory,string[] enemyIds,bool adjustSmallBody)
 {
  if(BuildPipeline.isBuildingPlayer||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))||IsolatedSavePlayGuard.RequiresAccountChoice||!string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")))throw new InvalidOperationException("Idle, free account required");
  string directory=Path.GetFullPath(outputDirectory),allowed=Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
  if(!directory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||Directory.Exists(directory))throw new InvalidOperationException("Fresh private output required");
  Directory.CreateDirectory(directory);var changed=new HashSet<string>();var created=new List<string>();var results=new JArray();
  Action<UnityEngine.Object> backup=asset=>{string path=AssetDatabase.GetAssetPath(asset);if(!changed.Add(path))return;foreach(string suffix in new[]{"",".meta"}){string destination=Path.Combine(directory,"Backup",path+suffix);Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(path+suffix,destination,false);}};
  if(adjustSmallBody){var skeleton=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root+"/Definitions/V3_SkeletonKnight_Small.asset");backup(skeleton.ActorPrefab);var skeletonRoot=PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(skeleton.ActorPrefab));
  try{
   var actor=skeletonRoot.GetComponent<EnemyActor>();var body=actor.CollisionRoot.GetComponentInChildren<CapsuleCollider>(true);body.radius=.30f;body.height=1.50f;body.center=new Vector3(body.center.x,.77f,body.center.z);var target=skeletonRoot.GetComponent<CombatTarget>();target.ConfigureVolume(body.center,body.radius,body.height);
   var hurt=new SerializedObject(target);if(hurt.FindProperty("useCustomHurtVolume").boolValue){hurt.FindProperty("hurtLocalCenter").vector3Value=new Vector3(body.center.x,.795f,body.center.z);hurt.FindProperty("hurtRadius").floatValue=Mathf.Max(.30f,hurt.FindProperty("hurtRadius").floatValue);hurt.FindProperty("hurtHeight").floatValue=1.55f;hurt.ApplyModifiedPropertiesWithoutUndo();}
   var placement=skeletonRoot.GetComponent<CombatTargetVfxPlacement>();if(placement!=null){var v=new SerializedObject(placement);v.FindProperty("localBodyCenter").vector3Value=body.center;v.FindProperty("bodyRadius").floatValue=body.radius;v.FindProperty("bodyHeight").floatValue=body.height;v.ApplyModifiedPropertiesWithoutUndo();}
   PrefabUtility.SaveAsPrefabAsset(skeletonRoot,AssetDatabase.GetAssetPath(skeleton.ActorPrefab),out bool saved);if(!saved)throw new InvalidOperationException("Skeleton body save failed");
  }finally{PrefabUtility.UnloadPrefabContents(skeletonRoot);}}
  foreach(string id in enemyIds){
   var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root+"/Definitions/"+id+".asset");var profile=definition.AnimationProfile;var source=profile.Death;string clipPath=Root+"/Animations/"+id+"_Death_Once.anim";
   if(AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath)!=null)throw new InvalidOperationException("Death copy already exists: "+clipPath);
   var copy=UnityEngine.Object.Instantiate(source);copy.name=id+"_Death_Once";copy.wrapMode=WrapMode.ClampForever;var settings=AnimationUtility.GetAnimationClipSettings(copy);settings.loopTime=false;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(copy,settings);
   float maximumLift=0;
   if(id=="DeathHarvest_Reaper"||id=="V3_darkKnight2"||id=="V3_SkeletonKnight_Small"||id=="V3_SkeletonKnight_Medium"){
    var instance=PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(definition.ActorPrefab));PlayableGraph graph=default;
    try{
     instance.SetActive(true);foreach(var b in instance.GetComponentsInChildren<MonoBehaviour>(true))if(b!=null)b.enabled=false;var actor=instance.GetComponent<EnemyActor>();actor.VisualRoot.localScale=definition.ResolveRuntimeStats().VisualScale;var animator=actor.Animator;animator.enabled=true;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.applyRootMotion=false;
     var pos=animator.transform.localPosition;var rotation=animator.transform.localRotation;var scale=animator.transform.localScale;string rootPath=id=="DeathHarvest_Reaper"?"REAPER_":"root";var bone=animator.transform.Find(rootPath);if(bone==null)throw new InvalidOperationException("Reaper animated root missing");
     var bindings=AnimationUtility.GetCurveBindings(source).Where(b=>b.path==rootPath&&b.propertyName.StartsWith("m_LocalPosition.",StringComparison.Ordinal)).OrderBy(b=>b.propertyName,StringComparer.Ordinal).ToArray();if(bindings.Length!=3)throw new InvalidOperationException("Reaper root translation bindings");
     var originals=bindings.Select(b=>AnimationUtility.GetEditorCurve(source,b)).ToArray();var keys=new List<Keyframe>[] {new List<Keyframe>(),new List<Keyframe>(),new List<Keyframe>()};int frames=Mathf.CeilToInt(source.length*Mathf.Max(60,source.frameRate));
     graph=PlayableGraph.Create("Measured Reaper death ground");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);var playable=AnimationClipPlayable.Create(graph,source);playable.SetApplyFootIK(false);AnimationPlayableOutput.Create(graph,"pose",animator).SetSourcePlayable(playable);graph.Play();
     for(int i=0;i<=frames;i++){
      float time=source.length*i/frames;playable.SetTime(i==frames?source.length-.0001:time);graph.Evaluate(0);animator.transform.SetLocalPositionAndRotation(pos,rotation);animator.transform.localScale=scale;
      float lift=Mathf.Max(0,.025f-UpcomingMonsterThemeReviewSizing.GeometryBounds(actor.VisualRoot.gameObject).min.y);maximumLift=Mathf.Max(maximumLift,lift);Vector3 correction=bone.parent.InverseTransformVector(Vector3.up*lift);
      for(int axis=0;axis<3;axis++)keys[axis].Add(new Keyframe(time,originals[axis].Evaluate(time)+correction[axis]));
     }
     for(int axis=0;axis<3;axis++){var curve=new AnimationCurve(keys[axis].ToArray());for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(copy,bindings[axis],curve);}
    }finally{if(graph.IsValid())graph.Destroy();PrefabUtility.UnloadPrefabContents(instance);}
   }
   AssetDatabase.CreateAsset(copy,clipPath);created.Add(clipPath);backup(profile);var serialized=new SerializedObject(profile);serialized.FindProperty("death").objectReferenceValue=copy;serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
   var controller=(AnimatorController)profile.RuntimeController;backup(controller);var death=controller.layers[0].stateMachine.states.Single(s=>s.state.name=="Death").state;death.motion=copy;EditorUtility.SetDirty(death);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);
   results.Add(new JObject{{"id",id},{"source",AssetDatabase.GetAssetPath(source)},{"clip",clipPath},{"sourcePreserved",true},{"loop",false},{"maximumGroundLift",maximumLift}});File.WriteAllText(Path.Combine(directory,"progress.json"),results.ToString());
  }
  File.WriteAllText(Path.Combine(directory,"applied.json"),new JObject{{"status","APPLIED"},{"entries",results},{"changedAssets",new JArray(changed.OrderBy(p=>p,StringComparer.Ordinal))},{"createdAssets",new JArray(created)}}.ToString());return "Saved "+enemyIds.Length+" non-looping death clips; measured skeletal poses grounded; source assets retained";
 }
}
