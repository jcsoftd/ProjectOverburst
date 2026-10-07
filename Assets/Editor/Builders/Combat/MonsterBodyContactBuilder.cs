using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Object = UnityEngine.Object;

// Body origins are authored from real torso triangles, retaining their original skin weights.
// This tool never changes combat volumes, model imports, animation or supplier assets.
public static class MonsterBodyContactBuilder
{
    public const int DirectionCount = 16;
    const string CatalogPath = "Assets/ProjectOverburst/Resources/Enemies/Themes/Catalog.asset";
    static readonly Dictionary<string,string> TorsoBones = new Dictionary<string,string>
    {
        {"SpiderBrood_RostrokarckLarvae","RostrokarckLarvae_Thorax"},
        {"SpiderBrood_Cavecrawler","CAVECRAWLER_THORAX"},
        {"V3_GigabdomocaudaLarva","GigabdomocaudaLarva_Thorax"},
        {"SpiderBrood_Carcinoptera","Carcinoptera_Body"},
        {"SpiderBrood_Rostrokarck","Rostrokarck_Thorax"},
        {"V3_GigabdomocaudaBombis","GigabdomocaudaBombis_Thorax"},
        {"SpiderBrood_Formickarce","Formickarce_Thorax"},
        {"VenomBrood_Venodonte_Tint1","Venodonte_Cephalothrorax"},
        {"VenomBrood_Venodonte_Tint3","Venodonte_Cephalothrorax"},
        {"VenomBrood_Arathrox","Arathrox_Thorax"},
        {"SpiderBrood_Kentriplokame","Kentriplokame_Spine2"},
        {"CavernMutants_Limadon","Limadon_Spine_02"},
        {"VenomBrood_Kupolobrach_Tint_Orange","Kupolobrach_Spine"},
        {"V3_Clypeosaurus","Clypeosaurus_Body"},
        {"V3_Gryllunguis","Gryllunguis_ Spine"},
        {"V3_Trimaxillopod","Trimaxillopod_Pelvis"},
        {"PrimalHunt_Caniathrox","Caniathrox_ Spine2"},
        {"PrimalHunt_CrustaspikanLarvae","CrustaspikanLarvae_ Spine"},
        {"PrimalHunt_Dimaxillosaurus","Dimaxillosaurus_ Spine1"},
        {"PrimalHunt_Venosaur_Tint_Brown","Venosaur_ Spine1"},
        {"V3_Serpenopod","Serpenopod_ Spine"},
        {"V3_Deinodonte","Deinodonte_ Spine"},
        {"V3_Perderos","Perderos_ Spine"},
        {"V3_Gobbler","Gobbler_ Spine2"},
        {"V3_Hexapodosaurus","Hexapodosuarus_Spine1"},
        {"V3_Lacodon","LACODON_ Spine1"},
        {"CavernMutants_Ceratoferox","Ceratoferox_ Spine1"},
        {"CavernMutants_Cephalonops","Cephalonops_Body"},
        {"V3_Crassoplast","Crassoplast_Spine"},
        {"CavernMutants_Gasterobrach","Gasterobrach_ Spine1"},
        {"CavernMutants_Gorhorrid","Gorhorrid_ Spine1"},
        {"V3_Hideoplast","Hideoplast_ Spine1"},
        {"CavernMutants_Ursacetus","Ursacetus_ Spine1"},
        {"V3_Crassorrid","Crassorrid_ Spine"},
        {"V3_Skorpmare","Skorpmare_ Spine"},
        {"V3_KingSpawnLarvae","KingSpawnLarvae_Lungs"},
        {"V3_Ghoul","GHOUL_ Spine1"},
        {"V3_SkeletonKnight_Small","SkeletonKnight_ Spine2"},
        {"V3_DeathSkull","DEATH_SKULL_HEAD"},
        {"DeathHarvest_RakeBrute","spine_03"},
        {"V3_SkeletonKnight_Medium","SkeletonKnight_ Spine2"},
        {"DeathHarvest_Reaper","REAPER_ Spine1"},
        {"V3_darkKnight2","spine_03"},
        {"V3_Funglicane","Funglicane_Head"},
        {"V3_Tetrapuss","Tetrapuss_Body"},
        {"V3_Tetruncivermis","Tetruncivermis_Neck"},
        {"V3_GiantSlug","GiantSlug_BodyRear1"},
        {"V3_Gasterodonte","Gaterodonte_Spine3"},
        {"V3_Telluropod","telluropod_Body"},
        {"V3_Kapeloproboskid","Kapeloproboskid_Body1"},
        {"V3_Gastarias","Gastarias_Upper_01"},
        {"V3_Gastarid","Gastarid_ Pelvis"},
        {"V3_Gastaroid","Gastaroid_ Spine1"},
        {"V3_Anglerox","Anglerox_ Spine1"},
        {"V3_Onyscidus","Onyscidus_UpperSpine1"},
        {"V3_Xenosaurid","Xenosaurid_ Spine1"},
        {"CavernMutants_UrsacetusKing_Boss","Ursacetus_ Spine1"},
        {"CrustaspikanMaterials","Crustaspikan_ Spine"}
    };

    public static EnemyDefinition[] Definitions()
    {
        var c=AssetDatabase.LoadAssetAtPath<EnemyCatalog>(CatalogPath);
        return Enumerable.Range(0,c.Count).Select(c.GetDefinition).Concat(
            AssetDatabase.FindAssets("t:EnemyDefinition",new[]{"Assets/ProjectOverburst/Resources/Enemies/Bosses"})
            .Select(g=>AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g))))
            .Where(d=>d && d.ActorPrefab).GroupBy(d=>AssetDatabase.GetAssetPath(d.ActorPrefab)).Select(g=>g.First()).ToArray();
    }
    public static void RequireIdle()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || BuildPipeline.isBuildingPlayer || EditorUtility.scriptCompilationFailed
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")))
            throw new InvalidOperationException("Idle Editor and unoccupied account required.");
    }
    public static string Output(string folder)
    {
        string allowed=Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        string full=Path.GetFullPath(folder);
        if(!full.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new Exception("Private artifact output required");
        Directory.CreateDirectory(full);return full;
    }
    public static JArray V(Vector3 v)=>new JArray(v.x,v.y,v.z);
    public static Vector3 Vector(JToken v)=>new Vector3((float)v[0],(float)v[1],(float)v[2]);
    public static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}

    public sealed class SkinData
    {
        public SkinnedMeshRenderer renderer;
        public Mesh mesh;
        public Vector3[] vertices, world;
        public int[] triangles;
        public Transform[] bones;
        public Matrix4x4[] bind;
        public BoneWeight1[][] weights;
        public int[] dominant;
        public void UpdateWorld()
        {
            var matrices=bones.Select((b,i)=>b.localToWorldMatrix*bind[i]).ToArray();
            for(int i=0;i<vertices.Length;i++){
                Vector3 p=Vector3.zero;foreach(var w in weights[i])p+=matrices[w.boneIndex].MultiplyPoint3x4(vertices[i])*w.weight;
                world[i]=weights[i].Length>0?p:renderer.transform.TransformPoint(vertices[i]);
            }
        }
    }
    public static List<SkinData> ReadSkins(EnemyActor actor)
    {
        var result=new List<SkinData>();
        foreach(var r in actor.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s=>s.enabled && s.sharedMesh)){
            var m=r.sharedMesh;var s=new SkinData{renderer=r,mesh=m,bones=r.bones,bind=m.bindposes,triangles=m.triangles};
            // Editor inspection keeps non-readable supplier imports unchanged, also in Play Mode.
            using(var data=MeshUtility.AcquireReadOnlyMeshData(m)){
                var v=new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount,Unity.Collections.Allocator.Temp);
                try{data[0].GetVertices(v);s.vertices=v.ToArray();}finally{v.Dispose();}
            }
            var counts=m.GetBonesPerVertex();var weights=m.GetAllBoneWeights();
            try{
                s.weights=new BoneWeight1[s.vertices.Length][];s.dominant=new int[s.vertices.Length];int cursor=0;
                for(int i=0;i<s.vertices.Length;i++){
                    s.weights[i]=new BoneWeight1[counts[i]];float best=0;
                    for(int j=0;j<counts[i];j++){var w=weights[cursor++];s.weights[i][j]=w;if(w.weight>best){best=w.weight;s.dominant[i]=w.boneIndex;}}
                }
            }finally{counts.Dispose();weights.Dispose();}
            s.world=new Vector3[s.vertices.Length];s.UpdateWorld();result.Add(s);
        }
        return result;
    }
    public static bool Intersect(Vector3 origin,Vector3 direction,Vector3 a,Vector3 b,Vector3 c,out float distance,out Vector3 bary)
    {
        distance=0;bary=default;Vector3 e1=b-a,e2=c-a,p=Vector3.Cross(direction,e2);float determinant=Vector3.Dot(e1,p);
        if(Mathf.Abs(determinant)<1e-8f)return false;
        float inverse=1/determinant;Vector3 t=origin-a;float u=Vector3.Dot(t,p)*inverse;if(u<0 || u>1)return false;
        Vector3 q=Vector3.Cross(t,e1);float v=Vector3.Dot(direction,q)*inverse;if(v<0 || u+v>1)return false;
        distance=Vector3.Dot(e2,q)*inverse;if(distance<.00001f)return false;bary=new Vector3(1-u-v,u,v);return true;
    }
    public static Vector3 ClosestBarycentric(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
    {
        Vector3 ab=b-a,ac=c-a,ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
        if(d1<=0 && d2<=0)return Vector3.right;
        Vector3 bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);if(d3>=0 && d4<=d3)return Vector3.up;
        float vc=d1*d4-d3*d2;if(vc<=0 && d1>=0 && d3<=0){float v=d1/(d1-d3);return new Vector3(1-v,v,0);}
        Vector3 cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);if(d6>=0 && d5<=d6)return Vector3.forward;
        float vb=d5*d2-d1*d6;if(vb<=0 && d2>=0 && d6<=0){float w=d2/(d2-d6);return new Vector3(1-w,0,w);}
        float va=d3*d6-d5*d4;if(va<=0 && d4-d3>=0 && d5-d6>=0){float w=(d4-d3)/((d4-d3)+(d5-d6));return new Vector3(0,1-w,w);}
        float denominator=va+vb+vc;if(Mathf.Abs(denominator)<1e-15f)return Vector3.right;
        float v2=vb/denominator,w2=vc/denominator;return new Vector3(1-v2-w2,v2,w2);
    }
    static Transform Anchor(GameObject root,string name,List<SkinData> skins)
    {
        return skins.SelectMany(s=>s.bones).Where(b=>b && b.name==name).Distinct()
            .OrderByDescending(b=>skins.Sum(s=>Enumerable.Range(0,s.dominant.Length).Count(i=>s.bones[s.dominant[i]]==b))).FirstOrDefault()
            ?? throw new Exception("Torso bone missing: "+root.name+" / "+name);
    }
    public static PlayableGraph Pose(EnemyActor actor,AnimationClip clip,double normalized)
    {
        var a=actor.Animator;Vector3 p=a.transform.localPosition,scale=a.transform.localScale;Quaternion rotation=a.transform.localRotation;
        a.enabled=true;a.applyRootMotion=false;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        var graph=PlayableGraph.Create("Owned torso pose");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);
        playable.SetTime(clip.length*normalized);AnimationPlayableOutput.Create(graph,"pose",a).SetSourcePlayable(playable);graph.Play();graph.Evaluate(0);
        a.transform.SetLocalPositionAndRotation(p,rotation);a.transform.localScale=scale;return graph;
    }
    static float Quantile(IEnumerable<float> values,float fraction)
    {
        var sorted=values.OrderBy(v=>v).ToArray();float index=(sorted.Length-1)*fraction;int lo=Mathf.FloorToInt(index);return Mathf.Lerp(sorted[lo],sorted[Mathf.Min(lo+1,sorted.Length-1)],index-lo);
    }
    static JObject Measure(EnemyDefinition definition,GameObject root)
    {
        var actor=root.GetComponent<EnemyActor>();var skins=ReadSkins(actor);
        if(!TorsoBones.TryGetValue(definition.EnemyId,out string name))throw new Exception("Choose torso bone for new definition: "+definition.EnemyId);
        var anchor=Anchor(root,name,skins);
        var core=skins.SelectMany(s=>Enumerable.Range(0,s.vertices.Length).Where(i=>s.bones[s.dominant[i]]==anchor).Select(i=>s.world[i])).ToArray();
        var min=new Vector3(Quantile(core.Select(p=>p.x),.05f),Quantile(core.Select(p=>p.y),.05f),Quantile(core.Select(p=>p.z),.05f));
        var max=new Vector3(Quantile(core.Select(p=>p.x),.95f),Quantile(core.Select(p=>p.y),.95f),Quantile(core.Select(p=>p.z),.95f));
        Vector3 center=(min+max)*.5f;
        var contacts=new JArray();
        for(int n=0;n<DirectionCount;n++){
            Vector3 direction=Quaternion.Euler(0,n*360f/DirectionCount,0)*Vector3.forward;
            SkinData bestSkin=null;int bestTriangle=-1;Vector3 bestBary=default;float best=float.PositiveInfinity;
            foreach(var skin in skins)for(int i=0;i<skin.triangles.Length;i+=3){
                int ia=skin.triangles[i],ib=skin.triangles[i+1],ic=skin.triangles[i+2];
                if(!Intersect(center,direction,skin.world[ia],skin.world[ib],skin.world[ic],out float distance,out Vector3 bary)||distance>=best)continue;
                // The nearest exit from a measured torso interior also covers seams between
                // adjacent thorax/abdomen bones; requiring only the anchor weight drops those seams.
                best=distance;bestSkin=skin;bestTriangle=i;bestBary=bary;
            }
            string mode="torso-ray";
            if(bestSkin==null){
                // Open ribs/armour cannot have an exit in every direction. Attach to an actual
                // nearby torso triangle instead of inventing a capsule point in the opening.
                mode="open-torso-surface";Vector3 desired=center+direction*Mathf.Min((max-min).x,(max-min).z)*.5f;
                foreach(var skin in skins)for(int i=0;i<skin.triangles.Length;i+=3){
                    int ia=skin.triangles[i],ib=skin.triangles[i+1],ic=skin.triangles[i+2];
                    if(skin.bones[skin.dominant[ia]]!=anchor && skin.bones[skin.dominant[ib]]!=anchor && skin.bones[skin.dominant[ic]]!=anchor)continue;
                    Vector3 bary=ClosestBarycentric(desired,skin.world[ia],skin.world[ib],skin.world[ic]);
                    Vector3 p=skin.world[ia]*bary.x+skin.world[ib]*bary.y+skin.world[ic]*bary.z;float distance=(p-desired).sqrMagnitude;
                    if(distance>=best)continue;best=distance;bestSkin=skin;bestTriangle=i;bestBary=bary;
                }
            }
            if(bestSkin==null)throw new Exception("No torso surface: "+definition.EnemyId+" sector "+n);
            var influences=new JArray();var ids=new[]{bestSkin.triangles[bestTriangle],bestSkin.triangles[bestTriangle+1],bestSkin.triangles[bestTriangle+2]};
            for(int k=0;k<3;k++)foreach(var weight in bestSkin.weights[ids[k]]){
                float value=weight.weight*bestBary[k];if(value<1e-7f)continue;
                influences.Add(new JObject{{"bone",AnimationUtility.CalculateTransformPath(bestSkin.bones[weight.boneIndex],root.transform)},
                    {"point",V(bestSkin.bind[weight.boneIndex].MultiplyPoint3x4(bestSkin.vertices[ids[k]]))},{"weight",value}});
            }
            var meshId=new JObject();AssetDatabase.TryGetGUIDAndLocalFileIdentifier(bestSkin.mesh,out string meshGuid,out long fileId);meshId["guid"]=meshGuid;meshId["fileId"]=fileId;
            contacts.Add(new JObject{{"sector",n},{"direction",V(anchor.InverseTransformDirection(direction))},{"weights",influences},
                {"renderer",AnimationUtility.CalculateTransformPath(bestSkin.renderer.transform,root.transform)},{"mesh",meshId},{"vertices",new JArray(ids)},{"barycentric",V(bestBary)},
                {"mode",mode},{"idlePoint",V(bestSkin.world[ids[0]]*bestBary.x+bestSkin.world[ids[1]]*bestBary.y+bestSkin.world[ids[2]]*bestBary.z)}});
        }
        string prefab=AssetDatabase.GetAssetPath(definition.ActorPrefab);
        return new JObject{{"id",definition.EnemyId},{"label",definition.DisplayName},{"definition",AssetDatabase.GetAssetPath(definition)},{"prefab",prefab},{"sha256Before",Hash(prefab)},
            {"anchor",AnimationUtility.CalculateTransformPath(anchor,root.transform)},{"localCenter",V(anchor.InverseTransformPoint(center))},{"idleCenter",V(center)},
            {"coreSize",V(max-min)},{"coreVertices",core.Length},{"contacts",contacts}};
    }
    public static string Generate(string directory)
    {
        RequireIdle();string folder=Output(directory);var rows=new JArray();var scene=EditorSceneManager.NewPreviewScene();
        try{
            foreach(var d in Definitions()){
                GameObject root=null;PlayableGraph graph=default;
                try{
                    root=(GameObject)PrefabUtility.InstantiatePrefab(d.ActorPrefab.gameObject,scene);root.SetActive(true);
                    foreach(var b in root.GetComponentsInChildren<MonoBehaviour>(true))if(b)b.enabled=false;
                    graph=Pose(root.GetComponent<EnemyActor>(),d.AnimationProfile.Idle,0);
                    rows.Add(Measure(d,root));File.WriteAllText(Path.Combine(folder,"plan.json"),new JObject{{"status","RUNNING"},{"rows",rows}}.ToString());
                }finally{if(graph.IsValid())graph.Destroy();if(root)Object.DestroyImmediate(root);}
            }
            File.WriteAllText(Path.Combine(folder,"plan.json"),new JObject{{"status","PASS"},{"rows",rows}}.ToString());return "PASS measured torso plan "+rows.Count;
        }finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
    public static void Configure(GameObject root,JToken row)
    {
        var placement=root.GetComponent<CombatTargetVfxPlacement>();if(!placement)placement=root.AddComponent<CombatTargetVfxPlacement>();
        var so=new SerializedObject(placement);
        so.FindProperty("bodyContactAnchor").objectReferenceValue=root.transform.Find((string)row["anchor"]);
        so.FindProperty("bodyContactLocalCenter").vector3Value=Vector(row["localCenter"]);
        var array=so.FindProperty("bodyContactSamples");array.arraySize=row["contacts"].Count();
        for(int i=0;i<array.arraySize;i++){
            var sample=array.GetArrayElementAtIndex(i);var data=row["contacts"][i];sample.FindPropertyRelative("localDirection").vector3Value=Vector(data["direction"]);
            var weights=sample.FindPropertyRelative("weights");weights.arraySize=data["weights"].Count();
            for(int j=0;j<weights.arraySize;j++){
                var w=weights.GetArrayElementAtIndex(j);var source=data["weights"][j];
                var bone=root.transform.Find((string)source["bone"]);if(!bone)throw new Exception("Bone reference missing");
                w.FindPropertyRelative("bone").objectReferenceValue=bone;w.FindPropertyRelative("localPoint").vector3Value=Vector(source["point"]);w.FindPropertyRelative("weight").floatValue=(float)source["weight"];
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    public static string Apply(string directory,string excludedId="")
    {
        RequireIdle();string folder=Output(directory);var plan=JObject.Parse(File.ReadAllText(Path.Combine(folder,"plan.json")));
        if((string)plan["status"]!="PASS")throw new Exception("Complete plan required");
        var done=File.Exists(Path.Combine(folder,"applied.json"))?(JArray)JObject.Parse(File.ReadAllText(Path.Combine(folder,"applied.json")))["rows"]:new JArray();
        var existing=new HashSet<string>(done.Select(r=>(string)r["prefab"]));
        var remaining=plan["rows"].Where(r=>(string)r["id"]!=excludedId && !existing.Contains((string)r["prefab"])).ToArray();
        // Preflight the whole batch before writing a prefab.
        foreach(var row in remaining)if(Hash((string)row["prefab"])!=(string)row["sha256Before"])throw new Exception("Prefab changed after measurement: "+row["id"]);
        foreach(var row in remaining){
            string path=(string)row["prefab"];foreach(string suffix in new[]{"",".meta"}){string backup=Path.Combine(folder,"Before",path+suffix);Directory.CreateDirectory(Path.GetDirectoryName(backup));File.Copy(path+suffix,backup,false);}
            var root=PrefabUtility.LoadPrefabContents(path);
            try{Configure(root,row);PrefabUtility.SaveAsPrefabAsset(root,path);done.Add(new JObject{{"prefab",path},{"guid",AssetDatabase.AssetPathToGUID(path)},{"sha256",Hash(path)}});
                File.WriteAllText(Path.Combine(folder,"applied.json"),new JObject{{"status","RUNNING"},{"rows",done}}.ToString());}
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        string status=done.Count==plan["rows"].Count()?"PASS":"PARTIAL";
        File.WriteAllText(Path.Combine(folder,"applied.json"),new JObject{{"status",status},{"rows",done}}.ToString());return status+" body contact prefabs "+done.Count;
    }
}
