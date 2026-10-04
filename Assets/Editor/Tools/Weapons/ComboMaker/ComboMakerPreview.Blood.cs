using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Overburst.EditorTools.ComboMaker
{
    internal sealed partial class ComboMakerPreview
    {
        public bool ShowBlood {get;set;}=true;
        public bool PackBlood {get;set;}
        private int previousBloodVariation = -1;
        private int previousPackVariation = -1;
        public string BloodStatus
        {
            get
            {
                var profile=BloodProfile!=null?BloodProfile:targetBlood;
                return !ShowBlood?"혈흔 표시 꺼짐":profile==null?"혈흔 프로필 없음":profile.suppressBlood?"이 몬스터는 혈흔 억제":$"{(PackBlood?"B · 크기 2 / 밝기 0.8":"A · 기존 혈흔")} · {profile.name}";
            }
        }

        private void SpawnPackBlood(BloodHitProfile profile,CombatImpactShape shape,Vector3 point,Vector3 direction,float size)
        {
            var catalog=Resources.Load<BloodEffectsPackCatalog>(BloodEffectsPackCatalog.ResourcePath);
            if(catalog==null)return;
            if(HitCount<=1)previousPackVariation=-1;
            int priority=HeavyDefinition!=null?1:0;
            uint seed=BloodHitVfxService.CosmeticSeed(17,HitCount,0,1);
            int index=catalog.ResolveSpray(shape,priority,seed,previousPackVariation);
            if(index<0)return;
            previousPackVariation=index;
            var spray=catalog.sprays[index];
            float scale=spray.scale*Mathf.Clamp(profile.size/3.5f,.4f,1.6f)*size*(priority>0?1.1f:1f)*2f;
            var go=InstantiateEffect(spray.prefab,point,Quaternion.LookRotation(direction,Vector3.up)*Quaternion.Euler(spray.localEuler),Vector3.one*scale);
            go.name="B 혈흔 비산 프리뷰";
            var copies=new Dictionary<Material,Material>();
            try
            {
                var block=new MaterialPropertyBlock();
                block.SetColor("_BaseColor",profile.mainColor);block.SetFloat("_Smoothness",Mathf.Clamp(profile.specular,.1f,.4f));
                block.SetFloat("_HueShift",0);block.SetFloat("_AlbedoPower",.45f);
                block.SetFloat("_ColorIntensity",1.1f*.8f);block.SetFloat("_AmbientColorIntensity",.7f);
                foreach(var renderer in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    var shared=renderer.sharedMaterials;
                    if(catalog.sprayProfileShader!=null)
                        for(int i=0;i<shared.Length;i++)
                        {
                            var source=shared[i];if(source==null)continue;
                            if(!copies.TryGetValue(source,out var material))
                            {material=new Material(source){hideFlags=HideFlags.HideAndDontSave,shader=catalog.sprayProfileShader};copies.Add(source,material);}
                            shared[i]=material;
                        }
                    renderer.sharedMaterials=shared;renderer.SetPropertyBlock(block);
                }
                ActivateEffect(go,spray.lifetime,copies.Values.ToArray());
            }
            catch
            {
                Object.DestroyImmediate(go);foreach(var material in copies.Values)Object.DestroyImmediate(material);throw;
            }
            var template=catalog.ResolveDecal(shape,false,HitCount-1);
            SpawnBloodDecalTemplate(template,profile,point,direction,size,true,catalog.groundProfileShader);
        }

        private void SpawnBloodDecal(BloodHitCatalog catalog,BloodHitProfile profile,CombatImpactShape shape,Vector3 point,Vector3 direction,float size)
        {
            var template=catalog.ResolveDecal(shape,false,HitCount-1);
            SpawnBloodDecalTemplate(template,profile,point,direction,size,false,null);
        }
        private void SpawnBloodDecalTemplate(GameObject template,BloodHitProfile profile,Vector3 point,Vector3 direction,float size,bool pack,Shader shader)
        {
            var source=template!=null?template.GetComponent<DecalProjector>():null;
            if(source==null||source.material==null)return;
            // The isolated preview has a flat floor at y=-.03, not the live scene's physics world.
            var root=new GameObject("혈흔 바닥 프리뷰");root.SetActive(false);root.transform.SetParent(stage.transform,false);
            Vector3 ground=point+direction*(.22f*Mathf.Clamp(size,.55f,1.5f));ground.y=-.015f;
            root.transform.SetPositionAndRotation(ground,Quaternion.LookRotation(Vector3.down,direction));
            var decal=root.AddComponent<DecalProjector>();
            var material=new Material(source.material){hideFlags=HideFlags.HideAndDontSave};
            if(pack&&shader!=null)material.shader=shader;
            float brightness=pack?.8f:1f;
            if(material.HasProperty("_MainColor"))material.SetColor("_MainColor",profile.mainColor.linear*brightness);
            if(material.HasProperty("_SecondaryColor"))material.SetColor("_SecondaryColor",profile.secondaryColor.linear*brightness);
            if(material.HasProperty("_SpecularColor"))material.SetColor("_SpecularColor",profile.specularColor.linear*brightness);
            if(pack)
            {
                if(material.HasProperty("_BaseColor"))material.SetVector("_BaseColor",profile.mainColor.linear*brightness);
                if(material.HasProperty("_AlbedoPower"))material.SetFloat("_AlbedoPower",.55f);
                if(material.HasProperty("_HueShift"))material.SetFloat("_HueShift",0);
                if(material.HasProperty("_ColorIntensity"))material.SetFloat("_ColorIntensity",.95f);
                if(material.HasProperty("_AmbientColorIntensity"))material.SetFloat("_AmbientColorIntensity",.25f);
                if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",Mathf.Clamp(profile.specular,.1f,.4f));
                var original=template.GetComponent<BloodPackGroundPattern>();
                if(original!=null)
                {
                    var pattern=root.AddComponent<BloodPackGroundPattern>();pattern.enabled=false;
                    pattern.variant=original.variant;pattern.columns=original.columns;pattern.rows=original.rows;pattern.frames=original.frames;pattern.spreadSeconds=original.spreadSeconds;
                    pattern.Apply(decal,0);
                }
            }
            if(material.HasProperty("_SpecularValue"))material.SetFloat("_SpecularValue",Mathf.Min(profile.specular,.4f));
            decal.material=material;float scale=Mathf.Max(.7f,.85f*size);
            float total=pack?2f:1f;
            decal.size=new Vector3(Mathf.Clamp(source.size.x*scale,.65f,2.4f)*total,Mathf.Clamp(source.size.y*scale,.65f,2.4f)*total,.07f);
            decal.pivot=Vector3.zero;decal.drawDistance=40;decal.fadeScale=source.fadeScale;
            ActivateEffect(root,.16f+BloodGroundDecalService.HoldSeconds+BloodGroundDecalService.FadeSeconds);
        }
    }
}
