using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class LootAutoMoveProgressVerifier
{
    const string Key="Overburst.LootAutoMoveProgressVerifier.";
    const string Guard="Overburst.IsolatedSavePlayGuard.";
    const string Weapon="Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS024_TrainingIronGreatsword/GRS024_TrainingIronGreatsword.asset";
    static readonly List<string> checks=new List<string>();
    static IEnumerator work;
    static int lastFrame=-1;
    static double deadline;
    static string Output=>SessionState.GetString(Key+"output","");
    static bool Pending=>SessionState.GetBool(Key+"pending",false);
    public static string RunWhenIdle(string output)
    {
        if(Pending||SessionState.GetBool(Key+"waiting",false))throw new InvalidOperationException("Owned verification already pending.");
        string full=Path.GetFullPath(output);Directory.CreateDirectory(full);
        SessionState.SetString(Key+"requestedOutput",full);SessionState.SetString(Key+"waitDeadline",(EditorApplication.timeSinceStartup+300).ToString(CultureInfo.InvariantCulture));SessionState.SetBool(Key+"waiting",true);
        return "QUEUED owned verification; waits for idle Editor and returned account for at most 300 seconds.";
    }
    static LootAutoMoveProgressVerifier(){EditorApplication.playModeStateChanged+=State;EditorApplication.update+=Update;}
    static string Scenes()=>JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return new{s.path,s.isDirty,roots=s.GetRootGameObjects().Select(r=>r.GetInstanceID()).OrderBy(x=>x).ToArray()};}).ToArray());
    public static string Run(string output)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||BuildPipeline.isBuildingPlayer||Pending)throw new InvalidOperationException("Idle Editor required.");
        if(!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))||!string.IsNullOrEmpty(SessionState.GetString(Guard+"prepared","")))throw new InvalidOperationException("Another isolated account is active/prepared.");
        if(SceneManager.GetActiveScene().name!="PersistentScene")throw new InvalidOperationException("PersistentScene required; user scenes are not opened or saved by this verifier.");
        var full=Path.GetFullPath(output);string allowed=Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출"))+Path.DirectorySeparatorChar;if(!full.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Owned private output required.");Directory.CreateDirectory(full);
        SessionState.SetString(Key+"output",full);SessionState.SetString(Key+"scenes",Scenes());SessionState.SetString(Key+"startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key+"background",Application.runInBackground);SessionState.SetFloat(Key+"timeScale",Time.timeScale);
        SessionState.SetBool(Key+"pending",true);SessionState.SetBool(Key+"return",false);SessionState.SetBool(Key+"entered",false);SessionState.SetString(Key+"status","RUNNING");
        SessionState.SetString(Key+"bootDeadline",(EditorApplication.timeSinceStartup+180).ToString(CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(full,"play-results.json"),"{\"status\":\"RUNNING\"}");
        try{IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(full,"IsolatedAccount"));}catch{SessionState.SetBool(Key+"return",true);throw;}
        return "STARTED owned isolated loot approach Play";
    }
    static void State(PlayModeStateChange state)
    {
        if(!Pending)return;
        if(state==PlayModeStateChange.EnteredPlayMode){SessionState.SetBool(Key+"entered",true);checks.Clear();lastFrame=-1;deadline=EditorApplication.timeSinceStartup+300;Application.runInBackground=true;work=Verify();}
        if(state==PlayModeStateChange.ExitingPlayMode){(work as IDisposable)?.Dispose();work=null;Application.runInBackground=SessionState.GetBool(Key+"background",false);SessionState.SetBool(Key+"return",true);}
        if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key+"return",true);}
    }
    static void Update()
    {
        if(SessionState.GetBool(Key+"waiting",false))
        {
            bool timedOut=double.TryParse(SessionState.GetString(Key+"waitDeadline",""),NumberStyles.Float,CultureInfo.InvariantCulture,out double until)&&EditorApplication.timeSinceStartup>until;
            if(!timedOut&&(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||BuildPipeline.isBuildingPlayer||IsolatedSavePlayGuard.RequiresAccountChoice||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))||!string.IsNullOrEmpty(SessionState.GetString(Guard+"prepared",""))))return;
            string requested=SessionState.GetString(Key+"requestedOutput","");SessionState.EraseBool(Key+"waiting");SessionState.EraseString(Key+"requestedOutput");SessionState.EraseString(Key+"waitDeadline");
            try{if(timedOut)throw new TimeoutException("Shared Editor did not become idle; no Play or account was changed.");Run(requested);}
            catch(Exception e){File.WriteAllText(Path.Combine(requested,"wait-result.json"),JsonConvert.SerializeObject(new{status="DEFERRED",error=e.ToString()},Formatting.Indented));}
            return;
        }
        if(SessionState.GetBool(Key+"return",false)){ReturnAccount();return;}
        if(!Pending)return;
        if(!EditorApplication.isPlayingOrWillChangePlaymode){SessionState.SetBool(Key+"return",true);return;}
        if(!EditorApplication.isPlaying)return;
        if(work==null){
            if(!string.Equals(IsolatedSavePlayGuard.ActiveDirectory,Path.Combine(Output,"IsolatedAccount"),StringComparison.OrdinalIgnoreCase))return;
            if(SessionState.GetBool(Key+"entered",false))Finish(new InvalidOperationException("Owned loot approach verification interrupted by script reload; normal-account return scheduled."));
            else if(double.TryParse(SessionState.GetString(Key+"bootDeadline",""),NumberStyles.Float,CultureInfo.InvariantCulture,out double bootDeadline)&&EditorApplication.timeSinceStartup>bootDeadline)Finish(new TimeoutException("Owned loot Play did not initialize within 180 seconds."));
            return;
        }
        EditorApplication.QueuePlayerLoopUpdate();if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
        try{if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Owned loot Play exceeded 300 seconds.");if(!work.MoveNext())Finish(null);}catch(Exception e){Finish(e);}
    }
    static void Finish(Exception error)
    {
        (work as IDisposable)?.Dispose();work=null;
        SessionState.SetString(Key+"status",error==null?"PASS_SCOPED":"FAIL");
        File.WriteAllText(Path.Combine(Output,"play-results.json"),JsonConvert.SerializeObject(new{status=SessionState.GetString(Key+"status",""),checks,error=error?.ToString(),width=Screen.width,height=Screen.height,combatEffects="NOT_APPLICABLE",playerBuild="NOT_RUN",humanFeel="NOT_RUN"},Formatting.Indented));
        SessionState.SetBool(Key+"return",true);
        string active=IsolatedSavePlayGuard.ActiveDirectory;
        if(EditorApplication.isPlaying&&string.Equals(active,Path.Combine(Output,"IsolatedAccount"),StringComparison.OrdinalIgnoreCase))EditorApplication.ExitPlaymode();
    }
    static void ReturnAccount()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||BuildPipeline.isBuildingPlayer)return;
        var current=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"";var prepared=SessionState.GetString(Guard+"prepared","");
        bool Own(string p)=>string.IsNullOrEmpty(p)||p.StartsWith(Output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(!Own(current)||!Own(prepared)||!Own(IsolatedSavePlayGuard.ActiveDirectory))return;
        try{
            IsolatedSavePlayGuard.UseRealAccount();Application.runInBackground=SessionState.GetBool(Key+"background",false);Time.timeScale=SessionState.GetFloat(Key+"timeScale",1);
            bool startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)==SessionState.GetString(Key+"startScene","");
            bool scenes=Scenes()==SessionState.GetString(Key+"scenes","");
            SessionState.SetBool(Key+"pending",false);SessionState.SetBool(Key+"return",false);
            var returned=new{status=!IsolatedSavePlayGuard.RequiresAccountChoice&&startScene&&scenes?"PASS_SCOPED":"FAIL",blocked=IsolatedSavePlayGuard.RequiresAccountChoice,environment=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"",active=IsolatedSavePlayGuard.ActiveDirectory,prepared=SessionState.GetString(Guard+"prepared",""),expires=SessionState.GetString(Guard+"expires",""),pending=Pending,returnPending=SessionState.GetBool(Key+"return",false),startScenePreserved=startScene,userScenesPreserved=scenes,backgroundRestored=Application.runInBackground==SessionState.GetBool(Key+"background",false),timeScaleRestored=Time.timeScale==SessionState.GetFloat(Key+"timeScale",1)};
            File.WriteAllText(Path.Combine(Output,"play-return.json"),JsonConvert.SerializeObject(returned,Formatting.Indented));
            if(SessionState.GetString(Key+"status","")=="RUNNING")File.WriteAllText(Path.Combine(Output,"play-results.json"),"{\"status\":\"INTERRUPTED_OR_BOOT_CANCELLED\"}");
            foreach(string suffix in new[]{"output","scenes","startScene","status","bootDeadline"})SessionState.EraseString(Key+suffix);
            SessionState.EraseBool(Key+"pending");SessionState.EraseBool(Key+"return");SessionState.EraseBool(Key+"entered");SessionState.EraseBool(Key+"background");SessionState.EraseFloat(Key+"timeScale");
        }catch(Exception e){File.WriteAllText(Path.Combine(Output,"play-return.json"),JsonConvert.SerializeObject(new{status="FAIL",error=e.ToString()},Formatting.Indented));SessionState.SetBool(Key+"return",false);}
    }
    static void Check(bool yes,string name){if(!yes)throw new InvalidOperationException(name);checks.Add(name);}
    sealed class ObservedDriver : IWorldLootAutoMoveDriver
    {
        public PlayerLootAutoMoveDriver Driver;
        public int Completions;
        public WorldLootAutoMoveDriverResult Result;
        public bool TryBegin(WorldLootAutoMoveRequest request)
        {
            Completions = 0;
            return Driver.TryBegin(new WorldLootAutoMoveRequest(request.Target, request.PickupRadius,
                result => { Completions++; Result = result; request.Complete(result); }));
        }
        public void Cancel(WorldItemPickup target) => Driver.Cancel(target);
    }
    static IEnumerable<object> Steps(IEnumerator routine)
    {
        try { while (routine.MoveNext()) yield return routine.Current; }
        finally { (routine as IDisposable)?.Dispose(); }
    }
    static GameObject Cube(string name, Vector3 position, Vector3 size)
    {
        var value = GameObject.CreatePrimitive(PrimitiveType.Cube); value.name = "OwnedLootFixture " + name;
        value.transform.position = position; value.transform.localScale = size; return value;
    }
    static IEnumerator Verify()
    {
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout
            || PlayerContext.Instance?.CurrentActor == null) yield return null;
        Check(AccountBootstrap.Ready && AccountBootstrap.SaveDirectory.StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Owned isolated loot account");
        var actor = PlayerContext.Instance.CurrentActor; var movement = actor.Movement;
        var controller = actor.GetComponent<CharacterController>();
        var driver = PlayerContext.Instance.GetComponent<PlayerLootAutoMoveDriver>();
        var core = actor.PlayerKit.PickupInteractor;
        Check(controller != null && controller.enabled && driver != null && core != null, "Product CharacterController, movement, driver and pickup core installed");
        Check(core.CanContinueAutoMove(actor), "Product actor can begin loot approach");
        var observed = new ObservedDriver { Driver = driver }; core.BindAutoMoveDriver(observed);
        var definition = AssetDatabase.LoadAssetAtPath<WeaponItemData>(Weapon);
        Check(definition != null && WeaponContentPolicy.IsAllowedItemData(definition), "Saved production training weapon supplies owned pickup fixture");
        var objects = new List<GameObject>();
        Vector3 originalPosition = actor.transform.position; Quaternion originalRotation = actor.transform.rotation;
        float originalTime = Time.timeScale;
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var speed = typeof(PlayerMovement).GetField("runSpeed", flags); float originalSpeed = (float)speed.GetValue(movement);
        var mode = typeof(PlayerPickupInteractor).GetField("sessionMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); object originalMode = mode.GetValue(null);
        var previousKeyboard = Keyboard.current; var keyboard = InputSystem.AddDevice<Keyboard>("OwnedLootProgressKeyboard");
        var previousMouse = Mouse.current; var mouse = InputSystem.AddDevice<Mouse>("OwnedLootProgressMouse");
        WorldItemPickup target = null;
        var floor = Cube("Floor", new Vector3(1000, -1, 1000), new Vector3(80, 2, 150)); objects.Add(floor);
        var wall = Cube("Wall", new Vector3(1000, 2, 1005), new Vector3(50, 4, 1)); objects.Add(wall);
        Vector3 start = new Vector3(1000, .2f, 1000);
        float radius = core.PickupRadius;
        bool HasItem(string id) => PlayerContext.Instance.CurrentActorInventory.Items.Any(i => i?.runtimeInstanceId == id);
        void Teleport() { ActorTeleportUtility.TeleportSafely(actor.transform, start, Quaternion.identity); Physics.SyncTransforms(); }
        WorldItemPickup Spawn(float distance)
        {
            if (target != null) Object.Destroy(target.gameObject);
            var item = new ItemData(definition, 1, ItemGrade.Common);
            target = WorldItemDropFactory.CreateWorldPickup(item, start + Vector3.forward * distance,
                PlayerContext.Instance.CurrentActorInventory, actor.transform, null);
            objects.Add(target.gameObject); return target;
        }
        IEnumerator Ready()
        {
            double until = EditorApplication.timeSinceStartup + 8;
            while (target != null && !target.CanPickup && EditorApplication.timeSinceStartup < until) yield return null;
            Check(target != null && target.CanPickup, "Native drop landed and became pickable");
            core.NotifyPrimaryPointerReleased(); yield return null;
        }
        void Begin()
        {
            Check(core.RequestPickupByLabelPointerDown(target) == WorldLootPickupRequestResult.AutoMovePending
                && core.PendingAutoMovePickup == target && driver.HasActiveRequest && movement.IsLootAutoMoveActive, "Product label request owns pending approach");
            core.NotifyPrimaryPointerReleased();
        }
        IEnumerator Completed(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (driver.HasActiveRequest && Time.unscaledTime < until) yield return null;
            Check(!driver.HasActiveRequest && !movement.IsLootAutoMoveActive && core.PendingAutoMovePickup == null && observed.Completions == 1,
                "Approach completes once and clears driver, movement and pending state");
            yield return null; yield return null;
            Check(core.CurrentSnapshot.PendingAutoMovePickup == null, "Normal snapshot refresh clears pending presentation");
        }
        try
        {
            mode.SetValue(null, WorldLootInteractionMode.LootFocus); Time.timeScale = 1;
            wall.SetActive(false); Teleport(); yield return null; yield return null;
            Spawn(35); foreach (var step in Steps(Ready())) yield return step;
            string id = target.RuntimeItem.runtimeInstanceId; float began = Time.time; Begin();
            foreach (var step in Steps(Completed(15))) yield return step;
            Check(observed.Result == WorldLootAutoMoveDriverResult.Arrived && Time.time - began > 2 && HasItem(id), "Unobstructed approach lasting over two seconds arrives and acquires actual item");
            Teleport(); yield return null; Spawn(30); foreach (var step in Steps(Ready())) yield return step;
            id = target.RuntimeItem.runtimeInstanceId; began = Time.time; Begin();
            while (driver.HasActiveRequest && Time.time - began < 15)
            { target.transform.position += Vector3.forward * (2 * Time.deltaTime); yield return null; }
            foreach (var step in Steps(Completed(1))) yield return step;
            Check(observed.Result == WorldLootAutoMoveDriverResult.Arrived && HasItem(id), "Meaningful pursuit of a moving valid target remains active until actual acquisition");
            Teleport(); yield return null; Spawn(30); foreach (var step in Steps(Ready())) yield return step;
            id = target.RuntimeItem.runtimeInstanceId; began = Time.time; Begin();
            while (driver.HasActiveRequest && Time.time - began < 5)
            { target.transform.position += Vector3.forward * (movement.RunMoveSpeed * 2 * Time.deltaTime); yield return null; }
            foreach (var step in Steps(Completed(1))) yield return step;
            Check(observed.Result == WorldLootAutoMoveDriverResult.Failed && target.CanPickup && !HasItem(id), "Actor moving toward a faster retreating target cannot renew an increasing-distance request");
            for (int repeat = 0; repeat < 2; repeat++)
            {
                wall.SetActive(true); Teleport(); yield return null; yield return null;
                Spawn(12); foreach (var step in Steps(Ready())) yield return step;
                id = target.RuntimeItem.runtimeInstanceId; began = Time.time; Vector3 before = actor.transform.position; Begin();
                foreach (var step in Steps(Completed(7))) yield return step;
                Check(observed.Result == WorldLootAutoMoveDriverResult.Failed && core.CurrentSnapshot.LastRequestResult == WorldLootPickupRequestResult.AutoMoveFailed
                    && target.CanPickup && !HasItem(id) && actor.transform.position.z > before.z + 1 && actor.transform.position.z < wall.transform.position.z,
                    "Solid collider stops actual CharacterController; watchdog fails without item mutation " + repeat);
                Check(Time.time - began >= 2 && Time.time - began < 5, "Stall duration is bounded after last progress " + repeat);
            }
            wall.SetActive(false); Begin(); foreach (var step in Steps(Completed(8))) yield return step;
            Check(observed.Result == WorldLootAutoMoveDriverResult.Arrived && HasItem(id), "Same failed target can be retried and acquired after wall removal");
            wall.SetActive(true); Teleport(); yield return null; Spawn(12); foreach (var step in Steps(Ready())) yield return step;
            target.transform.position += Vector3.right * 8; began = Time.time; Begin();
            while (driver.HasActiveRequest && Time.time - began < 9)
            { wall.transform.position = new Vector3(1000 + Mathf.Sin(Time.time * 20) * .02f, 2, 1005); Physics.SyncTransforms(); yield return null; }
            foreach (var step in Steps(Completed(1))) yield return step;
            Check(observed.Result == WorldLootAutoMoveDriverResult.Failed && actor.transform.position.x > start.x + 2 && target.CanPickup,
                "Diagonal wall sliding may progress; small collider jitter cannot indefinitely renew the final stall");
            wall.transform.position = new Vector3(1000, 2, 1005); wall.SetActive(false);
            Teleport(); yield return null; Spawn(12); foreach (var step in Steps(Ready())) yield return step;
            speed.SetValue(movement, .12f); began = Time.time; Vector3 slowStart = actor.transform.position; Begin();
            while (Time.time - began < 2.7f && driver.HasActiveRequest) yield return null;
            Check(driver.HasActiveRequest && actor.transform.position.z > slowStart.z + .1f, "Slow valid physical movement accumulates meaningful progress without false timeout");
            driver.Cancel(target); yield return null; Check(observed.Completions == 1 && observed.Result == WorldLootAutoMoveDriverResult.Cancelled, "Slow request explicit cancellation remains once"); speed.SetValue(movement, originalSpeed);
            wall.SetActive(true); Teleport(); yield return null; Spawn(12); foreach (var step in Steps(Ready())) yield return step;
            Time.timeScale = .25f; began = Time.unscaledTime; Begin(); foreach (var step in Steps(Completed(20))) yield return step;
            Check(observed.Result == WorldLootAutoMoveDriverResult.Failed && Time.unscaledTime - began >= 7.9f, "Watchdog uses scaled gameplay time under slow motion"); Time.timeScale = 1;
            Teleport(); yield return null; Spawn(12); foreach (var step in Steps(Ready())) yield return step;
            // The motor follows a translating solid floor while the wall and pickup share that motion.
            target.transform.SetParent(floor.transform, true); wall.transform.SetParent(floor.transform, true);
            began = Time.time; float floorZ = floor.transform.position.z; Vector3 commonStart = actor.transform.position; Begin();
            while (driver.HasActiveRequest && Time.time - began < 7)
            { floor.transform.position += Vector3.forward * (3 * Time.deltaTime); Physics.SyncTransforms(); yield return null; }
            foreach (var step in Steps(Completed(1))) yield return step;
            Check(observed.Result == WorldLootAutoMoveDriverResult.Failed && actor.transform.position.z > commonStart.z + 2,
                "Actual moving-platform displacement cannot indefinitely renew a blocked approach");
            target.transform.SetParent(null, true); wall.transform.SetParent(null, true); floor.transform.position = new Vector3(1000, -1, floorZ); wall.transform.position = new Vector3(1000, 2, 1005);
            Teleport(); yield return null; Spawn(15); foreach (var step in Steps(Ready())) yield return step; Begin(); began = Time.time;
            while (driver.HasActiveRequest && Time.time - began < 6)
            { target.transform.position -= Vector3.forward * (Time.deltaTime * .6f); yield return null; }
            foreach (var step in Steps(Completed(1))) yield return step;
            Check(observed.Result == WorldLootAutoMoveDriverResult.Failed && target.CanPickup, "Target-only approach cannot mask a solid obstruction");
            wall.SetActive(false);
            foreach (var key in new[] { UnityEngine.InputSystem.Key.W, UnityEngine.InputSystem.Key.LeftShift })
            {
                Teleport(); yield return null; Spawn(30); foreach (var step in Steps(Ready())) yield return step; Begin();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null; yield return null;
                Check(!driver.HasActiveRequest && observed.Completions == 1 && observed.Result == WorldLootAutoMoveDriverResult.Cancelled, "Actual manual input cancels approach " + key);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                float until = Time.time + 1; while (Time.time < until) yield return null;
            }
            Teleport(); yield return null; Spawn(30); foreach (var step in Steps(Ready())) yield return step; Begin();
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left)); yield return null; yield return null;
            Check(!driver.HasActiveRequest && observed.Completions == 1, "Unsuppressed actual attack input cancels approach");
            InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null; float combatUntil = Time.time + 2; while (Time.time < combatUntil) yield return null;
            Teleport(); yield return null; Spawn(30); foreach (var step in Steps(Ready())) yield return step; Begin();
            var menu = OverburstHudMenu.Instance; menu.Open(); yield return null; yield return null;
            Check(!driver.HasActiveRequest && core.PendingAutoMovePickup == null && observed.Completions == 1, "Product menu input ownership cancels and releases pending approach"); menu.CloseImmediate(); yield return null;
            Teleport(); Spawn(30); foreach (var step in Steps(Ready())) yield return step; Begin();
            target.gameObject.SetActive(false); yield return null; yield return null;
            Check(!driver.HasActiveRequest && core.PendingAutoMovePickup == null && observed.Completions == 1, "Invalid target completes once and clears pending");
            Spawn(30); foreach (var step in Steps(Ready())) yield return step; Begin();
            movement.enabled = false; yield return null; yield return null;
            Check(!driver.HasActiveRequest && observed.Completions == 1, "Disabled actor movement cancels request"); movement.enabled = true; yield return null;
            Spawn(30); foreach (var step in Steps(Ready())) yield return step; Begin();
            var oldScene = SceneManager.GetActiveScene(); var scene = SceneManager.CreateScene("OwnedLootCancellationFixture");
            SceneManager.SetActiveScene(scene); yield return null;
            Check(!driver.HasActiveRequest && observed.Completions == 1 && core.PendingAutoMovePickup == null, "Actual active scene change cancels once");
            SceneManager.SetActiveScene(oldScene); var unloading = SceneManager.UnloadSceneAsync(scene); while (!unloading.isDone) yield return null;
            // Arrival wins over an expired progress timer within the same update.
            Teleport(); Spawn(30); foreach (var step in Steps(Ready())) yield return step; id = target.RuntimeItem.runtimeInstanceId; Begin();
            typeof(PlayerLootAutoMoveDriver).GetField("lastProgressTime", flags).SetValue(driver, Time.time - 3);
            target.transform.position = actor.transform.position + Vector3.forward * (radius * .5f); yield return null; yield return null;
            Check(observed.Result == WorldLootAutoMoveDriverResult.Arrived && observed.Completions == 1 && HasItem(id), "Arrival takes precedence over expired watchdog");
            Spawn(30); foreach (var step in Steps(Ready())) yield return step;
            var replacement = WorldItemDropFactory.CreateWorldPickup(new ItemData(definition, 1, ItemGrade.Common),
                actor.transform.position + Vector3.right * 30, PlayerContext.Instance.CurrentActorInventory, actor.transform, null);
            objects.Add(replacement.gameObject); while (!replacement.CanPickup) yield return null;
            int firstCompletion = 0, replacementCompletion = 0; bool restarted = false; WorldLootAutoMoveDriverResult firstResult = default;
            Check(driver.TryBegin(new WorldLootAutoMoveRequest(target, radius, result =>
            {
                firstCompletion++; firstResult = result;
                restarted = driver.TryBegin(new WorldLootAutoMoveRequest(replacement, radius, next => replacementCompletion++));
            })), "Callback re-entry fixture starts through product driver");
            float restartTime = Time.time;
            typeof(PlayerLootAutoMoveDriver).GetField("lastProgressTime", flags).SetValue(driver, restartTime - 3);
            yield return null; yield return null;
            Check(firstCompletion == 1 && firstResult == WorldLootAutoMoveDriverResult.Failed && restarted && driver.ActiveTarget == replacement
                && driver.HasActiveRequest && (float)typeof(PlayerLootAutoMoveDriver).GetField("lastProgressTime", flags).GetValue(driver) >= restartTime,
                "Timeout callback can start a new request with independent timer and destination");
            driver.Cancel(replacement); yield return null;
            Check(firstCompletion == 1 && replacementCompletion == 1 && !driver.HasActiveRequest, "Re-entered request cancels once without a stale completion");
        }
        finally
        {
            if (driver.HasActiveRequest) driver.Cancel(driver.ActiveTarget);
            movement.enabled = true; speed.SetValue(movement, originalSpeed); mode.SetValue(null, originalMode);
            core.NotifyPrimaryPointerReleased(); core.BindAutoMoveDriver(driver); OverburstHudMenu.Instance?.CloseImmediate();
            Time.timeScale = originalTime; ActorTeleportUtility.TeleportSafely(actor.transform, originalPosition, originalRotation);
            foreach (var value in objects) if (value != null) Object.Destroy(value);
            if (keyboard.added) InputSystem.RemoveDevice(keyboard); if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            if (mouse.added) InputSystem.RemoveDevice(mouse); if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
        }
        yield return null; yield return null;
        Check(!driver.HasActiveRequest && !movement.IsLootAutoMoveActive && core.PendingAutoMovePickup == null
            && !GameplayInputBlocker.IsGameplayInputBlocked && Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).All(t => !t.name.StartsWith("OwnedLootFixture")), "Owned physical fixtures and pending input state removed");
    }
}
