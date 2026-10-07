using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overburst.Persistence;
using UnityEngine;

namespace Overburst.Appearance
{
    // Caches the original model once. Preview-body changes are a separate, explicit operation.
    public sealed class P09AppearanceApplier
    {
        private readonly Transform root;
        private readonly CharacterAppearanceCatalog catalog;
        private readonly Renderer[] renderers;
        private readonly Dictionary<string,List<Renderer>> byName;
        private readonly Dictionary<Renderer,Material[]> originalMaterials;
        private readonly Transform[] styleBones;
        private readonly Func<Material,Material> resolveMaterial;
        public Transform Root => root;

        public P09AppearanceApplier(Transform root, CharacterAppearanceCatalog catalog,
            Func<Material,Material> resolveMaterial=null)
        {
            this.root=root?root:throw new ArgumentNullException(nameof(root));
            this.catalog=catalog?catalog:throw new ArgumentNullException(nameof(catalog));
            this.resolveMaterial=resolveMaterial??(m=>m);
            renderers=root.GetComponentsInChildren<Renderer>(true);
            byName=new Dictionary<string,List<Renderer>>(StringComparer.Ordinal);
            originalMaterials=new Dictionary<Renderer,Material[]>();
            foreach (var r in renderers)
            {
                if (!byName.TryGetValue(r.name,out var list)) byName[r.name]=list=new List<Renderer>();
                list.Add(r);originalMaterials[r]=r.sharedMaterials;
            }
            styleBones=root.GetComponentsInChildren<Transform>(true)
                .Where(t=>t.name=="bust_01_L"||t.name=="bust_01_R").ToArray();
        }

        public void ValidatePlan(CharacterAppearanceSnapshot value)
        {
            catalog.Validate(value);
            RequireRenderer(CharacterAppearanceCatalog.Required(catalog.faces,value.faceId,x=>x.id).rendererName);
            var hair=CharacterAppearanceCatalog.Required(catalog.hairStyles,value.hairStyleId,x=>x.id);
            if (!string.IsNullOrEmpty(hair.rendererName)) RequireRenderer(hair.rendererName);
            if (styleBones.Length<2) throw new InvalidDataException("상체스타일 본이 연결되지 않았습니다.");
            foreach (var c in new[]{
                CharacterAppearanceCatalog.Required(catalog.hairColors,value.hairColorId,x=>x.id),
                CharacterAppearanceCatalog.Required(catalog.skinColors,value.skinColorId,x=>x.id),
                CharacterAppearanceCatalog.Required(catalog.eyeColors,value.eyeColorId,x=>x.id)})
                if (!c.material) throw new InvalidDataException("외모 재질이 연결되지 않았습니다: "+c.id);
        }

        public void ApplyAppearance(CharacterAppearanceSnapshot value)
        {
            ValidatePlan(value);
            var face=CharacterAppearanceCatalog.Required(catalog.faces,value.faceId,x=>x.id);
            var hair=CharacterAppearanceCatalog.Required(catalog.hairStyles,value.hairStyleId,x=>x.id);
            foreach (var option in catalog.faces) SetRenderer(option.rendererName,option.id==face.id);
            foreach (var option in catalog.hairStyles)
                if (!string.IsNullOrEmpty(option.rendererName)) SetRenderer(option.rendererName,option.id==hair.id);
            // Same data drives live and preview. Never touch armor/weapon activation here.
            var hairColor=CharacterAppearanceCatalog.Required(catalog.hairColors,value.hairColorId,x=>x.id);
            var skin=CharacterAppearanceCatalog.Required(catalog.skinColors,value.skinColorId,x=>x.id).material;
            var eye=CharacterAppearanceCatalog.Required(catalog.eyeColors,value.eyeColorId,x=>x.id).material;
            foreach (var r in renderers)
            {
                var original=originalMaterials[r];var next=new Material[original.Length];
                for (int i=0;i<original.Length;i++)
                {
                    var m=original[i];var selected=m;
                    if (m && m.name.Contains("_Skin")) selected=skin;
                    else if (m && m.name.StartsWith("P09_Eye",StringComparison.Ordinal)) selected=eye;
                    if (r.name==hair.rendererName && hair.rendererName!="SkinHead_Female")
                        selected=hair.rendererName=="Hair_10"&&hairColor.hair10Material
                            ?hairColor.hair10Material:hairColor.material;
                    next[i]=resolveMaterial(selected);
                }
                r.sharedMaterials=next;
            }
            ApplyBodyStyle(value.bodyShapeId);
        }

        public void ApplyBodyStyle(string id)
        {
            var style=CharacterAppearanceCatalog.Required(catalog.bodyStyles,id,x=>x.id);
            foreach (var bone in styleBones) if (bone) bone.localScale=style.scale;
        }

        public void ApplyPreviewBody(CharacterAppearanceSnapshot value, AppearancePreviewBody mode,
            string equipmentId=null, bool showHead=true)
        {
            ValidatePlan(value);
            var allowed=new HashSet<string>(StringComparer.Ordinal);
            if (mode==AppearancePreviewBody.Equipment)
            {
                var example=CharacterAppearanceCatalog.Required(catalog.equipmentExamples,equipmentId,x=>x.id);
                foreach(var name in example.rendererNames)
                    if (showHead || !name.EndsWith("_Head",StringComparison.Ordinal)) allowed.Add(name);
                foreach(var name in example.baseBodyNames) allowed.Add(name);
            }
#if UNITY_EDITOR
            else foreach(var name in mode==AppearancePreviewBody.Nude?catalog.nudeBodyNames:catalog.underwearBodyNames)allowed.Add(name);
#else
            else if(mode==AppearancePreviewBody.Underwear)foreach(var name in catalog.underwearBodyNames)allowed.Add(name);
            else throw new ArgumentOutOfRangeException(nameof(mode));
#endif
            allowed.Add(CharacterAppearanceCatalog.Required(catalog.faces,value.faceId,x=>x.id).rendererName);
            var hair=CharacterAppearanceCatalog.Required(catalog.hairStyles,value.hairStyleId,x=>x.id);
            if (!string.IsNullOrEmpty(hair.rendererName)) allowed.Add(hair.rendererName);
            foreach(var name in allowed) RequireRenderer(name);
            foreach(var r in renderers) r.gameObject.SetActive(allowed.Contains(r.name));
            foreach(var r in renderers) if(allowed.Contains(r.name)) EnsureAncestorsActive(r.transform);
            ApplyAppearance(value);
        }

        public void ClearExpressions()
        {
            foreach(var r in renderers)
                if(r is SkinnedMeshRenderer skin && skin.sharedMesh)
                    for(int i=0;i<skin.sharedMesh.blendShapeCount;i++) skin.SetBlendShapeWeight(i,0);
        }

        private void RequireRenderer(string name)
        {
            if (string.IsNullOrEmpty(name)||!byName.TryGetValue(name,out var list)||list.Count==0)
                throw new InvalidDataException("외모 모델 파츠가 없습니다: "+name);
        }
        private void SetRenderer(string name,bool enabled)
        {
            if (string.IsNullOrEmpty(name)||!byName.TryGetValue(name,out var values)) return;
            foreach(var r in values)
            {
                r.gameObject.SetActive(enabled);
                if(enabled) EnsureAncestorsActive(r.transform);
            }
        }
        private void EnsureAncestorsActive(Transform child)
        {
            while(child && child!=root) {child.gameObject.SetActive(true);child=child.parent;}
        }
    }
}

