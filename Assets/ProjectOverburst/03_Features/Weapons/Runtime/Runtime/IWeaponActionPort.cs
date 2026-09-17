public interface IWeaponActionPort : IWeaponRuntimeController
{
    bool TryGetAttackWindow(out WeaponAttackWindow window);

    WeaponActionResult TryStartAction(
        in WeaponActionRequest request,
        out WeaponActionHandle handle);

    WeaponActionResult TryContinue(
        in WeaponActionHandle handle,
        in WeaponActionRequest request);

    bool TryTransferActiveActionToPlayerInput(
        in WeaponActionHandle handle);

    bool TryGetActionState(
        in WeaponActionHandle handle,
        out WeaponActionState state);

    void CancelAction(
        in WeaponActionHandle handle,
        WeaponActionCancelReason reason);
}
