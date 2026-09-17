using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WeaponFireHud : MonoBehaviour
{
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private PlayerContext playerContext;
    [SerializeField] private Image cooldownFill;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private bool autoResolveReferences = true;
    private bool missingReferenceWarned;

    private void Awake()
    {
        ResolveReferences();
        ValidateSceneReferences();
    }

    private void Update()
    {
        if (autoResolveReferences)
            ResolveReferences();

        RefreshHud();
    }

    private void ResolveReferences()
    {
        if (playerContext == null)
            playerContext = PlayerContext.GetOrCreate();

        PlayerEquipment leaderEquipment = playerContext != null ? playerContext.CurrentActorEquipment : null;
        if (leaderEquipment != null)
            playerEquipment = leaderEquipment;

        if (playerEquipment == null)
            playerEquipment = FindFirstObjectByType<PlayerEquipment>();
    }

    private void ValidateSceneReferences()
    {
        if (missingReferenceWarned || (cooldownFill != null && statusText != null))
            return;

        Debug.LogWarning("[WeaponFireHud] CooldownFill or StatusText reference is missing. Check the explicit HUDCanvas/WeaponFireHud object.");
        missingReferenceWarned = true;
    }

    private void RefreshHud()
    {
        bool hasMagicWeapon = playerEquipment != null && playerEquipment.CanCurrentWeaponUseMagicCaster;
        bool hasMeleeSlashWeapon = playerEquipment != null && playerEquipment.CanCurrentWeaponUseMeleeSlash;
        WeaponRuntimeStatus runtimeStatus = playerEquipment != null ? playerEquipment.CurrentWeaponRuntimeStatus : WeaponRuntimeStatus.Empty;
        bool canMagicCast = hasMagicWeapon && runtimeStatus.IsReady;
        bool hasUsableWeapon = hasMagicWeapon || hasMeleeSlashWeapon;
        float progress = hasUsableWeapon ? runtimeStatus.CooldownProgress01 : 0f;

        if (cooldownFill != null)
        {
            cooldownFill.gameObject.SetActive(!hasMeleeSlashWeapon);
            if (!hasMeleeSlashWeapon)
            {
                cooldownFill.fillAmount = progress;
                cooldownFill.color = canMagicCast
                    ? new Color(0.25f, 0.9f, 0.35f, 0.75f)
                    : new Color(1f, 0.72f, 0.16f, 0.75f);
            }
        }

        if (statusText == null)
            return;

        if (hasMeleeSlashWeapon)
            statusText.text = runtimeStatus.IsBusy ? "SWORD ATTACKING" : "SWORD READY";
        else if (hasMagicWeapon)
            statusText.text = canMagicCast ? "ORB READY" : "ORB COOLDOWN " + GetCooldownRemainingText(runtimeStatus);
        else
            statusText.text = "NO WEAPON";
    }

    private string GetCooldownRemainingText(WeaponRuntimeStatus runtimeStatus)
    {
        return runtimeStatus.CooldownRemaining.ToString("0.00");
    }
}
