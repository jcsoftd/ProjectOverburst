using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OverburstSkillTreeVerifier
{
    static readonly List<string> checks = new List<string>();
    static void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
    static object SceneSnapshot() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => { var s=SceneManager.GetSceneAt(i);return new{path=s.path,dirty=s.isDirty,roots=s.GetRootGameObjects().Select(r=>r.GetInstanceID()).OrderBy(x=>x).ToArray()}; }).ToArray();
    public static void Run(string output)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new InvalidOperationException("Idle shared Editor required.");
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"native-results.json"),"{\"status\":\"RUNNING\"}");
        try{Validate(output);}catch(Exception e){File.WriteAllText(Path.Combine(output,"native-results.json"),JsonConvert.SerializeObject(new{status="FAIL",checks,error=e.ToString()},Formatting.Indented));throw;}
    }
    static void Validate(string output)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)throw new InvalidOperationException("Idle shared Editor required.");
        checks.Clear();string before=JsonConvert.SerializeObject(SceneSnapshot());int previews=EditorSceneManager.previewSceneCount;
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(OverburstSkillTreeBuilder.PrefabPath);
        Require(prefab!=null,"Authored Resources prefab exists");
        Require(prefab.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))==0,"Missing scripts 0");
        Require(AssetDatabase.LoadAllAssetsAtPath(OverburstSkillTreeBuilder.AtlasPath).OfType<Sprite>().Count()==16,"16 generated native sprites");
        Require(AssetDatabase.LoadAssetAtPath<Sprite>(OverburstSkillTreeBuilder.MoveGlyphPath)!=null,"Authored high-contrast movement glyph");
        var authored=prefab.GetComponent<OverburstSkillTreeUI>();var reference=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstInventory_Rpg11.prefab");
        var sharedHeader=prefab.transform.Find("Skill Tree Screen/Window/Shared Window Chrome/Header");
        Require(sharedHeader&&sharedHeader.Find("Text").GetComponent<Text>().font==reference.transform.Find("Header/Text").GetComponent<Text>().font,"Same authored header/font as inventory");
        Require(sharedHeader.Find("Border").GetComponent<Image>().type==Image.Type.Tiled&&Mathf.Approximately(((RectTransform)sharedHeader.Find("Border")).rect.height,44),"Header uses authored 22px bottom strip, not full-band stretch");
        Require(authored.elementSprites.Length==5&&authored.effectIcons.Length==6&&authored.effectIcons.Take(5).Select((icon,i)=>icon.sprite==authored.elementSprites[i]).All(v=>v),"All five existing HUD element icons in comparison rows");
        var asset=prefab.GetComponent<OverburstSkillTreeUI>().catalogAsset;var data=JsonUtility.FromJson<OverburstSkillTreeCatalog>(asset.text);data.Validate();
        Require(data.nodes.Count(n=>n.kind=="stat")==32 && data.nodes.Count(n=>n.kind=="root"||n.kind=="guide")==5 && data.nodes.Count(n=>n.IsReserved)==16,"32 stat / 5 free / 16 reserved / 53 shared nodes");
        SkillTreeFoundationVerifier.Run(Path.Combine(output,"Rules"));
        var segments=data.segments;
        for(int i=0;i<segments.Length;i++)for(int j=i+1;j<segments.Length;j++)if(Cross(segments[i].A,segments[i].B,segments[j].A,segments[j].B))throw new InvalidOperationException("Route crossing "+i+"/"+j); Require(true,"All native route pairs: crossings 0");
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1600,900),new Vector2Int(1280,720)})CaptureAndCheck(prefab,size,output);
        Require(JsonConvert.SerializeObject(SceneSnapshot())==before,"User scene identities / dirty flags preserved");Require(EditorSceneManager.previewSceneCount==previews,"Owned preview scene / RT / camera returned");
        File.WriteAllText(Path.Combine(output,"native-results.json"),JsonConvert.SerializeObject(new{status="PASS_SCOPED",checks,checkCount=checks.Count,images=new[]{"native-1920x1080.png","native-1600x900.png","native-1280x720.png","native-tooltip.png","native-zoom.png"},browser="NOT_USED",playerBuild="NOT_RUN",combatModifiers="ACCOUNT_STAT_MODIFIERS",sceneBefore=JsonConvert.DeserializeObject(before)},Formatting.Indented));
    }
    static float Turn(Vector2 a,Vector2 b,Vector2 c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
    static bool Cross(Vector2 a,Vector2 b,Vector2 c,Vector2 d)=>Turn(a,b,c)*Turn(a,b,d)<0&&Turn(c,d,a)*Turn(c,d,b)<0;
    static Rect Bounds(RectTransform r)
    {var c=new Vector3[4];r.GetWorldCorners(c);var camera=r.GetComponentInParent<Canvas>().rootCanvas.worldCamera;var s=c.Select(p=>RectTransformUtility.WorldToScreenPoint(camera,p)).ToArray();return Rect.MinMaxRect(s.Min(p=>p.x),s.Min(p=>p.y),s.Max(p=>p.x),s.Max(p=>p.y));}
    static bool Overlap(Rect a,Rect b)=>Mathf.Min(a.xMax,b.xMax)-Mathf.Max(a.xMin,b.xMin)>.5f && Mathf.Min(a.yMax,b.yMax)-Mathf.Max(a.yMin,b.yMin)>.5f;
    static void CaptureAndCheck(GameObject prefab,Vector2Int size,string output)
    {
        var previousSelection=EventSystem.current?EventSystem.current.currentSelectedGameObject:null; var scene=EditorSceneManager.NewPreviewScene();GameObject canvasGo=null,cameraGo=null;RenderTexture rt=null;Texture2D pixels=null;
        try
        {
            cameraGo=new GameObject("Owned Skill Tree Capture Camera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraGo,scene);var camera=cameraGo.GetComponent<Camera>();camera.scene=scene;camera.enabled=false;camera.cameraType=CameraType.Game;camera.useOcclusionCulling=false;camera.transform.position=new Vector3(0,0,-20);camera.orthographic=true;camera.orthographicSize=5;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.cullingMask=1<<5;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.041f,.03f);
            rt=new RenderTexture(size.x,size.y,24);rt.Create();camera.targetTexture=rt;
            canvasGo=new GameObject("Owned Native Skill Tree Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));SceneManager.MoveGameObjectToScene(canvasGo,scene);canvasGo.layer=5;
            var canvas=canvasGo.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
            var scaler=canvasGo.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var go=Object.Instantiate(prefab,canvasGo.transform,false);var ui=go.GetComponent<OverburstSkillTreeUI>();ui.Initialize();ui.Open();ui.HideTooltip();Canvas.ForceUpdateCanvases();ui.FitWindow();ui.Refresh();Canvas.ForceUpdateCanvases();Render(camera,rt);
            Require(Mathf.Approximately(ui.Zoom,1.2f)&&ui.Zoom>ui.FitZoom,"Opening zoom is readable 120% above full-map fit "+size);
            string[] guideIds={"G_W","G_H","G_D","G_Q"};
            for(int i=0;i<guideIds.Length;i++)
            {
                var guide=ui.nodes.First(n=>n.nodeId==guideIds[i]);
                Require(guide.captionRect==ui.areaLabels[i]&&guide.caption==ui.areaLabels[i].GetComponent<Text>(),"Direction caption belongs to guide "+guideIds[i]+" "+size);
                var labelPosition=guide.captionRect.anchoredPosition;
                Require(Vector2.Distance(labelPosition,((RectTransform)guide.transform).anchoredPosition+guide.captionOffset*ui.Zoom)<.01f,"Direction caption follows its guide "+guideIds[i]+" "+size);
                Require(ui.nodes.Where(n=>guideIds.Contains(n.nodeId)).OrderBy(n=>Vector2.Distance(labelPosition,((RectTransform)n.transform).anchoredPosition)).First()==guide,"Direction caption nearest correct guide "+guideIds[i]+" "+size);
                Require(guide.caption.preferredHeight<=guide.captionRect.rect.height&&guide.caption.cachedTextGenerator.characterCountVisible>0,"Direction caption renders without vertical truncation "+guideIds[i]+" "+size);
            }
            Require(ui.nodes.First(n=>n.nodeId=="S_W1").caption.fontSize==13&&ui.areaLabels[0].GetComponent<Text>().fontSize==14,"Compact default stat 13 / direction 14 typography "+size);
            Require(ui.nodeName.fontSize==21&&ui.trigger.fontSize==14&&ui.tipName.fontSize==18,"Compact detail and hover hierarchy "+size);
            Require(ui.nodes.All(n=>n.Accent&&!n.Accent.raycastTarget&&!n.selection.gameObject.activeSelf),"Separate focus accents never use the old gold selection fill "+size);
            Require(ui.Catalog.segments.All(s=>ui.routes.StateFor(s)==OverburstSkillTreeRoutes.RouteState.Inactive),"Free guides alone never light learned routes "+size);
            var openingBoxes=new List<KeyValuePair<string,Rect>>();
            foreach(var node in ui.nodes.Where(n=>n.gameObject.activeInHierarchy)){openingBoxes.Add(new KeyValuePair<string,Rect>(node.nodeId,Bounds((RectTransform)node.transform)));if(node.captionRect&&node.captionRect.gameObject.activeInHierarchy)openingBoxes.Add(new KeyValuePair<string,Rect>(node.nodeId+":label",Bounds(node.captionRect)));}
            var openingOverlaps=new List<string>();for(int i=0;i<openingBoxes.Count;i++)for(int j=i+1;j<openingBoxes.Count;j++)if(Overlap(openingBoxes[i].Value,openingBoxes[j].Value))openingOverlaps.Add(openingBoxes[i].Key+" / "+openingBoxes[j].Key);
            Require(openingOverlaps.Count==0,"Opening view node/caption/region overlaps 0 "+size+": "+string.Join(", ",openingOverlaps));
            ReadPixels(rt,Path.Combine(output,"native-default-"+size.x+"x"+size.y+".png"),ref pixels);
            ui.ResetMap();ui.Refresh();Canvas.ForceUpdateCanvases();Render(camera,rt);Canvas.ForceUpdateCanvases();Render(camera,rt);
            ReadPixels(rt,Path.Combine(output,"native-"+size.x+"x"+size.y+".png"),ref pixels);
            Require(pixels.GetPixels32().Where((v,i)=>i%31==0).Distinct().Take(64).Count()>32,"Native capture contains rendered pixels "+size);
            var boxes=new List<KeyValuePair<string,Rect>>();
            foreach(var node in ui.nodes){
                boxes.Add(new KeyValuePair<string,Rect>(node.nodeId+":node",Bounds((RectTransform)node.transform)));
                Require(node.icon.sprite!=null,"Glyph reference "+node.nodeId);
                if(node.captionRect){boxes.Add(new KeyValuePair<string,Rect>(node.nodeId+":label",Bounds(node.captionRect)));Require(node.caption.preferredWidth<=node.caption.rectTransform.rect.width+1,"Native label width "+node.nodeId);}
            }
            var overlaps=new List<string>();for(int i=0;i<boxes.Count;i++)for(int j=i+1;j<boxes.Count;j++)if(Overlap(boxes[i].Value,boxes[j].Value))overlaps.Add(boxes[i].Key+" / "+boxes[j].Key);
            File.WriteAllText(Path.Combine(output,"bounds-"+size.x+"x"+size.y+".json"),JsonConvert.SerializeObject(new{width=size.x,height=size.y,overlaps,boxes=boxes.Select(x=>new{name=x.Key,x=x.Value.x,y=x.Value.y,width=x.Value.width,height=x.Value.height})},Formatting.Indented));
            Require(overlaps.Count==0,"Native node / caption / region overlaps 0 at "+size+": "+string.Join(", ",overlaps));
            foreach(var segment in ui.Catalog.segments)
            {
                var a=segment.A*ui.MapScale+ui.Pan;var b=segment.B*ui.MapScale+ui.Pan;bool exposed=false,visible=false;
                foreach(float t in new[]{.25f,.5f,.75f})
                {
                    var local=Vector2.Lerp(a,b,t);var point=RectTransformUtility.WorldToScreenPoint(camera,ui.routes.rectTransform.TransformPoint(local));
                    if(boxes.Any(box=>box.Value.Contains(point)))continue;exposed=true;
                    int x=Mathf.Clamp(Mathf.RoundToInt(point.x),1,pixels.width-2),y=Mathf.Clamp(Mathf.RoundToInt(point.y),1,pixels.height-2);
                    for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++){var color=pixels.GetPixel(x+dx,y+dy);if(Mathf.Max(color.r,color.g,color.b)>.16f)visible=true;}
                }
                Require(!exposed||visible,"Actual route pixels visible "+segment.x1+","+segment.y1+" to "+segment.x2+","+segment.y2+" at "+size);
            }
            var viewportBounds=Bounds(ui.viewport);
            var cancelBounds=Bounds((RectTransform)ui.cancel.transform);var applyBounds=Bounds((RectTransform)ui.apply.transform);var actionBounds=Bounds((RectTransform)ui.action.transform);
            Require(Mathf.Abs(cancelBounds.yMin-applyBounds.yMin)<.5f&&Mathf.Abs(cancelBounds.height-applyBounds.height)<.5f&&Mathf.Abs(cancelBounds.width-applyBounds.width)<.5f,"Footer buttons share size and baseline "+size);
            Require(!Overlap(cancelBounds,applyBounds)&&actionBounds.yMin>applyBounds.yMax&&Bounds(ui.window).Contains(cancelBounds.min)&&Bounds(ui.window).Contains(applyBounds.max),"Action/footer buttons have spacing and remain inside window "+size);
            Require(ui.nodes.Where(n=>n.captionRect).All(n=>n.captionRect.gameObject.activeInHierarchy),"Full-map fit retains node captions "+size);
            Require(ui.nodes.All(n=>viewportBounds.Contains(Bounds((RectTransform)n.transform).min)&&viewportBounds.Contains(Bounds((RectTransform)n.transform).max)),"All 53 targets visible at full-map fit "+size);
            Require(ui.nodeName.cachedTextGenerator.characterCountVisible>0,"Native Korean text generated "+size);
            var save=Path.Combine(output,"native-"+size.x+"x"+size.y+".png");ReadPixels(rt,save,ref pixels);
            if(size.x==1920){
                foreach(var n in ui.Catalog.nodes){ui.SelectNode(n.id,true);ui.ShowTooltip(n.id);Canvas.ForceUpdateCanvases();Require(ui.tipName.text==n.name&&ui.tooltip.gameObject.activeSelf,"Native hover contents "+n.id);var frame=Bounds(ui.window);var tip=Bounds(ui.tooltip);Require(frame.Contains(tip.min)&&frame.Contains(tip.max),"Tooltip frame clamp "+n.id);}
                ui.Plan.Load(4,Array.Empty<string>());ui.Refresh();
                Require(ui.StateFor("S_W1")==OverburstSkillTreeNodeView.NodeState.Available && ui.StateFor("S_W3")==OverburstSkillTreeNodeView.NodeState.Locked,"Available versus connected-locked state");
                ui.Plan.Toggle("S_W1");ui.Refresh();Require(ui.StateFor("S_W1")==OverburstSkillTreeNodeView.NodeState.PurchaseDraft,"Purchase draft state");
                ui.Plan.Apply();ui.Refresh();Require(ui.StateFor("S_W1")==OverburstSkillTreeNodeView.NodeState.Learned,"Committed learned state");
                ui.Plan.Toggle("S_W1");ui.Refresh();Require(ui.StateFor("S_W1")==OverburstSkillTreeNodeView.NodeState.RefundDraft,"Refund draft state");
                ui.Plan.Cancel();ui.Refresh();
                Require(ui.nodes.Where(n=>n.stateMark).Select(n=>n.State).Distinct().Count()>=3,"Frame and icon badges use distinct states");
                var sample=new[]{"S_W1","S_H1","S_D1","S_Q1"};ui.Plan.Load(8,sample);ui.Refresh();ui.ResetDefaultMap();ui.SelectNode("S_W2",false);ui.HideTooltip();Canvas.ForceUpdateCanvases();Render(camera,rt);
                var learnedView=ui.nodes.First(n=>n.nodeId=="S_W1");var lockedView=ui.nodes.First(n=>n.nodeId=="S_W4");var availableView=ui.nodes.First(n=>n.nodeId=="S_W2");
                Require(learnedView.State==OverburstSkillTreeNodeView.NodeState.Learned&&learnedView.Accent.State==learnedView.State&&learnedView.stateMark.text=="✓","Learned node owns permanent gold rim and check badge");
                Require(availableView.State==OverburstSkillTreeNodeView.NodeState.Available&&availableView.stateMark.text=="○"&&availableView.Accent.Focused,"Available focus remains an unlearned hollow node");
                Require(learnedView.icon.color.grayscale>lockedView.icon.color.grayscale+.4f&&learnedView.frame.color.grayscale>lockedView.frame.color.grayscale+.4f,"Learned versus locked icon and fill luminance separation");
                var lockedTint=lockedView.frame.color;var lockedCaption=lockedView.caption.color;ui.SelectNode("S_W4",false);ui.HideTooltip();
                Require(lockedView.frame.color==lockedTint&&lockedView.caption.color==lockedCaption&&lockedView.Accent.Focused&&lockedView.Accent.State==OverburstSkillTreeNodeView.NodeState.Locked,"Selection never makes locked fill/caption learned gold");
                Require(ui.meta.text.Contains("미습득")&&ui.detailNodeIcon.color==lockedView.icon.color,"Detail explicitly exposes selected locked status");
                Require(ui.Catalog.segments.Where(s=>s.sources.Any(id=>id=="ROOT")).All(s=>ui.routes.StateFor(s)==OverburstSkillTreeRoutes.RouteState.Learned),"Paid allocation lights the correct central guide routes");
                Canvas.ForceUpdateCanvases();Render(camera,rt);ReadPixels(rt,Path.Combine(output,"native-states-default.png"),ref pixels);
                ui.Plan.Toggle("S_W2");ui.Refresh();Require(availableView.State==OverburstSkillTreeNodeView.NodeState.PurchaseDraft&&availableView.stateMark.text=="+"&&availableView.Accent.State==availableView.State,"Draft owns pale dashed rim and plus badge");
                Require(ui.Catalog.segments.Any(s=>ui.routes.StateFor(s)==OverburstSkillTreeRoutes.RouteState.PurchaseDraft),"Draft has separate pale dashed routes");
                ui.Plan.Toggle("S_W1");ui.Refresh();Require(learnedView.State==OverburstSkillTreeNodeView.NodeState.RefundDraft&&learnedView.stateMark.text=="−"&&ui.Catalog.segments.Any(s=>ui.routes.StateFor(s)==OverburstSkillTreeRoutes.RouteState.RefundDraft),"Refund owns red rim/minus and route state");
                ui.Plan.Cancel();ui.Refresh();ui.ResetMap();ui.SelectNode("S_W1",false);ui.HideTooltip();Canvas.ForceUpdateCanvases();Render(camera,rt);ReadPixels(rt,Path.Combine(output,"native-states-fit.png"),ref pixels);
                Require(ui.nodes.All(n=>n.transform.Find("State Accent")&&n.GetComponentsInChildren<OverburstSkillTreeNodeAccent>(true).Length==1),"Refresh/cancel never duplicates accent objects");
                foreach(var n in ui.Catalog.nodes.Where(n=>n.IsReserved)){ui.SelectNode(n.id,true);ui.ShowTooltip(n.id);Require(!ui.action.interactable&&ui.actionLabel.text.Contains("확장 예정")&&ui.tipState.text.Contains("확장 예정"),"Reserved node read-only detail "+n.id);}
                ui.SelectNode("K_W",true);ui.ShowTooltip("K_W");Canvas.ForceUpdateCanvases();Render(camera,rt);ReadPixels(rt,Path.Combine(output,"native-keystone.png"),ref pixels);
                ui.Plan.Load(0,Array.Empty<string>());ui.Refresh();ui.SelectNode("S_W1",true);ui.ShowTooltip("S_W1");Canvas.ForceUpdateCanvases();Render(camera,rt);ReadPixels(rt,Path.Combine(output,"native-tooltip.png"),ref pixels);
                ui.HideTooltip();ui.ResetMap();var prior=ui.Pan;var priorZoom=ui.Zoom;var anchor=new Vector2(200,120);ui.ZoomAt(1.6f,anchor);Require(Vector2.Distance(ui.Pan,anchor-(anchor-prior)*1.6f/priorZoom)<.01f,"Cursor anchored native zoom");Require(!ui.tooltip.gameObject.activeSelf,"Zoom closes tooltip");Require(ui.nodes.First(n=>n.nodeId=="S_W1").transform.localScale.x==1.6f&&ui.nodes.First(n=>n.nodeId=="S_W1").caption.fontSize>14,"Zoom grows native node / icon / crisp label");ui.SelectNode("S_W3",true);Canvas.ForceUpdateCanvases();Render(camera,rt);ReadPixels(rt,Path.Combine(output,"native-zoom.png"),ref pixels);
                ui.ZoomAt(50,Vector2.zero);Require(ui.Zoom==2.5f,"Native max zoom 250%");
                foreach(string id in guideIds){ui.SelectNode(id,true);Canvas.ForceUpdateCanvases();var guide=ui.nodes.First(n=>n.nodeId==id);Require(Bounds(ui.viewport).Contains(Bounds(guide.captionRect).min)&&Bounds(ui.viewport).Contains(Bounds(guide.captionRect).max),"Guide selection retains offset caption at 250% "+id);}
                ui.ZoomAt(-50,Vector2.zero);Require(ui.Zoom==ui.MinimumZoom,"Native dynamic min zoom");ui.SetPan(new Vector2(110,-70));Require(ui.Pan==new Vector2(110,-70),"Native pan");ui.ResetMap();Require(ui.Pan==Vector2.zero,"Native center reset");
            }
            ui.Close();Require(!ui.IsOpen&&!ui.tooltip.gameObject.activeSelf,"Window and hover close "+size);
        }
        finally {if(pixels)Object.DestroyImmediate(pixels);if(rt){rt.Release();Object.DestroyImmediate(rt);}if(canvasGo)Object.DestroyImmediate(canvasGo);if(cameraGo)Object.DestroyImmediate(cameraGo);EditorSceneManager.ClosePreviewScene(scene);if(EventSystem.current && previousSelection)EventSystem.current.SetSelectedGameObject(previousSelection);}
    }
    static void Render(Camera camera,RenderTexture target)
    { for(int pass=0;pass<3;pass++){foreach(var root in camera.scene.GetRootGameObjects())foreach(var graphic in root.GetComponentsInChildren<Graphic>())graphic.SetAllDirty();Canvas.ForceUpdateCanvases();var request=new UniversalRenderPipeline.SingleCameraRequest{destination=target};if(RenderPipeline.SupportsRenderRequest(camera,request))RenderPipeline.SubmitRenderRequest(camera,request);else camera.Render();} }
    static void ReadPixels(RenderTexture rt,string path,ref Texture2D pixels)
    {if(pixels)Object.DestroyImmediate(pixels);var prior=RenderTexture.active;try{RenderTexture.active=rt;pixels=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}finally{RenderTexture.active=prior;}}
}
