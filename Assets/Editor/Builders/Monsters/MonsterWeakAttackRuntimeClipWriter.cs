using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Creates a local supplier-derived clip from previously measured and validated curves.
// Actor/ability/Animator/SFX bindings are applied by the roster builder separately.
public static class MonsterWeakAttackRuntimeClipWriter
{
    public const string Root="Assets/ProjectOverburst/Resources/Enemies/Themes/Animations/RuntimeV3";
    public static AnimationClip ApplyFootCounter(AnimationClip source,string selectionKey,JObject measured,out bool created)
    {
        created=false;
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
            throw new InvalidOperationException("실행 클립은 유휴 EditMode에서 저장합니다.");
        ValidateSource(source,selectionKey,measured);
        var keys=(JArray)measured["runtimeCurveKeys"];
        var curves=new[]{new AnimationCurve(),new AnimationCurve(),new AnimationCurve()};
        float previous=-1;
        float sampling=(float)measured["runtimeCurveSamplingFps"];
        foreach(JObject row in keys)
        {
            float time=(float)row["seconds"];var position=row["parentLocalPosition"] as JArray;
            if(!Finite(time)||time<=previous||time<0||time>source.length+.0001f||position==null||position.Count!=3
                ||Mathf.Abs(time*sampling-Mathf.Round(time*sampling))>.002f)
                throw new ArgumentException("보정 키 시점/축이 유효하지 않습니다.");
            for(int axis=0;axis<3;axis++)
            {
                float value=(float)position[axis];if(!Finite(value))throw new ArgumentException("보정 좌표가 유효하지 않습니다.");
                curves[axis].AddKey(time,value);
            }
            previous=time;
        }
        if(Mathf.Abs(curves[0].keys[0].time)>.0001f||Mathf.Abs(previous-source.length)>.0001f)
            throw new ArgumentException("보정 곡선은 원본 전체 길이를 포함해야 합니다.");
        for(int axis=0;axis<3;axis++)for(int key=0;key<curves[axis].length;key++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curves[axis],key,AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curves[axis],key,AnimationUtility.TangentMode.Linear);
        }
        string name="V3_"+selectionKey+"_Stationary",path=Root+"/"+name+".anim";
        var candidate=UnityEngine.Object.Instantiate(source);candidate.name=name;
        var folders=new List<string>();
        try
        {
            string bone=(string)measured["poseRootBonePath"];
            for(int axis=0;axis<3;axis++)AnimationUtility.SetEditorCurve(candidate,
                EditorCurveBinding.FloatCurve(bone,typeof(Transform),"m_LocalPosition."+"xyz"[axis]),curves[axis]);
            if(candidate.frameRate!=source.frameRate||Mathf.Abs(candidate.length-source.length)>.0001f)
                throw new InvalidOperationException("보정 중 원본 FPS/길이가 바뀌었습니다.");
            var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(AssetDatabase.LoadMainAssetAtPath(path)!=null&&existing==null)throw new InvalidOperationException("대상 경로에 다른 자산이 있습니다.");
            if(existing!=null)
            {
                if(EditorJsonUtility.ToJson(existing)!=EditorJsonUtility.ToJson(candidate))
                    throw new InvalidOperationException("기존 실행 클립과 새 보정이 다릅니다. 기존 자산을 자동 덮어쓰지 않습니다.");
                return existing;
            }
            EnsureFolder(Root,folders);
            AssetDatabase.CreateAsset(candidate,path);created=true;
            EditorUtility.SetDirty(candidate);AssetDatabase.SaveAssetIfDirty(candidate);
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path)??throw new InvalidOperationException("저장한 실행 클립을 읽을 수 없습니다.");
        }
        catch
        {
            if(created){AssetDatabase.DeleteAsset(path);created=false;}
            for(int i=folders.Count-1;i>=0;i--)
                if(Directory.Exists(folders[i])&&Directory.GetFileSystemEntries(folders[i]).Length==0)AssetDatabase.DeleteAsset(folders[i]);
            throw;
        }
        finally {if(candidate!=null&&!EditorUtility.IsPersistent(candidate))UnityEngine.Object.DestroyImmediate(candidate);}
    }
    public static void ValidateSource(AnimationClip source,string selectionKey,JObject measured)
    {
        if(source==null||source.isHumanMotion||string.IsNullOrEmpty(selectionKey)
            ||selectionKey.Any(c=>!char.IsLetterOrDigit(c))||measured==null
            ||(string)measured["status"]!="PASS_NATIVE_FOOT_COUNTER"||(string)measured["selectionKey"]!=selectionKey
            ||!(measured["runtimeCurveKeys"] is JArray keys)||keys.Count<2
            ||string.IsNullOrEmpty((string)measured["poseRootBonePath"]))throw new ArgumentException("검증된 Generic 보정 입력이 아닙니다.");
        if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long id)
            ||guid!=(string)measured["sourceGuid"]||id!=(long?)measured["sourceLocalId"]
            ||Hash(AssetDatabase.GetAssetPath(source))!=(string)measured["sourceSha256"]
            ||Mathf.Abs(source.frameRate-(float)measured["nativeFps"])>.001f
            ||Mathf.Abs(source.length-(float)measured["clipLength"])>.0001f
            ||Mathf.Abs(source.frameRate-(float)measured["runtimeClipFrameRate"])>.001f)
            throw new ArgumentException("검증된 원본 클립의 native 참조/FPS/길이가 다릅니다.");
        float sampling=(float)measured["runtimeCurveSamplingFps"];
        if(!Finite(sampling)||sampling<source.frameRate||sampling>480
            ||(float)measured["maximumSkinInfluenceOutsideCounterSubtree"]>.00001f
            ||(float)measured["maximumSubframePlanarDriftMeters"]>=.003f
            ||(float)measured["maximumSubframeBoneYDifferenceMeters"]>=.001f)
            throw new ArgumentException("보정 정확도 검증이 유효하지 않습니다.");
    }
    static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    static string Hash(string path)
    {using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(
        Path.Combine(Directory.GetParent(Application.dataPath).FullName,path)))).Replace("-","").ToLowerInvariant();}
    static void EnsureFolder(string path,List<string> created)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent,created);
        AssetDatabase.CreateFolder(parent,Path.GetFileName(path));created.Add(path);
    }
}
