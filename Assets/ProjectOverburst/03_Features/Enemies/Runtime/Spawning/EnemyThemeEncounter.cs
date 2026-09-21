using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum EnemyThemeEncounterState { Idle, Telegraph, Spawning, Combat, Breather, Completed, Stopped, Failed }

[DisallowMultipleComponent]
public sealed class EnemyThemeEncounter : MonoBehaviour
{
    private sealed class Lease
    { public EnemyActor actor; public uint version; public float diedAt = -1; }
    [SerializeField] private EnemyThemeTable tableOverride;
    [SerializeField] private BoxCollider spawnBounds;
    [SerializeField,Min(1)] private int aliveLimit = 80;
    [SerializeField,Min(1)] private int waveCount = 3;
    [SerializeField,Min(.1f)] private float telegraphSeconds = 1.6f;
    [SerializeField,Min(.1f)] private float restSeconds = 3;
    [SerializeField] private Material warningMaterial;
    private readonly List<Lease> owned = new List<Lease>(160);
    private readonly List<Vector3> reserved = new List<Vector3>(50);
    private readonly List<float> reservedRadii = new List<float>(50);
    private EnemySpawnService service;
    private Transform target;
    private Coroutine sequence;
    private LineRenderer warning;
    private bool admissionBusy;
    public EnemyThemeEncounterState State { get; private set; }
    public string LastMessage { get; private set; } = "대기";
    public int SpawnedCount { get; private set; }
    public int DefeatedCount { get; private set; }
    public int FailedPlacements { get; private set; }
    public int Wave { get; private set; }
    public EnemyThemeTable Table => EnemyMapTheme.Resolve(transform,tableOverride);
    public int AliveCount
    {
        get { int count=0;foreach(var lease in owned)if(IsCurrent(lease) && !lease.actor.Health.IsDead)count++;return count; }
    }
    public bool Running => sequence != null || admissionBusy;
    public IReadOnlyList<EnemyActor> SnapshotActors()
    {var list=new List<EnemyActor>();foreach(var lease in owned)if(IsCurrent(lease))list.Add(lease.actor);return list;}
    private static bool IsCurrent(Lease lease) => lease.actor != null && lease.actor.IsLeased && lease.actor.LeaseVersion==lease.version;
    public void Configure(EnemyThemeTable table,BoxCollider bounds=null,Material material=null)
    {
        if(Running || owned.Count>0)throw new InvalidOperationException("진행 중인 전투를 먼저 정리하세요.");
        tableOverride=table;spawnBounds=bounds;warningMaterial=material;
    }
    public bool Begin(Transform player,bool onslaught,int seed=731)
    {
        if(!Application.isPlaying || Running || owned.Count>0 || player==null) return false;
        var health=player.GetComponent<CombatHealth>();if(health!=null && health.IsDead)return false;
        var table=Table;
        if(table==null || !table.Validate(out _)){Fail("유효한 맵/구역 테이블이 없습니다.");return false;}
        service=EnemySpawnService.Current;
        if(service==null && !EnemyDebugSpawnRuntimeContext.TryGetSpawnService(transform,out service)){Fail("스폰 서비스를 준비하지 못했습니다.");return false;}
        if(!service.RegisterAdditionalCatalog(table.Catalog,out string error)){Fail(error);return false;}
        target=player;SpawnedCount=0;DefeatedCount=0;FailedPlacements=0;Wave=0;
        admissionBusy=true;sequence=StartCoroutine(Run(onslaught,seed));admissionBusy=false;return true;
    }
    private IEnumerator Run(bool onslaught,int seed)
    {
        // StartCoroutine must return its handle before the sequence can finish/fail.
        yield return null;
        int waves=onslaught?Mathf.Max(1,waveCount):1;
        for(int w=0;w<waves;w++)
        {
            Wave=w+1;
            if(!TargetAvailable()){StopEncounter(true);yield break;}
            if(w>0)
            {
                State=EnemyThemeEncounterState.Combat;LastMessage="남은 무리를 정리하세요";
                while(AliveCount>12){if(!TargetAvailable()){StopEncounter(true);yield break;}yield return new WaitForSeconds(.2f);}
                State=EnemyThemeEncounterState.Breather;LastMessage="다음 공세까지 잠시 정비";
                yield return new WaitForSeconds(restSeconds);
            }
            float angle=onslaught?30+w*125:0;
            if(onslaught)
            {
                State=EnemyThemeEncounterState.Telegraph;LastMessage=$"{Wave}/{waves} 공세 · 진입 방향 주의";
                ShowWarning(angle);yield return new WaitForSeconds(telegraphSeconds);HideWarning();
            }
            State=EnemyThemeEncounterState.Spawning;LastMessage=$"{Table.DisplayName} 소환 중";
            reserved.Clear();reservedRadii.Clear();
            var roster=Table.BuildRoster(40,9,1,seed+w*7919);var random=new System.Random(seed+w*7919);
            int before=SpawnedCount;
            for(int i=0;i<roster.Count;i++)
            {
                if(!TargetAvailable()){StopEncounter(true);yield break;}
                if(AliveCount>=Mathf.Max(50,aliveLimit)){Fail("생존 몬스터 상한에 도달했습니다.");break;}
                var definition=roster[i];
                if(TryPosition(definition,random,onslaught,angle,out Vector3 position))
                {
                    Quaternion rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(target.position-position,Vector3.up).normalized);
                    var request=new EnemySpawnRequest(definition,position,rotation,target,gameObject,target,transform,1,1,seed+i+w*50);
                    if(service.TrySpawn(request,out var actor))
                    {
                        var lease=new Lease{actor=actor,version=actor.LeaseVersion};owned.Add(lease);actor.Health.OnDead+=OnDeath;
                        actor.AI.RequestAggro(target);SpawnedCount++;
                    }
                    else FailedPlacements++;
                }
                else FailedPlacements++;
                if((i+1)%5==0)yield return null;
                if(onslaught && i==39)yield return new WaitForSeconds(.8f);
            }
            if(SpawnedCount-before!=50)
            {Fail($"안전한 위치 부족 등으로 {SpawnedCount-before}/50마리 생성. 배치 공간과 로그를 확인하세요.");sequence=null;yield break;}
        }
        State=EnemyThemeEncounterState.Combat;LastMessage="소환 완료";
        while(AliveCount>0){if(!TargetAvailable()){StopEncounter(true);yield break;}yield return new WaitForSeconds(.2f);}
        State=EnemyThemeEncounterState.Completed;LastMessage="전투 완료";sequence=null;
    }
    private bool TargetAvailable()
    {var health=target!=null?target.GetComponent<CombatHealth>():null;return target!=null && target.gameObject.activeInHierarchy && (health==null || !health.IsDead)
        && (PersistentSceneFlow.Instance==null || !PersistentSceneFlow.Instance.IsSwitching);}
    private bool TryPosition(EnemyDefinition definition,System.Random random,bool arc,float angle,out Vector3 result)
    {
        result=default;
        var capsule=definition.ActorPrefab.CollisionRoot.GetComponentInChildren<CapsuleCollider>();
        float radius=capsule!=null?capsule.radius:.5f,height=capsule!=null?capsule.height:1;
        int mask=~((1<<LayerMask.NameToLayer("Enemy"))|(1<<LayerMask.NameToLayer("Player"))|(1<<LayerMask.NameToLayer("Ignore Raycast")));
        var walkable=RunWalkableContext.Current;
        for(int attempt=0;attempt<96;attempt++)
        {
            float direction=arc?angle+((float)random.NextDouble()-.5f)*110:(float)random.NextDouble()*360;
            float distance=12+(float)random.NextDouble()*15;
            Vector3 candidate=target.position+Quaternion.Euler(0,direction,0)*Vector3.forward*distance;
            if(spawnBounds!=null)
            {
                Vector3 local=spawnBounds.transform.InverseTransformPoint(candidate)-spawnBounds.center;
                if(Mathf.Abs(local.x)>spawnBounds.size.x*.5f-radius || Mathf.Abs(local.z)>spawnBounds.size.z*.5f-radius)continue;
            }
            if(walkable!=null && !walkable.IsWalkable(candidate))continue;
            if(!Physics.Raycast(candidate+Vector3.up*4,Vector3.down,out var ground,8,mask,QueryTriggerInteraction.Ignore))continue;
            if(ground.normal.y<.72f || Mathf.Abs(ground.point.y-target.position.y)>2.5f)continue;
            candidate=ground.point+Vector3.up*.035f;
            bool overlap=false;for(int j=0;j<reserved.Count;j++)if((candidate-reserved[j]).sqrMagnitude<Mathf.Pow(radius+reservedRadii[j]+.15f,2)){overlap=true;break;}
            if(overlap)continue;
            Vector3 bottom=candidate+Vector3.up*(radius+.12f),top=candidate+Vector3.up*Mathf.Max(radius+.12f,height-radius);
            if(Physics.CheckCapsule(bottom,top,radius,~(1<<LayerMask.NameToLayer("Ignore Raycast")),QueryTriggerInteraction.Ignore))continue;
            reserved.Add(candidate);reservedRadii.Add(radius);result=candidate;return true;
        }
        return false;
    }
    private void OnDeath(CombatHealth health,DamageInfo info)
    {foreach(var lease in owned)if(IsCurrent(lease) && lease.actor.Health==health && lease.diedAt<0){lease.diedAt=Time.time;DefeatedCount++;break;}}
    private void Update()
    {
        for(int i=owned.Count-1;i>=0;i--)
        {
            var lease=owned[i];if(!IsCurrent(lease)){if(lease.actor!=null)lease.actor.Health.OnDead-=OnDeath;owned.RemoveAt(i);continue;}
            if(lease.diedAt>=0 && Time.time-lease.diedAt>3)
            {lease.actor.Health.OnDead-=OnDeath;lease.actor.RequestPoolRelease();owned.RemoveAt(i);}
        }
    }
    public void StopEncounter(bool clear)
    {
        if(sequence!=null){StopCoroutine(sequence);sequence=null;}admissionBusy=false;HideWarning();
        if(clear)
        {
            foreach(var lease in owned)
            {
                if(lease.actor==null)continue;
                lease.actor.Health.OnDead-=OnDeath;if(IsCurrent(lease))lease.actor.RequestPoolRelease();
            }
            owned.Clear();
        }
        State=EnemyThemeEncounterState.Stopped;LastMessage=clear?"시험 소환 정리 완료":"공세 중지";
    }
    private void OnDisable(){StopEncounter(true);}
    private void Fail(string message)
    {
        // A failed exact-count batch must not leave an unintended partial encounter behind.
        StopEncounter(true);State=EnemyThemeEncounterState.Failed;LastMessage=message;Debug.LogWarning("[EnemyThemeEncounter] "+message,this);
    }
    private void ShowWarning(float angle)
    {
        if(warning==null)
        {
            var go=new GameObject("Wave arrival arc");go.transform.SetParent(transform,false);warning=go.AddComponent<LineRenderer>();
            warning.sharedMaterial=warningMaterial;warning.positionCount=19;warning.startWidth=.2f;warning.endWidth=.2f;
            warning.startColor=Table.Accent;warning.endColor=Table.Accent;warning.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        warning.enabled=true;
        for(int i=0;i<19;i++)warning.SetPosition(i,target.position+Vector3.up*.12f+Quaternion.Euler(0,angle-55+i*(110f/18),0)*Vector3.forward*12);
    }
    private void HideWarning(){if(warning!=null)warning.enabled=false;}
}
