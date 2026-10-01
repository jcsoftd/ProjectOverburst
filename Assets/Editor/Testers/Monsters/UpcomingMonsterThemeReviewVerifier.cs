using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class UpcomingMonsterThemeReviewVerifier
{
    const string ScenePath="Assets/ProjectOverburst/00_Scenes/DEV_UpcomingMonsterThemes.unity";
    public static string Run(string outputDirectory,bool reload=true)
    {
        if(EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Verifier requires idle Edit Mode.");
        var before=JObject.Parse(File.ReadAllText(Path.Combine(outputDirectory,"native-scene-job.json")));
        if((string)before["status"]!="PASS")throw new InvalidOperationException("Review build is not complete; inspect native-scene-job.json before validation.");
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(!scene.IsValid() || !scene.isLoaded)throw new InvalidOperationException("Review scene is not loaded.");
        if(scene.isDirty)throw new InvalidOperationException("Review scene has unsaved changes; validation will not save or discard them.");
        if(reload){EditorSceneManager.CloseScene(scene,true);scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);}
        bool gameSizes=before["instances"].Any(m=>m["modelScale"]!=null);
        if(gameSizes)UpcomingMonsterThemeReviewSizing.Prepare();
        var roots=scene.GetRootGameObjects();var objects=roots.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject).ToArray();
        var failures=new JArray();int checks=0;
        Action<bool,string> check=(passed,label)=>{checks++;if(!passed)failures.Add(label);};
        check(!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(ScenePath)),"scene GUID");
        check(roots.Count(r=>r.name.Length>=3 && r.name[2]=='_' && r.name.Substring(0,2)!="00")==8,"seven themes and independent boss section");
        check(objects.Count(g=>g.name.StartsWith("[",StringComparison.Ordinal))==(int)before["instanceCount"],"saved model instance count");
        foreach(var root in roots)check(root.CompareTag("EditorOnly"),"Editor-only review root: "+root.name);
        foreach(var station in objects.Where(g=>g.name.StartsWith("[",StringComparison.Ordinal)))
        {
            var expected=((JArray)before["instances"]).OfType<JObject>().FirstOrDefault(m=>(string)m["station"]==station.name);
            check(expected!=null,"station mapped to roster: "+station.name);
            if(expected!=null && station.transform.childCount>0)
            {
                var model=station.transform.GetChild(0).gameObject;
                // Preserve the selected variant, including its material/skin overrides.
                check(PrefabUtility.IsPartOfPrefabInstance(model) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(model)==(string)expected["source"],"prefab connection: "+station.name);
                if(gameSizes && expected["modelScale"] is JArray recorded)
                {
                    var scale=new Vector3((float)recorded[0],(float)recorded[1],(float)recorded[2]);
                    check(Vector3.Distance(model.transform.localScale,scale)<.0001f,"saved display scale: "+station.name);
                    var source=AssetDatabase.LoadAssetAtPath<GameObject>((string)expected["source"]);
                    var multiplier=UpcomingMonsterThemeReviewSizing.Divide(model.transform.localScale,source.transform.localScale);
                    var bounds=UpcomingMonsterThemeReviewSizing.GeometryBounds(model);
                    var raw=new Bounds(Vector3.zero,bounds.size/multiplier.x);
                    int slot=Array.IndexOf(new[]{"small","medium","elite","boss"},(string)expected["slot"]);
                    var target=UpcomingMonsterThemeReviewSizing.Resolve(source,raw,slot,(int)expected["theme"],out string basis,out string reference);
                    check(Vector3.Distance(model.transform.localScale,target)<.0001f,"current game size rule: "+station.name);
                    check(basis==(string)expected["sizeBasis"] && reference==(string)expected["sizeReference"],"current game reference: "+station.name);
                    check(Mathf.Abs(bounds.size.y-(float)expected["displayHeight"])<.001f,"saved mesh height: "+station.name);
                    check(Mathf.Abs(bounds.min.y-.08f)<.001f,"model grounded: "+station.name);
                }
            }
        }
        foreach(var go in objects)check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go)==0,"missing script: "+go.name);
        foreach(var renderer in objects.SelectMany(g=>g.GetComponents<Renderer>()))
        {
            check(renderer.gameObject.layer==30,"review camera layer: "+renderer.name);
            check(renderer.sharedMaterials.Length>0,"no material slots: "+renderer.name);
            foreach(var material in renderer.sharedMaterials)
            {
                check(material!=null,"missing material: "+renderer.name);
                if(material!=null)check(material.shader!=null && material.shader.isSupported,"unsupported shader: "+material.name);
            }
            if(renderer is SkinnedMeshRenderer skin)check(skin.sharedMesh!=null,"missing skinned mesh: "+renderer.name);
        }
        foreach(var camera in objects.SelectMany(g=>g.GetComponents<Camera>()))check(!camera.enabled,"review camera must remain disabled: "+camera.name);
        foreach(JObject prior in before["priorScenes"])
        {
            var old=SceneManager.GetSceneByPath((string)prior["path"]);
            check(old.IsValid() && old.isLoaded,"prior scene still open: "+prior["path"]);
            if(old.IsValid())
            {
                check(old.isDirty==(bool)prior["dirty"],"prior scene dirty flag preserved: "+prior["path"]);
                check(old.rootCount==(int)prior["roots"],"prior scene root count preserved: "+prior["path"]);
            }
        }
        var build=new JArray(EditorBuildSettings.scenes.Select(s=>new JObject{{"path",s.path},{"enabled",s.enabled}}));
        check(JToken.DeepEquals(build,before["buildSettingsBefore"]),"Build Settings unchanged");
        check(!EditorBuildSettings.scenes.Any(s=>s.path==ScenePath),"review scene absent from game builds");
        var sourcePlan=JObject.Parse(File.ReadAllText(Path.Combine(outputDirectory,"native-source-plan.json")));
        foreach(JObject model in sourcePlan["models"])
        {
            string asset=(string)model["assetPath"];
            check(AssetDatabase.LoadAssetAtPath<GameObject>(asset)!=null,"source model load: "+model["id"]);
            if(!string.IsNullOrEmpty((string)model["guid"]))check(AssetDatabase.AssetPathToGUID(asset)==(string)model["guid"],"original model GUID preserved: "+model["id"]);
        }
        var result=new JObject{{"status",failures.Count==0?"PASS":"FAIL"},{"checks",checks},{"failures",failures},
            {"scene",ScenePath},{"models",(int)before["instanceCount"]},{"reload",reload},{"play","NOT_RUN: static review scene"},{"runtimeApplied",false}};
        File.WriteAllText(Path.Combine(outputDirectory,"native-scene-validation.json"),result.ToString());
        return result["status"]+": "+checks+" native saved-scene checks; "+failures.Count+" failures";
    }
}
