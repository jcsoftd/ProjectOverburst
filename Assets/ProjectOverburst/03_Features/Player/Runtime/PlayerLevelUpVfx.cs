using System.Collections;
using UnityEngine;
using UnityEngine.VFX;

[DisallowMultipleComponent]
public sealed class PlayerLevelUpVfx : MonoBehaviour
{
    private const string PrefabResourcePath = "VFX/PF_PlayerLevelUpVfx";
    private const float EffectLifetimeSeconds = 5f;

    private PlayerProgression progression;
    private PlayerContext context;
    private GameObject prefab;
    private GameObject activeEffect;
    private VisualEffect activeVisualEffect;
    private Transform activeAnchor;
    private PlayerActorRuntime activeActor;
    private UnityEngine.SceneManagement.Scene activeScene;
    private int runRevision;
    private Coroutine releaseRoutine;
    private bool warnedMissingPrefab;

    public void Bind(PlayerProgression nextProgression, PlayerContext nextContext)
    {
        if (progression != null)
            progression.LeveledUp -= Play;
        if (context != null) context.CurrentActorChanged -= ActorChanged;
        ReleaseActive();
        progression = nextProgression;
        context = nextContext;
        if (context != null) context.CurrentActorChanged += ActorChanged;
        if (progression != null)
            progression.LeveledUp += Play;
    }

    private void LateUpdate()
    {
        if (activeEffect == null) return;
        if (activeActor == null || !activeActor.isActiveAndEnabled || activeActor.Health == null || activeActor.Health.IsDead
            || context == null || context.CurrentActor != activeActor || activeActor.gameObject.scene != activeScene
            || RunWalkableContext.Revision != runRevision || PersistentSceneFlow.Instance?.IsSwitching == true)
        { ReleaseActive(); return; }
        if (activeVisualEffect != null && activeAnchor != null) SyncTransform(activeVisualEffect, activeAnchor);
    }

    private void OnDisable()
    {
        if (progression != null)
            progression.LeveledUp -= Play;
        if (context != null) context.CurrentActorChanged -= ActorChanged;
        ReleaseActive(); progression = null; context = null;
    }

    private void ActorChanged(PlayerActorRuntime next) { if (activeActor != next) ReleaseActive(); }
    private void ReleaseActive()
    {
        if (releaseRoutine != null) StopCoroutine(releaseRoutine);
        releaseRoutine = null;
        if (activeEffect != null) { activeEffect.SetActive(false); Destroy(activeEffect); }
        activeEffect = null; activeVisualEffect = null; activeAnchor = null; activeActor = null;
    }

    private void Play(int previousLevel, int currentLevel)
    {
        PlayerActorRuntime actor = context != null ? context.CurrentActor : null;
        if (actor == null || !actor.isActiveAndEnabled || actor.Health == null || actor.Health.IsDead)
            return;

        if (prefab == null)
            prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            if (!warnedMissingPrefab)
            {
                Debug.LogWarning("[PlayerLevelUpVfx] Missing prefab: " + PrefabResourcePath);
                warnedMissingPrefab = true;
            }
            return;
        }

        ReleaseActive();

        SkinnedMeshRenderer body = ResolveBody(actor);
        if (body == null)
        {
            Debug.LogWarning("[PlayerLevelUpVfx] Active actor has no skinned body mesh: " + actor.name);
            return;
        }

        activeEffect = Instantiate(prefab, actor.transform);
        activeActor = actor; activeScene = actor.gameObject.scene; runRevision = RunWalkableContext.Revision;
        activeEffect.name = "PlayerLevelUpVfx";
        activeEffect.GetComponent<PlayerLevelUpBodyGlow>()?.Begin(actor);
        activeEffect.transform.localPosition = Vector3.zero;
        activeEffect.transform.localRotation = Quaternion.identity;
        activeEffect.transform.localScale = prefab.transform.localScale;

        activeVisualEffect = activeEffect.GetComponentInChildren<VisualEffect>(true);
        if (activeVisualEffect == null || activeVisualEffect.visualEffectAsset == null)
        {
            Debug.LogWarning("[PlayerLevelUpVfx] Replacement prefab has no usable VisualEffect.");
            ReleaseActive();
            return;
        }

        if (activeVisualEffect.HasSkinnedMeshRenderer("SkinnedMeshRenderer"))
            activeVisualEffect.SetSkinnedMeshRenderer("SkinnedMeshRenderer", body);
        activeAnchor = ResolveAnchor(body, actor.transform);
        SyncTransform(activeVisualEffect, activeAnchor);
        activeVisualEffect.Reinit();
        releaseRoutine = StartCoroutine(ReleaseAfterDelay(activeEffect));
    }

    private static SkinnedMeshRenderer ResolveBody(PlayerActorRuntime actor)
    {
        SkinnedMeshRenderer best = null;
        SkinnedMeshRenderer bestChest = null;
        int mostVertices = -1;
        int mostChestVertices = -1;
        foreach (SkinnedMeshRenderer candidate in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy ||
                candidate.sharedMesh == null)
                continue;
            int vertices = candidate.sharedMesh.vertexCount;
            if (vertices > mostVertices)
            {
                best = candidate;
                mostVertices = vertices;
            }
            if (vertices > mostChestVertices &&
                candidate.name.EndsWith("_Chest", System.StringComparison.OrdinalIgnoreCase))
            {
                bestChest = candidate;
                mostChestVertices = vertices;
            }
        }
        return bestChest != null ? bestChest : best;
    }

    private static Transform ResolveAnchor(SkinnedMeshRenderer body, Transform fallback)
    {
        foreach (Transform bone in body.bones)
        {
            if (bone != null && (bone.name.Equals("Hips", System.StringComparison.OrdinalIgnoreCase) ||
                                 bone.name.Equals("Pelvis", System.StringComparison.OrdinalIgnoreCase)))
                return bone;
        }
        return body.rootBone != null ? body.rootBone : fallback;
    }

    private static void SyncTransform(VisualEffect effect, Transform anchor)
    {
        if (!effect.HasVector3("Transform_position") || !effect.HasVector3("Transform_angles") ||
            !effect.HasVector3("Transform_scale"))
            return;

        bool local = effect.visualEffectAsset.GetExposedSpace("Transform") == VFXSpace.Local;
        Matrix4x4 matrix = local
            ? effect.transform.worldToLocalMatrix * anchor.localToWorldMatrix
            : anchor.localToWorldMatrix;
        effect.SetVector3("Transform_position", matrix.GetPosition());
        effect.SetVector3("Transform_angles", matrix.rotation.eulerAngles);
        effect.SetVector3("Transform_scale", matrix.lossyScale);
    }

    private IEnumerator ReleaseAfterDelay(GameObject effect)
    {
        yield return new WaitForSeconds(EffectLifetimeSeconds);
        if (activeEffect == effect) { releaseRoutine = null; ReleaseActive(); }
    }
}
