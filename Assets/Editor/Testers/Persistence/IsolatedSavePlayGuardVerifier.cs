using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class IsolatedSavePlayGuardVerifier
{
    const string Key = "Overburst.IsolatedSaveGuardVerifier.";
    const string GuardKey = "Overburst.IsolatedSavePlayGuard.";
    static string Output => SessionState.GetString(Key + "output", "");
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");
    static int Phase { get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value); }
    static string AccountDirectory => Path.Combine(Output, "IsolatedAccount");
    static List<string> Checks => JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "checks", "[]"));

    static IsolatedSavePlayGuardVerifier()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += ProtectRejectedEntry;
    }

    public static string VerifyPolicy()
    {
        var pass = new List<string>();
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "..", "개인파일", "코덱스산출", "Persistence", "guard-fixture");
        dir = IsolatedSavePlayGuard.ValidateDirectory(dir);
        void Assert(bool value, string name) { if (!value) throw new Exception(name); pass.Add(name); }
        Assert(IsolatedSavePlayGuard.CanEnter("", "", 0, 10, false), "실제 계정 기본 Play");
        Assert(!IsolatedSavePlayGuard.CanEnter("", "", 0, 10, true), "정리 뒤 무경로 재실행 차단");
        Assert(!IsolatedSavePlayGuard.CanEnter(dir, "", 100, 10, false), "준비 없는 격리 경로 차단");
        Assert(IsolatedSavePlayGuard.CanEnter(dir, dir, 130, 10, false), "준비한 격리 경로 허용");
        Assert(IsolatedSavePlayGuard.CanEnter(dir.ToUpperInvariant(), dir, 130, 10, false), "Windows 경로 대소문자");
        Assert(IsolatedSavePlayGuard.CanEnter(dir + Path.DirectorySeparatorChar, dir, 130, 10, false), "끝 구분자 동일 경로");
        Assert(!IsolatedSavePlayGuard.CanEnter(dir, dir + "2", 130, 10, false), "다른 경로 차단");
        Assert(!IsolatedSavePlayGuard.CanEnter("", dir, 130, 10, false), "준비 경로가 사라진 테스트 차단");
        Assert(!IsolatedSavePlayGuard.CanEnter(dir, dir, 130, 130, false), "2분 만료 경계 차단");
        Assert(!IsolatedSavePlayGuard.CanEnter(dir, dir, 130, 131, false), "만료 뒤 차단");
        Assert(IsolatedSavePlayGuard.CanEnter(dir, dir, 130, 10, true), "새 격리 준비로 차단 해제");
        Assert(!IsolatedSavePlayGuard.CanEnter("\0", dir, 130, 10, false), "잘못된 경로 차단");
        foreach (string invalid in new[] { "", Application.persistentDataPath, Application.dataPath,
            Path.GetDirectoryName(dir), Path.Combine(dir, "..", "..", "..", "..", "..", "Account") })
        {
            // guard-fixture의 부모(Persistence)는 허용된 산출 하위 폴더이므로 별도 검사한다.
            if (invalid == Path.GetDirectoryName(dir)) continue;
            bool rejected = false;
            try { IsolatedSavePlayGuard.ValidateDirectory(invalid); } catch (ArgumentException) { rejected = true; }
            Assert(rejected, "허용 루트 밖 경로 거부 " + pass.Count);
        }
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "개인파일", "코덱스산출"));
        bool rootRejected = false;
        try { IsolatedSavePlayGuard.ValidateDirectory(root); } catch (ArgumentException) { rootRejected = true; }
        Assert(rootRejected, "산출 루트 자체 거부");
        return JsonConvert.SerializeObject(new { status = "PASS", count = pass.Count, checks = pass }, Formatting.Indented);
    }

    public static string Begin(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || Phase != 0)
            throw new InvalidOperationException("Editor가 비어 있고 이전 검증이 끝나야 합니다.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "policy.json"), VerifyPolicy());
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "checks", "[]");
        SessionState.SetString(Key + "realHash", HashRealAccount());
        SessionState.SetString(Key + "status", "RUNNING");
        Phase = 1;
        SetDeadline();
        IsolatedSavePlayGuard.EnterIsolatedPlay(AccountDirectory);
        return output;
    }

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        var checks = Checks; checks.Add(name);
        SessionState.SetString(Key + "checks", JsonConvert.SerializeObject(checks));
        Save("RUNNING", null);
    }

    static void SetDeadline() => SessionState.SetFloat(Key + "deadline", (float)(EditorApplication.timeSinceStartup + 90));

    static void Tick()
    {
        if (Phase == 0) return;
        try
        {
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0)) throw new TimeoutException("단계 시간 초과 " + Phase);
            if (EditorApplication.isCompiling) return;
            if (EditorApplication.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
            if (Phase == 1 || Phase == 2)
            {
                if (!EditorApplication.isPlaying || !AccountBootstrap.Ready) return;
                Check(Path.GetFullPath(AccountBootstrap.SaveDirectory) == Path.GetFullPath(AccountDirectory), "격리 부팅 경로 " + Phase);
                InvokeGuard("CheckIdle");
                Check(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) == AccountDirectory, "Play 중 경로 보존 " + Phase);
                if (Phase == 1)
                {
                    int experience = checked(AccountGameplaySession.Current.Read().experience + 17);
                    Check(AccountGameplaySession.Current.ExecuteState("guard-fixture-" + Guid.NewGuid().ToString("N"), s => s.experience = experience), "격리 계정 시험값 저장");
                    SessionState.SetInt(Key + "experience", experience);
                    Phase = 3;
                }
                else
                {
                    Check(AccountGameplaySession.Current.Read().experience == SessionState.GetInt(Key + "experience", -1), "동일 격리 계정 재진입 복원");
                    Phase = 4;
                }
                SetDeadline(); EditorApplication.ExitPlaymode(); return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (Phase == 3)
            {
                Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)), "첫 Play 종료 경로 비움");
                Check(IsolatedSavePlayGuard.RequiresAccountChoice, "경로 없는 후속 테스트 보호");
                Phase = 2; SetDeadline();
                // 기존 도구를 래핑하는 시작 경로도 실제로 검사한다.
                IsolatedSavePlayGuard.RunIsolated((Action)(() =>
                {
                    Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, AccountDirectory);
                    EditorApplication.EnterPlaymode();
                }));
                return;
            }
            if (Phase == 4)
            {
                Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)), "두 번째 Play 종료 경로 비움");
                Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, AccountDirectory);
                InvokeGuard("CheckIdle");
                Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)), "종료 뒤 옛 경로 재주입 정리");
                Check(IsolatedSavePlayGuard.RequiresAccountChoice, "정리 후 실제 계정 자동 전환 금지");
                Phase = 5; SetDeadline();
                Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, AccountDirectory);
                EditorApplication.EnterPlaymode(); return;
            }
            if (Phase == 5)
            {
                Check(!EditorApplication.isPlaying && IsolatedSavePlayGuard.RequiresAccountChoice
                    && !string.IsNullOrEmpty(IsolatedSavePlayGuard.LastRejection), "준비 없는 실제 Play 진입 취소");
                IsolatedSavePlayGuard.PrepareIsolatedPlay(AccountDirectory);
                SessionState.SetString(GuardKey + "expires", "0");
                InvokeGuard("CheckIdle");
                Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) && IsolatedSavePlayGuard.RequiresAccountChoice,
                    "만료 경로 정리와 무경로 Play 차단 유지");
                Check(HashRealAccount() == SessionState.GetString(Key + "realHash", ""), "실제 계정 전체 파일 SHA256 불변");
                IsolatedSavePlayGuard.UseRealAccount();
                Check(!IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)),
                    "실제 계정 명시 전환");
                Finish("PASS", null);
            }
        }
        catch (Exception error) { Finish("FAIL", error.ToString()); }
    }

    static void ProtectRejectedEntry(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && (Phase == 1 || Phase == 2))
        {
            SessionState.SetBool(Key + "background", Application.runInBackground);
            SessionState.SetBool(Key + "backgroundSet", true);
            Application.runInBackground = true;
        }
        if (change == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Key + "backgroundSet", false))
        {
            Application.runInBackground = SessionState.GetBool(Key + "background", false);
            SessionState.EraseBool(Key + "backgroundSet");
        }
        // 가드가 고장 나도 준비 없는 진입 시험은 실제 계정까지 도달하지 않게 한다.
        if (Phase != 5) return;
        if (change == PlayModeStateChange.ExitingEditMode && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
            EditorApplication.isPlaying = false;
        if (change == PlayModeStateChange.EnteredPlayMode)
            Finish("FAIL", "준비 없는 테스트가 Play에 진입했습니다.");
    }

    static void InvokeGuard(string method) => typeof(IsolatedSavePlayGuard).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);

    static string HashRealAccount()
    {
        string root = Path.Combine(Application.persistentDataPath, "Account");
        using (var sha = SHA256.Create())
            return JsonConvert.SerializeObject(Directory.Exists(root)
                ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)
                    .Select(p => new { file = p.Substring(root.Length), hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p))) }).ToArray()
                : new object[0]);
    }

    static void Save(string status, string error)
    {
        File.WriteAllText(Path.Combine(Output, "lifecycle.json"), JsonConvert.SerializeObject(new { status, phase = Phase, checks = Checks, error }, Formatting.Indented));
    }

    static void Finish(string status, string error)
    {
        SessionState.SetString(Key + "status", status);
        Save(status, error);
        Phase = 0;
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        else IsolatedSavePlayGuard.UseRealAccount();
        Debug.Log("[격리 저장 검증] " + status + (error == null ? "" : " " + error));
    }
}
