using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// The three charging silhouettes keep a committed gap closer; other species use their own ground attacks.
// 2026-10-01: 역할 배정(돌진 공격 제외, Venodonte Tint3 AcidShot 추가, 돌진 쿨다운·가중치, 사거리 거리, VenomBrood 가중치)은
// 새로 만든 액터의 제작 기본값이다. 기존 액터의 능력 세트·쿨다운·가중치·거리는 밸런스와 공격 조정 작업이 원본이므로
// 일반 메뉴는 차이만 보고하고 쓰지 않는다. 기본값 적용은 전체 빌더가 이번 실행에서 만든 액터에만 한다(ApplyToCreated).
public static class MonsterThemeRoleBuilder
{
    private static bool IsCharger(string id) => id=="SpiderBrood_Carcinoptera" || id=="VenomBrood_Kupolojuve_Tint_Orange" || id=="PrimalHunt_Occisodonte";
    private static float ChargeCooldown(string id) => id=="PrimalHunt_Occisodonte"?9f:7f;
    private static float ChargeWeight(string id) => id=="PrimalHunt_Occisodonte"?.25f:.4f;
    private static float VenomWeight(string id) => id=="VenomBrood_Venodonte_Tint1"?4f:id=="VenomBrood_Kupolojuve_Tint_Orange"?2f:1f;

    [MenuItem("OVERBURST/Enemies/Themes/Validate Combat Roles (Read Only)")]
    public static void Validate()
    {
        var notes=new List<string>();
        foreach(var definition in Definitions())
        {
            string id=definition.EnemyId;var set=definition.AbilitySet;var animation=definition.AnimationProfile;
            for(int i=0;i<set.Count;i++)
            {
                var ability=set.GetAbility(i);var clip=i<animation.AttackClipCount?animation.GetAttackClip(i):null;
                if(ability.ExecutionMode==EnemyAbilityExecutionMode.Charge && !IsCharger(id))notes.Add(id+": keeps charge ability "+ability.AbilityId+" (role default removes it)");
                if(id=="VenomBrood_Kupolojuve_Tint_Orange" && clip!=null && clip.name=="JumpClawsAttack")notes.Add(id+": uses JumpClawsAttack (role default removes it)");
                if(ability.ExecutionMode==EnemyAbilityExecutionMode.Charge && IsCharger(id))
                {
                    if(!Mathf.Approximately(ability.Cooldown,ChargeCooldown(id)))notes.Add(ability.AbilityId+": cooldown "+ability.Cooldown+" (role default "+ChargeCooldown(id)+")");
                    if(!Mathf.Approximately(ability.Weight,ChargeWeight(id)))notes.Add(ability.AbilityId+": weight "+ability.Weight+" (role default "+ChargeWeight(id)+")");
                }
            }
        }
        var venom=AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(MonsterThemeCombatBuilder.Root+"/Tables/VenomBrood.asset");
        if(venom!=null)foreach(var entry in venom.Entries)
            if(entry.definition!=null && !Mathf.Approximately(entry.weight,VenomWeight(entry.definition.EnemyId)))
                notes.Add("VenomBrood table "+entry.definition.EnemyId+": weight "+entry.weight+" (role default "+VenomWeight(entry.definition.EnemyId)+")");
        Debug.Log("[MonsterThemeRoles] read-only: "+notes.Count+" difference(s) from creation defaults, nothing written."
            +(notes.Count>0?"\n"+string.Join("\n",notes):""));
    }

    // 전체 빌더가 이번 실행에서 새로 만든 액터에만 역할 기본값을 적용한다.
    public static void ApplyToCreated(IReadOnlyCollection<string> createdIds)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode");
        if(createdIds==null || createdIds.Count==0)return;
        var ids=new HashSet<string>(createdIds);
        int attacks=0,chargers=0;
        foreach(var definition in Definitions().Where(d=>ids.Contains(d.EnemyId)))
        {
            bool charger=IsCharger(definition.EnemyId);
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
                    data.FindProperty("cooldown").floatValue=ChargeCooldown(definition.EnemyId);
                    data.FindProperty("weight").floatValue=ChargeWeight(definition.EnemyId);
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                selected.Add(ability);clips.Add(clip);
            }
            if(definition.EnemyId=="VenomBrood_Venodonte_Tint3" && !selected.Any(a=>a.ExecutionMode==EnemyAbilityExecutionMode.Projectile))
            {
                var clip=optional.First(c=>c.name=="AcidShot");
                string path=MonsterThemeCombatBuilder.Root+"/Abilities/"+definition.EnemyId+"_AcidShot.asset";
                var acid=AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(path);
                if(acid!=null)throw new InvalidOperationException("AcidShot asset already exists outside the ability set: "+path+". It is not overwritten.");
                acid=ScriptableObject.CreateInstance<EnemyAbilityDefinition>();AssetDatabase.CreateAsset(acid,path);
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
            string prefabPath=AssetDatabase.GetAssetPath(definition.ActorPrefab);
            var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                if(prefab.GetComponent<EnemyCorpseFade>()==null)
                {prefab.AddComponent<EnemyCorpseFade>();PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);}
            }
            finally{PrefabUtility.UnloadPrefabContents(prefab);}
        }
        var venom=AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(MonsterThemeCombatBuilder.Root+"/Tables/VenomBrood.asset");
        if(venom!=null)
        {
            var roster=venom.Entries.ToArray();bool changed=false;
            for(int i=0;i<roster.Length;i++)
            {
                if(roster[i].definition==null || !ids.Contains(roster[i].definition.EnemyId))continue;
                roster[i].weight=VenomWeight(roster[i].definition.EnemyId);changed=true;
            }
            if(changed){venom.Configure(venom.ThemeId,venom.DisplayName,venom.Catalog,venom.Accent,roster);EditorUtility.SetDirty(venom);}
        }
        AssetDatabase.SaveAssets();Debug.Log("[MonsterThemeRoles] created actors only: attacks="+attacks+" chargingSpecies="+chargers);
    }

    private static IEnumerable<EnemyDefinition> Definitions() =>
        AssetDatabase.FindAssets("t:EnemyDefinition",new[]{MonsterThemeCombatBuilder.Root+"/Definitions"})
            .Select(guid=>AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(d=>d!=null && MonsterMixedSquadBuilder.IsOriginalTheme(d.EnemyId))
            .OrderBy(d=>d.EnemyId,StringComparer.Ordinal);
}
