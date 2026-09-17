public interface IWeaponRuntimeController
{
    WeaponRuntimeKind RuntimeKind { get; } // 무기 런타임 공통 식별자
    bool CanUseCurrentWeapon { get; }
    bool IsBusy { get; }
    bool IsReady { get; }
    float CooldownRemaining { get; }
    float CooldownProgress01 { get; }
    WeaponRuntimeStatus GetRuntimeStatus(); // 공통 무기 상태 조회
    void CancelCurrentAction(); // 무기 교체 시 이전 행동 취소
}
