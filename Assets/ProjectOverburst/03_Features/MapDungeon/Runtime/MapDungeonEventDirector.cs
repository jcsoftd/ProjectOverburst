using System;
using System.Collections.Generic;
using System.IO;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class MapDungeonEventDirector : MonoBehaviour
{
    private readonly List<MapDungeonEventNode> events = new List<MapDungeonEventNode>();
    private readonly List<MapRunTransferObject> transferObjects = new List<MapRunTransferObject>();
    private EnemyThemeTable theme;
    private EnemySpawnService spawnService;
    private EncounterContext encounter;
    private MapInstanceState map;
    private Material accent;
    private OverburstRunUi ui;
    private bool uiWarningLogged;
    private float nextHintCheck;
    private string lastHintTitle;
    private string lastHintBody;

    public IReadOnlyList<MapDungeonEventNode> Events => events;
    public IReadOnlyList<MapRunTransferObject> TransferObjects => transferObjects;

    public void Configure(DiamondCorner start, EnemyThemeTable selectedTheme,
        EnemySpawnService service, EncounterContext context, MapInstanceState activeMap,
        MapItemData mapDefinition, AccountContentRegistry registry,
        Material accentMaterial, IReadOnlyList<MapMonsterField> fields, int seed)
    {
        theme = selectedTheme;
        spawnService = service;
        encounter = context;
        map = activeMap;
        accent = accentMaterial;
        var random = new System.Random(seed);
        var eventPoints = new List<Vector3>();
        int total = Mathf.Max(3, Mathf.RoundToInt(DiamondDungeonLayout.Radius / 15f));
        int[] perBand = { total / 3, total / 3, total / 3 };
        for (int i = 0; i < total % 3; i++) perBand[1 + (i % 2)]++;
        int eventIndex = 0;
        for (int band = 0; band < 3; band++)
            for (int local = 0; local < perBand[band]; local++)
            {
                Vector3 point = RollEventPoint(random, start, band, eventPoints, fields);
                eventPoints.Add(point);
                MapEventKind kind = eventIndex == 0 ? MapEventKind.Hunt
                    : eventIndex == 1 ? MapEventKind.Guard
                    : random.Next(2) == 0 ? MapEventKind.Hunt : MapEventKind.Guard;
                string id = "event-" + eventIndex;
                var root = Child("MapEvent_" + band + "_" + local, point);
                var node = root.AddComponent<MapDungeonEventNode>();
                node.Configure(id, kind, band, random.Next(), theme, spawnService,
                    encounter, mapDefinition, registry, map.level,
                    PlayerProgression.CurrentLevel, accent);
                node.ChestRequested += OpenCards;
                events.Add(node);
                eventIndex++;
            }
        for (int i = 0; i < events.Count; i++)
        {
            // Independent low-probability natural rolls: no run-wide transfer cap.
            if (random.NextDouble() >= .05) continue;
            Vector3 point = DiamondDungeonLayout.RollPoint(random, start, .12f, .88f,
                eventPoints.ToArray(), 12f);
            eventPoints.Add(point);
            CreateTransferObject("transfer-natural-" + i, point);
        }
    }

    private Vector3 RollEventPoint(System.Random random, DiamondCorner start, int band,
        List<Vector3> eventsSoFar, IReadOnlyList<MapMonsterField> fields)
    {
        float min = band == 0 ? .11f : band == 1 ? .36f : .69f;
        float max = band == 0 ? .31f : band == 1 ? .64f : .88f;
        for (int attempt = 0; attempt < 3000; attempt++)
        {
            float x = ((float)random.NextDouble() * 2f - 1f) * DiamondDungeonLayout.Radius;
            float z = ((float)random.NextDouble() * 2f - 1f) * DiamondDungeonLayout.Radius;
            var point = new Vector3(x, .05f, z);
            float progress = DiamondDungeonLayout.Progress(point, start);
            if (!DiamondDungeonLayout.Contains(point, 9f) || progress < min || progress > max)
                continue;
            bool blocked = false;
            foreach (Vector3 previous in eventsSoFar)
                if ((point - previous).sqrMagnitude < 22f * 22f) { blocked = true; break; }
            if (blocked) continue;
            if (fields != null)
                foreach (MapMonsterField field in fields)
                    if (field != null && (point - field.transform.position).sqrMagnitude < 11f * 11f)
                    { blocked = true; break; }
            if (!blocked) return point;
        }
        throw new InvalidOperationException("이벤트 오브젝트 최소 간격을 만족하는 위치를 찾지 못했습니다.");
    }

    private GameObject Child(string name, Vector3 position)
    {
        var child = new GameObject(name);
        SceneManager.MoveGameObjectToScene(child, gameObject.scene);
        child.transform.SetParent(transform, false);
        child.transform.position = position;
        return child;
    }

    private MapRunTransferObject CreateTransferObject(string id, Vector3 position)
    {
        var root = Child("RunTransfer_" + id, position);
        var transfer = root.AddComponent<MapRunTransferObject>();
        transfer.Configure(id, encounter.RunId, accent);
        transfer.OpenRequested += OpenTransfer;
        transferObjects.Add(transfer);
        return transfer;
    }

    private void OpenCards(MapDungeonEventNode node)
    {
        if (node == null || node.Phase != MapEventPhase.Complete || !TryGetActiveRun(out var account, out var run))
            return;
        if (run.rewardedEncounters.Contains(node.EventId)) { node.MarkClaimed(); return; }
        var panel = EnsureUi();
        if (panel == null) return;
        var choices = new RunCardPresentation[3];
        for (int i = 0; i < choices.Length; i++)
        {
            MapCardChoice choice = node.Offer.Choices[i];
            choices[i] = new RunCardPresentation
            {
                Title = choice.Title, Description = choice.Description,
                Value = choice.EffectText, Icon = RunCardIconSet.Resolve(choice.Id), Grade = choice.Grade,
                IsReward = choice.Kind != MapCardKind.Buff
            };
        }
        panel.ShowCards(choices, index => ChooseCard(node, index));
    }

    private bool ChooseCard(MapDungeonEventNode node, int index)
    {
        if (node == null || node.Phase != MapEventPhase.Complete || index < 0 || index > 2
            || !TryGetActiveRun(out var account, out var run)) return false;
        if (run.rewardedEncounters.Contains(node.EventId)) { node.MarkClaimed(); return true; }
        MapCardChoice choice = node.Offer.Choices[index];
        GameObject prepared = null;
        try
        {
            if (choice.Kind == MapCardKind.LootChest)
            {
                ItemData reward = RollSpecialLoot(map.level, run.runId);
                if (reward == null) return false;
                prepared = Child("SpecialLoot_" + node.EventId,
                    node.transform.position + new Vector3(2.5f, 0f, 0f));
                prepared.AddComponent<MapRewardChest>().Configure(run.runId, reward, accent);
                prepared.SetActive(false);
            }
            else if (choice.Kind == MapCardKind.TransferObject)
            {
                MapRunTransferObject transfer = CreateTransferObject(
                    "transfer-card-" + node.EventId,
                    node.transform.position + new Vector3(2.5f, 0f, 0f));
                prepared = transfer.gameObject;
                prepared.SetActive(false);
            }
            bool committed = new AccountRunSession(account).ClaimEventCard(run.runId,
                node.EventId, choice.Kind == MapCardKind.Experience ? Mathf.RoundToInt(choice.Value) : 0);
            if (!committed) return false;
            if (choice.Kind == MapCardKind.Buff && !(MapRunBuffs.Current?.Add(node.EventId, choice) ?? false))
                Debug.LogError("카드 저장은 확정됐지만 런 버프 적용에 실패했습니다: " + node.EventId, this);
            node.MarkClaimed();
            if (prepared != null) prepared.SetActive(true);
            return true;
        }
        catch (Exception error)
        {
            Debug.LogError("카드 선택 실패: " + error.Message, this);
            // ExecuteState can commit before a projection error. Re-read the durable run before cleanup.
            bool committed = account.ReadRun()?.rewardedEncounters.Contains(node.EventId) == true;
            if (committed)
            {
                if (choice.Kind == MapCardKind.Buff) MapRunBuffs.Current?.Add(node.EventId, choice);
                node.MarkClaimed();
                if (prepared != null) prepared.SetActive(true);
                return true;
            }
            return false;
        }
        finally
        {
            if (prepared != null && node.Phase != MapEventPhase.Claimed)
                Destroy(prepared);
        }
    }

    private static ItemData RollSpecialLoot(int level, string runId)
    {
        GearItemData[] gear = Resources.LoadAll<GearItemData>("Items/Gear");
        FlaskItemData[] flasks = FlaskLootPolicy.GameplayCatalog;
        BaseItemData definition = null;
        if (gear.Length > 0 && (flasks.Length == 0 || UnityEngine.Random.value < .75f))
            definition = gear[UnityEngine.Random.Range(0, gear.Length)];
        else if (flasks.Length > 0)
            definition = flasks[UnityEngine.Random.Range(0, flasks.Length)];
        if (definition == null) return null;
        ItemGrade grade = FlaskLootPolicy.SelectGrade(
            Mathf.Lerp(.75f, 1f, UnityEngine.Random.value), level, true, false);
        return new ItemData(definition, level, grade) { originRunId = runId };
    }

    private void OpenTransfer(MapRunTransferObject transferObject)
    {
        if (transferObject == null || transferObject.Used || !TryGetActiveRun(out var account, out var run))
            return;
        var panel = EnsureUi();
        if (panel == null) return;
        panel.ShowTransfer(BuildTransferCandidates(account.Read(), run.runId, account.ContentRegistry),
            itemId => TransferItem(transferObject, itemId));
    }

    private static List<RunTransferPresentation> BuildTransferCandidates(AccountSnapshot state,
        string runId, AccountContentRegistry registry)
    {
        var carried = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in state.inventory) if (!string.IsNullOrEmpty(id)) carried.Add(id);
        foreach (string id in state.weapons) if (!string.IsNullOrEmpty(id)) carried.Add(id);
        foreach (string id in state.gear) if (!string.IsNullOrEmpty(id)) carried.Add(id);
        foreach (string id in state.bags) if (!string.IsNullOrEmpty(id)) carried.Add(id);
        var result = new List<RunTransferPresentation>();
        foreach (ItemSnapshot snapshot in state.items)
        {
            if (snapshot.originRunId != runId || !carried.Contains(snapshot.instanceId)) continue;
            ItemData item = ItemSnapshotCodec.Restore(snapshot, registry);
            result.Add(new RunTransferPresentation
            {
                ItemInstanceId = snapshot.instanceId,
                Name = item.itemName, Description = "아이템 레벨 " + item.level,
                Icon = item.icon, Grade = item.grade, Quantity = item.stackCount,
                IsEquipped = state.weapons.Contains(snapshot.instanceId)
                    || state.gear.Contains(snapshot.instanceId) || state.bags.Contains(snapshot.instanceId)
                    || state.flasks.Contains(snapshot.instanceId)
            });
        }
        return result;
    }

    private RunTransferResult TransferItem(MapRunTransferObject transferObject, string itemId)
    {
        if (transferObject == null || transferObject.Used || !TryGetActiveRun(out var account, out var run))
            return RunTransferResult.Unavailable;
        AccountSnapshot snapshot = account.Read();
        bool eligible = false;
        foreach (var item in snapshot.items)
            if (item.instanceId == itemId && item.originRunId == run.runId) { eligible = true; break; }
        if (!eligible) return RunTransferResult.StaleItem;
        try
        {
            bool committed = new AccountRunSession(account).Transfer(run.runId, transferObject.ObjectId, itemId);
            if (!committed) return RunTransferResult.Unavailable;
            transferObject.MarkUsed();
            return RunTransferResult.Success;
        }
        catch (InvalidOperationException error)
        {
            if (error.Message.Contains("no room")) return RunTransferResult.NoSpace;
            if (error.Message.Contains("not carried") || error.Message.Contains("not acquired"))
                return RunTransferResult.StaleItem;
            Debug.LogError("전송 실패: " + error.Message, this);
            return RunTransferResult.Unavailable;
        }
        catch (IOException error)
        {
            Debug.LogError("전송 저장 실패: " + error.Message, this);
            if (account.ReadRun()?.transferredObjects.Contains(transferObject.ObjectId) == true)
            {
                transferObject.MarkUsed();
                return RunTransferResult.Success;
            }
            return RunTransferResult.SaveFailed;
        }
    }

    private bool TryGetActiveRun(out AccountGameplaySession account, out RunSnapshot run)
    {
        account = AccountGameplaySession.Current;
        run = account?.ReadRun();
        return account != null && run != null && encounter != null && run.runId == encounter.RunId
            && AccountInvariants.IsRunning(run.phase) && WorldSessionState.Phase == WorldPhase.Run;
    }

    private OverburstRunUi EnsureUi()
    {
        if (ui != null) return ui;
        try
        {
            ui = OverburstRunUi.Create();
            SceneManager.MoveGameObjectToScene(ui.gameObject, gameObject.scene);
            return ui;
        }
        catch (Exception error)
        {
            if (!uiWarningLogged) { Debug.LogError("GOAL 4 UI 로드 실패: " + error.Message, this); uiWarningLogged = true; }
            return null;
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < nextHintCheck || WorldSessionState.Phase != WorldPhase.Run)
            return;
        nextHintCheck = Time.unscaledTime + .2f;
        Transform player = PlayerContext.Instance?.CurrentActor?.transform;
        MapDungeonEventNode nearest = null;
        float best = 24f * 24f;
        if (player != null)
            foreach (MapDungeonEventNode node in events)
            {
                if (node == null || node.Phase == MapEventPhase.Dormant
                    || node.Phase == MapEventPhase.Claimed) continue;
                float distance = (player.position - node.transform.position).sqrMagnitude;
                if (distance < best) { best = distance; nearest = node; }
            }
        string title = null, body = null;
        if (nearest != null)
        {
            title = nearest.Kind == MapEventKind.Hunt ? "몬스터 토벌" : "오브젝트 수호";
            if (nearest.Phase == MapEventPhase.Complete) body = "완료 · 상자를 열어 카드 한 장을 선택하세요";
            else if (nearest.Phase == MapEventPhase.Failed) body = "수호 실패 · 이 상자는 열 수 없습니다";
            else if (nearest.Kind == MapEventKind.Hunt)
                body = "소환 " + nearest.Wave + "/2 · 남은 몬스터 " + nearest.LivingCount;
            else body = "남은 시간 " + Mathf.CeilToInt(nearest.GuardRemaining) + "초 · 수호 대상 "
                + Mathf.RoundToInt(nearest.GuardHealthFraction * 100f) + "%";
        }
        if (title == lastHintTitle && body == lastHintBody) return;
        lastHintTitle = title; lastHintBody = body;
        EnsureUi()?.SetEventHint(title, body);
    }

    private void OnDestroy()
    {
        foreach (var node in events) if (node != null) node.ChestRequested -= OpenCards;
        foreach (var transfer in transferObjects) if (transfer != null) transfer.OpenRequested -= OpenTransfer;
        if (ui != null) ui.Close();
    }
}
