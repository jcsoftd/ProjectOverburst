using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;

// Deterministic original/batch image comparison. Isolated temporary objects in Play only.
public static class ChainBatchVisualProbe
{
    static GameObject root;
    static object batch;
    static Type batchType;
    public static string Capture(string path, float age = 0f, bool varied = false)
    {
        if(!Application.isPlaying)throw new Exception("Play required");
        Cleanup();root=new GameObject("ChainBatchVisualProbe");
        Vector3 center=new Vector3(5000,0,5000);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("6a732a6fd9d7cfd409ddc5d0b20337a8"));
        var template=prefab.GetComponentInChildren<ChainElectricityLiteVfxController>(true);
        batchType=typeof(ChainElectricityBatchRenderer).GetNestedType("Batch",BindingFlags.NonPublic);
        batch=batchType.GetMethod("Create",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{template});
        if(batch==null)throw new Exception("GPU batch rejected source profile");
        for(int i=0;i<5;i++)
        {
            Vector3 from=center+new Vector3(-5,0,-2+i),to=center+new Vector3(-1,0,-1.5f+i);
            if(varied) to=from+(i==0?new Vector3(.25f,0,0):i==1?new Vector3(0,0,1.5f):i==2?new Vector3(2,0,1):i==3?new Vector3(3,1,0):new Vector3(3,0,-1));
            var g=UnityEngine.Object.Instantiate(prefab,(from+to)*.5f,Quaternion.LookRotation((to-from).normalized),root.transform);
            g.transform.localScale=Vector3.Scale(prefab.transform.localScale,new Vector3(1,1,(to-from).magnitude));
            var c=g.GetComponentInChildren<ChainElectricityLiteVfxController>(true);c.enabled=false;c.RestartVfx();
            uint state=(uint)(42+i);float flicker=Next(ref state)*Mathf.PI*2;
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(ChainElectricityLiteVfxController).GetField("randomState",flags).SetValue(c,state);
            typeof(ChainElectricityLiteVfxController).GetField("flickerPhase",flags).SetValue(c,flicker);
            typeof(ChainElectricityLiteVfxController).GetMethod("RebuildShape",flags).Invoke(c,null);
            for(int step=1;step<=Mathf.FloorToInt(age/template.ShapeRefreshInterval+.0001f);step++)
                typeof(ChainElectricityLiteVfxController).GetMethod("RebuildShape",flags).Invoke(c,null);
            float width=age<=0?1:Mathf.Max(.015f,(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.3f,1,age/template.Lifetime)))*(.92f+Mathf.Sin(age*96+flicker)*.08f));
            typeof(ChainElectricityLiteVfxController).GetMethod("ApplyWidths",flags).Invoke(c,new object[]{width});
            batchType.GetMethod("Add").Invoke(batch,new object[]{from+Vector3.right*6,to+Vector3.right*6,Time.time-age,(uint)(42+i)});
        }
        var cg=new GameObject("comparison camera");cg.transform.SetParent(root.transform);
        var camera=cg.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=5;
        camera.transform.position=center+new Vector3(0,12,-12);camera.transform.LookAt(center);
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.012f,.018f,.028f);camera.farClipPlane=100;
        var rt=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);camera.targetTexture=rt;
        for(int step=1;step<=Mathf.FloorToInt(age/template.ShapeRefreshInterval+.0001f);step++)
            batchType.GetMethod("Advance").Invoke(batch,new object[]{Time.time-age+step*template.ShapeRefreshInterval+.00001f});
        batchType.GetMethod("Draw").Invoke(batch,new object[]{Time.time});
        camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
        System.IO.File.WriteAllBytes(path,image.EncodeToPNG());RenderTexture.active=previous;camera.targetTexture=null;
        UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
        return "Left original 5 links / right GPU batch 5 links; matching seeds; age="+age+". "+path;
    }
    static float Next(ref uint state){state^=state<<13;state^=state>>17;state^=state<<5;return(state&0xffffffu)/16777216f;}
    public static void Cleanup()
    {
        if(batch is IDisposable disposable)disposable.Dispose();batch=null;
        if(root!=null)UnityEngine.Object.DestroyImmediate(root);root=null;
    }
}
