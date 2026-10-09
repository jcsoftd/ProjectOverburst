using UnityEngine;

namespace Overburst.Caves
{
    public sealed class CaveConnectionGuide : MonoBehaviour
    {
        public CavePlatformLibrary.Region[] regions;

        void OnDrawGizmosSelected()
        {
            if (regions == null) return;
            var previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            foreach (var region in regions)
            {
                Gizmos.color = region.stone ? new Color(1, .75f, .25f) : new Color(.15f, .9f, 1);
                var pivot = region.pivot + Vector3.up * .15f;
                Gizmos.DrawSphere(pivot, .2f);
                Vector3 Point(float angle, float pitch)
                {
                    float a = angle * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad;
                    return pivot + new Vector3(Mathf.Cos(a), Mathf.Tan(p), Mathf.Sin(a)) * (region.radius + region.deckLength);
                }
                for (int n = 0; n <= 24; n++)
                {
                    float angle = Mathf.Lerp(region.MinAngle, region.MaxAngle, n / 24f);
                    if (n == 0 || n == 24 || n == 12) Gizmos.DrawLine(pivot, Point(angle, region.pitch));
                    if (n > 0) Gizmos.DrawLine(Point(Mathf.Lerp(region.MinAngle, region.MaxAngle, (n - 1) / 24f), region.pitch), Point(angle, region.pitch));
                }
                Gizmos.DrawLine(pivot, Point(region.heading, region.pitch - region.pitchTolerance));
                Gizmos.DrawLine(pivot, Point(region.heading, region.pitch + region.pitchTolerance));
            }
            Gizmos.matrix = previous;
        }
    }
}
