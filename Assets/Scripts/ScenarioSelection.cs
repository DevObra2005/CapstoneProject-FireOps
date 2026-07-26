using UnityEngine;
using UnityEngine.SceneManagement;

public class ScenarioSelection : MonoBehaviour
{
    public GameObject fireLearningPopup; // Assign your popup panel in Inspector

    // Shared helper — uses the persistent loading screen when available.
    private void Go(string sceneName, string tagline)
    {
        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.Show(sceneName, tagline);
        else
            SceneManager.LoadScene(sceneName);
    }

    // Called when the user clicks the scenario button
    public void OnScenario1Selected()
    {
        int completed = PlayerPrefs.GetInt("FireLearningCompleted", 0);

        if (completed == 0)
        {
            // Learning module not finished → show popup
            fireLearningPopup.SetActive(true);
        }
        else
        {
            // Learning module finished → go straight in
            Go("Office3DScene", "LOADING OFFICE");
        }
    }

    // Called by the "Start Learning" button on the popup
    public void StartLearningModule()
    {
        if (fireLearningPopup != null)
            fireLearningPopup.SetActive(false);

        Go("Office3DScene", "LOADING OFFICE");
    }

    // Called by the "Cancel" button on the popup
    public void CancelPopup()
    {
        fireLearningPopup.SetActive(false);
    }
}