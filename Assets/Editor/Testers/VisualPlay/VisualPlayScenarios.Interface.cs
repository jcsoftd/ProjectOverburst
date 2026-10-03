#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Overburst.DebugTools;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static partial class VisualPlayScenarios
{
    const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static void RegisterInterfaceScenarios()
    {
        Bind(Interaction, "VT04-01 VT04-02 VT04-03 VT04-04 VT04-05 VT04-06");
        Bind(Inventory, "VT09-10 VT18-01 VT18-02 VT18-03 VT18-04 VT18-06 VT18-07 VT18-08 VT18-09 VT18-11 VT18-12 VT18-13 VT18-15 VT18-16");
        Bind(Flasks, "VT19-01 VT19-02 VT19-03 VT19-04 VT19-05 VT19-06 VT19-07 VT19-08 VT19-09 VT19-10");
        Bind(Shop, "VT20-01 VT20-02 VT20-03 VT20-04 VT20-05 VT20-08 VT20-09 VT20-10 VT20-11");
        Bind(Dungeon, "VT21-01 VT21-05 VT21-06 VT21-07 VT21-08 VT21-09 VT21-10 VT23-01 VT23-02 VT23-03 VT23-04 VT23-05 VT23-06");
        RegisterEventScenarios();
    }
    static T Field<T>(object target, string name)
    {
        var field = target?.GetType().GetField(name, InstanceFields);
        VisualPlayContext.Require(field != null, "장면 연결이 바뀌었어요: " + name);
        return (T)field.GetValue(target);
    }
    static void ClickField(object target, string field) { var button = Field<Button>(target, field); VisualPlayContext.Require(button != null && button.isActiveAndEnabled && button.interactable, "버튼을 누를 수 없어요: " + field); button.onClick.Invoke(); }
    static void ClickNamed(Component root, string name)
    {
        var button = root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);
        VisualPlayContext.Require(button != null && button.isActiveAndEnabled && button.interactable, "화면 버튼이 없어요: " + name);
        button.onClick.Invoke();
    }
    static IInteractable[] Targets() => UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<IInteractable>().ToArray();
    static IEnumerator Approach(VisualPlayContext c, IInteractable target)
    {
        VisualPlayContext.Require(target != null, "씬에 상호작용 대상이 없어요");
        Vector3 point = target.InteractionTransform.position + Vector3.back * Mathf.Min(1.8f, target.InteractionRange * .65f);
        ActorTeleportUtility.TeleportSafely(c.Actor.transform, point, Quaternion.identity);
        c.Aim(target.InteractionTransform.position); yield return c.Wait(2f);
    }
    static IEnumerator Interaction(VisualPlayContext c)
    {
        string id = c.Entry.caseId;
        var targets = Targets();
        IInteractable target = id == "VT04-02" ? targets.FirstOrDefault(t => t is StashInteractable)
            : id == "VT04-03" ? targets.FirstOrDefault(t => t is MapDungeonPortal)
            : id == "VT04-04" ? targets.FirstOrDefault(t => t.GetType().Name.Contains("Training"))
            : targets.FirstOrDefault(t => t is GeneralGoodsMerchantInteractable);
        if (id == "VT04-04" && target == null) throw new VisualPlayUnavailable("현재 씬에는 독립 훈련 상호작용 안내 대상이 없어요");
        yield return Approach(c, target);
        if (id == "VT04-05" || id == "VT04-06")
        {
            c.Input(Vector2.down); yield return c.Wait(2f); c.ReleaseInput();
            if (id == "VT04-06") foreach (var other in targets.Take(3)) { yield return Approach(c, other); yield return c.Wait(1f); }
            yield break;
        }
        var result = target.TryInteract(c.Actor);
        VisualPlayContext.Require(result != InteractionExecutionResult.Rejected, "상호작용이 거부됐어요");
        yield return c.Wait(3f);
    }
    static SlotUI ItemSlot(ItemData item) => UnityEngine.Object.FindObjectsByType<SlotUI>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(slot => slot.isActiveAndEnabled && slot.DisplayItem != null && slot.DisplayItem.runtimeInstanceId == item.runtimeInstanceId && !slot.IsWeaponSlot && !slot.IsBagSlot);
    static void Hover(SlotUI slot)
    {
        VisualPlayContext.Require(slot != null, "아이템 슬롯을 찾지 못했어요");
        var data = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, slot.transform.position) };
        ExecuteEvents.Execute(slot.gameObject, data, ExecuteEvents.pointerEnterHandler);
    }
    static SlotUI EmptyInventorySlot() => UnityEngine.Object.FindObjectsByType<SlotUI>(FindObjectsSortMode.None)
        .FirstOrDefault(slot => slot.isActiveAndEnabled && slot.OwnerBridge is InventorySlotBridge && !slot.IsLocked && !slot.IsBagSlot && !slot.IsWeaponSlot && slot.DisplayItem == null);
    static IEnumerator DragItem(VisualPlayContext c, SlotUI source, SlotUI target, bool commit)
    {
        VisualPlayContext.Require(source != null && target != null, "드래그할 슬롯이 없어요");
        var drag = source.GetComponent<DragSlot>(); var drop = target.GetComponent<DropSlot>();
        VisualPlayContext.Require(drag != null && drop != null, "슬롯 드래그 연결이 없어요");
        var from = RectTransformUtility.WorldToScreenPoint(null, source.transform.position);
        var to = RectTransformUtility.WorldToScreenPoint(null, target.transform.position);
        var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = from };
        drag.OnBeginDrag(data);
        try
        {
            VisualPlayContext.Require(DragSlot.IsDragging, "드래그를 시작하지 못했어요");
            for (int i = 1; i <= 12; i++) { data.position = Vector2.Lerp(from, to, i / 12f); drag.OnDrag(data); yield return c.Wait(.08f); }
            drop.OnPointerEnter(data); yield return c.Wait(2f);
            if (commit) { drop.OnDrop(data); drag.OnEndDrag(data); } else DragSlot.ClearDragState();
            yield return c.Wait(2f);
        }
        finally { DragSlot.ClearDragState(); }
    }
    static IEnumerator Bags(VisualPlayContext c, OverburstGameUI gameUi)
    {
        var definitions = VisualPlayContext.Items.OfType<BagItemData>().OrderBy(data => data.level).ToArray();
        if (definitions.Length < 2) throw new VisualPlayUnavailable("비교할 정식 가방이 부족해요");
        var small = c.Make(definitions.First()); var large = c.Make(definitions.Last(), ItemGrade.Legendary);
        VisualPlayContext.Require(c.Inventory.AddItem(small) && c.Inventory.AddItem(large), "가방 준비 실패");
        yield return c.Wait(.5f);
        var bridge = (InventorySlotBridge)ItemSlot(large).OwnerBridge;
        VisualPlayContext.Require(bridge.EquipBagFromContextMenu(ItemSlot(large)), "큰 가방 장착 실패");
        yield return c.Wait(2f);
        if (c.Entry.caseId == "VT18-12")
        {
            var bagSlot = UnityEngine.Object.FindObjectsByType<SlotUI>(FindObjectsSortMode.None).First(slot => slot.isActiveAndEnabled && slot.IsBagSlot);
            yield return DragItem(c, ItemSlot(small), bagSlot, false);
        }
        if (c.Entry.caseId == "VT18-13")
        {
            while (c.Inventory.FindFirstEmptySlot() >= 0)
                VisualPlayContext.Require(c.Inventory.AddItem(c.Make(Definition<WeaponItemData>(c))), "초과 칸 준비 실패");
        }
        VisualPlayContext.Require(bridge.EquipBagFromContextMenu(ItemSlot(small)), "작은 가방 교체 실패");
        yield return c.Wait(3f);
        if (c.Entry.caseId == "VT18-13")
        {
            VisualPlayContext.Require(c.Inventory.IsOverCapacity, "가방 축소에서 초과 칸을 만들지 못했어요");
            var pickup = c.Drop(Definition<WeaponItemData>(c), Vector3.forward * 1.5f + Vector3.up);
            yield return c.Wait(1f); pickup.TryPickup(); yield return c.Wait(2f);
        }
        VisualPlayContext.Require(bridge.EquipBagFromContextMenu(ItemSlot(large)), "큰 가방 복구 실패");
        gameUi.Refresh(); yield return c.Wait(3f);
    }
    static IEnumerator Inventory(VisualPlayContext c)
    {
        var gameUi = UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>();
        VisualPlayContext.Require(gameUi != null && gameUi.inventory != null, "게임 인벤토리 UI가 없어요");
        string id = c.Entry.caseId;
        BaseItemData definition = id == "VT18-08" ? VisualPlayContext.Items.OfType<ConsumableItemData>().First(data => !(data is FlaskItemData))
            : id == "VT09-10" || id == "VT18-16" ? (BaseItemData)VisualPlayContext.Items.OfType<ElementGemItemData>().Last() : Definition<WeaponItemData>(c);
        var item = c.Make(definition, ItemGrade.Legendary, id == "VT18-08" ? 8 : 1); VisualPlayContext.Require(c.Inventory.AddItem(item), "시각 확인 아이템 지급 실패");
        gameUi.inventory.SetVisible(true); gameUi.ToggleEquipment(); yield return c.Wait(2f);
        if (id == "VT18-15") { yield return TooltipEdges(c, definition); yield break; }
        if (id == "VT18-01" || id == "VT18-04")
        {
            if (id == "VT18-04")
            {
                var preview = UnityEngine.Object.FindFirstObjectByType<OverburstUICharacterPreview>();
                VisualPlayContext.Require(preview != null, "캐릭터 미리보기가 없어요");
                var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, delta = new Vector2(12, 0) };
                preview.OnBeginDrag(data);
                try { for (int i = 0; i < 15; i++) { preview.OnDrag(data); yield return c.Wait(.08f); } }
                finally { preview.OnEndDrag(data); }
                yield return c.Wait(2f);
            }
            gameUi.CloseEquipment(); gameUi.inventory.SetVisible(false); yield return c.Wait(2f);
            gameUi.inventory.SetVisible(true); gameUi.ToggleEquipment(); yield return c.Wait(2f);
            if (id == "VT18-04")
            {
                gameUi.CloseEquipment(); gameUi.inventory.SetVisible(false);
                c.Detail("실제 던전 입장·복귀 후 미리보기 다시 열기"); yield return EnterDungeon(c); yield return ReturnHideout(c);
                gameUi = UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>();
                VisualPlayContext.Require(gameUi != null, "복귀한 게임 UI가 없어요");
                gameUi.inventory.SetVisible(true); gameUi.ToggleEquipment(); yield return c.Wait(3f);
                VisualPlayContext.Require(UnityEngine.Object.FindFirstObjectByType<OverburstUICharacterPreview>() != null, "복귀 후 미리보기가 없어요");
            }
            yield break;
        }
        if (id == "VT18-02" || id == "VT18-03" || id == "VT18-16")
        {
            if (definition is WeaponItemData weapon) c.Equip(weapon); else c.Gem(WeaponElement.Fire);
            gameUi.Refresh(); yield return c.Wait(2f);
            if (id == "VT18-02")
            {
                c.Detail("다른 무기 교체");
                var other = VisualPlayContext.Items.OfType<WeaponItemData>().FirstOrDefault(value => value != definition && WeaponContentPolicy.IsAllowedItemData(value));
                VisualPlayContext.Require(other != null, "교체할 다른 무기가 없어요"); c.Equip(other); gameUi.Refresh(); yield return c.Wait(2f);
                c.Detail("활성 무기 해제"); VisualPlayContext.Require(c.Actor.Equipment.ClearWeaponSlot(c.Actor.Equipment.ActiveWeaponSlotIndex), "무기 해제 실패");
                gameUi.Refresh(); VisualPlayContext.Require(c.Actor.Equipment.CurrentWeaponItem == null, "무기 해제가 표시되지 않았어요"); yield return c.Wait(2f); yield break;
            }
            if (definition is ElementGemItemData) { ElementGemEquipmentService.UnequipToInventory(); gameUi.Refresh(); }
        }
        if (id == "VT18-09")
        {
            var consumable = VisualPlayContext.Items.OfType<ConsumableItemData>().First(data => !(data is FlaskItemData) && data.consumableType == ConsumableType.HealHp);
            var potion = c.Make(consumable, ItemGrade.Common, 5); VisualPlayContext.Require(c.Inventory.AddItem(potion), "퀵슬롯 아이템 준비 실패");
            var quick = UnityEngine.Object.FindFirstObjectByType<InventoryQuickSlotBindingController>();
            VisualPlayContext.Require(quick != null, "퀵슬롯 연결이 없어요");
            var replacement = c.Make(VisualPlayContext.Items.OfType<ConsumableItemData>().First(data => !(data is FlaskItemData) && data.consumableType == ConsumableType.SpeedBoost), ItemGrade.Common, 5);
            VisualPlayContext.Require(c.Inventory.AddItem(replacement), "퀵슬롯 교체 전 아이템 준비 실패");
            for (int key = 1; key <= 10; key++) { VisualPlayContext.Require(quick.Bind(key, replacement), "퀵슬롯 지정 실패"); yield return c.Wait(.5f); }
            c.Detail("1번 퀵슬롯을 회복 물약으로 교체"); VisualPlayContext.Require(quick.Bind(1, potion), "퀵슬롯 교체 실패"); yield return c.Wait(2f);
            c.Actor.Health.TakeDamage(new DamageInfo(c.Actor.Health.MaxHp * .5f, c.Actor.transform.position));
            int countBefore = c.Inventory.FindFirstItemByBaseData(consumable).stackCount;
            var use = UnityEngine.Object.FindFirstObjectByType<InventoryItemActionService>();
            VisualPlayContext.Require(use != null && use.UseQuickSlot(1), "퀵슬롯 사용 실패");
            VisualPlayContext.Require(c.Inventory.FindFirstItemByBaseData(consumable).stackCount < countBefore, "퀵슬롯 사용이 수량에 반영되지 않았어요");
            c.Detail("퀵슬롯 사용 후 수량·쿨다운 표시"); yield return c.Wait(3f);
            for (int key = 1; key <= 10; key++) quick.Clear(key); yield return c.Wait(2f);
            yield break;
        }
        if (id == "VT18-11" || id == "VT18-12" || id == "VT18-13")
        {
            yield return Bags(c, gameUi); yield break;
        }
        var slot = ItemSlot(item); Hover(slot); yield return c.Wait(3f);
        if (id == "VT18-06" || id == "VT18-08")
        {
            var empty = EmptyInventorySlot();
            yield return DragItem(c, slot, empty, true);
            if (id == "VT18-08")
            {
                var other = c.Make(definition, ItemGrade.Legendary, 3); VisualPlayContext.Require(c.Inventory.AddItem(other), "스택 준비 실패");
                c.Inventory.ConsumeItem(c.Inventory.FindFirstItemByBaseData(definition), 2); yield return c.Wait(2f);
            }
            else
            {
                var other = c.Make(definition, ItemGrade.Rare); VisualPlayContext.Require(c.Inventory.AddItem(other), "교환 아이템 준비 실패");
                yield return c.Wait(.5f); yield return DragItem(c, ItemSlot(item), ItemSlot(other), true);
                yield return DragItem(c, ItemSlot(item), EmptyInventorySlot(), false);
            }
        }
        if (id == "VT18-07")
        {
            var bridge = (InventorySlotBridge)slot.OwnerBridge;
            bridge.SortInventory(ItemSortMode.Grade, ItemSortDirection.Descending); yield return c.Wait(2f);
            bridge.SortInventory(ItemSortMode.Grade, ItemSortDirection.Ascending); yield return c.Wait(2f);
            slot = ItemSlot(item);
            slot.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Right, position = RectTransformUtility.WorldToScreenPoint(null, slot.transform.position) });
            yield return c.Wait(3f);
        }
    }
    static IEnumerator Flasks(VisualPlayContext c)
    {
        string id = c.Entry.caseId;
        var flasks = PlayerFlaskController.Current;
        VisualPlayContext.Require(flasks != null, "물약 컨트롤러가 없어요");
        var definitions = VisualPlayContext.Items.OfType<FlaskItemData>().Where(data => data.AvailableForDropsAndShop)
            .Where(data => id == "VT19-01" || id == "VT19-02" || id == "VT19-04" || data.kind != FlaskKind.Life)
            .GroupBy(data => data.kind).Select(group => group.First()).ToArray();
        if (id == "VT19-05" || id == "VT19-06" || id == "VT19-07")
        {
            var potion = VisualPlayContext.Items.OfType<ConsumableItemData>().First(data => !(data is FlaskItemData) && (id == "VT19-05" ? data.consumableType == ConsumableType.HealHp : data.consumableType == ConsumableType.SpeedBoost));
            var item = c.Make(potion, ItemGrade.Common, 5); c.Inventory.AddItem(item);
            var quick = UnityEngine.Object.FindFirstObjectByType<InventoryQuickSlotBindingController>(); quick.Bind(1, item);
            c.Actor.Health.TakeDamage(new DamageInfo(c.Actor.Health.MaxHp * .5f, c.Actor.transform.position));
            var use = UnityEngine.Object.FindFirstObjectByType<InventoryItemActionService>();
            VisualPlayContext.Require(use.UseQuickSlot(1), "소모품 사용 실패"); yield return c.Wait(3f);
            if (id == "VT19-07") { use.UseQuickSlot(1); yield return c.Wait(3f); }
            yield break;
        }
        int amount = id == "VT19-01" || id == "VT19-08" ? Math.Min(3, definitions.Length) : 1;
        for (int i = 0; i < amount; i++)
        {
            var definition = id == "VT19-02" || id == "VT19-04" ? definitions.First(data => data.kind == FlaskKind.Life)
                : id == "VT19-03" ? UnityEditor.AssetDatabase.LoadAssetAtPath<FlaskItemData>(UnityEditor.AssetDatabase.GUIDToAssetPath(c.Entry.variant)) ?? definitions[i] : definitions[i];
            var item = c.Make(definition, ItemGrade.Legendary); c.Inventory.AddItem(item);
            VisualPlayContext.Require(flasks.TryEquip(i, item, out string reason), reason);
        }
        yield return c.Wait(2f);
        if (id == "VT19-10" && c.Entry.variant == "사망")
        {
            yield return EnterDungeon(c); flasks = PlayerFlaskController.Current;
            VisualPlayContext.Require(flasks != null, "던전의 물약 컨트롤러가 없어요");
        }
        if (id == "VT19-01") { for (int i = 0; i < amount; i++) { flasks.TryUnequip(i, out _); yield return c.Wait(1f); } yield break; }
        c.Actor.Health.TakeDamage(new DamageInfo(c.Actor.Health.MaxHp * .5f, c.Actor.transform.position));
        for (int i = 0; i < amount; i++) VisualPlayContext.Require(flasks.TryUse(i, out string reason), reason);
        yield return c.Wait(3f);
        if (id == "VT19-04") { flasks.TryUse(0, out string reason); c.Detail(reason); yield return c.Until(() => flasks.CooldownRemaining(0) <= 0, 120, "쿨다운 종료"); }
        if (id == "VT19-09") yield return c.Until(() => flasks.Remaining(0) <= 0, 120, "버프 자연 만료");
        if (id == "VT19-10")
        {
            c.Detail("물약 사용 후 " + c.Entry.variant);
            if (c.Entry.variant == "장비 교체")
            {
                c.Equip(Definition<WeaponItemData>(c)); yield return c.Wait(2f);
                var other = VisualPlayContext.Items.OfType<WeaponItemData>().First(value => value != Definition<WeaponItemData>(c) && WeaponContentPolicy.IsAllowedItemData(value));
                c.Equip(other); yield return c.Wait(3f);
            }
            else if (c.Entry.variant == "씬 왕복") { yield return EnterDungeon(c); yield return ReturnHideout(c); yield return c.Wait(3f); }
            else
            {
                c.Actor.Health.TakeDamage(new DamageInfo(c.Actor.Health.MaxHp * 100f, c.Actor.transform.position));
                yield return c.Until(() => WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching, 45, "물약 사용 후 사망 귀환"); yield return c.Wait(3f);
            }
        }
    }
    static IEnumerator Shop(VisualPlayContext c)
    {
        string id = c.Entry.caseId;
        if (id == "VT20-10")
        {
            var gameUi = UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>();
            VisualPlayContext.Require(gameUi != null && gameUi.stash != null && gameUi.inventory != null, "창고·가방 UI가 없어요");
            var stash = gameUi.stash;
            var item = c.Make(Definition<WeaponItemData>(c), ItemGrade.Legendary); VisualPlayContext.Require(c.Inventory.AddItem(item), "창고 이동 아이템 준비 실패");
            var storage = Targets().FirstOrDefault(target => target is StashInteractable);
            yield return Approach(c, storage);
            VisualPlayContext.Require(storage.TryInteract(c.Actor) != InteractionExecutionResult.Rejected && stash.IsOpen, "창고 상호작용이 열리지 않았어요");
            gameUi.inventory.SetVisible(true); yield return c.Wait(2f);
            var stashBridge = Field<StashSlotBridge>(stash, "slotBridge");
            var inventoryBridge = Field<InventorySlotBridge>(gameUi.inventory, "slotBridge");
            stashBridge.SwitchTab(0); stashBridge.RefreshSlots(); inventoryBridge.RefreshSlotsWithOwnershipCheck();
            var stashSlots = Field<SlotUI[]>(stashBridge, "stashSlots");
            var inventorySlots = Field<SlotUI[]>(inventoryBridge, "inventorySlots");
            var target = stashSlots.FirstOrDefault(slot => slot != null && slot.isActiveAndEnabled && slot.DisplayItem == null && !slot.IsLocked);
            var source = ItemSlot(item);
            c.Detail("창고로 아이템 이동");
            VisualPlayContext.Require(source != null, $"가방의 출발 슬롯이 표시되지 않았어요 (가방 표시 {gameUi.inventory.IsVisible}, 활성 슬롯 {inventorySlots.Count(slot => slot != null && slot.isActiveAndEnabled)}, 데이터 위치 {c.Inventory.FindFirstMatchingItemIndex(item)}, 브리지 일치 {ReferenceEquals(Field<PlayerInventory>(inventoryBridge, "inventory"), c.Inventory)})");
            VisualPlayContext.Require(target != null, "창고의 빈 슬롯이 표시되지 않았어요");
            yield return DragItem(c, source, target, true);
            VisualPlayContext.Require(c.Inventory.FindFirstMatchingItemIndex(item) < 0, "창고 이동이 적용되지 않았어요");
            stashBridge.RefreshSlots(); inventoryBridge.RefreshSlotsWithOwnershipCheck();
            source = stashSlots.FirstOrDefault(slot => slot != null && slot.DisplayItem?.runtimeInstanceId == item.runtimeInstanceId);
            target = EmptyInventorySlot();
            c.Detail("창고 아이템을 가방으로 회수"); yield return DragItem(c, source, target, true);
            VisualPlayContext.Require(c.Inventory.FindFirstMatchingItemIndex(item) >= 0, "창고 아이템 회수가 적용되지 않았어요"); yield break;
        }
        var merchant = Targets().FirstOrDefault(target => target is GeneralGoodsMerchantInteractable); yield return Approach(c, merchant);
        VisualPlayContext.Require(merchant.TryInteract(c.Actor) != InteractionExecutionResult.Rejected, "상점 열기 실패");
        var shop = UnityEngine.Object.FindFirstObjectByType<ShopUI>(); var trade = UnityEngine.Object.FindFirstObjectByType<MerchantTradeService>();
        VisualPlayContext.Require(shop != null && trade != null && shop.IsOpen, "상점 거래 화면이 없어요"); yield return c.Wait(2f);
        if (id != "VT20-05")
        {
            var currency = UnityEngine.Object.FindFirstObjectByType<StashCurrencyService>();
            VisualPlayContext.Require(currency != null && currency.TryAddCurrency(CurrencyType.Gold, 100000), "테스트 거래 골드 준비 실패");
            shop.Refresh();
        }
        if (id == "VT20-01")
        { foreach (string button in new[] { "questTabButton", "firstSpecialtyTabButton", "secondSpecialtyTabButton", "tradeTabButton" }) { ClickField(shop, button); yield return c.Wait(2f); } yield break; }
        if (id == "VT20-09") { trade.ReloadCurrentMerchantInventory(); shop.Refresh(); yield return c.Wait(2f); shop.Close(); yield return c.Wait(1f); merchant.TryInteract(c.Actor); yield break; }
        if (id == "VT20-05")
        {
            var currency = UnityEngine.Object.FindFirstObjectByType<StashCurrencyService>();
            VisualPlayContext.Require(trade.ToggleMerchantOffer(0, out string message), message);
            c.Detail("플레이어 골드 부족"); shop.Refresh(); ClickField(shop, "confirmButton"); yield return c.Wait(3f);
            ClickField(shop, "failurePopupConfirmButton"); trade.Session.Clear();
            var item = c.Make(Definition<WeaponItemData>(c), ItemGrade.Legendary); VisualPlayContext.Require(c.Inventory.AddItem(item), "판매 아이템 준비 실패");
            VisualPlayContext.Require(trade.MerchantInventory.TrySpendCurrency(CurrencyType.Gold, trade.GetMerchantGoldAmount()), "상인 잔액 준비 실패");
            VisualPlayContext.Require(trade.TogglePlayerOffer(c.Inventory.FindFirstMatchingItemIndex(item), out message), message);
            c.Detail("상인 골드 부족"); shop.Refresh(); ClickField(shop, "confirmButton"); yield return c.Wait(3f);
            ClickField(shop, "failurePopupConfirmButton"); trade.Session.Clear();
            VisualPlayContext.Require(currency != null && currency.TryAddCurrency(CurrencyType.Gold, 100000), "실패 조건 골드 준비 실패");
            while (c.Inventory.FindFirstEmptySlot() >= 0)
                VisualPlayContext.Require(c.Inventory.AddItem(c.Make(Definition<WeaponItemData>(c))), "인벤토리 공간 부족 조건 준비 실패");
            int stockIndex = Enumerable.Range(0, trade.MerchantInventory.Capacity).Where(index => trade.MerchantInventory.GetItemAt(index)?.baseData != null && !(trade.MerchantInventory.GetItemAt(index).baseData is FlaskItemData)).DefaultIfEmpty(-1).First();
            if (stockIndex < 0)
            {
                // 물약은 빈 물약 칸으로 바로 들어갈 수 있으므로 일반 소모품을 정식 거래 API에 준비한다.
                var regular = c.Make(VisualPlayContext.Items.OfType<ConsumableItemData>().First(data => !(data is FlaskItemData)));
                if (!trade.MerchantInventory.AddItem(regular))
                {
                    VisualPlayContext.Require(trade.MerchantInventory.RemoveItemAt(0, trade.MerchantInventory.GetItemAt(0)), "공간 부족 거래 조건 준비 실패");
                    VisualPlayContext.Require(trade.MerchantInventory.AddItem(regular), "일반 소모품 거래 조건 준비 실패");
                }
                stockIndex = Enumerable.Range(0, trade.MerchantInventory.Capacity).First(index => trade.MerchantInventory.GetItemAt(index)?.runtimeInstanceId == regular.runtimeInstanceId);
            }
            VisualPlayContext.Require(trade.ToggleMerchantOffer(stockIndex, out message), message);
            c.Detail("인벤토리 공간 부족"); shop.Refresh(); ClickField(shop, "confirmButton"); yield return c.Wait(3f);
            ClickField(shop, "failurePopupConfirmButton"); yield break;
        }
        if (id == "VT20-03")
        { var item = c.Make(Definition<WeaponItemData>(c), ItemGrade.Rare); c.Inventory.AddItem(item); VisualPlayContext.Require(trade.TogglePlayerOffer(c.Inventory.FindFirstMatchingItemIndex(item), out string message), message); }
        else VisualPlayContext.Require(trade.ToggleMerchantOffer(0, out string message), message);
        shop.Refresh(); yield return c.Wait(2f);
        if (id == "VT20-04") { ClickField(shop, "clearButton"); yield return c.Wait(2f); yield break; }
        if (id == "VT20-08")
        {
            for (int i = 0; i < 3; i++) { MerchantReputationService.AddReputationExperience(trade.CurrentMerchant, MerchantReputationService.GetRequiredExperience()); shop.Refresh(); yield return c.Wait(3f); }
            yield break;
        }
        if (id == "VT20-11")
        {
            var currency = UnityEngine.Object.FindFirstObjectByType<StashCurrencyService>();
            VisualPlayContext.Require(currency.TryAddCurrency(CurrencyType.MapFragment, 5), "지도조각 준비 실패"); shop.Refresh(); yield return c.Wait(3f);
            shop.Close();
            var storage = Targets().FirstOrDefault(target => target is StashInteractable);
            yield return Approach(c, storage);
            VisualPlayContext.Require(storage.TryInteract(c.Actor) != InteractionExecutionResult.Rejected, "재화 비교 창고 열기 실패");
            yield return c.Wait(3f); yield break;
        }
        ClickField(shop, "confirmButton"); yield return c.Wait(3f);
    }
    static IEnumerator ReturnHideout(VisualPlayContext c)
    {
        var driver = PersistentSceneFlow.Instance?.GetComponent<RunLifetimeDriver>(); VisualPlayContext.Require(driver != null, "귀환 관리자가 없어요");
        driver.RequestAbandon(); yield return c.Until(() => WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching, 45, "하이드아웃 귀환"); yield return c.Wait(2f);
    }
    static ItemData PrepareMap(VisualPlayContext c, int level, ItemGrade grade, string theme = null)
    {
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01"); var account = AccountGameplaySession.Current;
        VisualPlayContext.Require(definition != null && account != null, "지도 정의와 테스트 계정이 필요해요");
        var item = new ItemData(definition, level, grade);
        item.mapState = new MapInstanceState { mapContentId = Field<AccountContentRegistry>(account, "registry").IdFor(definition), level = level, grade = grade,
            monsterThemeId = theme ?? MapThemeCatalog.RollThemeId(), options = MapOptionPolicy.Roll(grade) };
        VisualPlayContext.Require(c.Inventory.AddItem(item), "지도 아이템 준비 실패"); return item;
    }
    static IEnumerator PortalEntry(VisualPlayContext c, bool free, ItemData map = null, bool fail = false, bool cancelFirst = false)
    {
        var portal = Targets().OfType<MapDungeonPortal>().FirstOrDefault(); yield return Approach(c, portal);
        VisualPlayContext.Require(portal.TryInteract(c.Actor) != InteractionExecutionResult.Rejected, "포탈 선택창 열기 실패"); yield return c.Wait(2f);
        var panel = UnityEngine.Object.FindFirstObjectByType<MapDungeonPortalPanel>(); VisualPlayContext.Require(panel != null, "지도 선택창이 없어요");
        if (cancelFirst)
        {
            c.Detail("입장 선택 취소"); ClickNamed(panel, "Close"); yield return c.Wait(1f);
            VisualPlayContext.Require(WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching, "취소 후 씬이 전환됐어요");
            portal.TryInteract(c.Actor); yield return c.Wait(1f); panel = UnityEngine.Object.FindFirstObjectByType<MapDungeonPortalPanel>();
        }
        if (free) ClickNamed(panel, "FreeLevelOne");
        else
        {
            VisualPlayContext.Require(map != null, "선택할 지도 아이템이 없어요");
            var maps = Field<System.Collections.Generic.List<ItemData>>(panel, "maps"); int index = maps.FindIndex(value => value.runtimeInstanceId == map.runtimeInstanceId);
            VisualPlayContext.Require(index >= 0 && index < 7, "선택할 지도 행이 첫 페이지에 없어요"); ClickNamed(panel, "MapRow_" + index);
        }
        c.Detail(free ? "무료 입장 선택" : "소유 지도 선택과 상세 정보"); yield return c.Wait(3f);
        if (fail)
        {
            VisualPlayContext.Require(c.Inventory.RemoveItem(map), "입장 실패 조건 준비 실패");
            ClickNamed(panel, "Enter"); yield return c.Wait(3f);
            VisualPlayContext.Require(WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching && !string.IsNullOrWhiteSpace(Field<TMPro.TMP_Text>(panel, "status").text), "입장 실패 안내가 표시되지 않았어요");
            ClickNamed(panel, "Close"); yield return c.Wait(1f); yield break;
        }
        ClickNamed(panel, "Enter"); yield return c.Until(() => !PersistentSceneFlow.Instance.IsSwitching && WorldSessionState.Phase == WorldPhase.Run, 60, "포탈 버튼 입장");
        if (map != null)
        {
            var active = AccountGameplaySession.Current.ReadRun().map;
            VisualPlayContext.Require(active.level == map.mapState.level && active.grade == map.mapState.grade && active.monsterThemeId == map.mapState.monsterThemeId, "선택 지도의 레벨·등급·테마가 런과 달라요");
        }
        yield return c.Wait(3f);
    }
    static IEnumerator TooltipEdges(VisualPlayContext c, BaseItemData original)
    {
        // 표시 전용 복제다. 정식 아이템·저장·인벤토리에는 등록하지 않는다.
        var definition = c.Own(UnityEngine.Object.Instantiate(original));
        definition.itemName = "매우 긴 이름을 가진 시각 확인용 전설 장비 — 이름 줄바꿈과 화면 가장자리 확인";
        definition.description = string.Join("\n", Enumerable.Repeat("긴 설명과 옵션 영역의 줄바꿈을 확인합니다.", 12));
        var item = c.Make(definition, ItemGrade.Legendary); var manager = TooltipManager.Instance;
        var tooltip = UnityEngine.Object.FindFirstObjectByType<OverburstGameTooltip>(FindObjectsInactive.Include);
        VisualPlayContext.Require(manager != null && tooltip != null, "실제 게임 툴팁 연결이 없어요");
        var slot = UnityEngine.Object.FindObjectsByType<SlotUI>(FindObjectsSortMode.None).FirstOrDefault(value => value.isActiveAndEnabled && value.DisplayItem != null);
        if (slot != null) Hover(slot); manager.ShowTooltip(item);
        foreach (Vector2 corner in new[] { new Vector2(2, 2), new Vector2(Screen.width - 2, 2), new Vector2(2, Screen.height - 2), new Vector2(Screen.width - 2, Screen.height - 2) })
        {
            c.Detail("긴 툴팁 · 화면 모서리 " + corner);
            Canvas.WillRenderCanvases place = () => tooltip.Place(corner);
            Canvas.willRenderCanvases += place;
            try { tooltip.Place(corner); yield return c.Wait(3f); }
            finally { Canvas.willRenderCanvases -= place; }
        }
        // 현재 제품 화면의 스크롤이 있을 때 실제 슬롯 Hover를 유지한 채 끝까지 이동한다.
        var scroll = UnityEngine.Object.FindObjectsByType<ScrollRect>(FindObjectsSortMode.None).FirstOrDefault(value => value.isActiveAndEnabled && value.GetComponentInChildren<SlotUI>() != null);
        if (scroll != null)
        {
            float previous = scroll.verticalNormalizedPosition;
            try { c.Detail("스크롤·마스크 안 슬롯의 툴팁"); scroll.verticalNormalizedPosition = 0; manager.ShowTooltip(item); yield return c.Wait(3f); }
            finally { scroll.verticalNormalizedPosition = previous; }
        }
        else { c.Detail("현재 가방 화면은 스크롤 없이 고정 슬롯을 표시해요 · 모서리와 긴 내용 확인"); yield return c.Wait(2f); }
        manager.HideTooltip();
    }
    static IEnumerator EnterDungeon(VisualPlayContext c)
    {
        var entry = DebugHub.Host.GetComponent<DungeonDebugEntry>() ?? DebugHub.Host.AddComponent<DungeonDebugEntry>();
        VisualPlayContext.Require(entry.TryEnter(1, true), entry.Status);
        yield return c.Until(() => !PersistentSceneFlow.Instance.IsSwitching && WorldSessionState.Phase == WorldPhase.Run, 60, "던전 입장");
        yield return c.Wait(2f);
    }
    static IEnumerator Dungeon(VisualPlayContext c)
    {
        string id = c.Entry.caseId;
        if (id == "VT21-01")
        {
            yield return PortalEntry(c, true, cancelFirst: true); yield return ReturnHideout(c);
            var map = PrepareMap(c, 10, ItemGrade.Rare); yield return PortalEntry(c, false, map); yield break;
        }
        if (id == "VT21-05")
        {
            var map = PrepareMap(c, 10, ItemGrade.Rare); yield return PortalEntry(c, false, map, fail: true);
            c.Detail("실패 뒤 무료 입장 재시도"); yield return PortalEntry(c, true); yield break;
        }
        if (id == "VT21-10")
        {
            string[] condition = c.Entry.variant.Split('|'); int level = int.Parse(condition[0]); var grade = (ItemGrade)Enum.Parse(typeof(ItemGrade), condition[1]);
            var map = PrepareMap(c, level, grade, condition[2]); yield return PortalEntry(c, false, map); yield break;
        }
        yield return EnterDungeon(c);
        if (id == "VT21-07")
        {
            string firstRun = AccountGameplaySession.Current.ReadRun().runId;
            c.Detail("첫 입장 후 실제 귀환"); yield return ReturnHideout(c);
            c.Detail("같은 포탈에서 재입장"); yield return PortalEntry(c, true);
            VisualPlayContext.Require(AccountGameplaySession.Current.ReadRun().runId != firstRun, "재입장에서 새 런이 시작되지 않았어요"); yield break;
        }
        if (id == "VT21-09")
        {
            var guard = c.Actor.GetComponent<RunFallGuard>();
            if (guard == null || !guard.enabled) throw new VisualPlayUnavailable("던전 플레이어의 낙하 복귀가 연결되지 않았어요");
            var origin = c.Actor.transform.position;
            c.Detail("발판 밖 경계"); c.Input(Vector2.down); yield return c.Wait(1.5f); c.ReleaseInput();
            c.Detail("수직 낙하와 현재 복귀"); ActorTeleportUtility.TeleportSafely(c.Actor.transform, origin + Vector3.down * 3f, c.Actor.transform.rotation);
            yield return c.Wait(4f); yield break;
        }
        if (id == "VT21-08")
        {
            var world = UnityEngine.Object.FindFirstObjectByType<DiamondDungeonWorld>();
            var field = world.Fields.FirstOrDefault();
            VisualPlayContext.Require(field != null, "던전 몬스터 필드가 없어요");
            ActorTeleportUtility.TeleportSafely(c.Actor.transform, field.transform.position, c.Actor.transform.rotation);
            yield return c.Wait(3f);
            foreach (var enemy in field.GetComponentsInChildren<EnemyActor>())
            { enemy.Health.TakeDamage(new DamageInfo(1000000, enemy.transform.position, c.Actor.gameObject)); yield return c.Wait(.5f); }
            yield return c.Wait(5f);
        }
        if (id == "VT23-01" || id == "VT23-02" || id == "VT23-03" || id == "VT23-04")
        {
            var world = UnityEngine.Object.FindFirstObjectByType<DiamondDungeonWorld>();
            VisualPlayContext.Require(world != null, "던전 월드가 없어요");
            var boss = Field<GameObject>(world, "boss"); VisualPlayContext.Require(boss != null, "현재 보스가 없어요");
            ActorTeleportUtility.TeleportSafely(c.Actor.transform, boss.transform.position + Vector3.back * 5f, Quaternion.identity);
            c.Actor.Health.SetMaxHp(100000, true); yield return c.Wait(id == "VT23-01" ? 12f : 3f);
            if (id == "VT23-01") yield break;
            var health = Field<CombatHealth>(world, "bossHealth");
            health.TakeDamage(new DamageInfo(10000000, health.transform.position, c.Actor.gameObject));
            yield return c.Until(() => AccountGameplaySession.Current.ReadRun().phase == RunPhase.BossCleared, 15, "보스 처치 보상");
            yield return c.Wait(4f);
            if (id == "VT23-03") PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>().RequestPortalExit();
            if (id == "VT23-04")
            {
                // 마지막 카운트다운만 준비한다. 이후 시간·실패 연출·귀환은 제품의 실제 시계로 진행한다.
                var account = AccountGameplaySession.Current;
                VisualPlayContext.Require(account.ExecuteState("visual-countdown-" + Guid.NewGuid().ToString("N"), snapshot => snapshot.run.bossClearedAtUtcTicks = DateTime.UtcNow.AddSeconds(-AccountRunSession.ExitDelaySeconds + 4).Ticks), "카운트다운 준비 실패");
            }
            if (id != "VT23-02") yield return c.Until(() => WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching, 45, "귀환 화면");
            yield return c.Wait(3f);
        }
        if (id == "VT23-05") { c.Actor.Health.TakeDamage(new DamageInfo(10000000, c.Actor.transform.position)); yield return c.Wait(8f); yield break; }
        if (id == "VT23-06")
        {
            var menu = OverburstGameMenu.Instance; menu.Open(); yield return c.Wait(2f);
            c.Detail("메뉴 귀환 확인창 취소"); ClickField(menu, "returnButton"); yield return c.Wait(2f); ClickField(menu, "modalCancel"); yield return c.Wait(2f);
            VisualPlayContext.Require(WorldSessionState.Phase == WorldPhase.Run && !PersistentSceneFlow.Instance.IsSwitching, "귀환 취소 후 런이 종료됐어요");
            c.Detail("메뉴 귀환 확인창 승인"); ClickField(menu, "returnButton"); yield return c.Wait(2f); ClickField(menu, "modalConfirm");
            yield return c.Until(() => WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching, 45, "메뉴 버튼 귀환"); yield return c.Wait(3f);
        }
    }
}
#endif
