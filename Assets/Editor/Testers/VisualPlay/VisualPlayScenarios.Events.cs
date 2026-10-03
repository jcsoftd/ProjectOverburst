#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.UI;

public static partial class VisualPlayScenarios
{
    static void RegisterEventScenarios()
    {
        Bind(Events, "VT22-01 VT22-02 VT22-04 VT22-05 VT22-06 VT22-07 VT22-08 VT22-09 VT22-10");
        Bind(Parry, "VT07-01 VT07-02 VT07-03 VT07-04 VT07-05 VT07-06 VT07-07");
        Bind(Occlusion, "VT01-07 VT03-12");
        Bind(PickupFeedback, "VT17-09");
    }
    static IEnumerator Events(VisualPlayContext c)
    {
        yield return EnterDungeon(c);
        c.Actor.Health.SetMaxHp(100000, true);
        var director = UnityEngine.Object.FindFirstObjectByType<MapDungeonEventDirector>();
        VisualPlayContext.Require(director != null, "던전 이벤트 관리자가 없어요");
        string id = c.Entry.caseId;
        var naturalTransfers = director.TransferObjects.ToArray();
        if (id == "VT22-09" || id == "VT22-10")
        {
            var transfer = director.TransferObjects.FirstOrDefault();
            VisualPlayContext.Require(transfer != null, "자연 전송 오브젝트가 없어요");
            yield return Approach(c, transfer); transfer.TryInteract(c.Actor); yield return c.Wait(3f);
            var ui = UnityEngine.Object.FindFirstObjectByType<OverburstRunUi>();
            VisualPlayContext.Require(ui != null && ui.IsTransferOpen, "전송 선택창이 열리지 않았어요");
            if (id == "VT22-10")
            {
                var run = AccountGameplaySession.Current.ReadRun();
                var item = c.Inventory.Items.FirstOrDefault(value => value != null && value.originRunId == run.runId);
                VisualPlayContext.Require(item != null && ui.TryStageTransfer(item.runtimeInstanceId), "전송할 런 아이템이 없어요");
                yield return c.Wait(2f); ClickNamed(ui, "Cancel"); yield return c.Wait(1f);
                transfer.TryInteract(c.Actor); yield return c.Wait(1f); ui.TryStageTransfer(item.runtimeInstanceId); yield return c.Wait(2f); ClickNamed(ui, "ConfirmTransfer");
            }
            yield return c.Wait(3f);
            if (id == "VT22-10") yield break;
            c.Detail("자연 전송 확인 뒤 카드 생성 전송 확인"); ClickNamed(ui, "Cancel"); yield return c.Wait(1f);
        }
        var kind = id == "VT22-02" || id == "VT22-04" ? MapEventKind.Guard : MapEventKind.Hunt;
        var node = director.Events.FirstOrDefault(value => value.Kind == kind && value.Phase == MapEventPhase.Dormant);
        VisualPlayContext.Require(node != null, "시작할 이벤트가 없어요: " + kind);
        ActorTeleportUtility.TeleportSafely(c.Actor.transform, node.transform.position, Quaternion.identity);
        yield return c.Wait(1f);
        if (node.Phase == MapEventPhase.Dormant) VisualPlayContext.Require(node.Activate(c.Actor.transform), "이벤트 시작 실패");
        yield return c.Wait(3f);
        if (id == "VT22-04")
        {
            var guard = Field<CombatHealth>(node, "guardHealth"); VisualPlayContext.Require(guard != null, "수호 대상이 없어요");
            guard.TakeDamage(new DamageInfo(1000000, guard.transform.position)); yield return c.Wait(4f); yield break;
        }
        float remaining = 80;
        while (node.Phase == MapEventPhase.Active)
        {
            // 실제 이벤트 몬스터를 차례로 처치한다. 웨이브·완료·드롭은 제품 이벤트가 처리한다.
            var living = Field<System.Collections.Generic.Dictionary<CombatHealth, EnemyThemeTier>>(node, "living");
            // 인근 필드와 이벤트가 같은 생존/스폰 예산을 쓴다. 먼저 일반 전투 몬스터도 실제 처치한다.
            var combatants = living.Keys.Concat(UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None)
                .Where(enemy => enemy.IsLeased && enemy.GetComponentInParent<DiamondDungeonWorld>() != null).Select(enemy => enemy.Health)).Distinct().ToArray();
            foreach (var health in combatants)
                if (health != null && !health.IsDead && c.Actor != null) { health.TakeDamage(new DamageInfo(Mathf.Max(1000000, health.MaxHp * 100f), health.transform.position, c.Actor.gameObject)); yield return c.Wait(.15f); }
            remaining -= .5f;
            if (remaining <= 0) throw new TimeoutException($"이벤트가 완료되지 않았어요 · 남은 적 {node.LivingCount} · 소환 중 {node.IsSpawning} · 웨이브 {node.Wave}");
            yield return c.Wait(.5f);
        }
        VisualPlayContext.Require(node.Phase == MapEventPhase.Complete, "이벤트가 실패했어요"); yield return c.Wait(2f);
        if (id == "VT22-01" || id == "VT22-02") { yield return Approach(c, node); node.TryInteract(c.Actor); yield return c.Wait(4f); yield break; }
        MapCardKind desired = id == "VT22-09" ? MapCardKind.TransferObject : id == "VT22-07" ? MapCardKind.LootChest : id == "VT22-08" ? MapCardKind.Experience : MapCardKind.Buff;
        MapBuffKind wantedBuff = default; if (id == "VT22-06") VisualPlayContext.Require(Enum.TryParse(c.Entry.variant, out wantedBuff), "선택할 지도 버프가 없어요");
        // 정식 카드 정책의 결과에서 필요한 종류를 고른다. 카드 렌더러·선택 명령은 그대로 사용한다.
        MapCardOffer offer = null; int choiceIndex = -1;
        for (int seed = 0; seed < 300 && choiceIndex < 0; seed++)
        { offer = MapRunCardPolicy.Roll(new System.Random(seed), 1, PlayerProgression.Current.Level); choiceIndex = Array.FindIndex(offer.Choices, choice => choice.Kind == desired && (id != "VT22-06" || choice.Buff == wantedBuff)); }
        VisualPlayContext.Require(choiceIndex >= 0, "정식 카드 목록에 필요한 종류가 없어요");
        typeof(MapDungeonEventNode).GetField("offer", InstanceFields).SetValue(node, offer);
        yield return Approach(c, node); node.TryInteract(c.Actor); yield return c.Wait(4f);
        var panel = UnityEngine.Object.FindFirstObjectByType<OverburstRunUi>();
        VisualPlayContext.Require(panel != null && panel.IsOpen, "카드 화면이 열리지 않았어요");
        var cards = panel.GetComponentsInChildren<OverburstRunCardView>();
        VisualPlayContext.Require(cards.Length > choiceIndex, "카드 슬롯이 없어요");
        cards[choiceIndex].Focus(); yield return c.Wait(2f);
        var choose = Field<Button>(cards[choiceIndex], "choose"); choose.onClick.Invoke(); yield return c.Wait(3f);
        if (desired == MapCardKind.TransferObject)
        {
            var transfer = director.TransferObjects.FirstOrDefault(value => !naturalTransfers.Contains(value));
            VisualPlayContext.Require(transfer != null, "카드 보상 전송 오브젝트가 생성되지 않았어요");
            yield return Approach(c, transfer); VisualPlayContext.Require(transfer.TryInteract(c.Actor) != InteractionExecutionResult.Rejected, "카드 생성 전송 상호작용 실패");
            yield return c.Wait(3f); VisualPlayContext.Require(panel.IsTransferOpen, "카드 생성 전송 선택창이 열리지 않았어요");
        }
        if (desired == MapCardKind.LootChest)
        { var chest = UnityEngine.Object.FindFirstObjectByType<MapRewardChest>(); VisualPlayContext.Require(chest != null, "보상 상자가 없어요"); yield return Approach(c, chest); chest.TryInteract(c.Actor); yield return c.Wait(4f); }
    }
    static IEnumerator Parry(VisualPlayContext c)
    {
        yield return Arm(c);
        string id = c.Entry.caseId;
        var definition = VisualPlayContext.Definitions.First(d => d.EnemyId == "CavernMutants_Ursacetus" && d.Grade.GradeType == EnemyGradeType.Elite);
        var parry = c.Actor.GetComponent<PlayerParryController>();
        VisualPlayContext.Require(parry != null, "패링 컨트롤러가 없어요");
        if (id == "VT07-06") { c.Detail("일반 강공"); yield return Attack(c, true); yield return c.Wait(2f); }
        int successBefore = parry.SuccessCount;
        int count = id == "VT07-07" ? 3 : 1;
        var enemies = new EnemyActor[count];
        for (int i = 0; i < count; i++)
        {
            enemies[i] = c.Spawn(definition, c.Actor.transform.position - c.Origin + new Vector3((i - (count - 1) * .5f) * .5f, 0, 1.6f));
            var ability = c.SingleStrong(enemies[i]);
            float distance = Mathf.Lerp(ability.MinimumRange, ability.Range, .5f);
            ActorTeleportUtility.TeleportSafely(enemies[i].transform, c.Actor.transform.position + new Vector3((i - (count - 1) * .5f) * .5f, 0, distance), Quaternion.LookRotation(Vector3.back));
            Physics.SyncTransforms(); yield return c.Wait(.35f);
            int enemyIndex = i;
            yield return c.Until(() => enemies[enemyIndex] != null && enemies[enemyIndex].AbilityController.TryStart(c.Actor.transform), 8f, "적 방향 정렬과 강공 시작");
        }
        if (id != "VT07-01" && id != "VT07-02")
        {
            yield return c.Wait(.2f);
            VisualPlayContext.Require(c.Melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "패링 강공 시작 실패");
        }
        yield return c.Wait(3f);
        if (id != "VT07-01" && id != "VT07-02")
        {
            VisualPlayContext.Require(parry.SuccessCount > successBefore, "이번 공격에서 새 패링이 성립하지 않았어요");
            if (id == "VT07-05" || id == "VT07-06") { c.Detail("패링 성공 후 강화 강공"); yield return Attack(c, true); }
        }
        yield return c.Wait(id == "VT07-04" ? 8f : 3f);
    }
    static IEnumerator Occlusion(VisualPlayContext c)
    {
        yield return c.Arena();
        var camera = Camera.main; VisualPlayContext.Require(camera != null, "카메라가 없어요");
        Vector3 direction = (camera.transform.position - c.Actor.transform.position).normalized;
        var wall = c.Own(GameObject.CreatePrimitive(PrimitiveType.Cube)); wall.name = "VisualPlay Occlusion";
        wall.transform.position = c.Actor.transform.position + direction * 2.5f + Vector3.up;
        wall.transform.localScale = new Vector3(4f, 4f, .3f); wall.transform.rotation = Quaternion.LookRotation(direction);
        yield return c.Wait(3f); c.Input(Vector2.right); yield return c.Wait(2f); c.ReleaseInput();
    }
    static IEnumerator PickupFeedback(VisualPlayContext c)
    {
        yield return c.Arena();
        while (c.Inventory.FindFirstEmptySlot() >= 0)
            VisualPlayContext.Require(c.Inventory.AddItem(c.Make(Definition<WeaponItemData>(c))), "획득 실패용 가방 채우기 실패");
        var pickup = c.Drop(Definition<WeaponItemData>(c), Vector3.forward * 1.5f + Vector3.up);
        c.Detail("가방이 가득 차 획득 실패"); yield return c.Wait(2f);
        VisualPlayContext.Require(!pickup.TryPickup(), "가방이 가득 찼는데 획득이 성공했어요"); yield return c.Wait(2f);
        var occupied = c.Inventory.Items.First(value => value != null);
        VisualPlayContext.Require(c.Inventory.RemoveItem(occupied), "빈 칸 준비 실패");
        c.Detail("빈 칸을 만든 뒤 획득 성공"); VisualPlayContext.Require(pickup.TryPickup(), "빈 칸에서 획득이 실패했어요"); yield return c.Wait(2f);
        var currency = UnityEngine.Object.FindFirstObjectByType<StashCurrencyService>();
        VisualPlayContext.Require(currency != null && currency.GetCurrencyData(CurrencyType.Gold) != null, "골드 정의가 없어요");
        var extra = c.Inventory.Items.First(value => value != null && value.baseData is WeaponItemData);
        VisualPlayContext.Require(c.Inventory.RemoveItem(extra), "골드가 들어갈 빈 칸 준비 실패");
        Func<int> goldAmount = () => c.Inventory.Items.Where(value => value?.baseData is CurrencyItemData data && data.currencyType == CurrencyType.Gold).Sum(value => value.stackCount);
        int before = goldAmount();
        var gold = WorldItemDropFactory.CreateCurrencyWorldPickup(currency.GetCurrencyData(CurrencyType.Gold), 5, c.Origin + Vector3.forward * 1.5f + Vector3.up, c.Inventory);
        VisualPlayContext.Require(gold != null, "실제 골드 획득 객체가 없어요");
        c.Detail("골드 자동 획득·가방 재화 표시");
        try
        {
            yield return c.Wait(2f);
            if (goldAmount() == before) VisualPlayContext.Require(gold.TryPickup(c.Inventory), "골드 획득 실패");
            VisualPlayContext.Require(goldAmount() == before + 5, "골드 획득이 가방 재화에 반영되지 않았어요"); yield return c.Wait(2f);
        }
        finally { if (gold != null && gold.gameObject.activeSelf) UnityEngine.Object.Destroy(gold.gameObject); }
    }
}
#endif
