using UnityEngine;

[RequireComponent(typeof(CombatHealth))]
public class TrainingDummy : MonoBehaviour // 전투 테스트용
{
    [SerializeField] private CombatHealth health;
    [SerializeField] private bool resetOnStart = true;

    private void Awake()
    {
        if (health == null)
            health = GetComponent<CombatHealth>(); // 같은 오브젝트
    }

    private void Start()
    {
        if (resetOnStart && health != null)
            health.ResetHealth();
    }

    [ContextMenu("Reset Dummy Health")]
    public void ResetDummyHealth()
    {
        if (health == null)
            health = GetComponent<CombatHealth>();

        if (health != null)
            health.ResetHealth(); // 테스트 초기화
    }
}
