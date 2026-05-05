using UnityEngine;
using UnityEngine.SceneManagement;

public class ScenarioSelection : MonoBehaviour
{
    public GameObject fireLearningPopup; // Assign your popup panel in Inspector

    // 1️⃣ Call this when the user clicks the scenario button
    public void OnScenario1Selected()
    {
        // 2️⃣ Check if the user completed the learning module
        int completed = PlayerPrefs.GetInt("FireLearningCompleted", 0);

        if (completed == 0)
        {
            // 3️⃣ Learning module not finished → show popup
            fireLearningPopup.SetActive(true);
        }
        else
        {
            // 4️⃣ Learning module finished → go directly to 3D scene
            SceneManager.LoadScene("Office3DScene");
        }
    }

    // 5️⃣ Called by the "Start Learning" button on the popup
    public void StartLearningModule()
    {
        SceneManager.LoadScene("Office3DScene"); // Same 3D Office scene
    }

    // 6️⃣ Called by the "Cancel" button on the popup
    public void CancelPopup()
    {
        fireLearningPopup.SetActive(false);
    }
}