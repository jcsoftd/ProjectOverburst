using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BagQualityPlayVerifier
{
    private const string Key = "BagQualityPlayVerifier";
    private static readonly List<string> checks = new List<string>(), errors = new List<string>();
    private static IEnumerator work;
    private static int frame;
    private static double deadline;
    private static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static BagQualityPlayVerifier() { EditorApplication.playModeStateChanged += State; }
    public static void Run(string output, bool restore)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Editor must be idle");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("PersistentScene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.SetBool(Key + ".restore", restore);
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".status", "RUNNING");
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Path.GetDirectoryName(output), "IsolatedAccount"));
    }
    private static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); errors.Clear(); frame = -1;
            SessionState.SetBool(Key + ".background", Application.runInBackground); Application.runInBackground = true;
            deadline = EditorApplication.timeSinceStartup + 120;
            work = Verify(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, null);
            SessionState.SetBool(Key, false);
        }
    }
    private static void Log(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Bag Play timed out");
            if (!work.MoveNext()) Finish();
        }
        catch (Exception error) { checks.Add("FAIL " + error); Finish(); }
    }
    private static void Finish()
    {
        string status = errors.Count == 0 && checks.All(x => x.StartsWith("PASS ")) ? "PASS" : "FAIL";
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "play-results.json"), JsonConvert.SerializeObject(new { status, checks, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }
    private static void Check(bool value, string detail)
    { checks.Add((value ? "PASS " : "FAIL ") + detail); if (!value) throw new InvalidOperationException(detail); }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
    private static object Call(object value, string method, params object[] args)
        => value.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, args);
    private static BagInstanceState Quality(BagStat a, BagStat b, BagStat c)
    {
        var state = new BagInstanceState { version = BagQuality.Version, seed = 7001 };
        state.rows.Add(new BagStatRoll { stat = BagStat.InventorySlots, stars = Enumerable.Repeat(WeaponGradeStarType.Yellow, 6).ToList() });
        state.rows.Add(new BagStatRoll { stat = a, stars = Enumerable.Repeat(WeaponGradeStarType.White, 4).ToList() });
        state.rows.Add(new BagStatRoll { stat = b, stars = Enumerable.Repeat(WeaponGradeStarType.White, 5).ToList() });
        state.rows.Add(new BagStatRoll { stat = c, stars = Enumerable.Repeat(WeaponGradeStarType.White, 5).ToList() });
        return state;
    }
    private static IEnumerator Verify()
    {
        while (!AccountBootstrap.Attempted || (!AccountBootstrap.Ready && AccountBootstrap.Error == null)) yield return null;
        Check(AccountBootstrap.Ready, "Isolated account boot " + AccountBootstrap.Error);
        for (int i = 0; i < 15; i++) yield return null;
        var session = AccountGameplaySession.Current;
        var game = Object.FindFirstObjectByType<OverburstGameUI>(FindObjectsInactive.Include);
        var inventory = PlayerAccountInventoryService.SharedInventory;
        var bridge = game.inventory.GetComponent<InventorySlotBridge>();
        game.inventory.SetVisible(true);
        for (int i = 0; i < 3; i++) yield return null;
        var slots = Field<SlotUI[]>(bridge, "inventorySlots");
        Check(inventory.Capacity == 48 && slots.Length == 48, "Actual account and bridge have 48 slots");
        var savedPath = Path.Combine(Path.GetDirectoryName(Output), "expected.json");
        if (SessionState.GetBool(Key + ".restore", false))
        {
            var expected = File.ReadAllText(savedPath);
            var saved = session.Read();
            Check(JsonConvert.SerializeObject(saved.items.Find(x => x.instanceId == saved.bags[0]).bag) == expected, "Saved bag exact stars on reentry");
            Check(saved.unlockedSlots == 47 && inventory.UnlockedSlotCount == 47, "Saved 47 unlocked slots restored");
            Check(saved.bagExperienceCarry == 9600 && saved.bagGoldCarry == 9600, "Fractional XP and gold carries restored");
            Check(BagTooltip.Details(PlayerAccountInventoryService.EquippedBag).Contains("◆"), "Saved bag tooltip retains stars");
            yield break;
        }
        Check(session.ExecuteState("bag-fixture-level", x => { x.level = 73; x.experience = 0; }), "Level fixture set in isolated account");
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var bagData = registry.Entries.Select(x => x.asset).OfType<BagItemData>().First();
        var big = new ItemData(bagData, 100, ItemGrade.Mythic) { bagState = Quality(BagStat.KillExperience, BagStat.GoldMagnetRadius, BagStat.CombatGold) };
        var small = new ItemData(bagData, 1, ItemGrade.Common);
        Check(inventory.AddItem(big) && inventory.AddItem(small), "Acquire two new bag instances");
        var context = PlayerContext.Instance;
        float hp = context.CurrentActorHealth.MaxHp;
        float speed = (float)Call(context.CurrentActorMovement, "GetTargetMoveSpeed");
        int bigIndex = inventory.Items.ToList().IndexOf(big);
        Check((bool)Call(bridge, "EquipBagFromInventorySlot", slots[bigIndex]), "Double-click route equips main bag");
        Check(inventory.UnlockedSlotCount == 47 && session.Read().unlockedSlots == 47, "Six yellow main stars unlock 31 additional slots");
        Check(Mathf.Approximately(hp, context.CurrentActorHealth.MaxHp) && Mathf.Approximately(speed, (float)Call(context.CurrentActorMovement, "GetTargetMoveSpeed")), "Bag never changes HP or movement speed");
        game.inventory.SetVisible(false); game.inventory.SetVisible(true);
        Check(PlayerAccountInventoryService.EquippedBag.runtimeInstanceId == big.runtimeInstanceId, "Close/reopen retains bag ownership");
        var currency = registry.Entries.Select(x => x.asset).OfType<CurrencyItemData>().First(x => x.currencyType == CurrencyType.Gold);
        var overflow = new ItemData(currency, 1, ItemGrade.Common, 1);
        Check(inventory.TryReplaceOwnedItemAt(40, null, overflow), "Place fixture in unlocked slot 40");
        int smallIndex = inventory.Items.ToList().IndexOf(small);
        Check((bool)Call(bridge, "EquipBagFromInventorySlot", slots[smallIndex]), "Swap to smaller bag while preserving overflow");
        Check(inventory.UnlockedSlotCount == 21 && inventory.GetItemAt(40).runtimeInstanceId == overflow.runtimeInstanceId && inventory.IsOverCapacity, "Overflow retained and smaller capacity applied");
        Check(!inventory.AddItem(new ItemData(currency, 1, ItemGrade.Common)), "Acquisition blocked while over capacity");
        var inventoryBig = inventory.Items.First(x => x != null && x.runtimeInstanceId == big.runtimeInstanceId);
        Check((bool)Call(bridge, "EquipBagFromInventorySlot", slots[inventory.Items.ToList().IndexOf(inventoryBig)]), "Reequip larger bag from owned inventory");
        Check(!inventory.IsOverCapacity && inventory.GetItemAt(40).runtimeInstanceId == overflow.runtimeInstanceId, "Overflow resolves without removing item");
        var equipped = PlayerAccountInventoryService.EquippedBag;
        var interactor = context.CurrentActorMovement.GetComponent<PlayerPickupInteractor>();
        float basePickup = Field<float>(interactor, "pickupRadius");
        Check(Mathf.Approximately(interactor.PickupRadius, basePickup), "No item pickup bonus from unrelated affixes");
        Check(session.Execute(() => { equipped.bagState.rows[2].stat = BagStat.ItemPickupDistance; return true; }), "Change isolated fixture to item pickup affix");
        Check(Mathf.Approximately(interactor.PickupRadius, basePickup * 1.225f), "Actual F/click/auto-walk pickup radius reads bag affix");
        Check(session.Execute(() => { equipped.bagState.rows[2].stat = BagStat.GoldMagnetRadius; return true; }), "Restore gold magnet fixture");
        var autoPickup = context.CurrentActorMovement.GetComponent<PlayerCurrencyAutoPickup>();
        float baseGold = Field<float>(autoPickup, "pickupRadius");
        Vector3 position = autoPickup.transform.position + Vector3.forward * (baseGold + .4f);
        var coin = WorldItemDropFactory.CreateCurrencyWorldPickup(currency, 1, position, inventory);
        var otherCurrency = registry.Entries.Select(x => x.asset).OfType<CurrencyItemData>().First(x => x.currencyType != CurrencyType.Gold);
        var other = WorldItemDropFactory.CreateCurrencyWorldPickup(otherCurrency, 1, position + Vector3.right * .1f, inventory);
        typeof(CurrencyWorldPickup).GetField("spawnTime", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(coin, Time.time - 10);
        Physics.SyncTransforms(); Call(autoPickup, "ScanNearbyCurrency");
        Check(Field<Transform>(coin, "magnetTarget") == autoPickup.transform, "Gold attracts from expanded radius");
        Check(Field<Transform>(other, "magnetTarget") == null, "Other currency keeps base radius");
        Object.Destroy(coin.gameObject); Object.Destroy(other.gameObject);
        int priorXp = PlayerProgression.Current.Experience;
        for (int i = 0; i < 49; i++) PlayerProgression.Current.AddKillExperience(1);
        Check(PlayerProgression.Current.FlushPendingExperience(), "Kill XP flush commits base and bonus together");
        Check(PlayerProgression.Current.Experience == priorXp + 50 && session.Read().bagExperienceCarry == 9600, "49 one-point kills at 4 percent -> 50 XP and .96 carry");
        int goldTotal = 0;
        for (int i = 0; i < 12; i++) goldTotal += BagFarmingLoot.CombatGoldAmount(1, BagQuality.EquippedBonus(BagStat.CombatGold));
        Check(goldTotal == 12 && session.Read().bagGoldCarry == 9600, "12 one-gold drops at 8 percent retain .96 carry");
        var extra = BagFarmingLoot.Extra(equipped, null, 73, ItemGrade.Common, 8);
        Check(extra.runtimeInstanceId != equipped.runtimeInstanceId && extra.level == 73 && BagQuality.IsValid(extra.bagState, extra.grade), "Extra farming drop rolls independent identity/grade/stars");
        var tooltip = Object.FindFirstObjectByType<TooltipManager>(FindObjectsInactive.Include);
        tooltip.ShowTooltip(equipped);
        Check(BagTooltip.Details(equipped).Contains("인벤토리 수납") && BagTooltip.Details(equipped).Contains("각인 +12칸"), "Four-row tooltip shows capacity and star increase");
        var skin = Field<OverburstGameTooltip>(tooltip, "rpgTooltip").view.GetComponent<OverburstTooltipHybridSkin>();
        var visibleMarks = skin.GetComponentsInChildren<TMPro.TextMeshProUGUI>(false).Where(x => x.name == "Marks" && x.text.Contains("<sprite index=")).ToArray();
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "tooltip-render-check.png"));
        for (int i = 0; i < 5; i++) yield return null;
        Check(visibleMarks.Length == 4 && visibleMarks.Sum(x => System.Text.RegularExpressions.Regex.Matches(x.text, "<sprite index=").Count) == 20,
            "Rendered tooltip shows all 20 stars in four rows: " + visibleMarks.Length + " rows, " + visibleMarks.Sum(x => System.Text.RegularExpressions.Regex.Matches(x.text, "<sprite index=").Count) + " stars");
        Check(Object.FindFirstObjectByType<AccountAutosave>().FlushNow(), "Bag, ownership and fractional carries saved to isolated A/B store");
        var savedBag = session.Read().items.Find(x => x.instanceId == equipped.runtimeInstanceId);
        File.WriteAllText(savedPath, JsonConvert.SerializeObject(savedBag.bag));
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "bag-tooltip.png"));
        for (int i = 0; i < 5; i++) yield return null;
        tooltip.HideTooltip();
    }
}
