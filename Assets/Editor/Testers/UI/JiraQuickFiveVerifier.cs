using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Small Jira regressions: native preview framing, preset preflight and controlled product Run hints.</summary>
[InitializeOnLoad]
public static class JiraQuickFiveVerifier
{
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<object> checks = new List<object>();
    static bool suppressing;
    static double suppressionDeadline;
    static Button suppressedButton;
    const string HintKey = "Overburst.JiraQuickFive.Hints";
    static JiraQuickFiveVerifier() { if (!string.IsNullOrEmpty(SessionState.GetString(HintKey,""))) EditorApplication.update += PendingHints; }
    public static void ArmRunHints(string output)
    {
        RequireIdle();
        if (!string.IsNullOrEmpty(SessionState.GetString(HintKey,""))) throw new InvalidOperationException("Owned hint verification already armed.");
        SessionState.SetString(HintKey,output);
        SessionState.SetString(HintKey+".expires",DateTime.UtcNow.AddMinutes(2).ToString("o"));
        EditorApplication.update -= PendingHints; EditorApplication.update += PendingHints;
    }
    static void PendingHints()
    {
        string directory=SessionState.GetString(HintKey,"");
        if (string.IsNullOrEmpty(directory)) { EditorApplication.update -= PendingHints; return; }
        try {
            if (DateTime.UtcNow > DateTime.Parse(SessionState.GetString(HintKey+".expires",""),null,System.Globalization.DateTimeStyles.RoundtripKind)) throw new TimeoutException("Owned Run hint fixture expired");
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating || PlayerContext.Instance?.CurrentActor == null) return;
            if (!string.Equals(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),Path.Combine(directory,"Account"),StringComparison.OrdinalIgnoreCase)) return;
            VerifyRunHints(directory);
        } catch(Exception e) {
            File.WriteAllText(Path.Combine(directory,"run-hints.json"),JsonConvert.SerializeObject(new{status="FAIL",error=e.ToString()},Formatting.Indented));
        }
        if (File.Exists(Path.Combine(directory,"run-hints.json"))) {
            EditorApplication.update -= PendingHints; SessionState.EraseString(HintKey); SessionState.EraseString(HintKey+".expires");
        }
    }


    public static string VerifyNative(string output)
    {
        RequireIdle();
        Directory.CreateDirectory(output);
        checks.Clear();
        var utility = new PreviewRenderUtility();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GroundIndicatorBuilder.PrefabPath);
        if (prefab == null) throw new InvalidOperationException("Saved indicator prefab required.");
        var instance = Object.Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        utility.AddSingleGO(instance);
        try
        {
            var indicator = instance.GetComponent<ProceduralGroundIndicator>();
            indicator.SetVisible(true);
            foreach (var shape in new[]{GroundIndicatorShape.Rectangle,GroundIndicatorShape.Circle,GroundIndicatorShape.Donut})
            foreach (var dimensions in shape == GroundIndicatorShape.Rectangle ? new[]{new Vector2(20,4),new Vector2(4,20),new Vector2(1,4)} : new[]{Vector2.one})
            foreach (var size in new[]{new Vector2(960,540),new Vector2(640,640),new Vector2(540,960)})
            foreach (bool top in new[]{true,false})
            {
                indicator.Configure(shape,4,.72f,80,dimensions.x,dimensions.y);
                indicator.SetFlameWidth(.12f); indicator.SetProgress(.72f);
                var rect = new Rect(0,0,size.x,size.y);
                utility.BeginPreview(rect,GUIStyle.none);
                GroundIndicatorWindow.FramePreviewCamera(utility.camera,indicator,top,rect);
                var renderer = indicator.Surface;
                var points = renderer.mesh.vertices.Select(v => utility.camera.WorldToViewportPoint(renderer.transform.TransformPoint(v))).ToArray();
                Check(points.Length > 0 && points.All(v => v.z > 0 && v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1),
                    "Saved surface fits " + shape + "/" + dimensions + "/" + size + "/" + top);
                utility.Render(true);
                var rendered = utility.EndPreview() as RenderTexture;
                if (shape == GroundIndicatorShape.Rectangle && dimensions.x == 20 && top)
                    SaveTexture(rendered,Path.Combine(output,"rectangle-" + (int)size.x + "x" + (int)size.y + ".png"));
            }
            var effects = (GameObject[])typeof(EdgeEffectsIconReviewBuilder).GetMethod("Effects",PrivateStatic).Invoke(null,null);
            var preflight = typeof(EdgeEffectsIconReviewBuilder).GetMethod("RequireCompletePresets",PrivateStatic);
            Check(effects.Length == 20,"Actual supplier presets present");
            foreach (int count in new[]{0,19,20})
            {
                bool accepted = true;
                try { preflight.Invoke(null,new object[]{effects.Take(count).ToArray()}); }
                catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { accepted = false; }
                Check(accepted == (count == 20),"Preset preflight " + count);
            }
            bool nullRejected = false;
            try { preflight.Invoke(null,new object[]{new GameObject[20]}); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { nullRejected = true; }
            Check(nullRejected,"Unloaded presets rejected");
            string json = JsonConvert.SerializeObject(new { status="PASS", checks },Formatting.Indented);
            File.WriteAllText(Path.Combine(output,"native.json"),json);
            return json;
        }
        finally { utility.Cleanup(); }
    }

    // Suppress only the owned temporary verifier's second launch callback.
    public static void VerifyHubMissingCallback(string output)
    {
        RequireIdle();
        if (suppressing) throw new InvalidOperationException("Owned hub fault fixture is already running.");
        OverburstToolHubVerifier.Verify(output);
        suppressionDeadline = EditorApplication.timeSinceStartup + 190;
        suppressing = true;
        EditorApplication.update += SuppressSecondLaunch;
        AssemblyReloadEvents.beforeAssemblyReload += EndSuppression;
    }

    static void SuppressSecondLaunch()
    {
        if (!OverburstToolHubVerifier.Running) { EndSuppression(); return; }
        if (EditorApplication.timeSinceStartup > suppressionDeadline)
        {
            typeof(OverburstToolHubVerifier).GetMethod("Finish",PrivateStatic).Invoke(null,new object[]{"Owned missing-callback fixture timeout"});
            EndSuppression(); return;
        }
        var type = typeof(OverburstToolHubVerifier);
        int index = (int)type.GetField("launchIndex",PrivateStatic).GetValue(null);
        int phase = (int)type.GetField("phase",PrivateStatic).GetValue(null);
        if (index == 1 && phase == 5 && suppressedButton == null)
        {
            suppressedButton = ((Button[])type.GetField("buttons",PrivateStatic).GetValue(null))[index];
            suppressedButton.SetEnabled(false);
        }
        else if (suppressedButton != null && (index > 1 || phase == 6)) suppressedButton.SetEnabled(true);
    }
    static void EndSuppression()
    {
        EditorApplication.update -= SuppressSecondLaunch;
        AssemblyReloadEvents.beforeAssemblyReload -= EndSuppression;
        suppressedButton?.SetEnabled(true); suppressedButton = null; suppressing = false;
    }

    public static string VerifyRunHints(string ownedPlayOutput)
    {
        if (!EditorApplication.isPlaying || !string.Equals(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
            Path.Combine(ownedPlayOutput,"Account"),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This regression requires its own isolated product Play.");
        var actor = PlayerContext.Instance?.CurrentActor;
        if (actor == null) throw new InvalidOperationException("Product player not ready.");
        var phase = typeof(WorldSessionState).GetField("<Phase>k__BackingField",PrivateStatic);
        object previousPhase = phase.GetValue(null);
        var root = new GameObject("Owned Jira event hint fixture");
        var director = root.AddComponent<MapDungeonEventDirector>(); director.enabled = false;
        var node = root.AddComponent<MapDungeonEventNode>(); node.enabled = false;
        root.transform.position = actor.transform.position;
        ((List<MapDungeonEventNode>)typeof(MapDungeonEventDirector).GetField("events",PrivateInstance).GetValue(director)).Add(node);
        var result = new List<object>();
        OverburstRunUi ui = null;
        try
        {
            // Controlled presentation fixture: preserve the live phase without notifying unrelated listeners.
            phase.SetValue(null,WorldPhase.Run);
            foreach (var kind in new[]{MapEventKind.Hunt,MapEventKind.Guard})
            foreach (var targetPhase in new[]{MapEventPhase.Failed,MapEventPhase.Complete})
            {
                typeof(MapDungeonEventNode).GetField("<Kind>k__BackingField",PrivateInstance).SetValue(node,kind);
                typeof(MapDungeonEventNode).GetField("<Phase>k__BackingField",PrivateInstance).SetValue(node,MapEventPhase.Active);
                if(targetPhase == MapEventPhase.Complete) typeof(MapDungeonEventNode).GetMethod("Complete",PrivateInstance).Invoke(node,null);
                else if(kind == MapEventKind.Guard) typeof(MapDungeonEventNode).GetMethod("HandleGuardDead",PrivateInstance).Invoke(node,new object[]{null,default(DamageInfo)});
                else InvokeZeroSpawnCompletion(node);
                typeof(MapDungeonEventDirector).GetField("nextHintCheck",PrivateInstance).SetValue(director,-1f);
                typeof(MapDungeonEventDirector).GetMethod("Update",PrivateInstance).Invoke(director,null);
                ui = (OverburstRunUi)typeof(MapDungeonEventDirector).GetField("ui",PrivateInstance).GetValue(director);
                Canvas.ForceUpdateCanvases();
                var title = (TMP_Text)typeof(OverburstRunUi).GetField("hintTitle",PrivateInstance).GetValue(ui);
                var body = (TMP_Text)typeof(OverburstRunUi).GetField("hintBody",PrivateInstance).GetValue(ui);
                string expectedTitle = kind == MapEventKind.Hunt ? "몬스터 토벌" : "오브젝트 수호";
                string expectedBody = targetPhase == MapEventPhase.Complete ? "완료 · 상자를 열어 카드 한 장을 선택하세요"
                    : kind == MapEventKind.Hunt ? "토벌 실패 · 이 상자는 열 수 없습니다" : "수호 실패 · 이 상자는 열 수 없습니다";
                bool pass = node.Phase == targetPhase && title.text == expectedTitle && body.text == expectedBody
                    && node.WantsInteractionPrompt == (targetPhase == MapEventPhase.Complete)
                    && (targetPhase != MapEventPhase.Failed || node.TryInteract(actor) == InteractionExecutionResult.Rejected);
                CaptureHint(ui,Path.Combine(ownedPlayOutput,"hint-"+kind+"-"+targetPhase+".png"));
                result.Add(new { kind=kind.ToString(), phase=node.Phase.ToString(), title=title.text, body=body.text, failedChestRejected=targetPhase != MapEventPhase.Failed || node.TryInteract(actor) == InteractionExecutionResult.Rejected, pass });
                if (!pass) throw new InvalidOperationException("Product Run hint mismatch: " + kind + "/" + targetPhase);
            }
            string json = JsonConvert.SerializeObject(new { status="PASS", controlledProductRunUiFixture=true, actualZeroSpawnCallback=true, naturalSpawnWave=false, cases=result },Formatting.Indented);
            File.WriteAllText(Path.Combine(ownedPlayOutput,"run-hints.json"),json);
            return json;
        }
        finally
        {
            phase.SetValue(null,previousPhase);
            if (ui != null) Object.DestroyImmediate(ui.gameObject);
            Object.DestroyImmediate(root);
        }
    }

    static void InvokeZeroSpawnCompletion(MapDungeonEventNode node)
    {
        bool Match(MethodInfo m) => m.Name.Contains("SpawnWave") && m.ReturnType == typeof(void) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(int);
        var direct=typeof(MapDungeonEventNode).GetMethods(PrivateInstance).Where(Match).ToArray();
        if(direct.Length==1){direct[0].Invoke(node,new object[]{0});return;}
        var nested=typeof(MapDungeonEventNode).GetNestedTypes(BindingFlags.NonPublic).SelectMany(t=>t.GetMethods(PrivateInstance|BindingFlags.Public)).Where(Match).ToArray();
        if(nested.Length!=1)throw new InvalidOperationException("Unique actual SpawnWave completion callback required.");
        var closure=Activator.CreateInstance(nested[0].DeclaringType,true);
        var owner=nested[0].DeclaringType.GetFields(PrivateInstance|BindingFlags.Public).Single(f=>f.FieldType == typeof(MapDungeonEventNode));
        owner.SetValue(closure,node);nested[0].Invoke(closure,new object[]{0});
    }
    static void CaptureHint(OverburstRunUi ui,string path)
    {
        var camera=Camera.main;if(camera==null)throw new InvalidOperationException("Product camera missing");
        var canvas=ui.GetComponent<Canvas>();if(canvas==null)canvas=ui.GetComponentInChildren<Canvas>();
        var mode=canvas.renderMode;var world=canvas.worldCamera;float distance=canvas.planeDistance;
        var previous=camera.targetTexture;RenderTexture target=null;
        try{
            target=new RenderTexture(1600,900,24);target.Create();camera.targetTexture=target;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            Canvas.ForceUpdateCanvases();
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=target});
            SaveTexture(target,path);
        }finally{canvas.renderMode=mode;canvas.worldCamera=world;canvas.planeDistance=distance;camera.targetTexture=previous;if(target!=null){target.Release();Object.DestroyImmediate(target);}}
    }

    static void Check(bool pass,string name)
    { checks.Add(new { name,pass }); if (!pass) throw new InvalidOperationException(name); }
    static void RequireIdle()
    { if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Idle Editor required."); }
    static void SaveTexture(RenderTexture source,string path)
    {
        if (source == null) throw new InvalidOperationException("Native preview texture missing.");
        var previous = RenderTexture.active; Texture2D pixels = null;
        try { RenderTexture.active=source; pixels=new Texture2D(source.width,source.height,TextureFormat.RGB24,false); pixels.ReadPixels(new Rect(0,0,source.width,source.height),0,0); pixels.Apply(); File.WriteAllBytes(path,pixels.EncodeToPNG()); }
        finally { RenderTexture.active=previous; if(pixels!=null)Object.DestroyImmediate(pixels); }
    }
}
