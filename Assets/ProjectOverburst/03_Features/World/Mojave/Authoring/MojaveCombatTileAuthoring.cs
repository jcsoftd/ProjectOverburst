#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using JBooth.MicroVerseCore;
using Overburst.Mojave;

// Keeps all eight source terrain layers when a height stamp is edited in MicroVerse.
public sealed class MojaveCombatTileAuthoring : MonoBehaviour
{
    public MicroVerse microVerse;
    public Terrain terrain;
    public TerrainData fullSurface;
    public MojavePatch patch;
    void OnEnable(){MicroVerse.OnFinishedUpdating.AddListener(QueueSurfaceRestore);}
    void OnDisable(){MicroVerse.OnFinishedUpdating.RemoveListener(QueueSurfaceRestore);EditorApplication.delayCall-=RestoreSurface;}
    void QueueSurfaceRestore(){if(microVerse!=null&&microVerse.enabled&&microVerse.explicitTerrains!=null&&microVerse.explicitTerrains.Contains(terrain)){EditorApplication.delayCall-=RestoreSurface;EditorApplication.delayCall+=RestoreSurface;}}
    void Guard(){if(terrain==null||microVerse==null||fullSurface==null||patch==null||terrain.terrainData==fullSurface||!AssetDatabase.GetAssetPath(terrain.terrainData).StartsWith("Assets/ProjectOverburst/04_Contents/World/Mojave/CombatTiles/",StringComparison.Ordinal))throw new InvalidOperationException("Select an independent combat tile working scene");}
    static Bounds ModelBounds(GameObject go){var rs=go.GetComponentsInChildren<Renderer>(true);if(rs.Length==0)return new Bounds(go.transform.position,Vector3.one);var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
    public void RestoreSurface(){if(this==null||terrain==null||fullSurface==null)return;Guard();var data=terrain.terrainData;data.SetAlphamaps(0,0,fullSurface.GetAlphamaps(0,0,fullSurface.alphamapWidth,fullSurface.alphamapHeight));for(int l=0;l<fullSurface.detailPrototypes.Length;l++)data.SetDetailLayer(0,0,l,fullSurface.GetDetailLayer(0,0,fullSurface.detailWidth,fullSurface.detailHeight,l));EditorUtility.SetDirty(data);}
    [ContextMenu("Bake height stamps and export combat tile")]
    public void Bake()
    {
        Guard();if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Wait for an idle Editor");
        bool enabledBefore=microVerse.enabled;
        try{microVerse.enabled=true;microVerse.Modify(true,true);microVerse.SaveBackToTerrain(true);RestoreSurface();Export();}
        finally{EditorApplication.delayCall-=RestoreSurface;microVerse.enabled=enabledBefore;}
    }
    [ContextMenu("Store edited surface paint")]
    public void StoreSurface(){Guard();var data=terrain.terrainData;fullSurface.SetAlphamaps(0,0,data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight));for(int l=0;l<data.detailPrototypes.Length;l++)fullSurface.SetDetailLayer(0,0,l,data.GetDetailLayer(0,0,data.detailWidth,data.detailHeight,l));EditorUtility.SetDirty(fullSurface);AssetDatabase.SaveAssetIfDirty(fullSurface);}
    public void Export()
    {
        Guard();var tile=terrain.transform.parent.gameObject;while(tile.transform.parent!=microVerse.transform)tile=tile.transform.parent.gameObject;
        var models=tile.transform.Find("Models");var old=patch.placements;var poses=new MojavePlacement[models.childCount];
        for(int i=0;i<models.childCount;i++){var model=models.GetChild(i);var b=ModelBounds(model.gameObject);var item=i<old.Length?old[i]:new MojavePlacement{kind=model.name.Contains("Rubble")?MojavePropKind.Rubble:model.name.Contains("Rock")?MojavePropKind.Boulder:b.size.y>1.5f?MojavePropKind.Tree:MojavePropKind.Plant};var p=model.position;
            if(i<old.Length)p.y=terrain.SampleHeight(p)+terrain.transform.position.y+old[i].position.y;model.position=p;
            item.position=new Vector3(p.x-patch.sourceFocus.x,p.y-terrain.SampleHeight(p)-terrain.transform.position.y,p.z-patch.sourceFocus.y);item.rotation=model.rotation;item.scale=model.localScale;item.radius=new Vector2(b.extents.x,b.extents.z).magnitude;item.height=b.size.y;poses[i]=item;
        }
        string path=AssetDatabase.GetAssetPath(patch.slicedPrefab);PrefabUtility.SaveAsPrefabAsset(tile,path);var savedModels=AssetDatabase.LoadAssetAtPath<GameObject>(path).transform.Find("Models");for(int i=0;i<poses.Length;i++){var p=poses[i];p.prefab=savedModels.GetChild(i).gameObject;poses[i]=p;}patch.placements=poses;
        var data=terrain.terrainData;float Height(Vector2 p)=>data.GetInterpolatedHeight(Mathf.Clamp01((p.x+patch.sourceFocus.x-terrain.transform.position.x)/data.size.x),Mathf.Clamp01((p.y+patch.sourceFocus.y-terrain.transform.position.z)/data.size.z));float baseline=Height(Vector2.zero);var alpha=data.GetAlphamaps(0,0,data.alphamapWidth,data.alphamapHeight);
        for(int z=0;z<patch.resolution;z++)for(int x=0;x<patch.resolution;x++){var p=new Vector2((x/(float)(patch.resolution-1)-.5f)*patch.size,(z/(float)(patch.resolution-1)-.5f)*patch.size);int cell=z*patch.resolution+x;patch.heights[cell]=Height(p)-baseline;float ax=Mathf.Clamp01((p.x+patch.sourceFocus.x-terrain.transform.position.x)/data.size.x)*(data.alphamapWidth-1),az=Mathf.Clamp01((p.y+patch.sourceFocus.y-terrain.transform.position.z)/data.size.z)*(data.alphamapHeight-1);int xi=Mathf.Min((int)ax,data.alphamapWidth-2),zi=Mathf.Min((int)az,data.alphamapHeight-2);float tx=ax-xi,tz=az-zi;
            for(int l=0;l<patch.layerCount;l++)patch.paint[cell*patch.layerCount+l]=Mathf.Lerp(Mathf.Lerp(alpha[zi,xi,l],alpha[zi,xi+1,l],tx),Mathf.Lerp(alpha[zi+1,xi,l],alpha[zi+1,xi+1,l],tx),tz);
        }
        EditorUtility.SetDirty(data);EditorUtility.SetDirty(patch);AssetDatabase.SaveAssetIfDirty(data);AssetDatabase.SaveAssetIfDirty(patch);UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }
}
#endif
