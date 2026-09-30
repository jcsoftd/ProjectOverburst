using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

public static partial class CombatBalanceGoal3Verifier
{
    sealed class BalanceInput : IDisposable
    {
        public bool Weak, Heavy, Roll, Right, Interact;
        public Vector3 Aim;
        readonly Keyboard keyboard=InputSystem.AddDevice<Keyboard>("BalanceKeyboard");
        readonly Mouse mouse=InputSystem.AddDevice<Mouse>("BalanceMouse");
        readonly InputSettings original=InputSystem.settings;
        readonly InputSettings fixture;
        readonly PlayerInputFacade facade;
        readonly UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>? devices;
        public BalanceInput(PlayerInputFacade p)
        {
            facade=p;devices=p.RuntimeAsset.devices;p.RuntimeAsset.devices=new InputDevice[]{keyboard,mouse};
            fixture=Object.Instantiate(original);
            fixture.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            fixture.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings=fixture;
            InputSystem.onBeforeUpdate+=Drive;
        }
        void Drive()
        {
            if(InputState.currentUpdateType!=InputUpdateType.Dynamic)return;
            var keys=new List<UnityEngine.InputSystem.Key>();if(Interact)keys.Add(UnityEngine.InputSystem.Key.F);if(Roll)keys.Add(UnityEngine.InputSystem.Key.LeftShift);if(Right)keys.Add(UnityEngine.InputSystem.Key.D);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys.ToArray()));
            var state=new MouseState {position=Camera.main!=null?(Vector2)Camera.main.WorldToScreenPoint(Aim):Vector2.zero};
            if(Weak)state=state.WithButton(MouseButton.Left);if(Heavy)state=state.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(mouse,state);
        }
        public void Dispose(){InputSystem.onBeforeUpdate-=Drive;facade.RuntimeAsset.devices=devices;InputSystem.RemoveDevice(keyboard);InputSystem.RemoveDevice(mouse);InputSystem.settings=original;Object.Destroy(fixture);}
    }
    static void WarpPlayer(PlayerInputFacade player,Vector3 p,Vector3 facing)
    {
        var cc=player.GetComponent<CharacterController>();bool on=cc!=null&&cc.enabled;
        if(on)cc.enabled=false;player.transform.position=p;player.transform.rotation=Quaternion.LookRotation(facing);
        if(on)cc.enabled=true;Physics.SyncTransforms();
    }
    static IEnumerator VerifyGoal6()
    {
        EnemySpawnService spawn=null;EnemyThemeDebugUI ui=null;MeleeRuntime melee=null;BalanceInput input=null;
        var leased=new List<EnemyActor>();
        try {
            while(PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||PersistentSceneFlow.Instance.CurrentSubSceneName!="HideoutScene")yield return null;
            Check(Overburst.Persistence.AccountBootstrap.SaveDirectory.StartsWith(Output,StringComparison.OrdinalIgnoreCase),"Account isolation");
            deadline=EditorApplication.timeSinceStartup+600;
            var player=PlayerInputFacade.Current;var actor=PlayerContext.GetOrCreate().CurrentActor;
            Check(Overburst.Persistence.AccountGameplaySession.Current.ExecuteState("balance-level-one",s=>{s.level=1;s.experience=0;}),"Reset isolated progression");
            var weapon=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            actor.Health.SetMaxHp(1000000,true);
            ui=Object.FindFirstObjectByType<EnemyThemeDebugUI>(FindObjectsInactive.Include);ui.gameObject.SetActive(true);if(!ui.InArena)ui.ToggleArena();
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform,out spawn),"Spawn service");
            foreach(var t in ui.tables)Check(spawn.RegisterAdditionalCatalog(t.Catalog,out string error),error);
            var defs=ui.tables.SelectMany(t=>t.Entries).Select(e=>e.definition).Distinct().ToArray();
            var medium=defs.First(d=>d.EnemyId.Contains("Scolokarck"));
            var origin=player.transform.position;
            input=new BalanceInput(player);melee=player.GetComponent<MeleeRuntime>();
            foreach(var element in new[]{WeaponElement.Fire,WeaponElement.Ice,WeaponElement.Electric,WeaponElement.Dark,WeaponElement.Light})
            {
                Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common,element:element)),"Equip "+element);
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);melee.SetManualInputEnabled(true);
                WarpPlayer(player,origin,Vector3.forward);yield return null;
                Check(spawn.TrySpawn(new EnemySpawnRequest(medium,origin+Vector3.forward*1.7f,Quaternion.LookRotation(Vector3.back),player.transform),out var enemy),"Spawn medium");
                leased.Add(enemy);enemy.AI.enabled=false;enemy.Movement.StopMovement();
                enemy.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var energy=player.GetComponent<OverburstElementEnergy>()??player.gameObject.AddComponent<OverburstElementEnergy>();energy.Clear();
                float started=Time.unscaledTime,originalHp=enemy.Health.CurrentHp;int weakHits=0;float sumBudget=0;
                void Hit(CombatHealth h,DamageInfo d){if((d.playerAttackKind&PlayerAttackKind.Weak)!=0){weakHits++;sumBudget+=Mathf.Max(0,d.weakKnockbackDistance);}}
                enemy.Health.OnDamaged+=Hit;
                Vector3 startEnemy=enemy.transform.position,startPlayer=player.transform.position;
                input.Weak=true;
                while(energy.Amount<100&&!enemy.Health.IsDead&&Time.unscaledTime<started+20){input.Aim=enemy.transform.position;yield return null;}
                input.Weak=false;
                float finish=Time.unscaledTime+8;while(melee.IsAttackInProgress){Check(Time.unscaledTime<finish,"Weak finish");yield return null;}
                float prepHp=enemy.Health.CurrentHp,charge=energy.Amount;
                results.Add(new{element=element.ToString(),phase="prep",originalHp,prepHp,charge,weakHits,sumBudget,playerTravel=Vector3.Distance(startPlayer,player.transform.position),enemyTravel=Vector3.Distance(startEnemy,enemy.transform.position)});
                Check(weakHits>0&&charge==100&&!enemy.Health.IsDead,"Medium must survive full preparation "+element);
                input.Aim=enemy.transform.position;input.Heavy=true;float startHeavy=Time.unscaledTime;
                while(!melee.IsHeavyAttackInProgress){Check(Time.unscaledTime<startHeavy+3,"Input heavy not started");yield return null;}
                input.Heavy=false;
                finish=Time.unscaledTime+10;while(melee.IsAttackInProgress){Check(Time.unscaledTime<finish,"Heavy finish");yield return null;}
                float delayed=Time.unscaledTime+2;while(Time.unscaledTime<delayed&&!enemy.Health.IsDead)yield return null;
                results.Add(new{element=element.ToString(),phase="heavy",dead=enemy.Health.IsDead,remainingHp=enemy.Health.CurrentHp,energyAfter=energy.Amount,elapsed=Time.unscaledTime-started});
                Check(energy.Amount==0,"Heavy consumes original charge");
                Check(enemy.Health.IsDead,"Full heavy should finish prepared medium "+element);
                enemy.Health.OnDamaged-=Hit;if(enemy.IsLeased)spawn.Release(enemy);leased.Remove(enemy);yield return null;
            }
            // A reward/loadout callback may end the action synchronously during derived Light damage.
            WarpPlayer(player,origin,Vector3.forward);
            Check(spawn.TrySpawn(new EnemySpawnRequest(medium,origin+Vector3.forward*1.7f,Quaternion.LookRotation(Vector3.back),player.transform),out var cancelEnemy),"Cancel target");
            leased.Add(cancelEnemy);cancelEnemy.AI.enabled=false;cancelEnemy.Movement.StopMovement();cancelEnemy.Health.SetMaxHp(100000,true);
            var cancelEnergy=player.GetComponent<OverburstElementEnergy>();cancelEnergy.Clear();
            for(int i=0;i<10;i++)cancelEnergy.RecordConfirmedHit(cancelEnergy.WeaponInstanceId,cancelEnergy.Element,8000+i,1);
            bool cancelledFromDerived=false;
            void CancelOnDerived(CombatHealth h,DamageInfo d){if(d.playerAttackKind==PlayerAttackKind.Elemental){cancelledFromDerived=true;melee.CancelCurrentAttackState();}}
            cancelEnemy.Health.OnDamaged+=CancelOnDerived;input.Aim=cancelEnemy.transform.position;
            Check(melee.TryStartHeavyAttack(Vector3.forward)==WeaponActionResult.Accepted,"Cancel fixture heavy");
            float cancelDeadline=Time.unscaledTime+8;
            while(!cancelledFromDerived&&Time.unscaledTime<cancelDeadline)yield return null;
            Check(cancelledFromDerived&&!melee.IsAttackInProgress,"Light derived callback cancellation");
            cancelEnemy.Health.OnDamaged-=CancelOnDerived;if(cancelEnemy.IsLeased)spawn.Release(cancelEnemy);leased.Remove(cancelEnemy);
            results.Add(new{lightDerivedCancellation=true,playerLevel=PlayerProgression.CurrentLevel});
            // Real Gameplay input route, followed by the production evade damage gate.
            WarpPlayer(player,origin,Vector3.forward);input.Roll=true;
            var evade=player.GetComponent<PlayerEvadeController>();float rollUntil=Time.unscaledTime+3;
            while(!evade.IsInvincible){Check(Time.unscaledTime<rollUntil,"Synthetic Shift roll route");yield return null;}
            float hp=actor.Health.CurrentHp;actor.Health.TakeDamage(new DamageInfo(10,player.transform.position,null));
            Check(actor.Health.CurrentHp==hp,"Roll invulnerability did not reject damage");input.Roll=false;
            results.Add(new{inputRoll=true,damageCancelled=true});
            while(evade.IsEvading)yield return null;
            foreach(int size in new[]{10,50})
            {
                WarpPlayer(player,origin,Vector3.forward);
                for(int i=0;i<size;i++){
                    float angle=i*2.39996f,r=3.5f+Mathf.Sqrt(i)*1.4f;
                    Vector3 p=origin+new Vector3(Mathf.Cos(angle)*r,0,Mathf.Sin(angle)*r);
                    var d=defs[i%defs.Length];
                    Check(spawn.TrySpawn(new EnemySpawnRequest(d,p,Quaternion.LookRotation(origin-p),player.transform),out var enemy),"Crowd spawn "+i);
                    leased.Add(enemy);
                }
                var frameTimes=new List<float>();int playerHits=0,enemyHits=0,strongPeak=0;
                void PlayerHit(CombatHealth h,DamageInfo d){playerHits++;}
                void EnemyHit(CombatHealth h,DamageInfo d){enemyHits++;}
                actor.Health.OnDamaged+=PlayerHit;foreach(var e in leased)e.Health.OnDamaged+=EnemyHit;
                float until=Time.unscaledTime+15,begin=Time.unscaledTime;
                while(Time.unscaledTime<until){
                    var closest=leased.Where(e=>e.IsLeased&&!e.Health.IsDead).OrderBy(e=>(e.transform.position-player.transform.position).sqrMagnitude).FirstOrDefault();
                    if(closest!=null)input.Aim=closest.transform.position;
                    var energy=player.GetComponent<OverburstElementEnergy>();bool heavy=energy!=null&&energy.Amount>=50;
                    input.Heavy=heavy;input.Weak=!heavy;
                    if(Time.unscaledTime>begin+2)frameTimes.Add(Time.unscaledDeltaTime*1000);
                    strongPeak=Mathf.Max(strongPeak,leased.Count(e=>e.IsLeased&&e.AbilityController.IsExecuting&&e.AbilityController.LastCommittedAbility!=null&&e.AbilityController.LastCommittedAbility.IsTelegraphedStrongAttack));
                    yield return null;
                }
                input.Weak=input.Heavy=false;melee.CancelCurrentAttackState();
                frameTimes.Sort();results.Add(new{crowd=size,playerHits,enemyHits,strongPeak,samples=frameTimes.Count,frameMedianMs=frameTimes[frameTimes.Count/2],frameP95Ms=frameTimes[(int)(frameTimes.Count*.95f)],scope="Editor wall frame time includes deliberate hitstop; not CPU/GPU profiler"});
                Check(enemyHits>0,"Crowd weapon collision absent");
                Check(strongPeak<=2,"Strong attacks exceeded reservation limit at common target");
                actor.Health.OnDamaged-=PlayerHit;foreach(var e in leased){e.Health.OnDamaged-=EnemyHit;if(e.IsLeased)spawn.Release(e);}leased.Clear();
            }
        } finally {input?.Dispose();melee?.CancelCurrentAttackState();if(spawn!=null)foreach(var e in leased)if(e!=null&&e.IsLeased)spawn.Release(e);if(ui!=null&&ui.InArena)ui.ToggleArena();}
    }
}
