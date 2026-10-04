#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// 제품 적중/공격 큐 호출과 Unity sceneUnloaded 이벤트를 함께 사용한다.
// 임시 씬은 계약 검사용이며 게임 전체 씬 전환 재현으로 확대하지 않는다.
public static class VfxSceneOwnershipChecks
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    public static IEnumerator Run(string output, string expectedAccount)
    {
        if (!EditorApplication.isPlaying || !Overburst.Persistence.AccountBootstrap.Ready || string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)) throw new InvalidOperationException("Owned isolated product Play required.");
        output = Path.GetFullPath(output);
        if (!string.Equals(Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory), Path.GetFullPath(expectedAccount), StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetFullPath(IsolatedSavePlayGuard.ActiveDirectory), Path.GetFullPath(expectedAccount), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Account does not belong to this isolated session.");
        Directory.CreateDirectory(output);
        var checks = new List<object>();
        void Check(bool pass, string label, object evidence = null)
        {
            checks.Add(new { pass, label, frame = Time.frameCount, evidence });
            File.WriteAllText(Path.Combine(output, "vfx-progress.json"), JsonConvert.SerializeObject(checks, Formatting.Indented));
            if (!pass) throw new InvalidOperationException(label);
        }
        Scene previousContent = WorldSessionState.ContentScene;
        Scene a = default, b = default, c = default;
        var ownedInstances = new HashSet<GameObject>();
        GameObject fixturePrefab = null;
        GameObject borrowedFireInstance = null;
        MeleeAttackVfxDefinition definition = null;
        MeleeElementHitVfxCatalog clone = null;
        var catalogField = typeof(MeleeElementHitVfxService).GetField("catalog", All);
        var attemptedField = typeof(MeleeElementHitVfxService).GetField("loadAttempted", All);
        object originalCatalog = catalogField.GetValue(null), originalAttempted = attemptedField.GetValue(null);
        var contentSetter = typeof(WorldSessionState).GetMethod("SetContentScene", All);
        string status = "ABORTED";
        try
        {
            Check(previousContent.IsValid() && previousContent.isLoaded, "Real content scene is loaded before fixture ownership checks", new { previousContent.name, previousContent.handle });
            a = SceneManager.CreateScene("KAN9_Hit_A_" + Guid.NewGuid().ToString("N"));
            b = SceneManager.CreateScene("KAN9_Cue_B_" + Guid.NewGuid().ToString("N"));
            c = SceneManager.CreateScene("KAN9_Reuse_C_" + Guid.NewGuid().ToString("N"));
            var targetObject = new GameObject("KAN9OwnedHitTarget"); SceneManager.MoveGameObjectToScene(targetObject, a);
            var target = targetObject.AddComponent<CombatHealth>();
            var authored = Resources.Load<MeleeElementHitVfxCatalog>(MeleeElementHitVfxCatalog.ResourcePath);
            Check(authored != null && authored.TryResolve(WeaponElement.Fire, out _), "Authored Fire hit content resolves");
            clone = UnityEngine.Object.Instantiate(authored); clone.hideFlags = HideFlags.HideAndDontSave;
            clone.fireLifetime = 60f; clone.firePlaybackSpeed = 1f;
            catalogField.SetValue(null, clone); attemptedField.SetValue(null, true);
            clone.TryResolve(WeaponElement.Fire, out var firePrefab);
            long fireReturnsBefore = TransientVfxPool.GetStatistics(firePrefab).Returns;
            fixturePrefab = new GameObject("KAN9OwnedCuePrefab"); fixturePrefab.SetActive(false);
            definition = ScriptableObject.CreateInstance<MeleeAttackVfxDefinition>(); definition.hideFlags = HideFlags.HideAndDontSave;
            definition.neutralPrefab = fixturePrefab; definition.lifetime = 60f;
            var persistent = TransientVfxPool.Spawn(fixturePrefab, Vector3.zero, Quaternion.identity, 60f, 8, useUnscaledTime: true);
            ownedInstances.Add(persistent);
            Check(persistent != null && OwnerOf(persistent) == 0, "Default handle0 lifetime remains independent");
            contentSetter.Invoke(null, new object[] { b });
            var player = new AttackVfxCuePlayer();
            player.Begin(new AttackPhaseData { vfxCues = new[] { new AttackVfxCueData { definition = definition, triggerProgress = 0f, placementMode = AttackVfxPlacementMode.OwnerOrigin, scaleMultiplier = 1f } } });
            player.Tick(default, new AttackPatternBasis(Vector3.zero, Vector3.forward), 1f, default, WeaponElement.None, 1f, 1f);
            var cue = Leases().FirstOrDefault(l => ReferenceEquals(Field<GameObject>(l, "Prefab"), fixturePrefab) && Field<int>(l, "ContentSceneHandle") == b.handle);
            Check(cue != null, "Full AttackVfxCuePlayer Begin/Tick passes content scene B");
            var cueObject = Field<GameObject>(cue, "Instance");
            ownedInstances.Add(cueObject);
            CombatHitFeedbackService.Request(new CombatHitFeedbackRequest(target, 1, null, false, WeaponElement.Fire, Vector3.zero, false, target: target));
            var hit = Leases().FirstOrDefault(l => ReferenceEquals(Field<GameObject>(l, "Prefab"), firePrefab) && Field<int>(l, "ContentSceneHandle") == a.handle);
            Check(hit != null, "Product CombatHitFeedbackService uses target scene A through element hit service");
            var hitObject = Field<GameObject>(hit, "Instance");
            borrowedFireInstance = hitObject;
            ownedInstances.Add(hitObject);
            var beforeB = TransientVfxPool.GetStatistics(fixturePrefab);
            var unloadB = SceneManager.UnloadSceneAsync(b); while (!unloadB.isDone) yield return null;
            var afterB = TransientVfxPool.GetStatistics(fixturePrefab);
            Check(OwnerOf(cueObject) == null && !cueObject.activeSelf && afterB.Active == beforeB.Active - 1 && afterB.Returns == beforeB.Returns + 1, "B unload stops cue and returns exactly its lease", new { beforeB, afterB });
            Check(OwnerOf(hitObject) == a.handle && hitObject.activeSelf && OwnerOf(persistent) == 0 && persistent.activeSelf, "B unload preserves A hit and independent handle0 effect");
            var oldA = TransientVfxPool.Spawn(fixturePrefab, Vector3.zero, Quaternion.identity, .1f, 8, useUnscaledTime: true, contentSceneHandle: a.handle);
            ownedInstances.Add(oldA);
            Check(ReferenceEquals(oldA, cueObject), "Returned B cue instance is reused for A lease");
            float end = Time.unscaledTime + 5f; while (OwnerOf(oldA) != null && Time.unscaledTime < end) yield return null;
            Check(OwnerOf(oldA) == null, "Old A lease returns by its own lifetime");
            var reused = TransientVfxPool.Spawn(fixturePrefab, Vector3.zero, Quaternion.identity, 60f, 8, useUnscaledTime: true, contentSceneHandle: c.handle);
            ownedInstances.Add(reused);
            Check(ReferenceEquals(oldA, reused) && OwnerOf(reused) == c.handle, "Same instance receives a fresh C ownership lease");
            var unloadA = SceneManager.UnloadSceneAsync(a); while (!unloadA.isDone) yield return null;
            Check(OwnerOf(hitObject) == null && !hitObject.activeSelf && hitObject.GetComponentsInChildren<ParticleSystem>(true).All(p => !p.IsAlive(true)) && TransientVfxPool.GetStatistics(firePrefab).Returns == fireReturnsBefore + 1, "A unload stops and clears real Fire particles and returns its pool resource");
            Check(OwnerOf(reused) == c.handle && reused.activeSelf, "Unloading old owner A does not return reused C lease");
            Check(OwnerOf(persistent) == 0 && persistent.activeSelf, "Second scene unload still preserves handle0 independent lifetime");
            var natural = TransientVfxPool.Spawn(fixturePrefab, Vector3.zero, Quaternion.identity, .1f, 8, useUnscaledTime: true);
            ownedInstances.Add(natural);
            end = Time.unscaledTime + 5f; while (OwnerOf(natural) != null && Time.unscaledTime < end) yield return null;
            Check(OwnerOf(natural) == null && !natural.activeSelf && OwnerOf(reused) == c.handle && OwnerOf(persistent) == 0, "Short handle0 effect returns naturally while C and long handle0 effects remain active");
            var unloadC = SceneManager.UnloadSceneAsync(c); while (!unloadC.isDone) yield return null;
            Check(OwnerOf(reused) == null && !reused.activeSelf && OwnerOf(persistent) == 0 && TransientVfxPool.GetStatistics(fixturePrefab).Active == 1, "Final C unload returns scoped lease and preserves the independent long handle0 lease");
            status = "PASS_SCOPED";
        }
        finally
        {
            ReturnOwned(ownedInstances);
            foreach (var instance in ownedInstances) if (instance != null && instance != borrowedFireInstance) UnityEngine.Object.DestroyImmediate(instance);
            if (fixturePrefab != null)
            {
                ((IDictionary)typeof(TransientVfxPool).GetField("Pools", All).GetValue(null)).Remove(fixturePrefab);
                ((IDictionary)typeof(TransientVfxPool).GetField("Diagnostics", All).GetValue(null)).Remove(fixturePrefab);
            }
            catalogField.SetValue(null, originalCatalog); attemptedField.SetValue(null, originalAttempted);
            contentSetter.Invoke(null, new object[] { previousContent });
            foreach (Scene scene in new[] { a, b, c }) if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            if (definition != null) UnityEngine.Object.Destroy(definition);
            if (clone != null) UnityEngine.Object.Destroy(clone);
            if (fixturePrefab != null) UnityEngine.Object.Destroy(fixturePrefab);
            File.WriteAllText(Path.Combine(output, "vfx-result.json"), JsonConvert.SerializeObject(new { status, checks, syntheticOwnershipScenes = true, realPlayerSceneTransitionResidual = "NOT_REPRODUCED", originalCatalogRestored = ReferenceEquals(catalogField.GetValue(null), originalCatalog), contentSceneRestored = WorldSessionState.ContentScene == previousContent }, Formatting.Indented));
        }
    }
    // 성공 판정 이후, 또는 실패 시 아직 활성인 이 검사의 대여 자원만 반환한다.
    static void ReturnOwned(HashSet<GameObject> instances)
    {
        object host = typeof(TransientVfxPool).GetField("host", All).GetValue(null);
        if (host == null) return;
        var leases = (IList)host.GetType().GetField("activeLeases", All).GetValue(host);
        var release = host.GetType().GetMethod("ReturnLeaseAt", All);
        for (int i = leases.Count - 1; i >= 0; i--)
            if (instances.Contains(Field<GameObject>(leases[i], "Instance"))) release.Invoke(host, new object[] { i });
    }
    static object[] Leases()
    {
        object host = typeof(TransientVfxPool).GetField("host", All).GetValue(null);
        if (host == null) return Array.Empty<object>();
        return ((IEnumerable)host.GetType().GetField("activeLeases", All).GetValue(host)).Cast<object>().ToArray();
    }
    static T Field<T>(object lease, string name) => (T)lease.GetType().GetField(name, All).GetValue(lease);
    static int? OwnerOf(GameObject instance)
    {
        var lease = Leases().FirstOrDefault(l => ReferenceEquals(Field<GameObject>(l, "Instance"), instance));
        return lease == null ? (int?)null : Field<int>(lease, "ContentSceneHandle");
    }
}
#endif
