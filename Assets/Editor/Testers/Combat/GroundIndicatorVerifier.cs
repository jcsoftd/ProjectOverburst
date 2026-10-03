using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class GroundIndicatorVerifier
{
    public static object Verify()
    {
        GroundIndicatorBuilder.RequireIdle();var checks=new List<object>();
        var scene=EditorSceneManager.NewPreviewScene();
        var original=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(GroundIndicatorBuilder.SourceGuid));
        var sourceMaterials=original.GetComponentsInChildren<ParticleSystemRenderer>(true).Select(r=>r.sharedMaterial).Where(m=>m!=null).ToArray();
        try
        {
            foreach(GroundIndicatorShape kind in Enum.GetValues(typeof(GroundIndicatorShape)))
            {
                string path=GroundIndicatorBuilder.Root+"/PF_Indicator_"+kind+".prefab";
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(prefab==null)throw new InvalidOperationException("Missing prefab: "+path);
                var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
                try
                {
                    var indicator=root.GetComponent<ProceduralGroundIndicator>();
                    if(indicator==null||indicator.Surface==null||indicator.Border==null)throw new InvalidOperationException("Missing original layers.");
                    if(root.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))!=0)throw new InvalidOperationException("Missing script.");
                    foreach(var r in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    {
                        if(r.sharedMaterial==null)continue;
                        if(!sourceMaterials.Contains(r.sharedMaterial))throw new InvalidOperationException("Original material changed.");
                        if(ShaderUtil.ShaderHasError(r.sharedMaterial.shader))throw new InvalidOperationException("Original shader error.");
                    }
                    indicator.Configure(kind,4,1,80,2,4);Mesh mesh=indicator.Surface.mesh;
                    Material material=indicator.Surface.sharedMaterial;
                    foreach(float radius in new[]{.25f,1f,4f,12f,50f})
                    {
                        indicator.Configure(kind,radius,radius*.25f,80,radius*.5f,radius);
                        if(indicator.OuterRadius!=radius||mesh!=indicator.Surface.mesh||material!=indicator.Surface.sharedMaterial)throw new InvalidOperationException("Resize replaced material or owned mesh.");
                        if(mesh.vertices.Any(v=>float.IsNaN(v.x)||float.IsInfinity(v.y)))throw new InvalidOperationException("Non-finite vertex.");
                    }
                    indicator.Configure(kind,4,999,-20);
                    if(indicator.InnerRadius>=indicator.OuterRadius||indicator.Angle!=1)throw new InvalidOperationException("Invalid geometry accepted.");
                    indicator.Configure(kind,float.NaN,float.PositiveInfinity,float.NaN);
                    if(float.IsNaN(indicator.OuterRadius)||float.IsInfinity(indicator.InnerRadius))throw new InvalidOperationException("Non-finite geometry.");
                    indicator.Configure(kind,4,1,80,2,4);
                    for(int n=0;n<3;n++)
                    {
                        indicator.SetVisible(false);if(indicator.IsVisible)throw new InvalidOperationException("Hide failed.");
                        indicator.SetVisible(true);indicator.SetProgress(.72f);
                        if(!indicator.IsVisible||root.GetComponentsInChildren<ParticleSystem>().Sum(p=>p.particleCount)==0)throw new InvalidOperationException("Show simulation failed.");
                        root.SetActive(false);root.SetActive(true);indicator.SetProgress(.72f);
                    }
                    checks.Add(new{shape=kind.ToString(),prefab=path,guid=AssetDatabase.AssetPathToGUID(path),missingScripts=0,radii=5,reentry=3,vertices=mesh.vertexCount,originalMaterials="PASS"});
                }
                finally{Object.DestroyImmediate(root);}
            }
            return new{status="PASS",checks};
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
}
