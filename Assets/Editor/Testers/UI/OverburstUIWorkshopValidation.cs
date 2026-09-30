using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
public static class OverburstUIWorkshopValidation {
 static string Output => Path.GetFullPath("../개인파일/코덱스산출/UI/20260923_UIRefinement");
 public static string Run() {
  if(!Application.isPlaying) throw new Exception("Play required");
  var w=UnityEngine.Object.FindFirstObjectByType<OverburstUIWorkshop>();
  var results=new List<string>();
  Action<bool,string> check=(ok,name)=>{results.Add((ok?"PASS ":"FAIL ")+name);};
  w.ShowComparison(); Canvas.ForceUpdateCanvases();
  var grid=w.transform.Find("PF_OverburstHUD_Rpg11/Action Bar/Main Slots/Grid");
  var slots=grid.Cast<Transform>().Where(t=>t.gameObject.activeSelf).ToArray();
  check(slots.Length==10 && slots.All(t=>((RectTransform)t).rect.size==new Vector2(84,84)),"10 shared square slots 84x84");
  check(string.Join("",slots.Select(t=>t.Find("Hotkey/Hotkey Text").GetComponent<Text>().text))=="1234567890","keys 1234567890");
  check(w.Equipment.GetComponentsInChildren<Text>(true).Any(t=>t.text=="벨트"),"belt label");
  foreach(var window in new[]{w.Inventory,w.Equipment,w.Stash}) {
   window.Show(); Canvas.ForceUpdateCanvases();
   var header=window.transform.Find("Header").gameObject;
   var canvas=window.GetComponentInParent<Canvas>(); var rect=(RectTransform)window.transform;
   var pos=RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,header.transform.position);
   var data=new PointerEventData(EventSystem.current){position=pos,button=PointerEventData.InputButton.Left,pointerPressRaycast=new RaycastResult{module=canvas.GetComponent<GraphicRaycaster>()}};
   var before=rect.anchoredPosition;
   ExecuteEvents.ExecuteHierarchy(window.gameObject,data,ExecuteEvents.pointerDownHandler);
   ExecuteEvents.Execute(header,data,ExecuteEvents.beginDragHandler);
   data.position+=new Vector2(60,30);
   ExecuteEvents.Execute(header,data,ExecuteEvents.dragHandler); ExecuteEvents.Execute(header,data,ExecuteEvents.endDragHandler);
   check(Vector2.Distance(before,rect.anchoredPosition)>10,window.name+" header drag");
   check(window.transform.GetSiblingIndex()==window.transform.parent.childCount-1,window.name+" focus");
   for(int i=0;i<3;i++){window.transform.Find("Header/Button (Close)").GetComponent<Button>().onClick.Invoke();check(!window.gameObject.activeSelf,window.name+" close cycle "+i);window.Show();}
   window.ResetPosition();
  }
  w.ShowStash();var preview=w.Stash.GetComponent<OverburstUIStashPreview>();
  w.Stash.transform.Find("Tab Menu/Buttons Group/Tab Button (3)").GetComponent<Button>().onClick.Invoke();check(preview.SelectedTab==2,"stash third tab");preview.First();
  var roots=UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
  check(roots.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))==0,"scene missing scripts 0");
  check(!EditorBuildSettings.scenes.Any(s=>s.path==OverburstUIWorkshopBuilder.ScenePath),"workshop excluded from build");
  var guids=AssetDatabase.FindAssets("t:Prefab",new[]{OverburstUIWorkshopBuilder.Root});
  check(guids.Length==10,"ten project prefabs including tooltip");
  foreach(var guid in guids){var go=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)); check(go.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))==0,go.name+" missing scripts 0");}
  File.WriteAllLines(Path.Combine(Output,"validation.txt"),results);
  w.ShowComparison();return string.Join("\n",results);
 }
 public static string Capture(string mode) {
  var w=UnityEngine.Object.FindFirstObjectByType<OverburstUIWorkshop>();
  switch(mode){case "hud":w.ShowHud();break;case "stash":w.ShowStash();break;case "enemies":w.ShowEnemies();break;case "grades":w.ShowGrades();break;case "notifications":w.ShowNotifications();break;default:w.ShowComparison();break;}
  Canvas.ForceUpdateCanvases();var cam=GameObject.Find("UI Preview Camera").GetComponent<Camera>();
  var rt=new RenderTexture(1920,1080,24);var old=cam.targetTexture;var active=RenderTexture.active;
  try{cam.targetTexture=rt;Canvas.ForceUpdateCanvases();cam.Render();RenderTexture.active=rt;var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();var path=Path.Combine(Output,"final-"+mode+".png");File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);return path;}finally{cam.targetTexture=old;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(rt);}
 }
}
