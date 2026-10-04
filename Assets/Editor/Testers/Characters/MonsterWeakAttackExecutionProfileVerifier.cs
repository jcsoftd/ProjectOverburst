using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class MonsterWeakAttackExecutionProfileVerifier
{
    public static string Run(string outputDirectory)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("약공 데이터 검사는 유휴 EditMode에서만 실행합니다.");
        string workspace=Directory.GetParent(Application.dataPath).Parent.FullName;
        string artifactRoot=Path.GetFullPath(Path.Combine(workspace,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        outputDirectory=Path.GetFullPath(outputDirectory);
        if(!outputDirectory.StartsWith(artifactRoot,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("산출 경로가 아닙니다.");
        var checks=new List<object>(); int failed=0;
        void Check(string name,bool pass) {checks.Add(new {name,pass});if(!pass)failed++;}
        void Reject(string name,Action operation)
        {
            bool rejected=false;
            try {operation();} catch(ArgumentException){rejected=true;} catch(InvalidOperationException){rejected=true;}
            Check(name,rejected);
        }
        var clip=new AnimationClip {name="V3_ContractFixture",frameRate=30};
        clip.SetCurve("",typeof(Transform),"localPosition.x",AnimationCurve.Linear(0,0,1,0));
        var profile=ScriptableObject.CreateInstance<EnemyWeakAttackExecutionProfile>();
        var ability=ScriptableObject.CreateInstance<EnemyAbilityDefinition>();
        try
        {
            void Configure(EnemyWeakAttackMotionPolicy policy,float reach,float advance,Vector2 window,AnimationCurve curve)
                =>profile.Configure("contract-fixture",clip,clip,new Vector2(0,clip.length),policy,reach,advance,window,curve,"");
            Configure(EnemyWeakAttackMotionPolicy.ShortAdvance,1.6f,.5f,new Vector2(.1f,.6f),AnimationCurve.Linear(0,0,1,1));
            Check("short advance range",Mathf.Abs(profile.ApproachStartRange-2.1f)<.0001f);
            Check("close target advance zero",profile.ResolveAdvanceBudget(1.5f)==0f);
            Check("necessary advance only",Mathf.Abs(profile.ResolveAdvanceBudget(1.9f)-.3f)<.0001f);
            Check("advance maximum",Mathf.Abs(profile.ResolveAdvanceBudget(8f)-.5f)<.0001f);
            Check("advance starts at authored window",profile.EvaluateAdvanceFraction(.1f)==0f);
            Check("advance ends at authored window",profile.EvaluateAdvanceFraction(.6f)==1f);
            Check("advance midpoint",Mathf.Abs(profile.EvaluateAdvanceFraction(.35f)-.5f)<.0001f);
            Reject("no movement on stationary",()=>Configure(EnemyWeakAttackMotionPolicy.Stationary,1.6f,.5f,Vector2.zero,null));
            Reject("reject NaN reach",()=>Configure(EnemyWeakAttackMotionPolicy.ShortAdvance,float.NaN,.5f,new Vector2(.1f,.6f),AnimationCurve.Linear(0,0,1,1)));
            Reject("reject reversed movement window",()=>Configure(EnemyWeakAttackMotionPolicy.ShortAdvance,1.6f,.5f,new Vector2(.7f,.2f),AnimationCurve.Linear(0,0,1,1)));
            var decreasing=new AnimationCurve(new Keyframe(0,0,0,10),new Keyframe(1,1,10,0));
            Reject("reject decrease between endpoints",()=>Configure(EnemyWeakAttackMotionPolicy.ShortAdvance,1.6f,.5f,new Vector2(.1f,.6f),decreasing));
            Check("failed configure preserves previous profile",Mathf.Abs(profile.ApproachStartRange-2.1f)<.0001f && profile.ValidateAuthoring(out _));
            Reject("reject invalid trim",()=>profile.Configure("bad-trim",clip,clip,new Vector2(.8f,.3f),EnemyWeakAttackMotionPolicy.Stationary,1.6f,0f,Vector2.zero,null,""));
            Configure(EnemyWeakAttackMotionPolicy.VisualJump,1.6f,0f,Vector2.zero,null);
            Check("visual jump no advance",profile.ResolveAdvanceBudget(3f)==0f && profile.IsStationaryMotion);
            ability.Configure("fixture","Attack1",10,1.6f,1f,120f,1f,.5f,.5f,1f,1f,false,animationDuration:1f);
            Check("legacy ability remains valid",ability.IsValid && !ability.HasWeakAttackExecution);
            ability.ConfigureWeakAttackExecution(profile);
            Check("new profile range gate",ability.MatchesUseConditions(1.6f,1f) && !ability.MatchesUseConditions(1.61f,1f));
            ability.ConfigureAdditionalHits(.65f,.8f);
            Check("three hit weak accepted",ability.IsValid);
            var windows = new [] { new Vector2(.4f,.55f), new Vector2(.6f,.7f), new Vector2(.75f,.85f) };
            profile.Configure("contract-fixture",clip,clip,new Vector2(0,1),EnemyWeakAttackMotionPolicy.VisualJump,
                1.6f,0,Vector2.zero,null,"",windows);
            windows[0] = Vector2.zero;
            Check("contact windows own a copy of authoring input",profile.TryGetContactWindow(0,out Vector2 copied) && copied==new Vector2(.4f,.55f));
            Check("valid contact times match profile",ability.IsValid && profile.ContactWindowCount==3);
            Check("contact window lookup rejects invalid phase",!profile.TryGetContactWindow(-1,out _) && !profile.TryGetContactWindow(3,out _));
            ability.ConfigureAdditionalHits(.72f,.8f);
            Check("editing impact outside contact window invalidates ability",!ability.IsValid);
            Reject("reject attaching timing mismatch",()=>ability.ConfigureWeakAttackExecution(profile));
            ability.ConfigureAdditionalHits(.65f,.8f);
            Reject("reject reversed contact window",()=>profile.Configure("bad-window",clip,clip,new Vector2(0,1),
                EnemyWeakAttackMotionPolicy.Stationary,1.6f,0,Vector2.zero,null,"",new []{new Vector2(.6f,.4f)}));
            Reject("reject nonfinite contact window",()=>profile.Configure("bad-window",clip,clip,new Vector2(0,1),
                EnemyWeakAttackMotionPolicy.Stationary,1.6f,0,Vector2.zero,null,"",new []{new Vector2(float.NaN,.6f)}));
            Check("failed contact configuration preserves prior windows",profile.ContactWindowCount==3 && ability.IsValid);
            Configure(EnemyWeakAttackMotionPolicy.VisualJump,1.6f,0f,Vector2.zero,null);
            ability.ConfigureAdditionalHits(.6f,.7f,.8f);
            Check("four hit updated weak invalid",!ability.IsValid);
            Reject("reject attaching four hit weak",()=>ability.ConfigureWeakAttackExecution(profile));
            ability.ConfigureWeakAttackExecution(null);
            Check("unmigrated legacy hit count unchanged",ability.IsValid && ability.HitCount==4);
            var serialized=new SerializedObject(ability);
            serialized.FindProperty("telegraphedStrongAttack").boolValue=true;serialized.ApplyModifiedPropertiesWithoutUndo();
            Reject("strong cannot receive weak profile",()=>ability.ConfigureWeakAttackExecution(profile));

            string v3Path=Path.Combine(workspace,"개인파일/코덱스산출/Design/20261003_MonsterOverhaulV3/몬스터_개편안_v3.json");
            var v3=JObject.Parse(File.ReadAllText(v3Path));string v3Hash=Hash(v3Path);
            const string cardKey="runtime:CavernMutants_Ceratoferox";
            var selected=(JObject)v3["cards"][cardKey]["weak"][0];
            var source=AssetDatabase.LoadAllAssetsAtPath((string)selected["sourcePath"]).OfType<AnimationClip>().Single(c=>c.name==(string)selected["clip"]);
            var current=AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>((string)selected["runtimeUses"][0]["path"]);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long localId);
            string selectionKey=(string)selected["key"];
            var authored=new JObject {
                {"nativeAuthoringComplete",true},{"cardKey",cardKey},{"selectionKey",selectionKey},
                {"sourceGuid",guid},{"sourceLocalId",localId},{"sourceSha256",Hash(AssetDatabase.GetAssetPath(source))},
                {"nativeFps",source.frameRate},{"motionPolicy",selected["motionPolicy"]},
                {"runtimeClipPath",AssetDatabase.GetAssetPath(source)},
                {"stationaryStartRange",current.Range},{"maxAdvanceDistance",0f},{"advanceWindow",new JArray(0f,0f)},
                {"sourceTrimSeconds",new JArray(0f,source.length)},{"poseRootBonePath",""},
                {"hitNormalizedTimes",new JArray(Enumerable.Range(0,current.HitCount).Select(current.GetHitNormalizedTime))},
                {"contactWindowsNormalized",new JArray(Enumerable.Range(0,current.HitCount).Select(i =>
                    new JArray(Mathf.Max(0,Mathf.Floor(current.GetHitNormalizedTime(i)*source.length*source.frameRate)-1)/(source.length*source.frameRate),
                        Mathf.Min(Mathf.Round(source.length*source.frameRate),Mathf.Ceil(current.GetHitNormalizedTime(i)*source.length*source.frameRate)+1)/(source.length*source.frameRate))))}
            };
            string before=EditorJsonUtility.ToJson(current);
            MonsterWeakAttackExecutionWriter.Validate(v3Path,v3Hash,cardKey,selectionKey,authored,current,source,source);
            Check("native current contact fixture validates",true);
            Reject("reject stale V3 hash",()=>MonsterWeakAttackExecutionWriter.Validate(v3Path,new string('0',64),cardKey,selectionKey,authored,current,source,source));
            foreach(string field in new [] {"sourceGuid","sourceLocalId","sourceSha256","nativeFps","motionPolicy","hitNormalizedTimes","nativeAuthoringComplete","runtimeClipPath","stationaryStartRange","contactWindowsNormalized"})
            {
                var changed=(JObject)authored.DeepClone();
                if(field=="sourceGuid"||field=="sourceSha256")changed[field]="incorrect";
                else if(field=="sourceLocalId")changed[field]=1L;
                else if(field=="nativeFps")changed[field]=1f;
                else if(field=="motionPolicy")changed[field]="VisualJump";
                else if(field=="hitNormalizedTimes")changed[field]=new JArray(.1f);
                else if(field=="runtimeClipPath")changed[field]="Assets/incorrect.anim";
                else if(field=="stationaryStartRange" || field=="contactWindowsNormalized")changed[field]=null;
                else changed[field]=false;
                Reject("reject altered "+field,()=>MonsterWeakAttackExecutionWriter.Validate(v3Path,v3Hash,cardKey,selectionKey,changed,current,source,source));
            }
            Check("writer validation never changes current ability",EditorJsonUtility.ToJson(current)==before);
            VerifyNativeApply(v3Path,v3Hash,cardKey,selectionKey,authored,current,source,Check,Reject);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ability);UnityEngine.Object.DestroyImmediate(profile);UnityEngine.Object.DestroyImmediate(clip);
        }
        Directory.CreateDirectory(outputDirectory);
        string path=Path.Combine(outputDirectory,"data-contract-results.json");
        File.WriteAllText(path,Newtonsoft.Json.JsonConvert.SerializeObject(new {status=failed==0?"PASS_SCOPED":"FAIL",checks,failed,
            runtimeApplied=false,gamePlay="NOT_RUN",audioApplied=false},Newtonsoft.Json.Formatting.Indented));
        if(failed>0)throw new InvalidOperationException("약공 데이터 검사 실패: "+failed);
        return path;
    }

    private static void VerifyNativeApply(string approved,string hash,string cardKey,string selectionKey,JObject authored,
        EnemyAbilityDefinition current,AnimationClip source,Action<string,bool> check,Action<string,Action> reject)
    {
        const string fixtureRoot="Assets/ProjectOverburst/Resources/Enemies/Themes/Validation/V3WeakContract";
        var folders=new List<string>();
        void Folder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);
            if(string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent,Path.GetFileName(path))))throw new IOException(path);
            folders.Add(path);
        }
        // Record only ancestors absent before this test, then remove only empty owned folders.
        string profileRoot=MonsterWeakAttackExecutionWriter.Root;
        for(string p=profileRoot;!AssetDatabase.IsValidFolder(p);p=Path.GetDirectoryName(p).Replace('\\','/'))folders.Insert(0,p);
        string id="V3WeakContract_"+Guid.NewGuid().ToString("N");
        string abilityPath=fixtureRoot+"/"+id+".asset",profilePath=profileRoot+"/"+id+".asset";
        EnemyAbilityDefinition fixture=ScriptableObject.CreateInstance<EnemyAbilityDefinition>();
        bool fixtureCreated=false;
        string currentBefore=EditorJsonUtility.ToJson(current);
        try
        {
            EditorJsonUtility.FromJsonOverwrite(currentBefore,fixture);
            fixture.ConfigureWeakAttackExecution(null);
            var serialized=new SerializedObject(fixture);serialized.FindProperty("abilityId").stringValue=id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Folder(fixtureRoot);AssetDatabase.CreateAsset(fixture,abilityPath);fixtureCreated=true;
            check("native apply writes selected profile",MonsterWeakAttackExecutionWriter.Apply(approved,hash,cardKey,selectionKey,authored,fixture,source,source));
            var profile=AssetDatabase.LoadAssetAtPath<EnemyWeakAttackExecutionProfile>(profilePath);
            check("native saved profile reference",profile!=null && fixture.WeakAttackExecution==profile && profile.ValidateAuthoring(out _));
            string abilityHash=Hash(abilityPath),profileHash=Hash(profilePath);
            check("native reapply returns unchanged",!MonsterWeakAttackExecutionWriter.Apply(approved,hash,cardKey,selectionKey,authored,fixture,source,source));
            check("native reapply bytes unchanged",Hash(abilityPath)==abilityHash && Hash(profilePath)==profileHash);
            var incomplete=(JObject)authored.DeepClone();incomplete["advanceWindow"]=null;
            reject("native invalid authoring rejected",()=>MonsterWeakAttackExecutionWriter.Apply(approved,hash,cardKey,selectionKey,incomplete,fixture,source,source));
            check("native invalid authoring preserves both assets",Hash(abilityPath)==abilityHash && Hash(profilePath)==profileHash);
            string profileJson=EditorJsonUtility.ToJson(profile);
            var profileSerialized=new SerializedObject(profile);profileSerialized.FindProperty("selectionKey").stringValue="other-selection";
            profileSerialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
            string conflictHash=Hash(profilePath);
            reject("native existing key conflict rejected",()=>MonsterWeakAttackExecutionWriter.Apply(approved,hash,cardKey,selectionKey,authored,fixture,source,source));
            check("native conflict leaves assets untouched",Hash(abilityPath)==abilityHash && Hash(profilePath)==conflictHash);
            EditorJsonUtility.FromJsonOverwrite(profileJson,profile);EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
            check("native fixture never changes game ability",EditorJsonUtility.ToJson(current)==currentBefore);
        }
        finally
        {
            if(AssetDatabase.LoadMainAssetAtPath(profilePath)!=null)AssetDatabase.DeleteAsset(profilePath);
            if(fixtureCreated)AssetDatabase.DeleteAsset(abilityPath);else UnityEngine.Object.DestroyImmediate(fixture);
            foreach(string path in folders.Distinct().OrderByDescending(p=>p.Length))
                if(AssetDatabase.IsValidFolder(path) && Directory.GetFileSystemEntries(path).Length==0)AssetDatabase.DeleteAsset(path);
        }
        check("native fixture assets cleaned",AssetDatabase.LoadMainAssetAtPath(abilityPath)==null && AssetDatabase.LoadMainAssetAtPath(profilePath)==null);
    }

    private static string Hash(string path)
    { using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant(); }
}
