using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// 2026-10-01 장착 무기 비교 검증: 격리 계정 Play에서 툴팁(좋음·나쁨·비슷함·장착 중·Alt 나란히·끔)과
// 인벤토리 ▲, 바닥 이름표 ▲▼를 촬영하고 화면 글자를 기록한다. 계정·씬을 저장하지 않는다.
[InitializeOnLoad]
public static class WeaponCompareCapture
{
    const string Key = "WeaponCompareCapture";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> shots = new List<string>(), notes = new List<string>(), errors = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static WeaponCompareCapture() { EditorApplication.playModeStateChanged += State; }

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
            deadline = EditorApplication.timeSinceStartup + 240;
            work = Capture();
            Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose(); work = null;
            EquippedWeaponComparison.SimulateAltForVerification = false;
            EquippedWeaponComparison.Enabled = true;
            EquippedWeaponComparison.GearEnabled = true;
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

    static IEnumerator Wait(float seconds) { float until = Time.unscaledTime + seconds; while (Time.unscaledTime < until) yield return null; }

    static IEnumerator Shot(string name)
    {
        yield return null;
        string path = Path.Combine(Output, (shots.Count + 1).ToString("00") + "_" + name + ".png");
        ScreenCapture.CaptureScreenshot(path); shots.Add(path);
        yield return null; yield return null;
    }

    static string Plain(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? string.Empty, "<[^>]+>", string.Empty).Replace("\n", " / ");

    // 화면에 떠 있는 승인 툴팁의 요약·비교 열·폭을 글자로 남긴다.
    static void DescribeTooltips(string label)
    {
        foreach (var view in UnityEngine.Object.FindObjectsByType<OverburstUITooltipView>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var t = view.transform;
            string Text(string path) { var x = t.Find(path); return x != null && x.gameObject.activeInHierarchy ? Plain(x.GetComponent<TMPro.TMP_Text>().text) : "-"; }
            var rows = new List<string>();
            var rowRoot = t.Find("Approved Content/Stat Rows");
            if (rowRoot != null)
                foreach (Transform row in rowRoot)
                    if (row.gameObject.activeSelf)
                        rows.Add(Plain(row.Find("Label").GetComponent<TMPro.TMP_Text>().text) + "=" + Plain(row.Find("Value").GetComponent<TMPro.TMP_Text>().text)
                            + (row.Find("Compare") != null && row.Find("Compare").gameObject.activeSelf ? "[" + Plain(row.Find("Compare").GetComponent<TMPro.TMP_Text>().text) + "]" : ""));
            notes.Add(label + " | view=" + view.name + " size=" + view.Rect.sizeDelta + " pos=" + view.Rect.anchoredPosition
                + " | title=" + Plain(t.Find("Item Name").GetComponent<TMPro.TMP_Text>().text)
                + " | equippedTag=" + (t.Find("Equipped Tag") != null && t.Find("Equipped Tag").gameObject.activeSelf)
                + " | heading=" + Text("Approved Content/Compare Heading") + " | rows=" + string.Join("; ", rows));
        }
    }

    static IEnumerator Capture()
    {
        var dropped = new List<WorldItemPickup>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            yield return Wait(2f);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            var inv = PlayerContext.Instance.CurrentActorInventory;
            notes.Add("screen " + Screen.width + "x" + Screen.height);

            var all = AssetDatabase.FindAssets("t:BaseItemData", new[] { "Assets/ProjectOverburst" })
                .Select(g => AssetDatabase.LoadAssetAtPath<BaseItemData>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x && x.icon).ToArray();
            var weapons = all.OfType<WeaponItemData>().ToArray();
            var baseWeapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            var equipped = new ItemData(baseWeapon, 1, ItemGrade.Rare, element: WeaponElement.Light);
            if (!actor.Equipment.EquipWeaponItem(equipped)) notes.Add("equip failed");
            yield return Wait(0.5f);

            ItemData better = null, worse = null, similar = null;
            foreach (var w in weapons)
            {
                if (better == null) foreach (var g in new[] { ItemGrade.Legendary, ItemGrade.Epic })
                {
                    var c = new ItemData(w, 1, g, element: WeaponElement.Fire);
                    if (EquippedWeaponComparison.GetVerdict(c) == WeaponCompareVerdict.Better) { better = c; break; }
                }
                if (worse == null)
                {
                    var c = new ItemData(w, 1, ItemGrade.Common, element: WeaponElement.Light);
                    if (EquippedWeaponComparison.GetVerdict(c) == WeaponCompareVerdict.Worse) worse = c;
                }
                if (better != null && worse != null) break;
            }
            for (int i = 0; i < 30 && similar == null; i++)
            {
                var c = new ItemData(baseWeapon, 1, ItemGrade.Rare, element: WeaponElement.Light);
                if (EquippedWeaponComparison.GetVerdict(c) == WeaponCompareVerdict.Similar) similar = c;
            }
            notes.Add("equipped=" + equipped.itemName + " better=" + better?.itemName + " worse=" + worse?.itemName + " similar=" + similar?.itemName);

            var energy = actor.Equipment.GetComponent<OverburstElementEnergy>() ?? actor.Equipment.gameObject.AddComponent<OverburstElementEnergy>();
            typeof(OverburstElementEnergy).GetProperty("Amount").GetSetMethod(true).Invoke(energy, new object[] { 80f });

            var flask = all.OfType<FlaskItemData>().Select(f => new ItemData(f, 1, ItemGrade.Rare)).FirstOrDefault();

            // 방어구·장신구 비교: 신발 1칸과 귀걸이 2칸을 채운 뒤, 두 번 클릭이 바꿔 낄 칸(귀걸이는 1번)과 비교한다.
            var gearData = all.OfType<GearItemData>().FirstOrDefault(g => g.kind != GearKind.Earring && g.kind != GearKind.Necklace);
            var earringData = all.OfType<GearItemData>().FirstOrDefault(g => g.kind == GearKind.Earring);
            ItemData gear = null, gearWorse = null, gearEquipped = null, accessory = null;
            if (gearData != null)
            {
                gearEquipped = new ItemData(gearData, 1, ItemGrade.Rare);
                if (!actor.Equipment.EquipGearItemToSlot(gearEquipped, GearEquipmentService.DefaultSlot(gearData.kind, actor.Equipment), out _)) notes.Add("gear equip failed");
                for (int i = 0; i < 400 && (gear == null || gearWorse == null); i++)
                {
                    var c = new ItemData(gearData, 1, i % 2 == 0 ? ItemGrade.Epic : ItemGrade.Common);
                    var v = EquippedWeaponComparison.GetVerdict(c);
                    var r = EquippedWeaponComparison.Compare(c, TooltipCompareMode.Auto);
                    // 새 기준 확인용: 주능력치는 높지만 보조능력치를 잃는 장비(▲이면서 빠지는 능력치 행이 있음)와, 주능력치가 낮은 장비.
                    if (gear == null && v == WeaponCompareVerdict.Better && r != null && r.Lost.Count > 0) gear = c;
                    if (gearWorse == null && v == WeaponCompareVerdict.Worse) gearWorse = c;
                }
            }
            if (earringData != null)
            {
                var one = new ItemData(earringData, 1, ItemGrade.Rare);
                var two = new ItemData(earringData, 1, ItemGrade.Uncommon);
                if (!actor.Equipment.EquipGearItemToSlot(one, (int)GearSlot.Earring, out _)) notes.Add("earring 1 equip failed");
                accessory = new ItemData(earringData, 1, ItemGrade.Legendary);
                notes.Add("earring target slot=" + GearEquipmentService.DefaultSlot(GearKind.Earring, actor.Equipment)
                    + " resolved=" + (EquippedWeaponComparison.ResolveEquippedGear(earringData) == one));
            }
            foreach (var it in new[] { gear, gearWorse, gearEquipped, accessory })
                if (it != null)
                {
                    var r = EquippedWeaponComparison.Compare(it, TooltipCompareMode.Auto);
                    notes.Add("gear " + it.itemName + " grade=" + it.grade + " verdict=" + EquippedWeaponComparison.GetVerdict(it)
                        + " equippedItem=" + (r != null && r.IsEquippedItem) + " lost=" + (r != null ? string.Join(",", r.Lost.Select(x => x.Label + Plain(x.Text))) : "-")
                        + " rolls=" + string.Join(",", it.gearRolls.Select(x => x.stat + ":" + GearQuality.Value(it, x).ToString("0.##"))));
                }
            foreach (var it in new[] { better, worse, similar, flask, gear, gearWorse, accessory }) if (it != null && !inv.AddItem(it)) notes.Add("add failed " + it.baseData.name);
            yield return Wait(0.3f);

            var ui = UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>();
            ui.inventory.SetVisible(true); yield return Wait(0.3f);
            ui.equipmentButton.onClick.Invoke(); ui.Refresh();
            yield return Wait(0.6f);
            var markers = UnityEngine.Object.FindObjectsByType<SlotUI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(s => s.DisplayItem != null).Select(s => s.DisplayItem.itemName + ":" + (s.transform.Find("CompareMarker")?.gameObject.activeSelf == true ? "▲" : "-"));
            notes.Add("slot markers: " + string.Join(", ", markers));
            foreach (var slot in UnityEngine.Object.FindObjectsByType<SlotUI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var marker = slot.transform.Find("CompareMarker");
                if (marker == null || !marker.gameObject.activeSelf) continue;
                var tmp = marker.GetComponent<TMPro.TextMeshProUGUI>();
                var corners = new Vector3[4]; ((RectTransform)marker).GetWorldCorners(corners);
                var order = new List<string>();
                foreach (Transform c in slot.transform) order.Add(c.name + (c.gameObject.activeSelf ? "" : "(off)"));
                notes.Add("marker slot=" + slot.name + " children=[" + string.Join(",", order) + "] font=" + (tmp.font ? tmp.font.name : "null")
                    + " mat=" + (tmp.fontSharedMaterial ? tmp.fontSharedMaterial.name : "null") + " col=" + tmp.color + " alpha=" + tmp.canvasRenderer.GetInheritedAlpha()
                    + " verts=" + tmp.textInfo.characterCount + " corners=" + corners[0] + ".." + corners[2] + " itemView=" + (slot.GetComponent<OverburstUIItemSlotView>() != null)
                    + " parentCanvas=" + tmp.canvas?.name);
            }
            yield return Shot("inventory_markers");

            foreach (var pair in new[] { ("better", better), ("worse", worse), ("similar", similar), ("equipped", equipped), ("flask", flask),
                ("gear", gear), ("gear_worse", gearWorse), ("gear_equipped", gearEquipped), ("accessory", accessory) })
            {
                if (pair.Item2 == null) { notes.Add(pair.Item1 + " missing"); continue; }
                TooltipManager.Instance.ShowTooltip(pair.Item2); yield return Wait(0.3f);
                DescribeTooltips(pair.Item1);
                yield return Shot("tooltip_" + pair.Item1);
                TooltipManager.Instance.HideTooltip(); yield return null;
            }

            if (better != null)
            {
                EquippedWeaponComparison.SimulateAltForVerification = true;
                TooltipManager.Instance.ShowTooltip(better); yield return Wait(0.4f);
                DescribeTooltips("alt");
                yield return Shot("tooltip_alt_side_by_side");
                TooltipManager.Instance.HideTooltip(); yield return null;
                EquippedWeaponComparison.SimulateAltForVerification = false;

                EquippedWeaponComparison.Enabled = false;
                TooltipManager.Instance.ShowTooltip(better); yield return Wait(0.3f);
                DescribeTooltips("disabled");
                yield return Shot("tooltip_disabled");
                TooltipManager.Instance.HideTooltip(); yield return null;
                EquippedWeaponComparison.Enabled = true;
            }

            foreach (var pair in new[] { ("alt_gear", gear), ("alt_accessory", accessory) })
            {
                if (pair.Item2 == null) continue;
                EquippedWeaponComparison.SimulateAltForVerification = true;
                TooltipManager.Instance.ShowTooltip(pair.Item2); yield return Wait(0.4f);
                DescribeTooltips(pair.Item1);
                yield return Shot("tooltip_" + pair.Item1);
                TooltipManager.Instance.HideTooltip(); yield return null;
                EquippedWeaponComparison.SimulateAltForVerification = false;
            }
            if (gear != null)
            {
                EquippedWeaponComparison.GearEnabled = false;
                TooltipManager.Instance.ShowTooltip(gear); yield return Wait(0.3f);
                DescribeTooltips("gear_disabled");
                yield return Shot("tooltip_gear_disabled");
                TooltipManager.Instance.HideTooltip(); yield return null;
                EquippedWeaponComparison.GearEnabled = true;
            }

            ui.CloseEquipment(); ui.inventory.SetVisible(false);
            yield return Wait(0.3f);

            var origin = player.transform.position;
            var drops = new[] { (better, new Vector3(1.4f, 0f, 0.6f)), (worse, new Vector3(-1.2f, 0f, 1.0f)), (similar, new Vector3(0.2f, 0f, 1.6f)) };
            foreach (var d in drops)
            {
                if (d.Item1 == null) continue;
                var copy = new ItemData(d.Item1.baseData, d.Item1.level, d.Item1.grade, element: d.Item1.ResolvedElement);
                var pickup = WorldItemDropFactory.CreateWorldPickup(copy, origin + d.Item2, inv, player.transform);
                if (pickup != null) dropped.Add(pickup);
            }
            yield return Wait(1.2f);
            foreach (var p in UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var it = p.RuntimeItem;
                if (it == null || !(it.baseData is WeaponItemData wd)) continue;
                var est = MeleeSingleTargetDpsCalculator.Estimate(wd, WeaponStatCalculator.Calculate(it));
                notes.Add("pickup " + it.itemName + " lv=" + it.level + " grade=" + it.grade + " dps=" + est.Dps.ToString("0.0") + " verdict=" + EquippedWeaponComparison.GetVerdict(it) + " dropped=" + dropped.Contains(p));
            }
            var labels = UnityEngine.Object.FindObjectsByType<WorldItemNameplateRowView>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(r => r.Label != null).Select(r => r.Label.text);
            notes.Add("nameplates: " + string.Join(" | ", labels));
            yield return Shot("world_nameplates");
        }
        finally
        {
            foreach (var p in dropped) if (p != null) UnityEngine.Object.Destroy(p.gameObject);
        }
    }
}
