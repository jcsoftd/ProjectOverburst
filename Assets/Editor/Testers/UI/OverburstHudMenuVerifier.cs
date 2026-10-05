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
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

public static class OverburstHudMenuVerifier
{
    static readonly List<string> checks = new List<string>();
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
    static string Scenes()=>JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return new{s.path,s.isDirty,roots=s.GetRootGameObjects().Select(r=>r.GetInstanceID()).OrderBy(x=>x).ToArray()};}));
    public static string Run(string output)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new InvalidOperationException("Idle Editor required.");
        Directory.CreateDirectory(output);checks.Clear();string before=Scenes();int previews=EditorSceneManager.previewSceneCount;
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(OverburstHudMenuBuilder.PrefabPath);
            Check(prefab,"Authored Resources menu prefab exists");
            Check(prefab.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))==0,"Missing scripts 0");
            var authored=prefab.GetComponent<OverburstHudMenu>();
            Check(authored.entries.Length==4&&string.Join(",",authored.entries.Select(e=>e.label))=="인벤토리,장비,스킬트리,설정","Approved four destinations and order");
            Check(authored.entries.All(e=>e.icon&&e.hoverIcon)&&authored.menuIcon&&authored.closeIcon,"Six native menu icons and six approved hover variants");
            Check(authored.triggerVisual&&authored.triggerVisual.normal&&authored.triggerVisual.hover&&authored.triggerVisual.expanded&&authored.rowTemplate.hoverBackground&&authored.rowTemplate.pressedBackground,"Approved background state references serialized");
            Check(authored.rowTemplate.label.font==AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf"),"Existing Korean UI font");
            Check(authored.rowTemplate.label.fontSize==15&&authored.rowTemplate.icon.rectTransform.sizeDelta==new Vector2(28,28),"Approved label and glyph sizes");
            foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1600,900),new Vector2Int(1280,720),new Vector2Int(2560,1080)})RenderAndCheck(prefab,size,output);
            Check(Scenes()==before,"User scene identities and dirty flags preserved");Check(EditorSceneManager.previewSceneCount==previews,"Owned previews returned");
            File.WriteAllText(Path.Combine(output,"native-results.json"),JsonConvert.SerializeObject(new{status="PASS_SCOPED",checks,checkCount=checks.Count,render="Native Unity UI",playerBuild="NOT_RUN"},Formatting.Indented));
            return "PASS_SCOPED native menu checks="+checks.Count;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"native-results.json"),JsonConvert.SerializeObject(new{status="FAIL",checks,error=e.ToString()},Formatting.Indented));throw;}
    }
    static Rect Bounds(RectTransform rect,Camera camera)
    {var corners=new Vector3[4];rect.GetWorldCorners(corners);var points=corners.Select(c=>RectTransformUtility.WorldToScreenPoint(camera,c)).ToArray();return Rect.MinMaxRect(points.Min(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.x),points.Max(p=>p.y));}
    static bool Contains(Rect outer,Rect inner)=>outer.Contains(inner.min+Vector2.one*.1f)&&outer.Contains(inner.max-Vector2.one*.1f);
    static bool Overlap(Rect a,Rect b)=>Mathf.Min(a.xMax,b.xMax)-Mathf.Max(a.xMin,b.xMin)>.5f&&Mathf.Min(a.yMax,b.yMax)-Mathf.Max(a.yMin,b.yMin)>.5f;
    static void RenderAndCheck(GameObject prefab,Vector2Int size,string output)
    {
        var scene=EditorSceneManager.NewPreviewScene();GameObject root=null,cameraGo=null;RenderTexture rt=null;Texture2D pixels=null;
        try
        {
            cameraGo=new GameObject("Owned HUD Menu Capture Camera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraGo,scene);
            var camera=cameraGo.GetComponent<Camera>();camera.scene=scene;camera.enabled=false;camera.cameraType=CameraType.Game;camera.transform.position=new Vector3(0,0,-20);camera.orthographic=true;camera.orthographicSize=5;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.cullingMask=1<<5;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.11f,.085f);
            rt=new RenderTexture(size.x,size.y,24);rt.Create();camera.targetTexture=rt;
            root=new GameObject("Owned HUD Menu Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));root.layer=5;SceneManager.MoveGameObjectToScene(root,scene);
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var instance=Object.Instantiate(prefab,root.transform,false);var ui=instance.GetComponent<OverburstHudMenu>();ui.Initialize();ui.Open();ui.popup.alpha=1;ui.panel.anchoredPosition=new Vector2(0,70);
            Canvas.ForceUpdateCanvases();Render(camera,rt);ui.FitLayout();Canvas.ForceUpdateCanvases();Render(camera,rt);
            var screen=new Rect(0,0,size.x,size.y);var panel=Bounds(ui.panel,camera);var trigger=Bounds((RectTransform)ui.trigger.transform,camera);var view=Bounds(ui.viewport,camera);
            Check(Contains(screen,panel)&&Contains(screen,trigger),"Menu inside screen "+size);
            Check(!Overlap(panel,trigger)&&Mathf.Abs(panel.xMax-trigger.xMax)<.6f,"Panel right alignment and separation "+size);
            Check(ui.panel.sizeDelta==new Vector2(244,226)&&ui.dock.anchoredPosition==new Vector2(-24,24),"Approved panel and edge dimensions "+size);
            var rows=ui.Rows.Select(row=>Bounds((RectTransform)row.transform,camera)).ToArray();
            for(int i=0;i<ui.Rows.Length;i++)
            {
                var row=ui.Rows[i];var icon=Bounds(row.icon.rectTransform,camera);var label=Bounds(row.label.rectTransform,camera);var arrow=Bounds(row.arrow.rectTransform,camera);
                Check(Contains(view,rows[i])&&Contains(rows[i],icon)&&Contains(rows[i],label)&&Contains(rows[i],arrow),"Row contents contained "+size+" / "+i);
                Check(!Overlap(icon,label)&&!Overlap(label,arrow)&&row.label.preferredWidth<=row.label.rectTransform.rect.width,"Glyph label arrow spacing "+size+" / "+i);
                Check(row.label.cachedTextGenerator.characterCountVisible==row.label.text.Length,"All Korean label glyphs rendered "+size+" / "+i);
                Check(row.separator.gameObject.activeSelf==(i>0),"Horizontal separator count "+size+" / "+i);
                Check(Mathf.Abs(icon.center.y-label.center.y)<.6f&&Mathf.Abs(label.center.y-arrow.center.y)<.6f,"Row vertical alignment "+size+" / "+i);
            }
            for(int i=0;i<rows.Length;i++)for(int j=i+1;j<rows.Length;j++)Check(!Overlap(rows[i],rows[j]),"Rows do not overlap "+size+" / "+i+"-"+j);
            Capture(rt,Path.Combine(output,"native-menu-"+size.x+"x"+size.y+".png"),ref pixels);
            Check(pixels.GetPixels32().Where((p,i)=>i%17==0).Distinct().Take(64).Count()>24,"Native rendered pixels "+size);
            var pointer=new PointerEventData(null){button=PointerEventData.InputButton.Left};
            for(int i=0;i<ui.Rows.Length;i++)
            {
                var row=ui.Rows[i];row.OnPointerEnter(pointer);
                Check(row.highlight.enabled&&row.highlight.sprite==row.hoverBackground&&row.icon.sprite==ui.entries[i].hoverIcon&&row.label.color==(Color)new Color32(255,240,211,255),"Approved row hover background icon and text "+size+" / "+i);
                row.OnPointerDown(pointer);Check(row.highlight.sprite==row.pressedBackground,"Approved pressed row "+size+" / "+i);row.OnPointerUp(pointer);
                if(size.x==1920&&i==2){Render(camera,rt);Capture(rt,Path.Combine(output,"native-menu-hover.png"),ref pixels);}
                row.OnPointerExit(pointer);Check(!row.highlight.enabled&&row.icon.sprite==ui.entries[i].icon&&row.label.color==(Color)new Color32(237,227,209,255),"Pointer exit restores row "+size+" / "+i);
                row.OnSelect(new BaseEventData(null));Check(row.highlight.enabled,"Keyboard focus highlights row "+size+" / "+i);row.OnDeselect(new BaseEventData(null));
            }
            ui.triggerVisual.OnPointerEnter(pointer);Check(ui.triggerVisual.background.sprite==ui.triggerVisual.expanded&&ui.triggerIcon.sprite==ui.triggerVisual.closeHover,"Expanded trigger retains approved surface and hover glyph "+size);ui.triggerVisual.OnPointerExit(pointer);
            var original=ui.entries;var extended=Enumerable.Range(0,24).Select(i=>new OverburstHudMenu.Entry{label="확장 메뉴 "+(i+1),icon=original[i%4].icon,destination=original[i%4].destination}).ToArray();
            ui.SetEntries(extended);ui.Open();ui.FitLayout();Canvas.ForceUpdateCanvases();Render(camera,rt);
            Check(ui.content.rect.height>ui.viewport.rect.height&&Contains(screen,Bounds(ui.panel,camera)),"Growing list clamped and scrollable "+size);
            ui.KeepVisible(23);Canvas.ForceUpdateCanvases();Check(Contains(Bounds(ui.viewport,camera),Bounds((RectTransform)ui.Rows[23].transform,camera)),"Keyboard last row scrolls into view "+size);
            if(size.x==1920){Render(camera,rt);Capture(rt,Path.Combine(output,"native-menu-expanded-list.png"),ref pixels);}
            ui.CloseImmediate();Check(!ui.IsOpen&&!ui.popup.gameObject.activeSelf&&!ui.outside.gameObject.activeSelf,"Closed popup leaves no screen blocker "+size);
            ui.triggerVisual.OnPointerEnter(pointer);Check(ui.triggerVisual.background.sprite==ui.triggerVisual.hover&&ui.triggerIcon.sprite==ui.triggerVisual.menuHover,"Collapsed trigger hover matches approved state "+size);
            ui.triggerVisual.OnPointerDown(pointer);Check(((RectTransform)ui.trigger.transform).anchoredPosition.y==-1,"Trigger pressed movement is one pixel "+size);ui.triggerVisual.OnPointerUp(pointer);
            if(size.x==1920){Render(camera,rt);Capture(rt,Path.Combine(output,"native-trigger-hover.png"),ref pixels);}
            ui.triggerVisual.OnPointerExit(pointer);Check(ui.triggerVisual.background.sprite==ui.triggerVisual.normal&&ui.triggerIcon.sprite==ui.menuIcon&&((RectTransform)ui.trigger.transform).anchoredPosition==Vector2.zero,"Trigger exit restores approved normal state "+size);
        }
        finally{if(pixels)Object.DestroyImmediate(pixels);if(rt){rt.Release();Object.DestroyImmediate(rt);}if(root)Object.DestroyImmediate(root);if(cameraGo)Object.DestroyImmediate(cameraGo);EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void Render(Camera camera,RenderTexture rt)
    {for(int i=0;i<3;i++){foreach(var g in camera.scene.GetRootGameObjects())foreach(var graphic in g.GetComponentsInChildren<Graphic>())graphic.SetAllDirty();Canvas.ForceUpdateCanvases();var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};if(RenderPipeline.SupportsRenderRequest(camera,request))RenderPipeline.SubmitRenderRequest(camera,request);else camera.Render();}}
    static void Capture(RenderTexture rt,string path,ref Texture2D pixels)
    {if(pixels)Object.DestroyImmediate(pixels);var old=RenderTexture.active;try{RenderTexture.active=rt;pixels=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}finally{RenderTexture.active=old;}}
}
