using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Overburst.EditorTools.Vfx;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 격리 계정 두 번 진입, 종료 콜백, VFX 저장 범위와 퀵슬롯 알림 회귀.
[InitializeOnLoad]
public static class OpusFollowupVerifier
{
    const string Key = "Overburst.OpusFollowupVerifier.";
    const BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;
    static string Output => SessionState.GetString(Key + "output", "");
    static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    static List<string> Checks => JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "checks", "[]"));
    static List<string> Errors => JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]"));
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");
    static OpusFollowupVerifier()
    {
        EditorApplication.update += Tick;
        EditorApplication.update += RestoreEditorState;
        EditorApplication.playModeStateChanged += State;
        Application.logMessageReceived += Log;
    }

    static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        var checks = Checks; checks.Add(message);
        SessionState.SetString(Key + "checks", JsonConvert.SerializeObject(checks));
    }

    public static string Begin(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || Phase != 0)
            throw new InvalidOperationException("Editor가 멈춘 뒤 실행하세요.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "checks", "[]");
        SessionState.SetString(Key + "errors", "[]");
        SessionState.SetString(Key + "realHash", HashRealAccount());
        SessionState.SetString(Key + "activeScene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
        SessionState.SetString(Key + "status", "RUNNING");
        try
        {
            VerifyExitCallbacks();
            VerifyVfxSave();
            Phase = 1; SetDeadline();
            IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        }
        catch (Exception e) { Finish("FAIL", e.ToString()); throw; }
        return output;
    }

    static void VerifyExitCallbacks()
    {
        string root = Path.Combine(Application.dataPath, "Editor", "Testers");
        int tested = 0;
        foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);
            if (!source.Contains("IsolatedSavePlayGuard.PrepareIsolatedPlay(")
                || !source.Contains("OVERBURST_SAVE_DIRECTORY") || !source.Contains("EnteredEditMode")) continue;
            Type type = typeof(OpusFollowupVerifier).Assembly.GetType(Path.GetFileNameWithoutExtension(file));
            if (type == null || type == typeof(OpusFollowupVerifier)) continue;
            var keyField = type.GetField("Key", Private) ?? type.GetField("SessionKey", Private);
            var callback = type.GetMethods(Private).FirstOrDefault(m => m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(PlayModeStateChange));
            if (keyField == null || callback == null) continue;
            string key = (string)keyField.GetValue(null);
            if (SessionState.GetBool(key, false)) throw new InvalidOperationException(type.Name + " 실행 중");
            string savedPrefs = SessionState.GetString(key + ".prefs", "{}");
            // 종료 콜백의 설정 복원은 이번 경로 검사와 분리한다.
            SessionState.SetString(key + ".prefs", "{}");
            SessionState.SetString(key + ".env", "stale-account");
            SessionState.SetString(key + ".oldEnv", "stale-account");
            SessionState.SetBool(key, true);
            Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, Path.Combine(Output, "OldTestAccount"));
            callback.Invoke(null, new object[] { PlayModeStateChange.EnteredEditMode });
            Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)), type.Name + " 종료 경로 비움");
            Check(!SessionState.GetBool(key, false), type.Name + " 실행 상태 해제");
            string legacy = source.Contains(".oldEnv\"") ? ".oldEnv" : ".env";
            if (source.Contains("EraseString(Key")) Check(SessionState.GetString(key + legacy, "") == "", type.Name + " 이전 경로 기록 제거");
            Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, "other-active-account");
            callback.Invoke(null, new object[] { PlayModeStateChange.EnteredEditMode });
            Check(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) == "other-active-account", type.Name + " 비소유 종료 무시");
            Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, null);
            SessionState.EraseString(key + ".env"); SessionState.EraseString(key + ".oldEnv");
            SessionState.SetString(key + ".prefs", savedPrefs);
            tested++;
        }
        Check(tested >= 29, "종료 콜백 " + tested + "개 검사");
        IsolatedSavePlayGuard.UseRealAccount();
    }

    static void VerifyVfxSave()
    {
        string prefix = "Assets/Editor/Testers/UI/OpusFollowupFixture_" + Guid.NewGuid().ToString("N");
        string ownerPath = prefix + "_Owner.asset", unrelatedPath = prefix + "_Other.asset", prefabPath = prefix + ".prefab";
        var owner = ScriptableObject.CreateInstance<EnemyTelegraphVisualLibrary>();
        var unrelated = ScriptableObject.CreateInstance<EnemyTelegraphVisualLibrary>();
        var go = new GameObject("FollowupFixture");
        try
        {
            AssetDatabase.CreateAsset(owner, ownerPath); AssetDatabase.CreateAsset(unrelated, unrelatedPath);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            AssetDatabase.SaveAssetIfDirty(owner); AssetDatabase.SaveAssetIfDirty(unrelated);
            string unrelatedBytes = File.ReadAllText(unrelatedPath);
            var other = new SerializedObject(unrelated); other.FindProperty("cone").objectReferenceValue = prefab;
            other.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(unrelated);
            var a = new VfxSlot { Key = "fixture.a", OwnerPath = ownerPath, PropertyPath = "cone", Label = "A" };
            var b = new VfxSlot { Key = "fixture.b", OwnerPath = ownerPath, PropertyPath = "nova", Label = "B" };
            string guid = AssetDatabase.AssetPathToGUID(prefabPath);
            var da = new VfxDraft { slotKey = a.Key, prefabGuid = guid };
            var db = new VfxDraft { slotKey = b.Key, prefabGuid = guid };
            bool rejected = false;
            try { VfxDraftOperations.Apply(new[] { (a, da), (b, db) }); } catch (ArgumentException) { rejected = true; }
            Check(rejected && owner.Cone == null && owner.Nova == null, "VFX 여러 항목 저장 거부·무변경");
            var report = VfxDraftOperations.Apply(new[] { (a, da) });
            Check(report.AppliedKeys.SetEquals(new[] { a.Key }) && report.Failed.Count == 0, "VFX 선택 항목 하나 저장");
            Check(owner.Cone == prefab && owner.Nova == null, "VFX 같은 SO의 다른 슬롯 미적용");
            Check(File.ReadAllText(ownerPath).Contains(guid), "VFX 선택 필드 디스크 저장");
            Check(File.ReadAllText(unrelatedPath) == unrelatedBytes && EditorUtility.IsDirty(unrelated), "VFX 다른 미저장 에셋 보존");
            Check(report.Reverse.Count == 1 && report.Reverse[0].clear, "VFX 직전 선택 항목 되돌리기 초안");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
            AssetDatabase.DeleteAsset(ownerPath); AssetDatabase.DeleteAsset(unrelatedPath); AssetDatabase.DeleteAsset(prefabPath);
        }
    }

    static void VerifyHud()
    {
        var go = new GameObject("FollowupHudFixture", typeof(RectTransform));
        var flask = ScriptableObject.CreateInstance<FlaskItemData>();
        var consumable = ScriptableObject.CreateInstance<ConsumableItemData>();
        var random = UnityEngine.Random.state;
        try
        {
            var hud = go.AddComponent<ActionSlotHudSlotUI>();
            var a = new ItemData(flask, 1, ItemGrade.Common) { runtimeInstanceId = "flask-a" };
            var b = new ItemData(flask, 1, ItemGrade.Common) { runtimeInstanceId = "flask-b" };
            float Flash() => (float)typeof(ActionSlotHudSlotUI).GetField("readyFlashStartedAt", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hud);
            void End() => typeof(ActionSlotHudSlotUI).GetMethod("EndReadyFlash", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, null);
            hud.SetFlask(a, 0, 0, true); Check(Flash() < 0, "물약 최초 준비 표시 무알림");
            hud.SetFlask(a, 0, 3, true); hud.SetFlask(b, 0, 0, true); Check(Flash() < 0, "동일 SO 다른 물약 교체 무알림");
            hud.SetFlask(b, 0, 2, true); hud.SetFlask(b, 0, 0, true); Check(Flash() >= 0, "같은 물약 쿨다운 완료 알림");
            End(); hud.SetFlask(b, 0, 0, true); Check(Flash() < 0, "완료 다음 프레임 중복 알림 없음");
            hud.SetFlask(b, 0, 2, true); hud.SetEmpty(false); hud.SetFlask(b, 0, 0, true); Check(Flash() < 0, "비운 슬롯 재장착 무알림");
            hud.SetFlask(a, 0, 2, true);
            var restored = new ItemData(flask, 1, ItemGrade.Common) { runtimeInstanceId = "flask-a" };
            hud.SetFlask(restored, 0, 0, true); Check(Flash() >= 0, "동일 물약 ID 객체 갱신 완료 알림");
            hud.SetEmpty(false); Check(Flash() < 0, "슬롯 비울 때 잔여 번쩍임 제거");
            hud.SetConsumable(consumable, null, 1); hud.SetCooldown(2); hud.SetCooldown(0); Check(Flash() >= 0, "일반 소모품 완료 알림 유지");
            End(); var skill = new FixtureSkill { Remaining = 2 }; hud.SetSkill(skill); skill.Remaining = 0; hud.SetSkill(skill); Check(Flash() >= 0, "스킬 완료 알림 유지");
            End(); skill.Remaining = 2; hud.SetSkill(skill); hud.SetSkill(new FixtureSkill()); Check(Flash() < 0, "다른 스킬 교체 무알림");
            hud.SetItem(new ItemData(flask, 1, ItemGrade.Common) { runtimeInstanceId = null }, 0, false, false);
            hud.SetCooldown(2); hud.SetCooldown(0); Check(Flash() < 0, "물약 ID 없는 잘못된 데이터 무알림");
        }
        finally { UnityEngine.Random.state = random; UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(flask); UnityEngine.Object.DestroyImmediate(consumable); }
    }

    sealed class FixtureSkill : IQuickSlotSkill
    {
        public float Remaining;
        public string DisplayName => "fixture";
        public Sprite Icon => null;
        public float CooldownRemaining => Remaining;
        public bool TryUse(out string reason) { reason = ""; return false; }
    }

    static void Tick()
    {
        if (Phase == 0 || EditorApplication.isCompiling) return;
        try
        {
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0)) throw new TimeoutException("Play 검증 시간 초과");
            if (EditorApplication.isPlaying)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                // 공유 씬의 활성 상태에 기대지 않고 준비된 제품 서비스를 격리 계정으로 초기화한다.
                if ((Phase == 1 || Phase == 3) && !AccountBootstrap.Attempted
                    && PlayerAccountInventoryService.Instance != null && PlayerProgression.Current != null
                    && PlayerContext.Instance != null && PlayerContext.Instance.CurrentActor != null)
                {
                    Check(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) == Path.Combine(Output, "IsolatedAccount"), "픽스처 초기화 전 격리 경로 확인");
                    Check(AccountBootstrap.Initialize(PlayerAccountInventoryService.Instance), "제품 계정 서비스 격리 초기화 " + Phase);
                }
                if ((Phase != 1 && Phase != 3) || !AccountBootstrap.Ready) return;
                Check(Path.GetFullPath(AccountBootstrap.SaveDirectory) == Path.GetFullPath(Path.Combine(Output, "IsolatedAccount")), "격리 계정 부팅 " + Phase);
                VerifyHud(); Phase++; SetDeadline(); EditorApplication.delayCall += EditorApplication.ExitPlaymode; return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (Phase == 2)
            {
                Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)), "첫 Play 종료 경로 해제");
                Phase = 3; SetDeadline(); IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output, "IsolatedAccount")); return;
            }
            if (Phase == 4)
            {
                Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)), "재진입 Play 종료 경로 해제");
                Check(HashRealAccount() == SessionState.GetString(Key + "realHash", ""), "실제 계정 파일 원본 보존");
                Check(Errors.Count == 0, "두 회차 런타임 오류 0: " + string.Join(" | ", Errors));
                IsolatedSavePlayGuard.UseRealAccount(); Finish("PASS", null);
            }
        }
        catch (Exception e)
        {
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            Finish("FAIL", e.ToString());
        }
    }

    static void SetDeadline() => SessionState.SetFloat(Key + "deadline", (float)(EditorApplication.timeSinceStartup + 120));
    static void State(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && (Phase == 1 || Phase == 3))
        {
            SessionState.SetBool(Key + "background", Application.runInBackground);
            SessionState.SetBool(Key + "backgroundSet", true);
            Application.runInBackground = true;
        }
        if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Key + "backgroundSet", false))
        {
            Application.runInBackground = SessionState.GetBool(Key + "background", false);
            SessionState.EraseBool(Key + "backgroundSet");
        }
    }
    static void Log(string message, string trace, LogType type)
    {
        if (Phase == 0 || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        var errors = Errors; errors.Add(message);
        SessionState.SetString(Key + "errors", JsonConvert.SerializeObject(errors));
    }
    static string HashRealAccount()
    {
        string root = Path.Combine(Application.persistentDataPath, "Account");
        if (!Directory.Exists(root)) return "ABSENT";
        using (var sha = SHA256.Create()) return string.Join("|", Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p)
            .Select(p => p.Substring(root.Length) + ":" + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p)))));
    }
    static void Finish(string status, string error)
    {
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonConvert.SerializeObject(new { status, checks = Checks, errors = Errors, error }, Formatting.Indented));
        SessionState.SetString(Key + "status", status); Phase = 0;
        SessionState.SetBool(Key + "restore", true);
        RestoreEditorState();
    }
    static void RestoreEditorState()
    {
        if (!SessionState.GetBool(Key + "restore", false) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(SessionState.GetString(Key + "activeScene", ""));
        if (scene.IsValid() && scene.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        IsolatedSavePlayGuard.UseRealAccount();
        SessionState.SetBool(Key + "restore", false);
    }
}
