using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Balance/Common Table", fileName = "OverburstBalanceTable")]
public sealed class OverburstBalanceTable : ScriptableObject
{
    public const string ResourcePath = "Balance/OverburstBalanceTable";
    private static OverburstBalanceTable current;
    public static OverburstBalanceTable Current
    {
        get
        {
            if (current != null) return current;
            current = Resources.Load<OverburstBalanceTable>(ResourcePath);
            if (current == null)
            {
                current = CreateInstance<OverburstBalanceTable>();
                current.hideFlags = HideFlags.HideAndDontSave;
            }
            return current;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => current = null;

    [SerializeField] private float[] gearTiers = {
        40,10,1,5,2,5, 100,16,1.5f,8,3,8, 180,24,2,12,5,12,
        280,32,3,16,7,16, 400,44,4,22,9,20, 550,56,5,28,12,25,
        720,70,6,35,15,30, 920,86,7,43,18,35, 1150,102,8,51,21,40,
        1400,120,10,60,24,45
    };
    [SerializeField, Min(0)] private float playerHealthPerLevel = 15;
    [SerializeField, Min(1)] private int playerArmorEveryLevels = 4;
    [SerializeField, Min(0)] private float playerAttackPerLevel = .005f;
    [SerializeField, Min(0)] private float itemAttackPerLevel = .03f;
    [SerializeField, Range(0, 1)] private float enemyMoveGrowth = .15f;
    [SerializeField, Range(0, 1)] private float enemyAttackGrowth = .12f;
    [SerializeField, Min(1)] private float enemyMoveCap = 1.35f;
    [SerializeField, Min(1)] private float enemyAttackCap = 1.20f;

    public float PlayerHealthPerLevel => Mathf.Max(0, playerHealthPerLevel);
    public int PlayerArmorEveryLevels => Mathf.Max(1, playerArmorEveryLevels);
    public float PlayerAttackPerLevel => Mathf.Max(0, playerAttackPerLevel);
    public float ItemAttackPerLevel => Mathf.Max(0, itemAttackPerLevel);
    public float EnemyMoveGrowth => Mathf.Clamp01(enemyMoveGrowth);
    public float EnemyAttackGrowth => Mathf.Clamp01(enemyAttackGrowth);
    public float EnemyMoveCap => Mathf.Max(1, enemyMoveCap);
    public float EnemyAttackCap => Mathf.Max(1, enemyAttackCap);
    public float GearBase(GearKind kind, int level)
    {
        int column = (int)kind;
        if (column < 0 || column >= 6 || gearTiers == null || gearTiers.Length != 60)
            throw new System.InvalidOperationException("장비 공통 밸런스 표의 10구간/6부위 계약이 유효하지 않습니다.");
        return gearTiers[(OverburstGrowthRules.ClampLevel(level) - 1) / 10 * 6 + column];
    }
}
