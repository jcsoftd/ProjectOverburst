using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>제품 장착·보석 교체·충전/방출을 격리 계정에서 확인하고 일반 Play로 반환한다.</summary>
[InitializeOnLoad]
public static class IceLowEnergyTrailPlayVerifier
{
    private const string Key = "Overburst.IceLowEnergyTrailPlayVerifier.";
    private static IEnumerator work;
    private static int lastFrame = -1;
    private static readonly List<string> checks = new List<string>();
    private static readonly List<string> errors = new List<string>();
    private static string Output => SessionState.GetString(Key + "output", "");
    private static int Round => SessionState.GetInt(Key + "round", 0);
    static IceLowEnergyTrailPlayVerifier() { if (!string.IsNullOrEmpty(Output)) EditorApplication.update += Tick; }

    public static void Begin(string directory, int round)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !string.IsNullOrEmpty(Output))
            throw new InvalidOperationException("공유 Editor가 유휴 상태여야 합니다.");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) ||
            !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("다른 격리 계정 준비가 남아 있습니다.");
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory);
        Directory.CreateDirectory(directory);
        SessionState.SetString(Key + "output", directory);
        SessionState.SetInt(Key + "round", round);
        SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key + "scenes", HideoutCatalogLayoutBuilder.EditorSnapshot());
        SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 180);
        SessionState.SetBool(Key + "return", false);
        SessionState.SetBool(Key + "started", false);
        SessionState.SetFloat(Key + "returnDeadline", 0);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(directory, "IsolatedAccount_" + round)); }
        catch { RequestReturn(); throw; }
    }

    private static void RequestReturn()
    {
        SessionState.SetBool(Key + "return", true);
        SessionState.SetFloat(Key + "returnDeadline", (float)EditorApplication.timeSinceStartup + 120);
    }

    public static void RetryReturn()
    {
        if (string.IsNullOrEmpty(Output) || !SessionState.GetBool(Key + "return", false))
            throw new InvalidOperationException("반환할 본인 검증이 없습니다.");
        RequestReturn(); EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        string output = Output;
        if (string.IsNullOrEmpty(output)) return;
        if (SessionState.GetBool(Key + "return", false))
        {
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "returnDeadline", 0))
            {
                File.WriteAllText(Path.Combine(output, "Return_" + Round + ".json"), JsonConvert.SerializeObject(new {
                    ready = false, status = "BLOCKED", reason = "공유 Editor가 120초 내 반환되지 않았습니다. 본인 상태를 보존하고 RetryReturn으로 재개하세요."
                }, Formatting.Indented));
                EditorApplication.update -= Tick;
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            // A subsequent other-owner account preparation must be left untouched.
            string returnOwn = Path.GetFullPath(Path.Combine(output, "IsolatedAccount_" + Round));
            string environment = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
            string active = IsolatedSavePlayGuard.ActiveDirectory;
            string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
            bool Other(string path) => !string.IsNullOrEmpty(path) && !string.Equals(Path.GetFullPath(path), returnOwn, StringComparison.OrdinalIgnoreCase);
            if (Other(environment) || Other(active) || Other(prepared)) return;
            string scene = SessionState.GetString(Key + "startScene", "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(scene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
            IsolatedSavePlayGuard.UseRealAccount();
            File.WriteAllText(Path.Combine(output, "Return_" + Round + ".json"), JsonConvert.SerializeObject(new {
                ready = !IsolatedSavePlayGuard.RequiresAccountChoice, env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
                scenePreserved = HideoutCatalogLayoutBuilder.EditorSnapshot() == SessionState.GetString(Key + "scenes", ""), startScene = scene
            }, Formatting.Indented));
            (work as IDisposable)?.Dispose(); work = null;
            SessionState.EraseString(Key + "output"); SessionState.EraseString(Key + "startScene"); SessionState.EraseString(Key + "scenes");
            SessionState.EraseInt(Key + "round"); SessionState.EraseFloat(Key + "deadline");
            SessionState.EraseBool(Key + "return"); SessionState.EraseBool(Key + "started");
            SessionState.EraseFloat(Key + "returnDeadline");
            EditorApplication.update -= Tick;
            checks.Clear(); errors.Clear(); lastFrame = -1;
            return;
        }
        bool timeout = EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0);
        if (!EditorApplication.isPlaying)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && (timeout || SessionState.GetBool(Key + "started", false)))
            {
                Application.logMessageReceived -= Log;
                (work as IDisposable)?.Dispose(); work = null;
                File.WriteAllText(Path.Combine(output, "Play_" + Round + ".json"), JsonConvert.SerializeObject(new {
                    status = "FAIL", failure = "Play 진입 취소 또는 사용자 중단", checks, errors
                }, Formatting.Indented));
                RequestReturn();
            }
            return;
        }
        string own = Path.GetFullPath(Path.Combine(output, "IsolatedAccount_" + Round));
        if (string.IsNullOrEmpty(AccountBootstrap.SaveDirectory) || !string.Equals(Path.GetFullPath(AccountBootstrap.SaveDirectory), own, StringComparison.OrdinalIgnoreCase))
        {
            if (timeout)
            {
                // A reload during boot can clear AccountBootstrap while the Guard still owns this Play.
                // Stop only when both independent ownership paths still identify our account.
                bool Owned(string path) => !string.IsNullOrEmpty(path) && string.Equals(Path.GetFullPath(path), own, StringComparison.OrdinalIgnoreCase);
                if (Owned(IsolatedSavePlayGuard.ActiveDirectory) && Owned(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
                    Finish("제품 부팅 상태가 초기화되거나 준비되지 않아 본인 Play를 종료했습니다.");
                else RequestReturn();
            }
            return;
        }
        if (work == null)
        {
            if (SessionState.GetBool(Key + "started", false)) { Finish("검증 중 도메인 재로딩으로 중단됨"); return; }
            if (!timeout && (!AccountBootstrap.Ready || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching ||
                !WorldSessionState.IsHideout || PlayerContext.Instance?.CurrentActor == null)) return;
            if (timeout) { Finish("제품 부팅 시간 초과"); return; }
            SessionState.SetBool(Key + "started", true); checks.Clear(); errors.Clear();
            Application.logMessageReceived += Log;
            work = Verify();
        }
        EditorApplication.QueuePlayerLoopUpdate();
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            if (timeout) throw new TimeoutException("대검 제품 검증 시간 초과");
            if (!work.MoveNext()) Finish(null);
        }
        catch (Exception error) { Finish(error.ToString()); }
    }

    private static void Log(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }

    private static void Finish(string failure)
    {
        (work as IDisposable)?.Dispose(); work = null; Application.logMessageReceived -= Log;
        File.WriteAllText(Path.Combine(Output, "Play_" + Round + ".json"), JsonConvert.SerializeObject(new {
            status = failure == null && errors.Count == 0 ? "PASS" : "FAIL", checks, errors, failure, account = AccountBootstrap.SaveDirectory
        }, Formatting.Indented));
        RequestReturn();
        EditorApplication.ExitPlaymode();
    }

    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }

    private static IEnumerator Verify()
    {
        var equipment = PlayerContext.Instance.CurrentActorEquipment;
        var energy = equipment.GetComponent<OverburstElementEnergy>() ?? equipment.gameObject.AddComponent<OverburstElementEnergy>();
        var inventory = PlayerAccountInventoryService.SharedInventory;
        Check(energy != null && inventory != null, "제품 플레이어·에너지·계정 준비");
        Check(AccountGameplaySession.Current.ExecuteState("ice-trail-capacity-" + Guid.NewGuid().ToString("N"), s =>
            s.unlockedSlots = s.baseUnlockedSlots = s.inventoryCapacity), "격리 가방 준비");
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        var weapons = WeaponLevelCatalog.Current.entries.Where(e => e.weapon != null).Select(e => e.weapon).ToArray();
        Check(weapons.Length == 34, "제품 카탈로그 34종");
        var definition = Resources.LoadAll<ElementGemItemData>("Items/ElementGems").First(g => g.element == WeaponElement.Ice);
        var gem = new ItemData(definition, 1, definition.fixedGrade);
        gem.gemState = ElementGemQuality.Roll(definition, 11, ElementGemArchetype.Heavy);
        Check(inventory.AddItem(gem) && ElementGemEquipmentService.EquipFromInventorySlot(inventory.FindFirstMatchingItemIndex(gem)), "얼음 보석 장착");
        int sequence = 970000;
        foreach (var weapon in weapons)
        {
            var item = new ItemData(weapon, 1, ItemGrade.Common);
            Check(inventory.AddItem(item) && equipment.EquipWeaponItem(item), "무기 장착 " + weapon.itemName);
            yield return null;
            var snapshot = new ElementGemAttackSnapshot(equipment);
            energy.Clear();
            for (int hit = 0; hit < 20; hit++) energy.RecordConfirmedHit(snapshot.WeaponId, WeaponElement.Ice, ++sequence, 10f);
            yield return null;
            var fx = equipment.CurrentWeaponRoot.GetComponent<MeleeWeaponElementFx>();
            var world = IceLowEnergyTrailVerifier.WorldParticles(fx);
            Check(world.Length > 0 && energy.Normalized == 1f, weapon.itemName + " 100% 기준");
            float[] baseline = world.Select(p => p.main.startSize.constantMax).ToArray();
            float[] baselineScale = world.Select(p => p.transform.lossyScale.x).ToArray();
            int instance = world[0].GetInstanceID();
            foreach (int hits in new[] { 0, 1, 2, 5, 10, 0, 1 })
            {
                energy.Clear();
                for (int hit = 0; hit < hits; hit++) Check(energy.RecordConfirmedHit(snapshot.WeaponId, WeaponElement.Ice, ++sequence, 10f), weapon.itemName + " 확정 적중 " + hits);
                yield return null;
                float charge = energy.Normalized;
                Check(hits == 0 ? charge == 0 : charge > 0, weapon.itemName + " 충전 " + hits);
                world = IceLowEnergyTrailVerifier.WorldParticles(fx);
                Check(world[0].GetInstanceID() == instance, weapon.itemName + " 효과 인스턴스 유지 " + hits);
                for (int i = 0; i < world.Length; i++)
                {
                    var p = world[i];
                    float expected = baseline[i] * charge * Mathf.Abs(baselineScale[i]) / Mathf.Max(.0001f, Mathf.Abs(p.transform.lossyScale.x));
                    Check(Mathf.Abs(p.main.startSize.constantMax - expected) < .00001f, weapon.itemName + " 실제 입자 크기 " + charge);
                    Check(charge == 0 ? !p.GetComponent<Renderer>().enabled && p.particleCount == 0 : p.GetComponent<Renderer>().enabled,
                        weapon.itemName + " 방출/복귀 " + charge);
                }
                fx.BeginTrail(); yield return null; fx.EndTrail();
                Check(fx.GetComponentsInChildren<TrailRenderer>(true).All(t => !t.emitting), weapon.itemName + " 검끝 공격 트레일 종료 " + charge);
            }
        }
        Check(AccountGameplaySession.Current.FlushPendingSave(), "격리 저장 완료");
    }
}
