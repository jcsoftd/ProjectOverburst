using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Imports immutable approved FBXs separately from the vendor model and its metadata.
// Import success is not an Actor binding or a gameplay verification result.
public static class MonsterV3ApprovedParryImporter
{
    public const string AssetRoot = "Assets/ProjectOverburst/03_Features/Enemies/Animations/ParryStun/ApprovedV3";
    static readonly string[] Roles = { "ParryCollapse", "StunnedLoop", "StunRecover" };

    public static Task<object> WaitAndApply(string manifestPath,string sourceDirectory,string outputDirectory,int first=1,int last=24)
    {
        var completion=new TaskCompletionSource<object>();
        double deadline=EditorApplication.timeSinceStartup+120; int stable=0;
        EditorApplication.CallbackFunction tick=null;
        AssemblyReloadEvents.AssemblyReloadCallback reload=null;
        void Detach(){EditorApplication.update-=tick; AssemblyReloadEvents.beforeAssemblyReload-=reload;}
        reload=()=>{Detach(); completion.TrySetException(new OperationCanceledException("Domain reload cancelled import wait; inspect receipts before retry."));};
        tick=()=>
        {
            if(EditorApplication.timeSinceStartup>deadline){Detach(); completion.TrySetException(new TimeoutException("Shared Editor stayed busy; no import performed."));return;}
            if(!IsIdle()){stable=0;return;}
            if(++stable<3)return;
            Detach();
            try{completion.TrySetResult(Apply(manifestPath,sourceDirectory,outputDirectory,first,last));}
            catch(Exception e){completion.TrySetException(e);}
        };
        EditorApplication.update+=tick; AssemblyReloadEvents.beforeAssemblyReload+=reload;
        return completion.Task;
    }

    public static object Apply(string manifestPath, string sourceDirectory, string outputDirectory, int first=1, int last=24)
    {
        RequireIdle();
        outputDirectory=Path.GetFullPath(outputDirectory);
        string allowed=Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        if(!outputDirectory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Private artifact output is required.");
        Directory.CreateDirectory(outputDirectory);
        JObject manifest=JObject.Parse(File.ReadAllText(manifestPath));
        if ((int)manifest["actor_count"] != 24 || (int)manifest["animation_clip_count"] != 72)
            throw new InvalidDataException("Expected the approved 24/72 manifest.");
        var rows=new JArray();
        var preserved=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (JObject record in manifest["records"])
        {
            foreach (JToken reference in OriginalReferences(record))
            {
                string path=(string)reference["projectPath"];
                if (Hash(path) != (string)reference["sha256"] || AssetDatabase.AssetPathToGUID(path) != (string)reference["guid"])
                    throw new InvalidDataException("Original changed: "+path);
                preserved[path]=Hash(path); preserved[path+".meta"]=Hash(path+".meta");
            }
        }
        try
        {
            foreach (JObject record in manifest["records"])
            {
                int number=(int)record["number"];
                if (number<first || number>last) continue;
                RequireIdle();
                string relative=(string)record["fbx_file"];
                string source=Path.GetFullPath(Path.Combine(sourceDirectory,relative));
                if (!source.StartsWith(Path.GetFullPath(sourceDirectory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Source escapes source directory.");
                string destination=AssetRoot+"/"+relative;
                if (Hash(source)!=(string)record["sha256"]) throw new InvalidDataException("Approved hash mismatch: "+relative);
                EnsureFolders(Path.GetDirectoryName(destination).Replace('\\','/'));
                if (File.Exists(destination))
                {
                    if (Hash(destination)!=(string)record["sha256"]) throw new InvalidDataException("Destination owned by different content: "+destination);
                }
                else File.Copy(source,destination);
                AssetDatabase.ImportAsset(destination,ImportAssetOptions.ForceSynchronousImport);
                var importer=(ModelImporter)AssetImporter.GetAtPath(destination);
                var modelPath=(string)record["source_models"][0]["projectPath"];
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                var original=(ModelImporter)AssetImporter.GetAtPath(modelPath);
                var avatar=model.GetComponentInChildren<Animator>(true)?.avatar;
                if (avatar==null || !avatar.isValid || original.animationType!=ModelImporterAnimationType.Generic)
                    throw new InvalidDataException("Original Generic Avatar missing: "+modelPath);
                var defaults=importer.defaultClipAnimations;
                if (defaults.Length!=3) throw new InvalidDataException("Expected three Takes: "+destination);
                var clips=new List<ModelImporterClipAnimation>();
                foreach(string role in Roles)
                {
                    var matches=defaults.Where(c=>c.name.EndsWith(role,StringComparison.Ordinal)||c.takeName.EndsWith(role,StringComparison.Ordinal)).ToArray();
                    if(matches.Length!=1) throw new InvalidDataException("Take ambiguity: "+destination+" "+role);
                    var clip=matches[0];
                    float expected=(float)record["clips"][role]["duration_seconds"];
                    if (Mathf.Abs((clip.lastFrame-clip.firstFrame)/30f-expected)>.002f)
                        throw new InvalidDataException("Take duration differs from approved manifest: "+destination+" "+role);
                    clip.name=Path.GetFileNameWithoutExtension(destination).Replace("_ParryAnimations","")+"_"+role;
                    clip.loopTime=role=="StunnedLoop"; clip.loopPose=false;
                    clip.lockRootRotation=true; clip.lockRootHeightY=true; clip.lockRootPositionXZ=true;
                    clip.keepOriginalOrientation=true; clip.keepOriginalPositionY=true; clip.keepOriginalPositionXZ=true;
                    clips.Add(clip);
                }
                importer.animationType=ModelImporterAnimationType.Generic;
                importer.avatarSetup=ModelImporterAvatarSetup.CopyFromOther; importer.sourceAvatar=avatar;
                importer.globalScale=original.globalScale; importer.useFileScale=original.useFileScale;
                importer.bakeAxisConversion=original.bakeAxisConversion;
                importer.importAnimation=true; importer.animationCompression=ModelImporterAnimationCompression.Off;
                importer.importCameras=false; importer.importLights=false;
                importer.materialImportMode=ModelImporterMaterialImportMode.None;
                importer.optimizeGameObjects=false;
                importer.clipAnimations=clips.ToArray(); importer.SaveAndReimport();
                var imported=AssetDatabase.LoadAllAssetsAtPath(destination).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal)).ToArray();
                var clipRows=new JArray();
                var transforms=model.GetComponentsInChildren<Transform>(true).ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,model.transform),t=>t);
                foreach(string role in Roles)
                {
                    var clip=imported.Single(c=>c.name.EndsWith("_"+role,StringComparison.Ordinal));
                    var settings=AnimationUtility.GetAnimationClipSettings(clip);
                    float expected=(float)record["clips"][role]["duration_seconds"];
                    if(Mathf.Abs(clip.length-expected)>.002f || Mathf.Abs(clip.frameRate-30f)>.001f || clip.isLooping!=(role=="StunnedLoop"))
                        throw new InvalidDataException("Imported clip contract failed: "+destination+" "+role);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip,out string guid,out long localId);
                    var bindings=AnimationUtility.GetCurveBindings(clip);
                    var paths=bindings.Where(b=>b.type==typeof(Transform)).Select(b=>b.path).Distinct().ToArray();
                    var missing=paths.Where(p=>!transforms.ContainsKey(p)).ToArray();
                    var adapter=BindOriginalPaths(clip,model,destination,role);
                    clipRows.Add(JObject.FromObject(new {role,clip=clip.name,guid,localId,seconds=clip.length,fps=clip.frameRate,
                        looping=clip.isLooping,curves=bindings.Length,transformPaths=paths.Length,missingPaths=missing,
                        firstFrame=clips.Single(c=>c.name==clip.name).firstFrame,lastFrame=clips.Single(c=>c.name==clip.name).lastFrame,
                        runtimeBinding=adapter,
                        rootMotionBake=new {settings.loopBlendOrientation,settings.loopBlendPositionY,settings.loopBlendPositionXZ}}));
                }
                if(imported.Length!=3 || Hash(destination)!=(string)record["sha256"]) throw new InvalidDataException("Approved bytes/clip count changed.");
                rows.Add(JObject.FromObject(new {number,name=(string)record["name"],assetPath=destination,sha256=Hash(destination),
                    guid=AssetDatabase.AssetPathToGUID(destination),avatarPath=modelPath,rig=importer.animationType.ToString(),
                    imported=true,actorBound=false,actualGameParryTested=false,clips=clipRows}));
                Write(outputDirectory,"IMPORTING",rows,preserved);
            }
            foreach(var pair in preserved) if(Hash(pair.Key)!=pair.Value) throw new InvalidDataException("Original mutation: "+pair.Key);
            Write(outputDirectory,"PASS_IMPORT_ONLY",rows,preserved);
            return new {status="PASS_IMPORT_ONLY",count=rows.Count,clips=rows.Count*3,actorBound=false,outputDirectory};
        }
        catch(Exception ex)
        {
            Write(outputDirectory,"FAIL_IMPORT_REVIEW_REQUIRED",rows,preserved,ex.ToString());
            throw;
        }
    }

    static object BindOriginalPaths(AnimationClip source,GameObject original,string fbxPath,string role)
    {
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        var originalPaths=original.GetComponentsInChildren<Transform>(true)
            .ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,original.transform),t=>t);
        var bindings=AnimationUtility.GetCurveBindings(source);
        var transformPaths=bindings.Where(b=>b.type==typeof(Transform)).Select(b=>b.path).Distinct().ToArray();
        var mapping=new Dictionary<string,string>();
        var dropped=new HashSet<string>();
        foreach(string path in transformPaths)
        {
            if(originalPaths.ContainsKey(path)){mapping[path]=path;continue;}
            if(path.StartsWith("Armature/",StringComparison.Ordinal)&&originalPaths.ContainsKey(path.Substring(9)))
            {mapping[path]=path.Substring(9);continue;}
            if(path=="Armature"){dropped.Add(path);continue;}
            throw new InvalidDataException("Bone path is not a removable export wrapper: "+path);
        }
        if(mapping.Values.Distinct().Count()!=mapping.Count)throw new InvalidDataException("Bone path mapping collision.");
        AnimationClip copy=UnityEngine.Object.Instantiate(source); copy.name=source.name;
        bool persisted=false; GameObject raw=null,bound=null;
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        float maxPosition=0,maxRotation=0,maxScale=0; int samples=0;
        try
        {
            foreach(var binding in bindings)
            {
                var curve=AnimationUtility.GetEditorCurve(source,binding);
                AnimationUtility.SetEditorCurve(copy,binding,null);
                if(binding.type==typeof(Transform)&&dropped.Contains(binding.path))continue;
                var target=binding;
                if(binding.type==typeof(Transform))target.path=mapping[binding.path];
                AnimationUtility.SetEditorCurve(copy,target,curve);
            }
            if(AnimationUtility.GetObjectReferenceCurveBindings(source).Length!=0)
                throw new InvalidDataException("Unexpected object curves in approved bone animation.");
            raw=UnityEngine.Object.Instantiate(model); bound=UnityEngine.Object.Instantiate(original);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(raw,scene);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(bound,scene);
            foreach(var a in raw.GetComponentsInChildren<Animator>(true))a.enabled=false;
            foreach(var a in bound.GetComponentsInChildren<Animator>(true))a.enabled=false;
            var rawPaths=raw.GetComponentsInChildren<Transform>(true).ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,raw.transform),t=>t);
            var boundPaths=bound.GetComponentsInChildren<Transform>(true).ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,bound.transform),t=>t);
            int steps=Mathf.RoundToInt(source.length*source.frameRate*2);
            for(int i=0;i<=steps;i++)
            {
                float t=source.length*i/steps;
                source.SampleAnimation(raw,t); copy.SampleAnimation(bound,t);
                foreach(string wrapperPath in dropped)
                {
                    var wrapper=rawPaths[wrapperPath];
                    if(wrapper.localPosition.magnitude>.00001f||Quaternion.Angle(wrapper.localRotation,Quaternion.identity)>.001f
                        ||Vector3.Distance(wrapper.localScale,Vector3.one)>.00001f)
                        throw new InvalidDataException("Export wrapper affects the approved pose; cannot drop: "+wrapperPath);
                }
                foreach(var pair in mapping)
                {
                    var a=rawPaths[pair.Key];var b=boundPaths[pair.Value];
                    maxPosition=Mathf.Max(maxPosition,Vector3.Distance(a.position,b.position));
                    maxRotation=Mathf.Max(maxRotation,Quaternion.Angle(a.rotation,b.rotation));
                    maxScale=Mathf.Max(maxScale,Vector3.Distance(a.lossyScale,b.lossyScale));
                }
                samples++;
            }
            if(maxPosition>.0001f||maxRotation>.08f||maxScale>.0001f)
                throw new InvalidDataException("Bound pose differs from approved motion: "+maxPosition+"m/"+maxRotation+"deg/"+maxScale);
            string path=Path.GetDirectoryName(fbxPath).Replace('\\','/')+"/"+source.name+"_Bound.anim";
            var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(existing==null){AssetDatabase.CreateAsset(copy,path);persisted=true;existing=copy;}
            else
            {
                if(!SameCurves(copy,existing))throw new InvalidDataException("Existing binding asset has different curves; preserve and review: "+path);
            }
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(existing,out string guid,out long id);
            return new {assetPath=path,guid,localId=id,role,remappedPaths=mapping.Count(p=>p.Key!=p.Value),
                discardedIdentityWrappers=dropped.ToArray(),keyframeValuesChanged=false,samples,
                maxPositionErrorMeters=maxPosition,maxRotationErrorDegrees=maxRotation,maxScaleError=maxScale,
                bindingValidated=true,sourceFbxUntouched=true};
        }
        finally
        {
            if(raw!=null)UnityEngine.Object.DestroyImmediate(raw);
            if(bound!=null)UnityEngine.Object.DestroyImmediate(bound);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            if(!persisted)UnityEngine.Object.DestroyImmediate(copy);
        }
    }
    static bool SameCurves(AnimationClip a,AnimationClip b)
    {
        if(a.name!=b.name||Mathf.Abs(a.length-b.length)>.001f||a.frameRate!=b.frameRate||a.isLooping!=b.isLooping)return false;
        var left=AnimationUtility.GetCurveBindings(a);var right=AnimationUtility.GetCurveBindings(b);
        if(left.Length!=right.Length)return false;
        foreach(var binding in left)
        {
            if(!right.Any(r=>r.path==binding.path&&r.propertyName==binding.propertyName&&r.type==binding.type))return false;
            var x=AnimationUtility.GetEditorCurve(a,binding);var y=AnimationUtility.GetEditorCurve(b,binding);
            if(x.keys.Length!=y.keys.Length)return false;
            for(int i=0;i<x.keys.Length;i++)if(!x.keys[i].Equals(y.keys[i]))return false;
        }
        return true;
    }

    static IEnumerable<JToken> OriginalReferences(JObject r)
    {
        yield return r["source_attack"]["reference"]; yield return r["source_idle"]; yield return r["source_prefab"];
        foreach(var m in r["source_models"]) yield return m;
    }
    static void RequireIdle()
    {
        if(!IsIdle())
            throw new InvalidOperationException("Shared Editor is busy; no import performed.");
    }
    static bool IsIdle()=>!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating
        &&string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
        &&string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
        &&string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""));
    static void EnsureFolders(string path)
    {
        string[] parts=path.Split('/'); string current=parts[0];
        for(int i=1;i<parts.Length;i++){string next=current+"/"+parts[i]; if(!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current,parts[i]); current=next;}
    }
    static string Hash(string path)
    {
        using(var sha=SHA256.Create()) using(var stream=File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    }
    static void Write(string output,string status,JArray rows,Dictionary<string,string> original,string error=null)
    {
        File.WriteAllText(Path.Combine(output,"import-result.json"),JsonConvert.SerializeObject(new {
            status,rows,originalFileHashes=original,error,actorBound=false,actualGameParryTested=false,
            utc=DateTime.UtcNow.ToString("o")},Formatting.Indented));
    }
}
