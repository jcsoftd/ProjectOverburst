using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CrustaspikanMaterialWindow : EditorWindow
{
    EnemyBossMaterialCollection collection;
    EnemyActor actor;
    EnemySpawnService service;
    GameObject ownedServices;
    bool ownsArena;
    int attack,motion;
    Vector2 scroll;
    string status;
    CrustaspikanMaterialPreview preview;
    bool showPreview=true,previewPlaying;
    int previewMode,previewMotion;
    float previewSeconds;
    double previousTick;
    AnimationClip previewClip;
    const float ParryPreviewLead=.70f;
    [MenuItem("OVERBURST/Bosses/Crustaspikan/전투 재료 검토")]
    public static void Open()=>GetWindow<CrustaspikanMaterialWindow>("Crustaspikan 재료").minSize=new Vector2(680f,780f);
    void OnEnable(){collection=AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);previousTick=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}
    void OnDisable(){EditorApplication.update-=Tick;DisposePreview();Release();}
    void OnGUI()
    {
        collection=(EnemyBossMaterialCollection)EditorGUILayout.ObjectField("재료 모음",collection,typeof(EnemyBossMaterialCollection),false);
        if(collection==null){EditorGUILayout.HelpBox("먼저 전투 재료를 생성하세요.",MessageType.Info);return;}
        scroll=EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("기본 공격 16개 · 타격 18회 · 기본 동작 37개 · RM 제외",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("PersistentScene에서 Play → 하이드아웃 진입 → 검토 시작. 공격을 고르면 실제 플레이어를 향해 실행합니다. 패링은 기존 강공 입력을 사용합니다.",MessageType.Info);
        attack=Mathf.Clamp(attack,0,collection.attacks.Length-1);
        attack=EditorGUILayout.Popup("공격 재료",attack,collection.attacks.Select(m=>m.displayName+" / "+m.runtimeClip.name).ToArray());
        if(GUILayout.Button("선택한 공격 재료 열기"))Selection.activeObject=collection.attacks[attack];
        foreach(var strike in collection.attacks[attack].strikes)
            EditorGUILayout.LabelField(strike.shape==GroundIndicatorShape.Donut?$"도넛 · 안쪽 {strike.innerRadius:0.##}m / 바깥 {strike.radius:0.##}m":strike.shape==GroundIndicatorShape.Sector?$"부채꼴 · {strike.radius:0.##}m / {strike.angle:0}° / 방향 {strike.yaw:0}°":strike.shape==GroundIndicatorShape.Rectangle?$"직선 · 폭 {strike.width:0.##}m / 길이 {strike.length:0.##}m":$"원형 · 반경 {strike.radius:0.##}m");
        DrawPreview();
        using(new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
        {
            if(actor==null){if(GUILayout.Button("검토 시작"))Try(Spawn);}
            else
            {
                var material=collection.attacks[attack];
                if(GUILayout.Button("선택 공격 실행"))Try(()=>
                {
                    if(actor.AbilityController.IsExecuting)throw new InvalidOperationException("현재 동작이 끝난 뒤 실행하세요.");
                    if(!actor.AbilityController.TryStartAbility(material.ability,PlayerInputFacade.Current.transform))throw new InvalidOperationException("거리·쿨다운·현재 동작 조건으로 시작하지 못했습니다.");status=material.displayName+" 실행";
                });
                if(GUILayout.Button("재료 자산 열기"))Selection.activeObject=material;
                var executor=actor.GetComponent<EnemyBossMaterialExecutor>();
                EditorGUILayout.LabelField("진행",executor.NormalizedTime.ToString("P0"));
                EditorGUILayout.LabelField("타격 / 발사 / 피해",$"{executor.ImpactCount} / {executor.LaunchCount} / {executor.DamageCount}");
                foreach(var strike in material.strikes)EditorGUILayout.LabelField($"접촉 {strike.contactStart:P1}~{strike.contactEnd:P1} · 타격 {strike.impact:P1}");
                var motions=collection.motions.Where(m=>m.IsPlayable).ToArray();motion=Mathf.Clamp(motion,0,motions.Length-1);
                motion=EditorGUILayout.Popup("동작 보기 / 준비",motion,motions.Select(m=>m.id).ToArray());
                if(GUILayout.Button("동작 실행 · 공격 판정 없음"))Try(()=>{var entry=motions[motion];if(!executor.TryPlayMotion(entry.id,entry.id=="UnearthRock"))throw new InvalidOperationException("현재 동작이 끝난 뒤 실행하세요.");status=entry.id;});
                if(GUILayout.Button("공격 중단 / 초기 자세"))Try(()=>{actor.AbilityController.Cancel();actor.AnimationBridge.ResetForReuse();});
                if(GUILayout.Button("검토 종료"))Release();
            }
        }
        if(!string.IsNullOrEmpty(status))EditorGUILayout.HelpBox(status,MessageType.None);
        foreach(var material in collection.attacks)
        {EditorGUILayout.LabelField(material.displayName,EditorStyles.boldLabel);EditorGUILayout.LabelField(material.assemblyNotes,EditorStyles.wordWrappedLabel);}
        EditorGUILayout.EndScrollView();
    }
    void Tick()
    {
        double now=EditorApplication.timeSinceStartup;
        if(now-previousTick<1.0/30.0)return;
        float delta=(float)Math.Min(.1,now-previousTick);previousTick=now;
        if(previewPlaying&&previewClip!=null)
        {
            previewSeconds=Mathf.Min(previewClip.length,previewSeconds+delta);
            if(previewSeconds>=previewClip.length)previewPlaying=false;
        }
        if(previewPlaying||actor!=null)Repaint();
    }
    void DisposePreview(){previewPlaying=false;preview?.Dispose();preview=null;previewClip=null;}
    void SeekPreview(float seconds){previewPlaying=false;previewSeconds=Mathf.Clamp(seconds,0,previewClip!=null?previewClip.length:0f);Repaint();}
    void DrawPreview()
    {
        bool expanded=EditorGUILayout.Foldout(showPreview,"모션 · 타격 시점 미리보기",true);
        if(showPreview&&!expanded)DisposePreview();showPreview=expanded;
        if(!showPreview)return;
        var material=collection.attacks[attack];
        previewMode=GUILayout.Toolbar(previewMode,new[]{"선택 공격","전체 기본 모션"});
        AnimationClip clip;
        if(previewMode==0)clip=material.runtimeClip;
        else
        {
            var motions=collection.motions.Where(m=>m.IsPlayable).ToArray();previewMotion=Mathf.Clamp(previewMotion,0,motions.Length-1);
            previewMotion=EditorGUILayout.Popup("모션",previewMotion,motions.Select(m=>m.id).ToArray());clip=motions[previewMotion].runtime;
        }
        if(clip!=previewClip){previewClip=clip;previewSeconds=0f;previewPlaying=false;}
        using(new EditorGUILayout.HorizontalScope())
        {
            if(GUILayout.Button(previewPlaying?"일시 정지":"재생",GUILayout.Width(85)))
            {if(previewSeconds>=clip.length)previewSeconds=0f;previewPlaying=!previewPlaying;previousTick=EditorApplication.timeSinceStartup;}
            if(GUILayout.Button("◀ 1F",GUILayout.Width(60)))SeekPreview(previewSeconds-1f/clip.frameRate);
            if(GUILayout.Button("1F ▶",GUILayout.Width(60)))SeekPreview(previewSeconds+1f/clip.frameRate);
            if(GUILayout.Button("처음",GUILayout.Width(55)))SeekPreview(0f);
            EditorGUILayout.LabelField($"{Mathf.RoundToInt(previewSeconds*clip.frameRate)} / {Mathf.RoundToInt(clip.length*clip.frameRate)}F · {previewSeconds:F3}s");
        }
        float seconds=EditorGUILayout.Slider("시점 (초)",previewSeconds,0f,clip.length);
        if(Mathf.Abs(seconds-previewSeconds)>.00001f)SeekPreview(seconds);
        using(new EditorGUILayout.HorizontalScope())
        {
            if(GUILayout.Button("정면"))SetPreviewView(0f,15f);
            if(GUILayout.Button("측면"))SetPreviewView(90f,15f);
            if(GUILayout.Button("후방"))SetPreviewView(180f,15f);
            if(GUILayout.Button("위에서"))SetPreviewView(25f,65f);
        }
        if(previewMode==0)
        {
            for(int i=0;i<material.strikes.Length;i++)
            {
                var strike=material.strikes[i];float impact=strike.impact*clip.length;
                using(new EditorGUILayout.HorizontalScope())
                {
                    string eventName=material.delivery==EnemyBossMaterialDelivery.Melee?"타격":"발사";
                    string window=material.delivery==EnemyBossMaterialDelivery.Melee?$"접촉 {Mathf.RoundToInt(strike.contactStart*clip.length*clip.frameRate)}–{Mathf.RoundToInt(strike.contactEnd*clip.length*clip.frameRate)}F":"비행체 접촉 시 피해";
                    EditorGUILayout.LabelField($"{i+1}타 · {eventName} {Mathf.RoundToInt(impact*clip.frameRate)}F · {window}");
                    if(material.ability.IsParryable&&GUILayout.Button("패링 시작",GUILayout.Width(75)))SeekPreview(impact-ParryPreviewLead);
                    if(GUILayout.Button(eventName,GUILayout.Width(55)))SeekPreview(impact);
                    if(material.delivery==EnemyBossMaterialDelivery.Melee&&GUILayout.Button("판정 끝",GUILayout.Width(65)))SeekPreview(strike.contactEnd*clip.length);
                }
            }
        }
        Rect rect=GUILayoutUtility.GetRect(100,Mathf.Clamp(position.width*.56f,210f,400f),GUILayout.ExpandWidth(true));
        if(Event.current.type==EventType.Repaint&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating)
        {
            try
            {
                if(preview==null)preview=new CrustaspikanMaterialPreview(collection);
                preview.Sample(clip,previewSeconds);
                var texture=preview.Render(Mathf.RoundToInt(rect.width),Mathf.RoundToInt(rect.height));
                GUI.DrawTexture(rect,texture,ScaleMode.StretchToFill,false);
            }
            catch(Exception error){status="모션 미리보기: "+error.Message;DisposePreview();}
        }
        EditorGUILayout.LabelField("전체 자세 자동 맞춤 · 청록 기둥 1.8m · 원본 프레임 기준 1배속",EditorStyles.miniLabel);
        if(previewMode==0&&material.ability.IsParryable)
            EditorGUILayout.HelpBox("패링 시작은 각 타격 0.70초 전 검토값입니다. 실제 패링은 다음 타격 순서·거리·피해 범위·시야 조건을 함께 검사합니다.",MessageType.None);
    }
    void SetPreviewView(float yaw,float pitch)
    {
        Try(()=>{if(preview==null)preview=new CrustaspikanMaterialPreview(collection);preview.Yaw=yaw;preview.Pitch=pitch;});Repaint();
    }
    void Try(Action action){try{action();}catch(Exception e){status=e.Message;}}
    void Spawn()
    {
        if(PlayerInputFacade.Current==null||EnemyThemeTrialService.Busy||EnemyThemeTrialService.InArena)throw new InvalidOperationException("하이드아웃에서 다른 시험을 마친 뒤 시작하세요.");
        EnemyThemeTrialService.ToggleArena();ownsArena=EnemyThemeTrialService.InArena;
        if(!ownsArena)throw new InvalidOperationException("하이드아웃 Play가 필요합니다.");
        var player=PlayerInputFacade.Current;service=EnemySpawnService.Current;
        if(service==null)
        {
            ownedServices=new GameObject("Crustaspikan material review services");SceneManager.MoveGameObjectToScene(ownedServices,player.gameObject.scene);
            var inactive=new GameObject("Pool").transform;inactive.SetParent(ownedServices.transform,false);inactive.gameObject.SetActive(false);
            var pool=ownedServices.AddComponent<EnemyPoolService>();pool.Configure(inactive,0);service=ownedServices.AddComponent<EnemySpawnService>();service.Configure(collection.catalog,pool);
        }
        if(!service.RegisterAdditionalCatalog(collection.catalog,out var message))throw new InvalidOperationException(message);
        Vector3 position=player.transform.position+Vector3.forward*11f;
        if(Physics.Raycast(position+Vector3.up*8,Vector3.down,out var floor,30,LayerMask.GetMask("Default","Environment","Ground")))position.y=floor.point.y+.035f;
        if(!service.TrySpawn(new EnemySpawnRequest(collection.actorDefinition,position,Quaternion.LookRotation(Vector3.back),player.transform,context:EncounterContext.Test),out actor))throw new InvalidOperationException("검토 보스 생성 실패");
        actor.AI.enabled=false;status="검토 준비 완료";
    }
    void Release()
    {
        if(actor!=null&&actor.IsLeased&&service!=null)service.Release(actor);actor=null;
        if(ownedServices!=null)Destroy(ownedServices);ownedServices=null;service=null;
        if(ownsArena&&EnemyThemeTrialService.InArena&&!EnemyThemeTrialService.Busy)EnemyThemeTrialService.ToggleArena();ownsArena=false;
    }
}
