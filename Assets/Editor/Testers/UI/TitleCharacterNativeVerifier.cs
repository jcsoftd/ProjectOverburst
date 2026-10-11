using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Appearance;
using Overburst.Persistence;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class TitleCharacterNativeVerifier
{
    public const string Output=TitleCharacterBuilder.Output+"/Evidence/Native";
    static readonly List<string> checks=new List<string>();
    static void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);checks.Add(name);}
    static string Scenes()=>JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>new{path=SceneManager.GetSceneAt(i).path,dirty=SceneManager.GetSceneAt(i).isDirty,roots=SceneManager.GetSceneAt(i).rootCount}));
    public static void Run()
    {
        AppearanceCustomizationBuilder.RequireIdle();checks.Clear();string before=Scenes();int previews=EditorSceneManager.previewSceneCount;
        var folder=Path.GetFullPath(Output);Directory.CreateDirectory(folder);
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(TitleCharacterBuilder.PrefabPath);
            Check(prefab&&GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab)==0,"title prefab and scripts resolve");
            foreach(var dep in AssetDatabase.GetDependencies(TitleCharacterBuilder.PrefabPath,true))Check(!dep.StartsWith("../"),"product dependency stays in Assets: "+dep);
            string saves=folder+"/Fixture-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            var registry=UnityEngine.Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);var catalog=UnityEngine.Resources.Load<CharacterAppearanceCatalog>(CharacterAppearanceCatalog.ResourcePath);
            var state=AccountCharacterCommands.First(Guid.NewGuid().ToString("N"),"세리아","knight",catalog.defaultAppearance,registry);state.level=24;state.skillTree=AccountSkillTree.GrantEligible(null,24);
            AccountCharacterCommands.Create(state,Guid.NewGuid().ToString("N"),"아린","knight",catalog.defaultAppearance,registry);state.characters[1].personal.level=12;state.characters[1].personal.skillTree=AccountSkillTree.GrantEligible(null,12);
            AccountCharacterCommands.Create(state,Guid.NewGuid().ToString("N"),"루나","knight",catalog.defaultAppearance,registry);new EasySaveAccountStore(saves).Save(state,state.lastTransactionId);
            foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(2560,1080)})Capture(prefab,saves,size,folder);
            Check(before==Scenes(),"user scenes dirty flags and roots preserved");Check(previews==EditorSceneManager.previewSceneCount,"owned preview scenes returned");
            File.WriteAllText(folder+"/result.json",JsonConvert.SerializeObject(new{status="PASS_NATIVE_SCOPED",checks=checks.Count,cases=checks,scenesBefore=before,scenesAfter=Scenes(),previewScenesBefore=previews,previewScenesAfter=EditorSceneManager.previewSceneCount,utc=DateTime.UtcNow,player="EXCLUDED_BY_USER",visualParity="REVIEW_REQUIRED"},Formatting.Indented));
        }
        catch(Exception error){File.WriteAllText(folder+"/result.json",JsonConvert.SerializeObject(new{status="FAIL",checks=checks.Count,cases=checks,error=error.ToString(),scenesBefore=before,scenesAfter=Scenes()},Formatting.Indented));throw;}
    }
    static void Capture(GameObject prefab,string save,Vector2Int size,string folder)
    {
        var scene=EditorSceneManager.NewPreviewScene();GameObject root=null,cameraRoot=null;RenderTexture rt=null;AppearanceCustomizationPanel appearance=null;var shots=new List<Texture2D>();
        try
        {
            cameraRoot=new GameObject("Owned Title UI Capture",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraRoot,scene);var camera=cameraRoot.GetComponent<Camera>();camera.scene=scene;camera.enabled=false;camera.transform.position=new Vector3(0,0,-20);camera.orthographic=true;camera.orthographicSize=5;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.cullingMask=1<<5;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            rt=new RenderTexture(size.x,size.y,24);rt.Create();camera.targetTexture=rt;root=Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(root,scene);var panel=root.GetComponent<TitleCharacterPanel>();panel.canvas.renderMode=RenderMode.ScreenSpaceCamera;panel.canvas.worldCamera=camera;panel.canvas.planeDistance=10;
            panel.Open(save);Render(camera,rt);AppearanceCustomizationVerifier.Save(rt,folder+"/title-"+size.x+"x"+size.y+".png");
            panel.quit.onClick.Invoke();Check(panel.quitDialog.activeSelf&&panel.quitDialog.GetComponent<Canvas>().sortingOrder==400,"quit opens approved confirmation layer "+size);Render(camera,rt);AppearanceCustomizationVerifier.Save(rt,folder+"/quit-"+size.x+"x"+size.y+".png");panel.quitCancel.onClick.Invoke();Check(!panel.quitDialog.activeSelf&&panel.Screen=="title","quit cancellation returns title "+size);
            panel.Show("characters");CaptureModels(panel,scene,shots);Render(camera,rt);AppearanceCustomizationVerifier.Save(rt,folder+"/characters-"+size.x+"x"+size.y+".png");ClearShots(panel,shots);
            Check(panel.rosterCount.text=="3명"&&panel.characterName.text=="세리아","fixture roster renders actual saved names "+size);
            panel.Show("classes");CaptureModels(panel,scene,shots);
            for(int i=0;i<3;i++)
            {
                panel.SelectClass(i);typeof(TitleCharacterPanel).GetField("rotation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(panel,-120f*i);typeof(TitleCharacterPanel).GetMethod("PlaceCast",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(panel,null);
                foreach(var preview in panel.classPreviews){var shot=preview.CaptureStill(768,1152,.7f);shots.Add(shot);panel.classTargets[Array.IndexOf(panel.classPreviews,preview)].texture=shot;}
                Check(panel.classNext.interactable==(i==0),"only knight creation enabled "+i+" "+size);
                Check(Mathf.Abs(Mathf.DeltaAngle(panel.classPreviews[i].Model.transform.localEulerAngles.y,180))<.1f,"selected character faces front "+i+" "+size);
                Check(Vector2.Distance(((RectTransform)panel.classButtons[i].transform).pivot,new Vector2(.5f,0))<.001f,"showcase bottom center pivot "+i);
                Render(camera,rt);AppearanceCustomizationVerifier.Save(rt,folder+"/classes-"+i+"-"+size.x+"x"+size.y+".png");
            }
            panel.SelectClass(0);typeof(TitleCharacterPanel).GetField("rotation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(panel,0f);typeof(TitleCharacterPanel).GetMethod("PlaceCast",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(panel,null);
            foreach(var preview in panel.classPreviews){var shot=preview.CaptureStill(768,1152,.7f);shots.Add(shot);panel.classTargets[Array.IndexOf(panel.classPreviews,preview)].texture=shot;}
            panel.classNext.onClick.Invoke();Check(panel.nameDialog.activeSelf&&panel.nameInput.characterLimit==12,"existing input field opens name dialog "+size);Check(panel.nameDialog.GetComponent<Canvas>().overrideSorting&&panel.nameDialog.GetComponent<Canvas>().sortingOrder>350,"name modal remains above shared creation input "+size);Render(camera,rt);AppearanceCustomizationVerifier.Save(rt,folder+"/name-"+size.x+"x"+size.y+".png");
            var metrics=panel.GetComponentsInChildren<TMP_Text>(false).Where(t=>t.text.Length>0).Select(t=>new{name=t.name,text=t.text,font=AssetDatabase.GetAssetPath(t.font),size=t.fontSize,color=new[]{t.color.r,t.color.g,t.color.b,t.color.a},overflow=t.isTextOverflowing}).ToArray();
            File.WriteAllText(folder+"/metrics-"+size.x+"x"+size.y+".json",JsonConvert.SerializeObject(metrics,Formatting.Indented));
            panel.nameInput.text="검증기사";panel.nameNext.onClick.Invoke();appearance=root.GetComponentInChildren<AppearanceCustomizationPanel>(true);
            if(appearance&&appearance.preview.Model)SceneManager.MoveGameObjectToScene(appearance.preview.Model.transform.root.gameObject,scene);
            Check(appearance&&appearance.IsCreation&&appearance.Session!=null,"creation reuses original panel and session "+size);appearance.rootCanvas.renderMode=RenderMode.ScreenSpaceCamera;appearance.rootCanvas.worldCamera=camera;appearance.rootCanvas.planeDistance=10;Render(camera,rt);
            var stage=(RectTransform)appearance.surface.transform;Check(Vector2.Distance(stage.sizeDelta,new Vector2(1920,1080))<.01f,"creation stage uses approved fixed aspect "+size);
            var background=appearance.surface.transform.Find("Dressing Alcove").GetComponent<UnityEngine.UI.Image>();var imageRect=background.rectTransform.rect;
            Check(imageRect.width>=stage.rect.width-.1f&&imageRect.height>=stage.rect.height-.1f&&Mathf.Abs(imageRect.width/imageRect.height-(float)background.sprite.texture.width/background.sprite.texture.height)<.001f,"shared background covers stage without distortion "+size);
            Check(!appearance.appearanceTab.gameObject.activeSelf&&!appearance.headgearToggle.gameObject.activeSelf&&!appearance.surface.transform.Find("View Hint").gameObject.activeSelf&&!appearance.GetComponentsInChildren<AppearanceOptionalTab>(true).Any(t=>t.tabButton.gameObject.activeInHierarchy),"creation shows only approved controls "+size);
            SceneManager.MoveGameObjectToScene(appearance.preview.Model.transform.root.gameObject,scene);var creationShot=appearance.preview.CaptureStill(1920,1080,.7f);shots.Add(creationShot);appearance.characterTarget.texture=creationShot;Render(camera,rt);AppearanceCustomizationVerifier.Save(rt,folder+"/creation-"+size.x+"x"+size.y+".png");
            panel.CaptureCreationConfirmation(appearance.creationConfirmationBackdrop);appearance.back.onClick.Invoke();Render(camera,rt);AppearanceCustomizationVerifier.Save(rt,folder+"/creation-cancel-"+size.x+"x"+size.y+".png");appearance.continueEditing.onClick.Invoke();panel.ReleaseCreationConfirmation();Check(!appearance.discardConfirmation.activeSelf&&appearance.Session!=null,"cancel confirmation returns the same appearance draft "+size);
            appearance.Close();Check(!appearance.IsCreation&&appearance.headgearToggle.gameObject.activeSelf&&appearance.surface.transform.Find("Title").GetComponent<TMP_Text>().text=="외모 변경","normal stylist context restores after creation "+size);
            panel.nameBack.onClick.Invoke();panel.Show("title");Check(panel.classPreviews.All(p=>!p.Model&&!p.Texture)&&!panel.rosterPreview.Model&&!panel.rosterPreview.Texture,"screen transition releases preview models and render textures "+size);
        }
        finally{if(appearance)appearance.Close();foreach(var shot in shots)if(shot)Object.DestroyImmediate(shot);if(root)Object.DestroyImmediate(root);if(rt){rt.Release();Object.DestroyImmediate(rt);}if(cameraRoot)Object.DestroyImmediate(cameraRoot);EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void CaptureModels(TitleCharacterPanel panel,Scene scene,List<Texture2D> shots)
    {
        if(panel.rosterPreview.Model){SceneManager.MoveGameObjectToScene(panel.rosterPreview.Model.transform.root.gameObject,scene);var shot=panel.rosterPreview.CaptureStill(1920,1080,.7f);shots.Add(shot);panel.rosterTarget.texture=shot;}
        for(int i=0;i<3;i++)if(panel.classPreviews[i].Model){SceneManager.MoveGameObjectToScene(panel.classPreviews[i].Model.transform.root.gameObject,scene);var shot=panel.classPreviews[i].CaptureStill(768,1152,.7f);shots.Add(shot);panel.classTargets[i].texture=shot;}
    }
    static void ClearShots(TitleCharacterPanel panel,List<Texture2D> shots){panel.rosterTarget.texture=panel.rosterPreview.Texture;for(int i=0;i<3;i++)panel.classTargets[i].texture=panel.classPreviews[i].Texture;foreach(var shot in shots)Object.DestroyImmediate(shot);shots.Clear();}
    static void Render(Camera camera,RenderTexture rt)
    {
        for(int i=0;i<3;i++){Canvas.ForceUpdateCanvases();var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};if(RenderPipeline.SupportsRenderRequest(camera,request))RenderPipeline.SubmitRenderRequest(camera,request);else camera.Render();}
        if(rt.width!=1920||rt.height!=1080)return;
        var panel=camera.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<TitleCharacterPanel>()).SingleOrDefault();if(!panel)return;
        var appearance=panel.GetComponentInChildren<AppearanceCustomizationPanel>(true);string screen=panel.quitDialog.activeSelf?"quit":panel.NameDialogOpen?"name":appearance&&appearance.Session!=null?(appearance.discardConfirmation.activeSelf?"creation-cancel":"creation"):panel.Screen;
        if(screen=="classes")screen+="-"+panel.classEnglish.text.ToLowerInvariant();
        var values=panel.GetComponentsInChildren<RectTransform>(false).Select(r=>{var corners=new Vector3[4];r.GetWorldCorners(corners);var points=corners.Select(c=>RectTransformUtility.WorldToScreenPoint(camera,c)).ToArray();var t=r.GetComponent<TMP_Text>();return new{path=PathOf(r,panel.transform),text=t?t.text:null,font=t?AssetDatabase.GetAssetPath(t.font):null,fontSize=t?t.fontSize:0,rect=new{x=points.Min(p=>p.x),y=rt.height-points.Max(p=>p.y),width=points.Max(p=>p.x)-points.Min(p=>p.x),height=points.Max(p=>p.y)-points.Min(p=>p.y)}};}).ToArray();
        File.WriteAllText(Path.GetFullPath(Output+"/ui-"+screen+".json"),JsonConvert.SerializeObject(values,Formatting.Indented));
    }
    static string PathOf(Transform value,Transform root){string path=value.name;while(value.parent&&value!=root){value=value.parent;path=value.name+"/"+path;}return path;}
}
