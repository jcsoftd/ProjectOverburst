using UnityEngine;

namespace Overburst.Mojave
{
    public sealed class MojavePortal : MonoBehaviour
    {
        public Light glow;
        public Transform halo;
        public void Place(MojaveWorld world)
        {
            world.EnsureLayout();transform.position=world.Ground(world.layout.places[0].center+new Vector2(-4,-2),.05f);
            transform.rotation=Quaternion.Euler(0,45,0);
        }
        void Update()
        {
            if(glow!=null)glow.intensity=2.0f+Mathf.Sin(Time.time*1.9f)*.3f;
            if(halo!=null)halo.localRotation=Quaternion.Euler(0,0,Time.time*11);
        }
    }
}
