using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.Mojave;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Completed-map authoring pass. It writes external candidate data without importing scripts or resaving the producer's scenes.
public static class DesertSpawnBakeBuilder
{
    public const string Version = "DesertSpawn.v1:grid1-five-samples:floor.4/body+.1:area.04/body.05:vertical.12";
    static void Guard()
    {
        MojaveWorldBuilder.Guard();
        if (IsolatedSavePlayGuard.ActiveDirectory != "" || SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "") != ""
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY"))
            || SessionState.GetString("Overburst.DesertMinimapBuilder.output", "") != ""
            || SessionState.GetString("Overburst.MapRunEntryPlayVerifier.pending", "") != ""
            || SessionState.GetString("Overburst.MapRunEntryPlayVerifier.return", "") != ""
            || SessionState.GetBool("Overburst.Cleanup.CaptureLifecycle", false))
            throw new InvalidOperationException("Wait for the existing Editor owner to return.");
    }
    static void Write(string file, object value) => File.WriteAllText(file, JsonConvert.SerializeObject(value, Formatting.Indented));
    static string SceneState() => JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => new {s.path,s.isDirty,s.rootCount}));
    static object Point(Vector3 p) => new {x=p.x,y=p.y,z=p.z};
    public static string Bake(string directory, string production, string id)
    {
        Guard();
        string allowed=Path.GetFullPath(Application.dataPath+"/../../개인파일/코덱스산출")+Path.DirectorySeparatorChar;
        directory=Path.GetFullPath(directory); production=Path.GetFullPath(production);
        if(!directory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||!production.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(id) || id.Any(c=>!char.IsLetterOrDigit(c)&&c!='_')) throw new ArgumentException("Use owned output and a completed map ID.");
        var complete=JObject.Parse(File.ReadAllText(Path.Combine(production,"Evidence/complete-"+id+".json")));
        if((string)complete["status"]!="PASS_RETURN"||!(bool)complete["saved"]||!(bool)complete["minimap"]||!(bool)complete["memoryReturned"])
            throw new InvalidOperationException("The individual map has not completed production and return.");
        var record=JObject.Parse(File.ReadAllText(Path.Combine(production,"Evidence/map-"+id+".json")));
        var candidate=(JObject)record["candidate"]; string scenePath=(string)candidate["scene"];
        if((string)record["status"]!="PASS_SAVED_MAP" || (string)candidate["id"]!=id || !scenePath.Contains("/CompactSamples_20261013/"))
            throw new InvalidDataException("Only the current approved compact map is accepted.");
        if(AssetDatabase.AssetPathToGUID(scenePath)!=(string)record["sceneGuid"]) throw new InvalidDataException("Completed scene identity changed.");
        string output=Path.Combine(directory,"Bakes",id); Directory.CreateDirectory(output);
        string resultFile=Path.Combine(output,"bake-result.json"), bankFile=Path.Combine(output,"candidates.json");
        if(File.Exists(resultFile)||File.Exists(bankFile)) throw new InvalidOperationException("Inspect the existing durable result before repeating a bake.");
        string beforeScenes=SceneState(), sourceHash=AssetDatabase.GetAssetDependencyHash(scenePath).ToString();
        int beforePreviews=EditorSceneManager.previewSceneCount; var active=SceneManager.GetActiveScene(); var selection=Selection.objects;
        var process=Process.GetCurrentProcess(); process.Refresh(); long beforeWorking=process.WorkingSet64,beforePrivate=process.PrivateMemorySize64;
        var watch=Stopwatch.StartNew(); Scene preview=default; bool succeeded=false; string failure=null; object result=null;
        try
        {
            preview=EditorSceneManager.OpenPreviewScene(scenePath);
            var roots=preview.GetRootGameObjects(); var world=roots.SelectMany(r=>r.GetComponentsInChildren<MojaveWorld>(true)).Single();
            world.EnsureLayout();
            if(world.seed!=(int)candidate["seed"] || world.bakedSeed!=world.seed || world.bakedLayoutVersion!=MojaveLayout.Version
                || world.layout.places.Count!=(int)candidate["areaCount"] || !world.surface || world.transform.lossyScale!=Vector3.one)
                throw new InvalidOperationException("Saved world does not match the completed recipe.");
            var bodies=CavePlatformSpawnBuilder.Bodies();
            var colliders=roots.SelectMany(r=>r.GetComponentsInChildren<Collider>(true)).Where(c=>c.enabled&&!c.isTrigger&&c.gameObject.activeInHierarchy).ToArray();
            if(colliders.Length==0)throw new InvalidOperationException("Saved map has no physical geometry.");
            var capsuleObject=new GameObject("Owned desert spawn bake capsule");SceneManager.MoveGameObjectToScene(capsuleObject,preview);
            var capsule=capsuleObject.AddComponent<CapsuleCollider>();capsule.direction=1;capsule.isTrigger=true; Physics.SyncTransforms();
            bool Floor(int index,Vector3 near,float margin,out Vector3 floor)
            {
                floor=default;var p=new Vector2(near.x,near.z);var place=world.layout.places[index];
                if(place.Distance(p)>-margin||!world.IsWalkable(p,margin))return false;
                for(int j=0;j<index;j++)if(world.layout.places[j].Distance(p)<=0)return false;
                floor=world.Ground(p);return true;
            }
            bool Clear(Vector3 p,float radius,float height,float padding,Collider[] obstacles)
            {
                radius+=padding;float bottom=radius+.12f,top=Mathf.Max(bottom,height-radius);
                capsule.radius=radius;capsule.height=top-bottom+radius*2;capsule.center=Vector3.up*((top+bottom)*.5f);
                var volume=new Bounds(p+capsule.center,new Vector3(radius*2,capsule.height,radius*2));
                foreach(var c in obstacles)
                    if(c.bounds.Intersects(volume)&&Physics.ComputePenetration(capsule,p,Quaternion.identity,c,c.transform.position,c.transform.rotation,out _,out _))return false;
                return true;
            }
            int totalCells=0,totalPoints=0,bodyChecks=0;var areas=new List<object>();float maxRadius=bodies.Max(b=>b.radius)+.2f;
            for(int index=0;index<world.layout.places.Count;index++)
            {
                var place=world.layout.places[index];float radius=Mathf.Max(place.radius.x,place.radius.y)*1.5f;
                var bounds=new Bounds(new Vector3(place.center.x,world.Ground(place.center).y,place.center.y),new Vector3(radius*2,6,radius*2));
                var broad=new Bounds(bounds.center,new Vector3(bounds.size.x+maxRadius*2,1000,bounds.size.z+maxRadius*2));
                var obstacles=colliders.Where(c=>c.bounds.Intersects(broad)).ToArray();var cells=new List<Vector3>();var points=new List<object>();
                int left=Mathf.FloorToInt(bounds.min.x),right=Mathf.CeilToInt(bounds.max.x),bottom=Mathf.FloorToInt(bounds.min.z),top=Mathf.CeilToInt(bounds.max.z);
                for(int z=bottom;z<top;z++)for(int x=left;x<right;x++)
                {
                    var near=new Vector3(x+.5f,0,z+.5f);
                    if(!Floor(index,near,.4f,out var cell)||!Clear(cell,.35f,1.8f,.04f,obstacles))continue;
                    cells.Add(cell);
                    for(int sample=0;sample<5;sample++)
                    {
                        var p=cell+(sample==0?Vector3.zero:new Vector3((sample&1)==0?-.25f:.25f,0,sample<=2?-.25f:.25f));
                        long mask=0;Vector3 position=default;
                        for(int b=0;b<bodies.Length;b++)
                        {
                            var body=bodies[b];bodyChecks++;
                            if(Floor(index,p,body.radius+.1f,out var floor)&&Clear(floor,body.radius,body.height,.05f,obstacles))
                                {mask|=1L<<b;position=floor;}
                        }
                        if(mask!=0)points.Add(new {position=Point(position),bodyMask=mask});
                    }
                }
                if(cells.Count<6||points.Count==0)throw new InvalidOperationException("No viable static floor/candidates in "+place.name);
                areas.Add(new {index,name=place.name,role=place.role.ToString(),place=JsonUtility.ToJson(place),cells=cells.Select(Point).ToArray(),points});
                totalCells+=cells.Count;totalPoints+=points.Count;
                Write(Path.Combine(output,"progress.json"),new {status="BAKING",id,area=index+1,areas=world.layout.places.Count,cells=totalCells,points=totalPoints,bodyChecks,seconds=watch.Elapsed.TotalSeconds,utc=DateTime.UtcNow});
            }
            if(AssetDatabase.GetAssetDependencyHash(scenePath).ToString()!=sourceHash)throw new InvalidOperationException("Source changed while authoring.");
            var data=new {version=Version,id,scenePath,sceneGuid=(string)record["sceneGuid"],sceneDependencyHash=sourceHash,seed=world.seed,layoutVersion=MojaveLayout.Version,
                terrainPath=AssetDatabase.GetAssetPath(world.surface.terrainData),bounds=Point(new Vector3(world.layout.bounds.x,0,world.layout.bounds.y)),
                bodies=bodies.Select(b=>new {b.radius,b.height,definitions=b.definitions.Select(d=>AssetDatabase.GetAssetPath(d)).ToArray()}),areas};
            Write(bankFile,data);
            // Check durable serialization including 64-bit body masks, without importing or modifying the producer's assets.
            var saved=JObject.Parse(File.ReadAllText(bankFile));int savedPoints=((JArray)saved["areas"]).Sum(a=>((JArray)a["points"]).Count);
            if(savedPoints!=totalPoints||saved["areas"].Any(a=>a["points"].Any(p=>(long)p["bodyMask"]<=0)))throw new InvalidDataException("Candidate serialization differs.");
            result=new {status="PASS_STATIC_BAKE",id,sourceHash,areas=areas.Count,cells=totalCells,points=totalPoints,profiles=bodies.Length,definitions=bodies.Sum(b=>b.definitions.Length),bodyChecks,
                seconds=watch.Elapsed.TotalSeconds,scope="Saved geometry and physical body candidates only. Assembled navigation, population parity, runtime consumer and Play NOT_RUN.",bankFile};
            Write(resultFile,result);succeeded=true;return JsonConvert.SerializeObject(result);
        }
        catch(Exception error){failure=error.ToString();Write(Path.Combine(output,"failure.json"),new {status="FAIL",id,error=failure,seconds=watch.Elapsed.TotalSeconds});throw;}
        finally
        {
            if(preview.IsValid())EditorSceneManager.ClosePreviewScene(preview);
            if(active.IsValid()&&active.isLoaded&&SceneManager.GetActiveScene()!=active)SceneManager.SetActiveScene(active);Selection.objects=selection;
            bool preserved=SceneState()==beforeScenes&&EditorSceneManager.previewSceneCount==beforePreviews;
            bool unchanged=AssetDatabase.GetAssetDependencyHash(scenePath).ToString()==sourceHash;
            // This synchronous authoring call owns only its closed preview; static references and producer assets remain alive.
            EditorUtility.UnloadUnusedAssetsImmediate(true);GC.Collect();
            int desired=EditorUserSettings.desiredImportWorkerCount,standby=EditorUserSettings.standbyImportWorkerCount;
            try{EditorUserSettings.desiredImportWorkerCount=0;EditorUserSettings.standbyImportWorkerCount=0;AssetDatabase.ForceToDesiredWorkerCount();}
            finally{EditorUserSettings.desiredImportWorkerCount=desired;EditorUserSettings.standbyImportWorkerCount=standby;}
            process.Refresh();Write(Path.Combine(output,"return.json"),new {status=preserved&&unchanged?"PASS":"FAIL",id,succeeded,sourceUnchanged=unchanged,sceneStatePreserved=preserved,
                previewBefore=beforePreviews,previewAfter=EditorSceneManager.previewSceneCount,playing=EditorApplication.isPlaying,changing=EditorApplication.isPlayingOrWillChangePlaymode,
                compiling=EditorApplication.isCompiling,updating=EditorApplication.isUpdating,accountBlocked=IsolatedSavePlayGuard.RequiresAccountChoice,
                active=IsolatedSavePlayGuard.ActiveDirectory,prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),environment=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
                beforeWorking,beforePrivate,afterWorking=process.WorkingSet64,afterPrivate=process.PrivateMemorySize64,desired,standby,error=failure,utc=DateTime.UtcNow});
            if(!preserved||!unchanged)throw new InvalidOperationException("Authoring return/source preservation failed.");
        }
    }

    public static object Apply(string directory, string id)
    {
        Guard(); directory=Path.GetFullPath(directory);
        string allowed=Path.GetFullPath(Application.dataPath+"/../../개인파일/코덱스산출")+Path.DirectorySeparatorChar;
        if(!directory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||string.IsNullOrEmpty(id)||id.Any(c=>!char.IsLetterOrDigit(c)&&c!='_'))throw new ArgumentException("Owned bake required.");
        string folder=Path.Combine(directory,"Bakes",id); var proof=JObject.Parse(File.ReadAllText(Path.Combine(folder,"bake-result.json")));
        var returned=JObject.Parse(File.ReadAllText(Path.Combine(folder,"return.json")));var raw=JObject.Parse(File.ReadAllText(Path.Combine(folder,"candidates.json")));
        if((string)proof["status"]!="PASS_STATIC_BAKE"||(string)returned["status"]!="PASS"||!(bool)returned["succeeded"])throw new InvalidDataException("Completed authoring and return required.");
        string scene=(string)raw["scenePath"];
        if(AssetDatabase.AssetPathToGUID(scene)!=(string)raw["sceneGuid"]||AssetDatabase.GetAssetDependencyHash(scene).ToString()!=(string)raw["sceneDependencyHash"])
            throw new InvalidOperationException("Completed source changed; review and rebake before applying.");
        string root="Assets/ProjectOverburst/Resources/World/MapRuns/DesertSpawns";
        string path=root+"/"+Path.GetFileNameWithoutExtension(scene)+".asset";MojaveWorldBuilder.Folder(root);
        var data=AssetDatabase.LoadAssetAtPath<DesertSpawnData>(path);
        if(data){string backup=Path.Combine(directory,"Recovery/NativeBefore",Path.GetFileName(path));Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!File.Exists(backup)){File.Copy(path,backup);File.Copy(path+".meta",backup+".meta");}}
        else {data=ScriptableObject.CreateInstance<DesertSpawnData>();AssetDatabase.CreateAsset(data,path);}
        data.scenePath=scene;data.sceneDependencyHash=(string)raw["sceneDependencyHash"];data.bakeVersion=(string)raw["version"];
        data.seed=(int)raw["seed"];data.layoutVersion=(int)raw["layoutVersion"];
        data.terrain=AssetDatabase.LoadAssetAtPath<TerrainData>((string)raw["terrainPath"]);if(!data.terrain)throw new InvalidDataException("Saved terrain is missing.");
        data.bodies=raw["bodies"].Select(b=>new DesertSpawnData.BodyProfile{radius=(float)b["radius"],height=(float)b["height"],definitions=b["definitions"].Select(d=>AssetDatabase.LoadAssetAtPath<EnemyDefinition>((string)d)).ToArray()}).ToArray();
        data.areas=raw["areas"].Select(a=>new DesertSpawnData.Area{index=(int)a["index"],place=(string)a["place"],cells=a["cells"].ToObject<Vector3[]>(),points=a["points"].ToObject<DesertSpawnData.SpawnPoint[]>()}).ToArray();
        EditorUtility.SetDirty(data);AssetDatabase.SaveAssetIfDirty(data);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var loaded=AssetDatabase.LoadAssetAtPath<DesertSpawnData>(path);
        int points=loaded.areas.Sum(a=>a.points.Length);if(points!=(int)proof["points"]||loaded.bodies.Any(b=>b.definitions.Any(d=>!d))
            ||loaded.areas.Any(a=>a.points.Any(p=>p.bodyMask<=0)))throw new InvalidDataException("Native spawn asset differs from the authored bank.");
        var result=new {status="PASS_NATIVE_APPLY",id,path,guid=AssetDatabase.AssetPathToGUID(path),points,profiles=loaded.bodies.Length,definitions=loaded.bodies.Sum(b=>b.definitions.Length)};
        Write(Path.Combine(folder,"apply.json"),result);return result;
    }
}
