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
public static class CombatMomentPreviewVerifier
{
    private const string Key = "Overburst.CombatMomentPreviewVerifier.";
    private static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    private static int Cycle { get => SessionState.GetInt(Key + "cycle", 0); set => SessionState.SetInt(Key + "cycle", value); }
    private static string Output => SessionState.GetString(Key + "output", "");
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");
    private static double Deadline => double.Parse(SessionState.GetString(Key + "deadline", "0"), System.Globalization.CultureInfo.InvariantCulture);

    static CombatMomentPreviewVerifier()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += Log;
        EditorApplication.playModeStateChanged += StateChanged;
    }

    public static string VerifyAssets()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(CombatMomentPreviewBuilder.PrefabPath);
        Require(prefab!=null && prefab.GetComponent<CombatMomentPresentation>()!=null,"Native moment prefab exists");
        var lines=prefab.GetComponentsInChildren<LineRenderer>(true);
        Require(lines.Length==51 && lines.All(x=>!x.enabled && x.sharedMaterial!=null),"51 prepared line renderers disabled at rest");
        Require(prefab.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"Moment prefab Missing Script 0");
        var material=AssetDatabase.LoadAssetAtPath<Material>(CombatMomentPreviewBuilder.MaterialPath);
        Require(material!=null && material.shader.name=="OVERBURST/CombatMomentGlow" && material.GetFloat("_Intensity")==4f,"Native HDR material binding");
        Require(AssetDatabase.AssetPathToGUID(CombatMomentPreviewBuilder.PrefabPath).Length==32,"Prefab GUID valid");
        Require(material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader),"HDR glow shader supported and error-free");
        var hub=AssetDatabase.LoadAssetAtPath<GameObject>(DebugHubPrefabBuilder.PrefabPath);
        var view=hub!=null?hub.GetComponent<Overburst.DebugTools.DebugHubView>():null;
        Require(view!=null && view.ItemIds.All(id=>!id.StartsWith("presentation.moment.",StringComparison.Ordinal)),"Moment items removed from native debug panel");
        Require(hub.GetComponentsInChildren<Transform>(true).All(t=>!t.name.StartsWith("Row presentation.moment.",StringComparison.Ordinal)),"No baked moment rows remain");
        var ui=prefab.GetComponentInChildren<CombatMomentPreviewToggle>(true);
        Require(ui!=null && ui.ParryButton!=null && ui.HeavyButton!=null && ui.ParryCaption.font!=null && ui.HeavyCaption.font!=null,"Two standalone toggle references load");
        Require(ui.GetComponent<Canvas>().renderMode==RenderMode.ScreenSpaceOverlay,"Temporary buttons render outside the debug panel");
        Require(((RectTransform)ui.ParryButton.transform).anchoredPosition==new Vector2(16,-216) && ((RectTransform)ui.HeavyButton.transform).anchoredPosition==new Vector2(16,-264),"Temporary buttons follow existing preview controls");
        var renderer=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(OverburstEdgeBlurPreviewBuilder.RendererPath);
        Require(renderer.rendererFeatures.OfType<OverburstEdgeBlurRendererFeature>().Count()==1,"Existing combined feature only once");
        OverburstEdgeBlurPreviewVerifier.VerifyAssets();
        VerifyMomentPixels(renderer.rendererFeatures.OfType<OverburstEdgeBlurRendererFeature>().Single().BlurShader);
        return "PASS: native moment/standalone toggle references / Missing Script / HDR material / center-alpha-gain GPU checks / existing edge blur GPU regression";
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
    public static bool UiOnly => SessionState.GetBool(Key+"uiOnly",false);
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
        SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 300).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Cycle = 1;
        Phase = 1;
        SceneManager.SetActiveScene(boot);
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        return "Started two isolated toggle/render lifecycle cycles";
    }

    private static double idleStarted=-1;
    public static string BeginWhenIdle(string output,bool uiOnly=false)
    {
        if(Phase!=0)throw new InvalidOperationException("This verifier already running");
        SessionState.SetString(Key+"pendingOutput",IsolatedSavePlayGuard.ValidateDirectory(output));
        SessionState.SetString(Key+"pendingDeadline",(EditorApplication.timeSinceStartup+600).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
        SessionState.SetBool(Key+"uiOnly",uiOnly);SessionState.SetString(Key+"status","WAITING");idleStarted=-1;
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
            if(UiOnly){CombatMomentPreviewBuilder.BuildToggle();CombatMomentPreviewBuilder.RemoveDebugPanelItems();}
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
                SessionState.EraseBool(Key + "uiOnly");
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
            new GameObject("EdgeBlurVerificationRunner").AddComponent<CombatMomentPreviewVerificationRunner>();
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

public sealed class CombatMomentPreviewVerificationRunner : MonoBehaviour
{
    private static readonly System.Reflection.BindingFlags Fields=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    private IEnumerator Start()
    {
        var routine=Run();
        while(true){object yielded;try{if(!routine.MoveNext())break;yielded=routine.Current;}catch(Exception error){CombatMomentPreviewVerifier.Fail(error);break;}yield return yielded;}
        (routine as IDisposable)?.Dispose();Destroy(gameObject);
    }
    private static void Check(bool value,string label)=>CombatMomentPreviewVerifier.Check(value,label);
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
        if(CombatMomentPreviewVerifier.UiOnly){yield return VerifyToggleUi();CombatMomentPreviewVerifier.CompleteCycle();yield break;}
        var actor=PlayerContext.Instance.CurrentActor;var equipment=actor.Equipment;var melee=actor.GetComponent<MeleeRuntime>();
        // Product energy is created on the first confirmed elemental hit; the isolated fixture fills it before any hit.
        var energy=equipment.GetComponent<OverburstElementEnergy>() ?? equipment.gameObject.AddComponent<OverburstElementEnergy>();
        var parry=actor.GetComponent<PlayerParryController>();
        var preview=Object.FindFirstObjectByType<CombatMomentPresentation>();
        var oldWeapon=equipment.CurrentWeaponItem;var oldGem=equipment.EquippedElementGem;
        var edge=Object.FindFirstObjectByType<OverburstEdgeBlurPreview>();bool oldEdge=OverburstEdgeBlurPreview.IsEnabled;
        var setGem=typeof(PlayerEquipment).GetMethod("SetElementGem",Fields);
        var success=typeof(PlayerParryController).GetMethod("PlaySuccess",Fields);
        try
        {
            Check(preview!=null && CombatMomentPresentation.IsPrepared,"Product boot prepares effect prefab");
            Check(Object.FindObjectsByType<CombatMomentPresentation>(FindObjectsSortMode.None).Length==1,"One persistent presentation service");
            Check(CombatMomentPresentation.ParryEnabled && CombatMomentPresentation.HeavyEnabled,"Two preview toggles default on each Play");
            Check(CombatMomentPresentation.ActiveVisualCount==0,"Prepared visuals start inactive");
            yield return VerifyToggleUi();
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
                    float deadline=Time.unscaledTime+8f;bool captured=false;
                    while(melee.IsHeavyAttackInProgress && Time.unscaledTime<deadline)
                    {
                        if(!captured && CombatMomentPresentation.HeavyPulses>before){captured=true;
                            Check(preview.BladeGlow.enabled && preview.ImpactCore.enabled,element+" full impact/blade active after consumption");
                            var committed=Field<OverburstElementDischarge>(melee,"activeDischarge");
                            Check(committed!=null && CombatMomentPresentation.LastHeavyElement==element && Mathf.Abs(CombatMomentPresentation.LastHeavyEnergy-committed.Energy)<.02f,element+" consumed energy snapshot");
                            Check(Mathf.Abs(CombatMomentPresentation.LastHeavyOvercharge-committed.Overcharge)<.0001f,element+" overcharge snapshot preserved");
                            if(element==WeaponElement.Fire){yield return new WaitForEndOfFrame();Capture("FireFull");}
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
            float dashDeadline=Time.unscaledTime+8f;while(melee.IsHeavyAttackInProgress && Time.unscaledTime<dashDeadline)yield return null;
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
            float parryDeadline=Time.unscaledTime+10f;while(melee.IsHeavyAttackInProgress && Time.unscaledTime<parryDeadline)yield return null;
            Check(!melee.IsHeavyAttackInProgress && CombatMomentPresentation.HeavyPulses==heavyBefore+1,"Parried two rotations discharge only one final pulse");
            Check(Mathf.Abs(energy.Amount-50f)<.02f,"Parried full-energy refund remains 50 percent");yield return Wait(.4f);
            var snapshot=new ElementGemAttackSnapshot(equipment);int count=CombatMomentPresentation.ParryPulses;
            CombatMomentPresentation.SetParryEnabled(false);CombatMomentPresentation.Parry(equipment,100001,0,2,snapshot,actor.transform.position+Vector3.up,actor.transform.forward);
            Check(CombatMomentPresentation.ParryPulses==count && CombatMomentPresentation.ActiveVisualCount==0,"Parry OFF suppresses only added visuals");CombatMomentPresentation.SetParryEnabled(true);
            CombatMomentPresentation.Parry(equipment,100002,0,2,snapshot,actor.transform.position+Vector3.up,actor.transform.forward);yield return null;
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
        CombatMomentPreviewVerifier.CompleteCycle();
    }
    private static void Capture(string label)
    {
        Texture2D texture=null;try{texture=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(CombatMomentPreviewVerifier.CapturePath(label),texture.EncodeToPNG());}finally{if(texture!=null)Object.Destroy(texture);}
    }
    private static IEnumerator VerifyToggleUi()
    {
        var ui=Object.FindFirstObjectByType<CombatMomentPreviewToggle>();
        Check(ui!=null && Object.FindObjectsByType<CombatMomentPreviewToggle>(FindObjectsSortMode.None).Length==1,"One standalone temporary toggle overlay");
        Check(CombatMomentPresentation.ParryEnabled && CombatMomentPresentation.HeavyEnabled,"Both standalone toggles default on each Play");
        Check(!Overburst.DebugTools.DebugRegistry.AllItems.Any(x=>x.Id.StartsWith("presentation.moment.",StringComparison.Ordinal)),"No moment items registered in debug panel");
        Overburst.DebugTools.DebugHub.OpenTab(Overburst.DebugTools.DebugTabs.Presentation);yield return null;
        Check(!Object.FindFirstObjectByType<Overburst.DebugTools.DebugHubView>().GetComponentsInChildren<Transform>(true).Any(t=>t.name.StartsWith("Row presentation.moment.",StringComparison.Ordinal)),"Opened debug panel has no moment rows");
        Overburst.DebugTools.DebugHub.Close();yield return null;
        ClickToggle(ui.ParryButton);
        Check(!CombatMomentPresentation.ParryEnabled && CombatMomentPresentation.HeavyEnabled && ui.ParryCaption.text=="패링 연출: 꺼짐","Outside parry button applies independently and updates caption");
        ClickToggle(ui.ParryButton);ClickToggle(ui.HeavyButton);
        Check(CombatMomentPresentation.ParryEnabled && !CombatMomentPresentation.HeavyEnabled && ui.HeavyCaption.text=="완충 강공: 꺼짐","Outside heavy button applies independently and updates caption");
        ClickToggle(ui.HeavyButton);
        yield return new WaitForEndOfFrame();Capture("OutsideToggles");
        Check(ui.ParryCaption.text=="패링 연출: 켜짐" && ui.HeavyCaption.text=="완충 강공: 켜짐","Both outside captions restore on");
        ui.gameObject.SetActive(false);yield return null;ui.gameObject.SetActive(true);
        Check(CombatMomentPresentation.ParryEnabled && CombatMomentPresentation.HeavyEnabled,"Temporary UI enable cycle preserves selected effects");
    }
    private static void ClickToggle(UnityEngine.UI.Button button)
    {
        Canvas.ForceUpdateCanvases();
        var pointer=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
        {button=UnityEngine.EventSystems.PointerEventData.InputButton.Left,position=RectTransformUtility.WorldToScreenPoint(null,button.transform.position)};
        var hits=new List<UnityEngine.EventSystems.RaycastResult>();UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointer,hits);
        Check(hits.Count>0 && hits[0].gameObject.GetComponentInParent<UnityEngine.UI.Button>()==button,"Outside "+button.name+" receives visible UI raycast");
        UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject,pointer,UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
    }
}
