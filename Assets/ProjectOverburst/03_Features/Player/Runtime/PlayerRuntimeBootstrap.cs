using System.Collections;
using UnityEngine;

public sealed class PlayerRuntimeBootstrap : MonoBehaviour
{
    private const int MaxWaitFrames = 180;
    private static bool bootstrapped;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState() => bootstrapped = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapAfterSceneLoad()
    {
        if (bootstrapped)
            return;
        GameObject owner = new GameObject("PlayerRuntimeBootstrap");
        DontDestroyOnLoad(owner);
        owner.AddComponent<PlayerRuntimeBootstrap>();
    }

    private IEnumerator Start()
    {
        PlayerContext context = PlayerContext.GetOrCreate();
        int waitedFrames = 0;
        while (context.CurrentActor == null && waitedFrames < MaxWaitFrames)
        {
            waitedFrames++;
            yield return null;
        }
        PlayerActorRuntime actor = context.CurrentActor;
        if (actor != null)
        {
            PlayerControlKit kit = PlayerCapabilityInstaller.EnsurePlayerKit(actor.gameObject);
            actor.ResolveReferences();
            kit.ApplyAuthority(ActorControlAuthority.Player);
            context.Bind(actor);
            PlayerCameraBinder cameraBinder = context.GetComponent<PlayerCameraBinder>();
            if (cameraBinder == null)
                cameraBinder = context.gameObject.AddComponent<PlayerCameraBinder>();
            cameraBinder.Bind(context);
            PlayerLootAutoMoveDriver lootDriver = context.GetComponent<PlayerLootAutoMoveDriver>();
            if (lootDriver == null)
                lootDriver = context.gameObject.AddComponent<PlayerLootAutoMoveDriver>();
            lootDriver.Bind(context);
            PlayerHealthHud healthHud = FindFirstObjectByType<PlayerHealthHud>(FindObjectsInactive.Include);
            if (healthHud != null)
                healthHud.Bind(context);
            bootstrapped = true;
        }
        Destroy(gameObject);
    }
}
