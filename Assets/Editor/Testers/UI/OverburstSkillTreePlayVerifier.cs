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
public static class OverburstSkillTreePlayVerifier
{
    const string Key="Overburst.SkillTreePlayVerifier.";
    const string Guard="Overburst.IsolatedSavePlayGuard.";
    const string Weapon="Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS024_TrainingIronGreatsword/GRS024_TrainingIronGreatsword.asset";
    static readonly List<string> checks=new List<string>();
    static IEnumerator work;
    static int lastFrame=-1;
    static double deadline;
    static string Output=>SessionState.GetString(Key+"output","");
    static bool Pending=>SessionState.GetBool(Key+"pending",false);
    static OverburstSkillTreePlayVerifier(){EditorApplication.playModeStateChanged+=State;EditorApplication.update+=Update;}
    static string Scenes()=>JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return new{s.path,s.isDirty,roots=s.GetRootGameObjects().Select(r=>r.GetInstanceID()).OrderBy(x=>x).ToArray()};}).ToArray());
    public static string Run(string output)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||Pending)throw new InvalidOperationException("Idle Editor required.");
        if(!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))||!string.IsNullOrEmpty(SessionState.GetString(Guard+"prepared","")))throw new InvalidOperationException("Another isolated account is active/prepared.");
        if(SceneManager.GetActiveScene().name!="PersistentScene")throw new InvalidOperationException("PersistentScene required; user scenes are not opened or saved by this verifier.");
        var full=Path.GetFullPath(output);IsolatedSavePlayGuard.ValidateDirectory(Path.Combine(full,"IsolatedAccount"));Directory.CreateDirectory(full);
        SessionState.SetString(Key+"output",full);SessionState.SetString(Key+"scenes",Scenes());SessionState.SetString(Key+"startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key+"background",Application.runInBackground);SessionState.SetFloat(Key+"timeScale",Time.timeScale);
        SessionState.SetBool(Key+"pending",true);SessionState.SetBool(Key+"return",false);SessionState.SetBool(Key+"entered",false);SessionState.SetString(Key+"status","RUNNING");
        SessionState.SetString(Key+"bootDeadline",(EditorApplication.timeSinceStartup+180).ToString(CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(full,"play-results.json"),"{\"status\":\"RUNNING\"}");
        try{IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(full,"IsolatedAccount"));}catch{SessionState.SetBool(Key+"return",true);throw;}
        return "STARTED owned isolated skill-tree UI Play";
    }
    static void State(PlayModeStateChange state)
    {
        if(!Pending)return;
        if(state==PlayModeStateChange.EnteredPlayMode){SessionState.SetBool(Key+"entered",true);checks.Clear();lastFrame=-1;deadline=EditorApplication.timeSinceStartup+180;Application.runInBackground=true;work=Verify();}
        if(state==PlayModeStateChange.ExitingPlayMode){(work as IDisposable)?.Dispose();work=null;Application.runInBackground=SessionState.GetBool(Key+"background",false);SessionState.SetBool(Key+"return",true);}
        if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key+"return",true);}
    }
    static void Update()
    {
        if(SessionState.GetBool(Key+"return",false)){ReturnAccount();return;}
        if(!Pending)return;
        if(!EditorApplication.isPlayingOrWillChangePlaymode){SessionState.SetBool(Key+"return",true);return;}
        if(!EditorApplication.isPlaying)return;
        if(work==null){
            if(!string.Equals(IsolatedSavePlayGuard.ActiveDirectory,Path.Combine(Output,"IsolatedAccount"),StringComparison.OrdinalIgnoreCase))return;
            if(SessionState.GetBool(Key+"entered",false))Finish(new InvalidOperationException("Owned UI verification interrupted by script reload; normal-account return scheduled."));
            else if(double.TryParse(SessionState.GetString(Key+"bootDeadline",""),NumberStyles.Float,CultureInfo.InvariantCulture,out double bootDeadline)&&EditorApplication.timeSinceStartup>bootDeadline)Finish(new TimeoutException("Owned UI Play did not initialize within 180 seconds."));
            return;
        }
        EditorApplication.QueuePlayerLoopUpdate();if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
        try{if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Owned UI Play exceeded 180 seconds.");if(!work.MoveNext())Finish(null);}catch(Exception e){Finish(e);}
    }
    static void Finish(Exception error)
    {
        (work as IDisposable)?.Dispose();work=null;
        SessionState.SetString(Key+"status",error==null?"PASS_SCOPED":"FAIL");
        File.WriteAllText(Path.Combine(Output,"play-results.json"),JsonConvert.SerializeObject(new{status=SessionState.GetString(Key+"status",""),checks,error=error?.ToString(),width=Screen.width,height=Screen.height,combatEffects="ACCOUNT_STAT_MODIFIERS",playerBuild="NOT_RUN",humanFeel="NOT_RUN"},Formatting.Indented));
        SessionState.SetBool(Key+"return",true);
        string active=IsolatedSavePlayGuard.ActiveDirectory;
        if(EditorApplication.isPlaying&&string.Equals(active,Path.Combine(Output,"IsolatedAccount"),StringComparison.OrdinalIgnoreCase))EditorApplication.ExitPlaymode();
    }
    static void ReturnAccount()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
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
    static Vector2 Point(RectTransform r)=>RectTransformUtility.WorldToScreenPoint(r.GetComponentInParent<Canvas>().rootCanvas.worldCamera,r.position);
    static PointerEventData Pointer(RectTransform r)=>new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=Point(r)};
    static void Click(UnityEngine.UI.Button b){Check(b.interactable,"Native button available: "+b.name);var e=Pointer((RectTransform)b.transform);ExecuteEvents.Execute(b.gameObject,e,ExecuteEvents.pointerClickHandler);}
    static GameObject Hit(RectTransform r){var results=new List<RaycastResult>();EventSystem.current.RaycastAll(Pointer(r),results);return results.Count==0?null:results[0].gameObject;}
    static string Stats(PlayerActorRuntime p)=>JsonConvert.SerializeObject(new{p.Equipment.CurrentWeaponStats,maxHp=p.Health.MaxHp,speed=p.Movement.BaseMoveSpeed,items=PlayerContext.Instance.CurrentActorInventory.Items.Select(i=>i?.runtimeInstanceId).ToArray(),gem=p.Equipment.EquippedElementGem?.runtimeInstanceId});
    static IEnumerable Steps(IEnumerator routine)
    {
        try { while (routine.MoveNext()) yield return routine.Current; }
        finally { (routine as IDisposable)?.Dispose(); }
    }
    static IEnumerator OpenFromMenu()
    {
        var menu = OverburstHudMenu.Instance;
        Check(menu != null, "Vertical HUD menu installed");
        Click(menu.trigger); yield return null;
        while (menu.popup.alpha < .999f) yield return null;
        var index = Array.FindIndex(menu.entries, e => e.destination == OverburstHudMenu.Destination.SkillTree);
        Check(index >= 0 && Hit((RectTransform)menu.Rows[index].transform) == menu.Rows[index].gameObject, "Actual HUD skill-tree row raycast");
        Click(menu.Rows[index].button); yield return null;
        Check(OverburstSkillTreeUI.IsWindowOpen && !menu.IsOpen, "HUD row transfers input ownership to skill tree");
    }
    static IEnumerator Verify()
    {
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout || PlayerContext.Instance?.CurrentActor == null) yield return null;
        Check(AccountBootstrap.Ready && AccountBootstrap.SaveDirectory.StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Owned isolated account boot");
        var ui = OverburstSkillTreeUI.Instance; var p = PlayerContext.Instance.CurrentActor; var session = AccountGameplaySession.Current;
        Check(ui != null && ui.nodes.Length == 53 && session.Read().skillTree.earnedPoints == 0, "Product installs expanded 53-node tree with zero new-account points");
        var input = PlayerInputFacade.Current; bool gameplay = input.IsGameplayEnabled;
        foreach (var step in Steps(OpenFromMenu())) yield return step;
        Check(GameplayInputBlocker.IsGameplayInputBlocked && !input.IsGameplayEnabled && ui.Plan.Remaining == 0, "Tree owns input and real account budget");
        foreach (var n in ui.Catalog.nodes.Where(n => n.IsReserved)) { ui.SelectNode(n.id, true); Check(!ui.action.interactable && !ui.Plan.Has(n.id) && ui.actionLabel.text.Contains("확장 예정"), "Visible reserved node never purchases " + n.id); }
        ui.SelectNode("S_W1", true); Check(!ui.action.interactable, "No points disables purchase action");
        int xp = Enumerable.Range(1, 19).Sum(OverburstGrowthRules.ExperienceToNext);
        PlayerProgression.Current.AddExperience(xp); Check(PlayerProgression.Current.FlushPendingExperience(), "Actual progression flush");
        ui.SyncAccount(true); Check(PlayerProgression.CurrentLevel == 20 && ui.Plan.Remaining == 4, "Actual level-up grants four milestone points");
        Canvas.ForceUpdateCanvases(); ui.ResetMap();
        foreach (var node in ui.nodes)
        {
            ui.HideTooltip(); Canvas.ForceUpdateCanvases(); var rect = (RectTransform)node.transform;
            Check(Hit(rect) == node.gameObject, "Native node raycast " + node.nodeId);
            ExecuteEvents.Execute(node.gameObject, Pointer(rect), ExecuteEvents.pointerEnterHandler);
            Check(ui.tooltip.gameObject.activeSelf && ui.tipName.text == ui.Catalog.nodes.First(n => n.id == node.nodeId).name, "Native hover " + node.nodeId);
            ExecuteEvents.Execute(node.gameObject, Pointer(rect), ExecuteEvents.pointerExitHandler);
        }
        float attack = p.Equipment.CurrentWeaponStats.damage, armor = PlayerProgression.Current.Armor, hp = p.Health.MaxHp, walk = p.Movement.WalkMoveSpeed, run = p.Movement.RunMoveSpeed;
        p.Health.TakeDamage(new DamageInfo(20, p.transform.position, suppressDefaultHitVfx: true)); float injured = p.Health.CurrentHp;
        Check(injured < hp, "Actual damaged-health fixture");
        ui.SelectNode("S_W1", true); Click(ui.action); Check(ui.Plan.Has("S_W1") && ui.Plan.Remaining == 3 && p.Equipment.CurrentWeaponStats.damage == attack, "Draft consumes one point and leaves combat unchanged");
        Click(ui.cancel); Check(!ui.Plan.Has("S_W1") && ui.Plan.Remaining == 4, "Cancel returns account baseline");
        foreach (var id in new[] { "S_W1", "S_W3", "S_Q1", "S_Q2" }) { ui.SelectNode(id, true); Click(ui.action); }
        Check(ui.Plan.Remaining == 0 && ui.Plan.Changed && ui.apply.interactable, "Four-stat draft and aligned active footer");
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "ingame-draft.png")); yield return null;
        var authority = (AccountTransactions)typeof(AccountGameplaySession).GetField("transactions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(session);
        var saveStore = (EasySaveAccountStore)typeof(AccountTransactions).GetField("store", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(authority);
        saveStore.FaultInjector = phase => { if (phase == "before-write") throw new IOException("Owned UI save failure"); };
        try { Click(ui.apply); Check(ui.Plan.Changed && session.Read().skillTree.learnedNodeIds.Count == 0 && p.Equipment.CurrentWeaponStats.damage == attack && ui.feedback.text.Contains("실패"), "Native UI failed save retains draft and committed stats"); }
        finally { saveStore.FaultInjector = null; }
        Click(ui.apply); yield return null;
        Check(!ui.Plan.Changed && session.PersistedRevision == session.Revision && session.Read().skillTree.learnedNodeIds.Count == 4, "Native apply durably stores four nodes");
        float expectedAttack = CombatBalanceFormulas.ComposePlayerWeaponStats(WeaponStatCalculator.Calculate(p.Equipment.CurrentWeaponItem), GearStatTotals.From(p.Equipment), 20, MapRunBuffs.Bonus(MapBuffKind.AttackSpeed), 3).damage;
        Check(p.Equipment.CurrentWeaponStats.damage == expectedAttack && expectedAttack > attack, "Attack bonus applied once in product weapon composition");
        Check(Mathf.Approximately(PlayerProgression.Current.Armor, armor + 1) && Mathf.Approximately(p.Health.MaxHp, hp * 1.04f), "Flat armor and percent maximum health projection");
        Check(Mathf.Approximately(p.Health.CurrentHp, injured), "Tree health purchase grants no healing");
        Check(Mathf.Approximately(p.Movement.WalkMoveSpeed, walk * 1.015f) && Mathf.Approximately(p.Movement.RunMoveSpeed, run * 1.015f), "Actual normal walk and run speed projection");
        for (int i = 0; i < 5; i++) PlayerProgression.Current.RefreshSkillTreeStats();
        Check(Mathf.Approximately(p.Health.MaxHp, hp * 1.04f) && p.Equipment.CurrentWeaponStats.damage == expectedAttack && Mathf.Approximately(p.Health.CurrentHp, injured), "Repeated projection cannot compound bonuses or heal");
        long appliedRevision = session.Revision; Check(!session.ApplySkillTree(session.Read().skillTree, ui.Plan.Planned) && session.Revision == appliedRevision, "Unchanged allocation is a no-op");
        ui.SelectNode("S_W3", true); Click(ui.action); Click(ui.close); yield return null; foreach (var step in Steps(OpenFromMenu())) yield return step;
        Check(ui.Plan.Changed && !ui.Plan.Has("S_W3"), "Accidental close preserves same-account draft"); Click(ui.cancel); Check(ui.Plan.Has("S_W3") && !ui.Plan.Changed, "Explicit cancel restores draft baseline");
        var saved = new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load();
        Check(saved.skillTree.learnedNodeIds.Count == 4 && saved.skillTree.grants.Count == 4, "Fresh disk store reads allocation and grant ledger");
        ui.HideTooltip(); ui.ResetMap(); ui.SelectNode("S_W1", true); Canvas.ForceUpdateCanvases(); yield return null;
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "ingame-skill-tree.png")); yield return null;
        ui.ShowTooltip("S_W1"); yield return null; ScreenCapture.CaptureScreenshot(Path.Combine(Output, "ingame-tooltip.png")); yield return null;
        ui.SelectNode("K_W",true);ui.ShowTooltip("K_W");yield return null;ScreenCapture.CaptureScreenshot(Path.Combine(Output,"ingame-keystone.png"));yield return null;
        ui.HideTooltip(); float fit = ui.Zoom; var pointer = Pointer(ui.viewport); pointer.scrollDelta = Vector2.up;
        ExecuteEvents.Execute(ui.viewport.gameObject, pointer, ExecuteEvents.scrollHandler); Check(Mathf.Approximately(ui.Zoom, fit + .1f), "Real native wheel zoom");
        Click(ui.zoomIn); Click(ui.zoomOut); Check(Mathf.Approximately(ui.Zoom, fit + .1f), "Native zoom buttons");
        pointer = Pointer(ui.viewport); ExecuteEvents.Execute(ui.viewport.gameObject, pointer, ExecuteEvents.beginDragHandler); pointer.position += new Vector2(80, -40); var beforePan = ui.Pan;
        ExecuteEvents.Execute(ui.viewport.gameObject, pointer, ExecuteEvents.dragHandler); ExecuteEvents.Execute(ui.viewport.gameObject, pointer, ExecuteEvents.endDragHandler);
        Check(ui.Pan != beforePan && !ui.tooltip.gameObject.activeSelf, "Real native drag"); Click(ui.center); Check(ui.Pan == Vector2.zero && Mathf.Approximately(ui.Zoom, ui.FitZoom), "Dynamic full-map fit");
        ui.ZoomAt(2, Vector2.zero); ui.SelectNode("S_W1", true); yield return null; ScreenCapture.CaptureScreenshot(Path.Combine(Output, "ingame-zoom200.png")); yield return null; ui.ResetMap();
        string allocation = string.Join(",", ui.Plan.Planned); string topology = JsonConvert.SerializeObject(ui.Catalog.connections);
        foreach (var element in new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light })
        {
            var data = Resources.LoadAll<ElementGemItemData>("Items/ElementGems").Where(g => g.element == element).OrderBy(g => g.fixedGrade).First();
            var gem = new ItemData(data, 1, data.fixedGrade); var inventory = PlayerContext.Instance.CurrentActorInventory;
            Check(inventory.AddItem(gem), "Actual gem inventory " + element); int slot = Enumerable.Range(0, inventory.Items.Count).First(i => inventory.GetItemAt(i)?.runtimeInstanceId == gem.runtimeInstanceId);
            Check(ElementGemEquipmentService.EquipFromInventorySlot(slot, gem.runtimeInstanceId), "Actual gem equip " + element); yield return null; ui.RefreshDetail(); ui.ShowTooltip("S_W1");
            Check(ui.CurrentElement == element && ui.equippedIcon.sprite == ui.elementSprites[OverburstSkillTreeCatalog.ElementIndex(element)], "Actual HUD element badge " + element);
            Check(string.Join(",", ui.Plan.Planned) == allocation && JsonConvert.SerializeObject(ui.Catalog.connections) == topology && SkillTreeBonuses.AttackPercent == 3 && SkillTreeBonuses.ArmorFlat == 1 && SkillTreeBonuses.HealthPercent == 4 && SkillTreeBonuses.MovePercent == 1.5f, "One shared tree and bonuses across " + element);
        }
        Check(ElementGemEquipmentService.UnequipToInventory(), "Actual gem unequip");
        ui.HideTooltip(); Click(ui.close); yield return null; foreach (var step in Steps(OpenFromMenu())) yield return step; Check(ui.Plan.Planned.Length == 4, "Reopen loads durable allocation");
        ui.SelectNode("S_W1", true); Check(ui.Plan.Refunds("S_W1").Length == 2, "Product cascading refund preview"); Click(ui.action); Click(ui.apply);
        Check(ui.Plan.Remaining == 2 && !ui.Plan.Has("S_W1") && !ui.Plan.Has("S_W3") && ui.Plan.Has("S_Q1") && SkillTreeBonuses.AttackPercent == 0 && SkillTreeBonuses.ArmorFlat == 0, "Durable refund removes disconnected bonuses and retains sibling branch");
        Click(ui.resetAllocation); Check(ui.Plan.Remaining == 4 && SkillTreeBonuses.HealthPercent == 4 && SkillTreeBonuses.MovePercent == 1.5f, "Full refund remains preview until applied"); Click(ui.cancel); Check(ui.Plan.Has("S_Q1"), "Full refund preview can cancel");
        p.Health.Heal(100000); Check(p.Health.CurrentHp > hp, "Refund maximum-health clamp fixture");
        Click(ui.resetAllocation); Click(ui.apply); Check(Mathf.Approximately(p.Health.MaxHp, hp) && Mathf.Approximately(p.Health.CurrentHp, hp) && Mathf.Approximately(p.Movement.WalkMoveSpeed, walk), "Health and movement refund return to baseline and clamp current health");
        foreach (var id in new[] { "S_W1", "S_W3", "S_W5", "S_W6" }) { ui.SelectNode(id, true); Click(ui.action); }
        Click(ui.apply);
        Check(ui.Plan.Remaining == 0 && SkillTreeBonuses.AttackPercent == 6 && SkillTreeBonuses.HealthPercent == 4 && SkillTreeBonuses.ArmorFlat == 1, "Added branch stat nodes commit and project actual bonuses");
        var expandedSave = new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load();
        Check(expandedSave.skillTree.learnedNodeIds.Contains("S_W5") && expandedSave.skillTree.learnedNodeIds.Contains("S_W6"), "New branch IDs persist in product account");
        ui.SelectNode("S_W3", true); Check(ui.Plan.Refunds("S_W3").Length == 3, "Expanded branch refund previews three disconnected nodes");
        Click(ui.action); Click(ui.apply);
        Check(ui.Plan.Has("S_W1") && !ui.Plan.Has("S_W5") && !ui.Plan.Has("S_W6") && SkillTreeBonuses.AttackPercent == 3 && SkillTreeBonuses.HealthPercent == 0 && SkillTreeBonuses.ArmorFlat == 0, "Expanded branch refund removes bonuses and preserves root-side node");
        Click(ui.resetAllocation); Click(ui.apply);
        for (int i = 0; i < 3; i++) { Click(ui.close); yield return null; Check(!GameplayInputBlocker.IsGameplayInputBlocked && input.IsGameplayEnabled == gameplay, "Repeated close returns input " + i); foreach (var step in Steps(OpenFromMenu())) yield return step; }
        var previous = Keyboard.current; var keyboard = InputSystem.AddDevice<Keyboard>("OwnedSkillTreeFoundationKeyboard");
        try
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.Escape)); yield return null; yield return null;
            Check(!ui.IsOpen && !OverburstGameMenu.IsOpen && !GameplayInputBlocker.IsGameplayInputBlocked && input.IsGameplayEnabled == gameplay, "Actual Escape closes tree and restores input");
        }
        finally { if (keyboard.added) InputSystem.RemoveDevice(keyboard); if (previous != null && previous.added) previous.MakeCurrent(); }
        Check(File.Exists(Path.Combine(Output, "ingame-skill-tree.png")) && File.Exists(Path.Combine(Output, "ingame-tooltip.png")), "Actual composited Play captures saved");
    }
}
