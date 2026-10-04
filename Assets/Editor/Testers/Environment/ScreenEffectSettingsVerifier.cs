using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ScreenEffectSettingsVerifier
{
    private const string Key = "Overburst.ScreenEffectSettingsVerifier.";
    private static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    private static int Cycle { get => SessionState.GetInt(Key + "cycle", 0); set => SessionState.SetInt(Key + "cycle", value); }
    private static string Output => SessionState.GetString(Key + "output", "");
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");
    private static double Deadline => double.Parse(SessionState.GetString(Key + "deadline", "0"), System.Globalization.CultureInfo.InvariantCulture);

    static ScreenEffectSettingsVerifier()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += Log;
        EditorApplication.playModeStateChanged += StateChanged;
    }

    public static string VerifyAssets()
    {
        var menu=AssetDatabase.LoadAssetAtPath<GameObject>(OverburstGameMenuBuilder.PrefabPath);
        var panel=menu.GetComponentInChildren<OverburstSettingsPanel>(true);
        Require(panel.motionBlur!=null && panel.edgeBlur!=null && panel.combatScroll!=null,"Remaining screen settings wired");
        Require(panel.combatScroll.content.childCount==8 && panel.combatScroll.content.rect.height==1430,"Eight settings rows without gaps");
        Require(menu.GetComponentsInChildren<Transform>(true).All(x=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(x.gameObject)==0),"Menu Missing Script 0");
        var motion=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/Resources/Camera/PF_OverburstMotionBlur.prefab");
        Require(motion!=null && motion.GetComponent<OverburstMotionBlur>()!=null,"Motion blur prefab retained");
        OverburstEdgeBlurVerifier.VerifyAssets();
        return "PASS: eight settings rows, references, Missing Script 0 and actual edge GPU checks";
    }
    public static int CurrentCycle=>Cycle;
    public static string Begin(string output)
    {
        if (Phase != 0 || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required.");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) ||
            !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))
            throw new InvalidOperationException("Another isolated account is active.");
        var boot = SceneManager.GetSceneByName(PersistentSceneFlow.PersistentSceneName);
        Require(boot.IsValid() && boot.isLoaded, "Loaded product PersistentScene required");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "checks", "[]");
        SessionState.SetString(Key + "errors", "[]");
        SessionState.SetString(Key + "status", "RUNNING");
        SessionState.EraseString(Key + "failure");
        SessionState.SetString(Key + "activeScene", SceneManager.GetActiveScene().path);
        SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 1800).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(Path.Combine(output,"IsolatedAccount"));
        File.WriteAllText(Path.Combine(output,"IsolatedAccount",OverburstGameSettings.FileName),"{\"version\":1,\"cameraShake\":0.4,\"hitEffect\":0.6,\"parryPresentation\":true,\"heavyPresentation\":true,\"parryPresentationIntensity\":1,\"heavyPresentationIntensity\":1}");
        Cycle = 1;
        Phase = 1;
        SceneManager.SetActiveScene(boot);
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        return "Started two isolated toggle/render lifecycle cycles";
    }

    private static double idleStarted=-1;
    public static string BeginWhenIdle(string output)
    {
        if(Phase!=0)throw new InvalidOperationException("This verifier already running");
        SessionState.SetString(Key+"pendingOutput",IsolatedSavePlayGuard.ValidateDirectory(output));
        SessionState.SetString(Key+"pendingDeadline",(EditorApplication.timeSinceStartup+600).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
        SessionState.SetString(Key+"status","WAITING");idleStarted=-1;
        return "Waiting for stable idle Editor; does not stop another Play";
    }
    private static void TryBeginQueued()
    {
        string output=SessionState.GetString(Key+"pendingOutput","");if(output.Length==0)return;
        double deadline=double.Parse(SessionState.GetString(Key+"pendingDeadline","0"),System.Globalization.CultureInfo.InvariantCulture);
        if(EditorApplication.timeSinceStartup>deadline){SessionState.EraseString(Key+"pendingOutput");SessionState.EraseString(Key+"pendingDeadline");SessionState.SetString(Key+"status","WAIT_TIMEOUT");return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""))){idleStarted=-1;return;}
        if(idleStarted<0){idleStarted=EditorApplication.timeSinceStartup;return;}
        if(EditorApplication.timeSinceStartup-idleStarted<2)return;
        SessionState.EraseString(Key+"pendingOutput");SessionState.EraseString(Key+"pendingDeadline");
        try
        {
            OverburstGameMenuBuilder.AddScreenEffectSettings();
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"assets-result.json"),JsonConvert.SerializeObject(new{status="PASS",result=VerifyAssets()},Formatting.Indented));
            Begin(output);
        }
        catch(Exception error){SessionState.SetString(Key+"output",output);SessionState.SetString(Key+"status","START_FAILED");SessionState.SetString(Key+"failure",error.ToString());WriteResult();}
    }
    private static void Tick()
    {
        if (Phase == 0) { TryBeginQueued(); return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (EditorApplication.timeSinceStartup > Deadline) throw new TimeoutException("Screen settings verification timeout");
            if (Phase == 3 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (Cycle == 1 && Status == "RUNNING")
                {
                    Cycle = 2;
                    Phase = 1;
                    IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output, "IsolatedAccount"));
                }
                else Phase = 4;
                return;
            }
            if (Phase == 4)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
                string active = IsolatedSavePlayGuard.ActiveDirectory;
                string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
                if (current.Length != 0 || active.Length != 0 || prepared.Length != 0)
                    throw new InvalidOperationException("Account return deferred: another account path is present");
                IsolatedSavePlayGuard.UseRealAccount();
                Require(!IsolatedSavePlayGuard.RequiresAccountChoice, "Normal Play account gate released");
                Require(string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", "")), "Guard preparation expiry cleared");
                var scene = SceneManager.GetSceneByPath(SessionState.GetString(Key + "activeScene", ""));
                if (scene.IsValid() && scene.isLoaded) SceneManager.SetActiveScene(scene);
                Phase = 0;
                if (Status == "RUNNING") SessionState.SetString(Key + "status", "PASS");
                WriteResult();
                SessionState.EraseString(Key + "deadline");
                SessionState.EraseString(Key + "activeScene");
                return;
            }
            if (!EditorApplication.isPlaying) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (Phase != 1) return;
            var flow = Object.FindFirstObjectByType<PersistentSceneFlow>(FindObjectsInactive.Include);
            if (flow != null && !flow.gameObject.activeInHierarchy) flow.gameObject.SetActive(true);
            if (!AccountBootstrap.Ready || !WorldSessionState.IsHideout || PlayerContext.Instance?.CurrentActor == null ||
                PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching) return;
            Check(Path.GetFullPath(AccountBootstrap.SaveDirectory) == Path.GetFullPath(Path.Combine(Output, "IsolatedAccount")), "Product boot uses isolated account");
            Phase = 2;
            new GameObject("ScreenEffectSettingsVerificationRunner").AddComponent<ScreenEffectSettingsVerificationRunner>();
        }
        catch (Exception error) { Fail(error); }
    }

    private static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || (Phase != 1 && Phase != 2)) return;
        SessionState.SetString(Key + "status", "CANCELLED");
        SessionState.SetString(Key + "failure", "Play stopped before verification completed");
        Phase = 4;
    }

    public static void Check(bool value, string label)
    {
        Require(value, label);
        var values = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "checks", "[]"));
        values.Add("Cycle " + Cycle + ": " + label);
        SessionState.SetString(Key + "checks", JsonConvert.SerializeObject(values));
    }

    private static void Require(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
    }

    public static string CapturePath(string label) => Path.Combine(Output, "Cycle" + Cycle + "_" + label + ".png");
    public static void CompleteCycle()
    {
        Check(JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]")).Count == 0, "Runtime errors 0");
        Phase = 3;
        EditorApplication.ExitPlaymode();
    }

    public static void Fail(Exception error)
    {
        // 다른 검증이 계정을 준비한 경우 반환을 중단해 그 계정과 상태를 보존한다.
        if (Phase == 4 && (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) ||
            !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) ||
            !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))))
        {
            Phase = 0;
            SessionState.SetString(Key + "status", "RETURN_DEFERRED");
            SessionState.SetString(Key + "failure", error.ToString());
            WriteResult();
            return;
        }
        SessionState.SetString(Key + "status", "FAIL");
        SessionState.SetString(Key + "failure", error.ToString());
        WriteResult();
        Phase = EditorApplication.isPlayingOrWillChangePlaymode ? 3 : 4;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    private static void Log(string message, string trace, LogType type)
    {
        if (Phase == 0 || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        var errors = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]"));
        errors.Add(message);
        SessionState.SetString(Key + "errors", JsonConvert.SerializeObject(errors));
    }

    private static void WriteResult()
    {
        if (string.IsNullOrEmpty(Output)) return;
        File.WriteAllText(Path.Combine(Output, "play-result.json"), JsonConvert.SerializeObject(new {
            status = Status, phase = Phase, cycles = Cycle,
            checks = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "checks", "[]")),
            errors = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]")),
            failure = SessionState.GetString(Key + "failure", ""),
            accountChoice = IsolatedSavePlayGuard.RequiresAccountChoice,
            isolatedDirectory = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
            guardActive = IsolatedSavePlayGuard.ActiveDirectory
        }, Formatting.Indented));
    }
}

public sealed class ScreenEffectSettingsVerificationRunner : MonoBehaviour
{
    private static readonly System.Reflection.BindingFlags Fields=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    private IEnumerator Start()
    {
        var stack=new Stack<IEnumerator>();stack.Push(Run());
        try
        {
            while(stack.Count>0)
            {
                object yielded=null;bool completed=false;Exception failure=null;
                try{var current=stack.Peek();if(!current.MoveNext()){(current as IDisposable)?.Dispose();stack.Pop();completed=true;}else yielded=current.Current;}
                catch(Exception error){failure=error;}
                if(failure!=null){ScreenEffectSettingsVerifier.Fail(failure);break;}
                if(completed)continue;
                if(yielded is IEnumerator inner && !(yielded is CustomYieldInstruction)){stack.Push(inner);continue;}
                yield return yielded;
            }
        }
        finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();Destroy(gameObject);}
    }
    private static void Check(bool value,string label)=>ScreenEffectSettingsVerifier.Check(value,label);
    private static IEnumerator Wait(float seconds){float end=Time.unscaledTime+seconds;while(Time.unscaledTime<end)yield return null;}
    private static void Amount(OverburstElementEnergy energy,float value)
    {
        energy.Clear();typeof(OverburstElementEnergy).GetProperty("Amount").SetValue(energy,value);
        if(energy.Element==WeaponElement.Light && value>=energy.Capacity)
            typeof(OverburstElementEnergy).GetField("holdUntil",Fields).SetValue(energy,Time.time+OverburstElementTuning.Current.SafeLightOverchargeFullHold);
    }
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Fields).GetValue(owner);
    private static IEnumerator Run()
    {
        yield return VerifySettingsUi();
        if(ScreenEffectSettingsVerifier.CurrentCycle==1)
        {
            var actor=PlayerContext.Instance.CurrentActor;var equipment=actor.Equipment;
            var melee=actor.GetComponent<MeleeRuntime>();var parry=actor.GetComponent<PlayerParryController>();
            var movement=actor.GetComponent<PlayerMovement>();var oldAuthority=movement.ControlAuthority;
            var energy=equipment.GetComponent<OverburstElementEnergy>()??equipment.gameObject.AddComponent<OverburstElementEnergy>();
            var oldWeapon=equipment.CurrentWeaponItem;var oldGem=equipment.EquippedElementGem;
            var setGem=typeof(PlayerEquipment).GetMethod("SetElementGem",Fields);
            bool enteredArena=false;
            try
            {
                var result=EnemyThemeTrialService.ToggleArena();Check(result.Success,"Native combat test arena entry");enteredArena=result.Success;
                yield return Wait(.8f);movement.SetControlAuthority(ActorControlAuthority.AI);
                var weapon=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
                var fire=AssetDatabase.FindAssets("t:ElementGemItemData").Select(id=>AssetDatabase.LoadAssetAtPath<ElementGemItemData>(AssetDatabase.GUIDToAssetPath(id))).Where(g=>g!=null && g.element==WeaponElement.Fire).OrderBy(g=>g.fixedGrade).First();
                Check(equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common)),"Original greatsword action fixture equipped");
                setGem.Invoke(equipment,new object[]{new ItemData(fire,1,fire.fixedGrade)});yield return Wait(.2f);
                foreach(int mode in new[]{0,1,2})
                {
                    melee.CancelCurrentAttackState();OverburstTimeEffectArbiter.ClearOwner(melee);OverburstTimeEffectArbiter.ClearOwner(parry);
                    Amount(energy,100f);
                    if(mode==2)typeof(MeleeRuntime).GetField("requestedDodgeFollowUp",Fields).SetValue(melee,PlayerDodgeFollowUpKind.Heavy);
                    Check(melee.TryStartHeavyAttack(Vector3.forward)==WeaponActionResult.Accepted,"Baseline heavy accepted mode "+mode);
                    if(mode==1)
                    {
                        melee.NotifyHeavyParried(Field<int>(melee,"activeActionId"));
                        typeof(PlayerParryController).GetField("nextSlowAt",Fields).SetValue(parry,0f);
                        typeof(PlayerParryController).GetMethod("PlaySuccess",Fields).Invoke(parry,new object[]{actor.transform.position+Vector3.up+Vector3.forward,1,false});
                        Check(parry.GetComponent<ParrySuccessVfx>()!=null && Time.timeScale<1f,"Original parry feedback and slow remain active");
                        yield return new WaitForEndOfFrame();Capture("ParryBaseline");
                    }
                    bool committed=false;float deadline=Time.unscaledTime+45f;
                    while(melee.IsHeavyAttackInProgress && Time.unscaledTime<deadline)
                    {
                        var discharge=Field<OverburstElementDischarge>(melee,"activeDischarge");
                        if(!committed && discharge!=null)
                        {
                            committed=true;
                            Check(discharge.Element==WeaponElement.Fire && discharge.NormalizedEnergy>=.999f,"Original full-energy fire discharge commits mode "+mode);
                            Check(Mathf.Abs(energy.Amount-(mode==1?50f:0f))<.02f,"Original energy consumption and parry half refund mode "+mode);
                            yield return new WaitForEndOfFrame();if(mode==0)Capture("FullHeavyBaseline");
                        }
                        yield return null;
                    }
                    Check(committed && !melee.IsHeavyAttackInProgress,"Baseline action completes mode "+mode);
                    yield return Wait(.35f);
                }
            }
            finally
            {
                melee.CancelCurrentAttackState();OverburstTimeEffectArbiter.ClearOwner(melee);OverburstTimeEffectArbiter.ClearOwner(parry);
                movement.SetControlAuthority(oldAuthority);
                if(oldWeapon!=null)equipment.EquipWeaponItem(oldWeapon);setGem.Invoke(equipment,new object[]{oldGem});
                if(enteredArena && EnemyThemeTrialService.InArena)EnemyThemeTrialService.ToggleArena();
            }
        }
        PreserveNextPlaySettings();ScreenEffectSettingsVerifier.CompleteCycle();
    }

    private static void Capture(string label)
    {
        Texture2D texture=null;try{texture=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(ScreenEffectSettingsVerifier.CapturePath(label),texture.EncodeToPNG());}finally{if(texture!=null)Object.Destroy(texture);}
    }
    private static IEnumerator VerifySettingsUi()
    {
        var menu=Object.FindFirstObjectByType<OverburstGameMenu>(FindObjectsInactive.Include);
        var motion=Object.FindFirstObjectByType<OverburstMotionBlur>();
        Check(menu!=null && motion!=null,"Formal menu and motion blur boot in product");
        Check(Object.FindObjectsByType<OverburstMotionBlur>(FindObjectsSortMode.None).Length==1,"One persistent motion service");
        Check(Object.FindObjectsByType<OverburstEdgeBlur>(FindObjectsSortMode.None).Length==1,"One persistent edge blur service");
        if(ScreenEffectSettingsVerifier.CurrentCycle==1)
        {
            Check(!OverburstGameSettings.MotionBlurEnabled && Mathf.Approximately(OverburstGameSettings.MotionBlurIntensity,.01f),"Old file keeps motion default OFF / 1%");
            Check(OverburstGameSettings.EdgeBlurEnabled && OverburstGameSettings.ExplorationEdgeBlurIntensity==.72f && OverburstGameSettings.CombatEdgeBlurIntensity==.42f,"Old settings retain exploration/combat edge defaults");
            Check(Mathf.Approximately(OverburstGameSettings.CameraShakeScale,.4f) && Mathf.Approximately(OverburstGameSettings.HitEffectScale,.6f),"Old settings preserve existing player choices");
        }
        else
        {
            Check(OverburstGameSettings.MotionBlurEnabled && Mathf.Approximately(motion.Settings.intensity.value,.2f),"New Play restores motion ON / 20%");
            Check(!OverburstGameSettings.EdgeBlurEnabled && Mathf.Approximately(OverburstGameSettings.ExplorationEdgeBlurIntensity,.55f) && Mathf.Approximately(OverburstGameSettings.CombatEdgeBlurIntensity,.25f),"New Play restores edge OFF and both saved mode intensities");
        }
        menu.Open();menu.OpenSettings();var panel=menu.settings;panel.tabs[2].isOn=true;
        yield return Wait(.25f);
        Check(panel.combatScroll.content.childCount==8 && panel.motionBlur!=null && panel.edgeBlur!=null,"Only remaining settings controls present");
        Check(Time.timeScale==0,"Settings pause retained");
        foreach(var toggle in new[]{panel.motionBlur,panel.edgeBlur})
        {
            bool before=toggle.isOn;ClickControl(toggle);Check(toggle.isOn!=before,"Visible settings toggle raycast applies "+toggle.transform.parent.parent.name);ClickControl(toggle);
        }
        panel.motionBlur.isOn=false;panel.motionBlurIntensity.value=.2f;
        Check(motion.Settings.intensity.value==0,"Motion intensity while OFF keeps actual volume disabled");
        panel.edgeBlur.isOn=false;panel.explorationEdgeBlurIntensity.value=.55f;panel.combatEdgeBlurIntensity.value=.25f;
        Check(!OverburstEdgeBlur.IsEnabled,"Changing edge intensities while OFF preserves disabled state");
        panel.edgeBlur.isOn=true;
        var mode=PlayerCombatModeController.GetOrCreate();bool wasCombat=mode.IsCombatModeActive;
        mode.ExitCombatMode(PlayerCombatModeReason.System);
        Check(Mathf.Approximately(OverburstEdgeBlur.CurrentStrength,.55f),"Exploration uses saved edge intensity immediately while paused");
        mode.EnterCombatMode(PlayerCombatModeReason.System);
        Check(Mathf.Approximately(OverburstEdgeBlur.CurrentStrength,.25f),"Combat uses its independent saved edge intensity immediately while paused");
        if(!wasCombat)mode.ExitCombatMode(PlayerCombatModeReason.System);
        panel.motionBlur.isOn=true;
        Check(Mathf.Approximately(motion.Settings.intensity.value,.2f),"Motion volume updates immediately while menu is paused");
        foreach(var slider in new[]{panel.motionBlurIntensity,panel.explorationEdgeBlurIntensity,panel.combatEdgeBlurIntensity})
        {
            ClickSlider(slider,.5f);
            Check(Mathf.Abs(slider.normalizedValue-.5f)<.03f,"Visible settings slider accepts pointer "+slider.transform.parent.parent.name);
        }
        panel.explorationEdgeBlurIntensity.value=.55f;panel.combatEdgeBlurIntensity.value=.25f;
        panel.motionBlurIntensity.value=.12f;
        menu.CloseSettings();
        var saved=File.ReadAllText(OverburstGameSettings.FilePath);
        Check(!saved.Contains("parryPresentation") && !saved.Contains("heavyPresentation") && saved.Contains("motionBlurIntensity"),"Saving drops retired fields and retains motion setting");
        Check(saved.Contains("edgeBlur") && saved.Contains("explorationEdgeBlurIntensity") && saved.Contains("combatEdgeBlurIntensity"),"Closing settings persists edge switch and both mode intensities");
        menu.OpenSettings();panel.tabs[2].isOn=true;yield return null;
        Check(Mathf.Approximately(panel.motionBlurIntensity.value,.12f),"Reopening restores selected motion intensity");
        Check(panel.edgeBlur.isOn && Mathf.Approximately(panel.explorationEdgeBlurIntensity.value,.55f) && Mathf.Approximately(panel.combatEdgeBlurIntensity.value,.25f),"Reopening settings restores both edge strengths");
        panel.combatScroll.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();Capture("FormalSettings");
        panel.resetButton.onClick.Invoke();
        Check(!panel.motionBlur.isOn && Mathf.Approximately(panel.motionBlurIntensity.value,.01f),"Reset retains motion OFF / 1%");
        Check(panel.edgeBlur.isOn && Mathf.Approximately(panel.explorationEdgeBlurIntensity.value,.72f) && Mathf.Approximately(panel.combatEdgeBlurIntensity.value,.42f),"Combat reset restores edge ON / exploration 72% / combat 42%");
        OverburstGameSettings.MotionBlurIntensity=float.NaN;Check(Mathf.Approximately(OverburstGameSettings.MotionBlurIntensity,.01f),"Motion NaN falls back safely");
        panel.resetButton.onClick.Invoke();menu.CloseSettings();menu.Close();yield return Wait(.25f);
        Check(!OverburstGameMenu.IsOpen && Time.timeScale>0,"Menu closes and resumes gameplay");
    }
    private static void ScrollTo(UnityEngine.UI.Selectable control)
    {
        var scroll=control.GetComponentInParent<UnityEngine.UI.ScrollRect>();
        if(scroll!=null)
        {
            Canvas.ForceUpdateCanvases();var local=scroll.viewport.InverseTransformPoint(control.transform.position);
            scroll.content.localPosition+=new Vector3(0,scroll.viewport.rect.center.y-local.y,0);scroll.velocity=Vector2.zero;
        }
        Canvas.ForceUpdateCanvases();
    }
    private static void ClickControl(UnityEngine.UI.Toggle control)
    {
        ScrollTo(control);
        var pointer=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){button=UnityEngine.EventSystems.PointerEventData.InputButton.Left,position=RectTransformUtility.WorldToScreenPoint(null,control.transform.position)};
        var hits=new List<UnityEngine.EventSystems.RaycastResult>();UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointer,hits);
        Check(hits.Count>0 && hits[0].gameObject.GetComponentInParent<UnityEngine.UI.Toggle>()==control,"Settings switch receives UI raycast");
        UnityEngine.EventSystems.ExecuteEvents.Execute(control.gameObject,pointer,UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
    }
    private static void ClickSlider(UnityEngine.UI.Slider slider,float position)
    {
        ScrollTo(slider);var area=slider.handleRect.parent.GetComponent<RectTransform>();
        var point=area.TransformPoint(new Vector3(Mathf.Lerp(area.rect.xMin,area.rect.xMax,position),area.rect.center.y,0));
        var pointer=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){button=UnityEngine.EventSystems.PointerEventData.InputButton.Left,position=RectTransformUtility.WorldToScreenPoint(null,point)};
        var hits=new List<UnityEngine.EventSystems.RaycastResult>();UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointer,hits);
        Check(hits.Count>0 && hits[0].gameObject.GetComponentInParent<UnityEngine.UI.Slider>()==slider,"Settings slider receives UI raycast");
        UnityEngine.EventSystems.ExecuteEvents.Execute(slider.gameObject,pointer,UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
        UnityEngine.EventSystems.ExecuteEvents.Execute(slider.gameObject,pointer,UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
    }
    private static void PreserveNextPlaySettings()
    {
        OverburstGameSettings.MotionBlurEnabled=true;OverburstGameSettings.MotionBlurIntensity=.2f;
        OverburstGameSettings.EdgeBlurEnabled=false;OverburstGameSettings.ExplorationEdgeBlurIntensity=.55f;OverburstGameSettings.CombatEdgeBlurIntensity=.25f;
        OverburstGameSettings.SaveIfDirty();
    }
}
