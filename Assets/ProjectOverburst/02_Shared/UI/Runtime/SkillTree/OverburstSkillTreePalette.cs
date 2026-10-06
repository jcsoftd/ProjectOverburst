using UnityEngine;

/// <summary>Warm brass and ivory shared with the RPG11 HUD; ownership remains separate from focus.</summary>
public static class OverburstSkillTreePalette
{
    public static readonly Color Brass = new Color32(180,141,85,255);
    public static readonly Color LightBrass = new Color32(198,162,104,255);
    public static readonly Color Ivory = new Color32(237,227,209,255);
    public static readonly Color InactiveRoute = new Color32(83,74,59,255);
    public static readonly Color Refund = new Color32(181,124,106,255);
    public static readonly Color Badge = new Color32(19,16,13,255);
    public static Color Frame(OverburstSkillTreeNodeView.NodeState state)
    {
        switch(state)
        {
            case OverburstSkillTreeNodeView.NodeState.Learned: return Brass;
            case OverburstSkillTreeNodeView.NodeState.Guide: return new Color32(117,100,71,255);
            case OverburstSkillTreeNodeView.NodeState.PurchaseDraft: return new Color32(128,113,88,255);
            case OverburstSkillTreeNodeView.NodeState.RefundDraft: return new Color32(94,56,47,255);
            case OverburstSkillTreeNodeView.NodeState.Available: return new Color32(108,92,70,255);
            case OverburstSkillTreeNodeView.NodeState.Insufficient: return new Color32(84,75,61,255);
            case OverburstSkillTreeNodeView.NodeState.Reserved: return new Color32(69,58,49,255);
            default: return new Color32(65,58,48,255);
        }
    }
    public static Color Icon(OverburstSkillTreeNodeView.NodeState state)
    {
        switch(state)
        {
            case OverburstSkillTreeNodeView.NodeState.Learned:
            case OverburstSkillTreeNodeView.NodeState.PurchaseDraft: return Ivory;
            case OverburstSkillTreeNodeView.NodeState.Guide: return new Color32(176,152,109,255);
            case OverburstSkillTreeNodeView.NodeState.RefundDraft: return Refund;
            case OverburstSkillTreeNodeView.NodeState.Available: return new Color32(197,183,159,255);
            case OverburstSkillTreeNodeView.NodeState.Insufficient: return new Color32(153,140,117,255);
            case OverburstSkillTreeNodeView.NodeState.Reserved: return new Color32(129,116,97,255);
            default: return new Color32(105,97,81,255);
        }
    }
    public static Color Caption(OverburstSkillTreeNodeView.NodeState state)
    {
        switch(state)
        {
            case OverburstSkillTreeNodeView.NodeState.Learned: return new Color32(214,180,120,255);
            case OverburstSkillTreeNodeView.NodeState.Guide: return new Color32(176,152,109,255);
            case OverburstSkillTreeNodeView.NodeState.PurchaseDraft: return Ivory;
            case OverburstSkillTreeNodeView.NodeState.RefundDraft: return new Color32(203,154,135,255);
            case OverburstSkillTreeNodeView.NodeState.Available: return new Color32(188,175,153,255);
            case OverburstSkillTreeNodeView.NodeState.Insufficient: return new Color32(178,159,125,255);
            case OverburstSkillTreeNodeView.NodeState.Reserved: return new Color32(151,133,111,255);
            default: return new Color32(148,138,119,255);
        }
    }
}
