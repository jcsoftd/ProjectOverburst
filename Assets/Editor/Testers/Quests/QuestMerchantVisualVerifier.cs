using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Persistence;
using Overburst.Quests;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class QuestMerchantVisualVerifier
{
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Idle editor required.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(QuestMerchantUIBuilder.Prefab);
        if (!prefab) throw new InvalidOperationException("Missing quest UI prefab.");
        string output = Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출/Implementation/20261011_QuestMerchant/Evidence/Visual-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(output);
        var scene = EditorSceneManager.NewPreviewScene(); RenderTexture target=null;
        try
        {
            var cameraRoot = new GameObject("Owned Quest UI Preview Camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraRoot,scene);
            var camera=cameraRoot.GetComponent<Camera>(); camera.scene=scene; camera.enabled=false;camera.cameraType=CameraType.Game;
            camera.orthographic=true;camera.orthographicSize=5; camera.transform.position=new Vector3(0,0,-20);
            camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.cullingMask=1<<5;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.065f,.07f,.06f);
            var canvasRoot=new GameObject("Owned Quest UI Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));SceneManager.MoveGameObjectToScene(canvasRoot,scene);canvasRoot.layer=5;
            var canvas=canvasRoot.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
            var scaler=canvasRoot.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var root=Object.Instantiate(prefab,canvasRoot.transform,false);var view=root.GetComponent<QuestMerchantUI>();
            foreach(var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=5;
            var windows=new[]{view.approach,view.merchantList,view.merchantDetail,view.journal,view.dialogue,view.reportPopup,view.tracker};
            var hunt=QuestCatalog.Find("quest.main-town.weapon.first-hunt");
            if(!hunt)throw new InvalidOperationException("First hunt missing.");
            var state=new AccountSnapshot();
            var detail=typeof(QuestMerchantUI).GetMethod("PresentDetail",BindingFlags.Instance|BindingFlags.NonPublic);
            foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)})
            {
                target=new RenderTexture(size.x,size.y,24);target.Create();camera.targetTexture=target;
                foreach(var window in windows)window.SetActive(false);
                view.merchantList.SetActive(true); view.merchantDetail.SetActive(true);
                view.merchantRowTemplate.gameObject.SetActive(true); view.merchantRowTemplate.Present(hunt.title,"야외 몬스터 처치 · 무기상인에게 보고","100 G  ·  경험치 30","수락 가능",true,true);
                var locked=Object.Instantiate(view.merchantRowTemplate,view.merchantRows,false);locked.name="Owned locked row";
                ((RectTransform)locked.transform).anchoredPosition=new Vector2(2,-132); locked.Present("미지의 지도","첫 사냥을 완료하면 받을 수 있습니다.","150 G  ·  경험치 40","선행 의뢰 필요",false,false);
                detail.Invoke(view,new object[]{view.merchantInfo,state,hunt.id,true});
                Save(camera,target,Path.Combine(output,"merchant-detail-"+size.x+"x"+size.y+".png"));
                Object.DestroyImmediate(locked.gameObject);
                foreach(var window in windows)window.SetActive(false);view.approach.SetActive(true);view.SelectOption(1);
                Save(camera,target,Path.Combine(output,"approach-"+size.x+"x"+size.y+".png"));
                foreach(var window in windows)window.SetActive(false);view.dialogue.SetActive(true);view.dialogueBody.text="마을 밖으로 나갈 생각인가?\n무기부터 점검하게. 밖에서는 자네를 지켜줄 사람이 없으니.";
                Save(camera,target,Path.Combine(output,"dialogue-"+size.x+"x"+size.y+".png"));
                foreach(var window in windows)window.SetActive(false);view.reportPopup.SetActive(true);view.popupMessage.text="첫 사냥의 목표를 달성했습니다.\n무기상인에게 보고하고 보상을 받으시겠습니까?";
                Save(camera,target,Path.Combine(output,"report-"+size.x+"x"+size.y+".png"));
                foreach(var window in windows)window.SetActive(false);view.journal.SetActive(true);
                state.quests.Add(new QuestSnapshot{questId=hunt.id,title=hunt.title,state=SavedQuestState.Active,description=hunt.description,journalDescription=hunt.journalDescription,rewardGold=100,rewardExperience=30,objectives=hunt.objectives.Select(o=>new QuestObjectiveSnapshot{kind=o.kind,targetId=o.targetId,label=o.label,required=o.required,current=3}).ToList()});
                detail.Invoke(view,new object[]{view.journalInfo,state,hunt.id,false});view.journalRowTemplate.gameObject.SetActive(true);view.journalRowTemplate.Present(hunt.title,"","","진행 중",true,true);
                Save(camera,target,Path.Combine(output,"journal-"+size.x+"x"+size.y+".png"));state.quests.Clear();
                state.quests.Clear();detail.Invoke(view,new object[]{view.journalInfo,state,null,false});
                view.journalRowTemplate.gameObject.SetActive(false);Save(camera,target,Path.Combine(output,"journal-empty-"+size.x+"x"+size.y+".png"));
                foreach(var window in windows)window.SetActive(false);view.merchantDetail.SetActive(true);
                var progress=new QuestSnapshot{questId=hunt.id,title=hunt.title,state=SavedQuestState.Active,description=string.Join("\n\n",Enumerable.Repeat(hunt.description,8)),journalDescription=hunt.journalDescription,rewardGold=100,rewardExperience=30,objectives=hunt.objectives.Select(o=>new QuestObjectiveSnapshot{kind=o.kind,targetId=o.targetId,label=o.label,required=o.required,current=o.required}).ToList()};state.quests.Add(progress);
                detail.Invoke(view,new object[]{view.merchantInfo,state,hunt.id,true});
                if(view.merchantInfo.scroll.content.rect.height<=view.merchantInfo.scroll.viewport.rect.height)throw new Exception("Long body does not scroll.");
                if(view.merchantInfo.primaryLabel.text!="의뢰 보고")throw new Exception("Ready action missing.");
                Save(camera,target,Path.Combine(output,"merchant-long-ready-"+size.x+"x"+size.y+".png"));
                progress.state=SavedQuestState.Completed;detail.Invoke(view,new object[]{view.merchantInfo,state,hunt.id,true});
                if(view.merchantInfo.primary.interactable)throw new Exception("Completed action still enabled.");
                view.merchantInfo.scroll.verticalNormalizedPosition=0;Save(camera,target,Path.Combine(output,"merchant-long-completed-"+size.x+"x"+size.y+".png"));state.quests.Clear();
                camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);target=null;
            }
            var missing=root.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            if(missing!=0)throw new InvalidOperationException("Missing scripts: "+missing);
            File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new {status="CAPTURED",missingScripts=missing,resolutions=new[]{"1920x1080","1280x720"},review="Screenshots require visual inspection; Play QA separate."},Formatting.Indented));
            return output;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(output,"failure.txt"),ex.ToString());throw;}
        finally {if(target){target.Release();Object.DestroyImmediate(target);}EditorSceneManager.ClosePreviewScene(scene);}
    }
    private static void Save(Camera camera,RenderTexture target,string path)
    {
        Canvas.ForceUpdateCanvases();
        // Each resolution starts from the same scroll state; completed stress shows the bottom.
        foreach(var scroll in camera.scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<ScrollRect>()))
            scroll.verticalNormalizedPosition=path.Contains("long-completed") ? 0 : 1;
        Canvas.ForceUpdateCanvases();camera.Render();var previous=RenderTexture.active;Texture2D image=null;
        try{RenderTexture.active=target;image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally{RenderTexture.active=previous;if(image)Object.DestroyImmediate(image);}
    }
}
