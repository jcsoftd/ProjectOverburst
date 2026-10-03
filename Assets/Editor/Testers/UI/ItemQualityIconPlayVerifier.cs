using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>격리 계정 Play에서 다섯 아이템 계열의 광택 수명과 UI 재사용을 검사한다.</summary>
[InitializeOnLoad]
public static class ItemQualityIconPlayVerifier
{
    public const int FixtureVersion = 7;
    private const string Key = "Overburst.ItemQualityIconPlayVerifier.";
    private static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath,
        "../../개인파일/코덱스산출/UI/20261003_StarQualityContinuous/Verification/Play"));
    private static string Account => Path.Combine(Output, "IsolatedAccount");
    private static IEnumerator work;
    private static GameObject root;
    private static int checks, frame;
    private static double deadline;
    private static readonly List<string> failures = new List<string>();
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");

    static ItemQualityIconPlayVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetBool(Key + "pending", false) && SessionState.GetBool(Key + "entered", false)) EditorApplication.delayCall += Recover;
        if (SessionState.GetBool(Key + "return", false)) EditorApplication.update += ReturnAccount;
    }

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return "DEFERRED: Editor is busy";
        if (SessionState.GetBool(Key + "pending", false) || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
            return "DEFERRED: another account operation is active";
        Directory.CreateDirectory(Output);
        SessionState.SetBool(Key + "pending", true);
        SessionState.SetBool(Key + "entered", false);
        SessionState.SetString(Key + "status", "RUNNING");
        SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManagerStartScene()));
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Account); }
        catch { SessionState.SetString(Key + "status", "FAIL: Play start rejected"); ScheduleReturn(); throw; }
        return "STARTED: isolated icon Play";
    }

    private static UnityEngine.Object EditorSceneManagerStartScene() => UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene;

    private static void Recover()
    {
        if (!SessionState.GetBool(Key + "pending", false)) return;
        if (EditorApplication.isPlaying && SessionState.GetBool(Key + "entered", false) && OwnAccount())
        {
            SessionState.SetString(Key + "status", "CANCELLED: domain reload during verification");
            EditorApplication.ExitPlaymode();
        }
        else if (!EditorApplication.isPlayingOrWillChangePlaymode) ScheduleReturn();
    }

    private static bool OwnAccount() => string.Equals(IsolatedSavePlayGuard.ActiveDirectory, Account, StringComparison.OrdinalIgnoreCase);

    private static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "pending", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode && OwnAccount())
        {
            SessionState.SetBool(Key + "entered", true);
            SessionState.SetBool(Key + "background", Application.runInBackground);
            Application.runInBackground = true;
            failures.Clear(); checks = 0; frame = -1; deadline = EditorApplication.timeSinceStartup + 90;
            work = Verify(); EditorApplication.update += Tick; Application.logMessageReceived += Log;
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            (work as IDisposable)?.Dispose(); work = null;
            if (root) Object.Destroy(root);
            root = null;
            if (SessionState.GetBool(Key + "entered", false)) Application.runInBackground = SessionState.GetBool(Key + "background", false);
            if (Status == "RUNNING") SessionState.SetString(Key + "status", "CANCELLED: Play interrupted");
        }
        else if (state == PlayModeStateChange.EnteredEditMode) ScheduleReturn();
    }

    private static void Log(string message, string stack, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            && (stack.Contains("ItemQualityIconEffect") || stack.Contains("ItemQualityIconPlayVerifier"))) failures.Add(message);
    }

    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!OwnAccount()) throw new InvalidOperationException("Isolated account ownership changed");
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Icon Play timed out");
            if (work.MoveNext()) return;
            Finish(failures.Count == 0 ? "PASS" : "FAIL");
        }
        catch (Exception error) { failures.Add(error.ToString()); Finish("FAIL"); }
    }

    private static void Check(bool pass, string message) { checks++; if (!pass) throw new InvalidOperationException(message); }
    private static void Finish(string status)
    {
        SessionState.SetString(Key + "status", status);
        File.WriteAllText(Path.Combine(Output, "play-result.txt"), status + ": " + checks + " checks; five star systems; continuous glow, repeated masterpiece sweep, unscaled clock, native frames, reuse and cleanup\n" + string.Join("\n", failures));
        EditorApplication.update -= Tick;
        if (OwnAccount()) EditorApplication.ExitPlaymode();
    }

    private static void ScheduleReturn()
    {
        SessionState.SetBool(Key + "pending", false);
        SessionState.SetBool(Key + "return", true);
        SessionState.SetString(Key + "returnDeadline", (EditorApplication.timeSinceStartup + 45).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        EditorApplication.update -= ReturnAccount; EditorApplication.update += ReturnAccount;
    }

    private static void ReturnAccount()
    {
        double.TryParse(SessionState.GetString(Key + "returnDeadline", "0"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double limit);
        if (EditorApplication.timeSinceStartup > limit)
        {
            EditorApplication.update -= ReturnAccount;
            File.WriteAllText(Path.Combine(Output, "return-result.txt"), "DEFERRED: Editor/account busy; return key retained, no callback left");
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
        string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
        if ((!string.IsNullOrEmpty(current) && !string.Equals(current, Account, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(prepared) && !string.Equals(prepared, Account, StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)) return;
        IsolatedSavePlayGuard.UseRealAccount();
        bool returned = !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""))
            && AssetDatabase.GetAssetPath(EditorSceneManagerStartScene()) == SessionState.GetString(Key + "startScene", "");
        EditorApplication.update -= ReturnAccount;
        if (!returned) { File.WriteAllText(Path.Combine(Output, "return-result.txt"), "FAIL: return conditions did not match; return key retained"); return; }
        foreach (string suffix in new[] { "return", "entered", "background", "pending" }) SessionState.EraseBool(Key + suffix);
        foreach (string suffix in new[] { "returnDeadline", "startScene" }) SessionState.EraseString(Key + suffix);
        File.WriteAllText(Path.Combine(Output, "return-result.txt"), "PASS: ready EditMode; actual account selected; environment, Guard and own pending/return keys clear; start scene preserved");
    }

    private static int ActiveEffects => (int)typeof(ItemQualityIconEffect).GetField("activeEffects", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
    private static float Clock => Shader.GetGlobalFloat("_OverburstItemQualityTime");
    private static ItemData Natural(BaseItemData source, ItemInscriptionQualityTier tier)
    {
        var random=UnityEngine.Random.state;
        try
        {
            for(int seed=701;seed<10701;seed++)
            {
                UnityEngine.Random.InitState(seed);
                var candidate=ItemInscriptionQualityVerifier.Fixture(source,ItemGrade.Mythic,seed);
                if(ItemInscriptionQuality.TryEvaluate(candidate,out var quality)&&quality.Tier==tier)return candidate;
            }
            throw new InvalidOperationException("Natural quality fixture unavailable: "+source.GetType().Name+"/"+tier);
        }
        finally{UnityEngine.Random.state=random;}
    }
    private static IEnumerator Verify()
    {
        while(PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching)yield return null;
        int stable=0;while(stable<6){yield return null;stable=Time.unscaledDeltaTime<.15f?stable+1:0;}
        int beforeActive=ActiveEffects;float originalScale=Time.timeScale;
        root=new GameObject("Continuous Item Quality Play Fixture",typeof(RectTransform),typeof(Canvas));
        root.hideFlags=HideFlags.HideAndDontSave;Object.DontDestroyOnLoad(root);
        root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
        var images=new List<Image>();var items=new List<ItemData>();var originals=new List<Material>();var animated=new List<Material>();
        try
        {
            foreach(var type in new[]{typeof(WeaponItemData),typeof(GearItemData),typeof(BagItemData),typeof(FlaskItemData),typeof(ElementGemItemData)})
            {
                var source=AssetDatabase.FindAssets("t:"+type.Name,new[]{"Assets/ProjectOverburst"}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p)
                    .Select(p=>AssetDatabase.LoadAssetAtPath<BaseItemData>(p)).First(a=>a&&a.icon&&(!(a is ElementGemItemData gem)||gem.fixedGrade==ItemGrade.Mythic&&ElementGemItemData.IsAllowed(gem.element,gem.fixedGrade)));
                var item=Natural(source,ItemInscriptionQualityTier.Masterpiece);
                var mask=new GameObject("Mask "+type.Name,typeof(RectTransform),typeof(Image),typeof(Mask));mask.transform.SetParent(root.transform,false);
                mask.GetComponent<RectTransform>().anchoredPosition=new Vector2(-10000,-10000);mask.GetComponent<Mask>().showMaskGraphic=false;
                var go=new GameObject(type.Name,typeof(RectTransform),typeof(Image));go.transform.SetParent(mask.transform,false);
                var image=go.GetComponent<Image>();image.sprite=source.icon;images.Add(image);items.Add(item);originals.Add(image.material);
            }
            for(int n=0;n<4;n++)yield return null;
            foreach(var image in images)
            {
                int i=images.IndexOf(image);ItemQualityIconEffect.Present(image,items[i]);animated.Add(image.material);
                Check(image.material.shader.name=="OVERBURST/UI/Item Quality Shine","Native effect shader");
                Check(Mathf.Approximately(image.material.GetFloat("_GlowStrength"),.085f),"Masterpiece persistent glow strength");
                Check(Mathf.Approximately(image.material.GetFloat("_ShineInterval"),6.5f),"Repeated masterpiece sweep interval");
                Check(image.material.GetFloat("_AnimateEffects")==1f,"GPU animation enabled");
                Check(image.materialForRendering.GetFloat("_Stencil")>0,"Mask stencil retained");
            }
            Check(ActiveEffects==beforeActive+5,"Five active effects share one clock");
            float first=animated[0].GetFloat("_AnimationStartTime");
            float until=Time.unscaledTime+.4f;while(Time.unscaledTime<until)yield return null;
            Canvas.ForceUpdateCanvases();
            for(int i=0;i<images.Count;i++)
            {
                Check(Clock-animated[i].GetFloat("_AnimationStartTime")>.1f,"Actual unscaled clock advances");
                float start=animated[i].GetFloat("_AnimationStartTime");
                for(int n=0;n<20;n++)ItemQualityIconEffect.Present(images[i],items[i]);
                Check(images[i].material==animated[i]&&images[i].material.GetFloat("_AnimationStartTime")==start,"Refresh preserves animation phase and material");
                Check(images[i].materialForRendering.GetFloat("_GlowStrength")==.085f&&images[i].materialForRendering.GetFloat("_AnimationStartTime")==start,"Stencil copy shares animated parameters");
            }
            until=Time.unscaledTime+1.3f;while(Time.unscaledTime<until)yield return null;
            for(int i=0;i<images.Count;i++)Check(images[i].material==animated[i],"Glow persists beyond initial sweep");
            Time.timeScale=0f;float clockBefore=Clock;float gameBefore=Time.time;
            until=Time.unscaledTime+.35f;while(Time.unscaledTime<until)yield return null;
            Canvas.ForceUpdateCanvases();Check(Clock>clockBefore+.2f&&Mathf.Approximately(Time.time,gameBefore),"Glow clock continues during pause");
            Time.timeScale=originalScale;
            until=first+6.9f;while(Time.unscaledTime<until)yield return null;Canvas.ForceUpdateCanvases();
            float sweepPhase=(Clock-first)%6.5f/1.15f;
            Check(sweepPhase>.1f&&sweepPhase<.95f,"Second masterpiece sweep occurs without reopening");
            for(int i=0;i<images.Count;i++)
            {
                ItemQualityIconEffect.Present(images[i],Natural(items[i].baseData,ItemInscriptionQualityTier.High));
                Check(images[i].material.GetFloat("_GlowStrength")==.035f&&images[i].material.GetFloat("_ShineInterval")==0f,"High: faint persistent glow, initial sweep only");
                ItemQualityIconEffect.Present(images[i],Natural(items[i].baseData,ItemInscriptionQualityTier.Finest));
                Check(images[i].material.GetFloat("_GlowStrength")==.06f&&images[i].material.GetFloat("_ShineInterval")==0f,"Finest: stronger persistent glow");
                ItemQualityIconEffect.Present(images[i],items[i]);animated[i]=images[i].material;
            }
            var capture=CapturePreview(items);
            try{while(capture.MoveNext())yield return null;}finally{(capture as IDisposable)?.Dispose();}
            for(int i=0;i<images.Count;i++)images[i].gameObject.SetActive(false);
            yield return null;
            Check(ActiveEffects==beforeActive,"Closing all icons removes their clock registrations");
            for(int i=0;i<images.Count;i++)
            {
                Check(!animated[i],"Closed icon releases its material");images[i].gameObject.SetActive(true);
                Check(images[i].material!=originals[i],"Reopened icon restores continuous effect");
                var material=images[i].material;images[i].enabled=false;ItemQualityIconEffect.Present(images[i],items[i]);
                Check(images[i].material==originals[i],"Disabled Image stops effect");images[i].enabled=true;ItemQualityIconEffect.Present(images[i],items[i]);
                Check(images[i].material!=originals[i],"Same-item enabled Image restarts effect");
                ItemQualityIconEffect.Present(images[i],items[i],Color.white,false);Check(images[i].material==originals[i],"Cooldown disables glow and sweep");
                ItemQualityIconEffect.Present(images[i],null,Color.clear);Check(images[i].material==originals[i]&&images[i].color==Color.clear,"Empty slot restores material and alpha");
                ItemInscriptionQualityVerifier.Assign(items[i],ItemInscriptionQualityVerifier.StarsForScore(20,0));ItemQualityIconEffect.Present(images[i],items[i]);
                Check(images[i].material==originals[i]&&Mathf.Approximately(images[i].color.r,.92f),"Lowest has dimming without glow");
                items[i].grade=ItemGrade.Common;ItemQualityIconEffect.Present(images[i],items[i]);Check(images[i].material==originals[i]&&images[i].color==Color.white,"Common removes quality effects");
            }
        }
        finally{Time.timeScale=originalScale;if(root)Object.Destroy(root);root=null;}
        yield return null;Check(ActiveEffects==beforeActive,"Owned roots, materials and shared clock registrations cleaned");
    }
    private static IEnumerator CapturePreview(List<ItemData> items)
    {
        string directory=Path.Combine(Output,"Frames");Directory.CreateDirectory(directory);
        GameObject captureRoot=null;Camera camera=null;RenderTexture target=null;Texture2D pixels=null;
        int samples=0;
        var oldTarget=RenderTexture.active;const int width=1100,height=560;
        var times=new List<float>();var ranges=new float[6,2];for(int i=0;i<6;i++){ranges[i,0]=float.MaxValue;ranges[i,1]=float.MinValue;}
        try
        {
            captureRoot=new GameObject("Continuous quality native capture");Object.DontDestroyOnLoad(captureRoot);
            var cameraGO=new GameObject("Preview camera",typeof(Camera));cameraGO.transform.SetParent(captureRoot.transform,false);
            camera=cameraGO.GetComponent<Camera>();camera.scene=captureRoot.scene;camera.enabled=false;camera.orthographic=true;camera.orthographicSize=height*.5f;
            camera.transform.position=new Vector3(0,0,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.065f,.075f,1);camera.cullingMask=1<<30;
            cameraGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing=false;
            target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
            pixels=new Texture2D(width,height,TextureFormat.RGBA32,false);
            var canvasGO=new GameObject("Native preview canvas",typeof(RectTransform),typeof(Canvas));canvasGO.transform.SetParent(captureRoot.transform,false);
            var canvas=canvasGO.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var tooltip=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstTooltip_Rpg11.prefab");
            var font=tooltip.GetComponentsInChildren<TMPro.TMP_Text>(true).First(t=>t.font).font;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/Slots/PF_OverburstItemSlot_Rpg11.prefab");
            Caption(canvas.transform,font,"각인 품질 · 지속 발광",30,18,1040,36,24);
            Caption(canvas.transform,font,"상급부터 은은한 밝기 변화 · 명품은 주기적인 광택",30,58,1040,28,16);
            for(int tier=0;tier<6;tier++)
            {
                var item=Natural(items[0].baseData,(ItemInscriptionQualityTier)tier);
                var slot=Object.Instantiate(prefab,canvas.transform,false);Top(slot.GetComponent<RectTransform>(),42+tier*176,104,84,84);slot.transform.localScale=Vector3.one*1.4f;
                slot.GetComponent<OverburstUIItemSlotView>().Present(item);
                Caption(canvas.transform,font,ItemInscriptionQuality.Label((ItemInscriptionQualityTier)tier),34+tier*176,238,150,28,19);
            }
            Caption(canvas.transform,font,"모든 별 품질 아이템에 공통 적용",30,294,1040,28,18);
            string[] labels={"무기","장비","가방","물약","보석"};
            for(int i=0;i<items.Count;i++)
            {
                var slot=Object.Instantiate(prefab,canvas.transform,false);Top(slot.GetComponent<RectTransform>(),72+i*200,342,84,84);slot.transform.localScale=Vector3.one*1.4f;
                slot.GetComponent<OverburstUIItemSlotView>().Present(items[i]);Caption(canvas.transform,font,labels[i]+" · 명품",66+i*200,477,170,28,18);
            }
            foreach(var t in captureRoot.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            Canvas.ForceUpdateCanvases();foreach(var t in canvasGO.GetComponentsInChildren<TMPro.TMP_Text>(true))t.ForceMeshUpdate(true,true);
            for(int n=0;n<4;n++)yield return null;
            float start=Time.unscaledTime,next=start;int frameIndex=0;
            while(Time.unscaledTime-start<7.8f)
            {
                if(Time.unscaledTime<next){yield return null;continue;}
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();RenderTexture.active=oldTarget;
                File.WriteAllBytes(Path.Combine(directory,"frame-"+frameIndex.ToString("D4")+".png"),pixels.EncodeToPNG());
                times.Add(Time.unscaledTime-start);frameIndex++;next=Time.unscaledTime+1f/15;
                // All six native slots share the same grade and artwork. Subtract the medium
                // control to remove common animated grade/background pixels. Sample only after
                // the first sweep and before the second, so variation proves persistent glow.
                if(times.Last()>=2f&&times.Last()<=6f)
                {
                    var means=new float[6];
                    for(int i=0;i<6;i++)
                    {
                        float sum=0;int count=0;
                        for(int x=54+i*176;x<144+i*176;x+=4)for(int y=height-204;y<height-114;y+=4){var c=pixels.GetPixel(x,y);sum+=c.r*.2126f+c.g*.7152f+c.b*.0722f;count++;}
                        means[i]=sum/count;
                    }
                    for(int i=0;i<6;i++){float relative=means[i]-means[2];ranges[i,0]=Mathf.Min(ranges[i,0],relative);ranges[i,1]=Mathf.Max(ranges[i,1],relative);}
                    samples++;
                }
                yield return null;
            }
            File.WriteAllText(Path.Combine(Output,"capture-result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{status="CAPTURED",frames=times.Count,times,qualityPixelRanges=ranges,samples,relativeToMedium=true,sampleWindow=new[]{2f,6f},width,height,native=true},Newtonsoft.Json.Formatting.Indented));
            Check(samples>=8,"Native control samples isolate persistent glow after initial sweep");
            for(int i=0;i<3;i++)Check(ranges[i,1]-ranges[i,0]<.0001f,"Lower qualities remain visually stable");
            for(int i=3;i<6;i++)Check(ranges[i,1]-ranges[i,0]>.0001f,"Rendered persistent glow changes pixels: "+i);
            Check(times.Count>=30&&times.Last()>7f,"Native animation frames cover repeated masterpiece sweep");
            File.WriteAllText(Path.Combine(Output,"capture-result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{status="PASS",frames=times.Count,times,qualityPixelRanges=ranges,samples,relativeToMedium=true,sampleWindow=new[]{2f,6f},width,height,native=true},Newtonsoft.Json.Formatting.Indented));
        }
        finally
        {
            RenderTexture.active=oldTarget;if(camera)camera.targetTexture=null;if(captureRoot)Object.Destroy(captureRoot);
            if(target){target.Release();Object.Destroy(target);}if(pixels)Object.Destroy(pixels);
        }
        yield return null;
    }
    private static void Top(RectTransform rect,float x,float y,float w,float h){rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);}
    private static void Caption(Transform parent,TMPro.TMP_FontAsset font,string text,float x,float y,float w,float h,float size)
    {
        var go=new GameObject("Preview caption",typeof(RectTransform),typeof(TMPro.TextMeshProUGUI));go.transform.SetParent(parent,false);
        var value=go.GetComponent<TMPro.TextMeshProUGUI>();value.font=font;value.text=text;value.fontSize=size;value.color=new Color(.88f,.82f,.7f);value.raycastTarget=false;Top(value.rectTransform,x,y,w,h);
    }
}
