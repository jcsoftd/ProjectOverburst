using UnityEngine;

public static class PlayerCapabilityInstaller
{
    public static PlayerControlKit EnsurePlayerKit(GameObject memberObject)
    {
        if (memberObject == null)
            return null;

        bool createdCharacterController;
        CharacterController characterController = EnsureComponent<CharacterController>(memberObject, out createdCharacterController);
        ConfigureCharacterController(characterController, createdCharacterController);

        EnsureComponent<CombatHealth>(memberObject);
        bool refreshCombatVolume = memberObject.GetComponent<CombatTarget>() == null;
        CombatTarget.EnsureConfigured(memberObject, CombatTeam.PlayerParty, refreshCombatVolume);
        EnsureComponent<PlayerEquipment>(memberObject);
        EnsureComponent<WeaponRuntimeHub>(memberObject);
        EnsureComponent<PlayerStaminaController>(memberObject);
        EnsureComponent<PlayerBuffController>(memberObject);
        EnsureComponent<PlayerMovementInputSource>(memberObject);
        EnsureComponent<OverburstCharacterMotor3D>(memberObject);
        EnsureComponent<PlayerLocomotion>(memberObject);
        EnsureComponent<CombatMotionDriver>(memberObject);
        EnsureComponent<PlayerMovement>(memberObject);
        EnsureComponent<PlayerAnimation>(memberObject);
        EnsureComponent<PlayerEvadeController>(memberObject);
        EnsureComponent<PlayerStateCoordinator>(memberObject);
        EnsureComponent<PlayerAimRotation>(memberObject);
        EnsureComponent<PlayerLeftHandGrip>(memberObject);
        EnsureComponent<PlayerPickupInteractor>(memberObject);
        EnsureComponent<PlayerCurrencyAutoPickup>(memberObject);
        EnsureComponent<MeleeRuntime>(memberObject);
        EnsureComponent<MagicRuntime>(memberObject);
        PlayerControlKit kit = EnsureComponent<PlayerControlKit>(memberObject);
        kit.ResolveReferences();
        return kit;
    }

    private static T EnsureComponent<T>(GameObject owner) where T : Component
    {
        return EnsureComponent<T>(owner, out _);
    }

    private static T EnsureComponent<T>(GameObject owner, out bool created) where T : Component
    {
        T component = owner.GetComponent<T>();
        created = false;
        if (component == null)
        {
            component = owner.AddComponent<T>();
            created = true;
        }

        return component;
    }

    private static void ConfigureCharacterController(CharacterController characterController, bool wasCreated)
    {
        if (characterController == null)
            return;

        if (characterController.height <= 0f)
            characterController.height = 2f;

        if (characterController.radius <= 0f)
            characterController.radius = 0.35f;

        if (wasCreated)
            characterController.center = new Vector3(0f, characterController.height * 0.5f, 0f);
    }
}
