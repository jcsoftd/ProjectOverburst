using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class AppearanceCustomizationVerifier
{
    static readonly List<string> checks=new List<string>();
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
    static string Scenes()=>JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>new{path=SceneManager.GetSceneAt(i).path,dirty=SceneManager.GetSceneAt(i).isDirty,roots=SceneManager.GetSceneAt(i).rootCount}));
    public static string Native()
    {
        AppearanceCustomizationBuilder.RequireIdle();checks.Clear();string before=Scenes();int previews=EditorSceneManager.previewSceneCount;
        string output=Path.GetFullPath(AppearanceCustomizationBuilder.Output+"/Native");Directory.CreateDirectory(output);
        try
        {
            var catalog=AssetDatabase.LoadAssetAtPath<CharacterAppearanceCatalog>(AppearanceCustomizationBuilder.CatalogPath);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AppearanceCustomizationBuilder.PrefabPath);
            var panel=prefab.GetComponent<AppearanceCustomizationPanel>();
            Check(catalog&&panel&&panel.catalog==catalog,"catalog and product prefab linked");catalog.Validate(catalog.defaultAppearance);
            Check(catalog.faces.Length==3&&catalog.hairStyles.Length==14&&catalog.hairColors.Length==9&&catalog.skinColors.Length==3&&catalog.eyeColors.Length==5&&catalog.bodyStyles.Length==3&&catalog.equipmentExamples.Length==12,"all confirmed appearance and equipment options present");
            Check(catalog.visualPrefab.GetComponentsInChildren<MonoBehaviour>(true).All(c=>c.GetType().Namespace=="MagicaCloth2")&&catalog.visualPrefab.GetComponentsInChildren<Collider>(true).Length==0&&catalog.visualPrefab.GetComponentsInChildren<Rigidbody>(true).Length==0,"preview prefab preserves only MagicaCloth visual physics without gameplay scripts or rigidbodies");
            foreach(var asset in new[]{prefab,catalog.visualPrefab,AssetDatabase.LoadAssetAtPath<GameObject>(AppearanceCustomizationBuilder.NpcPath)})
                Check(asset.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"no missing scripts: "+asset.name);
            var names=catalog.visualPrefab.GetComponentsInChildren<Renderer>(true).Select(r=>r.name).Distinct().ToArray();
            Check(names.Where(n=>n.StartsWith("Female_Face_",StringComparison.Ordinal)).All(n=>catalog.faces.Any(f=>f.rendererName==n)),"all female face assets are represented");
            Check(names.Where(n=>n.StartsWith("Hair_",StringComparison.Ordinal)).All(n=>catalog.hairStyles.Any(h=>h.rendererName==n)),"all hair assets are represented");
            Check(names.Where(n=>n.StartsWith("Fem_Armor_",StringComparison.Ordinal)).All(n=>catalog.equipmentExamples.Any(g=>g.rendererNames.Contains(n))),"all female outfit cloak skirt and headgear parts are represented");
            Check(catalog.visualPrefab.GetComponentsInChildren<MagicaCloth2.MagicaCloth>(true).Any(c=>c.name=="Physics_Breast")&&catalog.visualPrefab.GetComponentsInChildren<MagicaCloth2.MagicaCloth>(true).Any(c=>c.SerializeData.sourceRenderers.Any(r=>r&&r.name.StartsWith("Hair_",StringComparison.Ordinal))||c.SerializeData.rootBones.Any(b=>b&&b.name.StartsWith("Hair_",StringComparison.Ordinal))),"P09 upper body hair and outfit physics configurations are preserved");
            Check(panel.choices.Length==50&&panel.choices.All(x=>x.button&&x.selectedMark&&x.artwork),"authored choice references present");
            Check(!prefab.GetComponentsInChildren<TMP_Text>(true).Any(x=>x.text.Contains("누드")||x.text.Contains("DEV")||x.text.Contains("가슴")),"product labels use upper body style and unlabeled developer icon");
            foreach(var type in TypeCache.GetTypesDerivedFrom<AppearanceAuthoringExtension>().Where(t=>!t.IsAbstract))((AppearanceAuthoringExtension)Activator.CreateInstance(type)).CheckAssets(panel,checks);
            Check(panel.discardConfirmation&&!panel.surface.activeSelf,"prefab starts closed with explicit discard surface");
            Check(Vector3.Distance(prefab.transform.localScale,Vector3.one)<.0001f,"authored canvas root has nonzero unit scale");
            PersistenceChecks(catalog,output);
            Capture(prefab,new Vector2Int(1920,1080),output,true);
            Capture(prefab,new Vector2Int(1280,720),output,false);
            Capture(prefab,new Vector2Int(1600,900),output,false);
            Capture(prefab,new Vector2Int(2560,1080),output,false);
            Check(before==Scenes(),"user scene identities, roots and dirty flags preserved");
            Check(previews==EditorSceneManager.previewSceneCount,"owned preview scenes returned");
            File.WriteAllText(Path.Combine(output,"native-result.json"),JsonConvert.SerializeObject(new{status="PASS_NATIVE",checks,sceneBefore=before,sceneAfter=Scenes(),utc=DateTime.UtcNow},Formatting.Indented));return "PASS_NATIVE "+checks.Count;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"native-result.json"),JsonConvert.SerializeObject(new{status="FAIL_NATIVE",checks,error=e.ToString(),sceneBefore=before,sceneAfter=Scenes()},Formatting.Indented));throw;}
    }
    static void PersistenceChecks(CharacterAppearanceCatalog catalog,string output)
    {
        var fixture=new AccountSnapshot();fixture.appearance=null;
        string before=JsonConvert.SerializeObject(fixture);
        var legacy=JsonConvert.DeserializeObject<AccountSnapshot>(before);
        var draft=new AppearanceCustomizationSession(catalog,legacy.appearance);
        Check(draft.PreviewBody==AppearancePreviewBody.Equipment&&draft.EquipmentExampleId==catalog.equipmentExamples[0].id&&!draft.HasAppearanceChanges,"legacy appearance opens equipment 1 preview without account mutation");
        draft.SetDeveloperNude(true);draft.ShowEquipment(catalog.equipmentExamples[5].id);
        draft.Edit(x=>{x.faceId=catalog.faces[1].id;x.bodyShapeId=catalog.bodyStyles[2].id;});
        var candidate=draft.CandidateForSave();AccountAppearanceCommands.Apply(fixture,null,candidate);
        Check(fixture.appearance.Equals(candidate),"appearance command copies selected data");
        var expected=fixture.appearance.Copy();candidate.faceId=catalog.faces[0].id;
        Check(!fixture.appearance.Equals(candidate),"caller mutation cannot alter committed snapshot");
        fixture.appearance=null;Check(JsonConvert.SerializeObject(fixture)==before,"appearance mutation leaves equipment and all other account fields intact");
        fixture.appearance=expected;
        bool conflict=false;try{AccountAppearanceCommands.Apply(fixture,null,candidate);}catch(InvalidOperationException){conflict=true;}
        Check(conflict&&fixture.appearance.Equals(expected),"stale appearance conflicts preserve saved value");
        var persisted=ES3.Deserialize<AccountSnapshot>(ES3.Serialize(fixture));
        Check(persisted.appearance.Equals(expected),"Easy Save snapshot roundtrip retains appearance DTO");
        var reopened=new AppearanceCustomizationSession(catalog,persisted.appearance);
        Check(reopened.PreviewBody==AppearancePreviewBody.Equipment&&reopened.EquipmentExampleId==catalog.equipmentExamples[0].id,"equipment 1 is the default and animation developer states reset on reentry");
        var store=new EasySaveAccountStore(Path.Combine(output,"AppearanceStoreFixture"));store.Load();store.Save(fixture,Guid.NewGuid().ToString("N"));
        Check(new EasySaveAccountStore(Path.Combine(output,"AppearanceStoreFixture")).Load().appearance.Equals(expected),"two-generation account store persists appearance");
        var invalid=expected.Copy();invalid.hairStyleId="removed-test-hair";var resolved=catalog.ResolveForDisplay(invalid,out bool fallback);
        Check(fallback&&resolved.hairStyleId==catalog.defaultAppearance.hairStyleId&&invalid.hairStyleId=="removed-test-hair","missing option displays fallback while preserving stored IDs");
    }
    static void Capture(GameObject prefab,Vector2Int size,string output,bool exhaustive)
    {
        var scene=EditorSceneManager.NewPreviewScene();GameObject root=null,cameraRoot=null;RenderTexture rt=null;
        try
        {
            cameraRoot=new GameObject("Owned Appearance UI Capture Camera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraRoot,scene);
            var camera=cameraRoot.GetComponent<Camera>();camera.scene=scene;camera.enabled=false;camera.cameraType=CameraType.Game;camera.transform.position=new Vector3(0,0,-20);camera.orthographic=true;camera.orthographicSize=5;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.cullingMask=1<<5;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            rt=new RenderTexture(size.x,size.y,24);rt.Create();camera.targetTexture=rt;
            root=Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(root,scene);var panel=root.GetComponent<AppearanceCustomizationPanel>();
            panel.rootCanvas.renderMode=RenderMode.ScreenSpaceCamera;panel.rootCanvas.worldCamera=camera;panel.rootCanvas.planeDistance=10;
            Check(panel.OpenExhibition(),"exhibition session opens "+size);SceneManager.MoveGameObjectToScene(panel.preview.Model.transform.root.gameObject,scene);
            var title=panel.transform.Find("Fullscreen Surface/Title").GetComponent<TMP_Text>();Canvas.ForceUpdateCanvases();title.ForceMeshUpdate();
            Check(title.textInfo.characterCount>0&&!title.isTextOverflowing,"fullscreen title renders without clipping "+size);
            CheckStageBackground(panel,Check);
            CheckControlsLayout(panel,Check);
            var character=panel.preview.CaptureStill(size.x,size.y);try{Check(character.GetPixels32().Count(c=>c.a>0&&Mathf.Max(c.r,c.g,c.b)>15)>size.x*size.y*.01f,"character pixels rendered "+size);panel.characterTarget.texture=character;Render(camera,rt);Save(rt,Path.Combine(output,"appearance-"+size.x+"x"+size.y+".png"));}finally{panel.characterTarget.texture=panel.preview.Texture;Object.DestroyImmediate(character);}
            if(exhaustive)
            {
                var s=panel.Session;var c=panel.catalog;var visual=panel.preview.Model;
                foreach(var face in c.faces)s.Edit(v=>v.faceId=face.id);
                foreach(var hair in c.hairStyles)s.Edit(v=>v.hairStyleId=hair.id);
                foreach(var color in c.hairColors)s.Edit(v=>v.hairColorId=color.id);
                foreach(var color in c.skinColors)s.Edit(v=>v.skinColorId=color.id);
                foreach(var color in c.eyeColors)s.Edit(v=>v.eyeColorId=color.id);
                foreach(var style in c.bodyStyles){s.Edit(v=>v.bodyShapeId=style.id);Check(visual.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="bust_01_L"||t.name=="bust_01_R").All(t=>Vector3.Distance(t.localScale,style.scale)<.0001f),"body style applied "+style.displayName);}
                foreach(var example in c.equipmentExamples){s.ShowEquipment(example.id);var active=visual.GetComponentsInChildren<Renderer>(true).Where(r=>r.gameObject.activeInHierarchy).Select(r=>r.name).ToArray();Check(example.rendererNames.All(active.Contains)&&!active.Any(n=>n.StartsWith("Male",StringComparison.Ordinal)),"female equipment parts and coverage "+example.id);}
                s.ShowEquipment(c.equipmentExamples[1].id);var selectedArmor=visual.GetComponentsInChildren<Renderer>().Where(r=>!r.name.EndsWith("_Head",StringComparison.Ordinal)).Select(r=>r.name).OrderBy(n=>n).ToArray();
                s.ToggleHeadgear();Check(!s.HeadgearVisible&&!visual.GetComponentsInChildren<Renderer>().Any(r=>r.name.EndsWith("_Head",StringComparison.Ordinal)),"headgear switch hides helmet in preview");
                Check(selectedArmor.SequenceEqual(visual.GetComponentsInChildren<Renderer>().Select(r=>r.name).OrderBy(n=>n)),"helmet switch preserves selected outfit and appearance parts");s.ToggleHeadgear();
                s.SetDeveloperNude(true);Check(visual.GetComponentsInChildren<Renderer>().Any(r=>r.name=="Female_Body_Nakid_Chest")&&!visual.GetComponentsInChildren<Renderer>().Any(r=>r.name=="Female_Body_Chest"),"temporary body toggle replaces underwear");
                s.ShowUnderwear();s.ResetAppearance();
                foreach(var type in TypeCache.GetTypesDerivedFrom<AppearanceAuthoringExtension>().Where(t=>!t.IsAbstract))((AppearanceAuthoringExtension)Activator.CreateInstance(type)).CheckPreview(panel,checks);
                if(panel.ActiveExtension)
                {
                    var motion=panel.preview.CaptureStill(size.x,size.y,.4f);
                    try{panel.characterTarget.texture=motion;Render(camera,rt);Save(rt,Path.Combine(output,"appearance-animation-"+size.x+"x"+size.y+".png"));}
                    finally{panel.characterTarget.texture=panel.preview.Texture;Object.DestroyImmediate(motion);}
                }
                panel.Close();Check(panel.Session==null&&!panel.preview.Texture&&!panel.preview.Model&&!panel.surface.activeSelf,"session closes and releases visual graph model and RT");
                Check(panel.OpenExhibition()&&panel.Session.PreviewBody==AppearancePreviewBody.Equipment&&panel.Session.EquipmentExampleId==panel.catalog.equipmentExamples[0].id,"second open starts in equipment 1");panel.Close();
            }
            else panel.Close();
        }
        finally{if(root)Object.DestroyImmediate(root);if(rt){rt.Release();Object.DestroyImmediate(rt);}if(cameraRoot)Object.DestroyImmediate(cameraRoot);EditorSceneManager.ClosePreviewScene(scene);}
    }
    public static void CheckStageBackground(AppearanceCustomizationPanel panel,Action<bool,string> assert)
    {
        var surface=panel.surface.GetComponent<RectTransform>();
        var character=panel.characterTarget.transform;
        foreach(var name in new[]{"Left Feather","Right Feather"})
        {
            var shade=surface.Find(name);
            assert(shade&&shade.GetSiblingIndex()<character.GetSiblingIndex(),"background feather renders behind character: "+name);
        }
        var right=(RectTransform)surface.Find("Right Feather");var corners=new Vector3[4];right.GetWorldCorners(corners);
        var local=corners.Select(surface.InverseTransformPoint).ToArray();
        assert(local.Min(v=>v.x)>=surface.rect.xMin+surface.rect.width*.68f&&local.Max(v=>v.x)<=surface.rect.xMax+.1f,
            "mirrored right feather stays at right edge without covering portrait");
    }

    public static void CheckControlsLayout(AppearanceCustomizationPanel panel,Action<bool,string> assert)
    {
        Canvas.ForceUpdateCanvases();var surface=(RectTransform)panel.surface.transform;
        var controls=panel.GetComponentsInChildren<Button>(false).Where(b=>!b.GetComponentInParent<RectMask2D>()&&!b.GetComponentInParent<AppearanceDeveloperPreview>()).ToArray();
        foreach(var button in controls)
        {
            var fill=button.GetComponent<Image>();var border=button.transform.Find("Native Border").GetComponent<Image>();
            if(button==panel.apply)
            {
                var shape=button.transform.Find("Primary Shape");var primary=shape?shape.Find("Opaque Beveled Backing").GetComponent<AppearanceUiFrame>():null;
                assert(shape&&shape.GetComponent<Mask>()&&primary&&primary.fillColor.a==1,"opaque beveled primary control backing");
            }
            else assert(fill&&fill.color.a==1&&fill.sprite==null,"opaque control backing: "+button.name);
            assert(border.sprite&&AssetDatabase.GetAssetPath(border.sprite).StartsWith("Assets/ThirdParty/RPG and MMO UI 11/",StringComparison.Ordinal),"original UI11 button asset: "+button.name);
        }
        var rectangles=controls.Select(b=>{var corners=new Vector3[4];((RectTransform)b.transform).GetWorldCorners(corners);var local=corners.Select(surface.InverseTransformPoint).ToArray();return (button:b,rect:Rect.MinMaxRect(local.Min(v=>v.x),local.Min(v=>v.y),local.Max(v=>v.x),local.Max(v=>v.y)));}).ToArray();
        assert(rectangles.All(x=>x.rect.xMin>=surface.rect.xMin-.1f&&x.rect.xMax<=surface.rect.xMax+.1f&&x.rect.yMin>=surface.rect.yMin-.1f&&x.rect.yMax<=surface.rect.yMax+.1f),"active controls remain inside fullscreen viewport");
        var overlaps=new List<string>();
        for(int i=0;i<rectangles.Length;i++)for(int j=i+1;j<rectangles.Length;j++)if(rectangles[i].rect.Overlaps(rectangles[j].rect))overlaps.Add(rectangles[i].button.name+" / "+rectangles[j].button.name);
        assert(overlaps.Count==0,"all active control hit areas are disjoint: "+string.Join(", ",overlaps));
        var tabs=panel.GetComponentsInChildren<AppearanceOptionalTab>(true).Select(t=>(RectTransform)t.tabButton.transform).Append((RectTransform)panel.appearanceTab.transform).ToArray();
        assert(tabs.All(t=>Vector2.Distance(t.sizeDelta,tabs[0].sizeDelta)<.01f&&Mathf.Abs(t.anchoredPosition.y-tabs[0].anchoredPosition.y)<.01f),"appearance and optional tabs have identical size and vertical alignment");
        var modes=new[]{panel.faceFrame,panel.upperFrame,panel.fullFrame,panel.resetView}.Select(b=>(RectTransform)b.transform).ToArray();
        assert(modes.All(r=>Mathf.Abs(r.rect.width-modes[0].rect.width)<.01f&&Mathf.Abs(r.rect.height-modes[0].rect.height)<.01f&&Mathf.Abs(r.anchoredPosition.y-modes[0].anchoredPosition.y)<.01f),"four view controls use equal size and aligned baseline");
        var spacing=modes[1].anchoredPosition.x-modes[0].anchoredPosition.x;
        assert(Mathf.Abs((modes[2].anchoredPosition.x-modes[1].anchoredPosition.x)-spacing)<.01f&&Mathf.Abs((modes[3].anchoredPosition.x-modes[2].anchoredPosition.x)-spacing)<.01f,"view controls have equal horizontal spacing");
        var quiet=panel.GetComponentInChildren<AppearanceDeveloperPreview>(true).iconButton.GetComponent<RectTransform>();var quietCorners=new Vector3[4];quiet.GetWorldCorners(quietCorners);var quietRight=quietCorners.Max(v=>surface.InverseTransformPoint(v).x);
        assert(quietRight<rectangles.Single(x=>x.button==panel.back).rect.xMin,"quiet developer icon is separate from back button hit area");
    }

    static void Render(Camera camera,RenderTexture rt)
    {
        for(int pass=0;pass<3;pass++){Canvas.ForceUpdateCanvases();var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};if(RenderPipeline.SupportsRenderRequest(camera,request))RenderPipeline.SubmitRenderRequest(camera,request);else camera.Render();}
    }
    public static void Save(RenderTexture rt,string path)
    {
        var previous=RenderTexture.active;Texture2D pixels=null;
        try{RenderTexture.active=rt;pixels=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
        finally{RenderTexture.active=previous;if(pixels)Object.DestroyImmediate(pixels);}
    }
}
