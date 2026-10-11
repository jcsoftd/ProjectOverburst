using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Keep runtime owners on their existing roots; author large views as nested project prefabs.
public static class UIPrefabStructureBuilder
{
    public const string PartsRoot="Assets/ProjectOverburst/02_Shared/UI/Prefabs/Views";
    public static readonly string[] SourceRoots={"Assets/ProjectOverburst/Resources/UI","Assets/ProjectOverburst/Resources/OverburstUI","Assets/ProjectOverburst/02_Shared/UI/Prefabs"};
    public static string[] OwnerPaths()=>AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/ProjectOverburst"}).Select(AssetDatabase.GUIDToAssetPath)
        .Where(p=>!p.StartsWith(PartsRoot+"/",StringComparison.Ordinal))
        .Where(p=>SourceRoots.Any(r=>p.StartsWith(r+"/",StringComparison.Ordinal))||File.ReadLines(p).Any(l=>l=="RectTransform:"||l=="Canvas:"))
        .OrderBy(p=>p,StringComparer.Ordinal).ToArray();

    [MenuItem("Overburst/UI/정식 UI/중첩 프리팹 정리/1 타이틀")]
    public static void ApplyTitle()=>ApplyRegion(TitleCharacterBuilder.PrefabPath);
    [MenuItem("Overburst/UI/정식 UI/중첩 프리팹 정리/2 공용 외모")]
    public static void ApplyAppearance()=>ApplyRegion(AppearanceCustomizationBuilder.PrefabPath);
    [MenuItem("Overburst/UI/정식 UI/중첩 프리팹 정리/3 ESC와 설정")]
    public static void ApplyMenu()=>ApplyRegion(OverburstGameMenuBuilder.PrefabPath);
    public static void ApplyRegion(string owner)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if(!Plan.Any(p=>p.owner==owner))throw new ArgumentException("Unknown UI owner.",nameof(owner));
        var scene=SceneManager.GetSceneByPath(OverburstUIWorkshopBuilder.ScenePath);
        if(scene.IsValid()&&scene.isDirty)throw new InvalidOperationException("UI management scene has unsaved changes.");
        string recovery=Path.GetFullPath("../개인파일/코덱스산출/UI/20261011_UIPrefabStructure/Recovery/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        foreach(var path in new[]{owner,OverburstUIWorkshopBuilder.ScenePath,AppearanceCustomizationBuilder.ShowcasePath}.Concat(Plan.Where(p=>p.owner==owner).Select(p=>PartsRoot+"/"+p.group+"/PF_"+p.name+".prefab")))
            foreach(var file in new[]{path,path+".meta"})if(File.Exists(file)){string backup=Path.Combine(recovery,file);Directory.CreateDirectory(Path.GetDirectoryName(backup));File.Copy(file,backup,false);}
        bool changed=ApplyOne(owner);
        // Nested conversion changes child file IDs; reauthor only the owned gallery group.
        if(changed)TitleCharacterManagementBuilder.Apply(true);
    }
    public static bool ApplyOne(string owner)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        var root=PrefabUtility.LoadPrefabContents(owner);
        try {bool changed=Organize(root,owner);if(changed)PrefabUtility.SaveAsPrefabAsset(root,owner);return changed;}
        finally {PrefabUtility.UnloadPrefabContents(root);}
    }
    public static bool Organize(GameObject root,string owner)
    {
        bool changed=false;
        foreach(var part in Plan.Where(p=>p.owner==owner))
        {
            var view=root.transform.Find(part.hierarchy);
            if(!view)throw new InvalidOperationException("UI view missing: "+owner+"/"+part.hierarchy);
            string asset=PartsRoot+"/"+part.group+"/PF_"+part.name+".prefab";
            if(PrefabUtility.IsAnyPrefabInstanceRoot(view.gameObject))
            {
                if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(view.gameObject)!=asset)throw new InvalidOperationException("Unexpected nested owner: "+part.hierarchy);
                continue;
            }
            Folder(Path.GetDirectoryName(asset).Replace('\\','/'));
            var source=PrefabUtility.SaveAsPrefabAsset(view.gameObject,asset);
            if(!source)throw new InvalidOperationException("UI view save failed: "+asset);
            PrefabUtility.ConvertToPrefabInstance(view.gameObject,source,new ConvertToPrefabInstanceSettings{
                objectMatchMode=ObjectMatchMode.ByHierarchy,componentsNotMatchedBecomesOverride=true,
                gameObjectsNotMatchedBecomesOverride=true,recordPropertyOverridesOfMatches=true,
                changeRootNameToAssetName=false,logInfo=false},InteractionMode.AutomatedAction);
            changed=true;
        }
        return changed;
    }
    static void Folder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }
    public readonly struct Part
    {
        public readonly string owner,hierarchy,group,name;
        public Part(string owner,string hierarchy,string group,string name){this.owner=owner;this.hierarchy=hierarchy;this.group=group;this.name=name;}
    }
    public static readonly Part[] Plan={
        new Part(TitleCharacterBuilder.PrefabPath,"Stage/Title Screen","Title","TitleScreen"),
        new Part(TitleCharacterBuilder.PrefabPath,"Stage/Characters Screen","Title","CharacterSelection"),
        new Part(TitleCharacterBuilder.PrefabPath,"Stage/Classes Screen","Title","ClassSelection"),
        new Part(TitleCharacterBuilder.PrefabPath,"Stage/Name Dialog","Title","CharacterNameDialog"),
        new Part(TitleCharacterBuilder.PrefabPath,"Stage/Quit Dialog","Title","TitleQuitDialog"),
        new Part(AppearanceCustomizationBuilder.PrefabPath,"Fullscreen Surface/Appearance Options","Appearance","AppearanceOptions"),
        new Part(AppearanceCustomizationBuilder.PrefabPath,"Fullscreen Surface/Body and Equipment","Appearance","BodyAndEquipment"),
        new Part(AppearanceCustomizationBuilder.PrefabPath,"Fullscreen Surface/Discard Confirmation","Appearance","AppearanceDiscardDialog"),
        new Part(OverburstGameMenuBuilder.PrefabPath,"Menu Frame/Scaled Content/Main Panel","Menu","PauseMenu"),
        new Part(OverburstGameMenuBuilder.PrefabPath,"Menu Frame/Scaled Content/Settings Window","Menu","SettingsWindow"),
        new Part(OverburstGameMenuBuilder.PrefabPath,"Menu Frame/Scaled Content/Key Prompt","Menu","SettingsKeyPrompt"),
        new Part(OverburstGameMenuBuilder.PrefabPath,"Menu Frame/Scaled Content/Confirm Modal","Menu","MenuConfirmDialog"),
    };
}
