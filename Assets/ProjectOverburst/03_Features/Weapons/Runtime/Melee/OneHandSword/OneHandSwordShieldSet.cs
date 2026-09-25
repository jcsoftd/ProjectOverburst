using UnityEngine;

[DisallowMultipleComponent]
public sealed class OneHandSwordShieldSet : MonoBehaviour
{
    private const string BackAnchorName = "BackWeaponAnchor";

    [Header("Shield Prefab")]
    [SerializeField] private GameObject shieldEquippedPrefab;

    [Header("Character Sockets")]
    [SerializeField] private string handSocketName = P09CharacterVisualAdapter.LeftHandShieldSocketName;
    [SerializeField] private string backAnchorName = BackAnchorName;

    [Header("Socket Rotation Offsets")]
    [SerializeField] private Vector3 handSocketLocalRotationOffset = new Vector3(90f, 0f, 0f);
    [SerializeField] private Vector3 backSocketLocalRotationOffset = new Vector3(0f, 180f, 0f);

    private PlayerEquipment playerEquipment;
    private Transform handSocket;
    private Transform backAnchor;
    private GameObject shieldInstance;
    private ShieldGripMount shieldGripMount;
    private bool wasRegisteredAsCurrentWeapon;
    private bool missingSocketWarningLogged;

    public GameObject ShieldInstance => shieldInstance; // 현재 생성된 세트 방패
    public Vector3 HandSocketLocalRotationOffset => handSocketLocalRotationOffset;

    public void Configure(GameObject prefab)
    {
        shieldEquippedPrefab = prefab; // 한손검 전용 방패 프리팹
        handSocketName = P09CharacterVisualAdapter.LeftHandShieldSocketName;
        backAnchorName = BackAnchorName;
    }

    private void Awake()
    {
        ResolveCharacterReferences(); // 캐릭터 연결
        EnsureShieldInstance(); // 검과 함께 방패 생성
    }

    private void OnEnable()
    {
        EnsureShieldInstance(); // 재활성화 대응
    }

    private void LateUpdate()
    {
        ResolveCharacterReferences(); // 파티 리바인드 대응

        if (playerEquipment != null && playerEquipment.CurrentWeaponRoot == transform)
            wasRegisteredAsCurrentWeapon = true; // 정식 장착 확인

        if (wasRegisteredAsCurrentWeapon
            && playerEquipment != null
            && playerEquipment.CurrentWeaponRoot != transform)
        {
            ReleaseShield(); // 무기 교체 시 세트 방패 제거
            return;
        }

        EnsureShieldInstance();
        UpdateShieldPose(); // 검의 손/등 상태 동기화
    }

    private void OnDisable()
    {
        ReleaseShield(); // 비활성 검의 방패 정리
    }

    private void OnDestroy()
    {
        ReleaseShield(); // 검 제거 시 방패 정리
    }

    private void ResolveCharacterReferences()
    {
        if (playerEquipment == null)
            playerEquipment = GetComponentInParent<PlayerEquipment>();

        Transform searchRoot = playerEquipment != null ? playerEquipment.transform : transform.root;
        if (searchRoot == null)
            return;

        P09CharacterVisualAdapter adapter = searchRoot.GetComponentInChildren<P09CharacterVisualAdapter>(true);
        if (adapter != null)
            handSocket = adapter.GetNamedSocket(handSocketName); // 왼팔 방패 소켓

        if (backAnchor == null || backAnchor.name != backAnchorName)
            backAnchor = FindDeepChild(searchRoot, backAnchorName); // 공용 등 앵커
    }

    private void EnsureShieldInstance()
    {
        if (shieldInstance != null || shieldEquippedPrefab == null)
            return;

        ResolveCharacterReferences();
        Transform initialParent = handSocket != null ? handSocket : transform;
        shieldInstance = Instantiate(shieldEquippedPrefab, initialParent, false); // 세트 모델 생성
        shieldInstance.name = shieldEquippedPrefab.name + "_Instance";
        shieldGripMount = shieldInstance.GetComponent<ShieldGripMount>();
        if (shieldGripMount == null)
            shieldGripMount = shieldInstance.GetComponentInChildren<ShieldGripMount>(true);

        UpdateShieldPose();
    }

    private void UpdateShieldPose()
    {
        if (shieldInstance == null || shieldGripMount == null)
            return;

        bool useBackPoint = backAnchor != null && (transform.parent == backAnchor || transform.IsChildOf(backAnchor)); // 검 보관 상태
        Transform targetSocket = useBackPoint ? backAnchor : handSocket;

        if (targetSocket == null)
        {
            if (!missingSocketWarningLogged)
            {
                Debug.LogWarning("[OneHandSwordShieldSet] Shield socket is missing: "
                    + (useBackPoint ? backAnchorName : handSocketName), this);
                missingSocketWarningLogged = true;
            }

            return;
        }

        missingSocketWarningLogged = false;
        Vector3 socketLocalRotationOffset = useBackPoint
            ? backSocketLocalRotationOffset
            : handSocketLocalRotationOffset;
        shieldGripMount.TryAlignRootToSocket(
            shieldInstance.transform,
            targetSocket,
            useBackPoint,
            socketLocalRotationOffset); // 기준점을 소켓에 맞춘 뒤 상태별 축 회전을 적용
    }

    private void ReleaseShield()
    {
        if (shieldInstance == null)
            return;

        if (Application.isPlaying)
            Destroy(shieldInstance); // 런타임 정리
        else
            DestroyImmediate(shieldInstance); // 에디터 정리

        shieldInstance = null;
        shieldGripMount = null;
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
