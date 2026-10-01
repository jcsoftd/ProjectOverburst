using System;
using System.IO;
using System.Linq;
using Overburst.EditorTools.MonsterTuner;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MonsterTunerVerifier
{
    // Editor-only fixtures are owned by this verifier and are removed by their exact unique folder.
    // Production assets are used as read-only sources; every production file is hashed before/after.
    public static string SaveRoundTrip(bool keepForPlay = false)
    {
        Checks.Clear(); Directory.CreateDirectory(Output);
        string fixtures = "Assets/Editor/Testers/Characters/MonsterTuner/Fixtures";
        if (!AssetDatabase.IsValidFolder(fixtures)) AssetDatabase.CreateFolder("Assets/Editor/Testers/Characters/MonsterTuner", "Fixtures");
        string unique = "Run_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder(fixtures, unique); string root = fixtures + "/" + unique;
        var catalog = new MonsterTunerCatalog(); catalog.Refresh();
        var source = catalog.Entries.First(e => e.Definition.AbilitySet != null && Enumerable.Range(0, e.Definition.AbilitySet.Count).Any(i => e.Definition.AbilitySet.GetAbility(i)?.ExecutionMode == EnemyAbilityExecutionMode.Projectile)).Definition;
        var sources = AssetDatabase.GetDependencies(catalog.Entries.Select(e => AssetDatabase.GetAssetPath(e.Definition)).ToArray(), true)
            .Where(p => (p.StartsWith("Assets/ProjectOverburst/", StringComparison.Ordinal) || p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) && File.Exists(p)).Distinct().ToArray();
        var hashes = sources.ToDictionary(p => p, MonsterTunerStamp.FileHash);
        MonsterTunerSession session = null;
        bool keep = false;
        try
        {
            Check("fixture 프리팹 사본", AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source.ActorPrefab), root + "/Actor.prefab"));
            Check("fixture 정의 사본", AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), root + "/Definition.asset"));
            Check("fixture 공유 정의 사본", AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), root + "/Peer.asset"));
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(root + "/Definition.asset");
            var peer = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(root + "/Peer.asset");
            var actor = AssetDatabase.LoadAssetAtPath<GameObject>(root + "/Actor.prefab").GetComponent<EnemyActor>();
            SetFixtureReference(definition, "actorPrefab", actor); SetFixtureReference(peer, "actorPrefab", actor);
            catalog.Entries.Add(new MonsterTunerCatalog.Entry { Definition = definition, Guid = AssetDatabase.AssetPathToGUID(root + "/Definition.asset") });
            catalog.Entries.Add(new MonsterTunerCatalog.Entry { Definition = peer, Guid = AssetDatabase.AssetPathToGUID(root + "/Peer.asset") });
            session = MonsterTunerSession.Create(definition, false);
            foreach (string field in new[] { "visualScale", "collisionScale", "anchorScale" })
            {
                var value = session.Value("variant", field); value.vector = Vector3.one * 1.2f; session.Set("variant", field, value, field);
            }
            var collider = actor.GetComponentInChildren<CapsuleCollider>(true);
            if (collider != null)
            {
                string address = MonsterTunerAddress.Component(actor.gameObject, collider);
                var value = session.Value(address, "m_Center"); value.vector += new Vector3(.1f, .05f, -.1f); session.Set(address, "m_Center", value, "몸 충돌 중심");
            }
            var placement = actor.GetComponent<CombatTargetVfxPlacement>();
            Check("fixture 오라 배치 연결", placement != null);
            string vfxAddress = MonsterTunerAddress.Component(actor.gameObject, placement);
            var offset = session.Value(vfxAddress, "shockOffset"); offset.vector = new Vector3(.12f, .2f, -.1f); session.Set(vfxAddress, "shockOffset", offset, "감전 위치");
            var scale = session.Value(vfxAddress, "corrosionScale"); scale.number = 1.15f; session.Set(vfxAddress, "corrosionScale", scale, "잠식 크기");
            int projectileIndex = Enumerable.Range(0, definition.AbilitySet.Count).First(i => definition.AbilitySet.GetAbility(i)?.ExecutionMode == EnemyAbilityExecutionMode.Projectile);
            var projectileSource = definition.AbilitySet.GetAbility(projectileIndex);
            var range = session.Value("ability:" + projectileIndex, "range"); range.number += .5f; session.Set("ability:" + projectileIndex, "range", range, "공격 사거리");
            var preview = new MonsterTunerPreviewStage();
            string warningAddress, muzzleAddress;
            try
            {
                preview.Load(session);
                warningAddress = MonsterTunerAddress.Component(preview.Actor, preview.Actor.GetComponent<EnemyStrongAttackWarning>());
                var warningOffset = session.Value(warningAddress, "cueOffset"); warningOffset.vector = new Vector3(.15f, .25f, -.1f);
                session.Set(warningAddress, "cueOffset", warningOffset, "패링 예고 보정");
                muzzleAddress = MonsterTunerAddress.Component(preview.Actor, preview.Actor.GetComponent<EnemyThemeSpecialExecutor>());
                var muzzles = session.Value(muzzleAddress, "muzzleOverrides");
                muzzles.muzzles = new[] { new MonsterTunerMuzzleValue {
                    ability = new MonsterTunerValue { type = SerializedPropertyType.ObjectReference, text = GlobalObjectId.GetGlobalObjectIdSlow(projectileSource).ToString() },
                    socket = new MonsterTunerValue { type = SerializedPropertyType.ObjectReference, text = "actor:" + MonsterTunerAddress.Path(preview.Actor.transform, preview.Enemy.Anchors) },
                    offset = new Vector3(.1f, .7f, .2f) } };
                session.Set(muzzleAddress, "muzzleOverrides", muzzles, "능력별 머즐");
            }
            finally { preview.Dispose(); }
            var motionBindings = MonsterTunerAnimationBindings.Read(definition.AnimationProfile);
            var attackBinding = motionBindings.First(b => b.StatePath.Contains(".Attack_"));
            int meleeIndex = Enumerable.Range(0, definition.AbilitySet.Count).First(i => definition.AbilitySet.GetAbility(i)?.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc || definition.AbilitySet.GetAbility(i)?.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam);
            var firstHit = session.Value("ability:" + meleeIndex, "hitNormalizedTime"); firstHit.number = .4f; session.Set("ability:" + meleeIndex, "hitNormalizedTime", firstHit, "검증용 첫 타격");
            var moreHits = session.Value("ability:" + meleeIndex, "additionalHitNormalizedTimes"); moreHits.numbers = new[] { .6f, .8f }; session.Set("ability:" + meleeIndex, "additionalHitNormalizedTimes", moreHits, "검증용 추가 타격");
            var replacementClip = Object.Instantiate(attackBinding.Actual); replacementClip.name = "ReplacementDifferentName"; replacementClip.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(replacementClip, root + "/Replacement.anim");
            var replacement = session.Value("animation", "motion:" + attackBinding.Key); replacement.text = GlobalObjectId.GetGlobalObjectIdSlow(replacementClip).ToString();
            session.Set("animation", "motion:" + attackBinding.Key, replacement, "실제 공격 모션 교체");
            foreach (var binding in motionBindings.Where(b => b.Label.StartsWith("패링", StringComparison.Ordinal)))
            {
                var clip = Object.Instantiate(binding.Actual); clip.name = "Role_" + binding.Label; clip.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(clip, root + "/Role_" + motionBindings.IndexOf(binding) + ".anim");
                var value = session.Value("animation", "motion:" + binding.Key); value.text = GlobalObjectId.GetGlobalObjectIdSlow(clip).ToString();
                session.Set("animation", "motion:" + binding.Key, value, "패링 역할 교체");
            }
            MonsterTunerWriter.FixtureRoot = root;
            var saved = MonsterTunerWriter.Save(session, catalog); Check("선택 한 마리 저장", saved.Success, saved.Message);
            Check("사본 저장 뒤 clean", !session.Dirty);
            Check("공유 Variant 개별 분리", definition.Variant != source.Variant && peer.Variant == source.Variant);
            Check("공유 Actor 개별 분리", definition.ActorPrefab != actor && peer.ActorPrefab == actor);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(definition.Variant), ImportAssetOptions.ForceUpdate);
            Check("크기 디스크 재로드", definition.Variant.VisualScale == Vector3.one * 1.2f && definition.Variant.CollisionScale == Vector3.one * 1.2f);
            var reloadedPlacement = definition.ActorPrefab.GetComponent<CombatTargetVfxPlacement>();
            Check("오라 위치 디스크 재로드", new SerializedObject(reloadedPlacement).FindProperty("shockOffset").vector3Value == offset.vector);
            Check("오라 크기 디스크 재로드", Mathf.Abs(new SerializedObject(reloadedPlacement).FindProperty("corrosionScale").floatValue - 1.15f) < .0001f);
            Check("공유 Ability/Set 선택 분리", definition.AbilitySet != source.AbilitySet && definition.AbilitySet.GetAbility(projectileIndex) != projectileSource && peer.AbilitySet == source.AbilitySet);
            Check("추가 타격 실제 자산 재로드", definition.AbilitySet.GetAbility(meleeIndex).HitCount == 3 && Mathf.Approximately(definition.AbilitySet.GetAbility(meleeIndex).GetHitNormalizedTime(2), .8f));
            var savedExecutor = definition.ActorPrefab.GetComponent<EnemyThemeSpecialExecutor>();
            var savedMuzzles = new SerializedObject(savedExecutor).FindProperty("muzzleOverrides");
            Check("분리 Ability 머즐 키 재연결", savedMuzzles.GetArrayElementAtIndex(0).FindPropertyRelative("ability").objectReferenceValue == definition.AbilitySet.GetAbility(projectileIndex));
            Check("누락 예고 컴포넌트 선택 추가", definition.ActorPrefab.GetComponent<EnemyStrongAttackWarning>() != null && peer.ActorPrefab.GetComponent<EnemyStrongAttackWarning>() == null);
            Check("예고 위치 디스크 재로드", new SerializedObject(definition.ActorPrefab.GetComponent<EnemyStrongAttackWarning>()).FindProperty("cueOffset").vector3Value == new Vector3(.15f, .25f, -.1f));
            var actualMotion = MonsterTunerAnimationBindings.Read(definition.AnimationProfile).First(b => b.Key == attackBinding.Key).Actual;
            Check("실제 Controller 교체 클립 재로드", actualMotion == replacementClip && definition.AnimationProfile != source.AnimationProfile);
            Check("원본 클립 이름 보존", attackBinding.Actual.name != "ReplacementDifferentName");
            var profile = definition.AnimationProfile;
            EnemyAnimationRoleResolver.ResolveParryDurations(profile.RuntimeController, profile, out float collapseTime, out float loopTime, out float recoverTime);
            Check("다른 이름 패링 역할 유지", profile.ParryCollapse != null && profile.StunnedLoop != null && profile.StunRecover != null && profile.ParryCollapse.name.StartsWith("Role_") && Mathf.Abs(collapseTime - profile.ParryCollapse.length) < .001f && loopTime > 0f && recoverTime > 0f);
            Check("미편집 몬스터 불변", hashes.All(pair => pair.Value == MonsterTunerStamp.FileHash(pair.Key)));

            session.ReadBaseline(); var baseline = session.stamps.ToDictionary(s => s.path, s => s.hash);
            var failedScale = session.Value("variant", "visualScale"); failedScale.vector = Vector3.one * 1.3f; session.Set("variant", "visualScale", failedScale, "실패 복구용 크기");
            MonsterTunerWriter.FailureHook = phase => { if (phase == "after-definition-save") throw new InvalidOperationException("intentional verifier fault"); };
            var failed = MonsterTunerWriter.Save(session, catalog);
            Check("저장 실패 성공 오표시 없음", !failed.Success);
            Check("저장 실패 보상 복구", failed.RolledBack, failed.Message);
            Check("실패 뒤 사본 보존", session.Dirty && session.Value("variant", "visualScale").vector == Vector3.one * 1.3f);
            Check("실패 뒤 기존 파일·GUID 보존", baseline.All(pair => pair.Value == MonsterTunerStamp.FileHash(pair.Key)));
            MonsterTunerWriter.FailureHook = null;
            session.Discard(); session.ReadBaseline();

            var draftScale = session.Value("variant", "visualScale"); draftScale.vector = Vector3.one * 1.25f; session.Set("variant", "visualScale", draftScale, "외부 충돌용 크기");
            var externallyChanged = new SerializedObject(definition.Variant); externallyChanged.FindProperty("anchorScale").vector3Value = Vector3.one * 1.21f;
            externallyChanged.ApplyModifiedPropertiesWithoutUndo();
            Check("외부 dirty 변경 저장 거부", !MonsterTunerWriter.Save(session, catalog).Success);
            Check("충돌 뒤 편집 사본 유지", session.Dirty);
            externallyChanged.FindProperty("anchorScale").vector3Value = Vector3.one * 1.2f; externallyChanged.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(definition.Variant);
            Check("게임 원본 최종 불변", hashes.All(pair => pair.Value == MonsterTunerStamp.FileHash(pair.Key)));
            session.Discard(); session.ReadBaseline();
            string restored = MonsterTunerHistory.Read(session.definitionGuid).RestoreDraft(session);
            Check("직전 저장 사본 복원", string.IsNullOrEmpty(restored) && session.Dirty, restored);
            var restoredSave = MonsterTunerWriter.Save(session, catalog);
            Check("복원 변경 검증 후 저장", restoredSave.Success, restoredSave.Message);
            Check("복원 실효 크기", definition.Variant.VisualScale == source.Variant.VisualScale);
            Check("복원 실제 모션", MonsterTunerAnimationBindings.Read(definition.AnimationProfile).First(b => b.Key == attackBinding.Key).Actual == attackBinding.Actual);
            Check("복원 후 게임 원본 불변", hashes.All(pair => pair.Value == MonsterTunerStamp.FileHash(pair.Key)));
            if (keepForPlay)
            {
                string redo = MonsterTunerHistory.Read(session.definitionGuid).RestoreDraft(session);
                Check("Play 저장 fixture 다시 적용", string.IsNullOrEmpty(redo) && MonsterTunerWriter.Save(session, catalog).Success, redo);
                SessionState.SetString("MonsterTunerPlay.Root", root); keep = true;
            }
            return Save("save-roundtrip", true, "");
        }
        catch (Exception e) { return Save("save-roundtrip", false, e.ToString()); }
        finally
        {
            MonsterTunerWriter.FailureHook = null; MonsterTunerWriter.FixtureRoot = null;
            if (session != null) { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
            if (!keep) AssetDatabase.DeleteAsset(root);
            if (AssetDatabase.IsValidFolder(fixtures) && Directory.Exists(fixtures) && Directory.GetFileSystemEntries(fixtures).Length == 0) AssetDatabase.DeleteAsset(fixtures);
        }
    }
    private static void SetFixtureReference(Object asset, string property, Object value)
    {
        var serialized = new SerializedObject(asset); serialized.FindProperty(property).objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
    }
}
