using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// 사용자 승인에 따른 개발 계정 일회성 초기화. 게임 로드 경로에는 연결하지 않는다.
public static class AccountSaveResetTool
{
    public static string ResetDefaultAccount(string evidenceDirectory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || BuildPipeline.isBuildingPlayer || Overburst.Persistence.AccountGameplaySession.Current != null)
            throw new InvalidOperationException("빌드와 Play가 끝난 EditMode에서 실행하세요.");
        string source = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "Account"));
        if (!Directory.Exists(source)) return "EMPTY: " + source;
        string output = IsolatedSavePlayGuard.ValidateDirectory(evidenceDirectory);
        if (Directory.Exists(output)) throw new ArgumentException("새 결과 폴더를 지정하세요.");
        for (var parent = new DirectoryInfo(source); parent != null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("저장 경로가 링크입니다: " + source);
        var files = Files(source).Select(path => new { relative = path.Substring(source.Length + 1), sha256 = Hash(path) }).ToArray();
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "before.json"), JsonConvert.SerializeObject(new { source, files }, Formatting.Indented));
        DateTime started = DateTime.UtcNow;
        EditorFileRecycle.MoveDefaultAccountDirectory();
        string bin = Path.Combine(Path.GetPathRoot(source), "$Recycle.Bin", WindowsIdentity.GetCurrent().User.Value);
        string restoredRoot = null;
        foreach (string metadata in Directory.EnumerateFiles(bin, "$I*"))
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(metadata);
                if (bytes.Length < 28 || DateTime.FromFileTimeUtc(BitConverter.ToInt64(bytes, 16)) < started) continue;
                long version = BitConverter.ToInt64(bytes, 0);
                if (version != 1 && version != 2) continue;
                int offset = version == 2 ? 28 : 24;
                string original = Encoding.Unicode.GetString(bytes, offset, bytes.Length - offset).TrimEnd('\0');
                if (!string.Equals(original, source, StringComparison.OrdinalIgnoreCase)) continue;
                string payload = Path.Combine(bin, "$R" + Path.GetFileName(metadata).Substring(2));
                if (Directory.Exists(payload) && files.All(file => File.Exists(Path.Combine(payload, file.relative))
                    && Hash(Path.Combine(payload, file.relative)) == file.sha256)) { restoredRoot = payload; break; }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException) { }
        }
        bool verified = !Directory.Exists(source) && restoredRoot != null;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new
            { status = verified ? "PASS_RECYCLED" : "RECYCLED_RECOVERY_NOT_VERIFIED", source, restoredRoot, count = files.Length }, Formatting.Indented));
        if (!verified) throw new IOException("휴지통 복원 근거를 확인하지 못했습니다. result.json을 확인하세요.");
        return "PASS_RECYCLED: " + source + " -> " + restoredRoot;
    }

    static IEnumerable<string> Files(string directory)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("저장 폴더에 링크가 있습니다: " + path);
            if ((attributes & FileAttributes.Directory) != 0)
                foreach (string file in Files(path)) yield return file;
            else yield return path;
        }
    }

    static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
