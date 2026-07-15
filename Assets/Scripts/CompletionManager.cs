using UnityEngine;
using UnityEngine.SceneManagement;

public class CompletionManager : MonoBehaviour
{
    // -------------------------------------------------------
    // WHAT THIS SCRIPT DOES:
    // - Listens for when all hazards are found
    // - Shows the "Great Job!" modal
    // - Handles the START button to load Phase 2
    // -------------------------------------------------------

    public static CompletionManager Instance { get; private set; }

    [Header("UI Reference")]
    public GameObject completionModal;

    [Header("Next Scene")]
    public string nextSceneName = "ScenarioScene";

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    // Called by HazardCounterManager when all hazards are found
    public void ShowCompletionModal()
    {
        if (HazardPopupManager.Instance != null && HazardPopupManager.Instance.IsOpen)
        {
            HazardPopupManager.Instance.popupPanel.SetActive(false);
        }

        if (completionModal != null)
        {
            completionModal.SetActive(true);
            Debug.Log("[CompletionManager] Great Job modal shown!");
        }
    }

    // Called by the START button's OnClick event
    public void LoadNextScene()
    {
        PlayerPrefs.SetInt("FireLearningCompleted", 1);
        PlayerPrefs.SetInt("SimulationMode", 1);
        PlayerPrefs.Save();

        // Tell GameModeManager this is a legitimate Phase 2 transition.
        // Without this, GameModeManager.Awake() would reset SimulationMode
        // back to 0 on the next scene load, cancelling our flag above.
        GameModeManager.intentionalTransition = true;

        Debug.Log("[CompletionManager] Starting simulation mode...");
        SceneManager.LoadScene("Office3DScene");
    }
}