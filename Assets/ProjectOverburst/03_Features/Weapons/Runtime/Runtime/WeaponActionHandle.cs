using System;

public readonly struct WeaponActionHandle : IEquatable<WeaponActionHandle>
{
    public WeaponActionHandle(int actionId)
    {
        ActionId = actionId;
    }

    public int ActionId { get; }
    public bool IsValid => ActionId > 0;

    public bool Equals(WeaponActionHandle other)
    {
        return ActionId == other.ActionId;
    }

    public override bool Equals(object obj)
    {
        return obj is WeaponActionHandle other && Equals(other);
    }

    public override int GetHashCode()
    {
        return ActionId;
    }

    public static bool operator ==(WeaponActionHandle left, WeaponActionHandle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(WeaponActionHandle left, WeaponActionHandle right)
    {
        return !left.Equals(right);
    }

    public static WeaponActionHandle Invalid => default;
}
