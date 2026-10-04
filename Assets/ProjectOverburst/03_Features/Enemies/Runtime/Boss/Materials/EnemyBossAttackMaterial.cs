using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum EnemyBossMaterialDelivery { Melee, Spit, Boulder }

[Serializable]
public sealed class EnemyBossMaterialStrike
{
    [Range(0f,1f)] public float contactStart;
    [Range(0f,1f)] public float impact;
    [Range(0f,1f)] public float contactEnd;
    public GroundIndicatorShape shape = GroundIndicatorShape.Circle;
    public Vector3 localOrigin;
    public float yaw;
    [Min(.01f)] public float radius = 1f;
    [Min(0f)] public float innerRadius;
    [Range(1f,360f)] public float angle = 360f;
    [Min(.01f)] public float width = 1f;
    [Min(.01f)] public float length = 1f;
    public float minimumHeight;
    public float maximumHeight = 3f;
    [TextArea] public string timingBasis;

    public bool IsValid => contactStart >= 0f && contactStart <= impact && impact <= contactEnd && contactEnd <= 1f
        && Finite(radius) && Finite(width) && Finite(length) && Finite(innerRadius) && Finite(angle) && Finite(yaw)
        && Finite(localOrigin.x) && Finite(localOrigin.y) && Finite(localOrigin.z) && Finite(minimumHeight) && Finite(maximumHeight)
        && radius > 0f && innerRadius>=0f && innerRadius<radius && angle>0f && angle<=360f && width > 0f && length > 0f && maximumHeight > minimumHeight
        && (shape == GroundIndicatorShape.Circle || shape == GroundIndicatorShape.Sector || shape == GroundIndicatorShape.Donut || shape == GroundIndicatorShape.Rectangle)
        && (shape != GroundIndicatorShape.Donut || (innerRadius > 0f && angle >= 359.9f));
    public static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    public Quaternion Rotation(Transform owner) => owner.rotation * Quaternion.Euler(0f,yaw,0f);
    public Vector3 Origin(Transform owner) => owner.TransformPoint(localOrigin);

    // The collider is the physical body. CombatTarget's much larger combat volume is not substituted here.
    public bool Intersects(Collider collider, Transform owner, Vector3? worldOrigin = null, Quaternion? worldRotation = null)
    {
        if(collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) return false;
        Vector3 origin = worldOrigin ?? Origin(owner);
        if(collider.bounds.max.y < origin.y + minimumHeight || collider.bounds.min.y > origin.y + maximumHeight) return false;
        Quaternion rotation = worldRotation ?? Rotation(owner);
        Vector3 point = Quaternion.Inverse(rotation) * (collider.bounds.center-origin);
        float bodyRadius;
        if(collider is CapsuleCollider capsule && capsule.direction == 1)
            bodyRadius = capsule.radius * Mathf.Max(Mathf.Abs(capsule.transform.lossyScale.x),Mathf.Abs(capsule.transform.lossyScale.z));
        else if(collider is SphereCollider sphere)
            bodyRadius = sphere.radius * Mathf.Max(Mathf.Abs(sphere.transform.lossyScale.x),Mathf.Abs(sphere.transform.lossyScale.z));
        else
        {
            Vector3 closest = collider.ClosestPoint(origin + Vector3.up * Mathf.Clamp(collider.bounds.center.y-origin.y,minimumHeight,maximumHeight));
            point = Quaternion.Inverse(rotation)*(closest-origin);bodyRadius = 0f;
        }
        Vector2 p = new Vector2(point.x,point.z);
        if(shape == GroundIndicatorShape.Rectangle)
        {
            Vector2 nearest = new Vector2(Mathf.Clamp(p.x,-width*.5f,width*.5f),Mathf.Clamp(p.y,0f,length));
            return (p-nearest).sqrMagnitude <= bodyRadius*bodyRadius + .000001f;
        }
        float distance = p.magnitude;
        if(distance > radius + bodyRadius || distance + bodyRadius < innerRadius) return false;
        if(shape == GroundIndicatorShape.Circle || shape == GroundIndicatorShape.Donut || angle >= 359.9f) return true;
        float half = angle * .5f * Mathf.Deg2Rad;
        if(distance < .000001f || Mathf.Abs(Mathf.Atan2(p.x,p.y)) <= half) return true;
        for(int sideIndex=0;sideIndex<2;sideIndex++)
        {
            float side=sideIndex==0?-1f:1f;
            Vector2 ray = new Vector2(Mathf.Sin(half*side),Mathf.Cos(half));
            Vector2 nearest = ray*Mathf.Clamp(Vector2.Dot(p,ray),innerRadius,radius);
            if((p-nearest).sqrMagnitude <= bodyRadius*bodyRadius + .000001f) return true;
        }
        return false;
    }

    public int Query(Transform owner, Collider[] buffer, int layerMask, Vector3? worldOrigin=null, Quaternion? worldRotation=null)
    {
        Vector3 origin = worldOrigin ?? Origin(owner);
        Quaternion rotation = worldRotation ?? Rotation(owner);
        float halfHeight = (maximumHeight-minimumHeight)*.5f;
        Vector3 center = origin + Vector3.up*(minimumHeight+halfHeight);
        Vector3 extents = new Vector3(radius,halfHeight,radius);
        if(shape == GroundIndicatorShape.Rectangle)
        { center += rotation*Vector3.forward*(length*.5f); extents = new Vector3(width*.5f,halfHeight,length*.5f); }
        return owner.gameObject.scene.GetPhysicsScene().OverlapBox(center,extents,buffer,rotation,layerMask,QueryTriggerInteraction.Ignore);
    }
}

[CreateAssetMenu(menuName="OVERBURST/Enemies/Boss Attack Material",fileName="BAM_Attack")]
public sealed class EnemyBossAttackMaterial : ScriptableObject
{
    public string materialId;
    public string displayName;
    public EnemyAbilityDefinition ability;
    public AnimationClip originalClip;
    public AnimationClip runtimeClip;
    public EnemyBossMaterialDelivery delivery;
    public EnemyBossAttackTuning tuning = new EnemyBossAttackTuning();
    public float DamageMultiplier => tuning != null ? Mathf.Max(0f, tuning.damageMultiplier) : 1f;
    public float AnimationSpeedMultiplier => tuning != null ? Mathf.Max(.01f, tuning.animationSpeedMultiplier) : 1f;
    public bool IsStrikeParryable(int phase) => ability != null && ability.IsParryable
        && delivery == EnemyBossMaterialDelivery.Melee && (tuning == null || tuning.AllowsParry(phase));
    public bool IsParryWindowOpen(int phase, float progress, float remaining) => IsStrikeParryable(phase)
        && (tuning != null ? tuning.WindowOpen(phase, progress, remaining, EnemyAbilityController.ParryLeadSeconds)
            : remaining >= -.03f && remaining <= EnemyAbilityController.ParryLeadSeconds);
    public EnemyBossMaterialStrike[] strikes = Array.Empty<EnemyBossMaterialStrike>();
    public bool showTelegraph = true;
    public bool tracksTargetDuringWindup;
    [Min(0f)] public float aimLockLeadSeconds = .3f;
    public string muzzleBone;
    public Vector3 muzzleOffset;
    [Min(.01f)] public float projectileRadius = .18f;
    [Min(.1f)] public float projectileSpeed = 12f;
    [Min(.1f)] public float flightSeconds = 1.2f;
    [Min(0f)] public float arcHeight = 4f;
    [Min(0f)] public float advanceDistance;
    public Vector2 advanceWindow;
    [TextArea] public string assemblyNotes;
    public bool IsValid
    {
        get
        {
            if(string.IsNullOrWhiteSpace(materialId)||ability==null||!ability.IsValid||originalClip==null||runtimeClip==null
                ||runtimeClip.isLooping||runtimeClip.name.EndsWith("_RM",StringComparison.Ordinal)||Mathf.Abs(runtimeClip.length-ability.AttackAnimationDuration)>.01f
                ||strikes==null||strikes.Length!=ability.HitCount||strikes.Length==0||strikes.Length>3) return false;
            for(int i=0;i<strikes.Length;i++)
                if(strikes[i]==null||!strikes[i].IsValid||Mathf.Abs(strikes[i].impact-ability.GetHitNormalizedTime(i))>.00001f
                    ||i>0 && strikes[i].impact <= strikes[i-1].impact) return false;
            return (tuning == null || tuning.Validate(strikes)) && (advanceDistance==0f || advanceWindow.x>=0f && advanceWindow.y>advanceWindow.x && advanceWindow.y<=1f);
        }
    }
}
