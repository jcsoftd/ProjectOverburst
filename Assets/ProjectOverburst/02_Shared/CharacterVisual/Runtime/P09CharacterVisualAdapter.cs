using UnityEngine;

[DisallowMultipleComponent]
public sealed class P09CharacterVisualAdapter : MonoBehaviour, ICharacterWeaponSocketProvider
{
    public const string RightHandWeaponSocketName = "Weapon_Target_Hand_R";
    public const string LeftHandWeaponSocketName = "Weapon_Target_Hand_L";
    public const string LeftHandShieldSocketName = "Shield_Target_Hand_L";
    public const string BowBackSocketName = "Bow_Target_Back";
    public const string StaffBackSocketName = "Staff_Target_Back";

    [Header("Model")]
    [SerializeField] private Transform modelRoot;
    [SerializeField] private Animator animator;

    [Header("Weapon Sockets")]
    [SerializeField] private Transform rightHandWeaponSocket;
    [SerializeField] private Transform leftHandWeaponSocket;
    [SerializeField] private Transform leftHandShieldSocket;
    [SerializeField] private Transform bowBackSocket;
    [SerializeField] private Transform staffBackSocket;

    public Transform ModelRoot
    {
        get
        {
            ResolveReferences();
            return modelRoot;
        }
    }

    public Animator Animator
    {
        get
        {
            ResolveReferences();
            return animator;
        }
    }

    public Transform RightHandWeaponSocket
    {
        get
        {
            ResolveReferences();
            return rightHandWeaponSocket;
        }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    public void ResolveReferences()
    {
        if (modelRoot == null)
            modelRoot = transform;

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        rightHandWeaponSocket = ResolveSocket(rightHandWeaponSocket, RightHandWeaponSocketName);
        leftHandWeaponSocket = ResolveSocket(leftHandWeaponSocket, LeftHandWeaponSocketName);
        leftHandShieldSocket = ResolveSocket(leftHandShieldSocket, LeftHandShieldSocketName);
        bowBackSocket = ResolveSocket(bowBackSocket, BowBackSocketName);
        staffBackSocket = ResolveSocket(staffBackSocket, StaffBackSocketName);
    }

    public Transform GetWeaponSocket(WeaponItemData weaponData)
    {
        ResolveReferences();
        return rightHandWeaponSocket;
    }

    public Transform GetNamedSocket(string socketName)
    {
        if (string.IsNullOrEmpty(socketName))
            return null;

        ResolveReferences();

        if (socketName == RightHandWeaponSocketName)
            return rightHandWeaponSocket;

        if (socketName == LeftHandWeaponSocketName)
            return leftHandWeaponSocket;

        if (socketName == LeftHandShieldSocketName)
            return leftHandShieldSocket;

        if (socketName == BowBackSocketName)
            return bowBackSocket;

        if (socketName == StaffBackSocketName)
            return staffBackSocket;

        return FindDeepChild(transform, socketName);
    }

    private Transform ResolveSocket(Transform current, string socketName)
    {
        if (current != null && current.name == socketName)
            return current;

        Transform namedSocket = FindDeepChild(transform, socketName);
        return namedSocket != null ? namedSocket : current;
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }
}
