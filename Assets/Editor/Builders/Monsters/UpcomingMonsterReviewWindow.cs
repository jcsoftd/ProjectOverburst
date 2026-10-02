using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Owns an isolated visual preview; never samples a saved scene or a combat Animator.</summary>
public sealed class UpcomingMonsterReviewPreview : IDisposable
{
    Scene scene;
    public GameObject Model {get;private set;}
    Animator animator;
    Camera camera;
    PlayableGraph graph;
    AnimationClipPlayable playable;
    RenderTexture texture;
    Vector3 position,scale;
    Quaternion rotation;
    Vector3 viewCenter;float viewRadius;
    public AnimationClip Clip {get;private set;}
    public float Time {get;private set;}
    public UpcomingMonsterReviewPreview(UpcomingMonsterReviewStation source)
    {
        try
        {
            scene=EditorSceneManager.NewPreviewScene();
            Model=Object.Instantiate(source.model);SceneManager.MoveGameObjectToScene(Model,scene);
            Model.name="v3 모션 미리보기";Model.hideFlags=HideFlags.HideAndDontSave;
            Model.transform.position=source.model.transform.position-source.transform.position;
            Model.transform.rotation=source.model.transform.rotation;Model.transform.localScale=source.model.transform.lossyScale;
            foreach(var t in Model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            foreach(var b in Model.GetComponentsInChildren<MonoBehaviour>(true))if(b!=null)b.enabled=false;
            foreach(var a in Model.GetComponentsInChildren<Animation>(true))a.enabled=false;
            foreach(var a in Model.GetComponentsInChildren<Animator>(true))a.enabled=false;
            animator=Model.GetComponentInChildren<Animator>(true)??Model.AddComponent<Animator>();
            position=animator.transform.localPosition;rotation=animator.transform.localRotation;scale=animator.transform.localScale;
            animator.enabled=true;animator.applyRootMotion=false;animator.fireEvents=false;animator.runtimeAnimatorController=null;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            foreach(var s in Model.GetComponentsInChildren<SkinnedMeshRenderer>(true))s.updateWhenOffscreen=true;
            var g=new GameObject("v3 PreviewCamera");SceneManager.MoveGameObjectToScene(g,scene);camera=g.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
            viewRadius=Mathf.Max(.7f,source.displaySize.magnitude*.65f);viewCenter=new Vector3(0,source.displaySize.y*.5f,0);
            SetView(0,1);
            camera.fieldOfView=32;camera.nearClipPlane=.01f;camera.farClipPlane=500;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.09f,.12f,.15f);camera.cullingMask=1<<31;
            var data=camera.GetUniversalAdditionalCameraData();data.volumeLayerMask=0;data.renderPostProcessing=false;
            AddLight("Key",new Vector3(45,-35,0),1.5f);AddLight("Fill",new Vector3(25,140,0),.8f);
        }
        catch {Dispose();throw;}
    }
    public void SetView(float angle,float zoom)
    {
        camera.transform.position=viewCenter+Quaternion.Euler(0,angle,0)*new Vector3(viewRadius*.45f,viewRadius*.35f,-viewRadius*3.5f)*zoom;camera.transform.LookAt(viewCenter);
    }
    void AddLight(string name,Vector3 angle,float power)
    {
        var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);var light=go.AddComponent<Light>();
        light.type=LightType.Directional;light.intensity=power;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(angle);
    }
    public void Select(AnimationClip clip)
    {
        if(graph.IsValid())graph.Destroy();Clip=clip;Time=0;
        if(clip==null)return;
        animator.Rebind();RestoreAnchor();
        graph=PlayableGraph.Create("OVERBURST v3 review");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        playable=AnimationClipPlayable.Create(graph,clip);playable.SetSpeed(0);playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);
        var output=AnimationPlayableOutput.Create(graph,"Review clip",animator);output.SetSourcePlayable(playable);graph.Play();Sample(0);
    }
    void RestoreAnchor(){animator.transform.localPosition=position;animator.transform.localRotation=rotation;animator.transform.localScale=scale;}
    public void Sample(float seconds)
    {
        if(!graph.IsValid() || Clip==null)return;
        Time=Mathf.Clamp(seconds,0,Mathf.Max(0,Clip.length-.0001f));playable.SetTime(Time);graph.Evaluate(0);RestoreAnchor();
    }
    public RenderTexture Render(int width=640,int height=420)
    {
        width=Mathf.Clamp(width,64,1600);height=Mathf.Clamp(height,64,1000);
        if(texture==null || texture.width!=width || texture.height!=height)
        {
            if(texture!=null){texture.Release();Object.DestroyImmediate(texture);}
            texture=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){hideFlags=HideFlags.HideAndDontSave};texture.Create();
        }
        var request=new UniversalRenderPipeline.SingleCameraRequest{destination=texture};
        if(RenderPipeline.SupportsRenderRequest(camera,request))RenderPipeline.SubmitRenderRequest(camera,request);
        else {camera.targetTexture=texture;try{camera.Render();}finally{camera.targetTexture=null;}}
        return texture;
    }
    public void Dispose()
    {
        if(graph.IsValid())graph.Destroy();
        if(texture!=null){texture.Release();Object.DestroyImmediate(texture);texture=null;}
        if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);
        Model=null;Clip=null;animator=null;camera=null;scene=default;
    }
}

public sealed class UpcomingMonsterReviewWindow : EditorWindow
{
    UpcomingMonsterReviewStation source;
    UpcomingMonsterReviewPreview preview;
    int index;
    bool playing=true;
    float speed=1,viewAngle,zoom=1;
    double last;
    Vector2 scroll;
    string error;
    static readonly string[] Roles={"idle","move","weak","strong"};
    static readonly string[] Labels={"Idle","이동","약공","강공"};
    [MenuItem("OVERBURST/Monsters/추후 테마 검토/v3 모션 검토 창")]
    public static void Open(){var w=GetWindow<UpcomingMonsterReviewWindow>("v3 몬스터 모션");w.minSize=new Vector2(540,610);w.Show();w.SelectStation();}
    public static void StopAll(){foreach(var w in Resources.FindObjectsOfTypeAll<UpcomingMonsterReviewWindow>()){w.Release();w.source=null;w.Repaint();}}
    void OnEnable(){Selection.selectionChanged+=SelectStation;EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=PlayState;AssemblyReloadEvents.beforeAssemblyReload+=Release;last=EditorApplication.timeSinceStartup;}
    void OnDisable(){Selection.selectionChanged-=SelectStation;EditorApplication.update-=Tick;EditorApplication.playModeStateChanged-=PlayState;AssemblyReloadEvents.beforeAssemblyReload-=Release;Release();}
    void PlayState(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingEditMode)StopAll();}
    void Release(){preview?.Dispose();preview=null;}
    void SelectStation()
    {
        var selected=Selection.activeGameObject!=null?Selection.activeGameObject.GetComponentInParent<UpcomingMonsterReviewStation>():null;
        if(selected==source && preview!=null)return;Release();source=selected;error=null;index=0;
        if(source!=null && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            try {preview=new UpcomingMonsterReviewPreview(source);if(source.motions.Length>0)preview.Select(source.motions[0].clip);}
            catch(Exception e){error=e.Message;Release();}
        }
        last=EditorApplication.timeSinceStartup;Repaint();
    }
    void Tick()
    {
        double now=EditorApplication.timeSinceStartup;float dt=(float)Math.Min(.1,now-last);last=now;
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)return;
        if(preview!=null && playing && preview.Clip!=null){preview.Sample((preview.Time+dt*speed)%Mathf.Max(.001f,preview.Clip.length));Repaint();}
    }
    void OnGUI()
    {
        EditorGUILayout.LabelField("OVERBURST · v3 테마 / 모션 검토",EditorStyles.boldLabel);
        if(source==null){EditorGUILayout.HelpBox("쇼케이스 씬의 몬스터 또는 하위 모델을 선택하세요. Play 없이 선택된 실제 클립을 미리 봅니다.",MessageType.Info);return;}
        scroll=EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField(source.displayName+" · "+source.grade+" · "+source.selectionStatus,EditorStyles.boldLabel);
        EditorGUILayout.LabelField("표시 크기",$"{source.displaySize.x:F2} × {source.displaySize.y:F2} × {source.displaySize.z:F2}m");
        if(!string.IsNullOrEmpty(error))EditorGUILayout.HelpBox(error,MessageType.Error);
        if(preview!=null)
        {
            EditorGUILayout.BeginHorizontal();if(GUILayout.Button("정면"))viewAngle=0;if(GUILayout.Button("측면"))viewAngle=90;if(GUILayout.Button("후면"))viewAngle=180;EditorGUILayout.EndHorizontal();
            viewAngle=EditorGUILayout.Slider("시점 회전",viewAngle,-180,180);zoom=EditorGUILayout.Slider("보기 거리",zoom,.7f,1.8f);preview.SetView(viewAngle,zoom);
            Rect rect=GUILayoutUtility.GetRect(300,Mathf.Min(390,position.width*.6f),GUILayout.ExpandWidth(true));
            if(Event.current.type==EventType.Repaint)GUI.DrawTexture(rect,preview.Render((int)rect.width,(int)rect.height),ScaleMode.ScaleToFit,false);
        }
        if(source.motions.Length==0)EditorGUILayout.HelpBox("전용 모션이 없습니다. 모델 외형만 비교합니다.",MessageType.Warning);
        else
        {
            EditorGUILayout.BeginHorizontal();playing=GUILayout.Toggle(playing,"재생",GUI.skin.button,GUILayout.Width(65));
            if(GUILayout.Button("처음",GUILayout.Width(65)))preview?.Sample(0);
            speed=EditorGUILayout.Slider("속도",speed,.1f,2);EditorGUILayout.EndHorizontal();
            var m=source.motions[Mathf.Clamp(index,0,source.motions.Length-1)];
            if(preview?.Clip!=null){float time=EditorGUILayout.Slider("시간 (초)",preview.Time,0,preview.Clip.length);if(Mathf.Abs(time-preview.Time)>.0001f){playing=false;preview.Sample(time);Repaint();}}
            for(int role=0;role<Roles.Length;role++)
            {
                EditorGUILayout.LabelField(Labels[role],EditorStyles.boldLabel);
                var rows=source.motions.Select((motion,i)=>new{motion,i}).Where(x=>x.motion.role==Roles[role]).ToArray();
                if(rows.Length==0)EditorGUILayout.LabelField("연결 없음");
                foreach(var row in rows)
                {
                    var item=row.motion;string label=(row.i==index?"▶ ":"")+item.clip.name+"  "+item.count+(item.parryable?" · 패링":"")+(item.needsTrim?" · 최대 3타 편집 필요":"");
                    if(GUILayout.Button(label)){index=row.i;preview?.Select(item.clip);playing=true;Repaint();}
                }
            }
            EditorGUILayout.Space();EditorGUILayout.LabelField("선택 모션",m.clip.name);EditorGUILayout.ObjectField("클립 원본",m.clip,typeof(AnimationClip),false);
            EditorGUILayout.HelpBox((m.concept??"")+"\n"+m.connection+"\n"+m.selectionState,MessageType.Info);
        }
        if(!string.IsNullOrEmpty(source.warnings))EditorGUILayout.HelpBox(source.warnings,MessageType.Warning);
        EditorGUILayout.HelpBox("클립 원본 재생입니다. 이동 거리·피해 판정·3타 편집·패링 전환의 게임 구현은 후속 단계입니다.",MessageType.None);
        EditorGUILayout.EndScrollView();
    }
}
