using UnityEngine;
using UnityEngine.UI;

public sealed class ElementGemEquipmentSlotUI : EquipmentSlotInteraction
{
    [SerializeField] private Text feedback;
    public void Configure(Text label) { feedback = label; Configure(EquipmentSlotKind.Gem, 0); }
}
