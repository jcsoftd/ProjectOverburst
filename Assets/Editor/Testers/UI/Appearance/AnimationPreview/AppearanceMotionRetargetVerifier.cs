using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Appearance;
using Overburst.Appearance.AnimationPreview;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class AppearanceMotionRetargetVerifier
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    static string Scenes()=>JsonConvert.SerializeObject(Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount)
        .Select(i=>{var s=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);return new{s.path,s.isDirty,roots=s.rootCount};}));
    static string Hash(string path)
    {using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    public static void Native(string destination)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        string output=Path.GetFullPath(destination);Directory.CreateDirectory(output);
        string before=Scenes();int sceneCount=EditorSceneManager.previewSceneCount;
        var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;Mesh baked=null;
        var rows=new List<object>();int frames=0,skins=0;float worstHeight=0,worstCamera=0;var failures=new List<string>();
        try
        {
            var library=AssetDatabase.LoadAssetAtPath<AppearanceAnimationLibrary>(AppearanceAnimationAuthoring.LibraryPath);
            var motions=library.animations.Where(x=>x.category=="Kawaii").ToArray();Require(motions.Length==416,"Kawaii source count changed");
            var assets=motions.Select(x=>AssetDatabase.GetAssetPath(x.clip)).Distinct().SelectMany(p=>new[]{p,p+".meta"}).ToDictionary(p=>p,Hash);
            var sourceSettings=motions.ToDictionary(x=>x.id,x=>JsonConvert.SerializeObject(AnimationUtility.GetAnimationClipSettings(x.clip)));
            var catalog=AssetDatabase.LoadAssetAtPath<CharacterAppearanceCatalog>(AppearanceCustomizationBuilder.CatalogPath);
            var actor=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
            var gameAvatar=actor.transform.Find("VisualRoot/ModelInstance_P09").GetComponent<Animator>().avatar;
            root=new GameObject("Owned All Motion Retarget Verification",typeof(RectTransform),typeof(RawImage));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
            var session=new AppearanceCustomizationSession(catalog,null);session.SetDeveloperNude(true);
            var preview=root.AddComponent<AppearanceCharacterPreview>();preview.Open(catalog,session,root.GetComponent<RawImage>());
            var stage=preview.Model.transform.root;UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(stage.gameObject,scene);
            var animator=preview.Model.GetComponent<Animator>();Require(animator.avatar==gameAvatar&&animator.avatar.isHuman&&animator.avatar.isValid,"preview must use the actual game P09 avatar");
            var kind=typeof(AppearanceCharacterPreview);var frame=kind.GetMethod("FrameCamera",Fields);var graphField=kind.GetField("graph",Fields);var profileField=kind.GetField("retargeting",Fields);
            var camera=(Camera)kind.GetField("viewCamera",Fields).GetValue(preview);
            var fixedCamera=camera.transform.localPosition;var fixedRotation=camera.transform.localRotation;float fixedFov=camera.fieldOfView;
            var leg=preview.Model.GetComponentsInChildren<SkinnedMeshRenderer>().Single(x=>x.name=="Female_Body_Nakid_Leg");
            var feet=new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightToes}.Select(animator.GetBoneTransform).ToArray();
            var ids=new HashSet<int>(leg.bones.Select((t,i)=>new{t,i}).Where(x=>feet.Contains(x.t)).Select(x=>x.i));
            var contacts=leg.sharedMesh.boneWeights.Select((w,i)=>new{w,i}).Where(x=>(ids.Contains(x.w.boneIndex0)?x.w.weight0:0)+(ids.Contains(x.w.boneIndex1)?x.w.weight1:0)
                +(ids.Contains(x.w.boneIndex2)?x.w.weight2:0)+(ids.Contains(x.w.boneIndex3)?x.w.weight3:0)>=.5f).Select(x=>x.i).ToArray();
            baked=new Mesh();
            foreach(var option in motions)
            {
                preview.Play(option.clip,true);var graph=(PlayableGraph)graphField.GetValue(preview);graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var profile=profileField.GetValue(preview);Require(profile!=null,"missing authored pose: "+option.id);
                var pose=(AnimationClip)profile.GetType().GetProperty("Clip").GetValue(profile);Require(pose&&pose!=option.clip&&!AssetDatabase.Contains(pose),"preview clip must be transient");
                Require(Vector3.Distance(camera.transform.localPosition,fixedCamera)<.0001f&&Quaternion.Angle(camera.transform.localRotation,fixedRotation)<.001f&&Mathf.Abs(camera.fieldOfView-fixedFov)<.001f,"animation selection changed manual camera framing");
                var settings=AnimationUtility.GetAnimationClipSettings(pose);Require(settings.loopBlendOrientation&&settings.loopBlendPositionY&&settings.loopBlendPositionXZ&&!animator.applyRootMotion,"native body movement must remain in the pose");
                var rootYBinding=AnimationUtility.GetCurveBindings(option.clip).Single(b=>b.propertyName=="RootT.y");var sourceY=AnimationUtility.GetEditorCurve(option.clip,rootYBinding);
                var poseY=AnimationUtility.GetEditorCurve(pose,rootYBinding);var initialRoot=preview.Model.transform.localPosition;var initialBody=stage.InverseTransformPoint(animator.bodyPosition);
                float initialSource=sourceY.Evaluate(0),initialFloor=camera.WorldToViewportPoint(stage.position).y;
                float heightError=0,cameraError=0,rootError=0,minBody=float.PositiveInfinity,maxBody=float.NegativeInfinity,minSole=float.PositiveInfinity,maxSole=float.NegativeInfinity;
                int samples=Mathf.Max(1,Mathf.CeilToInt(option.clip.length*60));int skinSamples=0;
                for(int n=0;n<=samples;n++)
                {
                    if(n>0)graph.Evaluate(option.clip.length/samples);
                    frame.Invoke(preview,null);float time=preview.PlaybackTime;
                    float expected=(sourceY.Evaluate(time)-initialSource)*animator.humanScale;
                    var body=stage.InverseTransformPoint(animator.bodyPosition);float error=Mathf.Abs(body.y-initialBody.y-expected);
                    heightError=Mathf.Max(heightError,error);cameraError=Mathf.Max(cameraError,Mathf.Abs(camera.WorldToViewportPoint(stage.position).y-initialFloor));
                    rootError=Mathf.Max(rootError,Vector3.Distance(initialRoot,preview.Model.transform.localPosition));
                    Require(Mathf.Abs(poseY.Evaluate(time)-sourceY.Evaluate(time))<.00001f,"source height curve was modified: "+option.id);
                    Require(!float.IsNaN(body.y)&&!float.IsInfinity(body.y),"invalid humanoid pose: "+option.id);
                    minBody=Mathf.Min(minBody,body.y);maxBody=Mathf.Max(maxBody,body.y);frames++;
                    if(n%5==0||n==samples)
                    {
                        leg.BakeMesh(baked);var vertices=baked.vertices;float sole=contacts.Min(i=>leg.transform.TransformPoint(vertices[i]).y-stage.position.y);
                        minSole=Mathf.Min(minSole,sole);maxSole=Mathf.Max(maxSole,sole);skinSamples++;skins++;
                    }
                }
                if(heightError>.0004f||cameraError>.00005f||rootError>.0001f)failures.Add(option.displayName+" height="+heightError+" camera="+cameraError+" root="+rootError);
                worstHeight=Mathf.Max(worstHeight,heightError);worstCamera=Mathf.Max(worstCamera,cameraError);
                rows.Add(new{option.id,option.displayName,length=option.clip.length,evaluatedFrames=samples+1,skinSamples,heightError,cameraError,rootError,bodyHeightSpan=maxBody-minBody,minimumSole=minSole,maximumSole=maxSole});
                if(rows.Count%16==0)File.WriteAllText(Path.Combine(output,"progress.json"),JsonConvert.SerializeObject(new{completed=rows.Count,total=motions.Length,frames,utc=DateTime.UtcNow}));
            }
            Require(assets.All(p=>Hash(p.Key)==p.Value),"vendor FBX/meta changed");
            Require(motions.All(x=>JsonConvert.SerializeObject(AnimationUtility.GetAnimationClipSettings(x.clip))==sourceSettings[x.id]),"vendor clip settings changed");
            File.WriteAllText(Path.Combine(output,"all-motion-result.json"),JsonConvert.SerializeObject(new{success=failures.Count==0,count=rows.Count,frames,skinSamples=skins,worstHeightErrorMeters=worstHeight,worstCameraViewportError=worstCamera,
                avatar=AssetDatabase.GetAssetPath(gameAvatar),sourcePreserved=true,failures,rows,utc=DateTime.UtcNow},Formatting.Indented));
            Require(failures.Count==0,"continuous motion regression: "+string.Join("; ",failures.Take(8)));
        }
        catch(Exception error)
        {
            File.WriteAllText(Path.Combine(output,"error.json"),JsonConvert.SerializeObject(new{error=error.ToString(),completed=rows.Count,frames,utc=DateTime.UtcNow},Formatting.Indented));throw;
        }
        finally
        {
            if(baked)Object.DestroyImmediate(baked);if(root){root.GetComponent<AppearanceCharacterPreview>()?.Close();Object.DestroyImmediate(root);}EditorSceneManager.ClosePreviewScene(scene);
            File.WriteAllText(Path.Combine(output,"scene-return.json"),JsonConvert.SerializeObject(new{before,after=Scenes(),preserved=before==Scenes(),previewsBefore=sceneCount,previewsAfter=EditorSceneManager.previewSceneCount}));
        }
    }
}
