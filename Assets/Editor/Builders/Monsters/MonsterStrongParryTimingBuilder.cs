using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Saves reviewed regular-monster parry windows and the approved pacing revision.
// Animation clips and every field outside the explicit revision are preserved.
public static class MonsterStrongParryTimingBuilder
{
    static string Project => Directory.GetParent(Application.dataPath).FullName;
    static string Workspace => Directory.GetParent(Project).FullName;
    static string Output(string relative)
    {
        string path = Path.GetFullPath(Path.Combine(Workspace, relative));
        string allowed = Path.GetFullPath(Path.Combine(Workspace, "개인파일/코덱스산출/Monsters")) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Private monster output required.");
        return path;
    }
    static JObject Fields(EnemyAbilityDefinition ability) => JObject.Parse(EditorJsonUtility.ToJson(ability));
    static JObject Preserved(JObject fields)
    {
        var value = (JObject)fields.DeepClone();
        ((JObject)(value["MonoBehaviour"] ?? value)).Remove("parryMotionWindows"); return value;
    }
    static EnemyAbilityDefinition Load(string path)
    {
        if (!path.StartsWith("Assets/ProjectOverburst/Resources/Enemies/Themes/Abilities/", StringComparison.Ordinal))
            throw new ArgumentException("Regular theme strong required.");
        var ability = AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(path);
        if (ability == null || !ability.IsParryable || EditorUtility.IsDirty(ability))
            throw new InvalidOperationException("Missing, non-parryable or unsaved strong: " + path);
        return ability;
    }
    public static string Snapshot(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string directory = Output(outputRelative);
        var input = JObject.Parse(File.ReadAllText(Path.Combine(directory, "data/apply-input.json")));
        var records = new JArray();
        foreach (var row in input["records"])
        {
            string path = (string)row["ability"]["path"];
            var ability = Load(path); var windows = new JArray(); var hits = new JArray();
            for (int i = 0; i < ability.HitCount; i++)
            {
                float hit = ability.GetHitNormalizedTime(i); hits.Add(hit);
                bool authored = ability.TryGetParryMotionWindow(i, out var w);
                windows.Add(new JObject { ["strike"] = i + 1, ["authored"] = authored,
                    ["start"] = authored ? (JToken)w.x : null, ["end"] = authored ? (JToken)w.y : null });
            }
            records.Add(new JObject { ["id"] = row["id"], ["name"] = row["name"], ["path"] = path,
                ["guid"] = AssetDatabase.AssetPathToGUID(path), ["sha256"] = MonsterBlenderParryR6Builder.Hash(Path.Combine(Project,path)),
                ["fields"] = Fields(ability), ["hits"] = hits, ["windows"] = windows,
                ["speedAtOne"] = new JObject { ["preparation"] = ability.ResolvePhaseAnimationSpeed(0,1),
                    ["release"] = ability.ResolvePhaseAnimationSpeed(ability.HitNormalizedTime-.001f,1),
                    ["recovery"] = ability.ResolvePhaseAnimationSpeed(ability.GetHitNormalizedTime(ability.HitCount-1)+.001f,1) } });
        }
        string dest = Path.Combine(directory,"TimingAfterStroke/snapshot-before.json");
        if (File.Exists(dest)) throw new InvalidOperationException("Keep the previous native snapshot.");
        File.WriteAllText(dest,new JObject { ["utc"] = DateTime.UtcNow, ["status"] = "NATIVE_BEFORE", ["records"] = records }.ToString());
        return "NATIVE_SNAPSHOT_" + records.Count;
    }
    public static string Apply(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string directory = Output(outputRelative), timing = Path.Combine(directory,"TimingAfterStroke");
        string receipt = Path.Combine(timing,"apply-result.json");
        if (File.Exists(receipt)) throw new InvalidOperationException("Inspect the existing receipt before repeating saves.");
        var before = JObject.Parse(File.ReadAllText(Path.Combine(timing,"snapshot-before.json")));
        var plan = JObject.Parse(File.ReadAllText(Path.Combine(timing,"timing-plan.json")));
        if (plan["records"].Count()!=35 || before["records"].Count()!=35) throw new InvalidDataException("Complete reviewed roster required.");
        var pending = plan["records"].Where(r => (bool)r["apply"]).ToArray();
        var loaded = pending.Select(row => Load((string)row["abilityPath"])).ToArray();
        for (int i=0;i<pending.Length;i++)
        {
            var row=pending[i];var original=before["records"].Single(x=>(string)x["id"]==(string)row["id"]);
            string path=(string)row["abilityPath"];
            if (AssetDatabase.AssetPathToGUID(path)!=(string)original["guid"]
                || MonsterBlenderParryR6Builder.Hash(Path.Combine(Project,path))!=(string)original["sha256"]
                || !JToken.DeepEquals(Fields(loaded[i]),original["fields"])) throw new InvalidOperationException("Changed native input: "+path);
            var copy=UnityEngine.Object.Instantiate(loaded[i]);
            try { copy.ConfigureParryMotionWindows(row["strikes"].Select(s=>new Vector2((float)s["pingPhase"],(float)s["hitPhase"])).ToArray()); }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
            string backup=Path.Combine(timing,"Recovery/Before",path);Directory.CreateDirectory(Path.GetDirectoryName(backup));
            File.Copy(Path.Combine(Project,path),backup,false);File.Copy(Path.Combine(Project,path)+".meta",backup+".meta",false);
        }
        var saved=new JArray();
        try
        {
            for (int i=0;i<pending.Length;i++)
            {
                var row=pending[i];var ability=loaded[i];string path=(string)row["abilityPath"];
                var old=before["records"].Single(x=>(string)x["id"]==(string)row["id"]);
                Undo.RecordObject(ability,"강공 실제 동작 이후 패링 구간");
                ability.ConfigureParryMotionWindows(row["strikes"].Select(s=>new Vector2((float)s["pingPhase"],(float)s["hitPhase"])).ToArray());
                if (!JToken.DeepEquals(Preserved(Fields(ability)),Preserved((JObject)old["fields"])))
                    throw new InvalidOperationException("A field outside parry windows changed: "+path);
                EditorUtility.SetDirty(ability);AssetDatabase.SaveAssetIfDirty(ability);
                saved.Add(new JObject { ["id"] = row["id"], ["path"] = path, ["guid"] = AssetDatabase.AssetPathToGUID(path),
                    ["sha256"] = MonsterBlenderParryR6Builder.Hash(Path.Combine(Project,path)), ["fields"] = Fields(ability),
                    ["strikes"] = row["strikes"].DeepClone() });
            }
        }
        catch (Exception error)
        {
            foreach (var ability in loaded)
            {
                var old=before["records"].Single(x=>(string)x["path"]==AssetDatabase.GetAssetPath(ability));
                EditorJsonUtility.FromJsonOverwrite(old["fields"].ToString(),ability);EditorUtility.SetDirty(ability);AssetDatabase.SaveAssetIfDirty(ability);
            }
            File.WriteAllText(receipt,new JObject { ["status"]="FAIL_ROLLED_BACK_NATIVE",["error"]=error.ToString() }.ToString());throw;
        }
        File.WriteAllText(receipt,new JObject { ["status"]="PASS_NATIVE_WINDOWS_ONLY",["utc"]=DateTime.UtcNow,
            ["savedActors"]=saved.Count,["heldActors"]=35-saved.Count,["saved"]=saved,["hitTimesModified"]=false,
            ["pacingModified"]=false,["animationClipsModified"]=false }.ToString());
        return "SAVED_WINDOWS_"+saved.Count;
    }

    public static string SnapshotSlowdown(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string directory=Output(outputRelative), target=Path.Combine(directory,"FirstStrikeSlowdown/snapshot-before.json");
        if(File.Exists(target))throw new InvalidOperationException("Keep the existing native snapshot.");
        var input=JObject.Parse(File.ReadAllText(Path.Combine(directory,"data/apply-input.json")));
        var records=new JArray();
        foreach(var row in input["records"])
        {
            string path=(string)row["ability"]["path"];var ability=Load(path);
            records.Add(new JObject { ["id"]=row["id"],["path"]=path,["guid"]=AssetDatabase.AssetPathToGUID(path),
                ["sha256"]=MonsterBlenderParryR6Builder.Hash(Path.Combine(Project,path)),
                ["metaSha256"]=MonsterBlenderParryR6Builder.Hash(Path.Combine(Project,path)+".meta"),["fields"]=Fields(ability),
                ["hitCount"]=ability.HitCount,["firstImpactAtSpeedOne"]=ability.ResolveFirstImpactTime(1),
                ["executionAtSpeedOne"]=ability.ResolveExecutionDuration(1) });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        File.WriteAllText(target,new JObject{["utc"]=DateTime.UtcNow,["records"]=records}.ToString());
        return "SLOWDOWN_SNAPSHOT_"+records.Count;
    }

    static readonly string[] RevisionFields={"parryMotionWindows","firstStrikeOnlyParry","preparationDuration",
        "releaseDuration","recoveryDuration","minimumRecoveryTime","hitDelay","hitNormalizedTime"};
    static JObject OutsideRevision(JObject fields)
    {
        var value=(JObject)fields.DeepClone();var body=(JObject)(value["MonoBehaviour"]??value);
        foreach(string name in RevisionFields)body.Remove(name);return value;
    }
    static void ConfigureRevision(EnemyAbilityDefinition ability,JToken row)
    {
        var serialized=new SerializedObject(ability);
        foreach(var change in ((JObject)row["changes"]).Properties())
        {
            if(!RevisionFields.Contains(change.Name)||change.Name=="parryMotionWindows"||change.Name=="firstStrikeOnlyParry")
                throw new InvalidDataException("Unapproved field: "+change.Name);
            var property=serialized.FindProperty(change.Name);
            if(property==null||property.propertyType!=SerializedPropertyType.Float)throw new InvalidDataException(change.Name);
            float value=(float)change.Value;
            if(float.IsNaN(value)||float.IsInfinity(value)||value<=0)throw new InvalidDataException(change.Name);
            property.floatValue=value;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        ability.ConfigureFirstStrikeOnlyParry(true);
        ability.ConfigureParryMotionWindows(new Vector2((float)row["pingPhase"],ability.GetHitNormalizedTime(0)));
    }
    public static string ApplySlowdown(string outputRelative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string directory=Path.Combine(Output(outputRelative),"FirstStrikeSlowdown"), receipt=Path.Combine(directory,"apply-result.json");
        if(File.Exists(receipt))throw new InvalidOperationException("Inspect the existing receipt before repeating saves.");
        var before=JObject.Parse(File.ReadAllText(Path.Combine(directory,"snapshot-before.json")));
        var plan=JObject.Parse(File.ReadAllText(Path.Combine(directory,"plan.json")));
        var rows=plan["records"].ToArray();
        if(rows.Length!=35||before["records"].Count()!=35||rows.Select(r=>(string)r["id"]).Distinct().Count()!=35)
            throw new InvalidDataException("Complete reviewed roster required.");
        var loaded=rows.Select(row=>Load((string)row["abilityPath"])).ToArray();
        for(int i=0;i<rows.Length;i++)
        {
            var row=rows[i];var old=before["records"].Single(x=>(string)x["id"]==(string)row["id"]);string path=(string)row["abilityPath"];
            if(path!=(string)old["path"]||AssetDatabase.AssetPathToGUID(path)!=(string)old["guid"]
                ||MonsterBlenderParryR6Builder.Hash(Path.Combine(Project,path))!=(string)old["sha256"]
                ||!JToken.DeepEquals(Fields(loaded[i]),old["fields"]))throw new InvalidOperationException("Changed native input: "+path);
            var copy=UnityEngine.Object.Instantiate(loaded[i]);copy.name=loaded[i].name;
            try
            {
                ConfigureRevision(copy,row);
                if(copy.HitCount!=(int)old["hitCount"]||!copy.HasParryMotionWindows||copy.ParryStrikeCount!=1
                    ||!JToken.DeepEquals(OutsideRevision(Fields(copy)),OutsideRevision((JObject)old["fields"])))
                    throw new InvalidOperationException("Revision changed unrelated data: "+path);
            }
            finally{UnityEngine.Object.DestroyImmediate(copy);}
            string backup=Path.Combine(directory,"Recovery/Before",path);Directory.CreateDirectory(Path.GetDirectoryName(backup));
            File.Copy(Path.Combine(Project,path),backup,false);File.Copy(Path.Combine(Project,path)+".meta",backup+".meta",false);
        }
        var saved=new JArray();
        try
        {
            for(int i=0;i<rows.Length;i++)
            {
                var row=rows[i];var ability=loaded[i];string path=(string)row["abilityPath"];
                var old=before["records"].Single(x=>(string)x["id"]==(string)row["id"]);
                Undo.RecordObject(ability,"강공 감속 및 첫 타만 패링");ConfigureRevision(ability,row);
                EditorUtility.SetDirty(ability);AssetDatabase.SaveAssetIfDirty(ability);
                if(AssetDatabase.AssetPathToGUID(path)!=(string)old["guid"]
                    ||MonsterBlenderParryR6Builder.Hash(Path.Combine(Project,path)+".meta")!=(string)old["metaSha256"]
                    ||!JToken.DeepEquals(OutsideRevision(Fields(ability)),OutsideRevision((JObject)old["fields"])))
                    throw new InvalidOperationException("Native preservation failed: "+path);
                ability.TryGetParryMotionWindow(0,out var window);
                for(int strike=1;strike<ability.HitCount;strike++)
                    if(ability.TryGetParryMotionWindow(strike,out _))throw new InvalidOperationException("Later strike permits parry: "+path);
                saved.Add(new JObject{["id"]=row["id"],["path"]=path,["guid"]=AssetDatabase.AssetPathToGUID(path),
                    ["sha256"]=MonsterBlenderParryR6Builder.Hash(Path.Combine(Project,path)),["fields"]=Fields(ability),
                    ["hitCount"]=ability.HitCount,["parryStrikeCount"]=ability.ParryStrikeCount,
                    ["firstImpactAtSpeedOne"]=ability.ResolveFirstImpactTime(1),["executionAtSpeedOne"]=ability.ResolveExecutionDuration(1),
                    ["parryWindowAtSpeedOne"]=ability.ResolvePacedTime(window.y,1)-ability.ResolvePacedTime(window.x,1),
                    ["speedAtOne"]=new JObject{["preparation"]=ability.ResolvePhaseAnimationSpeed(0,1),
                        ["release"]=ability.ResolvePhaseAnimationSpeed(ability.HitNormalizedTime-.001f,1),
                        ["recovery"]=ability.ResolvePhaseAnimationSpeed(ability.GetHitNormalizedTime(ability.HitCount-1)+.001f,1)}});
            }
        }
        catch(Exception error)
        {
            foreach(var ability in loaded)
            {
                var old=before["records"].Single(x=>(string)x["path"]==AssetDatabase.GetAssetPath(ability));
                EditorJsonUtility.FromJsonOverwrite(old["fields"].ToString(),ability);EditorUtility.SetDirty(ability);AssetDatabase.SaveAssetIfDirty(ability);
            }
            File.WriteAllText(receipt,new JObject{["status"]="FAIL_ROLLED_BACK_NATIVE",["error"]=error.ToString()}.ToString());throw;
        }
        File.WriteAllText(receipt,new JObject{["status"]="PASS_NATIVE_FIRST_ONLY_SLOWDOWN",["utc"]=DateTime.UtcNow,
            ["savedActors"]=saved.Count,["damageHits"]=saved.Sum(r=>(int)r["hitCount"]),["saved"]=saved,
            ["animationClipsModified"]=false,["weakAttacksModified"]=false,["guidAndMetaPreserved"]=true}.ToString());
        return "SAVED_FIRST_ONLY_SLOWDOWN_"+saved.Count;
    }
}
