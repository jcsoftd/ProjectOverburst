using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Verifies the persisted clip against native original poses, including between-frame samples.
public static class MonsterWeakAttackRuntimeClipVerifier
{
    public static string Run(GameObject prefab,AnimationClip source,AnimationClip persisted,Vector3 scale,JObject measured,string outputDirectory)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
            throw new InvalidOperationException("실행 클립 검사는 유휴 EditMode에서 실행합니다.");
        string workspace=Directory.GetParent(Application.dataPath).Parent.FullName;
        string allowed=Path.GetFullPath(Path.Combine(workspace,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        outputDirectory=Path.GetFullPath(outputDirectory);
        if(!outputDirectory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||prefab==null||persisted==null
            ||!EditorUtility.IsPersistent(persisted)||!AssetDatabase.GetAssetPath(persisted).StartsWith(MonsterWeakAttackRuntimeClipWriter.Root+"/",StringComparison.Ordinal))
            throw new ArgumentException("저장된 실행 클립/산출 경로가 아닙니다.");
        MonsterWeakAttackRuntimeClipWriter.ValidateSource(source,(string)measured["selectionKey"],measured);
        var expectedScale=measured["modelScale"].Values<float>().ToArray();
        if(expectedScale.Length!=3||Vector3.Distance(scale,new Vector3(expectedScale[0],expectedScale[1],expectedScale[2]))>.00001f
            ||!Finite(scale.x)||!Finite(scale.y)||!Finite(scale.z)||scale.x<=0||scale.y<=0||scale.z<=0)
            throw new ArgumentException("측정 모델 크기와 다릅니다.");
        var scene=EditorSceneManager.NewPreviewScene();var graph=default(PlayableGraph);
        float maxNativeFoot=0,maxNativeY=0,maxNativeUniform=0,maxBetweenFoot=0,maxBetweenY=0,maxBetweenUniform=0;
        float outsideInfluence=0;int nativeCount=0,betweenCount=0;
        try
        {
            var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);model.transform.position=Vector3.zero;model.transform.localScale=scale;
            foreach(var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))if(behaviour!=null)behaviour.enabled=false;
            foreach(var legacy in model.GetComponentsInChildren<Animation>(true))legacy.enabled=false;
            var animator=model.GetComponentInChildren<Animator>(true)??model.AddComponent<Animator>();
            animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.enabled=true;
            var rootPosition=animator.transform.localPosition;var rootScale=animator.transform.localScale;var rootRotation=animator.transform.localRotation;
            var bones=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s=>s.bones).Where(t=>t!=null).Distinct().ToArray();
            var pelvis=animator.transform.Find((string)measured["poseRootBonePath"]);
            var supports=measured["supportBones"].Values<string>().Select(path=>animator.transform.Find(path)).ToArray();
            if(pelvis==null||supports.Length!=2||supports.Any(t=>t==null)||bones.Length<2)
                throw new InvalidOperationException("측정한 본 경로가 실제 모델에 없습니다.");
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var weights=skin.sharedMesh.GetAllBoneWeights();
                try {foreach(var weight in weights){var bone=skin.bones[weight.boneIndex];if(bone==null||bone!=pelvis&&!bone.IsChildOf(pelvis))outsideInfluence=Mathf.Max(outsideInfluence,weight.weight);}}
                finally {weights.Dispose();}
            }
            AnimationClipPlayable playable=default;
            void Select(AnimationClip clip)
            {
                if(graph.IsValid())graph.Destroy();
                graph=PlayableGraph.Create("Persisted V3 attack pose verification");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);
                var channel=AnimationPlayableOutput.Create(graph,"Pose",animator);channel.SetSourcePlayable(playable);graph.Play();
            }
            void Pose(float seconds)
            {
                playable.SetTime(Mathf.Min(seconds,source.length-.0001f));graph.Evaluate(0);
                animator.transform.localPosition=rootPosition;animator.transform.localScale=rootScale;animator.transform.localRotation=rootRotation;
            }
            Select(source);Pose(0);Vector3 baseline=(supports[0].position+supports[1].position)*.5f;
            int last=Mathf.RoundToInt(source.length*source.frameRate);
            void Compare(float seconds,bool native)
            {
                Select(source);Pose(seconds);var original=bones.Select(t=>t.position).ToArray();
                var shift=(supports[0].position+supports[1].position)*.5f-baseline;shift.y=0;
                Select(persisted);Pose(seconds);var drift=(supports[0].position+supports[1].position)*.5f-baseline;drift.y=0;
                float y=0,uniform=0;
                for(int i=0;i<bones.Length;i++)
                {
                    y=Mathf.Max(y,Mathf.Abs(bones[i].position.y-original[i].y));
                    var applied=bones[i]==pelvis||bones[i].IsChildOf(pelvis)?shift:Vector3.zero;
                    uniform=Mathf.Max(uniform,(bones[i].position-(original[i]-applied)).magnitude);
                }
                if(native){nativeCount++;maxNativeFoot=Mathf.Max(maxNativeFoot,drift.magnitude);maxNativeY=Mathf.Max(maxNativeY,y);maxNativeUniform=Mathf.Max(maxNativeUniform,uniform);}
                else {betweenCount++;maxBetweenFoot=Mathf.Max(maxBetweenFoot,drift.magnitude);maxBetweenY=Mathf.Max(maxBetweenY,y);maxBetweenUniform=Mathf.Max(maxBetweenUniform,uniform);}
            }
            for(int frame=0;frame<=last;frame++)Compare(frame/source.frameRate,true);
            for(int frame=0;frame<last;frame++)for(int eighth=1;eighth<=7;eighth++)Compare((frame+eighth*.125f)/source.frameRate,false);
        }
        finally {if(graph.IsValid())graph.Destroy();if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);}
        bool pass=outsideInfluence<=.00001f&&maxNativeFoot<.002f&&maxNativeY<.001f&&maxNativeUniform<.002f
            &&maxBetweenFoot<.003f&&maxBetweenY<.001f&&maxBetweenUniform<.003f
            &&source.frameRate==persisted.frameRate&&Mathf.Abs(source.length-persisted.length)<.0001f;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(persisted,out string guid,out long id);
        Directory.CreateDirectory(outputDirectory);string result=Path.Combine(outputDirectory,"persisted-runtime-clip-results.json");
        File.WriteAllText(result,new JObject{{"status",pass?"PASS_PERSISTED_NATIVE_RUNTIME_CLIP":"FAIL"},{"selectionKey",measured["selectionKey"]},
            {"runtimeClipPath",AssetDatabase.GetAssetPath(persisted)},{"runtimeGuid",guid},{"runtimeLocalId",id},
            {"sourceFps",source.frameRate},{"runtimeFps",persisted.frameRate},{"sourceSeconds",source.length},{"runtimeSeconds",persisted.length},
            {"nativeSamples",nativeCount},{"betweenFrameSamples",betweenCount},{"maximumNativeSupportDriftMeters",maxNativeFoot},
            {"maximumNativeYDifferenceMeters",maxNativeY},{"maximumNativeUniformErrorMeters",maxNativeUniform},
            {"maximumBetweenSupportDriftMeters",maxBetweenFoot},{"maximumBetweenYDifferenceMeters",maxBetweenY},
            {"maximumBetweenUniformErrorMeters",maxBetweenUniform},{"maximumSkinInfluenceOutsideCounterSubtree",outsideInfluence},
            {"gameActorBindingsApplied",false},{"newIndividualSoundsApplied",false},{"actualAnimatorPlayerLoop","NOT_RUN"}}.ToString());
        if(!pass)throw new InvalidOperationException("저장한 실행 클립의 native 자세가 측정 범위를 벗어났습니다.");
        return result;
    }
    static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
}
