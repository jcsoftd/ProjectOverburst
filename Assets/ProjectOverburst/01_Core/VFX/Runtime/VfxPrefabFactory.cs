using UnityEngine;

public static class VfxPrefabFactory // 시각 효과 프리팹 보조
{
    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (prefab == null)
            return null;

        return Object.Instantiate(prefab, position, rotation, parent);
    }

    public static GameObject SpawnAttached(GameObject prefab, Transform parent)
    {
        return SpawnAttached(prefab, parent, Vector3.zero, Quaternion.identity);
    }

    public static GameObject SpawnAttached(GameObject prefab, Transform parent, Vector3 localPosition, Quaternion localRotation)
    {
        if (prefab == null || parent == null)
            return null;

        GameObject instance = Object.Instantiate(prefab, parent);
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = localRotation;

        return instance;
    }

    public static GameObject SpawnFollowing(GameObject prefab, Transform target)
    {
        return SpawnFollowing(prefab, target, prefab != null ? prefab.transform.localPosition : Vector3.zero, false, true);
    }

    public static GameObject SpawnFollowing(GameObject prefab, Transform target, Vector3 localOffset, bool followRotation, bool destroyOnTargetLost)
    {
        if (prefab == null || target == null)
            return null;

        Quaternion rotation = followRotation ? target.rotation : prefab.transform.rotation; // 회전 기준
        GameObject instance = Object.Instantiate(prefab, target.TransformPoint(localOffset), rotation);
        VfxFollowTarget followTarget = instance.GetComponent<VfxFollowTarget>();

        if (followTarget == null)
            followTarget = instance.AddComponent<VfxFollowTarget>();

        followTarget.Initialize(target, localOffset, followRotation, destroyOnTargetLost);

        return instance;
    }
}
