using System;
using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.SceneManagement;

// Attached to the persistent debug UI, so the requested loadout survives the scene transition.
public sealed class DungeonDebugEntry : MonoBehaviour
{
    bool pending;
    int level;
    bool giveItems;
    string runId;
    List<ItemData> prepared;
    public string Status { get; private set; } = "하이드아웃에서 레벨 1~100에 입장할 수 있습니다.";
    public bool IsEntering => pending;
    public int LastDropCount { get; private set; }
    public bool TryEnter(int requestedLevel, bool dropEquipment)
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        return false;
#else
        var flow = PersistentSceneFlow.Instance;
        var account = AccountGameplaySession.Current;
        if (pending || flow == null || account == null || flow.IsSwitching || !WorldSessionState.IsHideout)
        { Status = "하이드아웃에서 입장해 주세요."; return false; }
        if (requestedLevel < 1 || requestedLevel > 100)
        { Status = "던전 레벨은 1~100으로 입력해 주세요."; return false; }
        try
        {
            level = requestedLevel; giveItems = dropEquipment; LastDropCount = 0;
            prepared = dropEquipment ? PrepareItems(requestedLevel) : null;
            var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
            var map = new MapInstanceState { mapContentId = account.ContentRegistry.IdFor(definition),
                level = level, grade = ItemGrade.Common, monsterThemeId = MapThemeCatalog.RollThemeId() };
            if (!flow.EnterDebugRun(DiamondDungeonWorld.SceneName, map))
            { prepared = null; Status = flow.RunEntryError ?? "던전 입장을 시작하지 못했습니다."; return false; }
            runId = account.ReadRun()?.runId;
            pending = true; Status = "Lv." + level + " 던전 준비 중";
            return true;
        }
        catch (Exception error) { prepared = null; Status = error.Message; Debug.LogException(error, this); return false; }
#endif
    }
    public static List<ItemData> PrepareItems(int level)
    {
        if (level < 1 || level > 100) throw new ArgumentOutOfRangeException(nameof(level));
        var catalog = WeaponLevelCatalog.Current;
        var weapons = catalog != null ? catalog.Candidates(level) : null;
        if (weapons == null || weapons.Count == 0) throw new InvalidOperationException("해당 레벨 무기 목록이 없습니다.");
        var result = new List<ItemData>(8);
        var weapon = weapons[UnityEngine.Random.Range(0, weapons.Count)];
        result.Add(new ItemData(weapon, level, RollGrade(level)));
        GearKind[] kinds = { GearKind.Helmet, GearKind.Chest, GearKind.Gloves, GearKind.Boots,
            GearKind.Earring, GearKind.Earring, GearKind.Necklace };
        var gear = GearLootPolicy.DefinitionsForLevel(level);
        foreach (var kind in kinds)
        {
            var choices = new List<GearItemData>();
            foreach (var entry in gear) if (entry.kind == kind) choices.Add(entry);
            if (choices.Count == 0) throw new InvalidOperationException("해당 레벨 장비 없음: " + kind);
            result.Add(new ItemData(choices[UnityEngine.Random.Range(0, choices.Count)], level, RollGrade(level)));
        }
        var registry = AccountGameplaySession.Current?.ContentRegistry;
        if (registry == null) throw new InvalidOperationException("계정이 아직 준비되지 않았습니다.");
        foreach (var item in result) registry.IdFor(item.baseData);
        return result;
    }
    static ItemGrade RollGrade(int level) => FlaskLootPolicy.SelectGrade(UnityEngine.Random.value, level, false, false);
    void Update()
    {
        if (!pending) return;
        var flow = PersistentSceneFlow.Instance;
        if (flow == null || flow.IsSwitching) return;
        var run = AccountGameplaySession.Current?.ReadRun();
        if (run == null || run.runId != runId || WorldSessionState.Phase != WorldPhase.Run)
        { pending = false; prepared = null; Status = "던전 입장이 취소되거나 실패했습니다."; return; }
        var player = PlayerContext.Instance?.CurrentActor;
        if (player == null) return;
        pending = false;
        if (giveItems && prepared != null)
        {
            var scene = SceneManager.GetSceneByName(DiamondDungeonWorld.SceneName);
            for (int i = 0; i < prepared.Count; i++)
            {
                var item = prepared[i]; item.originRunId = runId;
                float angle = i * Mathf.PI * 2 / prepared.Count;
                var position = player.transform.position + new Vector3(Mathf.Cos(angle) * 2.2f, .45f, Mathf.Sin(angle) * 2.2f);
                var pickup = WorldItemDropFactory.CreateWorldPickup(item, position,
                    PlayerAccountInventoryService.SharedInventory, player.transform);
                if (pickup == null) continue;
                SceneManager.MoveGameObjectToScene(pickup.gameObject, scene); LastDropCount++;
            }
        }
        prepared = null;
        Status = "Lv." + level + " 입장 완료" + (giveItems ? " · 장비 " + LastDropCount + "/8개 드랍" : "");
    }
}
