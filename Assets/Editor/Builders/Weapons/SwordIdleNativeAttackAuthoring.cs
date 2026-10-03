using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Unity.Collections;

// Forward Humanoid stream capture: no inverse pose reconstruction or runtime IK.
public static class SwordIdleNativeAttackAuthoring
{
    struct CaptureJob : IAnimationJob
    {
        public NativeArray<MuscleHandle> handles;
        public NativeArray<float> values;
        public void ProcessRootMotion(AnimationStream stream) { }
        public void ProcessAnimation(AnimationStream stream)
        {
            var human=stream.AsHuman();
            for(int i=0;i<handles.Length;i++)values[i]=human.GetMuscle(handles[i]);
            int n=handles.Length;var p=human.bodyLocalPosition;var q=human.bodyLocalRotation;
            values[n]=p.x;values[n+1]=p.y;values[n+2]=p.z;
            values[n+3]=q.x;values[n+4]=q.y;values[n+5]=q.z;values[n+6]=q.w;
        }
    }
    static string BindingName(string n)
    {
        if(n.Contains(".")||(!n.Contains("Stretched")&&!n.Contains("Spread")))return n;
        var tokens=n.Split(' ');return tokens[0]+"Hand."+tokens[1]+"."+string.Join(" ",tokens.Skip(2));
    }
    public static string Build(string output)
    {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var window=ScriptableObject.CreateInstance<ModelAnimationPreviewWindow>();
        var owned=new List<AnimationClip>();var graph=default(PlayableGraph);
        var hs=new MuscleHandle[MuscleHandle.muscleHandleCount];MuscleHandle.GetMuscleHandles(hs);
        var handles=new NativeArray<MuscleHandle>(hs,Allocator.Persistent);
        var values=new NativeArray<float>(hs.Length+7,Allocator.Persistent);
        try
        {
            typeof(ModelAnimationPreviewWindow).GetField("selectedModelPrefab",flags).SetValue(window,AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("4f97a974eab7ea34abcd80e33c41133d")));
            typeof(ModelAnimationPreviewWindow).GetMethod("RebuildPreviewInstance",flags).Invoke(window,null);
            var animator=(Animator)typeof(ModelAnimationPreviewWindow).GetMethod("FindPreviewAnimator",flags).Invoke(window,null);
            var root=(GameObject)typeof(ModelAnimationPreviewWindow).GetField("previewInstance",flags).GetValue(window);
            var names=hs.Select(h=>BindingName(h.name)).Concat(new[]{"RootT.x","RootT.y","RootT.z","RootQ.x","RootQ.y","RootQ.z","RootQ.w"}).ToArray();
            var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(SwordIdleAttackCopyBuilder.IdlePath);
            var targets=SwordIdleAttackCopyBuilder.Targets();var sources=new[]{idle}.Concat(targets.Select(t=>t.step.animationClip)).ToArray();
            var cache=new List<UnityEngine.Object>();var summaries=new List<object>();AnimationClip bakedIdle=null;
            for(int c=0;c<sources.Length;c++)
            {
                var source=sources[c];var copy=UnityEngine.Object.Instantiate(source);owned.Add(copy);
                var sample=UnityEngine.Object.Instantiate(source);owned.Add(sample);var sampleSettings=AnimationUtility.GetAnimationClipSettings(sample);sampleSettings.keepOriginalOrientation=true;sampleSettings.keepOriginalPositionXZ=true;if(c>0)sampleSettings.loopTime=false;AnimationUtility.SetAnimationClipSettings(sample,sampleSettings);
                graph=PlayableGraph.Create("Owned forward stream reference");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                animator.enabled=true;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var clip=AnimationClipPlayable.Create(graph,sample);clip.SetSpeed(0);clip.SetApplyFootIK(false);clip.SetApplyPlayableIK(false);
                var job=AnimationScriptPlayable.Create(graph,new CaptureJob{handles=handles,values=values},1);graph.Connect(clip,0,job,0);job.SetInputWeight(0,1);
                AnimationPlayableOutput.Create(graph,"Forward stream",animator).SetSourcePlayable(job);graph.Play();
                var times=new SortedSet<float>{0,source.length};for(int f=1;f<Mathf.CeilToInt(source.length*360);f++)times.Add(f/360f);
                var bindings=AnimationUtility.GetCurveBindings(source).Where(b=>b.type==typeof(Animator)&&b.path=="").ToDictionary(b=>b.propertyName,b=>b);
                foreach(var b in bindings.Values)foreach(var k in AnimationUtility.GetEditorCurve(source,b).keys)times.Add(k.time);
                var lists=names.Select(n=>new List<Keyframe>()).ToArray();
                foreach(float t in times)
                {
                    root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);clip.SetTime(t);graph.Evaluate(0);
                    for(int i=0;i<names.Length;i++)lists[i].Add(new Keyframe(t,values[i]));
                }
                graph.Destroy();var bs=new List<EditorCurveBinding>();var curves=new List<AnimationCurve>();
                for(int n=0;n<names.Length;n++)
                {
                    if(!bindings.ContainsKey(names[n]))continue;
                    var keys=lists[n].ToArray();for(int i=0;i<keys.Length;i++)
                    {
                        float slope=0;if(i==0&&keys.Length>1)slope=(keys[1].value-keys[0].value)/(keys[1].time-keys[0].time);
                        else if(i==keys.Length-1&&i>0)slope=(keys[i].value-keys[i-1].value)/(keys[i].time-keys[i-1].time);
                        else if(i>0)slope=(keys[i+1].value-keys[i-1].value)/(keys[i+1].time-keys[i-1].time);
                        keys[i].inTangent=keys[i].outTangent=slope;
                    }
                    bs.Add(bindings[names[n]]);curves.Add(new AnimationCurve(keys));
                }
                AnimationUtility.SetEditorCurves(copy,bs.ToArray(),curves.ToArray());
                var settings=AnimationUtility.GetAnimationClipSettings(copy);settings.keepOriginalOrientation=true;settings.keepOriginalPositionY=true;settings.keepOriginalPositionXZ=true;settings.loopTime=false;AnimationUtility.SetAnimationClipSettings(copy,settings);
                if(c==0)bakedIdle=copy;else SwordIdleNativeCurveAuthoring.Apply(source,copy,bakedIdle,targets[c-1],output);
                summaries.Add(new{source=source.name,count=times.Count,names,first=lists.Select(x=>x[0].value).ToArray(),humanScale=animator.humanScale});
                var raw=UnityEngine.Object.Instantiate(source);owned.Add(raw);
                var rs=AnimationUtility.GetAnimationClipSettings(raw);rs.keepOriginalOrientation=true;rs.keepOriginalPositionXZ=true;if(c>0)rs.loopTime=false;AnimationUtility.SetAnimationClipSettings(raw,rs);
                if(c==0)cache.Add(raw);else{cache.Add(raw);cache.Add(copy);}
            }
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(cache.ToArray(),Path.Combine(output,"forward-candidates.objects"),false);
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new UnityEngine.Object[]{bakedIdle},Path.Combine(output,"idle-forward.objects"),false);
            File.WriteAllText(Path.Combine(output,"forward-stream.json"),Newtonsoft.Json.JsonConvert.SerializeObject(summaries,Newtonsoft.Json.Formatting.Indented));
            return SwordIdleNativeClearanceAuthoring.Build(output,Path.Combine(output,"forward-candidates.objects"));
        }
        finally{if(graph.IsValid())graph.Destroy();handles.Dispose();values.Dispose();UnityEngine.Object.DestroyImmediate(window);foreach(var clip in owned)UnityEngine.Object.DestroyImmediate(clip);}
    }
}

public static class SwordIdleNativeCurveAuthoring
{
    static float Smooth(float x){x=Mathf.Clamp01(x);return x*x*x*(10+x*(-15+6*x));}
    static Quaternion SignedSlerp(Quaternion a,Quaternion b,float t)
    {
        float dot=Mathf.Clamp(Quaternion.Dot(a,b),-1,1),angle=Mathf.Acos(dot),s=Mathf.Sin(angle);
        if(Mathf.Abs(s)<.00001f)return Quaternion.LerpUnclamped(a,b,t);
        float x=Mathf.Sin((1-t)*angle)/s,y=Mathf.Sin(t*angle)/s;
        return new Quaternion(a.x*x+b.x*y,a.y*x+b.y*y,a.z*x+b.z*y,a.w*x+b.w*y).normalized;
    }
    public static void Apply(AnimationClip raw,AnimationClip copy,AnimationClip idle,SwordIdleAttackCopyBuilder.Target target,string output)
    {

        var bindings=AnimationUtility.GetCurveBindings(copy).Where(b=>b.type==typeof(Animator)&&b.path=="").ToArray();
        var src=bindings.ToDictionary(b=>b.propertyName,b=>AnimationUtility.GetEditorCurve(copy,b));
        var dst=AnimationUtility.GetCurveBindings(idle).Where(b=>b.type==typeof(Animator)&&b.path=="").ToDictionary(b=>b.propertyName,b=>AnimationUtility.GetEditorCurve(idle,b));
        var phases=target.step.attackPhases.Select(p=>new[]{p.SafeStart,p.SafeEnd}).Concat(target.step.trailPhases.Select(p=>new[]{p.SafeStart,p.SafeEnd})).ToArray();
        float entry=Mathf.Min(.30f,phases.Min(p=>p[0])*copy.length-.035f),recovery=Mathf.Max(phases.Max(p=>p[1])*copy.length+.045f,copy.length*.56f);
        float finish=target.role=="Combo2"?1.10f:copy.length;
        Quaternion Read(Dictionary<string,AnimationCurve> cs,string pre,float t)=>new Quaternion(cs[pre+"x"].Evaluate(t),cs[pre+"y"].Evaluate(t),cs[pre+"z"].Evaluate(t),cs[pre+"w"].Evaluate(t)).normalized;
        var continuous=new Dictionary<string,AnimationCurve>(src);
        foreach(var pre in new[]{"RootQ."})
        {
            var components=new[]{"x","y","z","w"};var keys=components.Select(n=>src[pre+n].keys).ToArray();Quaternion previous=Read(dst,pre,0);
            for(int k=0;k<keys[0].Length;k++)
            {
                var q=new Quaternion(keys[0][k].value,keys[1][k].value,keys[2][k].value,keys[3][k].value).normalized;float sign=Quaternion.Dot(previous,q)<0?-1:1;
                for(int n=0;n<4;n++){keys[n][k].value*=sign;keys[n][k].inTangent*=sign;keys[n][k].outTangent*=sign;}previous=new Quaternion(q.x*sign,q.y*sign,q.z*sign,q.w*sign);
            }
            for(int n=0;n<4;n++)continuous[pre+components[n]]=new AnimationCurve(keys[n]);
        }
        var changed=new List<EditorCurveBinding>();var curves=new List<AnimationCurve>();
        foreach(var b in bindings)
        {
            string name=b.propertyName;if(!dst.ContainsKey(name))continue;var old=src[name];bool heavy=target.role=="Heavy"&&name=="Right Arm Twist In-Out";
            Func<float,float> value=t=>
            {
                float a=Smooth(t/entry),z=Smooth((t-recovery)/(finish-recovery));
                if(name.Contains("Q.")&&!name.Contains("TDOF"))
                {
                    string pre=name.Substring(0,name.LastIndexOf('.')+1);var original=Read(continuous,pre,t);var goal=Read(dst,pre,0);var returnGoal=goal;if(Quaternion.Dot(Read(continuous,pre,recovery),returnGoal)<0)returnGoal=new Quaternion(-goal.x,-goal.y,-goal.z,-goal.w);
                    var q=pre=="RootQ."?SignedSlerp(SignedSlerp(goal,original,a),returnGoal,z):Quaternion.Slerp(Quaternion.Slerp(goal,original,a),goal,z);if(Quaternion.Dot(q,original)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
                    return name.EndsWith("x")?q.x:name.EndsWith("y")?q.y:name.EndsWith("z")?q.z:q.w;
                }
                float v=Mathf.Lerp(Mathf.Lerp(dst[name].Evaluate(0),old.Evaluate(t),a),dst[name].Evaluate(0),z);
                if(heavy)v=Mathf.Lerp(v,Mathf.Min(v,1.95f),Smooth((t-.36f)/.06f)*(1-Smooth((t-.54f)/.08f)));
                return v;
            };
            var ts=new SortedSet<float>(old.keys.Select(k=>k.time)){0,copy.length,entry,recovery,finish};
            for(int k=0;k<Mathf.CeilToInt(copy.length*360);k++){float t=k/360f;if(t<entry||t>recovery||(heavy&&t>=.35f&&t<=.64f))ts.Add(t);}
            var keys=ts.Select(t=>
            {
                int i=Array.FindIndex(old.keys,k=>k.time==t);bool unchanged=t>=entry&&t<=recovery&&!(heavy&&t>=.35f&&t<=.64f);
                if(unchanged&&i>=0)return old.keys[i];
                float x=Mathf.Max(0,t-.0001f),y=Mathf.Min(copy.length,t+.0001f),slope=(value(y)-value(x))/(y-x);
                return new Keyframe(t,value(t),slope,slope);
            }).ToArray();
            changed.Add(b);curves.Add(new AnimationCurve(keys));
        }
        // Equivalent quaternion signs must stay coherent across modified and preserved key boundaries.
        foreach(var pre in new[]{"RootQ."})
        {
            var indices=new[]{"x","y","z","w"}.Select(n=>changed.FindIndex(b=>b.propertyName==pre+n)).ToArray();if(indices.Any(i=>i<0))continue;
            var keys=indices.Select(i=>curves[i].keys).ToArray();var previous=Quaternion.identity;
            for(int k=0;k<keys[0].Length;k++){var q=new Quaternion(keys[0][k].value,keys[1][k].value,keys[2][k].value,keys[3][k].value).normalized;float sign=k>0&&Quaternion.Dot(previous,q)<0?-1:1;for(int n=0;n<4;n++){keys[n][k].value*=sign;keys[n][k].inTangent*=sign;keys[n][k].outTangent*=sign;}previous=new Quaternion(q.x*sign,q.y*sign,q.z*sign,q.w*sign);}
            for(int n=0;n<4;n++)curves[indices[n]].keys=keys[n];
        }
        AnimationUtility.SetEditorCurves(copy,changed.ToArray(),curves.ToArray());
        File.WriteAllText(Path.Combine(output,"fk-authoring-"+target.role+".json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{target.role,entry,recovery,finish,offlineIk=false,runtimeIk=false}));
    }
}


// Offline body translation authoring. Preserves airborne motion and all local joint curves.
public static class SwordIdleNativeClearanceAuthoring
{
    public static string Build(string output,string cache)
    {
        var objects=UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(cache);
        var window=ScriptableObject.CreateInstance<ModelAnimationPreviewWindow>();
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;var graph=default(PlayableGraph);
        try
        {
            typeof(ModelAnimationPreviewWindow).GetField("selectedModelPrefab",flags).SetValue(window,AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("4f97a974eab7ea34abcd80e33c41133d")));
            typeof(ModelAnimationPreviewWindow).GetMethod("RebuildPreviewInstance",flags).Invoke(window,null);
            var animator=(Animator)typeof(ModelAnimationPreviewWindow).GetMethod("FindPreviewAnimator",flags).Invoke(window,null);
            var root=(GameObject)typeof(ModelAnimationPreviewWindow).GetField("previewInstance",flags).GetValue(window);
            animator.enabled=true;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var feet=animator.GetComponent<HumanoidFootContactRig>();if(feet==null||!feet.IsConfigured)throw new Exception("Contact markers missing");
            var markers=new[]{feet.LeftHeel,feet.RightHeel,feet.LeftToe,feet.RightToe};
            var clips=objects.OfType<AnimationClip>().ToArray();var targets=SwordIdleAttackCopyBuilder.Targets();var records=new List<object>();
            for(int c=2;c<clips.Length;c+=2)
            {
                var clip=clips[c];var target=targets[(c-2)/2];
                var binding=AnimationUtility.GetCurveBindings(clip).Single(b=>b.type==typeof(Animator)&&b.path==""&&b.propertyName=="RootT.y");
                var old=AnimationUtility.GetEditorCurve(clip,binding);int count=Mathf.CeilToInt(clip.length*720)+1;
                var times=Enumerable.Range(0,count).Select(f=>Mathf.Min(f/720f,clip.length)).ToArray();var required=new float[count];
                graph=PlayableGraph.Create("Owned clearance capture");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable=AnimationClipPlayable.Create(graph,clip);playable.SetSpeed(0);playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);
                AnimationPlayableOutput.Create(graph,"Pose",animator).SetSourcePlayable(playable);graph.Play();
                for(int f=0;f<count;f++)
                {
                    root.transform.SetPositionAndRotation(Vector3.up*.022f,Quaternion.identity);playable.SetTime(times[f]);graph.Evaluate(0);
                    float height=target.step.visualHeightCurve?.Evaluate(times[f]/clip.length)??0;
                    float y=markers.Min(m=>m.position.y)+height;
                    if(float.IsNaN(y)||float.IsInfinity(y))throw new Exception("Invalid contact sample");
                    required[f]=Mathf.Max(0,-y+.002f)/animator.humanScale;
                }
                graph.Destroy();
                // A dilated envelope followed by a raised cosine average anticipates contact without sharp max() cusps.
                int radius=18;var envelope=new float[count];var smooth=new float[count];
                for(int f=0;f<count;f++)for(int n=Mathf.Max(0,f-2*radius);n<=Mathf.Min(count-1,f+2*radius);n++)envelope[f]=Mathf.Max(envelope[f],required[n]);
                for(int f=0;f<count;f++){float sum=0,weight=0;for(int n=-radius;n<=radius;n++){float w=.5f+.5f*Mathf.Cos(n*Mathf.PI/(radius+1));sum+=envelope[Mathf.Clamp(f+n,0,count-1)]*w;weight+=w;}smooth[f]=sum/weight;}
                for(int f=0;f<count;f++){float p=Mathf.Clamp01(times[f]/.05f),ramp=p*p*p*(10+p*(-15+6*p));smooth[f]=required[f]+(smooth[f]-required[f])*ramp;}
                if(target.role=="Combo4")for(int f=0;f<count;f++){float p=times[f]/.211f;if(p>0 && p<1)smooth[f]+=.055f*Mathf.Pow(Mathf.Sin(p*Mathf.PI),4)/animator.humanScale;}
                var correction=new AnimationCurve(times.Select((t,f)=>new Keyframe(t,smooth[f])).ToArray());
                var ck=correction.keys;for(int f=0;f<count;f++){int a=Mathf.Max(0,f-1),z=Mathf.Min(count-1,f+1);float d=(ck[z].value-ck[a].value)/(ck[z].time-ck[a].time);ck[f].inTangent=ck[f].outTangent=d;}correction.keys=ck;
                var ts=new SortedSet<float>(times.Concat(old.keys.Select(k=>k.time)));
                var keys=ts.Select(t=>{float x=Mathf.Max(0,t-.0001f),z=Mathf.Min(clip.length,t+.0001f);float slope=(old.Evaluate(z)-old.Evaluate(x)+correction.Evaluate(z)-correction.Evaluate(x))/(z-x);return new Keyframe(t,old.Evaluate(t)+correction.Evaluate(t),slope,slope);}).ToArray();
                AnimationUtility.SetEditorCurve(clip,binding,new AnimationCurve(keys));
                records.Add(new{target.role,maxBodyLiftMeters=smooth.Max()*animator.humanScale,startLift=smooth[0]*animator.humanScale,endLift=smooth[count-1]*animator.humanScale,localJointsChanged=false,runtimeIk=0,airbornePreserved=true});
            }
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(clips,Path.Combine(output,"candidate-native.objects"),false);
            File.WriteAllText(Path.Combine(output,"clearance-authoring.json"),Newtonsoft.Json.JsonConvert.SerializeObject(records,Newtonsoft.Json.Formatting.Indented));
            return Path.Combine(output,"candidate-native.objects");
        }
        finally{if(graph.IsValid())graph.Destroy();UnityEngine.Object.DestroyImmediate(window);foreach(var o in objects)if(o!=null)UnityEngine.Object.DestroyImmediate(o);}
    }
}
