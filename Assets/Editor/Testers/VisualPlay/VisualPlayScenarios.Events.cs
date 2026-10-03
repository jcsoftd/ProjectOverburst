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
            yield return c.Wait(3f); yield break;
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
        MapCardKind desired = id == "VT22-07" ? MapCardKind.LootChest : id == "VT22-08" ? MapCardKind.Experience : MapCardKind.Buff;
        // 정식 카드 정책의 결과에서 필요한 종류를 고른다. 카드 렌더러·선택 명령은 그대로 사용한다.
        MapCardOffer offer = null; int choiceIndex = -1;
        for (int seed = 0; seed < 300 && choiceIndex < 0; seed++)
        { offer = MapRunCardPolicy.Roll(new System.Random(seed), 1, PlayerProgression.Current.Level); choiceIndex = Array.FindIndex(offer.Choices, choice => choice.Kind == desired); }
        VisualPlayContext.Require(choiceIndex >= 0, "정식 카드 목록에 필요한 종류가 없어요");
        typeof(MapDungeonEventNode).GetField("offer", InstanceFields).SetValue(node, offer);
        yield return Approach(c, node); node.TryInteract(c.Actor); yield return c.Wait(4f);
        var panel = UnityEngine.Object.FindFirstObjectByType<OverburstRunUi>();
        VisualPlayContext.Require(panel != null && panel.IsOpen, "카드 화면이 열리지 않았어요");
        var cards = panel.GetComponentsInChildren<OverburstRunCardView>();
        VisualPlayContext.Require(cards.Length > choiceIndex, "카드 슬롯이 없어요");
        cards[choiceIndex].Focus(); yield return c.Wait(2f);
        var choose = Field<Button>(cards[choiceIndex], "choose"); choose.onClick.Invoke(); yield return c.Wait(3f);
        if (desired == MapCardKind.LootChest)
        { var chest = UnityEngine.Object.FindFirstObjectByType<MapRewardChest>(); VisualPlayContext.Require(chest != null, "보상 상자가 없어요"); yield return Approach(c, chest); chest.TryInteract(c.Actor); yield return c.Wait(4f); }
    }
    static IEnumerator Parry(VisualPlayContext c)
    {
        yield return Arm(c);
        string id = c.Entry.caseId;
        var definition = VisualPlayContext.Definitions.First(d => d.EnemyId == "CavernMutants_Ursacetus" && d.Grade.GradeType == EnemyGradeType.Elite);
        int count = id == "VT07-07" ? 3 : 1;
        var enemies = new EnemyActor[count];
        for (int i = 0; i < count; i++)
        {
            enemies[i] = c.Spawn(definition, new Vector3((i - (count - 1) * .5f) * .5f, 0, 1.6f));
            c.SingleStrong(enemies[i]);
            VisualPlayContext.Require(enemies[i].AbilityController.TryStart(c.Actor.transform), "적 강공 시작 실패");
        }
        if (id != "VT07-01" && id != "VT07-02")
        {
            yield return c.Wait(.2f);
            VisualPlayContext.Require(c.Melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "패링 강공 시작 실패");
        }
        yield return c.Wait(3f);
        if (id != "VT07-01" && id != "VT07-02")
        {
            var parry = c.Actor.GetComponent<PlayerParryController>();
            VisualPlayContext.Require(parry != null && parry.SuccessCount > 0, "이번 공격에서 패링이 성립하지 않았어요");
            if (id == "VT07-05" || id == "VT07-06") yield return Attack(c, true);
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
        int original = c.Inventory.UnlockedSlotCount;
        c.Inventory.SetUnlockedSlotCount(0);
        var pickup = c.Drop(Definition<WeaponItemData>(c), Vector3.forward * 1.5f + Vector3.up);
        yield return c.Wait(2f); pickup.TryPickup(); yield return c.Wait(2f);
        c.Inventory.SetUnlockedSlotCount(original); pickup.TryPickup(); yield return c.Wait(2f);
    }
}
#endif
