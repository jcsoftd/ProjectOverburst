using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object=UnityEngine.Object;

// Applies a measured, per-definition plan; keeps originals, GUIDs, account and open scenes.
public static class MonsterPresentationCalibrationBuilder
{
    static readonly HashSet<string> saved=new HashSet<string>();
    static string directory;
    static readonly HashSet<string> transformedContacts=new HashSet<string>();
    static readonly HashSet<string> transformedAbilities=new HashSet<string>();
    static void Backup(Object asset)
    {
        string path=AssetDatabase.GetAssetPath(asset);if(string.IsNullOrEmpty(path)||saved.Contains(path))return;
        if(!path.StartsWith("Assets/ProjectOverburst/Resources/Enemies/Themes/",StringComparison.Ordinal))throw new Exception("Theme-owned asset required: "+path);
        foreach(string suffix in new[]{"",".meta"}){string destination=Path.Combine(directory,"Backup",path+suffix);Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(path+suffix,destination,false);}
        saved.Add(path);
    }
    static void Save(Object asset){EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);}
    static void ScaleFloat(SerializedObject so,string name,float factor){var p=so.FindProperty(name);if(p!=null)p.floatValue*=factor;}
    static void ScaleVector(SerializedObject so,string name,float factor,Vector3 shift){var p=so.FindProperty(name);if(p!=null)p.vector3Value=p.vector3Value*factor+shift;}
    static void TransformContact(EnemyWeakAttackExecutionProfile profile,float factor,float offset)
    {
        if(!transformedContacts.Add(AssetDatabase.GetAssetPath(profile)))return;Backup(profile);float reach=profile.MaximumContactPlanarReach;float allowance=Mathf.Max(0,profile.StationaryStartRange-reach);var so=new SerializedObject(profile);var groups=so.FindProperty("contactGeometry");
        for(int g=0;g<groups.arraySize;g++){
            var frames=groups.GetArrayElementAtIndex(g).FindPropertyRelative("frames");
            for(int f=0;f<frames.arraySize;f++){
                var capsules=frames.GetArrayElementAtIndex(f).FindPropertyRelative("capsules");
                for(int c=0;c<capsules.arraySize;c++){var shape=capsules.GetArrayElementAtIndex(c);foreach(string name in new[]{"a","b"}){var point=shape.FindPropertyRelative(name);point.vector3Value=point.vector3Value*factor+Vector3.up*offset;}shape.FindPropertyRelative("radius").floatValue*=factor;}
            }
        }
        if(profile.HasContactGeometry){so.FindProperty("stationaryStartRange").floatValue=profile.StationaryStartRange*factor-allowance*(factor-1);ScaleFloat(so,"maxAdvanceDistance",factor);}so.ApplyModifiedPropertiesWithoutUndo();
        if(!profile.ValidateAuthoring(out string reason))throw new Exception(reason);Save(profile);
    }
    static IEnumerable<AnimationClip> Clips(Motion motion){if(motion is AnimationClip clip){yield return clip;yield break;}if(motion is BlendTree tree)foreach(var child in tree.children)foreach(var c in Clips(child.motion))yield return c;}
    public static string ApplyMeasuredClawContacts(string authoringPath,string outputDirectory)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
            throw new Exception("Idle Editor required");
        directory=Path.GetFullPath(outputDirectory);
        string allowed=Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        if(!directory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||Directory.Exists(directory))
            throw new Exception("Fresh private output required");
        saved.Clear();Directory.CreateDirectory(directory);var result=new JArray();
        foreach(var row in JObject.Parse(File.ReadAllText(authoringPath))["entries"])
        {
            string id=((string)row["cardKey"]).Substring("runtime:".Length);
            var d=AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/"+id+".asset");
            var ability=Enumerable.Range(0,d.AbilitySet.Count).Select(d.AbilitySet.GetAbility)
                .Single(a=>a.WeakAttackExecution?.SelectionKey==(string)row["selectionKey"]);
            var profile=ability.WeakAttackExecution;
            if(ability.IsTelegraphedStrongAttack)throw new Exception("Only saved weak attacks may be recalibrated");
            if(row["previousStationaryStartRange"]!=null&&Mathf.Abs(profile.StationaryStartRange-(float)row["previousStationaryStartRange"])>.00001f)throw new Exception("Start range changed after measurement");
            Backup(profile);
            var so=new SerializedObject(profile);var groups=so.FindProperty("contactGeometry");
            var phases=row["contactGeometry"]["phases"];
            if(groups.arraySize!=phases.Count())throw new Exception("Contact phase count changed");
            for(int g=0;g<groups.arraySize;g++)
            {
                var frames=groups.GetArrayElementAtIndex(g).FindPropertyRelative("frames");var authoredFrames=phases[g]["frames"];
                bool replaceWindow=(bool?)row["replaceContactWindows"]==true;
                if(!replaceWindow&&frames.arraySize!=authoredFrames.Count())throw new Exception("Contact sampling times changed");
                if(replaceWindow){profile.TryGetContactWindow(g,out var previousWindow);var priorWindowData=row["originalContactWindowsNormalized"][g];if(Mathf.Abs(previousWindow.x-(float)priorWindowData[0])>.00001f||Mathf.Abs(previousWindow.y-(float)priorWindowData[1])>.00001f)throw new Exception("Contact window changed after measurement");frames.arraySize=authoredFrames.Count();}
                for(int f=0;f<frames.arraySize;f++)
                {
                    var frame=frames.GetArrayElementAtIndex(f);var authored=authoredFrames[f];
                    if(!replaceWindow&&Mathf.Abs(frame.FindPropertyRelative("normalizedTime").floatValue-(float)authored["normalizedTime"])>.000001f)
                        throw new Exception("Contact sampling time changed");
                    if(replaceWindow)frame.FindPropertyRelative("normalizedTime").floatValue=(float)authored["normalizedTime"];
                    var capsules=frame.FindPropertyRelative("capsules");var shapes=authored["capsules"];capsules.arraySize=shapes.Count();
                    for(int c=0;c<capsules.arraySize;c++)
                    {
                        var shape=capsules.GetArrayElementAtIndex(c);foreach(string n in new[]{"a","b"}){
                            var v=shapes[c][n];shape.FindPropertyRelative(n).vector3Value=new Vector3((float)v[0],(float)v[1],(float)v[2]);
                        }
                        shape.FindPropertyRelative("radius").floatValue=(float)shapes[c]["radius"];
                    }
                }
            }
            if(row["stationaryStartRange"]!=null)so.FindProperty("stationaryStartRange").floatValue=(float)row["stationaryStartRange"];
            if((bool?)row["replaceContactWindows"]==true){var windows=so.FindProperty("contactWindows");for(int g=0;g<windows.arraySize;g++)windows.GetArrayElementAtIndex(g).vector2Value=new Vector2((float)row["contactWindowsNormalized"][g][0],(float)row["contactWindowsNormalized"][g][1]);}
            so.ApplyModifiedPropertiesWithoutUndo();if(!profile.ValidateAuthoring(out string reason))throw new Exception(reason);Save(profile);
            if(row["originalHitNormalizedTimes"] is JArray previous)
            {
                if(ability.IsTelegraphedStrongAttack||previous.Count!=ability.ReleaseCount)
                    throw new Exception("Only unchanged weak-attack strike counts can be recalibrated");
                for(int phase=0;phase<previous.Count;phase++)
                    if(Mathf.Abs(ability.GetHitNormalizedTime(phase)-(float)previous[phase])>.000001f)
                        throw new Exception("Strike time changed after measurement");
                Backup(ability);var settings=new SerializedObject(ability);var times=(JArray)row["hitNormalizedTimes"];
                settings.FindProperty("hitNormalizedTime").floatValue=(float)times[0];
                var extra=settings.FindProperty("additionalHitNormalizedTimes");extra.arraySize=times.Count-1;
                for(int phase=1;phase<times.Count;phase++)extra.GetArrayElementAtIndex(phase-1).floatValue=(float)times[phase];
                settings.ApplyModifiedPropertiesWithoutUndo();if(!ability.IsValid)throw new Exception("Measured strike is outside its contact window");Save(ability);
            }
            if(row["stationaryStartRange"]!=null){Backup(ability);var rangeSettings=new SerializedObject(ability);rangeSettings.FindProperty("range").floatValue=profile.ApproachStartRange;rangeSettings.ApplyModifiedPropertiesWithoutUndo();if(!ability.IsValid)throw new Exception("Invalid saved measured weak attack");Save(ability);}
            result.Add(new JObject{{"id",id},{"selectionKey",profile.SelectionKey},{"path",AssetDatabase.GetAssetPath(profile)},
                {"basis","Native skinned claw surface at saved actor scale; original joint capsules retained"}});
        }
        File.WriteAllText(Path.Combine(directory,"applied.json"),result.ToString());return "Saved measured claw contacts: "+result.Count;
    }
    public static string Apply(string planPath,string outputDirectory)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating
            ||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            ||!string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")))throw new Exception("Idle Editor required");
        directory=Path.GetFullPath(outputDirectory);string allowed=Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        if(!directory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||File.Exists(Path.Combine(directory,"applied.json")))throw new Exception("Fresh private output required");
        saved.Clear();transformedContacts.Clear();transformedAbilities.Clear();var plan=JObject.Parse(File.ReadAllText(planPath));var applied=new JArray();Directory.CreateDirectory(directory);
        foreach(var row in plan["entries"]){
            var d=AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)row["definition"]);float factor=(float)row["factor"],offset=(float)row["offset"];
            var result=new JObject{{"id",d.EnemyId},{"definition",(string)row["definition"]},{"factor",factor},{"offset",offset}};
            var references=row["references"];
            if(references!=null||factor!=1){
                var movement=d.MovementProfile;Backup(movement);
                movement.ConfigureAnimationReferenceSpeeds((float?)references?["walk"]??movement.AnimationReferenceSpeed*factor,(float?)references?["run"]??movement.RunAnimationReferenceSpeed*factor);
                movement.ConfigureBackpedalAnimationReferenceSpeed((float?)references?["back"]??movement.GetAnimationReferenceSpeed(EnemyLocomotionMode.Backpedal)*factor);
                movement.ConfigureCrowdAnimationSpeedLimit(1.12f);Save(movement);result["references"]=new JObject{{"walk",movement.AnimationReferenceSpeed},{"run",movement.RunAnimationReferenceSpeed},{"back",movement.GetAnimationReferenceSpeed(EnemyLocomotionMode.Backpedal)}};
            }
            string prefabPath=AssetDatabase.GetAssetPath(d.ActorPrefab);var root=PrefabUtility.LoadPrefabContents(prefabPath);PlayableGraph graph=default;
            try{
                var actor=root.GetComponent<EnemyActor>();bool authoredActive=root.activeSelf;root.SetActive(true);foreach(var b in root.GetComponentsInChildren<MonoBehaviour>(true))if(b!=null)b.enabled=false;
                var a=actor.Animator;var model=a.transform;var authoredCulling=a.cullingMode;bool authoredAnimatorEnabled=a.enabled,authoredRootMotion=a.applyRootMotion;
                var position=root.transform.InverseTransformPoint(model.position)*factor+Vector3.up*offset;model.position=root.transform.TransformPoint(position);model.localScale*=factor;
                foreach(var b in root.GetComponentsInChildren<MonoBehaviour>(true))if(b!=null)b.enabled=d.ActorPrefab.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(p=>p.GetType()==b.GetType()&&p.name==b.name)?.enabled??true;
                if(factor!=1){
                    var cap=actor.CollisionRoot.GetComponentInChildren<CapsuleCollider>(true);cap.radius*=factor;cap.height*=factor;cap.center=new Vector3(cap.center.x*factor,cap.height*.5f+.02f,cap.center.z*factor);
                    var target=root.GetComponent<CombatTarget>();var t=new SerializedObject(target);bool air=(string)row["id"]=="V3_DeathSkull";
                    ScaleVector(t,"hurtLocalCenter",factor,air?Vector3.up*offset:Vector3.zero);ScaleFloat(t,"hurtRadius",factor);ScaleFloat(t,"hurtHeight",factor);t.ApplyModifiedPropertiesWithoutUndo();target.ConfigureVolume(cap.center,cap.radius,cap.height);
                    var vfx=root.GetComponent<CombatTargetVfxPlacement>();if(vfx!=null){var v=new SerializedObject(vfx);foreach(string f in new[]{"bodyRadius","bodyHeight","hitRadius","hitHeight"})ScaleFloat(v,f,factor);foreach(string f in new[]{"localBodyCenter","localHitCenter"})ScaleVector(v,f,factor,air?Vector3.up*offset:Vector3.zero);v.ApplyModifiedPropertiesWithoutUndo();}
                    foreach(var anchor in actor.Anchors.GetComponentsInChildren<Transform>(true).Skip(1))anchor.localPosition=anchor.localPosition*factor+Vector3.up*offset;
                }
                var authoredModelOverrides=PrefabUtility.GetPropertyModifications(model.gameObject);
                var basePosition=model.localPosition;var baseRotation=model.localRotation;var baseScale=model.localScale;
                var transforms=root.GetComponentsInChildren<Transform>(true);var positions=transforms.Select(t=>t.localPosition).ToArray();var rotations=transforms.Select(t=>t.localRotation).ToArray();var scales=transforms.Select(t=>t.localScale).ToArray();
                var controller=(AnimatorController)d.AnimationProfile.RuntimeController;
                var motions=controller.layers[0].stateMachine.states.Where(st=>st.state.name=="Locomotion"||st.state.name.StartsWith("IdleVariant_",StringComparison.Ordinal)).SelectMany(st=>Clips(st.state.motion)).Distinct().ToArray();
                var curves=new List<EnemyLocomotionGroundClearance.Sample>();float maximum=0;
                foreach(var clip in motions){
                    a.enabled=true;a.applyRootMotion=false;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                    graph=PlayableGraph.Create("Measured locomotion clearance");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);var playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);AnimationPlayableOutput.Create(graph,"pose",a).SetSourcePlayable(playable);graph.Play();
                    var keys=new List<Keyframe>();float max=0;
                    const int count=64;
                    for(int i=0;i<=count;i++){
                        playable.SetTime(clip.length*(i%count)/(double)count);graph.Evaluate(0);model.SetLocalPositionAndRotation(basePosition,baseRotation);model.localScale=baseScale;
                        var bounds=UpcomingMonsterThemeReviewSizing.GeometryBounds(actor.VisualRoot.gameObject);float lift=Mathf.Max(0,.025f-bounds.min.y);if((bool)row["airborne"])lift=0;
                        keys.Add(new Keyframe(i/(float)count,lift));max=Mathf.Max(max,lift);
                    }
                    graph.Destroy();if(max>.045f){for(int i=0;i<keys.Count;i++){var key=keys[i];key.inTangent=i==0?0:(keys[i].value-keys[i-1].value)*count;key.outTangent=i==keys.Count-1?0:(keys[i+1].value-keys[i].value)*count;keys[i]=key;}var curve=new AnimationCurve(keys.ToArray());
                        var tree=(BlendTree)controller.layers[0].stateMachine.states.First(st=>st.state.name=="Locomotion").state.motion;bool reverse=tree.children.Any(c=>c.threshold<0&&c.timeScale<0&&Clips(c.motion).Contains(clip));
                        curves.Add(new EnemyLocomotionGroundClearance.Sample{clip=clip,lift=curve,reverseBackpedal=reverse});maximum=Mathf.Max(maximum,max);}
                }
                Bounds idleBounds=default;
                if(factor!=1||offset!=0){
                    graph=PlayableGraph.Create("Measured body anchors");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var idle=AnimationClipPlayable.Create(graph,d.AnimationProfile.Idle);idle.SetApplyFootIK(false);
                    AnimationPlayableOutput.Create(graph,"pose",a).SetSourcePlayable(idle);graph.Play();graph.Evaluate(0);
                    model.SetLocalPositionAndRotation(basePosition,baseRotation);model.localScale=baseScale;
                    idleBounds=UpcomingMonsterThemeReviewSizing.GeometryBounds(actor.VisualRoot.gameObject);graph.Destroy();
                }
                model.SetLocalPositionAndRotation(basePosition,baseRotation);model.localScale=baseScale;
                for(int i=0;i<transforms.Length;i++){transforms[i].SetLocalPositionAndRotation(positions[i],rotations[i]);transforms[i].localScale=scales[i];}a.cullingMode=authoredCulling;a.enabled=authoredAnimatorEnabled;a.applyRootMotion=authoredRootMotion;root.SetActive(authoredActive);
                // Sampling must not serialize transient child-bone rotations into the nested model instance.
                PrefabUtility.SetPropertyModifications(model.gameObject,authoredModelOverrides);
                if(factor!=1||offset!=0){
                    Vector3 center=root.transform.InverseTransformPoint(idleBounds.center);
                    var hit=actor.Anchors.Find("HitVfxPoint");if(hit!=null)hit.localPosition=center;
                    var hp=actor.Anchors.Find("HpBarAnchor");if(hp!=null)hp.localPosition=new Vector3(center.x,idleBounds.max.y+.3f,center.z);
                    var attack=actor.Anchors.Find("AttackPoint");if(attack!=null)attack.localPosition=new Vector3(center.x,center.y,Mathf.Clamp(idleBounds.max.z*.65f,.15f,.8f));
                    var ground=actor.Anchors.Find("GroundProbe");if(ground!=null)ground.localPosition=new Vector3(0,.08f,0);
                }
                bool changed=factor!=1||offset!=0||curves.Count>0;
                if(changed){Backup(d.ActorPrefab);if(curves.Count>0){var clearance=root.GetComponent<EnemyLocomotionGroundClearance>()??root.AddComponent<EnemyLocomotionGroundClearance>();clearance.Configure(a,curves.ToArray());}
                    if(!actor.IsAuthoringValid)throw new Exception("Actor root contract invalid "+d.EnemyId);PrefabUtility.SaveAsPrefabAsset(root,prefabPath,out bool success);if(!success)throw new Exception("Prefab save failed");}
                result["prefabChanged"]=changed;result["clearanceClips"]=curves.Count;result["maximumLift"]=maximum;
            }finally{if(graph.IsValid())graph.Destroy();PrefabUtility.UnloadPrefabContents(root);}
            if(factor!=1||offset!=0){
                foreach(var ability in Enumerable.Range(0,d.AbilitySet.Count).Select(d.AbilitySet.GetAbility)){
                    if(ability.WeakAttackExecution!=null)TransformContact(ability.WeakAttackExecution,factor,offset);
                    if(factor!=1&&EnemyAbilityDefinition.IsWeakMeleeExecution(ability.ExecutionMode)&&transformedAbilities.Add(AssetDatabase.GetAssetPath(ability))){Backup(ability);var ab=new SerializedObject(ability);foreach(string f in new[]{"minimumRange","range","hitRadius","verticalTolerance"})ScaleFloat(ab,f,factor);if(ability.WeakAttackExecution!=null)ab.FindProperty("range").floatValue=ability.WeakAttackExecution.ApproachStartRange;ab.ApplyModifiedPropertiesWithoutUndo();Save(ability);}
                }
                string footPath="Assets/ProjectOverburst/Resources/Enemies/Themes/Footfalls/"+d.EnemyId+".asset";var foot=AssetDatabase.LoadAssetAtPath<EnemyFootfallProfile>(footPath);
                if(foot!=null&&foot.HasDetailedContacts){Backup(foot);var fo=new SerializedObject(foot);foreach(string field in new[]{"walkContacts","runContacts"}){var contacts=fo.FindProperty(field);for(int i=0;i<contacts.arraySize;i++){var pos=contacts.GetArrayElementAtIndex(i).FindPropertyRelative("localPosition");pos.vector3Value=pos.vector3Value*factor+Vector3.up*offset;}}fo.ApplyModifiedPropertiesWithoutUndo();Save(foot);}
            }
            applied.Add(result);File.WriteAllText(Path.Combine(directory,"progress.json"),applied.ToString());
        }
        File.WriteAllText(Path.Combine(directory,"applied.json"),new JObject{{"entries",applied},{"changedAssets",new JArray(saved.OrderBy(p=>p,StringComparer.Ordinal))}}.ToString());return "Saved "+saved.Count+" measured theme assets";
    }
}
