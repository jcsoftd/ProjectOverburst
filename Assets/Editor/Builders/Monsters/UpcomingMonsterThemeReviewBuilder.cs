using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Upcoming-content review only. Does not edit combat assets or Build Settings.</summary>
public static partial class UpcomingMonsterThemeReviewBuilder
{
    public const string ScenePath="Assets/ProjectOverburst/00_Scenes/DEV_UpcomingMonsterThemes.unity";
    public const string MaterialRoot="Assets/ProjectOverburst/03_Features/Enemies/Showcase/UpcomingThemes/Materials";
    const int DisplayLayer=30;
    static Font font;
    static Dictionary<string,JObject> sources;
    static readonly Dictionary<int,Material> materialCache=new Dictionary<int,Material>();
    static JArray instances;
    static string output;
    static Material floor,baseMat;
    const string PendingOutputKey="OVERBURST.UpcomingThemes.PendingOutput";
    const string PendingLiveKey="OVERBURST.UpcomingThemes.PendingLive";
    static EditorApplication.CallbackFunction pendingBuild;
    static readonly string[] Slots={"small","medium","elite","boss"};
    static readonly string[] SlotNames={"소형","중형","엘리트","보스"};
    static string Workspace=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
    static string LiveRoster=>Path.Combine(Workspace,"개인파일/코덱스산출/Design/20261001_MonsterThemes/data/현재_몬스터_편성.json");

    [InitializeOnLoadMethod]
    static void RegisterPlayGuard()
    {
        EditorApplication.playModeStateChanged-=HideReviewDuringPlay;
        EditorApplication.playModeStateChanged+=HideReviewDuringPlay;
        if(!string.IsNullOrEmpty(SessionState.GetString(PendingOutputKey,"")))SchedulePendingBuild();
    }
    static void HideReviewDuringPlay(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode)return;
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(!scene.IsValid() || !scene.isLoaded)return;
        // Runtime copies only: Edit Mode restores the review scene's saved/manual state on exit.
        foreach(var root in scene.GetRootGameObjects())root.SetActive(false);
    }

    [MenuItem("OVERBURST/Monsters/추후 테마 검토/검토 씬 열기")]
    public static void Open()
    {
        RequireEditMode();
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(!scene.IsValid() || !scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene); Focus(1);
    }
    [MenuItem("OVERBURST/Monsters/추후 테마 검토/현재 JSON으로 다시 배치")]
    public static void RefreshFromJson()
    {
        RequireEditMode();
        if(AssetDatabase.LoadAssetAtPath<EnemyThemeTable>("Assets/ProjectOverburst/Resources/Enemies/Themes/Tables/GraveHunt.asset")!=null)
        {
            RefreshCurrentRoster(Path.Combine(Workspace,"개인파일/코덱스산출/UpcomingMonsterThemes/CurrentRoster",DateTime.Now.ToString("yyyyMMdd_HHmmssfff")));return;
        }
        if(File.Exists(V3SourcePath)){RefreshV3();return;}
        string root=Path.Combine(Workspace,"개인파일/코덱스산출/UpcomingMonsterThemes");
        string plan=Directory.Exists(root)?Directory.GetFiles(root,"native-source-plan.json",SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault():null;
        if(plan==null)throw new InvalidOperationException("검토용 모델 매핑이 없습니다. 보유 모델을 먼저 준비해 주세요.");
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(scene.IsValid() && scene.isLoaded && scene.isDirty)
            throw new InvalidOperationException("검토 씬에 직접 수정한 내용이 있습니다. 이 씬만 저장한 뒤 다시 배치해 주세요.");
        string folder=Path.GetDirectoryName(plan);
        // Preflight resolves every source before touching the existing review scene.
        var roster=JObject.Parse(File.ReadAllText(LiveRoster));
        CheckSources(JObject.Parse(File.ReadAllText(plan)),roster);
        string sceneFile=Path.GetFullPath(Path.Combine(Application.dataPath,"..",ScenePath));
        if(File.Exists(sceneFile))
        {
            string backup=Path.Combine(folder,"scene-backups",DateTime.Now.ToString("yyyyMMdd_HHmmssfff"));
            Directory.CreateDirectory(backup);
            File.Copy(sceneFile,Path.Combine(backup,"DEV_UpcomingMonsterThemes.unity"));
            if(File.Exists(sceneFile+".meta"))File.Copy(sceneFile+".meta",Path.Combine(backup,"DEV_UpcomingMonsterThemes.unity.meta"));
        }
        Queue(folder,true);
    }
    [MenuItem("OVERBURST/Monsters/추후 테마 검토/01 갑각 부화군락")]
    static void Focus1()=>Focus(1);
    [MenuItem("OVERBURST/Monsters/추후 테마 검토/02 독낭 외골격 군락")]
    static void Focus2()=>Focus(2);
    [MenuItem("OVERBURST/Monsters/추후 테마 검토/03 원시 포식자 무리")]
    static void Focus3()=>Focus(3);
    [MenuItem("OVERBURST/Monsters/추후 테마 검토/04 암굴 변이 군락")]
    static void Focus4()=>Focus(4);
    [MenuItem("OVERBURST/Monsters/추후 테마 검토/05 사령의 납골 수확단")]
    static void Focus5()=>Focus(5);
    [MenuItem("OVERBURST/Monsters/추후 테마 검토/06 포자낭 부패습지")]
    static void Focus6()=>Focus(6);
    [MenuItem("OVERBURST/Monsters/추후 테마 검토/07 격리구역의 외계 포식군")]
    static void Focus7()=>Focus(7);
    [MenuItem("OVERBURST/Monsters/추후 테마 검토/08 별도 보스 비교")]
    static void FocusBoss()=>Focus(8);

    public static void Focus(int number)
    {
        RequireEditMode();
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(!scene.IsValid() || !scene.isLoaded)throw new InvalidOperationException("검토 씬을 먼저 여세요.");
        var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name.StartsWith(number.ToString("00")+"_",StringComparison.Ordinal));
        if(root==null)throw new InvalidOperationException("Review section missing: "+number);
        var view=SceneView.lastActiveSceneView;
        if(view==null)view=EditorWindow.GetWindow<SceneView>();
        Selection.activeGameObject=root;
        view.sceneLighting=true;
        view.sceneViewState.showImageEffects=false;
        view.orthographic=false;
        view.LookAtDirect(root.transform.position+Vector3.up*2,Quaternion.Euler(36,0,0),58);
        view.Focus();
        view.Repaint();
    }
    public static string Queue(string outputDirectory,bool live=false)
    {
        RequireEditMode();
        string job=Path.Combine(outputDirectory,"native-scene-job.json");
        if(File.Exists(job) && (string)JObject.Parse(File.ReadAllText(job))["status"]=="RUNNING")
            throw new InvalidOperationException("Review builder is already running.");
        File.WriteAllText(job,new JObject{{"status","QUEUED"}}.ToString());
        SessionState.SetString(PendingOutputKey,outputDirectory);SessionState.SetBool(PendingLiveKey,live);
        SchedulePendingBuild();
        return "QUEUED review scene build";
    }
    static void SchedulePendingBuild()
    {
        if(pendingBuild!=null)EditorApplication.update-=pendingBuild;
        pendingBuild=()=>
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)return;
            string outputDirectory=SessionState.GetString(PendingOutputKey,"");
            bool live=SessionState.GetBool(PendingLiveKey,false);
            EditorApplication.update-=pendingBuild;pendingBuild=null;
            SessionState.EraseString(PendingOutputKey);SessionState.EraseBool(PendingLiveKey);
            if(string.IsNullOrEmpty(outputDirectory))return;
            string job=Path.Combine(outputDirectory,"native-scene-job.json");
            try { Create(outputDirectory,live); }
            catch(Exception e){File.WriteAllText(job,new JObject{{"status","FAIL"},{"error",e.ToString()}}.ToString());Debug.LogException(e);}
        };
        EditorApplication.update+=pendingBuild;EditorApplication.QueuePlayerLoopUpdate();
    }
    static void RequireEditMode()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("검토 씬 작업은 Play를 종료한 에디트 모드에서 실행합니다.");
    }
    static IEnumerable<JObject> Members(JObject roster)=>((JArray)roster["themes"]).OfType<JObject>()
        .SelectMany(t=>((JArray)t["members"]).OfType<JObject>()).Where(m=>(string)m["status"]!="removed");
    static void CheckSources(JObject plan,JObject roster)
    {
        var map=((JArray)plan["models"]).OfType<JObject>().ToDictionary(m=>(string)m["id"]);
        var ids=Members(roster).Select(m=>(string)m["creatureId"])
            .Concat(((JArray)roster["bosses"]).OfType<JObject>().Where(b=>(string)b["status"]!="removed").Select(b=>(string)b["creatureId"])).Distinct();
        var unavailable=new List<string>();
        foreach(string id in ids)
            if(!map.TryGetValue(id,out var m) || AssetDatabase.LoadAssetAtPath<GameObject>((string)m["assetPath"])==null)unavailable.Add(id);
        if(unavailable.Count>0)throw new InvalidOperationException("새로 선택한 모델을 먼저 가져와야 합니다: "+string.Join(", ",unavailable));
    }
    public static string Create(string outputDirectory,bool live=false)
    {
        RequireEditMode();
        if(File.Exists(Path.Combine(outputDirectory,"v3-source.json")))return CreateV3(outputDirectory);
        output=outputDirectory;
        var plan=JObject.Parse(File.ReadAllText(Path.Combine(output,"native-source-plan.json")));
        var roster=JObject.Parse(File.ReadAllText(live?LiveRoster:Path.Combine(output,"roster-source.json")));
        CheckSources(plan,roster);
        UpcomingMonsterThemeReviewSizing.Prepare();
        UpcomingMonsterThemeReviewSizing.WriteEvidence(output);
        var existing=SceneManager.GetSceneByPath(ScenePath);
        if(existing.IsValid() && existing.isLoaded && existing.isDirty)throw new InvalidOperationException("Review scene has unsaved manual changes.");
        var prior=new JArray();
        for(int i=0;i<SceneManager.sceneCount;i++)
        {
            var s=SceneManager.GetSceneAt(i);
            if(s.path!=ScenePath)prior.Add(new JObject{{"path",s.path},{"dirty",s.isDirty},{"roots",s.rootCount}});
        }
        var buildSettings=new JArray(EditorBuildSettings.scenes.Select(s=>new JObject{{"path",s.path},{"enabled",s.enabled}}));
        File.WriteAllText(Path.Combine(output,"native-scene-job.json"),new JObject{{"status","RUNNING"},{"phase","place native models"}}.ToString());
        var scene=existing.IsValid()&&existing.isLoaded?existing:EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        foreach(var g in scene.GetRootGameObjects())Object.DestroyImmediate(g);
        sources=((JArray)plan["models"]).OfType<JObject>().ToDictionary(m=>(string)m["id"]);
        materialCache.Clear();instances=new JArray();
        EnsureFolder(MaterialRoot);
        font=AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/ProjectMT/Source/SpoqaHanSansNeo-Regular.ttf");
        if(font==null)font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        floor=Solid("NeutralFloor",new Color(.14f,.17f,.20f));baseMat=Solid("NeutralPlinth",new Color(.23f,.28f,.31f));
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.68f,.71f,.75f);
        RenderSettings.fog=false;RenderSettings.skybox=null;
        var lighting=new GameObject("00_검토용 조명 · 카메라");
        Light(lighting.transform,"Key",new Vector3(50,-35,0),1.7f);
        Light(lighting.transform,"Fill",new Vector3(30,145,0),.7f);
        int index=0;
        foreach(JObject theme in roster["themes"])
        {
            int number=(int)theme["number"];
            Vector3 origin=new Vector3((index%2)*76,0,(index/2)*49);
            var root=new GameObject(number.ToString("00")+"_"+(string)theme["name"]);root.transform.position=origin;
            Cube("Theme floor",root.transform,new Vector3(0,-.3f,0),new Vector3(61,.3f,31),floor);
            Text("Theme title",root.transform,(string)theme["name"],new Vector3(0,8f,11),1.5f,56,Color.white);
            Text("Scope",root.transform,"추후 업데이트 예정 · 주력 / 예비 비교",new Vector3(0,6.6f,11),.7f,56,new Color(.6f,.85f,.93f));
            var reference=GameObject.CreatePrimitive(PrimitiveType.Capsule);reference.name="1.8m 비교 기준";
            reference.transform.SetParent(root.transform,false);reference.transform.localPosition=new Vector3(-23,.9f,-12.5f);
            reference.transform.localScale=new Vector3(.48f,.9f,.48f);Object.DestroyImmediate(reference.GetComponent<Collider>());
            reference.GetComponent<Renderer>().sharedMaterial=Solid("HeightReference",new Color(.75f,.83f,.87f));
            Text("Height",root.transform,"1.8m",new Vector3(-23,.35f,-14),.5f,5,Color.white);
            var members=((JArray)theme["members"]).OfType<JObject>().Where(m=>(string)m["status"]!="removed").ToArray();
            var core=new GameObject("주력");core.transform.SetParent(root.transform,false);
            for(int slot=0;slot<Slots.Length;slot++)
            {
                var section=new GameObject(SlotNames[slot]+" 후보");section.transform.SetParent(core.transform,false);
                var cards=members.Where(m=>(string)m["status"]=="core" && (string)m["slot"]==Slots[slot]).ToArray();
                float z=slot==0?-8:slot==1?0:8;
                Text("Role",section.transform,SlotNames[slot],new Vector3(slot==3?21:-26,.5f,z-3.1f),.65f,5,RoleColor(slot));
                for(int j=0;j<cards.Length;j++)
                {
                    float x=slot==3?21:slot==2?-15+j*14:-17+j*7;
                    Place(cards[j],section.transform,new Vector3(x,0,z),number,"core");
                }
            }
            var reserve=new GameObject("예비 · 승격 검토");reserve.transform.SetParent(root.transform,false);
            var reserveCards=members.Where(m=>(string)m["status"]=="reserve").ToArray();
            Text("Reserve",reserve.transform,"예비",new Vector3(22,.5f,-12),.65f,8,new Color(1,.73f,.3f));
            for(int j=0;j<reserveCards.Length;j++)
            {
                bool bossSpace=(string)reserveCards[j]["slot"]=="boss" && !members.Any(m=>(string)m["status"]=="core" && (string)m["slot"]=="boss");
                Place(reserveCards[j],reserve.transform,bossSpace?new Vector3(21,0,8):new Vector3(17+j*8,0,-8),number,"reserve");
            }
            ReviewCamera("ReviewCamera_"+number,lighting.transform,origin,26,42);
            index++;
        }
        var bosses=new GameObject("08_별도 보스 비교");bosses.transform.position=new Vector3(76,0,147);
        Cube("Boss floor",bosses.transform,new Vector3(0,-.3f,0),new Vector3(67,.3f,39),floor);
        Text("Title",bosses.transform,"별도 보스 후보 비교",new Vector3(0,9,15),1.3f,56,Color.white);
        Text("Notice",bosses.transform,"압도감 · 실루엣 비교 / 애니메이션 적용은 별도",new Vector3(0,7.3f,15),.6f,56,new Color(.6f,.85f,.93f));
        int bindex=0;
        foreach(JObject boss in ((JArray)roster["bosses"]).OfType<JObject>().Where(b=>(string)b["status"]!="removed"))
        {
            var card=new JObject{{"creatureId",boss["creatureId"]},{"slot","boss"},{"status",boss["status"]}};
            Place(card,bosses.transform,new Vector3(-23+(bindex%4)*15,0,-9+(bindex/4)*15),8,(string)boss["status"]);
            bindex++;
        }
        ReviewCamera("ReviewCamera_8",lighting.transform,bosses.transform.position,35,49);
        ReviewCamera("ReviewCamera_Overview",lighting.transform,new Vector3(38,0,74),145,200);
        var intro=new GameObject("검토 전용 · 편성 변경 후 OVERBURST > Monsters > 추후 테마 검토 > 현재 JSON으로 다시 배치");
        Text("Scene purpose",intro.transform,"OVERBURST · 추후 업데이트 예정 몬스터 테마",new Vector3(38,6,-29),1.5f,125,Color.white);
        Text("Scene guide",intro.transform,"주력 / 예비 / 별도 보스   |   일반·엘리트 현행 크기 / 보스 확대 기준   |   Hierarchy에서 추가·제거 가능",new Vector3(38,3.6f,-29),.65f,125,new Color(.65f,.8f,.87f));
        foreach(var root in scene.GetRootGameObjects()){root.tag="EditorOnly";SetLayer(root);}
        RegisterPlayGuard();
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new IOException("Review scene save failed.");
        File.WriteAllText(Path.Combine(output,"scene-roster-applied.json"),roster.ToString());
        var report=new JObject{{"status","PASS"},{"scene",ScenePath},{"sourceRevision",roster["serverRevision"]??plan["sourceRevision"]},
            {"themeCount",((JArray)roster["themes"]).Count},{"instanceCount",instances.Count},{"instances",instances},
            {"priorScenes",prior},{"buildSettingsBefore",buildSettings},{"runtimeApplied",false}};
        File.WriteAllText(Path.Combine(output,"native-scene-job.json"),report.ToString());
        Focus(1);
        return "PASS: 7 theme review scene, "+instances.Count+" model instances, game contracts untouched";
    }
    static Color RoleColor(int slot)=>slot==0?new Color(.35f,.9f,.8f):slot==1?new Color(.55f,.77f,1):slot==2?new Color(.9f,.6f,1):new Color(1,.63f,.34f);
    static void Place(JObject card,Transform group,Vector3 position,int theme,string status)
    {
        string key=(string)card["creatureId"];var source=sources[key];
        string id=(string)source["creatureId"]??key;string path=(string)source["assetPath"];
        int slot=Array.IndexOf(Slots,(string)card["slot"]);if(slot<0)slot=1;
        string display=(string)source["name"];
        var station=new GameObject("["+(status=="core"?"주력":status=="priority"?"우선 보스":status=="reference"?"현행 비교":"예비")+" · "+SlotNames[slot]+"] "+display);
        station.transform.SetParent(group,false);station.transform.localPosition=position;
        var sourcePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var model=(GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab,station.transform);
        model.name=display+" · 원본 모델";
        foreach(var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))if(behaviour!=null)behaviour.enabled=false;
        foreach(var t in model.GetComponentsInChildren<Transform>(true))GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        foreach(var rb in model.GetComponentsInChildren<Rigidbody>(true)){rb.isKinematic=true;rb.useGravity=false;}
        foreach(var c in model.GetComponentsInChildren<Collider>(true))c.enabled=false;
        foreach(var anim in model.GetComponentsInChildren<Animation>(true))anim.enabled=false;
        AnimationClip idle=source["v3"] is JObject v3Card?ResolveClip(((JArray)v3Card["idle"]).OfType<JObject>().FirstOrDefault()):null;
        foreach(var animator in model.GetComponentsInChildren<Animator>(true))
        {
            animator.enabled=false;
            var clips=animator.runtimeAnimatorController!=null?animator.runtimeAnimatorController.animationClips: Array.Empty<AnimationClip>();
            if(idle==null && source["v3"]==null)idle=clips.FirstOrDefault(c=>c!=null&&c.name.IndexOf("idle",StringComparison.OrdinalIgnoreCase)>=0);
            if(idle==null)
                foreach(string idlePath in ((JArray)source["idlePaths"]).Values<string>())
                {
                    idle=AssetDatabase.LoadAllAssetsAtPath(idlePath).OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal));
                    if(idle!=null)break;
                }
            if(idle!=null)
            {
                Vector3 p=animator.transform.localPosition;Quaternion r=animator.transform.localRotation;Vector3 s=animator.transform.localScale;
                idle.SampleAnimation(animator.gameObject,0);
                animator.transform.localPosition=p;animator.transform.localRotation=r;animator.transform.localScale=s;
            }
        }
        model.transform.localRotation=Quaternion.Euler(0,180,0)*model.transform.localRotation;
        if(id=="death-knight-2")model.transform.localRotation=Quaternion.Euler(0,-90,0)*model.transform.localRotation;
        foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            // The owned legacy Death Skull prefab has two unresolved optional glowing-eye materials.
            // Its publisher reference has dark sockets. Disable these optional meshes in this scene only.
            if(id=="death-skull" && renderer.name.StartsWith("GLOWING_EYE_",StringComparison.Ordinal)
                && renderer.sharedMaterials.Any(m=>m==null))
            {
                renderer.enabled=false;
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m==null?Solid("DeathSkullUnusedEye",new Color(.015f,.012f,.01f)):PreviewMaterial(m)).ToArray();
                continue;
            }
            renderer.sharedMaterials=renderer.sharedMaterials.Select(PreviewMaterial).ToArray();
            if(renderer is SkinnedMeshRenderer skin)skin.updateWhenOffscreen=true;
        }
        Bounds bounds=PhysicalBounds(model);
        var modelScale=UpcomingMonsterThemeReviewSizing.Resolve(sourcePrefab,bounds,slot,theme,out string sizeBasis,out string sizeReference);
        var multiplier=UpcomingMonsterThemeReviewSizing.Divide(modelScale,model.transform.localScale);
        UpcomingMonsterThemeReviewSizing.RequireUniform(multiplier,id);
        float scale=multiplier.x;
        model.transform.localScale=modelScale;
        bounds=PhysicalBounds(model);
        model.transform.position-=new Vector3(bounds.center.x-station.transform.position.x,bounds.min.y-.08f,bounds.center.z-station.transform.position.z);
        bounds=new Bounds(new Vector3(station.transform.position.x,.08f+bounds.size.y*.5f,station.transform.position.z),bounds.size);
        foreach(var component in model.GetComponentsInChildren<Component>(true))
            if(component!=null && PrefabUtility.IsPartOfPrefabInstance(component))PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        float radius=Mathf.Max(.7f,Mathf.Max(bounds.size.x,bounds.size.z)*.52f);
        Cube("Plinth",station.transform,new Vector3(0,-.03f,0),new Vector3(radius*2,.1f,radius*2),baseMat);
        string state=status=="core"?"주력":status=="priority"?"우선 보스":status=="reference"?"현행 비교":"예비";
        Text("Role badge",station.transform,state+" · "+SlotNames[slot],new Vector3(0,.34f,-radius-1.5f),.5f,Mathf.Max(4,radius*2),RoleColor(slot));
        string caption=id=="succubus-sisters-complete-edition"?"Succubus Sisters · 대표 모델":id=="the-rake-forest-beast-collection"?"The Rake":display;
        if(source["v3"] is JObject details){BindV3Station(station,model,key,details,Slots[slot],status,bounds.size);caption=display;}
        Text("Name",station.transform,caption,new Vector3(0,1,-radius-1.5f),.65f,Mathf.Max(6,radius*2),Color.white);
        instances.Add(new JObject{{"key",key},{"id",id},{"theme",theme},{"slot",Slots[slot]},{"status",status},{"source",path},
            {"station",station.name},{"idle",idle==null?"source pose":idle.name},{"displayHeight",bounds.size.y},{"displayWidth",bounds.size.x},{"reviewScale",scale},
            {"modelScale",UpcomingMonsterThemeReviewSizing.Vector(modelScale)},{"displaySize",UpcomingMonsterThemeReviewSizing.Vector(bounds.size)},
            {"sizeBasis",sizeBasis},{"sizeReference",sizeReference}});
    }
    static Bounds PhysicalBounds(GameObject model)
    {
        return UpcomingMonsterThemeReviewSizing.GeometryBounds(model);
    }
    static Material PreviewMaterial(Material source)
    {
        if(source==null)throw new InvalidOperationException("Source material reference is missing.");
        if(materialCache.TryGetValue(source.GetInstanceID(),out var cached))return cached;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long localId);
        string key=string.IsNullOrEmpty(guid)?source.GetInstanceID().ToString():guid+"_"+Math.Abs(localId);
        string path=MaterialRoot+"/Source_"+key+".mat";
        var result=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(result==null)
        {
            var shader=Shader.Find("Universal Render Pipeline/Lit");if(shader==null)throw new InvalidOperationException("URP Lit unavailable.");
            result=new Material(shader){name=source.name+" · Review"};
            string baseProperty=source.HasProperty("_BaseMap")?"_BaseMap":source.HasProperty("_BaseColorMap")?"_BaseColorMap":"_MainTex";
            if(source.HasProperty(baseProperty))
            {
                result.SetTexture("_BaseMap",source.GetTexture(baseProperty));
                result.SetTextureScale("_BaseMap",source.GetTextureScale(baseProperty));result.SetTextureOffset("_BaseMap",source.GetTextureOffset(baseProperty));
            }
            result.SetColor("_BaseColor",source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.HasProperty("_Color")?source.GetColor("_Color"):Color.white);
            foreach(string p in new[]{"_BumpMap","_MetallicGlossMap","_OcclusionMap","_EmissionMap"})if(source.HasProperty(p))result.SetTexture(p,source.GetTexture(p));
            foreach(string p in new[]{"_Metallic","_BumpScale","_OcclusionStrength"})if(source.HasProperty(p))result.SetFloat(p,source.GetFloat(p));
            result.SetFloat("_Smoothness",source.HasProperty("_Smoothness")?Mathf.Min(.5f,source.GetFloat("_Smoothness")):source.HasProperty("_Glossiness")?Mathf.Min(.5f,source.GetFloat("_Glossiness")):.3f);
            if(result.GetTexture("_BumpMap")!=null)result.EnableKeyword("_NORMALMAP");
            if(result.GetTexture("_MetallicGlossMap")!=null)result.EnableKeyword("_METALLICSPECGLOSSMAP");
            if(source.HasProperty("_EmissionColor")){result.SetColor("_EmissionColor",source.GetColor("_EmissionColor"));if(source.GetColor("_EmissionColor").maxColorComponent>0)result.EnableKeyword("_EMISSION");}
            if(source.HasProperty("_Mode") && source.GetFloat("_Mode")==1 || source.HasProperty("_AlphaClip") && source.GetFloat("_AlphaClip")>0)
            {result.SetFloat("_AlphaClip",1);result.EnableKeyword("_ALPHATEST_ON");result.SetFloat("_Cutoff",source.HasProperty("_Cutoff")?source.GetFloat("_Cutoff"):.5f);}
            if(source.HasProperty("_Mode") && source.GetFloat("_Mode")>=2 || source.HasProperty("_Surface") && source.GetFloat("_Surface")>=1)
            {
                result.SetFloat("_Surface",1);result.SetFloat("_Blend",0);result.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
                result.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);result.SetFloat("_ZWrite",0);
                result.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");result.renderQueue=(int)RenderQueue.Transparent;
            }
            if(source.name.IndexOf("hair",StringComparison.OrdinalIgnoreCase)>=0)result.SetFloat("_Cull",0);
            AssetDatabase.CreateAsset(result,path);
        }
        materialCache[source.GetInstanceID()]=result;return result;
    }
    static Material Solid(string name,Color color)
    {
        string path=MaterialRoot+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m!=null)return m;
        m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.22f);
        AssetDatabase.CreateAsset(m,path);return m;
    }
    static void Cube(string name,Transform parent,Vector3 position,Vector3 scale,Material material)
    {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=position;g.transform.localScale=scale;
        Object.DestroyImmediate(g.GetComponent<Collider>());g.GetComponent<Renderer>().sharedMaterial=material;g.layer=DisplayLayer;
    }
    static void Text(string name,Transform parent,string value,Vector3 position,float height,float maxWidth,Color color)
    {
        var text=new GameObject(name).AddComponent<TextMesh>();text.transform.SetParent(parent,false);text.transform.localPosition=position;
        text.font=font;text.fontSize=80;text.characterSize=1;text.text=value;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.color=color;
        var r=text.GetComponent<MeshRenderer>();r.sharedMaterial=font.material;text.gameObject.layer=DisplayLayer;
        float scale=Mathf.Min(height/Mathf.Max(.01f,r.bounds.size.y),maxWidth/Mathf.Max(.01f,r.bounds.size.x));text.transform.localScale=Vector3.one*scale;
    }
    static void Light(Transform parent,string name,Vector3 angles,float intensity)
    {
        var l=new GameObject(name).AddComponent<Light>();l.transform.SetParent(parent,false);l.type=LightType.Directional;l.intensity=intensity;
        l.transform.rotation=Quaternion.Euler(angles);l.shadows=LightShadows.Soft;l.cullingMask=1<<DisplayLayer;
    }
    static void ReviewCamera(string name,Transform parent,Vector3 origin,float height,float distance)
    {
        var c=new GameObject(name).AddComponent<Camera>();c.transform.SetParent(parent,false);c.transform.position=origin+new Vector3(0,height,-distance);
        c.transform.LookAt(origin+Vector3.up*2);c.fieldOfView=42;c.nearClipPlane=.1f;c.farClipPlane=1000;c.clearFlags=CameraClearFlags.SolidColor;
        c.backgroundColor=new Color(.055f,.073f,.092f);c.cullingMask=1<<DisplayLayer;c.enabled=false;
        var data=c.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.volumeLayerMask=0;
    }
    static void SetLayer(GameObject go)
    {
        foreach(var t in go.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.layer=DisplayLayer;
            if(PrefabUtility.IsPartOfPrefabInstance(t.gameObject))PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
        }
    }
    static void EnsureFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }
}
