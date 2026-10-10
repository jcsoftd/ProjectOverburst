using System;
using System.IO;
using System.Linq;
using Overburst.EditorTools.MonsterTuner;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MonsterTunerVerifier
{
    public static string Features()
    {
        Checks.Clear(); Directory.CreateDirectory(Output);
        var catalog = new MonsterTunerCatalog(); catalog.Refresh();
        var samples = catalog.Entries.OrderBy(e => e.Definition.ActorPrefab.GetComponent<CombatTargetVfxPlacement>()?.VisualVolume.HalfHeight ?? 1).ToArray();
        int live = MonsterTunerPreviewStage.LiveStages, threats = EnemyStrongAttackWarning.ActiveThreatSignalCount;
        try
        {
            foreach (var entry in new[] { samples.First(), samples[samples.Length / 2], samples.Last() }.Distinct())
            {
                var session = MonsterTunerSession.Create(entry.Definition, false); var stage = new MonsterTunerPreviewStage();
                try
                {
                    stage.Load(session);
                    foreach (var type in new[] { MeleeElementStatusAuraType.Burning, MeleeElementStatusAuraType.Shocked, MeleeElementStatusAuraType.Corroded })
                    foreach (int stacks in new[] { 1, 3, 5 })
                    {
                        float time = stage.Time; stage.ShowAura(type, stacks, null);
                        var presentation = Get<MeleeElementStatusAuraPresentation>(stage, "auraPresentation");
                        Check(entry.Label + " " + type + " " + stacks + "중첩 활성", presentation != null && presentation.IsAuraActive(type));
                        Check("오라 재시작 모션 시간 유지", stage.Time == time);
                        stage.Sample(.3f); stage.Sample(.1f); stage.Render(640, 480);
                        Check("오라 프리뷰 전역 등록 없음", EnemyStrongAttackWarning.ActiveThreatSignalCount == threats && !presentation.enabled);
                        if (stacks == 3) Capture(stage.Surface, Path.Combine(Output, "aura-" + Array.IndexOf(samples, entry) + "-" + type + ".png"));
                    }
                    var presentationReuse = Get<MeleeElementStatusAuraPresentation>(stage, "auraPresentation");
                    var target = stage.Actor.GetComponent<CombatTarget>(); var placement = stage.Actor.GetComponent<CombatTargetVfxPlacement>();
                    string address = MonsterTunerAddress.Component(stage.Actor, placement);
                    var offset = session.Value(address, "shockOffset"); offset.vector = new Vector3(.3f, .2f, -.1f); session.Set(address, "shockOffset", offset, "검증 보정"); stage.RefreshValues();
                    var shocked = Get<GameObject>(presentationReuse, "shockedAura");
                    Check("감전 위치 보정 실효값", Vector3.Distance(shocked.transform.position, placement.StatusAuraCenter + stage.Actor.transform.rotation * offset.vector) < .001f);
                    Vector3 once = shocked.transform.lossyScale;
                    for (int i = 0; i < 5; i++) presentationReuse.ConfigureTarget(target);
                    Check("반복 타깃 설정 크기 누적 없음", Vector3.Distance(once, shocked.transform.lossyScale) < .0001f);
                    session.Discard(); stage.RefreshValues();
                    CombatTargetVfxPlacement.ResolveAuraTuning(target, MeleeElementStatusAuraType.Shocked, out var resetOffset, out _);
                    Check("타깃 보정 초기화", Vector3.Distance(shocked.transform.position, placement.StatusAuraCenter + resetOffset) < .001f);
                    var otherSession = MonsterTunerSession.Create(samples.First(e => e != entry).Definition, false);
                    var otherStage = new MonsterTunerPreviewStage();
                    try
                    {
                        otherStage.Load(otherSession);
                        var otherTarget = otherStage.Actor.GetComponent<CombatTarget>();
                        presentationReuse.ClearAllAuras(); presentationReuse.ConfigureTarget(otherTarget);
                        CombatTargetVfxPlacement.ResolveAuraTuning(otherTarget, MeleeElementStatusAuraType.Shocked, out var otherOffset, out _);
                        Check("다른 타깃 재대여 이전 보정 제거", Vector3.Distance(shocked.transform.position, otherStage.Actor.GetComponent<CombatTargetVfxPlacement>().StatusAuraCenter + otherOffset) < .001f);
                        presentationReuse.ConfigureTarget(target);
                        Check("재대여 후 원래 타깃 크기 복원", Vector3.Distance(shocked.transform.lossyScale, once) < .0001f);
                    }
                    finally { otherStage.Dispose(); Object.DestroyImmediate(otherSession); }
                    Check("비교 효과 저장 차이 없음", !session.Dirty);
                    for (int i = 0; i < entry.Definition.AbilitySet.Count; i++)
                    {
                        var ability = entry.Definition.AbilitySet.GetAbility(i); if (ability == null) continue;
                        stage.SetClip(MonsterTunerAnimationBindings.AttackClip(session, ability, i), ability);
                        stage.Sample(Mathf.Max(0f, ability.ResolveFirstImpactTime(stage.AttackSpeed) - .1f));
                        var cue = Get<ParticleSystem>(stage, "cueParticles");
                        Check("패링 신호 근접 강공 제한", ability.IsParryable ? cue != null && cue.gameObject.activeSelf : cue == null || !cue.gameObject.activeSelf);
                        if (ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile)
                        {
                            stage.Sample(ability.ResolveFirstImpactTime(stage.AttackSpeed) + .15f);
                            Check("발사 프레임 궤적", stage.ProjectileVisible && Mathf.Abs(Vector3.Distance(stage.LaunchOrigin, stage.ProjectilePosition) - 1.5f) < .01f);
                        }
                    }
                }
                finally { stage.Dispose(); Object.DestroyImmediate(session); }
                Check("효과·패링·프리뷰 정리", MonsterTunerPreviewStage.LiveStages == live && EnemyStrongAttackWarning.ActiveThreatSignalCount == threats);
            }
            return Save("features", true, "");
        }
        catch (Exception e) { return Save("features", false, e.ToString()); }
    }
}
