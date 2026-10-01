using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 격리 저장 경로는 한 번의 Play에만 허용한다. 종료 시 실제 계정 경로로 비우고,
/// 남은 경로·만료된 준비 상태로 시작하려는 Play는 취소한다.
/// 기존 테스트는 RunIsolated(() => ExistingVerifier.Run(...))로 실행할 수 있다.
/// </summary>
[InitializeOnLoad]
public static class IsolatedSavePlayGuard
{
    public const string Variable = "OVERBURST_SAVE_DIRECTORY";
    public const double PreparationSeconds = 120;
    const string Key = "Overburst.IsolatedSavePlayGuard.";
    static bool wrappingStart;

    static IsolatedSavePlayGuard()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        EditorApplication.update += CheckIdle;
        EditorApplication.delayCall += CheckIdle;
    }

    public static bool RequiresAccountChoice => SessionState.GetBool(Key + "blocked", false);
    public static string ActiveDirectory => SessionState.GetString(Key + "active", "");
    public static string LastRejection => SessionState.GetString(Key + "rejection", "");
    static string PreparedDirectory => SessionState.GetString(Key + "prepared", "");
    static double ExpiresAt => double.TryParse(SessionState.GetString(Key + "expires", ""),
        NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
    static string CurrentDirectory => Environment.GetEnvironmentVariable(Variable) ?? "";

    /// <summary>경로 준비와 Play 시작을 같은 호출에서 처리한다.</summary>
    public static void EnterIsolatedPlay(string directory)
    {
        PrepareIsolatedPlay(directory);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>준비는 2분 동안만 유효하며 Play마다 다시 준비한다.</summary>
    public static void PrepareIsolatedPlay(string directory)
    {
        RequireEditMode();
        string full = ValidateDirectory(directory);
        SetPrepared(full);
        Environment.SetEnvironmentVariable(Variable, full);
    }

    /// <summary>기존 도구의 경로 설정과 EnterPlaymode를 한 번에 실행한다.</summary>
    public static void RunIsolated(Action start)
    {
        if (start == null) throw new ArgumentNullException(nameof(start));
        RequireEditMode();
        if (wrappingStart) throw new InvalidOperationException("격리 Play 시작을 중첩할 수 없습니다.");
        wrappingStart = true;
        try
        {
            start();
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("격리 테스트는 같은 호출 안에서 Play를 시작해야 합니다.");
            // EnterPlaymode가 전환 이벤트를 다음 업데이트에 보내는 경우도 준비한다.
            if (string.IsNullOrEmpty(ActiveDirectory))
                SetPrepared(ValidateDirectory(CurrentDirectory));
        }
        catch
        {
            if (!EditorApplication.isPlaying) Reject("격리 테스트 시작 실패");
            throw;
        }
        finally { wrappingStart = false; }
    }

    public static T RunIsolated<T>(Func<T> start)
    {
        T result = default;
        RunIsolated((Action)(() => { result = start(); }));
        return result;
    }

    [MenuItem("OVERBURST/Account/실제 계정으로 전환")]
    public static void UseRealAccount()
    {
        RequireEditMode();
        ClearPrepared();
        Environment.SetEnvironmentVariable(Variable, null);
        SessionState.SetBool(Key + "blocked", false);
        SessionState.EraseString(Key + "rejection");
        Debug.Log("[저장 경로] 다음 Play는 실제 계정을 사용합니다.");
    }

    [MenuItem("OVERBURST/Account/실제 계정으로 Play")]
    public static void EnterRealPlay()
    {
        UseRealAccount();
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("OVERBURST/Account/실제 계정으로 전환", true)]
    [MenuItem("OVERBURST/Account/실제 계정으로 Play", true)]
    static bool CanChooseAccount() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

    static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Play와 컴파일이 멈춘 뒤 저장 경로를 선택하세요.");
    }

    // 실제 계정과 공급사 폴더에 테스트가 기록되지 않도록 산출 경로만 허용한다.
    internal static string ValidateDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("격리 저장 폴더가 필요합니다.");
        string project = Directory.GetParent(Application.dataPath).FullName;
        string root = Path.GetFullPath(Path.Combine(project, "..", "개인파일", "코덱스산출"));
        string full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("격리 저장 폴더는 개인파일/코덱스산출의 하위 폴더여야 합니다.");
        // Junction과 symlink를 통해 허용한 경로 밖으로 나가는 경우도 거부한다.
        for (var parent = new DirectoryInfo(full); parent != null; parent = parent.Parent)
        {
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("링크를 통과하는 격리 저장 폴더는 사용할 수 없습니다.");
            if (string.Equals(parent.FullName.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)) break;
        }
        return full;
    }

    internal static bool CanEnter(string current, string prepared, double expires, double now, bool blocked)
    {
        if (string.IsNullOrWhiteSpace(current)) return string.IsNullOrEmpty(prepared) && !blocked;
        return !string.IsNullOrEmpty(prepared) && now < expires && SameDirectory(current, prepared);
    }

    static bool SameDirectory(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    static void SetPrepared(string directory)
    {
        SessionState.SetString(Key + "prepared", directory);
        SessionState.SetString(Key + "expires", (EditorApplication.timeSinceStartup + PreparationSeconds).ToString("R", CultureInfo.InvariantCulture));
        SessionState.SetBool(Key + "blocked", false);
        SessionState.EraseString(Key + "rejection");
    }

    static void ClearPrepared()
    {
        SessionState.EraseString(Key + "prepared");
        SessionState.EraseString(Key + "expires");
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            string current = CurrentDirectory;
            if (wrappingStart)
            {
                try { SetPrepared(ValidateDirectory(current)); }
                catch (Exception error) { Reject(error.Message); return; }
            }
            if (!CanEnter(current, PreparedDirectory, ExpiresAt, EditorApplication.timeSinceStartup, RequiresAccountChoice))
            {
                Reject("격리 경로가 준비되지 않았거나 만료되었습니다");
                return;
            }
            SessionState.SetString(Key + "active", string.IsNullOrWhiteSpace(current) ? "" : Path.GetFullPath(current));
            ClearPrepared();
        }
        else if (state == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(ActiveDirectory))
        {
            SessionState.EraseString(Key + "active");
            ClearPrepared();
            Environment.SetEnvironmentVariable(Variable, null);
            // 준비 없이 이어지는 옛 테스트가 실제 계정으로 전환되는 것을 막는다.
            SessionState.SetBool(Key + "blocked", true);
            EditorApplication.delayCall += CheckIdle;
            Debug.Log("[저장 경로] 격리 Play가 끝나 경로를 비웠습니다. 실제 Play는 OVERBURST → Account → 실제 계정으로 Play, 테스트는 매회 경로를 준비하세요.");
        }
    }

    static void CheckIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || wrappingStart) return;
        string prepared = PreparedDirectory;
        string current = CurrentDirectory;
        if (!string.IsNullOrEmpty(prepared) && EditorApplication.timeSinceStartup < ExpiresAt && SameDirectory(current, prepared)) return;
        if (!string.IsNullOrWhiteSpace(current) || !string.IsNullOrEmpty(prepared))
            Reject("남은 격리 저장 경로 또는 만료된 준비 상태를 정리했습니다");
    }

    static void Reject(string reason)
    {
        if (EditorApplication.isPlaying) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
        ClearPrepared();
        Environment.SetEnvironmentVariable(Variable, null);
        SessionState.SetBool(Key + "blocked", true);
        SessionState.SetString(Key + "rejection", reason);
        Debug.LogWarning("[저장 경로] " + reason + ". Play를 시작하지 않습니다. 실제 계정은 OVERBURST → Account → 실제 계정으로 Play, 기존 테스트는 IsolatedSavePlayGuard.RunIsolated(() => 검증기.Run(...))로 실행하세요.");
    }
}
