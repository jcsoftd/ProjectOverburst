using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
[DisallowMultipleComponent]
public sealed class EnemyThemeTriggerZone : MonoBehaviour
{
    [SerializeField] private EnemyThemeEncounter encounter;
    [SerializeField] private bool onslaught;
    [SerializeField] private bool oneShot = true;
    [SerializeField, Min(1)] private float repeatCooldown = 10;
    private bool fired;
    private float nextAllowed;
    public bool HasTriggered => fired;
    public void Configure(EnemyThemeEncounter owner,bool waves,bool once=true)
    {encounter=owner;onslaught=waves;oneShot=once;GetComponent<BoxCollider>().isTrigger=true;}
    private void Reset(){GetComponent<BoxCollider>().isTrigger=true;encounter=GetComponent<EnemyThemeEncounter>();}
    private void OnTriggerEnter(Collider other)
    {
        var facade=other.GetComponentInParent<PlayerInputFacade>();
        if(facade!=null)TryActivate(facade.transform);
    }
    public bool TryActivate(Transform player)
    {
        if(!isActiveAndEnabled || encounter==null || player==null || (oneShot && fired) || Time.time<nextAllowed)return false;
        if(player.GetComponent<PlayerInputFacade>()==null)return false;
        if(!encounter.Begin(player,onslaught))return false;
        fired=true;nextAllowed=Time.time+repeatCooldown;return true;
    }
    public bool Rearm()
    {
        if(encounter!=null && (encounter.Running || encounter.AliveCount>0))return false;
        fired=false;nextAllowed=0;return true;
    }
}
