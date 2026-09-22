using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Measures authored sole motion at the final model scale, using skin vertices rather than knee/ankle pivots.
public static class MonsterThemeStrideCalibration
{
    public sealed class Gait
    {
        public string clip;
        public float naturalSpeed;
        public string[] feet;
        public float[] footSpeeds;
    }
    public sealed class Result { public Gait walk, run, back; }
    public sealed class ContactProbe
    {
        public string name;
        public Func<Vector3> sample;
    }
    public static ContactProbe[] CreateRuntimeProbes(EnemyActor actor)
    {
        return FindSoles(actor).Select(s=>new ContactProbe{name=s.name,sample=s.WorldPosition}).ToArray();
    }
    private sealed class Sole
    {
        public Transform foot;
        public string name;
        public float initialHeight;
        public int[] indices;
        public Vector3[] vertices;
        public BoneWeight[] weights;
        public Matrix4x4[] bindposes;
        public Transform[] bones;
        public Vector3 WorldPosition()
        {
            Vector3 point=Vector3.zero;
            foreach(int i in indices)
            {
                Vector3 v=vertices[i];BoneWeight w=weights[i];
                point+=bones[w.boneIndex0].TransformPoint(bindposes[w.boneIndex0].MultiplyPoint3x4(v))*w.weight0;
                point+=bones[w.boneIndex1].TransformPoint(bindposes[w.boneIndex1].MultiplyPoint3x4(v))*w.weight1;
                point+=bones[w.boneIndex2].TransformPoint(bindposes[w.boneIndex2].MultiplyPoint3x4(v))*w.weight2;
                point+=bones[w.boneIndex3].TransformPoint(bindposes[w.boneIndex3].MultiplyPoint3x4(v))*w.weight3;
            }
            return point/indices.Length;
        }
    }

    public static EnemyTurnFootPlanting.Binding[] CreateTurnBindings(EnemyDefinition definition)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var root=(GameObject)PrefabUtility.InstantiatePrefab(definition.ActorPrefab.gameObject,scene);
            root.SetActive(false);
            var actor=root.GetComponent<EnemyActor>();var profile=definition.AnimationProfile;
            profile.Idle.SampleAnimation(actor.Animator.gameObject,0f);
            var soles=FindSoles(actor);
            var bindings=soles.Select(s=>new EnemyTurnFootPlanting.Binding {
                endPath=AnimationUtility.CalculateTransformPath(s.foot,actor.transform),
                soleInFoot=s.foot.InverseTransformPoint(s.WorldPosition())}).ToArray();
            foreach(bool left in new[]{true,false})
            {
                var clip=Enumerable.Range(0,profile.OptionalClipCount).Select(profile.GetOptionalClip).First(c=>c.name==(left?"Turn90Left":"Turn90Right"));
                const int count=90;var heights=new float[count+1][];
                for(int i=0;i<=count;i++)
                {
                    clip.SampleAnimation(actor.Animator.gameObject,clip.length*i/count);
                    heights[i]=soles.Select(s=>s.WorldPosition().y).ToArray();
                }
                for(int foot=0;foot<soles.Count;foot++)
                {
                    float low=heights.Min(h=>h[foot]);float lift=Mathf.Max(.02f,heights.Max(h=>h[foot])-low);
                    var keys=Enumerable.Range(0,count+1).Select(i=>new Keyframe(i/(float)count,1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(low+lift*.12f,low+lift*.38f,heights[i][foot])))).ToArray();
                    var curve=new AnimationCurve(keys);
                    for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
                    if(left)bindings[foot].leftContact=curve;else bindings[foot].rightContact=curve;
                }
            }
            return bindings;
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }

    public static Result Measure(EnemyDefinition definition, AnimationClip walk, AnimationClip run, AnimationClip back)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var root=(GameObject)PrefabUtility.InstantiatePrefab(definition.ActorPrefab.gameObject,scene);
            root.SetActive(false);
            var actor=root.GetComponent<EnemyActor>();
            var walking=Sample(actor,walk,false);
            return new Result {walk=walking,run=run==walk?walking:Sample(actor,run,false),back=Sample(actor,back,true)};
        }
        finally {EditorSceneManager.ClosePreviewScene(scene);}
    }

    private static Gait Sample(EnemyActor actor, AnimationClip clip, bool backwards)
    {
        const int count=120;
        clip.SampleAnimation(actor.Animator.gameObject,0f);
        var soles=FindSoles(actor);
        if(soles.Count<2)throw new InvalidOperationException(actor.Definition.EnemyId+" needs explicit supporting-foot markers");
        var frames=new Vector3[count+1][];
        for(int i=0;i<=count;i++)
        {
            clip.SampleAnimation(actor.Animator.gameObject,clip.length*i/count);
            frames[i]=soles.Select(s=>actor.transform.InverseTransformPoint(s.WorldPosition())).ToArray();
        }
        var speeds=new List<float>();var names=new List<string>();float dt=clip.length/count;
        for(int foot=0;foot<soles.Count;foot++)
        {
            float low=frames.Min(f=>f[foot].y);var planted=new List<float>();
            for(int i=2;i<count-1;i++)
            {
                Vector3 velocity=(frames[i+1][foot]-frames[i-1][foot])/(2f*dt);
                float speed=velocity.z*(backwards?1f:-1f);
                // Reject lift/landing and toe roll; infer travel only during the low, vertically steady stance.
                if(frames[i][foot].y<low+.035f && Mathf.Abs(velocity.y)<.2f && speed>.05f)planted.Add(speed);
            }
            if(planted.Count<4)continue;
            speeds.Add(Median(planted));names.Add(soles[foot].name);
        }
        if(speeds.Count<2)throw new InvalidOperationException(actor.Definition.EnemyId+" insufficient foot contact samples: "+clip.name);
        return new Gait {clip=clip.name,naturalSpeed=Median(speeds),feet=names.ToArray(),footSpeeds=speeds.ToArray()};
    }

    private static List<Sole> FindSoles(EnemyActor actor)
    {
        var meshes=actor.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var feet=meshes.SelectMany(m=>m.bones).Where(b=>b!=null).Distinct().Where(b=>IsFoot(actor.Definition.EnemyId,b)).ToArray();
        var result=new List<Sole>();
        foreach(var renderer in meshes)
        {
            var mesh=renderer.sharedMesh;var weights=mesh.boneWeights;var bones=renderer.bones;
            var baked=new Mesh();
            try
            {
                renderer.BakeMesh(baked);var pose=baked.vertices;
                foreach(var foot in feet)
                {
                    var indices=new List<int>();float low=float.MaxValue;
                    for(int i=0;i<pose.Length;i++)
                    {
                        var w=weights[i];float influence=0f;
                        if(bones[w.boneIndex0]==foot || bones[w.boneIndex0].IsChildOf(foot))influence+=w.weight0;
                        if(bones[w.boneIndex1]==foot || bones[w.boneIndex1].IsChildOf(foot))influence+=w.weight1;
                        if(bones[w.boneIndex2]==foot || bones[w.boneIndex2].IsChildOf(foot))influence+=w.weight2;
                        if(bones[w.boneIndex3]==foot || bones[w.boneIndex3].IsChildOf(foot))influence+=w.weight3;
                        if(influence<.8f)continue;
                        low=Mathf.Min(low,renderer.transform.TransformPoint(pose[i]).y);indices.Add(i);
                    }
                    indices.RemoveAll(i=>renderer.transform.TransformPoint(pose[i]).y>low+.015f);
                    if(indices.Count>0)result.Add(new Sole {foot=foot,name=foot.name,initialHeight=low,indices=indices.ToArray(),vertices=mesh.vertices,weights=weights,bindposes=mesh.bindposes,bones=bones});
                }
            }
            finally {Object.DestroyImmediate(baked);}
        }
        if(result.Count>0)
        {
            float ground=result.Min(s=>s.initialHeight);
            result.RemoveAll(s=>s.initialHeight>ground+.2f); // Raised hands on bipeds are not supporting feet.
        }
        return result;
    }

    private static bool IsFoot(string id,Transform bone)
    {
        string name=bone.name;
        if(name.EndsWith(" Foot") || name.EndsWith(" Hand"))return true;
        bool IsDistal(Transform t)=>(t.name.Contains("Leg") || id.Contains("Kupolobrach") && t.name.Contains("Arm"))
            && (t.name.EndsWith("3") || t.name.EndsWith("4"));
        return IsDistal(bone) && !bone.GetComponentsInChildren<Transform>(true).Any(t=>t!=bone && IsDistal(t));
    }
    private static float Median(List<float> values)
    {
        var sorted=values.OrderBy(x=>x).ToArray();int middle=sorted.Length/2;
        return sorted.Length%2==0?(sorted[middle-1]+sorted[middle])*.5f:sorted[middle];
    }
}
