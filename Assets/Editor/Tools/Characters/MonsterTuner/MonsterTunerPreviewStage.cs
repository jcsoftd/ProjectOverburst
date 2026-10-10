using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.MonsterTuner
{
    internal sealed class MonsterTunerPreviewStage : IDisposable
    {
        private Scene scene;
        private GameObject holder, aura, comparison;
        private Material floorMaterial;
        private VolumeProfile volumeProfile;
        private RenderTexture surface;
        private PlayableGraph graph;
        private AnimationClipPlayable clipPlayable;
        private AnimationClip clip;
        private EnemyAbilityDefinition ability;
        private Animator animator;
        private MeleeElementStatusAuraPresentation auraPresentation;
        private ParticleSystem[] particles = Array.Empty<ParticleSystem>();
        private GameObject effectsRoot;
        private GameObject comparisonPrefab;
        private string auraAssetStamp;
        private bool auraAssetsChanged;
        private UnityEngine.VFX.VisualEffect[] graphs = Array.Empty<UnityEngine.VFX.VisualEffect>();
        private float auraTime;
        private ParticleSystem cueParticles;
        public bool Loop { get; set; } = true;
        public EnemyAbilityDefinition Ability => ability;
        public bool HasGraphEffects => graphs.Length > 0;
        public Vector3 LaunchOrigin { get; private set; }
        public Vector3 ProjectilePosition { get; private set; }
        public bool ProjectileVisible { get; private set; }
        private int currentStacks = 1;
        private MeleeElementStatusAuraType currentAuraType;
        private Vector3 focus;
        private float yaw = QuarterViewCamera.DefaultYaw, pitch = 55f, distance = 6f, fitDistance = 6f;
        private bool needsRender = true;
        private MonsterTunerSession session;
        private readonly Dictionary<string, (string target, string property, MonsterTunerValue value)> actorBaseline = new Dictionary<string, (string, string, MonsterTunerValue)>();
        public GameObject Actor { get; private set; }
        public EnemyActor Enemy => Actor != null ? Actor.GetComponent<EnemyActor>() : null;
        public Camera Camera { get; private set; }
        public bool Playing { get; set; }
        public float Speed { get; set; } = 1f;
        public float Time { get; private set; }
        public float NormalizedTime { get; private set; }
        public float Duration => ability != null ? ability.ResolveExecutionDuration(AttackSpeed) : clip != null ? clip.length : 1f;
        public float AttackSpeed => Enemy?.Melee != null ? Enemy.Melee.AbilityAnimationSpeed : 1f;
        public RenderTexture Surface => surface;
        public bool NeedsRender => needsRender;
        public int ViewIndex { get; private set; } = 2;
        public float ZoomPercent => fitDistance / Mathf.Max(.01f, distance) * 100f;
        public AnimationClip Clip => clip;
        public string Message { get; private set; } = "몬스터를 선택하세요.";
        public static int LiveStages { get; private set; }
        private bool counted;

        public void Load(MonsterTunerSession data)
        {
            Dispose();
            session = data;
            if (data?.Definition?.ActorPrefab == null) { Message = "액터 프리팹 연결이 없습니다."; return; }
            try
            {
                scene = EditorSceneManager.NewPreviewScene();
                counted = true; LiveStages++;
                holder = Root("Monster Tuner isolated preview");
                holder.SetActive(false);
                Actor = Object.Instantiate(data.Definition.ActorPrefab.gameObject, holder.transform, false);
                Actor.name = data.Definition.DisplayName;
                Sanitize(Actor);
                if (Actor.GetComponent<EnemyStrongAttackWarning>() == null)
                {
                    var warning = Actor.AddComponent<EnemyStrongAttackWarning>(); warning.enabled = false;
                }
                RefreshActorEdits();
                animator = Enemy != null ? Enemy.Animator : Actor.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.runtimeAnimatorController = data.Definition.AnimationProfile?.RuntimeController;
                }
                RefreshValues();
                Camera = Root("Monster Tuner camera").AddComponent<Camera>();
                Camera.enabled = false; Camera.cameraType = CameraType.Game; Camera.scene = scene;
                Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = new Color(.16f, .19f, .23f);
                Camera.fieldOfView = 38f; Camera.nearClipPlane = .02f; Camera.farClipPlane = 500f;
                Camera.useOcclusionCulling = false; Camera.allowHDR = true;
                var cameraData = Camera.GetUniversalAdditionalCameraData();
                cameraData.renderPostProcessing = true; cameraData.volumeLayerMask = 1 << 31;
                cameraData.requiresColorOption = CameraOverrideOption.On; cameraData.requiresDepthOption = CameraOverrideOption.On;
                for (int i = 0; i < 2; i++)
                {
                    var light = Root("Monster Tuner light " + i).AddComponent<Light>();
                    light.type = LightType.Directional; light.shadows = LightShadows.None; light.intensity = i == 0 ? 2.0f : 1.15f;
                    light.color = i == 0 ? new Color(1f, .94f, .85f) : new Color(.62f, .76f, 1f);
                    light.transform.rotation = i == 0 ? Quaternion.Euler(45f, -30f, 0f) : Quaternion.Euler(320f, 130f, 0f);
                }
                var volumeRoot = Root("Monster Tuner bloom"); volumeRoot.layer = 31;
                var volume = volumeRoot.AddComponent<Volume>(); volume.isGlobal = true;
                volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = volumeProfile;
                var bloom = volumeProfile.Add<Bloom>(true); bloom.threshold.Override(.7f); bloom.intensity.Override(.65f);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                SceneManager.MoveGameObjectToScene(floor, scene); floor.hideFlags = HideFlags.HideAndDontSave;
                Object.DestroyImmediate(floor.GetComponent<Collider>());
                floor.transform.position = Vector3.down * .015f; floor.transform.localScale = Vector3.one * 8f;
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                floorMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, color = new Color(.23f, .26f, .3f) };
                floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                holder.SetActive(true);
                animator?.Rebind();
                SetClip(data.Definition.AnimationProfile?.Idle);
                Fit(); SetView(ViewIndex);
                Message = "우클릭 회전 · 중간 버튼 이동 · 휠 확대/축소";
            }
            catch { Dispose(); throw; }
        }
        private GameObject Root(string name)
        {
            var obj = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(obj, scene); return obj;
        }
        private static void Sanitize(GameObject root)
        {
            foreach (var node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.hideFlags = HideFlags.HideAndDontSave;
            foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true)) if (script != null) script.enabled = false;
            foreach (var audio in root.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
            foreach (var camera in root.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
            foreach (var particle in root.GetComponentsInChildren<ParticleSystem>(true))
            { var main = particle.main; main.playOnAwake = false; particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
        }
        public void RefreshValues()
        {
            if (Actor == null || session?.Definition == null) return;
            RefreshActorEdits();
            // Same baseline as a level-one lease, without AI/ability controllers or status effects.
            float attackSpeed = (session.Definition.Grade != null ? session.Definition.Grade.AttackSpeedMultiplier : 1f)
                * session.Value("variant", "attackSpeedMultiplier").number;
            Enemy?.Melee?.SetRuntimeAttackSpeedMultiplier(session.Definition.Grade != null && session.Definition.Grade.GradeType == EnemyGradeType.Boss
                ? Mathf.Min(OverburstBalanceTable.Current.EnemyAttackCap, attackSpeed)
                : EnemyRuntimeStats.ResolveAuthoredAttackSpeed(attackSpeed, 1f, OverburstBalanceTable.Current.EnemyAttackCap));
            float gradeScale = session.Definition.Grade != null ? session.Definition.Grade.ScaleMultiplier : 1f;
            Vector3 visual = session.Value("variant", "visualScale").vector * gradeScale;
            Vector3 collision = session.Value("variant", "collisionScale").vector * gradeScale;
            Vector3 anchors = session.Value("variant", "anchorScale").vector * gradeScale;
            if (Enemy?.VisualRoot != null) Enemy.VisualRoot.localScale = visual;
            if (Enemy?.CollisionRoot != null) Enemy.CollisionRoot.localScale = collision;
            if (Enemy?.Anchors != null) Enemy.Anchors.localScale = anchors;
            var target = Actor.GetComponent<CombatTarget>();
            if (target != null) { target.RefreshVolumeFromPrimaryCollider(); target.SetVariantHurtScale(visual); }
            Actor.GetComponent<CombatTargetVfxPlacement>()?.SetVariantScale(visual);
            auraPresentation?.ConfigureTarget(target);
            needsRender = true;
        }
        private void RefreshActorEdits()
        {
            foreach (var patch in session.edits.Where(e => e.target.StartsWith("component:", StringComparison.Ordinal)))
            {
                if (actorBaseline.ContainsKey(patch.Key)) continue;
                var component = MonsterTunerAddress.ResolveComponent(Actor, patch.target);
                if (component != null) actorBaseline.Add(patch.Key, (patch.target, patch.property, MonsterTunerValue.Read(new SerializedObject(component).FindProperty(patch.property), Actor)));
            }
            foreach (var baseline in actorBaseline.Values)
            {
                var component = MonsterTunerAddress.ResolveComponent(Actor, baseline.target);
                if (component == null) continue;
                var serialized = new SerializedObject(component); baseline.value.Write(serialized.FindProperty(baseline.property), Actor); serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            session.ApplyActor(Actor);
        }
        public void SetClip(AnimationClip next, EnemyAbilityDefinition attack = null)
        {
            if (graph.IsValid()) graph.Destroy();
            clip = next; ability = attack; Time = 0f;
            if (animator != null && next != null)
            {
                graph = PlayableGraph.Create("Monster Tuner manual animation");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                clipPlayable = AnimationClipPlayable.Create(graph, next);
                clipPlayable.SetApplyFootIK(false); clipPlayable.SetApplyPlayableIK(false);
                clipPlayable.SetSpeed(0);
                var output = AnimationPlayableOutput.Create(graph, "Monster", animator);
                output.SetSourcePlayable(clipPlayable); graph.Play();
            }
            Sample(0f);
        }
        public void Sample(float time, bool reconstructEffects = true)
        {
            Time = Mathf.Clamp(time, 0f, Mathf.Max(.001f, Duration));
            float normalized = clip != null ? Time / Mathf.Max(.001f, clip.length) : 0f;
            if (ability != null)
            {
                float t = Mathf.Max(0f, Time - ability.ResolveWindupDelay(AttackSpeed));
                float low = 0f, high = 1f;
                for (int i = 0; i < 20; i++) { float mid = (low + high) * .5f; if (ability.ResolvePacedTime(mid, AttackSpeed) < t) low = mid; else high = mid; }
                normalized = (low + high) * .5f;
            }
            NormalizedTime = Mathf.Clamp01(normalized);
            if (graph.IsValid())
            {
                var position = Actor.transform.localPosition; var rotation = Actor.transform.localRotation;
                clipPlayable.SetTime(NormalizedTime * clip.length); graph.Evaluate(0f);
                Actor.transform.localPosition = position; Actor.transform.localRotation = rotation;
            }
            auraPresentation?.ConfigureTarget(Actor != null ? Actor.GetComponent<CombatTarget>() : null);
            if (reconstructEffects && effectsRoot != null) ReconstructEffects(Time);
            SampleAttackVisuals();
            needsRender = true;
        }
        public void Advance(float delta)
        {
            if (!Playing) return;
            float next = Time + Mathf.Min(.1f, delta) * Speed;
            if (next > Duration && !Loop) Playing = false;
            bool wrapped = next > Duration && Loop;
            Sample(wrapped ? next % Mathf.Max(.01f, Duration) : next, false);
            if (wrapped && effectsRoot != null) ReconstructEffects(0f);
            foreach (var system in particles) if (system != null && system.gameObject.activeInHierarchy)
                system.Simulate(Mathf.Min(.1f, delta) * Speed, false, false, true);
            foreach (var effect in graphs) if (effect != null && effect.gameObject.activeInHierarchy)
                effect.Simulate(Mathf.Min(.1f, delta) * Speed, 1);
            auraTime += Mathf.Min(.1f, delta) * Speed;
        }
        private void SampleAttackVisuals()
        {
            ProjectileVisible = false;
            if (ability == null || Actor == null) { if (cueParticles != null) cueParticles.gameObject.SetActive(false); return; }
            float hit = ability.ResolveWindupDelay(AttackSpeed) + ability.ResolvePacedTime(ability.HitNormalizedTime, AttackSpeed);
            if (ability.IsParryable)
            {
                if (cueParticles == null)
                {
                    cueParticles = Root("Monster Tuner parry glint").AddComponent<ParticleSystem>();
                    var library = Resources.Load<EnemyTelegraphVisualLibrary>("Enemies/Balance/EnemyTelegraphVisualLibrary");
                    EnemyParryCueVisual.Configure(cueParticles, library != null ? library.ParryGlint : null);
                }
                var warning = Actor.GetComponent<EnemyStrongAttackWarning>();
                bool hasWindow = ability.TryGetParryMotionWindow(0, out var window);
                float cueAt = hasWindow ? ability.ResolveWindupDelay(AttackSpeed) + ability.ResolvePacedTime(window.x, AttackSpeed)
                    : Mathf.Max(0f, hit - EnemyAbilityController.ParryLeadSeconds);
                float elapsed = Time - cueAt;
                bool visible = elapsed >= 0f && Time <= hit;
                cueParticles.gameObject.SetActive(visible);
                if (visible)
                {
                    if (ability.TryResolveAttackCue(Enemy, out var socket, out var cuePosition))
                    {
                        warning.SetAttackCue(socket, Vector3.zero, cuePosition, ability.ParryCueScale);
                    }
                    else
                    {
                        warning.SetAttackCue(null, Vector3.zero);
                    }
                    warning.ConfigureSignalVisual(cueParticles);
                    cueParticles.transform.position = warning.ResolveCuePosition(Camera);
                    cueParticles.Simulate(elapsed, false, true, true); cueParticles.Pause(false);
                }
            }
            else if (cueParticles != null) cueParticles.gameObject.SetActive(false);
            if (ability.ExecutionMode != EnemyAbilityExecutionMode.Projectile) return;
            var executor = Actor.GetComponent<EnemyThemeSpecialExecutor>();
            var source = Enumerable.Range(0, session.Definition.AbilitySet.Count).Select(session.Definition.AbilitySet.GetAbility).FirstOrDefault(a => a != null && a.AbilityId == ability.AbilityId);
            LaunchOrigin = executor != null ? executor.ResolveMuzzlePosition(source, Actor.transform.forward) : Actor.transform.position + Vector3.up * .8f;
            // Re-evaluate the socket at the release pose, then restore the current pose.
            if (Time >= hit && graph.IsValid())
            {
                clipPlayable.SetTime(ability.HitNormalizedTime * clip.length); graph.Evaluate(0f);
                LaunchOrigin = executor != null ? executor.ResolveMuzzlePosition(source, Actor.transform.forward) : LaunchOrigin;
                float low = 0f, high = 1f, elapsed = Mathf.Max(0f, Time - ability.ResolveWindupDelay(AttackSpeed));
                for (int i = 0; i < 20; i++) { float mid = (low + high) * .5f; if (ability.ResolvePacedTime(mid, AttackSpeed) < elapsed) low = mid; else high = mid; }
                clipPlayable.SetTime((low + high) * .5f * clip.length); graph.Evaluate(0f);
            }
            float travel = Mathf.Clamp((Time - hit) * 10f, 0f, ability.Range + 2f);
            ProjectilePosition = LaunchOrigin + Actor.transform.forward * travel;
            ProjectileVisible = Time >= hit && (Time - hit) * 10f <= ability.Range + 2f;
        }
        public void ShowAura(MeleeElementStatusAuraType type, int stacks, GameObject compare)
        {
            ClearAura(); if (Actor == null) return;
            currentAuraType = type; currentStacks = stacks;
            effectsRoot = Root("Monster Tuner preview effects"); effectsRoot.SetActive(false);
            var prefab = Resources.Load<MeleeElementStatusAuraPresentation>(MeleeElementStatusAuraPresentation.ResourcePath);
            if (prefab != null)
            {
                aura = Object.Instantiate(prefab.gameObject, effectsRoot.transform, false); Sanitize(aura);
                auraPresentation = aura.GetComponentInChildren<MeleeElementStatusAuraPresentation>(true);
            }
            if (compare != null) { comparison = Object.Instantiate(compare, effectsRoot.transform, false); Sanitize(comparison); }
            comparisonPrefab = compare;
            auraAssetStamp = AuraAssetStamp(prefab, compare);
            EditorApplication.projectChanged += MarkAuraAssetsChanged;
            effectsRoot.SetActive(true);
            var target = Actor.GetComponent<CombatTarget>();
            if (auraPresentation != null)
            {
                auraPresentation.ClearAllAuras(); auraPresentation.ConfigureTarget(target);
                auraPresentation.SetAuraActive(type, true); auraPresentation.SetStackCount(type, stacks);
            }
            if (comparison != null) comparison.transform.position = target != null ? CombatTargetVfxPlacement.ResolveVolume(target).Center : Actor.transform.position;
            particles = effectsRoot.GetComponentsInChildren<ParticleSystem>(true);
            graphs = effectsRoot.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true);
            foreach (var effect in graphs) { effect.pause = true; effect.Reinit(); }
            ReconstructEffects(.15f); needsRender = true;
        }
        private static string AuraAssetStamp(Object prefab, Object compare)
        {
            string Stamp(Object asset)
            {
                string path = AssetDatabase.GetAssetPath(asset);
                return string.IsNullOrEmpty(path) ? string.Empty : path + ":" + AssetDatabase.GetAssetDependencyHash(path);
            }
            return Stamp(prefab) + "|" + Stamp(compare);
        }
        private void MarkAuraAssetsChanged() { auraAssetsChanged = true; needsRender = true; }
        private void RefreshAuraAssets()
        {
            if (effectsRoot == null || !auraAssetsChanged || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            auraAssetsChanged = false;
            var prefab = Resources.Load<MeleeElementStatusAuraPresentation>(MeleeElementStatusAuraPresentation.ResourcePath);
            if (AuraAssetStamp(prefab, comparisonPrefab) == auraAssetStamp) return;
            float time = auraTime;
            ShowAura(currentAuraType, currentStacks, comparisonPrefab);
            ReconstructEffects(time);
        }
        private void ReconstructEffects(float time)
        {
            auraTime = Mathf.Clamp(time, 0f, 30f);
            foreach (var system in particles) if (system != null && system.gameObject.activeInHierarchy)
            { system.Simulate(auraTime, false, true, true); system.Pause(false); }
            // Graphs have no reliable random-state reverse reconstruction; restart their local clock only.
            foreach (var effect in graphs) if (effect != null && effect.gameObject.activeInHierarchy)
            { effect.Reinit(); effect.pause = true; if (auraTime > 0f) effect.Simulate(Mathf.Min(auraTime, 1f) / 60f, 60); }
        }
        public void ClearAura()
        {
            EditorApplication.projectChanged -= MarkAuraAssetsChanged;
            comparisonPrefab = null; auraAssetStamp = null; auraAssetsChanged = false;
            if (auraPresentation != null) auraPresentation.ClearAllAuras();
            if (effectsRoot != null) Object.DestroyImmediate(effectsRoot);
            effectsRoot = null; aura = null; comparison = null; auraPresentation = null;
            particles = Array.Empty<ParticleSystem>(); graphs = Array.Empty<UnityEngine.VFX.VisualEffect>(); needsRender = true;
        }
        public void SetView(int index)
        {
            ViewIndex = Mathf.Clamp(index, 0, 2);
            yaw = ViewIndex == 0 ? 180f : ViewIndex == 1 ? 90f : QuarterViewCamera.DefaultYaw;
            pitch = ViewIndex == 2 ? 55f : 0f;
            PositionCamera();
        }
        public void Orbit(Vector2 delta) { yaw += delta.x * .35f; pitch = Mathf.Clamp(pitch + delta.y * .35f, -85f, 85f); PositionCamera(); }
        public void Zoom(float delta) { distance = Mathf.Clamp(distance * Mathf.Exp(delta * .07f), fitDistance * .08f, fitDistance * 12f); PositionCamera(); }
        public void Pan(Vector2 delta)
        {
            if (Camera == null) return;
            focus += (-Camera.transform.right * delta.x + Camera.transform.up * delta.y) * distance * .0013f;
            PositionCamera();
        }
        public void Fit()
        {
            if (Actor == null) return;
            var renderers = Actor.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer) && r.enabled).ToArray();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.up, Vector3.one * 2f);
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            focus = bounds.center;
            fitDistance = Mathf.Max(1f, bounds.extents.magnitude / Mathf.Tan(19f * Mathf.Deg2Rad) * 1.25f);
            distance = fitDistance; PositionCamera();
        }
        private void PositionCamera()
        {
            if (Camera == null) return;
            Camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            Camera.transform.position = focus - Camera.transform.forward * distance;
            if (Actor != null) SampleAttackVisuals();
            needsRender = true;
        }
        public void Render(int width, int height)
        {
            if (Camera == null || !scene.IsValid()) return;
            RefreshAuraAssets();
            float resolutionScale = Mathf.Min(1f, 2048f / Mathf.Max(32, width, height));
            width = Mathf.Max(32, Mathf.RoundToInt(width * resolutionScale)); height = Mathf.Max(32, Mathf.RoundToInt(height * resolutionScale));
            if (surface == null || surface.width != width || surface.height != height)
            {
                ReleaseSurface();
                surface = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf) { hideFlags = HideFlags.HideAndDontSave };
                surface.Create(); Camera.targetTexture = surface; needsRender = true;
            }
            if (!needsRender && !Playing) return;
            Camera.aspect = width / (float)height;
            var previous = RenderTexture.active;
            try { Camera.Render(); needsRender = false; }
            finally { RenderTexture.active = previous; }
        }
        private void ReleaseSurface()
        {
            if (Camera != null) Camera.targetTexture = null;
            if (surface != null) { surface.Release(); Object.DestroyImmediate(surface); surface = null; }
        }
        public void Dispose()
        {
            ClearAura();
            actorBaseline.Clear();
            if (graph.IsValid()) graph.Destroy();
            ReleaseSurface();
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            if (volumeProfile != null) Object.DestroyImmediate(volumeProfile);
            if (floorMaterial != null) Object.DestroyImmediate(floorMaterial);
            if (counted) { LiveStages--; counted = false; }
            cueParticles = null; ProjectileVisible = false;
            scene = default; holder = null; Actor = null; Camera = null; animator = null;
            volumeProfile = null; floorMaterial = null; aura = null; comparison = null; auraPresentation = null;
            effectsRoot = null; particles = Array.Empty<ParticleSystem>(); graphs = Array.Empty<UnityEngine.VFX.VisualEffect>(); clip = null; ability = null; Playing = false;
        }
    }
}
