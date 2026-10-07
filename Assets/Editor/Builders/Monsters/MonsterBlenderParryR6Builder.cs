using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

// Applies reviewed Blender samples to the existing native rigs. Source models,
// actor prefabs, attacks, sounds and parry windows are never saved by this builder.
[InitializeOnLoad]
public static class MonsterBlenderParryR6Builder
{
    const string Key = "Overburst.BlenderParryR6Apply.";
    public const string Revision = "2026-10-07-Blender-r6";
    static readonly string[] Roles = { "ParryCollapse", "StunnedLoop", "StunRecover" };
    static readonly string[] StateNames = { "Parry_Collapse", "Stunned_Loop", "Stun_Recover" };
    static readonly string[] Fields = { "parryCollapse", "stunnedLoop", "stunRecover" };
    static JObject input; static string output; static int index; static bool binding;
    static readonly JArray staged = new JArray(), bound = new JArray();
    static double deadline;
    static MonsterBlenderParryR6Builder()
    {
        AssemblyReloadEvents.beforeAssemblyReload += () => {
            if (input != null) Finish("INTERRUPTED_RELOAD", "Inspect persistent receipts before resuming.");
        };
    }
    static string Project => Directory.GetParent(Application.dataPath).FullName;
    static string Workspace => Directory.GetParent(Project).FullName;
    public static bool Busy => input != null;
    public static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || BuildPipeline.isBuildingPlayer || IsolatedSavePlayGuard.RequiresAccountChoice
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Idle Editor and unoccupied account required; no shared Play is interrupted.");
    }
    public static string Start(string privateOutputRelative)
    {
        RequireIdle(); if (Busy) throw new InvalidOperationException("This builder is already running.");
        output = Path.GetFullPath(Path.Combine(Workspace, privateOutputRelative));
        string allowed = Path.GetFullPath(Path.Combine(Workspace, "개인파일/코덱스산출/Monsters")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Private monster output required.");
        if (File.Exists(Path.Combine(output,"data/apply-result.json"))) throw new InvalidOperationException("Application receipt exists; inspect it rather than repeat the batch.");
        var plan = JObject.Parse(File.ReadAllText(Path.Combine(output,"data/apply-input.json")));
        if ((string)plan["revision"] != Revision || plan["records"].Count() != 35) throw new InvalidDataException("Reviewed r6/35 input required.");
        foreach (var p in (JObject)plan["preservedHashes"])
            if (Hash(Resolve(p.Key)) != (string)p.Value) throw new InvalidDataException("Input changed since preflight: " + p.Key);
        foreach (var record in plan["records"])
        {
            foreach (var p in (JObject)record["assetPaths"])
                if (File.Exists(Path.Combine(Project,(string)p.Value))) throw new InvalidOperationException("Preserve existing destination: " + p.Value);
        }
        input=plan; index=0; binding=false; staged.Clear(); bound.Clear(); deadline=EditorApplication.timeSinceStartup+1200;
        SessionState.SetString(Key+"output",output); SessionState.SetBool(Key+"active",true);
        Write("STAGING_CLIPS"); EditorApplication.update += Tick;
        return "QUEUED_NATIVE_CLIPS_THEN_BINDINGS";
    }
    public static string BindStaged(string privateOutputRelative)
    {
        RequireIdle(); if (Busy) throw new InvalidOperationException("This builder is already running.");
        output = Path.GetFullPath(Path.Combine(Workspace, privateOutputRelative));
        string allowed = Path.GetFullPath(Path.Combine(Workspace, "개인파일/코덱스산출/Monsters")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Private monster output required.");
        if (File.Exists(Path.Combine(output,"data/apply-result.json"))) throw new InvalidOperationException("Inspect existing application receipt before repeating bindings.");
        var receipt=JObject.Parse(File.ReadAllText(Path.Combine(output,"data/stage-result.json")));
        if((string)receipt["status"]!="READY_NATIVE_STAGED" || (int)receipt["stagedActors"]!=35) throw new InvalidDataException("All 105 clips must be staged and inspected first.");
        var plan=JObject.Parse(File.ReadAllText(Path.Combine(output,"data/apply-input.json")));
        foreach(var p in (JObject)plan["preservedHashes"])
            if(Hash(Resolve(p.Key))!=(string)p.Value) throw new InvalidDataException("Preserved input changed: "+p.Key);
        foreach(var row in receipt["staged"].SelectMany(x=>x["clips"]))
            if(Hash(Path.Combine(Project,(string)row["path"]))!=(string)row["sha256"]) throw new InvalidDataException("Staged clip changed: "+row["path"]);
        input=plan;binding=true;index=0;staged.Clear();bound.Clear();
        foreach(var item in receipt["staged"]) staged.Add(item.DeepClone());
        deadline=EditorApplication.timeSinceStartup+1200;
        SessionState.SetString(Key+"output",output);SessionState.SetBool(Key+"active",true);
        Write("BINDING_REVIEWED_NATIVE_CLIPS");EditorApplication.update+=Tick;
        return "QUEUED_EXISTING_NATIVE_BINDINGS";
    }
    static void Tick()
    {
        if (input == null) { EditorApplication.update -= Tick; return; }
        try
        {
            // Own asset creation may briefly enter an import update between actors.
            if(EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling) return;
            RequireIdle();
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Inspect persistent completed actors before resuming.");
            var records=(JArray)input["records"];
            if (index < records.Count)
            {
                var record=(JObject)records[index];
                if (!binding) staged.Add(Stage(record)); else bound.Add(Bind(record));
                index++; Write(binding?"BINDING":"STAGING_CLIPS"); return;
            }
            if (!binding) { Finish("READY_NATIVE_STAGED",null); return; }
            Finish("PASS_NATIVE_APPLIED",null);
        }
        catch (Exception error) { Finish("FAIL_REVIEW_REQUIRED",error.ToString()); }
    }
    static void Finish(string status,string error)
    {
        EditorApplication.update -= Tick;
        Write(status,error);
        File.Copy(Path.Combine(output,"data/apply-progress.json"),Path.Combine(output,status=="READY_NATIVE_STAGED"?"data/stage-result.json":"data/apply-result.json"),true);
        SessionState.EraseString(Key+"output"); SessionState.EraseBool(Key+"active"); input=null;
    }
    static void Write(string status,string error=null)
    {
        Directory.CreateDirectory(Path.Combine(output,"data"));
        File.WriteAllText(Path.Combine(output,"data/apply-progress.json"),new JObject {
            ["status"]=status,["revision"]=Revision,["staged"]=staged,["bound"]=bound,
            ["stagedActors"]=staged.Count,["boundActors"]=bound.Count,["clips"]=staged.Count*3,
            ["attackAndParryWindowsModified"]=false,["actorPrefabsSaved"]=0,["soundsModified"]=false,
            ["error"]=error,["utc"]=DateTime.UtcNow }.ToString());
    }
    static string Resolve(string relative) => Path.Combine(relative.StartsWith("Assets/",StringComparison.Ordinal)?Project:Workspace,relative);
    public static string Hash(string path)
    { using (var sha=SHA256.Create()) using (var stream=File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    static void Folders(string path)
    {
        var parts=path.Split('/'); string current=parts[0];
        for (int i=1;i<parts.Length;i++) { string next=current+"/"+parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current,parts[i]); current=next; }
    }
    static void Pose(Transform[] bones,JArray frame)
    {
        for (int i=0;i<bones.Length;i++)
        {
            var x=(JArray)frame[i];
            bones[i].SetLocalPositionAndRotation(new Vector3((float)x[0],(float)x[1],(float)x[2]),
                new Quaternion((float)x[3],(float)x[4],(float)x[5],(float)x[6]));
            bones[i].localScale=new Vector3((float)x[7],(float)x[8],(float)x[9]);
        }
    }
    static AnimationCurve Curve(IList<float> values,float duration)
    {
        bool constant=values.All(x=>Mathf.Abs(x-values[0])<.000001f);
        var keys=constant?new[]{new Keyframe(0,values[0]),new Keyframe(duration,values[values.Count-1])}
            : values.Select((value,i)=>new Keyframe(duration*i/(values.Count-1),value)).ToArray();
        var curve=new AnimationCurve(keys);
        for (int i=0;i<curve.length;i++) { AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear); }
        return curve;
    }
    static AnimationClip Generic(string name,JObject samples,string role)
    {
        var spec=samples["clips"][role];var frames=(JArray)spec["frames"];float duration=(float)spec["duration"];
        var bindings=new List<EditorCurveBinding>();var curves=new List<AnimationCurve>();
        string[] properties={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w","m_LocalScale.x","m_LocalScale.y","m_LocalScale.z"};
        for (int bone=0;bone<samples["paths"].Count();bone++)
        {
            string path=(string)samples["paths"][bone]; if (string.IsNullOrEmpty(path)) throw new InvalidDataException("Animator/root curve forbidden.");
            for (int channel=0;channel<10;channel++)
            {
                int b=bone,c=channel;bindings.Add(EditorCurveBinding.FloatCurve(path,typeof(Transform),properties[c]));
                curves.Add(Curve(frames.Select(f=>(float)f[b][c]).ToArray(),duration));
            }
        }
        var clip=new AnimationClip { name=name, frameRate=30, legacy=false };
        AnimationUtility.SetEditorCurves(clip,bindings.ToArray(),curves.ToArray()); clip.EnsureQuaternionContinuity();
        Settings(clip,role=="StunnedLoop"); return clip;
    }
    static void Settings(AnimationClip clip,bool loop)
    {
        var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;settings.loopBlend=false;
        settings.loopBlendOrientation=true;settings.loopBlendPositionY=true;settings.loopBlendPositionXZ=true;
        settings.keepOriginalOrientation=true;settings.keepOriginalPositionY=true;settings.keepOriginalPositionXZ=true;
        AnimationUtility.SetAnimationClipSettings(clip,settings);AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
        clip.wrapMode=loop?WrapMode.Loop:WrapMode.ClampForever;
    }
    static AnimationClip Humanoid(string name,JObject samples,string role,Animator animator,Transform[] bones,AnimationClip nativeIdle,AnimationClip nativeAttack,float contact)
    {
        var spec=samples["clips"][role];var frames=(JArray)spec["frames"];float duration=(float)spec["duration"];
        var channels=new List<float>[HumanTrait.MuscleCount+35];for(int i=0;i<channels.Length;i++)channels[i]=new List<float>();
        var pose=new HumanPose { muscles=new float[HumanTrait.MuscleCount] }; Quaternion previous=Quaternion.identity;
        using (var handler=new HumanPoseHandler(animator.avatar,animator.transform))
        {
            for(int frame=0;frame<frames.Count;frame++)
            {
                Pose(bones,(JArray)frames[frame]);handler.GetHumanPose(ref pose);
                for(int m=0;m<pose.muscles.Length;m++)channels[m].Add(pose.muscles[m]);
                var position=Quaternion.Inverse(animator.transform.rotation)*(pose.bodyPosition-animator.transform.position/(animator.humanScale*animator.transform.lossyScale.x));
                var q=Quaternion.Inverse(animator.transform.rotation)*pose.bodyRotation;if(frame>0&&Quaternion.Dot(previous,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous=q;
                for(int axis=0;axis<3;axis++)channels[HumanTrait.MuscleCount+axis].Add(position[axis]);
                for(int axis=0;axis<4;axis++)channels[HumanTrait.MuscleCount+3+axis].Add(q[axis]);
                for(int foot=0;foot<4;foot++)
                {
                    for(int axis=0;axis<3;axis++)channels[HumanTrait.MuscleCount+7+foot*7+axis].Add(pose.ikGoalPositions[foot][axis]);
                    var goal=pose.internalIkGoalRotations[foot];
                    if(frame>0)
                    {
                        int c=HumanTrait.MuscleCount+10+foot*7;
                        var prior=new Quaternion(channels[c][frame-1],channels[c+1][frame-1],channels[c+2][frame-1],channels[c+3][frame-1]);
                        if(Quaternion.Dot(goal,prior)<0)goal=new Quaternion(-goal.x,-goal.y,-goal.z,-goal.w);
                    }
                    for(int axis=0;axis<4;axis++)channels[HumanTrait.MuscleCount+10+foot*7+axis].Add(goal[axis]);
                }
            }
        }
        var clip=Object.Instantiate(nativeIdle);clip.name=name;clip.ClearCurves();clip.frameRate=30;
        var bindings=new List<EditorCurveBinding>();var curves=new List<AnimationCurve>();
        for(int channel=0;channel<channels.Length;channel++)
        {
            int slot=channel-HumanTrait.MuscleCount;
            string muscle=channel<HumanTrait.MuscleCount?HumanTrait.MuscleName[channel]:null;
            if(muscle!=null)foreach(string side in new[]{"Left","Right"})foreach(string finger in new[]{"Thumb","Index","Middle","Ring","Little"})
                if(muscle.StartsWith(side+" "+finger+" ",StringComparison.Ordinal))muscle=side+"Hand."+finger+"."+muscle.Substring(side.Length+finger.Length+2);
            string property=channel<HumanTrait.MuscleCount?muscle:slot<3?"RootT."+"xyz"[slot]
                :slot<7?"RootQ."+"xyzw"[slot-3]:new[]{"LeftFoot","RightFoot","LeftHand","RightHand"}[(slot-7)/7]+((slot-7)%7<3?"T."+"xyz"[(slot-7)%7]:"Q."+"xyzw"[(slot-10)%7]);
            bindings.Add(EditorCurveBinding.FloatCurve("",typeof(Animator),property));curves.Add(Curve(channels[channel],duration));
        }
        // Humanoid projection can redistribute limb twist. Match the native source
        // channel values at the same reviewed attack/Idle boundaries, over the same
        // short continuous bake calibration already used by the native TRS samples.
        if(role=="ParryCollapse"||role=="StunRecover")
        {
            bool start=role=="ParryCollapse";var source=start?nativeAttack:nativeIdle;float at=start?contact:0;
            var sourceBindings=AnimationUtility.GetCurveBindings(source).Where(b=>b.type==typeof(Animator)).ToDictionary(b=>b.propertyName);
            var targets=new float?[channels.Length];
            for(int c=0;c<channels.Length;c++)if(sourceBindings.TryGetValue(bindings[c].propertyName,out var binding))targets[c]=AnimationUtility.GetEditorCurve(source,binding).Evaluate(at);
            // Imported in-place Humanoid clips may project their initial XZ body
            // offset out of the pose. Read that evaluated native body position,
            // instead of reintroducing the unprojected FBX RootT curve at a seam.
            var graph=PlayableGraph.Create("Native Humanoid source boundary");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var rootPosition=animator.transform.localPosition;var rootRotation=animator.transform.localRotation;var rootScale=animator.transform.localScale;
            try
            {
                animator.enabled=true;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var playable=AnimationClipPlayable.Create(graph,source);playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);
                var channel=AnimationPlayableOutput.Create(graph,"Boundary",animator);channel.SetSourcePlayable(playable);graph.Play();playable.SetTime(at);graph.Evaluate(0);
                animator.transform.SetLocalPositionAndRotation(rootPosition,rootRotation);animator.transform.localScale=rootScale;
                var nativePose=new HumanPose();using(var handler=new HumanPoseHandler(animator.avatar,animator.transform))handler.GetHumanPose(ref nativePose);
                var nativePosition=Quaternion.Inverse(animator.transform.rotation)*(nativePose.bodyPosition-animator.transform.position/(animator.humanScale*animator.transform.lossyScale.x));
                for(int axis=0;axis<3;axis++)targets[HumanTrait.MuscleCount+axis]=nativePosition[axis];
            }
            finally{graph.Destroy();animator.enabled=false;animator.transform.SetLocalPositionAndRotation(rootPosition,rootRotation);animator.transform.localScale=rootScale;}
            foreach(int c in new[]{HumanTrait.MuscleCount+3,HumanTrait.MuscleCount+10,HumanTrait.MuscleCount+17,HumanTrait.MuscleCount+24,HumanTrait.MuscleCount+31})
                if(Enumerable.Range(c,4).All(i=>targets[i].HasValue))
                {
                    var target=new Quaternion(targets[c].Value,targets[c+1].Value,targets[c+2].Value,targets[c+3].Value);
                    int f=start?0:frames.Count-1;var anchor=new Quaternion(channels[c][f],channels[c+1][f],channels[c+2][f],channels[c+3][f]);
                    if(Quaternion.Dot(target,anchor)<0)for(int i=c;i<c+4;i++)targets[i]=-targets[i].Value;
                }
            for(int c=0;c<channels.Length;c++)if(targets[c].HasValue)
            {
                float offset=targets[c].Value-channels[c][start?0:frames.Count-1];
                for(int f=0;f<frames.Count;f++)
                {float t=Mathf.Clamp01((start?f:frames.Count-1-f)/30f/(start?.4f:.3f));float weight=1-t*t*(3-2*t);channels[c][f]+=offset*weight;}
                curves[c]=Curve(channels[c],duration);
            }
        }
        AnimationUtility.SetEditorCurves(clip,bindings.ToArray(),curves.ToArray());Settings(clip,role=="StunnedLoop");
        return clip;
    }
    static JObject Stage(JObject record)
    {
        string id=(string)record["id"], source=Path.Combine(output,(string)record["sampleFile"]);
        if(Hash(source)!=(string)record["sampleSHA256"])throw new InvalidDataException("Sample changed: "+id);
        var samples=JObject.Parse(File.ReadAllText(source));
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>((string)record["actor"]["path"]);
        var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)record["definition"]["path"]);
        var preview=EditorSceneManager.NewPreviewScene();GameObject clone=null;var rows=new JArray();
        try
        {
            clone=Object.Instantiate(prefab);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone,preview);
            foreach(var component in clone.GetComponentsInChildren<MonoBehaviour>(true))component.enabled=false;
            var animator=clone.transform.Find((string)record["animatorPath"]).GetComponent<Animator>();animator.enabled=false;animator.fireEvents=false;
            var bones=samples["paths"].Select(p=>animator.transform.Find((string)p)??throw new InvalidDataException("Native bone missing: "+id+" "+p)).ToArray();
            foreach(string role in Roles)
            {
                string path=(string)record["assetPaths"][role];Folders(Path.GetDirectoryName(path).Replace('\\','/'));
                var author=animator.isHuman?JObject.Parse(File.ReadAllText(Path.Combine(Resolve((string)record["sourceFolder"]),"authoring.json"))):null;
                var attack=author==null?null:AssetDatabase.LoadAllAssetsAtPath((string)author["sourceAttack"]["path"]).OfType<AnimationClip>().Single(c=>c.name==(string)author["sourceAttack"]["name"]);
                AnimationClip clip=animator.isHuman?Humanoid(Path.GetFileNameWithoutExtension(path),samples,role,animator,bones,definition.AnimationProfile.Idle,attack,(float)record["contact"]["seconds"])
                    :Generic(Path.GetFileNameWithoutExtension(path),samples,role);
                bool persisted=false;
                try
                {
                    float maxPosition=0,maxQuaternion=0,maxScale=0;var frames=(JArray)samples["clips"][role]["frames"];
                    for(int frame=0;frame<frames.Count;frame++)
                    {
                        clip.SampleAnimation(animator.gameObject,frame/30f);
                        for(int bone=0;bone<bones.Length;bone++)
                        {
                            var x=frames[frame][bone];var t=bones[bone];
                            maxPosition=Mathf.Max(maxPosition,Vector3.Distance(t.localPosition,new Vector3((float)x[0],(float)x[1],(float)x[2])));
                            var expected=new Quaternion((float)x[3],(float)x[4],(float)x[5],(float)x[6]);
                            maxQuaternion=Mathf.Max(maxQuaternion,1f-Mathf.Abs(Quaternion.Dot(t.localRotation,expected)));
                            maxScale=Mathf.Max(maxScale,Vector3.Distance(t.localScale,new Vector3((float)x[7],(float)x[8],(float)x[9])));
                        }
                    }
                    if(!animator.isHuman&&(maxPosition>.00005f||maxQuaternion>.00002f||maxScale>.00005f))
                        throw new InvalidDataException("Native sample differs: "+id+" "+role+" "+maxPosition+"/"+maxQuaternion+"/"+maxScale);
                    if(animator.isHuman&&!clip.isHumanMotion)throw new InvalidDataException("Humanoid actor requires a human-motion clip.");
                    if(Mathf.Abs(clip.length-(float)record["durations"][role])>.0001f||clip.isLooping!=(role=="StunnedLoop"))throw new InvalidDataException("Clip time/loop differs.");
                    AssetDatabase.CreateAsset(clip,path);persisted=true;AssetDatabase.SetLabels(clip,new[]{"OVERBURST_BlenderR6"});AssetDatabase.SaveAssetIfDirty(clip);
                    rows.Add(new JObject{["role"]=role,["path"]=path,["guid"]=AssetDatabase.AssetPathToGUID(path),["sha256"]=Hash(Path.Combine(Project,path)),
                        ["seconds"]=clip.length,["fps"]=clip.frameRate,["loop"]=clip.isLooping,["humanMotion"]=clip.isHumanMotion,["framesVerified"]=frames.Count,
                        ["maxLocalPositionDifference"]=maxPosition,["maxQuaternionDotDifference"]=maxQuaternion,["maxLocalScaleDifference"]=maxScale,
                        ["validation"]=animator.isHuman?"HUMANOID_POSE_PROJECTION_REQUIRES_RUNTIME_CHECK":"PASS_NATIVE_SAVED_SAMPLES"});
                }
                finally { if(!persisted&&clip!=null)Object.DestroyImmediate(clip); }
            }
            return new JObject{["id"]=id,["sampleSHA256"]=(string)record["sampleSHA256"],["humanoid"]=animator.isHuman,["clips"]=rows};
        }
        finally { if(clone!=null)Object.DestroyImmediate(clone);EditorSceneManager.ClosePreviewScene(preview); }
    }
    public static string RebuildStagedHumanoid(string privateOutputRelative,string id)
    {
        RequireIdle();if(Busy)throw new InvalidOperationException("Wait for the owned staging job.");
        output=Path.GetFullPath(Path.Combine(Workspace,privateOutputRelative));
        var plan=JObject.Parse(File.ReadAllText(Path.Combine(output,"data/apply-input.json")));
        var receipt=JObject.Parse(File.ReadAllText(Path.Combine(output,"data/stage-result.json")));
        if((string)receipt["status"]!="READY_NATIVE_STAGED"||File.Exists(Path.Combine(output,"data/apply-result.json")))throw new InvalidOperationException("Only unbound staged clips may be rebuilt.");
        var record=(JObject)plan["records"].Single(r=>(string)r["id"]==id);
        var samples=JObject.Parse(File.ReadAllText(Path.Combine(output,(string)record["sampleFile"])));
        var scene=EditorSceneManager.NewPreviewScene();GameObject clone=null;
        try
        {
            clone=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>((string)record["actor"]["path"]));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone,scene);
            foreach(var component in clone.GetComponentsInChildren<MonoBehaviour>(true))component.enabled=false;
            var animator=clone.transform.Find((string)record["animatorPath"]).GetComponent<Animator>();animator.enabled=false;
            if(!animator.isHuman)throw new InvalidOperationException("Humanoid correction only.");
            var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)record["definition"]["path"]);
            var bones=samples["paths"].Select(p=>animator.transform.Find((string)p)).ToArray();
            var author=JObject.Parse(File.ReadAllText(Path.Combine(Resolve((string)record["sourceFolder"]),"authoring.json")));
            var attack=AssetDatabase.LoadAllAssetsAtPath((string)author["sourceAttack"]["path"]).OfType<AnimationClip>().Single(c=>c.name==(string)author["sourceAttack"]["name"]);
            var row=receipt["staged"].Single(r=>(string)r["id"]==id);
            foreach(string role in Roles)
            {
                string path=(string)record["assetPaths"][role];var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                var previous=row["clips"].Single(r=>(string)r["role"]==role);
                if(Hash(Path.Combine(Project,path))!=(string)previous["sha256"])throw new InvalidOperationException("Staged clip was changed outside this builder.");
                var clip=Humanoid(existing.name,samples,role,animator,bones,definition.AnimationProfile.Idle,attack,(float)record["contact"]["seconds"]);
                try{EditorUtility.CopySerialized(clip,existing);AssetDatabase.SaveAssetIfDirty(existing);}
                finally{Object.DestroyImmediate(clip);}
                previous["sha256"]=Hash(Path.Combine(Project,path));previous["validation"]="HUMANOID_ROOT_AND_FOOT_GOALS_REBAKED_RUNTIME_CHECK_REQUIRED";
            }
            File.WriteAllText(Path.Combine(output,"data/stage-result.json"),receipt.ToString());return "PASS_STAGED_HUMANOID_REBAKE_GUIDS_PRESERVED";
        }
        finally{if(clone!=null)Object.DestroyImmediate(clone);EditorSceneManager.ClosePreviewScene(scene);}
    }
    static JObject Bind(JObject record)
    {
        string id=(string)record["id"], controllerPath=(string)record["controller"]["path"],profilePath=(string)record["profile"]["path"];
        foreach(string path in new[]{controllerPath,profilePath})
            if(Hash(Path.Combine(Project,path))!=(string)input["beforeBackups"][path]["sha256"])throw new InvalidOperationException("Owned binding changed before save: "+path);
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        var profile=AssetDatabase.LoadAssetAtPath<EnemyAnimationProfile>(profilePath);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>((string)record["actor"]["path"]);
        if(controller==null||profile==null||prefab.GetComponent<EnemyActor>().Animator.runtimeAnimatorController!=controller
            ||profile.RuntimeController!=controller)throw new InvalidDataException("Current native controller/profile differs.");
        var sm=controller.layers[0].stateMachine;var loco=sm.states.Single(s=>s.state.name=="Locomotion").state;
        var createdStates=new JArray();var serialized=new SerializedObject(profile);
        for(int i=0;i<Roles.Length;i++)
        {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>((string)record["assetPaths"][Roles[i]]);
            var state=sm.states.Select(s=>s.state).SingleOrDefault(s=>s.name==StateNames[i]);
            if(state==null){state=sm.AddState(StateNames[i]);createdStates.Add(StateNames[i]);}
            state.motion=clip;state.speed=i==0?EnemyAnimationBridge.ParryCollapseSpeed:1;state.speedParameterActive=false;EditorUtility.SetDirty(state);
            serialized.FindProperty(Fields[i]).objectReferenceValue=clip;
        }
        var collapse=sm.states.Single(s=>s.state.name==StateNames[0]).state;
        var loop=sm.states.Single(s=>s.state.name==StateNames[1]).state;
        var recover=sm.states.Single(s=>s.state.name==StateNames[2]).state;
        foreach(var pair in new[]{new[]{collapse,loop},new[]{recover,loco}})
            if(!pair[0].transitions.Any(t=>t.destinationState==pair[1]))
            {var t=pair[0].AddTransition(pair[1]);t.hasExitTime=true;t.exitTime=1;t.hasFixedDuration=true;t.duration=.08f;}
        serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(profile);EditorUtility.SetDirty(sm);EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);AssetDatabase.SaveAssetIfDirty(profile);
        foreach(int i in Enumerable.Range(0,3))
            if(sm.states.Single(s=>s.state.name==StateNames[i]).state.motion!=AssetDatabase.LoadAssetAtPath<AnimationClip>((string)record["assetPaths"][Roles[i]]))throw new InvalidOperationException("Persisted role differs.");
        return new JObject{["id"]=id,["controller"]=controllerPath,["profile"]=profilePath,["createdStates"]=createdStates,
            ["controllerSHA256"]=Hash(Path.Combine(Project,controllerPath)),["profileSHA256"]=Hash(Path.Combine(Project,profilePath)),
            ["controllerGUIDPreserved"]=AssetDatabase.AssetPathToGUID(controllerPath)==(string)record["controller"]["guid"],
            ["profileGUIDPreserved"]=AssetDatabase.AssetPathToGUID(profilePath)==(string)record["profile"]["guid"]};
    }
}
