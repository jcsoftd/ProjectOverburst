using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Object=UnityEngine.Object;

public static class OverburstUIRefinementValidation
{
    public static string Output=>Path.GetFullPath("../개인파일/코덱스산출/UI/20260923_UIRefinement");
    public static string Audit()
    {
        var w=Object.FindFirstObjectByType<OverburstUIWorkshop>();w.ShowStash();Canvas.ForceUpdateCanvases();
        var result=new List<string>();Action<bool,string> check=(ok,name)=>result.Add((ok?"PASS ":"FAIL ")+name);
        var roots=new[]{w.Inventory.gameObject,w.Equipment.gameObject,w.Stash.gameObject,w.transform.Find("PF_OverburstHUD_Rpg11").gameObject};
        var camera=GameObject.Find("UI Preview Camera").GetComponent<Camera>();
        foreach(var root in roots){
            var slots=root.GetComponentsInChildren<OverburstUIItemSlotView>(true).Where(s=>s.gameObject.activeSelf).ToArray();
            check(slots.Length==(root.name.Contains("Inventory")?42:root.name.Contains("Equipment")?12:root.name.Contains("Stash")?63:10),root.name+" slot count");
            foreach(var s in slots){var r=(RectTransform)s.transform;var corners=new Vector3[4];r.GetWorldCorners(corners);var size=RectTransformUtility.WorldToScreenPoint(camera,corners[2])-RectTransformUtility.WorldToScreenPoint(camera,corners[0]);
                check(Mathf.Abs(size.x-84)<.1f&&Mathf.Abs(size.y-84)<.1f,root.name+" / "+s.name+" visible 84x84");
                
            }
        }
        foreach(var root in roots){var asset=AssetDatabase.LoadAssetAtPath<GameObject>(OverburstUIWorkshopBuilder.Root+"/"+root.name+".prefab");var slots=asset.GetComponentsInChildren<OverburstUIItemSlotView>(true);check(slots.All(s=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(s.gameObject)==OverburstUIWorkshopBuilder.SharedSlotPath),root.name+" all slots share one source prefab");}
        foreach(var window in new[]{w.Inventory,w.Stash}){
            var scroll=window.GetComponentInChildren<ScrollRect>(true);scroll.verticalNormalizedPosition=1;Canvas.ForceUpdateCanvases();float top=scroll.content.anchoredPosition.y;scroll.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();check(scroll.content.anchoredPosition.y>top+100,window.name+" last row scroll");scroll.verticalNormalizedPosition=1;
        }
        w.ShowComparison();Canvas.ForceUpdateCanvases();
        var texts=w.transform.GetComponentsInChildren<Text>(false).Where(t=>!string.IsNullOrEmpty(t.text)&&t.transform.IsChildOf(w.Equipment.transform)).ToArray();
        check(texts.All(t=>t.preferredHeight<=t.rectTransform.rect.height+2 || t.verticalOverflow==VerticalWrapMode.Overflow),"equipment text fits height");
        var gallery=GameObject.Find(OverburstUIWorkshopBuilder.GalleryName);check(gallery.transform.Cast<Transform>().Sum(t=>t.childCount)==14,"14 organized boards / five groups");
        var instances=gallery.GetComponentsInChildren<Transform>(true).Where(t=>PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)).ToArray();check(instances.All(t=>PrefabUtility.GetPrefabInstanceStatus(t.gameObject)==PrefabInstanceStatus.Connected),"gallery prefab connections");
        check(!Object.FindObjectsByType<SlotGradeEffect>(FindObjectsSortMode.None).Any(e=>e.gameObject.layer==31),"gallery does not consume live grade effect budget");
        File.WriteAllLines(Path.Combine(Output,"geometry-validation.txt"),result);return string.Join("\n",result.Where(s=>s.StartsWith("FAIL")))+"\nChecks="+result.Count+" failures="+result.Count(s=>s.StartsWith("FAIL"));
    }
    public static string AuditDetails()
    {
        if(!Application.isPlaying)throw new Exception("Play required");
        var checks=new List<string>();Action<bool,string> check=(ok,label)=>checks.Add((ok?"PASS ":"FAIL ")+label);
        var w=Object.FindFirstObjectByType<OverburstUIWorkshop>();w.ShowComparison();Canvas.ForceUpdateCanvases();
        var gear=w.Equipment.transform.Find("Layout");
        var left=(RectTransform)gear.Find("Slot • 투구");var right=(RectTransform)gear.Find("Slot • 목걸이");
        check(left.anchoredPosition.x==((RectTransform)gear.Find("Weapon Slot")).anchoredPosition.x && right.anchoredPosition.x==((RectTransform)gear.Find("Flask Slot 3")).anchoredPosition.x,"equipment columns and lower row edge alignment");
        var portrait=gear.GetComponentInChildren<OverburstUICharacterPreview>();portrait.RenderNow();check(portrait.Texture&&portrait.Texture.IsCreated(),"actual player 3D render texture created");
        int users=OverburstUICharacterPreview.ActiveUsers;
        for(int i=0;i<3;i++){w.Equipment.Close();check(OverburstUICharacterPreview.ActiveUsers==users-1,"portrait close releases reference "+i);w.Equipment.Show();portrait.RenderNow();check(OverburstUICharacterPreview.ActiveUsers==users&&portrait.Texture.IsCreated(),"portrait reopen "+i);}
        var visual=AssetDatabase.LoadAssetAtPath<GameObject>(OverburstUIWorkshopBuilder.Root+"/PF_OverburstCharacterPreviewModel.prefab");
        check(visual.GetComponentsInChildren<MonoBehaviour>(true).Length==0&&visual.GetComponentsInChildren<Collider>(true).Length==0,"player preview contains only model, no gameplay controllers or colliders");
        var slot=gear.Find("Flask Slot 1").GetComponent<OverburstUIItemSlotView>();var icon=slot.transform.Find("Icon").GetComponent<Image>();var saved=icon.sprite;
        var profile=new SerializedObject(icon.GetComponent<OverburstUIIconFraming>()).FindProperty("artwork");int count=0;
        for(int i=0;i<profile.arraySize;i++){
            var entry=profile.GetArrayElementAtIndex(i);var sprite=(Sprite)entry.FindPropertyRelative("sprite").objectReferenceValue;if(!sprite.name.StartsWith("Flask_"))continue;
            slot.Present(sprite,ItemGrade.Epic);var bounds=entry.FindPropertyRelative("bounds").rectValue;var rect=icon.rectTransform;
            var center=rect.anchoredPosition+(bounds.center-Vector2.one*.5f)*rect.rect.size;
            check(center.magnitude<.01f&&Mathf.Abs(Mathf.Max(bounds.width,bounds.height)*rect.rect.width-68)<.01f,sprite.name+" visible bounds centered / 68px");count++;
        }
        check(count==12,"all twelve flask artwork bounds covered");slot.Present(saved,ItemGrade.Epic);
        w.ShowGrades();Canvas.ForceUpdateCanvases();
        var samples=Object.FindObjectsByType<OverburstUISlotGradePreview>(FindObjectsSortMode.None).Where(g=>g.gameObject.layer!=31&&g.transform.IsChildOf(w.transform)&&g.transform.parent.name.Contains("Row")).ToArray();
        // Locate diagnostic specimen directly; HUD slots also remain active.
        samples=w.transform.GetComponentsInChildren<OverburstUISlotGradePreview>().Where(g=>g.transform.root==w.transform && g.GetComponentInParent<OverburstUIWindow>()==null && g.transform.GetComponentsInParent<Transform>().Any(t=>t.name.Contains("GradeSpecimen"))).ToArray();
        check(samples.Length==24,"eight grades at three diagnostic sizes");
        foreach(var sample in samples){sample.Refresh();check(sample.LiveEffect&&sample.LiveEffect.gameObject.activeSelf,sample.name+" production grade effect alive");var r=(RectTransform)sample.LiveEffect.transform;check(r.offsetMin==Vector2.one*6&&r.offsetMax==Vector2.one*-6,sample.name+" effect bounds remain anchored");}
        w.ShowInventory();Canvas.ForceUpdateCanvases();var scroll=w.Inventory.GetComponentInChildren<ScrollRect>();
        var last=scroll.content.GetChild(scroll.content.childCount-1).GetComponent<OverburstUIItemSlotView>();last.Present(saved,ItemGrade.Epic);
        var grade=last.GetComponent<OverburstUISlotGradePreview>();var tick=typeof(OverburstUISlotGradePreview).GetMethod("LateUpdate",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        scroll.verticalNormalizedPosition=1;Canvas.ForceUpdateCanvases();tick.Invoke(grade,null);check(!grade.LiveEffect.gameObject.activeSelf,"offscreen last-row grade hidden");
        scroll.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();tick.Invoke(grade,null);check(grade.LiveEffect.gameObject.activeSelf,"scroll reveals last-row grade");
        last.Present(null,ItemGrade.Common);scroll.verticalNormalizedPosition=1;w.ShowComparison();
        File.WriteAllLines(Path.Combine(Output,"detail-validation.txt"),checks);return string.Join("\n",checks);
    }
    public static string CaptureComposite(string file)
    {
        Canvas.ForceUpdateCanvases();
        foreach(var preview in Object.FindObjectsByType<OverburstUICharacterPreview>(FindObjectsSortMode.None))preview.RenderNow();
        foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)){var type=b.GetType();if(type.Name=="SlotVefectsTopOverlayRuntime"||type.Name=="SlotShieldTopOverlayRuntime"){type.GetMethod("RefreshTargets",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)?.Invoke(b,null);type.GetMethod("LateUpdate",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)?.Invoke(b,null);}}
        var camera=GameObject.Find("UI Preview Camera").GetComponent<Camera>();
        var overlays=Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c=>c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
        var oldCameras=overlays.Select(c=>c.worldCamera).ToArray();var distances=overlays.Select(c=>c.planeDistance).ToArray();
        var rt=new RenderTexture(1920,1080,24);var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;int mask=camera.cullingMask;
        try{
            camera.cullingMask=~(1<<31);camera.targetTexture=rt;
            foreach(var c in overlays){c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=camera;c.planeDistance=1;}
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
            var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();
            var path=Path.Combine(Output,"Captures",file+".png");File.WriteAllBytes(path,tex.EncodeToPNG());Object.DestroyImmediate(tex);return path;
        }finally{camera.targetTexture=oldTarget;camera.cullingMask=mask;RenderTexture.active=oldActive;for(int i=0;i<overlays.Length;i++){overlays[i].renderMode=RenderMode.ScreenSpaceOverlay;overlays[i].worldCamera=oldCameras[i];overlays[i].planeDistance=distances[i];}Object.DestroyImmediate(rt);}
    }
    public static string CaptureGallery()
    {
        foreach(var p in Object.FindObjectsByType<OverburstUICharacterPreview>(FindObjectsSortMode.None))p.RenderNow();
        var root=GameObject.Find(OverburstUIWorkshopBuilder.GalleryName);var camera=new GameObject("Temporary Gallery Capture").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=3600;camera.aspect=1;camera.transform.position=root.transform.position+new Vector3(2120,-2640,-100);camera.farClipPlane=200;camera.cullingMask=1<<31;camera.backgroundColor=new Color(.025f,.025f,.025f);camera.clearFlags=CameraClearFlags.SolidColor;
        var rt=new RenderTexture(2400,2400,24);var active=RenderTexture.active;try{camera.targetTexture=rt;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;var tex=new Texture2D(2400,2400,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,2400,2400),0,0);tex.Apply();var path=Path.Combine(Output,"Captures","00-gallery.png");File.WriteAllBytes(path,tex.EncodeToPNG());Object.DestroyImmediate(tex);return path;}finally{RenderTexture.active=active;Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(rt);}
    }
}
