using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class MonsterThemeContentValidator
{
    [MenuItem("OVERBURST/Enemies/Themes/Validate Content")]
    public static void Validate()
    {
        int actors=0,attacks=0;
        foreach(string id in new[]{"SpiderBrood","VenomBrood","PrimalHunt"})
        {
            var table=AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(MonsterThemeCombatBuilder.Root+"/Tables/"+id+".asset");
            Require(table!=null,"Missing "+id);Require(table.Validate(out string error),error);
            var roster=table.BuildRoster(40,9,1,732);
            Require(roster.Count==50,"Roster size");
            Require(roster.SequenceEqual(table.BuildRoster(40,9,1,732)),"Seed stability");
            foreach(EnemyThemeTier tier in Enum.GetValues(typeof(EnemyThemeTier)))
                Require(roster.Count(d=>table.Entries.Any(e=>e.definition==d && e.tier==tier))==new[]{40,9,1}[(int)tier],"Tier count");
            var sharedPreset=table.Entries.First(e=>e.tier==EnemyThemeTier.Small).definition.AiPreset;
            Require(sharedPreset!=null && sharedPreset.ActivationCount<=49,"49 members must activate existing squads");
            foreach(var entry in table.Entries)
            {
                var definition=entry.definition;Require(definition.IsValid,definition.EnemyId+" invalid");
                var locomotionController=(UnityEditor.Animations.AnimatorController)definition.AnimationProfile.RuntimeController;
                var locomotionState=locomotionController.layers[0].stateMachine.states.First(s=>s.state.name=="Locomotion").state;
                foreach(var child in ((UnityEditor.Animations.BlendTree)locomotionState.motion).children)
                    ValidateLoop(child.motion,definition.EnemyId);
                Require(definition.SquadParticipationMode==(entry.tier==EnemyThemeTier.Elite?EnemySquadParticipationMode.Independent:EnemySquadParticipationMode.SquadMember),"Participation");
                Require(definition.AiPreset==sharedPreset,"Split preset would fragment the swarm");
                string path=AssetDatabase.GetAssetPath(definition.ActorPrefab);
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Require(root.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))==0,"Missing scripts");
                    var actor=root.GetComponent<EnemyActor>();Require(actor.Validate(out error),definition.EnemyId+": "+error);
                    Require(actor.Animator.avatar!=null && !actor.Animator.applyRootMotion,"Avatar / root motion");
                    Require(actor.GetComponent<EnemyVisualRootGuard>()!=null,"Root scale guard");
                    var executors=root.GetComponents<EnemyAbilityExecutor>();
                    var parameters=definition.AnimationProfile.RuntimeController is UnityEditor.Animations.AnimatorController controller?controller.parameters:Array.Empty<AnimatorControllerParameter>();
                    foreach(var ability in Enumerable.Range(0,definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility))
                    {
                        Require(executors.Any(e=>e.Supports(ability)),"No executor: "+ability.AbilityId);
                        Require(parameters.Any(p=>p.name==ability.AnimatorTrigger && p.type==AnimatorControllerParameterType.Trigger),"Missing attack trigger");
                        var clip=definition.AnimationProfile.GetAttackClip(attacksFor(definition,ability));
                        Require(clip!=null && Mathf.Abs(clip.length-ability.AttackAnimationDuration)<.001f,"Clip timing mismatch");
                        Require(!clip.name.EndsWith("_RM"),"Uncontrolled root motion clip");attacks++;
                    }
                    Require(actor.CollisionRoot.GetComponentsInChildren<Collider>().Any(c=>c.enabled && !c.isTrigger),"Body collider");
                    Require(!actor.VisualRoot.GetComponentsInChildren<Collider>(true).Any(c=>c.enabled),"Vendor collider competes with body");
                    foreach(var renderer in actor.VisualRoot.GetComponentsInChildren<Renderer>(true))
                        Require(renderer.sharedMaterials.All(m=>m!=null && m.shader!=null && m.shader.isSupported),"Broken material");
                    var vendor=actor.VisualRoot.GetChild(0).GetChild(0).gameObject;
                    Require(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(vendor)==AssetDatabase.GetAssetPath(definition.Species.VendorPrefab),"Source prefab connection");actors++;
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
        }
        Debug.Log($"[MonsterThemeContent] PASS tables=3 actors={actors} attacks={attacks} roster=40/9/1 missingScripts=0");
    }
    private static int attacksFor(EnemyDefinition definition,EnemyAbilityDefinition ability)
    {for(int i=0;i<definition.AbilitySet.Count;i++)if(definition.AbilitySet.GetAbility(i)==ability)return i;return -1;}
    private static void ValidateLoop(Motion motion,string id)
    {
        if(motion is UnityEditor.Animations.BlendTree tree)foreach(var child in tree.children)ValidateLoop(child.motion,id);
        else Require(motion is AnimationClip clip && clip.isLooping,"Non-looping gait: "+id+" / "+motion);
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("[MonsterThemeContent] "+message);}
}
