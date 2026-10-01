using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.VFX;

namespace Overburst.EditorTools.Weapons
{
    /// <summary>Read-only audit and isolated preview of the current greatsword VFX wiring.</summary>
    public sealed class GreatswordVfxCueReviewWindow : EditorWindow
    {
        private const string Root = "Assets/ProjectOverburst/";
        private const string Weapon = Root + "03_Features/Weapons/";
        private const string Shared = Weapon + "_Shared/Melee/VFX/";
        private const string Reaction = Root + "02_Shared/Combat/ElementalReactions/VFX/Prefabs/";
        private const string Heavy = Weapon + "WP02_Greatsword/Common/Heavy/GreatswordHeavyAttack.asset";
        private const string HitCatalog = Root + "Resources/Combat/VFX/MeleeElementHitVfxCatalog.asset";
        private const string ChoicePrefix = "Overburst.GreatswordVfxCueReview.";

        private enum CueState { Connected, Shared, Missing }

        private sealed class Cue
        {
            public string id, title, group, trigger, note;
            public CueState state;
            public UnityEngine.Object source;
            public UnityEngine.Object asset;
            public bool IsSelected => EditorPrefs.GetBool(ChoicePrefix + id);
        }

        private readonly List<Cue> cues = new List<Cue>();
        private Cue active;
        private ScrollView cueList;
        private TextField searchField;
        private DropdownField filterField;
        private Label countLabel, titleLabel, triggerLabel, stateLabel, noteLabel, assetLabel, clockLabel, hintLabel;
        private Image viewport;
        private Slider timeSlider;
        private Button playButton, pingButton;
        private Scene scene;
        private Camera camera;
        private VolumeProfile volumeProfile;
        private Material floorMaterial;
        private RenderTexture surface;
        private GameObject effectRoot;
        private ParticleSystem[] particles = Array.Empty<ParticleSystem>();
        private VisualEffect[] graphs = Array.Empty<VisualEffect>();
        private SwordShockwavePlayback[] shockwaves = Array.Empty<SwordShockwavePlayback>();
        private ChainElectricityLiteVfxController chainLink;
        private double lastUpdate, lastRender;
        private float clock, duration = 4f, yaw = 135f, pitch = 22f, distance = 7f;
        private Vector3 focus = new Vector3(0, .65f, 0);
        private bool playing, dragging, seeking;
        private Vector2 dragOrigin;
        private bool hasGraph;

        [MenuItem("OVERBURST/무기/대검 VFX 큐 리뷰")]
        public static void Open()
        {
            var window = GetWindow<GreatswordVfxCueReviewWindow>();
            window.titleContent = new GUIContent("대검 VFX 큐");
            window.minSize = new Vector2(1050, 670);
            window.Show();
        }

        private void OnEnable() => EditorApplication.update += UpdatePreview;

        private void OnDisable()
        {
            EditorApplication.update -= UpdatePreview;
            DisposeStage();
        }

        public void CreateGUI()
        {
            DisposeStage();
            var root = rootVisualElement;
            root.Clear();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Editor/Tools/Weapons/GreatswordVfxCueReview/GreatswordVfxCueReview.uss");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.AddToClassList("vfx-root");
            BuildUi(root);
            ReloadCues();
        }

        private static Label Text(string value, string className)
        {
            var label = new Label(value);
            label.AddToClassList(className);
            return label;
        }

        private static Button Action(string caption, Action action, string className = null)
        {
            var button = new Button(action) { text = caption };
            if (!string.IsNullOrEmpty(className)) button.AddToClassList(className);
            return button;
        }

        private void BuildUi(VisualElement root)
        {
            var header = new VisualElement(); header.AddToClassList("header"); root.Add(header);
            var headerText = new VisualElement(); headerText.AddToClassList("header-text"); header.Add(headerText);
            headerText.Add(Text("OVERBURST  /  VFX CUE REVIEW", "eyebrow"));
            headerText.Add(Text("대검 VFX 연결 · 프리뷰", "hero-title"));
            headerText.Add(Text("원소별 핵심 VFX가 연결됐는지, 어떤 프리팹인지 빠르게 확인합니다.", "hero-subtitle"));
            var headerActions = new VisualElement(); headerActions.AddToClassList("header-actions"); header.Add(headerActions);
            headerActions.Add(Action("연결 새로고침", ReloadCues));
            headerActions.Add(Action("선택 목록 복사", CopyChoices));

            var content = new VisualElement(); content.AddToClassList("content"); root.Add(content);
            var sidebar = new VisualElement(); sidebar.AddToClassList("sidebar"); content.Add(sidebar);
            var sidebarTop = new VisualElement(); sidebarTop.AddToClassList("sidebar-top"); sidebar.Add(sidebarTop);
            countLabel = Text("연결 확인 중", "count"); sidebarTop.Add(countLabel);
            searchField = new TextField { value = "" }; searchField.AddToClassList("search");
            searchField.label = "검색"; sidebarTop.Add(searchField);
            searchField.RegisterValueChangedCallback(_ => RebuildList());
            filterField = new DropdownField("보기", new List<string> { "전체", "연결", "공통 사용", "빈 자리", "내 선택" }, 0);
            filterField.AddToClassList("filter"); sidebarTop.Add(filterField);
            filterField.RegisterValueChangedCallback(_ => RebuildList());
            cueList = new ScrollView(); cueList.AddToClassList("cue-list"); sidebar.Add(cueList);

            var detail = new VisualElement(); detail.AddToClassList("detail"); content.Add(detail);
            var detailHead = new VisualElement(); detailHead.AddToClassList("detail-head"); detail.Add(detailHead);
            var detailCopy = new VisualElement(); detailCopy.AddToClassList("detail-copy"); detailHead.Add(detailCopy);
            titleLabel = Text("큐를 선택하세요", "detail-title"); detailCopy.Add(titleLabel);
            triggerLabel = Text("", "detail-trigger"); detailCopy.Add(triggerLabel);
            stateLabel = Text("", "state-chip"); detailHead.Add(stateLabel);

            var viewportBox = new VisualElement(); viewportBox.AddToClassList("viewport-box"); detail.Add(viewportBox);
            viewport = new Image { scaleMode = ScaleMode.ScaleToFit }; viewport.AddToClassList("viewport"); viewportBox.Add(viewport);
            hintLabel = Text("왼쪽 큐를 고르면 단독 프리뷰가 열립니다.", "viewport-hint"); viewportBox.Add(hintLabel);
            viewport.RegisterCallback<GeometryChangedEvent>(_ => ResizeSurface());
            viewport.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; dragging = true; dragOrigin = new Vector2(e.position.x, e.position.y); viewport.CapturePointer(e.pointerId); });
            viewport.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                Vector2 position = new Vector2(e.position.x, e.position.y);
                Vector2 delta = position - dragOrigin; dragOrigin = position;
                yaw += delta.x * .35f; pitch = Mathf.Clamp(pitch - delta.y * .25f, -15f, 80f);
                RenderPreview();
            });
            viewport.RegisterCallback<PointerUpEvent>(e => { dragging = false; if (viewport.HasPointerCapture(e.pointerId)) viewport.ReleasePointer(e.pointerId); });
            viewport.RegisterCallback<WheelEvent>(e => { distance = Mathf.Clamp(distance + e.delta.y * .018f, 1.5f, 22f); RenderPreview(); });

            var transport = new VisualElement(); transport.AddToClassList("transport"); detail.Add(transport);
            playButton = Action("▶ 재생", TogglePlayback, "play-button"); transport.Add(playButton);
            transport.Add(Action("↺ 처음부터", RestartPlayback));
            clockLabel = Text("0.00 / 0.00초", "clock"); transport.Add(clockLabel);
            timeSlider = new Slider(0, 4) { value = 0 }; timeSlider.AddToClassList("time-slider"); transport.Add(timeSlider);
            timeSlider.RegisterValueChangedCallback(e => { if (!seeking && active != null) Seek(e.newValue); });

            var options = new VisualElement(); options.AddToClassList("options"); detail.Add(options);
            options.Add(Text("드래그로 회전 · 휠로 확대", "option-hint"));

            var sourceBox = new VisualElement(); sourceBox.AddToClassList("source-box"); detail.Add(sourceBox);
            sourceBox.Add(Text("실제 연결", "section-label"));
            assetLabel = Text("—", "asset-title"); sourceBox.Add(assetLabel);
            noteLabel = Text("", "asset-note"); sourceBox.Add(noteLabel);
            var sourceActions = new VisualElement(); sourceActions.AddToClassList("source-actions"); sourceBox.Add(sourceActions);
            pingButton = Action("프로젝트에서 찾기", PingAsset); sourceActions.Add(pingButton);
        }

        private void CopyChoices()
        {
            EditorGUIUtility.systemCopyBuffer = "OVERBURST 대검 VFX 큐 선정\n"
                + string.Join("\n", cues.Where(c => c.IsSelected).Select(c => "[x] " + c.id + " / " + c.title + " / " + StateName(c.state)));
        }

        private static GameObject LoadPrefab(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        private void Add(string id, string title, string group, string trigger, CueState expected,
            UnityEngine.Object asset, UnityEngine.Object source, string note)
        {
            cues.Add(new Cue { id = id, title = title, group = group, trigger = trigger,
                state = asset == null && expected == CueState.Connected ? CueState.Missing : expected,
                asset = asset, source = source, note = note });
        }

        private void ReloadCues()
        {
            string oldId = active?.id;
            cues.Clear();
            var heavy = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>(Heavy);
            var hits = AssetDatabase.LoadAssetAtPath<MeleeElementHitVfxCatalog>(HitCatalog);
            GameObject fireHit = null, iceHit = null, electricHit = null, darkHit = null, lightHit = null;
            hits?.TryResolve(WeaponElement.Fire, out fireHit);
            hits?.TryResolve(WeaponElement.Ice, out iceHit);
            hits?.TryResolve(WeaponElement.Electric, out electricHit);
            hits?.TryResolve(WeaponElement.Dark, out darkHit);
            hits?.TryResolve(WeaponElement.Light, out lightHit);

            Add("VFX.FIRE.HIT", "불 · 원소 타격", "불", "불 속성 적중", CueState.Connected,
                fireHit, hits, "Projectile_Hit_Impact / 원소 타격 런타임 풀");
            Add("VFX.FIRE.HV.CIRCLE", "불 · 강공 착지", "불", "불 강공 지면 충돌", CueState.Connected,
                heavy != null ? heavy.elementVfx.fireImpact : null, heavy, "Fire Burst sim 1");
            Add("VFX.FIRE.HV.CHAIN", "불 · 연쇄폭발", "불", "연소 대상 전파", CueState.Connected,
                heavy != null ? heavy.elementVfx.fireChainExplosion : null, heavy, "착지 효과와 별도 슬롯");
            Add("VFX.FIRE.STATUS.BURN", "불 · 연소", "불", "연소 상태 지속", CueState.Connected,
                LoadPrefab(Shared + "ElementStatusAura/Modules/PF_VFX_MeleeElementStatusAura_BurningModule.prefab"),
                null, "Fire Loop sim 1");

            Add("VFX.ICE.HIT", "얼음 · 원소 타격", "얼음", "얼음 속성 적중", CueState.Connected,
                iceHit, hits, "Ice_Hit_FX / 원소 타격 런타임 풀");
            Add("VFX.ICE.HV.CIRCLE", "얼음 · 강공 착지", "얼음", "얼음 강공 지면 충돌", CueState.Connected,
                heavy != null ? heavy.elementVfx.iceImpact : null, heavy, "전용 iceImpact 비어 있음 · 공통 지면 파동 사용");
            Add("VFX.ICE.HV.SHATTER", "얼음 · 쇄빙", "얼음", "빙결 대상에 강공 적중", CueState.Connected,
                heavy != null ? heavy.elementVfx.iceShatter : null, heavy, "Shatter Proc");
            Add("VFX.ICE.STATUS.LOOP", "얼음 · 빙결", "얼음", "빙결 상태 지속", CueState.Connected,
                LoadPrefab(Reaction + "FreezeShatter/PF_VFX_Reaction_Freeze_Loop.prefab"),
                null, "FrostAura Loop 연결 · 시작·종료 래퍼 제거");

            Add("VFX.ELEC.HIT", "번개 · 원소 타격", "번개", "번개 속성 적중", CueState.Connected,
                electricHit, hits, "원소 타격 런타임 풀");
            Add("VFX.ELEC.HV.CIRCLE", "번개 · 강공 착지", "번개", "번개 강공 지면 충돌", CueState.Connected,
                heavy != null ? heavy.elementVfx.electricImpact : null, heavy, "Slam Circular 후반 폭발");
            bool chainComplete = heavy != null && heavy.elementVfx.electricChainLink != null;
            string chainNote = heavy == null ? "강공 정의를 찾을 수 없음" :
                $"연결 줄기 {(chainComplete ? "연결" : "비어 있음")} · 빈 시작·도착 래퍼는 제거됨";
            Add("VFX.ELEC.HV.CHAIN.LINK", "번개 · 연쇄번개", "번개", "첫 적중에서 파생 대상까지", chainComplete ? CueState.Connected : CueState.Missing,
                heavy != null ? heavy.elementVfx.electricChainLink : null, heavy,
                chainNote);
            Add("VFX.ELEC.STATUS.SHOCK", "번개 · 감전", "번개", "감전 상태 지속", CueState.Connected,
                LoadPrefab(Shared + "ElementStatusAura/Modules/PF_VFX_MeleeElementStatusAura_ShockedModule.prefab"),
                null, "대상 상태 오라");

            Add("VFX.DARK.HIT", "어둠 · 원소 타격", "어둠", "어둠 속성 적중", CueState.Connected,
                darkHit, hits, "원소 타격 런타임 풀");
            Add("VFX.DARK.HV.CIRCLE", "어둠 · 강공 착지", "어둠", "어둠 강공 지면 충돌", CueState.Shared,
                null, heavy, "전용 착지 프리팹 없음 · 공통 파동 사용");
            Add("VFX.DARK.HV.PULL", "어둠 · 인력", "어둠", "강공 범위 적 흡인", CueState.Missing,
                null, heavy, "메커니즘 구현 · 전용 VFX 슬롯/프리팹 없음");

            Add("VFX.LIGHT.HIT", "빛 · 원소 타격", "빛", "빛 속성 적중", CueState.Connected,
                lightHit, hits, "원소 타격 런타임 풀");
            Add("VFX.LIGHT.HV.CIRCLE", "빛 · 강공 착지", "빛", "빛 강공 지면 충돌", CueState.Shared,
                null, heavy, "전용 착지 프리팹 없음 · 공통 파동 사용");
            Add("VFX.LIGHT.HV.ECHO", "빛 · 잔광", "빛", "첫 폭발 뒤 2차 파동", CueState.Shared,
                null, heavy, "전용 빛 VFX 없이 공통 파동 재사용");

            active = cues.FirstOrDefault(c => c.id == oldId) ?? cues.FirstOrDefault();
            RebuildList();
            ShowActive();
        }
        private static string StateName(CueState state) => state == CueState.Connected ? "연결" : state == CueState.Shared ? "공통 사용" : "빈 자리";

        private void RebuildList()
        {
            if (cueList == null) return;
            int linked = cues.Count(c => c.state == CueState.Connected);
            int shared = cues.Count(c => c.state == CueState.Shared);
            int missing = cues.Count(c => c.state == CueState.Missing);
            countLabel.text = $"전체 {cues.Count}   ·   연결 {linked}   ·   공통 {shared}   ·   빈 자리 {missing}";
            cueList.Clear();
            string query = (searchField.value ?? "").Trim();
            string filter = filterField.value;
            string group = null;
            foreach (var cue in cues)
            {
                bool visible = (query.Length == 0 || (cue.title + cue.id + cue.note).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    && (filter == "전체" || (filter == "연결" && cue.state == CueState.Connected)
                    || (filter == "공통 사용" && cue.state == CueState.Shared)
                    || (filter == "빈 자리" && cue.state == CueState.Missing)
                    || (filter == "내 선택" && cue.IsSelected));
                if (!visible) continue;
                if (group != cue.group) { group = cue.group; cueList.Add(Text(group, "group-heading")); }
                var row = new VisualElement(); row.AddToClassList("cue-row");
                if (cue == active) row.AddToClassList("active");
                if (cue.state == CueState.Missing) row.AddToClassList("missing-row");
                var toggle = new Toggle { value = cue.IsSelected }; toggle.AddToClassList("cue-toggle"); row.Add(toggle);
                toggle.RegisterValueChangedCallback(e => { EditorPrefs.SetBool(ChoicePrefix + cue.id, e.newValue); if (filterField.value == "내 선택") RebuildList(); });
                var rowText = new VisualElement(); rowText.AddToClassList("cue-row-text"); row.Add(rowText);
                rowText.Add(Text(cue.title, "cue-name")); rowText.Add(Text(cue.asset != null ? cue.asset.name : cue.note, "cue-asset"));
                var badge = Text(StateName(cue.state), "row-badge"); badge.AddToClassList(cue.state.ToString().ToLowerInvariant()); row.Add(badge);
                row.RegisterCallback<ClickEvent>(e => { if (e.target == toggle) return; active = cue; RebuildList(); ShowActive(); });
                cueList.Add(row);
            }
        }

        private void ShowActive()
        {
            if (active == null) return;
            titleLabel.text = active.title;
            triggerLabel.text = active.id + "   ·   " + active.trigger;
            stateLabel.text = StateName(active.state);
            stateLabel.ClearClassList(); stateLabel.AddToClassList("state-chip");
            stateLabel.AddToClassList(active.state.ToString().ToLowerInvariant());
            assetLabel.text = active.asset != null ? active.asset.name : "전용 재생 자산 없음";
            noteLabel.text = active.note;
            pingButton.SetEnabled(active.asset != null || active.source != null);
            SpawnActive();
        }

        private void PingAsset()
        {
            var target = active?.asset ?? active?.source;
            if (target == null) return;
            EditorGUIUtility.PingObject(target);
            Selection.activeObject = target;
        }

        private void EnsureStage()
        {
            if (scene.IsValid()) return;
            scene = EditorSceneManager.NewPreviewScene();
            var cameraObject = StageObject("Cue preview camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.cameraType = CameraType.Game; camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            // Match the project's VFX prefab preview "game" environment for quick asset recognition.
            camera.backgroundColor = new Color(.065f, .085f, .125f);
            camera.fieldOfView = 38f; camera.nearClipPlane = .03f; camera.farClipPlane = 120f;
            camera.allowHDR = true; camera.useOcclusionCulling = false;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.volumeLayerMask = 1 << 31;
            var light = StageObject("Cue preview light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 2.6f; light.shadows = LightShadows.None;
            light.color = new Color(1f, .84f, .62f);
            light.transform.rotation = Quaternion.Euler(48f, -35f, 0);
            var fill = StageObject("Cue fill light").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 1.55f; fill.color = new Color(.32f, .58f, 1f);
            fill.transform.rotation = Quaternion.Euler(18f, 150f, 0);
            var volumeObject = StageObject("Cue bloom"); volumeObject.layer = 31;
            var volume = volumeObject.AddComponent<Volume>(); volume.isGlobal = true;
            volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = volumeProfile;
            var bloom = volumeProfile.Add<Bloom>(true);
            bloom.threshold.Override(.85f); bloom.intensity.Override(1.05f); bloom.scatter.Override(.62f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Cue preview ground"; floor.transform.localScale = new Vector3(2.2f, 1f, 2.2f);
            floor.transform.position = new Vector3(0, -.04f, 0);
            SceneManager.MoveGameObjectToScene(floor, scene);
            var collider = floor.GetComponent<Collider>(); if (collider != null) DestroyImmediate(collider);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                floorMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                floorMaterial.color = new Color(.15f, .18f, .24f);
                floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            }
            ResizeSurface();
        }

        private GameObject StageObject(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private void SpawnActive()
        {
            if (active == null || viewport == null) return;
            try
            {
                EnsureStage();
                if (effectRoot != null) { DestroyImmediate(effectRoot); effectRoot = null; }
                chainLink = null;
                UnityEngine.Object source = active.asset;
                if (source is GameObject prefab)
                    effectRoot = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                else if (source is VisualEffectAsset graph)
                {
                    effectRoot = StageObject(graph.name);
                    effectRoot.transform.position = new Vector3(0, 1.05f, 0);
                    effectRoot.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up) * Quaternion.Euler(0, 90, 0);
                    var effect = effectRoot.AddComponent<VisualEffect>();
                    effect.visualEffectAsset = graph;
                }
                if (effectRoot == null)
                {
                    particles = Array.Empty<ParticleSystem>(); graphs = Array.Empty<VisualEffect>();
                    shockwaves = Array.Empty<SwordShockwavePlayback>(); playing = false;
                    hintLabel.text = source == null ? "전용 프리팹이 없습니다. 왼쪽 상태와 연결 메모를 확인하세요." : "미리볼 프리팹이 없습니다.";
                    hintLabel.style.display = DisplayStyle.Flex;
                    playButton.SetEnabled(false); RenderPreview(); return;
                }
                effectRoot.SetActive(true);
                if (active.id == "VFX.ELEC.HV.CHAIN.LINK")
                {
                    // Runtime fallback stretches this source between two targets. Show that authored shape here.
                    effectRoot.transform.position = new Vector3(0, 1.1f, 0);
                    effectRoot.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
                    effectRoot.transform.localScale = Vector3.Scale(effectRoot.transform.localScale, new Vector3(1, 1, 3));
                    chainLink = effectRoot.GetComponentInChildren<ChainElectricityLiteVfxController>(true);
                    if (chainLink != null) chainLink.enabled = false;
                }
                particles = effectRoot.GetComponentsInChildren<ParticleSystem>(true);
                graphs = effectRoot.GetComponentsInChildren<VisualEffect>(true);
                shockwaves = effectRoot.GetComponentsInChildren<SwordShockwavePlayback>(true);
                hasGraph = graphs.Length > 0;
                duration = EstimateDuration();
                timeSlider.highValue = duration;
                distance = 7f;
                focus = new Vector3(0, .7f, 0);
                if (chainLink != null) { focus = new Vector3(0, 1.1f, 0); distance = 5f; }
                hintLabel.text = "프리팹 모양 확인용 · 실제 전투 배치와 크기는 다를 수 있습니다.";
                hintLabel.style.display = DisplayStyle.Flex;
                playButton.SetEnabled(true);
                RestartPlayback();
                if (chainLink == null && graphs.Length == 0)
                {
                    // The VFX prefab preview opens at the first visible particle frame and fits that shape.
                    playing = false;
                    while (clock < Mathf.Min(duration, 1.5f) && VisibleParticles() == 0) Advance(.05f);
                    FitCamera();
                    playing = true; lastUpdate = EditorApplication.timeSinceStartup;
                    SyncTransport(); RenderPreview();
                }
            }
            catch (Exception error)
            {
                playing = false;
                hintLabel.text = "프리뷰 오류: " + error.Message;
                hintLabel.style.display = DisplayStyle.Flex;
                Debug.LogException(error);
            }
        }

        private int VisibleParticles()
        {
            int count = 0;
            foreach (var p in particles)
                if (p != null && p.gameObject.activeInHierarchy && p.GetComponent<ParticleSystemRenderer>()?.enabled == true)
                    count += p.particleCount;
            return count;
        }

        private void FitCamera()
        {
            if (effectRoot == null) return;
            bool found = false;
            Bounds bounds = default;
            foreach (var system in particles)
            {
                if (system == null || !system.gameObject.activeInHierarchy || system.particleCount == 0) continue;
                var sample = new ParticleSystem.Particle[Mathf.Min(system.particleCount, 2000)];
                int count = system.GetParticles(sample);
                var main = system.main;
                for (int i = 0; i < count; i++)
                {
                    Vector3 point = sample[i].position;
                    if (main.simulationSpace == ParticleSystemSimulationSpace.Local)
                        point = system.transform.TransformPoint(point);
                    else if (main.simulationSpace == ParticleSystemSimulationSpace.Custom && main.customSimulationSpace != null)
                        point = main.customSimulationSpace.TransformPoint(point);
                    float size = Mathf.Max(.08f, sample[i].GetCurrentSize(system));
                    var partBounds = new Bounds(point, Vector3.one * size);
                    if (!found) { bounds = partBounds; found = true; }
                    else bounds.Encapsulate(partBounds);
                }
            }
            foreach (var renderer in effectRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!found) return;
            focus = bounds.center;
            float radius = Mathf.Clamp(bounds.extents.magnitude, .5f, 7f);
            distance = Mathf.Clamp(radius * 1.35f / Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad), 1.5f, 22f);
            distance = Mathf.Min(distance, 10f);
            if (active != null && (active.id.EndsWith(".HIT") || active.id.Contains(".STATUS.")))
                distance = Mathf.Min(distance, 5.5f);
        }

        private float EstimateDuration()
        {
            float max = 1.8f;
            if (chainLink != null) return Mathf.Max(1.2f, chainLink.Lifetime + .4f);
            foreach (var particle in particles)
            {
                var main = particle.main;
                max = Mathf.Max(max, main.duration + main.startDelay.constantMax + main.startLifetime.constantMax);
            }
            return Mathf.Clamp(max + .3f, 2f, 12f);
        }

        private void RestartPlayback()
        {
            if (effectRoot == null) return;
            clock = 0; playing = true; lastUpdate = EditorApplication.timeSinceStartup;
            foreach (var p in particles)
            {
                p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                p.useAutoRandomSeed = false; p.randomSeed = 17;
                var main = p.main; main.stopAction = ParticleSystemStopAction.None;
                p.Simulate(0, false, true, false);
                p.Play(false);
            }
            foreach (var g in graphs)
            {
                g.initialEventName = "ComboMakerIdle";
                g.Reinit(); g.pause = true; g.playRate = 0; g.Play();
            }
            foreach (var s in shockwaves) { s.enabled = false; s.RestartVfx(); }
            chainLink?.RestartVfx();
            SyncTransport(); RenderPreview();
        }

        private void TogglePlayback()
        {
            if (effectRoot == null) return;
            if (clock >= duration - .01f) { RestartPlayback(); return; }
            playing = !playing; lastUpdate = EditorApplication.timeSinceStartup; SyncTransport();
        }

        private void Seek(float target)
        {
            if (effectRoot == null) return;
            playing = false;
            RestartPlayback(); playing = false;
            Advance(Mathf.Clamp(target, 0, duration));
            SyncTransport(); RenderPreview();
        }

        private void Advance(float delta)
        {
            delta = Mathf.Max(0, delta);
            clock = Mathf.Min(duration, clock + delta);
            foreach (var p in particles) if (p != null) p.Simulate(delta, false, false, false);
            foreach (var s in shockwaves) if (s != null) s.Advance(delta);
            if (chainLink != null && clock >= chainLink.Lifetime) chainLink.StopAndClearVfx();
            foreach (var g in graphs)
            {
                if (g == null) continue;
                uint steps = (uint)Mathf.Max(1, Mathf.CeilToInt(delta * 60f));
                g.Simulate(delta / steps, steps);
                g.AdvanceOneFrame();
            }
        }

        private void SyncTransport()
        {
            seeking = true;
            timeSlider.SetValueWithoutNotify(clock);
            seeking = false;
            clockLabel.text = $"{clock:0.00} / {duration:0.00}초";
            playButton.text = playing ? "Ⅱ 일시정지" : "▶ 재생";
        }

        private void UpdatePreview()
        {
            if (!playing || effectRoot == null) return;
            double now = EditorApplication.timeSinceStartup;
            if (lastUpdate <= 0) lastUpdate = now;
            if (now - lastRender < 1.0 / 30.0) return;
            float delta = Mathf.Clamp((float)(now - lastUpdate), 0, .08f);
            lastUpdate = now; lastRender = now;
            Advance(delta);
            if (clock >= duration) playing = false;
            SyncTransport(); RenderPreview();
        }

        private void ResizeSurface()
        {
            if (viewport == null || camera == null) return;
            int width = Mathf.Clamp(Mathf.RoundToInt(viewport.contentRect.width), 400, 1400);
            int height = Mathf.Clamp(Mathf.RoundToInt(viewport.contentRect.height), 280, 900);
            if (surface != null && surface.width == width && surface.height == height) return;
            if (surface != null) { surface.Release(); DestroyImmediate(surface); }
            surface = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                { hideFlags = HideFlags.HideAndDontSave, name = "Greatsword VFX cue preview" };
            surface.Create(); viewport.image = surface;
            RenderPreview();
        }

        private void RenderPreview()
        {
            if (camera == null || surface == null) return;
            camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            camera.transform.position = focus - camera.transform.forward * distance;
            camera.aspect = (float)surface.width / surface.height;
            camera.targetTexture = surface;
            RenderTexture previous = RenderTexture.active;
            try
            {
                if (hasGraph) VFXManager.PrepareCamera(camera);
                RenderPipeline.SubmitRenderRequest(camera,
                    new UniversalRenderPipeline.SingleCameraRequest { destination = surface });
                viewport.MarkDirtyRepaint();
            }
            catch (Exception error)
            {
                playing = false;
                hintLabel.text = "렌더 오류: " + error.Message;
                Debug.LogException(error);
            }
            finally { RenderTexture.active = previous; camera.targetTexture = null; }
        }

        private void DisposeStage()
        {
            playing = false;
            if (effectRoot != null) { DestroyImmediate(effectRoot); effectRoot = null; }
            chainLink = null;
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            scene = default; camera = null;
            if (surface != null) { surface.Release(); DestroyImmediate(surface); surface = null; }
            if (volumeProfile != null) { DestroyImmediate(volumeProfile); volumeProfile = null; }
            if (floorMaterial != null) { DestroyImmediate(floorMaterial); floorMaterial = null; }
            particles = Array.Empty<ParticleSystem>(); graphs = Array.Empty<VisualEffect>();
            shockwaves = Array.Empty<SwordShockwavePlayback>();
        }
    }
}
