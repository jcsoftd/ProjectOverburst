using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Appearance;
using Overburst.Appearance.AnimationPreview;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public sealed class AppearanceAnimationAuthoring:AppearanceAuthoringExtension
{
    public const string DataRoot=AppearanceCustomizationBuilder.Art+"/EditorPreview/AnimationPreview";
    public const string LibraryPath=DataRoot+"/AnimationLibrary.asset";
    public const string ThumbnailRoot=AppearanceCustomizationBuilder.Art+"/AnimationPreview";
    const string ConfigPath="Assets/Editor/Builders/UI/Appearance/AnimationPreview/AppearanceAnimationAuthoringSettings.asset";
    const string P09="Assets/ThirdParty/02_인간캐릭터/P09_Modular_Humanoid";
    AppearanceAnimationLibrary library;
    static AppearanceAnimationAuthoringSettings Config()
    {
        var config=AssetDatabase.LoadAssetAtPath<AppearanceAnimationAuthoringSettings>(ConfigPath);
        if(!config){config=ScriptableObject.CreateInstance<AppearanceAnimationAuthoringSettings>();AssetDatabase.CreateAsset(config,ConfigPath);}return config;
    }
    static void Folder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        var parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }
    public override void Prepare(CharacterAppearanceCatalog catalog)
    {
        if(!Config().includeTab)return;
        Folder(DataRoot);Folder(ThumbnailRoot);
        library=AssetDatabase.LoadAssetAtPath<AppearanceAnimationLibrary>(LibraryPath);
        if(!library){library=ScriptableObject.CreateInstance<AppearanceAnimationLibrary>();AssetDatabase.CreateAsset(library,LibraryPath);}
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
        var controller=source.transform.Find("VisualRoot/ModelInstance_P09").GetComponent<Animator>().runtimeAnimatorController;
        var game=controller.animationClips.Where(c=>c&&!c.name.StartsWith("__preview__",StringComparison.Ordinal)).Distinct();
        var kawaii=AssetDatabase.FindAssets("t:Model",new[]{"Assets/ThirdParty/03_애니메이션/Kawaii_Animations_100"})
            .SelectMany(g=>AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g))).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal)&&c.isHumanMotion).Distinct();
        var expressions=AssetDatabase.FindAssets("t:AnimationClip",new[]{P09+"/Scenes/DemoScene_Data/Animation/Facial_Emotion"})
            .Select(g=>AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g))).Where(c=>c);
        library.animations=game.Select(c=>Option(c,"Game")).Concat(kawaii.Select(c=>Option(c,"Kawaii"))).Concat(expressions.Select(c=>Option(c,"Expression"))).OrderBy(x=>x.category).ThenBy(x=>x.displayName,StringComparer.Ordinal).ToArray();
        if(library.animations.Count(x=>x.category=="Kawaii")!=416||library.animations.Count(x=>x.category=="Game")!=87||library.animations.Count(x=>x.category=="Expression")!=10)
            throw new InvalidOperationException("애니메이션 원본 수가 조사와 다릅니다.");
        CaptureThumbnails(catalog);EditorUtility.SetDirty(library);AssetDatabase.SaveAssetIfDirty(library);
    }
    static AppearanceAnimationOption Option(AnimationClip clip,string category)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip,out string guid,out long local);
        return new AppearanceAnimationOption{id="anim."+guid+"."+local,displayName=clip.name,category=category,clip=clip,assetGuid=guid,localFileId=local,assetPath=AssetDatabase.GetAssetPath(clip)};
    }
    void CaptureThumbnails(CharacterAppearanceCatalog catalog)
    {
        const int size=128,columns=32;string atlasPath=ThumbnailRoot+"/MotionPoseAtlas.png";
        var known=AssetDatabase.LoadAllAssetsAtPath(atlasPath).OfType<Sprite>().ToDictionary(x=>x.name);
        if(library.thumbnailCaptureVersion==8&&known.Count==library.animations.Length&&library.animations.All(x=>known.ContainsKey(x.id)))
        {foreach(var option in library.animations)option.thumbnail=known[option.id];return;}
        var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;Texture2D atlas=null;
        try
        {
            root=new GameObject("Owned Motion Thumbnail Capture",typeof(RectTransform),typeof(RawImage));SceneManager.MoveGameObjectToScene(root,scene);
            var session=new AppearanceCustomizationSession(catalog,null);session.SetDeveloperNude(true);
            var preview=root.AddComponent<AppearanceCharacterPreview>();preview.Open(catalog,session,root.GetComponent<RawImage>());
            SceneManager.MoveGameObjectToScene(preview.Model.transform.root.gameObject,scene);
            int rows=Mathf.CeilToInt(library.animations.Length/(float)columns);atlas=new Texture2D(columns*size,rows*size,TextureFormat.RGBA32,false);
            var sprites=new List<SpriteMetaData>();
            for(int index=0;index<library.animations.Length;index++)
            {
                var option=library.animations[index];preview.SetFraming(option.category=="Expression"?AppearanceFraming.Face:AppearanceFraming.FullBody);preview.Play(option.clip,false);
                var shot=preview.CaptureStill(size,size,Mathf.Min(option.clip.length*.4f,.8f));int x=index%columns*size,y=index/columns*size;
                try{atlas.SetPixels32(x,y,size,size,shot.GetPixels32());}finally{Object.DestroyImmediate(shot);}
                sprites.Add(new SpriteMetaData{name=option.id,rect=new Rect(x,y,size,size),alignment=(int)SpriteAlignment.Center,pivot=new Vector2(.5f,.5f)});
                if((index+1)%25==0)File.WriteAllText(Path.GetFullPath(AppearanceCustomizationBuilder.Output+"/animation-thumbnail-progress.json"),JsonConvert.SerializeObject(new{completed=index+1,total=library.animations.Length,utc=DateTime.UtcNow}));
            }
            atlas.Apply();File.WriteAllBytes(atlasPath,atlas.EncodeToPNG());preview.Close();
            AssetDatabase.ImportAsset(atlasPath,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(atlasPath);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Multiple;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=4096;
#pragma warning disable 0618
            importer.spritesheet=sprites.ToArray();
#pragma warning restore 0618
            importer.SaveAndReimport();known=AssetDatabase.LoadAllAssetsAtPath(atlasPath).OfType<Sprite>().ToDictionary(x=>x.name);
            foreach(var option in library.animations)option.thumbnail=known[option.id];
            library.thumbnailCaptureVersion=8;
        }
        finally{if(atlas)Object.DestroyImmediate(atlas);if(root)Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
    }
    public override void CreateUI(Transform surface,AppearanceCustomizationPanel panel)
    {if(library)AppearanceCustomizationBuilder.CreateAnimationTab(surface,panel,library);}
    public override void CheckAssets(AppearanceCustomizationPanel panel,List<string> checks)
    {
        var editorPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(AppearanceCustomizationPanel.EditorPreviewPath);
        var tab=editorPrefab?editorPrefab.GetComponentInChildren<AppearanceAnimationPreviewTab>(true):null;
        if(panel.GetComponentInChildren<AppearanceDeveloperPreview>(true)||panel.GetComponentInChildren<AppearanceAnimationPreviewTab>(true))throw new InvalidOperationException("제품 프리팹에 Editor 전용 모듈 잔존");
        if(!Config().includeTab){if(tab)throw new InvalidOperationException("비활성 탭이 남아 있습니다.");checks.Add("optional animation module absent");return;}
        if(tab&&tab.tabButton.gameObject.activeSelf)throw new InvalidOperationException("애니메이션 탭 기본 노출");
        if(!tab||!tab.library||tab.library.animations.Length!=513||!tab.library.animations.All(x=>x.clip&&x.thumbnail))throw new InvalidOperationException("애니메이션/썸네일 연결 누락");
        if(tab.library.animations.Select(x=>x.id).Distinct().Count()!=513)throw new InvalidOperationException("애니메이션 ID 중복");
        if(!tab.list.GetComponentInParent<ScrollRect>(true).viewport.GetComponent<RectMask2D>())throw new InvalidOperationException("스크롤 뷰 누락");
        var detached=Object.Instantiate(panel.gameObject);try
        {
            foreach(var optional in detached.GetComponentsInChildren<AppearanceAnimationPreviewTab>(true))Object.DestroyImmediate(optional.gameObject);
            if(detached.GetComponentsInChildren<Transform>(true).Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0))throw new InvalidOperationException("모듈 제거 후 Missing Script");
            if(!detached.GetComponent<AppearanceCustomizationPanel>().catalog)throw new InvalidOperationException("외모 데이터가 모듈에 종속됨");
        }finally{Object.DestroyImmediate(detached);}
        checks.Add("513 animations and actual pose thumbnails in removable module");checks.Add("removing module keeps core prefab references valid");
    }
    public override void CheckPreview(AppearanceCustomizationPanel panel,List<string> checks)
    {
        var tab=panel.GetComponentInChildren<AppearanceAnimationPreviewTab>(true);if(!tab)return;
        var blocked=tab.library.animations.First(x=>x.category=="Kawaii");
        if(tab.IsAvailable||tab.tabButton.gameObject.activeSelf||tab.page.activeSelf)throw new InvalidOperationException("일반 외모 모드에 애니메이션 탭 노출");
        panel.ShowExtension(tab);tab.Select(blocked);panel.preview.Play(blocked.clip);
        if(panel.ActiveExtension||tab.Selected!=null||panel.preview.ActiveClip!=panel.catalog.idleClip)throw new InvalidOperationException("누드 비활성 재생 우회");
        checks.Add("animation tab and direct selection/playback unavailable outside developer nude preview");
        panel.Session.SetDeveloperNude(true);panel.ShowExtension(tab);tab.Select(blocked);
        if(!tab.IsAvailable||!tab.tabButton.gameObject.activeSelf||panel.ActiveExtension!=tab||panel.preview.ActiveClip!=blocked.clip)throw new InvalidOperationException("누드 활성 모션 사용 불가");
        panel.Session.SetDeveloperNude(false);
        if(tab.IsAvailable||tab.tabButton.gameObject.activeSelf||tab.page.activeSelf||panel.ActiveExtension||!panel.appearanceOptions.activeSelf||tab.Selected!=null||panel.preview.ActiveClip!=panel.catalog.idleClip)throw new InvalidOperationException("누드 해제 시 모션/탭 반환 실패");
        checks.Add("disabling nude stops motion, clears selection and restores appearance page immediately");
        panel.Session.SetDeveloperNude(true);
        string path=Path.GetFullPath(AppearanceCustomizationBuilder.Output+"/Native/ExclusionFixtures/"+Guid.NewGuid().ToString("N")+".json");
        tab.SetExclusionStore(new AppearanceAnimationExclusions(path));panel.ShowExtension(tab);tab.kawaiiCategory.onClick.Invoke();Canvas.ForceUpdateCanvases();
        if(tab.list.GetComponentsInChildren<Button>().Length!=416||tab.list.rect.height<=tab.list.GetComponentInParent<ScrollRect>().viewport.rect.height)throw new InvalidOperationException("카와이 목록 수/스크롤 높이 오류");
        var option=tab.library.animations.First(x=>x.category=="Kawaii");string assetBefore=AssetDatabase.AssetPathToGUID(option.assetPath);
        tab.Select(option);tab.ExcludeOrRestore();
        if(tab.list.GetComponentsInChildren<Button>().Length!=415||!new AppearanceAnimationExclusions(path).Contains(option.id))throw new InvalidOperationException("목록 제외/영속화 실패");
        var entry=new AppearanceAnimationExclusions(path).Document.excluded.Single();
        if(entry.assetGuid!=option.assetGuid||entry.assetPath!=option.assetPath||entry.localFileId!=option.localFileId||assetBefore!=AssetDatabase.AssetPathToGUID(option.assetPath)||!option.clip)throw new InvalidOperationException("제외 기록/원본 보존 실패");
        tab.excludedList.onClick.Invoke();tab.Select(option);tab.ExcludeOrRestore();
        if(new AppearanceAnimationExclusions(path).Contains(option.id))throw new InvalidOperationException("복원 실패");
        tab.kawaiiCategory.onClick.Invoke();tab.search.text="Dance";if(tab.list.GetComponentsInChildren<Button>().Length>=416)throw new InvalidOperationException("검색 실패");tab.search.text="";
        var expressionHashes=new HashSet<string>();
        foreach(var expression in tab.library.animations.Where(x=>x.category=="Expression"))
        {
            tab.Select(expression);var shot=panel.preview.CaptureStill(128,128,0);
            try
            {
                if(shot.GetPixels32().Count(p=>p.a>0)<100)throw new InvalidOperationException("표정 얼굴이 카메라 밖입니다: "+expression.displayName);
                using(var sha=System.Security.Cryptography.SHA256.Create())expressionHashes.Add(BitConverter.ToString(sha.ComputeHash(shot.EncodeToPNG())));
                var animator=panel.preview.Model.GetComponent<Animator>();
                if(animator.GetBoneTransform(HumanBodyBones.Head).position.y-panel.preview.Model.transform.root.position.y<1)throw new InvalidOperationException("표정이 기본 몸 자세를 덮어썼습니다.");
            }
            finally{Object.DestroyImmediate(shot);}
        }
        if(expressionHashes.Count!=10)throw new InvalidOperationException("표정10종의 실제 촬영 이미지가 구분되지 않습니다.");checks.Add("10 expression renders remain distinct and preserve humanoid presentation pose");
        tab.Select(option);panel.preview.Seek(.4f);checks.Add("animation search, select, exclude, reload and restore preserve source GUID and clip");
    }
    [MenuItem("Overburst/UI/외모 커스터마이징/애니메이션 탭 전체 제거")]
    public static void RemoveFromProduct()
    {
        AppearanceCustomizationBuilder.RequireIdle();var config=Config();config.includeTab=false;EditorUtility.SetDirty(config);AssetDatabase.SaveAssetIfDirty(config);
        var root=PrefabUtility.LoadPrefabContents(AppearanceCustomizationPanel.EditorPreviewPath);
        try{foreach(var module in root.GetComponentsInChildren<AppearanceAnimationPreviewTab>(true))Object.DestroyImmediate(module.gameObject);PrefabUtility.SaveAsPrefabAsset(root,AppearanceCustomizationPanel.EditorPreviewPath);}
        finally{PrefabUtility.UnloadPrefabContents(root);}
        foreach(var path in new[]{DataRoot,ThumbnailRoot})if(AssetDatabase.IsValidFolder(path))AssetDatabase.DeleteAsset(path);
        Debug.Log("애니메이션 탭과 전용 카탈로그/썸네일을 제거했습니다. 외모·NPC·제외 기록과 원본 애니메이션은 보존됩니다.");
    }
    [MenuItem("Overburst/UI/외모 커스터마이징/애니메이션 제외 목록 내보내기")]
    public static void ExportExclusions()
    {
        var exclusions=new AppearanceAnimationExclusions();string output=Path.GetFullPath(AppearanceCustomizationBuilder.Output+"/AnimationPreview/Exclusions");Directory.CreateDirectory(output);
        string path=Path.Combine(output,"excluded-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".json");File.WriteAllText(path,exclusions.Json());Debug.Log(path);
    }
}

