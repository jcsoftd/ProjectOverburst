using System;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class PlayerFlaskController : MonoBehaviour
{
    public const int SlotCount = 3;
    public const int FirstKey = 4;
    [SerializeField] private string[] equippedIds = new string[SlotCount];
    private PlayerInventory inventory;
    private CombatHealth health;
    private PlayerEquipment equipment;
    private PlayerStateCoordinator conditions;
    private readonly FlaskActiveEffects effects = new FlaskActiveEffects();
    private readonly FlaskChargeRules chargeRules = new FlaskChargeRules();
    private float previousTime;
    private readonly ItemData[] equippedItems = new ItemData[SlotCount];
    public event Action Changed;
    public FlaskActiveEffects Effects => effects;
    public static PlayerFlaskController Current
    {
        get
        {
            CombatHealth actor = PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActorHealth : null;
            return actor != null ? actor.GetComponent<PlayerFlaskController>() : null;
        }
    }

    private void OnEnable()
    {
        health = GetComponent<CombatHealth>(); equipment = GetComponent<PlayerEquipment>();
        conditions = GetComponent<PlayerStateCoordinator>();
        if (health != null) { health.OnDead += Died; health.OnReset += ResetHealth; }
        if (equipment != null) equipment.WeaponSlotsChanged += WeaponChanged;
        SceneManager.sceneLoaded += SceneLoaded;
        previousTime = Time.time;
        ResolveInventory();
    }
    private void OnDisable()
    {
        if (health != null) { health.OnDead -= Died; health.OnReset -= ResetHealth; }
        if (equipment != null) equipment.WeaponSlotsChanged -= WeaponChanged;
        if (inventory != null) inventory.Changed -= InventoryChanged;
        SceneManager.sceneLoaded -= SceneLoaded;
        ClearEffects(); inventory = null;
    }
    private void Update() { Tick(Time.time); }

    public void Tick(float now)
    {
        ResolveInventory();
        float elapsed = Mathf.Max(0f, now - previousTime);
        if (health == null || health.IsDead || health.CurrentHp <= 0f) { previousTime = now; return; }
        float heal = effects.Advance(previousTime, now, out bool changed);
        if (heal > 0f) health.Heal(health.MaxHp * heal);
        if (IsCombatScene)
            GainAll(chargeRules.ParticipatingDuration(previousTime, now) * FlaskChargeRules.PassiveCombatRate);
        previousTime = now;
        if (changed) Changed?.Invoke();
    }

    public ItemData GetItem(int index)
    {
        if (index < 0 || index >= SlotCount || equippedIds == null || string.IsNullOrEmpty(equippedIds[index]) || inventory == null) return null;
        foreach (ItemData item in inventory.Items)
            if (item != null && item.runtimeInstanceId == equippedIds[index] && item.baseData is FlaskItemData) return item;
        return null;
    }

    public bool TryEquip(int index, ItemData item, out string reason)
    {
        reason = string.Empty;
        ResolveInventory();
        if (!CanChangeLoadout) { reason = "물약은 은신처에서 교체할 수 있습니다."; return false; }
        if (index < 0 || index >= SlotCount || item == null || !(item.baseData is FlaskItemData data)
            || inventory == null || !inventory.ContainsItem(item) || FlaskRuntime.State(item) == null)
        { reason = "장착할 물약을 찾을 수 없습니다."; return false; }
        for (int i = 0; i < SlotCount; i++)
        {
            ItemData other = GetItem(i);
            if (i != index && other != null && ((FlaskItemData)other.baseData).kind == data.kind)
            { reason = "같은 종류의 물약은 하나만 장착할 수 있습니다."; return false; }
        }
        ItemData previous = GetItem(index);
        if (previous != null) { effects.Remove(previous.runtimeInstanceId); FlaskRuntime.State(previous).equippedSlot = -1; }
        equippedIds[index] = item.runtimeInstanceId;
        equippedItems[index] = item;
        FlaskRuntime.State(item).equippedSlot = index;
        Changed?.Invoke(); return true;
    }

    public bool TryUnequip(int index, out string reason)
    {
        reason = string.Empty;
        if (!CanChangeLoadout) { reason = "물약은 은신처에서 해제할 수 있습니다."; return false; }
        if (index < 0 || index >= SlotCount) return false;
        ItemData item = GetItem(index);
        if (item != null && FlaskRuntime.State(item) != null) FlaskRuntime.State(item).equippedSlot = -1;
        effects.Remove(equippedIds[index]); equippedIds[index] = null; equippedItems[index] = null;
        Changed?.Invoke(); return true;
    }

    public bool TryUse(int index, out string reason)
    {
        reason = string.Empty;
        Tick(Time.time);
        ItemData item = GetItem(index);
        if (item == null) { reason = "물약 슬롯이 비어 있습니다."; return false; }
        FlaskItemData data = (FlaskItemData)item.baseData;
        if (health == null || health.IsDead || health.CurrentHp <= 0f || !isActiveAndEnabled
            || (conditions != null && conditions.CurrentCondition != PlayerConditionState.Normal)
            || GameplayInputBlocker.IsGameplayInputBlocked
            || (PlayerInputFacade.Current != null && !PlayerInputFacade.Current.IsGameplayEnabled))
        { reason = "지금은 물약을 사용할 수 없습니다."; return false; }
        if (!MatchesWeapon(data)) { reason = "물약과 일치하는 원소 무기가 필요합니다."; return false; }
        if (effects.Contains(item.runtimeInstanceId)) { reason = "물약 효과가 이미 적용 중입니다."; return false; }
        if (!chargeRules.CanUse(Time.time)) { reason = "잠시 후 사용할 수 있습니다."; return false; }
        if (data.kind == FlaskKind.Life && health.CurrentHp >= health.MaxHp)
        { reason = "체력이 가득 차 있습니다."; return false; }
        FlaskInstanceState state = FlaskRuntime.State(item);
        FlaskStats stats = FlaskRuntime.Stats(item);
        if (state == null || state.charge + .00001f < stats.cost)
        { reason = "물약 충전이 부족합니다."; return false; }
        if (!effects.Add(item.runtimeInstanceId, data, stats, Time.time)) return false;
        if (!FlaskChargeRules.Spend(state, stats)) { effects.Remove(item.runtimeInstanceId); return false; }
        chargeRules.Used(Time.time);
        float immediate = data.primaryEffect == FlaskEffect.InstantHeal ? stats.primary
            : data.secondaryEffect == FlaskEffect.InstantHeal ? stats.secondary : 0f;
        if (immediate > 0f) PlayerHealFeedback.ApplyHealPercent(health, immediate);
        DamageNumberSpawner.SpawnStatusText(health.transform.position, data.itemName, new Color(.65f, 1f, .85f, 1f));
        Changed?.Invoke(); return true;
    }

    public float Bonus(FlaskEffect effect) => effects.Get(effect);
    public float Remaining(int index) { ItemData item = GetItem(index); return item != null ? effects.Remaining(item.runtimeInstanceId, Time.time) : 0f; }
    public bool MatchesWeapon(FlaskItemData data)
    {
        WeaponElement required = FlaskRuntime.RequiredElement(data.kind);
        return required == WeaponElement.None || (equipment != null && equipment.CurrentWeaponItem != null
            && equipment.CurrentWeaponItem.ResolvedElement == required);
    }

    public void ReportCombat(float bonus = 0f)
    {
        if (health == null || health.IsDead || health.CurrentHp <= 0f || !IsCombatScene) return;
        Tick(Time.time);
        chargeRules.Participate(Time.time);
        if (bonus > 0f) GainAll(chargeRules.TakeBonus(bonus, Time.time));
    }
    public void GrantBonus(float bonus)
    {
        if (health != null && !health.IsDead && health.CurrentHp > 0f && IsCombatScene && bonus > 0f)
            GainAll(chargeRules.TakeBonus(bonus, Time.time));
    }
    public void Refill()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            ItemData item = GetItem(i); if (item == null) continue;
            FlaskInstanceState state = FlaskRuntime.State(item);
            if (state != null) state.charge = FlaskRuntime.Stats(item).capacity;
        }
        Changed?.Invoke();
    }
    public void ClearEffects() { effects.Clear(); chargeRules.Clear(); GetComponent<FlaskGhostCollision>()?.Restore(); Changed?.Invoke(); }

    public static bool CanChangeLoadout
    {
        get
        {
            bool hideout = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                string name = SceneManager.GetSceneAt(i).name;
                if (name == "DungeonRunScene") return false;
                if (name.IndexOf("Hideout", StringComparison.OrdinalIgnoreCase) >= 0) hideout = true;
            }
            return hideout;
        }
    }
    public static bool IsCombatScene => !CanChangeLoadout;
    private void GainAll(float amount)
    {
        for (int i = 0; i < SlotCount; i++)
        { ItemData item = GetItem(i); if (item != null) FlaskChargeRules.Gain(FlaskRuntime.State(item), FlaskRuntime.Stats(item), amount); }
    }
    private void ResolveInventory()
    {
        PlayerInventory next = PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActorInventory : null;
        if (next == null) next = GetComponent<PlayerInventory>();
        if (next == inventory) return;
        if (inventory != null) inventory.Changed -= InventoryChanged;
        inventory = next;
        if (inventory != null) inventory.Changed += InventoryChanged;
        if (equippedIds == null || equippedIds.Length != SlotCount) equippedIds = new string[SlotCount];
        if (inventory != null)
            foreach (var item in inventory.Items)
            {
                if (!(item?.baseData is FlaskItemData)) continue;
                var state = FlaskRuntime.State(item);
                if (state == null || state.equippedSlot < 0 || state.equippedSlot >= SlotCount) continue;
                equippedIds[state.equippedSlot] = item.runtimeInstanceId;
                equippedItems[state.equippedSlot] = item;
            }
    }
    private void InventoryChanged()
    {
        for (int i = 0; i < SlotCount; i++)
            if (!string.IsNullOrEmpty(equippedIds[i]) && GetItem(i) == null)
            {
                effects.Remove(equippedIds[i]); equippedIds[i] = null;
                if (equippedItems[i]?.flaskState != null) equippedItems[i].flaskState.equippedSlot = -1;
                equippedItems[i] = null;
            }
        Changed?.Invoke();
    }
    private void WeaponChanged()
    {
        for (int i = 0; i < SlotCount; i++)
        { ItemData item = GetItem(i); if (item != null && !MatchesWeapon((FlaskItemData)item.baseData)) effects.Remove(item.runtimeInstanceId); }
        Changed?.Invoke();
    }
    private void Died(CombatHealth _, DamageInfo info) => ClearEffects();
    private void ResetHealth(CombatHealth _) { ClearEffects(); if (CanChangeLoadout) Refill(); previousTime = Time.time; }
    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "DungeonRunScene" && scene.name.IndexOf("Hideout", StringComparison.OrdinalIgnoreCase) < 0) return;
        ClearEffects(); previousTime = Time.time; Refill();
    }
}
