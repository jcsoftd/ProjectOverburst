using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class UpcomingMonsterThemeReviewBuilder
{
    static string V3SourcePath => Path.Combine(Workspace,"개인파일/코덱스산출/Design/20261003_MonsterOverhaulV3/몬스터_개편안_v3.json");
    public static string V3Output => Path.Combine(Workspace,"개인파일/코덱스산출/UpcomingMonsterThemes/20261003_V3");

    public static AnimationClip ResolveClip(JObject row)
    {
        if(row==null)return null;
        var all=AssetDatabase.LoadAllAssetsAtPath((string)row["sourcePath"]).OfType<AnimationClip>()
            .Where(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal)).ToArray();
        var exact=all.FirstOrDefault(c=>string.Equals(c.name,(string)row["clip"],StringComparison.OrdinalIgnoreCase));
        if(exact!=null)return exact;
        // Single-clip FBXs may retain their exporter take name.
        if(all.Length==1)return all[0];
        throw new InvalidOperationException("선택 클립 연결 실패: "+row["sourcePath"]+" / "+row["clip"]);
    }
    static void RefreshV3()
    {
        RequireEditMode();
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(scene.IsValid() && scene.isLoaded && scene.isDirty)throw new InvalidOperationException("검토 씬을 먼저 저장하세요. 다른 씬은 저장하지 않습니다.");
        UpcomingMonsterReviewWindow.StopAll();
        Directory.CreateDirectory(V3Output);
        string sceneFile=Path.Combine(Application.dataPath,"..",ScenePath);
        var folder=Path.Combine(V3Output,"scene-backups",DateTime.Now.ToString("yyyyMMdd_HHmmssfff"));Directory.CreateDirectory(folder);
        if(File.Exists(sceneFile))File.Copy(sceneFile,Path.Combine(folder,Path.GetFileName(sceneFile)));
        if(File.Exists(sceneFile+".meta"))File.Copy(sceneFile+".meta",Path.Combine(folder,Path.GetFileName(sceneFile)+".meta"));
        File.Copy(V3SourcePath,Path.Combine(V3Output,"v3-source.json"),true);
        Queue(V3Output);
    }
    public static string CreateV3(string outputDirectory)
    {
        try {return CreateV3Core(outputDirectory);}
        catch
        {
            var owned=SceneManager.GetSceneByName("DEV_UpcomingMonsterThemes");
            if(owned.IsValid() && owned.isLoaded){EditorSceneManager.CloseScene(owned,true);EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);}
            throw;
        }
        finally {sources=null;materialCache.Clear();}
    }
    static string CreateV3Core(string outputDirectory)
    {
        RequireEditMode();UpcomingMonsterReviewWindow.StopAll();output=outputDirectory;
        var v3=JObject.Parse(File.ReadAllText(Path.Combine(output,"v3-source.json")));
        foreach(JObject input in v3["inputs"])
        {
            using(var hash=System.Security.Cryptography.SHA256.Create())
            {
                string actual=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes((string)input["path"]))).Replace("-","").ToLowerInvariant();
                if(actual!=(string)input["sha256"])throw new InvalidOperationException("v3 이후 편성/배정이 수정되었습니다. v3를 갱신한 뒤 다시 실행하세요: "+input["path"]);
            }
        }
        var cards=(JObject)v3["cards"];
        string[] keys=((JArray)v3["themes"]).OfType<JObject>().SelectMany(t=>t["entries"].Select(e=>(string)e["key"]))
            .Concat(v3["bosses"].Values<string>()).Distinct().ToArray();
        sources=keys.ToDictionary(k=>k,k=>
        {
            var c=(JObject)cards[k];string path=(string)c["vendor"];
            if(k=="runtime:Boss_UrsKing")
            {
                // Resolve the authored boss visual through the definition, without copying AI.
                foreach(string guid in AssetDatabase.FindAssets("t:EnemyDefinition",new[]{"Assets/ProjectOverburst/Resources/Enemies/Bosses"}))
                {
                    var def=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                    if(def.EnemyId=="Boss_UrsKing" && def.Species?.VendorPrefab!=null){path=AssetDatabase.GetAssetPath(def.Species.VendorPrefab);break;}
                }
            }
            if(AssetDatabase.LoadAssetAtPath<GameObject>(path)==null)throw new InvalidOperationException("모델 연결 실패: "+k+" / "+path);
            foreach(string role in new[]{"idle","move","weak","strong"})foreach(JObject m in c[role])ResolveClip(m);
            return new JObject{{"id",k},{"creatureId",c["id"]},{"name",c["name"]},{"assetPath",path},{"idlePaths",new JArray(c["idle"].Select(m=>m["sourcePath"]))},{"v3",c.DeepClone()}};
        });
        var existing=SceneManager.GetSceneByPath(ScenePath);
        if(existing.IsValid() && existing.isLoaded && existing.isDirty)throw new InvalidOperationException("검토 씬의 미저장 수정이 있습니다.");
        UpcomingMonsterThemeReviewSizing.Prepare();UpcomingMonsterThemeReviewSizing.WriteEvidence(output);
        var prior=new JArray(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i)).Where(s=>s.path!=ScenePath)
            .Select(s=>new JObject{{"path",s.path},{"dirty",s.isDirty},{"roots",s.rootCount}}));
        var buildSettings=new JArray(EditorBuildSettings.scenes.Select(s=>new JObject{{"path",s.path},{"enabled",s.enabled}}));
        File.WriteAllText(Path.Combine(output,"native-source-plan.json"),new JObject{{"version",3},{"models",new JArray(sources.Values.Select(s=>{var c=(JObject)s.DeepClone();c.Remove("v3");return c;}))}}.ToString());
        File.WriteAllText(Path.Combine(output,"native-scene-job.json"),new JObject{{"status","RUNNING"},{"phase","v3 placement"}}.ToString());
        var scene=existing.IsValid() && existing.isLoaded?existing:EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        scene.name="DEV_UpcomingMonsterThemes";SceneManager.SetActiveScene(scene);foreach(var g in scene.GetRootGameObjects())Object.DestroyImmediate(g);
        materialCache.Clear();instances=new JArray();EnsureFolder(MaterialRoot);
        font=AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/ProjectMT/Source/SpoqaHanSansNeo-Regular.ttf")??Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        floor=Solid("NeutralFloor",new Color(.14f,.17f,.20f));baseMat=Solid("NeutralPlinth",new Color(.23f,.28f,.31f));
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.68f,.71f,.75f);RenderSettings.fog=false;RenderSettings.skybox=null;
        var lighting=new GameObject("00_검토용 조명 · 카메라");Light(lighting.transform,"Key",new Vector3(50,-35,0),1.7f);Light(lighting.transform,"Fill",new Vector3(30,145,0),.7f);
        int index=0;
        foreach(JObject theme in v3["themes"])
        {
            int number=(int)theme["number"];var origin=new Vector3((index%2)*104,0,(index/2)*84);
            var root=new GameObject(number.ToString("00")+"_"+theme["name"]);root.transform.position=origin;
            Cube("Theme floor",root.transform,new Vector3(0,-.3f,7),new Vector3(86,.3f,68),floor);
            Text("Theme title",root.transform,number+" · "+theme["name"],new Vector3(0,13,31),1.8f,78,Color.white);
            Text("Scope",root.transform,"v3 · "+theme["concept"],new Vector3(0,10.7f,31),.9f,78,new Color(.6f,.85f,.93f));
            var entries=theme["entries"].OfType<JObject>().ToArray();
            for(int slot=0;slot<4;slot++)
            {
                var section=new GameObject(SlotNames[slot]);section.transform.SetParent(root.transform,false);
                var rows=entries.Where(e=>(string)e["grade"]==Slots[slot]).ToArray();float z=slot==0?-17:slot==1?-3:slot==2?12:27;
                if(rows.Length==0)continue;
                float spacing=slot==0?9:slot==1?10:14;
                Text("Role",section.transform,SlotNames[slot],new Vector3(-38,.6f,z-5),.8f,8,RoleColor(slot));
                for(int j=0;j<rows.Length;j++)Place(new JObject{{"creatureId",rows[j]["key"]},{"slot",Slots[slot]}},section.transform,
                    new Vector3(slot==3?27:(j-(rows.Length-1)*.5f)*spacing,0,z),number,(string)rows[j]["status"]);
            }
            AddHeightReference(root.transform,new Vector3(-36,0,-22));
            ReviewCamera("ReviewCamera_"+number,lighting.transform,origin+new Vector3(0,0,7),45,68);index++;
        }
        var bossRoot=new GameObject("08_별도 보스 비교");bossRoot.transform.position=new Vector3(104,0,252);
        Cube("Boss floor",bossRoot.transform,new Vector3(0,-.3f,2),new Vector3(86,.3f,66),floor);
        Text("Title",bossRoot.transform,"보스 7후보 + 현행 Urs King 비교",new Vector3(0,13,28),1.7f,80,Color.white);
        Text("Notice",bossRoot.transform,"2~3종 선정 전 · 서큐버스 전용 모션 없음",new Vector3(0,10.7f,28),.9f,78,new Color(.6f,.85f,.93f));
        int bi=0;foreach(string key in v3["bosses"].Values<string>())
        {
            Place(new JObject{{"creatureId",key},{"slot","boss"}},bossRoot.transform,new Vector3(-27+(bi%4)*18,0,-15+(bi/4)*25),8,key.StartsWith("runtime:")?"reference":(string)cards[key]["bossStatus"]??"reserve");bi++;
        }
        AddHeightReference(bossRoot.transform,new Vector3(-38,0,-24));ReviewCamera("ReviewCamera_8",lighting.transform,bossRoot.transform.position,48,75);
        ReviewCamera("ReviewCamera_Overview",lighting.transform,new Vector3(52,0,126),250,335);
        foreach(var root in scene.GetRootGameObjects()){root.tag="EditorOnly";SetLayer(root);}
        RegisterPlayGuard();EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new IOException("검토 씬 저장 실패");
        var report=new JObject{{"status","PASS"},{"version",3},{"scene",ScenePath},{"sourceRevision",v3["themeRevision"]},{"weakRevision",v3["weakRevision"]},
            {"themeCount",7},{"instanceCount",instances.Count},{"instances",instances},{"priorScenes",prior},{"buildSettingsBefore",buildSettings},{"runtimeApplied",false}};
        File.WriteAllText(Path.Combine(output,"native-scene-job.json"),report.ToString());
        sources=null;materialCache.Clear();Focus(1);return "PASS: v3 showcase "+instances.Count+" stations";
    }
    static void AddHeightReference(Transform parent,Vector3 position)
    {
        var r=GameObject.CreatePrimitive(PrimitiveType.Capsule);r.name="1.8m 비교 기준";r.transform.SetParent(parent,false);
        r.transform.localPosition=position+Vector3.up*.9f;r.transform.localScale=new Vector3(.48f,.9f,.48f);
        Object.DestroyImmediate(r.GetComponent<Collider>());r.GetComponent<Renderer>().sharedMaterial=Solid("HeightReference",new Color(.75f,.83f,.87f));
        Text("Height",parent,"1.8m",position+new Vector3(0,.4f,-2),.6f,5,Color.white);
    }
    static void BindV3Station(GameObject station,GameObject model,string key,JObject card,string grade,string status,Vector3 size)
    {
        var data=station.AddComponent<UpcomingMonsterReviewStation>();data.cardKey=key;data.displayName=(string)card["name"];data.model=model;data.grade=grade;data.selectionStatus=status;data.displaySize=size;
        data.warnings=string.Join("\n",card["warnings"].Values<string>());data.motions=new[]{"idle","move","weak","strong"}.SelectMany(role=>card[role].OfType<JObject>().Select(m=>new UpcomingMonsterReviewStation.Motion
        {
            role=role,clip=ResolveClip(m),concept=(string)m["concept"]??(string)m["description"],count=(string)m["countText"],connection=(string)m["connection"],
            selectionState=(string)m["selectionState"]??(string)m["stateLabel"],parryable=(bool?)m["parryable"]??false,needsTrim=(bool?)m["needsTrim"]??false
        })).ToArray();
        string summary="Idle "+card["idle"].Count()+" / 이동 "+card["move"].Count()+" / 약공 "+card["weak"].Count()+" / 강공 "+card["strong"].Count();
        float radius=Mathf.Max(.7f,Mathf.Max(size.x,size.z)*.52f);
        Text("Motion summary",station.transform,summary,new Vector3(0,1.7f,-radius-1.5f),.34f,9,new Color(.7f,.86f,.91f));
        if(!string.IsNullOrEmpty(data.warnings))Text("Review pending",station.transform,"검토 항목 있음 · 모션 창에서 확인",new Vector3(0,2.2f,-radius-1.5f),.32f,9,new Color(1,.74f,.36f));
    }
}
