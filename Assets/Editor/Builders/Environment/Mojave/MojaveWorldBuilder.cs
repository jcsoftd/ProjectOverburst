using System;
using System.IO;
using System.Linq;
using Overburst.Mojave;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
public static class MojaveWorldBuilder
{
    public const string OwnedRoot="Assets/ProjectOverburst/04_Contents/World/Mojave";
    public const string SceneRoot="Assets/ProjectOverburst/00_Scenes/World/Mojave";
    public const string ScenePath=SceneRoot+"/Mojave_Optimization_Final9.unity";
    public const string CatalogPath=OwnedRoot+"/CombatTiles/MojaveCombatCatalog.asset";
    public const string SizedLibraryPath=OwnedRoot+"/CombatTiles/SizedTiles/MojaveCombatSizeLibrary.asset";
    public const string BackdropLibraryPath=OwnedRoot+"/CombatTiles/Backdrops/NaturalSeparated/Data/MojaveSeparatedBackdropLibrary.asset";
    public const string PolishedLibraryPath=OwnedRoot+"/CombatTiles/Backdrops/Polished/MojaveBackdropLibrary_Polished.asset";
    public const string SourceRoot="Assets/ThirdParty/04_환경맵/BK/PureNature_Mojave";
    static readonly MojaveCombatKind[] Kinds={MojaveCombatKind.LongWash,MojaveCombatKind.BroadCourt,MojaveCombatKind.AsymmetricHollow,MojaveCombatKind.CrescentBasin,MojaveCombatKind.TwinClearing,MojaveCombatKind.LongWash,MojaveCombatKind.JunctionClearing,MojaveCombatKind.OpenBasin,MojaveCombatKind.CoverCourt,MojaveCombatKind.AsymmetricHollow,MojaveCombatKind.BroadCourt,MojaveCombatKind.OpenBasin};
    static readonly Vector2[] Radii={new Vector2(17,18),new Vector2(18,17),new Vector2(17,16),new Vector2(18,16),new Vector2(17,15),new Vector2(17,19),new Vector2(17,17),new Vector2(20,16),new Vector2(18,16),new Vector2(17,15),new Vector2(16,16),new Vector2(18,17)};
    static readonly float[][] Exits={new float[]{0,180,90},new float[]{15,155,280},new float[]{0,140,255},new float[]{20,165,270},new float[]{0,180,95},new float[]{0,180,305},new float[]{20,150,260},new float[]{0,135,255},new float[]{20,160,275},new float[]{0,150,270},new float[]{30,180,285},new float[]{15,140,255}};
    public static MojaveCombatTileSet.Rule[] Rules(MojaveCatalog catalog)=>catalog.patches.Select((p,i)=>new MojaveCombatTileSet.Rule{patch=p,kind=Kinds[i],radius=Radii[i],exits=Exits[i]}).ToArray();
    public static void ApplyEnvironment(Light sun = null,Material skybox=null,bool volcano=false)
    {
        RenderSettings.skybox=skybox!=null?skybox:AssetDatabase.LoadAssetAtPath<Material>(SourceRoot+"/Textures/Sky/SkyDay.mat");
        RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.56f,.59f,.62f);
        RenderSettings.ambientEquatorColor=new Color(.40f,.39f,.365f);RenderSettings.ambientGroundColor=new Color(.24f,.22f,.20f);RenderSettings.ambientIntensity=1;
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.0023f;RenderSettings.fogColor=new Color(.65f,.62f,.57f);
        if(volcano) {
            RenderSettings.ambientSkyColor=new Color(.55f,.60f,.67f);RenderSettings.ambientEquatorColor=new Color(.40f,.41f,.45f);RenderSettings.ambientGroundColor=new Color(.26f,.24f,.22f);
            RenderSettings.fogColor=new Color(.22f,.19f,.18f);RenderSettings.fogDensity=.0032f;
        }
        RenderSettings.sun=sun;
    }

    public static void PersistWaterMeshes(MojaveWorld world,string folder,string label)
    {
        if(!world.catalog.AuthoredSurface)return;Folder(folder);int index=0;
        foreach(var filter in world.generatedRoot.GetComponentsInChildren<MeshFilter>(true)) {
            if(filter.sharedMesh==null || !(filter.name.StartsWith("Oasis authored shore",StringComparison.Ordinal)||filter.name.StartsWith("Volcano authored lava",StringComparison.Ordinal)))continue;
            var copy=Object.Instantiate(filter.sharedMesh);copy.hideFlags=HideFlags.None;
            string path=AssetDatabase.GenerateUniqueAssetPath(folder+"/"+label+"_Water_"+(++index)+".asset");
            AssetDatabase.CreateAsset(copy,path);filter.sharedMesh=copy;
            var collider=filter.GetComponent<MeshCollider>();if(collider!=null)collider.sharedMesh=copy;
        }
        // The builder generated these meshes. A snapshot clone has no runtime mesh ownership.
        world.ReleaseWaterMeshes();
    }

    public static void Folder(string path) {if(AssetDatabase.IsValidFolder(path))return;var parent=path.Substring(0,path.LastIndexOf('/'));Folder(parent);AssetDatabase.CreateFolder(parent,path.Substring(path.LastIndexOf('/')+1));}
    public static void Guard() {
        if(!Application.dataPath.Replace('\\','/').EndsWith("/ProjectOverburst/Assets"))throw new InvalidOperationException("Use the ProjectOverburst Editor.");
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||BuildPipeline.isBuildingPlayer)throw new InvalidOperationException("Editor is busy.");
    }
}
