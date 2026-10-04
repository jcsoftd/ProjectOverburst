using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MonsterWeakAttackClockVerifier
{
    public static string Run(string outputDirectory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("약공 시계 검사는 유휴 EditMode에서만 실행합니다.");
        string workspace=Directory.GetParent(Application.dataPath).Parent.FullName;
        string allowed=Path.GetFullPath(Path.Combine(workspace,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        outputDirectory=Path.GetFullPath(outputDirectory);
        if (!outputDirectory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("산출 경로가 아닙니다.");
        var checks=new List<object>(); int failed=0;
        void Check(string name,bool pass) { checks.Add(new {name,pass}); if (!pass) failed++; }
        var clock=new EnemyAttackClock();
        clock.Begin(10,false,0); clock.Observe(10,.1f,true,.6f);
        Check("same frame stale sample ignored",!clock.HasEntered && clock.NormalizedTime==0);
        clock.Observe(11,.1f,true,.2f);
        Check("native motion enters clock",clock.HasEntered && Mathf.Abs(clock.NormalizedTime-.2f)<.0001f);
        clock.Observe(12,.1f,true,.19f);
        Check("small blending regression is monotonic",Mathf.Abs(clock.NormalizedTime-.2f)<.0001f);
        clock.Observe(13,0,true,.2f);
        Check("paused animation cannot advance strike",Mathf.Abs(clock.NormalizedTime-.2f)<.0001f);
        clock.Observe(14,.1f,true,1.01f);
        Check("completion comes from motion",clock.State==EnemyAttackClock.Phase.Completed && clock.NormalizedTime==1);
        clock.Observe(15,1,false,0);
        Check("completed clock remains terminal",clock.State==EnemyAttackClock.Phase.Completed);
        clock.Begin(20,true,.8f);clock.Observe(21,.2f,true,.9f);
        Check("previous same state cannot deliver new hits",!clock.HasEntered && clock.NormalizedTime==0);
        clock.Observe(22,.1f,true,.1f);
        Check("restarted same state is a new execution",clock.HasEntered && Mathf.Abs(clock.NormalizedTime-.1f)<.0001f);
        clock.Observe(23,.1f,false,0);
        Check("missing motion fails without fallback",clock.State==EnemyAttackClock.Phase.Failed);
        clock.Begin(30,false,0);clock.Observe(31,.8f,false,0);
        Check("entry timeout never fabricates progress",clock.State==EnemyAttackClock.Phase.Failed && clock.NormalizedTime==0);
        clock.Begin(40,false,0);clock.Observe(41,.1f,true,float.NaN);
        Check("invalid native sample fails",clock.State==EnemyAttackClock.Phase.Failed);
        clock.Begin(50,false,0);clock.Observe(51,.1f,true,.7f);clock.Observe(52,.1f,true,.1f);
        Check("unexpected animation restart fails",clock.State==EnemyAttackClock.Phase.Failed);
        clock.Cancel();Check("cancellation clears progress",clock.State==EnemyAttackClock.Phase.Cancelled && clock.NormalizedTime==0);

        VerifySelection(Check);
        VerifyNativeAnimator(Check);
        Directory.CreateDirectory(outputDirectory);
        string path=Path.Combine(outputDirectory,"clock-selection-results.json");
        File.WriteAllText(path,JsonConvert.SerializeObject(new {status=failed==0?"PASS_SCOPED":"FAIL",checks,failed,
            actualGameProfilesAttached=false,physicsQueue="NOT_IMPLEMENTED",boundedMotion="NOT_IMPLEMENTED",gamePlay="NOT_RUN",audioApplied=false},Formatting.Indented));
        if (failed>0) throw new InvalidOperationException("약공 시계/선택 검사 실패: "+failed);
        return path;
    }

    private static void VerifySelection(Action<string,bool> check)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var owned=new List<UnityEngine.Object>();
        try
        {
            var clip=new AnimationClip {name="V3_SelectionFixture",frameRate=30}; owned.Add(clip);
            clip.SetCurve("",typeof(Transform),"localPosition.x",AnimationCurve.Linear(0,0,1,0));
            EnemyAbilityDefinition Ability(string id,EnemyWeakAttackMotionPolicy? policy,int priority)
            {
                var ability=ScriptableObject.CreateInstance<EnemyAbilityDefinition>();owned.Add(ability);
                ability.Configure(id,"Attack1",10,1.6f,1f,120,1f,.4f,.4f,1f,1f,false,animationDuration:1f);
                ability.ConfigureUsePolicy(0,priority);
                if (policy.HasValue)
                {
                    var profile=ScriptableObject.CreateInstance<EnemyWeakAttackExecutionProfile>();owned.Add(profile);
                    bool advance=policy==EnemyWeakAttackMotionPolicy.ShortAdvance;
                    profile.Configure(id,clip,clip,new Vector2(0,1),policy.Value,1.6f,advance?.5f:0,
                        advance?new Vector2(.1f,.6f):Vector2.zero,advance?AnimationCurve.Linear(0,0,1,1):null,"");
                    ability.ConfigureWeakAttackExecution(profile);
                }
                return ability;
            }
            var stationary=Ability("stationary",EnemyWeakAttackMotionPolicy.Stationary,0);
            var advance=Ability("advance",EnemyWeakAttackMotionPolicy.ShortAdvance,99);
            var jump=Ability("jump",EnemyWeakAttackMotionPolicy.VisualJump,1);
            var legacy=Ability("legacy",null,201);
            var strong=Ability("strong",null,200);
            var serialized=new SerializedObject(strong);serialized.FindProperty("telegraphedStrongAttack").boolValue=true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var set=ScriptableObject.CreateInstance<EnemyAbilitySet>();owned.Add(set);
            var actor=new GameObject("V3_SelectionFixture");owned.Add(actor);SceneManager.MoveGameObjectToScene(actor,scene);
            var target=new GameObject("V3_TargetFixture");owned.Add(target);SceneManager.MoveGameObjectToScene(target,scene);
            var executor=actor.AddComponent<MonsterWeakAttackSelectionFixtureExecutor>();
            var controller=actor.AddComponent<EnemyAbilityController>();
            var binding=new SerializedObject(controller);
            var executors=binding.FindProperty("executors");executors.arraySize=1;executors.GetArrayElementAtIndex(0).objectReferenceValue=executor;
            binding.ApplyModifiedPropertiesWithoutUndo();
            var method=typeof(EnemyAbilityController).GetMethod("TrySelectAbility",BindingFlags.NonPublic|BindingFlags.Instance);
            EnemyAbilityDefinition Select(params EnemyAbilityDefinition[] abilities)
            {
                set.Configure("selection-fixture",abilities);controller.Configure(set,1,1);
                var args=new object[]{target.transform,null};
                if (!(bool)method.Invoke(controller,args)) return null;
                return (EnemyAbilityDefinition)args[1].GetType().GetProperty("Ability").GetValue(args[1]);
            }
            EnemyAbilityDefinition SelectCurrent()
            {
                var args=new object[]{target.transform,null};
                if (!(bool)method.Invoke(controller,args)) return null;
                return (EnemyAbilityDefinition)args[1].GetType().GetProperty("Ability").GetValue(args[1]);
            }
            target.transform.position=Vector3.forward*1.59f;
            check("stationary family before advance priority",Select(stationary,advance)==stationary);
            check("readiness agrees with selection",controller.HasAvailableAbility(target.transform));
            target.transform.position=Vector3.forward*1.61f;
            check("slightly outside selects advance",Select(stationary,advance)==advance);
            target.transform.position=Vector3.forward*2.11f;
            check("outside bounded approach has no candidate",Select(stationary,advance)==null && !controller.HasAvailableAbility(target.transform));
            target.transform.position=Vector3.forward*1.59f;
            executor.Blocked.Add(stationary);
            check("blocked stationary does not hide ready advance",Select(stationary,advance)==advance);
            executor.Blocked.Clear();Select(stationary,advance);
            var cooldowns=(Dictionary<EnemyAbilityDefinition,float>)typeof(EnemyAbilityController)
                .GetField("readyTimeByAbility",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller);
            cooldowns[stationary]=Time.time+100;
            check("cooling stationary does not hide advance",SelectCurrent()==advance);
            check("priority still chooses within stationary family",Select(stationary,jump,advance)==jump);
            check("legacy candidate priority retained",Select(stationary,advance,legacy)==legacy);
            check("first attack still obeys strong cadence",Select(stationary,advance,strong)==stationary && controller.IsStrongAttackLocked);
            var count=typeof(EnemyAbilityController).GetMethod("CountCommittedAttack",BindingFlags.NonPublic|BindingFlags.Instance);
            count.Invoke(controller,new object[]{stationary});
            check("stationary family does not remove unlocked strong",SelectCurrent()==strong && !controller.IsStrongAttackLocked);
            count.Invoke(controller,new object[]{strong});
            count.Invoke(controller,new object[]{stationary});count.Invoke(controller,new object[]{stationary});
            check("medium strong remains locked after two weak commits",controller.IsStrongAttackLocked);
            count.Invoke(controller,new object[]{stationary});
            check("medium strong opens after three weak commits",!controller.IsStrongAttackLocked && SelectCurrent()==strong);
            controller.BeginStrongOnlyPass();
            check("strong-only pass survives family selection",SelectCurrent()==strong);controller.EndStrongOnlyPass();
            count.Invoke(controller,new object[]{strong});controller.BeginStrongOnlyPass();
            check("locked strong-only pass cannot substitute weak",SelectCurrent()==null);controller.EndStrongOnlyPass();
            check("read-only availability leaves cadence unchanged",controller.HasAvailableAbility(target.transform) && controller.IsStrongAttackLocked);
        }
        finally
        {
            for (int i=owned.Count-1;i>=0;i--) if (owned[i]!=null) UnityEngine.Object.DestroyImmediate(owned[i]);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void VerifyNativeAnimator(Action<string,bool> check)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var owned=new List<UnityEngine.Object>();
        string folder="Assets/Editor/Testers/Characters/V3ClockFixture_"+Guid.NewGuid().ToString("N");
        try
        {
            AssetDatabase.CreateFolder("Assets/Editor/Testers/Characters",Path.GetFileName(folder));
            AnimationClip Clip(string name,float distance)
            {
                var clip=new AnimationClip {name=name,frameRate=30};
                clip.SetCurve("Visual",typeof(Transform),"localPosition.x",AnimationCurve.Linear(0,0,1,distance));
                AssetDatabase.CreateAsset(clip,folder+"/"+name+".anim");return clip;
            }
            var correct=Clip("CorrectAttack",1);var wrong=Clip("OtherAttack",2);
            var controller=AnimatorController.CreateAnimatorControllerAtPath(folder+"/Controller.controller");
            var machine=controller.layers[0].stateMachine;
            var state=machine.AddState("Attack_1");state.motion=correct;machine.defaultState=state;
            var other=machine.AddState("Attack_2");other.motion=wrong;
            controller.AddParameter("Attack1",AnimatorControllerParameterType.Trigger);
            AssetDatabase.SaveAssetIfDirty(controller);
            var root=new GameObject("NativeClockFixture");owned.Add(root);SceneManager.MoveGameObjectToScene(root,scene);
            var visual=new GameObject("Visual");visual.transform.SetParent(root.transform,false);
            var animator=root.AddComponent<Animator>();animator.runtimeAnimatorController=controller;
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(0);
            var bridge=root.AddComponent<EnemyAnimationBridge>();bridge.SetAnimator(animator);
            check("native selected motion is available",bridge.CanPlayAttackMotion("Attack1",correct));
            animator.Play("Attack_1",0,.3f);animator.Update(0);
            check("native exact clip sample",bridge.TryGetAttackMotionTime("Attack1",correct,out float sample) && Mathf.Abs(sample-.3f)<.001f);
            check("same state wrong clip is rejected",!bridge.TryGetAttackMotionTime("Attack1",wrong,out _));
            var profile=ScriptableObject.CreateInstance<EnemyWeakAttackExecutionProfile>();owned.Add(profile);
            profile.Configure("native-clock-fixture",correct,correct,new Vector2(0,correct.length),EnemyWeakAttackMotionPolicy.Stationary,1.6f,0,Vector2.zero,null,"");
            var melee=root.AddComponent<EnemyMeleeAttackController>();
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
            typeof(EnemyMeleeAttackController).GetField("animationBridge",flags).SetValue(melee,bridge);
            typeof(EnemyMeleeAttackController).GetField("activeWeakExecution",flags).SetValue(melee,profile);
            typeof(EnemyMeleeAttackController).GetField("weakClockTrigger",flags).SetValue(melee,"Attack1");
            var executionClock=(EnemyAttackClock)typeof(EnemyMeleeAttackController).GetField("weakAttackClock",flags).GetValue(melee);
            executionClock.Begin(0,false,0);
            typeof(EnemyMeleeAttackController).GetMethod("ObserveWeakClock",flags).Invoke(melee,null);
            check("actual melee fixed sample shares native clock",melee.HasEnteredWeakAttack && Mathf.Abs(melee.WeakAttackNormalizedTime-.3f)<.001f);
            melee.CancelAttack();
            check("actual melee cancel clears selected clock",!melee.HasEnteredWeakAttack && melee.ActiveWeakExecution==null && melee.WeakAttackNormalizedTime==0);
            var ability=ScriptableObject.CreateInstance<EnemyAbilityDefinition>();owned.Add(ability);
            ability.Configure("last-frame-fixture","Attack1",10,1.6f,1f,.4f,1f);
            ability.ConfigureAdditionalHits(.6f,.8f);ability.ConfigureWeakAttackExecution(profile);
            typeof(EnemyMeleeAttackController).GetField("attackSequenceId",flags).SetValue(melee,EnemyAttackSequence.Next());
            var routine=(System.Collections.IEnumerator)typeof(EnemyMeleeAttackController).GetMethod("WeakAttackRoutine",flags)
                .Invoke(melee,new object[]{"Attack1",ability,null,true});
            bool requested=routine.MoveNext();
            executionClock.Begin(Time.frameCount,false,0);executionClock.Observe(Time.frameCount+1,.1f,true,1f);
            bool finished=!routine.MoveNext();
            int finalPhase=(int)typeof(EnemyMeleeAttackController).GetField("attackPhaseIndex",flags).GetValue(melee);
            check("terminal sample preserves all final crossed strikes",requested && finished && finalPhase==2);


            bool previous=bridge.TryGetAttackMotionTime("Attack1",correct,out float baseline);
            var clock=new EnemyAttackClock();clock.Begin(10,previous,baseline);
            animator.CrossFadeInFixedTime("Attack_1",.1f,0,0);animator.Update(.02f);
            bool present=bridge.TryGetAttackMotionTime("Attack1",correct,out sample);
            clock.Observe(11,.02f,present,sample);
            check("native incoming restart beats outgoing sample",present && sample<baseline && clock.HasEntered);
            animator.Play("Attack_2",0,.5f);animator.Update(0);
            check("native other attack cannot continue selected clock",!bridge.TryGetAttackMotionTime("Attack1",correct,out _));
            controller.RemoveParameter(0);
            check("missing trigger rejected before execution",!bridge.CanPlayAttackMotion("Attack1",correct));

        }
        finally
        {
            for (int i=owned.Count-1;i>=0;i--) if (owned[i]!=null) UnityEngine.Object.DestroyImmediate(owned[i]);
            EditorSceneManager.ClosePreviewScene(scene);
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        }
        check("native fixture assets removed",!AssetDatabase.IsValidFolder(folder));
    }
}

// This executor exercises the real selection/cadence controller while isolating
// animation, physics and hit resolution from an EditMode contract test.
public sealed class MonsterWeakAttackSelectionFixtureExecutor : EnemyAbilityExecutor
{
    public readonly HashSet<EnemyAbilityDefinition> Blocked=new HashSet<EnemyAbilityDefinition>();
    public override bool IsExecuting => false;
    public override bool Supports(EnemyAbilityDefinition ability) => ability!=null;
    public override bool CanStart(EnemyAbilityDefinition ability,Transform target) => !Blocked.Contains(ability);
    public override bool TryStart(EnemyAbilityDefinition ability,int index,Transform target) => false;
    public override float ResolveCooldown(float cooldown) => cooldown;
    public override void Cancel() { }
    public override void ResetForReuse() { Blocked.Clear(); }
}
