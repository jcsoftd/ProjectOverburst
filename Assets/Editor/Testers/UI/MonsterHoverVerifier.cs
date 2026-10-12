using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class MonsterHoverVerifier
{
    const string Key = "Overburst.MonsterHoverVerifier";
    const string HudPath = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstHUD_Rpg11.prefab";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static MonsterHoverVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetBool(Key+".running",false)) EditorApplication.update += Tick;
    }

    public static string Native(string output)
    {
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Native fixture requires an idle Editor.");
        return Verify(output, false);
    }

    static string Verify(string output, bool playing)
    {
        checks.Clear();
        Directory.CreateDirectory(output);
        var oldContent = WorldSessionState.ContentScene;
        var oldActive = SceneManager.GetActiveScene();
        var saved = new Dictionary<FieldInfo, object>();
        foreach (var spec in new[] { (typeof(EnemyTargetHpHud), "instance"), (typeof(EnemyTargetHpHud), "pendingTarget"),
            (typeof(EnemyTargetHpHud), "hasPendingTarget"), (typeof(EnemyTargetHpHud), "pendingPreHitHp"),
            (typeof(WorldUiOverlayService), "instance"), (typeof(PlayerCombatModeController), "instance") })
        { var field = spec.Item1.GetField(spec.Item2, Private); saved[field] = field.GetValue(null); }
        Scene scene = playing ? SceneManager.CreateScene("MonsterHoverFixture") : EditorSceneManager.NewPreviewScene();
        GameObject root = new GameObject("MonsterHoverFixture") { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(root, scene);
        root.SetActive(false);
        var ranks = new List<EnemyRank>();
        try
        {
            typeof(WorldSessionState).GetMethod("SetContentScene", Private).Invoke(null, new object[] { scene });
            var mode = Child(root, "Mode").AddComponent<PlayerCombatModeController>();
            SetStatic(typeof(PlayerCombatModeController), "instance", mode);
            var camera = Child(root, "Camera").AddComponent<Camera>(); camera.scene=scene;
            camera.transform.position = new Vector3(0, 2, -8); camera.transform.LookAt(new Vector3(0, 1, 0));
            if (!playing) camera.pixelRect = new Rect(0,0,640,480);
            var canvas = Child(root, "Canvas", true).AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var barRoot = (RectTransform)canvas.transform;
            var overlay = Child(root, "Overlay").AddComponent<WorldUiOverlayService>();
            Set(overlay, "healthBarRoot", barRoot); Set(overlay, "eliteHealthBarPrewarm", 0);
            SetStatic(typeof(WorldUiOverlayService), "instance", overlay);
            var hudSource = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath).GetComponentInChildren<EnemyTargetHpHud>(true);
            var hud = Object.Instantiate(hudSource, root.transform); hud.gameObject.SetActive(true);
            var controller = Child(root, "Highlight").AddComponent<OverburstWorldHighlightController>();
            CombatHealth a = Enemy(root, "갈퀴턱 유충", new Vector3(0, 1, 0), ranks);
            CombatHealth b = Enemy(root, "암굴 추격수", new Vector3(0, 1, 3), ranks);
            var source = Child(root, "PlayerDamageSource"); source.AddComponent<CombatAffiliation>().Configure(CombatTeam.PlayerParty);
            root.SetActive(true);
            if (!playing)
            {
                foreach (var rank in ranks) { Call(rank.GetComponent<CombatHealth>(), "Awake"); Call(rank, "OnEnable"); }
                Call(hud.GetComponent<EnemyTargetHpSlotUI>(), "Awake"); Call(hud, "Awake"); Call(hud, "OnEnable");
            }
            Set(overlay, "targetCamera", camera);
            a.SetMaxHp(100, true); b.SetMaxHp(100, true);
            Set(mode, "currentState", PlayerCombatModeState.Exploration);
            var info = new DamageInfo(10, a.transform.position, source, triggersOnHitEffects:false, suppressDefaultHitVfx:true);
            EnemyTargetHpHud.ReportPlayerDamage(a, info, 10);
            Call(hud, "LateUpdate"); Check(hud.CurrentTarget == a && hud.LastHitTarget == a, "Existing last-hit HUD path");
            EnemyTargetHpHud.SetExplorationHoverTarget(b); Call(hud, "LateUpdate");
            Check(hud.CurrentTarget == b && hud.LastHitTarget == a, "Exploration hover takes priority without losing hit memory");
            Set(mode, "currentState", PlayerCombatModeState.Combat); Call(hud, "LateUpdate");
            Check(hud.CurrentTarget == a, "Combat hover preserves the last-hit HUD");
            Set(mode, "currentState", PlayerCombatModeState.Exploration); Call(hud, "LateUpdate");
            Check(hud.CurrentTarget == b, "Mode return restores the hovered target");
            EnemyTargetHpHud.SetExplorationHoverTarget(null); Call(hud, "LateUpdate");
            Check(hud.CurrentTarget == a, "Pointer exit returns to the hit target");
            int refreshes = hud.PresentationRefreshCount; Call(hud, "LateUpdate"); Call(hud, "LateUpdate");
            Check(hud.PresentationRefreshCount == refreshes, "Unchanged target does not bind or format repeatedly");
            EnemyTargetHpHud.SetExplorationHoverTarget(b); Call(hud, "LateUpdate");
            b.TakeDamage(new DamageInfo(1000,b.transform.position,source,triggersOnHitEffects:false,suppressDefaultHitVfx:true));
            Call(hud,"LateUpdate"); Check(hud.CurrentTarget == a, "Hovered death falls back to the living hit target");
            b.ResetHealth(); EnemyTargetHpHud.SetExplorationHoverTarget(b); Call(hud,"LateUpdate");
            a.ResetHealth(); EnemyTargetHpHud.SetExplorationHoverTarget(null); Call(hud,"LateUpdate");
            Check(hud.CurrentTarget == null && hud.LastHitTarget == null, "Pool reset discards old hit memory");
            hud.ShowTarget(a); EnemyTargetHpHud.SetExplorationHoverTarget(b); Call(hud,"LateUpdate");
            EnemyTargetHpHud.ForgetTarget(b); Call(hud,"LateUpdate");
            Check(hud.CurrentTarget == a, "ForgetTarget clears hover without clearing another hit");
            Vector2 projected;
            if (!overlay.TryProject(new Vector3(0,2,0),out projected))
                throw new InvalidOperationException("Fixture projection: "+JsonConvert.SerializeObject(new{screenWidth=Screen.width,screenHeight=Screen.height,cameraWidth=camera.pixelWidth,cameraHeight=camera.pixelHeight}));
            overlay.ShowHoverName("갈퀴턱 유충",new Vector3(0,2,0));
            var label = overlay.HoverName; Check(label != null && label.gameObject.activeSelf, "Authored name-only prefab loads and projects");
            Check(label.fontSize == 14 && label.color == new Color(.94f,.87f,.76f,1) && !label.raycastTarget, "Approved font size/color and click passthrough");
            Check(label.GetComponentsInChildren<UnityEngine.UI.Image>(true).Length == 0 && label.GetComponent<EnemyHpBarView>() == null, "No HP graphics or HP view in the hover label");
            overlay.ShowHoverName("암굴 추격수",new Vector3(0,2,0));
            Check(overlay.HoverName == label && label.text == "암굴 추격수", "One label is reused for another monster");
            if (!playing) CaptureName(output, label, canvas, camera);
            overlay.HideHoverName(); Check(!label.gameObject.activeSelf, "Hover exit hides the owned label");
            Check(overlay.AcquireHealthBar(a,EnemyHpBarTier.Small) == null && overlay.AcquireHealthBar(a,EnemyHpBarTier.Medium) == null, "Both modes retain general overhead HP exclusion");
            var ray = new Ray(new Vector3(0,1,-8),Vector3.forward);
            object selected = Call(controller,"ResolveEnemyHover",ray,float.MaxValue);
            Check(selected != null && (CombatHealth)selected.GetType().GetField("Health").GetValue(selected) == a, "Nearest model is selected through the current renderer ray path");
            object blocked = Call(controller,"ResolveEnemyHover",ray,1f);
            Check(blocked == null, "A closer NPC keeps priority over a farther monster");
            Check(Call(controller,"ResolveEnemyHover",new Ray(new Vector3(10,1,-8),Vector3.forward),float.MaxValue) == null, "Ray outside the model clears the candidate");
            Check(Resources.Load<GameObject>("UI/World/MonsterHpBars/PF_EnemyHpBar_Elite_Tier") != null,
                "Existing elite prefab remains available");
            string json = JsonConvert.SerializeObject(new { status="PASS",scope=playing?"PLAY_FIXTURE":"NATIVE_FIXTURE",checks,ordinaryHpAdded=false },Formatting.Indented);
            File.WriteAllText(Path.Combine(output,playing?"play-results.json":"native-results.json"),json);
            return json;
        }
        catch (Exception error)
        {
            string json=JsonConvert.SerializeObject(new {status="FAIL",checks,error=error.ToString()},Formatting.Indented);
            File.WriteAllText(Path.Combine(output,playing?"play-results.json":"native-results.json"),json);
            throw;
        }
        finally
        {
            if (!playing) foreach(var rank in ranks) if(rank != null) Call(rank,"OnDisable");
            Object.DestroyImmediate(root);
            if (playing) SceneManager.UnloadSceneAsync(scene); else EditorSceneManager.ClosePreviewScene(scene);
            typeof(WorldSessionState).GetMethod("SetContentScene",Private).Invoke(null,new object[]{oldContent});
            foreach(var pair in saved) pair.Key.SetValue(null,pair.Value);
            if (oldActive.IsValid() && oldActive.isLoaded) SceneManager.SetActiveScene(oldActive);
        }
    }

    static void CaptureName(string output,TextMeshProUGUI label,Canvas canvas,Camera camera)
    {
        RenderTexture target=null; Texture2D pixels=null; RenderTexture oldActive=RenderTexture.active;
        int oldMask=camera.cullingMask; var oldClear=camera.clearFlags; Color oldColor=camera.backgroundColor;
        RenderMode oldMode=canvas.renderMode; Camera oldCamera=canvas.worldCamera; Rect oldRect=camera.pixelRect;
        try
        {
            target=new RenderTexture(720,180,24); target.Create(); camera.targetTexture=target;camera.pixelRect=new Rect(0,0,720,180);
            camera.cullingMask=1<<31; camera.clearFlags=CameraClearFlags.SolidColor;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            canvas.gameObject.layer=31;label.gameObject.layer=31;label.rectTransform.anchoredPosition=Vector2.zero;
            label.text="갈퀴턱 유충";label.gameObject.SetActive(true);label.ForceMeshUpdate(true);Canvas.ForceUpdateCanvases();
            pixels=new Texture2D(720,180,TextureFormat.RGB24,false);
            foreach (var background in new[]{new Color(.09f,.11f,.10f,1),new Color(.63f,.60f,.50f,1)})
            {
                camera.backgroundColor=background;camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,720,180),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(output,background.r<.2f?"name-dark.png":"name-light.png"),pixels.EncodeToPNG());
            }
        }
        finally
        {
            RenderTexture.active=oldActive;camera.targetTexture=null;camera.pixelRect=oldRect;camera.cullingMask=oldMask;
            camera.clearFlags=oldClear;camera.backgroundColor=oldColor;canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;
            label.gameObject.layer=0;canvas.gameObject.layer=0;
            if (pixels!=null) Object.DestroyImmediate(pixels);
            if (target!=null) {target.Release();Object.DestroyImmediate(target);}
        }
    }

    public static void Play(string output)
    {
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Play fixture requires an idle Editor.");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(PreparedDirectory()))
            throw new InvalidOperationException("Another save session is prepared; preserve it.");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key+".output",Path.GetFullPath(output)); SessionState.SetBool(Key+".running",true);
        SessionState.SetBool(Key+".entered",false);
        SessionState.SetBool(Key+".return",false);
        SessionState.SetString(Key+".returnDeadline",(EditorApplication.timeSinceStartup+90).ToString(System.Globalization.CultureInfo.InvariantCulture));
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output,"Account")); }
        catch { StopWaiting(); throw; }
    }
    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key+".running",false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            if (SessionState.GetBool(Key+".entered",false)
                || !IsolatedSavePlayGuard.ActiveDirectory.StartsWith(SessionState.GetString(Key+".output",""),StringComparison.OrdinalIgnoreCase)) return;
            SessionState.SetBool(Key+".entered",true);
            try { Verify(SessionState.GetString(Key+".output",""),true); }
            catch(Exception error) { Debug.LogError("MonsterHover fixture: "+error.Message); }
            finally { SessionState.SetBool(Key+".return",true); EditorApplication.ExitPlaymode(); }
        }
        if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key+".entered",false))
            SessionState.SetString(Key+".returnDeadline",(EditorApplication.timeSinceStartup+30).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key+".running",false)) { EditorApplication.update -= Tick; return; }
        string output=SessionState.GetString(Key+".output","");
        double deadline;
        bool expired=double.TryParse(SessionState.GetString(Key+".returnDeadline","0"),System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,out deadline) && EditorApplication.timeSinceStartup > deadline;
        if (expired && !SessionState.GetBool(Key+".return",false))
        {
            File.WriteAllText(Path.Combine(output,"play-results.json"),"{\"status\":\"FAIL_START_TIMEOUT\"}");
            SessionState.SetBool(Key+".return",true);
        }
        if (!SessionState.GetBool(Key+".return",false)) return;
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (expired && IsolatedSavePlayGuard.ActiveDirectory.StartsWith(output,StringComparison.OrdinalIgnoreCase))
                EditorApplication.ExitPlaymode();
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string current=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"";
        string prepared=PreparedDirectory(),active=IsolatedSavePlayGuard.ActiveDirectory;
        bool foreign=(current.Length != 0 && !current.StartsWith(output,StringComparison.OrdinalIgnoreCase))
            || (prepared.Length != 0 && !prepared.StartsWith(output,StringComparison.OrdinalIgnoreCase))
            || (active.Length != 0 && !active.StartsWith(output,StringComparison.OrdinalIgnoreCase));
        if (foreign)
        {
            File.WriteAllText(Path.Combine(output,"returned.json"),"{\"status\":\"BLOCKED_FOREIGN_SAVE_SESSION\"}");
            StopWaiting();
            return;
        }
        IsolatedSavePlayGuard.UseRealAccount();
        StopWaiting();
        File.WriteAllText(Path.Combine(output,"returned.json"),JsonConvert.SerializeObject(new {
            playing=EditorApplication.isPlaying,transitioning=EditorApplication.isPlayingOrWillChangePlaymode,
            guardReady=!IsolatedSavePlayGuard.RequiresAccountChoice,saveEnvironmentEmpty=string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))},Formatting.Indented));
    }
    static void StopWaiting()
    {
        SessionState.SetBool(Key+".return",false); SessionState.SetBool(Key+".running",false);
        SessionState.EraseString(Key+".returnDeadline"); EditorApplication.update -= Tick;
    }
    static string PreparedDirectory() => (string)typeof(IsolatedSavePlayGuard).GetProperty("PreparedDirectory",Private).GetValue(null);
    static GameObject Child(GameObject parent,string name,bool rect=false)
    {var child=rect?new GameObject(name,typeof(RectTransform)):new GameObject(name);
     SceneManager.MoveGameObjectToScene(child,parent.scene);child.transform.SetParent(parent.transform,false);return child;}
    static CombatHealth Enemy(GameObject parent,string name,Vector3 position,List<EnemyRank> ranks)
    { var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;SceneManager.MoveGameObjectToScene(go,parent.scene);go.transform.SetParent(parent.transform,false);go.transform.position=position;
      go.AddComponent<CombatAffiliation>().Configure(CombatTeam.Enemy);var health=go.AddComponent<CombatHealth>();ranks.Add(go.AddComponent<EnemyRank>());return health; }
    static void Check(bool pass,string name) {if(!pass)throw new InvalidOperationException(name);checks.Add(name);}
    static void Set(object target,string field,object value) => target.GetType().GetField(field,Private).SetValue(target,value);
    static void SetStatic(Type type,string field,object value) => type.GetField(field,Private).SetValue(null,value);
    static object Call(object target,string method,params object[] args) => target.GetType().GetMethod(method,Private).Invoke(target,args);
}
