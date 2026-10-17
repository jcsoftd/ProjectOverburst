using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Editor-only baking. Gameplay never renders an extra camera for item preparation.
public static class WorldDropPresentationBuilder
{
    const string Root = "Assets/ProjectOverburst/03_Features/Items/Art/WorldDrop";
    public static void CreateMapPickup(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("EditMode required");
        var definition=Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        string definitionPath=AssetDatabase.GetAssetPath(definition);Backup(definitionPath,output);Backup(definitionPath+".meta",output);
        if(definition.worldPickupPrefab)throw new InvalidOperationException("Map already has an authored pickup");
        string path=Root+"/MapShadow.png";var texture=new Texture2D(128,128,TextureFormat.RGBA32,false);
        var colors=new Color32[128*128];
        for(int y=0;y<128;y++)for(int x=0;x<128;x++)
        {float radius=new Vector2(x-63.5f,y-63.5f).magnitude;colors[y*128+x]=new Color32(255,255,255,(byte)(Mathf.Clamp01((57-radius)/3)*255));}
        texture.SetPixels32(colors);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());Object.DestroyImmediate(texture);ImportTexture(path,128,true);
        var shadowMat=new Material(Shader.Find("Overburst/World Item Ground Shadow"));shadowMat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        AssetDatabase.CreateAsset(shadowMat,Root+"/MapShadow.mat");
        var bodyMat=new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));bodyMat.SetFloat("_SpecularHighlights",0);bodyMat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        bodyMat.SetFloat("_ReceiveShadows",0);bodyMat.EnableKeyword("_RECEIVE_SHADOWS_OFF");AssetDatabase.CreateAsset(bodyMat,Root+"/MapBody.mat");
        var root=new GameObject("PF_MapWorldPickup");
        try
        {
            var model=GameObject.CreatePrimitive(PrimitiveType.Sphere);model.name="ModelRoot";model.transform.SetParent(root.transform,false);model.transform.localScale=Vector3.one*.24f;
            Object.DestroyImmediate(model.GetComponent<Collider>());var body=model.GetComponent<MeshRenderer>();body.sharedMaterial=bodyMat;body.shadowCastingMode=ShadowCastingMode.Off;body.receiveShadows=false;
            root.AddComponent<WorldItemPickup>();root.AddComponent<WorldPickupPresentation>();
            var node=new GameObject("Baked Item Shadow",typeof(MeshFilter),typeof(MeshRenderer));node.transform.SetParent(root.transform,false);
            node.GetComponent<MeshFilter>().sharedMesh=Quad();var renderer=node.GetComponent<MeshRenderer>();renderer.sharedMaterial=shadowMat;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            root.AddComponent<WorldItemGroundShadow>().Configure(node.transform,Vector3.zero,Vector2.one*.27f,Vector3.zero);
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,"Assets/ProjectOverburst/Resources/Items/WorldPickups/PF_MapWorldPickup.prefab");
            var so=new SerializedObject(definition);so.FindProperty("worldPickupPrefab").objectReferenceValue=prefab;so.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssetIfDirty(definition);
        }
        finally {Object.DestroyImmediate(root);}
    }
    public static void Apply(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Idle EditMode required");
        Directory.CreateDirectory(output);
        EnsureFolder(Root);
        var prefabs = Prefabs().OrderBy(AssetDatabase.GetAssetPath).ToArray();
        var manifest = new List<object>();
        var materials = new Dictionary<Material, Material>();
        var scene = EditorSceneManager.NewPreviewScene();
        var cameraObject = new GameObject("Owned item bake camera", typeof(Camera));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.scene = scene;
        camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear; camera.nearClipPlane = .01f; camera.farClipPlane = 100;
        camera.allowHDR = false; camera.allowMSAA = false;
        var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false; data.renderShadows = false;
        var white = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        white.SetColor("_BaseColor", Color.white);
        try
        {
            foreach (var prefab in prefabs)
            {
                string path = AssetDatabase.GetAssetPath(prefab), id = prefab.name;
                Backup(path, output); Backup(path + ".meta", output);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                    var before = renderers.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToArray();
                    foreach (var renderer in renderers)
                    {
                        var slots = renderer.sharedMaterials;
                        for (int i = 0; i < slots.Length; i++)
                        {
                            if (!slots[i]) continue;
                            if (!materials.TryGetValue(slots[i], out var low))
                            {
                                low = MakeMaterial(slots[i]); materials.Add(slots[i], low);
                            }
                            slots[i] = low;
                        }
                        renderer.sharedMaterials = slots;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                    }
                    var presentation = root.GetComponent<WorldPickupPresentation>();
                    var visual = presentation ? presentation.VisualRoot : root.transform;
                    Vector3 settled = presentation ? new SerializedObject(presentation).FindProperty("settledLocalEulerAngles").vector3Value : Vector3.zero;
                    var clone = Object.Instantiate(root); clone.name = "Owned silhouette bake";
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone, scene);
                    try
                    {
                        clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        var cp = clone.GetComponent<WorldPickupPresentation>();
                        var cv = cp ? cp.VisualRoot : clone.transform;
                        cv.localRotation = Quaternion.Euler(settled);
                        bool weapon = id.EndsWith("_World", StringComparison.Ordinal);
                        if (weapon) cv.localScale *= WorldPickupPresentation.WeaponDropVisualScale;
                        var meshes = clone.GetComponentsInChildren<MeshRenderer>(true);
                        Bounds bounds = meshes[0].bounds;
                        foreach (var r in meshes) bounds.Encapsulate(r.bounds);
                        float side = Mathf.Max(bounds.size.x, bounds.size.z) * 1.14f;
                        side = Mathf.Max(.1f, side);
                        camera.transform.SetPositionAndRotation(bounds.center + Vector3.up * 10, Quaternion.Euler(90, 0, 0));
                        camera.orthographicSize = side * .5f;
                        foreach (var r in meshes) r.sharedMaterials = Enumerable.Repeat(white, r.sharedMaterials.Length).ToArray();
                        string maskPath = Root + "/" + id + "_Shadow.png";
                        BakeSilhouette(camera, maskPath);
                        var shadowMaterial = new Material(Shader.Find("Overburst/World Item Ground Shadow"));
                        shadowMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath));
                        shadowMaterial.SetColor("_Color", new Color(0, 0, 0, .24f));
                        string shadowPath = Root + "/" + id + "_Shadow.mat";
                        AssetDatabase.CreateAsset(shadowMaterial, shadowPath);
                        var node = new GameObject("Baked Item Shadow", typeof(MeshFilter), typeof(MeshRenderer));
                        node.transform.SetParent(root.transform, false);
                        node.GetComponent<MeshFilter>().sharedMesh = Quad();
                        var shadowRenderer = node.GetComponent<MeshRenderer>(); shadowRenderer.sharedMaterial = shadowMaterial;
                        shadowRenderer.shadowCastingMode = ShadowCastingMode.Off; shadowRenderer.receiveShadows = false;
                        var shadow = root.AddComponent<WorldItemGroundShadow>();
                        shadow.Configure(node.transform, new Vector3(bounds.center.x, 0, bounds.center.z), new Vector2(side, side), settled);
                        manifest.Add(new { path, materialsBefore = before.Select(AssetDatabase.GetAssetPath).ToArray(),
                            materialsAfter = renderers.SelectMany(r=>r.sharedMaterials).Select(AssetDatabase.GetAssetPath).ToArray(),
                            shadow = maskPath, maskSize = 128, realtimeShadows = false });
                    }
                    finally { Object.DestroyImmediate(clone); }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                File.WriteAllText(Path.Combine(output, "progress.json"), JsonConvert.SerializeObject(new { completed=manifest.Count, total=prefabs.Length, path }));
            }
            foreach (var material in materials.Values) AssetDatabase.SaveAssetIfDirty(material);
            File.WriteAllText(Path.Combine(output, "assets.json"), JsonConvert.SerializeObject(manifest, Formatting.Indented));
        }
        finally { Object.DestroyImmediate(white); EditorSceneManager.ClosePreviewScene(scene); }
    }

    static IEnumerable<GameObject> Prefabs()
    {
        var result = new HashSet<GameObject>();
        for (int level=1;level<=100;level++) foreach (var d in WeaponLootPolicy.DefinitionsForLevel(level)) if(d.worldPickupPrefab) result.Add(d.worldPickupPrefab);
        foreach(var d in FlaskLootPolicy.Catalog) if(d.worldPickupPrefab) result.Add(d.worldPickupPrefab);
        foreach(var d in ElementGemLootPolicy.Catalog) if(d.worldPickupPrefab) result.Add(d.worldPickupPrefab);
        foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/ProjectOverburst/Resources/Items/WorldPickups"}))
            result.Add(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)));
        return result;
    }
    static Material MakeMaterial(Material source)
    {
        var target = new Material(Shader.Find("Universal Render Pipeline/Simple Lit")) { name=source.name+"_Drop" };
        target.SetColor("_BaseColor", source.GetColor("_BaseColor"));
        if(source.GetTexture("_BaseMap")) target.SetTexture("_BaseMap", SmallTexture(source.GetTexture("_BaseMap"), source.name+"_Base"));
        target.SetFloat("_SpecularHighlights", 0); target.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        target.SetFloat("_ReceiveShadows", 0); target.EnableKeyword("_RECEIVE_SHADOWS_OFF");
        target.SetFloat("_Smoothness", .15f);
        if(source.IsKeywordEnabled("_EMISSION"))
        {
            target.SetColor("_EmissionColor",source.GetColor("_EmissionColor"));
            if(source.GetTexture("_EmissionMap")) target.SetTexture("_EmissionMap",SmallTexture(source.GetTexture("_EmissionMap"),source.name+"_Emission"));
            target.EnableKeyword("_EMISSION");
        }
        AssetDatabase.CreateAsset(target,Root+"/"+target.name+".mat"); return target;
    }
    static Texture2D SmallTexture(Texture source,string name)
    {
        string path=Root+"/"+name+".png";
        var previous=RenderTexture.active; var rt=RenderTexture.GetTemporary(256,256,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        Texture2D image=null;
        try { Graphics.Blit(source,rt);RenderTexture.active=rt;image=new Texture2D(256,256,TextureFormat.RGBA32,false,false);image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG()); }
        finally { RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);if(image)Object.DestroyImmediate(image); }
        ImportTexture(path,256,false);return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static void BakeSilhouette(Camera camera,string path)
    {
        var previous=RenderTexture.active;var rt=RenderTexture.GetTemporary(128,128,24,RenderTextureFormat.ARGB32);Texture2D image=null;
        try
        {
            camera.targetTexture=rt;
            var request=new UniversalRenderPipeline.SingleCameraRequest {destination=rt};
            if(!RenderPipeline.SupportsRenderRequest(camera,request))throw new InvalidOperationException("URP bake request unsupported");
            RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=rt;
            image=new Texture2D(128,128,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();
            var pixels=image.GetPixels32();var soft=new Color32[pixels.Length];int covered=0;
            for(int y=1;y<127;y++)for(int x=1;x<127;x++){int sum=0;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)sum+=pixels[(y+dy)*128+x+dx].a;byte alpha=(byte)(sum/9);soft[y*128+x]=new Color32(255,255,255,alpha);if(alpha>0)covered++;}
            if(covered<8 || covered>16000)throw new InvalidOperationException("Invalid baked silhouette: "+path+" "+covered);
            image.SetPixels32(soft);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        }
        finally { camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);if(image)Object.DestroyImmediate(image); }
        ImportTexture(path,128,true);
    }
    static void ImportTexture(string path,int size,bool mask)
    {
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.maxTextureSize=size;importer.isReadable=false;
        importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.alphaIsTransparency=mask;
        importer.textureCompression=TextureImporterCompression.Compressed;importer.sRGBTexture=!mask;importer.SaveAndReimport();
    }
    static Mesh Quad()
    {
        string path=Root+"/ShadowQuad.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh)return mesh;
        mesh=new Mesh{name="Item silhouette quad"};mesh.vertices=new[]{new Vector3(-.5f,0,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f),new Vector3(.5f,0,-.5f)};
        mesh.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    static void EnsureFolder(string path){if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
    static void Backup(string path,string output){string target=Path.Combine(output,"Before",path);Directory.CreateDirectory(Path.GetDirectoryName(target));if(!File.Exists(target))File.Copy(path,target);}
}
