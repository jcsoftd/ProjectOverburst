using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TMPro;

public static class CombatMomentPresentationBuilder
{
    public const string PrefabPath="Assets/ProjectOverburst/Resources/Combat/VFX/PF_CombatMomentPresentation.prefab";
    public const string MaterialPath="Assets/ProjectOverburst/Resources/Combat/VFX/CombatMomentGlow.mat";
    public const string ShaderPath="Assets/ProjectOverburst/05_Art/Shaders/Combat/CombatMomentGlow.shader";
    [MenuItem("OVERBURST/Combat/Build Combat Moment Effects")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)throw new InvalidOperationException("Idle Editor required");
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);if(shader==null)throw new InvalidOperationException("Moment shader missing");
        Material material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,MaterialPath);}
        material.shader=shader;material.SetFloat("_Intensity",4f);EditorUtility.SetDirty(material);
        GameObject root=new GameObject("CombatMomentPresentation");
        try
        {
            var component=root.AddComponent<CombatMomentPresentation>();var so=new SerializedObject(component);
            so.FindProperty("bladeGlow").objectReferenceValue=Line(root.transform,"BladeGlow",material,.035f,2);
            so.FindProperty("bladeSweep").objectReferenceValue=Line(root.transform,"BladeSweep",material,.025f,2);
            so.FindProperty("impactCore").objectReferenceValue=Line(root.transform,"PrimaryImpact",material,.045f,28);
            var contacts=so.FindProperty("contacts");contacts.arraySize=4;
            for(int i=0;i<4;i++)
            {
                var parent=new GameObject("Contact"+i).transform;parent.SetParent(root.transform,false);var item=contacts.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("core").objectReferenceValue=Line(parent,"Core",material,.035f,2);
                item.FindPropertyRelative("cross").objectReferenceValue=Line(parent,"Cross",material,.025f,2);
                var sparks=item.FindPropertyRelative("sparks");sparks.arraySize=10;
                for(int j=0;j<10;j++)sparks.GetArrayElementAtIndex(j).objectReferenceValue=Line(parent,"Spark"+j,material,.014f,2);
            }
            so.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            AssetDatabase.SaveAssets();
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
    public static void UpgradePreviewAssets()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)throw new InvalidOperationException("Idle Editor required");
        const string oldMoment="Assets/ProjectOverburst/Resources/Combat/VFX/PF_CombatMomentPreview.prefab";
        if(AssetDatabase.LoadAssetAtPath<GameObject>(oldMoment)==null && AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/Resources/Debug/PF_OverburstMotionBlurToggle.prefab")==null)
        {
            OverburstEdgeBlurBuilder.BuildPrefab();OverburstGameMenuBuilder.AddPresentationSettings();return;
        }
        if(AssetDatabase.LoadAssetAtPath<GameObject>(oldMoment)!=null){string error=AssetDatabase.MoveAsset(oldMoment,PrefabPath);if(error.Length>0)throw new InvalidOperationException(error);}
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var temporary=root.transform.Find("Temporary_MomentToggles");if(temporary!=null)UnityEngine.Object.DestroyImmediate(temporary.gameObject);
            root.name="CombatMomentPresentation";PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        const string folder="Assets/ProjectOverburst/Resources/Camera";
        if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/ProjectOverburst/Resources","Camera");
        const string oldMotion="Assets/ProjectOverburst/Resources/Debug/PF_OverburstMotionBlurToggle.prefab";
        const string motionPath=folder+"/PF_OverburstMotionBlur.prefab";
        if(AssetDatabase.LoadAssetAtPath<GameObject>(oldMotion)!=null){string error=AssetDatabase.MoveAsset(oldMotion,motionPath);if(error.Length>0)throw new InvalidOperationException(error);}
        root=PrefabUtility.LoadPrefabContents(motionPath);
        try
        {
            for(int i=root.transform.childCount-1;i>=0;i--)UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            foreach(var component in root.GetComponents<UnityEngine.UI.GraphicRaycaster>())UnityEngine.Object.DestroyImmediate(component);
            foreach(var component in root.GetComponents<UnityEngine.UI.CanvasScaler>())UnityEngine.Object.DestroyImmediate(component);
            foreach(var component in root.GetComponents<Canvas>())UnityEngine.Object.DestroyImmediate(component);
            if(root.GetComponent<OverburstMotionBlur>()==null)throw new InvalidOperationException("Motion blur script GUID binding lost");
            var host=new GameObject("MotionBlurVolume",typeof(UnityEngine.Rendering.Volume));host.transform.SetParent(root.transform,false);
            var volume=host.GetComponent<UnityEngine.Rendering.Volume>();volume.isGlobal=true;volume.priority=10000;
            root.name="OverburstMotionBlur";PrefabUtility.SaveAsPrefabAsset(root,motionPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        OverburstEdgeBlurBuilder.BuildPrefab();OverburstGameMenuBuilder.AddPresentationSettings();
    }

    private static LineRenderer Line(Transform parent,string name,Material material,float width,int points)
    {
        var line=new GameObject(name).AddComponent<LineRenderer>();line.transform.SetParent(parent,false);
        line.useWorldSpace=true;line.positionCount=points;line.sharedMaterial=material;
        line.startWidth=line.endWidth=1f;line.widthMultiplier=width;line.numCapVertices=2;line.numCornerVertices=2;
        line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.lightProbeUsage=LightProbeUsage.Off;
        line.reflectionProbeUsage=ReflectionProbeUsage.Off;line.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
        line.startColor=line.endColor=Color.clear;line.enabled=false;return line;
    }
}
