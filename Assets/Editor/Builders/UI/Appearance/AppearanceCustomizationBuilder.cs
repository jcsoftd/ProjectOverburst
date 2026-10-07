using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Appearance;
using Overburst.Persistence;
using P09.Modular.Humanoid.Data;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using Object=UnityEngine.Object;

public static partial class AppearanceCustomizationBuilder
{
    public const string Root="Assets/ProjectOverburst/Resources/UI/Appearance";
    public const string Art="Assets/ProjectOverburst/05_Art/UI/Appearance";
    public const string PrefabPath=Root+"/PF_OverburstAppearance_Rpg11.prefab";
    public const string CatalogPath=Root+"/AppearanceCatalog.asset";
    public const string ModelPath=Root+"/PF_AppearancePreviewModel.prefab";
    public const string NpcPath="Assets/ProjectOverburst/03_Features/World/Prefabs/PF_HideoutAppearanceStylist.prefab";
    public const string Output="../개인파일/코덱스산출/UI/20261006_AppearanceCustomizationGoal";
    const string P09="Assets/ThirdParty/02_인간캐릭터/P09_Modular_Humanoid";
    const string Kit="Assets/ThirdParty/RPG and MMO UI 11/Textures/";
    static TMP_FontAsset headingFont,bodyFont;
    static AppearanceAuthoringExtension[] extensions=Array.Empty<AppearanceAuthoringExtension>();
    static readonly Color Gold=new Color(.82f,.68f,.40f),Ivory=new Color(.90f,.85f,.73f),Muted=new Color(.48f,.46f,.42f);

    [MenuItem("Overburst/UI/외모 커스터마이징/제품 자산 만들기")]
    public static void Build()
    {
        RequireIdle();
        Directory.CreateDirectory(Path.GetFullPath(Output));
        var scenes=SceneState();
        var resultPath=Path.GetFullPath(Output+"/build-result.json");
        try
        {
            Folder(Root);Folder(Art);Folder(Art+"/Thumbnails");Folder(Root+"/Materials");Folder(Root+"/Fonts");
            ImportBackground();
            headingFont=FontAsset("AppearanceHeading","Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-VariableFont_wght.ttf");
            bodyFont=FontAsset("AppearanceBody","Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");
            var catalog=BuildCatalog();
            ApplyColorIcons(catalog);
            BuildPreviewQuality(catalog);
            extensions=TypeCache.GetTypesDerivedFrom<AppearanceAuthoringExtension>().Where(t=>!t.IsAbstract).Select(t=>(AppearanceAuthoringExtension)Activator.CreateInstance(t)).ToArray();
            foreach(var extension in extensions)extension.Prepare(catalog);
            CaptureThumbnails(catalog);
            BuildPanel(catalog);
            BuildNpc(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            if(scenes!=SceneState())throw new InvalidOperationException("열린 사용자 씬 상태가 변경됐습니다.");
            File.WriteAllText(resultPath,JsonConvert.SerializeObject(new{status="PASS_NATIVE_BUILD",catalog=CatalogPath,prefab=PrefabPath,npc=NpcPath,
                faces=catalog.faces.Length,hair=catalog.hairStyles.Length,colors=catalog.hairColors.Length,skin=catalog.skinColors.Length,eyes=catalog.eyeColors.Length,
                gear=catalog.equipmentExamples.Length,optionalTabs=extensions.Select(x=>x.GetType().Name).ToArray(),scenesBefore=scenes,scenesAfter=SceneState(),utc=DateTime.UtcNow},Formatting.Indented));
        }
        catch(Exception e){File.WriteAllText(resultPath,JsonConvert.SerializeObject(new{status="FAIL_NATIVE_BUILD",error=e.ToString(),scenesBefore=scenes,scenesAfter=SceneState()},Formatting.Indented));throw;}
    }
    public static void RequireIdle()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 Editor에서 실행해 주세요.");
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY"))||IsolatedSavePlayGuard.RequiresAccountChoice)
            throw new InvalidOperationException("다른 격리 검증이 준비되어 있습니다.");
    }
    static string SceneState()=>JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>new{path=SceneManager.GetSceneAt(i).path,dirty=SceneManager.GetSceneAt(i).isDirty,roots=SceneManager.GetSceneAt(i).rootCount}));
    static void Folder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        var parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }
    static void ImportBackground()
    {
        string source=Path.GetFullPath(Output+"/GeneratedAssets/appearance-background-v2.png");
        string destination=Art+"/AppearanceBackground.png";
        if(!File.Exists(source))throw new FileNotFoundException("생성한 배경이 없습니다.",source);
        File.Copy(source,destination,true);ImportSprite(destination,4096);
    }
    static void ImportSprite(string path,int size)
    {
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
        importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.maxTextureSize=size;importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport();
    }
    static TMP_FontAsset FontAsset(string name,string source)
    {
        string path=Root+"/Fonts/"+name+".asset";var asset=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if(asset)return asset;
        var font=AssetDatabase.LoadAssetAtPath<Font>(source);if(!font)throw new FileNotFoundException(source);
        asset=TMP_FontAsset.CreateFontAsset(font,72,8,GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic,true);
        asset.name=name;AssetDatabase.CreateAsset(asset,path);
        foreach(var texture in asset.atlasTextures)if(texture&&!AssetDatabase.Contains(texture))AssetDatabase.AddObjectToAsset(texture,asset);
        if(asset.material&&!AssetDatabase.Contains(asset.material))AssetDatabase.AddObjectToAsset(asset.material,asset);
        asset.TryAddCharacters("외모 변경은신처성별여성남성준비중얼굴머리없음피부색눈색상상체스타일장비착용예시속옷미리보기전용돌아가기기본외모적용전신상체회전확대애니메이션게임모션표정검색선택제외목록복원시점재생일시정지반복정지속도취소계속편집변경내용을버릴까요저장되지않습니다치장사F1234567890SM L↻◈‹›×. /:0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_-()");
        EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);return asset;
    }
    static T[] Data<T>(string folder) where T:ScriptableObject=>AssetDatabase.FindAssets("t:"+typeof(T).Name,new[]{P09+"/Scenes/DemoScene_Data/ScriptableObject/"+folder})
        .Select(g=>AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g))).Where(x=>x).ToArray();
    static Material PaletteMaterial(Material source)
    {
        if(!source)throw new InvalidOperationException("팔레트 원본 재질이 없습니다.");
        string path=Root+"/Materials/"+source.name+"_Appearance.mat";
        var existing=AssetDatabase.LoadAssetAtPath<Material>(path);if(existing)return existing;
        var material=new Material(source){name=source.name+"_Appearance"};
        if(material.HasProperty("_MonochromeLighting"))material.SetFloat("_MonochromeLighting",.8f);
        if(material.HasProperty("_LightMinLimit"))material.SetFloat("_LightMinLimit",.15f);
        if(material.HasProperty("_AsUnlit"))material.SetFloat("_AsUnlit",0);
        AssetDatabase.CreateAsset(material,path);return material;
    }
    [MenuItem("Overburst/UI/외모 커스터마이징/모델 물리 연결 다시 만들기")]
    public static void RebuildPreviewModel()
    {
        RequireIdle();var scenes=SceneState();var catalog=BuildCatalog();ApplyColorIcons(catalog);AssetDatabase.SaveAssetIfDirty(catalog);
        if(scenes!=SceneState())throw new InvalidOperationException("열린 사용자 씬 상태가 변경됐습니다.");
        File.WriteAllText(Path.GetFullPath(Output+"/physics-model-build.json"),JsonConvert.SerializeObject(new{status="PASS_NATIVE_PHYSICS_MODEL",cloth=catalog.visualPrefab.GetComponentsInChildren<MagicaCloth2.MagicaCloth>(true).Length,scenesBefore=scenes,scenesAfter=SceneState(),utc=DateTime.UtcNow},Formatting.Indented));
    }
    static CharacterAppearanceCatalog BuildCatalog()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<CharacterAppearanceCatalog>(CatalogPath);
        if(!catalog){catalog=ScriptableObject.CreateInstance<CharacterAppearanceCatalog>();AssetDatabase.CreateAsset(catalog,CatalogPath);}
        var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
        var source=player.transform.Find("VisualRoot/ModelInstance_P09");if(!source)throw new InvalidOperationException("P09 모델이 없습니다.");
        var holder=new GameObject("Inactive Appearance Authoring");holder.SetActive(false);
        try
        {
            var model=Object.Instantiate(source.gameObject,holder.transform,false);model.name="PF_AppearancePreviewModel";
            foreach(var item in model.GetComponentsInChildren<MonoBehaviour>(true))if(item.GetType().Namespace!="MagicaCloth2")Object.DestroyImmediate(item);
            foreach(var cloth in model.GetComponentsInChildren<MagicaCloth2.MagicaCloth>(true)){cloth.SerializeData.updateMode=MagicaCloth2.ClothUpdateMode.Unscaled;cloth.SerializeData.cullingSettings.cameraCullingMode=MagicaCloth2.CullingSettings.CameraCullingMode.Off;cloth.SerializeData.cullingSettings.distanceCullingLength.use=false;}
            foreach(var item in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(item);
            foreach(var item in model.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(item);
            foreach(var item in model.GetComponentsInChildren<AudioSource>(true))Object.DestroyImmediate(item);
            foreach(var item in model.GetComponentsInChildren<AudioListener>(true))Object.DestroyImmediate(item);
            var animator=model.GetComponent<Animator>();
            var controller=source.GetComponent<Animator>().runtimeAnimatorController;
            if(!animator||!controller)throw new InvalidOperationException("플레이어 Avatar/controller가 없습니다.");
            animator.runtimeAnimatorController=null;animator.applyRootMotion=false;animator.fireEvents=false;
            model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.identity;model.transform.localScale=Vector3.one;
            catalog.visualPrefab=PrefabUtility.SaveAsPrefabAsset(model,ModelPath);
            catalog.faces=Enumerable.Range(1,3).Select(i=>new AppearanceMeshOption{id="p09.face.female."+i.ToString("00"),displayName="얼굴 "+i,rendererName="Female_Face_"+i.ToString("00")}).ToArray();
            var hair=Data<RendererEditPartData>("HairStyle").Where(x=>x.MeshName.StartsWith("Hair_",StringComparison.Ordinal)).OrderBy(x=>x.MeshName).ToArray();
            catalog.hairStyles=hair.Select(x=>new AppearanceMeshOption{id="p09.hair."+int.Parse(x.MeshName.Substring(5)).ToString("00"),displayName="머리 "+int.Parse(x.MeshName.Substring(5)),rendererName=x.MeshName})
                .Append(new AppearanceMeshOption{id="p09.hair.none",displayName="없음",rendererName="SkinHead_Female"}).ToArray();
            catalog.hairColors=Data<HairColorEditPartData>("HairColor").OrderBy(x=>x.ContentId).Select((x,i)=>new AppearanceColorOption{id="p09.haircolor."+(i+1).ToString("00"),displayName=x.DisplayName,
                material=PaletteMaterial(x.Material),hair10Material=PaletteMaterial(x.GetMaterial(10)),icon=x.Icon,swatch=Color.white}).ToArray();
            catalog.skinColors=Data<ColorEditPartData>("SkinColor").Where(x=>x.Material&&x.Material.name.Contains("Female")).OrderBy(x=>x.ContentId).Select((x,i)=>new AppearanceColorOption{id="p09.skin.female."+(i+1).ToString("00"),displayName=x.DisplayName,material=PaletteMaterial(x.Material),icon=x.Icon,swatch=Color.white}).ToArray();
            catalog.eyeColors=Data<ColorEditPartData>("EyeColor").OrderBy(x=>x.ContentId).Select((x,i)=>new AppearanceColorOption{id="p09.eye."+(i+1).ToString("00"),displayName=x.DisplayName,material=PaletteMaterial(x.Material),icon=x.Icon,swatch=Color.white}).ToArray();
            catalog.bodyStyles=new[]{new AppearanceBodyStyle{id="p09.bodyshape.s",displayName="S",scale=new Vector3(.85f,.8f,.85f)},new AppearanceBodyStyle{id="p09.bodyshape.m",displayName="M",scale=Vector3.one},new AppearanceBodyStyle{id="p09.bodyshape.l",displayName="L",scale=new Vector3(1.2f,1.2f,1.2f)}};
            var names=model.GetComponentsInChildren<Renderer>(true).Select(x=>x.name).Distinct().ToArray();
            catalog.equipmentExamples=Enumerable.Range(1,12).Select(i=>new AppearanceEquipmentExample{id="p09.armor.female."+i.ToString("000"),displayName="장비 "+i,
                rendererNames=names.Where(n=>n.StartsWith("Fem_Armor_"+i.ToString("000")+"_",StringComparison.Ordinal)).ToArray(),baseBodyNames=Array.Empty<string>()}).ToArray();
            foreach(var gear in catalog.equipmentExamples)if(gear.rendererNames.Length==0)throw new InvalidOperationException("장비 예시 모델이 없습니다: "+gear.id);
            var clips=controller.animationClips.Where(c=>c&&!c.name.StartsWith("__preview__",StringComparison.Ordinal)).Distinct().ToArray();
            catalog.idleClip=clips.FirstOrDefault(c=>c.name.IndexOf("Idle",StringComparison.OrdinalIgnoreCase)>=0&&c.isHumanMotion)
                ??throw new InvalidOperationException("인간형 Idle이 없습니다.");
            catalog.idleClip=PresentationIdle(catalog.idleClip);
            catalog.defaultAppearance=new CharacterAppearanceSnapshot{hairColorId="p09.haircolor.09",eyeColorId="p09.eye.03"};
            catalog.Validate(catalog.defaultAppearance);
            if(catalog.hairStyles.Length!=14||catalog.hairColors.Length!=9||catalog.skinColors.Length!=3||catalog.eyeColors.Length!=5)
                throw new InvalidOperationException("옵션 수가 원본 조사 결과와 다릅니다.");
            EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssetIfDirty(catalog);return catalog;
        }
        finally{Object.DestroyImmediate(holder);}
    }
    static void CaptureThumbnails(CharacterAppearanceCatalog catalog)
    {
        var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;
        try
        {
            root=new GameObject("Owned Appearance Thumbnail Capture");SceneManager.MoveGameObjectToScene(root,scene);
            var image=new GameObject("Image",typeof(RectTransform),typeof(UnityEngine.UI.RawImage));image.transform.SetParent(root.transform,false);
            var preview=image.AddComponent<AppearanceCharacterPreview>();
            var session=new AppearanceCustomizationSession(catalog,null);
            preview.Open(catalog,session,image.GetComponent<UnityEngine.UI.RawImage>());
            // Move the owned render rig into the preview scene, keeping dirty user scenes untouched.
            SceneManager.MoveGameObjectToScene(preview.Model.transform.root.gameObject,scene);
            Sprite Shot(string id,AppearanceFraming frame)
            {
                string path=Art+"/Thumbnails/"+id+".png";preview.SetFraming(frame);
                var texture=preview.CaptureStill(384,384);
                try{File.WriteAllBytes(path,texture.EncodeToPNG());}finally{Object.DestroyImmediate(texture);}
                ImportSprite(path,512);return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            foreach(var face in catalog.faces){session.Edit(v=>v.faceId=face.id);face.thumbnail=Shot(face.id,AppearanceFraming.Face);}
            session.ResetAppearance();
            foreach(var hair in catalog.hairStyles){session.Edit(v=>v.hairStyleId=hair.id);hair.thumbnail=Shot(hair.id,AppearanceFraming.Face);}
            session.ResetAppearance();session.ShowUnderwear();Shot("underwear",AppearanceFraming.UpperBody);
            for(int i=0;i<catalog.equipmentExamples.Length;i++)catalog.equipmentExamples[i].thumbnail=OriginalEquipmentIcon(i+1);
            preview.Close();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssetIfDirty(catalog);
        }
        finally{if(root)Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
    }
}
