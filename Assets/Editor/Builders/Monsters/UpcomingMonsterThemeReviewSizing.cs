using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Reads current combat content; only upcoming review instances use the resolved sizes.</summary>
public static class UpcomingMonsterThemeReviewSizing
{
    sealed class Reference
    {
        public string id,slot,theme,path,signature;
        public Vector3 scale,size;
    }
    static readonly List<Reference> references=new List<Reference>();
    static readonly Dictionary<string,Vector2> limits=new Dictionary<string,Vector2>();
    static readonly Dictionary<string,Bounds> sourceBounds=new Dictionary<string,Bounds>();
    static readonly string[] Slots={"small","medium","elite","boss"};
    public const float BossReviewMaxHeight=9f;
    public const float BossReviewMaxFootprint=12f;
    public static void Prepare()
    {
        references.Clear();limits.Clear();sourceBounds.Clear();
        foreach(string guid in AssetDatabase.FindAssets("t:EnemyThemeTable",new[]{"Assets/ProjectOverburst/Resources/Enemies/Themes/Tables"}))
        {
            var table=AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(AssetDatabase.GUIDToAssetPath(guid));
            foreach(var entry in table.Entries)Read(entry.definition,entry.tier.ToString().ToLowerInvariant(),table.ThemeId);
        }
        foreach(string guid in AssetDatabase.FindAssets("t:EnemyDefinition",new[]{"Assets/ProjectOverburst/Resources/Enemies/Bosses"}))
        {
            var d=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if(d.ActorPrefab!=null && d.Species!=null)Read(d,"boss","boss");
        }
        foreach(string slot in Slots)
        {
            SetLimit(slot,references.Where(r=>r.slot==slot && r.theme!="DeathHarvest").ToArray());
            if(slot!="boss")SetLimit("DeathHarvest/"+slot,references.Where(r=>r.slot==slot && r.theme=="DeathHarvest").ToArray());
        }
    }
    static void Read(EnemyDefinition definition,string slot,string theme)
    {
        var actor=definition.ActorPrefab;var vendor=definition.Species.VendorPrefab;
        var model=actor.VisualRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>
            PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject)==vendor || PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject)==vendor);
        if(model==null)throw new InvalidOperationException("게임 모델 크기 연결 누락: "+definition.EnemyId);
        var scale=Vector3.Scale(Divide(model.lossyScale,actor.VisualRoot.lossyScale),definition.ResolveRuntimeStats().VisualScale);
        var multiplier=Divide(scale,vendor.transform.localScale);
        RequireUniform(multiplier,definition.EnemyId);
        var reference=new Reference{id=definition.EnemyId,slot=slot,theme=theme,path=AssetDatabase.GetAssetPath(vendor),
            signature=Signature(vendor),scale=scale,size=MeasureSource(vendor,definition.AnimationProfile.Idle).size*multiplier.x};
        references.Add(reference);
    }
    static void SetLimit(string key,Reference[] rows)
    {
        if(rows.Length==0)throw new InvalidOperationException("현행 체급 크기 기준 누락: "+key);
        // Existing authored content contains low crawlers and tall bipeds. Keep those proportions.
        limits[key]=new Vector2(rows.Max(r=>r.size.y),rows.Max(r=>Mathf.Max(r.size.x,r.size.z)));
    }
    public static Vector3 Resolve(GameObject source,Bounds raw,int slot,int theme,out string basis,out string gameReference)
    {
        string path=AssetDatabase.GetAssetPath(source),role=Slots[slot];
        if(slot==3)
        {
            float bossFactor=Mathf.Min(BossReviewMaxHeight/Mathf.Max(.01f,raw.size.y),BossReviewMaxFootprint/Mathf.Max(.01f,Mathf.Max(raw.size.x,raw.size.z)));
            basis="별도 보스 확대 기준";gameReference="boss-large";
            return source.transform.localScale*bossFactor;
        }
        string signature=Signature(source);
        var exact=references.Where(r=>r.slot==role && (r.path==path || r.signature==signature))
            .OrderBy(r=>r.path==path?0:1).ThenBy(r=>r.id,StringComparer.Ordinal).FirstOrDefault();
        if(exact!=null)
        {
            basis=exact.path==path?"현행 게임 동일 모델·역할":"현행 게임 동일 메시·역할";gameReference=exact.id;
            return exact.scale;
        }
        string key=theme==5 && slot<3?"DeathHarvest/"+role:role;
        var cap=limits[key];
        float factor=Mathf.Min(cap.x/Mathf.Max(.01f,raw.size.y),cap.y/Mathf.Max(.01f,Mathf.Max(raw.size.x,raw.size.z)));
        basis="현행 체급 외형 범위 · 신규/역할 변경 후보";gameReference=key;
        return source.transform.localScale*factor;
    }
    public static Bounds MeasureSource(GameObject source,AnimationClip idle)
    {
        string key=AssetDatabase.GetAssetPath(source)+":"+(idle==null?"pose":AssetDatabase.GetAssetPath(idle)+"/"+idle.name);
        if(sourceBounds.TryGetValue(key,out var value))return value;
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var model=(GameObject)PrefabUtility.InstantiatePrefab(source,scene);
            foreach(var b in model.GetComponentsInChildren<MonoBehaviour>(true))if(b!=null)b.enabled=false;
            foreach(var animator in model.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled=false;
                if(idle!=null)
                {
                    Vector3 p=animator.transform.localPosition,s=animator.transform.localScale;Quaternion q=animator.transform.localRotation;
                    idle.SampleAnimation(animator.gameObject,0);
                    animator.transform.localPosition=p;animator.transform.localRotation=q;animator.transform.localScale=s;
                }
            }
            value=GeometryBounds(model);sourceBounds[key]=value;return value;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    public static Bounds GeometryBounds(GameObject model)
    {
        Bounds bounds=default;bool any=false;
        foreach(var renderer in model.GetComponentsInChildren<Renderer>())
        {
            if(!renderer.enabled || !renderer.gameObject.activeInHierarchy)continue;
            Mesh mesh=null;
            var skin=renderer as SkinnedMeshRenderer;
            if(skin!=null)mesh=skin.sharedMesh;
            else {var filter=renderer.GetComponent<MeshFilter>();if(filter!=null)mesh=filter.sharedMesh;}
            if(mesh==null)continue;
            Vector3[] vertices;
            if(mesh.isReadable || !Application.isPlaying)vertices=mesh.vertices;
            else
            {
                // Editor-only inspection without changing the imported model's Read/Write setting.
                using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    var points=new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount,Unity.Collections.Allocator.Temp);
                    try { data[0].GetVertices(points); vertices=points.ToArray(); }
                    finally { points.Dispose(); }
                }
            }
            Matrix4x4[] matrices=null;
            if(skin!=null)
            {
                var bones=skin.bones;var bindposes=mesh.bindposes;
                if(bones.Length==bindposes.Length && bones.Length>0)
                    matrices=bones.Select((bone,i)=>bone==null?skin.transform.localToWorldMatrix:bone.localToWorldMatrix*bindposes[i]).ToArray();
            }
            // Match world-space shader skinning. BakeMesh can duplicate FBX scale in legacy rigs.
            if(matrices!=null)
            {
                var counts=mesh.GetBonesPerVertex();var weights=mesh.GetAllBoneWeights();int cursor=0;
                try
                {
                    for(int i=0;i<vertices.Length;i++)
                    {
                        Vector3 point=Vector3.zero;int count=counts[i];
                        for(int j=0;j<count;j++)
                        {
                            var weight=weights[cursor++];
                            point+=matrices[weight.boneIndex].MultiplyPoint3x4(vertices[i])*weight.weight;
                        }
                        if(count==0)point=renderer.transform.TransformPoint(vertices[i]);
                        if(!any){bounds=new Bounds(point,Vector3.zero);any=true;}else bounds.Encapsulate(point);
                    }
                }
                finally { counts.Dispose();weights.Dispose(); }
            }
            else foreach(var vertex in vertices)
            {
                var point=renderer.transform.TransformPoint(vertex);
                if(!any){bounds=new Bounds(point,Vector3.zero);any=true;}else bounds.Encapsulate(point);
            }
        }
        if(!any)throw new InvalidOperationException("표시 메시 누락: "+model.name);
        return bounds;
    }
    static string Signature(GameObject source)
    {
        var meshes=source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r=>r.sharedMesh)
            .Concat(source.GetComponentsInChildren<MeshFilter>(true).Select(r=>r.sharedMesh)).Where(m=>m!=null);
        return string.Join("|",meshes.Select(m=>{
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m,out string guid,out long localId);return guid+":"+localId;
        }).OrderBy(k=>k,StringComparer.Ordinal));
    }
    public static Vector3 Divide(Vector3 numerator,Vector3 denominator)=>new Vector3(numerator.x/denominator.x,numerator.y/denominator.y,numerator.z/denominator.z);
    public static void RequireUniform(Vector3 v,string name)
    {
        if(!float.IsFinite(v.x) || v.x<=0 || Mathf.Abs(v.x-v.y)>.0001f || Mathf.Abs(v.x-v.z)>.0001f)
            throw new InvalidOperationException("균등 비율 검토가 필요한 모델: "+name);
    }
    public static string WriteEvidence(string folder)
    {
        Directory.CreateDirectory(folder);
        var rows=new JArray(references.Select(r=>new JObject{{"id",r.id},{"slot",r.slot},{"theme",r.theme},{"source",r.path},
            {"modelScale",Vector(r.scale)},{"size",Vector(r.size)}}));
        var caps=new JObject();foreach(var pair in limits)caps[pair.Key]=new JObject{{"height",pair.Value.x},{"footprint",pair.Value.y}};
        caps["boss-large"]=new JObject{{"height",BossReviewMaxHeight},{"footprint",BossReviewMaxFootprint}};
        File.WriteAllText(Path.Combine(folder,"game-size-reference.json"),new JObject{{"references",rows},{"limits",caps}}.ToString());
        return "PASS: "+rows.Count+" current game sizing references";
    }
    public static string Inspect(string folder){Prepare();return WriteEvidence(folder);}
    public static JArray Vector(Vector3 v)=>new JArray(v.x,v.y,v.z);
}
