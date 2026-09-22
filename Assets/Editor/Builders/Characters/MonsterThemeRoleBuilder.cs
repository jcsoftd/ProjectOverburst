using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// The three charging silhouettes keep a committed gap closer; other species use their own ground attacks.
public static class MonsterThemeRoleBuilder
{
    [MenuItem("OVERBURST/Enemies/Themes/Apply Combat Roles")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode");
        int attacks=0,chargers=0;
        foreach(string guid in AssetDatabase.FindAssets("t:EnemyDefinition",new[]{MonsterThemeCombatBuilder.Root+"/Definitions"}))
        {
            var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            bool charger=definition.EnemyId=="SpiderBrood_Carcinoptera" || definition.EnemyId=="VenomBrood_Kupolojuve_Tint_Orange" || definition.EnemyId=="PrimalHunt_Occisodonte";
            var animation=definition.AnimationProfile;var set=definition.AbilitySet;
            var selected=new List<EnemyAbilityDefinition>();var clips=new List<AnimationClip>();
            var optional=Enumerable.Range(0,animation.OptionalClipCount).Select(animation.GetOptionalClip).ToList();
            for(int i=0;i<set.Count;i++)
            {
                var ability=set.GetAbility(i);var clip=animation.GetAttackClip(i);
                bool keep=ability.ExecutionMode!=EnemyAbilityExecutionMode.Charge || charger;
                if(definition.EnemyId=="VenomBrood_Kupolojuve_Tint_Orange" && clip.name=="JumpClawsAttack")keep=false;
                if(!keep){if(!optional.Contains(clip))optional.Add(clip);continue;}
                if(ability.ExecutionMode==EnemyAbilityExecutionMode.Charge)
                {
                    var data=new SerializedObject(ability);
                    data.FindProperty("cooldown").floatValue=definition.EnemyId=="PrimalHunt_Occisodonte"?9f:7f;
                    data.FindProperty("weight").floatValue=definition.EnemyId=="PrimalHunt_Occisodonte"?.25f:.4f;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                selected.Add(ability);clips.Add(clip);
            }
            if(definition.EnemyId=="VenomBrood_Venodonte_Tint3" && !selected.Any(a=>a.ExecutionMode==EnemyAbilityExecutionMode.Projectile))
            {
                var clip=optional.First(c=>c.name=="AcidShot");
                string path=MonsterThemeCombatBuilder.Root+"/Abilities/"+definition.EnemyId+"_AcidShot.asset";
                var acid=AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(path);
                if(acid==null){acid=ScriptableObject.CreateInstance<EnemyAbilityDefinition>();AssetDatabase.CreateAsset(acid,path);}
                acid.Configure(definition.EnemyId+"_AcidShot","AttackAcid",5f,7f,.4f,80f,4.5f,clip.length*.52f,.52f,clip.length,.8f,false,EnemyAbilityExecutionMode.Projectile,1.5f,true,clip.length);
                acid.ConfigureUsePolicy(2.8f,1);EditorUtility.SetDirty(acid);
                selected.Add(acid);clips.Add(clip);optional.Remove(clip);
                var controller=(AnimatorController)animation.RuntimeController;var machine=controller.layers[0].stateMachine;
                if(!controller.parameters.Any(p=>p.name=="AttackAcid"))controller.AddParameter("AttackAcid",AnimatorControllerParameterType.Trigger);
                var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Attack_Acid");
                if(state==null)
                {
                    state=machine.AddState("Attack_Acid");
                    var enter=machine.AddAnyStateTransition(state);enter.hasExitTime=false;enter.duration=.06f;enter.canTransitionToSelf=false;enter.AddCondition(AnimatorConditionMode.If,0,"AttackAcid");
                    var exit=state.AddTransition(machine.states.First(s=>s.state.name=="Locomotion").state);exit.hasExitTime=true;exit.exitTime=.98f;exit.duration=.08f;
                }
                state.motion=clip;state.speedParameter="AttackAnimSpeed";state.speedParameterActive=true;EditorUtility.SetDirty(controller);
            }
            if(selected.Count==0)throw new InvalidOperationException("Empty role: "+definition.EnemyId);
            set.Configure(definition.EnemyId,selected.ToArray());EditorUtility.SetDirty(set);
            animation.Configure(animation.ProfileId,animation.RuntimeController,animation.Idle,animation.Walk,animation.Run,clips.ToArray(),animation.Hit,animation.Death,optional.ToArray(),
                Enumerable.Range(0,animation.ExcludedRootMotionClipCount).Select(animation.GetExcludedRootMotionClipPath).ToArray());EditorUtility.SetDirty(animation);
            if(definition.EnemyId=="VenomBrood_Venodonte_Tint3")
            {
                var data=new SerializedObject(definition.BehaviorProfile);
                data.FindProperty("preferredMinDistance").floatValue=2.5f;
                data.FindProperty("preferredApproachDistance").floatValue=3.7f;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            attacks+=selected.Count;if(selected.Any(a=>a.ExecutionMode==EnemyAbilityExecutionMode.Charge))chargers++;
        }
        AssetDatabase.SaveAssets();Debug.Log("[MonsterThemeRoles] attacks="+attacks+" chargingSpecies="+chargers+" / 14; inactive source assets preserved");
    }
}
