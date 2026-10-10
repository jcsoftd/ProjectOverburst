using System;
using UnityEngine;

namespace Overburst.Caves
{
    public sealed class CaveRigidConnection : MonoBehaviour
    {
        public CavePlatformBoundary boundaryA, boundaryB;
        public int portA = -1, portB = -1;
        public float walkWidth;

        public bool BoundaryOpen => isActiveAndEnabled && boundaryA && boundaryB
            && boundaryA.isActiveAndEnabled && boundaryB.isActiveAndEnabled
            && pieces != null && pieces.Length > 0 && PiecesPresent();

        bool PiecesPresent()
        {
            foreach (var piece in pieces) if (!piece.instance || !piece.instance.gameObject.activeInHierarchy) return false;
            return true;
        }

        public bool Sample(Vector3 near, float up, float down, out Vector3 point)
        {
            point = near;
            if (walkWidth <= 0) return false;
            var axis = end - start; axis.y = 0;
            float length = axis.magnitude;
            if (length < .01f) return false;
            axis /= length;
            var delta = near - start; delta.y = 0;
            float along = Vector3.Dot(delta, axis);
            // Overlap the platform approach so footprint tests cross a seam as one continuous deck.
            const float approach = 2f;
            if (along < -approach || along > length + approach || Mathf.Abs(Vector3.Dot(delta, new Vector3(-axis.z, 0, axis.x))) > walkWidth * .5f) return false;
            if (!BoundaryOpen) return false;
            if (along < 0 && !boundaryA.ContainsLocal(boundaryA.transform.InverseTransformPoint(start - axis * approach))) return false;
            if (along > length && !boundaryB.ContainsLocal(boundaryB.transform.InverseTransformPoint(end + axis * approach))) return false;
            point.y = Mathf.Lerp(start.y, end.y, Mathf.Clamp01(along / length));
            return point.y <= near.y + up && point.y >= near.y - down;
        }
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
