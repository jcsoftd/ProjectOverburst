using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

public static partial class UpcomingMonsterThemeReviewBuilder
{
    // Rebuild the owned theme exhibit from saved runtime assets; preserve its separate boss exhibit.
    public static string RefreshCurrentRoster(string directory)
    {
        RequireEditMode();
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))throw new Exception("Idle Editor required");
        var existing=SceneManager.GetSceneByPath(ScenePath);
        if(existing.IsValid()&&existing.isLoaded&&existing.isDirty)throw new Exception("Unsaved showcase edits must be preserved");
        var tables=new[]{"SpiderBrood","VenomBrood","PrimalHunt","CavernMutants","DeathHarvest","RotsporeMarsh","AlienContainment","GraveHunt"}
            .Select(id=>AssetDatabase.LoadAssetAtPath<EnemyThemeTable>("Assets/ProjectOverburst/Resources/Enemies/Themes/Tables/"+id+".asset")).ToArray();
        if(tables.Any(t=>t==null||!t.Validate(out _))||tables.Sum(t=>t.Entries.Count)!=63)throw new Exception("Eight valid themes / 63 actors required");
        Directory.CreateDirectory(directory);string project=Directory.GetParent(Application.dataPath).FullName;
        foreach(string suffix in new[]{"",".meta"})File.Copy(Path.Combine(project,ScenePath+suffix),Path.Combine(directory,"showcase-before"+suffix+(suffix==""?".unity":"")),false);
        var previousActive=SceneManager.GetActiveScene();bool wasOpen=existing.IsValid()&&existing.isLoaded;
        var scene=wasOpen?existing:EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);
        RenderSettings.fog=false;
        UpcomingMonsterReviewWindow.StopAll();materialCache.Clear();
        font=AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/ProjectMT/Source/SpoqaHanSansNeo-Regular.ttf")??Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        floor=Solid("NeutralFloor",new Color(.14f,.17f,.20f));baseMat=Solid("NeutralPlinth",new Color(.23f,.28f,.31f));
        var roots=scene.GetRootGameObjects();var boss=roots.FirstOrDefault(r=>r.name.Contains("별도 보스 비교"));
        if(boss!=null){boss.name="09_별도 보스 비교";boss.transform.position=new Vector3(0,0,336);}
        foreach(var root in roots.Where(r=>r!=boss&&r.name.Length>2&&r.name.Substring(0,2)!="00"&&char.IsDigit(r.name[0])&&char.IsDigit(r.name[1])&&r.name[2]=='_'))UnityEngine.Object.DestroyImmediate(root);
        var lighting=scene.GetRootGameObjects().First(r=>r.name.Contains("조명"));
        foreach(var camera in lighting.GetComponentsInChildren<Camera>(true)){
            if(camera.name=="ReviewCamera_8"){camera.name="ReviewCamera_9";camera.transform.position+=new Vector3(-104,0,84);}
            else if(camera.name!="ReviewCamera_Overview")UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }
        var stations=new JArray();
        try{
            for(int index=0;index<tables.Length;index++){
                var table=tables[index];int number=index+1;var origin=new Vector3((index%2)*104,0,(index/2)*84);
                var theme=new GameObject(number.ToString("00")+"_"+table.DisplayName);theme.transform.position=origin;theme.tag="EditorOnly";
                Cube("Theme floor",theme.transform,new Vector3(0,-.3f,7),new Vector3(86,.3f,68),floor);
                Text("Theme title",theme.transform,number+" · "+table.DisplayName,new Vector3(0,13,31),1.8f,78,Color.white);
                Text("Scope",theme.transform,"현재 게임 편성 · "+table.Entries.Count+"종",new Vector3(0,10.7f,31),.9f,78,new Color(.6f,.85f,.93f));
                for(int tier=0;tier<3;tier++){
                    var entries=table.Entries.Where(e=>(int)e.tier==tier).ToArray();float z=tier==0?-17:tier==1?-3:12,spacing=tier==0?9:tier==1?10:14;
                    Text("Role",theme.transform,SlotNames[tier],new Vector3(-38,.6f,z-5),.8f,8,RoleColor(tier));
                    for(int i=0;i<entries.Length;i++){
                        var d=entries[i].definition;var station=new GameObject(d.EnemyId);station.transform.SetParent(theme.transform,false);station.transform.localPosition=new Vector3((i-(entries.Length-1)*.5f)*spacing,0,z);
                        var actor=UnityEngine.Object.Instantiate(d.ActorPrefab.gameObject,station.transform);actor.name="Saved game visual";
                        foreach(var b in actor.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;foreach(var c in actor.GetComponentsInChildren<Collider>(true))c.enabled=false;
                        actor.SetActive(true);actor.transform.localRotation=Quaternion.Euler(0,180,0);
                        var animator=actor.GetComponentInChildren<Animator>(true);animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                        var graph=PlayableGraph.Create("Current theme idle");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);try{var p=AnimationClipPlayable.Create(graph,d.AnimationProfile.Idle);p.SetApplyFootIK(false);var o=AnimationPlayableOutput.Create(graph,"Idle",animator);o.SetSourcePlayable(p);graph.Play();graph.Evaluate(.00001f);}finally{graph.Destroy();}
                        var data=station.AddComponent<UpcomingMonsterReviewStation>();data.cardKey="runtime:"+d.EnemyId;data.displayName=d.DisplayName;data.model=actor;data.grade=Slots[tier];data.selectionStatus="runtime";data.displaySize=UpcomingMonsterThemeReviewSizing.GeometryBounds(actor).size;
                        var motions=new System.Collections.Generic.List<UpcomingMonsterReviewStation.Motion>{new UpcomingMonsterReviewStation.Motion{role="idle",clip=d.AnimationProfile.Idle,concept="실제 게임 대기"},new UpcomingMonsterReviewStation.Motion{role="move",clip=d.AnimationProfile.Walk,concept="실제 게임 보행"},new UpcomingMonsterReviewStation.Motion{role="move",clip=d.AnimationProfile.Run,concept="실제 게임 달리기"}};
                        for(int a=0;a<d.AbilitySet.Count;a++){var ability=d.AbilitySet.GetAbility(a);motions.Add(new UpcomingMonsterReviewStation.Motion{role=ability.IsTelegraphedStrongAttack?"strong":"weak",clip=ability.WeakAttackExecution?.RuntimeClip??d.AnimationProfile.GetAttackClip(a),concept=ability.WeakAttackExecution?.MotionPolicy.ToString()??"확정 강공",count=ability.HitCount+"타",parryable=ability.IsParryable,connection=ability.AnimatorTrigger});}
                        data.motions=motions.ToArray();data.warnings=d.EnemyId.StartsWith("GraveHunt_")&&tier>0?"전용 승인 패링 3종 미제작 · 공용 반응 처리":"";
                        Text("Name",station.transform,d.DisplayName,new Vector3(0,.5f,-2.5f),.6f,11,Color.white);
                        Text("Motions",station.transform,"약공 "+Enumerable.Range(0,d.AbilitySet.Count).Count(a=>!d.AbilitySet.GetAbility(a).IsTelegraphedStrongAttack)+" / 강공 "+Enumerable.Range(0,d.AbilitySet.Count).Count(a=>d.AbilitySet.GetAbility(a).IsTelegraphedStrongAttack),new Vector3(0,.4f,-3.5f),.38f,11,new Color(.7f,.84f,.9f));
                        SetLayer(station);stations.Add(new JObject{["id"]=d.EnemyId,["theme"]=table.ThemeId,["tier"]=Slots[tier],["size"]=new JArray(data.displaySize.x,data.displaySize.y,data.displaySize.z)});
                    }
                }
                AddHeightReference(theme.transform,new Vector3(-36,0,-22));SetLayer(theme);ReviewCamera("ReviewCamera_"+number,lighting.transform,origin+new Vector3(0,0,7),45,68);
            }
            foreach(var root in scene.GetRootGameObjects())root.tag="EditorOnly";
            EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new Exception("Showcase save failed");
            File.WriteAllText(Path.Combine(directory,"showcase-result.json"),new JObject{["status"]="PASS_SAVED_SHOWCASE",["scene"]=ScenePath,["themes"]=8,["actors"]=stations.Count,["stations"]=stations,["bossExhibitPreserved"]=boss!=null}.ToString());
        }finally{materialCache.Clear();if(previousActive.IsValid()&&previousActive.isLoaded)SceneManager.SetActiveScene(previousActive);if(!wasOpen&&!scene.isDirty)EditorSceneManager.CloseScene(scene,true);}
        return "PASS_CURRENT_SHOWCASE_8_THEMES_63_ACTORS";
    }
}
