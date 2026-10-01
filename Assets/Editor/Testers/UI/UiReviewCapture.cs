using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only: UI·HUD 다듬기 후보를 고르려고 대표 화면을 실제 Play(격리 계정)에서 촬영한다. 저장하지 않는다.
// 1 은신처 기본 HUD  2 전투 HUD(적 6, 피해 숫자·치명타·상태, 빛 과충전, 플레이어 체력 60%)
// 3 체력 25%  4 인벤토리+장비창  5 무기 툴팁  6 물약 툴팁
[InitializeOnLoad]
public static class UiReviewCapture
{
    const string Key = "UiReviewCapture";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> shots = new List<string>(), notes = new List<string>(), errors = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static UiReviewCapture() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.EraseString(Key + ".env");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".status", "RUNNING");
        EditorApplication.EnterPlaymode();
    }
    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground); Application.runInBackground = true;
            shots.Clear(); notes.Clear(); errors.Clear(); stack.Clear(); frame = -1;
            deadline = EditorApplication.timeSinceStartup + 300;
            work = Capture();
            Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null); SessionState.EraseString(Key + ".env");
            SessionState.SetBool(Key, false);
        }
    }
    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(m + "\n" + s); }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout"); if (Step()) return; Finish("COMPLETE"); }
        catch (Exception e) { Finish("FAIL " + e); }
    }
    static bool Step()
    {
        if (stack.Count == 0 && work != null) { stack.Push(work); work = null; }
        while (stack.Count > 0)
        {
            IEnumerator top = stack.Peek();
            if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } return true; }
            stack.Pop();
        }
        return false;
    }
    static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonConvert.SerializeObject(new { status, screen = new[] { Screen.width, Screen.height }, shots, notes, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }
    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }
    static IEnumerator Shot(string name)
    {
        yield return null;
        string path = Path.Combine(Output, (shots.Count + 1).ToString("00") + "_" + name + ".png");
        ScreenCapture.CaptureScreenshot(path); shots.Add(path);
        yield return null; yield return null;
    }
    static void Warp(PlayerInputFacade player, Vector3 p, Vector3 facing)
    {
        var cc = player.GetComponent<CharacterController>(); bool on = cc != null && cc.enabled;
        if (on) cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(facing);
        if (on) cc.enabled = true; Physics.SyncTransforms();
    }

    static IEnumerator Capture()
    {
        EnemySpawnService spawn = null; var leased = new List<EnemyActor>(); EnemyThemeTrialHarness debug = null;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            yield return Wait(2f);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            notes.Add("screen " + Screen.width + "x" + Screen.height);

            // 1) 은신처 기본 HUD
            yield return Shot("hideout_idle");

            // 아이템 준비: 무기(빛, 에픽), 물약, 소비품 여러 개
            var all = AssetDatabase.FindAssets("t:BaseItemData", new[] { "Assets/ProjectOverburst" })
                .Select(g => AssetDatabase.LoadAssetAtPath<BaseItemData>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x && x.icon).ToArray();
            var weaponAsset = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            var light = new ItemData(weaponAsset, 1, ItemGrade.Epic, element: WeaponElement.Light);
            var inv = PlayerContext.Instance.CurrentActorInventory;
            var extras = new List<ItemData>();
            var grades = new[] { ItemGrade.Common, ItemGrade.Rare, ItemGrade.Epic, ItemGrade.Mythic };
            int gi = 0;
            foreach (var w in all.OfType<WeaponItemData>().Take(3)) extras.Add(new ItemData(w, 1, grades[gi++ % grades.Length], element: WeaponElement.Fire));
            foreach (var f in all.OfType<FlaskItemData>().Take(2)) extras.Add(new ItemData(f, 1, grades[gi++ % grades.Length]));
            foreach (var c in all.OfType<ConsumableItemData>().Where(x => !(x is FlaskItemData) && !x.IsPermanentSingleItem).Take(3)) { var it = new ItemData(c, 1, ItemGrade.Rare); it.stackCount = 7; extras.Add(it); }
            foreach (var it in extras) if (!inv.AddItem(it)) notes.Add("add failed " + it.baseData.name);
            if (!actor.Equipment.EquipWeaponItem(light)) notes.Add("equip light failed");
            yield return Wait(0.5f);

            // 2) 전투 HUD
            debug = EnemyThemeTrialHarness.Current; if (!debug.InArena) debug.ToggleArena();
            yield return Wait(0.8f);
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            var cam = Camera.main; var origin = player.transform.position; var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var table in debug.tables) spawn.RegisterAdditionalCatalog(table.Catalog, out _);
            var defs = debug.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            string[] ids = { "CavernMutants_Ursacetus", "SpiderBrood_Horridomorph", "SpiderBrood_Rostrokarck", "DeathHarvest_Reaper", "VenomBrood_Kupolojuve_Tint_Orange", "SpiderBrood_Carcinoptera" };
            var spots = new[] { new Vector2(.30f, .62f), new Vector2(.45f, .70f), new Vector2(.62f, .66f), new Vector2(.74f, .52f), new Vector2(.28f, .40f), new Vector2(.68f, .34f) };
            for (int i = 0; i < ids.Length; i++)
            {
                var def = defs.FirstOrDefault(d => d.EnemyId == ids[i]) ?? defs[i % defs.Length];
                Vector3 at = Ground(spots[i].x, spots[i].y); Vector3 look = origin - at; look.y = 0f;
                if (!spawn.TrySpawn(new EnemySpawnRequest(def, at, Quaternion.LookRotation(look), player.transform), out var e)) { notes.Add("spawn failed " + ids[i]); continue; }
                leased.Add(e); e.Movement.StopMovement(); e.Health.SetMaxHp(5000, true);
            }
            // AI stays on (as in a real fight) so the AI-state label would show if it were still on by default.
            actor.Health.SetMaxHp(1000000, true);
            yield return Wait(0.8f);
            actor.Health.SetMaxHp(1000, true);
            var energy = actor.Equipment.GetComponent<OverburstElementEnergy>() ?? actor.Equipment.gameObject.AddComponent<OverburstElementEnergy>();
            typeof(OverburstElementEnergy).GetProperty("Amount").GetSetMethod(true).Invoke(energy, new object[] { 150f });
            typeof(OverburstElementEnergy).GetProperty("RadianceStacks").GetSetMethod(true).Invoke(energy, new object[] { 40 });
            actor.Health.TakeDamage(new DamageInfo(400f, actor.transform.position + Vector3.up, leased.Count > 0 ? leased[0].gameObject : null, Vector3.forward));
            var statuses = new[] { WeaponElement.Fire, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Fire, WeaponElement.Ice, WeaponElement.None };
            for (int i = 0; i < leased.Count; i++)
            {
                var e = leased[i]; var body = e.GetComponent<CombatTarget>();
                if (statuses[i] != WeaponElement.None)
                    for (int k = 0; k < 1 + i % 3; k++)
                        e.GetComponent<ElementalStatusController>()?.TryApplyDirectHit(new ElementalStatusApplication(statuses[i], 100, player.gameObject, energy.WeaponInstanceId, true, false, e.transform.position, Vector3.forward));
                for (int k = 0; k < 3; k++)
                    e.Health.TakeDamage(new DamageInfo(80f + 173f * k + 41f * i, body.CurrentHurtVolume.Center, player.gameObject, Vector3.forward, isCritical: k == 2, element: statuses[i], playerAttackKind: PlayerAttackKind.Weak));
            }
            yield return Wait(0.25f);
            foreach (var tmp in UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                string p = tmp.name; for (var x = tmp.transform.parent; x != null; x = x.parent) p = x.name + "/" + p;
                if (!(p.Contains("EnemyTargetHpHud/Bar/HpText") || p.Contains("ZoomValue"))) continue; // 2026-10-01 대상 HUD 재구성: HpText는 Bar 아래
                var cr = tmp.canvasRenderer;
                var subs = tmp.GetComponentsInChildren<TMPro.TMP_SubMeshUI>(true).Select(s => s.sharedMaterial ? s.sharedMaterial.name : "null");
                notes.Add($"TMP {p}: text='{tmp.text}' size={tmp.fontSize} col={tmp.color} shared={tmp.fontSharedMaterial?.name} render={tmp.materialForRendering?.name} cr0={(cr.materialCount > 0 ? cr.GetMaterial(0)?.name : "none")} underlay={tmp.materialForRendering?.IsKeywordEnabled("UNDERLAY_ON")} outlineW={tmp.materialForRendering?.GetFloat("_OutlineWidth")} alpha={cr.GetInheritedAlpha()} subs=[{string.Join(",", subs)}] scale={tmp.canvas?.scaleFactor}");
            }
            yield return Shot("combat_hits");
            foreach (var e in leased) if (e != null) { e.AI.enabled = false; e.Movement.StopMovement(); }
            yield return Wait(1.2f);
            yield return Shot("combat_after");

            // 3) 체력 25%
            actor.Health.TakeDamage(new DamageInfo(350f, actor.transform.position + Vector3.up, leased.Count > 0 ? leased[0].gameObject : null, Vector3.forward));
            yield return Wait(0.8f);
            yield return Shot("combat_lowhp");
            foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
            leased.Clear();
            if (debug.InArena) debug.ToggleArena();
            yield return Wait(0.8f);

            // 4) 인벤토리 + 장비창, 5·6) 툴팁
            var ui = UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>();
            notes.Add("debug defaults: attackPattern=" + CombatDebugSettings.ShowAttackPatternDebug + " aiState=" + CombatDebugSettings.ShowEnemyAiStateDebug + " aimLine=" + UnifiedDebugAimLine.DebugLineEnabled);
            notes.Add("player level " + (PlayerProgression.Current ? PlayerProgression.Current.Level : -1));
            string Label(UnityEngine.UI.Button b) => b ? (b.GetComponentInChildren<UnityEngine.UI.Text>(true)?.text ?? b.GetComponentInChildren<TMPro.TMP_Text>(true)?.text) : "null";
            ui.inventory.SetVisible(true); yield return Wait(0.3f);
            notes.Add("inventory only: equipmentButton='" + Label(ui.equipmentButton) + "'");
            yield return Shot("inventory_only");
            ui.equipmentButton.onClick.Invoke(); ui.Refresh();
            yield return Wait(0.6f);
            notes.Add("both open: equipmentButton='" + Label(ui.equipmentButton) + "' inventoryButton='" + Label(ui.inventoryButton) + "' equipOpen=" + ui.equipmentWindow.gameObject.activeSelf);
            yield return Shot("inventory_equipment");
            ui.inventoryButton.onClick.Invoke(); yield return Wait(0.3f);
            notes.Add("after inventoryButton: inventoryVisible=" + ui.inventory.IsVisible + " inventoryButton='" + Label(ui.inventoryButton) + "'");
            yield return Shot("equipment_only");
            ui.inventoryButton.onClick.Invoke(); yield return Wait(0.3f);
            notes.Add("after inventoryButton again: inventoryVisible=" + ui.inventory.IsVisible + " inventoryButton='" + Label(ui.inventoryButton) + "'");
            TooltipManager.Instance.ShowTooltip(extras.FirstOrDefault(x => x.baseData is WeaponItemData) ?? light);
            yield return Wait(0.3f);
            yield return Shot("tooltip_weapon");
            TooltipManager.Instance.HideTooltip();
            foreach (var grade in new[] { ItemGrade.Epic, ItemGrade.Mythic })
            {
                var hi = new ItemData(weaponAsset, 1, grade, element: WeaponElement.Fire);
                TooltipManager.Instance.ShowTooltip(hi); yield return Wait(0.3f);
                yield return Shot("tooltip_weapon_" + grade);
                TooltipManager.Instance.HideTooltip();
            }
            var flask = extras.FirstOrDefault(x => x.baseData is FlaskItemData);
            if (flask != null) { TooltipManager.Instance.ShowTooltip(flask); yield return Wait(0.3f); yield return Shot("tooltip_flask"); TooltipManager.Instance.HideTooltip(); }
            ui.CloseEquipment(); ui.inventory.SetVisible(false);
            yield return Wait(0.3f);
        }
        finally
        {
            if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
        }
    }
}
