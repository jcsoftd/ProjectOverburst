using System;
using UnityEngine;

namespace Overburst.Caves
{
    public sealed class CaveRigidConnection : MonoBehaviour
    {
        public CavePlatformBoundary boundaryA, boundaryB;
        public int portA = -1, portB = -1;
        public float walkWidth;
        public Vector3[] walkPoints = Array.Empty<Vector3>();
        public MeshCollider walkCollider;
        [SerializeField, HideInInspector] Mesh ownedWalkMesh;

        public bool BoundaryOpen => isActiveAndEnabled && boundaryA && boundaryB
            && boundaryA.isActiveAndEnabled && boundaryB.isActiveAndEnabled
            && pieces != null && pieces.Length > 0 && PiecesPresent()
            && (walkPoints == null || walkPoints.Length == 0 || walkCollider && walkCollider.enabled && walkCollider.sharedMesh);

        public void CreateStairWalkSurface(Vector3[] points)
        {
            if (walkCollider || points == null || points.Length < 2) throw new InvalidOperationException("Create one continuous stair surface per connector.");
            walkPoints = points;
            var direction = end - start; direction.y = 0; direction.Normalize();
            var side = new Vector3(-direction.z, 0, direction.x) * (walkWidth * .5f);
            var vertices = new Vector3[points.Length * 2];
            var triangles = new int[(points.Length - 1) * 6];
            for (int i = 0; i < points.Length; i++)
            {
                vertices[i * 2] = transform.InverseTransformPoint(points[i] - side);
                vertices[i * 2 + 1] = transform.InverseTransformPoint(points[i] + side);
                if (i == points.Length - 1) continue;
                int v = i * 2, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            ownedWalkMesh = new Mesh { name = "Continuous stair walk surface", vertices = vertices, triangles = triangles };
            ownedWalkMesh.RecalculateNormals(); ownedWalkMesh.RecalculateBounds();
            walkCollider = gameObject.AddComponent<MeshCollider>(); walkCollider.sharedMesh = ownedWalkMesh;
            foreach (var piece in pieces)
                foreach (var collider in piece.instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        }

        void OnDestroy()
        {
            if (!ownedWalkMesh) return;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(ownedWalkMesh)) return;
#endif
            if (Application.isPlaying) Destroy(ownedWalkMesh); else DestroyImmediate(ownedWalkMesh);
        }

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
            if (along < -approach - .001f || along > length + approach + .001f || Mathf.Abs(Vector3.Dot(delta, new Vector3(-axis.z, 0, axis.x))) > walkWidth * .5f + .001f) return false;
            if (!BoundaryOpen) return false;
            if (along < 0 && !boundaryA.ContainsLocal(boundaryA.transform.InverseTransformPoint(start - axis * approach))) return false;
            if (along > length && !boundaryB.ContainsLocal(boundaryB.transform.InverseTransformPoint(end + axis * approach))) return false;
            point.y = Mathf.Lerp(start.y, end.y, Mathf.Clamp01(along / length));
            if (walkPoints != null && walkPoints.Length > 1)
            {
                int first = 0, last = walkPoints.Length - 1;
                while (last - first > 1)
                {
                    int middle = (first + last) / 2;
                    if (along < Vector3.Dot(walkPoints[middle] - start, axis)) last = middle; else first = middle;
                }
                float a = Vector3.Dot(walkPoints[first] - start, axis), b = Vector3.Dot(walkPoints[last] - start, axis);
                point.y = Mathf.Lerp(walkPoints[first].y, walkPoints[last].y, Mathf.InverseLerp(a, b, along));
            }
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
