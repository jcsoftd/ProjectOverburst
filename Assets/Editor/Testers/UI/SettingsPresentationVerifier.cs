using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static partial class SettingsPresentationVerifier
{
    const string Key = "Overburst.SettingsPresentationVerifier.";
    static readonly List<string> checks = new List<string>();
    static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
    static readonly List<Object> owned = new List<Object>();
    static string output;
    static double deadline;
    static int frame, sequence;
    static bool background;
    static InputSettings previousInput, temporaryInput;
    static Mouse testMouse;
    static Keyboard testKeyboard;
    static MouseState wantedPointer;
    static readonly Dictionary<InputActionAsset,InputDevice[]> originalDevices=new Dictionary<InputActionAsset,InputDevice[]>();
    static SettingsPresentationVerifier()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        if (!string.IsNullOrEmpty(SessionState.GetString(Key + "pending", ""))) EditorApplication.update += AutoStart;
        if (!string.IsNullOrEmpty(SessionState.GetString(Key + "return", ""))) EditorApplication.update += Return;
        if (SessionState.GetBool(Key+"running",false)) EditorApplication.update += RecoverInterrupted;
    }
    static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks.Add(message);
    }
    public static string AssetsCheck()
    {
        RequireIdle(); checks.Clear();
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(OverburstGameMenuBuilder.PrefabPath);
        var panel = root.GetComponentInChildren<OverburstSettingsPanel>(true);
        Check(panel && panel.tabs.Length == 4 && panel.pages.Length == 4, "four settings tabs retained");
        Check(panel.bloodRows.Length == 7 && panel.bloodStyle && panel.bloodPalette && panel.bloodResetButton, "saved blood controls serialized");
        Check(panel.combatScroll.content.childCount == 21, "18 controls and three section headings");
        Check(Mathf.Approximately(panel.combatScroll.content.sizeDelta.y, 3168f), "finite combat content height");
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "loaded scripts " + t.name);
        foreach (var number in panel.bloodRows)
        {
            Check(number.increase && number.decrease && number.slider && number.valueLabel, "complete blood row " + number.control);
            Check(number.slider.wholeNumbers && Mathf.Approximately(number.slider.minValue, BloodComparisonTuning.Minimum(number.control) * 10f)
                && Mathf.Approximately(number.slider.maxValue, BloodComparisonTuning.Maximum(number.control) * 10f), "tenth step and bounds " + number.control);
            Check(number.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) == null, "no per frame blood row polling " + number.control);
        }
        foreach (var row in panel.GetComponentsInChildren<RectTransform>(true).Where(t => t.name.StartsWith("Row • ")))
        {
            var label = row.Find("Label") as RectTransform;
            var slot = row.Find("Control") as RectTransform;
            if (label == null || slot == null) continue;
            Check(Mathf.Approximately(row.rect.height, 160f), "finite aligned row " + row.name);
            var left = RectTransformUtility.CalculateRelativeRectTransformBounds(row, label);
            var right = RectTransformUtility.CalculateRelativeRectTransformBounds(row, slot);
            Check(left.max.x <= right.min.x, "separate label and control columns " + row.name);
            Check(label.GetComponent<Text>().alignment == TextAnchor.MiddleLeft, "label alignment " + row.name);
        }
        string directory = Path.GetFullPath("../개인파일/코덱스산출/UI/20261005_SettingsBloodAndUX/data");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "assets-result.json"), JsonConvert.SerializeObject(new { success = true, checks }, Formatting.Indented));
        return "PASS " + checks.Count;
    }
    static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required");
        if (!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Another isolated Play owns the Editor");
    }
    static IEnumerator Verify()
    {
        yield return Wait(2f);
        if (File.Exists(Path.Combine(output,"only-c-ground"))) { yield return VerifyCGroundFixture(); yield break; }
        bool reload = File.Exists(Path.Combine(output, "expect-reload"));
        if (File.Exists(Path.Combine(output,"expect-c-reload"))) { CheckCReload(); OverburstGameSettings.BloodPack=true; }
        Check(Object.FindFirstObjectByType<TemporaryBloodComparisonToggle>() == null, "temporary comparison HUD no longer auto creates");
        Check(BloodHitVfxService.PackEnabled == reload && OverburstGameSettings.BloodPack == reload, "saved style applied at boot");
        CheckExclusivePools(Object.FindFirstObjectByType<BloodHitVfxService>(), reload, "saved boot");
        Check(OverburstGameSettings.BloodUniformRed == reload && BloodHitVfxService.UniformRed == reload, "saved palette applied at boot");
        Check(Mathf.Approximately(OverburstGameSettings.MasterVolume, .62f) && Mathf.Approximately(OverburstGameSettings.CameraShakeScale, .4f), "legacy audio and camera fields preserved");
        Check(Mathf.Approximately(BloodComparisonTuning.Scale, reload ? 1.6f : 1f), "scale initial or restored");
        if (reload)
        {
            Check(Mathf.Approximately(BloodComparisonTuning.GroundRgb.x, 1.8f), "ground color restored across Play");
            OverburstGameSettings.ResetBloodStyle(true);
            CheckBDefaults();
        }
        else
        {
            OverburstGameSettings.BloodPack = true; CheckBDefaults();
            OverburstGameSettings.BloodPack = false;
        }
        previousInput = InputSystem.settings; temporaryInput = Object.Instantiate(previousInput);
        SessionState.SetInt(Key+"previousInput",previousInput.GetInstanceID());
        SessionState.SetInt(Key+"temporaryInput",temporaryInput.GetInstanceID());
        temporaryInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        temporaryInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = temporaryInput;
        testMouse = InputSystem.AddDevice<Mouse>("SettingsVerifierMouse"); testKeyboard = InputSystem.AddDevice<Keyboard>("SettingsVerifierKeyboard");
        SessionState.SetInt(Key+"mouse",testMouse.deviceId); SessionState.SetInt(Key+"keyboard",testKeyboard.deviceId);
        var module=EventSystem.current.GetComponent<InputSystemUIInputModule>();
        foreach(var asset in new[]{PlayerInputFacade.Current.RuntimeAsset,module.actionsAsset}.Where(a=>a!=null).Distinct())
        {
            originalDevices[asset]=asset.devices.HasValue?asset.devices.Value.ToArray():null;
            asset.devices=new InputDevice[]{testMouse,testKeyboard};
        }
        SessionState.SetString(Key+"deviceMasks",JsonConvert.SerializeObject(originalDevices.Select(p=>new{asset=p.Key.GetInstanceID(),devices=p.Value?.Select(d=>d.deviceId).ToArray()})));
        wantedPointer=new MouseState(); InputSystem.onBeforeUpdate+=PushInput;
        yield return Frames(4);
        var menu = OverburstGameMenu.Instance;
        Check(menu != null, "product ESC menu installed");
        menu.Open(); menu.OpenSettings(); yield return Frames(5);
        var panel = menu.settings;
        panel.tabs[2].isOn = true; yield return Frames(2);
        foreach(var row in panel.bloodRows)
            Check(Mathf.Approximately(row.slider.handleRect.rect.height,52f) && Mathf.Approximately(row.slider.handleRect.rect.width,28f), "bounded slider handle " + row.control);
        foreach (int i in new[] {0,1,2,3})
        {
            panel.tabs[i].isOn = true; yield return Frames(3);
            Check(panel.pages[i].activeSelf && panel.pages.Where((_, index) => index != i).All(p => !p.activeSelf), "exclusive tab " + i);
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "settings-tab-" + i + ".png")); yield return Frames(2);
        }
        panel.tabs[2].isOn = true; yield return Frames(2);
        SetScroll(panel.combatScroll, .29f); yield return Frames(3);
        ScreenCapture.CaptureScreenshot(Path.Combine(output,"settings-blood-size.png")); yield return Frames(2);
        float bookmark = panel.combatScroll.verticalNormalizedPosition;
        yield return Click(panel.bloodRows[0].increase);
        // The click is a real input-system pointer event. Its row is visible at this bookmark.
        Check(Mathf.Abs(panel.combatScroll.verticalNormalizedPosition - bookmark) < .01f, "pointer number adjustment retains scroll position");
        Check(Mathf.Approximately(BloodComparisonTuning.Scale, reload ? 1.6f : 1.1f), "pointer plus changes exactly 0.1");
        yield return Click(panel.bloodRows[0].decrease);
        Check(Mathf.Approximately(BloodComparisonTuning.Scale, reload ? 1.5f : 1f), "pointer minus changes exactly 0.1");
        EventSystem.current.SetSelectedGameObject(null); yield return Frames(4);
        Check(EventSystem.current.currentSelectedGameObject == null && Mathf.Abs(panel.combatScroll.verticalNormalizedPosition - bookmark) < .01f, "empty pointer selection never jumps to first row");
        panel.tabs[3].isOn = true; SetScroll(panel.keyScroll, .23f); yield return Frames(2);
        panel.tabs[2].isOn = true; yield return Frames(3);
        Check(Mathf.Abs(panel.combatScroll.verticalNormalizedPosition - bookmark) < .01f, "tab switch restores combat bookmark");
        menu.CloseSettings(); menu.OpenSettings(); yield return Frames(3);
        Check(Mathf.Abs(panel.combatScroll.verticalNormalizedPosition - bookmark) < .01f, "close and reopen retains scroll");
        panel.tabs[3].isOn = true; yield return Frames(3);
        Check(Mathf.Abs(panel.keyScroll.verticalNormalizedPosition - .23f) < .01f, "key tab retains independent bookmark");
        panel.tabs[2].isOn = true; yield return Frames(2);
        panel.bloodStyle.SelectOptionByIndex(1); panel.bloodPalette.SelectOptionByIndex(1); yield return Frames(3);
        Check(OverburstGameSettings.BloodPack && BloodHitVfxService.PackEnabled && OverburstGameSettings.BloodUniformRed && BloodHitVfxService.UniformRed, "formal UI switches effect and palette");
        CheckBDefaults();
        SetScroll(panel.combatScroll, .29f); yield return Frames(2);
        var slider = panel.bloodRows[0].slider;
        var track = slider.GetComponent<RectTransform>();
        Vector2 start = ScreenPoint(track, new Vector2(.4f, .5f));
        Vector2 end = ScreenPoint(track, new Vector2(.55f, .5f));
        Pointer(start, false); yield return Frames(2); Pointer(start, true); yield return Frames(2);
        Pointer(end, true); yield return Frames(3); Pointer(end, false); yield return Frames(3);
        Check(Mathf.Abs(panel.combatScroll.verticalNormalizedPosition - bookmark) < .01f, "actual slider drag retains scroll");
        Check(BloodComparisonTuning.Scale > 2f && Mathf.Approximately(BloodComparisonTuning.Scale * 10f, Mathf.Round(BloodComparisonTuning.Scale * 10f)), "slider drag uses tenth steps");
        BloodComparisonTuning.ResetCurrent(); panel.bloodRows[0].RefreshValue();
        yield return Click(panel.bloodRows[0].increase);
        SetScroll(panel.combatScroll, 0f); yield return Frames(3);
        yield return Click(panel.bloodRows[4].increase);
        Check(Mathf.Approximately(BloodComparisonTuning.Scale, 1.6f) && Mathf.Approximately(BloodComparisonTuning.GroundRgb.x, 1.8f), "independent size and ground red controls");
        yield return Frames(10);
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "settings-blood-adjusted.png")); yield return Frames(2);
        // Disabling parent effects also disables their dependent intensity controls.
        panel.combatFacingIndicator.isOn = false; panel.motionBlur.isOn = false; panel.edgeBlur.isOn = false;
        Check(!panel.combatFacingBrightness.interactable && !panel.motionBlurIntensity.interactable && !panel.explorationEdgeBlurIntensity.interactable && !panel.combatEdgeBlurIntensity.interactable, "dependent controls disabled coherently");
        panel.combatFacingIndicator.isOn = true; panel.motionBlur.isOn = true; panel.edgeBlur.isOn = true;
        Check(panel.combatFacingBrightness.interactable && panel.motionBlurIntensity.interactable && panel.explorationEdgeBlurIntensity.interactable, "dependent controls restored");
        menu.CloseSettings(); menu.Close(); yield return Frames(3);
        string saved = Path.Combine(output, "isolated-save", OverburstGameSettings.FileName);
        Check(File.Exists(saved), "settings saved when menu closes");
        var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(saved));
        Check((int)json["version"] == 3 && (bool)json["bloodPack"] && (bool)json["bloodUniformRed"], "version three stores selection and palette");
        Check(Mathf.Approximately((float)json["bloodB"]["scale"], 1.6f) && Mathf.Approximately((float)json["bloodB"]["groundRgb"]["x"], 1.8f), "B size and ground color serialized");
        Check(Mathf.Approximately((float)json["bloodA"]["scale"], 1f), "A adjustments kept independently");
        var blood = Object.FindFirstObjectByType<BloodHitVfxService>();
        Check(blood != null, "blood service remains active after menu");
        int materialsBefore = blood.GetComponentsInChildren<ParticleSystemRenderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().Count();
        float sample = 0f;
        for (int i = 0; i < 1000; i++) sample += BloodComparisonTuning.Scale + BloodComparisonTuning.SprayBrightness;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) sample += BloodComparisonTuning.Scale + BloodComparisonTuning.SprayBrightness;
        long getterBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(getterBytes == 0, "100000 steady tuning reads allocate zero bytes");
        yield return Frames(20);
        Check(blood.GetComponentsInChildren<ParticleSystemRenderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().Count() == materialsBefore, "idle settings system creates no new spray materials");
        File.WriteAllText(Path.Combine(output, "overhead-result.json"), JsonConvert.SerializeObject(new { getterReads = 100000, getterBytes, materialsBefore, sample, note = "Allocation and material stability only; not an FPS comparison" }, Formatting.Indented));
        var player=PlayerInputFacade.Current.GetComponent<CombatHealth>();
        var targetRoot=new GameObject("Owned Settings Blood Target"); owned.Add(targetRoot);
        SessionState.SetInt(Key+"target",targetRoot.GetInstanceID());
        targetRoot.transform.position=player.transform.position+Vector3.forward*2f;
        var health=targetRoot.AddComponent<CombatHealth>(); var target=targetRoot.AddComponent<BloodHitTarget>();
        var targetData=new SerializedObject(target); targetData.FindProperty("profile").objectReferenceValue=Resources.Load<BloodHitProfile>("Combat/Blood/SpiderBrood"); targetData.ApplyModifiedPropertiesWithoutUndo();
        var ground=Object.FindFirstObjectByType<BloodGroundDecalService>();
        foreach(bool pack in new[]{false,true})
        {
            OverburstGameSettings.BloodPack=pack;
            int played=blood.PlayedCount;
            var point=targetRoot.transform.position+Vector3.up;
            BloodHitVfxService.Request(new CombatHitFeedbackRequest(player.gameObject,sequence++,null,false,WeaponElement.None,point,false,target:health,impactDirection:Vector3.right),point,1f);
            bool particles=false;
            for(int i=0;i<8;i++)
            {
                yield return null;
                particles|=pack?blood.GetComponentsInChildren<ParticleSystem>(true).Any(p=>p.particleCount>0):blood.GetComponentsInChildren<VisualEffect>(true).Any(v=>v.aliveParticleCount>0);
            }
            Check(blood.PlayedCount>played && particles,"formal style routes actual monster blood "+pack);
            CheckExclusivePools(blood, pack, "actual hit");
            yield return Wait(.65f);
            Check(ground.ActiveCount>0,"formal style routes actual ground blood "+pack);
            int count=ground.MaterialVariantCount; int revision=BloodComparisonTuning.Revision;
            BloodComparisonTuning.Adjust(BloodComparisonTuning.Control.GroundBrightness,1); yield return Frames(3);
            Check(BloodComparisonTuning.Revision>revision && ground.MaterialVariantCount==count,"adjustment reuses existing ground materials "+pack);
            BloodComparisonTuning.Adjust(BloodComparisonTuning.Control.GroundBrightness,-1);
            player.TakeDamage(new DamageInfo(1,player.transform.position+Vector3.up,targetRoot,Vector3.right)); yield return Frames(2);
            Check(Object.FindFirstObjectByType<PlayerDamageFeedback>().UsingPackVignette==pack,"actual player hit selects saved vignette style "+pack);
            yield return Wait(.65f);
        }
        var reusedGround = ground.GetComponentsInChildren<DecalProjector>(true).Select(p=>p.GetInstanceID()).ToArray();
        var switchSamples = new List<object>();
        for (int pass = 0; pass < 4; pass++)
        {
            bool pack = pass % 2 != 0;
            var oldSprays = blood.transform.Cast<Transform>().Where(t=>t.name.StartsWith("Blood Pack ") || (t.name.StartsWith("Blood ") && t.GetComponent<VisualEffect>()!=null)).Select(t=>t.gameObject).ToArray();
            var oldMaterials = ground.GetComponentsInChildren<DecalProjector>(true).Select(p=>p.material)
                .Concat(blood.GetComponentsInChildren<ParticleSystemRenderer>(true).SelectMany(r=>r.sharedMaterials))
                .Where(m=>m!=null && !EditorUtility.IsPersistent(m)).Distinct().ToArray();
            long memoryBefore = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            OverburstGameSettings.BloodPack = pack; clock.Stop();
            yield return Frames(3);
            CheckExclusivePools(blood, pack, "repeat " + pass);
            Check(oldSprays.All(go=>go==null), "previous native spray objects destroyed " + pass);
            Check(oldMaterials.All(m=>m==null), "previous owned spray and ground materials destroyed " + pass);
            Check(ground.ActiveCount==0 && ground.MaterialVariantCount==0, "previous ground marks and material cache released " + pass);
            Check(ground.GetComponentsInChildren<DecalProjector>(true).Select(p=>p.GetInstanceID()).SequenceEqual(reusedGround), "one shared ground pool retained " + pass);
            int count = blood.PlayedCount;
            var point = targetRoot.transform.position + Vector3.up;
            BloodHitVfxService.Request(new CombatHitFeedbackRequest(player.gameObject,sequence++,null,false,WeaponElement.None,point,false,target:health,impactDirection:Vector3.right),point,1f);
            yield return Frames(8);
            Check(blood.PlayedCount>count, "hit after pool recreation " + pass);
            yield return Wait(.65f);
            Check(ground.ActiveCount>0, "ground after pool recreation " + pass);
            var currentSprays = blood.transform.Cast<Transform>().Where(t=>t.name.StartsWith("Blood Pack ") || (t.name.StartsWith("Blood ") && t.GetComponent<VisualEffect>()!=null)).Select(t=>t.GetInstanceID()).ToArray();
            Check(BloodHitVfxService.SetPackEnabled(pack), "same pack remains ready " + pass);
            yield return Frames(2);
            Check(blood.transform.Cast<Transform>().Where(t=>t.name.StartsWith("Blood Pack ") || (t.name.StartsWith("Blood ") && t.GetComponent<VisualEffect>()!=null)).Select(t=>t.GetInstanceID()).SequenceEqual(currentSprays), "same selection reuses current pool " + pass);
            switchSamples.Add(new { pass, pack, switchMs=clock.Elapsed.TotalMilliseconds, memoryBefore, memoryAfter=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(), note="Editor sample; object ownership verified, no FPS or process RAM claim" });
        }
        File.WriteAllText(Path.Combine(output,"pool-switch-result.json"), JsonConvert.SerializeObject(switchSamples,Formatting.Indented));
        yield return VerifyZeroStrengthEffects();
        if (File.Exists(Path.Combine(output,"verify-c"))) yield return VerifyVolumetric(player,health,targetRoot,blood,ground);
        else { OverburstGameSettings.BloodPack=true; OverburstGameSettings.SaveIfDirty(); }
    }
    static IEnumerator VerifyZeroStrengthEffects()
    {
        var camera=Camera.main;
        var actor=PlayerInputFacade.Current;
        var effect=actor.GetComponent<PlayerCombatFacingVfx>();
        var mode=PlayerCombatModeController.GetOrCreate();
        Check(camera!=null && effect!=null && effect.VisualRoot!=null, "actual camera and player facing effect for zero strength");
        bool previousMode=mode.IsCombatModeActive;
        bool previousEdge=OverburstGameSettings.EdgeBlurEnabled, previousFacing=OverburstGameSettings.CombatFacingIndicator;
        float previousExplore=OverburstGameSettings.ExplorationEdgeBlurIntensity, previousCombat=OverburstGameSettings.CombatEdgeBlurIntensity;
        float previousBrightness=OverburstGameSettings.CombatFacingBrightness;
        RenderTexture render=null;
        try
        {
            render=new RenderTexture(640,360,24,RenderTextureFormat.ARGBHalf){name="Owned Zero Strength Render"};render.Create();
            int RenderPasses()
            {
                int before=OverburstEdgeBlurRendererFeature.RecordedPassCount;
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=render});
                return OverburstEdgeBlurRendererFeature.RecordedPassCount-before;
            }
            OverburstGameSettings.EdgeBlurEnabled=true;
            for(int repeat=0;repeat<2;repeat++)
            {
                mode.ExitCombatMode(PlayerCombatModeReason.System);
                OverburstGameSettings.ExplorationEdgeBlurIntensity=0f;OverburstGameSettings.CombatEdgeBlurIntensity=.42f;
                Check(OverburstEdgeBlur.CurrentStrength==0f && RenderPasses()==0, "exploration zero skips actual render graph pass "+repeat);
                OverburstGameSettings.ExplorationEdgeBlurIntensity=.72f;
                Check(RenderPasses()>0, "exploration positive resumes actual pass "+repeat);
                mode.EnterCombatMode(PlayerCombatModeReason.System);
                OverburstGameSettings.CombatEdgeBlurIntensity=0f;
                Check(OverburstEdgeBlur.CurrentStrength==0f && RenderPasses()==0, "combat zero skips actual render graph pass "+repeat);
                OverburstGameSettings.CombatEdgeBlurIntensity=.42f;
                Check(RenderPasses()>0, "combat positive resumes actual pass "+repeat);
                OverburstGameSettings.EdgeBlurEnabled=false;
                Check(RenderPasses()==0, "edge toggle off still skips actual pass "+repeat);
                OverburstGameSettings.EdgeBlurEnabled=true;
            }
            OverburstGameSettings.CombatFacingIndicator=true;OverburstGameSettings.CombatFacingBrightness=1f;
            yield return Wait(.4f);
            Check(effect.IsVisible, "actual player facing visible before zero optimization");
            var root=effect.VisualRoot;
            var rendererIds=root.GetComponentsInChildren<Renderer>(true).Select(r=>r.GetInstanceID()).ToArray();
            var materials=root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).ToArray();
            for(int repeat=0;repeat<3;repeat++)
            {
                var menu=OverburstGameMenu.Instance;menu.Open();menu.OpenSettings();menu.settings.tabs[2].isOn=true;
                float fade=effect.Visibility;
                menu.settings.combatFacingBrightness.value=0f;
                Check(!effect.IsVisible && !root.gameObject.activeSelf, "paused UI zero immediately hides rendering "+repeat);
                Check(Mathf.Approximately(effect.Visibility,fade), "zero retains existing fade state "+repeat);
                menu.settings.combatFacingBrightness.value=1f;
                Check(effect.IsVisible && Mathf.Approximately(effect.Visibility,fade), "paused positive immediately resumes same fade "+repeat);
                menu.settings.combatFacingBrightness.value=0f;menu.CloseSettings();menu.Close();
                float clock=effect.FlowTime;Vector3 position=root.position;
                yield return Frames(20);
                Check(!root.gameObject.activeSelf && Mathf.Approximately(clock,effect.FlowTime) && root.position==position, "unpaused zero skips clock placement and rendering "+repeat);
                OverburstGameSettings.CombatFacingBrightness=1f;
                yield return Frames(4);
                Check(effect.IsVisible && !Mathf.Approximately(clock,effect.FlowTime), "positive resumes actual player update "+repeat);
                Check(root.GetComponentsInChildren<Renderer>(true).Select(r=>r.GetInstanceID()).SequenceEqual(rendererIds)
                    && root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).SequenceEqual(materials), "zero positive creates no replacement renderers or materials "+repeat);
            }
            OverburstGameSettings.CombatFacingBrightness=0f;mode.ExitCombatMode(PlayerCombatModeReason.System);
            OverburstGameSettings.CombatFacingBrightness=1f;yield return Wait(.25f);
            Check(!effect.IsVisible, "restored brightness in exploration stays hidden");
        }
        finally
        {
            if(render!=null){render.Release();Object.DestroyImmediate(render);}
            OverburstGameSettings.EdgeBlurEnabled=previousEdge;OverburstGameSettings.ExplorationEdgeBlurIntensity=previousExplore;OverburstGameSettings.CombatEdgeBlurIntensity=previousCombat;
            OverburstGameSettings.CombatFacingIndicator=previousFacing;OverburstGameSettings.CombatFacingBrightness=previousBrightness;
            if(previousMode)mode.EnterCombatMode(PlayerCombatModeReason.System);else mode.ExitCombatMode(PlayerCombatModeReason.System);
        }
    }
    static void CheckExclusivePools(BloodHitVfxService blood, bool pack, string phase)
    {
        Check(blood != null, "blood service present " + phase);
        Check(blood.GetComponentsInChildren<VisualEffect>(true).Length == (pack ? 0 : BloodHitVfxService.Capacity), "only selected legacy pool " + phase);
        Check(blood.transform.Cast<Transform>().Count(t=>t.name.StartsWith("Blood Pack ")) == (pack ? BloodEffectsPackPool.Capacity : 0), "only selected pack pool " + phase);
        Check(blood.GetComponentsInChildren<DecalProjector>(true).Length == BloodGroundDecalService.Capacity, "one ground pool " + phase);
    }
    static void CheckBDefaults()
    {
        Check(Mathf.Approximately(BloodComparisonTuning.Scale, 1.5f) && Mathf.Approximately(BloodComparisonTuning.SprayBrightness, .8f)
            && Mathf.Approximately(BloodComparisonTuning.GroundScale, 1f) && Mathf.Approximately(BloodComparisonTuning.GroundBrightness, .8f)
            && BloodComparisonTuning.GroundRgb == new Vector3(1.7f,1f,1f), "B defaults match approved screenshot");
    }
    static void SetScroll(ScrollRect scroll, float value) { scroll.StopMovement(); Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = value; }
    static Vector2 ScreenPoint(RectTransform rect, Vector2 normalized)
    {
        var local = new Vector3(Mathf.Lerp(rect.rect.xMin,rect.rect.xMax,normalized.x), Mathf.Lerp(rect.rect.yMin,rect.rect.yMax,normalized.y));
        return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(local));
    }
    static void Pointer(Vector2 point, bool down) => wantedPointer=new MouseState { position = point, buttons = (ushort)(down ? 1 : 0) };
    static void PushInput() { if(testMouse!=null && testMouse.added)InputSystem.QueueStateEvent(testMouse,wantedPointer); }
    static IEnumerator Click(Button button)
    {
        Canvas.ForceUpdateCanvases(); var point = ScreenPoint((RectTransform)button.transform, new Vector2(.5f,.5f));
        var data = new PointerEventData(EventSystem.current) { position = point };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data,hits);
        Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, "actual raycast reaches " + button.transform.parent.parent.name + "/" + button.name);
        Pointer(point,false); yield return Frames(2); Pointer(point,true); yield return Frames(2); Pointer(point,false); yield return Frames(3);
    }
    public static void Start(string directory)
    {
        RequireIdle();
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY"))) throw new InvalidOperationException("External settings override is active");
        output = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(output);
        SessionState.SetBool(Key + "background", Application.runInBackground);
        SessionState.SetString(Key + "pending",output);
        SessionState.SetString(Key + "return",output);
        SessionState.SetFloat(Key + "returnDeadline",(float)(EditorApplication.timeSinceStartup + 600));
        SessionState.SetString(Key + "startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        File.WriteAllText(Path.Combine(output,"edit-before.json"),JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i=> {var s=SceneManager.GetSceneAt(i);return new {s.path,s.isDirty};}),Formatting.Indented));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update -= AutoStart; EditorApplication.update += AutoStart;
        SessionState.SetFloat(Key + "deadline",(float)(EditorApplication.timeSinceStartup + 240));
        string save = Path.Combine(output,"isolated-save"); Directory.CreateDirectory(save);
        if (!File.Exists(Path.Combine(output,"expect-reload"))) File.WriteAllText(Path.Combine(save,OverburstGameSettings.FileName),"{\"version\":1,\"masterVolume\":0.62,\"cameraShake\":0.4}");
        IsolatedSavePlayGuard.EnterIsolatedPlay(save);
    }
    static void StateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && work.Count > 0) Finish(new Exception("User or external Play exit"),false);
        if (state == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(SessionState.GetString(Key + "return","")))
        { EditorApplication.update -= Return; EditorApplication.update += Return; }
    }
    static void AutoStart()
    {
        string pending = SessionState.GetString(Key + "pending","");
        if (string.IsNullOrEmpty(pending)) {EditorApplication.update -= AutoStart;return;}
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline",0))
        {
            output=pending; SessionState.EraseString(Key+"pending"); EditorApplication.update -= AutoStart;
            Finish(new Exception("Startup timeout"),EditorApplication.isPlaying); return;
        }
        if (!EditorApplication.isPlaying || PlayerInputFacade.Current == null || Camera.main == null) return;
        output=pending; SessionState.EraseString(Key+"pending"); EditorApplication.update -= AutoStart;
        checks.Clear(); frame=-1; sequence=100; deadline=EditorApplication.timeSinceStartup+180;
        background=SessionState.GetBool(Key+"background",Application.runInBackground); Application.runInBackground=true;
        SessionState.SetBool(Key+"running",true);
        work.Push(Verify()); EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (Time.frameCount == frame && EditorApplication.timeSinceStartup < deadline) return;
        frame=Time.frameCount;
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new Exception("Play timeout or exit");
            while (work.Count>0)
            {
                var item=work.Peek();
                if (!item.MoveNext()) { (work.Pop() as IDisposable)?.Dispose();continue; }
                if (item.Current is IEnumerator nested) {work.Push(nested);continue;}
                return;
            }
            Finish(null,true);
        }
        catch(Exception error) { Finish(error,true); }
    }
    static IEnumerator Frames(int count) {for(int i=0;i<count;i++)yield return null;}
    static IEnumerator Wait(float seconds) {float until=Time.unscaledTime+seconds;while(Time.unscaledTime<until)yield return null;}
    static void Finish(Exception error,bool exit)
    {
        EditorApplication.update-=RecoverInterrupted;
        InputSystem.onBeforeUpdate-=PushInput;
        EditorApplication.update-=Tick; EditorApplication.update-=AutoStart;
        while(work.Count>0)(work.Pop() as IDisposable)?.Dispose();
        if (OverburstGameMenu.Instance != null && OverburstGameMenu.IsOpen) OverburstGameMenu.Instance.Close();
        foreach(var pair in originalDevices)if(pair.Key!=null)pair.Key.devices=pair.Value;
        originalDevices.Clear(); SessionState.EraseString(Key+"deviceMasks");
        if(testMouse != null && testMouse.added)InputSystem.RemoveDevice(testMouse); testMouse=null;
        if(testKeyboard != null && testKeyboard.added)InputSystem.RemoveDevice(testKeyboard); testKeyboard=null;
        if(previousInput != null)
        {
            InputSystem.settings=previousInput;
            // Persist the restored reference before disposing the temporary settings.
            // InputSystemObject otherwise can retain a destroyed clone across reload.
            var stateType=typeof(InputSettings).Assembly.GetType("UnityEngine.InputSystem.InputSystemObject");
            if(stateType!=null)foreach(var state in Resources.FindObjectsOfTypeAll(stateType))
                (state as ISerializationCallbackReceiver)?.OnBeforeSerialize();
        }
        if(temporaryInput != null)Object.DestroyImmediate(temporaryInput);
        previousInput=null; temporaryInput=null;
        foreach(var item in owned)if(item!=null)Object.DestroyImmediate(item); owned.Clear();
        Application.runInBackground=SessionState.GetBool(Key+"background",Application.runInBackground);
        SessionState.EraseBool(Key+"background");
        SessionState.EraseString(Key+"pending"); SessionState.EraseFloat(Key+"deadline");
        SessionState.EraseBool(Key+"running");
        foreach(string item in new[]{"previousInput","temporaryInput","mouse","keyboard","target"}) SessionState.EraseInt(Key+item);
        File.WriteAllText(Path.Combine(output,"play-result.json"),JsonConvert.SerializeObject(new {success=error==null,checks,error=error?.ToString()},Formatting.Indented));
        if(exit && EditorApplication.isPlaying)EditorApplication.ExitPlaymode();
        if(!EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.update-=Return;EditorApplication.update+=Return;}
    }
    static void RecoverInterrupted()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        string path=SessionState.GetString(Key+"return","");
        if(string.IsNullOrEmpty(path)){EditorApplication.update-=RecoverInterrupted;return;}
        if(EditorApplication.isPlaying && !string.Equals(IsolatedSavePlayGuard.ActiveDirectory,Path.Combine(path,"isolated-save"),StringComparison.OrdinalIgnoreCase))return;
        output=path;
        string masks=SessionState.GetString(Key+"deviceMasks","");
        if(!string.IsNullOrEmpty(masks))foreach(var row in Newtonsoft.Json.Linq.JArray.Parse(masks))
        {
            var asset=EditorUtility.InstanceIDToObject((int)row["asset"]) as InputActionAsset;
            if(asset!=null) originalDevices[asset]=row["devices"].Type==Newtonsoft.Json.Linq.JTokenType.Null?null:row["devices"].Select(id=>InputSystem.GetDeviceById((int)id)).Where(d=>d!=null).ToArray();
        }
        temporaryInput=EditorUtility.InstanceIDToObject(SessionState.GetInt(Key+"temporaryInput",0)) as InputSettings;
        if(temporaryInput != null && InputSystem.settings==temporaryInput)
            previousInput=EditorUtility.InstanceIDToObject(SessionState.GetInt(Key+"previousInput",0)) as InputSettings;
        var mouse=InputSystem.GetDeviceById(SessionState.GetInt(Key+"mouse",0));
        if(mouse is Mouse m && m.name=="SettingsVerifierMouse")testMouse=m;
        var keyboard=InputSystem.GetDeviceById(SessionState.GetInt(Key+"keyboard",0));
        if(keyboard is Keyboard k && k.name=="SettingsVerifierKeyboard")testKeyboard=k;
        var target=EditorUtility.InstanceIDToObject(SessionState.GetInt(Key+"target",0)) as GameObject;
        if(target!=null && target.name=="Owned Settings Blood Target")owned.Add(target);
        Finish(new Exception("Verification interrupted by assembly reload; owned resources and account returned"),EditorApplication.isPlaying);
    }
    static void Return()
    {
        string path=SessionState.GetString(Key+"return","");
        if(string.IsNullOrEmpty(path)){EditorApplication.update-=Return;return;}
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+"returnDeadline",0))
        {
            EditorApplication.update-=Return;
            File.WriteAllText(Path.Combine(path,"return-result.json"),"{\"success\":false,\"reason\":\"idle return timeout\"}");
            return;
        }
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        string current=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"";
        if(!string.IsNullOrEmpty(current) && !Path.GetFullPath(current).StartsWith(Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))return;
        if(!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))return;
        string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
        if(!string.IsNullOrEmpty(prepared) && !Path.GetFullPath(prepared).StartsWith(Path.GetFullPath(path)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return;
        string startScene=SessionState.GetString(Key+"startScene","");
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(startScene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(startScene);
        IsolatedSavePlayGuard.UseRealAccount();
        SessionState.EraseString(Key+"return");SessionState.EraseString(Key+"startScene");SessionState.EraseFloat(Key+"returnDeadline");EditorApplication.update-=Return;
        File.WriteAllText(Path.Combine(path,"return-result.json"),JsonConvert.SerializeObject(new {success=!IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")) && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires","")), pid=System.Diagnostics.Process.GetCurrentProcess().Id, project=Directory.GetParent(Application.dataPath).FullName,guardActive=IsolatedSavePlayGuard.ActiveDirectory,prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),expires=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),scenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return new{s.path,s.isDirty};})},Formatting.Indented));
    }
}
