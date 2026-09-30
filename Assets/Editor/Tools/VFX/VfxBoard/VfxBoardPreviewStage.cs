using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.Vfx
{
    /// <summary>
    /// 격리된 프리뷰 씬에 VFX 하나를 띄워 RenderTexture로 그린다. UI Toolkit Image가 결과를 표시한다.
    /// 재생 시간 추정·첫 보이는 프레임·파티클 수동 시뮬레이션은 VFX 프리팹 미리보기 창과 같은 방식이다.
    /// </summary>
    internal sealed class VfxBoardPreviewStage : IDisposable
    {
        internal readonly struct ViewPreset
        {
            public ViewPreset(string label, Vector3 direction) { Label = label; Direction = direction; }
            public string Label { get; }
            public Vector3 Direction { get; }
        }

        internal readonly struct EnvironmentPreset
        {
            public EnvironmentPreset(string label, Color background, Color floor, Color key, Color fill, float keyIntensity, float fillIntensity)
            {
                Label = label; Background = background; Floor = floor;
                Key = key; Fill = fill; KeyIntensity = keyIntensity; FillIntensity = fillIntensity;
            }

            public string Label { get; }
            public Color Background { get; }
            public Color Floor { get; }
            public Color Key { get; }
            public Color Fill { get; }
            public float KeyIntensity { get; }
            public float FillIntensity { get; }
        }

        // 앞·뒤·좌·우는 20도 위에서 내려다본다. 바닥에 깔리는 효과가 옆면만 보이지 않게 한다.
        internal static readonly ViewPreset[] Views =
        {
            new ViewPreset("앞", new Vector3(0f, .36f, -1f)),
            new ViewPreset("뒤", new Vector3(0f, .36f, 1f)),
            new ViewPreset("좌", new Vector3(-1f, .36f, 0f)),
            new ViewPreset("우", new Vector3(1f, .36f, 0f)),
            new ViewPreset("상", new Vector3(0f, 1f, 0f)),
            new ViewPreset("하", new Vector3(0f, -1f, 0f)),
            new ViewPreset("좌상", new Vector3(-1f, 1f, -1f)),
            new ViewPreset("우상", new Vector3(1f, 1f, -1f)),
            new ViewPreset("좌하", new Vector3(-1f, -1f, -1f)),
            new ViewPreset("우하", new Vector3(1f, -1f, -1f))
        };

        internal static readonly EnvironmentPreset[] Environments =
        {
            new EnvironmentPreset("스튜디오", new Color(.235f, .255f, .29f), new Color(.43f, .45f, .48f), new Color(1f, .96f, .9f), new Color(.58f, .7f, 1f), 2.2f, 1.35f),
            new EnvironmentPreset("다크", new Color(.025f, .03f, .045f), new Color(.09f, .1f, .13f), new Color(1f, .9f, .78f), new Color(.36f, .55f, 1f), 1.9f, 1.05f),
            new EnvironmentPreset("라이트", new Color(.72f, .74f, .77f), new Color(.56f, .58f, .61f), new Color(1f, .98f, .94f), new Color(.72f, .82f, 1f), 1.65f, .8f),
            new EnvironmentPreset("게임", new Color(.065f, .085f, .125f), new Color(.15f, .18f, .24f), new Color(1f, .84f, .62f), new Color(.32f, .58f, 1f), 2.6f, 1.55f)
        };

        private const float DefaultLoopDuration = 3f;
        private const float MinLoopDuration = 1f;
        private const float MaxLoopDuration = 8f;
        private const float MaxOneShotDuration = 30f;
        private const int DurationScanSamples = 160;
        private const float InitialVisibleTime = .08f;
        private const float FirstVisibleScanMax = 2f;
        private const float FirstVisibleScanStep = .05f;
        private const float FieldOfView = 40f;
        private const float FitPadding = 1.35f;
        private const float MinDistance = 1.2f;
        private const float MinDistanceScale = .08f;
        private const float MaxDistanceScale = 12f;
        private const float ZoomSensitivity = .1f;
        private const float OrbitSensitivity = .35f;
        private const float BlendDuration = .28f;
        private const float MinPitch = -85f;
        private const float MaxPitch = 85f;
        private const float FloorY = -.05f;
        private const int VolumeLayer = 31;
        private const int GraphStepsPerSecond = 60;

        private Scene scene;
        private Camera camera;
        private UniversalAdditionalCameraData cameraData;
        private Light keyLight;
        private Light fillLight;
        private GameObject floor;
        private Material floorMaterial;
        private Volume volume;
        private VolumeProfile volumeProfile;
        private GameObject holder;
        private ParticleSystem[] systems = Array.Empty<ParticleSystem>();
        private ParticleSystem[] rootSystems = Array.Empty<ParticleSystem>();
        private VisualEffect[] graphs = Array.Empty<VisualEffect>();
        private Renderer[] renderers = Array.Empty<Renderer>();
        private ParticleSystem.Particle[] buffer = Array.Empty<ParticleSystem.Particle>();
        private RenderTexture surface;
        private bool restartOnAdvance = true;
        private bool framed;
        private float yaw = 200f;
        private float pitch = 14f;
        private float distanceScale = 1f;
        private Vector3 focus = new Vector3(0f, .5f, 0f);
        private float frameRadius = 1f;
        private bool blending;
        private double blendStart;
        private float blendFromYaw, blendFromPitch, blendFromScale, blendToYaw, blendToPitch, blendToScale;

        public float CurrentTime { get; private set; }
        public float Duration { get; private set; } = DefaultLoopDuration;
        public bool Looping { get; private set; }
        public bool Playing { get; set; } = true;
        public bool Loop { get; set; } = true;
        public float Speed { get; set; } = 1f;
        public int LiveParticles { get; private set; }
        public int VisibleParticles { get; private set; }
        public int ParticleSystemCount => systems.Length;
        public int GraphCount => graphs.Length;
        public bool HasContent => holder != null;
        public string Message { get; private set; } = string.Empty;
        public int ViewIndex { get; private set; }
        public int EnvironmentIndex { get; private set; } = 3;
        public bool FloorVisible { get; private set; } = true;
        public bool BloomEnabled { get; private set; } = true;
        public RenderTexture Surface => surface;
        public float ZoomPercent => 100f / Mathf.Max(.001f, distanceScale);
        public Quaternion CameraRotation => camera != null ? camera.transform.rotation : Quaternion.identity;
        public EnvironmentPreset Environment => Environments[Mathf.Clamp(EnvironmentIndex, 0, Environments.Length - 1)];

        // ---------- 씬 구성 ----------

        private void EnsureScene()
        {
            if (scene.IsValid())
                return;

            scene = EditorSceneManager.NewPreviewScene();
            camera = CreateStageObject("VFX Board Camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.cameraType = CameraType.Game;
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = .03f;
            camera.farClipPlane = 500f;
            camera.allowHDR = true;
            camera.useOcclusionCulling = false;
            cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.volumeLayerMask = 1 << VolumeLayer;

            keyLight = CreateLight("VFX Board Key Light", Quaternion.Euler(42f, -32f, 0f));
            fillLight = CreateLight("VFX Board Fill Light", Quaternion.Euler(325f, 138f, 0f));

            GameObject volumeObject = CreateStageObject("VFX Board Bloom");
            volumeObject.layer = VolumeLayer;
            volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.enabled = false; // 이 카메라를 그리는 동안에만 켠다(다른 카메라에 새지 않게)
            volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            volumeProfile.hideFlags = HideFlags.HideAndDontSave;
            volume.sharedProfile = volumeProfile;
            Bloom bloom = volumeProfile.Add<Bloom>(true);
            bloom.threshold.Override(.85f);
            bloom.intensity.Override(1.05f);
            bloom.scatter.Override(.62f);

            floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "VFX Board Floor";
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.hideFlags = HideFlags.HideAndDontSave;
            Collider floorCollider = floor.GetComponent<Collider>();
            if (floorCollider != null)
                Object.DestroyImmediate(floorCollider);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                floorMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                if (floorMaterial.HasProperty("_Smoothness"))
                    floorMaterial.SetFloat("_Smoothness", .18f);
                floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            }

            ApplyEnvironment();
            UpdateFloor();
        }

        private GameObject CreateStageObject(string name)
        {
            var gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            return gameObject;
        }

        private Light CreateLight(string name, Quaternion rotation)
        {
            Light light = CreateStageObject(name).AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.transform.rotation = rotation;
            return light;
        }

        // ---------- 대상 ----------

        /// <summary>미리볼 대상을 바꾼다. keepCamera면 비교를 위해 카메라 거리와 초점을 유지한다.</summary>
        public void SetSource(Object source, Quaternion rotation, Vector3? scale, bool keepCamera)
        {
            EnsureScene();
            ClearContent();
            Message = string.Empty;
            if (source == null)
            {
                UpdateFloor();
                return;
            }

            try
            {
                holder = CreateInstance(source, rotation, scale);
            }
            catch (Exception error)
            {
                Message = "미리보기를 만들지 못했습니다: " + error.Message;
                ClearContent();
                return;
            }

            if (holder == null)
            {
                Message = "미리볼 수 없는 에셋입니다.";
                return;
            }

            systems = holder.GetComponentsInChildren<ParticleSystem>(true);
            rootSystems = ResolveRootSystems(systems);
            graphs = holder.GetComponentsInChildren<VisualEffect>(true);
            renderers = holder.GetComponentsInChildren<Renderer>(true);
            foreach (VisualEffect graph in graphs)
            {
                graph.pause = true;
                graph.playRate = 0f;
            }

            ResetSystems();
            ResolveDuration();
            ShowFirstVisibleFrame();
            if (!keepCamera || !framed)
            {
                FitAtBusiestFrame();
                SetView(ViewIndex, false);
                framed = true;
            }

            UpdateFloor();
            if (systems.Length == 0 && graphs.Length == 0 && renderers.Length == 0)
                Message = "Renderer·ParticleSystem·VisualEffect가 없는 프리팹입니다.";
            else if (systems.Length == 0 && graphs.Length > 0)
                Message = "VFX Graph 전용 효과입니다. 이 미리보기에서는 재생이 보이지 않을 수 있으니 씬이나 게임에서 확인하세요.";
        }

        private GameObject CreateInstance(Object source, Quaternion rotation, Vector3? scale)
        {
            // 비활성 부모 아래에서 만들어 플레이 중에도 런타임 스크립트가 먼저 돌지 않게 한다.
            var root = new GameObject("[VFX Board Preview]");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);

            GameObject created;
            if (source is VisualEffectAsset graphAsset)
            {
                created = new GameObject(graphAsset.name);
                created.transform.SetParent(root.transform, false);
                created.AddComponent<VisualEffect>().visualEffectAsset = graphAsset;
            }
            else
            {
                GameObject template = source as GameObject;
                if (template == null && source is Component component)
                    template = component.gameObject;
                if (template == null)
                {
                    Object.DestroyImmediate(root);
                    return null;
                }

                created = Object.Instantiate(template, root.transform, false);
                created.name = template.name;
            }

            created.transform.localPosition = Vector3.zero;
            created.transform.localRotation = rotation;
            if (scale.HasValue)
                created.transform.localScale = scale.Value;
            created.SetActive(true);

            if (Application.isPlaying)
                foreach (MonoBehaviour behaviour in created.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour != null)
                        behaviour.enabled = false;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.hideFlags = HideFlags.HideAndDontSave;
            root.SetActive(true);
            return root;
        }

        private void ClearContent()
        {
            if (holder != null)
                Object.DestroyImmediate(holder);
            holder = null;
            systems = Array.Empty<ParticleSystem>();
            rootSystems = Array.Empty<ParticleSystem>();
            graphs = Array.Empty<VisualEffect>();
            renderers = Array.Empty<Renderer>();
            LiveParticles = 0;
            VisibleParticles = 0;
            CurrentTime = 0f;
            Duration = DefaultLoopDuration;
            Looping = false;
        }

        // ---------- 재생 ----------

        /// <summary>한 틱 진행. 화면을 다시 그려야 하면 true.</summary>
        public bool Tick(float deltaTime)
        {
            bool changed = false;
            if (holder != null && Playing)
            {
                Advance(deltaTime);
                changed = true;
            }

            if (blending)
            {
                AdvanceBlend();
                changed = true;
            }

            return changed;
        }

        public void Restart(bool play)
        {
            if (holder == null) return;
            ResetSystems();
            CurrentTime = 0f;
            Playing = play;
            CountParticles();
        }

        public void Scrub(float time)
        {
            if (holder == null) return;
            ScrubTo(Mathf.Clamp(time, 0f, Duration));
        }

        private void Advance(float deltaTime)
        {
            float scaled = Mathf.Max(0f, deltaTime) * Mathf.Max(.01f, Speed);
            if (scaled <= 0f) return;

            float next = CurrentTime + scaled;
            if (next > Duration)
            {
                if (Loop)
                {
                    ScrubTo(next % Duration);
                    return;
                }

                AdvanceBy(Mathf.Max(0f, Duration - CurrentTime));
                CurrentTime = Duration;
                Playing = false;
                return;
            }

            AdvanceBy(scaled);
            CurrentTime = next;
        }

        private void ScrubTo(float time)
        {
            ResetSystems();
            SimulateFromStart(time);
            CurrentTime = time;
            CountParticles();
        }

        private void ResetSystems()
        {
            foreach (ParticleSystem system in rootSystems)
                if (system != null)
                    system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            foreach (VisualEffect graph in graphs)
            {
                if (graph == null) continue;
                graph.Reinit();
                graph.pause = true;
                graph.playRate = 0f;
                graph.Play();
            }

            restartOnAdvance = true;
            CountParticles();
        }

        private void AdvanceBy(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            bool restart = restartOnAdvance;
            foreach (ParticleSystem system in rootSystems)
                if (system != null)
                    system.Simulate(deltaTime, true, restart, true);
            StepGraphs(deltaTime);
            restartOnAdvance = false;
            CountParticles();
        }

        private void SimulateFromStart(float time)
        {
            if (time <= 0f) return;
            foreach (ParticleSystem system in rootSystems)
                if (system != null)
                    system.Simulate(time, true, true, true);
            StepGraphs(time);
            restartOnAdvance = false;
        }

        private void StepGraphs(float deltaTime)
        {
            if (graphs.Length == 0 || deltaTime <= 0f) return;
            uint steps = (uint)Mathf.Max(1, Mathf.CeilToInt(deltaTime * GraphStepsPerSecond));
            foreach (VisualEffect graph in graphs)
            {
                if (graph == null) continue;
                graph.Simulate(deltaTime / steps, steps);
                graph.AdvanceOneFrame();
            }
        }

        private void ResolveDuration()
        {
            Looping = false;
            float theoretical = 0f, emission = 0f, longestLoop = 0f;
            foreach (ParticleSystem system in systems)
            {
                if (system == null || !system.gameObject.activeInHierarchy) continue;
                ParticleSystem.MainModule main = system.main;
                float delay = CurveMax(main.startDelay);
                float duration = Mathf.Max(0f, main.duration);
                theoretical = Mathf.Max(theoretical, delay + duration + CurveMax(main.startLifetime));
                emission = Mathf.Max(emission, delay + duration);
                if (!main.loop || !system.emission.enabled) continue;
                Looping = true;
                if (duration >= MinLoopDuration && duration <= MaxLoopDuration)
                    longestLoop = Mathf.Max(longestLoop, duration);
            }

            if (Looping || (systems.Length == 0 && graphs.Length > 0))
            {
                Looping = true;
                Duration = Mathf.Clamp(longestLoop > 0f ? longestLoop : DefaultLoopDuration, MinLoopDuration, MaxLoopDuration);
                ResetSystems();
                return;
            }

            if (systems.Length == 0)
            {
                Duration = DefaultLoopDuration;
                return;
            }

            float limit = Mathf.Clamp(theoretical, .5f, MaxOneShotDuration);
            float detected = DetectLastVisibleTime(limit);
            if (detected <= 0f)
                detected = emission > 0f ? emission : DefaultLoopDuration;
            Duration = Mathf.Clamp(detected, .5f, MaxOneShotDuration);
        }

        private float DetectLastVisibleTime(float limit)
        {
            if (rootSystems.Length == 0) return 0f;
            float step = Mathf.Clamp(limit / DurationScanSamples, .025f, .1f);
            float last = 0f;
            for (float time = step; time <= limit + step * .5f; time += step)
            {
                ResetSystems();
                SimulateFromStart(time);
                if (CountVisible() > 0)
                    last = time;
            }

            ResetSystems();
            return last <= 0f ? 0f : Mathf.Min(limit, Mathf.Ceil((last + step) * 20f) / 20f);
        }

        private void ShowFirstVisibleFrame()
        {
            float maxTime = Mathf.Min(Duration, FirstVisibleScanMax);
            for (float time = InitialVisibleTime; time <= maxTime; time += FirstVisibleScanStep)
            {
                ScrubTo(time);
                if (VisibleParticles > 0 || graphs.Length > 0)
                    return;
            }

            ScrubTo(Mathf.Min(InitialVisibleTime, Duration));
        }

        private static float CurveMax(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant: return Mathf.Max(0f, curve.constant);
                case ParticleSystemCurveMode.TwoConstants: return Mathf.Max(0f, curve.constantMax);
                case ParticleSystemCurveMode.Curve: return Mathf.Max(0f, KeyMax(curve.curve) * curve.curveMultiplier);
                case ParticleSystemCurveMode.TwoCurves:
                    return Mathf.Max(0f, Mathf.Max(KeyMax(curve.curveMin), KeyMax(curve.curveMax)) * curve.curveMultiplier);
                default: return 0f;
            }
        }

        private static float KeyMax(AnimationCurve curve)
        {
            if (curve == null) return 0f;
            float max = 0f;
            foreach (Keyframe key in curve.keys)
                max = Mathf.Max(max, key.value);
            return max;
        }

        private static ParticleSystem[] ResolveRootSystems(ParticleSystem[] all)
        {
            var roots = new System.Collections.Generic.List<ParticleSystem>(all.Length);
            foreach (ParticleSystem system in all)
            {
                if (system == null) continue;
                bool nested = false;
                for (Transform parent = system.transform.parent; parent != null && !nested; parent = parent.parent)
                    nested = parent.GetComponent<ParticleSystem>() != null;
                if (!nested)
                    roots.Add(system);
            }

            return roots.ToArray();
        }

        private void CountParticles()
        {
            int live = 0;
            foreach (ParticleSystem system in systems)
                if (system != null)
                    live += system.particleCount;
            int graphAlive = 0;
            foreach (VisualEffect graph in graphs)
                if (graph != null)
                    graphAlive += Mathf.Max(0, graph.aliveParticleCount); // 아직 집계 전이면 음수가 온다
            LiveParticles = live + graphAlive;
            VisibleParticles = CountVisible() + graphAlive;
        }

        private int CountVisible()
        {
            int count = 0;
            foreach (ParticleSystem system in systems)
            {
                if (system == null || !system.gameObject.activeInHierarchy) continue;
                var particleRenderer = system.GetComponent<ParticleSystemRenderer>();
                if (particleRenderer == null || !particleRenderer.enabled) continue;
                int particleCount = system.particleCount;
                if (particleCount <= 0) continue;
                if (buffer.Length < particleCount)
                    buffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(particleCount)];
                int copied = system.GetParticles(buffer);
                for (int i = 0; i < copied; i++)
                    if (buffer[i].GetCurrentSize(system) > .0001f && buffer[i].GetCurrentColor(system).a > 2)
                        count++;
            }

            return count;
        }

        // ---------- 카메라 ----------

        public void Fit()
        {
            FitToContent();
            StartBlend(yaw, pitch, 1f);
            UpdateFloor();
        }

        private void FitToContent()
        {
            // 보이는 입자만 모으고 가장 먼 10%는 버린다. 투명·거대 글로우 입자 때문에 카메라가 멀어지는 것을 막는다.
            var points = new System.Collections.Generic.List<Vector3>();
            var sizes = new System.Collections.Generic.List<float>();
            foreach (ParticleSystem system in systems)
            {
                if (system == null || !system.gameObject.activeInHierarchy || system.particleCount == 0) continue;
                var particleRenderer = system.GetComponent<ParticleSystemRenderer>();
                if (particleRenderer == null || !particleRenderer.enabled) continue;
                int particleCount = system.particleCount;
                if (buffer.Length < particleCount)
                    buffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(particleCount)];
                int copied = system.GetParticles(buffer);
                ParticleSystem.MainModule main = system.main;
                for (int i = 0; i < copied; i++)
                {
                    float size = buffer[i].GetCurrentSize(system);
                    if (size <= .0001f || buffer[i].GetCurrentColor(system).a <= 2) continue;
                    Vector3 point = buffer[i].position;
                    if (main.simulationSpace == ParticleSystemSimulationSpace.Local)
                        point = system.transform.TransformPoint(point);
                    else if (main.simulationSpace == ParticleSystemSimulationSpace.Custom && main.customSimulationSpace != null)
                        point = main.customSimulationSpace.TransformPoint(point);
                    points.Add(point);
                    sizes.Add(Mathf.Min(size, 3f));
                }
            }

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || renderer is ParticleSystemRenderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds rendererBounds = renderer.bounds;
                points.Add(rendererBounds.center);
                sizes.Add(Mathf.Min(rendererBounds.size.magnitude, 6f));
            }

            if (points.Count == 0)
            {
                focus = new Vector3(0f, .5f, 0f);
                frameRadius = 1f;
                distanceScale = 1f;
                return;
            }

            Vector3 center = Vector3.zero;
            foreach (Vector3 point in points)
                center += point;
            center /= points.Count;

            var distances = new float[points.Count];
            for (int i = 0; i < points.Count; i++)
                distances[i] = Vector3.Distance(points[i], center);
            Array.Sort(distances);
            float[] sortedSizes = sizes.ToArray();
            Array.Sort(sortedSizes);
            float spread = distances[Mathf.Clamp(Mathf.FloorToInt((distances.Length - 1) * .9f), 0, distances.Length - 1)];
            float typicalSize = sortedSizes[sortedSizes.Length / 2];

            focus = center;
            frameRadius = Mathf.Clamp(spread + typicalSize * .5f, .3f, 40f);
            distanceScale = 1f;
        }

        /// <summary>재생 구간을 몇 번 훑어 입자가 가장 많이 보이는 순간에 카메라를 맞춘다.</summary>
        private void FitAtBusiestFrame()
        {
            float displayTime = CurrentTime;
            float window = Mathf.Min(Duration, 3f);
            float bestTime = displayTime;
            int bestCount = VisibleParticles;
            const int samples = 8;
            for (int i = 1; i <= samples && systems.Length > 0; i++)
            {
                float time = window * i / (samples + 1f);
                ScrubTo(time);
                if (VisibleParticles > bestCount)
                {
                    bestCount = VisibleParticles;
                    bestTime = time;
                }
            }

            ScrubTo(bestTime);
            FitToContent();
            ScrubTo(displayTime);
        }

        public void SetView(int index, bool animate)
        {
            ViewIndex = Mathf.Clamp(index, 0, Views.Length - 1);
            Vector3 direction = Views[ViewIndex].Direction.normalized;
            float targetPitch = Mathf.Clamp(Mathf.Asin(direction.y) * Mathf.Rad2Deg, MinPitch, MaxPitch);
            float targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            if (animate)
            {
                StartBlend(targetYaw, targetPitch, 1f);
                return;
            }

            blending = false;
            yaw = Normalize(targetYaw);
            pitch = targetPitch;
            distanceScale = 1f;
        }

        public void Orbit(Vector2 pointerDelta)
        {
            blending = false;
            yaw -= pointerDelta.x * OrbitSensitivity;
            pitch = Mathf.Clamp(pitch + pointerDelta.y * OrbitSensitivity, MinPitch, MaxPitch);
        }

        public void Zoom(float wheelDelta)
        {
            blending = false;
            distanceScale = Mathf.Clamp(distanceScale * Mathf.Exp(wheelDelta * ZoomSensitivity), MinDistanceScale, MaxDistanceScale);
        }

        private void StartBlend(float targetYaw, float targetPitch, float targetScale)
        {
            blending = true;
            blendStart = EditorApplication.timeSinceStartup;
            blendFromYaw = yaw; blendFromPitch = pitch; blendFromScale = distanceScale;
            blendToYaw = yaw + Mathf.DeltaAngle(yaw, targetYaw);
            blendToPitch = targetPitch;
            blendToScale = targetScale;
        }

        private void AdvanceBlend()
        {
            float t = Mathf.Clamp01((float)((EditorApplication.timeSinceStartup - blendStart) / BlendDuration));
            float eased = Mathf.SmoothStep(0f, 1f, t);
            yaw = Mathf.Lerp(blendFromYaw, blendToYaw, eased);
            pitch = Mathf.Lerp(blendFromPitch, blendToPitch, eased);
            distanceScale = Mathf.Lerp(blendFromScale, blendToScale, eased);
            if (t >= 1f)
            {
                blending = false;
                yaw = Normalize(blendToYaw);
            }
        }

        private void UpdateCameraTransform()
        {
            float yawRad = yaw * Mathf.Deg2Rad, pitchRad = pitch * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Sin(yawRad) * Mathf.Cos(pitchRad), Mathf.Sin(pitchRad), Mathf.Cos(yawRad) * Mathf.Cos(pitchRad));
            float distance = Mathf.Max(MinDistance, frameRadius * FitPadding / Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad)) * distanceScale;
            camera.transform.position = focus + direction * distance;
            Vector3 up = Mathf.Abs(direction.y) > .95f ? Vector3.forward : Vector3.up;
            camera.transform.rotation = Quaternion.LookRotation(-direction, up);
        }

        private static float Normalize(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            else if (angle < -180f) angle += 360f;
            return angle;
        }

        // ---------- 환경 ----------

        public void SetEnvironment(int index)
        {
            EnvironmentIndex = Mathf.Clamp(index, 0, Environments.Length - 1);
            ApplyEnvironment();
        }

        public void SetFloor(bool visible)
        {
            FloorVisible = visible;
            UpdateFloor();
        }

        public void SetBloom(bool enabled) => BloomEnabled = enabled;

        private void ApplyEnvironment()
        {
            if (camera == null) return;
            EnvironmentPreset preset = Environment;
            camera.backgroundColor = preset.Background;
            keyLight.color = preset.Key;
            keyLight.intensity = preset.KeyIntensity;
            fillLight.color = preset.Fill;
            fillLight.intensity = preset.FillIntensity;
            if (floorMaterial != null)
            {
                if (floorMaterial.HasProperty("_BaseColor")) floorMaterial.SetColor("_BaseColor", preset.Floor);
                if (floorMaterial.HasProperty("_Color")) floorMaterial.SetColor("_Color", preset.Floor);
            }
        }

        private void UpdateFloor()
        {
            if (floor == null) return;
            floor.SetActive(FloorVisible);
            float size = Mathf.Max(6f, frameRadius * 5f) / 10f;
            floor.transform.SetPositionAndRotation(new Vector3(focus.x, FloorY, focus.z), Quaternion.identity);
            floor.transform.localScale = new Vector3(size, 1f, size);
        }

        // ---------- 렌더 ----------

        public void Resize(int width, int height)
        {
            width = Mathf.Clamp(width, 64, 4096);
            height = Mathf.Clamp(height, 64, 4096);
            if (surface != null && surface.width == width && surface.height == height) return;
            ReleaseSurface();
            surface = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "VFX Board Preview"
            };
            surface.Create();
        }

        public void Render()
        {
            EnsureScene();
            if (surface == null) return;

            UpdateCameraTransform();
            camera.aspect = (float)surface.width / surface.height;
            camera.targetTexture = surface;
            cameraData.renderPostProcessing = BloomEnabled;
            volume.enabled = BloomEnabled;
            RenderTexture previous = RenderTexture.active;
            try
            {
                if (graphs.Length > 0)
                    VFXManager.PrepareCamera(camera);
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = surface };
                if (RenderPipeline.SupportsRenderRequest(camera, request))
                    RenderPipeline.SubmitRenderRequest(camera, request);
                else
                    camera.Render();
            }
            catch (Exception error)
            {
                Message = "렌더 오류: " + error.Message;
            }
            finally
            {
                volume.enabled = false;
                RenderTexture.active = previous;
                camera.targetTexture = null;
            }
        }

        private void ReleaseSurface()
        {
            if (surface == null) return;
            surface.Release();
            Object.DestroyImmediate(surface);
            surface = null;
        }

        public void Dispose()
        {
            ClearContent();
            if (scene.IsValid())
                EditorSceneManager.ClosePreviewScene(scene);
            scene = default;
            camera = null;
            cameraData = null;
            floor = null;
            volume = null;
            ReleaseSurface();
            if (volumeProfile != null) Object.DestroyImmediate(volumeProfile);
            if (floorMaterial != null) Object.DestroyImmediate(floorMaterial);
            volumeProfile = null;
            floorMaterial = null;
        }
    }
}
