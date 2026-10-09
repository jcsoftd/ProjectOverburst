using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace Overburst.Mojave
{
    [RequireComponent(typeof(Camera))]
    public sealed class MojaveCamera : MonoBehaviour
    {
        public Transform target;
        public float yaw = 225;
        public float distance = 20;
        public float pitch = 45;
        public const float ReferenceSize = 6.886552f;
        Vector3 focus;
        float desiredDistance=20;
        Camera view;
        readonly Dictionary<Renderer,bool> hidden=new Dictionary<Renderer,bool>();

        void Awake() { view=GetComponent<Camera>();desiredDistance=distance;Snap(); }
        void LateUpdate()
        {
            if(target==null)return;
            var explorer=target.GetComponent<MojaveExplorer>();
            var mouse=explorer!=null&&explorer.reviewInputSuppressed?null:Mouse.current;
            if(mouse!=null)desiredDistance=Mathf.Clamp(desiredDistance-mouse.scroll.ReadValue().y/120f*1.25f,6,28);
            if(mouse!=null&&mouse.middleButton.isPressed)yaw+=mouse.delta.ReadValue().x*.15f;
            distance=Mathf.Lerp(distance,desiredDistance,1-Mathf.Exp(-18*Time.deltaTime));
            focus=Vector3.Lerp(focus,target.position,1-Mathf.Exp(-24*Time.deltaTime));Apply();
        }
        public void Snap()
        {
            if(view==null)view=GetComponent<Camera>();if(target!=null)focus=target.position;Apply();
        }
        public void FocusAt(Vector3 position) {focus=position;Apply();}
        void Apply()
        {
            float close=Mathf.SmoothStep(0,1,Mathf.InverseLerp(14,6,distance));
            var rotation=Quaternion.Euler(Mathf.Lerp(pitch,12,close),yaw,0);
            transform.SetPositionAndRotation(focus+Vector3.up*(1.3f*close)+rotation*Vector3.back*(distance*Mathf.Lerp(1,.32f,close)),rotation);
            view.orthographic=false;view.fieldOfView=2*Mathf.Atan(ReferenceSize/20)*Mathf.Rad2Deg;
            view.nearClipPlane=.3f;view.farClipPlane=500;
            RevealOccluders();
            if(target!=null) {
                var start=focus+Vector3.up*1.1f;var delta=transform.position-start;
                foreach(var hit in Physics.SphereCastAll(start,.4f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore)) {
                    if(hit.collider is TerrainCollider||hit.collider.GetComponentInParent<MojaveExplorer>()!=null)continue;
                    var lod=hit.collider.GetComponentInParent<LODGroup>();
                    var renderers=lod!=null?lod.GetComponentsInChildren<Renderer>():hit.collider.GetComponentsInChildren<Renderer>();
                    foreach(var renderer in renderers)if(!hidden.ContainsKey(renderer)){hidden[renderer]=renderer.forceRenderingOff;renderer.forceRenderingOff=true;}
                }
            }
        }
        void RevealOccluders() {foreach(var pair in hidden)if(pair.Key!=null)pair.Key.forceRenderingOff=pair.Value;hidden.Clear();}
        void OnDisable() {RevealOccluders();}
    }
}
