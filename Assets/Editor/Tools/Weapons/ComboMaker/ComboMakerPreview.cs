using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.ComboMaker
{
    // Owns every preview object and clock. Never calls the game pools, damage service or player input.
    internal sealed partial class ComboMakerPreview : IDisposable
    {
        internal const string PlayerPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
        private ComboMakerStage renderer;
        private RenderTexture surface;
        private bool rendering,preparingEffects,disposePending;
        private GameObject stage, actor, weapon, dummy;
        private WeaponPose pose;
        private WeaponTraceBinding trace;
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Effect> effects = new List<Effect>();
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable current, previous;
        private int previousIndex = -1;
        private float previousTime;
        private Animator animator;
        private sealed class PartRig { public AnimationMixerPlayable mixer; public AnimationClipPlayable current, previous; }
        private readonly List<PartRig> partRigs = new List<PartRig>();
        private struct ConstraintBinding { public Transform source,target; public Vector3 position; public Quaternion rotation; }
        private readonly List<ConstraintBinding> constraints = new List<ConstraintBinding>();
        private MeleeComboDefinition combo;
        private WeaponFinalStats stats;
        private float width;
        private readonly AttackVisualHeightExecutor visualHeight = new AttackVisualHeightExecutor();
        private readonly AttackMovementExecutor movement = new AttackMovementExecutor();
        private readonly AttackTrailExecutor trailExecutor = new AttackTrailExecutor();
        private bool[] hit, completed;
        private bool[][] cues;
        private AttackPatternBasis[] bases;
        private bool[] begun;
        private float elapsed, entry, entryElapsed, lastSample;
        private Vector3 origin;
        private float yaw = 45, pitch = 45, distance = 20f;
        private bool productView = true;
        private Vector3 pan;
        public int StepIndex { get; private set; }
        public float Progress { get; private set; }
        public int HitCount { get; private set; }
        public int CueCount { get; private set; }
        public int EffectCount => effects.Count;
        public bool PendingEffects => effects.Any(e=>e.PendingGraph);
        public void PrepareEffects()
        {
            if(preparingEffects||!PendingEffects||Application.isPlaying)return;
            preparingEffects=true;
            try
            {
                foreach(var effect in effects)effect.PrepareGraph();
                ComboMakerVfxClock.Flush();
            }
            finally
            {
                preparingEffects=false;
                if(disposePending)EditorApplication.delayCall+=DeferredDispose;
            }
        }
        public bool Playing { get; set; }
        public bool All { get; set; }
        public bool Loop { get; set; } = true;
        public bool ShowEffects { get; set; } = true;
        public bool ShowHits { get; set; } = true;
        public bool ShowGeometry { get; set; } = true;
        public float Speed { get; set; } = 1;
        public WeaponElement Element { get; set; } = WeaponElement.Fire;
        public Vector3 TargetPosition { get; set; } = new Vector3(0, .9f, 1.5f);
        public BloodHitProfile BloodProfile { get; set; }
        public GameObject HitOverride { get; set; }
        public string Status { get; private set; } = "무기를 선택하세요.";
        public bool Ready => actor != null && combo != null && combo.StepCount > 0;
        public Camera Camera => renderer?.camera;
        public GameObject Actor => actor;
        public int TrailParticleCount => energyFx != null ? energyFx.EditorTrailParticleCount : 0;
        public float Elapsed => elapsed;

        public float CueTime(int phaseIndex, float trigger)
        {
            if (!Ready || StepIndex < 0 || StepIndex >= combo.StepCount
                || combo.steps[StepIndex].attackPhases == null || phaseIndex < 0
                || phaseIndex >= combo.steps[StepIndex].attackPhases.Length) return 0;
            var phase=combo.steps[StepIndex].attackPhases[phaseIndex];
            var pattern=phase.ResolvePattern(stats.range,stats.meleeSlashAngle,width);
            float low=phase.SafeStart,high=phase.SafeEnd;
            for(int i=0;i<20;i++)
            {
                float mid=(low+high)*.5f;
                float progress=pattern.EvaluateProgress(Mathf.InverseLerp(phase.SafeStart,phase.SafeEnd,mid));
                bool valid=true;
                if(phase.progressSource!=AttackProgressSource.NormalizedTime)
                    valid=combo.TryGetAttackTrajectoryStep(StepIndex,out var baked,out _)
                        && baked.TryGetPhase(phaseIndex,phase.progressSource,out var samples)
                        && samples.TryEvaluate(mid,out _,out progress);
                if(valid && progress>=trigger) high=mid; else low=mid;
            }
            return high;
        }

        public void Load(WeaponItemData item, MeleeComboDefinition data)
        {
            if (rendering)
                throw new InvalidOperationException("프리뷰를 렌더링하는 동안 무기를 교체할 수 없습니다.");
            Dispose();
            try { LoadCore(item, data); }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void LoadCore(WeaponItemData item, MeleeComboDefinition data)
        {
            combo = data;
            if (item == null || data == null) return;
            renderer = new ComboMakerStage();
            // URP skips decal passes for Preview cameras; culling remains scoped to our preview scene.
            renderer.camera.cameraType = CameraType.Game;
            renderer.camera.clearFlags = CameraClearFlags.SolidColor;
            renderer.camera.backgroundColor = new Color(.28f, .30f, .33f);
            renderer.camera.nearClipPlane = .03f;
            renderer.camera.farClipPlane = 100;
            renderer.camera.fieldOfView = 38;
            var cameraData = renderer.camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.requiresColorOption = CameraOverrideOption.On;
            cameraData.requiresDepthOption = CameraOverrideOption.On;
            cameraData.volumeLayerMask = 1 << 31;
            renderer.lights[0].intensity = 1.6f;
            renderer.lights[0].enabled = true;
            renderer.lights[0].transform.rotation = Quaternion.Euler(45, -30, 0);
            renderer.lights[1].intensity = 1f;
            renderer.lights[1].enabled = true;
            renderer.lights[1].transform.rotation = Quaternion.Euler(320, 130, 0);
            stage = new GameObject("Combo Maker Preview") { hideFlags = HideFlags.HideAndDontSave };
            renderer.AddSingleGO(stage);
            // Inactive parent suppresses ExecuteAlways / OnEnable on full player and VFX prefabs.
            stage.SetActive(false);
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            if (playerPrefab == null) throw new InvalidOperationException("플레이어 프리팹을 찾지 못했습니다.");
            actor = Object.Instantiate(playerPrefab, stage.transform, false);
            var adapter = actor.GetComponentInChildren<P09CharacterVisualAdapter>(true);
            if (adapter == null) throw new InvalidOperationException("플레이어 리그 어댑터가 없습니다.");
            adapter.ResolveReferences();
            animator = adapter.Animator;
            var weaponPrefab = WeaponLevelCatalog.ResolveVisual(item);
            if (animator == null || adapter.RightHandWeaponSocket == null || weaponPrefab == null)
                throw new InvalidOperationException("Animator, 오른손 소켓 또는 장착 무기가 없습니다.");
            weapon = Object.Instantiate(weaponPrefab, adapter.RightHandWeaponSocket, false);
            pose = weapon.GetComponentInChildren<WeaponPose>(true);
            trace = weapon.GetComponentInChildren<WeaponTraceBinding>(true);
            DisableBehaviours(actor);
            energyFx=weapon.GetComponentInChildren<MeleeWeaponElementFx>(true);
            // Manual graph sampling must invalidate skin matrices even while the Editor is idle.
            foreach (var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.updateWhenOffscreen = true;
                skin.forceMatrixRecalculationPerRender = true;
            }
            foreach (var c in actor.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var c in actor.GetComponentsInChildren<Camera>(true)) c.enabled = false;
            foreach (var c in actor.GetComponentsInChildren<AudioSource>(true)) c.enabled = false;
            foreach (var c in actor.GetComponentsInChildren<ParticleSystem>(true)) c.gameObject.SetActive(false);
            animator.enabled = true;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            stage.SetActive(true);
            foreach(var constraint in actor.GetComponentsInChildren<ParentConstraint>(true))
            {
                if(!constraint.gameObject.activeInHierarchy || !constraint.enabled || !constraint.constraintActive
                    || constraint.sourceCount!=1 || !Mathf.Approximately(constraint.weight,1)) continue;
                var source=constraint.GetSource(0);
                if(source.sourceTransform==null || !Mathf.Approximately(source.weight,1)) continue;
                constraints.Add(new ConstraintBinding {source=source.sourceTransform,target=constraint.transform,
                    position=source.sourceTransform.InverseTransformPoint(constraint.transform.position),
                    rotation=Quaternion.Inverse(source.sourceTransform.rotation)*constraint.transform.rotation});
                constraint.enabled=false;
            }
            pose?.PreviewPoseInstant(WeaponPoseSlot.Aim);
            stats = WeaponStatCalculator.Calculate(item);
            width = item.GetMeleeDefinition().baseSettings.hitWidth;
            Primitive("바닥", PrimitiveType.Cube, new Vector3(0, -.08f, 0), new Vector3(16, .1f, 16), new Color(.10f,.12f,.15f));
            dummy = Primitive("타격 대상", PrimitiveType.Capsule, TargetPosition, new Vector3(.45f, .65f, .45f), new Color(.5f,.57f,.64f));
            CreateMonsterTarget();
            // A physical grid gives the refractive shockwave a readable background.
            for (int i = -7; i <= 7; i++)
            {
                Primitive("격자", PrimitiveType.Cube, new Vector3(i,-.017f,0), new Vector3(.012f,.01f,14), new Color(.18f,.20f,.24f));
                Primitive("격자", PrimitiveType.Cube, new Vector3(0,-.017f,i), new Vector3(14,.01f,.012f), new Color(.18f,.20f,.24f));
            }
            graph = PlayableGraph.Create("OVERBURST Combo Maker");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            var output = AnimationPlayableOutput.Create(graph, "Character", animator);
            output.SetSourcePlayable(mixer);
            foreach(var part in actor.GetComponentsInChildren<Animator>(true))
            {
                if(part==animator || !part.gameObject.activeInHierarchy || !part.isHuman || part.transform.IsChildOf(weapon.transform)) continue;
                part.enabled=true;part.runtimeAnimatorController=null;part.applyRootMotion=false;part.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var rig=new PartRig {mixer=AnimationMixerPlayable.Create(graph,2)};
                AnimationPlayableOutput.Create(graph,part.name,part).SetSourcePlayable(rig.mixer);
                partRigs.Add(rig);
            }
            graph.Play();
            Begin(0, false, true);
            Status = "드래그: 회전 · 휠: 확대 · 가운데 드래그: 이동";
        }

        private GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(stage.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
            material.color = color;
            materials.Add(material);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static void DisableBehaviours(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null) behaviour.enabled = false;
        }

        public void Begin(int index, bool continuation, bool clear)
        {
            if (!Ready) return;
            bool canBlend = continuation && current.IsValid();
            previousIndex = canBlend ? StepIndex : -1;
            previousTime = canBlend ? (float)current.GetTime() : 0;
            if (previous.IsValid()) { mixer.DisconnectInput(1); graph.DestroyPlayable(previous); }
            if (current.IsValid())
            {
                mixer.DisconnectInput(0);
                if (canBlend) { previous = current; graph.Connect(previous, 0, mixer, 1); }
                else graph.DestroyPlayable(current);
            }
            if (clear) { ClearEffects(); actor.transform.position = Vector3.zero; HitCount = CueCount = 0; ResetCombatPreview(); }
            StepIndex = Mathf.Clamp(index, 0, combo.StepCount - 1);
            var step = combo.steps[StepIndex];
            trailExecutor.Begin(step.trailPhases,
                () => { if (ShowEffects) energyFx?.EditorBeginTrailPreview(); },
                () => energyFx?.EditorEndTrailPreview());
            entry = continuation ? step.continuationStartNormalizedTime : 0;
            entryElapsed = step.playbackAcceleration.ToElapsed(entry);
            elapsed = 0;
            Progress = entry;
            lastSample = entry;
            origin = actor.transform.position;
            int count = step.attackPhases?.Length ?? 0;
            hit = new bool[count]; completed = new bool[count]; begun = new bool[count];
            cues = new bool[count][]; bases = new AttackPatternBasis[count];
            for (int i = 0; i < count; i++) cues[i] = new bool[step.attackPhases[i].vfxCues?.Length ?? 0];
            if (step.animationClip != null)
            {
                current = AnimationClipPlayable.Create(graph, step.animationClip);
                current.SetApplyFootIK(false); current.SetApplyPlayableIK(false); current.SetSpeed(0);
                graph.Connect(current, 0, mixer, 0);
            }
            mixer.SetInputWeight(0, 1); mixer.SetInputWeight(1, 0);
            foreach(var rig in partRigs)
            {
                if(rig.previous.IsValid()) {rig.mixer.DisconnectInput(1);graph.DestroyPlayable(rig.previous);}
                if(rig.current.IsValid())
                {
                    rig.mixer.DisconnectInput(0);
                    if(canBlend) {rig.previous=rig.current;graph.Connect(rig.previous,0,rig.mixer,1);}
                    else graph.DestroyPlayable(rig.current);
                }
                if(step.animationClip!=null)
                {
                    rig.current=AnimationClipPlayable.Create(graph,step.animationClip);
                    rig.current.SetApplyFootIK(false);rig.current.SetApplyPlayableIK(false);rig.current.SetSpeed(0);
                    graph.Connect(rig.current,0,rig.mixer,0);
                }
                rig.mixer.SetInputWeight(0,1);rig.mixer.SetInputWeight(1,0);
            }
            visualHeight.Begin(actor.transform.Find("VisualRoot"), step.visualHeightCurve);
            movement.Begin(step.movementPhases, Vector3.forward, d => actor.transform.position += d, entry);
            Sample(entry);
            if (clear)
            {
                energyFx?.EditorClearEnergyPreview();
                previewedElement = WeaponElement.None;
                RenderEnergy();
            }
        }

        public void Seek(float progress)
        {
            if (!Ready) return;
            int index = StepIndex;
            Playing = false;
            Begin(index, false, true);
            var step = combo.steps[index];
            float end = step.playbackAcceleration.ToElapsed(Mathf.Clamp01(progress)) * ComboMakerSession.Duration(combo,index);
            // Reconstruct event and particle history at fixed intervals for backward scrubbing too.
            while (elapsed + 1f / 60f < end) Advance(1f / 60f, false);
            if (end > elapsed) Advance(end - elapsed, false);
        }

        public void Tick(float seconds)
        {
            if (!Playing || !Ready) return;
            float remaining = Mathf.Min(seconds, .25f) * Speed;
            while (remaining > .000001f)
            {
                float dt = Mathf.Min(remaining, 1f / 60f);
                Advance(dt, true);
                remaining -= dt;
                if (!Playing) break;
            }
        }

        private void Advance(float dt, bool transition)
        {
            var step = combo.steps[StepIndex];
            elapsed += dt;
            combatClock+=dt;
            Progress = Mathf.Clamp01(step.playbackAcceleration.ToClipProgress(entryElapsed + elapsed / ComboMakerSession.Duration(combo, StepIndex)));
            Sample(Progress);
            movement.Tick(Progress);
            pose?.PreviewPoseInstant(WeaponPoseSlot.Aim);
            TickTarget();
            RenderEnergy();
            trailExecutor.Tick(Progress);
            energyFx?.EditorAdvanceEnergyPreview(dt);
            energyFx?.EditorAdvanceTrailPreview(dt);
            EvaluatePhases(step);
            foreach (var effect in effects) effect.Tick(dt);
            for (int i = effects.Count - 1; i >= 0; i--)
                if (effects[i].Finished) { effects[i].Dispose(); effects.RemoveAt(i); }
            lastSample = Progress;
            if (!transition) return;
            if (All && StepIndex < combo.StepCount - 1 && Progress >= step.comboInputWindow.SafeStart)
                Begin(StepIndex + 1, true, false);
            else if (Progress >= 1 && elapsed >= (step.playbackAcceleration.ToElapsed(1) - entryElapsed) * ComboMakerSession.Duration(combo, StepIndex) + .35f)
            {
                if (Loop) Begin(All ? 0 : StepIndex, false, true);
                else Playing = false;
            }
        }

        private void Sample(float progress)
        {
            if (!current.IsValid()) return;
            var step = combo.steps[StepIndex];
            current.SetTime(progress * step.animationClip.length);
            float blend = step.transitionDuration > 0 ? Mathf.Clamp01(elapsed / step.transitionDuration) : 1;
            if (previous.IsValid() && previousIndex >= 0)
            {
                var old = combo.steps[previousIndex];
                previous.SetTime(Mathf.Min(old.animationClip.length, previousTime + elapsed * combo.baseAnimationSpeed * old.animationSpeedMultiplier));
                mixer.SetInputWeight(1, 1-blend);
                mixer.SetInputWeight(0, blend);
            }
            foreach(var rig in partRigs)
            {
                if(rig.current.IsValid()) rig.current.SetTime(progress*step.animationClip.length);
                if(rig.previous.IsValid() && previousIndex>=0)
                {rig.previous.SetTime(previous.GetTime());rig.mixer.SetInputWeight(1,1-blend);rig.mixer.SetInputWeight(0,blend);}
            }
            graph.Evaluate(0);
            visualHeight.Tick(progress);
            foreach(var binding in constraints)
                binding.target.SetPositionAndRotation(binding.source.TransformPoint(binding.position),binding.source.rotation*binding.rotation);
            pose?.PreviewPoseInstant(WeaponPoseSlot.Aim);
        }

        private void EvaluatePhases(MeleeComboStepData step)
        {
            EvaluateHeavyDischarge(step);
            for (int i = 0; i < (step.attackPhases?.Length ?? 0); i++)
            {
                var phase = step.attackPhases[i];
                if (completed[i] || Progress < phase.SafeStart || phase.attackPattern == null) continue;
                if (!begun[i]) { bases[i] = new AttackPatternBasis(actor.transform.position, Vector3.forward); begun[i] = true; }
                if (phase.basisFollowMode == AttackBasisFollowMode.FollowOwnerPosition) bases[i] = bases[i].WithOrigin(actor.transform.position);
                var pattern = phase.ResolvePattern(stats.range, stats.meleeSlashAngle, width);
                float p = pattern.EvaluateProgress(Mathf.InverseLerp(phase.SafeStart, phase.SafeEnd, Progress));
                bool canHit = true;
                if (phase.progressSource != AttackProgressSource.NormalizedTime)
                {
                    canHit = combo.TryGetAttackTrajectoryStep(StepIndex, out var bakedStep, out _)
                        && bakedStep.TryGetPhase(i, phase.progressSource, out var bakedPhase)
                        && bakedPhase.TryEvaluate(Progress, out _, out p);
                }
                if (canHit)
                {
                    if (ShowEffects)
                        for (int c = 0; c < cues[i].Length; c++)
                            if (!cues[i][c] && p + .0001f >= phase.vfxCues[c].triggerProgress)
                            { cues[i][c] = true; SpawnCue(phase, phase.vfxCues[c], pattern, bases[i]); CueCount++; }
                    if (!hit[i] && AttackPatternEvaluator.TryEvaluate(pattern, bases[i],
                        targetVolume!=null?targetVolume.CurrentHurtVolume:new CombatTargetVolume(dummy.transform.position, .225f, .65f), out float required) && p + .0001f >= required)
                    {
                        hit[i] = true; HitCount++;
                        if (ShowHits || ShowBlood) SpawnHit(pattern,phase);
                        ApplyTargetReaction(phase);
                    }
                }
                if (Progress >= phase.SafeEnd) completed[i] = true;
            }
        }

        private void SpawnCue(AttackPhaseData phase, AttackVfxCueData cue, AttackPatternRuntimeData pattern, AttackPatternBasis basis)
        {
            var definition = cue.definition;
            if (definition == null) return;
            var prefab = definition.neutralPrefab;
            if (prefab == null) return;
            var rotation = Quaternion.LookRotation(basis.Forward, Vector3.up);
            var position = cue.placementMode == AttackVfxPlacementMode.OwnerOrigin ? basis.Origin
                : cue.placementMode == AttackVfxPlacementMode.WeaponTracePoint && trace != null && trace.WeaponTip != null
                    ? trace.WeaponTip.position : basis.GetPatternOrigin(pattern);
            if (cue.placementMode == AttackVfxPlacementMode.PatternGround) position.y = definition.groundSurfaceOffset;
            position += rotation * definition.localPositionOffset;
            float slope = phase.ResolveVfxSwingRotationOffset(phase.useBakedVfxSwingSlope ? phase.bakedVfxSwingSlopeDegrees : 0);
            rotation *= Quaternion.Euler(0,0,cue.ResolveSwingSlope(slope)) * Quaternion.Euler(definition.localEulerOffset + cue.localEulerOffset);
            Vector3 scale = definition.baseScale * cue.SafeScaleMultiplier * phase.geometry.SafeVfxScaleMultiplier;
            bool mirror = cue.mirrorAxis == AttackVfxMirrorAxis.Horizontal || definition.mirrorRightToLeft && pattern.Direction == AttackFillDirection.RightToLeft;
            if (mirror) { int axis = (int)definition.horizontalMirrorScaleAxis; scale[axis] = -scale[axis]; }
            if (cue.mirrorAxis == AttackVfxMirrorAxis.Vertical) scale.y = -scale.y;
            var go = InstantiateEffect(prefab, position, rotation, scale);
            go.GetComponent<VfxMirrorCompensation>()?.Apply(mirror);
            go.GetComponent<SwordShockwavePlayback>()?.Configure(cue.SafeShockwaveIntensity, cue.SafeShockwaveSpeed);
            ActivateEffect(go, SwordShockwavePlayback.ResolveCueLifetime(prefab, definition.lifetime, cue.SafeShockwaveSpeed));
        }

        private void SpawnHit(AttackPatternRuntimeData pattern,AttackPhaseData phase)
        {
            GameObject prefab = HitOverride;
            var catalog = Resources.Load<MeleeElementHitVfxCatalog>(MeleeElementHitVfxCatalog.ResourcePath);
            if (prefab == null && catalog != null) catalog.TryResolve(Element, out prefab);
            Vector3 hitPoint=targetVolume!=null?targetVolume.CurrentHurtVolume.Center:dummy.transform.position;
            float bodySize=1f;
            if(targetVolume!=null)hitPoint=CombatTargetVfxPlacement.ResolveContact(targetVolume,hitPoint,Vector3.forward,out bodySize);
            if (prefab != null && ShowHits)
            {
                bool fromCatalog=HitOverride==null&&catalog!=null;
                float speed=fromCatalog?catalog.ResolvePlaybackSpeed(Element):1;
                Vector3 scale=fromCatalog?prefab.transform.localScale*catalog.ResolveTierScale(bodySize)*catalog.ResolveHitScale(Element):prefab.transform.localScale;
                var go = InstantiateEffect(prefab, hitPoint, Quaternion.identity, scale);
                var controller=go.GetComponent<MeleeElementHitVfxController>();
                controller?.SetElement(Element);controller?.SetPlaybackSpeed(speed);
                ActivateEffect(go, fromCatalog ? catalog.ResolveLifetime(Element)/speed : 3);
            }
            var blood = Resources.Load<BloodHitCatalog>(BloodHitCatalog.ResourcePath);
            var bloodProfile=BloodProfile!=null?BloodProfile:targetBlood;
            if (!ShowBlood || bloodProfile == null || bloodProfile.suppressBlood) return;
            var shape=pattern.IsThrust?CombatImpactShape.Thrust:phase.vfxSwingSettings.orientation==AttackVfxSwingOrientation.Vertical?CombatImpactShape.Downward:CombatImpactShape.Sweep;
            var direction=shape==CombatImpactShape.Sweep?Vector3.right*(phase.vfxSwingSettings.reverseDirection?-1:1):Vector3.forward;
            var weight=WeightOverride!=null?WeightOverride:TargetDefinition?.MovementProfile?.HitWeightProfile;
            float weightSize=weight!=null&&weight.Weight==EnemyHitWeight.Heavy?1.28f:weight!=null&&weight.Weight==EnemyHitWeight.Standard?1.14f:1;
            float visualSize=bodySize*weightSize;
            if(weightSize>1.2f)visualSize=Mathf.Max(visualSize,1.25f);
            else if(weightSize>1f)visualSize=Mathf.Max(visualSize,.8f);
            visualSize=Mathf.Clamp(visualSize,.55f,1.95f);
            if(PackBlood){SpawnPackBlood(bloodProfile,shape,hitPoint,direction,visualSize);return;}
            if(blood==null)return;
            var asset = blood.Resolve(shape);
            uint bloodSeed = BloodHitVfxService.CosmeticSeed(17, HitCount, 0, 1);
            BloodHitCatalog.SweepVariation bloodVariation = null;
            if (HitCount <= 1) previousBloodVariation = -1;
            if (shape == CombatImpactShape.Sweep && blood.TryResolveSweep(bloodSeed, previousBloodVariation, out int bloodIndex, out bloodVariation))
            { asset = bloodVariation.graph; previousBloodVariation = bloodIndex; }
            if (asset == null) return;
            var bloodGo = new GameObject("피격 혈흔 프리뷰");
            bloodGo.SetActive(false); bloodGo.transform.SetParent(stage.transform, false);
            bloodGo.transform.position = hitPoint;
            bloodGo.transform.rotation=Quaternion.LookRotation(direction,Vector3.up)*(bloodVariation!=null?Quaternion.Euler(bloodVariation.localEuler):shape==CombatImpactShape.Downward?Quaternion.identity:Quaternion.Euler(0,90,0));
            var vfx = bloodGo.AddComponent<VisualEffect>();
            vfx.visualEffectAsset = asset; vfx.initialEventName="BloodIdle"; vfx.startSeed = bloodVariation!=null?bloodSeed:17; vfx.resetSeedOnPlay = false;
            if (vfx.HasFloat("HitSize")) vfx.SetFloat("HitSize", bloodProfile.size*visualSize*(HeavyDefinition!=null?1.1f:1f)*(bloodVariation!=null?bloodVariation.sizeMultiplier:1f));
            if (vfx.HasVector4("BloodColorMain")) vfx.SetVector4("BloodColorMain", bloodProfile.mainColor.linear);
            if (vfx.HasVector4("BloodColorSecondary")) vfx.SetVector4("BloodColorSecondary", bloodProfile.secondaryColor.linear);
            if (vfx.HasVector4("BloodSpecularColor")) vfx.SetVector4("BloodSpecularColor", bloodProfile.specularColor.linear);
            if (vfx.HasFloat("SpecularValue")) vfx.SetFloat("SpecularValue", bloodProfile.specular);
            if (vfx.HasInt("LoopCount")) vfx.SetInt("LoopCount", 1);
            ActivateEffect(bloodGo, blood.lifetime);
            SpawnBloodDecal(blood,bloodProfile,shape,hitPoint,direction,visualSize);
        }

        private GameObject InstantiateEffect(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            // Dedicated inactive container prevents OnEnable on supplier scripts.
            var holder = new GameObject("VFX setup"); holder.SetActive(false); holder.transform.SetParent(stage.transform, false);
            var go = Object.Instantiate(prefab, holder.transform, false);
            DisableBehaviours(go);
            go.SetActive(false);
            go.transform.SetParent(stage.transform, false);
            Object.DestroyImmediate(holder);
            go.transform.SetPositionAndRotation(position, rotation); go.transform.localScale = scale;
            foreach (var audio in go.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
            return go;
        }

        private void ActivateEffect(GameObject go, float lifetime, Material[] ownedMaterials=null)
        {
            go.SetActive(true);
            effects.Add(new Effect(go, lifetime, ownedMaterials));
        }

        public void SetView(int view)
        {
            productView = view == 4;
            yaw = productView ? 45 : view == 1 ? 180 : view == 2 ? 90 : 135;
            pitch = productView ? 45 : view == 3 ? 80 : view == 0 ? 22 : 10;
            distance = productView ? 20f : Mathf.Max(4.8f,Mathf.Abs(TargetPosition.z)*2f);
            pan = Vector3.zero;
        }

        public Texture Render(Rect rect)
        {
            if (!Ready || rendering || preparingEffects || disposePending || rect.width < 2 || rect.height < 2) return null;
            PrepareEffects();
            if(disposePending)return null;
            // Preview scene animators/constraints can be visited by Editor repaint between ticks.
            // Reassert all humanoid parts and the head binding immediately before rendering.
            Sample(Progress);
            RenderEnergy();
            var camera = renderer.camera;
            camera.orthographic = productView;
            if (productView)
                camera.orthographicSize = distance * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            Vector3 focus = productView ? new Vector3(0, .015f, .24f)
                : new Vector3(0,framingHeight,Mathf.Max(.5f,TargetPosition.z*.5f));
            camera.transform.position = focus + pan - camera.transform.forward * distance;
            int w=Mathf.Max(2,Mathf.RoundToInt(rect.width)),h=Mathf.Max(2,Mathf.RoundToInt(rect.height));
            if(surface==null || surface.width!=w || surface.height!=h)
            {
                if(surface!=null) {surface.Release();Object.DestroyImmediate(surface);}
                surface=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32) {name="Combo Maker Surface",hideFlags=HideFlags.HideAndDontSave};
                surface.Create();
            }
            camera.targetTexture=surface;camera.aspect=(float)w/h;
            // SingleCameraRequest bypasses URP's camera-stack VFX material preparation.
            VFXManager.PrepareCamera(camera);
            rendering=true;
            var previousTarget=RenderTexture.active;
            try
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                    new UniversalRenderPipeline.SingleCameraRequest { destination=surface });
            }
            finally
            {
                RenderTexture.active=previousTarget;
                rendering=false;
                if(disposePending) EditorApplication.delayCall+=DeferredDispose;
            }
            return surface;
        }

        public void Orbit(Vector2 delta)
        {
            yaw+=delta.x*.4f;pitch=Mathf.Clamp(pitch+delta.y*.3f,-10,85);
        }
        public void Pan(Vector2 delta) {if(renderer!=null) pan+=(renderer.camera.transform.right*-delta.x+renderer.camera.transform.up*delta.y)*distance*.001f;}
        public void Zoom(float delta) => distance=Mathf.Clamp(distance*(1+delta*.05f),productView?6f:1f,productView?28f:18f);

        public List<(Vector2[] points, Color color)> GeometryLines(Vector2 size)
        {
            var result=new List<(Vector2[],Color)>();
            if (!Ready || !ShowGeometry) return result;
            var step = combo.steps[StepIndex];
            foreach (var phase in step.attackPhases ?? Array.Empty<AttackPhaseData>())
            {
                if (phase.attackPattern == null) continue;
                var pattern = phase.ResolvePattern(stats.range, stats.meleeSlashAngle, width);
                Vector3 center = actor.transform.position + Vector3.forward * pattern.ForwardOffset + Vector3.up * .03f;
                var color = Progress >= phase.SafeStart && Progress <= phase.SafeEnd ? new Color(1,.67f,.25f,.9f) : new Color(.45f,.65f,.7f,.35f);
                var points = new List<Vector3>();
                if (pattern.Shape == AttackAreaShape.Rectangle)
                {
                    points.Add(center + new Vector3(-pattern.Width/2,0,0)); points.Add(center + new Vector3(-pattern.Width/2,0,pattern.Range));
                    points.Add(center + new Vector3(pattern.Width/2,0,pattern.Range)); points.Add(center + new Vector3(pattern.Width/2,0,0)); points.Add(points[0]);
                }
                else
                {
                    float angle = pattern.Shape == AttackAreaShape.Circle ? 360 : pattern.Angle;
                    if (angle < 360) points.Add(center);
                    for (int n = 0; n <= 64; n++) points.Add(center + Quaternion.Euler(0, -angle/2 + angle*n/64 + pattern.AngleOffset, 0) * Vector3.forward * pattern.Range);
                    if (angle < 360) points.Add(center);
                }
                result.Add((points.Select(p=>ToSurface(p,size)).ToArray(),color));
            }
            return result;
        }

        private Vector2 ToSurface(Vector3 world, Vector2 size)
        {
            Vector3 p = renderer.camera.WorldToViewportPoint(world);
            return new Vector2(p.x*size.x,(1-p.y)*size.y);
        }
        public void ClearEffects()
        {
            trailExecutor.Cancel();
            foreach (var effect in effects) effect.Dispose();
            effects.Clear();
            energyFx?.EditorClearTrailPreview();
        }
        public void Dispose()
        {
            if(rendering||preparingEffects){disposePending=true;return;}
            disposePending=false;
            EditorApplication.delayCall-=DeferredDispose;
            Playing = false; ClearEffects(); movement.Cancel(); visualHeight.Cancel();
            energyFx?.EditorClearEnergyPreview();energyFx=null;monsterVisual=null;targetReaction=null;targetIdle=targetHit=null;targetVolume=null;targetBlood=null;
            if (graph.IsValid()) graph.Destroy();
            partRigs.Clear(); constraints.Clear();
            if(renderer?.camera!=null) renderer.camera.targetTexture=null;
            if(surface!=null) {surface.Release();Object.DestroyImmediate(surface);surface=null;}
            renderer?.Cleanup(); renderer = null;
            foreach (var mat in materials) if (mat != null) Object.DestroyImmediate(mat);
            materials.Clear(); actor = weapon = dummy = stage = null;
            combo = null; animator = null; pose = null; trace = null;
        }
        private void DeferredDispose()=>Dispose();

        private sealed class Effect : IDisposable
        {
            private readonly GameObject root;
            private readonly ParticleSystem[] particles;
            private readonly VisualEffect[] graphs;
            private readonly SwordShockwavePlayback shockwave;
            private readonly float lifetime;
            private float age;
            private bool graphStarted;
            private float graphSeconds;
            public bool PendingGraph => graphs.Length>0&&(!graphStarted||graphSeconds>0);
            private readonly DecalProjector decal;
            private readonly Material decalMaterial;
            private readonly Material[] ownedMaterials;
            private readonly BloodPackGroundPattern groundPattern;
            public bool Finished => age >= lifetime;
            public Effect(GameObject go, float requestedLifetime, Material[] ownedMaterials=null)
            {
                root = go;
                this.ownedMaterials=ownedMaterials??Array.Empty<Material>();
                groundPattern=go.GetComponent<BloodPackGroundPattern>();
                decal=go.name=="혈흔 바닥 프리뷰"?go.GetComponent<DecalProjector>():null;
                if(decal!=null){decalMaterial=decal.material;decal.enabled=false;}
                particles = go.GetComponentsInChildren<ParticleSystem>(false);
                graphs = go.GetComponentsInChildren<VisualEffect>(false);
                foreach (var p in particles)
                {
                    p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    p.useAutoRandomSeed = false; p.randomSeed = 17;
                    var main = p.main; main.stopAction = ParticleSystemStopAction.None;
                    p.Simulate(0, false, true, false);
                }
                foreach (var vfx in graphs)
                {
                    vfx.initialEventName="ComboMakerIdle";
                    vfx.Reinit();vfx.pause=true;vfx.playRate=0;
                }
                lifetime = requestedLifetime > 0 ? requestedLifetime : Mathf.Max(1, particles.Length == 0 ? 1 : particles.Max(p => p.main.duration + p.main.startLifetime.constantMax + p.main.startDelay.constantMax));
                shockwave = go.GetComponent<SwordShockwavePlayback>();
                if (shockwave != null)
                {
                    shockwave.enabled = false; // Shared playback driven by the preview clock.
                    shockwave.RestartVfx();
                    foreach (var p in particles) p.Pause(false);
                }
            }
            public void Tick(float dt)
            {
                age += dt;
                if(decal!=null){decal.enabled=age>=.16f;decal.fadeFactor=1-Mathf.Clamp01((age-.16f-BloodGroundDecalService.HoldSeconds)/BloodGroundDecalService.FadeSeconds);}
                if(decal!=null&&groundPattern!=null)groundPattern.Apply(decal,Mathf.Max(0,age-.16f));
                foreach (var p in particles) if (p != null) p.Simulate(dt, false, false, false);
                graphSeconds+=dt;
                shockwave?.Advance(dt);
            }
            public void PrepareGraph()
            {
                if(!PendingGraph)return;
                foreach(var vfx in graphs)if(vfx!=null)
                {
                    if(!graphStarted)vfx.Play();
                    if(graphSeconds>0){uint steps=(uint)Mathf.Max(1,Mathf.CeilToInt(graphSeconds*60));vfx.Simulate(graphSeconds/steps,steps);}
                    // Simulate queues work; an explicit native frame submits the GPU simulation.
                    // A zero play rate prevents that flush from adding wall-clock time.
                    vfx.AdvanceOneFrame();
                }
                graphStarted=true;graphSeconds=0;
            }
            public void Dispose()
            {
                if (root == null) return;
                shockwave?.StopAndClearVfx();
                foreach (var p in particles) if (p != null) p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                foreach (var trail in root.GetComponentsInChildren<TrailRenderer>(true)) trail.Clear();
                foreach (var vfx in graphs) if (vfx != null) vfx.Stop();
                Object.DestroyImmediate(root);
                if(decalMaterial!=null)Object.DestroyImmediate(decalMaterial);
                foreach(var material in ownedMaterials)if(material!=null)Object.DestroyImmediate(material);
            }
        }
    }
}
