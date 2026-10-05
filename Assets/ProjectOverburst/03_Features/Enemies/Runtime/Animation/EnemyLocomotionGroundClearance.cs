using System;
using System.Collections.Generic;
using UnityEngine;

// Curves are measured once from the skinned model in Editor. No mesh sampling runs in combat.
// Only locomotion is lifted; approved combat/parry poses keep their authored transforms.
[DefaultExecutionOrder(900)]
[DisallowMultipleComponent]
public sealed class EnemyLocomotionGroundClearance : MonoBehaviour
{
    [Serializable] public struct Sample
    {
        public AnimationClip clip;
        public AnimationCurve lift;
        public bool reverseBackpedal;
    }
    [SerializeField] private Animator animator;
    [SerializeField] private Vector3 authoredPosition;
    [SerializeField] private Sample[] samples = Array.Empty<Sample>();
    private readonly List<AnimatorClipInfo> clips = new List<AnimatorClipInfo>(8);
    private EnemyAnimationBridge bridge;
    private EnemyMovement movement;
    public float CurrentLift { get; private set; }
    public void Configure(Animator target, Sample[] curves)
    { animator=target; authoredPosition=target.transform.localPosition; samples=curves??Array.Empty<Sample>(); }
    private void Awake() { bridge=GetComponent<EnemyAnimationBridge>(); movement=GetComponent<EnemyMovement>(); }
    private void OnEnable() { CurrentLift=0; Restore(); }
    private void OnDisable() { CurrentLift=0; Restore(); }
    private void Restore() { if(animator!=null)animator.transform.localPosition=authoredPosition; }
    private void LateUpdate()
    {
        if(animator==null || !animator.isActiveAndEnabled)return;
        float lift=0,weight=0;
        if(bridge==null || !bridge.IsBlockingActionActive)
        {
            Accumulate(animator.GetCurrentAnimatorStateInfo(0),false,ref lift,ref weight);
            if(animator.IsInTransition(0))Accumulate(animator.GetNextAnimatorStateInfo(0),true,ref lift,ref weight);
        }
        lift/=Mathf.Max(1,weight);
        CurrentLift=lift;
        var parent=animator.transform.parent;
        Vector3 offset=Vector3.up*lift;
        if(parent!=null)offset=parent.InverseTransformVector(offset);
        animator.transform.localPosition=authoredPosition+offset;
    }
    private void Accumulate(AnimatorStateInfo state,bool next,ref float lift,ref float weight)
    {
        if(next)animator.GetNextAnimatorClipInfo(0,clips);else animator.GetCurrentAnimatorClipInfo(0,clips);
        for(int i=0;i<clips.Count;i++)
        {
            var info=clips[i];weight+=info.weight;
            for(int j=0;j<samples.Length;j++)
            {
                var sample=samples[j];if(sample.clip!=info.clip)continue;
                float phase=state.normalizedTime;
                if(sample.reverseBackpedal && movement!=null && movement.LocomotionMode==EnemyLocomotionMode.Backpedal)phase=-phase;
                lift+=Mathf.Max(0,sample.lift.Evaluate(Mathf.Repeat(phase,1)))*info.weight;break;
            }
        }
    }
}
