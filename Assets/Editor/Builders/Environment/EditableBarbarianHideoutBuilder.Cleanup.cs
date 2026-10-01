using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;

public static partial class EditableBarbarianHideoutBuilder
{
    public static string RemoveLegacyCampCopies(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output); RequireIdle();
        string legacy = BarbarianHideoutBuilder.GeneratedRoot;
        string project = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
        if (!string.Equals(Path.GetFullPath(legacy), Path.GetFullPath(Path.Combine(project, legacy)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Legacy removal target is outside the intended project directory.");
        if (!AssetDatabase.IsValidFolder(legacy)) throw new InvalidOperationException("Legacy folder already removed; inspect the previous receipt.");
        var files = Directory.GetFiles(legacy, "*", SearchOption.AllDirectories).Concat(new[] { legacy + ".meta" }).OrderBy(p => p).ToArray();
        foreach (string file in files)
        {
            string backup = Path.Combine(output, "BeforeLegacy", file);
            if (!File.Exists(backup) || !File.ReadAllBytes(backup).SequenceEqual(File.ReadAllBytes(file)))
                throw new IOException("Exact legacy backup required: " + file);
        }
        var candidates = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)
            && !p.StartsWith(legacy + "/", StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(p)
            && new[] { ".unity", ".prefab", ".asset", ".mat", ".controller", ".overrideController" }.Contains(Path.GetExtension(p))).ToArray();
        var references = candidates.Where(p => AssetDatabase.GetDependencies(p, true).Any(d => d == legacy || d.StartsWith(legacy + "/", StringComparison.Ordinal))).ToArray();
        File.WriteAllText(Path.Combine(output, "legacy_dependencies.json"), JsonConvert.SerializeObject(new { scanned = candidates.Length, references }, Formatting.Indented));
        if (references.Length != 0) throw new InvalidOperationException("Legacy references remain: " + string.Join(", ", references));
        if (!AssetDatabase.DeleteAsset(legacy)) throw new IOException("Legacy camp copy removal failed.");
        File.WriteAllText(Path.Combine(output, "legacy_cleanup_result.json"), JsonConvert.SerializeObject(new { status = "PASS", deleted = legacy, files = files.Length, scannedAssets = candidates.Length, remainingReferences = 0 }, Formatting.Indented));
        return "Unused whole-camp prefab and seven duplicate materials removed after full dependency/backup checks.";
    }
}
