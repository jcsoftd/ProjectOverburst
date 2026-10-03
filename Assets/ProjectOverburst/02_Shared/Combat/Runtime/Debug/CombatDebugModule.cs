#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Overburst.DebugTools;
using UnityEngine;

/// <summary>
/// <see cref="CombatDebugSettings"/>의 디버그 토글 5개와 원소 항목(에너지 가득·비우기·가득 유지, 적 원소 상태 해제)을
/// 디버그 창에 등록한다(90C 7.1~7.4). 토글 값은 계속 CombatDebugSettings가 소유한다.
/// </summary>
internal static class CombatDebugModule
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        DebugRegistry.Section(DebugTabs.Player, "생존", 10)
            .Toggle("받는 피해 99.9% 감소",
                () => CombatDebugSettings.ReduceIncomingPlayerDamageBy99_9Percent,
                CombatDebugSettings.SetPlayerDamageReductionDebug)
            .WithId("player.survival.damageReduction")
            .Keywords("무적", "god", "피해")
            .Tip("플레이어가 받는 피해를 1000분의 1로 줄인다.");

        DebugRegistry.Section(DebugTabs.Combat, "표시", 10)
            .Toggle("공격 판정창",
                () => CombatDebugSettings.ShowAttackPatternDebug,
                CombatDebugSettings.SetAttackPatternDebug)
            .WithId("combat.display.attackPattern")
            .Keywords("hitbox", "판정")
            .Tip("플레이어 공격의 판정 범위를 그린다.");

        DebugSection element = DebugRegistry.Section(DebugTabs.Combat, "원소", 25);
        element.Readout("원소 에너지", ElementEnergyDebug.Text)
            .WithId("combat.element.energy")
            .Pinnable();
        element.Buttons("에너지")
            .Add("가득", ElementEnergyDebug.Fill)
            .Add("비우기", ElementEnergyDebug.Empty)
            .WithId("combat.element.energyActions")
            .EnabledWhen(() => ElementEnergyDebug.HasElementWeapon, "원소 무기를 들었을 때만")
            .Keywords("energy", "에너지", "원소");
        element.Toggle("에너지 가득 유지", () => ElementEnergyDebug.Hold, ElementEnergyDebug.SetHold)
            .WithId("combat.element.holdFull")
            .Keywords("energy", "에너지", "원소")
            .Tip("강공으로 방출하거나 무기를 바꿔 에너지가 줄면 그 자리에서 기본 최대치로 다시 채운다(가득 참 소리가 난다). 켤 때 한 번 채운다.");
        element.Buttons("적 원소 상태")
            .Add("모두 해제", ElementEnergyDebug.ClearEnemyStatuses)
            .WithId("combat.element.clearStatuses")
            .Keywords("status", "상태", "원소")
            .Tip("살아 있는 적에게 걸린 원소 상태를 모두 지운다.");

        DebugSection visual = DebugRegistry.Section(DebugTabs.Enemies, "시각화", 30);
        visual.Toggle("AI 상태 글자",
                () => CombatDebugSettings.ShowEnemyAiStateDebug,
                CombatDebugSettings.SetEnemyAiStateDebug)
            .WithId("enemies.visual.aiState")
            .Keywords("ai", "상태")
            .Tip("적 체력 바 위에 AI 상태를 글자로 띄운다.");
        visual.Toggle("부대 형상",
                () => CombatDebugSettings.ShowEnemySquadGeometryDebug,
                CombatDebugSettings.SetEnemySquadGeometryDebug)
            .WithId("enemies.visual.squadGeometry")
            .Keywords("분대", "squad")
            .Tip("분대 추격 구역과 부대 배치를 그린다.");

        DebugRegistry.Section(DebugTabs.Spawn, "대량 스폰", 20)
            .Toggle("하이드아웃 몬스터 스폰",
                () => CombatDebugSettings.SpawnHideoutMonsters,
                CombatDebugSettings.SetHideoutMonsterSpawn)
            .WithId("spawn.mass.hideoutMonsters")
            .Keywords("spawn", "몬스터")
            .Tip("하이드아웃에 몬스터가 나오게 한다.");

        DebugPresets.RegisterBuiltIn("전투 테스트",
            ("player.survival.damageReduction", "1"),
            ("combat.display.attackPattern", "1"),
            ("enemies.visual.aiState", "1"));
    }
}

/// <summary>
/// 원소 에너지 디버그(90C 7.2). <see cref="OverburstElementEnergy"/>를 고치지 않고 기존 함수만 쓴다:
/// 채우기는 패링 환급과 같은 <c>RefundParried</c>, 비우기는 <c>Clear</c>. '가득 유지'는 매 프레임 채우지 않고
/// <c>Changed</c> 이벤트에서 기본 최대치보다 줄었을 때만 다시 채운다.
/// </summary>
public static class ElementEnergyDebug
{
    private static bool hold;
    private static bool refilling;
    private static OverburstElementEnergy bound;

    public static bool Hold => hold;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        hold = false;
        refilling = false;
        bound = null;
    }

    private static PlayerActorRuntime Actor => PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActor : null;

    private static PlayerEquipment Equipment => Actor != null ? Actor.Equipment : null;

    /// <summary>읽기 전용. 에너지 컴포넌트는 첫 원소 적중 때 붙으므로(OverburstElementCombat) 아직 없을 수 있다.</summary>
    public static OverburstElementEnergy Energy => Equipment != null ? Equipment.GetComponent<OverburstElementEnergy>() : null;

    /// <summary>채울 때는 게임 코드(OverburstElementCombat)와 같은 방식으로 없으면 붙인다.</summary>
    private static OverburstElementEnergy EnsureEnergy()
    {
        PlayerEquipment equipment = Equipment;
        if (equipment == null)
            return null;
        OverburstElementEnergy energy = equipment.GetComponent<OverburstElementEnergy>();
        return energy != null ? energy : equipment.gameObject.AddComponent<OverburstElementEnergy>();
    }

    private static WeaponElement WeaponElementNow
    {
        get
        {
            ItemData weapon = Equipment != null ? Equipment.CurrentWeaponItem : null;
            return Equipment != null ? Equipment.ActiveElement : WeaponElement.None;
        }
    }

    public static bool HasElementWeapon => OverburstElementRules.IsActive(WeaponElementNow);

    public static string Text()
    {
        if (Equipment == null)
            return "-";
        WeaponElement element = WeaponElementNow;
        if (!OverburstElementRules.IsActive(element))
            return "원소 없는 무기";
        OverburstElementEnergy energy = Energy;
        if (energy == null)
            return $"{OverburstElementRules.Label(element)} 0 (아직 적중 없음)";
        string text = $"{OverburstElementRules.Label(energy.Element)} {energy.Amount:0} / {energy.BaseMaximum:0}";
        return energy.RadianceStacks > 0 ? text + $" · 광휘 {energy.RadianceStacks}" : text;
    }

    public static DebugResult Fill()
        => HasElementWeapon && TopUp(EnsureEnergy()) ? DebugResult.Ok(Text()) : DebugResult.Fail("원소 무기를 들고 있지 않아요");

    public static DebugResult Empty()
    {
        OverburstElementEnergy energy = Energy;
        if (energy == null)
            return DebugResult.Fail("플레이어가 없어요");
        bool wasHeld = hold;
        SetHold(false); // 가득 유지 중에는 비워도 곧바로 다시 차므로 먼저 끈다
        energy.Clear();
        return DebugResult.Ok(wasHeld ? "가득 유지를 끄고 비웠어요" : Text());
    }

    public static void SetHold(bool enabled)
    {
        hold = enabled;
        Bind(enabled && HasElementWeapon ? EnsureEnergy() : null);
        if (!enabled)
            return;
        GameObject host = DebugHub.Host;
        if (host != null && !host.TryGetComponent(out ElementEnergyDebugRunner _))
            host.AddComponent<ElementEnergyDebugRunner>();
        TopUp(bound);
    }

    /// <summary>러너가 0.5초마다 부른다. 플레이어 액터가 바뀌면 새 에너지 컴포넌트에 다시 붙는다.</summary>
    internal static void Tick()
    {
        if (!hold)
            return;
        OverburstElementEnergy current = HasElementWeapon ? EnsureEnergy() : Energy;
        if (current != bound)
        {
            Bind(current);
            TopUp(bound);
        }
    }

    private static void Bind(OverburstElementEnergy energy)
    {
        if (ReferenceEquals(bound, energy))
            return;
        if (!ReferenceEquals(bound, null))
            bound.Changed -= OnChanged;
        bound = energy;
        if (!ReferenceEquals(bound, null))
            bound.Changed += OnChanged;
    }

    private static void OnChanged()
    {
        if (hold && !refilling)
            TopUp(bound);
    }

    /// <summary>기본 최대치까지 채운다. 빛 과충전 구간(최대치 위)은 쌓지 않는다.</summary>
    private static bool TopUp(OverburstElementEnergy energy)
    {
        if (energy == null || !energy.isActiveAndEnabled || !OverburstElementRules.IsActive(energy.Element))
            return false;
        CombatHealth health = Actor != null ? Actor.Health : null;
        if (health != null && health.IsDead)
            return false;
        float missing = energy.BaseMaximum - energy.Amount;
        if (missing <= 0.0001f)
            return true;
        refilling = true;
        try
        {
            energy.RefundParried(missing);
        }
        finally
        {
            refilling = false;
        }
        return true;
    }

    public static DebugResult ClearEnemyStatuses()
    {
        int cleared = 0;
        foreach (ElementalStatusController status in Object.FindObjectsByType<ElementalStatusController>(FindObjectsSortMode.None))
        {
            if (status == null || !status.isActiveAndEnabled || status.GetComponentInParent<EnemyRank>() == null)
                continue;
            status.ClearAllStatuses();
            cleared++;
        }
        return DebugResult.Ok($"적 {cleared}마리");
    }
}

/// <summary>디버그 창 Host에 붙어 '에너지 가득 유지'가 켜져 있을 때 대상 액터가 바뀌었는지 본다.</summary>
public sealed class ElementEnergyDebugRunner : MonoBehaviour
{
    private float nextTick;

    private void Update()
    {
        if (Time.unscaledTime < nextTick)
            return;
        nextTick = Time.unscaledTime + 0.5f;
        ElementEnergyDebug.Tick();
    }
}
#endif
