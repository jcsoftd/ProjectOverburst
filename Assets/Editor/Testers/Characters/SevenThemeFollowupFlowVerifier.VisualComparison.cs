using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

public static partial class SevenThemeFollowupFlowVerifier
{
    static Action previewBeforeCapture;
    static IEnumerator InterpolationComparison(EnemyCatalog catalog, CombatHealth health)
    {
        foreach (string id in new[] { "SpiderBrood_Formickarce", "V3_Anglerox" })
        foreach (int fps in new[] { 60, 120 })
        foreach (RigidbodyInterpolation interpolation in new[] { RigidbodyInterpolation.None, RigidbodyInterpolation.Interpolate })
        {
            if(Mode=="VisualInterpolationRender2"&&id=="SpiderBrood_Formickarce"&&fps==60)continue;
            Time.captureDeltaTime = 1f / fps;
            if (!catalog.TryGet(id, out var d)) throw new InvalidOperationException(id);
            currentId = id + "_" + fps + "_" + interpolation; trace = new JArray(); damages = new JArray(); begin = Time.time;
            phase = "constant_destination"; target.position = Vector3.forward * 20; health.SetMaxHp(1e9f, true);
            if (!service.TrySpawn(new EnemySpawnRequest(d, Vector3.up * .035f, Quaternion.identity, target, context: EncounterContext.Test), out actor)) throw new InvalidOperationException(id);
            actor.AI.enabled = false; actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var body = actor.GetComponent<Rigidbody>(); var original = body.interpolation; body.interpolation = interpolation;
            camera = new Capture(Path.Combine(Folder, currentId), target, actor, true, fps); camera.Mark(phase);
            actor.Movement.SetDestination(Vector3.forward * 9, .15f, EnemyLocomotionMode.Run);
            yield return new WaitForSeconds(3f);
            phase = "stop_then_reverse"; camera.Mark(phase); actor.Movement.StopMovement(); yield return new WaitForSeconds(.5f);
            actor.Movement.SetDestination(Vector3.zero, .15f, EnemyLocomotionMode.Run); yield return new WaitForSeconds(3f);
            actor.Movement.StopMovement();
            results.Add(new JObject { ["id"] = id, ["name"] = d.DisplayName, ["captureFps"] = fps, ["fixedDeltaTime"] = Time.fixedDeltaTime,
                ["interpolation"] = interpolation.ToString(), ["originalInterpolation"] = original.ToString(), ["assetWrites"] = 0,
                ["physicsPositionAndRenderedPositionRecorded"] = true, ["actualSavedMovementAnimator"] = true, ["actualPlayerInput"] = false });
            body.interpolation = original; SaveCase(currentId); yield return null;
        }
        Time.captureDeltaTime = 1f / 30;
    }

    sealed class DeathSupportSamples
    {
        public readonly float[] bottom;
        public readonly AnimationClip clip;
        public readonly float parentBaseY;
        public bool UsedUnityBakeFallback;
        public DeathSupportSamples(AnimationClip value, float[] measured, float parentY) { clip = value; bottom = measured; parentBaseY = parentY; }
        public float At(float normalized)
        {
            float f = Mathf.Clamp01(normalized) * (bottom.Length - 1); int i = Mathf.Min(bottom.Length - 2, Mathf.FloorToInt(f));
            return Mathf.Lerp(bottom[i], bottom[i + 1], f - i);
        }
    }
    static float ShaderPoseBottom(IEnumerable<SkinnedMeshRenderer> skins)
    {
        float low = float.PositiveInfinity;
        foreach (var skin in skins)
        {
            var mesh = skin.sharedMesh; if (mesh == null) continue;
            var vertices = mesh.vertices; var binds = mesh.bindposes;
            var matrices = binds.Select((bind, index) => (skin.bones[index] != null ? skin.bones[index].localToWorldMatrix : skin.localToWorldMatrix) * bind).ToArray();
            var counts = mesh.GetBonesPerVertex(); var weights = mesh.GetAllBoneWeights(); int cursor = 0;
            try
            {
                for (int index = 0; index < vertices.Length; index++)
                {
                    Vector3 point = Vector3.zero; int count = counts[index];
                    for (int bone = 0; bone < count; bone++) { var weight = weights[cursor++]; point += matrices[weight.boneIndex].MultiplyPoint3x4(vertices[index]) * weight.weight; }
                    if (count == 0) point = skin.localToWorldMatrix.MultiplyPoint3x4(vertices[index]);
                    low = Mathf.Min(low, point.y);
                }
            }
            finally { counts.Dispose(); weights.Dispose(); }
        }
        if (float.IsInfinity(low) || float.IsNaN(low)) throw new InvalidOperationException("Visible skin support samples missing.");
        return low;
    }
    static float UnityBakedPoseBottom(IEnumerable<SkinnedMeshRenderer> skins)
    {
        float low=float.PositiveInfinity;var bake=new Mesh();
        try{foreach(var skin in skins){skin.BakeMesh(bake,false);foreach(var vertex in bake.vertices)low=Mathf.Min(low,skin.transform.TransformPoint(vertex).y);}}
        finally{Object.DestroyImmediate(bake);}
        if(float.IsInfinity(low)||float.IsNaN(low))throw new InvalidOperationException("Both shader pose and Unity BakeMesh failed for "+string.Join(",",skins.Select(s=>s.name+":"+s.sharedMesh.vertexCount)));
        return low;
    }
    static DeathSupportSamples MeasureDeathSupport(EnemyActor source)
    {
        GameObject clone = null; PlayableGraph graph = default;
        try
        {
            clone = Object.Instantiate(source.VisualRoot.gameObject); clone.name = "Owned offline death support pose";
            clone.transform.SetPositionAndRotation(source.VisualRoot.position, source.VisualRoot.rotation); clone.transform.localScale = source.VisualRoot.lossyScale;
            foreach (var behavior in clone.GetComponentsInChildren<MonoBehaviour>(true)) behavior.enabled = false;
            var skins = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null).ToArray();
            foreach (var renderer in clone.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            var animator = clone.GetComponentInChildren<Animator>(); animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var model = animator.transform; Vector3 position = model.localPosition, scale = model.localScale; Quaternion rotation = model.localRotation;
            var clip = source.Definition.AnimationProfile.Death;
            graph = PlayableGraph.Create("Owned death support sample graph"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
            AnimationPlayableOutput.Create(graph, "pose", animator).SetSourcePlayable(playable); graph.Play();
            var samples = new float[33]; bool fallback=false;
            for (int frame = 0; frame < samples.Length; frame++)
            {
                playable.SetTime(clip.length * Mathf.Min(.99999f, frame / (float)(samples.Length - 1))); graph.Evaluate(0);
                model.SetLocalPositionAndRotation(position, rotation); model.localScale = scale;
                try{samples[frame] = ShaderPoseBottom(skins) - source.transform.position.y;}
                catch(InvalidOperationException){samples[frame]=UnityBakedPoseBottom(skins)-source.transform.position.y;fallback=true;}
            }
            return new DeathSupportSamples(clip, samples, source.Animator.transform.parent.position.y - source.transform.position.y){UsedUnityBakeFallback=fallback};
        }
        finally { if (graph.IsValid()) graph.Destroy(); if (clone != null) Object.DestroyImmediate(clone); }
    }
    static IEnumerator DeathComparison(EnemyCatalog catalog, CombatHealth health)
    {
        Time.captureDeltaTime = 1f / 30;
        var curves = new Dictionary<string, DeathSupportSamples>();
        string[] ids = Mode=="VisualDeath3"?new[]{"V3_Gasterodonte","V3_Perderos","CavernMutants_Ursacetus"}:new[]{"V3_Lacodon","V3_Gasterodonte","V3_Perderos","CavernMutants_Ursacetus"};
        foreach (string id in ids) foreach (string entry in new[] { "normal", "strong_hit", "stunned", "jump_attack", "strong_attack" }) foreach (bool proposal in new[] { false, true })
        {
            if (!catalog.TryGet(id, out var d)) throw new InvalidOperationException(id);
            currentId = id + "_" + entry + (proposal ? "_B" : "_A"); trace = new JArray(); damages = new JArray(); begin = Time.time; phase = "entry_" + entry;
            target.position = Vector3.forward * 2; health.SetMaxHp(1e9f, true);
            if (!service.TrySpawn(new EnemySpawnRequest(d, Vector3.up * .035f, Quaternion.identity, target, context: EncounterContext.Test), out actor)) throw new InvalidOperationException(id);
            actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; actor.AI.enabled = false; yield return null;
            if (!curves.TryGetValue(id, out var curve)) { curve = MeasureDeathSupport(actor); curves.Add(id, curve); }
            var model = actor.Animator.transform; Vector3 modelBase = model.localPosition;
            bool entryReached = entry == "normal" || entry == "strong_hit", available = true;
            camera = new Capture(Path.Combine(Folder, currentId), target, actor, true); camera.Mark(phase);
            if (entry == "stunned")
            {
                available = actor.AnimationBridge.TryPlayParryStun(out float duration);
                if (available)
                {
                    actor.GetComponent<EnemyMovementReaction>().ApplyParryStun(duration);
                    float until = Time.time + 4;
                    while (Time.time < until && !actor.Animator.GetCurrentAnimatorStateInfo(0).IsName(EnemyAnimationBridge.StunnedLoopStateName)) yield return null;
                    entryReached = actor.Animator.GetCurrentAnimatorStateInfo(0).IsName(EnemyAnimationBridge.StunnedLoopStateName);
                }
            }
            if (entry == "jump_attack")
            {
                var candidates = Enumerable.Range(0, d.AbilitySet.Count).Select(d.AbilitySet.GetAbility);
                var jump = candidates.FirstOrDefault(a => a.WeakAttackExecution != null && a.WeakAttackExecution.MotionPolicy == EnemyWeakAttackMotionPolicy.VisualJump);
                available = jump != null;
                if (available)
                {
                    target.position = Vector3.forward * Mathf.Max(.6f, jump.Range * .65f); Physics.SyncTransforms();
                    bool started = actor.AbilityController.TryStartAbility(jump, target); float until = Time.time + 5, n = 0;
                    while (started && Time.time < until && (!actor.AnimationBridge.TryGetAttackNormalizedTime(jump.AnimatorTrigger, out n) || n < .3f)) yield return null;
                    entryReached = started && actor.AbilityController.IsExecuting && n >= .3f;
                }
            }
            if (entry == "strong_attack")
            {
                var strong=Enumerable.Range(0,d.AbilitySet.Count).Select(d.AbilitySet.GetAbility).FirstOrDefault(a=>a.IsParryable);
                available=strong!=null;
                if(available)
                {
                    cadenceField.SetValue(actor.AbilityController,3);
                    target.position=Vector3.forward*Mathf.Max(1.3f,EnemyAttackThreatGeometry.ResolveStartRange(actor,strong)*.6f);Physics.SyncTransforms();
                    bool started=actor.AbilityController.TryStartAbility(strong,target);float until=Time.time+5,n=0;
                    while(started&&Time.time<until&&(!actor.AnimationBridge.TryGetAttackNormalizedTime(strong.AnimatorTrigger,out n)||n<.20f))yield return null;
                    entryReached=started&&actor.AbilityController.IsExecuting&&n>=.20f;
                }
            }
            yield return new WaitForSeconds(.12f);
            if (!available || !entryReached)
            {
                results.Add(new JObject { ["id"] = id, ["name"] = d.DisplayName, ["entry"] = entry, ["proposal"] = proposal, ["status"] = available ? "ENTRY_NOT_REACHED" : "NOT_APPLICABLE_SAVED_MOTION_MISSING" });
                SaveCase(currentId); yield return null; continue;
            }
            phase = "death"; camera.Mark(phase); float diedAt = Time.time; int instance = actor.GetInstanceID();
            actor.Health.TakeDamage(new DamageInfo(actor.Health.CurrentHp + 1e8f, actor.transform.position, target.gameObject, Vector3.forward,
                isCritical: entry == "strong_hit", knockback: entry == "strong_hit" ? 8f : 0f, suppressDefaultHitVfx: true));
            float maxCorrection = 0; bool markedEnd = false; float deathLimit = Time.time + Mathf.Max(6, curve.clip.length + 2);
            previewBeforeCapture = proposal ? (Action)(() =>
            {
                if (actor == null || !actor.IsLeased || !actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Death")) return;
                float normalized = actor.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                float weight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - diedAt - .32f) / .18f));
                model.localPosition = modelBase;
                float carried = model.parent.position.y - actor.transform.position.y - curve.parentBaseY;
                float correction = (.02f - actor.transform.position.y - carried - curve.At(normalized)) * weight;
                model.localPosition = modelBase + model.parent.InverseTransformVector(Vector3.up * correction);
                maxCorrection = Mathf.Max(maxCorrection, Mathf.Abs(correction));
            }) : null;
            while (actor.IsLeased && Time.time < deathLimit)
            {
                float normalized = actor.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                if (!markedEnd && actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Death") && normalized >= .9f)
                {
                    camera.Mark("corpse_end"); markedEnd = true;
                }
                yield return null;
            }
            previewBeforeCapture = null;
            bool disappeared = !actor.IsLeased; model.localPosition = modelBase;
            results.Add(new JObject { ["id"] = id, ["name"] = d.DisplayName, ["entry"] = entry, ["proposal"] = proposal, ["status"] = "CAPTURED",
                ["actualSavedDeathClip"] = AssetDatabase.GetAssetPath(curve.clip), ["sampledSupportBottom"] = new JArray(curve.bottom), ["maxPreviewCorrectionMeters"] = maxCorrection,
                ["disappearedNaturally"] = disappeared, ["observedSeconds"] = Time.time - diedAt, ["instance"] = instance, ["assetWrites"] = 0,
                ["proposalAppliedToGame"] = false, ["runtimeMeshSampling"] = false,["offlineUnityBakeFallback"] = curve.UsedUnityBakeFallback });
            SaveCurrentTrace(); camera?.Dispose(); camera=null; service.Release(actor); actor=null;
            if (!service.TrySpawn(new EnemySpawnRequest(d,Vector3.up*.035f,Quaternion.identity,target,context:EncounterContext.Test),out actor)) throw new InvalidOperationException("Death respawn "+id);
            actor.AI.enabled=false; actor.Movement.StopMovement(); yield return null;
            bool reset=!actor.Health.IsDead && !actor.AbilityController.IsExecuting && !actor.Melee.IsAttacking && !actor.AnimationBridge.IsParryStunAnimating;
            ((JObject)results.Last)["poolRespawnReset"]=reset;
            ((JObject)results.Last)["respawnModelY"]=actor.Animator.transform.position.y;
            camera=new Capture(Path.Combine(Folder,currentId,"Respawn"),target,actor,true);camera.Mark("respawn");camera.Frame();camera.Dispose();camera=null;
            SaveCase(currentId); yield return null;
        }
    }
}
