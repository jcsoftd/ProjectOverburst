using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class WorldLevelVisualVerifier
{
    public static string Run(string output)
    {
        Directory.CreateDirectory(output);var scene=EditorSceneManager.NewPreviewScene();RenderTexture target=null;var previous=RenderTexture.active;
        try
        {
            var cameraGo=new GameObject("Owned World Level Camera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraGo,scene);
            var camera=cameraGo.GetComponent<Camera>();camera.scene=scene;camera.enabled=false;camera.cameraType=CameraType.Game;
            camera.orthographic=true;camera.orthographicSize=5;camera.transform.position=new Vector3(0,0,-20);
            camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.cullingMask=1<<5;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.065f,.07f,.06f);
            var canvasGo=new GameObject("Owned World Level Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));SceneManager.MoveGameObjectToScene(canvasGo,scene);canvasGo.layer=5;
            var canvas=canvasGo.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
            var scaler=canvasGo.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(WorldLevelUIBuilder.Prefab),canvasGo.transform,false);
            root.GetComponent<Canvas>().overrideSorting=false;var view=root.GetComponent<WorldLevelUI>();
            foreach(var transform in root.GetComponentsInChildren<Transform>(true))transform.gameObject.layer=5;
            var state=new AccountSnapshot{level=35,unlockedWorldLevel=1,appliedWorldLevel=1,activeCharacterId="preview",characters=new System.Collections.Generic.List<AccountCharacterSnapshot>{new AccountCharacterSnapshot{id="preview",name="세린"}}};
            foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)})
            {
                target=new RenderTexture(size.x,size.y,24);target.Create();camera.targetTexture=target;
                Canvas.ForceUpdateCanvases();((RectTransform)root.transform).localScale=Vector3.one;
                state.unlockedWorldLevel=1;state.appliedWorldLevel=1;view.SetPreview(state,4);Save(camera,target,Path.Combine(output,"eligible-"+size.x+"x"+size.y+".png"));
                state.unlockedWorldLevel=4;view.SetPreview(state,4);Save(camera,target,Path.Combine(output,"unlocked-"+size.x+"x"+size.y+".png"));
                view.SetPreview(state,5);Save(camera,target,Path.Combine(output,"locked-"+size.x+"x"+size.y+".png"));
                state.appliedWorldLevel=4;view.SetPreview(state,4);Save(camera,target,Path.Combine(output,"applied-"+size.x+"x"+size.y+".png"));
                var modal=root.transform.Find("Confirmation");modal.gameObject.SetActive(true);
                Save(camera,target,Path.Combine(output,"confirmation-"+size.x+"x"+size.y+".png"));
                modal.gameObject.SetActive(false);camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);target=null;
            }
            int missing=root.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            if(missing!=0)throw new InvalidOperationException("Missing scripts");
            File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new{status="CAPTURED",missingScripts=missing,buttons=root.GetComponentsInChildren<Button>(true).Length,labels=root.GetComponentsInChildren<Text>(true).Length},Formatting.Indented));return output;
        }
        catch(Exception error){File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString());throw;}
        finally{RenderTexture.active=previous;if(target){target.Release();Object.DestroyImmediate(target);}EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void Save(Camera camera,RenderTexture target,string path)
    {
        Canvas.ForceUpdateCanvases();camera.Render();var previous=RenderTexture.active;Texture2D image=null;
        try{RenderTexture.active=target;image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally{RenderTexture.active=previous;if(image)Object.DestroyImmediate(image);}
    }
}
