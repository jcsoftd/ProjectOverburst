using UnityEngine;

// A small shared pool prevents a prefab Instantiate spike on every enemy impact.
public static class EnemyStrongAttackImpactVfx
{
    private const int PoolSize = 6;
    private static readonly GameObject[] instances = new GameObject[PoolSize];
    private static readonly ParticleSystem[][] systems = new ParticleSystem[PoolSize][];
    private static EnemyTelegraphVisualLibrary library;
    private static int cursor;

    public static void Prewarm()
    {
        if (!Application.isPlaying) return;
        if (library == null) library = Resources.Load<EnemyTelegraphVisualLibrary>(
            "Enemies/Balance/EnemyTelegraphVisualLibrary");
        if (library == null || library.GroundImpact == null) return;
        // Only one new object per frame of first use. Subsequent attacks fill the pool.
        for (int i = 0; i < PoolSize; i++)
            if (instances[i] == null)
            {
                Create(i);
                break;
            }
    }

    public static void Play(Vector3 origin)
    {
        if (!Application.isPlaying) return;
        if (library == null) Prewarm();
        if (library == null || library.GroundImpact == null) return;
        int index = cursor++ % PoolSize;
        if (instances[index] == null) Create(index);
        GameObject instance = instances[index];
        if (instance == null) return;
        Vector3 point = origin;
        int mask = Physics.DefaultRaycastLayers;
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer >= 0) mask &= ~(1 << enemyLayer);
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0) mask &= ~(1 << playerLayer);
        if (Physics.Raycast(origin + Vector3.up * 1.5f, Vector3.down,
            out RaycastHit hit, 4f, mask, QueryTriggerInteraction.Ignore))
            point = hit.point;
        instance.transform.position = point + Vector3.up * .035f;
        instance.transform.rotation = Quaternion.identity;
        for (int i = 0; i < systems[index].Length; i++)
        {
            systems[index][i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            systems[index][i].Play(true);
        }
    }

    private static void Create(int index)
    {
        if (library == null || library.GroundImpact == null) return;
        GameObject instance = Object.Instantiate(library.GroundImpact);
        instance.name = "Enemy ground impact " + index;
        instances[index] = instance;
        systems[index] = instance.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem system in systems[index])
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
