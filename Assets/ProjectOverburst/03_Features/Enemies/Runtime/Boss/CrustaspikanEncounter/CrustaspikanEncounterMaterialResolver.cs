using System.Collections.Generic;

public static class CrustaspikanEncounterMaterialResolver
{
    public static bool TryResolveTuning(CrustaspikanEncounterSettings settings, EnemyBossAttackMaterial material,
        CrustaspikanParryRecoilProfile recoil, out EnemyBossAttackTuning tuning, out string reason)
    {
        tuning = null;
        if (settings == null || material == null || !material.IsValid)
        { reason = "전투에 사용할 공격 재료가 유효하지 않습니다: " + (material != null ? material.name : "없음"); return false; }
        var rule = settings.Rule(material.runtimeClip.name);
        tuning = new EnemyBossAttackTuning {
            animationSpeedMultiplier = rule?.speed ?? 1f, damageMultiplier = rule?.damage ?? 1f,
            parries = new EnemyBossStrikeParryTuning[material.strikes.Length]
        };
        for (int i = 0; i < material.strikes.Length; i++)
            tuning.parries[i] = new EnemyBossStrikeParryTuning {
                canParry = material.delivery == EnemyBossMaterialDelivery.Melee
                    && ((rule?.finalHitParry ?? true) && i == material.strikes.Length - 1
                        || (rule?.firstHitParry ?? false) && i == 0)
            };
        recoil?.ApplyWindows(material, tuning);
        if (tuning.Validate(material.strikes)) { reason = ""; return true; }
        for (int i = 0; i < tuning.parries.Length; i++)
        {
            var p = tuning.parries[i];
            if (p.overrideWindow && (p.startNormalized < 0f || p.startNormalized > p.endNormalized
                || p.endNormalized > material.strikes[i].impact))
            {
                reason = $"{material.runtimeClip.name} {i + 1}타: 전투 설정과 반동 프로필을 적용한 패링 구간 "
                    + $"{p.startNormalized:0.######}~{p.endNormalized:0.######}이 타격 {material.strikes[i].impact:0.######}과 맞지 않습니다.";
                return false;
            }
        }
        reason = material.runtimeClip.name + ": 전투 설정과 반동 프로필을 적용한 피해·배속·패링 값이 유효하지 않습니다.";
        return false;
    }

    public static bool ValidateCollection(CrustaspikanEncounterSettings settings, EnemyBossMaterialCollection collection,
        CrustaspikanParryRecoilProfile recoil, out string reason,
        IReadOnlyDictionary<EnemyBossAttackMaterial, EnemyBossAttackMaterial> edits = null)
    {
        if (settings == null || collection == null || collection.attacks == null || collection.attacks.Length == 0)
        { reason = "전투에 사용할 공격 컬렉션이 없습니다."; return false; }
        var ids = new HashSet<string>();
        foreach (var source in collection.attacks)
        {
            var material = source;
            if (source != null && edits != null && edits.TryGetValue(source, out var edited)) material = edited;
            if (material == null || material.runtimeClip == null)
            { reason = "공격 컬렉션에 비어 있는 재료 또는 클립이 있습니다."; return false; }
            if (!ids.Add(material.runtimeClip.name))
            { reason = "중복된 전투 공격 키입니다: " + material.runtimeClip.name; return false; }
            if (!TryResolveTuning(settings, material, recoil, out _, out reason)) return false;
        }
        var rules = new HashSet<string>();
        if (settings.materialRules == null) { reason = "전투 공격 규칙 배열이 없습니다."; return false; }
        foreach (var rule in settings.materialRules)
        {
            if (rule == null || string.IsNullOrEmpty(rule.clip) || !ids.Contains(rule.clip) || !rules.Add(rule.clip))
            { reason = "전투 공격 규칙의 키가 없거나 중복됐습니다: " + (rule != null ? rule.clip : "없음"); return false; }
        }
        if (!ids.Contains("ThrowRock") || collection.FindMotion("UnearthRock")?.IsPlayable != true)
        { reason = "투척 조립에 필요한 ThrowRock 또는 UnearthRock이 없습니다."; return false; }
        if (settings.composites == null || settings.composites.spitPatterns == null)
        { reason = "복합 공격 연결이 없습니다."; return false; }
        foreach (var pattern in settings.composites.spitPatterns)
            if (pattern?.material?.runtimeClip == null || !ids.Contains(pattern.material.runtimeClip.name))
            { reason = "복합 분사의 공격 키가 컬렉션에 없습니다."; return false; }
        reason = "";
        return true;
    }
}
