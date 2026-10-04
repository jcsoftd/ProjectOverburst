using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Overburst.EditorTools.ComboMaker
{
    internal sealed partial class ComboMakerPreview
    {
        public MeleeHeavyAttackDefinition HeavyDefinition {get;set;}
        public float EnergyNormalized {get;set;}=1;
        public bool ShowKnockback {get;set;}
        public GameObject TargetPrefab {get;set;}
        public EnemyDefinition TargetDefinition {get;set;}
        public EnemyHitWeightProfile WeightOverride {get;set;}
        public float CurrentEnergy => heavyDischarged?0:Mathf.Clamp01(EnergyNormalized);
        public float PreviewEnergyAmount => heavyDischarged?0:Mathf.Max(0,Element==WeaponElement.Light?EnergyNormalized:Mathf.Min(1,EnergyNormalized))*OverburstElementTuning.Current.maximumEnergy;
        public float TargetDisplacement => dummy==null?0:Vector3.Distance(dummy.transform.position,TargetPosition);
        public bool HeavyDischarged => heavyDischarged;
        private MeleeWeaponElementFx energyFx;
        private WeaponElement previewedElement = WeaponElement.None;
        private GameObject monsterVisual;
        private EnemyMovementReaction targetReaction;
        private AnimationClip targetIdle,targetHit;
        private GameObject targetAnimationRoot;
        private Transform targetLiftRoot;
        private Vector3 targetLiftRest;
        private CombatTarget targetVolume;
        private BloodHitProfile targetBlood;
        private float combatClock,reactionStart=-100,reactionDuration=.12f,nextReaction,liftDuration,liftHeight;
        private bool heavyDischarged;
        private float framingHeight=.85f;
        private Vector3 reactionFrom,reactionTo;
        public void PlaceTargetAtImpact()
        {
            if(!Ready||combo.steps[StepIndex].attackPhases==null||combo.steps[StepIndex].attackPhases.Length==0)return;
            var phase=combo.steps[StepIndex].attackPhases[0];
            Seek(phase.SafeStart);
            var pattern=phase.ResolvePattern(stats.range,stats.meleeSlashAngle,width);
            float offset=pattern.ForwardOffset+(pattern.Shape==AttackAreaShape.Circle?0:pattern.Range*.5f);
            var point=actor.transform.position+Vector3.forward*offset;
            TargetPosition=new Vector3(point.x,.9f,point.z);
            pan=Vector3.zero;distance=Mathf.Max(4.8f,Mathf.Abs(point.z)*2f);
            Seek(0);
            if(monsterVisual!=null)
            {
                var skins=monsterVisual.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled).ToArray();
                if(skins.Length>0)
                {
                    var bounds=skins[0].bounds;foreach(var skin in skins)bounds.Encapsulate(skin.bounds);
                    framingHeight=Mathf.Max(.85f,bounds.max.y*.5f);distance=Mathf.Max(distance,bounds.size.y*2.5f);
                }
            }
        }

        private void CreateMonsterTarget()
        {
            if(TargetPrefab==null)return;
            var holder=new GameObject("몬스터 프리뷰");holder.SetActive(false);holder.transform.SetParent(stage.transform,false);
            monsterVisual=Object.Instantiate(TargetPrefab,holder.transform,false);
            DisableBehaviours(monsterVisual);
            foreach(var c in monsterVisual.GetComponentsInChildren<Collider>(true))c.enabled=false;
            foreach(var body in monsterVisual.GetComponentsInChildren<Rigidbody>(true)){body.isKinematic=true;body.detectCollisions=false;}
            foreach(var agent in monsterVisual.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))agent.enabled=false;
            foreach(var audio in monsterVisual.GetComponentsInChildren<AudioSource>(true))audio.enabled=false;
            foreach(var light in monsterVisual.GetComponentsInChildren<Light>(true))light.enabled=false;
            foreach(var ps in monsterVisual.GetComponentsInChildren<ParticleSystem>(true))ps.gameObject.SetActive(false);
            var clips=AnimationUtility.GetAnimationClips(TargetPrefab);
            targetIdle=clips.FirstOrDefault(c=>c.name.IndexOf("idle",StringComparison.OrdinalIgnoreCase)>=0);
            targetHit=clips.FirstOrDefault(c=>c.name.IndexOf("hit",StringComparison.OrdinalIgnoreCase)>=0);
            var enemyActor=monsterVisual.GetComponent<EnemyActor>();
            if(enemyActor!=null&&TargetDefinition!=null)
            {
                var resolved=TargetDefinition.ResolveRuntimeStats();
                if(enemyActor.VisualRoot!=null)enemyActor.VisualRoot.localScale=resolved.VisualScale;
                if(enemyActor.CollisionRoot!=null)enemyActor.CollisionRoot.localScale=resolved.CollisionScale;
                if(enemyActor.Anchors!=null)enemyActor.Anchors.localScale=resolved.AnchorScale;
            }
            targetVolume=monsterVisual.GetComponentInChildren<CombatTarget>(true);
            targetBlood=monsterVisual.GetComponentInChildren<BloodHitTarget>(true)?.Profile;
            targetAnimationRoot=enemyActor!=null&&enemyActor.Animator!=null?enemyActor.Animator.gameObject:monsterVisual.GetComponentInChildren<Animator>(true)?.gameObject;
            if(TargetDefinition!=null){targetIdle=TargetDefinition.AnimationProfile?.Idle;targetHit=TargetDefinition.AnimationProfile?.Hit;}
            foreach(var a in monsterVisual.GetComponentsInChildren<Animator>(true)){a.enabled=false;a.runtimeAnimatorController=null;}
            foreach(var skin in monsterVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true)){skin.updateWhenOffscreen=true;skin.forceMatrixRecalculationPerRender=true;}
            targetReaction=monsterVisual.GetComponentInChildren<EnemyMovementReaction>(true);
            targetLiftRoot=targetReaction!=null?targetReaction.VisualReactionRoot:null;
            targetLiftRest=targetLiftRoot!=null?targetLiftRoot.localPosition:Vector3.zero;
            monsterVisual.transform.SetParent(dummy.transform,false);
            dummy.transform.localScale=Vector3.one;
            monsterVisual.transform.localPosition=Vector3.down*.9f;
            monsterVisual.transform.localRotation=Quaternion.Euler(0,180,0);
            dummy.GetComponent<Renderer>().enabled=false;
            Object.DestroyImmediate(holder);
            monsterVisual.SetActive(true);
        }
        private void ResetCombatPreview()
        {
            combatClock=0;reactionStart=-100;nextReaction=0;liftHeight=0;heavyDischarged=false;
            reactionFrom=reactionTo=TargetPosition;
            if(dummy!=null)dummy.transform.position=TargetPosition;
            TickTarget();
        }
        private void TickTarget()
        {
            if(dummy==null)return;
            float t=Mathf.Clamp01((combatClock-reactionStart)/Mathf.Max(.02f,reactionDuration));
            dummy.transform.position=ShowKnockback?Vector3.Lerp(reactionFrom,reactionTo,1-Mathf.Pow(1-t,3)):TargetPosition;
            if(monsterVisual==null)return;
            float hitTime=combatClock-reactionStart;
            var clip=hitTime>=0&&hitTime<(targetHit!=null?targetHit.length:0)?targetHit:targetIdle;
            if(clip!=null&&targetAnimationRoot!=null)clip.SampleAnimation(targetAnimationRoot,clip==targetHit?hitTime:Mathf.Repeat(combatClock,Mathf.Max(.01f,clip.length)));
            float lift=ShowKnockback?liftHeight*Mathf.Pow(Mathf.Sin(Mathf.PI*Mathf.Clamp01(hitTime/Mathf.Max(.02f,liftDuration))),2):0;
            monsterVisual.transform.localPosition=Vector3.down*.9f;
            if(targetLiftRoot!=null)targetLiftRoot.localPosition=targetLiftRest+Vector3.up*lift;
            monsterVisual.transform.localRotation=Quaternion.Euler(0,180,0);
        }
        private void ApplyTargetReaction(AttackPhaseData phase)
        {
            if(!ShowKnockback || combatClock<nextReaction)return;
            var profile=WeightOverride!=null?WeightOverride:TargetDefinition!=null?TargetDefinition.MovementProfile?.HitWeightProfile:targetReaction!=null?targetReaction.HitWeightProfile:null;
            float strength=Mathf.Max(0,stats.knockback)*phase.impact.SafeKnockbackMultiplier;
            float distance=profile!=null?profile.ResolveDistance(strength):targetReaction!=null?targetReaction.ResolveKnockbackDistance(strength):strength*.1f;
            reactionFrom=dummy.transform.position;reactionTo=reactionFrom+Vector3.forward*distance;
            reactionStart=combatClock;reactionDuration=profile!=null?profile.TravelDuration:.12f;
            liftHeight=profile!=null?profile.VisualLiftHeight:0;liftDuration=profile!=null?profile.VisualLiftDuration:.22f;
            nextReaction=combatClock+(profile!=null?profile.ReactionCooldown:reactionDuration);
        }
        private void RenderEnergy()
        {
            if(energyFx==null)return;
            var element=ShowEffects?Element:WeaponElement.None;
            energyFx.EditorPreviewEnergy(element,CurrentEnergy);
            if(previewedElement!=element) energyFx.EditorSampleEnergy(3f);
            previewedElement=element;
        }
        private void EvaluateHeavyDischarge(MeleeComboStepData step)
        {
            if(HeavyDefinition==null||heavyDischarged||step.attackPhases==null||step.attackPhases.Length==0)return;
            int index=Mathf.Clamp(HeavyDefinition.dischargePhaseIndex,0,step.attackPhases.Length-1);
            if(Progress<step.attackPhases[index].SafeStart)return;
            heavyDischarged=true;
            if(EnergyNormalized<=0 || !ShowEffects)return;
            var prefab=Element==WeaponElement.Light?HeavyDefinition.elementVfx.GetLightImpact(EnergyNormalized>1):HeavyDefinition.elementVfx.GetImpact(Element);
            if(prefab==null)return;
            var phase=step.attackPhases[index];var pattern=phase.ResolvePattern(stats.range,stats.meleeSlashAngle,width);
            Vector3 center=actor.transform.position+Vector3.forward*pattern.ForwardOffset;
            float radius=CombatBalanceFormulas.DischargeRadius(OverburstElementTuning.Current,Mathf.Clamp01(EnergyNormalized));
            var go=InstantiateEffect(prefab,center,Quaternion.identity,Vector3.one*HeavyDefinition.elementVfx.ImpactScale(Element,radius));
            if(Element==WeaponElement.Light)
                foreach(var particle in go.GetComponentsInChildren<ParticleSystem>(true))
                {var main=particle.main;main.simulationSpeed*=OverburstElementTuning.Current.SafeLightTripleVfxPlaybackSpeed;}
            ActivateEffect(go,0);
        }
    }
}
