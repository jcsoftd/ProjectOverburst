using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public static class BuffTooltipVerifier
{
    public const string Output = "../개인파일/코덱스산출/UI/20261008_BuffTooltipImplementation/Evidence";
    public static string Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required");
        var checks = new List<string>();
        void Check(bool ok,string label) { if(!ok) throw new InvalidOperationException(label); checks.Add(label); }
        var scene = EditorSceneManager.NewPreviewScene();
        var previousTarget = RenderTexture.active;
        GameObject canvasObject=null,cameraObject=null,eventObject=null;
        RenderTexture target=null; Texture2D texture=null;
        var flasks = new List<FlaskItemData>();
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(StatusBuffIconBuilder.HudPath);
            Check(prefab!=null&&prefab.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))==0,"saved HUD has no missing scripts");
            Check(prefab.GetComponentsInChildren<BuffTooltipUI>(true).Length==1,"one authored tooltip per HUD");
            var savedView=prefab.GetComponentInChildren<BuffTooltipUI>(true);
            var savedSo=new SerializedObject(savedView);
            Check(new[]{"panel","nameText","remainingText","effectText"}.All(f=>savedSo.FindProperty(f).objectReferenceValue!=null),"all tooltip references saved");
            Check(!((RectTransform)savedSo.FindProperty("panel").objectReferenceValue).gameObject.activeSelf,"saved tooltip starts hidden");
            Check(savedView.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).All(g=>!g.raycastTarget),"tooltip graphics cannot intercept hover");
            Check(!savedView.GetComponent<CanvasGroup>().blocksRaycasts,"tooltip canvas group cannot intercept hover");
            var savedSlots=prefab.GetComponentInChildren<BuffBarUI>(true).GetComponentsInChildren<BuffIconSlotUI>(true).Where(s=>s.gameObject.activeSelf).ToArray();
            Check(savedSlots.Length==7&&savedSlots.All(s=>new SerializedObject(s).FindProperty("tooltip").objectReferenceValue==savedView
                &&s.GetComponent<UnityEngine.UI.Image>().raycastTarget),"all seven original slots reference the shared view and hit area");
            Check(savedView.GetComponentsInChildren<TMP_Text>(true).All(t=>AssetDatabase.GetAssetPath(t.font).StartsWith(BuffTooltipBuilder.FontRoot,StringComparison.Ordinal)),"tooltip uses owned font copies");
            cameraObject=new GameObject("BuffTooltipVerificationCamera",typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject,scene);
            Camera camera=cameraObject.GetComponent<Camera>(); camera.scene=scene; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.125f,.114f,.094f);
            camera.orthographic=true; camera.orthographicSize=5; camera.transform.position=new Vector3(0,0,-10); camera.cullingMask=1<<5;
            target=new RenderTexture(1920,1080,24); target.Create(); camera.targetTexture=target;
            canvasObject=new GameObject("BuffTooltipVerificationCanvas",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasObject,scene); canvasObject.layer=5;
            Canvas canvas=canvasObject.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=5;
            var hud=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene); hud.transform.SetParent(canvas.transform,false);
            var hudRect=(RectTransform)hud.transform; hudRect.anchorMin=Vector2.zero; hudRect.anchorMax=Vector2.one; hudRect.offsetMin=hudRect.offsetMax=Vector2.zero;
            foreach(Transform t in hud.GetComponentsInChildren<Transform>(true))t.gameObject.layer=5;
            foreach(Transform t in hud.transform)if(t.name!="Action Bar"&&t.name!="Action Bar Unit Frame"&&t.name!="BuffIconRoot"&&t.name!="BuffTooltipHost")t.gameObject.SetActive(false);
            var bar=hud.GetComponentInChildren<BuffBarUI>(true); bar.enabled=false;
            var slots=bar.GetComponentsInChildren<BuffIconSlotUI>(true).Where(s=>s.gameObject.activeSelf).OrderBy(s=>s.name).ToArray();
            var view=hud.GetComponentInChildren<BuffTooltipUI>(true);
            var recovery=new BuffInstance(new BuffDefinition()); recovery.Tick(11.6f); slots[0].SetBuff(recovery);
            var slow=new BuffInstance(new BuffDefinition{buffId=PlayerBuffController.SlowBuffId,displayName="Slow",duration=6f,initialHealPercent=0f,healPercentPerTick=0f,moveSpeedMultiplier=.75f,isDebuff=true}); slow.Tick(2.2f); slots[1].SetBuff(slow);
            var ghost=ScriptableObject.CreateInstance<FlaskItemData>();ghost.Configure(FlaskKind.Ghost);flasks.Add(ghost);
            var effects=new FlaskActiveEffects();effects.Add("rolled-ghost",ghost,new FlaskStats(.41f,.73f,9f,20f),0f);
            var snapshots=new List<FlaskEffectSnapshot>();effects.GetActive(snapshots,3f);slots[2].SetFlask("flask_Ghost",snapshots[0]);
            Check(slots[2].TooltipEffect.Contains("41%")&&slots[2].TooltipEffect.Contains("73%")&&!slots[2].TooltipEffect.Contains("25%"),"flask description uses applied rolled values");
            slots[3].SetRadiance(3,20);slots[4].SetMapBuff("map_MaxHealth",MapBuffKind.MaxHealth,2,.24f);
            slots[5].SetMapBuff("map_Armor",MapBuffKind.Armor,3,137f);slots[6].SetMapBuff("map_MoveSpeed",MapBuffKind.MoveSpeed,9,.6f);
            Check(slots[4].TooltipEffect.Contains("24%")&&slots[4].TooltipEffect.Contains("2중첩"),"map buff shows actual total and stacks");
            Check(slots[5].TooltipEffect.Contains("137")&&!slots[5].TooltipEffect.Contains("%"),"armor is flat armor rather than a percentage");
            Check(slots[6].TooltipEffect.Contains("60%")&&slots[6].TooltipEffect.Contains("9중첩"),"capped map bonus does not multiply the cap by stack count");
            eventObject=new GameObject("BuffTooltipVerificationEvents",typeof(EventSystem));SceneManager.MoveGameObjectToScene(eventObject,scene);
            var events=eventObject.GetComponent<EventSystem>();var pointer=new PointerEventData(events);
            var raycaster=canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            Canvas.ForceUpdateCanvases();camera.Render();
            foreach(BuffIconSlotUI slot in slots)
            {
                pointer.position=RectTransformUtility.WorldToScreenPoint(camera,((RectTransform)slot.transform).TransformPoint(((RectTransform)slot.transform).rect.center));
                var hits=new List<RaycastResult>();raycaster.Raycast(pointer,hits);
                var hitGraphic=slot.GetComponent<UnityEngine.UI.Image>();
                Check(hits.Any(h=>h.gameObject==slot.gameObject),slot.name+" transparent hit area is raycastable "
                    +JsonConvert.SerializeObject(new{hitGraphic.enabled,hitGraphic.raycastTarget,hitGraphic.depth,cull=hitGraphic.canvasRenderer.cull,x=pointer.position.x,y=pointer.position.y,results=hits.Select(h=>h.gameObject.name).ToArray()}));
                ExecuteEvents.Execute(slot.gameObject,pointer,ExecuteEvents.pointerEnterHandler);
                Check(view.IsShown&&view.Owner==slot,slot.name+" actual pointer handler opens tooltip");
                ExecuteEvents.Execute(slot.gameObject,pointer,ExecuteEvents.pointerExitHandler);
                Check(!view.IsShown,slot.name+" pointer exit closes tooltip");
            }
            slots[0].OnPointerEnter(pointer);string first=view.GetComponentsInChildren<TMP_Text>(true).First(t=>t.name=="Remaining").text;
            recovery.Tick(.7f);slots[0].SetBuff(recovery);view.SendMessage("LateUpdate");
            Check(view.GetComponentsInChildren<TMP_Text>(true).First(t=>t.name=="Remaining").text!=first,"remaining time refreshes while hovered");
            slots[0].SetBuff(slow);view.SendMessage("LateUpdate");
            Check(view.IsShown&&view.GetComponentsInChildren<TMP_Text>(true).First(t=>t.name=="Name").text=="둔화","hovered slot reordering updates effect identity");
            slots[0].SetVisible(false);Check(!view.IsShown&&!slots[0].GetComponent<UnityEngine.UI.Image>().enabled,"expiration closes tooltip and removes empty hit area");
            slots[0].SetBuff(recovery);slots[0].OnPointerEnter(pointer);slots[0].enabled=false;view.SendMessage("LateUpdate");Check(!view.IsShown,"disabled slot closes tooltip");slots[0].enabled=true;
            foreach(FlaskKind kind in Enum.GetValues(typeof(FlaskKind)))
            { var data=ScriptableObject.CreateInstance<FlaskItemData>();data.Configure(kind);flasks.Add(data);Check(!string.IsNullOrEmpty(BuffTooltipText.FlaskEffectText(new FlaskEffectSnapshot("all-kinds",data,6,6))),kind+" flask has ongoing description"); }
            foreach(Vector2 size in new[]{new Vector2(1920,1080),new Vector2(1280,720),new Vector2(960,540)})
            {
                hudRect.anchorMin=hudRect.anchorMax=new Vector2(.5f,.5f);hudRect.sizeDelta=size;
                foreach(int i in new[]{0,1,2,4,5,6})
                {
                    slots[i].OnPointerEnter(pointer);view.SendMessage("LateUpdate");Canvas.ForceUpdateCanvases();
                    var panel=(RectTransform)view.transform.Find("Panel");Vector3 p=panel.localPosition;Rect bounds=((RectTransform)view.transform).rect;
                    Check(p.x>=bounds.xMin&&p.x+panel.rect.width<=bounds.xMax+.1f&&p.y>=bounds.yMin&&p.y+panel.rect.height<=bounds.yMax+.1f,"panel bounds / "+size+" / "+i);
                    var texts=view.GetComponentsInChildren<TMP_Text>(true);foreach(TMP_Text text in texts)text.ForceMeshUpdate(true,true);
                    Check(texts.All(t=>!t.isTextOverflowing),"text fit / "+size+" / "+i);
                }
            }
            hudRect.anchorMin=Vector2.zero;hudRect.anchorMax=Vector2.one;hudRect.offsetMin=hudRect.offsetMax=Vector2.zero;Canvas.ForceUpdateCanvases();
            // A world-space canvas is required for manual URP camera rendering in the preview scene.
            canvas.renderMode=RenderMode.WorldSpace;var canvasRect=(RectTransform)canvas.transform;
            canvasRect.sizeDelta=new Vector2(1920f,1080f);canvasRect.position=Vector3.zero;canvasRect.rotation=Quaternion.identity;canvasRect.localScale=Vector3.one*.005f;
            camera.orthographicSize=2.7f;Canvas.ForceUpdateCanvases();
            foreach(int i in new[]{0,1,2,4})
            {
                slots[i].OnPointerEnter(pointer);view.SendMessage("LateUpdate");
                foreach(TMP_Text text in hud.GetComponentsInChildren<TMP_Text>(true))text.ForceMeshUpdate(true,true);
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();
                File.WriteAllBytes(Path.Combine(Output,"native-"+i+".png"),texture.EncodeToPNG());
                var detail=new Texture2D(1000,390,TextureFormat.RGB24,false);
                try{detail.SetPixels(texture.GetPixels(440,0,1000,390));detail.Apply();File.WriteAllBytes(Path.Combine(Output,"native-detail-"+i+".png"),detail.EncodeToPNG());}
                finally{UnityEngine.Object.DestroyImmediate(detail);UnityEngine.Object.DestroyImmediate(texture);texture=null;}
            }
            bar.Refresh();Check(!view.IsShown&&slots.All(s=>string.IsNullOrEmpty(s.DisplayedKey)),"missing actor clears bar and active tooltip");
            File.WriteAllText(Path.Combine(Output,"native-checks.json"),JsonConvert.SerializeObject(new{status="PASS_NATIVE",checks,scope="saved production prefab, native pointer/raycast fixtures, actual effect snapshot values, Unity renders; not Game Play"},Formatting.Indented));
            return "PASS_NATIVE: "+checks.Count+" checks";
        }
        catch(Exception error){File.WriteAllText(Path.Combine(Output,"native-checks.json"),JsonConvert.SerializeObject(new{status="FAIL_NATIVE",checks,error=error.ToString()},Formatting.Indented));throw;}
        finally
        {
            RenderTexture.active=previousTarget;if(cameraObject!=null)cameraObject.GetComponent<Camera>().targetTexture=null;
            if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);
            foreach(var data in flasks)if(data!=null)UnityEngine.Object.DestroyImmediate(data);
            if(canvasObject!=null)UnityEngine.Object.DestroyImmediate(canvasObject);if(cameraObject!=null)UnityEngine.Object.DestroyImmediate(cameraObject);if(eventObject!=null)UnityEngine.Object.DestroyImmediate(eventObject);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
