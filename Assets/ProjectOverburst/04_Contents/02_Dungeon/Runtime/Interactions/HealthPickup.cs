using UnityEngine;

public class HealthPickup : MonoBehaviour
{
    [SerializeField] private BuffDefinition healingBuff = new BuffDefinition();
    [SerializeField] private float bobAmplitude = 0.25f;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float rotateSpeed = 60f;
    [SerializeField] private GameObject pickupVfxPrefab;
    [SerializeField] private AudioClip pickupSound;

    private Vector3 startPosition;
    private bool pickedUp;

    private void Start()
    {
        startPosition = transform.position;
    }

    private void Update()
    {
        float bobOffset = Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
        transform.position = startPosition + Vector3.up * bobOffset;

        if (rotateSpeed > 0f)
            transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (pickedUp)
            return;

        CombatHealth playerHealth = FindPlayerHealth(other);
        if (playerHealth == null)
            return;

        pickedUp = true;
        healingBuff.Normalize();
        PlayerHealFeedback.ApplyHealPercent(playerHealth, healingBuff.initialHealPercent);

        PlayerBuffController buffController = playerHealth.GetComponentInParent<PlayerBuffController>();
        if (buffController == null)
            buffController = playerHealth.gameObject.AddComponent<PlayerBuffController>();

        buffController.ApplyBuff(healingBuff);
        PlayPickupFeedback();
        Destroy(gameObject);
    }

    private CombatHealth FindPlayerHealth(Collider other)
    {
        if (other == null)
            return null;

        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        if (player == null && !IsPlayerTagged(other.transform))
            return null;

        CombatHealth health = other.GetComponentInParent<CombatHealth>();
        if (health != null)
            return health;

        return player != null ? player.GetComponentInChildren<CombatHealth>() : null;
    }

    private bool IsPlayerTagged(Transform target)
    {
        Transform current = target;
        while (current != null)
        {
            if (current.CompareTag("Player"))
                return true;

            current = current.parent;
        }

        return false;
    }

    private void PlayPickupFeedback()
    {
        Vector3 feedbackPosition = ResolveFeedbackPosition();
        if (pickupVfxPrefab != null)
            Instantiate(pickupVfxPrefab, feedbackPosition, Quaternion.identity);

        if (pickupSound != null)
            AudioSource.PlayClipAtPoint(pickupSound, feedbackPosition);
    }

    private Vector3 ResolveFeedbackPosition()
    {
        Transform pickupAnchor = transform.Find("PickupVfxAnchor");
        if (pickupAnchor != null)
            return pickupAnchor.position;

        Transform vfxAnchor = transform.Find("VfxAnchor");
        if (vfxAnchor != null)
            return vfxAnchor.position;

        return transform.position;
    }
}
