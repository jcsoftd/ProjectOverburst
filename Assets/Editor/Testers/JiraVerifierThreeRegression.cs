using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Native result-contract regressions; product assets and user scenes are read only.
public static class JiraVerifierThreeRegression
{
    public static string RunNative(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
            throw new InvalidOperationException("Returned idle Editor required.");
        directory = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Private output required.");
        Directory.CreateDirectory(directory);
        var checks = new JArray();
        Action<bool,string> check = (ok,name) => checks.Add(new JObject { ["pass"] = ok, ["name"] = name });
        check(CrustaspikanTargetingVerifier.ExecutionCompleted(5,6,1,null), "material normal completion");
        check(CrustaspikanTargetingVerifier.ExecutionCompleted(5,7,2,null), "multi-attack pattern complete");
        check(!CrustaspikanTargetingVerifier.ExecutionCompleted(5,5,1,null), "cancel without failure is incomplete");
        check(!CrustaspikanTargetingVerifier.ExecutionCompleted(5,6,2,null), "second attack cancellation cannot reuse first completion");
        check(!CrustaspikanTargetingVerifier.ExecutionCompleted(5,6,1,"error"), "executor failure blocks completion");
        check(CrustaspikanTargetingVerifier.ExecutionCompleted(5,5,0,null), "unused executor has zero expected completions");
        check(!CrustaspikanTargetingVerifier.ExecutionCompleted(5,6,0,null), "unexpected completion rejected");

        string definition = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions" })
            .Select(AssetDatabase.GUIDToAssetPath).First(p => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(p).IsValid);
        Func<JArray,JObject> batch = paths => new JObject { ["verifyPresentationCalibration"] = true, ["definitions"] = paths, ["entries"] = new JArray(), ["validationFrameRates"] = new JArray(30) };
        foreach (var invalid in new[] { batch(new JArray()), batch(null), batch(new JArray(definition,definition)), batch(new JArray(JValue.CreateNull())), batch(new JArray("Assets/missing.asset")) })
        {
            bool rejected = false;
            try { MonsterWeakAttackPlayerLoopVerifier.BuildPresentationCaseKeys(invalid); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "invalid presentation definitions rejected: " + invalid["definitions"]);
        }
        var expected = MonsterWeakAttackPlayerLoopVerifier.BuildPresentationCaseKeys(batch(new JArray(definition)));
        check(expected.Length==2, "single saved definition subset remains allowed");
        var validCases = new JArray(expected.Select(k => { int i=k.IndexOf('|'); return new JObject { ["id"]=k.Substring(0,i), ["scenario"]=k.Substring(i+1), ["pass"]=true }; }));
        check(MonsterWeakAttackPlayerLoopVerifier.PresentationCoverageMatches(expected,validCases,out _), "exact expected subset coverage");
        check(!MonsterWeakAttackPlayerLoopVerifier.PresentationCoverageMatches(Array.Empty<string>(),new JArray(),out _), "zero-result coverage cannot PASS");
        check(!MonsterWeakAttackPlayerLoopVerifier.PresentationCoverageMatches(expected,new JArray(validCases[0].DeepClone()),out _), "missing scenario rejected");
        check(!MonsterWeakAttackPlayerLoopVerifier.PresentationCoverageMatches(expected,new JArray(validCases[0].DeepClone(),validCases[0].DeepClone()),out _), "duplicate scenario rejected even at expected count");
        var wrong = (JArray)validCases.DeepClone(); wrong[1]["id"]="unexpected";
        check(!MonsterWeakAttackPlayerLoopVerifier.PresentationCoverageMatches(expected,wrong,out _), "unexpected actor rejected at expected count");
        wrong=(JArray)validCases.DeepClone(); wrong[1]["scenario"]="calibrated-unexpected";
        check(!MonsterWeakAttackPlayerLoopVerifier.PresentationCoverageMatches(expected,wrong,out _), "unexpected scenario rejected at expected count");
        string invalidPlan = Path.Combine(directory,"empty-presentation-plan.json"); File.WriteAllText(invalidPlan,batch(new JArray()).ToString());
        bool entryRejected=false;
        try { MonsterWeakAttackPlayerLoopVerifier.StartSavedAttackBatch(Path.Combine(directory,"RejectedBeforePlay"),invalidPlan); }
        catch (ArgumentException) { entryRejected=true; }
        check(entryRejected&&!Directory.Exists(Path.Combine(directory,"RejectedBeforePlay"))&&!EditorApplication.isPlayingOrWillChangePlaymode,
            "real batch entry rejects empty input before output, scene or Play changes");

        var good = new JObject { ["status"]="PASS", ["frames"]=20, ["audioPeak"]=.1, ["error"]=null };
        var badTakes = new List<object> { null, JValue.CreateNull(), new JObject { ["status"]="FAIL", ["frames"]=20, ["audioPeak"]=.1 },
            new JObject { ["status"]="PASS", ["frames"]=0, ["audioPeak"]=.1 }, new JObject { ["status"]="PASS", ["frames"]=20, ["audioPeak"]=0 },
            new JObject { ["status"]="PASS", ["frames"]=20, ["audioPeak"]=.1, ["error"]="encoder error" } };
        check(PlayerEvadeVerifier.IsValidRecordedTake(good), "valid recorded audio/video take accepted");
        foreach (var bad in badTakes)
        {
            check(!PlayerEvadeVerifier.IsValidRecordedTake(bad), "failed/null/empty/silent take rejected");
            var rows = new object[] { new { phase="first", take=good }, new { phase="second", take=bad } };
            var summary=PlayerEvadeVerifier.RecordingSummary(rows,2,true);
            check((string)summary["status"]=="FAIL"&&!(bool)summary["actualAudio"]&&!(bool)summary["nativeAudio"], "failed take propagates to recording summary");
        }
        var passingRows = new object[] { new { phase="good", take=good } };
        check((string)PlayerEvadeVerifier.RecordingSummary(passingRows,1,true)["status"]=="PASS", "normal complete recording summary PASS");
        check((string)PlayerEvadeVerifier.RecordingSummary(passingRows,2,true)["status"]=="FAIL", "missing take summary FAIL");
        check((string)PlayerEvadeVerifier.RecordingSummary(passingRows,1,true,null,true)["status"]=="FAIL", "interrupted capture cannot PASS despite valid takes");
        check((string)PlayerEvadeVerifier.RecordingSummary(Array.Empty<object>(),0,true)["status"]=="FAIL", "empty recording cannot PASS");

        var preview=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Owned recorder failure fixture");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,preview);
        var recorder=root.AddComponent<ElementSupplementMovieRecorder>();
        recorder.Bind(root.AddComponent<PlayerActorRuntime>(),null,null,null);
        try
        {
            string takeDirectory=Path.Combine(directory,"RecorderZeroFrames"); Directory.CreateDirectory(takeDirectory);
            var type=typeof(ElementSupplementMovieRecorder); var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            type.GetField("directory",flags).SetValue(recorder,takeDirectory);
            type.GetField("target",flags).SetValue(recorder,new RenderTexture(8,8,0));
            var result=recorder.End();
            check(result!=null&&!PlayerEvadeVerifier.IsValidRecordedTake(result), "actual recorder nonthrowing FAIL is rejected");
            check(recorder.End()==null, "actual recorder second End returns null");
            var summary=PlayerEvadeVerifier.RecordingSummary(new object[]{new{take=result}},1,true);
            File.WriteAllText(Path.Combine(takeDirectory,"summary.json"),summary.ToString());
            check((string)JObject.Parse(File.ReadAllText(Path.Combine(takeDirectory,"take.json")))["status"]=="FAIL"
                &&(string)summary["status"]=="FAIL"&&!(bool)summary["actualAudio"], "actual take file FAIL agrees with summary FAIL");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
        var report=new JObject { ["status"]=checks.All(c=>(bool)c["pass"])?"PASS_SCOPED":"FAIL", ["checks"]=checks, ["definition"]=definition, ["utc"]=DateTime.UtcNow.ToString("o") };
        File.WriteAllText(Path.Combine(directory,"native-results.json"),report.ToString());
        return report.ToString();
    }
}
