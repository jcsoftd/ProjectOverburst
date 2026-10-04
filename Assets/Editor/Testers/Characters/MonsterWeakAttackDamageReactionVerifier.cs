using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MonsterWeakAttackDamageReactionVerifier
{
    public static string Run(string outputDirectory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("피해/반응 검사는 유휴 EditMode에서만 실행합니다.");
        string workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
        string allowed = Path.GetFullPath(Path.Combine(workspace, "개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (!outputDirectory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("산출 경로가 아닙니다.");
        var checks = new List<object>(); int failed = 0;
        void Check(string name, bool pass) { checks.Add(new { name, pass }); if (!pass) failed++; }
        bool Near(float a, float b) => Mathf.Abs(a-b) < .001f;
        EnemyWeakAttackDamageBudget.TryCreate(10, 3, out var budget);
        Check("three contacts conserve total with deterministic remainder", Near(budget.ForPhase(0), 3) && Near(budget.ForPhase(1), 3) && Near(budget.ForPhase(2), 4));
        EnemyWeakAttackDamageBudget.TryCreate(3.2f, 3, out budget);
        Check("fractional total retains remainder without minimum inflation", Near(budget.ForPhase(0)+budget.ForPhase(1)+budget.ForPhase(2), 3.2f) && budget.ForPhase(2) >= 1);
        Check("insufficient total fails instead of becoming three", !EnemyWeakAttackDamageBudget.TryCreate(2, 3, out _));
        Check("invalid and non-finite budgets fail", !EnemyWeakAttackDamageBudget.TryCreate(float.NaN, 2, out _) && !EnemyWeakAttackDamageBudget.TryCreate(float.PositiveInfinity, 1, out _) && !EnemyWeakAttackDamageBudget.TryCreate(10, 4, out _));
        Check("unknown phase cannot create damage", budget.ForPhase(-1) == 0 && budget.ForPhase(3) == 0);
        bool exhaustive = true;
        for (int count=1; count<=3; count++) for (int total=count; total<=100; total++)
        {
            if (!EnemyWeakAttackDamageBudget.TryCreate(total, count, out var candidate)) { exhaustive=false; continue; }
            float sum=0;
            for (int phase=0;phase<count;phase++) { float hit=candidate.ForPhase(phase); sum+=hit; exhaustive &= hit >= 1; }
            exhaustive &= Near(sum, total);
        }
        Check("one through three contacts conserve all integer totals up to 100", exhaustive);
        var ability = ScriptableObject.CreateInstance<EnemyAbilityDefinition>();
        var scene = EditorSceneManager.NewPreviewScene(); var owned = new List<GameObject>();
        try
        {
            ability.Configure("damage-fixture", "Attack1", 10, 2, 1, 120, 1, 0, .3f, 1, 1, false);
            ability.ConfigureAdditionalHits(.5f, .7f);
            ability.TryResolveWeakDamageBudget(1, 2, out var legacyBudget);
            Check("definition multiplier applies once to V3 legacy pattern", Near(legacyBudget.Total, 20) && Near(legacyBudget.ForPhase(0)+legacyBudget.ForPhase(1)+legacyBudget.ForPhase(2), 20));
            Check("legacy per-contact API remains ten", Near(ability.ResolveDamage(1), 10));
            Set(ability, "referencePatternDamagePercent", 10f);
            ability.TryResolveWeakDamageBudget(50, 1.5f, out var levelBudget);
            float expected = Mathf.Max(1, OverburstCombatBalance.RoundStat(OverburstCombatBalance.ReferenceEffectiveHealth(50)*.1f))*1.5f;
            Check("level budget rounds whole pattern before division", Near(levelBudget.Total, expected));
            Check("level pattern still excludes Health legacy regrowth", !CombatBalanceFormulas.UsesLegacyEnemyDamageGrowth(ability));
            Check("invalid definition multiplier fails", !ability.TryResolveWeakDamageBudget(50, float.NaN, out _));

            GameObject Root(string name)
            { var go=new GameObject(name); owned.Add(go); SceneManager.MoveGameObjectToScene(go,scene); return go; }
            var source=Root("V3_ReactionSource"); var secondSource=Root("V3_OtherSource");
            var victim=Root("V3_ReactionVictim"); var hp=victim.AddComponent<CombatHealth>();
            Set(hp,"showDamageNumbers",false); hp.ResetHealth();
            var body=victim.AddComponent<Rigidbody>(); body.useGravity=false; body.linearDamping=0;
            var events=new List<DamageInfo>(); var losses=new List<float>();
            hp.OnDamageResolved += (_,info,loss,__) => { events.Add(info); losses.Add(loss); };
            var scope=new EnemyWeakAttackReactionScope(source,10);
            DamageInfo Hit(EnemyWeakAttackReactionScope context, GameObject owner, int sequence, int phase=0)
                => new DamageInfo(10,Vector3.zero,owner,Vector3.forward,2,sourceAttackSequenceId:sequence,sourceAttackPhaseIndex:phase,weakAttackReactionScope:context);
            hp.TakeDamage(new DamageInfo(0,Vector3.zero,source,sourceAttackSequenceId:10,weakAttackReactionScope:scope));
            Check("zero input neither reports contact nor spends reaction", events.Count==0);
            Set(hp,"currentHp",1f); hp.SetDamageDeathPrevention(hp,true);
            hp.TakeDamage(Hit(scope,source,10)); scene.GetPhysicsScene().Simulate(.02f);
            Check("zero actual HP loss does not push", events.Count==1 && losses[0]==0 && events[0].suppressRepeatedAttackReaction && Near(body.linearVelocity.z,0));
            hp.SetDamageDeathPrevention(hp,false); hp.ResetHealth(); events.Clear(); losses.Clear();
            hp.TakeDamage(Hit(scope,source,10)); scene.GetPhysicsScene().Simulate(.02f);
            float firstVelocity=body.linearVelocity.z;
            Check("later positive contact earns first reaction", Near(hp.CurrentHp,90) && !events[0].suppressRepeatedAttackReaction && firstVelocity > 1.9f);
            hp.TakeDamage(Hit(scope,source,10,1)); scene.GetPhysicsScene().Simulate(.02f);
            Check("second contact damages without another impulse", Near(hp.CurrentHp,80) && events[1].suppressRepeatedAttackReaction && Near(body.linearVelocity.z,firstVelocity));
            Check("on-hit flags and both resolved events are retained", events.Count==2 && events.TrueForAll(info=>info.triggersOnHitEffects && !info.isDamageOverTime && !info.suppressDefaultHitVfx) && Near(losses[0]+losses[1],20));
            var next=new EnemyWeakAttackReactionScope(source,11);
            hp.TakeDamage(Hit(next,source,11)); scene.GetPhysicsScene().Simulate(.02f);
            Check("same source next execution has independent reaction", !events[2].suppressRepeatedAttackReaction && body.linearVelocity.z > firstVelocity+1.9f);
            var other=new EnemyWeakAttackReactionScope(secondSource,10);
            hp.TakeDamage(Hit(other,secondSource,10)); scene.GetPhysicsScene().Simulate(.02f);
            Check("another source is not blocked by global victim immunity", !events[3].suppressRepeatedAttackReaction && Near(hp.CurrentHp,60));
            var otherVictim=Root("V3_OtherVictim"); var otherHp=otherVictim.AddComponent<CombatHealth>(); Set(otherHp,"showDamageNumbers",false);
            DamageInfo otherInfo=default; otherHp.OnDamaged+=(_,info)=>otherInfo=info;
            otherHp.TakeDamage(Hit(scope,source,10));
            Check("each target gets its own first reaction", !otherInfo.suppressRepeatedAttackReaction);
            Check("mismatched source and sequence cannot consume rights", !next.TryConsume(secondSource,11,otherHp,10) && !next.TryConsume(source,12,otherHp,10) && next.TryConsume(source,11,otherHp,10));
            scope.Cancel();
            Check("cancelled scope cannot acquire another target", !scope.TryConsume(source,10,otherHp,10));
            body.linearVelocity=Vector3.zero; events.Clear(); hp.ResetHealth();
            hp.TakeDamage(Hit(null,source,20)); hp.TakeDamage(Hit(null,source,20,1)); scene.GetPhysicsScene().Simulate(.02f);
            Check("profileless legacy still allows every contact reaction", Near(hp.CurrentHp,80) && events.TrueForAll(info=>!info.suppressRepeatedAttackReaction) && body.linearVelocity.z > 3.9f);
            hp.ResetHealth(); Set(hp,"currentHp",5f); bool dead=false; hp.OnDead+=(_,__)=>dead=true;
            hp.TakeDamage(Hit(other,secondSource,10,1));
            Check("suppressed repeated reaction cannot suppress lethal processing", hp.IsDead && dead && hp.CurrentHp==0);
            var sourceActor=source.AddComponent<EnemyActor>();
            var leaseScope=new EnemyWeakAttackReactionScope(source,30,sourceActor);
            Check("current source lease allows its first contact", leaseScope.TryConsume(source,30,otherHp,1));
            typeof(EnemyActor).GetProperty("LeaseVersion").GetSetMethod(true).Invoke(sourceActor,new object[]{1u});
            Check("source pool reuse invalidates old reaction scope", !leaseScope.TryConsume(source,30,hp,1));
            var currentScope=new EnemyWeakAttackReactionScope(source,31,sourceActor);
            var targetActor=otherVictim.AddComponent<EnemyActor>();
            Check("target current lease has one reaction", currentScope.TryConsume(source,31,otherHp,1) && !currentScope.TryConsume(source,31,otherHp,1));
            typeof(EnemyActor).GetProperty("LeaseVersion").GetSetMethod(true).Invoke(targetActor,new object[]{1u});
            Check("reused target lease earns a fresh reaction", currentScope.TryConsume(source,31,otherHp,1));
            UnityEngine.Object.DestroyImmediate(sourceActor);
            Check("destroyed actor component cannot revive old scope", !currentScope.TryConsume(source,31,hp,1));
            leaseScope.Cancel(); currentScope.Cancel();
            next.Cancel(); other.Cancel();
        }
        finally
        {
            foreach(var go in owned) if(go!=null) UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(ability);
            if(scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
        Check("owned preview scene returned", !scene.IsValid());
        Directory.CreateDirectory(outputDirectory);
        string path=Path.Combine(outputDirectory,"damage-reaction-results.json");
        File.WriteAllText(path,JsonConvert.SerializeObject(new {status=failed==0?"PASS_SCOPED":"FAIL",checks,failed,
            ownPlay=false,actualPlayerHitAnimation="NOT_RUN",actualEvadeParry="NOT_RUN",audioApplied=false},Formatting.Indented));
        if(failed>0) throw new InvalidOperationException("약공 피해/반응 검사 실패: "+failed);
        return path;
    }
    private static void Set(object value,string name,object field) => value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(value,field);
}
