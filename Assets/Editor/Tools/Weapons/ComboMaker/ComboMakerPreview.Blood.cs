using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Overburst.EditorTools.ComboMaker
{
    internal sealed partial class ComboMakerPreview
    {
        public bool ShowBlood {get;set;}=true;
        public string BloodStatus
        {
            get
            {
                var profile=BloodProfile!=null?BloodProfile:targetBlood;
                return !ShowBlood?"혈흔 표시 꺼짐":profile==null?"혈흔 프로필 없음":profile.suppressBlood?"이 몬스터는 혈흔 억제":$"혈흔 · {profile.name}";
            }
        }
        private void SpawnBloodDecal(BloodHitCatalog catalog,BloodHitProfile profile,CombatImpactShape shape,Vector3 point,Vector3 direction,float size)
        {
            var template=catalog.ResolveDecal(shape,false,HitCount-1);
            var source=template!=null?template.GetComponent<DecalProjector>():null;
            if(source==null||source.material==null)return;
            // The isolated preview has a flat floor at y=-.03, not the live scene's physics world.
            var root=new GameObject("혈흔 바닥 프리뷰");root.SetActive(false);root.transform.SetParent(stage.transform,false);
            Vector3 ground=point+direction*(.22f*Mathf.Clamp(size,.55f,1.5f));ground.y=-.015f;
            root.transform.SetPositionAndRotation(ground,Quaternion.LookRotation(Vector3.down,direction));
            var decal=root.AddComponent<DecalProjector>();
            var material=new Material(source.material){hideFlags=HideFlags.HideAndDontSave};
            if(material.HasProperty("_MainColor"))material.SetColor("_MainColor",profile.mainColor.linear);
            if(material.HasProperty("_SecondaryColor"))material.SetColor("_SecondaryColor",profile.secondaryColor.linear);
            if(material.HasProperty("_SpecularColor"))material.SetColor("_SpecularColor",profile.specularColor.linear);
            if(material.HasProperty("_SpecularValue"))material.SetFloat("_SpecularValue",Mathf.Min(profile.specular,.4f));
            decal.material=material;float scale=Mathf.Max(.7f,.85f*size);
            decal.size=new Vector3(Mathf.Clamp(source.size.x*scale,.65f,2.4f),Mathf.Clamp(source.size.y*scale,.65f,2.4f),.07f);
            decal.pivot=Vector3.zero;decal.drawDistance=40;decal.fadeScale=source.fadeScale;
            ActivateEffect(root,.16f+BloodGroundDecalService.HoldSeconds+BloodGroundDecalService.FadeSeconds);
        }
    }
}
