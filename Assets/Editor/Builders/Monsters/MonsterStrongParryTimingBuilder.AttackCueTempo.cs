using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class MonsterStrongParryTimingBuilder
{
    public static string QueueAttackCueTempo(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string directory = Output(outputRelative), pending = Path.Combine(directory, "apply-queue.json");
        if (File.Exists(Path.Combine(directory, "apply-result.json"))) throw new InvalidOperationException("Inspect the existing result.");
        File.WriteAllText(pending, new JObject { ["status"] = "QUEUED", ["utc"] = DateTime.UtcNow }.ToString());
        double deadline = EditorApplication.timeSinceStartup + 30;
        EditorApplication.update += Run; AssemblyReloadEvents.beforeAssemblyReload += Cancel;
        return "ATTACK_CUE_TEMPO_NATIVE_QUEUE_ACCEPTED";
        void Detach() { EditorApplication.update -= Run; AssemblyReloadEvents.beforeAssemblyReload -= Cancel; }
        void Cancel() { Detach(); File.WriteAllText(pending, new JObject { ["status"] = "CANCELLED_RELOAD" }.ToString()); }
        void Run()
        {
            Detach();
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Native apply queue expired.");
                ApplyAttackCueTempo(outputRelative);
                MonsterTunerVerifier.StartAttackCueTempoNative(outputRelative);
                File.WriteAllText(pending, new JObject { ["status"] = "APPLIED_PREVIEW_RUNNING" }.ToString());
            }
            catch (Exception e) { File.WriteAllText(pending, new JObject { ["status"] = "FAIL", ["error"] = e.ToString() }.ToString()); }
        }
    }
    static readonly string[] TempoFields = { "telegraphedAttack", "preparationEndNormalized", "preparationDuration", "releaseDuration",
        "recoveryDuration", "parryCueBonePath", "parryCueLocalPosition", "parryCueScale" };
    static JObject OutsideTempo(JObject source)
    {
        var result=(JObject)source.DeepClone(); var body=(JObject)(result["MonoBehaviour"]??result);
        foreach(var field in TempoFields)body.Remove(field);return result;
    }
    public static string ApplyAttackCueTempo(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string directory=Output(outputRelative),receipt=Path.Combine(directory,"apply-result.json");
        if(File.Exists(receipt))throw new InvalidOperationException("Inspect the existing result before retrying.");
        var rows=JObject.Parse(File.ReadAllText(Path.Combine(directory,"plan.json")))["records"].ToArray();
        if(rows.Length!=35||rows.Select(r=>(string)r["id"]).Distinct().Count()!=35)throw new InvalidDataException("Reviewed roster must contain 35 unique actors.");
        var before=new JArray();var loaded=rows.Select(r=>Load((string)r["abilityPath"])).ToArray();
        for(int i=0;i<rows.Length;i++)
        {
            var row=rows[i];var ability=loaded[i];string path=(string)row["abilityPath"];
            var old=new JObject{["id"]=row["id"],["path"]=path,["fields"]=Fields(ability),["guid"]=AssetDatabase.AssetPathToGUID(path),
                ["metaHash"]=MonsterBlenderParryR6Builder.Hash(Path.Combine(Project,path)+".meta"),["hitCount"]=ability.HitCount};
            before.Add(old);
            var copy=UnityEngine.Object.Instantiate(ability);copy.name=ability.name;
            try
            {
                ConfigureTempo(copy,row);
                if(!JToken.DeepEquals(OutsideTempo(Fields(copy)),OutsideTempo((JObject)old["fields"])))throw new InvalidDataException("Unrelated attack fields changed: "+path);
            }
            finally{UnityEngine.Object.DestroyImmediate(copy);}
        }
        foreach (var old in before)
        {
            string path=(string)old["path"],backup=Path.Combine(directory,"Recovery",path);
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            foreach(string suffix in new[]{"",".meta"})
            {
                string source=Path.Combine(Project,path)+suffix,destination=backup+suffix;
                if(File.Exists(destination))
                { if(MonsterBlenderParryR6Builder.Hash(source)!=MonsterBlenderParryR6Builder.Hash(destination))throw new IOException("Recovery snapshot differs: "+path); }
                else File.Copy(source,destination,false);
            }
        }
        File.WriteAllText(Path.Combine(directory,"snapshot-before.json"),new JObject{["records"]=before}.ToString());
        var saved=new JArray();
        try
        {
            for(int i=0;i<rows.Length;i++)
            {
                var row=rows[i];var ability=loaded[i];var old=before[i];string path=(string)row["abilityPath"];
                Undo.RecordObject(ability,"몬스터 공격 구간 속도와 공격 부위 패링 빛");ConfigureTempo(ability,row);
                EditorUtility.SetDirty(ability);AssetDatabase.SaveAssetIfDirty(ability);
                if(AssetDatabase.AssetPathToGUID(path)!=(string)old["guid"]||MonsterBlenderParryR6Builder.Hash(Path.Combine(Project,path)+".meta")!=(string)old["metaHash"]
                    ||!JToken.DeepEquals(OutsideTempo(Fields(ability)),OutsideTempo((JObject)old["fields"])))throw new InvalidOperationException("Native preservation failed: "+path);
                ability.TryGetParryMotionWindow(0,out var window);
                saved.Add(new JObject{["id"]=row["id"],["path"]=path,["guid"]=old["guid"],["fields"]=Fields(ability),["hitCount"]=ability.HitCount,
                    ["firstWindowSeconds"]=ability.ResolvePacedTime(window.y,1)-ability.ResolvePacedTime(window.x,1),
                    ["executionSeconds"]=ability.ResolveExecutionDuration(1),["cueBone"]=ability.ParryCueBonePath});
            }
        }
        catch(Exception error)
        {
            for(int i=0;i<loaded.Length;i++){EditorJsonUtility.FromJsonOverwrite(before[i]["fields"].ToString(),loaded[i]);EditorUtility.SetDirty(loaded[i]);AssetDatabase.SaveAssetIfDirty(loaded[i]);}
            File.WriteAllText(receipt,new JObject{["status"]="FAIL_NATIVE_ROLLBACK",["error"]=error.ToString()}.ToString());throw;
        }
        File.WriteAllText(receipt,new JObject{["status"]="PASS_NATIVE_ATTACK_CUE_TEMPO",["utc"]=DateTime.UtcNow,["actors"]=saved.Count,
            ["damageHits"]=saved.Sum(r=>(int)r["hitCount"]),["saved"]=saved,["guidAndUnrelatedFieldsPreserved"]=true}.ToString());
        return "SAVED_ATTACK_CUE_TEMPO_"+saved.Count;
    }
    static void ConfigureTempo(EnemyAbilityDefinition ability,JToken row)
    {
        var serialized=new SerializedObject(ability);
        foreach(var change in ((JObject)row["changes"]).Properties())
        {
            if(!TempoFields.Contains(change.Name))throw new InvalidDataException(change.Name);
            var property=serialized.FindProperty(change.Name);
            if(property.propertyType==SerializedPropertyType.Boolean)property.boolValue=(bool)change.Value;
            else
            {
                float value=(float)change.Value;if(float.IsNaN(value)||float.IsInfinity(value)||value<=0f)throw new InvalidDataException(change.Name);
                property.floatValue=value;
            }
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if(!ability.UsesPacedTimeline||!ability.TryGetParryMotionWindow(0,out var window)||!ability.FirstStrikeOnlyParry||ability.PreparationEnd>=window.x)throw new InvalidDataException("Ping must follow the reviewed stroke start: "+row["id"]);
        var position=row["cueLocalPosition"];
        ability.ConfigureAttackCue((string)row["cueBonePath"],new Vector3((float)position[0],(float)position[1],(float)position[2]),(float)row["cueScale"]);
        if(ability.ResolvePacedTime(window.y,1)-ability.ResolvePacedTime(window.x,1)<.2999f)throw new InvalidDataException("Baseline post-cue window too short.");
        var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)row["definitionPath"]);
        if(definition==null||definition.ActorPrefab.transform.Find(ability.ParryCueBonePath)==null)throw new InvalidDataException("Reviewed attack bone missing: "+row["id"]);
    }
}
