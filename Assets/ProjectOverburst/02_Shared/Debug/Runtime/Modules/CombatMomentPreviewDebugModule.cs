#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace Overburst.DebugTools
{
    internal static class CombatMomentPreviewDebugModule
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            var section=DebugRegistry.Section(DebugTabs.Presentation,"전투 순간 연출 · 임시 비교",15,"이번 Play에서만 적용 · 기본 켜짐");
            section.Toggle("패링 순간 연출",()=>CombatMomentPresentation.ParryEnabled,CombatMomentPresentation.SetParryEnabled)
                .WithId("presentation.moment.parry").NoPreset().Tip("추가 접촉 불꽃·검날 빛·화면 반응을 비교한다. 기존 패링 연출은 유지한다.");
            section.Toggle("완충 강공 순간 연출",()=>CombatMomentPresentation.HeavyEnabled,CombatMomentPresentation.SetHeavyEnabled)
                .WithId("presentation.moment.heavy").NoPreset().Tip("기본 에너지 완충의 첫 방출만 강조한다. 빛 과충전은 국소 발광이 조금 강하다.");
            section.Toggle("순간 화면 반응",()=>CombatMomentPresentation.ScreenEnabled,CombatMomentPresentation.SetScreenEnabled)
                .WithId("presentation.moment.screen").NoPreset();
            section.Toggle("순간 검날 발광",()=>CombatMomentPresentation.BladeEnabled,CombatMomentPresentation.SetBladeEnabled)
                .WithId("presentation.moment.blade").NoPreset();
            section.Toggle("접촉·방출 코어",()=>CombatMomentPresentation.LocalEnabled,CombatMomentPresentation.SetLocalEnabled)
                .WithId("presentation.moment.local").NoPreset();
        }
    }
}
#endif
