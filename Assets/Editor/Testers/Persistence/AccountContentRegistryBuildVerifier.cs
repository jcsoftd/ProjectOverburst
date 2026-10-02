using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;

public static class AccountContentRegistryBuildVerifier
{
    [MenuItem("OVERBURST/Tests/Persistence/Content Registry Build")]
    public static void Verify()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Finish Play Mode or the current build first.");

        var catalogs = Resources.LoadAll<AccountContentRegistry>("Persistence/Supplemental");
        var supplemental = catalogs.SelectMany(catalog => catalog.Entries).ToArray();
        var registry = AccountContentRegistryBuilder.Build();
        var ids = new Dictionary<UnityEngine.Object, string>();
        int checks = 0;
        foreach (var entry in registry.Entries.Concat(supplemental))
        {
            Require(entry != null && entry.asset != null && !string.IsNullOrEmpty(entry.id), "Invalid content entry");
            Require(registry.IdFor(entry.asset) == entry.id, "Canonical ID changed: " + entry.id);
            Require(registry.Resolve<UnityEngine.Object>(entry.id) == entry.asset, "Content round trip failed: " + entry.id);
            ids[entry.asset] = entry.id;
            checks += 3;
        }

        foreach (var entry in supplemental)
        {
            Require(!registry.Entries.Any(main => main.asset == entry.asset), "Supplemental asset was registered under a main GUID");
            checks++;
        }

        // Rebuilding again must preserve both GUID IDs and the published supplemental IDs.
        registry = AccountContentRegistryBuilder.Build();
        foreach (var pair in ids)
        {
            Require(registry.IdFor(pair.Key) == pair.Value, "Repeated build changed a saved content ID");
            checks++;
        }

        string output = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../개인파일/코덱스산출/Builds/20261002_SecondSubmission/Work/content-registry-verification.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            status = "PASS", checks, mainEntries = registry.Entries.Count,
            supplementalCatalogs = catalogs.Length, supplementalEntries = supplemental.Length,
            roundTrips = ids.Count, repeatedBuildStable = true
        }, Newtonsoft.Json.Formatting.Indented));
        Debug.Log("[OVERBURST Content Registry Build] PASS checks=" + checks);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
