using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Overburst.Persistence;

[InitializeOnLoad]
public static partial class ElementGemPlayVerifier
{
    const string Key = "Overburst.ElementGemPlayVerifier";
    static readonly List<string> checks = new List<string>();
    static bool executed;
    static ElementGemPlayVerifier() { EditorApplication.update += Tick; }
    public static void Begin(string output, int goal, string report = null, bool realAccount = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("An idle Editor is required.");
        if (!string.IsNullOrEmpty(SessionState.GetString(Key, ""))) throw new InvalidOperationException("Gem verifier already pending.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key, output);
        SessionState.SetInt(Key + ".Goal", goal);
        SessionState.SetString(Key + ".Report", report ?? ("GOAL" + goal.ToString("00")));
        SessionState.SetString(Key + ".StartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetFloat(Key + ".Deadline", (float)EditorApplication.timeSinceStartup + 180);
        SessionState.SetBool(Key + ".Return", false);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        try
        {
            if (realAccount) { IsolatedSavePlayGuard.UseRealAccount(); EditorApplication.isPlaying = true; }
            else IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount_G" + goal + "_" + Guid.NewGuid().ToString("N")));
        }
        catch { EditorSceneManager.playModeStartScene = LoadStart(); SessionState.EraseString(Key); throw; }
    }
    static SceneAsset LoadStart()
    { string path = SessionState.GetString(Key + ".StartScene", ""); return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(path); }
    static void Tick()
    {
        string output = SessionState.GetString(Key, "");
        if (string.IsNullOrEmpty(output)) return;
        if (executed && EditorApplication.isPlaying && SessionState.GetFloat(Key + ".StopAt", 0) > 0 && EditorApplication.timeSinceStartup >= SessionState.GetFloat(Key + ".StopAt", 0))
        { SessionState.EraseFloat(Key + ".StopAt"); SessionState.SetBool(Key + ".Return", true); EditorApplication.isPlaying = false; return; }
        if (SessionState.GetBool(Key + ".Return", false))
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string env = Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY");
            if (!string.IsNullOrEmpty(env)) return; // A newly prepared other-owner account must not be cleared.
            EditorSceneManager.playModeStartScene = LoadStart();
            IsolatedSavePlayGuard.UseRealAccount();
            File.WriteAllText(Path.Combine(output, "Return_G" + SessionState.GetInt(Key + ".Goal", 0) + ".json"), JsonConvert.SerializeObject(new { ready = true, choice = IsolatedSavePlayGuard.RequiresAccountChoice, env = Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY"), start = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) }));
            SessionState.EraseString(Key); SessionState.EraseString(Key + ".StartScene"); SessionState.EraseInt(Key + ".Goal");
            SessionState.EraseBool(Key + ".Return"); SessionState.EraseFloat(Key + ".Deadline");
            SessionState.EraseString(Key + ".Report"); SessionState.EraseFloat(Key + ".StopAt");
            executed = false; checks.Clear();
            return;
        }
        if (!EditorApplication.isPlaying)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && (executed || EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + ".Deadline", 0)))
                SessionState.SetBool(Key + ".Return", true);
            return;
        }
        if (executed) return;
        bool timeout = EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + ".Deadline", 0);
        if (!AccountBootstrap.Ready && !timeout && string.IsNullOrEmpty(AccountBootstrap.Error)) return;
        int goal = SessionState.GetInt(Key + ".Goal", 3);
        if (goal >= 5 && !timeout && (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !UnityEngine.SceneManagement.SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName).isLoaded)) return;
        executed = true;
        string failure = null;
        try
        {
            if (goal >= 5 && PersistentSceneFlow.Instance.IsSwitching) throw new InvalidOperationException("Hideout scene readiness timeout.");
            if (!AccountBootstrap.Ready) throw new InvalidOperationException(AccountBootstrap.Error ?? "Account bootstrap timeout.");
            if (goal == 3) Equipment();
            else if (goal == 4) Combat();
            else if (goal == 5) Ui();
            else if (goal == 6) Farming();
            else if (goal == 7)
            {
                Check(AccountGameplaySession.Current.ExecuteState("gem-test-capacity-"+Guid.NewGuid().ToString("N"),s=>s.unlockedSlots=s.baseUnlockedSlots=s.inventoryCapacity),"isolated fixture capacity");
                Boundaries(); Equipment(); Combat(); Farming();
            }
            else if (goal == 8 || goal == 9) FreshRealAccount(goal == 9);
            else throw new InvalidOperationException("Unknown gem verification stage.");
            AccountGameplaySession.Current.FlushPendingSave();
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            File.WriteAllText(Path.Combine(output, SessionState.GetString(Key + ".Report", "GOAL"+goal.ToString("00")) + ".json"), JsonConvert.SerializeObject(new { goal, status = failure == null ? "PASS" : "FAIL", checks, failure, account = AccountBootstrap.SaveDirectory }, Formatting.Indented));
            if (goal == 5 && failure == null) SessionState.SetFloat(Key + ".StopAt", (float)EditorApplication.timeSinceStartup + 1.5f);
            else { SessionState.SetBool(Key + ".Return", true); EditorApplication.isPlaying = false; }
        }
    }
    static void Check(bool condition, string label) => ElementGemVerifier.Require(condition, label, checks);
    static void Equipment()
    {
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var equipment = PlayerContext.Instance.CurrentActorEquipment;
        var inventory = PlayerAccountInventoryService.SharedInventory;
        var session = AccountGameplaySession.Current;
        Check(equipment != null && inventory != null && session != null, "actual player and account ready");
        Check(session.Read().gear.Count == 6 && Enum.GetValues(typeof(GearSlot)).Length == 6, "six gear contract");
        var definitions = registry.Entries.Select(x => x.asset).OfType<GearItemData>().ToArray();
        Check(definitions.Length == 140, "140 gear definitions retained");
        foreach (GearKind kind in Enum.GetValues(typeof(GearKind)))
        {
            var gear = new ItemData(definitions.First(x => x.kind == kind && x.AppearsAtLevel(1)), 1, ItemGrade.Common);
            Check(inventory.AddItem(gear), "add " + kind);
            Check(GearEquipmentService.EquipFromInventorySlot(inventory.FindFirstMatchingItemIndex(gear)), "equip " + kind);
            Check(equipment.GetGearSlotItem((int)kind)?.runtimeInstanceId == gear.runtimeInstanceId, "slot maps " + kind);
        }
        var earring = new ItemData(definitions.Last(x => x.kind == GearKind.Earring && x.AppearsAtLevel(1)), 1, ItemGrade.Common);
        var previous = equipment.GetGearSlotItem(4);
        Check(inventory.AddItem(earring), "add second earring appearance");
        Check(GearEquipmentService.EquipFromInventorySlot(inventory.FindFirstMatchingItemIndex(earring)), "second appearance replaces same earring slot");
        Check(inventory.Items.Any(x => x?.runtimeInstanceId == previous.runtimeInstanceId) && equipment.GetGearSlotItem(5)?.baseData is GearItemData necklace && necklace.kind == GearKind.Necklace, "earring replacement preserves necklace");
        var data = registry.Resolve<ElementGemItemData>("item.elementgem.fire.legendary");
        var gem = new ItemData(data, 20, data.fixedGrade);
        for (int seed = 0; seed < 100; seed++)
        {
            gem.gemState = ElementGemQuality.Roll(data, seed, ElementGemArchetype.Weak);
            if (gem.gemState.rolls.Any(x => x.optionId == "gem.common.max_health")) break;
        }
        Check(gem.gemState.rolls.Any(x => x.optionId == "gem.common.max_health"), "survival gem fixture");
        Check(inventory.AddItem(gem), "add farmable gem");
        float hp = PlayerContext.Instance.CurrentActorHealth.CurrentHp;
        int revision = equipment.GemRevision;
        var before = GearStatTotals.From(equipment);
        Check(ElementGemEquipmentService.EquipFromInventorySlot(inventory.FindFirstMatchingItemIndex(gem)), "equip actual gem command");
        Check(!inventory.Items.Any(x => x?.runtimeInstanceId == gem.runtimeInstanceId) && session.Read().elementalGemInstanceId == gem.runtimeInstanceId, "one gem owner after equip");
        Check(equipment.GemRevision == revision + 1 && GearStatTotals.From(equipment).MaxHealth > before.MaxHealth, "gem change revision and stats");
        Check(Mathf.Abs(PlayerContext.Instance.CurrentActorHealth.CurrentHp - hp) < .001f, "gem equip does not heal");
        string quality = JsonConvert.SerializeObject(gem.gemState);
        Check(inventory.AddItem(MerchantTradeItemUtility.CreateGoldItem(3)), "unrelated currency change");
        Check(equipment.GemRevision == revision + 1, "currency does not change gem context");
        var replacement = new ItemData(data, 20, data.fixedGrade);
        Check(inventory.AddItem(replacement) && ElementGemEquipmentService.EquipFromInventorySlot(inventory.FindFirstMatchingItemIndex(replacement)), "same element different gem swaps");
        Check(equipment.GemRevision == revision + 2 && inventory.Items.Any(x => x?.runtimeInstanceId == gem.runtimeInstanceId), "swap revision and returned gem");
        Check(JsonConvert.SerializeObject(inventory.Items.Single(x => x?.runtimeInstanceId == gem.runtimeInstanceId).gemState) == quality, "swap preserves quality");
        Check(ElementGemEquipmentService.UnequipToInventory() && equipment.EquippedElementGem == null, "gem unequip");
        AccountInvariants.Validate(session.Read(), registry);
        Check(session.Read().elementalGemInstanceId == null, "scalar cleared and ownership valid");
        Check(inventory.AddItem(new ItemData(data, 1, data.fixedGrade)) && inventory.Items.Count(x => x?.baseData == data) >= 3, "gems do not stack");
    }

    static ItemData EquipGem(WeaponElement element, ElementGemArchetype type, string option = null)
    {
        var definition = Resources.LoadAll<ElementGemItemData>("Items/ElementGems").First(x => x.element == element && x.fixedGrade == ItemGrade.Legendary);
        var item = new ItemData(definition, 30, definition.fixedGrade);
        for (int seed = 0; seed < 1000; seed++)
        {
            item.gemState = ElementGemQuality.Roll(definition, seed, type);
            if (option == null || item.gemState.rolls.Any(x => x.optionId == option && ElementGemQuality.Value(item,x) > 0)) break;
        }
        var inventory = PlayerAccountInventoryService.SharedInventory;
        Check(inventory.AddItem(item) && ElementGemEquipmentService.EquipFromInventorySlot(inventory.FindFirstMatchingItemIndex(item)), "equip " + element + " " + type);
        return item;
    }
    static void Combat()
    {
        var equipment = PlayerContext.Instance.CurrentActorEquipment;
        var energy = equipment.GetComponent<OverburstElementEnergy>() ?? equipment.gameObject.AddComponent<OverburstElementEnergy>();
        Check(equipment.CurrentWeaponItem.ResolvedElement == WeaponElement.None && equipment.CurrentWeaponContext.Element == WeaponElement.None, "neutral starter weapon and context");
        var root = new GameObject("ElementGemCombatFixture"); root.transform.position = new Vector3(8000, 0, 8000);
        try
        {
            var hp = root.AddComponent<CombatHealth>(); hp.SetMaxHp(100000, true);
            var statuses = root.AddComponent<ElementalStatusController>();
            var tuning = OverburstElementTuning.Current;
            int sequence = 700000;
            foreach (var element in new[]{WeaponElement.Fire,WeaponElement.Ice,WeaponElement.Electric,WeaponElement.Dark,WeaponElement.Light})
            {
                EquipGem(element, ElementGemArchetype.Weak);
                var snapshot = new ElementGemAttackSnapshot(equipment);
                Check(snapshot.IsCurrent && snapshot.Element == element && energy.Element == element && energy.Amount == 0, "active resolver and fresh energy " + element);
                foreach (var kind in new[]{PlayerAttackKind.Weak,PlayerAttackKind.Heavy})
                {
                    hp.ResetHealth(); statuses.ClearAllStatuses();
                    var impact = new AttackImpactData { triggersOnHitEffects = true };
                    var result = MeleeDamageResolver.Apply(new MeleeDamageRequest(hp, 100, impact, 0, 0, 1, root.transform.position, equipment.gameObject, Vector3.forward, 0, true, element, snapshot.WeaponId, ++sequence, kind, gemAttack: snapshot));
                    float expected = CombatBalanceFormulas.ApplyPlayerOutgoing(100, snapshot.Stats, false, EnemyGradeType.Normal, kind | PlayerAttackKind.Elemental, snapshot.RunAttack, snapshot.RunElemental);
                    Check(Mathf.Abs(result.ActualDamage - expected) < .02f, "direct " + element + " " + kind + " bonus once");
                }
                hp.ResetHealth(); statuses.ClearAllStatuses();
                if (element == WeaponElement.Fire || element == WeaponElement.Electric)
                {
                    statuses.TryApplyDirectHit(new ElementalStatusApplication(element, 100, equipment.gameObject, snapshot.WeaponId, true, false, root.transform.position, Vector3.forward, snapshot));
                    float before = hp.CurrentHp;
                    statuses.AdvanceReactionStatesForValidation(Time.time + tuning.TickInterval(element) + .001f);
                    float multiplier = element == WeaponElement.Fire ? snapshot.Modifiers.BurnDamage : snapshot.Modifiers.ShockDamage;
                    float expected = CombatBalanceFormulas.StatusTickDamage(tuning, element, 100, 1) * (1 + multiplier/100f);
                    Check(Mathf.Abs(before - hp.CurrentHp - expected) < .02f, "resolved tick avoids repeated common damage " + element);
                }
                Check(energy.RecordConfirmedHit(snapshot.WeaponId, element, ++sequence, 100), "energy gain " + element);
                float first = energy.Amount;
                Check(!energy.RecordConfirmedHit(snapshot.WeaponId, element, sequence, 100) && energy.Amount == first, "phase many-target dedup " + element);
                energy.RecordConfirmedHit(snapshot.WeaponId, element, sequence, 100, true);
                Check(Mathf.Abs(energy.Amount - Mathf.Max(first, CombatBalanceFormulas.PhaseEnergyGain(tuning,true,FlaskCombatModifiers.Bonus(equipment.gameObject,FlaskEffect.EnergyGain)))) < .01f, "critical phase only pays difference " + element);
                Check(energy.TryCommitDischarge(100,out var discharge,snapshot), "heavy commit " + element);
                Check(discharge.TryRefundParried() && !discharge.TryRefundParried(), "parry half refund once " + element);
                Check(Mathf.Abs(energy.Amount - Mathf.Min(discharge.Energy,energy.BaseMaximum)*.5f)<.01f,"parry 50 percent " + element);
                var older = snapshot;
                EquipGem(element, ElementGemArchetype.Heavy);
                Check(!older.IsCurrent && energy.Amount == 0 && !discharge.TryRefundParried(), "same element replacement cancels snapshot and refund " + element);
                Check(statuses.GetStackCount(element) == 0, "old statuses removed " + element);
                var fresh = new ElementGemAttackSnapshot(equipment);
                PlayerAccountInventoryService.SharedInventory.AddItem(MerchantTradeItemUtility.CreateGoldItem(1));
                Check(fresh.IsCurrent, "currency keeps attack context " + element);
                float oldhp = hp.CurrentHp;
                hp.TakeDamage(new DamageInfo(100,root.transform.position,equipment.gameObject, gemAttack:older));
                Check(hp.CurrentHp == oldhp, "stale hit rejected " + element);
            }
            var ice = EquipGem(WeaponElement.Ice,ElementGemArchetype.Weak,"gem.ice.freeze_threshold_reduction");
            var cold = new OverburstElementState(); var icemod = equipment.GemModifiers;
            Check(icemod.FreezeReduction == 1, "freeze special reduces one stack");
            for(int i=0;i<3;i++) cold.Add(WeaponElement.Ice,0,tuning,1+icemod.FreezeDuration/100f,1);
            Check(!cold.IsFrozen(0),"cold 3 not frozen"); cold.Add(WeaponElement.Ice,0,tuning,1+icemod.FreezeDuration/100f,1);
            Check(cold.IsFrozen(0) && cold.RawCount(WeaponElement.Ice)==4 && Mathf.Abs(cold.FrozenRemaining(0)-tuning.freezeDuration*(1+icemod.FreezeDuration/100f))<.01f,"cold 4 freeze and duration");
            float until=cold.FrozenRemaining(0); cold.Add(WeaponElement.Ice,.1f,tuning,2,1);
            Check(Mathf.Abs(cold.FrozenRemaining(0)-until)<.01f,"freeze does not extend from weak hits");
            cold.Add(WeaponElement.Ice,.1f,tuning,1,1,0,true);
            Check(cold.RawCount(WeaponElement.Ice)==5,"immune target keeps cold cap 5");
            EquipGem(WeaponElement.Dark,ElementGemArchetype.Weak,"gem.dark.corrosion_max_stacks");
            var dark = equipment.GemModifiers; var state = new OverburstElementState();
            for(int i=0;i<20;i++) state.Add(WeaponElement.Dark,0,tuning,1,0,dark.CorrosionExtra);
            Check(state.RawCount(WeaponElement.Dark)==tuning.maximumStacks+dark.CorrosionExtra,"independent corrosion cap");
            statuses.ClearAllStatuses(); var darkshot = new ElementGemAttackSnapshot(equipment);
            for(int i=0;i<3;i++) statuses.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Dark,100,equipment.gameObject,darkshot.WeaponId,true,false,root.transform.position,Vector3.forward,darkshot));
            Check(Mathf.Abs(darkshot.WeakBonus(hp)-3*dark.WeakPerStack)<.001f,"dark weak uses pre-hit stacks");
            int held=statuses.ReserveCorrosion();Check(held==3 && statuses.ReserveCorrosion()==0,"corrosion reserved once");
            statuses.ReleaseCorrosionReservation(held);Check(statuses.ReserveCorrosion()==3 && statuses.ConsumeReservedCorrosion(3)==3,"reservation release and actual hit consumption");
            EquipGem(WeaponElement.Light,ElementGemArchetype.Weak,"gem.light.radiance_max_stacks");
            var light = new ElementGemAttackSnapshot(equipment);
            for(int i=0;i<250;i++) energy.RecordConfirmedHit(light.WeaponId,WeaponElement.Light,++sequence,100);
            Check(energy.RadianceStacks==energy.RadianceMaximum && energy.RadianceMaximum>100 && energy.Capacity==tuning.SafeLightOverchargeMaximum,"radiance extra independent of energy capacity");
            light=new ElementGemAttackSnapshot(equipment);
            Check(energy.TryCommitDischarge(100,out var luminous,light) && energy.RadianceStacks==0,"light snapshot and consume");
            for(int i=0;i<3;i++) Check(Mathf.Abs(luminous.LightHitDamage(i)-luminous.FirstBlastDamage*CombatBalanceFormulas.LightTripleHitScale(tuning,i,luminous.RadianceStacks,luminous.Overcharge)*(1+FlaskCombatModifiers.Bonus(equipment.gameObject,FlaskEffect.LightTripleImpactDamage)+light.Modifiers.LightHitDamage/100f))<.02f,"light hit modifier " + i);
            LightTripleImpactScheduler.Submit(equipment.gameObject,CombatTeam.PlayerParty,root.transform.position,2,100,2,3,2,100,light);
            Check(LightTripleImpactScheduler.PendingCount>0,"light followup scheduled");
            Check(ElementGemEquipmentService.UnequipToInventory() && !light.IsCurrent && energy.Amount==0 && equipment.ActiveElement==WeaponElement.None,"gem removal clears active element and energy");
            LightTripleImpactScheduler.ClearAll();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void Ui()
    {
        var equipment=PlayerContext.Instance.CurrentActorEquipment;
        var inventory=PlayerAccountInventoryService.SharedInventory;
        var game=UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>();
        Check(game!=null,"actual gameplay UI instantiated");
        game.stash.Close(); game.equipmentWindow.gameObject.SetActive(true);game.inventory.SetVisible(true);game.Refresh();
        var layout=game.equipmentWindow.transform.Find("Layout");
        Check(layout.Find("Slot • 귀걸이")!=null && layout.Find("Slot • 목걸이")!=null && layout.Find("Slot • 원소보석")!=null,"production six gear and gem layout");
        Check(layout.Find("Slot • 귀걸이 1")==null && layout.Find("Slot • 귀걸이 2")==null,"old earring slot names removed");
        var pointer=layout.Find("Slot • 원소보석").GetComponent<ElementGemEquipmentSlotUI>();
        Check(pointer!=null && pointer.GetComponents<GearEquipmentSlotUI>().Length==0,"gem has one dedicated handler");
        foreach(var definition in Resources.LoadAll<ElementGemItemData>("Items/ElementGems"))
        foreach(ElementGemArchetype type in Enum.GetValues(typeof(ElementGemArchetype)))
        {
            var item=new ItemData(definition,100,definition.fixedGrade);item.gemState=ElementGemQuality.Roll(definition,17,type);
            var rows=ElementGemTooltip.Rows(item);string text=SimpleItemTooltipBuilder.Build(item);
            Check(rows.Count(x=>!x.Fixed)==4 && text.Contains(ElementGemTooltip.Archetype(type)) && text.Contains("아이템 레벨 100"),"grade/type/four random rows "+definition.name+" "+type);
            foreach(var roll in item.gemState.rolls)
            {
                var option=ElementGemQuality.Option(roll.optionId);var row=rows.Single(x=>!x.Fixed&&x.Label==option.Label);
                Check(row.Value==ElementGemQuality.Value(item,roll),"tooltip exact runtime value "+roll.optionId);
            }
        }
        var gem=EquipGem(WeaponElement.Fire,ElementGemArchetype.Weak);
        var uiIcon=UnityEngine.Object.FindFirstObjectByType<WeaponElementHudIcon>();
        uiIcon.RefreshActor();
        Check(uiIcon.View!=null && equipment.ActiveElement==WeaponElement.Fire,"HUD reads equipped gem context");
        pointer.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){button=UnityEngine.EventSystems.PointerEventData.InputButton.Right});
        Check(equipment.EquippedElementGem==null && equipment.ActiveElement==WeaponElement.None,"actual gem slot right click unequips");
        var bridge=game.inventory.GetComponent<InventorySlotBridge>() ?? game.inventory.GetComponentInChildren<InventorySlotBridge>(true);
        bridge.RefreshSlotsWithOwnershipCheck();
        int index=inventory.FindFirstMatchingItemIndex(gem);
        var slot=game.inventory.GetComponentsInChildren<SlotUI>(true).First(x=>x.OwnerBridge==bridge&&!x.IsWeaponSlot&&!x.IsBagSlot&&x.SlotIndex==index);
        var data=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){button=UnityEngine.EventSystems.PointerEventData.InputButton.Left,clickCount=2};
        Check(bridge.HandleSlotClick(SlotClickContext.Create(slot,data,true)) && equipment.EquippedElementGem.runtimeInstanceId==gem.runtimeInstanceId,"inventory actual double-click route equips; source="+slot.DisplayItem?.itemType+" locked="+slot.IsLocked+" index="+index);
        var candidate=new ItemData((ElementGemItemData)gem.baseData,100,gem.grade);
        Check(inventory.AddItem(candidate),"add comparison and drag candidate");
        var comparison=EquippedWeaponComparison.Compare(candidate,TooltipCompareMode.Auto);
        Check(comparison!=null && comparison.IsComparable && comparison.Stats.Count>0,"same element row comparison");
        bridge.RefreshSlotsWithOwnershipCheck();index=inventory.FindFirstMatchingItemIndex(candidate);
        slot=game.inventory.GetComponentsInChildren<SlotUI>(true).First(x=>x.OwnerBridge==bridge&&!x.IsWeaponSlot&&!x.IsBagSlot&&x.SlotIndex==index);
        var drag=slot.GetComponent<DragSlot>(); data.position=new Vector2(800,400);drag.OnBeginDrag(data);pointer.OnDrop(data);drag.OnEndDrag(data);
        Check(equipment.EquippedElementGem.runtimeInstanceId==candidate.runtimeInstanceId && !DragSlot.IsDragging,"actual drag onto gem slot and cleanup");
        Check(!ElementGemEquipmentService.EquipFromInventorySlot(inventory.FindFirstMatchingItemIndex(gem),"stale-instance-id"),"stale source ID rejected");
        var tooltip=UnityEngine.Object.FindFirstObjectByType<OverburstGameTooltip>(FindObjectsInactive.Include);
        tooltip.Show(gem);game.Refresh();Canvas.ForceUpdateCanvases();
        Check(tooltip.view.Rect.sizeDelta.y>300 && tooltip.view.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).Any(x=>x.text.Contains("고정 효과")),"authored tooltip renders fixed/random headings");
        ScreenCapture.CaptureScreenshot(Path.Combine(SessionState.GetString(Key,""),"GOAL05_UI.png"));
        AccountInvariants.Validate(AccountGameplaySession.Current.Read(), Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath));
    }

    static void SetWorldPhase(WorldPhase phase) => typeof(WorldSessionState).GetMethod("SetPhase",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{phase});
    static void Farming()
    {
        var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var session=AccountGameplaySession.Current;
        var inventory=PlayerAccountInventoryService.SharedInventory;
        var equipment=PlayerContext.Instance.CurrentActorEquipment;
        var game=UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>();
        int cases=0;var seen=new HashSet<WeaponElement>();
        foreach(int level in new[]{1,5,10,18,25,100}) foreach(EnemyGradeType rank in Enum.GetValues(typeof(EnemyGradeType)))
        for(int i=0;i<500;i++)
        {
            var definition=ElementGemLootPolicy.Select(i/499f,level,rank);
            if(definition==null || !ElementGemItemData.IsAllowed(definition.element,definition.fixedGrade) || definition.fixedGrade==ItemGrade.Cursed&&(rank!=EnemyGradeType.Boss||level<25)) throw new InvalidOperationException("illegal loot pair");
            seen.Add(definition.element);cases++;
        }
        Check(cases>=9000&&seen.Count==5,"joint drop candidates cover five legal elements and late-boss cursed source");
        Check(!ItemGradeAvailabilityPolicy.IsEnabled(ItemGrade.Cursed),"other item cursed global policy retained");
        Check(ItemPickupSpawner.CollectHideoutCatalog().OfType<ElementGemItemData>().Count()==32,"actual deduplicated hideout catalog has 32 gems");
        foreach(var definition in ElementGemLootPolicy.Catalog)
        {
            var item=WorldItemDropFactory.CreateRuntimeItem(definition,33,33,definition.fixedGrade,definition.fixedGrade,true,5);
            Check(item!=null&&item.level==33&&item.grade==definition.fixedGrade&&item.stackCount==1,"definition-specific debug/drop creation "+definition.name);
            var pickup=WorldItemDropFactory.CreateWorldPickup(item,new Vector3(7000,0,7000),inventory,equipment.transform);
            try
            {
                Check(pickup!=null&&pickup.RuntimeItem.runtimeInstanceId==item.runtimeInstanceId&&pickup.RuntimeItem.icon==definition.icon,"world model/grade/instance binding "+definition.name);
                Check(pickup.GetComponentsInChildren<Renderer>().Any() && pickup.GetComponentsInChildren<Renderer>().All(x=>x.sharedMaterials.All(m=>m!=null)),"textured gameplay wrapper "+definition.element);
                Check(pickup.GetComponentsInChildren<Collider>().All(x=>!x.enabled),"non-blocking pickup collider policy");
                var next=new ItemData(definition,44,definition.fixedGrade);
                pickup.Initialize(next,inventory,equipment.transform);
                Check(pickup.RuntimeItem.runtimeInstanceId==next.runtimeInstanceId&&pickup.RuntimeItem.level==44,"reuse rebind "+definition.name);
            }
            finally { if(pickup!=null)UnityEngine.Object.DestroyImmediate(pickup.gameObject); }
        }
        var common=ElementGemLootPolicy.Catalog.First(x=>x.fixedGrade==ItemGrade.Common);
        var gem=new ItemData(common,12,common.fixedGrade);
        var world=WorldItemDropFactory.CreateWorldPickup(gem,new Vector3(7000,0,7000),inventory,equipment.transform);
        string quality=JsonConvert.SerializeObject(gem.gemState);
        Check(world.TryPickup(inventory),"actual normal gem pickup");
        Check(session.HasPendingSave,"ordinary pickup deferred save");
        game.stash.Open(); var stash=game.stash.GetComponent<StashSlotBridge>();
        Check(ReferenceEquals(Field(stash,"inventory"),inventory)&&ReferenceEquals(Field(stash,"stash"),PlayerAccountInventoryService.SharedStash),"stash bridge uses account storage; inventory="+((PlayerInventory)Field(stash,"inventory")).GetInstanceID()+" shared="+inventory.GetInstanceID()+" stash="+((PlayerStash)Field(stash,"stash")).GetInstanceID()+" shared="+PlayerAccountInventoryService.SharedStash.GetInstanceID());
        int slot=inventory.FindFirstMatchingItemIndex(gem); // Opening the inventory may apply its saved sort mode.
        Check(stash.TryMoveInventorySlotToFirstAvailableStashSlot(slot),"actual gem store to stash");
        stash.RefreshSlots();
        var stashViews=UnityEngine.Object.FindObjectsByType<SlotUI>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        var inStash=stashViews.FirstOrDefault(x=>ReferenceEquals(x.OwnerBridge,stash)&&x.DisplayItem?.runtimeInstanceId==gem.runtimeInstanceId);
        Check(inStash!=null,"stash view ownership; slots="+stashViews.Count(x=>ReferenceEquals(x.OwnerBridge,stash))+" stored="+session.Read().stashTabs.Count(x=>x.slots.Contains(gem.runtimeInstanceId))+" tab="+stash.CurrentTabIndex);
        Check(stash.HandleSlotClick(SlotClickContext.Create(inStash,new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current),true)),"actual stash return");
        Check(JsonConvert.SerializeObject(inventory.Items.First(x=>x?.runtimeInstanceId==gem.runtimeInstanceId).gemState)==quality,"stash preserves instance quality");
        game.stash.Close();
        var trade=UnityEngine.Object.FindFirstObjectByType<MerchantTradeService>(FindObjectsInactive.Include);
        var merchant=registry.Entries.Select(x=>x.asset).OfType<MerchantDefinition>().First();
        trade.Open(merchant);trade.MerchantInventory.TryAddCurrency(CurrencyType.Gold,100000);
        string message;
        Check(trade.TogglePlayerOffer(inventory.FindFirstMatchingItemIndex(gem),out message),"offer gem sale");
        Check(trade.ExecuteTrade().success,"actual merchant sale");
        int bought=trade.MerchantInventory.Items.ToList().FindIndex(x=>x?.runtimeInstanceId==gem.runtimeInstanceId);
        Check(bought>=0 && trade.ToggleMerchantOffer(bought,out message) && trade.ExecuteTrade().success,"actual gem buyback");
        Check(JsonConvert.SerializeObject(inventory.Items.First(x=>x?.runtimeInstanceId==gem.runtimeInstanceId).gemState)==quality,"sale and buyback retain actual option stars");
        inventory.SortUnlockedSlots(ItemSortMode.Price,ItemSortDirection.Descending);
        Check(inventory.Items.Any(x=>x?.runtimeInstanceId==gem.runtimeInstanceId),"sort retains gem identity");
        var cursed=ElementGemLootPolicy.Catalog.First(x=>x.fixedGrade==ItemGrade.Cursed);
        var curse=new ItemData(cursed,25,cursed.fixedGrade);
        Check(AccountGameplaySession.RequiresPickupCheckpoint(curse),"cursed gem dedicated checkpoint");
        var cursePickup=WorldItemDropFactory.CreateWorldPickup(curse,new Vector3(7000,0,7000),inventory,equipment.transform);
        Check(cursePickup.TryPickup(inventory)&&session.PersistedRevision==session.Revision,"cursed actual pickup durable before world removal");
        var filler=new List<ItemData>();
        while(inventory.FindFirstEmptySlot()>=0) { var value=new ItemData(common,1,common.fixedGrade);Check(inventory.AddItem(value),"fill inventory");filler.Add(value); }
        var blocked=new ItemData(common,1,common.fixedGrade);var blockedPickup=WorldItemDropFactory.CreateWorldPickup(blocked,new Vector3(7000,0,7000),inventory,equipment.transform);
        Check(!blockedPickup.TryPickup(inventory)&&blockedPickup!=null&&!session.Read().items.Any(x=>x.instanceId==blocked.runtimeInstanceId),"full inventory leaves world gem and no ownership");
        UnityEngine.Object.DestroyImmediate(blockedPickup.gameObject);
        Check(ElementGemEquipmentService.EquipFromInventorySlot(inventory.FindFirstMatchingItemIndex(gem)),"equip from full inventory frees its source slot");
        var extra=new ItemData(common,1,common.fixedGrade);Check(inventory.AddItem(extra),"refill vacated slot");
        long revision=session.Revision;
        Check(!ElementGemEquipmentService.UnequipToInventory()&&equipment.EquippedElementGem.runtimeInstanceId==gem.runtimeInstanceId&&session.Revision==revision,"unequip full inventory preserves gem and revision");
        foreach(var value in filler) inventory.ConsumeItem(value,1);
        inventory.ConsumeItem(extra,1);
        Check(ElementGemEquipmentService.UnequipToInventory(),"unequip after freeing capacity");
        // Real encounter death invokes the product's natural drop source.
        string run="gem-natural-"+Guid.NewGuid().ToString("N");
        var map=new MapInstanceState { level=25,grade=ItemGrade.Common,options=new List<MapOptionRoll>() };
        Check(session.ExecuteState(run+"-entry",state=>{AccountRunCommands.PrepareEntry(state,run,map,null,true);AccountRunCommands.Activate(state,run);}),"run enters authoritative state");
        SetWorldPhase(WorldPhase.Run);
        var enemy=new GameObject("ElementGemNaturalBoss");enemy.transform.position=new Vector3(7000,0,7000);
        try
        {
            var health=enemy.AddComponent<CombatHealth>();var rank=enemy.AddComponent<EnemyRank>();rank.ConfigureTemporaryBoss(new EncounterContext(run,25,ItemGrade.Common));
            var drop=enemy.AddComponent<EnemyLootDropper>();drop.Configure(null,inventory,equipment.transform,null);drop.ConfigureEncounter(new EncounterContext(run,25,ItemGrade.Common));
            var before=new List<WorldItemPickup>();WorldItemPickup.CopyActivePickups(before);var old=new HashSet<int>(before.Select(x=>x.GetInstanceID()));
            health.TakeDamage(new DamageInfo(1000000,enemy.transform.position,equipment.gameObject,triggersOnHitEffects:false));
            var after=new List<WorldItemPickup>();WorldItemPickup.CopyActivePickups(after);
            var natural=after.Where(x=>!old.Contains(x.GetInstanceID())&&x.RuntimeItem?.baseData is ElementGemItemData).ToArray();
            Check(natural.Length>=1 && natural.All(x=>x.RuntimeItem.level==25&&x.RuntimeItem.originRunId==run),"actual boss death drops source-level run gem");
            var farmed=natural[0].RuntimeItem; Check(natural[0].TryPickup(inventory),"natural gem actually acquired");
            Check(ElementGemEquipmentService.EquipFromInventorySlot(inventory.FindFirstMatchingItemIndex(farmed)),"run loot equips to scalar slot");
            Check(session.ExecuteState(run+"-fail",state=>AccountRunCommands.Fail(state,run)),"actual run failure command");
            Check(equipment.EquippedElementGem==null&&!session.Read().items.Any(x=>x.instanceId==farmed.runtimeInstanceId)&&session.Read().items.Any(x=>x.instanceId==gem.runtimeInstanceId),"run failure removes new equipped gem and retains prior gem");
            foreach(var pickup in after)if(pickup!=null&&!old.Contains(pickup.GetInstanceID()))UnityEngine.Object.DestroyImmediate(pickup.gameObject);
        }
        finally { UnityEngine.Object.DestroyImmediate(enemy); SetWorldPhase(WorldPhase.Hideout); }
        foreach(bool transfer in new[]{true,false})
        {
            run="gem-settle-"+Guid.NewGuid().ToString("N");
            var loot=new ItemData(common,25,common.fixedGrade);var saved=ItemSnapshotCodec.Capture(loot,registry);
            Check(session.ExecuteState(run+"-entry",state=>{AccountRunCommands.PrepareEntry(state,run,map,null,true);AccountRunCommands.Activate(state,run);AccountRunCommands.Acquire(state,run,saved);}),"settlement run acquire");
            Check(ElementGemEquipmentService.EquipFromInventorySlot(inventory.FindFirstMatchingItemIndex(loot)),"settlement loot equipped");
            if(transfer)
            {
                Check(session.ExecuteState(run+"-transfer",state=>AccountRunCommands.Transfer(state,run,"transfer-object",loot.runtimeInstanceId,registry)),"transfer equipped gem to stash");
                Check(session.ExecuteState(run+"-fail",state=>AccountRunCommands.Fail(state,run)),"failure after transfer");
                Check(session.Read().stashTabs.Any(x=>x.slots.Contains(loot.runtimeInstanceId))&&equipment.EquippedElementGem==null,"transferred gem survives failure and clears equipped reference");
            }
            else
            {
                Check(session.ExecuteState(run+"-clear",state=>AccountRunCommands.ClearBoss(state,run,DateTime.UtcNow.Ticks)),"boss-clear checkpoint");
                Check(session.Read().items.Single(x=>x.instanceId==loot.runtimeInstanceId).originRunId==run,"checkpoint is not extraction");
                Check(session.ExecuteState(run+"-extract",state=>AccountRunCommands.Extract(state,run)),"successful extraction");
                Check(session.Read().items.Single(x=>x.instanceId==loot.runtimeInstanceId).originRunId==null&&equipment.EquippedElementGem.runtimeInstanceId==loot.runtimeInstanceId,"extraction keeps equipped gem and clears run origin");
            }
        }
        session.FlushPendingSave();
        var restored=new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load();
        AccountInvariants.Validate(restored,registry);
        Check(restored.elementalGemInstanceId==equipment.EquippedElementGem.runtimeInstanceId,"disk restore retains extracted gem slot");
        Check(JsonConvert.SerializeObject(restored.items.Single(x=>x.instanceId==gem.runtimeInstanceId).gemState)==quality,"pickup/stash/trade/run save keeps original quality");
    }
}
