using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>실제 얼음 월드 입자의 저에너지 크기와 전환·정리 동작을 전수 확인한다.</summary>
public static class IceLowEnergyTrailVerifier
{
    public static void Verify(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 편집 모드가 필요합니다.");
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory);
        Directory.CreateDirectory(directory);
        var checks = new List<object>(); var failures = new List<string>(); var samples = new List<object>();
        void Check(bool pass, string name) { checks.Add(new { pass, name }); if (!pass) failures.Add(name); }
        var paths = GreatswordReferenceBindingBuilder.EquippedPaths();
        Check(paths.Length == 34, "34 weapons");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (string path in paths)
            {
                GameObject instance = null;
                try
                {
                    instance = CreatePreview(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                    var fx = instance.GetComponent<MeleeWeaponElementFx>();
                    fx.EditorPreviewEnergy(WeaponElement.Ice, 1f);
                    var baseline = WorldParticles(fx).Select(p => new { p.name, curve = p.main.startSize.constantMax, scale = p.transform.lossyScale.x, delay = p.main.startDelay.constantMax }).ToArray();
                    Check(baseline.Length > 0, path + " world particles");
                    // Descending/ascending transitions exercise the same retained effect instance.
                    foreach (float energy in new[] { 1f, .8f, .6f, .4f, .2f, .1f, .05f, .01f, 0f, .1f, 1f })
                    {
                        fx.EditorPreviewEnergy(WeaponElement.Ice, energy);
                        // Supplier world particles have a start delay; sample beyond it before testing emission.
                        fx.EditorSampleEnergy(baseline.Max(b => b.delay) + .1f);
                        instance.transform.rotation = Quaternion.Euler(0, 0, energy * 23f);
                        for (int frame = 0; frame < 45; frame++)
                        {
                            instance.transform.position += new Vector3(.015f, 0, 0);
                            fx.EditorAdvanceEnergyPreview(1f / 60f);
                        }
                        var particles = WorldParticles(fx);
                        for (int i = 0; i < particles.Length; i++)
                        {
                            var p = particles[i]; var renderer = p.GetComponent<ParticleSystemRenderer>();
                            float expected = baseline[i].curve * energy * Mathf.Abs(baseline[i].scale) / Mathf.Max(.0001f, Mathf.Abs(p.transform.lossyScale.x));
                            float actual = p.main.startSize.constantMax;
                            Check(Mathf.Abs(actual - expected) <= .00001f, path + " size " + energy + "/" + p.name);
                            Check(energy > 0 ? renderer.enabled && p.particleCount > 0 : !renderer.enabled && p.particleCount == 0 && !p.emission.enabled,
                                path + " emission/clear " + energy + "/" + p.name);
                            var buffer = new ParticleSystem.Particle[p.particleCount]; p.GetParticles(buffer);
                            Check(buffer.All(v => Finite(v.position) && Finite(v.velocity) && Finite(v.GetCurrentSize3D(p))), path + " finite particles " + energy);
                            float maximumSize = buffer.Length == 0 ? 0 : buffer.Max(v => v.GetCurrentSize3D(p).x);
                            Check(maximumSize <= baseline[i].curve + .0001f, path + " no low-energy enlargement " + energy);
                            samples.Add(new { path, energy, particle = p.name, actual, expected, maximumSize, count = p.particleCount });
                        }
                    }
                    fx.EditorClearEnergyPreview();
                    Check(!instance.GetComponentsInChildren<Transform>(true).Any(t => t.name == "WeaponEffects2Blade"), path + " disposed");
                }
                finally { if (instance != null) UnityEngine.Object.DestroyImmediate(instance); }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        Check(!scene.IsValid(), "preview closed");
        File.WriteAllText(Path.Combine(directory, "IceLowEnergyVerification.json"), JsonConvert.SerializeObject(new {
            status = failures.Count == 0 ? "PASS" : "FAIL", weapons = paths.Length, cases = paths.Length * 11, checks = checks.Count, failures, samples
        }, Formatting.Indented));
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(" | ", failures.Take(10)));
        Debug.Log("[얼음 저에너지 트레일] PASS " + checks.Count + "검사");
    }

    internal static ParticleSystem[] WorldParticles(MeleeWeaponElementFx fx)
    {
        var root = fx.GetComponentsInChildren<Transform>(true).Single(t => t.name == "WeaponEffects2Blade");
        return root.GetComponentsInChildren<ParticleSystem>(true).Where(p => p.main.simulationSpace == ParticleSystemSimulationSpace.World).OrderBy(p => p.name).ToArray();
    }
    static bool Finite(Vector3 value) => !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));
    static GameObject CreatePreview(GameObject prefab, UnityEngine.SceneManagement.Scene scene)
    {
        var host = new GameObject("IceLowEnergyPreviewHost"); host.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
        GameObject instance = null;
        try
        {
            instance = UnityEngine.Object.Instantiate(prefab, host.transform, false);
            foreach (var script in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(script is MeleeWeaponElementFx)) UnityEngine.Object.DestroyImmediate(script);
            instance.GetComponent<MeleeWeaponElementFx>().enabled = false;
            instance.transform.SetParent(null, true); instance.SetActive(true);
            return instance;
        }
        catch { if (instance != null) UnityEngine.Object.DestroyImmediate(instance); throw; }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }
}
