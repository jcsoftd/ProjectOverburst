using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>원본 재질과 파티클을 사용하는 숫자 조절 미리보기. 열린 씬은 저장하지 않는다.</summary>
public sealed class GroundIndicatorWindow : EditorWindow
{
    private PreviewRenderUtility preview;
    private GameObject instance;
    private ProceduralGroundIndicator indicator;
    private GroundIndicatorShape shape;
    private float outer=4,inner=.72f,angle=80,width=1,length=4,flame=.12f,progress=.72f;
    private bool top;
    [MenuItem("JC Tool/VFX/인디케이터/숫자 조절 미리보기")]
    public static void Open()=>GetWindow<GroundIndicatorWindow>("인디케이터");
    private void OnGUI()
    {
        if(!EnsurePreview())
        {
            EditorGUILayout.HelpBox("기본 프리팹을 생성하면 원본 효과로 미리 볼 수 있습니다.",MessageType.Info);
            if(GUILayout.Button("기본 프리팹 생성"))GroundIndicatorBuilder.Build();return;
        }
        EditorGUILayout.LabelField("범위 · 미터 단위",EditorStyles.boldLabel);
        shape=(GroundIndicatorShape)EditorGUILayout.EnumPopup("형태",shape);
        if(shape==GroundIndicatorShape.Rectangle)
        {
            width=EditorGUILayout.FloatField("폭 (m)",width);length=EditorGUILayout.FloatField("길이 (m)",length);
        }
        else
        {
            outer=EditorGUILayout.FloatField("바깥 반경 (m)",outer);
            if(shape!=GroundIndicatorShape.Circle)inner=EditorGUILayout.FloatField("안쪽 반경 (m)",inner);
            if(shape==GroundIndicatorShape.Sector)angle=EditorGUILayout.Slider("각도 (°)",angle,1,360);
        }
        flame=EditorGUILayout.FloatField("불꽃 테두리 폭 (m)",flame);
        progress=EditorGUILayout.Slider("예고 진행률",progress,0,1);
        top=EditorGUILayout.Toggle("위에서 보기",top);
        indicator.Configure(shape,outer,inner,angle,width,length);
        indicator.SetFlameWidth(flame);indicator.SetProgress(progress);
        outer=indicator.OuterRadius;inner=indicator.InnerRadius;angle=indicator.Angle;flame=indicator.FlameWidth;
        Rect rect=GUILayoutUtility.GetRect(100,100,200,500,GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true));
        if(Event.current.type==EventType.Repaint&&rect.width>1&&rect.height>1)
        {
            preview.BeginPreview(rect,GUIStyle.none);
            float extent=Mathf.Max(outer,shape==GroundIndicatorShape.Rectangle?length:0,.1f);
            Vector3 center=shape==GroundIndicatorShape.Rectangle?Vector3.forward*length*.5f:Vector3.zero;
            var camera=preview.camera;camera.orthographic=true;camera.orthographicSize=extent*1.15f;
            camera.transform.position=center+(top?new Vector3(0,10,.001f):new Vector3(0,8,6)).normalized*extent*4;
            camera.transform.LookAt(center);camera.nearClipPlane=.01f;camera.farClipPlane=extent*10+20;
            camera.clearFlags=CameraClearFlags.Color;camera.backgroundColor=new Color(.04f,.045f,.055f);
            preview.Render(true);GUI.DrawTexture(rect,preview.EndPreview(),ScaleMode.StretchToFill,false);
        }
        if(GUILayout.Button("현재 수치로 새 프리팹 저장"))
        {
            GroundIndicatorBuilder.RequireIdle();
            string path=EditorUtility.SaveFilePanelInProject("인디케이터 저장","PF_Indicator_Custom","prefab","저장할 이름을 입력하세요.",GroundIndicatorBuilder.Root);
            if(!string.IsNullOrEmpty(path))
            {
                SavePreviewPrefab(instance,path);
            }
        }
    }
    public static void SavePreviewPrefab(GameObject root,string path)
    {
        GroundIndicatorBuilder.RequireIdle();
        var indicator = root.GetComponent<ProceduralGroundIndicator>();
        if (indicator == null || !indicator.UsesApprovedDesign) throw new System.InvalidOperationException("Approved source references missing.");
        float progress = indicator.Progress;
        try
        {
            indicator.ReleaseRuntime();
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            if (!ok || saved == null) throw new System.InvalidOperationException("Indicator save failed: " + path);
        }
        finally { indicator.Refresh(); indicator.SetProgress(progress); }
    }
    private bool EnsurePreview()
    {
        if(preview!=null&&indicator!=null)return true;
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(GroundIndicatorBuilder.PrefabPath);
        if(prefab==null)return false;
        preview=new PreviewRenderUtility();instance=Instantiate(prefab);instance.hideFlags=HideFlags.HideAndDontSave;
        preview.AddSingleGO(instance);indicator=instance.GetComponent<ProceduralGroundIndicator>();if(indicator!=null)indicator.SetVisible(true);return indicator!=null;
    }
    private void OnDisable()
    {
        try{if(preview!=null)preview.Cleanup();}
        finally{preview=null;instance=null;indicator=null;}
    }
}
