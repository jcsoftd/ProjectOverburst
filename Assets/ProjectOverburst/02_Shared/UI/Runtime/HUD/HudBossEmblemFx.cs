using UnityEngine;
using UnityEngine.UI;

// 2026-10-01: 상단 보스 HUD 마름모 연출 종류. 보스마다 EnemyBossDefinition.EmblemFx로 골라 돌려쓴다.
public enum EnemyBossEmblemFxMode
{
    Fire = 1,   // 심홍 불꽃
    Smoke = 2,  // 어둠 연기 + 붉은 가장자리
    Pulse = 3   // 붉은 맥동 + 테두리 빛
}

// 마름모 뒤층(불꽃·연기·맥동)과 앞층(테두리 빛)을 같은 셰이더(Resources/Shaders/HudBossEmblemFxUI)의 모드로 그린다.
// 재질은 HudResourceBarFx처럼 실행 중에 만들고 지운다(자산 재질을 두지 않는다).
[DisallowMultipleComponent]
public sealed class HudBossEmblemFx : MonoBehaviour
{
    private const string ShaderPath = "Shaders/HudBossEmblemFxUI";
    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static readonly int LayerId = Shader.PropertyToID("_Layer");

    [SerializeField] private Image backLayer;
    [SerializeField] private Image frontLayer;
    [SerializeField] private EnemyBossEmblemFxMode mode = EnemyBossEmblemFxMode.Fire;

    private Material backMaterial;
    private Material frontMaterial;

    public EnemyBossEmblemFxMode Mode => mode;

    private void Awake()
    {
        Prepare();
    }

    private void OnDestroy()
    {
        DestroyMaterial(ref backMaterial);
        DestroyMaterial(ref frontMaterial);
    }

    public void Configure(Image back, Image front)
    {
        backLayer = back;
        frontLayer = front;
    }

    public void SetMode(EnemyBossEmblemFxMode next)
    {
        mode = next;
        Prepare();
        Apply();
    }

    // 편집 모드 캡처에서도 부를 수 있게 공개한다(Awake가 돌지 않는 경우).
    public void Prepare()
    {
        if (backMaterial != null && frontMaterial != null)
            return;

        Shader shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null)
        {
            Debug.LogError("[HudBossEmblemFx] Missing shader: " + ShaderPath);
            return;
        }

        backMaterial = new Material(shader) { name = "HudBossEmblemFx Back (Runtime)", hideFlags = HideFlags.DontSave };
        frontMaterial = new Material(shader) { name = "HudBossEmblemFx Front (Runtime)", hideFlags = HideFlags.DontSave };
        backMaterial.SetFloat(LayerId, 0f);
        frontMaterial.SetFloat(LayerId, 1f);
        if (backLayer != null)
        {
            backLayer.material = backMaterial;
            backLayer.raycastTarget = false;
        }

        if (frontLayer != null)
        {
            frontLayer.material = frontMaterial;
            frontLayer.raycastTarget = false;
        }

        Apply();
    }

    private void Apply()
    {
        float value = (float)mode;
        if (backMaterial != null)
            backMaterial.SetFloat(ModeId, value);
        if (frontMaterial != null)
            frontMaterial.SetFloat(ModeId, value);
        if (frontLayer != null)
            frontLayer.enabled = mode == EnemyBossEmblemFxMode.Pulse; // 앞층은 테두리 빛(맥동)만 쓴다
    }

    private static void DestroyMaterial(ref Material material)
    {
        if (material == null)
            return;

        if (Application.isPlaying)
            Destroy(material);
        else
            DestroyImmediate(material);
        material = null;
    }
}
