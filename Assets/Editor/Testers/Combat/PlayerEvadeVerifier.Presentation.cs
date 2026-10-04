using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Records real gameplay in an isolated account. The studio only mirrors evaluated renderers.
public static partial class PlayerEvadeVerifier
{
    public const int SwordPresentationCaptureRevision = 5;
    const string SwordPresentationKey = "Overburst.PlayerEvadeVerifier.SwordPresentation";
    [InitializeOnLoadMethod]
    static void RegisterSwordPresentationReturn()
    {
        EditorApplication.playModeStateChanged -= ClearSwordPresentationMode;
        EditorApplication.playModeStateChanged += ClearSwordPresentationMode;
    }
    static void ClearSwordPresentationMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetString(PendingKey, "") == ""
            && SessionState.GetString(FacingQueueKey, "") == "") SessionState.EraseBool(SwordPresentationKey);
    }
    public static string QueueSwordPresentationIsolated(string directory)
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(FacingQueueKey,""))
            || !string.IsNullOrEmpty(SessionState.GetString(PendingKey,""))
            || !string.IsNullOrEmpty(SessionState.GetString(ReturnKey,"")))
            throw new InvalidOperationException("Preserve the existing verification owner.");
        SessionState.SetBool(SwordPresentationKey,true);
        try {return QueueSwordFacingIsolated(directory);}
        catch {SessionState.EraseBool(SwordPresentationKey);throw;}
    }
    static void SetPresentationGauge(OverburstElementEnergy energy, float amount)
    {
        energy.Clear(); FillEnergy(energy, 160000);
        typeof(OverburstElementEnergy).GetProperty(nameof(OverburstElementEnergy.Amount)).SetValue(energy,amount);
        (typeof(OverburstElementEnergy).GetField("Changed",Private)?.GetValue(energy) as Action)?.Invoke();
        Check(Mathf.Abs(energy.Amount-amount)<.001f,"촬영 시작 게이지 " + amount);
    }
    static IEnumerator CaptureSwordPresentation()
    {
        deadline = EditorApplication.timeSinceStartup + 420;
        var definition = actor.Equipment.CurrentWeaponData.GetMeleeDefinition();
        double preloadStart=EditorApplication.timeSinceStartup;
        var expectedComboClips=definition.comboDefinition.steps.Select(step=>step.animationClip).ToArray();
        Check(expectedComboClips.Length==4 && expectedComboClips.All(clip=>clip!=null && clip.length>0),"촬영 비교용4타 클립 사전 로드");
        double preloadSeconds=EditorApplication.timeSinceStartup-preloadStart;
        var ui = EnemyThemeTrialHarness.Current;
        Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(actor.transform,out spawn),"촬영 실제 몬스터 스폰 서비스");
        foreach (var table in ui.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog,out _),"촬영 적 카탈로그");
        var roster = ui.tables.SelectMany(t=>t.Entries).ToArray();
        var enemyDefinition = roster.First(e=>e.tier==EnemyThemeTier.Small && e.definition!=null
            && e.definition.EnemyId=="SpiderBrood_RostrokarckLarvae").definition;
        var parryDefinition = roster.First(e=>e.tier==EnemyThemeTier.Elite && e.definition!=null
            && e.definition.EnemyId=="CavernMutants_Ursacetus").definition;
        var ability = Enumerable.Range(0,parryDefinition.AbilitySet.Count).Select(parryDefinition.AbilitySet.GetAbility)
            .First(a=>a.name=="CavernMutants_Ursacetus_2HandsSmashAttack" && a.IsParryable && a.UsesPacedTimeline);
        var targets = new List<EnemyActor>();
        var hits = new List<object>();
        var listeners = new List<Action<CombatHealth,DamageInfo>>();
        var cases = new List<object>();
        Vector3 right = Vector3.Cross(Vector3.up,forward);
        yield return Reset(); EquipDashHeavyGem(WeaponElement.Fire);
        var energy = actor.GetComponent<OverburstElementEnergy>();
        if (energy==null) {energy=actor.gameObject.AddComponent<OverburstElementEnergy>();fixtures.Add(energy);}
        facingProbe = blocker.AddComponent<SwordFacingPoseProbe>();
        facingProbe.Bind(animator,actor.GetComponent<PlayerCombatFacingController>());
        facingProbe.StartCapture(Path.Combine(output,"Capture"),false);
        facingProbe.presentationCamera = true;

        try
        {
            for (int i=0;i<3;i++)
            {
                Vector3 point=origin+forward*(2+PresentationEnemyOffset(i,3))+right*PresentationEnemyLateral(i,3);
                Check(Physics.Raycast(point+Vector3.up*4,Vector3.down,out var floor,9,
                    LayerMask.GetMask("Default","Environment","Ground")),"촬영 몬스터 바닥 " + i);
                Check(spawn.TrySpawn(new EnemySpawnRequest(enemyDefinition,floor.point+Vector3.up*.035f,
                    Quaternion.LookRotation(-forward),actor.transform),out var enemy),"촬영 몬스터 생성 " + i);
                targets.Add(enemy);leased.Add(enemy);enemy.AI.enabled=false;enemy.Movement.StopMovement();
                enemy.Health.SetMaxHp(1000000,true);enemy.Health.ResetHealth();enemy.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;facingProbe.AddCaptureActor(enemy.transform);
                int targetIndex=i;
                Action<CombatHealth,DamageInfo> listener=(h,d)=>hits.Add(new{phase=facingProbe?.phase,time=Time.time,
                    unscaled=Time.unscaledTime,target=targetIndex,kind=d.playerAttackKind.ToString(),
                    primary=d.triggersOnHitEffects,hp=h.CurrentHp});
                listeners.Add(listener);enemy.Health.OnDamaged+=listener;
            }
            yield return PreparePresentationCase(targets,2f,3.2f);
            // Warm the zero-gauge attack path as well as the charged effect variants.
            SetPresentationGauge(energy,0); facingProbe.phase="combat_warmup";
            Send(false,false,true);
            float zeroWarmUntil=Time.time+4.5f;
            var warmCheckedSteps=new HashSet<int>();
            while(Time.time<zeroWarmUntil)
            {
                if(melee.IsAttackInProgress)
                {
                    int warmIndex=Field<int>(melee,"comboStepIndex");
                    if(warmIndex>=0 && warmIndex<expectedComboClips.Length && warmCheckedSteps.Add(warmIndex))
                        Check(Field<AnimationClip>(melee,"activeAttackAnimationClip")==expectedComboClips[warmIndex],
                            "촬영 비교검사 예열 " + (warmIndex+1));
                }
                yield return null;
            }
            Send();yield return PresentationAttackEnd();
            yield return PreparePresentationCase(targets,2f,3.2f);
            SetPresentationGauge(energy,100); facingProbe.phase="combat_warmup";
            Send(false,false,true);
            float warmUntil=Time.time+4.5f, warmTimeout=Time.unscaledTime+150;
            while(Time.time<warmUntil && Time.unscaledTime<warmTimeout) yield return null;
            Send(); yield return PresentationAttackEnd();
            yield return PreparePresentationCase(targets,2f,3.2f);
            SetPresentationGauge(energy,100); facingProbe.phase="combat_warmup";
            yield return StartFocusHeavy("촬영 효과 예열 실제 강공");
            yield return PresentationAttackEnd(); yield return Wait(.5f);
            hits.Clear(); Progress("presentation effects warmed");
            foreach (int gauge in new[]{0,50,100})
            {
                yield return PreparePresentationCase(targets,2f,3.2f);
                SetPresentationGauge(energy,gauge);
                string phase="combat_combo_"+gauge;facingProbe.phase=phase;
                yield return Wait(.35f);
                Send(false,false,true);
                int previous=-1,count=0;double maximumAssertionSeconds=0;float limit=Time.time+15;
                while (count<4 && Time.time<limit)
                {
                    if (melee.IsAttackInProgress && Field<int>(melee,"comboStepIndex")!=previous)
                    {
                        double assertionStart=EditorApplication.timeSinceStartup;
                        previous=Field<int>(melee,"comboStepIndex");count++;
                        Check(Field<AnimationClip>(melee,"activeAttackAnimationClip")==expectedComboClips[previous],
                            phase+" 실제 콤보 " + (previous+1));
                        maximumAssertionSeconds=Math.Max(maximumAssertionSeconds,EditorApplication.timeSinceStartup-assertionStart);
                    }
                    yield return null;
                }
                Send();Check(count==4,phase+" 실제 입력4타 완주");
                yield return PresentationAttackEnd();yield return Wait(.6f);
                Check(hits.Any(h=>JsonConvert.SerializeObject(h).Contains(phase)),phase+" 실제 몬스터 타격");
                cases.Add(new{phase,startingGauge=gauge,endingGauge=energy.Amount,attacks=count,maximumAssertionSeconds});Progress(phase);
            }
            foreach (int gauge in new[]{0,50,100})
            {
                yield return PreparePresentationCase(targets,2f,3f);SetPresentationGauge(energy,gauge);
                string phase="combat_heavy_"+gauge;facingProbe.phase=phase;yield return Wait(.35f);
                yield return StartFocusHeavy(phase+" 제품 강공 수락");
                yield return PresentationAttackEnd();yield return Wait(.6f);
                Check(hits.Any(h=>JsonConvert.SerializeObject(h).Contains(phase)),phase+" 실제 몬스터 타격");
                cases.Add(new{phase,startingGauge=gauge,endingGauge=energy.Amount});Progress(phase);
            }
            foreach (bool heavy in new[]{false,true})
            {
                yield return PreparePresentationCase(targets,5.7f,4.1f);SetPresentationGauge(energy,heavy?100:50);
                string phase=heavy?"combat_dash_heavy":"combat_dash_light";facingProbe.phase=phase;
                yield return Wait(.35f);yield return StartDodge(false,!heavy,heavy);Send();
                float limit=Time.time+3;
                while(evade.IsEvading && Time.time<limit)yield return null;
                yield return Frames(1);
                Check(melee.IsAttackInProgress,phase+" 실제 대시 공격 인계");
                Check(melee.ActiveDodgeFollowUp==(heavy?PlayerDodgeFollowUpKind.Heavy:PlayerDodgeFollowUpKind.Light),phase+" 전용 슬롯");
                yield return PresentationAttackEnd();yield return Wait(.6f);
                Check(hits.Any(h=>JsonConvert.SerializeObject(h).Contains(phase)),phase+" 실제 몬스터 타격");
                cases.Add(new{phase,startingGauge=heavy?100:50,endingGauge=energy.Amount});Progress(phase);
            }
            // The Hideout elite replaces the small combat actors for the actual parry shot.
            ReleasePresentationTargets(targets,listeners);
            Vector3 elitePoint=origin+forward*Mathf.Max(1.1f,ability.Range*.85f);
            Check(Physics.Raycast(elitePoint+Vector3.up*4,Vector3.down,out var eliteFloor,9,
                LayerMask.GetMask("Default","Environment","Ground")),"하이드아웃 엘리트 촬영 바닥");
            Check(spawn.TrySpawn(new EnemySpawnRequest(parryDefinition,eliteFloor.point+Vector3.up*.035f,
                Quaternion.LookRotation(-forward),actor.transform),out var elite),"하이드아웃 엘리트 실제 모델");
            targets.Add(elite);leased.Add(elite);elite.AI.enabled=false;elite.Movement.StopMovement();
            elite.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;facingProbe.AddCaptureActor(elite.transform);
            Action<CombatHealth,DamageInfo> eliteListener=(h,d)=>hits.Add(new{phase=facingProbe?.phase,time=Time.time,
                unscaled=Time.unscaledTime,target=0,kind=d.playerAttackKind.ToString(),primary=d.triggersOnHitEffects,hp=h.CurrentHp});
            listeners.Add(eliteListener);elite.Health.OnDamaged+=eliteListener;
            yield return PreparePresentationCase(targets,Mathf.Max(1.1f,ability.Range*.85f),4.3f);

            SetPresentationGauge(energy,100);facingProbe.phase="combat_parry";
            var fixture=ScriptableObject.CreateInstance<EnemyAbilitySet>();fixtures.Add(fixture);
            var serialized=new SerializedObject(fixture);serialized.FindProperty("abilitySetId").stringValue="presentation-owned-parry";
            var entries=serialized.FindProperty("abilities");entries.arraySize=1;entries.GetArrayElementAtIndex(0).objectReferenceValue=ability;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var attackingEnemy=targets[0];attackingEnemy.AbilityController.Configure(fixture,.1f,1);
            int successes=actor.GetComponent<PlayerParryController>().SuccessCount;
            yield return Wait(.35f);Check(attackingEnemy.AbilityController.TryStart(actor.transform),"촬영 실제 몬스터 패링 공격 시작");
            float threatLimit=Time.time+5;
            while(!attackingEnemy.AbilityController.IsParryThreatTo(actor.GetComponent<CombatTarget>()) && Time.time<threatLimit)yield return null;
            Check(attackingEnemy.AbilityController.IsParryThreatTo(actor.GetComponent<CombatTarget>()),"촬영 실제 패링 위협");
            yield return StartDodge(false);Send(false,false,false,true);yield return Frames(2);Send();
            float parryLimit=Time.time+8;bool parryPose=false;
            while((evade.IsEvading || melee.IsAttackInProgress || melee.IsDashHeavyWindupActive) && Time.time<parryLimit)
            {parryPose|=melee.IsHeavyParryMotionActive;yield return null;}
            Check(actor.GetComponent<PlayerParryController>().SuccessCount==successes+1 && parryPose,"촬영 실제 패링 성공과 전용 모션");
            yield return Wait(.8f);
            cases.Add(new{phase="combat_parry",startingGauge=100,endingGauge=energy.Amount,actualSuccess=true});Progress("combat_parry");
            File.WriteAllText(Path.Combine(output,"Presentation.json"),JsonConvert.SerializeObject(new{status="PASS",
                actualInput=true,actualHits=true,rendererOnlyStudio=true,monsters=3,parryMonsters=1,view="live gameplay Camera.main",combatMonster=enemyDefinition.name,
                parryMonster=parryDefinition.name,parryAbility=ability.name,preloadSeconds,cases,hits},Formatting.Indented));
        }
        finally
        {
            Send();
            if(facingProbe!=null)
            {
                File.WriteAllText(Path.Combine(output,"FacingPoses.json"),JsonConvert.SerializeObject(facingProbe.frames));
                UnityEngine.Object.DestroyImmediate(facingProbe);facingProbe=null;
            }
            ReleasePresentationTargets(targets,listeners);
        }
    }
    static void ReleasePresentationTargets(List<EnemyActor> targets,List<Action<CombatHealth,DamageInfo>> listeners)
    {
        for(int i=0;i<targets.Count;i++)if(targets[i]!=null)
        {
            if(i<listeners.Count)targets[i].Health.OnDamaged-=listeners[i];
            spawn.Release(targets[i]);leased.Remove(targets[i]);
        }
        targets.Clear();listeners.Clear();
    }
    static float PresentationEnemyOffset(int i,int count) => count==1||i==1?0:i==0?1.6f:3f;
    static float PresentationEnemyLateral(int i,int count) => count==1||i==1?0:i==0?-.7f:.7f;
    static IEnumerator PreparePresentationCase(List<EnemyActor> targets,float distance,float cameraSize)
    {
        facingProbe.phase="reset";yield return Reset();
        Vector3 right=Vector3.Cross(Vector3.up,forward);
        for(int i=0;i<targets.Count;i++)
        {
            var enemy=targets[i];enemy.AbilityController.Cancel();enemy.Movement.StopMovement();
            enemy.Health.SetMaxHp(1000000,true);enemy.Health.ResetHealth();
            ActorTeleportUtility.TeleportSafely(enemy.transform,origin+forward*(distance+PresentationEnemyOffset(i,targets.Count))+right*PresentationEnemyLateral(i,targets.Count),Quaternion.LookRotation(-forward));
        }

        yield return Wait(.5f);
    }
    static IEnumerator PresentationAttackEnd()
    {
        float limit=Time.time+10;
        while((melee.IsAttackInProgress || evade.IsEvading || melee.IsDashHeavyWindupActive) && Time.time<limit)yield return null;
        Check(!melee.IsAttackInProgress && !evade.IsEvading && !melee.IsDashHeavyWindupActive,"촬영 행동 정상 종료");
        yield return WaitForSwordIdle(actor.Equipment.CurrentWeaponData.GetMeleeDefinition().animationProfile.combatIdleClip);
    }
}
