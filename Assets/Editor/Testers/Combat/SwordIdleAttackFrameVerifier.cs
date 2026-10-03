using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

// Read-only native resampling of existing, saved candidates. All preview objects are owned here.
public static class SwordIdleAttackFrameVerifier
{
    static bool Busy()=>EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating;
    static string[] Scenes()=>Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return s.path+"|"+s.isDirty+"|"+s.rootCount;}).ToArray();
    public static string MicroNow(string output,string requests)
    {
        if(Busy())throw new InvalidOperationException("Short microprobe requires idle EditMode.");
        if(Newtonsoft.Json.JsonConvert.DeserializeObject<List<Frame>>(File.ReadAllText(requests)).Count>200)throw new Exception("Use detached Main for larger audits.");
        using(var session=new Session(output,requests,false))while(session.Step()){}
        return "PASS: bounded microprobe completed; owned resources released.";
    }
    public static Task<string> Main(string output,string requests="",bool render=true)
    {
        var task=new TaskCompletionSource<string>();Session session=null;double deadline=EditorApplication.timeSinceStartup+180,stable=0;string stamp=null;bool finished=false;
        EditorApplication.CallbackFunction update=null;AssemblyReloadEvents.AssemblyReloadCallback reload=null;
        Action<Exception> finish=e=>{if(finished)return;finished=true;EditorApplication.update-=update;AssemblyReloadEvents.beforeAssemblyReload-=reload;try{session?.Dispose();}catch(Exception x){if(e==null)e=x;}if(e!=null)task.TrySetException(e);else task.TrySetResult("PASS: native frame audit complete; preview resources released.");};
        reload=()=>finish(new InvalidOperationException("Reload interrupted owned audit."));
        update=()=>{try{if(session==null){if(Busy()){stamp=null;if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Shared Editor stayed busy; audit did not start.");return;}var now=string.Join(";",Scenes());if(now!=stamp){stamp=now;stable=EditorApplication.timeSinceStartup;return;}if(EditorApplication.timeSinceStartup-stable<2)return;session=new Session(output,requests,render);}if(Busy())throw new InvalidOperationException("Shared Editor became busy; owned audit stopped.");for(int i=0;i<(render&&requests!=""?1:16);i++)if(!session.Step()){finish(null);return;}}catch(Exception e){finish(e);}};
        EditorApplication.update+=update;AssemblyReloadEvents.beforeAssemblyReload+=reload;return task.Task;
    }
    public static string InspectCurves(string output)
    {
        if(Busy())throw new InvalidOperationException("Inspect saved assets in idle EditMode.");
        Directory.CreateDirectory(output);var records=new List<object>();
        foreach(var target in SwordIdleAttackCopyBuilder.Targets())
        {
            var a=target.step.animationClip;var z=AssetDatabase.LoadAssetAtPath<AnimationClip>(SwordIdleAttackCopyBuilder.Folder+"/GS_"+target.role+"_SwordIdle.anim");
            var bindings=AnimationUtility.GetCurveBindings(a);float beginningError=0,beginningTime=0;string property="";
            foreach(var binding in bindings)
            {
                var original=AnimationUtility.GetEditorCurve(a,binding);var edited=AnimationUtility.GetEditorCurve(z,binding);if(edited==null)throw new Exception("Missing copied curve "+binding.propertyName);
                for(int i=0;i<=36;i++){float time=i/720f,error=Mathf.Abs(original.Evaluate(time)-edited.Evaluate(time));if(error>beginningError){beginningError=error;beginningTime=time;property=binding.propertyName;}}
            }
            bool qualityA,qualityZ;using(var serialized=new SerializedObject(a))qualityA=serialized.FindProperty("m_UseHighQualityCurve").boolValue;using(var serialized=new SerializedObject(z))qualityZ=serialized.FindProperty("m_UseHighQualityCurve").boolValue;
            records.Add(new{target.role,beginningCurveError=beginningError,beginningTime,property,sourceHighQuality=qualityA,editedHighQuality=qualityZ,sourceHash=SwordIdleAttackCopyBuilder.Hash(AssetDatabase.GetAssetPath(a)),editedHash=SwordIdleAttackCopyBuilder.Hash(AssetDatabase.GetAssetPath(z)),sourceHumanMotion=a.humanMotion,editedHumanMotion=z.humanMotion,curveAudit=new[]{Session.AuditCurves(a),Session.AuditCurves(z)}});
        }
        File.WriteAllText(Path.Combine(output,"saved-curve-check.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{assembly=typeof(SwordIdleAttackFrameVerifier).Assembly.FullName,records,assetsChanged=false},Newtonsoft.Json.Formatting.Indented));
        return "PASS: saved source/candidate curves inspected; no assets changed.";
    }
    public sealed class Frame
    {
        public string scenario,role;public int side,frame,clipA,clipB,index;public float time,pA,pB,weight,idleTime;public string view="overview";
    }
    sealed class Rig { public AnimationMixerPlayable mixer;public AnimationClipPlayable[] clips; }
    sealed class Session:IDisposable
    {
        const int Fps=120,W=896,H=730;const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly string output;readonly bool capture;readonly string[] before;readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        readonly List<Rig> rigs=new List<Rig>();readonly List<Tuple<SkinnedMeshRenderer,Mesh>> skins=new List<Tuple<SkinnedMeshRenderer,Mesh>>();
        readonly List<Frame> frames=new List<Frame>();readonly List<SwordIdleAttackCopyBuilder.Target> targets;
        ModelAnimationPreviewWindow window;GameObject root,weapon;PreviewRenderUtility utility;PlayableGraph graph;AnimationClip[] clips;
        Transform[] bones;string[] boneNames;Transform tip;HumanoidFootContactRig feet;StreamWriter samples;
        Mesh floor;Material floorMat,gridMat;int cursor;bool complete,disposed;
        object Get(string n)=>typeof(ModelAnimationPreviewWindow).GetField(n,Flags).GetValue(window);
        void Set(string n,object v)=>typeof(ModelAnimationPreviewWindow).GetField(n,Flags).SetValue(window,v);
        object Call(string n,params object[] a)=>typeof(ModelAnimationPreviewWindow).GetMethod(n,Flags).Invoke(window,a);
        T Own<T>(T o)where T:UnityEngine.Object{o.hideFlags=HideFlags.HideAndDontSave;owned.Add(o);return o;}
        Material Mat(Color color){var m=Own(new Material(Shader.Find("Universal Render Pipeline/Unlit")));m.SetColor("_BaseColor",color);return m;}
        public Session(string output,string requests,bool render)
        {
            this.output=output;capture=requests!=""&&render;Directory.CreateDirectory(output);before=Scenes();targets=SwordIdleAttackCopyBuilder.Targets();
            try
            {
                window=ScriptableObject.CreateInstance<ModelAnimationPreviewWindow>();window.hideFlags=HideFlags.HideAndDontSave;Set("isPlaying",false);Set("autoUseAnimationSelection",false);
                Set("selectedModelPrefab",AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("4f97a974eab7ea34abcd80e33c41133d")));
                Set("selectedWeaponPrefab",AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("4eccfacbe53efa34dbb7ad4a2e73e255")));Call("RebuildPreviewInstance");Call("DestroyAnimatorPreviewGraph");
                root=(GameObject)Get("previewInstance");weapon=(GameObject)Get("previewWeaponInstance");utility=(PreviewRenderUtility)Get("previewUtility");var main=(Animator)Call("FindPreviewAnimator");
                if(root==null||main==null)throw new Exception("Native preview rig unavailable.");
                feet=main.GetComponent<HumanoidFootContactRig>();if(feet==null||!feet.IsConfigured)throw new Exception("Heel/toe markers unavailable.");
                var binding=weapon.GetComponentInChildren<WeaponTraceBinding>(true);tip=binding!=null?binding.WeaponTip:null;if(tip==null)throw new Exception("Real weapon tip marker unavailable.");
                var sources=new List<AnimationClip>{AssetDatabase.LoadAssetAtPath<AnimationClip>(SwordIdleAttackCopyBuilder.IdlePath)};
                foreach(var target in targets){sources.Add(target.step.animationClip);sources.Add(AssetDatabase.LoadAssetAtPath<AnimationClip>(SwordIdleAttackCopyBuilder.Folder+"/GS_"+target.role+"_SwordIdle.anim"));}
                if(sources.Any(c=>c==null))throw new Exception("Saved clip missing.");
                clips=sources.Select(s=>Own(UnityEngine.Object.Instantiate(s))).ToArray();
                foreach(var clip in clips){var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.keepOriginalOrientation=true;settings.keepOriginalPositionXZ=true;if(clip!=clips[0])settings.loopTime=false;AnimationUtility.SetAnimationClipSettings(clip,settings);}
                graph=PlayableGraph.Create("Owned frame audit");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                foreach(var animator in root.GetComponentsInChildren<Animator>(true).Where(a=>a.avatar!=null&&a.avatar.isHuman&&a.gameObject.activeInHierarchy).OrderBy(a=>a==main?0:1))
                {
                    animator.enabled=true;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;var rig=new Rig{mixer=AnimationMixerPlayable.Create(graph,clips.Length),clips=clips.Select(c=>AnimationClipPlayable.Create(graph,c)).ToArray()};
                    for(int i=0;i<clips.Length;i++){rig.clips[i].SetSpeed(0);rig.clips[i].SetApplyFootIK(false);rig.clips[i].SetApplyPlayableIK(false);graph.Connect(rig.clips[i],0,rig.mixer,i);}var playable=AnimationPlayableOutput.Create(graph,"Pose",animator);playable.SetSourcePlayable(rig.mixer);rigs.Add(rig);
                }
                var ids=Enumerable.Range(0,(int)HumanBodyBones.LastBone).Select(i=>(HumanBodyBones)i).Where(b=>main.GetBoneTransform(b)!=null).ToArray();bones=ids.Select(main.GetBoneTransform).ToArray();boneNames=ids.Select(b=>b.ToString()).ToArray();graph.Play();
                if(requests!="")frames.AddRange(Newtonsoft.Json.JsonConvert.DeserializeObject<List<Frame>>(File.ReadAllText(requests)));
                else
                {
                    var definition=AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(SwordIdleAttackCopyBuilder.Definition);float baseSpeed=definition.comboDefinition.baseAnimationSpeed;
                    var records=new List<object>();
                    for(int r=0;r<targets.Count;r++)
                    {
                        var t=targets[r];var step=t.step;float length=step.animationClip.length,speed=Mathf.Max(.01f,step.animationSpeedMultiplier*(r<4?baseSpeed:1));
                        float duration=step.playbackAcceleration.ToElapsed(1)*length/speed;
                        for(int f=0;f<=Mathf.CeilToInt(length*Fps);f++)for(int side=0;side<2;side++)frames.Add(new Frame{scenario="clip",role=t.role,frame=f,time=Mathf.Min(f/(float)Fps,length),side=side,clipA=1+r*2+side,clipB=0,pA=Mathf.Min(f/(float)Fps/length,1),weight=1});
                        for(int f=0;f<Mathf.CeilToInt((.45f+duration+.40f)*Fps);f++)for(int side=0;side<2;side++)
                        {
                            float time=f/(float)Fps,p=Mathf.Clamp01(step.playbackAcceleration.ToClipProgress(Mathf.Max(0,time-.45f)*speed/length)),weight=Smooth((time-.38f)/.12f)*(1-Smooth((time-.45f-duration)/.18f));
                            frames.Add(new Frame{scenario="blend",role=t.role,frame=f,time=time,side=side,clipA=1+r*2+side,clipB=0,pA=p,weight=weight,idleTime=time});
                        }
                        var phases=(step.attackPhases??new AttackPhaseData[0]).Select(p=>new[]{p.SafeStart,p.SafeEnd}).ToArray();
                        var trails=(step.trailPhases??new AttackTrailPhaseData[0]).Select(p=>new[]{p.SafeStart,p.SafeEnd}).ToArray();
                        records.Add(new{t.role,length,speed,duration,entry=t.adaptEntry,step.continuationStartNormalizedTime,step.transitionDuration,comboStart=step.comboInputWindow.SafeStart,comboEnd=step.comboInputWindow.SafeEnd,phases,trails,sourcePath=AssetDatabase.GetAssetPath(sources[1+2*r]),editedPath=AssetDatabase.GetAssetPath(sources[2+2*r]),sourceHash=SwordIdleAttackCopyBuilder.Hash(AssetDatabase.GetAssetPath(sources[1+2*r])),editedHash=SwordIdleAttackCopyBuilder.Hash(AssetDatabase.GetAssetPath(sources[2+2*r])),curveAudit=new[]{CurveAudit(sources[1+2*r]),CurveAudit(sources[2+2*r])}});
                    }
                    for(int r=0;r<3;r++)
                    {
                        var previous=targets[r].step;var next=targets[r+1].step;float[] cuts={previous.comboInputWindow.SafeStart,(previous.comboInputWindow.SafeStart+previous.comboInputWindow.SafeEnd)*.5f,previous.comboInputWindow.SafeEnd};
                        for(int cut=0;cut<3;cut++)for(int f=0;f<=66;f++)for(int side=0;side<2;side++)
                        {
                            float time=-.15f+f/(float)Fps;
                            float pA=Mathf.Clamp01(previous.playbackAcceleration.ToClipProgress(previous.playbackAcceleration.ToElapsed(cuts[cut])+time*baseSpeed*previous.animationSpeedMultiplier/previous.animationClip.length));
                            // Reviewed candidate profile: restart from Idle only after the previous copy has settled.
                            float start=side==1&&cuts[cut]>=(r==1?.825f:.90f)?0:next.continuationStartNormalizedTime;
                            float pB=Mathf.Clamp01(next.playbackAcceleration.ToClipProgress(next.playbackAcceleration.ToElapsed(start)+Mathf.Max(0,time)*baseSpeed*next.animationSpeedMultiplier/next.animationClip.length));
                            float blendSeconds=side==0?next.transitionDuration:.12f;
                            float weight=time<0?1:blendSeconds<=0?0:1-Mathf.Clamp01(time/blendSeconds);
                            frames.Add(new Frame{scenario="combo-"+new[]{"early","mid","late"}[cut],role=targets[r].role+"->"+targets[r+1].role,frame=f,time=time,side=side,clipA=1+r*2+side,clipB=1+(r+1)*2+side,pA=pA,pB=pB,weight=weight});
                        }
                    }
                    File.WriteAllText(Path.Combine(output,"metadata.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{fps=Fps,boneNames,totalSamples=frames.Count,records,baseSpeed,footIk=0,guardExcluded=true,comboBlend="preview profile: original serialized transitions; candidate .12s and settled Idle restart at 0; gameplay definition unchanged",rootMotion="actor fixed; relative pose trajectories only",blend="same illustrative .12 entry / .18 exit as existing video"},Newtonsoft.Json.Formatting.Indented));
                }
                samples=new StreamWriter(Path.Combine(output,"samples.jsonl"),false,new System.Text.UTF8Encoding(false));
                if(capture)
                {
                    foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)){if(!skin.enabled||!skin.gameObject.activeInHierarchy||skin.sharedMesh==null)continue;var mesh=Own(new Mesh());var go=new GameObject("Owned audit mesh",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(skin.transform,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials.Select(src=>{var m=Mat(Color.white);var texture=src!=null&&src.HasProperty("_BaseMap")?src.GetTexture("_BaseMap"):src!=null&&src.HasProperty("_MainTex")?src.GetTexture("_MainTex"):null;if(texture!=null)m.SetTexture("_BaseMap",texture);return m;}).ToArray();skin.enabled=false;skins.Add(Tuple.Create(skin,mesh));}
                    floor=Own(new Mesh());floor.vertices=new[]{new Vector3(-.5f,0,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f),new Vector3(.5f,0,-.5f)};floor.triangles=new[]{0,1,2,0,2,3};floor.RecalculateNormals();floorMat=Mat(new Color(.10f,.13f,.17f));gridMat=Mat(new Color(.21f,.27f,.32f));
                }
            }catch{Dispose();throw;}
        }
        public static object AuditCurves(AnimationClip clip)=>CurveAudit(clip);
        static object CurveAudit(AnimationClip clip)
        {
            var all=AnimationUtility.GetCurveBindings(clip).Where(b=>b.type==typeof(Animator)&&b.path=="").ToDictionary(b=>b.propertyName,b=>AnimationUtility.GetEditorCurve(clip,b));var result=new List<object>();
            foreach(var prefix in new[]{"RootQ.","LeftFootQ.","RightFootQ."})
            {
                if(!all.ContainsKey(prefix+"w"))continue;var curves=new[]{"x","y","z","w"}.Select(c=>all[prefix+c]).ToArray();float worstNorm=0,worstNormTime=0,jump=0,jumpTime=0;
                Func<float,Quaternion> q=time=>new Quaternion(curves[0].Evaluate(time),curves[1].Evaluate(time),curves[2].Evaluate(time),curves[3].Evaluate(time));
                for(int f=0;f<=Mathf.CeilToInt(clip.length*Fps);f++){float time=Mathf.Min(f/(float)Fps,clip.length);var value=q(time);float norm=Mathf.Sqrt(Quaternion.Dot(value,value)),error=Mathf.Abs(norm-1);if(error>worstNorm){worstNorm=error;worstNormTime=time;}}
                foreach(var time in curves.SelectMany(c=>c.keys.Select(k=>k.time)).Distinct().Where(t=>t>.00002f&&t<clip.length-.00002f)){float angle=PreciseAngle(q(time-.00001f),q(time+.00001f));if(angle>jump){jump=angle;jumpTime=time;}}
                result.Add(new{prefix,worstNorm,worstNormTime,microJump_deg=jump,jumpTime,epsilon_sec=.00001f});
            }
            return result;
        }
        static float PreciseAngle(Quaternion a,Quaternion b)
        {
            double[] x={a.x,a.y,a.z,a.w},y={b.x,b.y,b.z,b.w};double na=Math.Sqrt(x.Sum(v=>v*v)),nb=Math.Sqrt(y.Sum(v=>v*v));for(int i=0;i<4;i++){x[i]/=na;y[i]/=nb;}double sign=x.Zip(y,(v,w)=>v*w).Sum()<0?-1:1;
            double difference=Math.Sqrt(x.Zip(y,(v,w)=>(v-sign*w)*(v-sign*w)).Sum()),sum=Math.Sqrt(x.Zip(y,(v,w)=>(v+sign*w)*(v+sign*w)).Sum());return (float)(4*Math.Atan2(difference,sum)*180/Math.PI);
        }
        static float Smooth(float p){p=Mathf.Clamp01(p);return p*p*(3-2*p);}
        static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
        static float[] Q(Quaternion q)=>new[]{q.x,q.y,q.z,q.w};
        float Height(int clip,float p){if(clip==0)return 0;var step=targets[(clip-1)/2].step;return step.visualHeightCurve!=null&&step.visualHeightCurve.length>0?step.visualHeightCurve.Evaluate(p):0;}
        void Pose(Frame frame)
        {
            root.transform.SetPositionAndRotation(Vector3.up*.022f,Quaternion.identity);
            foreach(var rig in rigs)for(int i=0;i<clips.Length;i++){rig.mixer.SetInputWeight(i,i==frame.clipA?frame.weight:i==frame.clipB?1-frame.weight:0);rig.clips[i].SetTime(i==0?frame.idleTime:i==frame.clipA?frame.pA*clips[i].length:i==frame.clipB?frame.pB*clips[i].length:0);}
            graph.Evaluate(0);Call("ApplyPreviewConstraintBindings");Call("ApplyWeaponPose");root.transform.position+=Vector3.up*(Height(frame.clipA,frame.pA)*frame.weight+Height(frame.clipB,frame.pB)*(1-frame.weight));
        }
        public bool Step()
        {
            if(cursor>=frames.Count){complete=true;return false;}var frame=frames[cursor];frame.index=cursor;Pose(frame);
            if(capture)Render(Path.Combine(output,cursor.ToString("D4")+".jpg"),frame.view);
            samples.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{request=frame,positions=bones.SelectMany(b=>V(b.position)).ToArray(),rotations=bones.SelectMany(b=>Q(b.localRotation)).ToArray(),feet=new[]{feet.LeftHeel,feet.RightHeel,feet.LeftToe,feet.RightToe}.SelectMany(b=>V(b.position)).ToArray(),weaponTip=V(tip.position),weaponPosition=V(weapon.transform.position),weaponRotation=Q(weapon.transform.rotation)}));
            cursor++;if(cursor%128==0){samples?.Flush();File.WriteAllText(Path.Combine(output,"progress.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{cursor,count=frames.Count,frame.role,frame.scenario}));}return true;
        }
        void Render(string path,string view)
        {
            foreach(var skin in skins)skin.Item1.BakeMesh(skin.Item2);var camera=utility.camera;camera.fieldOfView=view=="legs"?26:36;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.08f,.11f);var rot=Quaternion.Euler(view=="legs"?14:21,view=="side"?110:205,0);camera.transform.SetPositionAndRotation(new Vector3(0,view=="legs"?.38f:.86f,0)-rot*Vector3.forward*(view=="legs"?3.2f:5.4f),rot);
            utility.BeginPreview(new Rect(0,0,W,H),GUIStyle.none);utility.DrawMesh(floor,Matrix4x4.TRS(new Vector3(0,-.01f,0),Quaternion.identity,new Vector3(14,1,18)),floorMat,0);
            for(int z=-8;z<=8;z++)utility.DrawMesh(floor,Matrix4x4.TRS(new Vector3(0,-.005f,z),Quaternion.identity,new Vector3(14,1,.012f)),gridMat,0);for(int x=-7;x<=7;x++)utility.DrawMesh(floor,Matrix4x4.TRS(new Vector3(x,-.005f,0),Quaternion.identity,new Vector3(.012f,1,18)),gridMat,0);utility.Render(true);var texture=utility.EndPreview() as RenderTexture;var previous=RenderTexture.active;Texture2D pixels=null;
            try{RenderTexture.active=texture;pixels=new Texture2D(W,H,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,W,H),0,0);pixels.Apply(false);File.WriteAllBytes(path,pixels.EncodeToJPG(93));}finally{RenderTexture.active=previous;if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;try{samples?.Dispose();}finally{if(graph.IsValid())graph.Destroy();if(window!=null)UnityEngine.Object.DestroyImmediate(window);for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)UnityEngine.Object.DestroyImmediate(owned[i]);File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{status=complete?"PASS":"INTERRUPTED",cursor,capture,before,after=Scenes(),scenesPreserved=before.SequenceEqual(Scenes()),resourcesReleased=true,gameplayApplied=false,globalCleanup="DEFERRED: shared ownership unknown"},Newtonsoft.Json.Formatting.Indented));}
        }
    }
}
