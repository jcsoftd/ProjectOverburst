using UnityEngine;
using UnityEngine.InputSystem;

// Kept inside the removable Mojave trial so other experiments can be removed independently.
[DisallowMultipleComponent]
public sealed class MojaveBreakablePlant : CombatHealth, IDamageable
{
    [SerializeField] GameObject intactVisual;
    [SerializeField] Rigidbody[] fragments;
    [SerializeField] Collider intactCollider;
    [SerializeField] CombatTarget target;
    [SerializeField] float scatterScale=1f;
    [SerializeField,Min(1f)] float debrisLifetime=3f;
    Vector3[] positions,scales;
    Quaternion[] rotations;
    Collider[] fragmentColliders;
    float brokenAt;
    public bool Broken {get;private set;}
    public bool DebrisCleared {get;private set;}
    public int BreakCount {get;private set;}
    public int FragmentCount=>fragments?.Length??0;
    public bool BlocksMovement=>intactCollider!=null&&intactCollider.enabled&&!intactCollider.isTrigger;

    void CachePose()
    {
        if(positions!=null)return;
        positions=new Vector3[FragmentCount];scales=new Vector3[FragmentCount];rotations=new Quaternion[FragmentCount];fragmentColliders=new Collider[FragmentCount];
        for(int i=0;i<FragmentCount;i++){positions[i]=fragments[i].transform.localPosition;scales[i]=fragments[i].transform.localScale;rotations[i]=fragments[i].transform.localRotation;fragmentColliders[i]=fragments[i].GetComponent<Collider>();}
    }

    void IDamageable.TakeDamage(DamageInfo info)
    {
        if(!Application.isPlaying||Broken||info.damage<=0||info.isDamageOverTime)return;
        CachePose();Broken=true;DebrisCleared=false;brokenAt=Time.time;BreakCount++;
        target.enabled=false;intactCollider.enabled=false;intactVisual.SetActive(false);
        var direction=Vector3.ProjectOnPlane(info.direction,Vector3.up);direction=direction.sqrMagnitude>.001f?direction.normalized:transform.forward;
        var actor=PlayerContext.Instance?.CurrentActor;var playerColliders=actor!=null?actor.GetComponentsInChildren<Collider>():null;
        for(int i=0;i<FragmentCount;i++)
        {
            var body=fragments[i];body.gameObject.SetActive(true);fragmentColliders[i].enabled=true;
            if(playerColliders!=null)foreach(var playerCollider in playerColliders)if(playerCollider!=null)Physics.IgnoreCollision(fragmentColliders[i],playerCollider);
            body.isKinematic=false;body.useGravity=true;
            body.linearVelocity=(direction*Random.Range(1.2f,2.1f)+Vector3.up*Random.Range(.7f,1.3f))*scatterScale;
            body.angularVelocity=Random.insideUnitSphere*4f;
        }
    }

    public void ResetPlant()
    {
        CachePose();
        for(int i=0;i<FragmentCount;i++)
        {
            var body=fragments[i];if(!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
            body.isKinematic=true;body.useGravity=false;body.transform.localPosition=positions[i];body.transform.localRotation=rotations[i];body.transform.localScale=scales[i];fragmentColliders[i].enabled=false;body.gameObject.SetActive(false);
        }
        intactVisual.SetActive(true);intactCollider.enabled=true;target.enabled=true;Broken=false;DebrisCleared=false;ResetHealth();Physics.SyncTransforms();
    }

    void Update()
    {
        if(Keyboard.current!=null&&Keyboard.current.f9Key.wasPressedThisFrame&&!GameplayInputBlocker.IsGameplayInputBlocked)ResetPlant();
        if(!Broken||DebrisCleared)return;float age=Time.time-brokenAt;if(age<debrisLifetime-.5f)return;
        float scale=Mathf.Clamp01((debrisLifetime-age)/.5f);
        for(int i=0;i<FragmentCount;i++){fragments[i].transform.localScale=scales[i]*Mathf.Max(.001f,scale);if(age>=debrisLifetime)fragments[i].gameObject.SetActive(false);}
        DebrisCleared=age>=debrisLifetime;
    }

    void OnDisable(){if(fragments==null)return;foreach(var body in fragments)if(body!=null){body.isKinematic=true;body.useGravity=false;body.gameObject.SetActive(false);}}
}
