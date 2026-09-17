using UnityEngine;

public static class PlayerControlShellBuilder
{
    public static void EnsureControllableShell(GameObject memberObject)
    {
        PlayerCapabilityInstaller.EnsurePlayerKit(memberObject);
    }
}
