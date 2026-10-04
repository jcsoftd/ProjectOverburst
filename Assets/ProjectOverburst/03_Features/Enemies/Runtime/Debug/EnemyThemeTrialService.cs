#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Overburst.DebugTools;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>적 테마 시험 카탈로그 항목. 테마를 더하려면 <see cref="EnemyThemeTrialService.Themes"/>에 한 줄 넣는다.</summary>
public sealed class EnemyThemeTrialEntry
{
    public string Id;
    public string ShortName;
    public EnemyThemeTable Table;
    public Material Warning;
}

/// <summary>
/// 적 테마 시험 로직(90C 8.1). 옛 HUD 테마 패널(2026-10-01 삭제)의 소환·정리·시험장·순간이동 동작을 UI 없이 옮겼다.
/// 카탈로그는 Resources의 테마 표·경고 재질·시험장 프리팹을 이름으로 읽는다(SO 에셋 없이 코드 한 줄로 테마 추가).
/// 옛 UI는 기존 검증기 60여 개가 직접 쓰므로 그대로 두고, 두 경로는 상태를 나누지 않는다.
/// </summary>
public static class EnemyThemeTrialService
{
    // 옛 MonsterThemeDebugBuilder와 같은 순서·짧은 이름.
    internal static readonly (string id, string shortName)[] Themes =
    {
        ("SpiderBrood", "갑각"),
        ("VenomBrood", "독낭"),
        ("PrimalHunt", "원시"),
        ("CavernMutants", "암굴"),
        ("DeathHarvest", "사령"),
        ("RotsporeMarsh", "부패습지"),
        ("AlienContainment", "격리구역"),
    };

    private const string TablePath = "Enemies/Themes/Tables/";
    private const string MaterialPath = "Enemies/Themes/Materials/";
    private const string ArenaPath = "Enemies/Themes/Arena/PF_EnemyThemeDebugArena";
    private const float ZoneSpacing = 40f;

    private static List<EnemyThemeTrialEntry> entries;
    private static EnemyThemeDebugArena arenaPrefab;
    private static EnemyThemeTrialMode mode = EnemyThemeTrialMode.Normal;
    private static EnemyThemeEncounter encounter;
    private static EnemyThemeDebugArena arena;
    private static Transform arenaPlayer;
    private static Vector3 returnPosition;
    private static Quaternion returnRotation;
    private static bool previousHideoutSpawn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        entries = null;
        arenaPrefab = null;
        mode = EnemyThemeTrialMode.Normal;
        encounter = null;
        arena = null;
        arenaPlayer = null;
    }

    public static IReadOnlyList<EnemyThemeTrialEntry> Entries
    {
        get
        {
            if (entries != null)
                return entries;
            entries = new List<EnemyThemeTrialEntry>(Themes.Length);
            foreach ((string id, string shortName) in Themes)
            {
                var table = Resources.Load<EnemyThemeTable>(TablePath + id);
                if (table == null || !MapThemeCatalog.IsEnabledForRuns(id) || !table.Validate(out _))
                {
                    Debug.LogWarning("[EnemyThemeTrial] 테마 표가 없어요: " + id);
                    continue;
                }
                entries.Add(new EnemyThemeTrialEntry
                {
                    Id = id,
                    ShortName = shortName,
                    Table = table,
                    Warning = Resources.Load<Material>(MaterialPath + table.ThemeId)
                });
            }
            arenaPrefab = Resources.Load<EnemyThemeDebugArena>(ArenaPath);
            return entries;
        }
    }

    public static EnemyThemeTrialMode Mode => mode;
    public static bool InArena => arena != null;
    public static int ArenaZoneCount => arena != null ? arena.GetComponentsInChildren<EnemyThemeTriggerZone>(true).Length : 0;
    public static bool Busy => (encounter != null && encounter.HasOutstandingLeases) || (arena != null && arena.HasActiveEncounters);
    public static bool HasPlayer => PlayerInputFacade.Current != null;
    public static EnemyThemeEncounter CurrentEncounter => encounter;

    public static bool CanEnterArena
    {
        get
        {
            PersistentSceneFlow flow = PersistentSceneFlow.Instance;
            IReadOnlyList<EnemyThemeTrialEntry> list = Entries;
            return arenaPrefab != null && list.Count > 0 && HasPlayer && flow != null && !flow.IsSwitching
                && flow.CurrentSubSceneName == PersistentSceneFlow.HideoutSceneName;
        }
    }

    public static int CountOf(EnemyThemeTrialEntry entry)
        => entry?.Table != null ? EnemyThemeTrialPresets.Resolve(entry.Table, mode).Total : 0;

    public static DebugResult SetMode(EnemyThemeTrialMode value)
    {
        if (Busy)
            return DebugResult.Fail("진행 중인 시험을 먼저 정리하세요");
        if (arena != null && !arena.ConfigureTrialMode(value))
            return DebugResult.Fail("시험장 구역을 바꾸지 못했어요");
        mode = value;
        return DebugResult.Ok(EnemyThemeTrialPresets.Label(value));
    }

    public static DebugResult Begin(EnemyThemeTrialEntry entry, bool waves)
    {
        if (entry?.Table == null)
            return DebugResult.Fail("테마가 없어요");
        PlayerInputFacade player = PlayerInputFacade.Current;
        if (player == null)
            return DebugResult.Fail("플레이어가 있는 전투 씬에서 쓰세요");
        if (Busy)
            return DebugResult.Fail("진행 중인 시험을 먼저 정리하세요");
        Clear();
        EnsureRunner();
        // 이름은 옛 HUD 패널과 같다. 검증기 여럿이 이 이름으로 조우를 찾는다.
        var root = new GameObject("Theme debug encounter");
        SceneManager.MoveGameObjectToScene(root, player.gameObject.scene);
        encounter = root.AddComponent<EnemyThemeEncounter>();
        encounter.Configure(entry.Table, null, entry.Warning);
        EnemyThemeTrialRoster roster = EnemyThemeTrialPresets.Resolve(entry.Table, mode);
        encounter.ConfigureTrialRoster(roster);
        bool started = encounter.Begin(player.transform, waves);
        return started
            ? DebugResult.Ok($"{entry.ShortName} {roster.Total}마리{(waves ? " ×3회" : string.Empty)}")
            : DebugResult.Fail(encounter.LastMessage);
    }

    public static DebugResult Clear()
    {
        if (arena != null)
            arena.ClearEncounters();
        if (encounter != null)
        {
            encounter.StopEncounter(true);
            Object.Destroy(encounter.gameObject);
            encounter = null;
        }
        return DebugResult.Ok("정리");
    }

    public static DebugResult ToggleArena()
    {
        if (arena != null)
        {
            ExitArena(true);
            return DebugResult.Ok("하이드아웃으로 돌아감");
        }
        if (!CanEnterArena)
            return DebugResult.Fail("시험장은 하이드아웃에서 입장하세요");
        Clear();
        EnsureRunner();
        arenaPlayer = PlayerInputFacade.Current.transform;
        returnPosition = arenaPlayer.position;
        returnRotation = arenaPlayer.rotation;
        previousHideoutSpawn = CombatDebugSettings.SpawnHideoutMonsters;
        CombatDebugSettings.SetHideoutMonsterSpawn(false);
        arena = Object.Instantiate(arenaPrefab, new Vector3(1000f, 0f, 1000f), Quaternion.identity);
        arena.name = "Monster Theme Arena (Debug Hub)";
        SceneManager.MoveGameObjectToScene(arena.gameObject, arenaPlayer.gameObject.scene);
        EnsureZones();
        arena.ConfigureTrialMode(mode);
        arena.ProtectPlayer(arenaPlayer.GetComponent<CombatHealth>());
        Teleport(arena.entry.position, arena.entry.rotation);
        return DebugResult.Ok($"시험장 입장 · 구역 {ArenaZoneCount}개 · 최소 체력 1 보호");
    }

    public static DebugProgress Progress
    {
        get
        {
            if (encounter == null)
                return new DebugProgress(0f, 1f, InArena ? "시험장 · 구역에 들어가면 시작" : "대기");
            string name = encounter.Table != null ? encounter.Table.DisplayName : "테마";
            int spawned = Mathf.Max(1, encounter.SpawnedCount);
            return new DebugProgress(encounter.DefeatedCount, spawned,
                $"{name} · {encounter.Wave}공세 · 생존 {encounter.AliveCount} / 처치 {encounter.DefeatedCount} / 누적 {encounter.SpawnedCount}");
        }
    }

    public static string StatusText
    {
        get
        {
            if (encounter != null)
                return encounter.LastMessage;
            return InArena ? "시험장 보호 중 · 최소 체력 1" : $"{EnemyThemeTrialPresets.Label(mode)} · 1회 / 3회 공세";
        }
    }

    /// <summary>러너가 0.2초마다 부른다. 하이드아웃을 벗어나거나 씬이 바뀌면 시험장에서 자동으로 나온다.</summary>
    internal static void Tick()
    {
        if (arena == null)
            return;
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        if (arenaPlayer == null || flow == null || flow.IsSwitching
            || flow.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            ExitArena(false);
    }

    internal static void Shutdown()
    {
        ExitArena(false);
        Clear();
    }

    /// <summary>카탈로그 항목 수만큼 구역을 맞춘다. 모자라면 마지막 구역을 복제해 설정한다(옛 EnsureDeathHarvestArenaZone의 일반화).</summary>
    private static void EnsureZones()
    {
        IReadOnlyList<EnemyThemeTrialEntry> list = Entries;
        EnemyThemeTriggerZone[] zones = arena.GetComponentsInChildren<EnemyThemeTriggerZone>(true);
        if (zones.Length == 0)
            return;
        for (int i = zones.Length; i < list.Count; i++)
        {
            EnemyThemeTriggerZone extra = Object.Instantiate(zones[zones.Length - 1], arena.transform);
            extra.name = list[i].Table.DisplayName + " trigger";
            var zoneEncounter = extra.GetComponent<EnemyThemeEncounter>();
            zoneEncounter.Configure(list[i].Table, null, list[i].Warning);
            extra.Configure(zoneEncounter, true);
            var label = extra.transform.Find("Zone label")?.GetComponent<TextMeshPro>();
            if (label != null)
                label.text = list[i].Table.DisplayName + "\n진입하면 3회 공세";
            var marker = extra.transform.Find("Entry marker")?.GetComponent<Renderer>();
            if (marker != null && list[i].Warning != null)
                marker.sharedMaterial = list[i].Warning;
        }
        zones = arena.GetComponentsInChildren<EnemyThemeTriggerZone>(true);
        for (int i = 0; i < zones.Length; i++)
            zones[i].transform.localPosition = new Vector3((i - (zones.Length - 1) * 0.5f) * ZoneSpacing, 0f, 0f);
    }

    private static void ExitArena(bool restorePosition)
    {
        if (arena == null)
            return;
        arena.ReleasePlayerProtection();
        Clear();
        if (restorePosition)
            Teleport(returnPosition, returnRotation);
        Object.Destroy(arena.gameObject);
        arena = null;
        arenaPlayer = null;
        CombatDebugSettings.SetHideoutMonsterSpawn(previousHideoutSpawn);
    }

    private static void Teleport(Vector3 position, Quaternion rotation)
    {
        if (arenaPlayer == null)
            return;
        DebugTeleport.To(position);
        arenaPlayer.rotation = rotation;
    }

    private static void EnsureRunner()
    {
        GameObject host = DebugHub.Host;
        if (host != null && !host.TryGetComponent(out EnemyThemeTrialRunner _))
            host.AddComponent<EnemyThemeTrialRunner>();
    }
}

/// <summary>디버그 창 Host에 붙어 시험장 자동 퇴장을 검사한다.</summary>
public sealed class EnemyThemeTrialRunner : MonoBehaviour
{
    private float nextTick;

    private void Update()
    {
        if (Time.unscaledTime < nextTick)
            return;
        nextTick = Time.unscaledTime + 0.2f;
        EnemyThemeTrialService.Tick();
    }

    private void OnDestroy()
    {
        EnemyThemeTrialService.Shutdown();
    }
}
#endif
