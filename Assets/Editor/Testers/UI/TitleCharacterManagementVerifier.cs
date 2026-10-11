using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Overburst.Appearance;
using Object=UnityEngine.Object;

public static class TitleCharacterManagementVerifier
{
    public const string Output="../개인파일/코덱스산출/UI/20261011_MenuTitleAndHover/Evidence/Management";
    static readonly List<string> checks=new List<string>();
    static void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
    static string Scenes()=>JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return new{s.path,s.isDirty,roots=s.rootCount};}));
    public static void Run()
    {
        AppearanceCustomizationBuilder.RequireIdle();checks.Clear();Directory.CreateDirectory(Path.GetFullPath(Output));string before=Scenes();int previews=EditorSceneManager.previewSceneCount;
        var active=SceneManager.GetActiveScene();var scene=SceneManager.GetSceneByPath(OverburstUIWorkshopBuilder.ScenePath);bool opened=!scene.IsValid()||!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(OverburstUIWorkshopBuilder.ScenePath,OpenSceneMode.Additive);
        try
        {
            var group=scene.GetRootGameObjects().Single(g=>g.name==OverburstUIWorkshopBuilder.GalleryName).transform.Find(TitleCharacterManagementBuilder.GroupName);
            Check(group&&group.childCount==10,"ten newly added UI states are registered in the existing management gallery");
            for(int i=0;i<10;i++)
            {
                var board=group.Find(TitleCharacterManagementBuilder.Names[i]);Check(board,"management state exists "+i);
                var title=board.GetComponentInChildren<TitleCharacterPanel>(true);var menu=board.GetComponentInChildren<OverburstGameMenu>(true);
                var source=title?title.gameObject:menu.gameObject;string path=title?TitleCharacterBuilder.PrefabPath:OverburstGameMenuBuilder.PrefabPath;
                Check(((RectTransform)board).sizeDelta==new Vector2(1920,1080)&&source.transform.localScale==Vector3.one,"saved exhibit retains full-size canvas and nonzero scale "+i);
                Check(PrefabUtility.GetPrefabInstanceStatus(source)==PrefabInstanceStatus.Connected,"connected production prefab "+i);
                Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source)==path,"same exact production source path and GUID "+i);
                Check(source.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"all registered scripts resolve "+i);
                Check(source.GetComponentsInChildren<Transform>(true).All(t=>t.gameObject.tag=="EditorOnly"&&t.gameObject.layer==31),"exhibit stays outside the gameplay camera and build "+i);
                Check(source.GetComponentsInChildren<GraphicRaycaster>(true).All(r=>!r.enabled),"exhibit cannot receive gameplay pointer input "+i);
                if(title)
                {
                    Check(title.managementPreview&&title.GetComponentInChildren<AppearanceCustomizationPanel>(true).managementPreview,"title and shared appearance cannot register game services "+i);
                    Check(title.GetComponentInChildren<AppearanceCustomizationPanel>(true).catalog==Resources.Load<CharacterAppearanceCatalog>(CharacterAppearanceCatalog.ResourcePath),"existing appearance catalog is shared "+i);
                    var appearance=title.GetComponentInChildren<AppearanceCustomizationPanel>(true);
                    Check(title.GetComponentsInChildren<AppearanceCustomizationPanel>(true).Length==1&&PrefabUtility.GetPrefabInstanceStatus(appearance.gameObject)==PrefabInstanceStatus.Connected
                        &&PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(appearance.gameObject)==AppearanceCustomizationBuilder.PrefabPath,"one connected production appearance source is reused "+i);
                    if(i==3||i==5||i==6)Check(BackdropHasScene(i==3?title.modalBackdrop.texture:i==5?appearance.creationConfirmationBackdrop.texture:title.quitBackdrop.texture),"modal backdrop retains the underlying scene "+i);
                }
                else Check(menu.managementPreview&&!menu.GetComponent<OverburstGameMenuGate>().enabled,"ESC exhibit cannot pause gameplay or claim Escape "+i);
                foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)})Capture(board.gameObject,i,size);
            }
            Check(!Resources.Load<TitleCharacterPanel>(TitleCharacterPanel.ResourcePath).managementPreview,"actual game title keeps runtime behavior enabled");
            Check(!Resources.Load<OverburstGameMenu>(OverburstGameMenu.ResourcePath).managementPreview,"actual game ESC keeps runtime behavior enabled");
            Check(!AssetDatabase.LoadAssetAtPath<AppearanceCustomizationPanel>(AppearanceCustomizationBuilder.PrefabPath).managementPreview,"shared product appearance keeps runtime behavior enabled");
            Check(!AssetDatabase.GetDependencies(TitleCharacterBuilder.PrefabPath).Any(p=>p.StartsWith(TitleCharacterManagementBuilder.Art+"/",StringComparison.Ordinal)),"exhibit character stills are not dependencies of the game title");
        }
        finally
        {
            if(active.IsValid()&&active.isLoaded)SceneManager.SetActiveScene(active);
            if(opened&&scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
        }
        Check(before==Scenes(),"existing user scenes dirty flags and root counts are preserved");Check(previews==EditorSceneManager.previewSceneCount,"all capture scenes are returned");
        File.WriteAllText(Path.GetFullPath(Output+"/native-result.json"),JsonConvert.SerializeObject(new{success=true,checks=checks.Count,cases=checks,before,after=Scenes(),previews,utc=DateTime.UtcNow,player="EXCLUDED_BY_USER"},Formatting.Indented));
    }
    static void Capture(GameObject board,int index,Vector2Int size)
    {
        var scene=EditorSceneManager.NewPreviewScene();GameObject root=null,cameraObject=null;RenderTexture rt=null;
        try
        {
            root=Object.Instantiate(board);SceneManager.MoveGameObjectToScene(root,scene);root.transform.SetParent(null,false);root.transform.position=Vector3.zero;
            cameraObject=new GameObject("Owned Management UI Capture",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraObject,scene);var camera=cameraObject.GetComponent<Camera>();camera.scene=scene;camera.enabled=false;camera.transform.position=new Vector3(0,0,-20);camera.orthographic=true;camera.orthographicSize=5;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            rt=new RenderTexture(size.x,size.y,24);rt.Create();camera.targetTexture=rt;
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
            var scaler=root.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            for(int pass=0;pass<3;pass++){Canvas.ForceUpdateCanvases();var request=new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=rt};if(UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera,request))UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);else camera.Render();}
            AppearanceCustomizationVerifier.Save(rt,Path.GetFullPath(Output+"/"+TitleCharacterManagementBuilder.States[index]+"-"+size.x+"x"+size.y+".png"));
            var pixels=new Texture2D(2,2);
            try{pixels.LoadImage(File.ReadAllBytes(Path.GetFullPath(Output+"/"+TitleCharacterManagementBuilder.States[index]+"-"+size.x+"x"+size.y+".png")));Check(pixels.GetPixels32().Any(p=>p.r>20||p.g>20||p.b>20),"captured state has visible UI pixels "+index+" "+size);}
            finally{Object.DestroyImmediate(pixels);}
            Check(root.GetComponentsInChildren<RawImage>(false).All(i=>i.texture||i.color.a==0||!i.enabled||!Visible(i)),"active state images resolve "+index+" "+size);
        }
        finally{if(root)Object.DestroyImmediate(root);if(rt){rt.Release();Object.DestroyImmediate(rt);}if(cameraObject)Object.DestroyImmediate(cameraObject);EditorSceneManager.ClosePreviewScene(scene);}
    }
    static bool Visible(RawImage image)
    {
        for(var t=image.transform;t!=null;t=t.parent){var group=t.GetComponent<CanvasGroup>();if(group&&group.alpha==0)return false;}
        return true;
    }
    static bool BackdropHasScene(Texture texture)
    {
        var pixels=new Texture2D(2,2);
        try
        {
            pixels.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(texture)));var colors=pixels.GetPixels32();int width=pixels.width;
            return colors.Where((p,index)=>index%width<width/4).Count(p=>p.r>20||p.g>20||p.b>20)>100;
        }
        finally{Object.DestroyImmediate(pixels);}
    }
}
