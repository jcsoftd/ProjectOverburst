using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class MonsterV3ApprovedParryImportVerifier
{
    public static object Run(string receiptPath,string outputPath)
    {
        outputPath=Path.GetFullPath(outputPath);
        string allowed=Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        if(!outputPath.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Private artifact output required.");
        var receipt=JObject.Parse(File.ReadAllText(receiptPath));
        var checks=new JArray();int curves=0,keys=0;
        foreach(JObject row in receipt["rows"])
        {
            string path=(string)row["assetPath"];
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            if(importer.animationType!=ModelImporterAnimationType.Generic || importer.sourceAvatar==null || !importer.sourceAvatar.isValid)
                throw new Exception("Original Avatar binding failed: "+path);
            foreach(JObject entry in row["clips"])
            {
                var raw=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c=>c.name==(string)entry["clip"]);
                var adapted=AssetDatabase.LoadAssetAtPath<AnimationClip>((string)entry["runtimeBinding"]["assetPath"]);
                if(adapted==null || adapted.frameRate!=raw.frameRate || Mathf.Abs(adapted.length-raw.length)>.001f || adapted.isLooping!=raw.isLooping)
                    throw new Exception("Saved role contract failed: "+entry["clip"]);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(adapted,out string guid,out long localId);
                if(guid!=(string)entry["runtimeBinding"]["guid"] || localId!=(long)entry["runtimeBinding"]["localId"])
                    throw new Exception("Saved binding identity changed.");
                var originalPaths=AssetDatabase.LoadAssetAtPath<GameObject>((string)row["avatarPath"]).GetComponentsInChildren<Transform>(true)
                    .Select(t=>AnimationUtility.CalculateTransformPath(t,AssetDatabase.LoadAssetAtPath<GameObject>((string)row["avatarPath"]).transform)).ToHashSet();
                foreach(var sourceBinding in AnimationUtility.GetCurveBindings(raw))
                {
                    if(sourceBinding.type==typeof(Transform)&&sourceBinding.path=="Armature")continue;
                    var target=sourceBinding;
                    if(target.type==typeof(Transform)&&target.path.StartsWith("Armature/",StringComparison.Ordinal))target.path=target.path.Substring(9);
                    if(target.type==typeof(Transform)&&!originalPaths.Contains(target.path))throw new Exception("Saved bone path is unbound: "+target.path);
                    var sourceCurve=AnimationUtility.GetEditorCurve(raw,sourceBinding);
                    var targetCurve=AnimationUtility.GetEditorCurve(adapted,target);
                    if(targetCurve==null||sourceCurve.length!=targetCurve.length)throw new Exception("Saved curve missing: "+target.path);
                    var sourceKeys=sourceCurve.keys;var targetKeys=targetCurve.keys;
                    for(int i=0;i<sourceKeys.Length;i++)
                        if(!sourceKeys[i].Equals(targetKeys[i]))throw new Exception("Approved key changed on serialization: "+target.path+" / "+target.propertyName+" / "+i);
                    curves++;keys+=sourceCurve.length;
                }
                checks.Add(new JObject{["number"]=row["number"],["role"]=entry["role"],["status"]="PASS_SAVED_BINDING",["guid"]=guid});
            }
        }
        var result=new {status="PASS_SAVED_24_72_BINDINGS",records=receipt["rows"].Count(),clipCount=checks.Count,curves,keys,
            checks,approvedKeysChanged=0,originalRigModified=false,actorBound=false,gameplayParryTested=false};
        File.WriteAllText(outputPath,JsonConvert.SerializeObject(result,Formatting.Indented));
        return new {result.status,result.records,result.clipCount,curves,keys,outputPath};
    }
}
