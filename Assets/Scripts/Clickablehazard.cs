using UnityEngine;

public class ClickableHazard : MonoBehaviour
{
    [Header("Assign in Inspector")]
    public HazardData hazardData;
    public float interactDistance = 5f;

    private Renderer objectRenderer;
    private Color originalColor;

    // Cooldown prevents the popup from re-triggering immediately after closing
    private float clickCooldown = 0f;
    private const float COOLDOWN_TIME = 1.2f;

    // ✅ NEW: tracks if this hazard has been found — so it only counts ONCE
    private bool hasBeenFound = false;

    private void Update()
    {
        if (clickCooldown > 0f)
            clickCooldown -= Time.deltaTime;
    }

    private void Start()
    {
        objectRenderer = GetComponent<Renderer>();
        if (objectRenderer != null)
            originalColor = objectRenderer.material.color;

        if (GetComponent<Collider>() == null)
            Debug.LogWarning($"[ClickableHazard] '{gameObject.name}' has no Collider!");

        // ✅ NEW: tells HazardCounterManager this hazard exists in the scene
        // This is what makes the denominator (the /8) accurate automatically
        HazardCounterManager.Instance.RegisterHazard();
    }

    public void OnClicked()
    {
        // Block during cooldown
        if (clickCooldown > 0f)
        {
            Debug.Log("[ClickableHazard] Cooling down...");
            return;
        }

        if (hazardData == null)
        {
            Debug.LogWarning($"[ClickableHazard] '{gameObject.name}' has no HazardData!");
            return;
        }

        clickCooldown = COOLDOWN_TIME;
        HazardPopupManager.Instance.ShowPopup(hazardData);

        // ✅ NEW: only increments the counter the very first time this hazard is tapped
        // hasBeenFound stays true forever, so re-tapping the same hazard won't count again
        if (!hasBeenFound)
        {
            hasBeenFound = true;
            HazardCounterManager.Instance.HazardFound();
        }
    }

    public void OnHoverEnter()
    {
        if (objectRenderer != null)
            objectRenderer.material.color = Color.yellow;
    }

    public void OnHoverExit()
    {
        if (objectRenderer != null)
            objectRenderer.material.color = originalColor;
    }
}