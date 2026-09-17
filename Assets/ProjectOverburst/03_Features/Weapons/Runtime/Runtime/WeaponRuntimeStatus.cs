public readonly struct WeaponRuntimeStatus
{
    public static readonly WeaponRuntimeStatus Empty = new WeaponRuntimeStatus(WeaponRuntimeKind.None, false, false, false, 0f, 0f); // 무기 및 런타임 없음 기본 상태

    public WeaponRuntimeStatus(
        WeaponRuntimeKind kind,
        bool isUsable,
        bool isBusy,
        bool isReady,
        float cooldownRemaining,
        float cooldownProgress01)
    {
        Kind = kind;
        IsUsable = isUsable;
        IsBusy = isBusy;
        IsReady = isReady;
        CooldownRemaining = cooldownRemaining;
        CooldownProgress01 = cooldownProgress01;
    }

    public WeaponRuntimeKind Kind { get; }
    public bool IsUsable { get; } // 현재 무기 사용 가능 여부
    public bool IsBusy { get; } // 공격 및 시전 진행 여부
    public bool IsReady { get; } // 다음 행동 가능 여부
    public float CooldownRemaining { get; }
    public float CooldownProgress01 { get; }
}
