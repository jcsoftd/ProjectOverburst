using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Overburst.Appearance;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

// Connected production instances. Only exhibit state, framing and character stills are overrides.
public static class TitleCharacterManagementBuilder
{
    public const string GroupName="08_Title • 타이틀·캐릭터·메뉴";
    public const string Art="Assets/ProjectOverburst/05_Art/UI/Title/Management";
    public static readonly string[] States={"title","characters","classes","name","creation","discard","quit","menu","return","settings"};
    public static readonly string[] Names={"18 타이틀","19 보유 캐릭터","20 클래스 선택","21 이름 입력","22 캐릭터 생성","23 생성 취소 확인","24 타이틀 종료 확인","25 Esc 일시정지","26 타이틀 복귀 확인","27 설정"};
    [MenuItem("OVERBURST/UI/Add Title And Character Screens To Management")]
    public static void Apply()
    {
        AppearanceCustomizationBuilder.RequireIdle();
        var previous=SceneManager.GetActiveScene();var scene=SceneManager.GetSceneByPath(OverburstUIWorkshopBuilder.ScenePath);
        bool opened=!scene.IsValid()||!scene.isLoaded;
        if(!opened&&scene.isDirty)throw new InvalidOperationException("관리씬의 미저장 변경을 먼저 보존해 주세요.");
        if(opened)scene=EditorSceneManager.OpenScene(OverburstUIWorkshopBuilder.ScenePath,OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var gallery=scene.GetRootGameObjects().Single(g=>g.name==OverburstUIWorkshopBuilder.GalleryName).transform;
            var group=gallery.Find(GroupName);if(!group){group=new GameObject(GroupName).transform;group.SetParent(gallery,false);}group.gameObject.tag="EditorOnly";
            var font=gallery.GetComponentsInChildren<Text>(true).First(t=>t.name=="Board Title").font;
            string fixture=Fixture();
            for(int i=0;i<States.Length;i++)
            {
                var existing=group.Find(Names[i]);var board=existing?(RectTransform)existing:Rect(Names[i],group,new Vector2(1920,1080));
                board.anchoredPosition=new Vector2(i%3*2120,-9240-i/3*1320);
                var canvas=board.GetComponent<Canvas>();if(!canvas)canvas=board.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
                Label(board,font,"Board Title",Names[i],600,32);
                Label(board,font,"Board Note","1920 × 1080 / 게임과 동일한 정식 프리팹 / 전시 상태만 다름",560,18);
                bool menu=i>=7;string path=menu?OverburstGameMenuBuilder.PrefabPath:TitleCharacterBuilder.PrefabPath;
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var instance=board.Find(source.name)?.gameObject;
                if(!instance)instance=(GameObject)PrefabUtility.InstantiatePrefab(source,board);
                if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance)!=path)throw new InvalidOperationException("다른 전시 원본은 덮어쓰지 않습니다.");
                StretchRoot((RectTransform)instance.transform);
                if(menu)MenuState(instance.GetComponent<OverburstGameMenu>(),States[i]);
                else
                {
                    // Modal capture needs its own root canvas, exactly as in the running game.
                    var previewScene=EditorSceneManager.NewPreviewScene();GameObject cameraObject=null;RenderTexture frame=null;
                    instance.transform.SetParent(null,false);SceneManager.MoveGameObjectToScene(instance,previewScene);
                    try
                    {
                        foreach(var t in instance.GetComponentsInChildren<Transform>(true))t.gameObject.layer=5;
                        var previewRect=(RectTransform)instance.transform;previewRect.localScale=Vector3.one;previewRect.anchorMin=previewRect.anchorMax=new Vector2(.5f,.5f);previewRect.sizeDelta=new Vector2(1920,1080);
                        instance.GetComponent<CanvasScaler>().enabled=true;
                        cameraObject=new GameObject("Owned Management State Capture",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraObject,previewScene);
                        var camera=cameraObject.GetComponent<Camera>();camera.enabled=false;camera.scene=previewScene;camera.transform.position=new Vector3(0,0,-20);camera.orthographic=true;camera.orthographicSize=5;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.cullingMask=1<<5;
                        frame=new RenderTexture(1920,1080,24);frame.Create();camera.targetTexture=frame;
                        var previewCanvas=instance.GetComponent<Canvas>();previewCanvas.enabled=true;previewCanvas.renderMode=RenderMode.ScreenSpaceCamera;previewCanvas.worldCamera=camera;previewCanvas.planeDistance=10;
                        Canvas.ForceUpdateCanvases();
                        TitleState(instance.GetComponent<TitleCharacterPanel>(),States[i],fixture);
                    }
                    finally
                    {
                        SceneManager.MoveGameObjectToScene(instance,scene);instance.transform.SetParent(board,false);
                        if(cameraObject){cameraObject.GetComponent<Camera>().targetTexture=null;Object.DestroyImmediate(cameraObject);}
                        if(frame){frame.Release();Object.DestroyImmediate(frame);}EditorSceneManager.ClosePreviewScene(previewScene);
                    }
                }
                foreach(var c in instance.GetComponentsInChildren<Canvas>(true)){c.renderMode=RenderMode.WorldSpace;c.worldCamera=null;c.overrideSorting=false;c.enabled=true;}
                StretchRoot((RectTransform)instance.transform);
                foreach(var appearance in instance.GetComponentsInChildren<AppearanceCustomizationPanel>(true))StretchRoot((RectTransform)appearance.transform);
                foreach(var c in instance.GetComponentsInChildren<CanvasScaler>(true))c.enabled=false;
                foreach(var c in instance.GetComponentsInChildren<GraphicRaycaster>(true))c.enabled=false;
                foreach(var c in instance.GetComponentsInChildren<AppearanceButtonVisual>(true))c.enabled=false;
                foreach(var t in board.GetComponentsInChildren<Transform>(true)){t.gameObject.tag="EditorOnly";t.gameObject.layer=31;}
                Canvas.ForceUpdateCanvases();
                // Overlay modal capture can resize a reused root and its parent canvas in Edit Mode.
                board.sizeDelta=new Vector2(1920,1080);
                StretchRoot((RectTransform)instance.transform);
                foreach(var appearance in instance.GetComponentsInChildren<AppearanceCustomizationPanel>(true))StretchRoot((RectTransform)appearance.transform);
                foreach(var t in instance.GetComponentsInChildren<Transform>(true))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
                    foreach(var c in t.GetComponents<Component>())if(c)PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                }
                FreezeRoot((RectTransform)instance.transform);
                foreach(var appearance in instance.GetComponentsInChildren<AppearanceCustomizationPanel>(true))FreezeRoot((RectTransform)appearance.transform);
            }
            EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new IOException("관리씬 저장 실패");
        }
        finally
        {
            if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
            if(opened&&scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
        }
    }
    static string Fixture()
    {
        var folder=Path.GetFullPath(TitleCharacterBuilder.Output+"/Evidence/Management/Fixture-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);var catalog=Resources.Load<CharacterAppearanceCatalog>(CharacterAppearanceCatalog.ResourcePath);
        var state=AccountCharacterCommands.First(Guid.NewGuid().ToString("N"),"세리아","knight",catalog.defaultAppearance,registry);
        state.level=24;state.skillTree=AccountSkillTree.GrantEligible(null,24);
        AccountCharacterCommands.Create(state,Guid.NewGuid().ToString("N"),"아린","knight",catalog.defaultAppearance,registry);
        new EasySaveAccountStore(folder).Save(state,state.lastTransactionId);return folder;
    }
    static void TitleState(TitleCharacterPanel p,string state,string fixture)
    {
        p.managementPreview=true;var appearance=p.GetComponentInChildren<AppearanceCustomizationPanel>(true);
        if(!appearance)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(AppearanceCustomizationBuilder.PrefabPath);
            appearance=((GameObject)PrefabUtility.InstantiatePrefab(source,p.transform)).GetComponent<AppearanceCustomizationPanel>();
        }
        appearance.managementPreview=true;
        p.nameDialog.SetActive(false);p.quitDialog.SetActive(false);appearance.surface.SetActive(false);
        p.Open(fixture);p.Show(state=="characters"?"characters":state=="title"||state=="quit"?"title":"classes");
        if(state=="quit")p.quit.onClick.Invoke();
        var temporary=new List<Texture2D>();
        try
        {
        if(state=="name"||state=="creation"||state=="discard")
        {
            RenderPreviews(p,appearance,temporary);p.classNext.onClick.Invoke();
        }
        if(state=="creation"||state=="discard")
        {
            // The gallery already has a connected shared appearance instance; the game installs this same source on demand.
            if(!appearance.OpenCreation(null,_=>false,()=>{}))throw new InvalidOperationException("공용 캐릭터 생성 전시를 열지 못했습니다.");
            appearance.SetCreationName("새로운기사");p.nameDialog.SetActive(false);p.ReleaseCreationConfirmation();
            typeof(TitleCharacterPanel).GetMethod("CloseVisuals",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(p,null);
            p.classesScreen.SetActive(false);
            if(state=="discard"){RenderPreviews(p,appearance,temporary);p.CaptureCreationConfirmation(appearance.creationConfirmationBackdrop);appearance.back.onClick.Invoke();}
        }
        var stills=new Dictionary<RawImage,Texture2D>();
        int previewIndex=0;
        foreach(var preview in p.classPreviews.Concat(new[]{p.rosterPreview,appearance.preview}))
        {
            int index=previewIndex++;
            if(!preview.Model)continue;
            var target=preview==appearance.preview?appearance.characterTarget:preview==p.rosterPreview?p.rosterTarget:p.classTargets[Array.IndexOf(p.classPreviews,preview)];
            var shot=preview.CaptureStill(preview==p.rosterPreview||preview==appearance.preview?1920:768,preview==p.rosterPreview||preview==appearance.preview?1080:1152,.7f);
            stills[target]=Import(shot,state+"-"+index+"-"+target.name);Object.DestroyImmediate(shot);preview.Close();
        }
        foreach(var image in new[]{p.modalBackdrop,p.quitBackdrop,appearance.creationConfirmationBackdrop})
        {
            if(!image||!(image.texture is RenderTexture rt))continue;
            var previous=RenderTexture.active;var shot=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
            try{RenderTexture.active=rt;shot.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);shot.Apply();stills[image]=Import(shot,state+"-"+image.name);}
            finally{RenderTexture.active=previous;Object.DestroyImmediate(shot);}
        }
        // Release temporary captures while preserving the authored exhibit state.
        p.ReleaseCreationConfirmation();
        typeof(TitleCharacterPanel).GetMethod("ReleaseModalBackdrop",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(p,null);
        foreach(var pair in stills)pair.Key.texture=pair.Value;
        }
        finally{foreach(var shot in temporary)if(shot)Object.DestroyImmediate(shot);}
    }
    static void RenderPreviews(TitleCharacterPanel p,AppearanceCustomizationPanel appearance,List<Texture2D> shots)
    {
        for(int i=0;i<p.classPreviews.Length;i++)if(p.classPreviews[i].Model){var shot=p.classPreviews[i].CaptureStill(768,1152);shots.Add(shot);p.classTargets[i].texture=shot;}
        if(appearance.preview.Model){var shot=appearance.preview.CaptureStill(1920,1080);shots.Add(shot);appearance.characterTarget.texture=shot;}
    }
    static void MenuState(OverburstGameMenu p,string state)
    {
        p.managementPreview=true;p.GetComponent<OverburstGameMenuGate>().enabled=false;
        p.group.gameObject.SetActive(true);p.group.alpha=1;p.group.blocksRaycasts=false;p.group.interactable=true;
        p.mainPanel.gameObject.SetActive(state!="settings");p.settings.gameObject.SetActive(state=="settings");p.modal.SetActive(false);
        p.title.text="일시정지";p.subtitle.text="은신처 · 게임이 멈춰 있습니다";p.returnButton.gameObject.SetActive(false);p.titleButton.interactable=true;
        p.FitMainPanel();
        if(state=="return")p.Confirm("타이틀로 돌아갈까요?","진행 상황을 저장하고 타이틀로 돌아갑니다.","타이틀로 돌아가기","취소",()=>{});
        if(state=="settings"){p.settings.Bind(p);p.settings.Show();}
    }
    static Texture2D Import(Texture2D shot,string name)
    {
        Directory.CreateDirectory(Art);string path=Art+"/"+name+".png";File.WriteAllBytes(path,shot.EncodeToPNG());AssetDatabase.ImportAsset(path);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=2048;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static RectTransform Rect(string name,Transform parent,Vector2 size)
    {
        var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.sizeDelta=size;return r;
    }
    static void StretchRoot(RectTransform r)
    {
        r.localScale=Vector3.one;r.localRotation=Quaternion.identity;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.pivot=new Vector2(.5f,.5f);r.offsetMin=r.offsetMax=Vector2.zero;r.anchoredPosition3D=Vector3.zero;
    }
    static void FreezeRoot(RectTransform r)
    {
        StretchRoot(r);
        // Unity omits driven overlay-root transforms from RecordPropertyModifications.
        // Keep the exhibition transform explicit while retaining the exact connected source.
        var source=PrefabUtility.GetCorrespondingObjectFromSource(r);var instance=PrefabUtility.GetNearestPrefabInstanceRoot(r.gameObject);
        string[] paths={"m_LocalScale.x","m_LocalScale.y","m_LocalScale.z","m_AnchorMin.x","m_AnchorMin.y","m_AnchorMax.x","m_AnchorMax.y","m_Pivot.x","m_Pivot.y","m_AnchoredPosition.x","m_AnchoredPosition.y","m_SizeDelta.x","m_SizeDelta.y"};
        string[] values={"1","1","1","0","0","1","1","0.5","0.5","0","0","0","0"};
        var mods=(PrefabUtility.GetPropertyModifications(instance)??Array.Empty<PropertyModification>()).Where(m=>m.target!=source||!paths.Contains(m.propertyPath)).ToList();
        for(int i=0;i<paths.Length;i++)mods.Add(new PropertyModification{target=source,propertyPath=paths[i],value=values[i]});
        PrefabUtility.SetPropertyModifications(instance,mods.ToArray());
    }
    static void Label(RectTransform board,Font font,string name,string text,float y,int size)
    {
        var existing=board.Find(name);var r=existing?(RectTransform)existing:Rect(name,board,new Vector2(1920,50));r.anchoredPosition=new Vector2(0,y);
        var label=r.GetComponent<Text>();if(!label)label=r.gameObject.AddComponent<Text>();label.font=font;label.fontSize=size;label.text=text;label.color=new Color(.88f,.74f,.48f);label.alignment=TextAnchor.MiddleLeft;label.raycastTarget=false;
    }
}
