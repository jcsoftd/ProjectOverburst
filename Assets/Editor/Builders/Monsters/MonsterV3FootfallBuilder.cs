using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
// Adds contact data for one saved Actor, using existing sole/VFX/SFX infrastructure.
// Never runs the legacy whole-roster builder or rewrites shared effects/audio assets.
public static class MonsterV3FootfallBuilder
{
    public static object Create(string definitionPath,string[] supportingBones,string output)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating
            ||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            ||!string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")))throw new InvalidOperationException("Idle, unoccupied Editor required.");
        var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(definitionPath);
        if(definition==null||!definition.IsValid||definition.ActorPrefab==null)throw new ArgumentException("Saved Actor required");
        string path="Assets/ProjectOverburst/Resources/Enemies/Themes/Footfalls/"+definition.EnemyId+".asset";
        string disk=Path.Combine(Directory.GetParent(Application.dataPath).FullName,path);
        if(File.Exists(disk)||File.Exists(disk+".meta"))throw new InvalidOperationException("Creation only; existing profile preserved.");
        var scene=EditorSceneManager.NewPreviewScene();var graph=default(PlayableGraph);EnemyFootfallProfile created=null;bool saved=false;
        try
        {
            var root=(GameObject)PrefabUtility.InstantiatePrefab(definition.ActorPrefab.gameObject,scene);
            root.SetActive(true);foreach(var b in root.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
            var actor=root.GetComponent<EnemyActor>();var animator=actor.Animator;
            animator.enabled=true;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            graph=PlayableGraph.Create("Owned V3 sole contact measurement");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var clip=definition.AnimationProfile.Walk;
            if(definition.AnimationProfile.Run!=clip)throw new InvalidOperationException("Separate run clip requires a separate measured gait.");
            var player=AnimationClipPlayable.Create(graph,clip);player.SetApplyFootIK(false);player.SetApplyPlayableIK(false);
            var channel=AnimationPlayableOutput.Create(graph,"Native",animator);channel.SetSourcePlayable(player);graph.Play();
            var pos=animator.transform.localPosition;var rot=animator.transform.localRotation;var scale=animator.transform.localScale;
            void Pose(float time){player.SetTime(time);graph.Evaluate(0);animator.transform.localPosition=pos;animator.transform.localRotation=rot;animator.transform.localScale=scale;}
            Pose(0);var probes=MonsterThemeStrideCalibration.CreateRuntimeProbes(actor,supportingBones);
            const int count=120;var frames=new Vector3[count,probes.Length];var lows=Enumerable.Repeat(float.PositiveInfinity,probes.Length).ToArray();
            for(int frame=0;frame<count;frame++)
            {
                Pose(clip.length*frame/count);
                for(int foot=0;foot<probes.Length;foot++){frames[frame,foot]=actor.transform.InverseTransformPoint(probes[foot].sample());lows[foot]=Mathf.Min(lows[foot],frames[frame,foot].y);}
            }
            var candidates=new List<(float phase,Vector3 position,string bone)>();
            for(int foot=0;foot<probes.Length;foot++)for(int frame=0;frame<count;frame++)
            {
                int previous=(frame+count-1)%count;
                if(frames[frame,foot].y<=lows[foot]+.035f&&frames[previous,foot].y>lows[foot]+.035f)
                    candidates.Add(((float)frame/count,frames[frame,foot],probes[foot].name));
            }
            candidates=candidates.OrderBy(x=>x.phase).ToList();var chosen=new List<(float phase,Vector3 position,string bone)>();
            foreach(var c in candidates)if(chosen.Count==0||c.phase-chosen.Last().phase>=.085f)chosen.Add(c);
            if(chosen.Count>1&&chosen[0].phase+1-chosen.Last().phase<.085f)chosen.RemoveAt(chosen.Count-1);
            if(chosen.Count<2||chosen.Count>6)throw new InvalidOperationException("Need 2-6 measured contact groups, found "+chosen.Count+"; no fallback invented.");
            var contacts=chosen.Select(c=>new EnemyFootfallContact(c.phase,c.position)).ToArray();
            created=ScriptableObject.CreateInstance<EnemyFootfallProfile>();
            created.ConfigureDetailed(definition.EnemyId,clip,clip,definition.MovementProfile.HitWeightProfile.Weight,contacts,contacts);
            var so=new SerializedObject(created);var grade=definition.Grade.GradeType;
            so.FindProperty("groundStepTier").enumValueIndex=grade==EnemyGradeType.Elite||grade==EnemyGradeType.GreaterElite?(int)EnemyGroundStepTier.Elite:
                grade==EnemyGradeType.Normal&&definition.MovementProfile.HitWeightProfile.Weight!=EnemyHitWeight.Light?(int)EnemyGroundStepTier.Medium:(int)EnemyGroundStepTier.None;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(created,path);saved=true;AssetDatabase.SaveAssetIfDirty(created);
            var loaded=AssetDatabase.LoadAssetAtPath<EnemyFootfallProfile>(path);
            if(!loaded.IsValid||loaded.EnemyId!=definition.EnemyId||loaded.ContactCount!=chosen.Count||loaded.LocomotionClip!=clip)throw new InvalidOperationException("Saved contact mismatch");
            var samples=new JArray();for(int foot=0;foot<probes.Length;foot++)
                samples.Add(new JObject{["bone"]=probes[foot].name,["minimumY"]=lows[foot],["maximumY"]=Enumerable.Range(0,count).Max(f=>frames[f,foot].y)});
            var result=new JObject{["status"]="PASS_SAVED_NATIVE_FOOTFALLS",["definition"]=definitionPath,["profile"]=path,["clip"]=AssetDatabase.GetAssetPath(clip),
                ["nativeFps"]=clip.frameRate,["samplesPerCycle"]=count,["supportingBones"]=samples,
                ["contacts"]=new JArray(chosen.Select(c=>new JObject{["phase"]=c.phase,["sourceSeconds"]=c.phase*clip.length,["bone"]=c.bone,["actorLocalPosition"]=new JArray(c.position.x,c.position.y,c.position.z)})),
                ["groundStepTier"]=loaded.GroundStepTier.ToString(),["sharedEffectsAndAudioChanged"]=false,["gameplayVerified"]=false};
            File.WriteAllText(output,result.ToString());return result;
        }
        catch{if(saved)AssetDatabase.DeleteAsset(path);else if(created!=null)UnityEngine.Object.DestroyImmediate(created);throw;}
        finally{if(graph.IsValid())graph.Destroy();EditorSceneManager.ClosePreviewScene(scene);}
    }
}
