using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DefaultExecutionOrder(10003)]
public sealed class BuffBarUI : MonoBehaviour
{
    public const int TimedSlotCount = 7;
    public const int MapSlotCount = 8;
    public const int SlotCount = TimedSlotCount;
    [SerializeField] private PlayerBuffController buffController;
    [SerializeField] private BuffIconSlotUI[] slots = new BuffIconSlotUI[SlotCount];
    [SerializeField] private TextMeshProUGUI moreIndicator;
    [SerializeField] private bool autoResolveReferences = true;
    private readonly List<BuffInstance> activeBuffs = new List<BuffInstance>(9);
    private readonly List<FlaskEffectSnapshot> activeFlasks = new List<FlaskEffectSnapshot>(3);
    private readonly int[] mapStacks = new int[MapSlotCount];
    private static readonly string[] FlaskKeys = {
        "flask_Life", "flask_Regeneration", "flask_Berserker", "flask_Giant", "flask_Executioner", "flask_Overcharge",
        "flask_Ironclad", "flask_Ghost", "flask_Fire", "flask_Ice", "flask_Lightning", "flask_Dark", "flask_Light"
    };
    private static readonly string[] MapKeys = {
        "map_MaxHealth", "map_Armor", "map_Attack", "map_ElementalDamage", "map_AttackSpeed", "map_MoveSpeed", "map_ItemDrop", "map_ExperienceGain"
    };
    private CombatHealth actor;
    private PlayerFlaskController flasks;
    private OverburstElementEnergy energy;
    public int VisibleTimedCount { get; private set; }
    public int VisibleMapCount { get; private set; }

    private void Awake() => BindVisuals();
    private void OnEnable() => Refresh();
    private void OnDisable() => HideAll();
    private void Update() => Refresh();

    public void Refresh()
    {
        BindVisuals();
        if (autoResolveReferences) ResolveReferences();
        if (actor == null || actor.IsDead || actor.CurrentHp <= 0f) { HideAll(); return; }
        int shown = 0, total = 0;
        activeBuffs.Clear();
        if (buffController != null) buffController.GetActiveBuffs(activeBuffs);
        activeBuffs.Sort(CompareRemaining);
        foreach (BuffInstance buff in activeBuffs)
        {
            if (shown < SlotCount && slots[shown] != null) slots[shown++].SetBuff(buff);
            total++;
        }
        activeFlasks.Clear();
        if (flasks != null) flasks.Effects.GetActive(activeFlasks, Time.time);
        foreach (FlaskEffectSnapshot flask in activeFlasks)
        {
            if (shown < SlotCount && slots[shown] != null)
                slots[shown++].SetEffect(FlaskKeys[(int)flask.Data.kind], StatusBuffIcons.Flask(flask.Data), flask.Remaining, flask.Duration);
            total++;
        }
        if (energy != null && energy.Element == WeaponElement.Light && energy.RadianceStacks > 0)
        {
            if (shown < SlotCount && slots[shown] != null)
                slots[shown++].SetEffect("radiance", StatusBuffIcons.Status("radiance"), stacks: energy.RadianceStacks, permanent: true);
            total++;
        }
        VisibleTimedCount = shown;

        System.Array.Clear(mapStacks, 0, mapStacks.Length);
        MapRunBuffs map = MapRunBuffs.Current;
        if (map != null && WorldSessionState.Phase == WorldPhase.Run)
            foreach (MapCardChoice card in map.Selected)
                if (card.Kind == MapCardKind.Buff) mapStacks[(int)card.Buff]++;
        int shownMap = 0;
        for (int i = 0; i < MapSlotCount; i++)
        {
            if (mapStacks[i] == 0) continue;
            if (shown < SlotCount && slots[shown] != null)
            {
                slots[shown++].SetEffect(MapKeys[i], StatusBuffIcons.Map((MapBuffKind)i), stacks: mapStacks[i], permanent: true);
                shownMap++;
            }
            total++;
        }
        for (int i = shown; i < SlotCount; i++) slots[i]?.SetVisible(false);
        VisibleMapCount = shownMap;
        if (moreIndicator != null) moreIndicator.gameObject.SetActive(total > SlotCount);
    }

    private void HideAll()
    {
        if (slots != null) foreach (BuffIconSlotUI slot in slots) slot?.SetVisible(false);
        if (moreIndicator != null) moreIndicator.gameObject.SetActive(false);
        VisibleTimedCount = VisibleMapCount = 0;
    }

    private void BindVisuals()
    {
        if (slots == null || slots.Length != SlotCount) System.Array.Resize(ref slots, SlotCount);
        for (int i = 0; i < SlotCount; i++)
            if (slots[i] == null)
                slots[i] = transform.Find("BuffIconSlot_" + (i + 1).ToString("00"))?.GetComponent<BuffIconSlotUI>();
        if (moreIndicator == null) moreIndicator = transform.Find("MoreIndicator")?.GetComponent<TextMeshProUGUI>();
    }

    private void ResolveReferences()
    {
        CombatHealth next = PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActorHealth : null;
        if (actor == next) return;
        actor = next;
        buffController = actor != null ? actor.GetComponent<PlayerBuffController>() : null;
        flasks = actor != null ? actor.GetComponent<PlayerFlaskController>() : null;
        energy = actor != null ? actor.GetComponent<OverburstElementEnergy>() : null;
    }

    private static int CompareRemaining(BuffInstance left, BuffInstance right)
        => left.RemainingTime.CompareTo(right.RemainingTime);
}
