using UnityEngine;

public sealed class OverburstUITargetHpPreview : MonoBehaviour
{
    [SerializeField] private EnemyTargetHpHud targetHud;
    [SerializeField] private string sampleName = "정예 수호자";
    [SerializeField] private EnemyRankType sampleRank = EnemyRankType.Elite;
    [SerializeField] private float sampleHp = 450f;
    [SerializeField] private float sampleMaxHp = 1000f;

    private void Start()
    {
        if (GetComponent<OverburstUIWorkshop>() == null)
            return;

        if (targetHud == null)
            targetHud = GetComponentInChildren<EnemyTargetHpHud>(true);
        if (targetHud == null)
            return;

        targetHud.gameObject.SetActive(true);
        targetHud.GetComponent<EnemyTargetHpSlotUI>()?.ShowPreview(sampleName, sampleRank, sampleHp, sampleMaxHp);
    }
}
