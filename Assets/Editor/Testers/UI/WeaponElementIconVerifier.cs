using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class WeaponElementIconVerifier
{
    const string Key = "WeaponElementIconVerifier";
    const string Weapon = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS024_TrainingIronGreatsword/GRS024_TrainingIronGreatsword.asset";
    static readonly WeaponElement[] Elements = { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light };
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static IEnumerator work;
    static int frame;
    static double deadline;
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");

    static WeaponElementIconVerifier() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output, bool restore)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Editor must be idle");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene")
            throw new InvalidOperationException("PersistentScene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.SetBool(Key + ".restore", restore);
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".status", "RUNNING");
        string account = Path.Combine(Path.GetDirectoryName(output), "IsolatedAccount");
        IsolatedSavePlayGuard.EnterIsolatedPlay(account);
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); errors.Clear(); frame = -1;
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            Application.runInBackground = true;
            deadline = EditorApplication.timeSinceStartup + 180;
            work = Verify();
            Application.logMessageReceived += Log;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, null);
            SessionState.SetBool(Key, false);
        }
    }

    static void Log(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
    }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Play verification timed out");
            if (!work.MoveNext()) Finish();
        }
        catch (Exception exception) { checks.Add("FAIL " + exception.Message); Finish(); }
    }

    static void Finish()
    {
        string status = errors.Count == 0 && checks.All(c => c.StartsWith("PASS ")) ? "PASS" : "FAIL";
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "play-results.json"), JsonConvert.SerializeObject(new { status, checks, errors }, Formatting.Indented));
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }

    static void Check(bool pass, string description)
    {
        checks.Add((pass ? "PASS " : "FAIL ") + description);
        if (!pass) throw new InvalidOperationException(description);
    }

    static WeaponElementIconView Icon(SlotUI slot) => slot.transform.Find("Weapon Element Badge")?.GetComponent<WeaponElementIconView>();
    static bool Shows(WeaponElementIconView view, WeaponElement element)
    {
        Sprite expected = element == WeaponElement.None ? null : AssetDatabase.LoadAssetAtPath<Sprite>(
            WeaponElementIconBuilder.IconRoot + "Icon_WeaponElement_" + element + ".png");
        return view != null && view.DisplayedElement == element && view.DisplayedSprite == expected;
    }

    static IEnumerator Verify()
    {
        while (PlayerContext.Instance == null || PlayerContext.Instance.CurrentActorEquipment == null
            || !WorldSessionState.IsHideout) yield return null;
        var equipment = PlayerContext.Instance.CurrentActorEquipment;
        var inventory = PlayerContext.Instance.CurrentActorInventory;
        var game = Object.FindFirstObjectByType<OverburstGameUI>();
        var hud = Object.FindFirstObjectByType<WeaponElementHudIcon>();
        var tooltip = Object.FindFirstObjectByType<OverburstGameTooltip>(FindObjectsInactive.Include);
        Check(game != null && hud != null && tooltip != null, "Product UI loaded");
        if (SessionState.GetBool(Key + ".restore", false))
        {
            for (int n = 0; n < 15; n++) yield return null;
            Check(equipment.CurrentWeaponItem?.ResolvedElement == WeaponElement.Electric, "Saved equipped element restored");
            Check(Shows(hud.View, WeaponElement.Electric), "HUD icon restored on second Play");
            game.ToggleEquipment();
            for (int n = 0; n < 10; n++) yield return null;
            var equippedSlot = game.equipmentWindow.GetComponentsInChildren<SlotUI>(true).First(s => s.IsWeaponSlot);
            Check(Shows(Icon(equippedSlot), WeaponElement.Electric), "Equipped slot icon restored on second Play");
            ScreenCapture.CaptureScreenshot(Path.Combine(Output, "restored.png"));
            for (int n = 0; n < 4; n++) yield return null;
            yield break;
        }
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>(Weapon);
        Check(weapon != null, "Active weapon asset loaded");
        ItemData[] items = Elements.Select(e => new ItemData(weapon, 1, ItemGrade.Rare, element: e)).ToArray();
        foreach (ItemData item in items) Check(inventory.AddItem(item), "Inventory add " + item.ResolvedElement);
        game.inventory.SetVisible(true);
        game.ToggleEquipment();
        for (int n = 0; n < 15; n++) yield return null;
        foreach (ItemData item in items)
        {
            var slot = game.inventoryWindow.GetComponentsInChildren<SlotUI>(true).First(s => s.DisplayItem?.runtimeInstanceId == item.runtimeInstanceId);
            Check(Shows(Icon(slot), item.ResolvedElement), "Inventory instance icon " + item.ResolvedElement);
        }
        var shopSlot = Object.FindObjectsByType<SlotUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .First(s => s.transform.Find("Weapon Element Badge") != null && s.GetComponent<OverburstUIItemSlotView>() == null);
        var stashSlot = game.stashWindow.GetComponentsInChildren<SlotUI>(true).First();
        ItemData shopBefore = shopSlot.DisplayItem, stashBefore = stashSlot.DisplayItem;
        foreach (ItemData item in items)
        {
            shopSlot.SetDisplayItem(item);
            stashSlot.SetDisplayItem(item);
            Check(Shows(Icon(shopSlot), item.ResolvedElement), "Shop slot projection " + item.ResolvedElement);
            Check(Shows(Icon(stashSlot), item.ResolvedElement), "Stash slot projection " + item.ResolvedElement);
            tooltip.Show(item);
            var tooltipIcon = tooltip.GetComponentsInChildren<WeaponElementIconView>(true)
                .First(v => v.transform.parent.name == "Approved Icon Frame");
            Check(Shows(tooltipIcon, item.ResolvedElement), "Tooltip instance icon " + item.ResolvedElement);
            Check(equipment.EquipWeaponItem(item), "Equip actual weapon " + item.ResolvedElement);
            Check(Shows(hud.View, item.ResolvedElement), "Immediate equipped event updates HUD " + item.ResolvedElement);
            for (int n = 0; n < 10; n++) yield return null;
            var equippedSlot = game.equipmentWindow.GetComponentsInChildren<SlotUI>(true).First(s => s.IsWeaponSlot);
            Check(Shows(Icon(equippedSlot), item.ResolvedElement), "Equipped weapon slot " + item.ResolvedElement);
            Check(Icon(equippedSlot).IsVisible, "Equipped badge visible " + item.ResolvedElement);
            var badgeRect = (RectTransform)Icon(equippedSlot).transform;
            var backing = badgeRect.Find("Backing")?.GetComponent<Image>();
            Check(badgeRect.anchorMin == Vector2.up && backing != null && backing.color.a >= .9f,
                "Equipped badge separated from weapon handle " + item.ResolvedElement);
            tooltip.Hide();
            ScreenCapture.CaptureScreenshot(Path.Combine(Output, item.ResolvedElement + ".png"));
            for (int n = 0; n < 4; n++) yield return null;
        }
        tooltip.Hide();
        shopSlot.SetLocked(true);
        Check(Shows(Icon(shopSlot), WeaponElement.None), "Locked slot hides element");
        shopSlot.SetLocked(false);
        Check(Shows(Icon(shopSlot), WeaponElement.Light), "Unlock restores instance element");
        shopSlot.SetDisplayItem(null);
        Check(Shows(Icon(shopSlot), WeaponElement.None), "Empty slot clears previous element");
        var nonWeapon = AssetDatabase.FindAssets("t:FlaskItemData", new[] { "Assets/ProjectOverburst" })
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<FlaskItemData>).First();
        shopSlot.SetDisplayItem(new ItemData(nonWeapon, 1, ItemGrade.Common));
        Check(Shows(Icon(shopSlot), WeaponElement.None), "Nonweapon does not inherit element");
        shopSlot.SetDisplayItem(new ItemData(weapon, 1, ItemGrade.Rare, element: WeaponElement.None));
        Check(Shows(Icon(shopSlot), WeaponElement.None), "None element has no badge");
        shopSlot.SetDisplayItem(shopBefore); stashSlot.SetDisplayItem(stashBefore);
        equipment.ClearCurrentWeapon();
        Check(Shows(hud.View, WeaponElement.None), "Unequip clears HUD immediately");
        Check(equipment.EquipWeaponItem(items[2]), "Reequip electric weapon for saved reentry");
        hud.gameObject.SetActive(false);
        hud.gameObject.SetActive(true);
        Check(Shows(hud.View, WeaponElement.Electric), "HUD disable and reenable rebinds equipment");
        var weaponSlot = game.equipmentWindow.GetComponentsInChildren<SlotUI>(true).First(s => s.IsWeaponSlot);
        game.CloseEquipment();
        Icon(weaponSlot).Present(WeaponElement.None);
        game.ToggleEquipment();
        for (int n = 0; n < 10; n++) yield return null;
        Check(Shows(Icon(weaponSlot), WeaponElement.Electric) && Icon(weaponSlot).IsVisible,
            "Equipment reopen restores equipped badge after cleared presentation");
        Check(Shows(hud.View, WeaponElement.Electric), "Window reopen preserves HUD element");
        Check(Object.FindFirstObjectByType<AccountAutosave>().FlushNow(), "Isolated loadout saved");
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "final-electric.png"));
        for (int n = 0; n < 4; n++) yield return null;
    }
}
