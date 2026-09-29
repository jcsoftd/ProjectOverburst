using UnityEngine;
using UnityEngine.Rendering;

// Retains whole chain-electricity shapes at their world positions while equipped.
[DisallowMultipleComponent]
public sealed class WeaponElectricLineAfterimage : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxAfterimages = 3;
    [SerializeField] private bool includeBranches;
    [SerializeField, Min(.01f)] private float lifetime = .26f;
    [SerializeField, Min(.001f)] private float spacing = .07f;
    [SerializeField, Min(.01f)] private float widthScale = .72f;
    [SerializeField, Min(0f)] private float driftDistance = .035f;

    private sealed class Snapshot
    {
        public LineRenderer[] lines;
        public Vector3[][] points;
        public float[] widths;
        public Color[] startColors;
        public Color[] endColors;
        public float age;
        public float birthEnergy;
        public Vector3 drift;
        public bool active;
    }

    private ChainElectricityLiteVfxController chain;
    private LineRenderer[] sources;
    private Snapshot[] snapshots;
    private bool emitting;
    private bool hasPreviousTip;
    private float energy = 1f;
    private float distanceSinceSnapshot;
    private float timeSinceSnapshot;
    private Vector3 previousTip;
    private int nextSnapshot;

    public bool IsEmitting => emitting;

    public void SetEnergy(float normalizedEnergy)
    {
        energy = Mathf.Clamp01(normalizedEnergy);
        if (energy <= 0f) Clear();
    }

    public void Begin()
    {
        Clear();
        if (energy <= 0f) return;
        EnsurePool();
        emitting = snapshots != null;
    }

    public void End()
    {
        emitting = false;
        hasPreviousTip = false;
    }

    public void Clear()
    {
        End();
        distanceSinceSnapshot = 0f;
        timeSinceSnapshot = 0f;
        nextSnapshot = 0;
        if (snapshots == null) return;
        foreach (var snapshot in snapshots)
        {
            snapshot.active = false;
            foreach (var line in snapshot.lines) line.enabled = false;
        }
    }

    private void OnDisable() => Clear();

    // The preview calls this at 60 Hz. Runtime calls it after the sword pose updates.
    public void Sample(float elapsed, Vector3 bladeBase, Vector3 bladeTip)
    {
        AgeSnapshots(Mathf.Max(0f, elapsed));
        if (!emitting || energy <= 0f || snapshots == null) return;
        if (!hasPreviousTip)
        {
            previousTip = bladeTip;
            hasPreviousTip = true;
            if ((bladeTip - bladeBase).sqrMagnitude > .0001f)
                Capture(Vector3.zero);
            return;
        }

        Vector3 movement = bladeTip - previousTip;
        float distance = movement.magnitude;
        previousTip = bladeTip;
        if (distance > .6f) // A seek or weapon swap must not connect unrelated poses.
        {
            distanceSinceSnapshot = 0f;
            timeSinceSnapshot = 0f;
            return;
        }
        distanceSinceSnapshot += distance;
        timeSinceSnapshot += Mathf.Max(0f, elapsed);
        float idleInterval = lifetime / (Mathf.Max(1, maxAfterimages) + 1f) / Mathf.Max(.2f, energy);
        if (distanceSinceSnapshot < spacing / Mathf.Max(.2f, energy) && timeSinceSnapshot < idleInterval) return;
        distanceSinceSnapshot = 0f;
        timeSinceSnapshot = 0f;
        if ((bladeTip - bladeBase).sqrMagnitude > .0001f)
            Capture(movement);
    }

    private void EnsurePool()
    {
        if (snapshots != null) return;
        chain = GetComponentInChildren<ChainElectricityLiteVfxController>(true);
        if (chain == null) return;
        int sourceCount = includeBranches ? 2 + chain.BranchCount * 2 : 2;
        sources = new LineRenderer[sourceCount];
        for (int i = 0; i < sourceCount; i++)
        {
            sources[i] = chain.GetBatchLine(i);
            if (sources[i] == null) return;
        }

        snapshots = new Snapshot[Mathf.Max(1, maxAfterimages)];
        for (int slot = 0; slot < snapshots.Length; slot++)
        {
            var snapshot = new Snapshot
            {
                lines = new LineRenderer[sourceCount],
                points = new Vector3[sourceCount][],
                widths = new float[sourceCount],
                startColors = new Color[sourceCount],
                endColors = new Color[sourceCount]
            };
            for (int layer = 0; layer < sourceCount; layer++)
            {
                var child = new GameObject("ElectricArcAfterimage_" + slot + "_" + layer);
                child.transform.SetParent(transform, false);
                var line = child.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.sharedMaterial = sources[layer].sharedMaterial;
                line.alignment = sources[layer].alignment;
                line.textureMode = sources[layer].textureMode;
                line.widthCurve = sources[layer].widthCurve;
                line.numCornerVertices = 2;
                line.numCapVertices = 1;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                snapshot.lines[layer] = line;
            }
            snapshots[slot] = snapshot;
        }
    }

    private void Capture(Vector3 movement)
    {
        var snapshot = snapshots[nextSnapshot];
        nextSnapshot = (nextSnapshot + 1) % snapshots.Length;
        snapshot.age = 0f;
        snapshot.birthEnergy = energy;
        snapshot.drift = movement.normalized * driftDistance;
        snapshot.active = false;
        for (int layer = 0; layer < sources.Length; layer++)
        {
            var source = sources[layer];
            var ghost = snapshot.lines[layer];
            if (!source.enabled || source.positionCount < 3)
            {
                ghost.enabled = false;
                continue;
            }

            int pointCount = source.positionCount;
            if (snapshot.points[layer] == null || snapshot.points[layer].Length != pointCount)
                snapshot.points[layer] = new Vector3[pointCount];
            ghost.positionCount = pointCount;
            snapshot.widths[layer] = source.widthMultiplier * widthScale;
            snapshot.startColors[layer] = source.startColor;
            snapshot.endColors[layer] = source.endColor;
            for (int point = 0; point < pointCount; point++)
            {
                Vector3 position = source.useWorldSpace
                    ? source.GetPosition(point)
                    : source.transform.TransformPoint(source.GetPosition(point));
                snapshot.points[layer][point] = position;
                ghost.SetPosition(point, position);
            }
            ghost.widthMultiplier = snapshot.widths[layer];
            ghost.startColor = snapshot.startColors[layer];
            ghost.endColor = snapshot.endColors[layer];
            ghost.enabled = true;
            snapshot.active = true;
        }
    }

    private void AgeSnapshots(float elapsed)
    {
        if (snapshots == null) return;
        foreach (var snapshot in snapshots)
        {
            if (!snapshot.active) continue;
            snapshot.age += elapsed;
            float fade = 1f - snapshot.age / lifetime;
            if (fade <= 0f)
            {
                snapshot.active = false;
                foreach (var line in snapshot.lines) line.enabled = false;
                continue;
            }
            float energyRatio = energy / Mathf.Max(.001f, snapshot.birthEnergy);
            Vector3 offset = snapshot.drift * (1f - fade);
            for (int layer = 0; layer < snapshot.lines.Length; layer++)
            {
                var line = snapshot.lines[layer];
                if (!line.enabled) continue;
                line.widthMultiplier = snapshot.widths[layer] * fade * energyRatio;
                Color start = snapshot.startColors[layer];
                Color end = snapshot.endColors[layer];
                start.a *= fade;
                end.a *= fade;
                line.startColor = start;
                line.endColor = end;
                if (snapshot.drift.sqrMagnitude > 0f)
                {
                    var points = snapshot.points[layer];
                    for (int point = 0; point < points.Length; point++)
                        line.SetPosition(point, points[point] + offset);
                }
            }
        }
    }
}
