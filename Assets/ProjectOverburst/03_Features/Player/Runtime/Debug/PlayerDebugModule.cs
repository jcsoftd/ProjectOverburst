#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Overburst.DebugTools;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>플레이어 탭: 생존·이동·성장·장비·물약(90C 7.1). 받는 피해 감소는 CombatDebugModule이 같은 '생존' 섹션에 등록한다.</summary>
internal static class PlayerDebugModule
{
    private static int targetLevel = 10;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        DebugSection survival = DebugRegistry.Section(DebugTabs.Player, "생존", 10);
        survival.Button("체력 가득 채우기", FillHealth)
            .WithId("player.survival.fillHealth")
            .EnabledWhen(() => Health != null, "플레이어가 있는 씬에서만")
            .Keywords("heal", "회복");
        survival.Readout("체력", () => Health != null ? $"{Health.CurrentHp:0} / {Health.MaxHp:0}" : "-")
            .WithId("player.survival.hp")
            .Pinnable();

        DebugSection move = DebugRegistry.Section(DebugTabs.Player, "이동", 20);
        move.Button("커서 위치로 순간이동", DebugTeleport.ToCursor)
            .WithId("player.move.teleportCursor")
            .Hotkey(Key.F3)
            .EnabledWhen(() => DebugTeleport.HasPlayer, "플레이어가 있는 씬에서만")
            .Keywords("teleport", "warp", "이동")
            .Tip("마우스가 가리키는 바닥으로 옮긴다. 창을 닫은 채 F3으로도 쓴다.");
        move.Readout("위치", () =>
            {
                Transform player = DebugTeleport.Player;
                return player != null ? $"{player.position.x:0.0}, {player.position.y:0.0}, {player.position.z:0.0}" : "-";
            })
            .WithId("player.move.position")
            .Pinnable();

        DebugSection growth = DebugRegistry.Section(DebugTabs.Player, "성장", 30, "경험치는 계정에 저장돼요");
        growth.Readout("레벨", LevelText)
            .WithId("player.growth.level")
            .Pinnable();
        growth.Buttons("경험치")
            .Add("+100", () => AddExperience(100))
            .Add("+1000", () => AddExperience(1000))
            .Add("다음 레벨까지", ToNextLevel)
            .WithId("player.growth.experience")
            .EnabledWhen(() => PlayerProgression.Current != null, "플레이어가 있는 씬에서만")
            .ConfirmOnRealAccount()
            .Keywords("exp", "xp", "레벨");
        growth.Integer("목표 레벨", () => TargetLevel, value => targetLevel = value, 2, OverburstGrowthRules.MaximumLevel)
            .WithId("player.growth.targetLevel")
            .NoPreset();
        growth.Button(() => $"Lv.{TargetLevel}까지 올리기", RaiseToTarget)
            .WithId("player.growth.raise")
            .EnabledWhen(() => PlayerProgression.Current != null && PlayerProgression.Current.Level < TargetLevel,
                "지금 레벨보다 높은 목표만 가능해요(내리기는 없어요)")
            .ConfirmOnRealAccount();

        DebugSection gear = DebugRegistry.Section(DebugTabs.Player, "장비", 40, "아이템은 아이템·경제 탭에서도 만들어요");
        gear.Buttons("지급")
            .Add("시작 무기", GrantStarterWeapon)
            .Add("테스트 가방", GrantTestBags)
            .Add("인벤토리 첫 무기 장착", EquipFirstWeapon)
            .WithId("player.gear.grant")
            .EnabledWhen(() => Equipment != null, "플레이어가 있는 씬에서만")
            .ConfirmOnRealAccount();

        DebugSection flask = DebugRegistry.Section(DebugTabs.Player, "물약", 50);
        flask.Buttons("물약")
            .Add("쿨다운 초기화", ResetFlaskCooldowns)
            .Add("효과 지우기", ClearFlaskEffects)
            .WithId("player.flask.actions")
            .EnabledWhen(() => PlayerFlaskController.Current != null, "물약 컨트롤러가 없어요");
    }

    /// <summary>목표는 늘 지금 레벨보다 높게 보인다(내리기는 없어서). 직접 고친 값이 더 높으면 그 값을 쓴다.</summary>
    private static int TargetLevel
    {
        get
        {
            int current = PlayerProgression.Current != null ? PlayerProgression.Current.Level : 1;
            return Mathf.Clamp(Mathf.Max(targetLevel, current + 1), 2, OverburstGrowthRules.MaximumLevel);
        }
    }

    private static PlayerActorRuntime Actor => PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActor : null;
    private static CombatHealth Health => Actor != null ? Actor.Health : null;
    private static PlayerEquipment Equipment => Actor != null ? Actor.Equipment : null;

    private static DebugResult FillHealth()
    {
        CombatHealth health = Health;
        if (health == null)
            return DebugResult.Fail("플레이어가 없어요");
        if (health.IsDead)
            return DebugResult.Fail("사망 상태에서는 채울 수 없어요");
        // 회복 배율(지도 효과 등)과 상관없이 가득 채운다. CombatHealth 우클릭 메뉴 'Reset Health'와 같은 함수다.
        health.ResetHealth();
        return DebugResult.Ok($"{health.CurrentHp:0} / {health.MaxHp:0}");
    }

    private static string LevelText()
    {
        PlayerProgression progression = PlayerProgression.Current;
        if (progression == null)
            return "-";
        return progression.Level >= OverburstGrowthRules.MaximumLevel
            ? $"Lv.{progression.Level} (최대)"
            : $"Lv.{progression.Level} · {progression.Experience} / {progression.ExperienceToNext}";
    }

    private static DebugResult AddExperience(int amount)
    {
        PlayerProgression progression = PlayerProgression.Current;
        if (progression == null)
            return DebugResult.Fail("플레이어가 없어요");
        if (progression.Level >= OverburstGrowthRules.MaximumLevel)
            return DebugResult.Fail("최대 레벨이에요");
        progression.AddExperience(amount);
        return DebugResult.Ok($"+{amount}");
    }

    private static DebugResult ToNextLevel()
    {
        PlayerProgression progression = PlayerProgression.Current;
        if (progression == null)
            return DebugResult.Fail("플레이어가 없어요");
        int need = progression.ExperienceToNext - progression.Experience;
        return need > 0 ? AddExperience(need) : DebugResult.Fail("최대 레벨이에요");
    }

    /// <summary>레벨을 직접 정하는 함수가 없어 필요한 경험치를 계산해 넣는다(계정 세션 중에는 계정 거래로 들어간다).</summary>
    private static DebugResult RaiseToTarget()
    {
        PlayerProgression progression = PlayerProgression.Current;
        if (progression == null)
            return DebugResult.Fail("플레이어가 없어요");
        int target = TargetLevel;
        if (progression.Level >= target)
            return DebugResult.Fail("이미 목표 레벨 이상이에요");
        long total = progression.ExperienceToNext - progression.Experience;
        for (int level = progression.Level + 1; level < target; level++)
            total += OverburstGrowthRules.ExperienceToNext(level);
        if (total > int.MaxValue)
            return DebugResult.Fail("필요 경험치가 너무 커요");
        progression.AddExperience((int)total);
        return DebugResult.Ok($"경험치 +{total} → Lv.{target}");
    }

    private static DebugResult GrantStarterWeapon()
    {
        PlayerStarterLoadout loadout = FindLoadout();
        if (loadout == null)
            return DebugResult.Fail("시작 지급 컴포넌트가 없어요");
        loadout.GrantStarterWeapon();
        return DebugResult.Ok();
    }

    private static DebugResult GrantTestBags()
    {
        PlayerStarterLoadout loadout = FindLoadout();
        if (loadout == null)
            return DebugResult.Fail("시작 지급 컴포넌트가 없어요");
        loadout.GrantTestBags();
        return DebugResult.Ok();
    }

    private static DebugResult EquipFirstWeapon()
    {
        PlayerEquipment equipment = Equipment;
        if (equipment == null)
            return DebugResult.Fail("플레이어가 없어요");
        return equipment.EquipFirstWeaponFromInventory()
            ? DebugResult.Ok("장착")
            : DebugResult.Fail("인벤토리에 장착할 무기가 없어요");
    }

    private static PlayerStarterLoadout FindLoadout()
    {
        PlayerActorRuntime actor = Actor;
        PlayerStarterLoadout loadout = actor != null ? actor.GetComponentInChildren<PlayerStarterLoadout>(true) : null;
        return loadout != null ? loadout : Object.FindFirstObjectByType<PlayerStarterLoadout>(FindObjectsInactive.Include);
    }

    private static DebugResult ResetFlaskCooldowns()
    {
        PlayerFlaskController flasks = PlayerFlaskController.Current;
        if (flasks == null)
            return DebugResult.Fail("물약 컨트롤러가 없어요");
        flasks.ResetCooldowns();
        return DebugResult.Ok();
    }

    private static DebugResult ClearFlaskEffects()
    {
        PlayerFlaskController flasks = PlayerFlaskController.Current;
        if (flasks == null)
            return DebugResult.Fail("물약 컨트롤러가 없어요");
        flasks.ClearEffects();
        return DebugResult.Ok();
    }
}
#endif
