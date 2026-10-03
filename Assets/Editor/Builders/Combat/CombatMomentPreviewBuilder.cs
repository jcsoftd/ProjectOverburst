using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CombatMomentPreviewBuilder
{
    public const string PrefabPath="Assets/ProjectOverburst/Resources/Combat/VFX/PF_CombatMomentPreview.prefab";
    public const string MaterialPath="Assets/ProjectOverburst/Resources/Combat/VFX/CombatMomentGlow.mat";
    public const string ShaderPath="Assets/ProjectOverburst/05_Art/Shaders/Combat/CombatMomentGlow.shader";
    [MenuItem("OVERBURST/Combat/Build Temporary Moment Effects")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)throw new InvalidOperationException("Idle Editor required");
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);if(shader==null)throw new InvalidOperationException("Moment shader missing");
        Material material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,MaterialPath);}
        material.shader=shader;material.SetFloat("_Intensity",4f);EditorUtility.SetDirty(material);
        GameObject root=new GameObject("CombatMomentPreview");
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
        var hub = AssetDatabase.LoadAssetAtPath<GameObject>(DebugHubPrefabBuilder.PrefabPath);
        var view = hub != null ? hub.GetComponent<Overburst.DebugTools.DebugHubView>() : null;
        string[] ids = { "presentation.moment.parry", "presentation.moment.heavy", "presentation.moment.screen", "presentation.moment.blade", "presentation.moment.local" };
        if (view == null || view.ItemIds == null || !ids.All(id => view.ItemIds.Contains(id)))
            DebugHubPrefabBuilder.Build();
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
