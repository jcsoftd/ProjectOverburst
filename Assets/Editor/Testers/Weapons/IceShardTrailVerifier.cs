using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class IceShardTrailVerifier
{
    public static void Run(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle EditMode required.");
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>(); var failures = new List<string>(); var samples = new List<object>();
        void Check(bool pass, string label) { checks.Add(new { pass, label }); if (!pass) failures.Add(label); }
        var profile = AssetDatabase.LoadAssetAtPath<GreatswordElementFxProfile>(IceShardTrailBuilder.ProfilePath);
        Check(profile != null && profile.IceShardTrail != null, "Shared ice shard prefab assigned");
        var paths = GreatswordReferenceBindingBuilder.EquippedPaths();
        Check(paths.Length == 34, "34 equipped greatswords");
        var create = typeof(GreatswordReferenceBindingVerifier).GetMethod("CreatePreview", BindingFlags.Static | BindingFlags.NonPublic);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (string path in paths)
            {
                GameObject instance = null;
                try
                {
                    instance = (GameObject)create.Invoke(null, new object[] { AssetDatabase.LoadAssetAtPath<GameObject>(path), scene, 0 });
                    var fx = instance.GetComponent<MeleeWeaponElementFx>();
                    foreach (float energy in new[] { 0f, .01f, .05f, .1f, .2f, .5f, 1f, 0f, .1f, 1f })
                    {
                        fx.EditorPreviewEnergy(WeaponElement.Ice, energy);
                        var wake = fx.GetComponentInChildren<WeaponIceShardTrail>(true);
                        Check(wake != null, path + " shard instance " + energy);
                        var shards = wake.GetComponentInChildren<ParticleSystem>();
                        var original = IceLowEnergyTrailVerifier.WorldParticles(fx);
                        Check(original.All(p => !p.emission.enabled && !p.GetComponent<Renderer>().enabled && p.particleCount == 0), path + " old world wake suppressed " + energy);
                        fx.ClearTrail();
                        for (int frame = 0; frame < 50; frame++)
                        {
                            instance.transform.rotation = Quaternion.Euler(0, 0, frame * 2f);
                            instance.transform.position += new Vector3(.015f, 0, 0);
                            fx.EditorAdvanceEnergyPreview(1f / 60f);
                        }
                        var particles = new ParticleSystem.Particle[shards.particleCount]; int count = shards.GetParticles(particles);
                        float max = count == 0 ? 0 : particles.Max(p => p.GetCurrentSize3D(shards).x);
                        float min = count == 0 ? 0 : particles.Min(p => p.startSize);
                        Check(energy == 0 ? count == 0 && !shards.GetComponent<Renderer>().enabled : count > 0 && shards.GetComponent<Renderer>().enabled, path + " charge emission/zero " + energy);
                        Check(particles.All(p => Finite(p.position) && Finite(p.velocity) && Finite(p.GetCurrentSize3D(shards))), path + " finite/world size " + energy);
                        Check(max <= .135f * Mathf.Pow(energy, .35f) + .0001f, path + " low-energy size bound " + energy);
                        if (energy >= .1f) Check(min < max, path + " varied sizes " + energy);
                        samples.Add(new { path, energy, count, min, max });
                        for (int frame = 0; frame < 40; frame++) fx.EditorAdvanceEnergyPreview(1f / 60f);
                        Check(wake.ParticleCount == 0, path + " stationary fade " + energy);
                    }
                    fx.EditorPreviewEnergy(WeaponElement.Fire, 1f);
                    Check(fx.GetComponentInChildren<WeaponIceShardTrail>(true) == null, path + " element swap disposes shards");
                    fx.EditorClearEnergyPreview();
                }
                finally { if (instance != null) UnityEngine.Object.DestroyImmediate(instance); }
            }
            foreach (var element in new[] { WeaponElement.Fire, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light })
            {
                var instance = (GameObject)create.Invoke(null, new object[] { AssetDatabase.LoadAssetAtPath<GameObject>(paths[0]), scene, 0 });
                try
                {
                    var fx = instance.GetComponent<MeleeWeaponElementFx>(); fx.EditorPreviewEnergy(element, 1);
                    for (int i = 0; i < 150; i++) fx.EditorAdvanceEnergyPreview(1f / 60f);
                    Check(fx.GetComponentInChildren<WeaponIceShardTrail>(true) == null, element + " no ice wake");
                    Check(fx.GetComponentsInChildren<ParticleSystem>(true).Any(p => p.main.simulationSpace == ParticleSystemSimulationSpace.World && p.emission.enabled), element + " original world emission retained");
                    fx.EditorClearEnergyPreview();
                }
                finally { UnityEngine.Object.DestroyImmediate(instance); }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        Check(!scene.IsValid(), "Owned preview closed");
        File.WriteAllText(Path.Combine(directory, "IceShardVerification.json"), JsonConvert.SerializeObject(new {
            status = failures.Count == 0 ? "PASS" : "FAIL", weapons = paths.Length, cases = paths.Length * 10, checks = checks.Count, failures, samples
        }, Formatting.Indented));
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(" | ", failures.Take(8)));
    }
    static bool Finite(Vector3 value) => !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));
}
