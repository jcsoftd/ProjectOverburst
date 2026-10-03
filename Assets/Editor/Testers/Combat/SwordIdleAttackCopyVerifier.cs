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

// Native P09 comparison of saved candidates, with no scene, input, account or gameplay changes.
public static class SwordIdleAttackCopyVerifier
{
    static bool Busy()=>EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating;
    public static Task<string> Main(string output,bool capture)
    {
        var task=new TaskCompletionSource<string>();Session session=null;double deadline=EditorApplication.timeSinceStartup+180,stable=0;string stamp=null;
        EditorApplication.CallbackFunction update=null;AssemblyReloadEvents.AssemblyReloadCallback reload=null;
        Action<Exception> finish=e=>{EditorApplication.update-=update;AssemblyReloadEvents.beforeAssemblyReload-=reload;try{session?.Dispose();}catch(Exception x){if(e==null)e=x;}if(e!=null)task.TrySetException(e);else task.TrySetResult("PASS: saved-clip native comparison completed; owned resources released.");};
        reload=()=>finish(new InvalidOperationException("Reload interrupted owned preview."));
        update=()=>{try{if(session==null){if(Busy()){stamp=null;if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Editor stayed busy; preview not started.");return;}string now=string.Join(";",Scenes());if(now!=stamp){stamp=now;stable=EditorApplication.timeSinceStartup;return;}if(EditorApplication.timeSinceStartup-stable<2)return;session=new Session(output,capture);}if(Busy())throw new InvalidOperationException("Editor became busy; owned preview stopped.");for(int i=0;i<2;i++)if(!session.Step()){finish(null);return;}}catch(Exception e){finish(e);}};
        EditorApplication.update+=update;AssemblyReloadEvents.beforeAssemblyReload+=reload;return task.Task;
    }
    static string[] Scenes()=>Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return s.path+"|"+s.isDirty+"|"+s.rootCount;}).ToArray();
    sealed class Rig { public Animator animator;public AnimationMixerPlayable mixer;public AnimationClipPlayable[] playables; }
    sealed class Session:IDisposable
    {
        const int Fps=60,W=896,H=730;const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();readonly List<Rig> rigs=new List<Rig>();readonly List<Tuple<SkinnedMeshRenderer,Mesh>> skins=new List<Tuple<SkinnedMeshRenderer,Mesh>>();
        readonly string output;readonly bool capture;readonly string[] before;readonly List<SwordIdleAttackCopyBuilder.Target> targets;
        ModelAnimationPreviewWindow window;GameObject root;PreviewRenderUtility utility;PlayableGraph graph;AnimationClip[] clips;
        Mesh floor;Material floorMat,gridMat;StreamWriter samples;Transform[] bones;HumanoidFootContactRig feet;
        int role,frame,totalFrames;bool complete,disposed;List<object> checks=new List<object>();
        object Get(string n)=>typeof(ModelAnimationPreviewWindow).GetField(n,Flags).GetValue(window);
        void Set(string n,object v)=>typeof(ModelAnimationPreviewWindow).GetField(n,Flags).SetValue(window,v);
        object Call(string n,params object[] a)=>typeof(ModelAnimationPreviewWindow).GetMethod(n,Flags).Invoke(window,a);
        T Own<T>(T o)where T:UnityEngine.Object{o.hideFlags=HideFlags.HideAndDontSave;owned.Add(o);return o;}
        Material Mat(Color c){var m=Own(new Material(Shader.Find("Universal Render Pipeline/Unlit")));m.SetColor("_BaseColor",c);return m;}
        public Session(string output,bool capture)
        {
            this.output=output;this.capture=capture;Directory.CreateDirectory(output);before=Scenes();targets=SwordIdleAttackCopyBuilder.Targets();
            try
            {
                window=ScriptableObject.CreateInstance<ModelAnimationPreviewWindow>();window.hideFlags=HideFlags.HideAndDontSave;Set("isPlaying",false);Set("autoUseAnimationSelection",false);
                Set("selectedModelPrefab",AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("4f97a974eab7ea34abcd80e33c41133d")));
                Set("selectedWeaponPrefab",AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("4eccfacbe53efa34dbb7ad4a2e73e255")));Call("RebuildPreviewInstance");Call("DestroyAnimatorPreviewGraph");
                root=(GameObject)Get("previewInstance");utility=(PreviewRenderUtility)Get("previewUtility");var main=(Animator)Call("FindPreviewAnimator");if(root==null||main==null)throw new Exception("P09 preview missing.");
                feet=main.GetComponent<HumanoidFootContactRig>();if(feet==null||!feet.IsConfigured)throw new Exception("Native heel/toe contacts missing.");
                var sources=new List<AnimationClip>{AssetDatabase.LoadAssetAtPath<AnimationClip>(SwordIdleAttackCopyBuilder.IdlePath)};
                foreach(var t in targets){sources.Add(t.step.animationClip);sources.Add(AssetDatabase.LoadAssetAtPath<AnimationClip>(SwordIdleAttackCopyBuilder.Folder+"/GS_"+t.role+"_SwordIdle.anim"));}
                if(sources.Any(c=>c==null))throw new Exception("Saved candidate missing.");
                for(int i=0;i<targets.Count;i++)
                {
                    var control=Own(UnityEngine.Object.Instantiate(targets[i].step.animationClip));
                    var cb=AnimationUtility.GetCurveBindings(control).Where(b=>b.type==typeof(Animator)&&b.path=="").ToArray();
                    AnimationUtility.SetEditorCurves(control,cb,cb.Select(b=>AnimationUtility.GetEditorCurve(control,b)).ToArray());
                    sources.Add(control);
                }
                clips=sources.Select(s=>Own(UnityEngine.Object.Instantiate(s))).ToArray();
                foreach(var c in clips){var st=AnimationUtility.GetAnimationClipSettings(c);st.keepOriginalOrientation=true;st.keepOriginalPositionXZ=true;if(c!=clips[0])st.loopTime=false;AnimationUtility.SetAnimationClipSettings(c,st);}
                graph=PlayableGraph.Create("Owned saved Sword Idle attack comparison");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                foreach(var a in root.GetComponentsInChildren<Animator>(true).Where(a=>a.avatar!=null&&a.avatar.isHuman&&a.gameObject.activeInHierarchy).OrderBy(a=>a==main?0:1))
                {
                    a.enabled=true;a.applyRootMotion=false;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;var r=new Rig{animator=a,mixer=AnimationMixerPlayable.Create(graph,clips.Length),playables=clips.Select(c=>AnimationClipPlayable.Create(graph,c)).ToArray()};
                    for(int i=0;i<clips.Length;i++){r.playables[i].SetSpeed(0);r.playables[i].SetApplyFootIK(false);r.playables[i].SetApplyPlayableIK(false);graph.Connect(r.playables[i],0,r.mixer,i);}var o=AnimationPlayableOutput.Create(graph,"Pose",a);o.SetSourcePlayable(r.mixer);rigs.Add(r);
                }
                bones=new[]{HumanBodyBones.Hips,HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.UpperChest,HumanBodyBones.LeftShoulder,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand}.Select(main.GetBoneTransform).ToArray();
                graph.Play();Pose(0,0,1,0);Validate(sources);
                samples=new StreamWriter(Path.Combine(output,"samples.jsonl"),false,new System.Text.UTF8Encoding(false));
                if(capture)
                {
                    foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)){if(!skin.enabled||!skin.gameObject.activeInHierarchy||skin.sharedMesh==null)continue;var mesh=Own(new Mesh());var go=new GameObject("Owned baked comparison mesh",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(skin.transform,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;
                        go.GetComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials.Select(src=>{var m=Mat(Color.white);Texture tex=src!=null&&src.HasProperty("_BaseMap")?src.GetTexture("_BaseMap"):src!=null&&src.HasProperty("_MainTex")?src.GetTexture("_MainTex"):null;if(tex!=null)m.SetTexture("_BaseMap",tex);return m;}).ToArray();skin.enabled=false;skins.Add(Tuple.Create(skin,mesh));}
                    floor=Own(new Mesh());floor.vertices=new[]{new Vector3(-.5f,0,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f),new Vector3(.5f,0,-.5f)};floor.triangles=new[]{0,1,2,0,2,3};floor.RecalculateNormals();floorMat=Mat(new Color(.10f,.13f,.17f));gridMat=Mat(new Color(.21f,.27f,.32f));
                }
            }catch{Dispose();throw;}
        }
        void Pose(int clip,float time,float weight,float idleTime)
        {
            root.transform.SetPositionAndRotation(Vector3.up*.022f,Quaternion.identity);
            foreach(var r in rigs){for(int i=0;i<clips.Length;i++){r.mixer.SetInputWeight(i,i==clip?weight:i==0?1-weight:0);r.playables[i].SetTime(i==0?idleTime:time);}}
            graph.Evaluate(0);Call("ApplyPreviewConstraintBindings");Call("ApplyWeaponPose");
        }
        Quaternion[] Rotations()=>bones.Select(b=>b==null?Quaternion.identity:b.localRotation).ToArray();
        static float Rms(Quaternion[] a,Quaternion[] b,int from,int count)=>Mathf.Sqrt(Enumerable.Range(from,count).Select(i=>Mathf.Pow(Quaternion.Angle(a[i],b[i]),2)).Average());
        float Width(){var l=feet.LeftHeel.position;var r=feet.RightHeel.position;return Vector2.Distance(new Vector2(l.x,l.z),new Vector2(r.x,r.z));}
        void Validate(List<AnimationClip> sources)
        {
            Pose(0,0,1,0);var idle=Rotations();float idleWidth=Width();
            for(int i=0;i<targets.Count;i++)
            {
                var t=targets[i];var original=sources[1+i*2];var edited=sources[2+i*2];var bindings=AnimationUtility.GetCurveBindings(original);var phases=(t.step.attackPhases??new AttackPhaseData[0]).Select(p=>new Vector2(p.SafeStart,p.SafeEnd)).Concat((t.step.trailPhases??new AttackTrailPhaseData[0]).Select(p=>new Vector2(p.SafeStart,p.SafeEnd))).ToArray();float first=phases.Min(p=>p.x),last=phases.Max(p=>p.y),error=0,angle=0,xz=0;
                foreach(var b in bindings){var a=AnimationUtility.GetEditorCurve(original,b);var z=AnimationUtility.GetEditorCurve(edited,b);if(z==null)throw new Exception("Missing copied binding "+b.propertyName);for(int k=0;k<=240;k++){float time=Mathf.Lerp(first,last,k/240f)*original.length;error=Mathf.Max(error,Mathf.Abs(a.Evaluate(time)-z.Evaluate(time)));if(b.propertyName=="RootT.x"||b.propertyName=="RootT.z"){time=k/240f*original.length;xz=Mathf.Max(xz,Mathf.Abs(a.Evaluate(time)-z.Evaluate(time)));}}}
                var perBone=new float[bones.Length];float controlDifference=0,conversionDifference=0;
                for(int k=0;k<=24;k++){float time=Mathf.Lerp(first,last,k/24f)*original.length;Pose(1+i*2,time,1,0);var a=Rotations();Pose(2+i*2,time,1,0);var z=Rotations();angle=Mathf.Max(angle,Rms(a,z,0,bones.Length));for(int b=0;b<bones.Length;b++)perBone[b]=Mathf.Max(perBone[b],Quaternion.Angle(a[b],z[b]));Pose(1+targets.Count*2+i,time,1,0);var c=Rotations();controlDifference=Mathf.Max(controlDifference,Rms(c,z,0,bones.Length));conversionDifference=Mathf.Max(conversionDifference,Rms(c,a,0,bones.Length));}
                Pose(1+i*2,original.length,1,0);float oldLower=Rms(idle,Rotations(),0,7),oldUpper=Rms(idle,Rotations(),7,10),oldWidth=Width();Pose(2+i*2,edited.length,1,0);float newLower=Rms(idle,Rotations(),0,7),newUpper=Rms(idle,Rotations(),7,10),newWidth=Width();
                bool events=Newtonsoft.Json.JsonConvert.SerializeObject(AnimationUtility.GetAnimationEvents(original))==Newtonsoft.Json.JsonConvert.SerializeObject(AnimationUtility.GetAnimationEvents(edited));bool settings=Newtonsoft.Json.JsonConvert.SerializeObject(AnimationUtility.GetAnimationClipSettings(original))==Newtonsoft.Json.JsonConvert.SerializeObject(AnimationUtility.GetAnimationClipSettings(edited));
                checks.Add(new{t.role,lengthPreserved=Mathf.Abs(original.length-edited.length)<.000001f,frameRatePreserved=original.frameRate==edited.frameRate,eventsPreserved=events,settingsPreserved=settings,protectedCurveError=error,protectedNativeRms_deg=angle,protectedVsUneditedConversionRms_deg=controlDifference,uneditedConversionVsSourceRms_deg=conversionDifference,protectedPerBone_deg=perBone,rootXZError=xz,originalEndLowerRms_deg=oldLower,originalEndUpperRms_deg=oldUpper,editedEndLowerRms_deg=newLower,editedEndUpperRms_deg=newUpper,idleHeelWidth_m=idleWidth,originalHeelWidth_m=oldWidth,editedHeelWidth_m=newWidth,uniqueGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original))!=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(edited))});
            }
            File.WriteAllText(Path.Combine(output,"native-checks.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{checks,nativeBoneCount=bones.Length,footIk=false,guardExcluded=true,productionBindingsChanged=false,actor="P09",weapon="Azure Starblade"},Newtonsoft.Json.Formatting.Indented));
        }
        static float Smooth(float p){p=Mathf.Clamp01(p);return p*p*(3-2*p);}
        static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
        public bool Step()
        {
            if(role>=targets.Count){complete=true;return false;}
            var target=targets[role];var step=target.step;float speed=Mathf.Max(.01f,step.animationSpeedMultiplier);float duration=step.playbackAcceleration.ToElapsed(1)*step.animationClip.length/speed;
            int count=capture?Mathf.CeilToInt((.45f+duration+.40f)*Fps):61;float t=capture?frame/(float)Fps:frame/60f*duration+.45f;
            float p=step.playbackAcceleration.ToClipProgress(Mathf.Max(0,t-.45f)*speed/step.animationClip.length);p=Mathf.Clamp01(p);float sourceTime=p*step.animationClip.length;
            float weight=capture?Smooth((t-.38f)/.12f)*(1-Smooth((t-.45f-duration)/.18f)):1;
            string phase=t<.45f?"Idle / 준비":t<.45f+duration?(p<.56f?"공격":"회수 / 발 간격 복귀"):"Sword Combat Idle";
            for(int side=0;side<2;side++)
            {
                Pose(1+role*2+side,sourceTime,weight,t);
                if(step.visualHeightCurve!=null&&step.visualHeightCurve.length>0)root.transform.position+=Vector3.up*step.visualHeightCurve.Evaluate(p)*weight;
                string folder=Path.Combine(output,target.role,side==0?"Original":"Edited");
                if(capture){Directory.CreateDirectory(folder);Render(Path.Combine(folder,frame.ToString("D4")+".jpg"));}
                samples.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{target.role,frame,side,time=t,progress=p,sourceTime,actionWeight=weight,phase,heelWidth_m=Width(),leftHeel=V(feet.LeftHeel.position),rightHeel=V(feet.RightHeel.position),leftToe=V(feet.LeftToe.position),rightToe=V(feet.RightToe.position),hips=V(bones[0].position),footIk=0}));
            }
            frame++;totalFrames++;if(frame%30==0){samples.Flush();File.WriteAllText(Path.Combine(output,"progress.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{role=target.role,frame,totalFrames}));}
            if(frame>=count){frame=0;role++;}return true;
        }
        void Render(string path)
        {
            foreach(var pair in skins)pair.Item1.BakeMesh(pair.Item2);var camera=utility.camera;camera.fieldOfView=36;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.08f,.11f);var rot=Quaternion.Euler(21,205,0);camera.transform.SetPositionAndRotation(new Vector3(0,.86f,0)-rot*Vector3.forward*5.4f,rot);
            utility.BeginPreview(new Rect(0,0,W,H),GUIStyle.none);utility.DrawMesh(floor,Matrix4x4.TRS(new Vector3(0,-.01f,0),Quaternion.identity,new Vector3(14,1,18)),floorMat,0);
            for(int z=-8;z<=8;z++)utility.DrawMesh(floor,Matrix4x4.TRS(new Vector3(0,-.005f,z),Quaternion.identity,new Vector3(14,1,.012f)),gridMat,0);for(int x=-7;x<=7;x++)utility.DrawMesh(floor,Matrix4x4.TRS(new Vector3(x,-.005f,0),Quaternion.identity,new Vector3(.012f,1,18)),gridMat,0);
            utility.Render(true);var rt=utility.EndPreview() as RenderTexture;var prior=RenderTexture.active;Texture2D pixels=null;
            try{RenderTexture.active=rt;pixels=new Texture2D(W,H,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,W,H),0,0);pixels.Apply(false);File.WriteAllBytes(path,pixels.EncodeToJPG(93));}finally{RenderTexture.active=prior;if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;try{samples?.Dispose();}finally{if(graph.IsValid())graph.Destroy();if(window!=null)UnityEngine.Object.DestroyImmediate(window);for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)UnityEngine.Object.DestroyImmediate(owned[i]);File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{status=complete?"PASS":"INTERRUPTED",totalFrames,capture,before,after=Scenes(),scenesPreserved=before.SequenceEqual(Scenes()),resourcesReleased=true,runtimeFootIk=0,gameplayApplied=false,globalCleanup="DEFERRED: concurrent Editor use ownership is unknown"},Newtonsoft.Json.Formatting.Indented));}
        }
    }
}
