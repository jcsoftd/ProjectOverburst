using System;
using System.Collections.Generic;

public sealed class CombatSpatialIndexCore<T> where T : class
{
    private readonly struct CellKey : IEquatable<CellKey>
    {
        public readonly int X;
        public readonly int Z;

        public CellKey(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(CellKey other)
        {
            return X == other.X && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is CellKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Z;
            }
        }
    }

    private struct Membership
    {
        public CellKey Key;
        public int MemberIndex;

        public Membership(CellKey key, int memberIndex)
        {
            Key = key;
            MemberIndex = memberIndex;
        }
    }

    private struct CellMember
    {
        public Entry TargetEntry;
        public int MembershipIndex;

        public CellMember(Entry targetEntry, int membershipIndex)
        {
            TargetEntry = targetEntry;
            MembershipIndex = membershipIndex;
        }
    }

    private sealed class Cell
    {
        public readonly List<CellMember> Members = new List<CellMember>(8);
    }

    private sealed class Entry
    {
        public int TargetId;
        public T Target;
        public float X;
        public float Z;
        public float Radius;
        public long RegistrationOrder;
        public int MinCellX;
        public int MaxCellX;
        public int MinCellZ;
        public int MaxCellZ;
        public int LastQueryStamp;
        public readonly List<Membership> Memberships = new List<Membership>(4);
    }

    private sealed class RegistrationOrderComparer : IComparer<Entry>
    {
        public int Compare(Entry left, Entry right)
        {
            if (ReferenceEquals(left, right))
                return 0;

            if (left == null)
                return 1;

            if (right == null)
                return -1;

            int orderCompare = right.RegistrationOrder.CompareTo(left.RegistrationOrder); // 기존 Registry 역순 유지
            return orderCompare != 0 ? orderCompare : right.TargetId.CompareTo(left.TargetId);
        }
    }

    private static readonly RegistrationOrderComparer EntryComparer = new RegistrationOrderComparer();

    private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>(128);
    private readonly Dictionary<CellKey, Cell> cells = new Dictionary<CellKey, Cell>(128);
    private readonly List<Entry> queryEntries = new List<Entry>(128);
    private readonly float cellSize;
    private long nextRegistrationOrder;
    private int queryStamp;

    public CombatSpatialIndexCore(float configuredCellSize)
    {
        if (float.IsNaN(configuredCellSize)
            || float.IsInfinity(configuredCellSize)
            || configuredCellSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(configuredCellSize), "Cell size must be finite and greater than zero.");
        }

        cellSize = configuredCellSize;
    }

    public float CellSize => cellSize;
    public int Count => entries.Count;
    public int OccupiedCellCount => cells.Count;

    public bool RegisterOrUpdate(int targetId, T target, float x, float z, float radius)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        ValidateFinite(x, nameof(x));
        ValidateFinite(z, nameof(z));
        ValidateFinite(radius, nameof(radius));
        float safeRadius = Math.Max(0f, radius);

        if (entries.TryGetValue(targetId, out Entry existing))
        {
            existing.Target = target;
            UpdateEntry(existing, x, z, safeRadius);
            return false;
        }

        Entry entry = new Entry
        {
            TargetId = targetId,
            Target = target,
            X = x,
            Z = z,
            Radius = safeRadius,
            RegistrationOrder = nextRegistrationOrder++
        };
        ResolveCellRange(x, z, safeRadius, out entry.MinCellX, out entry.MaxCellX, out entry.MinCellZ, out entry.MaxCellZ);
        entries.Add(targetId, entry);
        AddEntryToCells(entry);
        return true;
    }

    public bool Remove(int targetId)
    {
        if (!entries.TryGetValue(targetId, out Entry entry))
            return false;

        RemoveEntryFromCells(entry);
        entries.Remove(targetId);
        return true;
    }

    public void Clear()
    {
        entries.Clear();
        cells.Clear();
        queryEntries.Clear();
        nextRegistrationOrder = 0L;
        queryStamp = 0;
    }

    public void CollectPotential(float originX, float originZ, float radius, List<T> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        ValidateFinite(originX, nameof(originX));
        ValidateFinite(originZ, nameof(originZ));
        ValidateFinite(radius, nameof(radius));
        float safeRadius = Math.Max(0f, radius);

        results.Clear();
        queryEntries.Clear();
        int activeStamp = AcquireQueryStamp();
        ResolveCellRange(originX, originZ, safeRadius, out int minX, out int maxX, out int minZ, out int maxZ);

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                if (!cells.TryGetValue(new CellKey(x, z), out Cell cell))
                    continue;

                List<CellMember> members = cell.Members;
                for (int i = 0; i < members.Count; i++)
                {
                    Entry entry = members[i].TargetEntry;
                    if (entry.LastQueryStamp == activeStamp)
                        continue; // 대형 대상 다중 셀 중복 차단

                    entry.LastQueryStamp = activeStamp;
                    float deltaX = entry.X - originX;
                    float deltaZ = entry.Z - originZ;
                    float combinedRadius = safeRadius + entry.Radius;
                    if (deltaX * deltaX + deltaZ * deltaZ <= combinedRadius * combinedRadius)
                        queryEntries.Add(entry);
                }
            }
        }

        queryEntries.Sort(EntryComparer); // 기존 Registry의 최신 등록 우선 순서 유지
        for (int i = 0; i < queryEntries.Count; i++)
            results.Add(queryEntries[i].Target);
    }

    public bool ValidateIntegrity(out string error)
    {
        foreach (KeyValuePair<int, Entry> pair in entries)
        {
            Entry entry = pair.Value;
            if (entry.TargetId != pair.Key)
            {
                error = "Entry target id mismatch: " + pair.Key;
                return false;
            }

            for (int membershipIndex = 0; membershipIndex < entry.Memberships.Count; membershipIndex++)
            {
                Membership membership = entry.Memberships[membershipIndex];
                if (!cells.TryGetValue(membership.Key, out Cell cell)
                    || membership.MemberIndex < 0
                    || membership.MemberIndex >= cell.Members.Count)
                {
                    error = "Missing cell membership for target: " + entry.TargetId;
                    return false;
                }

                CellMember member = cell.Members[membership.MemberIndex];
                if (!ReferenceEquals(member.TargetEntry, entry) || member.MembershipIndex != membershipIndex)
                {
                    error = "Broken reverse membership for target: " + entry.TargetId;
                    return false;
                }
            }
        }

        foreach (KeyValuePair<CellKey, Cell> pair in cells)
        {
            List<CellMember> members = pair.Value.Members;
            if (members.Count == 0)
            {
                error = "Empty cell remained in the index.";
                return false;
            }

            for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                CellMember member = members[memberIndex];
                if (member.MembershipIndex < 0 || member.MembershipIndex >= member.TargetEntry.Memberships.Count)
                {
                    error = "Broken cell member index.";
                    return false;
                }

                Membership membership = member.TargetEntry.Memberships[member.MembershipIndex];
                if (!membership.Key.Equals(pair.Key) || membership.MemberIndex != memberIndex)
                {
                    error = "Cell and entry membership disagree.";
                    return false;
                }
            }
        }

        error = null;
        return true;
    }

    private void UpdateEntry(Entry entry, float x, float z, float radius)
    {
        ResolveCellRange(x, z, radius, out int minX, out int maxX, out int minZ, out int maxZ);
        bool cellRangeChanged = entry.MinCellX != minX
                                || entry.MaxCellX != maxX
                                || entry.MinCellZ != minZ
                                || entry.MaxCellZ != maxZ;

        if (cellRangeChanged)
            RemoveEntryFromCells(entry);

        entry.X = x;
        entry.Z = z;
        entry.Radius = radius;
        entry.MinCellX = minX;
        entry.MaxCellX = maxX;
        entry.MinCellZ = minZ;
        entry.MaxCellZ = maxZ;

        if (cellRangeChanged)
            AddEntryToCells(entry);
    }

    private void AddEntryToCells(Entry entry)
    {
        for (int x = entry.MinCellX; x <= entry.MaxCellX; x++)
        {
            for (int z = entry.MinCellZ; z <= entry.MaxCellZ; z++)
            {
                CellKey key = new CellKey(x, z);
                if (!cells.TryGetValue(key, out Cell cell))
                {
                    cell = new Cell();
                    cells.Add(key, cell);
                }

                int membershipIndex = entry.Memberships.Count;
                int memberIndex = cell.Members.Count;
                entry.Memberships.Add(new Membership(key, memberIndex));
                cell.Members.Add(new CellMember(entry, membershipIndex));
            }
        }
    }

    private void RemoveEntryFromCells(Entry entry)
    {
        for (int membershipIndex = entry.Memberships.Count - 1; membershipIndex >= 0; membershipIndex--)
        {
            Membership membership = entry.Memberships[membershipIndex];
            if (!cells.TryGetValue(membership.Key, out Cell cell))
                continue;

            int lastMemberIndex = cell.Members.Count - 1;
            if (membership.MemberIndex != lastMemberIndex)
            {
                CellMember moved = cell.Members[lastMemberIndex];
                cell.Members[membership.MemberIndex] = moved;
                Membership movedMembership = moved.TargetEntry.Memberships[moved.MembershipIndex];
                movedMembership.MemberIndex = membership.MemberIndex;
                moved.TargetEntry.Memberships[moved.MembershipIndex] = movedMembership;
            }

            cell.Members.RemoveAt(lastMemberIndex);
            if (cell.Members.Count == 0)
                cells.Remove(membership.Key);
        }

        entry.Memberships.Clear();
    }

    private int AcquireQueryStamp()
    {
        if (queryStamp == int.MaxValue)
        {
            foreach (Entry entry in entries.Values)
                entry.LastQueryStamp = 0;

            queryStamp = 0;
        }

        queryStamp++;
        return queryStamp;
    }

    private void ResolveCellRange(
        float x,
        float z,
        float radius,
        out int minX,
        out int maxX,
        out int minZ,
        out int maxZ)
    {
        minX = FloorToCell(x - radius);
        maxX = FloorToCell(x + radius);
        minZ = FloorToCell(z - radius);
        maxZ = FloorToCell(z + radius);
    }

    private int FloorToCell(float coordinate)
    {
        return (int)Math.Floor(coordinate / cellSize);
    }

    private static void ValidateFinite(float value, string parameterName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(parameterName, "Spatial values must be finite.");
    }
}
