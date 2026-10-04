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
public static class CombatPresentationSettingsVerifier
{
    private const string Key = "Overburst.CombatPresentationSettingsVerifier.";
    private static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    private static int Cycle { get => SessionState.GetInt(Key + "cycle", 0); set => SessionState.SetInt(Key + "cycle", value); }
    private static string Output => SessionState.GetString(Key + "output", "");
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");
    private static double Deadline => double.Parse(SessionState.GetString(Key + "deadline", "0"), System.Globalization.CultureInfo.InvariantCulture);

    static CombatPresentationSettingsVerifier()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += Log;
        EditorApplication.playModeStateChanged += StateChanged;
    }

    public static string VerifyAssets()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(CombatMomentPresentationBuilder.PrefabPath);
        Require(prefab!=null && prefab.GetComponent<CombatMomentPresentation>()!=null,"Native moment prefab exists");
        var lines=prefab.GetComponentsInChildren<LineRenderer>(true);
        Require(lines.Length==51 && lines.All(x=>!x.enabled && x.sharedMaterial!=null),"51 prepared line renderers disabled at rest");
        Require(prefab.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"Moment prefab Missing Script 0");
        var material=AssetDatabase.LoadAssetAtPath<Material>(CombatMomentPresentationBuilder.MaterialPath);
        Require(material!=null && material.shader.name=="OVERBURST/CombatMomentGlow" && material.GetFloat("_Intensity")==4f,"Native HDR material binding");
        Require(AssetDatabase.AssetPathToGUID(CombatMomentPresentationBuilder.PrefabPath).Length==32,"Prefab GUID valid");
        Require(material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader),"HDR glow shader supported and error-free");
        var hub=AssetDatabase.LoadAssetAtPath<GameObject>(DebugHubPrefabBuilder.PrefabPath);
        var view=hub!=null?hub.GetComponent<Overburst.DebugTools.DebugHubView>():null;
        Require(view!=null && view.ItemIds.All(id=>!id.StartsWith("presentation.moment.",StringComparison.Ordinal)),"Moment items removed from native debug panel");
        Require(hub.GetComponentsInChildren<Transform>(true).All(t=>!t.name.StartsWith("Row presentation.moment.",StringComparison.Ordinal)),"No baked moment rows remain");
        Require(prefab.GetComponentsInChildren<Canvas>(true).Length==0,"Moment prefab contains no temporary UI");
        var menu=AssetDatabase.LoadAssetAtPath<GameObject>(OverburstGameMenuBuilder.PrefabPath);
        var panel=menu.GetComponentInChildren<OverburstSettingsPanel>(true);
        Require(panel.parryPresentation!=null && panel.heavyPresentation!=null && panel.motionBlur!=null && panel.edgeBlur!=null && panel.combatScroll!=null,"Four native settings toggles and scroll references");
        Require(panel.parryPresentationIntensity.maxValue==2 && panel.heavyPresentationIntensity.maxValue==2 && panel.motionBlurIntensity.maxValue==1 && panel.explorationEdgeBlurIntensity.maxValue==1 && panel.combatEdgeBlurIntensity.maxValue==1,"Settings slider bounds");
        Require(menu.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"Game menu Missing Script 0");
        var motion=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/Resources/Camera/PF_OverburstMotionBlur.prefab");
        Require(motion.GetComponent<OverburstMotionBlur>()!=null && motion.GetComponentsInChildren<Canvas>(true).Length==0,"Formal motion prefab binding and no temporary UI");
        var renderer=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(OverburstEdgeBlurBuilder.RendererPath);
        Require(renderer.rendererFeatures.OfType<OverburstEdgeBlurRendererFeature>().Count()==1,"Existing combined feature only once");
        OverburstEdgeBlurVerifier.VerifyAssets();
        VerifyMomentPixels(renderer.rendererFeatures.OfType<OverburstEdgeBlurRendererFeature>().Single().BlurShader);
        return "PASS: native presentation settings and service references / Missing Script / HDR material / center-alpha-gain GPU checks / existing edge blur GPU regression";
    }

    private static void VerifyMomentPixels(Shader shader)
    {
        Texture2D pattern=null,readback=null;RenderTexture source=null,target=null;
        RTHandle handle=null;Material material=null;CommandBuffer commands=null;
        var previous=RenderTexture.active;const int width=640,height=360;
        try
        {
            pattern=new Texture2D(width,height,TextureFormat.RGBA32,false,true);
            var pixels=Enumerable.Repeat(new Color32(128,128,128,128),width*height).ToArray();
            pattern.SetPixels32(pixels);pattern.Apply();
            source=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            target=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            source.Create();target.Create();Graphics.Blit(pattern,source);handle=RTHandles.Alloc(source);
            material=CoreUtils.CreateEngineMaterial(shader);readback=new Texture2D(width,height,TextureFormat.RGBA32,false,true);
            commands=new CommandBuffer{name="Moment gain pixel verification"};
            foreach(float blur in new[]{0f,.72f})foreach(float gain in new[]{0f,-.05f,.04f,-1f,1f})
            {
                material.SetFloat("_EdgeBlurStrength",blur);material.SetVector("_MomentPulse",new Vector4(.5f,.5f,gain,0));
                commands.Clear();commands.SetRenderTarget(target);Blitter.BlitTexture(commands,handle,new Vector4(1,1,0,0),material,0);Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,width,height),0,0);readback.Apply();
                float baseline=128f/255f;var center=readback.GetPixel(width/2,height/2);var edge=readback.GetPixel(2,height/2);
                Require(Mathf.Abs(center.r-baseline)<.008f,"Moment protects center pixels");
                Require(Mathf.Abs(edge.r-baseline*(1f+Mathf.Clamp(gain,-.06f,.05f)))<.008f,"Moment edge gain and bounds correct");
                Require(Mathf.Abs(center.a-baseline)<.008f && Mathf.Abs(edge.a-baseline)<.008f,"Moment preserves source alpha");
            }
        }
        finally
        {
            RenderTexture.active=previous;commands?.Release();handle?.Release();CoreUtils.Destroy(material);
            if(source!=null){source.Release();Object.DestroyImmediate(source);}if(target!=null){target.Release();Object.DestroyImmediate(target);}
            if(pattern!=null)Object.DestroyImmediate(pattern);if(readback!=null)Object.DestroyImmediate(readback);
        }
    }
    public static int CurrentCycle=>Cycle;
    public static bool CaptureComparisons=>SessionState.GetBool(Key+"captureVideos",false);
    public static bool CaptureOnly=>SessionState.GetBool(Key+"captureOnly",false);
    public static string ComparisonOutput=>Path.Combine(Output,"Frames");
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
        File.WriteAllText(Path.Combine(output,"IsolatedAccount",OverburstGameSettings.FileName),"{\"version\":1,\"cameraShake\":0.4,\"hitEffect\":0.6}");
        Cycle = 1;
        Phase = 1;
        SceneManager.SetActiveScene(boot);
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        return "Started two isolated toggle/render lifecycle cycles";
    }

    private static double idleStarted=-1;
    public static string BeginWhenIdle(string output,bool uiOnly=false,bool captureOnly=false)
    {
        if(Phase!=0)throw new InvalidOperationException("This verifier already running");
        SessionState.SetString(Key+"pendingOutput",IsolatedSavePlayGuard.ValidateDirectory(output));
        SessionState.SetString(Key+"pendingDeadline",(EditorApplication.timeSinceStartup+600).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
        SessionState.SetBool(Key+"uiOnly",uiOnly);SessionState.SetBool(Key+"captureOnly",captureOnly);SessionState.SetString(Key+"status","WAITING");idleStarted=-1;
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
            CombatMomentPresentationBuilder.UpgradePreviewAssets();
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
            if (EditorApplication.timeSinceStartup > Deadline) throw new TimeoutException("Moment verification timeout");
            if (Phase == 3 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (Cycle == 1 && Status == "RUNNING" && !CaptureOnly)
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
                SessionState.EraseBool(Key + "uiOnly");
                SessionState.EraseBool(Key + "captureVideos");
                SessionState.EraseBool(Key + "captureOnly");
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
            new GameObject("CombatPresentationSettingsVerificationRunner").AddComponent<CombatPresentationSettingsVerificationRunner>();
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

public sealed class CombatPresentationSettingsVerificationRunner : MonoBehaviour
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
                if(failure!=null){CombatPresentationSettingsVerifier.Fail(failure);break;}
                if(completed)continue;
                if(yielded is IEnumerator inner && !(yielded is CustomYieldInstruction)){stack.Push(inner);continue;}
                yield return yielded;
            }
        }
        finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();Destroy(gameObject);}
    }
    private static void Check(bool value,string label)=>CombatPresentationSettingsVerifier.Check(value,label);
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
        if(CombatPresentationSettingsVerifier.CaptureOnly)
        {
            yield return CombatPresentationComparisonCapture.Run(CombatPresentationSettingsVerifier.ComparisonOutput,Check);
            CombatPresentationSettingsVerifier.CompleteCycle();yield break;
        }
        var actor=PlayerContext.Instance.CurrentActor;var equipment=actor.Equipment;var melee=actor.GetComponent<MeleeRuntime>();
        // Product energy is created on the first confirmed elemental hit; the isolated fixture fills it before any hit.
        var energy=equipment.GetComponent<OverburstElementEnergy>() ?? equipment.gameObject.AddComponent<OverburstElementEnergy>();
        var parry=actor.GetComponent<PlayerParryController>();
        var preview=Object.FindFirstObjectByType<CombatMomentPresentation>();
        var oldWeapon=equipment.CurrentWeaponItem;var oldGem=equipment.EquippedElementGem;
        var edge=Object.FindFirstObjectByType<OverburstEdgeBlur>();bool oldEdge=OverburstEdgeBlur.IsEnabled;
        var setGem=typeof(PlayerEquipment).GetMethod("SetElementGem",Fields);
        var success=typeof(PlayerParryController).GetMethod("PlaySuccess",Fields);
        try
        {
            Check(preview!=null && CombatMomentPresentation.IsPrepared,"Product boot prepares effect prefab");
            Check(Object.FindObjectsByType<CombatMomentPresentation>(FindObjectsSortMode.None).Length==1,"One persistent presentation service");
            
            Check(CombatMomentPresentation.ActiveVisualCount==0,"Prepared visuals start inactive");
            yield return VerifySettingsUi();
            var weapon=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            Check(weapon!=null && equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common)),"Product greatsword equipped in isolated account");
            var gems=AssetDatabase.FindAssets("t:ElementGemItemData").Select(x=>AssetDatabase.LoadAssetAtPath<ElementGemItemData>(AssetDatabase.GUIDToAssetPath(x))).Where(x=>x!=null).ToArray();
            int baseRenderers=preview.GetComponentsInChildren<LineRenderer>(true).Length;
            foreach(var element in new[]{WeaponElement.Fire,WeaponElement.Ice,WeaponElement.Electric,WeaponElement.Dark,WeaponElement.Light})
            {
                var gem=gems.Where(x=>x.element==element).OrderBy(x=>x.fixedGrade).First();setGem.Invoke(equipment,new object[]{new ItemData(gem,1,gem.fixedGrade)});
                yield return Wait(.12f);Check(energy.Element==element,element+" product gem/energy context");
                foreach(float amount in element==WeaponElement.Light?new[]{0f,50f,100f,200f}:new[]{0f,50f,100f})
                {
                    melee.CancelCurrentAttackState();Amount(energy,amount);int before=CombatMomentPresentation.HeavyPulses;
                    Check(melee.TryStartHeavyAttack(actor.transform.forward)==WeaponActionResult.Accepted,element+"/"+amount+" actual heavy accepted");
                    float deadline=Time.unscaledTime+45f;bool captured=false;
                    while(melee.IsHeavyAttackInProgress && Time.unscaledTime<deadline)
                    {
                        if(!captured && CombatMomentPresentation.HeavyPulses>before){captured=true;
                            Check(preview.BladeGlow.enabled && preview.ImpactCore.enabled,element+" full impact/blade active after consumption");
                            var committed=Field<OverburstElementDischarge>(melee,"activeDischarge");
                            Check(committed!=null && CombatMomentPresentation.LastHeavyElement==element && Mathf.Abs(CombatMomentPresentation.LastHeavyEnergy-committed.Energy)<.02f,element+" consumed energy snapshot");
                            Check(Mathf.Abs(CombatMomentPresentation.LastHeavyOvercharge-committed.Overcharge)<.0001f,element+" overcharge snapshot preserved");
                            if(element==WeaponElement.Fire)
                            {
                                var menu=Object.FindFirstObjectByType<OverburstGameMenu>(FindObjectsInactive.Include);menu.Open();
                                var effect=Object.FindFirstObjectByType<CombatMomentPresentation>();float bladeAlpha=effect.BladeGlow.startColor.a,coreAlpha=effect.ImpactCore.startColor.a;
                                OverburstGameSettings.HeavyPresentationIntensity=.5f;
                                Check(Mathf.Abs(effect.BladeGlow.startColor.a-bladeAlpha*.5f)<.01f && Mathf.Abs(effect.ImpactCore.startColor.a-coreAlpha*.5f)<.01f,"Heavy strength scales blade and impact immediately during pause");
                                OverburstGameSettings.HeavyPresentationIntensity=1f;menu.Close();
                                yield return new WaitForEndOfFrame();Capture("FireFull");
                            }
                        }
                        yield return null;
                    }
                    Check(!melee.IsHeavyAttackInProgress,"Actual heavy completes");
                    Check(CombatMomentPresentation.HeavyPulses-before==(amount>=energy.BaseMaximum?1:0),element+"/"+amount+" exactly one full-energy pulse or none");
                    yield return Wait(.32f);Check(CombatMomentPresentation.ActiveVisualCount==0 && Mathf.Abs(CombatMomentPresentation.CurrentScreenGain)<.00001f,"Effect returns to neutral");
                }
            }
            var fire=gems.Where(x=>x.element==WeaponElement.Fire).OrderBy(x=>x.fixedGrade).First();setGem.Invoke(equipment,new object[]{new ItemData(fire,1,fire.fixedGrade)});yield return Wait(.12f);
            Amount(energy,100f);int dashBefore=CombatMomentPresentation.HeavyPulses;
            typeof(MeleeRuntime).GetField("requestedDodgeFollowUp",Fields).SetValue(melee,PlayerDodgeFollowUpKind.Heavy);
            Check(melee.TryStartHeavyAttack(actor.transform.forward)==WeaponActionResult.Accepted,"E definition accepted via pending-followup fixture");
            float dashDeadline=Time.unscaledTime+45f;while(melee.IsHeavyAttackInProgress && Time.unscaledTime<dashDeadline)yield return null;
            Check(!melee.IsHeavyAttackInProgress && CombatMomentPresentation.HeavyPulses==dashBefore+1,"E separate commit produces one full pulse");yield return Wait(.4f);
            Amount(energy,100f);int disabled=CombatMomentPresentation.HeavyPulses;CombatMomentPresentation.SetHeavyEnabled(false);
            Check(melee.TryStartHeavyAttack(actor.transform.forward)==WeaponActionResult.Accepted,"Disabled presentation keeps heavy action");
            while(melee.IsHeavyAttackInProgress)yield return null;
            Check(CombatMomentPresentation.HeavyPulses==disabled && energy.Amount==0f,"Heavy OFF suppresses new presentation and preserves consumption");CombatMomentPresentation.SetHeavyEnabled(true);
            Amount(energy,100f);int parryBefore=CombatMomentPresentation.ParryPulses,heavyBefore=CombatMomentPresentation.HeavyPulses;
            Check(melee.TryStartHeavyAttack(actor.transform.forward)==WeaponActionResult.Accepted,"Parried action accepted");
            int action=Field<int>(melee,"activeActionId");melee.NotifyHeavyParried(action);
            success.Invoke(parry,new object[]{actor.transform.position+Vector3.up+actor.transform.forward,1,false});
            Check(CombatMomentPresentation.ParryPulses==parryBefore+1 && CombatMomentPresentation.ActiveContactCount==1,"Existing parry feedback endpoint adds local pulse");
            success.Invoke(parry,new object[]{actor.transform.position+Vector3.up+actor.transform.forward,1,false});
            Check(CombatMomentPresentation.ParryPulses==parryBefore+1,"Same-window duplicate parry does not restart screen/blade");
            yield return new WaitForEndOfFrame();Capture("Parry");
            Overburst.DebugTools.DebugHub.OpenTab(Overburst.DebugTools.DebugTabs.Presentation);float pausedGain=CombatMomentPresentation.CurrentScreenGain;
            yield return Wait(.3f);Check(Mathf.Abs(CombatMomentPresentation.CurrentScreenGain-pausedGain)<.00001f,"F1 pause freezes moment clock");
            Overburst.DebugTools.DebugHub.Close();
            float parryDeadline=Time.unscaledTime+45f;while(melee.IsHeavyAttackInProgress && Time.unscaledTime<parryDeadline)yield return null;
            Check(!melee.IsHeavyAttackInProgress && CombatMomentPresentation.HeavyPulses==heavyBefore+1,"Parried two rotations discharge only one final pulse");
            Check(Mathf.Abs(energy.Amount-50f)<.02f,"Parried full-energy refund remains 50 percent");yield return Wait(.4f);
            var snapshot=new ElementGemAttackSnapshot(equipment);int count=CombatMomentPresentation.ParryPulses;
            CombatMomentPresentation.SetParryEnabled(false);CombatMomentPresentation.Parry(equipment,100001,0,2,snapshot,actor.transform.position+Vector3.up,actor.transform.forward);
            Check(CombatMomentPresentation.ParryPulses==count && CombatMomentPresentation.ActiveVisualCount==0,"Parry OFF suppresses only added visuals");CombatMomentPresentation.SetParryEnabled(true);
            CombatMomentPresentation.Parry(equipment,100002,0,2,snapshot,actor.transform.position+Vector3.up,actor.transform.forward);
            {
                var menu=Object.FindFirstObjectByType<OverburstGameMenu>(FindObjectsInactive.Include);menu.Open();
                var effect=Object.FindFirstObjectByType<CombatMomentPresentation>();float alpha=effect.BladeGlow.startColor.a;
                OverburstGameSettings.ParryPresentationIntensity=.5f;
                Check(Mathf.Abs(effect.BladeGlow.startColor.a-alpha*.5f)<.01f,"Parry strength scales current blade immediately during pause");
                typeof(CombatMomentPresentation).GetField("screenStarted",Fields).SetValue(effect,OverburstGameClock.UnscaledTime-.03f);
                Check(Mathf.Abs(CombatMomentPresentation.CurrentScreenGain+.025f)<.002f,"Parry screen strength is scaled independently");
                OverburstGameSettings.ParryPresentationIntensity=0;
                Check(OverburstGameSettings.ParryPresentationEnabled && CombatMomentPresentation.ActiveContactCount==0 && CombatMomentPresentation.CurrentScreenGain==0,"Zero strength ends parry lease while enabled choice remains saved");
                OverburstGameSettings.ParryPresentationIntensity=1;menu.Close();
            }
            CombatMomentPresentation.Parry(equipment,100012,0,2,snapshot,actor.transform.position+Vector3.up,actor.transform.forward);yield return null;
            edge.SetEnabled(false);int passes=OverburstEdgeBlurRendererFeature.RecordedPassCount;
            for(int i=0;i<2;i++)yield return new WaitForEndOfFrame();
            Check(OverburstEdgeBlurRendererFeature.RecordedPassCount>passes,"Moment works while edge blur is off");
            CombatMomentPresentation.SetParryEnabled(false);CombatMomentPresentation.SetHeavyEnabled(false);passes=OverburstEdgeBlurRendererFeature.RecordedPassCount;
            for(int i=0;i<3;i++)yield return new WaitForEndOfFrame();
            Check(OverburstEdgeBlurRendererFeature.RecordedPassCount==passes,"All screen effects off records zero passes");
            CombatMomentPresentation.SetParryEnabled(true);CombatMomentPresentation.SetHeavyEnabled(true);
            CombatMomentPresentation.Parry(equipment,100003,0,2,snapshot,actor.transform.position+Vector3.up,actor.transform.forward);
            string oldId=equipment.CurrentWeaponItem.runtimeInstanceId;var oldRoot=equipment.CurrentWeaponRoot;
            Check(equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common)),"Weapon swap accepted");
            yield return new WaitForEndOfFrame();
            Check(equipment.CurrentWeaponItem.runtimeInstanceId!=oldId && equipment.CurrentWeaponRoot!=oldRoot,"Actual weapon identity and root changed");
            Check(CombatMomentPresentation.ActiveVisualCount==0 && CombatMomentPresentation.CurrentScreenGain==0f,"Weapon swap cancels old lease");
            Check(preview.GetComponentsInChildren<LineRenderer>(true).Length==baseRenderers,"Repeated effects do not add renderers");
        }
        finally
        {
            Overburst.DebugTools.DebugHub.Close();melee.CancelCurrentAttackState();
            CombatMomentPresentation.SetParryEnabled(true);CombatMomentPresentation.SetHeavyEnabled(true);CombatMomentPresentation.SetScreenEnabled(true);CombatMomentPresentation.SetBladeEnabled(true);CombatMomentPresentation.SetLocalEnabled(true);
            if(edge!=null)edge.SetEnabled(oldEdge);if(oldWeapon!=null)equipment.EquipWeaponItem(oldWeapon);setGem.Invoke(equipment,new object[]{oldGem});
        }
        if(CombatPresentationSettingsVerifier.CurrentCycle==2 && CombatPresentationSettingsVerifier.CaptureComparisons)
            yield return CombatPresentationComparisonCapture.Run(CombatPresentationSettingsVerifier.ComparisonOutput,Check);
        PreserveNextPlaySettings();CombatPresentationSettingsVerifier.CompleteCycle();
    }
    private static void Capture(string label)
    {
        Texture2D texture=null;try{texture=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(CombatPresentationSettingsVerifier.CapturePath(label),texture.EncodeToPNG());}finally{if(texture!=null)Object.Destroy(texture);}
    }
    private static IEnumerator VerifySettingsUi()
    {
        var menu=Object.FindFirstObjectByType<OverburstGameMenu>(FindObjectsInactive.Include);
        var motion=Object.FindFirstObjectByType<OverburstMotionBlur>();
        Check(menu!=null && motion!=null,"Formal menu and motion blur boot in product");
        Check(Object.FindObjectsByType<OverburstMotionBlur>(FindObjectsSortMode.None).Length==1,"One persistent motion service");
        Check(!Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Any(x=>x.name=="Temporary_MomentToggles" || x.name=="Temporary_MotionBlurToggle" || x.name=="Temporary_EdgeBlurToggle"),"Temporary presentation buttons removed");
        Check(Object.FindObjectsByType<OverburstEdgeBlur>(FindObjectsSortMode.None).Length==1,"One persistent edge blur service");
        if(CombatPresentationSettingsVerifier.CurrentCycle==1)
        {
            Check(OverburstGameSettings.ParryPresentationEnabled && OverburstGameSettings.HeavyPresentationEnabled && !OverburstGameSettings.MotionBlurEnabled,"Old settings file defaults: moments ON / motion OFF");
            Check(OverburstGameSettings.ParryPresentationIntensity==1 && OverburstGameSettings.HeavyPresentationIntensity==1 && OverburstGameSettings.MotionBlurIntensity==.01f,"Old settings intensity defaults retained");
            Check(OverburstGameSettings.EdgeBlurEnabled && OverburstGameSettings.ExplorationEdgeBlurIntensity==.72f && OverburstGameSettings.CombatEdgeBlurIntensity==.42f,"Old settings retain exploration/combat edge defaults");
            Check(Mathf.Approximately(OverburstGameSettings.CameraShakeScale,.4f) && Mathf.Approximately(OverburstGameSettings.HitEffectScale,.6f),"Old settings preserve existing player choices");
        }
        else
        {
            Check(!OverburstGameSettings.ParryPresentationEnabled && OverburstGameSettings.HeavyPresentationEnabled && OverburstGameSettings.MotionBlurEnabled,"New Play restores three saved switches");
            Check(Mathf.Approximately(OverburstGameSettings.ParryPresentationIntensity,.35f) && Mathf.Approximately(OverburstGameSettings.HeavyPresentationIntensity,.65f) && Mathf.Approximately(motion.Settings.intensity.value,.2f),"New Play restores saved intensities and actual motion volume");
            Check(!OverburstGameSettings.EdgeBlurEnabled && Mathf.Approximately(OverburstGameSettings.ExplorationEdgeBlurIntensity,.55f) && Mathf.Approximately(OverburstGameSettings.CombatEdgeBlurIntensity,.25f),"New Play restores edge OFF and both saved mode intensities");
        }
        menu.Open();menu.OpenSettings();var panel=menu.settings;panel.tabs[2].isOn=true;
        yield return Wait(.25f);
        Check(panel.combatScroll!=null && panel.parryPresentation!=null && panel.heavyPresentation!=null && panel.motionBlur!=null,"Native combat settings controls are wired");
        Check(Time.timeScale==0,"Settings pause retained");
        foreach(var toggle in new[]{panel.parryPresentation,panel.heavyPresentation,panel.motionBlur,panel.edgeBlur})
        {
            bool before=toggle.isOn;ClickControl(toggle);Check(toggle.isOn!=before,"Visible settings toggle raycast applies "+toggle.transform.parent.parent.name);ClickControl(toggle);
        }
        panel.parryPresentation.isOn=false;panel.heavyPresentation.isOn=false;panel.motionBlur.isOn=false;
        panel.parryPresentationIntensity.value=.35f;panel.heavyPresentationIntensity.value=.65f;panel.motionBlurIntensity.value=.2f;
        panel.edgeBlur.isOn=false;panel.explorationEdgeBlurIntensity.value=.55f;panel.combatEdgeBlurIntensity.value=.25f;
        Check(!OverburstEdgeBlur.IsEnabled,"Changing edge intensities while OFF preserves disabled state");
        panel.edgeBlur.isOn=true;
        var mode=PlayerCombatModeController.GetOrCreate();bool wasCombat=mode.IsCombatModeActive;
        mode.ExitCombatMode(PlayerCombatModeReason.System);
        Check(Mathf.Approximately(OverburstEdgeBlur.CurrentStrength,.55f),"Exploration uses saved edge intensity immediately while paused");
        mode.EnterCombatMode(PlayerCombatModeReason.System);
        Check(Mathf.Approximately(OverburstEdgeBlur.CurrentStrength,.25f),"Combat uses its independent saved edge intensity immediately while paused");
        if(!wasCombat)mode.ExitCombatMode(PlayerCombatModeReason.System);
        Check(!CombatMomentPresentation.ParryEnabled && !CombatMomentPresentation.HeavyEnabled && motion.Settings.intensity.value==0,"Intensity changes while off preserve switches and actual zero motion");
        panel.motionBlur.isOn=true;
        Check(Mathf.Approximately(motion.Settings.intensity.value,.2f),"Motion volume updates immediately while menu is paused");
        panel.parryPresentation.isOn=true;panel.heavyPresentation.isOn=true;
        foreach(var slider in new[]{panel.parryPresentationIntensity,panel.heavyPresentationIntensity,panel.motionBlurIntensity,panel.explorationEdgeBlurIntensity,panel.combatEdgeBlurIntensity})
        {
            ClickSlider(slider,.5f);
            Check(Mathf.Abs(slider.normalizedValue-.5f)<.03f,"Visible settings slider accepts pointer "+slider.transform.parent.parent.name);
        }
        panel.parryPresentationIntensity.value=1.5f;panel.heavyPresentationIntensity.value=.5f;panel.motionBlurIntensity.value=.12f;
        panel.explorationEdgeBlurIntensity.value=.55f;panel.combatEdgeBlurIntensity.value=.25f;
        menu.CloseSettings();
        var saved=File.ReadAllText(OverburstGameSettings.FilePath);
        Check(saved.Contains("parryPresentationIntensity") && saved.Contains("heavyPresentationIntensity") && saved.Contains("motionBlurIntensity"),"Closing settings persists all presentation fields");
        Check(saved.Contains("edgeBlur") && saved.Contains("explorationEdgeBlurIntensity") && saved.Contains("combatEdgeBlurIntensity"),"Closing settings persists edge switch and both mode intensities");
        menu.OpenSettings();panel.tabs[2].isOn=true;yield return null;
        Check(panel.parryPresentationIntensity.value==1.5f && panel.heavyPresentationIntensity.value==.5f && Mathf.Approximately(panel.motionBlurIntensity.value,.12f),"Reopening settings restores selected values");
        Check(panel.edgeBlur.isOn && Mathf.Approximately(panel.explorationEdgeBlurIntensity.value,.55f) && Mathf.Approximately(panel.combatEdgeBlurIntensity.value,.25f),"Reopening settings restores both edge strengths");
        panel.combatScroll.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();Capture("FormalSettings");
        panel.resetButton.onClick.Invoke();
        Check(panel.parryPresentation.isOn && panel.heavyPresentation.isOn && !panel.motionBlur.isOn,"Combat reset restores ON / ON / OFF");
        Check(panel.parryPresentationIntensity.value==1 && panel.heavyPresentationIntensity.value==1 && Mathf.Approximately(panel.motionBlurIntensity.value,.01f),"Combat reset restores 100% / 100% / 1%");
        Check(panel.edgeBlur.isOn && Mathf.Approximately(panel.explorationEdgeBlurIntensity.value,.72f) && Mathf.Approximately(panel.combatEdgeBlurIntensity.value,.42f),"Combat reset restores edge ON / exploration 72% / combat 42%");
        Check(panel.parryPresentationIntensity.GetComponentInChildren<UnityEngine.UI.Text>().text=="100%","Reset refreshes visible percentage");
        OverburstGameSettings.ParryPresentationIntensity=float.NaN;OverburstGameSettings.HeavyPresentationIntensity=20;OverburstGameSettings.MotionBlurIntensity=-1;
        Check(OverburstGameSettings.ParryPresentationIntensity==1 && OverburstGameSettings.HeavyPresentationIntensity==2 && OverburstGameSettings.MotionBlurIntensity==0,"Saved numeric inputs normalize invalid and out-of-range values");
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
        OverburstGameSettings.ParryPresentationEnabled=false;OverburstGameSettings.HeavyPresentationEnabled=true;OverburstGameSettings.MotionBlurEnabled=true;
        OverburstGameSettings.ParryPresentationIntensity=.35f;OverburstGameSettings.HeavyPresentationIntensity=.65f;OverburstGameSettings.MotionBlurIntensity=.2f;
        OverburstGameSettings.EdgeBlurEnabled=false;OverburstGameSettings.ExplorationEdgeBlurIntensity=.55f;OverburstGameSettings.CombatEdgeBlurIntensity=.25f;
        OverburstGameSettings.SaveIfDirty();
    }

}
