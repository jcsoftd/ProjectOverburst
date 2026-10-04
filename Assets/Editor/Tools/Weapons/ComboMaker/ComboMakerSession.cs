using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Overburst.EditorTools.ComboMaker
{
    internal sealed class ComboMakerSession : IDisposable
    {
        public MeleeComboDefinition Source { get; private set; }
        public MeleeComboDefinition Working { get; private set; }
        public MeleeHeavyAttackDefinition HeavySource { get; private set; }
        public MeleeHeavyAttackDefinition HeavyWorking { get; private set; }
        public bool IsHeavy => HeavySource != null;
        public UnityEngine.Object Asset => IsHeavy ? (UnityEngine.Object)HeavySource : Source;
        private string baseline;
        public bool Dirty => Working != null && Capture() != baseline;
        public bool SourceChanged => Asset != null && EditorJsonUtility.ToJson(Asset) != baseline;
        public string Capture()
        {
            if (Working == null) return "";
            if (IsHeavy) { SyncHeavy(); return EditorJsonUtility.ToJson(HeavyWorking); }
            return EditorJsonUtility.ToJson(Working);
        }
        public string Baseline => baseline;

        public void LoadWeapon(WeaponItemData weapon, ComboMakerAttackMode mode, string recovery=null, string originalBaseline=null)
        {
            var definition = weapon != null ? weapon.GetMeleeDefinition() : null;
            var asset = ComboMakerAttackBinding.Asset(definition, mode);
            if (asset == null) throw new InvalidOperationException("이 무기에 연결된 " + ComboMakerAttackBinding.Label(mode) + " 자산이 없습니다.");
            if (asset is MeleeHeavyAttackDefinition heavy)
                LoadHeavy(definition.comboDefinition, heavy, recovery, originalBaseline);
            else Load((MeleeComboDefinition)asset, recovery, originalBaseline);
        }

        public void Load(MeleeComboDefinition source, string recovery = null, string originalBaseline = null)
        {
            Dispose();
            Source = source;
            if (source == null) return;
            baseline = NormalizeBaseline(source, originalBaseline);
            Working = ScriptableObject.CreateInstance<MeleeComboDefinition>();
            Working.hideFlags = HideFlags.HideAndDontSave;
            EditorJsonUtility.FromJsonOverwrite(recovery ?? baseline, Working);
        }

        public void LoadHeavy(MeleeComboDefinition binding, MeleeHeavyAttackDefinition source, string recovery=null, string originalBaseline=null)
        {
            Dispose(); Source=binding; HeavySource=source;
            if(source==null) throw new InvalidOperationException("이 무기에 연결된 강공 자산이 없습니다.");
            baseline=NormalizeBaseline(source, originalBaseline);
            HeavyWorking=ScriptableObject.CreateInstance<MeleeHeavyAttackDefinition>();
            HeavyWorking.hideFlags=HideFlags.HideAndDontSave;
            EditorJsonUtility.FromJsonOverwrite(recovery??baseline,HeavyWorking);
            Working=ScriptableObject.CreateInstance<MeleeComboDefinition>();
            Working.hideFlags=HideFlags.HideAndDontSave;
            Working.baseAnimationSpeed=1; Working.resetDelay=1;
            Working.steps=new[]{HeavyWorking.attack};
            Working.steps[0].comboInputWindow=new ComboNormalizedWindow{startNormalizedTime=0,endNormalizedTime=1};
        }
        private static string NormalizeBaseline(UnityEngine.Object source, string snapshot)
        {
            if (string.IsNullOrEmpty(snapshot)) return EditorJsonUtility.ToJson(source);
            // Re-read old drafts through the current schema. Newly added optional Cue fields
            // must not look like an external asset edit, while real authored changes still do.
            var copy = ScriptableObject.CreateInstance(source.GetType());
            try
            {
                EditorJsonUtility.FromJsonOverwrite(snapshot, copy);
                return EditorJsonUtility.ToJson(copy);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }

        private void SyncHeavy()
        {
            var step=Working.steps[0];step.comboInputWindow=HeavyWorking.attack.comboInputWindow;
            HeavyWorking.attack=step;
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (Working == null) { errors.Add("무기를 선택하세요."); return errors; }
            if (!Working.TryGetStableAttackIds(out _, out var idError)) errors.Add(idError);
            if (!FinitePositive(Working.baseAnimationSpeed)) errors.Add("기본 속도는 0보다 커야 합니다.");
            if (!FinitePositive(Working.resetDelay)) errors.Add("콤보 대기시간은 0보다 커야 합니다.");
            if (IsHeavy && (!FinitePositive(HeavyWorking.chargedDamageMultiplier) || !FinitePositive(HeavyWorking.emptyDamageMultiplier)))
                errors.Add("강공 피해 배율은 0보다 커야 합니다.");
            if (IsHeavy && (HeavyWorking.dischargePhaseIndex < 0 || HeavyWorking.dischargePhaseIndex >= (Working.steps[0].attackPhases?.Length ?? 0)))
                errors.Add("원소 방출 판정 인덱스는 현재 타격 구간 안에 있어야 합니다. 첫 구간은 0입니다.");
            for (int i = 0; i < Working.StepCount; i++)
            {
                var step = Working.steps[i];
                if (!MeleeComboStepValidator.TryValidate(step, out var error)) errors.Add($"{i + 1}타: {error}");
                if (!FinitePositive(step.animationSpeedMultiplier)) errors.Add($"{i + 1}타: 속도는 0보다 커야 합니다.");
                if (!FiniteNormalized(step.actionCancelStartNormalized) || !FiniteNormalized(step.continuationStartNormalizedTime) || !FiniteNormalized(step.settledIdleStartNormalizedTime))
                    errors.Add($"{i + 1}타: 진입점과 취소 시점은 0~1이어야 합니다.");
                if (float.IsNaN(step.transitionDuration) || float.IsInfinity(step.transitionDuration) || step.transitionDuration < 0)
                    errors.Add($"{i + 1}타: 블렌딩 시간은 0 이상이어야 합니다.");
                if (step.continuationStartNormalizedTime < 0 || step.continuationStartNormalizedTime >= 1)
                    errors.Add($"{i + 1}타: 연계 진입점은 0 이상 1 미만이어야 합니다.");
                foreach (var phase in step.attackPhases ?? Array.Empty<AttackPhaseData>())
                {
                    if (i > 0 && step.continuationStartNormalizedTime > phase.SafeStart)
                        errors.Add($"{i + 1}타: 연계 진입점이 타격 구간을 건너뜁니다.");
                    foreach(var cue in phase.vfxCues ?? Array.Empty<AttackVfxCueData>())
                    {
                        if(cue.definition==null) errors.Add($"{i+1}타: VFX Cue의 정의가 비어 있습니다.");
                        if(!FiniteNormalized(cue.triggerProgress)) errors.Add($"{i+1}타: VFX 발동 진행률은 0~1이어야 합니다.");
                    }
                }
            }
            return errors;
        }

        public void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play 종료 후 적용하세요.");
            if (SourceChanged) throw new InvalidOperationException("원본이 다른 곳에서 변경되었습니다. 원본을 다시 불러온 뒤 편집하세요.");
            var errors = Validate();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            // Bake may fail: until it succeeds, Source and its disk contents remain untouched.
            MeleeAttackVfxSlopeBakeUtility.BakeWorkingCopy(Source, Working);
            if (IsHeavy)
            {
                SyncHeavy();
                Undo.RegisterCompleteObjectUndo(HeavySource,"강공 메이커 적용");
                EditorUtility.CopySerialized(HeavyWorking,HeavySource);HeavySource.hideFlags=HideFlags.None;
                EditorUtility.SetDirty(HeavySource);AssetDatabase.SaveAssetIfDirty(HeavySource);
                baseline=EditorJsonUtility.ToJson(HeavySource);
                EditorJsonUtility.FromJsonOverwrite(baseline,HeavyWorking);
                return;
            }
            Undo.RegisterCompleteObjectUndo(Source, "콤보 메이커 적용");
            EditorUtility.CopySerialized(Working, Source);
            Source.hideFlags = HideFlags.None;
            EditorUtility.SetDirty(Source);
            AssetDatabase.SaveAssetIfDirty(Source);
            baseline = EditorJsonUtility.ToJson(Source);
            EditorJsonUtility.FromJsonOverwrite(baseline, Working);
        }

        public static float Duration(MeleeComboDefinition combo, int index) =>
            combo.steps[index].animationClip == null ? 1f : combo.steps[index].animationClip.length /
            Mathf.Max(.01f, combo.baseAnimationSpeed * combo.steps[index].animationSpeedMultiplier);
        private static bool FinitePositive(float x) => x > 0 && !float.IsInfinity(x) && !float.IsNaN(x);
        private static bool FiniteNormalized(float x) => !float.IsNaN(x) && x>=0 && x<=1;
        public void Dispose()
        {
            if (Working != null) UnityEngine.Object.DestroyImmediate(Working);
            if (HeavyWorking != null) UnityEngine.Object.DestroyImmediate(HeavyWorking);
            HeavyWorking=null;HeavySource=null;
            Working = null;
        }
    }
}
