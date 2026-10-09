using System;
using UnityEngine;

namespace Overburst.Caves
{
    public sealed class CaveRigidConnection : MonoBehaviour
    {
        [Serializable] public sealed class Piece
        {
            public GameObject source;
            public Transform instance;
            public Vector3 localStart, localEnd;
        }
        public Piece[] pieces;
        public int stairCount;
        public Vector3 start, end;
        public float startContactGap, endContactGap;
        public bool needsReview;
        void OnDrawGizmosSelected()
        {
            Gizmos.color = needsReview ? new Color(1, .5f, .1f) : Color.cyan;
            Gizmos.DrawWireSphere(start, .3f); Gizmos.DrawWireSphere(end, .3f);
            if (pieces == null) return;
            foreach (var p in pieces)
                if (p.instance) Gizmos.DrawLine(p.instance.TransformPoint(p.localStart), p.instance.TransformPoint(p.localEnd));
        }
    }
}
