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
public static class StatusBuffIconVerifier
{
    const string Key = "StatusBuffIconVerifier";
    const string WeaponPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS024_TrainingIronGreatsword/GRS024_TrainingIronGreatsword.asset";
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static IEnumerator work;
    static int frame;
    static double deadline;
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static StatusBuffIconVerifier() => EditorApplication.playModeStateChanged += State;

    public static string ValidateAssets()
    {
        var results = new List<string>();
        void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); results.Add("PASS " + message); }
        string[] icons = Directory.GetFiles(StatusBuffIconBuilder.IconRoot, "*.png", SearchOption.AllDirectories);
        Require(icons.Length == 23, "23 original icons");
        foreach (string file in icons)
        {
            string path = file.Replace('\\', '/');
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            byte[] png = File.ReadAllBytes(path);
            int width = png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19];
            int height = png[20] << 24 | png[21] << 16 | png[22] << 8 | png[23];
            Require(width == 1254 && height == 1254 && png[25] == 6 && sprite != null
                && importer.textureType == TextureImporterType.Sprite && importer.alphaIsTransparency
                && !importer.mipmapEnabled && !importer.isReadable && importer.maxTextureSize == 256,
                Path.GetFileNameWithoutExtension(path) + " RGBA source and Sprite settings");
        }
        foreach (string path in StatusBuffIconBuilder.Prefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Require(prefab != null && prefab.GetComponentsInChildren<Transform>(true)
                .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)) == 0, Path.GetFileName(path) + " loads without missing scripts");
            foreach (ElementalStatusIconStrip strip in prefab.GetComponentsInChildren<ElementalStatusIconStrip>(true))
            {
                var so = new SerializedObject(strip);
                Require(new[] { "fireIcon", "iceIcon", "electricIcon", "darkIcon", "lightIcon", "freezeIcon", "stunIcon" }
                    .All(f => so.FindProperty(f).objectReferenceValue != null), Path.GetFileName(path) + " status references");
            }
            foreach (EnemyTargetStatusRow row in prefab.GetComponentsInChildren<EnemyTargetStatusRow>(true))
                Require(new SerializedObject(row).FindProperty("cells").arraySize == 6, "Target row reserves independent stun cell");
        }
        var bar = AssetDatabase.LoadAssetAtPath<GameObject>(StatusBuffIconBuilder.HudPath).GetComponentInChildren<BuffBarUI>(true);
        Require(new SerializedObject(bar).FindProperty("slots").arraySize == BuffBarUI.SlotCount, "7 authored buff slots in one row");
        foreach (BuffIconSlotUI slot in Slots(bar))
        {
            var polarity = slot.transform.Find("Polarity")?.GetComponent<BuffPolarityIndicatorUI>();
            Require(polarity != null && polarity.GetComponent<CanvasRenderer>() != null && !polarity.raycastTarget && polarity.rectTransform.sizeDelta == new Vector2(10f, 12f),
                slot.name + " has a small non-interactive polarity marker");
            Require(slot.transform.Find("Value").GetComponent<TMPro.TMP_Text>().alignment == TMPro.TextAlignmentOptions.BottomLeft,
                slot.name + " keeps time and stacks clear of its marker");
        }
        foreach (BuffIconSlotUI slot in bar.GetComponentsInChildren<BuffIconSlotUI>(true))
            Require(slot.GetComponentsInChildren<Image>(true).All(i => !i.raycastTarget), slot.name + " leaves gameplay input available");
        var cards = AssetDatabase.LoadAssetAtPath<RunCardIconSet>(StatusBuffIconBuilder.CardPath);
        foreach (RunCardIconSet.Entry entry in cards.Entries.Where(e => e.Key.StartsWith("buff_")))
            Require(entry.Icon == StatusBuffIcons.Map((MapBuffKind)Enum.Parse(typeof(MapBuffKind), entry.Key.Substring(5))), entry.Key + " card icon");
        File.WriteAllText(Path.Combine(StatusBuffIconBuilder.Output, "asset-results.json"), JsonConvert.SerializeObject(new { status = "PASS", checks = results }, Formatting.Indented));
        return "PASS " + results.Count;
    }

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Editor must be idle");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("PersistentScene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", Path.GetFullPath(output));
        SessionState.SetString(Key + ".status", "RUNNING");
        SessionState.SetBool(Key, true);
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output, "IsolatedAccount"));
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); errors.Clear(); frame = -1;
            deadline = EditorApplication.timeSinceStartup + 180;
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            Application.runInBackground = true;
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
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Play timed out");
            if (!work.MoveNext()) Finish();
        }
        catch (Exception exception) { checks.Add("FAIL " + exception); Finish(); }
    }
    static void Finish()
    {
        string status = errors.Count == 0 && checks.All(c => c.StartsWith("PASS ")) ? "PASS" : "FAIL";
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "play-results.json"), JsonConvert.SerializeObject(new { status, checks, errors, width = Screen.width, height = Screen.height }, Formatting.Indented));
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }
    static void Check(bool ok, string name) { checks.Add((ok ? "PASS " : "FAIL ") + name); if (!ok) throw new InvalidOperationException(name); }
    static BuffIconSlotUI[] Slots(BuffBarUI bar) => bar.GetComponentsInChildren<BuffIconSlotUI>(true).Where(s => s.gameObject.activeSelf && s.name.StartsWith("BuffIconSlot_")).ToArray();
    static bool Shows(BuffBarUI bar, string key, Sprite sprite) => Slots(bar).Any(s => s.DisplayedKey == key && s.DisplayedSprite == sprite);
    static bool Overflow(BuffBarUI bar) => ((TMPro.TMP_Text)new SerializedObject(bar).FindProperty("moreIndicator").objectReferenceValue).gameObject.activeSelf;
    static bool Polarity(BuffBarUI bar, string key, bool debuff) => Slots(bar).Any(s => s.DisplayedKey == key
        && s.DisplayedIsDebuff == debuff && s.transform.Find("Polarity").GetComponent<BuffPolarityIndicatorUI>().IsDebuff == debuff
        && s.transform.Find("Polarity").GetComponent<BuffPolarityIndicatorUI>().enabled);

    static IEnumerator Verify()
    {
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) yield return null;
        Check(AccountBootstrap.SaveDirectory.StartsWith(Output, StringComparison.OrdinalIgnoreCase), "Isolated account used");
        var player = PlayerContext.Instance.CurrentActor;
        var bar = Object.FindFirstObjectByType<BuffBarUI>();
        Check(bar != null && Slots(bar).Length == BuffBarUI.SlotCount, "Product HUD contains 7 single-row slots");
        var buffs = player.GetComponent<PlayerBuffController>();
        var flasks = player.GetComponent<PlayerFlaskController>();
        Check(buffs != null && flasks != null, "Product buff and flask controllers");
        buffs.ApplyBuff(new BuffDefinition { buffId = "icon_healing_fixture", duration = 12f, initialHealPercent = 0f, healPercentPerTick = .01f });
        buffs.ApplyBuff(new BuffDefinition { buffId = "icon_haste_fixture", duration = 12f, initialHealPercent = 0f, healPercentPerTick = 0f, moveSpeedMultiplier = 1.1f });
        buffs.ApplySlowDebuff(12f, .9f);
        bar.Refresh();
        Check(Shows(bar, "icon_healing_fixture", StatusBuffIcons.Status("healing")), "Recovery uses healing icon");
        Check(Shows(bar, "icon_haste_fixture", StatusBuffIcons.Status("haste")), "Haste uses haste icon");
        Check(Shows(bar, PlayerBuffController.SlowBuffId, StatusBuffIcons.Status("slow")), "Slow stays upright");
        Check(Polarity(bar, "icon_healing_fixture", false) && Polarity(bar, "icon_haste_fixture", false), "Beneficial effects have upward green markers");
        Check(Polarity(bar, PlayerBuffController.SlowBuffId, true), "Slow has a downward red marker");
        var kinds = new[] { FlaskKind.Regeneration, FlaskKind.Berserker, FlaskKind.Giant, FlaskKind.Executioner, FlaskKind.Ironclad, FlaskKind.Ghost };
        for (int batch = 0; batch < 2; batch++)
        {
            for (int i = 0; i < 3; i++)
            {
                FlaskKind kind = kinds[batch * 3 + i];
                var data = Resources.Load<FlaskItemData>("Items/Flasks/Flask_" + kind);
                var item = new ItemData(data, 1, ItemGrade.Common);
                Check(player.Inventory.AddItem(item), "Inventory add " + kind);
                Check(flasks.TryEquip(i, item, out string equipReason), "Equip " + kind + " " + equipReason);
                Check(flasks.TryUse(i, out string useReason), "Use " + kind + " " + useReason);
                bar.Refresh();
                Check(Shows(bar, "flask_" + kind, StatusBuffIcons.Flask(data)), "Active symbol " + kind);
            }
            yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(Output, "flask-batch-" + batch + ".png"));
            yield return null;
            flasks.ClearEffects(); bar.Refresh();
            Check(!Slots(bar).Any(s => s.DisplayedKey != null && s.DisplayedKey.StartsWith("flask_")), "Flask clearing removes symbols");
        }
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>(WeaponPath);
        var light = new ItemData(weapon, 1, ItemGrade.Common, element: WeaponElement.Light);
        Check(player.Inventory.AddItem(light) && player.Equipment.EquipWeaponItem(light), "Equip light weapon");
        var energy = player.GetComponent<OverburstElementEnergy>();
        for (int i = 1; i <= 40; i++) energy.RecordConfirmedHit(light.runtimeInstanceId, WeaponElement.Light, i, 100f);
        bar.Refresh();
        Check(energy.RadianceStacks > 0 && Shows(bar, "radiance", StatusBuffIcons.Status("radiance")), "Radiance reads player stacks");
        Check(energy.TryCommitDischarge(100f, out _), "Consume light discharge");
        bar.Refresh();
        Check(!Slots(bar).Any(s => s.DisplayedKey == "radiance"), "Discharge removes radiance symbol");

        GameObject fixture = new GameObject("StatusBuffIconFixture");
        EnemyActor enemy = null;
        EnemySpawnService spawn = null;
        try
        {
            var poolRoot = new GameObject("Pool"); poolRoot.transform.SetParent(fixture.transform); poolRoot.SetActive(false);
            var pool = fixture.AddComponent<EnemyPoolService>(); pool.Configure(poolRoot.transform, 0);
            var theme = MapThemeCatalog.Resolve("SpiderBrood");
            spawn = fixture.AddComponent<EnemySpawnService>(); spawn.Configure(theme.Catalog, pool);
            enemy = spawn.Spawn(new EnemySpawnRequest(theme.BuildRoster(1, 1, 0, 27100)[0], new Vector3(5000, 0, 5000), Quaternion.identity, player.transform, fixture, player.transform, fixture.transform));
            enemy.Health.SetMaxHp(100000f, true); enemy.AI.enabled = false; enemy.Movement.enabled = false;
            var hud = Object.FindFirstObjectByType<EnemyTargetHpHud>(FindObjectsInactive.Include);
            hud.ShowTarget(enemy.Health);
            var row = hud.GetComponentInChildren<EnemyTargetStatusRow>(true);
            var source = hud.GetComponentInChildren<ElementalStatusIconStrip>(true);
            var status = enemy.GetComponent<ElementalStatusController>();
            foreach (WeaponElement element in new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark })
            {
                Check(status.TryApplyDirectHit(new ElementalStatusApplication(element, 100f, player.gameObject, "icon-fixture", true, false, enemy.transform.position, Vector3.forward)), "Apply " + element);
                Check(source.TryGetIcon(element, out Sprite icon) && icon == StatusBuffIcons.Element(element), "Target symbol " + element);
            }
            yield return null;
            Check(row.VisibleCount == 4, "Four simultaneous elemental states");
            for (int i = 1; i < OverburstElementTuning.Current.maximumStacks; i++)
                status.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Ice, 100f, player.gameObject, "icon-freeze", true, false, enemy.transform.position, Vector3.forward));
            yield return null;
            Check(status.IsFrozen && source.TryGetIcon(WeaponElement.Ice, out Sprite frozen) && frozen == StatusBuffIcons.Status("freeze"), "Cold changes to freeze");
            enemy.GetComponent<EnemyMovementReaction>().ApplyParryStun(.6f);
            yield return null;
            Check(row.VisibleCount == 5 && source.TryGetStun(out _, out Sprite stunned) && stunned == StatusBuffIcons.Status("stun"), "Independent stun joins elemental states");
            ScreenCapture.CaptureScreenshot(Path.Combine(Output, "target-freeze-stun.png"));
            float until = Time.time + .8f; while (Time.time < until) yield return null;
            Check(!source.TryGetStun(out _, out _) && row.VisibleCount == 4, "Stun expiry removes only stun");
            status.ClearAllStatuses(); yield return null;
            Check(row.VisibleCount == 0, "Status clearing removes target symbols");
            hud.Clear();
        }
        finally { if (enemy != null) spawn.Release(enemy); Object.Destroy(fixture); }
        buffs.ApplyBuff(new BuffDefinition { buffId = "icon_expiry_fixture", duration = .3f, tickInterval = 1f, initialHealPercent = 0f, healPercentPerTick = 0f, moveSpeedMultiplier = 1.1f });
        bar.Refresh(); Check(Slots(bar).Any(s => s.DisplayedKey == "icon_expiry_fixture"), "Timed buff appears");
        float expireAt = Time.time + .5f; while (Time.time < expireAt) yield return null;
        bar.Refresh(); Check(!Slots(bar).Any(s => s.DisplayedKey == "icon_expiry_fixture"), "Timed buff expires through controller");

        Check(Object.FindFirstObjectByType<MapDungeonPortal>().EnterLevelOne(), "Enter actual map run");
        while (PersistentSceneFlow.Instance.IsSwitching || WorldSessionState.Phase != WorldPhase.Run) yield return null;
        var map = MapRunBuffs.Current;
        Check(map != null, "Product map buff owner");
        for (int i = 0; i < BuffBarUI.MapSlotCount; i++)
        {
            var kind = (MapBuffKind)i;
            Check(map.Add("icon_event_" + i, new MapCardChoice("icon_card_" + i, "아이콘 검증", "", "", MapCardKind.Buff, kind, ItemGrade.Common, kind == MapBuffKind.Armor ? 10f : .01f)), "Select map buff " + kind);
            bar.Refresh();
            int capacity = Mathf.Max(0, BuffBarUI.SlotCount - bar.VisibleTimedCount);
            Check(i < capacity ? Shows(bar, "map_" + kind, StatusBuffIcons.Map(kind)) : Overflow(bar), "Run symbol or overflow " + kind);
        }
        Check(map.Add("icon_event_duplicate", new MapCardChoice("icon_card_duplicate", "", "", "", MapCardKind.Buff, MapBuffKind.Attack, ItemGrade.Common, .01f)), "Repeated attack selection");
        bar.Refresh(); Check(bar.VisibleMapCount == Mathf.Min(BuffBarUI.MapSlotCount, BuffBarUI.SlotCount - bar.VisibleTimedCount)
            && Overflow(bar), "Same map buffs share one cell and extra types use ellipsis");
        yield return null;
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "map-buffs.png"));
        yield return null;
        player.Health.TakeDamage(new DamageInfo(1e9f, player.transform.position));
        while (PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) yield return null;
        bar.Refresh(); Check(bar.VisibleMapCount == 0, "Return clears map row");
        Check(Object.FindFirstObjectByType<MapDungeonPortal>().EnterLevelOne(), "Repeat map entry");
        while (PersistentSceneFlow.Instance.IsSwitching || WorldSessionState.Phase != WorldPhase.Run) yield return null;
        bar.Refresh(); Check(bar.VisibleMapCount == 0 && MapRunBuffs.Current.Selected.Count == 0, "New run starts without old buffs");
        player.Health.TakeDamage(new DamageInfo(1e9f, player.transform.position));
        while (PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) yield return null;
        bar.Refresh(); Check(bar.VisibleMapCount == 0 && bar.VisibleTimedCount == 0, "Death and return clear transient icons");
        Check(AccountGameplaySession.Current.FlushPendingSave(), "Flush isolated account");
    }
}
