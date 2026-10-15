using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

public static class OverburstBgmBuilder
{
    public const string CatalogPath = "Assets/ProjectOverburst/Resources/Audio/OverburstBgmCatalog.asset";
    private sealed class ClipRow { public string id, path, guid, action; }

    public static void Apply(string output)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        var rows = JsonConvert.DeserializeObject<ClipRow[]>(File.ReadAllText(Path.Combine(output, "selected-import.json")));
        var clips = rows.ToDictionary(row => row.id, row => AssetDatabase.LoadAssetAtPath<AudioClip>(row.path));
        foreach (var row in rows)
        {
            if (!clips[row.id] || AssetDatabase.AssetPathToGUID(row.path) != row.guid) throw new InvalidOperationException("Missing approved clip " + row.id);
            if (row.action == "reuse") continue;
            var importer = (AudioImporter)AssetImporter.GetAtPath(row.path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = .7f;
            settings.preloadAudioData = false;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = true;
            importer.SaveAndReimport();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
        var catalog = AssetDatabase.LoadAssetAtPath<OverburstBgmCatalog>(CatalogPath);
        if (catalog) throw new InvalidOperationException("Existing BGM catalog: review current values before applying again.");
        catalog = ScriptableObject.CreateInstance<OverburstBgmCatalog>();
        catalog.title = clips["S01"]; catalog.tutorial = clips["F05"]; catalog.town = clips["F35"];
        catalog.outdoor = new[] { clips["F18"], clips["F15"], clips["S20"] };
        catalog.desert = new[] { clips["S17"], clips["F20"] };
        catalog.cave = new[] { clips["L05"], clips["F13"], clips["F14"], clips["F15"] };
        catalog.volume = .35f;
        AssetDatabase.CreateAsset(catalog, CatalogPath);
        AssetDatabase.SaveAssetIfDirty(catalog);
        AssetDatabase.ImportAsset(CatalogPath, ImportAssetOptions.ForceSynchronousImport);
        File.WriteAllText(Path.Combine(output, "Evidence", "catalog-applied.json"), JsonConvert.SerializeObject(new { status = "APPLIED_PENDING_VERIFICATION", path = CatalogPath, guid = AssetDatabase.AssetPathToGUID(CatalogPath), utc = DateTime.UtcNow }, Formatting.Indented));
    }
}
