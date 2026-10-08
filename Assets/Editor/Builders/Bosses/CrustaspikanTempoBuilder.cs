using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class CrustaspikanTempoBuilder
{
    public static string Apply(string output)
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle();
        string project = Path.GetDirectoryName(Application.dataPath);
        string allowed = Path.GetFullPath(Path.Combine(project, "../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        output = Path.GetFullPath(output);
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(output, "applied-assets.json")))
            throw new InvalidOperationException("Fresh private output required; inspect prior result before resubmitting.");
        var settings = Resources.Load<CrustaspikanEncounterSettings>("Enemies/Bosses/CrustaspikanEncounter/CE_Crustaspikan");
        string reason = "Missing settings";
        if (settings == null || !settings.Validate(out reason)) throw new InvalidOperationException("Current encounter invalid: " + reason);
        var profile = settings.materials.actorDefinition.ActorPrefab.GetComponent<EnemyAnimationBridge>().PlaybackProfile;
        if (profile == null || !profile.Validate(out reason)) throw new InvalidOperationException(reason);
        var originals = new List<ScriptableObject> { settings, profile };
        originals.AddRange(settings.materials.attacks.Select(m => m.ability));
        if (originals.Distinct().Count() != 18) throw new InvalidOperationException("Expected 16 distinct abilities and two settings assets.");
        var rows = new List<object>();
        string backup = Path.Combine(output, "AssetBackups"); Directory.CreateDirectory(backup);
        var beforeFiles = new List<object>();
        foreach (var asset in originals)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (EditorUtility.IsDirty(asset) || !(path.StartsWith(CrustaspikanMaterialBuilder.Root + "/", StringComparison.Ordinal)
                || path == "Assets/ProjectOverburst/Resources/Enemies/Bosses/CrustaspikanEncounter/CE_Crustaspikan.asset"))
                throw new InvalidOperationException("Unsaved or unexpected shared asset: " + path);
            foreach (string relative in new[] { path, path + ".meta" })
            {
                string source = Path.Combine(project, relative), target = Path.Combine(backup, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(source, target, false);
                beforeFiles.Add(new { path = relative, sha256 = Hash(source), guid = AssetDatabase.AssetPathToGUID(path) });
            }
        }
        File.WriteAllText(Path.Combine(output, "asset-before.json"), JsonConvert.SerializeObject(beforeFiles, Formatting.Indented));
        var snapshots = originals.Select(o => Object.Instantiate(o)).ToArray();
        var working = settings.materials.attacks.Select(m => Object.Instantiate(m.ability)).ToArray();
        for (int i = 0; i < snapshots.Length; i++) snapshots[i].name = originals[i].name;
        for (int i = 0; i < working.Length; i++) working[i].name = settings.materials.attacks[i].ability.name;
        bool copied = false;
        try
        {
            for (int i = 0; i < working.Length; i++)
            {
                var material = settings.materials.attacks[i]; var ability = working[i];
                if (!CrustaspikanEncounterMaterialResolver.TryResolveTuning(settings, material, settings.ParryRecoilProfile, out var tuning, out reason))
                    throw new InvalidOperationException(reason);
                float speed = tuning.animationSpeedMultiplier, first = material.strikes[0].impact;
                float recoveryStart = material.strikes.Max(s => s.contactEnd);
                float preparationEnd = Mathf.Max(.02f, first - .12f);
                float recoverySeconds = material.delivery != EnemyBossMaterialDelivery.Melee ? 1.1f
                    : material.runtimeClip.name == "LeftHandAttack" || material.runtimeClip.name == "RightHandAttack" ? .9f
                    : material.runtimeClip.name.Contains("Smash") ? 1.35f : 1.1f;
                var before = new { first = material.ability.ResolveFirstImpactTime(speed), last = material.ability.ResolveLastImpactTime(speed), total = material.ability.ResolveExecutionDuration(speed) };
                var data = new SerializedObject(ability);
                data.FindProperty("telegraphedAttack").boolValue = true;
                data.FindProperty("preparationEndNormalized").floatValue = preparationEnd;
                data.FindProperty("preparationDuration").floatValue = material.runtimeClip.length * preparationEnd;
                data.FindProperty("releaseDuration").floatValue = material.runtimeClip.length * (first - preparationEnd);
                data.FindProperty("recoveryDuration").floatValue = Mathf.Min(recoverySeconds * speed,
                    material.runtimeClip.length * (1f - recoveryStart) * .75f);
                data.ApplyModifiedPropertiesWithoutUndo(); ability.ConfigureMotionPacing(first, recoveryStart);
                var after = new { first = ability.ResolveFirstImpactTime(speed), last = ability.ResolveLastImpactTime(speed), total = ability.ResolveExecutionDuration(speed) };
                if (!ability.IsValid || Mathf.Abs(after.first - before.first) > .0005f || Mathf.Abs(after.last - before.last) > .0005f
                    || after.total >= before.total - .02f)
                    throw new InvalidOperationException("Pacing changed impact times or did not shorten recovery: " + material.runtimeClip.name);
                rows.Add(new { key = material.runtimeClip.name, path = AssetDatabase.GetAssetPath(material.ability), speed, before, after, recoveryStart });
            }
            // Preserve impact poses and their contact windows. Only the tail after contact is compressed.
            copied = true;
            for (int i = 0; i < working.Length; i++) EditorUtility.CopySerialized(working[i], settings.materials.attacks[i].ability);
            foreach (string id in new[] { "Turn90Left", "Turn90Right", "Turn180Left", "Turn180Right" })
            {
                var binding = profile.Find(id);
                binding.rate = profile.ResolveClip(binding).length / (id.Contains("180") ? 1.8f : 1.2f);
                binding.blendIn = .08f; binding.blendOut = .1f; binding.settleSeconds = .05f;
            }
            settings.approachSpeed = 3f;
            if (!settings.Validate(out reason) || !profile.Validate(out reason)) throw new InvalidOperationException(reason);
            foreach (var asset in originals) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
            File.WriteAllText(Path.Combine(output, "applied-assets.json"), JsonConvert.SerializeObject(new { status = "APPLIED_NATIVE", attacks = rows,
                settings.approachSpeed, patternGapOne = settings.betweenPatterns, patternGapTwo = settings.phaseTwoBetweenPatterns,
                turns = profile.Bindings.Where(b => b.motionId.StartsWith("Turn", StringComparison.Ordinal)).Select(b => new { b.motionId, b.rate, b.blendIn, b.blendOut, b.settleSeconds }),
                utc = DateTime.UtcNow }, Formatting.Indented));
            return "Applied recovery pacing to 16 existing abilities, four turn bindings and 3m/s encounter approach. Impact times preserved.";
        }
        catch
        {
            if (copied)
            {
                for (int i = 0; i < originals.Count; i++)
                { EditorUtility.CopySerialized(snapshots[i], originals[i]); EditorUtility.SetDirty(originals[i]); AssetDatabase.SaveAssetIfDirty(originals[i]); }
            }
            File.WriteAllText(Path.Combine(output, "asset-rollback.json"), JsonConvert.SerializeObject(new { status = copied ? "ROLLED_BACK" : "VALIDATION_REJECTED_NO_ASSET_WRITE" }));
            throw;
        }
        finally
        {
            foreach (var item in snapshots) if (item != null) Object.DestroyImmediate(item);
            foreach (var item in working) if (item != null) Object.DestroyImmediate(item);
        }
    }
    static string Hash(string path)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
}
