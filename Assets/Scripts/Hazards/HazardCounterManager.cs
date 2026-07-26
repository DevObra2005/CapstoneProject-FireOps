using UnityEngine;
using TMPro;

public class HazardCounterManager : MonoBehaviour
{
    // -------------------------------------------------------
    // SINGLETON
    // A static self-reference so any script in the scene can
    // call HazardCounterManager.Instance.SomeMethod()
    // without needing a drag-and-drop in the Inspector.
    // -------------------------------------------------------
    public static HazardCounterManager Instance { get; private set; }

    [Header("UI Reference")]
    [SerializeField] private TextMeshProUGUI counterText;

    private int totalHazards = 0;  // built up by RegisterHazard() calls
    private int foundHazards = 0;  // built up by HazardFound() calls

    // ✅ NEW: Flag so HazardPopupManager knows to show
    //         completion modal after Got It is clicked
    public bool AllHazardsFound { get; private set; } = false;

    private void Awake()
    {
        // Awake runs before Start — safe place to set up the Singleton.
        // If no instance exists yet, this becomes it.
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            // A second one somehow got created — destroy the duplicate.
            Destroy(gameObject);
        }
    }

    // Called once per ClickableHazard during their Start()
    // This is how the manager learns how many hazards are in the scene
    public void RegisterHazard()
    {
        totalHazards++;
        UpdateUI();
    }

    // Called the first time a hazard is tapped
    public void HazardFound()
    {
        foundHazards++;
        UpdateUI();

        if (foundHazards >= totalHazards)
        {
            Debug.Log("[HazardCounter] All hazards found!");

            // ✅ CHANGED: Don't show modal immediately
            //    Just set the flag — HazardPopupManager will
            //    check this flag when Got It is clicked
            AllHazardsFound = true;
        }
    }

    // Pushes the latest numbers into the on-screen text
    private void UpdateUI()
    {
        if (counterText != null)
            counterText.text = $"{foundHazards} / {totalHazards}";
        else
            Debug.LogWarning("[HazardCounterManager] counterText is not assigned!");
    }
}