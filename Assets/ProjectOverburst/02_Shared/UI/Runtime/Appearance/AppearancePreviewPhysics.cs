using System;
using System.Collections.Generic;
using System.Linq;
using MagicaCloth2;
using UnityEngine;

namespace Overburst.Appearance
{
    // Uses the P09 provider's configured cloth, spring bones and collision shapes.
    public sealed class AppearancePreviewPhysics : MonoBehaviour
    {
        private AppearanceCharacterPreview owner;
        private GameObject model;
        private MagicaCloth[] cloths=Array.Empty<MagicaCloth>();
        private bool subscribed;
        public MagicaCloth[] Cloths=>cloths;
        public void Initialize(AppearanceCharacterPreview preview,GameObject visual)
        {
            owner=preview;model=visual;cloths=model.GetComponentsInChildren<MagicaCloth>(true);
            RefreshSelection();
            if(Application.isPlaying){MagicaManager.OnPreSimulation+=BeforeSimulation;MagicaManager.OnPostSimulation+=AfterSimulation;subscribed=true;}
        }
        public void RefreshSelection()
        {
            if(!model)return;
            var active=model.GetComponentsInChildren<SkinnedMeshRenderer>(false);
            var used=new HashSet<Transform>(active.SelectMany(r=>r.bones).Where(t=>t));
            foreach(var cloth in cloths)
            {
                var data=cloth.SerializeData;
                bool needed=data.sourceRenderers.Any(r=>r&&r.gameObject.activeInHierarchy)||data.rootBones.Any(b=>b&&used.Any(t=>t==b||t.IsChildOf(b)));
                cloth.enabled=needed;
                if(needed&&!cloth.GetComponent<Renderer>())cloth.gameObject.SetActive(true);
            }
        }
        public void ResetSimulation(){foreach(var cloth in cloths)if(cloth&&cloth.enabled&&cloth.IsValid())cloth.ResetCloth();}
        private void BeforeSimulation(){if(owner)owner.PreparePhysicsPose();}
        private void AfterSimulation(){if(owner)owner.RenderAfterPhysics();}
        public void Dispose()
        {
            if(subscribed){MagicaManager.OnPreSimulation-=BeforeSimulation;MagicaManager.OnPostSimulation-=AfterSimulation;subscribed=false;}
            owner=null;model=null;cloths=Array.Empty<MagicaCloth>();
        }
        private void OnDestroy()=>Dispose();
    }
}
