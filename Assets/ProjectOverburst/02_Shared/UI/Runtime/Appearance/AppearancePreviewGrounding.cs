#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overburst.Appearance
{
    // The preview floor stays fixed. Source clips and the gameplay animator remain unchanged.
    internal sealed class AppearancePreviewGrounding
    {
        private readonly struct ContactVertex
        {
            public readonly Vector3 position;
            public readonly BoneWeight weight;
            public ContactVertex(Vector3 position,BoneWeight weight){this.position=position;this.weight=weight;}
        }
        private readonly Transform[] bones;
        private readonly Matrix4x4[] bindposes,matrices;
        private readonly int[] usedBones;
        private readonly ContactVertex[] contacts;
        public float AvatarFloorOffset { get; private set; }

        public AppearancePreviewGrounding(GameObject model,Animator animator,CharacterAppearanceCatalog catalog)
        {
            var feet=new HashSet<Transform>();
            foreach(var id in new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightToes})
            {var bone=animator.GetBoneTransform(id);if(bone)feet.Add(bone);}
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(!skin.sharedMesh||Array.IndexOf(catalog.nudeBodyNames,skin.name)<0)continue;
                var skinBones=skin.bones;var indices=new HashSet<int>();
                for(int i=0;i<skinBones.Length;i++)if(feet.Contains(skinBones[i]))indices.Add(i);
                if(indices.Count==0)continue;
                var mesh=skin.sharedMesh;
                if(!mesh.isReadable)throw new InvalidOperationException("발 접점 메시를 읽을 수 없습니다: "+skin.name);
                var weights=mesh.boneWeights;var vertices=mesh.vertices;
                var selected=new List<ContactVertex>();var used=new HashSet<int>();
                for(int i=0;i<weights.Length;i++)
                {
                    var w=weights[i];
                    float footWeight=(indices.Contains(w.boneIndex0)?w.weight0:0)+(indices.Contains(w.boneIndex1)?w.weight1:0)
                        +(indices.Contains(w.boneIndex2)?w.weight2:0)+(indices.Contains(w.boneIndex3)?w.weight3:0);
                    if(footWeight<.5f)continue;
                    selected.Add(new ContactVertex(vertices[i],w));
                    if(w.weight0>0)used.Add(w.boneIndex0);if(w.weight1>0)used.Add(w.boneIndex1);
                    if(w.weight2>0)used.Add(w.boneIndex2);if(w.weight3>0)used.Add(w.boneIndex3);
                }
                if(selected.Count==0)continue;
                bones=skinBones;bindposes=mesh.bindposes;matrices=new Matrix4x4[bones.Length];
                usedBones=new int[used.Count];used.CopyTo(usedBones);contacts=selected.ToArray();
                var inverse=model.transform.worldToLocalMatrix;
                foreach(int index in usedBones)matrices[index]=inverse*bones[index].localToWorldMatrix*bindposes[index];
                float plane=float.PositiveInfinity;
                foreach(bool left in new[]{true,false})
                {
                    var foot=animator.GetBoneTransform(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
                    var toe=animator.GetBoneTransform(left?HumanBodyBones.LeftToes:HumanBodyBones.RightToes);
                    var side=new HashSet<int>();for(int i=0;i<bones.Length;i++)if(bones[i]==foot||bones[i]==toe)side.Add(i);
                    float minimum=float.PositiveInfinity;
                    foreach(var contact in contacts)
                    {
                        var w=contact.weight;
                        if((side.Contains(w.boneIndex0)?w.weight0:0)+(side.Contains(w.boneIndex1)?w.weight1:0)
                            +(side.Contains(w.boneIndex2)?w.weight2:0)+(side.Contains(w.boneIndex3)?w.weight3:0)<.5f)continue;
                        minimum=Mathf.Min(minimum,Height(contact));
                    }
                    plane=Mathf.Min(plane,minimum-model.transform.InverseTransformPoint(foot.position).y
                        +(left?animator.leftFeetBottomHeight:animator.rightFeetBottomHeight));
                }
                AvatarFloorOffset=-plane;return;
            }
            throw new InvalidOperationException("프리뷰의 발 접점을 찾지 못했습니다.");
        }

        private float Height(ContactVertex contact)
        {
            var w=contact.weight;var v=contact.position;float y=0;
            if(w.weight0>0)y+=matrices[w.boneIndex0].MultiplyPoint3x4(v).y*w.weight0;
            if(w.weight1>0)y+=matrices[w.boneIndex1].MultiplyPoint3x4(v).y*w.weight1;
            if(w.weight2>0)y+=matrices[w.boneIndex2].MultiplyPoint3x4(v).y*w.weight2;
            if(w.weight3>0)y+=matrices[w.boneIndex3].MultiplyPoint3x4(v).y*w.weight3;
            return y;
        }
        public void Plant(Transform stage,Transform model)
        {
            var worldToStage=stage.worldToLocalMatrix;
            foreach(int index in usedBones)matrices[index]=worldToStage*bones[index].localToWorldMatrix*bindposes[index];
            float lowest=float.PositiveInfinity;
            foreach(var contact in contacts)
            {
                lowest=Mathf.Min(lowest,Height(contact));
            }
            if(float.IsNaN(lowest)||float.IsInfinity(lowest))throw new InvalidOperationException("애니메이션 발 접점이 유효하지 않습니다.");
            var position=model.localPosition;position.y-=lowest;model.localPosition=position;
        }
    }
}
#endif
