using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TMPro;

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
        BuildToggle();
        RemoveDebugPanelItems();
    }
    public static void BuildToggle()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)throw new InvalidOperationException("Idle Editor required");
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var old=root.transform.Find("Temporary_MomentToggles");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
            var ui=new GameObject("Temporary_MomentToggles",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            ui.transform.SetParent(root.transform,false);ui.layer=5;
            var canvas=ui.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=12062;
            var scaler=ui.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var font=Resources.Load<TMP_FontAsset>("UI/Fonts/ProjectMT/FontAssets/TMP_SpoqaHanSansNeo_Body");if(font==null)throw new InvalidOperationException("Project UI font missing");
            var parry=ToggleButton(ui.transform,"ParryToggle","패링 연출: 켜짐",font,-216);
            var heavy=ToggleButton(ui.transform,"HeavyToggle","완충 강공: 켜짐",font,-264);
            var fields=new SerializedObject(ui.AddComponent<CombatMomentPreviewToggle>());
            fields.FindProperty("parryButton").objectReferenceValue=parry;fields.FindProperty("heavyButton").objectReferenceValue=heavy;
            fields.FindProperty("parryCaption").objectReferenceValue=parry.GetComponentInChildren<TMP_Text>();fields.FindProperty("heavyCaption").objectReferenceValue=heavy.GetComponentInChildren<TMP_Text>();
            fields.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    private static Button ToggleButton(Transform parent,string name,string label,TMP_FontAsset font,float y)
    {
        var button=RunUiLayout.Button(parent,name,label,font,null,0,0,232,40,null);
        var rect=(RectTransform)button.transform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(16,y);
        button.navigation=new Navigation{mode=Navigation.Mode.None};button.targetGraphic.color=new Color(.20f,.31f,.27f,.94f);
        button.GetComponentInChildren<TMP_Text>().fontSize=19;return button;
    }
    public static void RemoveDebugPanelItems()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)throw new InvalidOperationException("Idle Editor required");
        var root=PrefabUtility.LoadPrefabContents(DebugHubPrefabBuilder.PrefabPath);
        try
        {
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
                if(t!=null && (t.name.StartsWith("Row presentation.moment.",StringComparison.Ordinal) ||
                    (t.name.StartsWith("Section ",StringComparison.Ordinal) && t.name.Contains("전투 순간 연출 · 임시 비교"))))UnityEngine.Object.DestroyImmediate(t.gameObject);
            var fields=new SerializedObject(root.GetComponent<Overburst.DebugTools.DebugHubView>());var ids=fields.FindProperty("itemIds");
            for(int i=ids.arraySize-1;i>=0;i--)if(ids.GetArrayElementAtIndex(i).stringValue.StartsWith("presentation.moment.",StringComparison.Ordinal))ids.DeleteArrayElementAtIndex(i);
            fields.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root,DebugHubPrefabBuilder.PrefabPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
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
