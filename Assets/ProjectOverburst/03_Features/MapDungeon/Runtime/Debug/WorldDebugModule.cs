#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Overburst.DebugTools;
using Overburst.Persistence;
using UnityEngine;

/// <summary>
/// 던전·씬 탭(90C 7.6). 던전 입장은 기존 DungeonDebugEntry를 디버그 창 Host(DontDestroyOnLoad)에 붙여 쓴다.
/// 던전에서 돌아올 때는 RunLifetimeDriver의 포기(실패 정산)나 포털 귀환(보스 처치 뒤)을 거친다. ReturnToHub를 직접 부르면 정산을 건너뛴다.
/// </summary>
internal static class WorldDebugModule
{
    private static int dungeonLevel = 1;
    private static bool dropEquipment = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        dungeonLevel = 1;
        dropEquipment = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        DebugSection state = DebugRegistry.Section(DebugTabs.World, "현재 상태", 10);
        state.Readout("씬", SceneText)
            .WithId("world.state.scene")
            .Pinnable();
        state.Readout("런", RunText)
            .WithId("world.state.run")
            .Pinnable();

        DebugSection dungeon = DebugRegistry.Section(DebugTabs.World, "던전 입장", 20, "하이드아웃에서만");
        dungeon.Integer("던전 레벨", () => dungeonLevel, value => dungeonLevel = value, 1, OverburstGrowthRules.MaximumLevel)
            .WithId("world.dungeon.level")
            .NoPreset();
        dungeon.Toggle("같은 레벨 장비 8개 드랍", () => dropEquipment, value => dropEquipment = value)
            .WithId("world.dungeon.dropEquipment")
            .NoPreset()
            .Tip("입장하면 무기 1개와 장비 7개를 플레이어 주변에 떨어뜨린다(런 아이템).");
        dungeon.Button(() => $"Lv.{dungeonLevel} 던전 입장", EnterDungeon)
            .WithId("world.dungeon.enter")
            .EnabledWhen(CanEnter, "하이드아웃에서 입장해 주세요")
            .Keywords("dungeon", "던전", "입장");
        dungeon.Readout("입장 상태", () => Entry != null ? Entry.Status : "-")
            .WithId("world.dungeon.status");

        DebugSection back = DebugRegistry.Section(DebugTabs.World, "돌아가기", 30);
        back.Button("하이드아웃으로 돌아가기", ReturnToHideout)
            .WithId("world.return.hideout")
            .EnabledWhen(CanReturn, "이미 하이드아웃이에요")
            .ConfirmWhen(InRun, "런 실패로 정산돼요(사망과 같음). 돌아갈까요?")
            .Keywords("hideout", "귀환", "포기");
        back.Button("성공 귀환(포털)", SuccessReturn)
            .WithId("world.return.success")
            .EnabledWhen(() => RunPhaseNow == RunPhase.BossCleared, "보스를 처치한 뒤에만")
            .Tip("RunLifetimeDriver.RequestPortalExit와 같다.");
    }

    private static DungeonDebugEntry Entry
    {
        get
        {
            GameObject host = DebugHub.Host;
            if (host == null)
                return null;
            return host.TryGetComponent(out DungeonDebugEntry entry) ? entry : host.AddComponent<DungeonDebugEntry>();
        }
    }

    private static RunLifetimeDriver Driver
    {
        get
        {
            PersistentSceneFlow flow = PersistentSceneFlow.Instance;
            return flow != null ? flow.GetComponent<RunLifetimeDriver>() : null;
        }
    }

    private static RunPhase? RunPhaseNow => AccountGameplaySession.Current?.ReadRun()?.phase;

    private static bool InRun() => WorldSessionState.Phase == WorldPhase.Run;

    private static bool CanEnter()
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        DungeonDebugEntry entry = Entry;
        return flow != null && !flow.IsSwitching && WorldSessionState.IsHideout && entry != null && !entry.IsEntering;
    }

    private static bool CanReturn()
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        if (flow == null || flow.IsSwitching)
            return false;
        return InRun() || (!WorldSessionState.IsHideout && PersistentSceneFlow.IsHubSceneName(flow.CurrentSubSceneName));
    }

    private static DebugResult EnterDungeon()
    {
        DungeonDebugEntry entry = Entry;
        if (entry == null)
            return DebugResult.Fail("디버그 창이 준비되지 않았어요");
        return entry.TryEnter(dungeonLevel, dropEquipment)
            ? DebugResult.Ok(entry.Status)
            : DebugResult.Fail(entry.Status);
    }

    private static DebugResult ReturnToHideout()
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        if (flow == null)
            return DebugResult.Fail("씬 흐름이 없어요");
        if (InRun())
        {
            RunLifetimeDriver driver = Driver;
            if (driver == null)
                return DebugResult.Fail("런 관리자가 없어요");
            driver.RequestAbandon();
            return DebugResult.Ok("런 포기 → 실패 정산 뒤 하이드아웃");
        }
        flow.SwitchHubScene(PersistentSceneFlow.HideoutSceneName);
        return DebugResult.Ok("하이드아웃으로 이동");
    }

    private static DebugResult SuccessReturn()
    {
        RunLifetimeDriver driver = Driver;
        if (driver == null)
            return DebugResult.Fail("런 관리자가 없어요");
        return driver.RequestPortalExit()
            ? DebugResult.Ok("포털 귀환 요청")
            : DebugResult.Fail("보스를 처치한 뒤에만 돼요");
    }

    private static string SceneText()
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        if (flow == null)
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        string name = string.IsNullOrEmpty(flow.CurrentSubSceneName) ? "-" : flow.CurrentSubSceneName;
        return flow.IsSwitching ? name + " (전환 중)" : name;
    }

    private static string RunText()
    {
        RunPhase? phase = RunPhaseNow;
        return $"{WorldSessionState.Phase} · 런 {(phase.HasValue ? phase.Value.ToString() : "없음")}";
    }
}
#endif
