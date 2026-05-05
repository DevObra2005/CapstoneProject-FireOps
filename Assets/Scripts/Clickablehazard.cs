using UnityEngine;

public class ClickableHazard : MonoBehaviour
{
    [Header("Assign in Inspector")]
    public HazardData hazardData;

    public float interactDistance = 5f;

    private Renderer objectRenderer;
    private Color originalColor;

    // ✅ NEW: cooldown instead of permanent block
    private float clickCooldown = 0f;
    private const float COOLDOWN_TIME = 1.2f; // adjust if needed

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
    }

    public void OnClicked()
    {
        // ✅ BLOCK only during cooldown
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