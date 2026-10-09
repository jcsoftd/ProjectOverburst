using System;
using System.Collections.Generic;
using System.Linq;
using Overburst.Mojave;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

[Overlay(typeof(SceneView),"전투 맵 영역",true)]
public sealed class MojaveSceneOverlayPanel : Overlay
{
    public override VisualElement CreatePanelContent()
    {
        var root=new VisualElement();root.style.paddingLeft=8;root.style.paddingRight=8;root.style.paddingTop=5;root.style.paddingBottom=5;
        var regions=new Toggle("전투지역 / 길") {value=MojaveSceneOverlay.Regions,name="scene-regions"};
        var tiles=new Toggle("타일 경계 / 이름") {value=MojaveSceneOverlay.Tiles,name="scene-tiles"};
        regions.RegisterValueChangedCallback(e=>MojaveSceneOverlay.SetOptions(e.newValue,MojaveSceneOverlay.Tiles));
        tiles.RegisterValueChangedCallback(e=>MojaveSceneOverlay.SetOptions(MojaveSceneOverlay.Regions,e.newValue));
        root.Add(regions);root.Add(tiles);root.Add(new Label("열린 Mojave / Oasis / Volcano 씬에 표시"));
        root.schedule.Execute(()=> {regions.SetValueWithoutNotify(MojaveSceneOverlay.Regions);tiles.SetValueWithoutNotify(MojaveSceneOverlay.Tiles);}).Every(300);
        return root;
    }
}

/// <summary>Editor-only annotation of the currently open world; no overlay objects are saved.</summary>
[InitializeOnLoad]
public static class MojaveSceneOverlay
{
    const string RegionsKey="Mojave.SceneOverlay.Regions",TilesKey="Mojave.SceneOverlay.Tiles";
    static MojaveWorld cachedWorld;
    static int cachedSeed,cachedSurface;
    static Mesh mesh;
    static Material material;
    static GUIStyle labelStyle;
    public static bool Regions=>SessionState.GetBool(RegionsKey,false);
    public static bool Tiles=>SessionState.GetBool(TilesKey,false);
    public static Mesh CachedMesh=>mesh;

    static MojaveSceneOverlay()
    {
        SceneView.duringSceneGui+=Draw;
        AssemblyReloadEvents.beforeAssemblyReload+=Release;
        EditorApplication.quitting+=Release;
        EditorSceneManager.sceneClosed+=_=>Release();
        EditorSceneManager.activeSceneChangedInEditMode+=(_,__)=>Release();
        EditorApplication.playModeStateChanged+=_=>Release();
    }
    public static void SetOptions(bool regions,bool tiles)
    {
        SessionState.SetBool(RegionsKey,regions);SessionState.SetBool(TilesKey,tiles);
        if(!regions)Release();SceneView.RepaintAll();
    }
    public static MojaveWorld CurrentWorld()=>Object.FindObjectsByType<MojaveWorld>(FindObjectsSortMode.None)
        .FirstOrDefault(w=>w.gameObject.scene==SceneManager.GetActiveScene()&&!EditorSceneManager.IsPreviewSceneObject(w)&&w.surface!=null);
    public static Vector3[][] TileOutlines(MojaveWorld world)
    {
        world.EnsureLayout();var result=new List<Vector3[]>();
        foreach(var place in world.layout.places) {
            float half=world.catalog.patches[place.patch].size*.5f;
            var corners=new[]{new Vector2(-half,-half),new Vector2(half,-half),new Vector2(half,half),new Vector2(-half,half)};
            var line=new List<Vector3>();
            for(int side=0;side<4;side++)for(int i=0;i<32;i++)line.Add(world.Ground(place.World(Vector2.Lerp(corners[side],corners[(side+1)%4],i/32f)),.15f));
            line.Add(line[0]);result.Add(line.ToArray());
        }
        return result.ToArray();
    }
    static void EnsureMesh(MojaveWorld world,Camera camera)
    {
        int surfaceId=world.surface.terrainData.GetInstanceID();
        if(mesh!=null&&cachedWorld==world&&cachedSeed==world.seed&&cachedSurface==surfaceId)return;
        Release();var geometry=new MojaveGameplayOverlayElement();geometry.Bind(world,camera);mesh=geometry.CreateWorldMesh(.3f);
        var shader=Shader.Find("Hidden/Internal-Colored");if(shader==null)throw new InvalidOperationException("Editor overlay shader unavailable.");
        material=new Material(shader) {name="Mojave Scene overlay",hideFlags=HideFlags.HideAndDontSave};
        material.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);material.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_Cull",(int)CullMode.Off);material.SetInt("_ZWrite",0);material.SetInt("_ZTest",(int)CompareFunction.Always);
        cachedWorld=world;cachedSeed=world.seed;cachedSurface=surfaceId;
    }
    static void Draw(SceneView view)
    {
        if(Event.current.type!=EventType.Repaint||(!Regions&&!Tiles)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        var world=CurrentWorld();if(world==null){Release();return;}world.EnsureLayout();
        if(Regions) {
            EnsureMesh(world,view.camera);
            // SceneView supplies a clipped camera viewport; preserve its native handle projection.
            Handles.SetCamera(view.camera);
            if(material.SetPass(0))Graphics.DrawMeshNow(mesh,Matrix4x4.identity);
        }
        var depth=Handles.zTest;var tint=Handles.color;
        try {
            Handles.zTest=CompareFunction.Always;
            for(int i=0;i<world.layout.places.Count;i++) {
                var place=world.layout.places[i];
                if(Regions) {Handles.color=MojaveCombatPalette.For(place.kind);DrawLabel(world.Ground(place.center,.4f),$"{i+1}  {place.KindLabel}",Handles.color);}
                if(Tiles) {
                    float half=world.catalog.patches[place.patch].size*.5f;
                    Handles.color=new Color(1,.89f,.58f,1);
                    DrawLabel(world.Ground(place.World(new Vector2(-half,half)),.3f),$"T{i+1}  {world.catalog.patches[place.patch].name}",Handles.color);
                }
            }
            if(Tiles) {Handles.color=new Color(1,.89f,.58f,.95f);foreach(var outline in TileOutlines(world))Handles.DrawAAPolyLine(2,outline);}
        } finally {Handles.zTest=depth;Handles.color=tint;}
    }
    static void DrawLabel(Vector3 position,string text,Color color)
    {
        if(labelStyle==null)labelStyle=new GUIStyle(EditorStyles.helpBox) {fontSize=12,fontStyle=FontStyle.Bold,padding=new RectOffset(5,5,3,3)};
        labelStyle.normal.textColor=color;Handles.Label(position,text,labelStyle);
    }
    public static void Release()
    {
        if(mesh!=null)Object.DestroyImmediate(mesh);if(material!=null)Object.DestroyImmediate(material);
        mesh=null;material=null;cachedWorld=null;
    }
}
