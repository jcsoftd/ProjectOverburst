using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class MeleeAttackVfxSlopeBakeUtility
{
    private const int SampleCount = 128;
    private const int PhaseDerivedSampleCount = 128;
    private const int MinimumSlopeSampleCount = 8;
    private const string PlayerPrefabPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    private const string OneHandSwordItemPath =
        "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/OHS01_FleurDeLys/OHS01_FleurDeLys.asset";
    private const string OneHandSwordComboPath =
        "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/Common/Combos/OneHandSwordPrimaryCombo.asset";
    private const string GreatswordItemPath =
        "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset";
    private const string GreatswordComboPath =
        "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Combos/GreatswordComboSet01.asset";
    private const string FailureSessionPrefix = "ProjectVTP.MeleeTrajectoryBakeFailure.";

    private readonly struct BakeProfile
    {
        public readonly string ItemPath;
        public readonly string ComboPath;
        public readonly string DisplayName;
        public readonly int ExpectedStepCount;
        public readonly int ExpectedSlashCount;

        public BakeProfile(
            string itemPath,
            string comboPath,
            string displayName,
            int expectedStepCount,
            int expectedSlashCount)
        {
            ItemPath = itemPath;
            ComboPath = comboPath;
            DisplayName = displayName;
            ExpectedStepCount = expectedStepCount;
            ExpectedSlashCount = expectedSlashCount;
        }
    }

    private readonly struct WeaponTipSample
    {
        public readonly float NormalizedTime;
        public readonly Vector3 LocalPoint;

        public WeaponTipSample(float normalizedTime, Vector3 localPoint)
        {
            NormalizedTime = normalizedTime;
            LocalPoint = localPoint;
        }
    }

    private static readonly BakeProfile[] Profiles =
    {
        new BakeProfile(OneHandSwordItemPath, OneHandSwordComboPath, "한손검", 3, 3),
        new BakeProfile(GreatswordItemPath, GreatswordComboPath, "대검", 4, 0)
    };

    public static void RunFromCommandLine()
    {
        List<string> reports = new List<string>();
        for (int i = 0; i < Profiles.Length; i++)
        {
            MeleeComboDefinition combo = AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(
                Profiles[i].ComboPath);
            reports.Add(BakeSelectedCombo(combo));
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        RunValidationFromCommandLine();
        Debug.Log("[MeleeAttackTrajectoryBake]\n" + string.Join("\n", reports));
    }

    public static void RunValidationFromCommandLine()
    {
        ValidateRuntimeEvaluationContract();

        List<string> report = new List<string>();
        for (int i = 0; i < Profiles.Length; i++)
        {
            MeleeComboDefinition combo = AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(
                Profiles[i].ComboPath);
            if (!ValidateCombo(combo, out string error))
                throw new InvalidOperationException(Profiles[i].DisplayName + ": " + error);

            report.Add(Profiles[i].DisplayName + " / 정상");
        }

        Debug.Log("[MeleeAttackTrajectoryValidation]\n" + string.Join("\n", report));
    }

    public static void RunCoreValidationFromCommandLine()
    {
        ValidateRuntimeEvaluationContract();
        Debug.Log("[MeleeAttackTrajectoryCoreValidation] OK");
    }

    public static void RunDryRunValidationFromCommandLine()
    {
        ValidateRuntimeEvaluationContract();
        List<string> reports = new List<string>();
        for (int i = 0; i < Profiles.Length; i++)
        {
            MeleeComboDefinition source = AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(
                Profiles[i].ComboPath);
            if (source == null)
                throw new MissingReferenceException(Profiles[i].ComboPath);

            MeleeComboDefinition workingCopy = ScriptableObject.CreateInstance<MeleeComboDefinition>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), workingCopy);
                string bakeReport = BuildBake(Profiles[i], workingCopy);
                if (!ValidateStructure(workingCopy, Profiles[i], out string error))
                    throw new InvalidOperationException(Profiles[i].DisplayName + ": " + error);
                reports.Add(Profiles[i].DisplayName + " / OK\n" + bakeReport);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(workingCopy);
            }
        }

        Debug.Log("[MeleeAttackTrajectoryDryRunValidation]\n" + string.Join("\n", reports));
    }

    public static bool CanBake(MeleeComboDefinition combo, out string profileName)
    {
        if (TryResolveProfile(combo, out BakeProfile profile))
        {
            profileName = profile.DisplayName;
            return true;
        }

        profileName = null;
        return false;
    }

    public static MeleeAttackTrajectoryBakeStatus GetBakeStatus(
        MeleeComboDefinition combo,
        out string message)
    {
        if (combo == null || !TryResolveProfile(combo, out BakeProfile profile))
        {
            message = "등록된 공격 궤적 베이크 프로필이 없습니다.";
            return MeleeAttackTrajectoryBakeStatus.Missing;
        }

        string failure = SessionState.GetString(GetFailureSessionKey(combo), string.Empty);
        if (!string.IsNullOrEmpty(failure))
        {
            message = failure;
            return MeleeAttackTrajectoryBakeStatus.Failed;
        }

        MeleeAttackTrajectoryBakeData data = combo.AttackTrajectoryBakeData;
        if (data == null || !data.IsStructurallyValid)
        {
            message = "공격 궤적 베이크 데이터가 없습니다.";
            return MeleeAttackTrajectoryBakeStatus.Missing;
        }

        if (!TryComputeFingerprints(profile, combo, out string source, out string derived, out message))
            return MeleeAttackTrajectoryBakeStatus.Stale;

        if (!string.Equals(data.sourceFingerprint, source, StringComparison.Ordinal)
            || !string.Equals(data.derivedFingerprint, derived, StringComparison.Ordinal))
        {
            message = "애니메이션이 변경되어 오래된 베이크 데이터를 사용 중입니다. 공격 궤적을 다시 베이크하세요.";
            return MeleeAttackTrajectoryBakeStatus.Stale;
        }

        if (!ValidateStructure(combo, profile, out message))
            return MeleeAttackTrajectoryBakeStatus.Stale;

        message = "공격 궤적 베이크가 최신 상태입니다.";
        return MeleeAttackTrajectoryBakeStatus.Current;
    }

    public static string BakeSelectedCombo(MeleeComboDefinition combo)
    {
        if (!TryResolveProfile(combo, out BakeProfile profile))
            throw new InvalidOperationException("등록된 공격 궤적 베이크 프로필이 없습니다.");

        MeleeComboDefinition workingCopy = ScriptableObject.CreateInstance<MeleeComboDefinition>();
        try
        {
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(combo), workingCopy);
            string report = BuildBake(profile, workingCopy);
            if (!ValidateStructure(workingCopy, profile, out string validationError))
                throw new InvalidOperationException(validationError);

            Undo.RecordObject(combo, "Bake Melee Attack Trajectory");
            combo.steps = workingCopy.steps;
            combo.EditorAssignAttackTrajectoryBakeData(workingCopy.AttackTrajectoryBakeData);
            EditorUtility.SetDirty(combo); // 모든 결과 검증 뒤 한 번만 커밋
            Undo.FlushUndoRecordObjects();
            SessionState.EraseString(GetFailureSessionKey(combo));
            AssetDatabase.SaveAssets();
            Debug.Log("[MeleeAttackTrajectoryBake]\n" + report, combo);
            return report;
        }
        catch (Exception exception)
        {
            SessionState.SetString(GetFailureSessionKey(combo), exception.Message);
            throw;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(workingCopy);
        }
    }

    public static bool ValidateCombo(MeleeComboDefinition combo, out string error)
    {
        MeleeAttackTrajectoryBakeStatus status = GetBakeStatus(combo, out string statusMessage);
        if (status != MeleeAttackTrajectoryBakeStatus.Current)
        {
            error = statusMessage;
            return false;
        }

        if (!TryResolveProfile(combo, out BakeProfile profile))
        {
            error = "등록된 베이크 프로필이 없습니다.";
            return false;
        }

        return ValidateStructure(combo, profile, out error);
    }

    public static bool TryGetSlashCueKey(AttackPhaseData phase, out string cueKey)
    {
        int cueIndex = FindSlashCueIndex(phase.vfxCues);
        cueKey = cueIndex >= 0 ? phase.vfxCues[cueIndex].elementOverrideKey : null;
        return cueIndex >= 0;
    }

    private static string BuildBake(BakeProfile profile, MeleeComboDefinition combo)
    {
        WeaponItemData item = AssetDatabase.LoadAssetAtPath<WeaponItemData>(profile.ItemPath);
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (item == null || item.weaponRootPrefab == null || playerPrefab == null || combo == null)
            throw new MissingReferenceException("공격 궤적 베이크 의존성이 누락되었습니다: " + profile.ComboPath);
        if (combo.steps == null || combo.steps.Length != profile.ExpectedStepCount)
            throw new InvalidOperationException("예상 콤보 타수와 다릅니다: " + profile.ComboPath);
        if (!TryComputeFingerprints(
                profile,
                combo,
                out string sourceFingerprint,
                out string derivedFingerprint,
                out string fingerprintError))
        {
            throw new InvalidOperationException(fingerprintError);
        }

        WeaponFinalStats stats = WeaponStatCalculator.Calculate(item);
        MeleeWeaponDefinition meleeDefinition = item.GetMeleeDefinition();
        if (meleeDefinition == null)
            throw new MissingReferenceException("근접 무기 정의가 없습니다: " + profile.ItemPath);

        GameObject player = UnityEngine.Object.Instantiate(playerPrefab);
        GameObject weapon = null;
        bool startedAnimationMode = false;
        List<string> report = new List<string>();
        try
        {
            P09CharacterVisualAdapter adapter = player.GetComponentInChildren<P09CharacterVisualAdapter>(true);
            if (adapter == null)
                throw new MissingComponentException("플레이어 리그 어댑터가 없습니다.");
            adapter.ResolveReferences();
            GameObject animationRoot = adapter.ModelRoot != null ? adapter.ModelRoot.gameObject : null;
            Transform socket = adapter.RightHandWeaponSocket;
            if (animationRoot == null || socket == null)
                throw new MissingReferenceException("P09 리그 또는 Weapon_Target_Hand_R이 없습니다.");

            weapon = UnityEngine.Object.Instantiate(item.weaponRootPrefab, socket, false);
            WeaponPose pose = weapon.GetComponentInChildren<WeaponPose>(true);
            WeaponTraceBinding trace = weapon.GetComponentInChildren<WeaponTraceBinding>(true);
            if (trace == null || trace.WeaponTip == null)
                throw new MissingComponentException("Equipped 프리팹의 WeaponTip이 없습니다.");

            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
                startedAnimationMode = true;
            }

            MeleeAttackStepTrajectoryBakeData[] bakedSteps =
                new MeleeAttackStepTrajectoryBakeData[combo.steps.Length];
            int slashCount = 0;
            for (int stepIndex = 0; stepIndex < combo.steps.Length; stepIndex++)
            {
                MeleeComboStepData step = combo.steps[stepIndex];
                if (step.animationClip == null || step.attackPhases == null || step.attackPhases.Length == 0)
                    throw new MissingReferenceException("공격 클립 또는 Phase가 없습니다: " + step.attackId);

                List<WeaponTipSample> samples = SampleWeaponTip(
                    animationRoot,
                    player.transform,
                    pose,
                    trace,
                    step.animationClip);
                MeleeAttackTrajectoryRawSample[] rawSamples = BuildRawSamples(samples);
                MeleeAttackPhaseTrajectoryBakeData[] bakedPhases =
                    new MeleeAttackPhaseTrajectoryBakeData[step.attackPhases.Length];

                for (int phaseIndex = 0; phaseIndex < step.attackPhases.Length; phaseIndex++)
                {
                    AttackPhaseData phase = step.attackPhases[phaseIndex];
                    AttackPatternRuntimeData pattern = phase.ResolvePattern(
                        stats.range,
                        stats.meleeSlashAngle,
                        meleeDefinition.baseSettings.hitWidth);
                    MeleeAttackTrajectoryRawSample[] phaseTrajectorySamples = ResamplePhaseTrajectory(
                        rawSamples,
                        phase.SafeStart,
                        phase.SafeEnd);
                    MeleeAttackPhaseTrajectoryBakeData bakedPhase = BuildPhaseTrajectory(
                        phaseIndex,
                        phase,
                        pattern,
                        phaseTrajectorySamples);
                    bakedPhases[phaseIndex] = bakedPhase;

                    int slashCueIndex = FindSlashCueIndex(phase.vfxCues);
                    if (slashCueIndex < 0)
                        continue;

                    List<WeaponTipSample> phaseSamples = ToWeaponTipSamples(phaseTrajectorySamples);
                    List<WeaponTipSample> activeSamples = SelectActiveSwingSegment(phaseSamples, phase);
                    if (activeSamples.Count < MinimumSlopeSampleCount)
                    {
                        throw new InvalidOperationException(
                            "VFX 경사 마스크가 유효한 궤적을 찾지 못했습니다: "
                            + step.attackId + " / Phase " + phaseIndex);
                    }

                    float slope = ResolveDominantSlope(activeSamples);
                    AttackVfxCueData cue = phase.vfxCues[slashCueIndex];
                    if (cue.motionRole == AttackVfxMotionRole.VerticalFalling
                        && bakedPhase.TryEvaluate(
                            activeSamples[0].NormalizedTime,
                            out _,
                            out float triggerProgress))
                    {
                        cue.triggerProgress = Mathf.Clamp01(triggerProgress);
                        phase.vfxCues[slashCueIndex] = cue; // Slash만 공용 궤적 발동점 사용
                    }

                    phase.useBakedVfxSwingSlope = true;
                    phase.bakedVfxSwingSlopeDegrees = slope;
                    step.attackPhases[phaseIndex] = phase;
                    slashCount++;
                    report.Add(
                        step.attackId + " / Phase " + phaseIndex
                        + " / " + cue.elementOverrideKey
                        + " / Samples=" + phaseSamples.Count
                        + " / ActiveSamples=" + activeSamples.Count
                        + " / Slope=" + slope.ToString("0.###"));
                }

                combo.steps[stepIndex] = step;
                bakedSteps[stepIndex] = new MeleeAttackStepTrajectoryBakeData
                {
                    attackId = step.attackId,
                    rawSamples = rawSamples,
                    phases = bakedPhases
                };
                report.Add(step.attackId + " / TrajectorySamples=" + rawSamples.Length
                    + " / HitPhases=" + bakedPhases.Length);
            }

            if (slashCount != profile.ExpectedSlashCount)
                throw new InvalidOperationException("예상 Slash Cue 수와 다릅니다: " + slashCount);

            if (!TryComputeFingerprints(
                    profile,
                    combo,
                    out sourceFingerprint,
                    out derivedFingerprint,
                    out fingerprintError))
            {
                throw new InvalidOperationException(fingerprintError);
            }

            combo.EditorAssignAttackTrajectoryBakeData(new MeleeAttackTrajectoryBakeData
            {
                schemaVersion = MeleeAttackTrajectoryBakeData.CurrentSchemaVersion,
                sourceFingerprint = sourceFingerprint,
                derivedFingerprint = derivedFingerprint,
                bakedUtc = DateTime.UtcNow.ToString("O"),
                steps = bakedSteps
            });
            return profile.DisplayName + " / " + string.Join("\n", report);
        }
        finally
        {
            if (startedAnimationMode && AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();
            if (weapon != null)
                UnityEngine.Object.DestroyImmediate(weapon);
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    private static List<WeaponTipSample> SampleWeaponTip(
        GameObject animationRoot,
        Transform referenceRoot,
        WeaponPose pose,
        WeaponTraceBinding trace,
        AnimationClip clip)
    {
        List<WeaponTipSample> samples = new List<WeaponTipSample>(SampleCount + 1);
        for (int i = 0; i <= SampleCount; i++)
        {
            float normalizedTime = i / (float)SampleCount;
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animationRoot, clip, normalizedTime * clip.length);
            AnimationMode.EndSampling();
            pose?.PreviewPoseInstant(WeaponPoseSlot.Aim);
            Vector3 localPoint = referenceRoot.InverseTransformPoint(trace.WeaponTip.position);
            samples.Add(new WeaponTipSample(normalizedTime, localPoint));
        }

        return samples;
    }

    private static MeleeAttackTrajectoryRawSample[] BuildRawSamples(List<WeaponTipSample> samples)
    {
        MeleeAttackTrajectoryRawSample[] result = new MeleeAttackTrajectoryRawSample[samples.Count];
        float previousWrapped = Mathf.Atan2(samples[0].LocalPoint.x, samples[0].LocalPoint.z) * Mathf.Rad2Deg;
        float unwrapped = previousWrapped;
        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 point = samples[i].LocalPoint;
            float wrapped = Mathf.Atan2(point.x, point.z) * Mathf.Rad2Deg;
            if (i > 0)
                unwrapped += Mathf.DeltaAngle(previousWrapped, wrapped);
            previousWrapped = wrapped;
            result[i] = new MeleeAttackTrajectoryRawSample
            {
                normalizedTime = samples[i].NormalizedTime,
                localPosition = point,
                unwrappedAngle = unwrapped,
                forwardDistance = point.z
            };
        }

        return result;
    }

    private static MeleeAttackPhaseTrajectoryBakeData BuildPhaseTrajectory(
        int phaseIndex,
        AttackPhaseData phase,
        AttackPatternRuntimeData pattern,
        MeleeAttackTrajectoryRawSample[] samples)
    {
        List<MeleeAttackTrajectoryProgressSample> progress =
            new List<MeleeAttackTrajectoryProgressSample>(samples.Length + 2);
        bool canApply = phase.progressSource != AttackProgressSource.WeaponTipSectorAngle;
        float entryTime = phase.SafeStart;
        float resolved = 0f;

        if (phase.progressSource == AttackProgressSource.NormalizedTime)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                float linear = Mathf.InverseLerp(phase.SafeStart, phase.SafeEnd, samples[i].normalizedTime);
                float value = pattern.EvaluateProgress(linear);
                progress.Add(CreateProgressSample(samples[i].normalizedTime, value, value));
            }
        }
        else if (phase.progressSource == AttackProgressSource.WeaponTipSectorAngle)
        {
            BuildSectorProgress(pattern, samples, progress, ref canApply, ref entryTime);
        }
        else
        {
            float previousWrapped = ResolvePatternAngle(samples[0].localPosition, pattern.ForwardOffset);
            float directionalTravel = 0f;
            float baselineForward = samples[0].forwardDistance - pattern.ForwardOffset;
            float directionSign = pattern.Direction == AttackFillDirection.RightToLeft ? -1f : 1f;
            for (int i = 0; i < samples.Length; i++)
            {
                float raw;
                if (phase.progressSource == AttackProgressSource.WeaponTipAngularTravel)
                {
                    float wrapped = ResolvePatternAngle(samples[i].localPosition, pattern.ForwardOffset);
                    if (i > 0)
                        directionalTravel += Mathf.DeltaAngle(previousWrapped, wrapped) * directionSign;
                    previousWrapped = wrapped;
                    raw = directionalTravel / 360f;
                }
                else
                {
                    float forward = samples[i].forwardDistance - pattern.ForwardOffset;
                    raw = (forward - baselineForward) / Mathf.Max(0.0001f, pattern.Range);
                }

                raw = Mathf.Clamp01(raw);
                resolved = Mathf.Max(resolved, raw);
                progress.Add(CreateProgressSample(samples[i].normalizedTime, raw, resolved));
            }

            if (phase.progressSource == AttackProgressSource.WeaponTipAngularTravel
                && directionalTravel < 359f)
            {
                throw new InvalidOperationException(
                    "원형 WeaponTip 궤적이 360도에 도달하지 못했습니다: "
                    + directionalTravel.ToString("0.###"));
            }
        }

        return new MeleeAttackPhaseTrajectoryBakeData
        {
            phaseIndex = phaseIndex,
            progressSource = phase.progressSource,
            canApplyHits = canApply,
            hitEntryNormalizedTime = entryTime,
            progressSamples = progress.ToArray()
        };
    }

    private static void BuildSectorProgress(
        AttackPatternRuntimeData pattern,
        MeleeAttackTrajectoryRawSample[] samples,
        List<MeleeAttackTrajectoryProgressSample> progress,
        ref bool canApply,
        ref float entryTime)
    {
        float directionSign = pattern.Direction == AttackFillDirection.RightToLeft ? -1f : 1f;
        float previousWrapped = ResolvePatternAngle(samples[0].localPosition, pattern.ForwardOffset);
        float previousDirectional = ResolveDirectionalSectorAngle(pattern, previousWrapped);
        float resolved = 0f;
        canApply = IsInsideSector(pattern, previousDirectional);
        entryTime = canApply ? samples[0].normalizedTime : PhaseSafeFallback(samples);
        progress.Add(CreateProgressSample(samples[0].normalizedTime, 0f, 0f));

        for (int i = 1; i < samples.Length; i++)
        {
            float wrapped = ResolvePatternAngle(samples[i].localPosition, pattern.ForwardOffset);
            float currentDirectional = previousDirectional
                + Mathf.DeltaAngle(previousWrapped, wrapped) * directionSign;
            previousWrapped = wrapped;

            if (!canApply
                && currentDirectional + 0.0001f >= previousDirectional
                && previousDirectional <= 0f
                && currentDirectional >= 0f)
            {
                float crossing = ResolveCrossingTime(
                    samples[i - 1].normalizedTime,
                    samples[i].normalizedTime,
                    previousDirectional,
                    currentDirectional,
                    0f);
                entryTime = crossing;
                canApply = true;
                progress.Add(CreateProgressSample(crossing, 0f, 0f));
            }

            if (canApply)
            {
                float previousRaw = Mathf.Clamp01(previousDirectional / Mathf.Max(0.0001f, pattern.Angle));
                float raw = Mathf.Clamp01(currentDirectional / Mathf.Max(0.0001f, pattern.Angle));
                if (previousRaw < 1f && raw >= 1f)
                {
                    float crossing = ResolveCrossingTime(
                        samples[i - 1].normalizedTime,
                        samples[i].normalizedTime,
                        previousDirectional,
                        currentDirectional,
                        pattern.Angle);
                    progress.Add(CreateProgressSample(crossing, 1f, 1f));
                }

                resolved = Mathf.Max(resolved, raw);
                progress.Add(CreateProgressSample(samples[i].normalizedTime, raw, resolved));
            }
            else
            {
                progress.Add(CreateProgressSample(samples[i].normalizedTime, 0f, 0f));
            }

            previousDirectional = currentDirectional;
        }
    }

    private static float PhaseSafeFallback(MeleeAttackTrajectoryRawSample[] samples)
    {
        return samples.Length > 0 ? samples[0].normalizedTime : 0f;
    }

    private static MeleeAttackTrajectoryProgressSample CreateProgressSample(
        float time,
        float raw,
        float resolved)
    {
        return new MeleeAttackTrajectoryProgressSample
        {
            normalizedTime = Mathf.Clamp01(time),
            rawProgress = Mathf.Clamp01(raw),
            resolvedProgress = Mathf.Clamp01(resolved)
        };
    }

    private static float ResolveCrossingTime(
        float firstTime,
        float secondTime,
        float firstValue,
        float secondValue,
        float boundary)
    {
        float denominator = secondValue - firstValue;
        float t = Mathf.Abs(denominator) > 0.000001f
            ? Mathf.Clamp01((boundary - firstValue) / denominator)
            : 0f;
        return Mathf.Lerp(firstTime, secondTime, t);
    }

    private static MeleeAttackTrajectoryRawSample[] ResamplePhaseTrajectory(
        MeleeAttackTrajectoryRawSample[] source,
        float start,
        float end)
    {
        MeleeAttackTrajectoryRawSample[] result =
            new MeleeAttackTrajectoryRawSample[PhaseDerivedSampleCount + 1];
        for (int i = 0; i <= PhaseDerivedSampleCount; i++)
        {
            float normalizedTime = Mathf.Lerp(start, end, i / (float)PhaseDerivedSampleCount);
            result[i] = EvaluateRawSample(source, normalizedTime);
        }
        return result;
    }

    private static MeleeAttackTrajectoryRawSample EvaluateRawSample(
        MeleeAttackTrajectoryRawSample[] samples,
        float time)
    {
        float clamped = Mathf.Clamp01(time);
        for (int i = 1; i < samples.Length; i++)
        {
            if (clamped > samples[i].normalizedTime)
                continue;

            MeleeAttackTrajectoryRawSample left = samples[i - 1];
            MeleeAttackTrajectoryRawSample right = samples[i];
            float t = Mathf.InverseLerp(left.normalizedTime, right.normalizedTime, clamped);
            return new MeleeAttackTrajectoryRawSample
            {
                normalizedTime = clamped,
                localPosition = Vector3.Lerp(left.localPosition, right.localPosition, t),
                unwrappedAngle = Mathf.Lerp(left.unwrappedAngle, right.unwrappedAngle, t),
                forwardDistance = Mathf.Lerp(left.forwardDistance, right.forwardDistance, t)
            };
        }

        return samples[samples.Length - 1];
    }

    private static List<WeaponTipSample> ToWeaponTipSamples(
        MeleeAttackTrajectoryRawSample[] samples)
    {
        List<WeaponTipSample> result = new List<WeaponTipSample>(samples.Length);
        for (int i = 0; i < samples.Length; i++)
            result.Add(new WeaponTipSample(samples[i].normalizedTime, samples[i].localPosition));
        return result;
    }

    private static float ResolvePatternAngle(Vector3 localPoint, float forwardOffset)
    {
        return Mathf.Atan2(localPoint.x, localPoint.z - forwardOffset) * Mathf.Rad2Deg;
    }

    private static float ResolveDirectionalSectorAngle(
        AttackPatternRuntimeData pattern,
        float wrappedAngle)
    {
        float halfAngle = pattern.Angle * 0.5f;
        bool rightToLeft = pattern.Direction == AttackFillDirection.RightToLeft;
        float startAngle = pattern.AngleOffset + (rightToLeft ? halfAngle : -halfAngle);
        float delta = Mathf.DeltaAngle(startAngle, wrappedAngle);
        return rightToLeft ? -delta : delta;
    }

    private static bool IsInsideSector(AttackPatternRuntimeData pattern, float directionalAngle)
    {
        return directionalAngle >= -0.0001f && directionalAngle <= pattern.Angle + 0.0001f;
    }

    private static List<WeaponTipSample> SelectActiveSwingSegment(
        List<WeaponTipSample> samples,
        AttackPhaseData phase)
    {
        List<WeaponTipSample> best = new List<WeaponTipSample>();
        List<WeaponTipSample> current = new List<WeaponTipSample>();
        float bestTravel = -1f;
        for (int i = 0; i <= samples.Count; i++)
        {
            bool inside = i < samples.Count && IsInsideBakeMask(samples[i].LocalPoint, phase);
            if (inside)
            {
                current.Add(samples[i]);
                continue;
            }

            float travel = CalculateProjectedTravel(current);
            if (current.Count >= MinimumSlopeSampleCount && travel > bestTravel)
            {
                best = new List<WeaponTipSample>(current);
                bestTravel = travel;
            }
            current.Clear();
        }

        return best;
    }

    private static bool IsInsideBakeMask(Vector3 point, AttackPhaseData phase)
    {
        AttackVfxSwingSettings settings = phase.vfxSwingSettings;
        if (settings.bakeMask == AttackVfxBakeMask.VerticalFrontSector)
        {
            if (Mathf.Abs(point.x) > settings.SafeVerticalHalfWidth || point.z <= 0f)
                return false;
            float angle = Mathf.Atan2(
                point.y - settings.SafeVerticalPivotHeight,
                point.z) * Mathf.Rad2Deg;
            return Mathf.Abs(angle) <= settings.SafeMaskAngleDegrees * 0.5f;
        }

        float originZ = phase.geometry.overrideForwardOffset ? phase.geometry.forwardOffset : 0f;
        float horizontalAngle = Mathf.Atan2(point.x, point.z - originZ) * Mathf.Rad2Deg;
        return Mathf.Abs(horizontalAngle) <= settings.SafeMaskAngleDegrees * 0.5f;
    }

    private static float CalculateProjectedTravel(List<WeaponTipSample> samples)
    {
        float travel = 0f;
        for (int i = 1; i < samples.Count; i++)
        {
            Vector3 first = samples[i - 1].LocalPoint;
            Vector3 second = samples[i].LocalPoint;
            travel += Vector2.Distance(new Vector2(first.x, first.y), new Vector2(second.x, second.y));
        }
        return travel;
    }

    private static float ResolveDominantSlope(List<WeaponTipSample> samples)
    {
        float meanX = 0f;
        float meanY = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            meanX += samples[i].LocalPoint.x;
            meanY += samples[i].LocalPoint.y;
        }
        meanX /= samples.Count;
        meanY /= samples.Count;

        float xx = 0f;
        float xy = 0f;
        float yy = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            float x = samples[i].LocalPoint.x - meanX;
            float y = samples[i].LocalPoint.y - meanY;
            xx += x * x;
            xy += x * y;
            yy += y * y;
        }
        return 0.5f * Mathf.Atan2(2f * xy, xx - yy) * Mathf.Rad2Deg;
    }

    private static bool TryComputeFingerprints(
        BakeProfile profile,
        MeleeComboDefinition combo,
        out string sourceFingerprint,
        out string derivedFingerprint,
        out string error)
    {
        sourceFingerprint = null;
        derivedFingerprint = null;
        WeaponItemData item = AssetDatabase.LoadAssetAtPath<WeaponItemData>(profile.ItemPath);
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (item == null || item.weaponRootPrefab == null || playerPrefab == null || combo == null)
        {
            error = "공격 궤적 fingerprint 의존성이 누락되었습니다.";
            return false;
        }

        WeaponTraceBinding trace = item.weaponRootPrefab.GetComponentInChildren<WeaponTraceBinding>(true);
        if (trace == null || trace.WeaponTip == null)
        {
            error = "Equipped 프리팹의 WeaponTip fingerprint를 만들 수 없습니다.";
            return false;
        }

        System.Text.StringBuilder source = new System.Text.StringBuilder(2048);
        source.Append("Schema=").Append(MeleeAttackTrajectoryBakeData.CurrentSchemaVersion);
        AppendAssetFingerprint(source, PlayerPrefabPath, "PlayerRig");
        AppendAssetFingerprint(source, AssetDatabase.GetAssetPath(item.weaponRootPrefab), "EquippedWeapon");
        Transform rightHandSocket = FindNamedTransform(
            playerPrefab.transform,
            P09CharacterVisualAdapter.RightHandWeaponSocketName);
        if (rightHandSocket != null)
        {
            source.Append("|WeaponSocket=").Append(GetHierarchyPath(rightHandSocket, playerPrefab.transform));
            AppendTransformFingerprint(source, rightHandSocket);
        }
        source.Append("|WeaponTip=").Append(GetHierarchyPath(trace.WeaponTip, item.weaponRootPrefab.transform));
        AppendTransformFingerprint(source, trace.WeaponTip);
        WeaponPose pose = item.weaponRootPrefab.GetComponentInChildren<WeaponPose>(true);
        if (pose != null)
        {
            source.Append("|WeaponPose=").Append(GetHierarchyPath(pose.transform, item.weaponRootPrefab.transform));
            AppendTransformFingerprint(source, pose.transform);
        }
        WeaponGripMount grip = item.weaponRootPrefab.GetComponentInChildren<WeaponGripMount>(true);
        if (grip != null)
        {
            source.Append("|Grip=").Append(GetHierarchyPath(grip.transform, item.weaponRootPrefab.transform));
            AppendTransformFingerprint(source, grip.transform);
            if (grip.RightHandGripPoint != null)
            {
                source.Append("|RightHandGrip=")
                    .Append(GetHierarchyPath(grip.RightHandGripPoint, item.weaponRootPrefab.transform));
                AppendTransformFingerprint(source, grip.RightHandGripPoint);
            }
        }

        for (int i = 0; i < combo.steps.Length; i++)
            AppendClipFingerprint(source, combo.steps[i].animationClip, i);
        sourceFingerprint = Hash128.Compute(source.ToString()).ToString();

        System.Text.StringBuilder derived = new System.Text.StringBuilder(source.ToString());
        for (int stepIndex = 0; stepIndex < combo.steps.Length; stepIndex++)
        {
            MeleeComboStepData step = combo.steps[stepIndex];
            derived.Append("|AttackId=").Append(step.attackId);
            if (step.attackPhases == null)
                continue;
            for (int phaseIndex = 0; phaseIndex < step.attackPhases.Length; phaseIndex++)
            {
                AttackPhaseData phase = step.attackPhases[phaseIndex];
                derived.Append("|Phase=").Append(stepIndex).Append(':').Append(phaseIndex)
                    .Append(':').Append(phase.startNormalizedTime)
                    .Append(':').Append(phase.endNormalizedTime)
                    .Append(':').Append((int)phase.progressSource)
                    .Append(':').Append(JsonUtility.ToJson(phase.geometry))
                    .Append(':').Append(JsonUtility.ToJson(phase.vfxSwingSettings));
                if (phase.attackPattern != null)
                    AppendAssetFingerprint(derived, AssetDatabase.GetAssetPath(phase.attackPattern), "Pattern");

                if (phase.vfxCues == null)
                    continue;

                for (int cueIndex = 0; cueIndex < phase.vfxCues.Length; cueIndex++)
                {
                    AttackVfxCueData cue = phase.vfxCues[cueIndex];
                    derived.Append("|Cue=").Append(stepIndex).Append(':').Append(phaseIndex).Append(':').Append(cueIndex)
                        .Append(':').Append((int)cue.motionRole)
                        .Append(':').Append((int)cue.mirrorAxis)
                        .Append(':').Append(cue.triggerProgress.ToString("R"))
                        .Append(':').Append((int)cue.placementMode)
                        .Append(':').Append(cue.scaleMultiplier.ToString("R"))
                        .Append(':').Append(cue.autoSwingSlope)
                        .Append(':').Append(cue.swingSlopeOffsetDegrees.ToString("R"))
                        .Append(':').Append(cue.localEulerOffset.ToString("R"))
                        .Append(':').Append(cue.elementOverrideKey ?? string.Empty);
                    if (cue.definition != null)
                    {
                        AppendAssetFingerprint(
                            derived,
                            AssetDatabase.GetAssetPath(cue.definition),
                            "CueDefinition");
                    }
                }
            }
        }
        derivedFingerprint = Hash128.Compute(derived.ToString()).ToString();
        error = null;
        return true;
    }

    private static void ValidateRuntimeEvaluationContract()
    {
        MeleeAttackTrajectoryRawSample[] raw =
        {
            new MeleeAttackTrajectoryRawSample
            {
                normalizedTime = 0f,
                localPosition = new Vector3(0f, 0f, 0f),
                unwrappedAngle = 170f,
                forwardDistance = 0f
            },
            new MeleeAttackTrajectoryRawSample
            {
                normalizedTime = 0.5f,
                localPosition = new Vector3(1f, 0f, 2f),
                unwrappedAngle = 190f,
                forwardDistance = 2f
            },
            new MeleeAttackTrajectoryRawSample
            {
                normalizedTime = 1f,
                localPosition = new Vector3(2f, 0f, 4f),
                unwrappedAngle = 210f,
                forwardDistance = 4f
            }
        };
        MeleeAttackTrajectoryRawSample[] phaseSamples =
            ResamplePhaseTrajectory(raw, 0.25f, 0.75f);
        if (phaseSamples.Length != PhaseDerivedSampleCount + 1
            || Mathf.Abs(phaseSamples[0].normalizedTime - 0.25f) > 0.0001f
            || Mathf.Abs(phaseSamples[phaseSamples.Length - 1].normalizedTime - 0.75f) > 0.0001f
            || Mathf.Abs(phaseSamples[PhaseDerivedSampleCount / 2].unwrappedAngle - 190f) > 0.0001f
            || Mathf.Abs(phaseSamples[PhaseDerivedSampleCount / 2].forwardDistance - 2f) > 0.0001f)
        {
            throw new InvalidOperationException("Phase trajectory resampling lost raw trajectory continuity.");
        }

        MeleeAttackPhaseTrajectoryBakeData phase = new MeleeAttackPhaseTrajectoryBakeData
        {
            phaseIndex = 0,
            progressSource = AttackProgressSource.WeaponTipSectorAngle,
            canApplyHits = true,
            hitEntryNormalizedTime = 0.25f,
            progressSamples = new[]
            {
                new MeleeAttackTrajectoryProgressSample
                {
                    normalizedTime = 0.1f,
                    rawProgress = 0f,
                    resolvedProgress = 0f
                },
                new MeleeAttackTrajectoryProgressSample
                {
                    normalizedTime = 0.25f,
                    rawProgress = 0f,
                    resolvedProgress = 0f
                },
                new MeleeAttackTrajectoryProgressSample
                {
                    normalizedTime = 0.5f,
                    rawProgress = 1f,
                    resolvedProgress = 1f
                }
            }
        };

        if (phase.TryEvaluate(0.24f, out _, out _))
            throw new InvalidOperationException("Sector entry gate accepted a pre-entry sample.");
        if (!phase.TryEvaluate(0.375f, out float middleRaw, out float middleResolved)
            || Mathf.Abs(middleRaw - 0.5f) > 0.0001f
            || Mathf.Abs(middleResolved - 0.5f) > 0.0001f)
        {
            throw new InvalidOperationException("Baked trajectory interpolation is invalid.");
        }
        if (!phase.TryEvaluate(1f, out _, out float skippedResolved)
            || skippedResolved < 0.9999f)
        {
            throw new InvalidOperationException("A low-frame time jump lost final attack progress.");
        }
    }

    private static void AppendAssetFingerprint(
        System.Text.StringBuilder builder,
        string path,
        string label)
    {
        builder.Append('|').Append(label).Append('=')
            .Append(AssetDatabase.AssetPathToGUID(path)).Append(':')
            .Append(AssetDatabase.GetAssetDependencyHash(path));
    }

    private static void AppendClipFingerprint(
        System.Text.StringBuilder builder,
        AnimationClip clip,
        int index)
    {
        if (clip == null)
        {
            builder.Append("|Clip").Append(index).Append("=Missing");
            return;
        }

        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long localId);
        string path = AssetDatabase.GetAssetPath(clip);
        builder.Append("|Clip").Append(index).Append('=')
            .Append(guid).Append(':').Append(localId).Append(':')
            .Append(AssetDatabase.GetAssetDependencyHash(path)).Append(':')
            .Append(clip.length.ToString("R")); // 정규화 속도 배율은 제외
    }

    private static void AppendTransformFingerprint(
        System.Text.StringBuilder builder,
        Transform value)
    {
        builder.Append(':').Append(value.localPosition.ToString("R"))
            .Append(':').Append(value.localRotation.ToString("R"))
            .Append(':').Append(value.localScale.ToString("R"));
    }

    private static string GetHierarchyPath(Transform value, Transform root)
    {
        List<string> names = new List<string>();
        Transform current = value;
        while (current != null)
        {
            names.Add(current.name);
            if (current == root)
                break;
            current = current.parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }

    private static Transform FindNamedTransform(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;
        if (string.Equals(root.name, name, StringComparison.Ordinal))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindNamedTransform(root.GetChild(i), name);
            if (result != null)
                return result;
        }

        return null;
    }

    private static bool ValidateStructure(
        MeleeComboDefinition combo,
        BakeProfile profile,
        out string error)
    {
        MeleeAttackTrajectoryBakeData data = combo != null ? combo.AttackTrajectoryBakeData : null;
        if (data == null || !data.IsStructurallyValid)
        {
            error = "공격 궤적 데이터가 없거나 구버전입니다.";
            return false;
        }
        if (combo.steps == null
            || combo.steps.Length != profile.ExpectedStepCount
            || data.steps == null
            || data.steps.Length != combo.steps.Length)
        {
            error = "콤보 타수와 베이크 타수 구성이 다릅니다.";
            return false;
        }

        int slashCount = 0;
        for (int stepIndex = 0; stepIndex < combo.steps.Length; stepIndex++)
        {
            MeleeComboStepData step = combo.steps[stepIndex];
            MeleeAttackStepTrajectoryBakeData bakedStep = data.steps[stepIndex];
            if (bakedStep == null
                || !string.Equals(step.attackId, bakedStep.attackId, StringComparison.Ordinal)
                || bakedStep.rawSamples == null
                || bakedStep.rawSamples.Length != SampleCount + 1
                || bakedStep.phases == null
                || step.attackPhases == null
                || bakedStep.phases.Length != step.attackPhases.Length)
            {
                error = "타수별 원시 궤적 구성이 다릅니다: " + step.attackId;
                return false;
            }

            for (int phaseIndex = 0; phaseIndex < step.attackPhases.Length; phaseIndex++)
            {
                AttackPhaseData phase = step.attackPhases[phaseIndex];
                MeleeAttackPhaseTrajectoryBakeData bakedPhase = bakedStep.phases[phaseIndex];
                if (bakedPhase == null || !bakedPhase.IsUsableFor(phase.progressSource))
                {
                    error = "Phase 파생 궤적이 없습니다: " + step.attackId + " / " + phaseIndex;
                    return false;
                }

                if (phase.progressSource == AttackProgressSource.WeaponTipSectorAngle
                    && !bakedPhase.canApplyHits)
                {
                    error = "Sector trajectory never crossed its attack entry boundary: "
                        + step.attackId + " / " + phaseIndex;
                    return false;
                }

                int lastProgressIndex = bakedPhase.progressSamples.Length - 1;
                if (phase.progressSource == AttackProgressSource.WeaponTipSectorAngle
                    && bakedPhase.progressSamples[lastProgressIndex].resolvedProgress < 0.999f)
                {
                    error = "WeaponTip trajectory did not reach full attack progress: "
                        + step.attackId + " / " + phaseIndex;
                    return false;
                }

                if (FindSlashCueIndex(phase.vfxCues) >= 0)
                {
                    slashCount++;
                    if (!phase.useBakedVfxSwingSlope)
                    {
                        error = "Slash VFX 경사가 공용 궤적에서 생성되지 않았습니다: " + step.attackId;
                        return false;
                    }
                }
            }
        }

        if (slashCount != profile.ExpectedSlashCount)
        {
            error = "Slash Cue 수가 예상과 다릅니다: " + slashCount;
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryResolveProfile(
        MeleeComboDefinition combo,
        out BakeProfile profile)
    {
        string comboPath = combo != null ? AssetDatabase.GetAssetPath(combo) : null;
        for (int i = 0; i < Profiles.Length; i++)
        {
            if (string.Equals(Profiles[i].ComboPath, comboPath, StringComparison.Ordinal))
            {
                profile = Profiles[i];
                return true;
            }
        }

        profile = default;
        return false;
    }

    private static int FindSlashCueIndex(AttackVfxCueData[] cues)
    {
        if (cues == null)
            return -1;
        for (int i = 0; i < cues.Length; i++)
        {
            string key = cues[i].elementOverrideKey;
            if (key == "BasicSlash" || key == "CircularSlash" || key == "GroundSlamSlash")
                return i;
        }
        return -1;
    }

    private static string GetFailureSessionKey(MeleeComboDefinition combo)
    {
        return FailureSessionPrefix + AssetDatabase.GetAssetPath(combo);
    }

    [InitializeOnLoadMethod]
    private static void RegisterPlayModeStaleWarning()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.ExitingEditMode)
            return;

        for (int i = 0; i < Profiles.Length; i++)
        {
            MeleeComboDefinition combo = AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(
                Profiles[i].ComboPath);
            MeleeAttackTrajectoryBakeStatus status = GetBakeStatus(combo, out string message);
            if (status != MeleeAttackTrajectoryBakeStatus.Current)
                Debug.LogWarning("[MeleeAttackTrajectory] " + Profiles[i].DisplayName + " / " + message, combo);
        }
    }
}
