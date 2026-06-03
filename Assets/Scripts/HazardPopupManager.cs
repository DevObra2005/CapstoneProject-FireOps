using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HazardPopupManager : MonoBehaviour
{
    public static HazardPopupManager Instance { get; private set; }

    [Header("UI References")]
    public GameObject popupPanel;
    public TextMeshProUGUI hazardTitleText;
    public TextMeshProUGUI hazardDescriptionText;
    public TextMeshProUGUI safetyActionText;
    public Button gotItButton;

    [Header("Player Control")]
    public MonoBehaviour firstPersonController;

    public bool IsOpen { get; private set; }

    private float closeCooldown = 0f;
    private const float COOLDOWN_TIME = 0.5f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        popupPanel.SetActive(false);
        gotItButton.onClick.AddListener(ClosePopup);
        IsOpen = false;
    }

    private void Update()
    {
        if (closeCooldown > 0f)
        {
            closeCooldown -= Time.deltaTime;
            if (closeCooldown <= 0f)
                IsOpen = false;
        }
    }

    public void ShowPopup(HazardData data)
    {
        if (closeCooldown > 0f) return;

        hazardTitleText.text = "Identified Hazard: " + data.hazardTitle;
        hazardDescriptionText.text = data.hazardDescription;
        safetyActionText.text = "Safety Action:\n" + data.safetyAction;

        popupPanel.SetActive(true);
        IsOpen = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (firstPersonController != null)
            firstPersonController.enabled = false;
    }

    public void ClosePopup()
    {
        popupPanel.SetActive(false);
        IsOpen = false;  // ✅ FIXED: was incorrectly set to true before
        closeCooldown = COOLDOWN_TIME;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // ✅ NEW: Check if this was the last hazard
        // If all hazards found → show completion modal
        // instead of re-enabling player movement
        if (HazardCounterManager.Instance != null &&
            HazardCounterManager.Instance.AllHazardsFound)
        {
            Debug.Log("[HazardPopupManager] All hazards found! Showing completion modal.");

            if (CompletionManager.Instance != null)
                CompletionManager.Instance.ShowCompletionModal();

            // Don't re-enable controller — player shouldn't
            // move while completion modal is showing
            return;
        }

        // Normal close — re-enable player movement
        if (firstPersonController != null)
            firstPersonController.enabled = true;
    }
}